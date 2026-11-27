using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;
using CastMPV.Core;

namespace CastMPV.UI
{
    public sealed class SettingsForm : Form
    {
        private readonly AppSettings _settings;
        private readonly Action _applyLiveCallback;
        private readonly Action<string> _loadSubtitleCallback;

        private Button _tabGeneralBtn;
        private Button _tabSubtitleBtn;
        private Panel _generalPanel;
        private Panel _subtitlePanel;

        // General & DLNA controls
        private TextBox _txtDeviceName;
        private NumericUpDown _numPort;
        private CheckBox _chkDlnaEnabled;
        private CheckBox _chkAutoFullscreen;
        private CheckBox _chkAutoMinimizeOnStop;
        private CheckBox _chkAutoStartWindows;
        private CheckBox _chkStartMinimized;
        private CheckBox _chkMinimizeOnClose;
        private CheckBox _chkHwDec;
        private NumericUpDown _numDefaultVol;
        private TextBox _txtScreenshots;

        // Subtitle controls
        private ComboBox _cmbSubFont;
        private CheckBox _chkSubBold;
        private TrackBar _trkSubSize;
        private NumericUpDown _numSubSize;
        private Button _btnTextColor;
        private TrackBar _trkTextOpacity;
        private Label _lblTextOpacityVal;
        private Button _btnBackColor;
        private TrackBar _trkBackOpacity;
        private Label _lblBackOpacityVal;
        private NumericUpDown _numBorderSize;
        private Button _btnBorderColor;
        private NumericUpDown _numShadowOffset;
        private TrackBar _trkSubPos;
        private Label _lblSubPosVal;
        private NumericUpDown _numSubDelay;
        private ComboBox _cmbAssOverride;
        private ComboBox _cmbCodepage;
        private CheckBox _chkAutoDetectSub;
        private ComboBox _cmbOpenSubLang;
        private TextBox _txtOpenSubApiKey;
        private Panel _previewCanvas;

        private string _subTextHex;
        private string _subBackHex;
        private string _subBorderHex;
        private bool _syncingControls;

        public SettingsForm(
            AppSettings settings,
            int initialTab = 0,
            Action applyLiveCallback = null,
            Action<string> loadSubtitleCallback = null)
        {
            _settings = settings;
            _applyLiveCallback = applyLiveCallback;
            _loadSubtitleCallback = loadSubtitleCallback;

            _subTextHex = settings.SubtitleColor;
            _subBackHex = settings.SubtitleBackColor;
            _subBorderHex = settings.SubtitleBorderColor;

            Text = "CastMPV Settings";
            Size = new Size(640, 620);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = ThemePalette.BgSurface;
            ForeColor = ThemePalette.TextPrimary;
            Font = new Font("Segoe UI", 9.5f);

            BuildTabHeader();
            BuildGeneralPanel();
            BuildSubtitlePanel();
            BuildFooter();

            SwitchTab(initialTab);
            HandleCreated += (s, e) => ThemeHelper.EnableDarkTitleBar(this);
        }

        private void BuildTabHeader()
        {
            var tabBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 42,
                BackColor = ThemePalette.BgCanvas,
                Padding = new Padding(16, 6, 16, 0)
            };

            _tabGeneralBtn = MakeTabButton("General & DLNA", 16);
            _tabGeneralBtn.Click += (s, e) => SwitchTab(0);

            _tabSubtitleBtn = MakeTabButton("Subtitle Settings", 162);
            _tabSubtitleBtn.Click += (s, e) => SwitchTab(1);

            tabBar.Controls.Add(_tabGeneralBtn);
            tabBar.Controls.Add(_tabSubtitleBtn);
            Controls.Add(tabBar);
        }

