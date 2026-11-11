
using System.Drawing;

namespace Veyron.Client.Themes;

public static class AppTheme
{
    public static readonly Color Background = Color.FromArgb(15, 16, 24);
        public static readonly Color Sidebar = Color.FromArgb(20, 21, 31);
        public static readonly Color Card = Color.FromArgb(25, 27, 40);
        public static readonly Color CardHover = Color.FromArgb(34, 36, 52);
        public static readonly Color Accent = Color.FromArgb(139, 92, 246);
        public static readonly Color AccentHover = Color.FromArgb(157, 119, 255);
        public static readonly Color Text = Color.FromArgb(240, 240, 248);
        public static readonly Color Secondary = Color.FromArgb(145, 148, 167);
        public static readonly Color Success = Color.FromArgb(65, 200, 135);
        public static readonly Color Border = Color.FromArgb(43, 45, 62);
        public static readonly Font MainFont = new Font("Segoe UI", 10F);
        public static readonly Font TitleFont = new Font("Segoe UI Semibold", 19F);
        public static Font Font => new Font("Segoe UI", 9F, FontStyle.Regular);
}