using System;
using System.Drawing;
using System.Windows.Forms;
using CastMPV.Core;

namespace CastMPV.UI
{
    public sealed class SettingsForm : Form
    {
        private readonly AppSettings _settings;

        private readonly TextBox _txtDeviceName;
        private readonly NumericUpDown _numPort;
        private readonly CheckBox _chkDlnaEnabled;
        private readonly CheckBox _chkAutoFullscreen;
        private readonly CheckBox _chkAutoMinimizeOnStop;
        private readonly CheckBox _chkAutoStartWindows;
        private readonly CheckBox _chkStartMinimized;
        private readonly CheckBox _chkMinimizeOnClose;
        private readonly CheckBox _chkHwDec;
        private readonly NumericUpDown _numSubSize;
        private readonly ComboBox _cmbSubColor;
        private readonly TextBox _txtScreenshots;

        public SettingsForm(AppSettings settings)
        {
            _settings = settings;

            Text = "CastMPV Settings";
            Size = new Size(500, 480);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = ThemePalette.BgSurface;
            ForeColor = ThemePalette.TextPrimary;
            Font = new Font("Segoe UI", 9.5f);

            int y = 18;
            Controls.Add(SectionLabel("DLNA / UPnP Receiver", 18, y)); y += 26;

            Controls.Add(FieldLabel("Device Name:", 18, y + 3));
            _txtDeviceName = DarkTextBox(150, y, 310, _settings.DeviceName);
            Controls.Add(_txtDeviceName); y += 34;

            Controls.Add(FieldLabel("HTTP Port:", 18, y + 3));
            _numPort = new NumericUpDown
            {
                Location = new Point(150, y),
                Width = 90,
                Minimum = 1024,
                Maximum = 65535,
                Value = _settings.DlnaPort,
                BackColor = ThemePalette.BgElevated,
                ForeColor = ThemePalette.TextPrimary
            };
            Controls.Add(_numPort);

            _chkDlnaEnabled = DarkCheck("Enable DLNA Media Renderer", 260, y + 2, _settings.DlnaEnabled);
            Controls.Add(_chkDlnaEnabled); y += 34;

            _chkAutoFullscreen = DarkCheck("Auto-Fullscreen when video is cast", 18, y, _settings.AutoFullscreenOnCast);
            Controls.Add(_chkAutoFullscreen); y += 26;

            _chkAutoMinimizeOnStop = DarkCheck("Minimize to System Tray when playback stops/ends", 18, y, _settings.AutoMinimizeOnStop);
            Controls.Add(_chkAutoMinimizeOnStop); y += 34;

            Controls.Add(SectionLabel("Windows Integration & Engine", 18, y)); y += 26;

            _chkAutoStartWindows = DarkCheck("Start CastMPV automatically with Windows", 18, y, StartupManager.IsAutoStartEnabled());
            Controls.Add(_chkAutoStartWindows); y += 26;

            _chkStartMinimized = DarkCheck("Start silently in System Tray", 18, y, _settings.StartMinimizedToTray);
            Controls.Add(_chkStartMinimized); y += 26;

            _chkMinimizeOnClose = DarkCheck("Minimize to System Tray instead of exiting on Close (X)", 18, y, _settings.MinimizeToTrayOnClose);
            Controls.Add(_chkMinimizeOnClose); y += 26;

            _chkHwDec = DarkCheck("Enable Hardware Video Decoding (auto-safe fallback)", 18, y, _settings.HardwareAcceleration);
            Controls.Add(_chkHwDec); y += 34;

            Controls.Add(SectionLabel("Subtitles & Screenshots", 18, y)); y += 26;

            Controls.Add(FieldLabel("Subtitle Size:", 18, y + 3));
            _numSubSize = new NumericUpDown
            {
                Location = new Point(150, y),
                Width = 75,
                Minimum = 16,
                Maximum = 120,
                Value = _settings.SubtitleFontSize,
                BackColor = ThemePalette.BgElevated,
                ForeColor = ThemePalette.TextPrimary
            };
            Controls.Add(_numSubSize);

            Controls.Add(FieldLabel("Color:", 245, y + 3));
            _cmbSubColor = new ComboBox
            {
                Location = new Point(295, y),
                Width = 165,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = ThemePalette.BgElevated,
                ForeColor = ThemePalette.TextPrimary
            };
            _cmbSubColor.Items.AddRange(new object[] { "#FFFFFF (White)", "#FACC15 (Yellow)", "#38BDF8 (Sky)", "#4ADE80 (Green)" });
            SelectColorItem(_settings.SubtitleColor);
            Controls.Add(_cmbSubColor); y += 34;

            Controls.Add(FieldLabel("Screenshots:", 18, y + 3));
            _txtScreenshots = DarkTextBox(150, y, 235, _settings.ScreenshotDirectory);
            Controls.Add(_txtScreenshots);

            var btnBrowse = new Button
            {
                Text = "Browse...",
                Location = new Point(392, y - 1),
                Size = new Size(68, 25),
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemePalette.BgElevated,
                ForeColor = ThemePalette.TextPrimary
            };
            btnBrowse.FlatAppearance.BorderColor = ThemePalette.Border;
            btnBrowse.Click += (s, e) =>
            {
                using var dlg = new FolderBrowserDialog { SelectedPath = _txtScreenshots.Text };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _txtScreenshots.Text = dlg.SelectedPath;
            };
            Controls.Add(btnBrowse); y += 44;

            var btnSave = new Button
            {
                Text = "Save Settings",
                DialogResult = DialogResult.OK,
                Location = new Point(246, y),
                Size = new Size(112, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemePalette.Accent,
                ForeColor = Color.FromArgb(10, 15, 25),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += SaveClicked;

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(366, y),
                Size = new Size(94, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemePalette.BgElevated,
                ForeColor = ThemePalette.TextPrimary
            };
            btnCancel.FlatAppearance.BorderColor = ThemePalette.Border;

            Controls.Add(btnSave);
            Controls.Add(btnCancel);

            AcceptButton = btnSave;
            CancelButton = btnCancel;
            HandleCreated += (s, e) => ThemeHelper.EnableDarkTitleBar(this);
        }

        private void SelectColorItem(string hex)
        {
            for (int i = 0; i < _cmbSubColor.Items.Count; i++)
            {
                if (_cmbSubColor.Items[i].ToString().StartsWith(hex, StringComparison.OrdinalIgnoreCase))
                {
                    _cmbSubColor.SelectedIndex = i;
                    return;
                }
            }
            _cmbSubColor.SelectedIndex = 0;
        }

        private void SaveClicked(object sender, EventArgs e)
        {
            _settings.DeviceName = string.IsNullOrWhiteSpace(_txtDeviceName.Text) ? "CastMPV Receiver" : _txtDeviceName.Text.Trim();
            _settings.DlnaPort = (int)_numPort.Value;
            _settings.DlnaEnabled = _chkDlnaEnabled.Checked;
            _settings.AutoFullscreenOnCast = _chkAutoFullscreen.Checked;
            _settings.AutoMinimizeOnStop = _chkAutoMinimizeOnStop.Checked;
            _settings.StartMinimizedToTray = _chkStartMinimized.Checked;
            _settings.MinimizeToTrayOnClose = _chkMinimizeOnClose.Checked;
            _settings.HardwareAcceleration = _chkHwDec.Checked;
            _settings.SubtitleFontSize = (int)_numSubSize.Value;

            string selColor = _cmbSubColor.SelectedItem?.ToString() ?? "#FFFFFF";
            _settings.SubtitleColor = selColor.Split(' ')[0];
            _settings.ScreenshotDirectory = _txtScreenshots.Text.Trim();

            _settings.Save();
            StartupManager.SetAutoStart(_chkAutoStartWindows.Checked);
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
