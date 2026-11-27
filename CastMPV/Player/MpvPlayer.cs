using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using CastMPV.Core;

namespace CastMPV.Player
{
    public sealed class MediaTrackInfo
    {
        public long Id { get; set; }
        public string Type { get; set; } // "video", "audio", "sub"
        public string Title { get; set; }
        public string Lang { get; set; }
        public bool Selected { get; set; }

        public string DisplayLabel
        {
            get
            {
                string langPart = string.IsNullOrWhiteSpace(Lang) ? "" : $"[{Lang.ToUpperInvariant()}] ";
                string titlePart = string.IsNullOrWhiteSpace(Title) ? $"Track {Id}" : Title;
                return $"{langPart}{titlePart}";
            }
        }
    }

    public sealed class MpvPlayer : IDisposable
    {
        private readonly object _sync = new object();
        private IntPtr _mpv;
        private Thread _eventThread;
        private volatile bool _disposed;
        private volatile bool _isPlaying;
        private volatile bool _isPaused;
        private volatile bool _isMuted;
        private double _duration;
        private double _position;
        private int _volume = 85;
        private double _speed = 1.0;
        private double _subDelay;
        private string _currentUri = string.Empty;
        private string _currentTitle = string.Empty;
        private string _aspectMode = "Original";

        public event Action<double> PositionChanged;
        public event Action<double> DurationChanged;
        public event Action<bool> PauseStateChanged;
        public event Action<int> VolumeChanged;
        public event Action<bool> MuteChanged;
        public event Action<string> MediaTitleChanged;
        public event Action FileLoaded;
        public event Action<bool> PlaybackEnded; // true = natural EOF or stop
        public event Action<string> ErrorOccurred;

        public bool IsInitialized => _mpv != IntPtr.Zero && !_disposed;
        public bool IsPlaying => _isPlaying;
        public bool IsPaused => _isPaused;
        public bool IsMuted => _isMuted;
        public double Duration => _duration;
        public double Position => _position;
        public int Volume => _volume;
        public double Speed => _speed;
        public double SubtitleDelay => _subDelay;
        public string CurrentUri => _currentUri;
        public string CurrentTitle => _currentTitle;
        public string AspectMode => _aspectMode;

        public bool Initialize(IntPtr windowHandle, AppSettings settings, out string errorMessage)
        {
            if (!MpvNative.EnsureLibraryLoaded(out errorMessage))
                return false;

            _mpv = MpvNative.mpv_create();
            if (_mpv == IntPtr.Zero)
            {
                errorMessage = "mpv_create() returned null.";
                return false;
            }

            long wid = windowHandle.ToInt64();
            MpvNative.mpv_set_option(_mpv, MpvNative.ToUtf8("wid"), MpvFormat.Int64, ref wid);

            // Disable built-in OSC/keybindings so CastMPV UI stays in full control
            SetOption("osc", "no");
            SetOption("osd-level", "1");
            SetOption("input-default-bindings", "no");
            SetOption("input-vo-keyboard", "no");
            SetOption("keep-open", "no");
            SetOption("idle", "yes");
            SetOption("force-window", "no");

            // Video output & hardware decoding with automatic software fallback
            SetOption("vo", "gpu,direct3d,sdl");
            SetOption("hwdec", settings.HardwareAcceleration ? "auto-safe" : "no");

            // Robust stream caching & reconnect for HTTP/HLS/DASH/IPTV
            SetOption("cache", "yes");
            SetOption("demuxer-max-bytes", "150MiB");
            SetOption("demuxer-max-back-bytes", "50MiB");
            SetOption("network-timeout", "15");
            SetOption("stream-lavf-o", "reconnect=1,reconnect_streamed=1,reconnect_delay_max=5");
            SetOption("ytdl", "no");

            // Subtitle defaults
            ApplySubtitleSettings(settings.SubtitleFontSize, settings.SubtitleColor, applyToMpv: true);
            SetOption("sub-auto", "fuzzy");

            // Screenshot settings
            Directory.CreateDirectory(settings.ScreenshotDirectory);
            SetOption("screenshot-format", "png");
            SetOption("screenshot-directory", settings.ScreenshotDirectory);

            int initRes = MpvNative.mpv_initialize(_mpv);
            if (initRes < 0)
            {
                errorMessage = "mpv_initialize failed: " + MpvNative.GetErrorString(initRes);
                MpvNative.mpv_terminate_destroy(_mpv);
                _mpv = IntPtr.Zero;
                return false;
            }

            SetVolume(settings.DefaultVolume);

            // Observe real-time properties
            Observe("time-pos", MpvFormat.Double, 1);
            Observe("duration", MpvFormat.Double, 2);
            Observe("pause", MpvFormat.Flag, 3);
            Observe("volume", MpvFormat.Double, 4);
            Observe("mute", MpvFormat.Flag, 5);
            Observe("media-title", MpvFormat.String, 6);

            _eventThread = new Thread(EventLoop)
            {
                IsBackground = true,
                Name = "CastMPV-MpvEventLoop"
            };
            _eventThread.Start();

            errorMessage = null;
            return true;
        }

