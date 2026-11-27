using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using CastMPV.Core;
using CastMPV.DLNA;
using CastMPV.UI;

namespace CastMPV
{
    internal static class Program
    {
        private const string MutexName = @"Global\CastMPV_SingleInstance_Mutex";
        private const string PipeName = "CastMPV_SingleInstance_Pipe";

        [STAThread]
        private static void Main(string[] args)
        {
            bool backgroundStart = args.Any(a =>
                a.Equals("--background", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
            string initialMedia = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));

            using var mutex = new Mutex(true, MutexName, out bool isFirstInstance);
            if (!isFirstInstance)
            {
                SendCommandToRunningInstance(string.IsNullOrWhiteSpace(initialMedia) ? "__SHOW__" : initialMedia);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using var context = new CastMpvContext(backgroundStart, initialMedia);
            Application.Run(context);
        }

        private static void SendCommandToRunningInstance(string payload)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(1500);
                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                client.Write(bytes, 0, bytes.Length);
                client.Flush();
            }
            catch
            {
                // Ignore if primary instance is shutting down
            }
        }

        private sealed class CastMpvContext : ApplicationContext
        {
            private readonly AppSettings _settings;
            private readonly NotifyIcon _trayIcon;
            private readonly ToolStripMenuItem _dlnaStatusItem;
            private readonly ToolStripMenuItem _autoStartItem;
            private readonly MainForm _mainForm;
            private DlnaServer _dlnaServer;
            private volatile bool _shuttingDown;

            public CastMpvContext(bool startSilent, string initialMedia)
            {
                _settings = AppSettings.Load();
                _mainForm = new MainForm(_settings, OpenSettings);

                var trayMenu = new ContextMenuStrip
                {
                    Renderer = new DarkContextMenuRenderer(),
                    BackColor = ThemePalette.BgElevated,
                    ForeColor = ThemePalette.TextPrimary
                };

                var openItem = new ToolStripMenuItem("Open CastMPV") { Font = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold) };
                openItem.Click += (s, e) => _mainForm.EnsureVisibleAndFocused();

                var openUrlItem = new ToolStripMenuItem("Open Stream / URL...");
                openUrlItem.Click += (s, e) => _mainForm.OpenUrlDialog();

                _autoStartItem = new ToolStripMenuItem("Start with Windows")
                {
                    Checked = StartupManager.IsAutoStartEnabled(),
                    CheckOnClick = true
                };
                _autoStartItem.Click += (s, e) => StartupManager.SetAutoStart(_autoStartItem.Checked);

                _dlnaStatusItem = new ToolStripMenuItem("DLNA Receiver: Starting...");
                _dlnaStatusItem.Click += (s, e) => ToggleDlnaServer();

                var settingsItem = new ToolStripMenuItem("Settings...");
                settingsItem.Click += (s, e) => OpenSettings(0);

                var subSettingsItem = new ToolStripMenuItem("Subtitle Settings...");
                subSettingsItem.Click += (s, e) => OpenSettings(1);

                var searchOpenSubsItem = new ToolStripMenuItem("Search OpenSubtitles...");
                searchOpenSubsItem.Click += (s, e) => _mainForm.OpenOpenSubtitlesDialog();

                var exitItem = new ToolStripMenuItem("Exit CastMPV");
                exitItem.Click += (s, e) => ExitApplication();

                trayMenu.Items.AddRange(new ToolStripItem[]
                {
                    openItem,
                    openUrlItem,
                    new ToolStripSeparator(),
                    _dlnaStatusItem,
                    _autoStartItem,
                    settingsItem,
                    subSettingsItem,
                    searchOpenSubsItem,
                    new ToolStripSeparator(),
                    exitItem
                });

                _trayIcon = new NotifyIcon
                {
                    Icon = IconFactory.CreateAppIcon(32),
                    Text = $"CastMPV — {_settings.DeviceName}",
                    ContextMenuStrip = trayMenu,
                    Visible = true
                };
                _trayIcon.DoubleClick += (s, e) => _mainForm.EnsureVisibleAndFocused();

                StartPipeListener();
                StartOrRestartDlnaServer();

                if (!string.IsNullOrWhiteSpace(initialMedia))
                {
                    _mainForm.WakeAndPlay(new CastMediaRequest { Uri = initialMedia }, fromDlnaCast: false);
                }
                else if (!startSilent && !_settings.StartMinimizedToTray)
                {
                    _mainForm.EnsureVisibleAndFocused();
                }
            }

