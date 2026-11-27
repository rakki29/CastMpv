using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CastMPV.UI
{
    public sealed class SeekTrackBar : Control
    {
        private double _maximum = 100;
        private double _value;
        private bool _dragging;
        private bool _hovering;
        private int _hoverX;

        public event Action<double> SeekCommitted;

        public double Maximum
        {
            get => _maximum;
            set
            {
                _maximum = Math.Max(0, value);
                Invalidate();
            }
        }

        public double Value
        {
            get => _value;
            set
            {
                if (_dragging) return;
                _value = Math.Max(0, Math.Min(_maximum > 0 ? _maximum : double.MaxValue, value));
                Invalidate();
            }
        }

        public SeekTrackBar()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
            Height = 22;
            Cursor = Cursors.Hand;
            BackColor = ThemePalette.BgSurface;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovering = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovering = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _maximum > 0)
            {
                _dragging = true;
                Capture = true;
                UpdateFromX(e.X);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            _hoverX = Math.Max(8, Math.Min(Width - 8, e.X));
            if (_dragging && _maximum > 0)
            {
                UpdateFromX(e.X);
            }
            else if (_hovering)
            {
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                Capture = false;
                UpdateFromX(e.X);
                SeekCommitted?.Invoke(_value);
            }
            base.OnMouseUp(e);
        }

        private void UpdateFromX(int x)
        {
            int trackWidth = Math.Max(1, Width - 16);
            double ratio = Math.Max(0.0, Math.Min(1.0, (x - 8.0) / trackWidth));
            _value = ratio * _maximum;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            int pad = 8;
            int trackWidth = Math.Max(1, Width - pad * 2);
            int barHeight = (_hovering || _dragging) ? 6 : 4;
            int barY = (Height - barHeight) / 2;

            using var trackBrush = new SolidBrush(ThemePalette.BgHover);
            g.FillRectangle(trackBrush, pad, barY, trackWidth, barHeight);

            double ratio = _maximum > 0 ? Math.Max(0.0, Math.Min(1.0, _value / _maximum)) : 0.0;
            int fillWidth = (int)Math.Round(trackWidth * ratio);

            if (fillWidth > 0)
            {
                using var fillBrush = new SolidBrush(ThemePalette.Accent);
                g.FillRectangle(fillBrush, pad, barY, fillWidth, barHeight);
            }

            if (_hovering && _maximum > 0)
            {
                double hoverRatio = Math.Max(0.0, Math.Min(1.0, (_hoverX - pad) / (double)trackWidth));
                double hoverTime = hoverRatio * _maximum;
                string preview = FormatDuration(hoverTime);

                using var previewPen = new Pen(Color.FromArgb(140, 241, 245, 249), 1f);
                g.DrawLine(previewPen, _hoverX, barY - 2, _hoverX, barY + barHeight + 2);

                using var font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
                SizeF textSize = g.MeasureString(preview, font);
                float bx = Math.Max(2, Math.Min(Width - textSize.Width - 8, _hoverX - textSize.Width / 2f - 3));
                float by = 0f;
                var boxRect = new RectangleF(bx, by, textSize.Width + 6, textSize.Height);
                using var tipBrush = new SolidBrush(Color.FromArgb(220, 15, 23, 42));
                using var tipTextBrush = new SolidBrush(ThemePalette.TextPrimary);
                g.FillRectangle(tipBrush, boxRect);
                g.DrawString(preview, font, tipTextBrush, bx + 3, by);
            }

            if (_hovering || _dragging)
            {
                int thumbSize = 12;
                int tx = pad + fillWidth - thumbSize / 2;
                int ty = (Height - thumbSize) / 2;
                using var thumbBrush = new SolidBrush(Color.White);
                g.FillEllipse(thumbBrush, tx, ty, thumbSize, thumbSize);
            }
        }

        private static string FormatDuration(double sec)
        {
            if (double.IsNaN(sec) || sec < 0) return "00:00";
            TimeSpan ts = TimeSpan.FromSeconds(sec);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
                : $"{ts.Minutes:00}:{ts.Seconds:00}";
        }
    }

    public sealed class VolumeSlider : Control
    {
        private int _value = 85;
        private bool _dragging;

        public event Action<int> VolumeChanged;

        public int Value
        {
            get => _value;
            set
            {
                int clamped = Math.Max(0, Math.Min(100, value));
                if (_value == clamped) return;
                _value = clamped;
                Invalidate();
            }
        }

        public VolumeSlider()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
            Size = new Size(88, 24);
            Cursor = Cursors.Hand;
            BackColor = ThemePalette.BgSurface;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                Capture = true;
                UpdateFromX(e.X);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging) UpdateFromX(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                Capture = false;
                UpdateFromX(e.X);
            }
            base.OnMouseUp(e);
        }

        private void UpdateFromX(int x)
        {
            int pad = 6;
            int w = Math.Max(1, Width - pad * 2);
            double ratio = Math.Max(0.0, Math.Min(1.0, (x - pad) / (double)w));
            int next = (int)Math.Round(ratio * 100);
            if (next != _value)
            {
                _value = next;
                Invalidate();
                VolumeChanged?.Invoke(_value);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            int pad = 6;
            int w = Math.Max(1, Width - pad * 2);
            int barH = 4;
            int barY = (Height - barH) / 2;

            using var bgBrush = new SolidBrush(ThemePalette.BgHover);
            g.FillRectangle(bgBrush, pad, barY, w, barH);

            int fill = (int)Math.Round(w * (_value / 100.0));
            using var fgBrush = new SolidBrush(ThemePalette.Accent);
            if (fill > 0) g.FillRectangle(fgBrush, pad, barY, fill, barH);

            int thumb = 10;
            using var thumbBrush = new SolidBrush(ThemePalette.TextPrimary);
            g.FillEllipse(thumbBrush, pad + fill - thumb / 2, (Height - thumb) / 2, thumb, thumb);
        }
    }

    public sealed class DarkFlatButton : Button
    {
        public DarkFlatButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = ThemePalette.BgHover;
            FlatAppearance.MouseDownBackColor = ThemePalette.BgElevated;
            BackColor = Color.Transparent;
            ForeColor = ThemePalette.TextPrimary;
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            Cursor = Cursors.Hand;
            TabStop = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            UseCompatibleTextRendering = false;
            TextAlign = ContentAlignment.MiddleCenter;
            Padding = new Padding(10, 4, 10, 4);
        }
    }

    public sealed class DarkMenuColorTable : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin => ThemePalette.BgSurface;
        public override Color MenuStripGradientEnd => ThemePalette.BgSurface;
        public override Color ToolStripDropDownBackground => ThemePalette.BgElevated;
        public override Color MenuBorder => ThemePalette.Border;
        public override Color MenuItemBorder => ThemePalette.Accent;
        public override Color MenuItemSelected => ThemePalette.BgHover;
        public override Color MenuItemSelectedGradientBegin => ThemePalette.BgHover;
        public override Color MenuItemSelectedGradientEnd => ThemePalette.BgHover;
        public override Color MenuItemPressedGradientBegin => ThemePalette.BgElevated;
        public override Color MenuItemPressedGradientEnd => ThemePalette.BgElevated;
        public override Color ImageMarginGradientBegin => ThemePalette.BgElevated;
        public override Color ImageMarginGradientMiddle => ThemePalette.BgElevated;
        public override Color ImageMarginGradientEnd => ThemePalette.BgElevated;
        public override Color SeparatorDark => ThemePalette.Border;
        public override Color SeparatorLight => ThemePalette.Border;
        public override Color CheckBackground => ThemePalette.Accent;
        public override Color CheckSelectedBackground => ThemePalette.AccentHover;
    }

    public sealed class DarkContextMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkContextMenuRenderer() : base(new DarkMenuColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? ThemePalette.TextPrimary : ThemePalette.TextMuted;
            base.OnRenderItemText(e);
        }
    }
}