        private void SetOption(string name, string value)
        {
            if (_mpv != IntPtr.Zero)
                MpvNative.mpv_set_option_string(_mpv, MpvNative.ToUtf8(name), MpvNative.ToUtf8(value));
        }

        private void Observe(string name, MpvFormat format, ulong id)
        {
            if (_mpv != IntPtr.Zero)
                MpvNative.mpv_observe_property(_mpv, id, MpvNative.ToUtf8(name), format);
        }

        public void LoadMedia(string uri, string title = null, string userAgent = null, string referer = null)
        {
            if (string.IsNullOrWhiteSpace(uri) || !IsInitialized) return;

            lock (_sync)
            {
                _currentUri = uri.Trim();
                _currentTitle = string.IsNullOrWhiteSpace(title) ? InferTitleFromUri(_currentUri) : title.Trim();
                _subDelay = 0;

                if (!string.IsNullOrWhiteSpace(userAgent))
                {
                    SetPropertyString("user-agent", userAgent.Trim());
                }

                if (!string.IsNullOrWhiteSpace(referer))
                {
                    SetPropertyString("referrer", referer.Trim());
                    SetPropertyString("http-header-fields", $"Referer: {referer.Trim()}");
                }
                else
                {
                    SetPropertyString("referrer", string.Empty);
                    SetPropertyString("http-header-fields", string.Empty);
                }

                SetPropertyString("sub-delay", "0");
                SetPropertyString("force-media-title", _currentTitle);
                ExecuteCommand("loadfile", _currentUri, "replace");
                SetPaused(false);
                _isPlaying = true;
            }

            MediaTitleChanged?.Invoke(_currentTitle);
        }

        public void SetPaused(bool paused)
        {
            if (!IsInitialized) return;
            int flag = paused ? 1 : 0;
            lock (_sync)
            {
                MpvNative.mpv_set_property(_mpv, MpvNative.ToUtf8("pause"), MpvFormat.Flag, ref flag);
            }
        }

        public void TogglePause()
        {
            SetPaused(!_isPaused);
        }

        public void Stop()
        {
            if (!IsInitialized) return;
            lock (_sync)
            {
                _isPlaying = false;
                _position = 0;
                _duration = 0;
                ExecuteCommand("stop");
            }
            PositionChanged?.Invoke(0);
            DurationChanged?.Invoke(0);
        }

        public void SeekAbsolute(double seconds)
        {
            if (!IsInitialized) return;
            if (seconds < 0) seconds = 0;
            if (_duration > 0 && seconds > _duration) seconds = _duration;

            lock (_sync)
            {
                ExecuteCommand("seek", seconds.ToString("0.###", CultureInfo.InvariantCulture), "absolute");
            }
        }

        public void SeekRelative(double deltaSeconds)
        {
            if (!IsInitialized) return;
            lock (_sync)
            {
                ExecuteCommand("seek", deltaSeconds.ToString("0.###", CultureInfo.InvariantCulture), "relative");
            }
        }

        public void SetVolume(int volumePercent)
        {
            if (!IsInitialized) return;
            int clamped = Math.Max(0, Math.Min(100, volumePercent));
            double val = clamped;
            lock (_sync)
            {
                MpvNative.mpv_set_property(_mpv, MpvNative.ToUtf8("volume"), MpvFormat.Double, ref val);
                _volume = clamped;
            }
        }

        public void SetMute(bool muted)
        {
            if (!IsInitialized) return;
            int flag = muted ? 1 : 0;
            lock (_sync)
            {
                MpvNative.mpv_set_property(_mpv, MpvNative.ToUtf8("mute"), MpvFormat.Flag, ref flag);
                _isMuted = muted;
            }
        }

        public void ToggleMute()
        {
            SetMute(!_isMuted);
        }

        public void SetSpeed(double speed)
        {
            if (!IsInitialized) return;
            double clamped = Math.Max(0.25, Math.Min(4.0, speed));
            lock (_sync)
            {
                MpvNative.mpv_set_property(_mpv, MpvNative.ToUtf8("speed"), MpvFormat.Double, ref clamped);
                _speed = clamped;
            }
        }

