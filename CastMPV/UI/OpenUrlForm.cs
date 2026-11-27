using System;
using System.Drawing;
using System.Windows.Forms;

namespace CastMPV.UI
{
    public sealed class OpenUrlForm : Form
    {
        private readonly TextBox _txtUrl;
        private readonly TextBox _txtUserAgent;
        private readonly TextBox _txtReferer;

        public string MediaUrl => _txtUrl.Text.Trim();
        public string CustomUserAgent => _txtUserAgent.Text.Trim();
        public string CustomReferer => _txtReferer.Text.Trim();

        public OpenUrlForm(string defaultUserAgent)
        {
            Text = "Open Stream / URL";
            Size = new Size(540, 290);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = ThemePalette.BgSurface;
            ForeColor = ThemePalette.TextPrimary;
            Font = new Font("Segoe UI", 9.5f);

            var lblUrl = new Label
            {
                Text = "Stream or Video URL (HTTP, HTTPS, M3U8, MPD, RTMP, IPTV):",
                Location = new Point(18, 16),
                AutoSize = true,
                ForeColor = ThemePalette.TextPrimary
            };

            _txtUrl = new TextBox
            {
                Location = new Point(18, 40),
                Width = 486,
                BackColor = ThemePalette.BgElevated,
                ForeColor = ThemePalette.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblUa = new Label
            {
                Text = "User-Agent (optional):",
                Location = new Point(18, 78),
                AutoSize = true,
                ForeColor = ThemePalette.TextMuted
            };

            _txtUserAgent = new TextBox
            {
                Location = new Point(18, 100),
                Width = 486,
                Text = defaultUserAgent ?? string.Empty,
                BackColor = ThemePalette.BgElevated,
                ForeColor = ThemePalette.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblRef = new Label
            {
                Text = "Referer Header (optional):",
                Location = new Point(18, 138),
                AutoSize = true,
                ForeColor = ThemePalette.TextMuted
            };

            _txtReferer = new TextBox
            {
                Location = new Point(18, 160),
                Width = 486,
                BackColor = ThemePalette.BgElevated,
                ForeColor = ThemePalette.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle
            };

            var btnPlay = new Button
            {
                Text = "Play Stream",
                DialogResult = DialogResult.OK,
                Location = new Point(304, 206),
                Size = new Size(100, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemePalette.Accent,
                ForeColor = Color.FromArgb(10, 15, 25),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            btnPlay.FlatAppearance.BorderSize = 0;

            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(412, 206),
                Size = new Size(92, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemePalette.BgElevated,
                ForeColor = ThemePalette.TextPrimary
            };
            btnCancel.FlatAppearance.BorderColor = ThemePalette.Border;

            AcceptButton = btnPlay;
            CancelButton = btnCancel;

            Controls.AddRange(new Control[]
            {
                lblUrl, _txtUrl,
                lblUa, _txtUserAgent,
                lblRef, _txtReferer,
                btnPlay, btnCancel
            });

            HandleCreated += (s, e) => ThemeHelper.EnableDarkTitleBar(this);

            // Auto-populate from clipboard if it contains an HTTP/RTMP URL
            try
            {
                if (Clipboard.ContainsText())
                {
                    string clip = Clipboard.GetText().Trim();
                    if (clip.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        clip.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                        clip.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase))
                    {
                        _txtUrl.Text = clip;
                        _txtUrl.SelectAll();
                    }
                }
            }
            catch
            {
                // Ignore clipboard lock errors
            }
        }
    }
}
