using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Silverdash360.Controls
{
    /// <summary>
    /// The dashboard's colours and text styles, for views built in code. A
    /// theme changes the colours in place (<see cref="Apply"/>), so what's on
    /// screen takes them at once.
    /// </summary>
    public static class Theme
    {
        /// <summary>The colours a theme sets.</summary>
        public sealed class Palette
        {
            public Color Accent;
            public Color SubText;
            public Color Panel;
            public Color PanelHighlight;
            /// <summary>The pages' background, top left to bottom right.</summary>
            public Color[] Background;
        }

        public static readonly SolidColorBrush Accent = new SolidColorBrush(Color.FromArgb(0xFF, 0x3F, 0xA9, 0xF5));
        public static readonly SolidColorBrush Text = new SolidColorBrush(Colors.White);
        public static readonly SolidColorBrush SubText = new SolidColorBrush(Color.FromArgb(0xFF, 0x9A, 0xA8, 0xB6));
        public static readonly SolidColorBrush Panel = new SolidColorBrush(Color.FromArgb(0xFF, 0x1A, 0x22, 0x2C));
        public static readonly SolidColorBrush PanelHighlight = new SolidColorBrush(Color.FromArgb(0xFF, 0x24, 0x30, 0x3E));
        public static readonly SolidColorBrush Success = new SolidColorBrush(Color.FromArgb(0xFF, 0x6C, 0xC0, 0x4A));

        /// <summary>Silverdash's own colours, App.xaml's and the brushes' above.</summary>
        public static Palette DefaultPalette()
        {
            return new Palette
            {
                Accent = Color.FromArgb(0xFF, 0x3F, 0xA9, 0xF5),
                SubText = Color.FromArgb(0xFF, 0x9A, 0xA8, 0xB6),
                Panel = Color.FromArgb(0xFF, 0x1A, 0x22, 0x2C),
                PanelHighlight = Color.FromArgb(0xFF, 0x24, 0x30, 0x3E),
                Background = new[]
                {
                    Color.FromArgb(0xFF, 0x16, 0x20, 0x2C), Color.FromArgb(0xFF, 0x0A, 0x0E, 0x13),
                    Color.FromArgb(0xFF, 0x0B, 0x16, 0x22),
                },
            };
        }

        /// <summary>Whether the colours are Silverdash's own.</summary>
        public static bool IsDefault { get; private set; }

        /// <summary>Sets the colours here and App.xaml's, which the pages' markup uses.</summary>
        public static void Apply(Palette palette, bool isDefault)
        {
            IsDefault = isDefault;
            Accent.Color = palette.Accent;
            SubText.Color = palette.SubText;
            Panel.Color = palette.Panel;
            PanelHighlight.Color = palette.PanelHighlight;
            ResourceDictionary resources = Application.Current.Resources;
            SetColor(resources, "Accent", palette.Accent);
            SetColor(resources, "SubText", palette.SubText);
            SetColor(resources, "Panel", palette.Panel);
            LinearGradientBrush background = resources["PageBackground"] as LinearGradientBrush;
            if (background != null)
            {
                for (int i = 0; i < background.GradientStops.Count && i < palette.Background.Length; i++)
                {
                    background.GradientStops[i].Color = palette.Background[i];
                }
            }
        }

        private static void SetColor(ResourceDictionary resources, string key, Color color)
        {
            SolidColorBrush brush = resources[key] as SolidColorBrush;
            if (brush != null)
            {
                brush.Color = color;
            }
        }

        // The image is drawn this small to read its colours: a few hundred
        // pixels say enough, where the whole image's pixels would be
        // megabytes to copy on the console.
        private const int SampleWidth = 32;
        private const int SampleHeight = 18;
        private const int HueBuckets = 24;

        /// <summary>
        /// Colours for a wallpaper: the accent in its strongest hue, and dark
        /// panels and background in the same hue. Null for an image with
        /// little colour, which keeps Silverdash's own.
        /// </summary>
        public static Palette PaletteFrom(BitmapSource image)
        {
            int[] pixels = Sample(image);
            if (pixels == null)
            {
                return null;
            }
            double[] weight = new double[HueBuckets];
            double[] hueSum = new double[HueBuckets];
            double[] saturationSum = new double[HueBuckets];
            double total = 0;
            foreach (int argb in pixels)
            {
                double h, s, v;
                ToHsv(((argb >> 16) & 0xFF) / 255.0, ((argb >> 8) & 0xFF) / 255.0, (argb & 0xFF) / 255.0,
                      out h, out s, out v);
                // Dark and grey pixels say little about the colour.
                if (v < 0.12)
                {
                    continue;
                }
                double w = s * s * v;
                int bucket = (int)(h / 360 * HueBuckets) % HueBuckets;
                weight[bucket] += w;
                hueSum[bucket] += h * w;
                saturationSum[bucket] += s * w;
                total += w;
            }
            // The strongest hue, counting half of the buckets beside it.
            int best = 0;
            double bestWeight = -1;
            for (int i = 0; i < HueBuckets; i++)
            {
                double around = weight[i] + 0.5 * (weight[(i + 1) % HueBuckets] +
                                                   weight[(i + HueBuckets - 1) % HueBuckets]);
                if (around > bestWeight)
                {
                    bestWeight = around;
                    best = i;
                }
            }
            if (total < pixels.Length * 0.01 || weight[best] <= 0)
            {
                return null;
            }
            double hue = hueSum[best] / weight[best];
            double saturation = Math.Max(0.5, Math.Min(0.85, saturationSum[best] / weight[best] * 1.2));
            return new Palette
            {
                Accent = FromHsv(hue, saturation, 0.95),
                SubText = FromHsv(hue, 0.14, 0.78),
                // A little see-through, so the wallpaper shows behind the tiles.
                Panel = WithAlpha(FromHsv(hue, 0.45, 0.17), 0xE0),
                PanelHighlight = WithAlpha(FromHsv(hue, 0.5, 0.30), 0xF0),
                Background = new[] { FromHsv(hue, 0.5, 0.2), FromHsv(hue, 0.5, 0.07), FromHsv(hue, 0.5, 0.12) },
            };
        }

        // The image's pixels, drawn small; null when they can't be read.
        private static int[] Sample(BitmapSource image)
        {
            try
            {
                Image element = new Image
                {
                    Source = image,
                    Width = SampleWidth,
                    Height = SampleHeight,
                    Stretch = Stretch.Fill,
                };
                element.Measure(new Size(SampleWidth, SampleHeight));
                element.Arrange(new Rect(0, 0, SampleWidth, SampleHeight));
                WriteableBitmap small = new WriteableBitmap(SampleWidth, SampleHeight);
                small.Render(element, null);
                small.Invalidate();
                int[] pixels = small.Pixels;
                foreach (int pixel in pixels)
                {
                    if (pixel != 0)
                    {
                        return pixels;
                    }
                }
                // Nothing drawn: the whole image's pixels instead, a stride apart.
                int[] all = new WriteableBitmap(image).Pixels;
                int step = Math.Max(1, all.Length / (SampleWidth * SampleHeight * 4));
                int[] picked = new int[all.Length / step];
                for (int i = 0; i < picked.Length; i++)
                {
                    picked[i] = all[i * step];
                }
                return picked;
            }
            catch (Exception e)
            {
                Core.Log.Write("Couldn't read a wallpaper's colours: {0}", e.Message);
                return null;
            }
        }

        private static Color WithAlpha(Color color, byte alpha)
        {
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        private static void ToHsv(double r, double g, double b, out double h, out double s, out double v)
        {
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;
            v = max;
            s = max > 0 ? delta / max : 0;
            if (delta == 0)
            {
                h = 0;
                return;
            }
            if (max == r)
            {
                h = 60 * ((g - b) / delta);
            }
            else if (max == g)
            {
                h = 60 * ((b - r) / delta + 2);
            }
            else
            {
                h = 60 * ((r - g) / delta + 4);
            }
            if (h < 0)
            {
                h += 360;
            }
        }

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
