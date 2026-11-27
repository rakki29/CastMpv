using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CastMPV.Core
{
    public static class IconFactory
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static Icon CreateAppIcon(int size = 64)
        {
            using var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                float pad = size * 0.06f;
                float rectSize = size - pad * 2f;
                float radius = size * 0.22f;

                using var bgPath = CreateRoundedRect(new RectangleF(pad, pad, rectSize, rectSize), radius);
                using var bgBrush = new LinearGradientBrush(
                    new PointF(0, 0),
                    new PointF(size, size),
                    Color.FromArgb(24, 30, 44),
                    Color.FromArgb(12, 16, 26));
                g.FillPath(bgBrush, bgPath);

                using var borderPen = new Pen(Color.FromArgb(56, 189, 248), Math.Max(1.5f, size * 0.045f));
                g.DrawPath(borderPen, bgPath);

                // Play triangle
                float cx = size * 0.44f;
                float cy = size * 0.50f;
                float triR = size * 0.22f;
                var triangle = new[]
                {
                    new PointF(cx - triR * 0.55f, cy - triR * 0.85f),
                    new PointF(cx + triR * 0.95f, cy),
                    new PointF(cx - triR * 0.55f, cy + triR * 0.85f)
                };
                using var playBrush = new SolidBrush(Color.FromArgb(56, 189, 248));
                g.FillPolygon(playBrush, triangle);

                // Subtle cast arc on top-right
                using var arcPen = new Pen(Color.FromArgb(224, 242, 254), Math.Max(1.5f, size * 0.055f))
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };
                float arc1 = size * 0.22f;
                g.DrawArc(arcPen, size * 0.62f, size * 0.22f, arc1, arc1, -80, 75);
            }

            IntPtr hIcon = bmp.GetHicon();
            try
            {
                using Icon temp = Icon.FromHandle(hIcon);
                return (Icon)temp.Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }

        private static GraphicsPath CreateRoundedRect(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2f;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
