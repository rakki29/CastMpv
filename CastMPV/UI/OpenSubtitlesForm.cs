using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CastMPV.Core;

namespace CastMPV.UI
{
    public sealed class OpenSubtitlesForm : Form
    {
        private readonly AppSettings _settings;
        private readonly OpenSubtitlesClient _client = new OpenSubtitlesClient();

        private TextBox _txtQuery;
        private ComboBox _cmbLang;
        private TextBox _txtApiKey;
        private Button _btnSearch;
        private ListView _listResults;
        private Label _lblStatus;
        private Button _btnDownload;
        private Button _btnClose;

        private CancellationTokenSource _cts;
        public string DownloadedSubtitlePath { get; private set; }

        public OpenSubtitlesForm(AppSettings settings, string initialQuery = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            Text = "Search OpenSubtitles";
            Size = new Size(760, 520);
            MinimumSize = new Size(640, 420);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = ThemePalette.BgSurface;
            ForeColor = ThemePalette.TextPrimary;
            Font = new Font("Segoe UI", 9.5f);

            BuildUi(initialQuery);
            HandleCreated += (s, e) => ThemeHelper.EnableDarkTitleBar(this);
            FormClosing += (s, e) => _cts?.Cancel();
        }

        private void BuildUi(string initialQuery)
        {
            var topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 104,
                BackColor = ThemePalette.BgCanvas,
                Padding = new Padding(16, 12, 16, 10)
            };

            var lblQuery = new Label
            {
                Text = "Movie / Episode:",
                Location = new Point(16, 16),
                AutoSize = true,
                ForeColor = ThemePalette.TextMuted
            };

            _txtQuery = new TextBox
            {
                Location = new Point(132, 13),
                Width = 370,
                Text = CleanTitleForQuery(initialQuery),
                BackColor = ThemePalette.BgElevated,
                ForeColor = ThemePalette.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle
            };

            _cmbLang = new ComboBox
            {
                Location = new Point(512, 12),
                Width = 104,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = ThemePalette.BgElevated,
                ForeColor = ThemePalette.TextPrimary
            };
            _cmbLang.Items.AddRange(new object[]
            {
                "en", "ar", "es", "fr", "de", "it", "pt", "tr", "ru", "zh", "ja", "ko", "nl", "pl", "all"
            });
            string prefLang = _settings.OpenSubtitlesLanguage ?? "en";
            _cmbLang.SelectedItem = _cmbLang.Items.Contains(prefLang) ? prefLang : "en";

