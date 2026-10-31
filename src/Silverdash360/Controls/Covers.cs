using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Silverdash360.Model;

namespace Silverdash360.Controls
{
    /// <summary>
    /// The cover a title gets when the Marketplace has no box art for it,
    /// after the cases of its kind: a band across the top, coloured and
    /// labelled by what the title comes in (XBOX for an original Xbox game,
    /// XBOX 360, ARCADE, INDIE, DEMO, APP, HOMEBREW), over a dark body with
    /// the title's icon, or its initials when it has none.
    /// </summary>
    public static class Covers
    {
        private sealed class Style
        {
            public string Label;
            public Color Top;
            public Color Bottom;
        }

        // Made each time: a static readonly struct (Color) can read as zero
        // on the console's runtime.
        private static Style StyleOf(TitleEntry title)
        {
            if (title.IsDisc)
            {
                return new Style { Label = "DISC", Top = Rgb(0xD8, 0xDE, 0xE4), Bottom = Rgb(0x7C, 0x88, 0x94) };
            }
            if (title.Kind == TitleKind.App)
            {
                return new Style { Label = "APP", Top = Rgb(0x5C, 0xB8, 0xF7), Bottom = Rgb(0x1E, 0x78, 0xC2) };
            }
            ContentItem launch = title.LaunchItem;
            uint type = launch != null ? launch.Type : 0;
            switch (type)
            {
                case ContentType.XboxOriginalGame:
                    return new Style { Label = "XBOX", Top = Rgb(0x6C, 0xC2, 0x2E), Bottom = Rgb(0x2F, 0x7A, 0x12) };
                case ContentType.ArcadeTitle:
                    return new Style { Label = "ARCADE", Top = Rgb(0xF5, 0x9E, 0x3B), Bottom = Rgb(0xC2, 0x5E, 0x0A) };
                case ContentType.Xna:
                case ContentType.CommunityGame:
                    return new Style { Label = "INDIE", Top = Rgb(0xA0, 0x7C, 0xE0), Bottom = Rgb(0x5E, 0x3E, 0xA8) };
                case ContentType.GameDemo:
                    return new Style { Label = "DEMO", Top = Rgb(0x9A, 0xA8, 0xB6), Bottom = Rgb(0x56, 0x63, 0x70) };
            }
            if (launch == null && title.Executables.Count > 0)
            {
                return new Style { Label = "HOMEBREW", Top = Rgb(0x8A, 0x8A, 0x8A), Bottom = Rgb(0x4A, 0x4A, 0x4A) };
            }
            return new Style { Label = "XBOX 360", Top = Rgb(0x9B, 0xD1, 0x4B), Bottom = Rgb(0x4E, 0x8F, 0x16) };
        }

        /// <summary>A cover's parts: the root to show, and where the icon goes.</summary>
        public sealed class Cover
        {
            public Grid Root;
            public Image Icon;
            public TextBlock Initials;

            /// <summary>Puts the icon in, in place of the initials.</summary>
            public void SetIcon(ImageSource icon)
            {
                Icon.Source = icon;
                Initials.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// The cover for a title; scale sizes the band's text and the
        /// initials, 1 for a cover about 200 pixels wide.
        /// </summary>
        public static Cover For(TitleEntry title, double scale)
        {
            Style style = StyleOf(title);
            Grid root = new Grid
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1),
                    GradientStops =
                    {
                        new GradientStop { Color = Shade(style.Bottom, 0.45), Offset = 0 },
                        new GradientStop { Color = Shade(style.Bottom, 0.12), Offset = 1 },
                    },
                },
            };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.15, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.85, GridUnitType.Star) });

            Grid band = new Grid
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1),
                    GradientStops =
                    {
                        new GradientStop { Color = style.Top, Offset = 0 },
                        new GradientStop { Color = style.Bottom, Offset = 1 },
                    },
                },
            };
            TextBlock label = Theme.TextBlock(style.Label, 18 * scale, Theme.Text);
            label.FontWeight = FontWeights.Bold;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(12 * scale, 0, 0, 0);
            band.Children.Add(label);
            root.Children.Add(band);

            // The icon over the middle of the body, clear of the name the
            // tile writes along the bottom.
            Grid body = new Grid { Margin = new Thickness(0, 0, 0, 44 * scale) };
            Grid.SetRow(body, 1);
            TextBlock initials = Theme.TextBlock(Theme.Initials(title.Name), 56 * scale, Theme.Text);
            initials.HorizontalAlignment = HorizontalAlignment.Center;
            initials.VerticalAlignment = VerticalAlignment.Center;
            initials.Opacity = 0.85;
            body.Children.Add(initials);
            Image icon = new Image
            {
                Stretch = Stretch.Uniform,
                Width = 96 * scale,
                Height = 96 * scale,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            body.Children.Add(icon);
            root.Children.Add(body);
            return new Cover { Root = root, Icon = icon, Initials = initials };
        }

        private static Color Rgb(byte r, byte g, byte b)
        {
            return Color.FromArgb(0xFF, r, g, b);
        }

        private static Color Shade(Color color, double amount)
        {
            return Color.FromArgb(0xFF, (byte)(color.R * amount), (byte)(color.G * amount), (byte)(color.B * amount));
        }
    }
}
