using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Silverdash360.Controls
{
    /// <summary>
    /// A page's button hints, drawn as the controller's buttons with what
    /// each does: Text is "A play   START details   LB RB sections", items
    /// three spaces apart, each the buttons and then what they do. A, B, X
    /// and Y are their coloured faces; the shoulders and triggers, START and
    /// BACK are labelled shapes.
    /// </summary>
    public sealed class ButtonLegend : StackPanel
    {
        private const double FaceSize = 22;
        private const double FontSize = 18;
        private const string ItemGap = "   ";

        private const double ItemSpacing = 22;
        private const double GlyphSpacing = 4;
        private const double LabelSpacing = 7;

        private static readonly string[] Buttons = { "A", "B", "X", "Y", "LB", "RB", "LT", "RT", "START", "BACK" };

        private string text = "";

        public ButtonLegend()
        {
            Orientation = Orientation.Horizontal;
        }

        public string Text
        {
            get { return text; }
            set
            {
                text = value ?? "";
                Build();
            }
        }

        private void Build()
        {
            Children.Clear();
            foreach (string item in text.Split(new[] { ItemGap }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                string[] words = item.Trim().Split(' ');
                double gap = Children.Count == 0 ? 0 : ItemSpacing;
                int i = 0;
                while (i < words.Length && System.Array.IndexOf(Buttons, words[i]) >= 0)
                {
                    FrameworkElement glyph = Glyph(words[i]);
                    glyph.Margin = new Thickness(gap, 0, 0, 0);
                    Children.Add(glyph);
                    gap = GlyphSpacing;
                    i++;
                }
                string label = string.Join(" ", words, i, words.Length - i);
                if (label.Length > 0)
                {
                    TextBlock what = Theme.TextBlock(label, FontSize, Theme.SubText);
                    what.VerticalAlignment = VerticalAlignment.Center;
                    what.Margin = new Thickness(i > 0 ? LabelSpacing : gap, 0, 0, 0);
                    Children.Add(what);
                }
            }
        }

        private static FrameworkElement Glyph(string button)
        {
            switch (button)
            {
                case "A": return Face("A", Color.FromArgb(0xFF, 0x6D, 0xB3, 0x3F));
                case "B": return Face("B", Color.FromArgb(0xFF, 0xE0, 0x3A, 0x3E));
                case "X": return Face("X", Color.FromArgb(0xFF, 0x3A, 0x7F, 0xDB));
                case "Y": return Face("Y", Color.FromArgb(0xFF, 0xF2, 0xB7, 0x05));
                case "LB":
                case "RB":
                    return Shape(button, new CornerRadius(8, 8, 3, 3), 32);
                case "LT":
                case "RT":
                    return Shape(button, new CornerRadius(3, 3, 9, 9), 28);
                default:
                    return Shape(button, new CornerRadius(FaceSize / 2), 0);
            }
        }

        // A face button: a coloured disc with its letter.
        private static FrameworkElement Face(string letter, Color color)
        {
            Grid face = new Grid { Width = FaceSize, Height = FaceSize, VerticalAlignment = VerticalAlignment.Center };
            face.Children.Add(new Ellipse
            {
                Fill = new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.35, 0.3),
                    GradientStops =
                    {
                        new GradientStop { Color = Lighter(color), Offset = 0 },
                        new GradientStop { Color = color, Offset = 0.7 },
                    },
                },
            });
            TextBlock text = Theme.TextBlock(letter, 13, new SolidColorBrush(Colors.White));
            text.FontWeight = FontWeights.Bold;
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
            face.Children.Add(text);
            return face;
        }

        // A shoulder, trigger, START or BACK: a grey shape with its name.
        private static FrameworkElement Shape(string name, CornerRadius corners, double width)
        {
            TextBlock text = Theme.TextBlock(name, 11, new SolidColorBrush(Colors.White));
            text.FontWeight = FontWeights.Bold;
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
            Border shape = new Border
            {
                Height = FaceSize - 2,
                CornerRadius = corners,
                Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x4A, 0x55, 0x62)),
                Padding = new Thickness(7, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = text,
            };
            if (width > 0)
            {
                shape.Width = width;
            }
            return shape;
        }

        private static Color Lighter(Color color)
        {
            return Color.FromArgb(0xFF, (byte)(color.R + (255 - color.R) / 3), (byte)(color.G + (255 - color.G) / 3),
                                  (byte)(color.B + (255 - color.B) / 3));
        }
    }
}