            private void StartOrRestartDlnaServer()
            {
                _dlnaServer?.Dispose();
                _dlnaServer = null;

                if (!_settings.DlnaEnabled)
                {
                    _dlnaStatusItem.Text = "DLNA Receiver: Disabled (Click to Enable)";
                    _trayIcon.Text = "CastMPV (DLNA Disabled)";
                    return;
                }

                _dlnaServer = new DlnaServer(_settings.DeviceName, _settings.DeviceUuid, _settings.DlnaPort);
                _dlnaServer.GetPlaybackSnapshot = () => new PlaybackSnapshot
                {
                    TransportState = !_mainForm.Player.IsPlaying
                        ? "STOPPED"
                        : (_mainForm.Player.IsPaused ? "PAUSED_PLAYBACK" : "PLAYING"),
                    CurrentUri = _mainForm.Player.CurrentUri,
                    PositionSeconds = _mainForm.Player.Position,
                    DurationSeconds = _mainForm.Player.Duration,
                    Volume = _mainForm.Player.Volume,
                    Muted = _mainForm.Player.IsMuted
                };

                _dlnaServer.CastMediaRequested += req => _mainForm.WakeAndPlay(req, fromDlnaCast: true);
                _dlnaServer.PlayRequested += () => _mainForm.Player.SetPaused(false);
                _dlnaServer.PauseRequested += () => _mainForm.Player.SetPaused(true);
                _dlnaServer.StopRequested += () => _mainForm.Player.Stop();
                _dlnaServer.SeekRequested += sec => _mainForm.Player.SeekAbsolute(sec);
                _dlnaServer.VolumeRequested += vol => _mainForm.Player.SetVolume(vol);
                _dlnaServer.MuteRequested += muted => _mainForm.Player.SetMute(muted);

                try
                {
                    _dlnaServer.Start();
                    string info = $"DLNA: {_settings.DeviceName} ({_dlnaServer.GetPrimaryLanIp()}:{_dlnaServer.ActivePort})";
                    _dlnaStatusItem.Text = info;
                    _trayIcon.Text = info.Length > 63 ? info.Substring(0, 63) : info;
                }
                catch (Exception ex)
                {
                    _dlnaStatusItem.Text = $"DLNA Error: {ex.Message}";
                }
            }

            private void ToggleDlnaServer()
            {
                _settings.DlnaEnabled = !_settings.DlnaEnabled;
                _settings.Save();
                StartOrRestartDlnaServer();
            }

            private void OpenSettings(int initialTab = 0)
            {
                _mainForm.EnsureVisibleAndFocused();
                string oldName = _settings.DeviceName;
                int oldPort = _settings.DlnaPort;
                bool oldEnabled = _settings.DlnaEnabled;

                using var dlg = new SettingsForm(
                    _settings,
                    initialTab,
                    applyLiveCallback: () => _mainForm.ApplySettings(),
                    loadSubtitleCallback: path => _mainForm.Player.AddExternalSubtitle(path));

                if (dlg.ShowDialog(_mainForm) == DialogResult.OK)
                {
                    _autoStartItem.Checked = StartupManager.IsAutoStartEnabled();
                    _mainForm.ApplySettings();

                    if (oldName != _settings.DeviceName || oldPort != _settings.DlnaPort || oldEnabled != _settings.DlnaEnabled)
                    {
                        StartOrRestartDlnaServer();
                    }
                }
            }

            private void StartPipeListener()
            {
                var thread = new Thread(() =>
                {
                    while (!_shuttingDown)
                    {
                        try
                        {
                            using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1);
                            server.WaitForConnection();
                            using var reader = new StreamReader(server, Encoding.UTF8);
                            string payload = reader.ReadToEnd()?.Trim();
                            if (string.IsNullOrWhiteSpace(payload) || _shuttingDown) continue;

                            if (payload == "__SHOW__")
                            {
                                _mainForm.BeginInvoke(new Action(() => _mainForm.EnsureVisibleAndFocused()));
                            }
                            else
                            {
                                _mainForm.WakeAndPlay(new CastMediaRequest { Uri = payload }, fromDlnaCast: false);
                            }
                        }
                        catch
                        {
                            if (_shuttingDown) break;
                            Thread.Sleep(250);
                        }
                    }
                })
                {
                    IsBackground = true,
                    Name = "CastMPV-SingleInstancePipe"
                };
                thread.Start();
            }

            private void ExitApplication()
            {
                _shuttingDown = true;
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _dlnaServer?.Dispose();
                _mainForm.ForceExit();
                ExitThread();
            }
        }
    }
}
