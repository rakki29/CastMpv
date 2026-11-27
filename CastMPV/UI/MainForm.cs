using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CastMPV.Core;
using CastMPV.DLNA;
using CastMPV.Player;

namespace CastMPV.UI
{
    public sealed class MainForm : Form
    {
        private static readonly string[] SubtitleExtensions = { ".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx" };

        private readonly AppSettings _settings;
        private readonly MpvPlayer _player;
        private readonly Action _openSettingsCallback;

        private Panel _topBar;
        private Label _lblTitle;
        private DarkFlatButton _btnPin;

        private Panel _videoContainer;
        private Panel _mpvHostPanel;
        private Label _lblIdleHint;
        private Label _lblOsd;

        private Panel _bottomBar;
        private SeekTrackBar _seekBar;
        private DarkFlatButton _btnPlayPause;
        private DarkFlatButton _btnStop;
        private DarkFlatButton _btnBack10;
        private DarkFlatButton _btnFwd10;
        private Label _lblTime;
        private DarkFlatButton _btnMute;
        private VolumeSlider _volumeSlider;
        private DarkFlatButton _btnSpeed;
        private DarkFlatButton _btnAspect;
        private DarkFlatButton _btnSubs;
        private DarkFlatButton _btnAudio;
        private DarkFlatButton _btnShot;
        private DarkFlatButton _btnFullscreen;

        private ContextMenuStrip _videoContextMenu;
        private System.Windows.Forms.Timer _fullscreenHideTimer;
        private System.Windows.Forms.Timer _osdTimer;
        private System.Windows.Forms.Timer _mousePollTimer;

        private bool _isFullscreen;
        private FormBorderStyle _savedBorderStyle;
        private FormWindowState _savedWindowState;
        private Rectangle _savedBounds;
        private bool _forceExit;
        private bool _castSessionActive;
        private bool _cursorHidden;
        private Point _lastMousePos;
        private DateTime _lastMouseMoveUtc = DateTime.UtcNow;

        public MpvPlayer Player => _player;

        public MainForm(AppSettings settings, Action openSettingsCallback)
        {
            _settings = settings;
            _openSettingsCallback = openSettingsCallback;
            _player = new MpvPlayer();

            Text = "CastMPV";
            Size = new Size(1060, 640);
            MinimumSize = new Size(640, 400);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = ThemePalette.BgCanvas;
            ForeColor = ThemePalette.TextPrimary;
            KeyPreview = true;
            AllowDrop = true;
            Icon = IconFactory.CreateAppIcon(64);

            BuildLayout();
            BuildContextMenu();
            WireEvents();

            HandleCreated += (s, e) =>
            {
                ThemeHelper.EnableDarkTitleBar(this);
                InitializePlayerEngine();
            };
        }

        private void BuildLayout()
        {
            // Top bar
            _topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = ThemePalette.BgSurface,
                Padding = new Padding(10, 4, 10, 4)
            };

            var lblBrand = new Label
            {
                Text = "CastMPV",
                Dock = DockStyle.Left,
                AutoSize = true,
                ForeColor = ThemePalette.Accent,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 4, 10, 0)
            };

            _lblTitle = new Label
            {
                Text = $"DLNA Ready ({_settings.DeviceName})",
                Dock = DockStyle.Fill,
                ForeColor = ThemePalette.TextMuted,
                Font = new Font("Segoe UI", 9f),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            var topRight = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent
            };

            var btnOpenFile = MakeTopButton("Open File", 76, (s, e) => OpenFileDialog());
            var btnOpenUrl = MakeTopButton("Open URL", 74, (s, e) => OpenUrlDialog());
            _btnPin = MakeTopButton("Pin", 44, (s, e) => ToggleAlwaysOnTop());
            var btnSettings = MakeTopButton("Settings", 68, (s, e) => _openSettingsCallback?.Invoke());

            topRight.Controls.AddRange(new Control[] { btnOpenFile, btnOpenUrl, _btnPin, btnSettings });
            _topBar.Controls.Add(_lblTitle);
            _topBar.Controls.Add(lblBrand);
            _topBar.Controls.Add(topRight);

