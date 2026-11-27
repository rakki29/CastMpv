using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CastMPV.UI
{
    public static class ThemePalette
    {
        public static readonly Color BgCanvas = Color.FromArgb(14, 15, 18);
        public static readonly Color BgSurface = Color.FromArgb(24, 26, 32);
        public static readonly Color BgElevated = Color.FromArgb(34, 37, 46);
        public static readonly Color BgHover = Color.FromArgb(46, 51, 64);
        public static readonly Color Border = Color.FromArgb(48, 52, 64);
        public static readonly Color Accent = Color.FromArgb(56, 189, 248);
        public static readonly Color AccentHover = Color.FromArgb(14, 165, 233);
        public static readonly Color TextPrimary = Color.FromArgb(241, 245, 249);
        public static readonly Color TextMuted = Color.FromArgb(148, 163, 184);
        public static readonly Color Success = Color.FromArgb(34, 197, 94);
        public static readonly Color Danger = Color.FromArgb(239, 68, 68);
    }

    public static class ThemeHelper
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public static void EnableDarkTitleBar(Form form)
        {
            if (form == null || !form.IsHandleCreated) return;
            try
            {
                int useImmersiveDarkMode = 1;
                if (DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useImmersiveDarkMode, sizeof(int));
                }
            }
            catch
            {
                // Ignore on older Windows versions (Windows 7/8)
            }
        }
    }
}