        public void SetAspectRatio(string mode)
        {
            if (!IsInitialized) return;
            _aspectMode = mode;
            lock (_sync)
            {
                switch (mode)
                {
                    case "16:9":
                        SetPropertyString("keepaspect", "yes");
                        SetPropertyString("video-aspect-override", "16:9");
                        break;
                    case "4:3":
                        SetPropertyString("keepaspect", "yes");
                        SetPropertyString("video-aspect-override", "4:3");
                        break;
                    case "Stretch":
                        SetPropertyString("video-aspect-override", "-1");
                        SetPropertyString("keepaspect", "no");
                        break;
                    default: // "Original"
                        _aspectMode = "Original";
                        SetPropertyString("keepaspect", "yes");
                        SetPropertyString("video-aspect-override", "-1");
                        break;
                }
            }
        }

        public void SetHardwareAcceleration(bool enabled)
        {
            if (!IsInitialized) return;
            lock (_sync)
            {
                SetPropertyString("hwdec", enabled ? "auto-safe" : "no");
            }
        }

        public void AddExternalSubtitle(string subtitlePath)
        {
            if (!IsInitialized || string.IsNullOrWhiteSpace(subtitlePath)) return;
            lock (_sync)
            {
                ExecuteCommand("sub-add", subtitlePath, "select");
            }
        }

        public void AdjustSubtitleDelay(double deltaSeconds)
        {
            if (!IsInitialized) return;
            _subDelay = Math.Round(_subDelay + deltaSeconds, 2);
            SetSubtitleDelay(_subDelay);
        }

        public void SetSubtitleDelay(double seconds)
        {
            if (!IsInitialized) return;
            _subDelay = Math.Round(seconds, 2);
            double val = _subDelay;
            lock (_sync)
            {
                MpvNative.mpv_set_property(_mpv, MpvNative.ToUtf8("sub-delay"), MpvFormat.Double, ref val);
            }
        }

        public void ApplySubtitleSettings(int fontSize, string hexColor, bool applyToMpv = true)
        {
            if (!IsInitialized || !applyToMpv) return;
            lock (_sync)
            {
                SetPropertyString("sub-font-size", fontSize.ToString(CultureInfo.InvariantCulture));
                SetPropertyString("sub-color", string.IsNullOrWhiteSpace(hexColor) ? "#FFFFFF" : hexColor);
                SetPropertyString("sub-border-size", "2.5");
                SetPropertyString("sub-border-color", "#000000");
            }
        }

        public List<MediaTrackInfo> GetTracks()
        {
            var list = new List<MediaTrackInfo>();
            if (!IsInitialized) return list;

            lock (_sync)
            {
                if (MpvNative.mpv_get_property(_mpv, MpvNative.ToUtf8("track-list/count"), MpvFormat.Int64, out long count) < 0)
                    return list;

                for (long i = 0; i < count; i++)
                {
                    string prefix = $"track-list/{i}/";
                    string type = GetPropertyString(prefix + "type") ?? "";
                    MpvNative.mpv_get_property(_mpv, MpvNative.ToUtf8(prefix + "id"), MpvFormat.Int64, out long id);
                    string title = GetPropertyString(prefix + "title");
                    string lang = GetPropertyString(prefix + "lang");
                    MpvNative.mpv_get_property(_mpv, MpvNative.ToUtf8(prefix + "selected"), MpvFormat.Flag, out int selected);

                    list.Add(new MediaTrackInfo
                    {
                        Id = id,
                        Type = type,
                        Title = title,
                        Lang = lang,
                        Selected = selected != 0
                    });
                }
            }

            return list;
        }

        public void SetAudioTrack(long id)
        {
            if (!IsInitialized) return;
            lock (_sync)
            {
                SetPropertyString("aid", id.ToString(CultureInfo.InvariantCulture));
            }
        }

        public void SetSubtitleTrack(long? id)
        {
            if (!IsInitialized) return;
            lock (_sync)
            {
                SetPropertyString("sid", id.HasValue ? id.Value.ToString(CultureInfo.InvariantCulture) : "no");
            }
        }

        public string CaptureScreenshot(string outputDir)
        {
            if (!IsInitialized) return null;
            Directory.CreateDirectory(outputDir);
            string fileName = $"CastMPV_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
            string fullPath = Path.Combine(outputDir, fileName);

            lock (_sync)
            {
                ExecuteCommand("screenshot-to-file", fullPath, "subtitles");
            }
            return fullPath;
        }

        private void SetPropertyString(string name, string value)
        {
            MpvNative.mpv_set_property_string(_mpv, MpvNative.ToUtf8(name), MpvNative.ToUtf8(value ?? string.Empty));
        }

        private string GetPropertyString(string name)
        {
            IntPtr ptr = MpvNative.mpv_get_property_string(_mpv, MpvNative.ToUtf8(name));
            if (ptr == IntPtr.Zero) return null;
            try
            {
                return MpvNative.FromUtf8(ptr);
            }
            finally
            {
                MpvNative.mpv_free(ptr);
            }
        }