        private Button MakeTabButton(string text, int x)
        {
            var btn = new Button
            {
                Text = text,
                Location = new Point(x, 6),
                Size = new Size(140, 36),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private void SwitchTab(int index)
        {
            bool isSub = index == 1;
            _generalPanel.Visible = !isSub;
            _subtitlePanel.Visible = isSub;

            _tabGeneralBtn.BackColor = !isSub ? ThemePalette.BgSurface : ThemePalette.BgCanvas;
            _tabGeneralBtn.ForeColor = !isSub ? ThemePalette.Accent : ThemePalette.TextMuted;

            _tabSubtitleBtn.BackColor = isSub ? ThemePalette.BgSurface : ThemePalette.BgCanvas;
            _tabSubtitleBtn.ForeColor = isSub ? ThemePalette.Accent : ThemePalette.TextMuted;
        }

        private void BuildGeneralPanel()
        {
            _generalPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ThemePalette.BgSurface,
                Padding = new Padding(20)
            };

            int y = 18;
            _generalPanel.Controls.Add(SectionLabel("DLNA / UPnP Receiver", 20, y)); y += 28;

            _generalPanel.Controls.Add(FieldLabel("Device Name:", 20, y + 3));
            _txtDeviceName = DarkTextBox(150, y, 440, _settings.DeviceName);
            _generalPanel.Controls.Add(_txtDeviceName); y += 36;

            _generalPanel.Controls.Add(FieldLabel("HTTP Port:", 20, y + 3));
            _numPort = DarkNumeric(150, y, 95, 1024, 65535, _settings.DlnaPort);
            _generalPanel.Controls.Add(_numPort);

            _chkDlnaEnabled = DarkCheck("Enable DLNA Media Renderer", 270, y + 2, _settings.DlnaEnabled);
            _generalPanel.Controls.Add(_chkDlnaEnabled); y += 36;

            _chkAutoFullscreen = DarkCheck("Auto-Fullscreen when video is cast", 20, y, _settings.AutoFullscreenOnCast);
            _generalPanel.Controls.Add(_chkAutoFullscreen); y += 28;

            _chkAutoMinimizeOnStop = DarkCheck("Minimize to System Tray when playback stops/ends", 20, y, _settings.AutoMinimizeOnStop);
            _generalPanel.Controls.Add(_chkAutoMinimizeOnStop); y += 38;

            _generalPanel.Controls.Add(SectionLabel("Windows Integration & Engine", 20, y)); y += 28;

            _chkAutoStartWindows = DarkCheck("Start CastMPV automatically with Windows", 20, y, StartupManager.IsAutoStartEnabled());
            _generalPanel.Controls.Add(_chkAutoStartWindows); y += 28;

            _chkStartMinimized = DarkCheck("Start silently in System Tray", 20, y, _settings.StartMinimizedToTray);
            _generalPanel.Controls.Add(_chkStartMinimized); y += 28;

            _chkMinimizeOnClose = DarkCheck("Minimize to System Tray instead of exiting on Close (X)", 20, y, _settings.MinimizeToTrayOnClose);
            _generalPanel.Controls.Add(_chkMinimizeOnClose); y += 28;

            _chkHwDec = DarkCheck("Enable Hardware Video Decoding (auto-safe fallback)", 20, y, _settings.HardwareAcceleration);
            _generalPanel.Controls.Add(_chkHwDec); y += 36;

            _generalPanel.Controls.Add(FieldLabel("Default Volume (%):", 20, y + 3));
            _numDefaultVol = DarkNumeric(150, y, 80, 0, 100, _settings.DefaultVolume);
            _generalPanel.Controls.Add(_numDefaultVol); y += 38;

            _generalPanel.Controls.Add(SectionLabel("Screenshots", 20, y)); y += 28;
            _generalPanel.Controls.Add(FieldLabel("Output Folder:", 20, y + 3));
            _txtScreenshots = DarkTextBox(150, y, 345, _settings.ScreenshotDirectory);
            _generalPanel.Controls.Add(_txtScreenshots);

            var btnBrowse = MakeSecondaryButton("Browse...", 504, y - 1, 86, 26);
            btnBrowse.Click += (s, e) =>
            {
                using var dlg = new FolderBrowserDialog { SelectedPath = _txtScreenshots.Text };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _txtScreenshots.Text = dlg.SelectedPath;
            };
            _generalPanel.Controls.Add(btnBrowse);

            Controls.Add(_generalPanel);
            _generalPanel.BringToFront();
        }

        private void BuildSubtitlePanel()
        {
            _subtitlePanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ThemePalette.BgSurface,
                AutoScroll = true,
                Padding = new Padding(20)
            };

            int y = 14;
            _subtitlePanel.Controls.Add(SectionLabel("Font & Typography (UTF-8 / Arabic Supported)", 20, y)); y += 26;

            _subtitlePanel.Controls.Add(FieldLabel("Font Family:", 20, y + 4));
            _cmbSubFont = new ComboBox
            {
                Location = new Point(130, y),
                Width = 260,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = ThemePalette.BgElevated,
                ForeColor = ThemePalette.TextPrimary
            };
            PopulateInstalledFonts(_cmbSubFont, _settings.SubtitleFontFamily);
            _cmbSubFont.SelectedIndexChanged += (s, e) => UpdatePreview();
            _subtitlePanel.Controls.Add(_cmbSubFont);

            _chkSubBold = DarkCheck("Bold Text", 408, y + 3, _settings.SubtitleBold);
            _chkSubBold.CheckedChanged += (s, e) => UpdatePreview();
            _subtitlePanel.Controls.Add(_chkSubBold); y += 34;

            _subtitlePanel.Controls.Add(FieldLabel("Font Size:", 20, y + 4));
            _trkSubSize = new TrackBar
            {
                Location = new Point(126, y),
                Width = 220,
                Minimum = 16,
                Maximum = 120,
                TickStyle = TickStyle.None,
                Value = Math.Max(16, Math.Min(120, _settings.SubtitleFontSize)),
                BackColor = ThemePalette.BgSurface
            };
            _numSubSize = DarkNumeric(355, y + 2, 65, 16, 120, _settings.SubtitleFontSize);
            _trkSubSize.Scroll += (s, e) =>
            {
                if (_syncingControls) return;
                _syncingControls = true;
                _numSubSize.Value = _trkSubSize.Value;
                _syncingControls = false;
                UpdatePreview();
            };
            _numSubSize.ValueChanged += (s, e) =>
            {
                if (_syncingControls) return;
                _syncingControls = true;
                _trkSubSize.Value = (int)_numSubSize.Value;
                _syncingControls = false;
                UpdatePreview();
            };
            _subtitlePanel.Controls.Add(_trkSubSize);
            _subtitlePanel.Controls.Add(_numSubSize); y += 40;

            _subtitlePanel.Controls.Add(SectionLabel("Colors, Opacity & Background Box", 20, y)); y += 26;

            // Text Color + Opacity
            _subtitlePanel.Controls.Add(FieldLabel("Text Color:", 20, y + 4));
            _btnTextColor = MakeColorSwatchButton(130, y, _subTextHex);
            _btnTextColor.Click += (s, e) => PickColor(ref _subTextHex, _btnTextColor);
            _subtitlePanel.Controls.Add(_btnTextColor);

            _subtitlePanel.Controls.Add(FieldLabel("Opacity:", 250, y + 4));
            _trkTextOpacity = new TrackBar
            {
                Location = new Point(312, y),
                Width = 175,
                Minimum = 0,
                Maximum = 100,
                TickStyle = TickStyle.None,
                Value = _settings.SubtitleOpacity,
                BackColor = ThemePalette.BgSurface
            };
            _lblTextOpacityVal = FieldLabel($"{_settings.SubtitleOpacity}%", 495, y + 4);
            _lblTextOpacityVal.ForeColor = ThemePalette.TextPrimary;
            _trkTextOpacity.Scroll += (s, e) =>
            {
                _lblTextOpacityVal.Text = $"{_trkTextOpacity.Value}%";
                UpdatePreview();
            };
            _subtitlePanel.Controls.Add(_trkTextOpacity);
            _subtitlePanel.Controls.Add(_lblTextOpacityVal); y += 36;

            // Background Box Color + Opacity
            _subtitlePanel.Controls.Add(FieldLabel("Box Color:", 20, y + 4));
            _btnBackColor = MakeColorSwatchButton(130, y, _subBackHex);
            _btnBackColor.Click += (s, e) => PickColor(ref _subBackHex, _btnBackColor);
            _subtitlePanel.Controls.Add(_btnBackColor);

            _subtitlePanel.Controls.Add(FieldLabel("Box Opacity:", 250, y + 4));
            _trkBackOpacity = new TrackBar
            {
                Location = new Point(332, y),
                Width = 155,
                Minimum = 0,
                Maximum = 100,
                TickStyle = TickStyle.None,
                Value = _settings.SubtitleBackOpacity,
                BackColor = ThemePalette.BgSurface
            };
            _lblBackOpacityVal = FieldLabel(_settings.SubtitleBackOpacity == 0 ? "Off" : $"{_settings.SubtitleBackOpacity}%", 495, y + 4);
            _lblBackOpacityVal.ForeColor = ThemePalette.TextPrimary;
            _trkBackOpacity.Scroll += (s, e) =>
            {
                _lblBackOpacityVal.Text = _trkBackOpacity.Value == 0 ? "Off" : $"{_trkBackOpacity.Value}%";
                UpdatePreview();
            };
            _subtitlePanel.Controls.Add(_trkBackOpacity);
            _subtitlePanel.Controls.Add(_lblBackOpacityVal); y += 38;

            // Border & Shadow
            _subtitlePanel.Controls.Add(FieldLabel("Border Size:", 20, y + 4));
            _numBorderSize = new NumericUpDown
            {
                Location = new Point(130, y),
                Width = 68,
                Minimum = 0,
                Maximum = 10,
                DecimalPlaces = 1,
                Increment = 0.5m,
                Value = (decimal)_settings.SubtitleBorderSize,
                BackColor = ThemePalette.BgElevated,
                ForeColor = ThemePalette.TextPrimary
            };
            _numBorderSize.ValueChanged += (s, e) => UpdatePreview();
            _subtitlePanel.Controls.Add(_numBorderSize);

            _subtitlePanel.Controls.Add(FieldLabel("Border Color:", 212, y + 4));
            _btnBorderColor = MakeColorSwatchButton(300, y, _subBorderHex);
            _btnBorderColor.Click += (s, e) => PickColor(ref _subBorderHex, _btnBorderColor);
            _subtitlePanel.Controls.Add(_btnBorderColor);

            _subtitlePanel.Controls.Add(FieldLabel("Shadow:", 420, y + 4));
            _numShadowOffset = new NumericUpDown
            {
                Location = new Point(480, y),
                Width = 68,
                Minimum = 0,
                Maximum = 12,
                DecimalPlaces = 1,
                Increment = 0.5m,
                Value = (decimal)_settings.SubtitleShadowOffset,
                BackColor = ThemePalette.BgElevated,
                ForeColor = ThemePalette.TextPrimary
            };
            _numShadowOffset.ValueChanged += (s, e) => UpdatePreview();
            _subtitlePanel.Controls.Add(_numShadowOffset); y += 40;

            _subtitlePanel.Controls.Add(SectionLabel("Position, Sync & External Subtitles", 20, y)); y += 26;

            // Vertical Position
            _subtitlePanel.Controls.Add(FieldLabel("Vertical Pos:", 20, y + 4));
            _trkSubPos = new TrackBar
            {
                Location = new Point(126, y),
                Width = 240,
                Minimum = 0,
                Maximum = 100,
                TickStyle = TickStyle.None,
                Value = _settings.SubtitlePosition,
                BackColor = ThemePalette.BgSurface
            };
            _lblSubPosVal = FieldLabel($"{_settings.SubtitlePosition}%", 374, y + 4);
            _lblSubPosVal.ForeColor = ThemePalette.TextPrimary;
            _trkSubPos.Scroll += (s, e) =>
            {
                _lblSubPosVal.Text = $"{_trkSubPos.Value}%";
                UpdatePreview();
            };
            _subtitlePanel.Controls.Add(_trkSubPos);
            _subtitlePanel.Controls.Add(_lblSubPosVal); y += 38;

            // Delay (+/- 0.1s)
            _subtitlePanel.Controls.Add(FieldLabel("Sync Delay (s):", 20, y + 4));
            _numSubDelay = new NumericUpDown
            {
                Location = new Point(130, y),
                Width = 78,
                Minimum = -60,
                Maximum = 60,
                DecimalPlaces = 1,
                Increment = 0.1m,
                Value = (decimal)_settings.SubtitleDefaultDelay,
                BackColor = ThemePalette.BgElevated,
                ForeColor = ThemePalette.TextPrimary
            };
            _subtitlePanel.Controls.Add(_numSubDelay);

            var btnDelayMinus = MakeSecondaryButton("-0.1s", 216, y - 1, 56, 26);
            btnDelayMinus.Click += (s, e) => _numSubDelay.Value = Math.Max(_numSubDelay.Minimum, _numSubDelay.Value - 0.1m);
            var btnDelayPlus = MakeSecondaryButton("+0.1s", 278, y - 1, 56, 26);
            btnDelayPlus.Click += (s, e) => _numSubDelay.Value = Math.Min(_numSubDelay.Maximum, _numSubDelay.Value + 0.1m);
            var btnDelayReset = MakeSecondaryButton("Reset", 340, y - 1, 58, 26);
            btnDelayReset.Click += (s, e) => _numSubDelay.Value = 0;

            var btnLoadSub = MakeSecondaryButton("Load External Subtitle (.srt/.ass/.vtt)...", 408, y - 1, 195, 26);
            btnLoadSub.Click += LoadExternalSubtitleClicked;

            _subtitlePanel.Controls.AddRange(new Control[] { btnDelayMinus, btnDelayPlus, btnDelayReset, btnLoadSub });
            y += 36;

            // ASS Override & Codepage
            _subtitlePanel.Controls.Add(FieldLabel("ASS Override:", 20, y + 4));
            _cmbAssOverride = new ComboBox
            {
                Location = new Point(130, y),
                Width = 140,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = ThemePalette.BgElevated,
                ForeColor = ThemePalette.TextPrimary
            };
            _cmbAssOverride.Items.AddRange(new object[] { "yes", "force", "scale", "strip", "no" });
            _cmbAssOverride.SelectedItem = _cmbAssOverride.Items.Contains(_settings.SubtitleAssOverride) ? _settings.SubtitleAssOverride : "yes";
            _subtitlePanel.Controls.Add(_cmbAssOverride);

            _subtitlePanel.Controls.Add(FieldLabel("Encoding:", 290, y + 4));
            _cmbCodepage = new ComboBox
            {
                Location = new Point(365, y),
                Width = 155,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = ThemePalette.BgElevated,
                ForeColor = ThemePalette.TextPrimary
            };
            _cmbCodepage.Items.AddRange(new object[] { "auto", "utf-8", "cp1256", "cp1252" });
            _cmbCodepage.SelectedItem = _cmbCodepage.Items.Contains(_settings.SubtitleCodepage) ? _settings.SubtitleCodepage : "auto";
            _subtitlePanel.Controls.Add(_cmbCodepage);

            var btnResetDefaults = MakeSecondaryButton("Defaults", 530, y - 1, 73, 26);
            btnResetDefaults.Click += ResetSubtitleDefaults;
            _subtitlePanel.Controls.Add(btnResetDefaults);
            y += 36;

            _chkAutoDetectSub = DarkCheck("Auto-detect matching .srt/.ass/.vtt files in local folder or cache", 20, y, _settings.AutoDetectLocalSubtitles);
            _subtitlePanel.Controls.Add(_chkAutoDetectSub);
            y += 32;

            _subtitlePanel.Controls.Add(SectionLabel("OpenSubtitles Online Search", 20, y)); y += 26;

            _subtitlePanel.Controls.Add(FieldLabel("Language:", 20, y + 4));
            _cmbOpenSubLang = new ComboBox
            {
                Location = new Point(130, y),
                Width = 72,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = ThemePalette.BgElevated,
                ForeColor = ThemePalette.TextPrimary
            };
            _cmbOpenSubLang.Items.AddRange(new object[] { "en", "ar", "es", "fr", "de", "it", "pt", "tr", "ru", "zh", "ja", "ko", "all" });
            _cmbOpenSubLang.SelectedItem = _cmbOpenSubLang.Items.Contains(_settings.OpenSubtitlesLanguage) ? _settings.OpenSubtitlesLanguage : "en";
            _subtitlePanel.Controls.Add(_cmbOpenSubLang);

            _subtitlePanel.Controls.Add(FieldLabel("API Key:", 212, y + 4));
            _txtOpenSubApiKey = DarkTextBox(272, y + 1, 165, _settings.OpenSubtitlesApiKey);
            _txtOpenSubApiKey.UseSystemPasswordChar = true;
            _subtitlePanel.Controls.Add(_txtOpenSubApiKey);

            var btnSearchOpenSubs = MakeSecondaryButton("Search OpenSubtitles...", 446, y - 1, 157, 26);
            btnSearchOpenSubs.Click += SearchOpenSubtitlesClicked;
            _subtitlePanel.Controls.Add(btnSearchOpenSubs);
            y += 36;

            // Live Preview Box
            _previewCanvas = new Panel
            {
                Location = new Point(20, y),
                Size = new Size(583, 86),
                BackColor = Color.FromArgb(16, 20, 30),
                BorderStyle = BorderStyle.FixedSingle
            };
            _previewCanvas.Paint += PaintSubtitlePreview;
            _subtitlePanel.Controls.Add(_previewCanvas);

            Controls.Add(_subtitlePanel);
            _subtitlePanel.BringToFront();
        }