            // Bottom control dock
            _bottomBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 68,
                BackColor = ThemePalette.BgSurface,
                Padding = new Padding(8, 2, 8, 6)
            };

            _seekBar = new SeekTrackBar
            {
                Dock = DockStyle.Top,
                Height = 22
            };

            var controlsRow = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 4, 0, 0)
            };

            var leftFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Left,
                AutoSize = true,
                WrapContents = false,
                BackColor = Color.Transparent
            };

            _btnPlayPause = MakeControlButton("Play", 52, (s, e) => _player.TogglePause());
            _btnStop = MakeControlButton("Stop", 48, (s, e) => StopPlayback());
            _btnBack10 = MakeControlButton("-10s", 44, (s, e) => _player.SeekRelative(-10));
            _btnFwd10 = MakeControlButton("+10s", 44, (s, e) => _player.SeekRelative(10));
            _lblTime = new Label
            {
                Text = "00:00 / 00:00",
                AutoSize = true,
                ForeColor = ThemePalette.TextMuted,
                Font = new Font("Consolas", 9f),
                Margin = new Padding(8, 8, 8, 0)
            };
            leftFlow.Controls.AddRange(new Control[] { _btnPlayPause, _btnStop, _btnBack10, _btnFwd10, _lblTime });

            var rightFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                WrapContents = false,
                BackColor = Color.Transparent
            };

            _btnMute = MakeControlButton("Vol", 38, (s, e) => _player.ToggleMute());
            _volumeSlider = new VolumeSlider
            {
                Value = _settings.DefaultVolume,
                Margin = new Padding(2, 4, 8, 0)
            };
            _btnSpeed = MakeControlButton("1.0x", 48, ShowSpeedMenu);
            _btnAspect = MakeControlButton("Original", 64, ShowAspectMenu);
            _btnSubs = MakeControlButton("Subtitles", 68, ShowSubtitleMenu);
            _btnAudio = MakeControlButton("Audio", 54, ShowAudioMenu);
            _btnShot = MakeControlButton("Shot", 48, (s, e) => TakeScreenshot());
            _btnFullscreen = MakeControlButton("Full", 46, (s, e) => ToggleFullscreen());

            rightFlow.Controls.AddRange(new Control[]
            {
                _btnMute, _volumeSlider, _btnSpeed, _btnAspect, _btnSubs, _btnAudio, _btnShot, _btnFullscreen
            });

            controlsRow.Controls.Add(leftFlow);
            controlsRow.Controls.Add(rightFlow);
            _bottomBar.Controls.Add(controlsRow);
            _bottomBar.Controls.Add(_seekBar);

            // Video viewport
            _videoContainer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ThemePalette.BgCanvas
            };

            _mpvHostPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ThemePalette.BgCanvas,
                AllowDrop = true
            };

            _lblIdleHint = new Label
            {
                Text = $"CastMPV Ready\nDrop a video file here, press Ctrl+U to open a stream URL,\nor cast directly from your phone to \"{_settings.DeviceName}\".",
                ForeColor = ThemePalette.TextMuted,
                Font = new Font("Segoe UI", 11f),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };

            _lblOsd = new Label
            {
                AutoSize = true,
                Visible = false,
                Location = new Point(18, 18),
                Padding = new Padding(10, 6, 10, 6),
                BackColor = Color.FromArgb(200, 15, 23, 42),
                ForeColor = ThemePalette.TextPrimary,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold)
            };

            _mpvHostPanel.Controls.Add(_lblIdleHint);
            _mpvHostPanel.Controls.Add(_lblOsd);
            _lblOsd.BringToFront();

            _videoContainer.Controls.Add(_mpvHostPanel);

            Controls.Add(_videoContainer);
            Controls.Add(_topBar);
            Controls.Add(_bottomBar);

            _fullscreenHideTimer = new System.Windows.Forms.Timer { Interval = 2500 };
            _fullscreenHideTimer.Tick += (s, e) => HideFullscreenOverlays();

            _osdTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _osdTimer.Tick += (s, e) =>
            {
                _osdTimer.Stop();
                _lblOsd.Visible = false;
            };

            _mousePollTimer = new System.Windows.Forms.Timer { Interval = 150 };
            _mousePollTimer.Tick += PollMouseInFullscreen;
        }

        private DarkFlatButton MakeTopButton(string text, int width, EventHandler onClick)
        {
            var btn = new DarkFlatButton
            {
                Text = text,
                Size = new Size(width, 28),
                Margin = new Padding(2, 0, 2, 0)
            };
            btn.Click += onClick;
            return btn;
        }

        private DarkFlatButton MakeControlButton(string text, int width, EventHandler onClick)
        {
            var btn = new DarkFlatButton
            {
                Text = text,
                Size = new Size(width, 32),
                Margin = new Padding(2, 0, 2, 0)
            };
            btn.Click += onClick;
            return btn;
        }

        private void BuildContextMenu()
        {
            _videoContextMenu = new ContextMenuStrip
            {
                Renderer = new DarkContextMenuRenderer(),
                BackColor = ThemePalette.BgElevated,
                ForeColor = ThemePalette.TextPrimary
            };

            _videoContextMenu.Items.Add("Open File...", null, (s, e) => OpenFileDialog());
            _videoContextMenu.Items.Add("Open URL / Stream...", null, (s, e) => OpenUrlDialog());
            _videoContextMenu.Items.Add(new ToolStripSeparator());
            _videoContextMenu.Items.Add("Play / Pause", null, (s, e) => _player.TogglePause());
            _videoContextMenu.Items.Add("Stop", null, (s, e) => StopPlayback());
            _videoContextMenu.Items.Add(new ToolStripSeparator());
            _videoContextMenu.Items.Add("Load External Subtitle...", null, (s, e) => LoadSubtitleDialog());
            _videoContextMenu.Items.Add("Capture Screenshot", null, (s, e) => TakeScreenshot());
            _videoContextMenu.Items.Add("Toggle Fullscreen", null, (s, e) => ToggleFullscreen());
            _videoContextMenu.Items.Add(new ToolStripSeparator());
            _videoContextMenu.Items.Add("Settings...", null, (s, e) => _openSettingsCallback?.Invoke());

            _mpvHostPanel.ContextMenuStrip = _videoContextMenu;
            _lblIdleHint.ContextMenuStrip = _videoContextMenu;
        }

        private void WireEvents()
        {
            _seekBar.SeekCommitted += sec => _player.SeekAbsolute(sec);
            _volumeSlider.VolumeChanged += vol => _player.SetVolume(vol);

            _mpvHostPanel.DoubleClick += (s, e) => ToggleFullscreen();
            _lblIdleHint.DoubleClick += (s, e) => ToggleFullscreen();

            _mpvHostPanel.MouseWheel += OnVideoMouseWheel;
            _lblIdleHint.MouseWheel += OnVideoMouseWheel;

            DragEnter += OnMediaDragEnter;
            DragDrop += OnMediaDragDrop;
            _mpvHostPanel.DragEnter += OnMediaDragEnter;
            _mpvHostPanel.DragDrop += OnMediaDragDrop;

            KeyDown += OnPlayerKeyDown;

            _player.PositionChanged += pos => BeginUi(() =>
            {
                _seekBar.Value = pos;
                UpdateTimeLabel();
            });

            _player.DurationChanged += dur => BeginUi(() =>
            {
                _seekBar.Maximum = dur > 0 ? dur : 100;
                UpdateTimeLabel();
            });

            _player.PauseStateChanged += paused => BeginUi(() =>
            {
                _btnPlayPause.Text = paused ? "Play" : "Pause";
            });

            _player.VolumeChanged += vol => BeginUi(() =>
            {
                _volumeSlider.Value = vol;
            });

            _player.MuteChanged += muted => BeginUi(() =>
            {
                _btnMute.Text = muted ? "Muted" : "Vol";
                _btnMute.ForeColor = muted ? ThemePalette.Danger : ThemePalette.TextPrimary;
            });

            _player.MediaTitleChanged += title => BeginUi(() =>
            {
                Text = string.IsNullOrWhiteSpace(title) ? "CastMPV" : $"{title} — CastMPV";
                _lblTitle.Text = title ?? string.Empty;
            });

            _player.FileLoaded += () => BeginUi(() =>
            {
                _lblIdleHint.Visible = false;
                _btnPlayPause.Text = "Pause";
            });

            _player.PlaybackEnded += completedOrStopped => BeginUi(() =>
            {
                _lblIdleHint.Visible = true;
                _btnPlayPause.Text = "Play";
                _seekBar.Value = 0;
                _lblTime.Text = "00:00 / 00:00";
                Text = "CastMPV";
                _lblTitle.Text = $"DLNA Ready ({_settings.DeviceName})";

                if (completedOrStopped && _castSessionActive && _settings.AutoMinimizeOnStop)
                {
                    _castSessionActive = false;
                    if (_isFullscreen) SetFullscreen(false);
                    HideToTray();
                }
            });

            _player.ErrorOccurred += err => BeginUi(() => ShowOsd(err));
        }

        private void InitializePlayerEngine()
        {
            if (_player.IsInitialized) return;
            if (!_player.Initialize(_mpvHostPanel.Handle, _settings, out string err))
            {
                MessageBox.Show(
                    this,
                    err,
                    "CastMPV — libmpv Engine Missing",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        public void ApplySettings()
        {
            _player.SetHardwareAcceleration(_settings.HardwareAcceleration);
            _player.ApplySubtitleSettings(_settings.SubtitleFontSize, _settings.SubtitleColor);
            if (!_player.IsPlaying)
            {
                _lblTitle.Text = $"DLNA Ready ({_settings.DeviceName})";
                _lblIdleHint.Text = $"CastMPV Ready\nDrop a video file here, press Ctrl+U to open a stream URL,\nor cast directly from your phone to \"{_settings.DeviceName}\".";
            }
        }

        public void WakeAndPlay(CastMediaRequest request, bool fromDlnaCast)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Uri)) return;

            BeginUi(() =>
            {
                _castSessionActive = fromDlnaCast;
                EnsureVisibleAndFocused();

                if (fromDlnaCast && _settings.AutoFullscreenOnCast && !_isFullscreen)
                {
                    SetFullscreen(true);
                }

                _lblIdleHint.Visible = false;
                _player.LoadMedia(request.Uri, request.Title, request.UserAgent, request.Referer);
                ShowOsd(string.IsNullOrWhiteSpace(request.Title) ? "Playing stream..." : request.Title);
            });
        }

        public void EnsureVisibleAndFocused()
        {
            if (!IsHandleCreated) CreateHandle();
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;

            // Brief TopMost pulse guarantees reliable foreground activation on Windows
            bool wasTopMost = TopMost;
            TopMost = true;
            Activate();
            BringToFront();
            TopMost = wasTopMost;
        }

        public void HideToTray()
        {
            if (_isFullscreen) SetFullscreen(false);
            ShowCursorIfHidden();
            Hide();
        }

        public void ForceExit()
        {
            _forceExit = true;
            _fullscreenHideTimer.Stop();
            _mousePollTimer.Stop();
            _osdTimer.Stop();
            _player.Dispose();
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_forceExit && e.CloseReason == CloseReason.UserClosing && _settings.MinimizeToTrayOnClose)
            {
                e.Cancel = true;
                _castSessionActive = false;
                _player.Stop();
                HideToTray();
                return;
            }
            _player.Dispose();
            base.OnFormClosing(e);
        }

        private void StopPlayback()
        {
            _castSessionActive = false;
            _player.Stop();
        }

        private void OpenFileDialog()
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Open Video or Audio File",
                Filter = "Media Files|*.mp4;*.mkv;*.avi;*.ts;*.m2ts;*.mov;*.webm;*.flv;*.wmv;*.mp3;*.flac;*.aac;*.m3u8;*.mpd|All Files|*.*"
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _castSessionActive = false;
                _lblIdleHint.Visible = false;
                _player.LoadMedia(dlg.FileName, Path.GetFileName(dlg.FileName));
            }
        }

        public void OpenUrlDialog()
        {
            EnsureVisibleAndFocused();
            using var dlg = new OpenUrlForm(_settings.DefaultUserAgent);
            if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.MediaUrl))
            {
                _castSessionActive = false;
                _lblIdleHint.Visible = false;
                _player.LoadMedia(dlg.MediaUrl, null, dlg.CustomUserAgent, dlg.CustomReferer);
            }
        }

        private void LoadSubtitleDialog()
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Load External Subtitle",
                Filter = "Subtitle Files|*.srt;*.ass;*.ssa;*.vtt;*.sub|All Files|*.*"
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _player.AddExternalSubtitle(dlg.FileName);
                ShowOsd($"Subtitle loaded: {Path.GetFileName(dlg.FileName)}");
            }
        }

        private void ShowSpeedMenu(object sender, EventArgs e)
        {
            var menu = CreateDarkDropdown();
            double[] speeds = { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };
            foreach (double spd in speeds)
            {
                var item = new ToolStripMenuItem($"{spd:0.##}x")
                {
                    Checked = Math.Abs(_player.Speed - spd) < 0.01
                };
                item.Click += (_, __) =>
                {
                    _player.SetSpeed(spd);
                    _btnSpeed.Text = $"{spd:0.##}x";
                    ShowOsd($"Speed: {spd:0.##}x");
                };
                menu.Items.Add(item);
            }
            menu.Show(_btnSpeed, new Point(0, -menu.PreferredSize.Height));
        }

        private void ShowAspectMenu(object sender, EventArgs e)
        {
            var menu = CreateDarkDropdown();
            string[] modes = { "Original", "16:9", "4:3", "Stretch" };
            foreach (string mode in modes)
            {
                var item = new ToolStripMenuItem(mode)
                {
                    Checked = _player.AspectMode == mode
                };
                item.Click += (_, __) =>
                {
                    _player.SetAspectRatio(mode);
                    _btnAspect.Text = mode;
                    ShowOsd($"Aspect: {mode}");
                };
                menu.Items.Add(item);
            }
            menu.Show(_btnAspect, new Point(0, -menu.PreferredSize.Height));
        }

        private void ShowAudioMenu(object sender, EventArgs e)
        {
            var menu = CreateDarkDropdown();
            List<MediaTrackInfo> tracks = _player.GetTracks().Where(t => t.Type == "audio").ToList();
            if (tracks.Count == 0)
            {
                menu.Items.Add(new ToolStripMenuItem("No audio tracks") { Enabled = false });
            }
            else
            {
                foreach (MediaTrackInfo track in tracks)
                {
                    var item = new ToolStripMenuItem(track.DisplayLabel) { Checked = track.Selected };
                    item.Click += (_, __) =>
                    {
                        _player.SetAudioTrack(track.Id);
                        ShowOsd($"Audio: {track.DisplayLabel}");
                    };
                    menu.Items.Add(item);
                }
            }
            menu.Show(_btnAudio, new Point(0, -menu.PreferredSize.Height));
        }

        private void ShowSubtitleMenu(object sender, EventArgs e)
        {
            var menu = CreateDarkDropdown();
            menu.Items.Add("Load External Subtitle (SRT/ASS/VTT)...", null, (_, __) => LoadSubtitleDialog());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add($"Delay -0.5s (Current: {_player.SubtitleDelay:+0.0;-0.0;0.0}s)", null, (_, __) =>
            {
                _player.AdjustSubtitleDelay(-0.5);
                ShowOsd($"Subtitle Delay: {_player.SubtitleDelay:+0.0;-0.0;0.0}s");
            });
            menu.Items.Add("Delay +0.5s", null, (_, __) =>
            {
                _player.AdjustSubtitleDelay(0.5);
                ShowOsd($"Subtitle Delay: {_player.SubtitleDelay:+0.0;-0.0;0.0}s");
            });
            menu.Items.Add("Reset Subtitle Delay", null, (_, __) =>
            {
                _player.SetSubtitleDelay(0);
                ShowOsd("Subtitle Delay: 0.0s");
            });

            List<MediaTrackInfo> subTracks = _player.GetTracks().Where(t => t.Type == "sub").ToList();
            if (subTracks.Count > 0)
            {
                menu.Items.Add(new ToolStripSeparator());
                var offItem = new ToolStripMenuItem("Subtitles Off") { Checked = subTracks.All(t => !t.Selected) };
                offItem.Click += (_, __) => _player.SetSubtitleTrack(null);
                menu.Items.Add(offItem);

                foreach (MediaTrackInfo track in subTracks)
                {
                    var item = new ToolStripMenuItem(track.DisplayLabel) { Checked = track.Selected };
                    item.Click += (_, __) => _player.SetSubtitleTrack(track.Id);
                    menu.Items.Add(item);
                }
            }

            menu.Show(_btnSubs, new Point(0, -menu.PreferredSize.Height));
        }

        private static ContextMenuStrip CreateDarkDropdown() => new ContextMenuStrip
        {
            Renderer = new DarkContextMenuRenderer(),
            BackColor = ThemePalette.BgElevated,
            ForeColor = ThemePalette.TextPrimary
        };

        private void TakeScreenshot()
        {
            string saved = _player.CaptureScreenshot(_settings.ScreenshotDirectory);
            if (!string.IsNullOrEmpty(saved))
                ShowOsd($"Screenshot saved: {Path.GetFileName(saved)}");
        }

        private void ToggleAlwaysOnTop()
        {
            TopMost = !TopMost;
            _btnPin.ForeColor = TopMost ? ThemePalette.Accent : ThemePalette.TextPrimary;
            ShowOsd(TopMost ? "Always on Top: ON" : "Always on Top: OFF");
        }

        public void ToggleFullscreen() => SetFullscreen(!_isFullscreen);

        private void SetFullscreen(bool fullscreen)
        {
            if (_isFullscreen == fullscreen) return;
            _isFullscreen = fullscreen;

            if (fullscreen)
            {
                _savedBorderStyle = FormBorderStyle;
                _savedWindowState = WindowState;
                _savedBounds = Bounds;

                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal;
                Bounds = Screen.FromControl(this).Bounds;

                _btnFullscreen.Text = "Exit";
                _lastMousePos = Cursor.Position;
                _lastMouseMoveUtc = DateTime.UtcNow;
                _fullscreenHideTimer.Start();
                _mousePollTimer.Start();
            }
            else
            {
                _fullscreenHideTimer.Stop();
                _mousePollTimer.Stop();
                ShowCursorIfHidden();

                _topBar.Visible = true;
                _bottomBar.Visible = true;
                FormBorderStyle = _savedBorderStyle;
                WindowState = _savedWindowState;
                if (_savedWindowState == FormWindowState.Normal)
                    Bounds = _savedBounds;
                _btnFullscreen.Text = "Full";
            }
        }

        private void PollMouseInFullscreen(object sender, EventArgs e)
        {
            if (!_isFullscreen) return;
            Point pos = Cursor.Position;
            if (pos != _lastMousePos)
            {
                _lastMousePos = pos;
                _lastMouseMoveUtc = DateTime.UtcNow;
                ShowCursorIfHidden();
                if (!_topBar.Visible || !_bottomBar.Visible)
                {
                    _topBar.Visible = true;
                    _bottomBar.Visible = true;
                }
                _fullscreenHideTimer.Stop();
                _fullscreenHideTimer.Start();
            }
        }

        private void HideFullscreenOverlays()
        {
            if (!_isFullscreen) return;
            Point local = PointToClient(Cursor.Position);
            if (_bottomBar.Bounds.Contains(local) || _topBar.Bounds.Contains(local))
                return;

            if ((DateTime.UtcNow - _lastMouseMoveUtc).TotalMilliseconds >= 2300)
            {
                _topBar.Visible = false;
                _bottomBar.Visible = false;
                if (!_cursorHidden)
                {
                    Cursor.Hide();
                    _cursorHidden = true;
                }
                _fullscreenHideTimer.Stop();
            }
        }

        private void ShowCursorIfHidden()
        {
            if (_cursorHidden)
            {
                Cursor.Show();
                _cursorHidden = false;
            }
        }

        private void OnVideoMouseWheel(object sender, MouseEventArgs e)
        {
            int next = _player.Volume + (e.Delta > 0 ? 5 : -5);
            _player.SetVolume(next);
            ShowOsd($"Volume: {Math.Max(0, Math.Min(100, next))}%");
        }

        private void OnMediaDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void OnMediaDragDrop(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] files) || files.Length == 0)
                return;

            string first = files[0];
            string ext = Path.GetExtension(first)?.ToLowerInvariant() ?? string.Empty;
            if (SubtitleExtensions.Contains(ext))
            {
                _player.AddExternalSubtitle(first);
                ShowOsd($"Subtitle loaded: {Path.GetFileName(first)}");
            }
            else
            {
                _castSessionActive = false;
                _lblIdleHint.Visible = false;
                _player.LoadMedia(first, Path.GetFileName(first));
            }
        }

        private void OnPlayerKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Space:
                case Keys.K:
                    _player.TogglePause();
                    e.Handled = true;
                    break;
                case Keys.F:
                case Keys.Enter:
                    ToggleFullscreen();
                    e.Handled = true;
                    break;
                case Keys.Escape:
                    if (_isFullscreen) SetFullscreen(false);
                    e.Handled = true;
                    break;
                case Keys.Left:
                    _player.SeekRelative(e.Shift ? -30 : -5);
                    e.Handled = true;
                    break;
                case Keys.Right:
                    _player.SeekRelative(e.Shift ? 30 : 5);
                    e.Handled = true;
                    break;
                case Keys.Up:
                    _player.SetVolume(_player.Volume + 5);
                    ShowOsd($"Volume: {_player.Volume}%");
                    e.Handled = true;
                    break;
                case Keys.Down:
                    _player.SetVolume(_player.Volume - 5);
                    ShowOsd($"Volume: {_player.Volume}%");
                    e.Handled = true;
                    break;
                case Keys.M:
                    _player.ToggleMute();
                    ShowOsd(_player.IsMuted ? "Muted" : "Unmuted");
                    e.Handled = true;
                    break;
                case Keys.S:
                    TakeScreenshot();
                    e.Handled = true;
                    break;
                case Keys.OemOpenBrackets:
                    double slower = Math.Max(0.5, Math.Round(_player.Speed - 0.25, 2));
                    _player.SetSpeed(slower);
                    _btnSpeed.Text = $"{slower:0.##}x";
                    ShowOsd($"Speed: {slower:0.##}x");
                    e.Handled = true;
                    break;
                case Keys.OemCloseBrackets:
                    double faster = Math.Min(2.0, Math.Round(_player.Speed + 0.25, 2));
                    _player.SetSpeed(faster);
                    _btnSpeed.Text = $"{faster:0.##}x";
                    ShowOsd($"Speed: {faster:0.##}x");
                    e.Handled = true;
                    break;
                case Keys.O when e.Control:
                    OpenFileDialog();
                    e.Handled = true;
                    break;
                case Keys.U when e.Control:
                    OpenUrlDialog();
                    e.Handled = true;
                    break;
            }
        }

        private void UpdateTimeLabel()
        {
            _lblTime.Text = $"{FormatTime(_player.Position)} / {FormatTime(_player.Duration)}";
        }

        private static string FormatTime(double sec)
        {
            if (double.IsNaN(sec) || sec <= 0) return "00:00";
            TimeSpan ts = TimeSpan.FromSeconds(sec);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
                : $"{ts.Minutes:00}:{ts.Seconds:00}";
        }

        private void ShowOsd(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            _lblOsd.Text = message;
            _lblOsd.Visible = true;
            _lblOsd.BringToFront();
            _osdTimer.Stop();
            _osdTimer.Start();
        }

        private void BeginUi(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch
            {
                // Ignore if form is disposing
            }
        }
    }
}
