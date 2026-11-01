using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Silverdash360.Model;
using Silverdash360.Services;

namespace Silverdash360.Controls
{
    /// <summary>
    /// Selection shared by the tile templates: an accent frame and a slightly
    /// larger tile while selected, dimmer when the grid doesn't have focus.
    /// </summary>
    public abstract class TileTemplateBase : ITileTemplate
    {
        protected const string FrameName = "SelectionFrame";

        /// <summary>How much the selected tile grows, 1 for not at all.</summary>
        protected double Grow = 1.06;

        public abstract FrameworkElement Create(object item);

        public void SetSelected(FrameworkElement tile, bool selected, bool focused)
        {
            Border frame = (Border)((Grid)tile).Children[((Grid)tile).Children.Count - 1];
            frame.Opacity = selected ? (focused ? 1 : 0.35) : 0;
            double scale = selected && focused ? Grow : 1;
            ScaleTransform transform = (ScaleTransform)tile.RenderTransform;
            transform.ScaleX = transform.ScaleY = scale;
            OnSelected(tile, selected && focused);
        }

        protected virtual void OnSelected(FrameworkElement tile, bool selected)
        {
        }

        /// <summary>The tile's root; add content, then call <see cref="Finish"/>.</summary>
        protected static Grid NewTile(Brush background)
        {
            return new Grid
            {
                Background = background,
                RenderTransform = new ScaleTransform(),
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
        }

        protected static Grid Finish(Grid tile)
        {
            tile.Children.Add(new Border
            {
                BorderBrush = Theme.Accent,
                BorderThickness = new Thickness(4),
                Opacity = 0,
            });
            return tile;
        }
    }

    /// <summary>A title's box art with its name over the bottom, for the cover view.</summary>
    public sealed class CoverTileTemplate : TileTemplateBase
    {
        private readonly ImageService images;
        private readonly Func<uint> userIndex;

        public CoverTileTemplate(ImageService images, Func<uint> userIndex)
        {
            this.images = images;
            this.userIndex = userIndex;
        }

        public override FrameworkElement Create(object item)
        {
            TitleEntry title = (TitleEntry)item;
            Grid tile = NewTile(Theme.Panel);
            // The kind's cover with the title's icon, until the box front
            // arrives over it.
            Covers.Cover cover = Covers.For(title, 1);
            tile.Children.Add(cover.Root);
            images.TitleArt(title, userIndex(), cover.SetIcon);

            Image art = new Image { Stretch = Stretch.UniformToFill };
            tile.Children.Add(art);
            images.BoxArt(title, delegate(ImageSource source)
            {
                art.Source = source;
                cover.Root.Visibility = Visibility.Collapsed;
            });

            Grid caption = new Grid
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1),
                    GradientStops =
                    {
                        new GradientStop { Color = Color.FromArgb(0x00, 0, 0, 0), Offset = 0 },
                        new GradientStop { Color = Color.FromArgb(0xE0, 0, 0, 0), Offset = 0.55 },
                    },
                },
            };
            StackPanel text = new StackPanel { Margin = new Thickness(10, 22, 10, 8) };
            text.Children.Add(Theme.TextBlock(title.Name, 17, Theme.Text));
            text.Children.Add(Theme.TextBlock(Subtitle(title), 13, Theme.SubText));
            caption.Children.Add(text);
            tile.Children.Add(caption);