        private void BuildFooter()
        {
            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 54,
                BackColor = ThemePalette.BgCanvas,
                Padding = new Padding(16, 10, 16, 10)
            };

            var btnApply = new Button
            {
                Text = "Apply Live",
                Location = new Point(304, 11),
                Size = new Size(92, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemePalette.BgElevated,
                ForeColor = ThemePalette.TextPrimary,
                Cursor = Cursors.Hand
            };
            btnApply.FlatAppearance.BorderColor = ThemePalette.Border;
            btnApply.Click += (s, e) =>
            {
                CommitFormToSettings();
                _applyLiveCallback?.Invoke();
            };

            var btnSave = new Button
            {
                Text = "Save Settings",
                DialogResult = DialogResult.OK,
                Location = new Point(404, 11),
                Size = new Size(112, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemePalette.Accent,
                ForeColor = Color.FromArgb(10, 15, 25),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += (s, e) => CommitFormToSettings();

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(524, 11),
                Size = new Size(92, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemePalette.BgElevated,
                ForeColor = ThemePalette.TextPrimary,
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderColor = ThemePalette.Border;

            footer.Controls.AddRange(new Control[] { btnApply, btnSave, btnCancel });
            Controls.Add(footer);

            AcceptButton = btnSave;
            CancelButton = btnCancel;
        }

        private void PopulateInstalledFonts(ComboBox combo, string selectedFamily)
        {
            try
            {
                using var fonts = new InstalledFontCollection();
                var names = fonts.Families.Select(f => f.Name).OrderBy(n => n).ToArray();
                combo.Items.AddRange(names);
            }
            catch
            {
                combo.Items.AddRange(new object[] { "Segoe UI", "Arial", "Tahoma", "Consolas" });
            }

            if (combo.Items.Contains(selectedFamily))
                combo.SelectedItem = selectedFamily;
            else if (combo.Items.Contains("Segoe UI"))
                combo.SelectedItem = "Segoe UI";
            else if (combo.Items.Count > 0)
                combo.SelectedIndex = 0;
        }

        private void PickColor(ref string hexTarget, Button swatchButton)
        {
            Color initial = ParseHexColor(hexTarget, Color.White);
            using var dlg = new ColorDialog { Color = initial, FullOpen = true };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                hexTarget = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
                swatchButton.Text = hexTarget;
                swatchButton.BackColor = dlg.Color;
                swatchButton.ForeColor = dlg.Color.GetBrightness() > 0.55f ? Color.Black : Color.White;
                UpdatePreview();
            }
        }

        private void LoadExternalSubtitleClicked(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Load External Subtitle",
                Filter = "Subtitle Files|*.srt;*.ass;*.ssa;*.vtt;*.sub|All Files|*.*"
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _loadSubtitleCallback?.Invoke(dlg.FileName);
            }
        }

        private void SearchOpenSubtitlesClicked(object sender, EventArgs e)
        {
            _settings.OpenSubtitlesApiKey = _txtOpenSubApiKey.Text?.Trim() ?? string.Empty;
            _settings.OpenSubtitlesLanguage = _cmbOpenSubLang.SelectedItem?.ToString() ?? "en";
            _settings.Save();

            using var dlg = new OpenSubtitlesForm(_settings);
            if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.DownloadedSubtitlePath))
            {
                _loadSubtitleCallback?.Invoke(dlg.DownloadedSubtitlePath);
            }
            _txtOpenSubApiKey.Text = _settings.OpenSubtitlesApiKey;
            if (_cmbOpenSubLang.Items.Contains(_settings.OpenSubtitlesLanguage))
                _cmbOpenSubLang.SelectedItem = _settings.OpenSubtitlesLanguage;
        }

        private void ResetSubtitleDefaults(object sender, EventArgs e)
        {
            _syncingControls = true;
            if (_cmbSubFont.Items.Contains("Segoe UI")) _cmbSubFont.SelectedItem = "Segoe UI";
            _chkSubBold.Checked = false;
            _trkSubSize.Value = 48;
            _numSubSize.Value = 48;

            _subTextHex = "#FFFFFF";
            UpdateSwatch(_btnTextColor, _subTextHex);
            _trkTextOpacity.Value = 100;
            _lblTextOpacityVal.Text = "100%";

            _subBackHex = "#000000";
            UpdateSwatch(_btnBackColor, _subBackHex);
            _trkBackOpacity.Value = 55;
            _lblBackOpacityVal.Text = "55%";

            _numBorderSize.Value = 2.0m;
            _subBorderHex = "#000000";
            UpdateSwatch(_btnBorderColor, _subBorderHex);
            _numShadowOffset.Value = 1.0m;
            _trkSubPos.Value = 96;
            _lblSubPosVal.Text = "96%";
            _numSubDelay.Value = 0.0m;
            _cmbAssOverride.SelectedItem = "yes";
            _cmbCodepage.SelectedItem = "auto";
            _syncingControls = false;
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            _previewCanvas?.Invalidate();
        }

        private void PaintSubtitlePreview(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            // Subtle cinema gradient backdrop
            using var bgBrush = new LinearGradientBrush(
                _previewCanvas.ClientRectangle,
                Color.FromArgb(20, 26, 38),
                Color.FromArgb(10, 13, 20),
                LinearGradientMode.Vertical);
            g.FillRectangle(bgBrush, _previewCanvas.ClientRectangle);

            string sample = "Subtitle Preview · معاينة الترجمة العربية";
            string family = _cmbSubFont.SelectedItem?.ToString() ?? "Segoe UI";
            FontStyle style = _chkSubBold.Checked ? FontStyle.Bold : FontStyle.Regular;
            float previewPx = Math.Max(10f, Math.Min(26f, (float)_numSubSize.Value * 0.34f));

            using var font = new Font(family, previewPx, style);
            SizeF textSize = g.MeasureString(sample, font);

            float x = (_previewCanvas.Width - textSize.Width) / 2f;
            float usableH = Math.Max(8f, _previewCanvas.Height - textSize.Height - 10f);
            float y = 5f + usableH * (_trkSubPos.Value / 100f);

            if (_trkBackOpacity.Value > 0)
            {
                Color boxRgb = ParseHexColor(_subBackHex, Color.Black);
                int boxAlpha = (int)Math.Round(_trkBackOpacity.Value / 100.0 * 255.0);
                using var boxBrush = new SolidBrush(Color.FromArgb(boxAlpha, boxRgb));
                g.FillRectangle(boxBrush, x - 8f, y - 2f, textSize.Width + 16f, textSize.Height + 4f);
            }

            float shadow = (float)_numShadowOffset.Value;
            if (shadow > 0)
            {
                using var shadowBrush = new SolidBrush(Color.FromArgb(140, 0, 0, 0));
                g.DrawString(sample, font, shadowBrush, x + shadow, y + shadow);
            }

            if (_numBorderSize.Value > 0)
            {
                Color borderCol = ParseHexColor(_subBorderHex, Color.Black);
                using var borderBrush = new SolidBrush(Color.FromArgb(220, borderCol));
                float b = Math.Min(2.5f, (float)_numBorderSize.Value * 0.5f);
                g.DrawString(sample, font, borderBrush, x - b, y);
                g.DrawString(sample, font, borderBrush, x + b, y);
                g.DrawString(sample, font, borderBrush, x, y - b);
                g.DrawString(sample, font, borderBrush, x, y + b);
            }

            Color textRgb = ParseHexColor(_subTextHex, Color.White);
            int textAlpha = (int)Math.Round(_trkTextOpacity.Value / 100.0 * 255.0);
            using var textBrush = new SolidBrush(Color.FromArgb(textAlpha, textRgb));
            g.DrawString(sample, font, textBrush, x, y);
        }

        private void CommitFormToSettings()
        {
            _settings.DeviceName = string.IsNullOrWhiteSpace(_txtDeviceName.Text) ? "CastMPV Receiver" : _txtDeviceName.Text.Trim();
            _settings.DlnaPort = (int)_numPort.Value;
            _settings.DlnaEnabled = _chkDlnaEnabled.Checked;
            _settings.AutoFullscreenOnCast = _chkAutoFullscreen.Checked;
            _settings.AutoMinimizeOnStop = _chkAutoMinimizeOnStop.Checked;
            _settings.StartMinimizedToTray = _chkStartMinimized.Checked;
            _settings.MinimizeToTrayOnClose = _chkMinimizeOnClose.Checked;
            _settings.HardwareAcceleration = _chkHwDec.Checked;
            _settings.DefaultVolume = (int)_numDefaultVol.Value;
            _settings.ScreenshotDirectory = _txtScreenshots.Text.Trim();

            _settings.SubtitleFontFamily = _cmbSubFont.SelectedItem?.ToString() ?? "Segoe UI";
            _settings.SubtitleBold = _chkSubBold.Checked;
            _settings.SubtitleFontSize = (int)_numSubSize.Value;
            _settings.SubtitleColor = _subTextHex;
            _settings.SubtitleOpacity = _trkTextOpacity.Value;
            _settings.SubtitleBackColor = _subBackHex;
            _settings.SubtitleBackOpacity = _trkBackOpacity.Value;
            _settings.SubtitleBorderSize = (double)_numBorderSize.Value;
            _settings.SubtitleBorderColor = _subBorderHex;
            _settings.SubtitleShadowOffset = (double)_numShadowOffset.Value;
            _settings.SubtitlePosition = _trkSubPos.Value;
            _settings.SubtitleDefaultDelay = (double)_numSubDelay.Value;
            _settings.SubtitleAssOverride = _cmbAssOverride.SelectedItem?.ToString() ?? "yes";
            _settings.SubtitleCodepage = _cmbCodepage.SelectedItem?.ToString() ?? "auto";
            _settings.AutoDetectLocalSubtitles = _chkAutoDetectSub.Checked;
            _settings.OpenSubtitlesLanguage = _cmbOpenSubLang.SelectedItem?.ToString() ?? "en";
            _settings.OpenSubtitlesApiKey = _txtOpenSubApiKey.Text?.Trim() ?? string.Empty;

            _settings.Save();
            StartupManager.SetAutoStart(_chkAutoStartWindows.Checked);
        }

        private static Color ParseHexColor(string hex, Color fallback)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hex))
                    return ColorTranslator.FromHtml(hex.Trim());
            }
            catch { }
            return fallback;
        }

        private static Button MakeColorSwatchButton(int x, int y, string hex)
        {
            var btn = new Button
            {
                Location = new Point(x, y),
                Size = new Size(92, 26),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Consolas", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = ThemePalette.Border;
            UpdateSwatch(btn, hex);
            return btn;
        }

        private static void UpdateSwatch(Button btn, string hex)
        {
            Color c = ParseHexColor(hex, Color.White);
            btn.Text = hex;
            btn.BackColor = c;
            btn.ForeColor = c.GetBrightness() > 0.55f ? Color.Black : Color.White;
        }

        private static Button MakeSecondaryButton(string text, int x, int y, int w, int h)
        {
            var btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemePalette.BgElevated,
                ForeColor = ThemePalette.TextPrimary,
                Font = new Font("Segoe UI", 8.5f),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = ThemePalette.Border;
            return btn;
        }

        private static Label SectionLabel(string text, int x, int y) => new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = ThemePalette.Accent,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };

        private static Label FieldLabel(string text, int x, int y) => new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = ThemePalette.TextMuted
        };

        private static TextBox DarkTextBox(int x, int y, int w, string value) => new TextBox
        {
            Location = new Point(x, y),
            Width = w,
            Text = value ?? string.Empty,
            BackColor = ThemePalette.BgElevated,
            ForeColor = ThemePalette.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle
        };

        private static NumericUpDown DarkNumeric(int x, int y, int w, int min, int max, int val) => new NumericUpDown
        {
            Location = new Point(x, y),
            Width = w,
            Minimum = min,
            Maximum = max,
            Value = Math.Max(min, Math.Min(max, val)),
            BackColor = ThemePalette.BgElevated,
            ForeColor = ThemePalette.TextPrimary
        };

        private static CheckBox DarkCheck(string text, int x, int y, bool isChecked) => new CheckBox
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            Checked = isChecked,
            ForeColor = ThemePalette.TextPrimary
        };
    }
}