        private void ExecuteCommand(params string[] args)
        {
            if (_mpv == IntPtr.Zero || args == null || args.Length == 0) return;

            IntPtr[] ptrs = new IntPtr[args.Length + 1];
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    byte[] utf8 = MpvNative.ToUtf8(args[i]);
                    IntPtr mem = Marshal.AllocHGlobal(utf8.Length);
                    Marshal.Copy(utf8, 0, mem, utf8.Length);
                    ptrs[i] = mem;
                }
                ptrs[args.Length] = IntPtr.Zero;
                MpvNative.mpv_command(_mpv, ptrs);
            }
            finally
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (ptrs[i] != IntPtr.Zero)
                        Marshal.FreeHGlobal(ptrs[i]);
                }
            }
        }

        private void EventLoop()
        {
            while (!_disposed && _mpv != IntPtr.Zero)
            {
                IntPtr evPtr = MpvNative.mpv_wait_event(_mpv, 0.25);
                if (evPtr == IntPtr.Zero) continue;

                var ev = Marshal.PtrToStructure<MpvEvent>(evPtr);
                if (ev.EventId == MpvEventId.None) continue;

                if (ev.EventId == MpvEventId.Shutdown)
                    break;

                switch (ev.EventId)
                {
                    case MpvEventId.FileLoaded:
                        _isPlaying = true;
                        FileLoaded?.Invoke();
                        break;

                    case MpvEventId.EndFile:
                        var endInfo = Marshal.PtrToStructure<MpvEventEndFile>(ev.Data);
                        _isPlaying = false;
                        if (endInfo.Reason == MpvEndFileReason.Error)
                        {
                            ErrorOccurred?.Invoke($"Playback error: {MpvNative.GetErrorString(endInfo.Error)}");
                        }
                        bool completedOrStopped = endInfo.Reason == MpvEndFileReason.Eof || endInfo.Reason == MpvEndFileReason.Stop;
                        PlaybackEnded?.Invoke(completedOrStopped);
                        break;

                    case MpvEventId.PropertyChange:
                        if (ev.Data != IntPtr.Zero)
                        {
                            var prop = Marshal.PtrToStructure<MpvEventProperty>(ev.Data);
                            HandlePropertyChange(prop);
                        }
                        break;
                }
            }
        }

        private void HandlePropertyChange(MpvEventProperty prop)
        {
            string propName = MpvNative.FromUtf8(prop.Name);
            if (string.IsNullOrEmpty(propName) || prop.Data == IntPtr.Zero) return;

            switch (propName)
            {
                case "time-pos" when prop.Format == MpvFormat.Double:
                    double pos = ReadDouble(prop.Data);
                    _position = pos;
                    PositionChanged?.Invoke(pos);
                    break;

                case "duration" when prop.Format == MpvFormat.Double:
                    double dur = ReadDouble(prop.Data);
                    _duration = dur;
                    DurationChanged?.Invoke(dur);
                    break;

                case "pause" when prop.Format == MpvFormat.Flag:
                    bool paused = Marshal.ReadInt32(prop.Data) != 0;
                    _isPaused = paused;
                    PauseStateChanged?.Invoke(paused);
                    break;

                case "volume" when prop.Format == MpvFormat.Double:
                    int vol = (int)Math.Round(ReadDouble(prop.Data));
                    _volume = vol;
                    VolumeChanged?.Invoke(vol);
                    break;

                case "mute" when prop.Format == MpvFormat.Flag:
                    bool muted = Marshal.ReadInt32(prop.Data) != 0;
                    _isMuted = muted;
                    MuteChanged?.Invoke(muted);
                    break;

                case "media-title" when prop.Format == MpvFormat.String:
                    IntPtr strPtr = Marshal.ReadIntPtr(prop.Data);
                    string title = MpvNative.FromUtf8(strPtr);
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        _currentTitle = title;
                        MediaTitleChanged?.Invoke(title);
                    }
                    break;
            }
        }

        private static double ReadDouble(IntPtr ptr)
        {
            byte[] buf = new byte[8];
            Marshal.Copy(ptr, buf, 0, 8);
            return BitConverter.ToDouble(buf, 0);
        }

        private static string InferTitleFromUri(string uri)
        {
            try
            {
                if (Uri.TryCreate(uri, UriKind.Absolute, out Uri parsed))
                {
                    string seg = Path.GetFileName(parsed.LocalPath);
                    if (!string.IsNullOrWhiteSpace(seg))
                        return Uri.UnescapeDataString(seg);
                    return parsed.Host;
                }
            }
            catch
            {
                // Ignore
            }
            return Path.GetFileName(uri);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_mpv != IntPtr.Zero)
            {
                try { MpvNative.mpv_wakeup(_mpv); } catch { }
                try { _eventThread?.Join(800); } catch { }
                try { MpvNative.mpv_terminate_destroy(_mpv); } catch { }
                _mpv = IntPtr.Zero;
            }
        }
    }
}