            _btnSearch = new Button
            {
                Text = "Search",
                Location = new Point(626, 11),
                Size = new Size(102, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemePalette.Accent,
                ForeColor = Color.FromArgb(10, 15, 25),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnSearch.FlatAppearance.BorderSize = 0;
            _btnSearch.Click += async (s, e) => await ExecuteSearchAsync();

            var lblApi = new Label
            {
                Text = "API Key (v1):",
                Location = new Point(16, 52),
                AutoSize = true,
                ForeColor = ThemePalette.TextMuted
            };

            _txtApiKey = new TextBox
            {
                Location = new Point(132, 49),
                Width = 370,
                Text = _settings.OpenSubtitlesApiKey ?? string.Empty,
                UseSystemPasswordChar = true,
                BackColor = ThemePalette.BgElevated,
                ForeColor = ThemePalette.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblApiHint = new Label
            {
                Text = "Optional for legacy search; free key at opensubtitles.com/consumers",
                Location = new Point(132, 77),
                AutoSize = true,
                Font = new Font("Segoe UI", 8f),
                ForeColor = ThemePalette.TextMuted
            };

            var chkShowKey = new CheckBox
            {
                Text = "Show Key",
                Location = new Point(512, 51),
                AutoSize = true,
                ForeColor = ThemePalette.TextMuted
            };
            chkShowKey.CheckedChanged += (s, e) => _txtApiKey.UseSystemPasswordChar = !chkShowKey.Checked;

            topPanel.Controls.AddRange(new Control[]
            {
                lblQuery, _txtQuery, _cmbLang, _btnSearch, lblApi, _txtApiKey, lblApiHint, chkShowKey
            });

            _listResults = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                GridLines = false,
                BackColor = ThemePalette.BgSurface,
                ForeColor = ThemePalette.TextPrimary,
                BorderStyle = BorderStyle.None
            };
            _listResults.Columns.Add("Subtitle Release / Title", 420);
            _listResults.Columns.Add("Lang", 65);
            _listResults.Columns.Add("Format", 65);
            _listResults.Columns.Add("Downloads", 90);
            _listResults.Columns.Add("Source", 110);
            _listResults.DoubleClick += async (s, e) => await DownloadSelectedAsync();
            _listResults.SelectedIndexChanged += (s, e) =>
            {
                _btnDownload.Enabled = _listResults.SelectedItems.Count > 0;
            };

            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                BackColor = ThemePalette.BgCanvas,
                Padding = new Padding(16, 11, 16, 11)
            };

            _lblStatus = new Label
            {
                Text = "Enter a movie or episode title and click Search.",
                Location = new Point(16, 18),
                Size = new Size(440, 22),
                AutoEllipsis = true,
                ForeColor = ThemePalette.TextMuted
            };

            _btnDownload = new Button
            {
                Text = "Download & Apply",
                Enabled = false,
                Location = new Point(474, 12),
                Size = new Size(146, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemePalette.Accent,
                ForeColor = Color.FromArgb(10, 15, 25),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnDownload.FlatAppearance.BorderSize = 0;
            _btnDownload.Click += async (s, e) => await DownloadSelectedAsync();

            _btnClose = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.Cancel,
                Location = new Point(630, 12),
                Size = new Size(98, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemePalette.BgElevated,
                ForeColor = ThemePalette.TextPrimary,
                Cursor = Cursors.Hand
            };
            _btnClose.FlatAppearance.BorderColor = ThemePalette.Border;

            bottomPanel.Controls.AddRange(new Control[] { _lblStatus, _btnDownload, _btnClose });

            Controls.Add(_listResults);
            Controls.Add(topPanel);
            Controls.Add(bottomPanel);

            AcceptButton = _btnSearch;
            CancelButton = _btnClose;
        }

        private async Task ExecuteSearchAsync()
        {
            string query = _txtQuery.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                _lblStatus.Text = "Please enter a search query.";
                return;
            }

            PersistCredentials();

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            CancellationToken ct = _cts.Token;

            _btnSearch.Enabled = false;
            _btnDownload.Enabled = false;
            _listResults.Items.Clear();
            _lblStatus.ForeColor = ThemePalette.Accent;
            _lblStatus.Text = $"Searching OpenSubtitles for \"{query}\"...";

            try
            {
                string lang = _cmbLang.SelectedItem?.ToString() ?? "en";
                List<SubtitleSearchResult> items = await _client.SearchAsync(query, lang, _txtApiKey.Text.Trim(), ct);

                if (ct.IsCancellationRequested || IsDisposed) return;

                _listResults.BeginUpdate();
                foreach (SubtitleSearchResult item in items)
                {
                    var row = new ListViewItem(item.Title) { Tag = item };
                    row.SubItems.Add(item.Language.ToUpperInvariant());
                    row.SubItems.Add(item.Format);
                    row.SubItems.Add(item.Downloads.ToString("N0"));
                    row.SubItems.Add(item.Source);
                    _listResults.Items.Add(row);
                }
                _listResults.EndUpdate();

                if (items.Count > 0)
                {
                    _listResults.Items[0].Selected = true;
                    _lblStatus.ForeColor = ThemePalette.Success;
                    _lblStatus.Text = $"Found {items.Count} subtitles. Select one and click Download & Apply.";
                }
                else
                {
                    _lblStatus.ForeColor = ThemePalette.TextMuted;
                    _lblStatus.Text = "No matching subtitles found. Try simplifying the query.";
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore cancelled search
            }
            catch (Exception ex)
            {
                _lblStatus.ForeColor = ThemePalette.Danger;
                _lblStatus.Text = ex.Message;
            }
            finally
            {
                if (!IsDisposed) _btnSearch.Enabled = true;
            }
        }

        private async Task DownloadSelectedAsync()
        {
            if (_listResults.SelectedItems.Count == 0) return;
            if (!(_listResults.SelectedItems[0].Tag is SubtitleSearchResult selected)) return;

            PersistCredentials();

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            CancellationToken ct = _cts.Token;

            _btnDownload.Enabled = false;
            _btnSearch.Enabled = false;
            _lblStatus.ForeColor = ThemePalette.Accent;
            _lblStatus.Text = $"Downloading {Path.GetFileName(selected.FileName)}...";

            try
            {
                string localPath = await _client.DownloadSubtitleAsync(selected, _txtApiKey.Text.Trim(), ct);
                if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
                {
                    _lblStatus.ForeColor = ThemePalette.Danger;
                    _lblStatus.Text = "Failed to download subtitle file.";
                    return;
                }

                DownloadedSubtitlePath = localPath;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                _lblStatus.ForeColor = ThemePalette.Danger;
                _lblStatus.Text = "Download failed: " + ex.Message;
            }
            finally
            {
                if (!IsDisposed)
                {
                    _btnSearch.Enabled = true;
                    _btnDownload.Enabled = _listResults.SelectedItems.Count > 0;
                }
            }
        }

        private void PersistCredentials()
        {
            _settings.OpenSubtitlesApiKey = _txtApiKey.Text?.Trim() ?? string.Empty;
            _settings.OpenSubtitlesLanguage = _cmbLang.SelectedItem?.ToString() ?? "en";
            _settings.Save();
        }

        private static string CleanTitleForQuery(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string s = Path.GetFileNameWithoutExtension(raw);
            if (string.IsNullOrWhiteSpace(s)) s = raw;
            s = s.Replace('.', ' ').Replace('_', ' ');
            return s.Trim();
        }
    }
}