            if (title.IsDisc)
            {
                tile.Children.Add(DiscBadge());
            }
            if (!title.IsInstalled)
            {
                tile.Opacity = 0.75;
            }
            return Finish(tile);
        }

        // A small disc in the cover's lower right corner, for the disc in
        // the drive: silver, with the hole in the middle.
        private static FrameworkElement DiscBadge()
        {
            Grid badge = new Grid
            {
                Width = 30,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 8, 8),
            };
            badge.Children.Add(new Ellipse
            {
                Fill = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop { Color = Color.FromArgb(0xFF, 0xF4, 0xF6, 0xF8), Offset = 0.3 },
                        new GradientStop { Color = Color.FromArgb(0xFF, 0xA8, 0xB4, 0xC0), Offset = 1 },
                    },
                },
                Stroke = new SolidColorBrush(Color.FromArgb(0xC0, 0x20, 0x28, 0x30)),
                StrokeThickness = 1.5,
            });
            badge.Children.Add(new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = new SolidColorBrush(Color.FromArgb(0xFF, 0x20, 0x28, 0x30)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            return badge;
        }

        internal static string Subtitle(TitleEntry title)
        {
            if (title.Played != null && title.Played.GamerscoreTotal > 0)
            {
                return string.Format("{0}/{1} G", title.Played.GamerscoreEarned, title.Played.GamerscoreTotal);
            }
            if (!title.IsInstalled)
            {
                return "Not installed";
            }
            return title.Publisher.Length > 0 ? title.Publisher : title.Source;
        }
    }

    /// <summary>
    /// What a title's tile shows, for <see cref="TileGrid.TileKey"/>. Only
    /// what's shown: while a start reads the library a section at a time, a
    /// title's size and packages grow, and a cover that looks the same keeps
    /// its tile.
    /// </summary>
    public static class TileKeys
    {
        /// <summary>
        /// A cover, or an icon with the name. From what decides the subtitle
        /// rather than the subtitle itself, which takes a millisecond or more
        /// to work out on the console.
        /// </summary>
        public static string Cover(TitleEntry title, int themeVersion)
        {
            ContentItem launch = title.LaunchItem;
            PlayedTitle played = title.Played;
            return themeVersion + "|" + title.Key + "|" + title.Name + "|" + (int)title.Kind + "|" +
                   (played != null ? played.GamerscoreEarned + "/" + played.GamerscoreTotal : "") + "|" +
                   (title.IsDisc ? "disc" : "") + "|" +
                   (launch != null
                       ? launch.FileName + "|" + launch.Type + "|" + (launch.Metadata != null ? launch.Metadata.Publisher : "")
                       : title.Content.Count + "|" + title.Executables.Count);
        }

        /// <summary>A list row, which shows the size and the publisher too.</summary>
        public static string Row(TitleEntry title, int themeVersion)
        {
            return Cover(title, themeVersion) + "|" + title.Publisher + "|" + title.TotalSize;
        }
    }

    /// <summary>
    /// A letter heading the titles under it, in a shelf sorted by name: the
    /// letter large, with how many titles it has.
    /// </summary>
    public sealed class LetterTileTemplate : TileTemplateBase
    {
        private readonly Func<object, string> letter;
        private readonly Func<object, string> detail;

        public LetterTileTemplate(Func<object, string> letter, Func<object, string> detail)
        {
            this.letter = letter;
            this.detail = detail;
        }

        public override FrameworkElement Create(object item)
        {
            Grid tile = NewTile(Theme.Panel);
            tile.Children.Add(new Border { Height = 6, VerticalAlignment = VerticalAlignment.Top, Background = Theme.Accent });
            TextBlock big = Theme.TextBlock(letter(item), 120, Theme.Text);
            big.FontWeight = FontWeights.Bold;
            big.HorizontalAlignment = HorizontalAlignment.Center;
            big.VerticalAlignment = VerticalAlignment.Center;
            big.Margin = new Thickness(0, 0, 0, 24);
            tile.Children.Add(big);
            TextBlock count = Theme.TextBlock(detail(item), 15, Theme.SubText);
            count.HorizontalAlignment = HorizontalAlignment.Center;
            count.VerticalAlignment = VerticalAlignment.Bottom;
            count.Margin = new Thickness(0, 0, 0, 16);
            tile.Children.Add(count);
            return Finish(tile);
        }
    }

    /// <summary>Tiles of more than one kind in a grid, each made by the template picked for its item.</summary>
    public sealed class MixedTileTemplate : ITileTemplate
    {
        private readonly Func<object, ITileTemplate> pick;

        public MixedTileTemplate(Func<object, ITileTemplate> pick)
        {
            this.pick = pick;
        }

        public FrameworkElement Create(object item)
        {
            ITileTemplate template = pick(item);
            FrameworkElement tile = template.Create(item);
            tile.Tag = template;
            return tile;
        }

        public void SetSelected(FrameworkElement tile, bool selected, bool focused)
        {
            ((ITileTemplate)tile.Tag).SetSelected(tile, selected, focused);
        }
    }

    /// <summary>What a list row shows.</summary>
    public sealed class RowData
    {
        public string Title;
        public string Subtitle;
        public string Detail;
        /// <summary>0 to 1 for a progress bar under the row, or null.</summary>
        public double? Progress;
        public uint ArtSeed;
        /// <summary>Loads art for the row's icon, or null for none.</summary>
        public Action<Action<ImageSource>> Art;
        public bool Dimmed;
        /// <summary>Shows a padlock in place of art, for something still locked.</summary>
        public bool Locked;
        /// <summary>Marks it new beside the title, as a setting an update added.</summary>
        public bool New;
    }

    /// <summary>A row with an icon, two lines of text and a detail on the right.</summary>
    public sealed class RowTemplate : TileTemplateBase
    {
        private readonly Func<object, RowData> describe;

        public RowTemplate(Func<object, RowData> describe)
        {
            this.describe = describe;
            Grow = 1.0;
        }

        /// <summary>Space for the icon; zero leaves it out.</summary>
        public double IconSize = 64;

        public override FrameworkElement Create(object item)
        {
            RowData row = describe(item);
            Grid tile = NewTile(Theme.Panel);
            Grid layout = new Grid { Margin = new Thickness(8) };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IconSize > 0 ? IconSize + 14 : 6) });
            layout.ColumnDefinitions.Add(new ColumnDefinition());
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (IconSize > 0)
            {
                Grid icon = new Grid
                {
                    Width = IconSize,
                    Height = IconSize,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = row.Locked ? Theme.PanelHighlight : Theme.PlaceholderBrush(row.ArtSeed),
                };
                TextBlock initials = Theme.TextBlock(row.Locked ? "" : Theme.Initials(row.Title), IconSize * 0.38, Theme.Text);
                initials.HorizontalAlignment = HorizontalAlignment.Center;
                initials.VerticalAlignment = VerticalAlignment.Center;
                icon.Children.Add(initials);
                if (row.Locked)
                {
                    icon.Children.Add(Padlock(IconSize * 0.45));
                }
                Image art = new Image { Stretch = Stretch.UniformToFill };
                icon.Children.Add(art);
                if (row.Art != null)
                {
                    row.Art(delegate(ImageSource source)
                    {
                        art.Source = source;
                        initials.Visibility = Visibility.Collapsed;
                    });
                }
                layout.Children.Add(icon);
            }

            StackPanel text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            if (row.New)
            {
                StackPanel titled = new StackPanel { Orientation = Orientation.Horizontal };
                titled.Children.Add(Theme.TextBlock(row.Title, 21, Theme.Text));
                Border mark = new Border
                {
                    Background = Theme.Accent,
                    Padding = new Thickness(7, 1, 7, 2),
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = Theme.TextBlock("new", 14, Theme.Text),
                };
                titled.Children.Add(mark);
                text.Children.Add(titled);
            }
            else
            {
                text.Children.Add(Theme.TextBlock(row.Title, 21, Theme.Text));
            }
            if (!string.IsNullOrEmpty(row.Subtitle))
            {
                text.Children.Add(Theme.TextBlock(row.Subtitle, 15, Theme.SubText));
            }
            if (row.Progress.HasValue)
            {
                Grid bar = new Grid { Height = 4, Margin = new Thickness(0, 6, 0, 0), Background = Theme.PanelHighlight };
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, row.Progress.Value), GridUnitType.Star) });
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 1 - row.Progress.Value), GridUnitType.Star) });
                bar.Children.Add(new Border { Background = Theme.Success });
                text.Children.Add(bar);
            }
            Grid.SetColumn(text, 1);
            layout.Children.Add(text);

            if (!string.IsNullOrEmpty(row.Detail))
            {
                TextBlock detail = Theme.TextBlock(row.Detail, 17, Theme.SubText);
                detail.VerticalAlignment = VerticalAlignment.Center;
                detail.Margin = new Thickness(12, 0, 8, 0);
                Grid.SetColumn(detail, 2);
                layout.Children.Add(detail);
            }
            tile.Children.Add(layout);
            if (row.Dimmed)
            {
                tile.Opacity = 0.6;
            }
            return Finish(tile);
        }

        protected override void OnSelected(FrameworkElement tile, bool selected)
        {
            ((Grid)tile).Background = selected ? Theme.PanelHighlight : Theme.Panel;
        }

        // A padlock: a shackle over a body with a keyhole.
        private static FrameworkElement Padlock(double size)
        {
            Canvas canvas = new Canvas { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            double body = size * 0.55;
            canvas.Children.Add(new Ellipse
            {
                Width = size * 0.56,
                Height = size * 0.7,
                Stroke = Theme.SubText,
                StrokeThickness = size * 0.1,
            });
            Canvas.SetLeft(canvas.Children[0], size * 0.22);
            Rectangle block = new Rectangle
            {
                Width = size * 0.8,
                Height = body,
                RadiusX = size * 0.06,
                RadiusY = size * 0.06,
                Fill = Theme.SubText,
            };
            Canvas.SetLeft(block, size * 0.1);
            Canvas.SetTop(block, size - body);
            canvas.Children.Add(block);
            Ellipse keyhole = new Ellipse { Width = size * 0.14, Height = size * 0.14, Fill = Theme.PanelHighlight };
            Canvas.SetLeft(keyhole, size * 0.43);
            Canvas.SetTop(keyhole, size - body * 0.65);
            canvas.Children.Add(keyhole);
            return canvas;
        }
    }
}
