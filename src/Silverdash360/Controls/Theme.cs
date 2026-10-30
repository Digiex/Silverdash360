using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Silverdash360.Controls
{
    /// <summary>The dashboard's colours and text styles, for views built in code.</summary>
    public static class Theme
    {
        public static readonly Color AccentColor = Color.FromArgb(0xFF, 0x3F, 0xA9, 0xF5);
        public static readonly Color SilverColor = Color.FromArgb(0xFF, 0xC8, 0xD2, 0xDC);

        public static readonly SolidColorBrush Accent = new SolidColorBrush(AccentColor);
        public static readonly SolidColorBrush Text = new SolidColorBrush(Colors.White);
        public static readonly SolidColorBrush SubText = new SolidColorBrush(Color.FromArgb(0xFF, 0x9A, 0xA8, 0xB6));
        public static readonly SolidColorBrush Panel = new SolidColorBrush(Color.FromArgb(0xFF, 0x1A, 0x22, 0x2C));
        public static readonly SolidColorBrush PanelHighlight = new SolidColorBrush(Color.FromArgb(0xFF, 0x24, 0x30, 0x3E));
        public static readonly SolidColorBrush Success = new SolidColorBrush(Color.FromArgb(0xFF, 0x6C, 0xC0, 0x4A));

        public static TextBlock TextBlock(string text, double size, Brush brush)
        {
            return new TextBlock
            {
                Text = text ?? "",
                FontSize = size,
                Foreground = brush,
            };
        }

        /// <summary>A colour of its own for each title, for art it doesn't have.</summary>
        public static Brush PlaceholderBrush(uint seed)
        {
            double hue = (seed * 2654435761u) % 360;
            return new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = FromHsv(hue, 0.55, 0.55), Offset = 0 },
                    new GradientStop { Color = FromHsv((hue + 40) % 360, 0.65, 0.28), Offset = 1 },
                },
            };
        }

        /// <summary>Up to two initials of a name, for placeholder art.</summary>
        public static string Initials(string name)
        {
            string initials = "";
            foreach (string word in (name ?? "").Split(new[] { ' ', '-', ':', '.' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (char.IsLetterOrDigit(word[0]))
                {
                    initials += char.ToUpperInvariant(word[0]);
                }
                if (initials.Length == 2)
                {
                    break;
                }
            }
            return initials;
        }

        private static Color FromHsv(double h, double s, double v)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
            double m = v - c;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }
            return Color.FromArgb(0xFF, (byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }
    }
}
