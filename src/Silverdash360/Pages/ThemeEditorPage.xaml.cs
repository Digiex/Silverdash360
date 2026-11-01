using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Microsoft.Xbox.Controls;
using Silverdash360.Controls;
using Silverdash360.Core;
using Silverdash360.Model;
using Silverdash360.Modules;
using Silverdash360.Services;

namespace Silverdash360.Pages
{
    /// <summary>
    /// Edits the theme shown: its colours, each from a grid of swatches with
    /// the dashboard taking it on as the selection moves, and four
    /// wallpapers, from other themes or pictures on the console's drives
    /// (<see cref="WallpaperPickerPage"/>). Saving makes it the custom theme
    /// of the profile signed in, or of nobody signed in, and can save it
    /// for nobody signed in too. B leaves without saving and puts the
    /// colours back.
    /// </summary>
    public partial class ThemeEditorPage : XboxApplicationPage
    {
        private sealed class Row
        {
            public string Title;
            public string Subtitle;
            public Func<string> Detail;
            public Action Run;
        }

        // A colour of the draft, by its place in the palette. A generic over
        // Color, such as a Func returning one, is avoided: a generic over a
        // value type is compiled for it on the console's runtime, and such
        // instantiations broke pages there before.
        private sealed class ColourSetting
        {
            public string Name;
            public int Index;

            public Color Get(Theme.Palette p)
            {
                switch (Index)
                {
                    case 0: return p.Accent;
                    case 1: return p.SubText;
                    case 2: return p.Panel;
                    case 3: return p.PanelHighlight;
                    default: return p.Background[Index - 4];
                }
            }

            public void Set(Theme.Palette p, Color c)
            {
                switch (Index)
                {
                    case 0: p.Accent = c; break;
                    case 1: p.SubText = c; break;
                    case 2: p.Panel = c; break;
                    case 3: p.PanelHighlight = c; break;
                    default: p.Background[Index - 4] = c; break;
                }
            }
        }

        private ColourSetting picking;
        private Color before;
        private bool saving;
        private bool saved;
        private bool rowsShown;

        public ThemeEditorPage()
        {
            InitializeComponent();
            Motion.UsePageTransitions(this);
            Rows.Direction = ScrollDirection.Vertical;
            Rows.Lines = 1;
            Rows.TileWidth = 0;
            Rows.TileHeight = 60;
            Rows.Spacing = 6;
            Rows.TileTemplate = new RowTemplate(o =>
            {
                Row row = (Row)o;
                return new RowData { Title = row.Title, Subtitle = row.Subtitle, Detail = row.Detail != null ? row.Detail() : null };
            }) { IconSize = 0 };
            Rows.ItemInvoked += delegate(object sender, ItemEventArgs e)
            {
                if (!saving)
                {
                    ((Row)e.Item).Run();
                }
            };

            Swatches.Direction = ScrollDirection.Vertical;
            Swatches.Lines = 12;
            Swatches.TileWidth = 40;
            Swatches.TileHeight = 40;
            Swatches.Spacing = 6;
            Swatches.LeadTiles = 0;
            Swatches.TileTemplate = new SwatchTemplate();
            Swatches.Items = SwatchColours();
            Swatches.SelectionChanged += delegate
            {
                if (picking != null && Swatches.SelectedItem != null)
                {
                    Preview((Color)Swatches.SelectedItem);
                }
            };
            Swatches.ItemInvoked += delegate { EndPicking(true); };

            GamePadButtonDown += delegate(object sender, GamePadButtonEventArgs e)
            {
                if (e.Button != GamePadButton.B)
                {
                    return;
                }
                if (picking != null)
                {
                    // B puts back the colour it had.
                    EndPicking(false);
                    e.Handled = true;
                }
                else if (saving)
                {
                    e.Handled = true;
                }
            };
        }

        private static DashboardContext Context { get { return App.Context; } }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (ThemeDraft.Fresh)
            {
                ThemeDraft.Opened();
            }
            ThemeDraft.PickTaken();
            Theme.Apply(ThemeDraft.Palette, false);
            ShowRows();
            ShowWallpaper();
            FocusHelper.Focus(Rows);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            // Back to the theme page unsaved: the colours as they were. To
            // a picker, the draft stays as it is.
            if (!saved && e.Uri.OriginalString.IndexOf("Picker", StringComparison.OrdinalIgnoreCase) < 0 &&
                e.Uri.OriginalString.IndexOf("FilesPage", StringComparison.OrdinalIgnoreCase) < 0)
            {
                Theme.Apply(ThemeDraft.Original, ThemeDraft.OriginalIsDefault);
            }
        }

        private void ShowRows()
        {
            // Back from a picker on a page made anew: the row it was opened from.
            bool first = !rowsShown;
            int selected = first ? ThemeDraft.Row : Math.Max(0, Rows.SelectedIndex);
            rowsShown = true;
            List<Row> rows = new List<Row>();
            foreach (ColourSetting colour in Colours())
            {
                ColourSetting setting = colour;
                rows.Add(new Row
                {
                    Title = setting.Name,
                    Detail = () => Hex(setting.Get(ThemeDraft.Palette)),
                    Run = () => StartPicking(setting),
                });
            }
            for (int i = 0; i < ThemeDraft.Slots; i++)
            {
                int slot = i;
                rows.Add(new Row
                {
                    Title = "Wallpaper " + (slot + 1),
                    Subtitle = slot == 0 ? "Home and every fourth section" : "The next section",
                    Detail = () => ThemeDraft.Wallpapers[slot] != null ? (ThemeDraft.Wallpapers[slot].Length / 1024) + " KB" : "none",
                    Run = () =>
                    {
                        ThemeDraft.Slot = slot;
                        ThemeDraft.Row = Rows.SelectedIndex;
                        Context.OpenWallpaperPicker(null);
                    },
                });
            }
            Gamer gamer = Context.Library.Gamer;
            rows.Add(new Row
            {
                Title = "Save",
                Subtitle = gamer != null ? "Use it whenever " + gamer.Gamertag + " is signed in" : "Use it while nobody is signed in",
                Run = () => Save(false),
            });
            if (gamer != null)
            {
                rows.Add(new Row
                {
                    Title = "Save for nobody signed in too",
                    Subtitle = "Use it for " + gamer.Gamertag + " and while nobody is signed in",
                    Run = () => Save(true),
                });
            }
            rows.Add(new Row
            {
                Title = "Save as...",
                Subtitle = "Keep it under a name in Silverdash, for everyone on this console",
                Run = SaveAs,
            });
            if (first)
            {
                // Back from a picker: the row in view, as it was opened from.
                Rows.ShowItems(rows.Cast<object>().ToList(), Math.Min(selected, rows.Count - 1), 0);
                return;
            }
            Rows.Items = rows.Cast<object>().ToList();
            Rows.SelectedIndex = Math.Min(selected, rows.Count - 1);
        }

        private static List<ColourSetting> Colours()
        {
            string[] names =
            {
                "Accent", "Secondary text", "Panels", "Selected panel", "Background, top left", "Background, middle",
                "Background, bottom right",
            };
            List<ColourSetting> colours = new List<ColourSetting>();
            for (int i = 0; i < names.Length; i++)
            {
                colours.Add(new ColourSetting { Name = names[i], Index = i });
            }
            return colours;
        }

        private void StartPicking(ColourSetting setting)
        {
            picking = setting;
            before = setting.Get(ThemeDraft.Palette);
            SwatchTitle.Text = setting.Name;
            HelpText.Visibility = Visibility.Collapsed;
            Swatches.Visibility = Visibility.Visible;
            Legend.Text = "A use colour   B keep the old one";
            // The swatch nearest the colour it has.
            IList<object> colours = Swatches.Items;
            int nearest = 0;
            int best = int.MaxValue;
            for (int i = 0; i < colours.Count; i++)
            {
                int d = Distance((Color)colours[i], before);
                if (d < best)
                {
                    best = d;
                    nearest = i;
                }
            }
            Swatches.SelectedIndex = nearest;
            FocusHelper.Focus(Swatches);
        }

        private void EndPicking(bool keep)
        {
            if (!keep)
            {
                Preview(before);
            }
            picking = null;
            SwatchTitle.Text = "";
            Swatches.Visibility = Visibility.Collapsed;
            HelpText.Visibility = Visibility.Visible;
            Legend.Text = "A change   B back without saving";
            ShowRows();
            FocusHelper.Focus(Rows);
        }

        // The dashboard in the colour at once, so the pick is seen in place.
        private void Preview(Color colour)
        {
            picking.Set(ThemeDraft.Palette, colour);
            Theme.Apply(ThemeDraft.Palette, false);
        }

        private void Save(bool forNobodyToo)
        {
            saving = true;
            StatusText.Text = "Saving...";
            Context.Themes.SaveCustomTheme(ThemeDraft.Palette.Copy(), ThemeDraft.SetWallpapers(), forNobodyToo, delegate
            {
                saving = false;
                saved = true;
                StatusText.Text = "";
                if (NavigationService.CanGoBack)
                {
                    NavigationService.GoBack();
                }
            });
        }

        // Asks for a name with the console's keyboard, then saves it for everyone.
        private void SaveAs()
        {
            BasicSettings.Ask(Context, "Save theme as", "A name for the theme, for everyone on this console.", "My theme",
                              delegate(string name)
                              {
                                  saving = true;
                                  StatusText.Text = "Saving " + name + "...";
                                  Context.Themes.SaveThemeAs(name, ThemeDraft.Palette.Copy(), ThemeDraft.SetWallpapers(), delegate
                                  {
                                      saving = false;
                                      saved = true;
                                      StatusText.Text = "";
                                      if (NavigationService.CanGoBack)
                                      {
                                          NavigationService.GoBack();
                                      }
                                  });
                              });
        }

        private void ShowWallpaper()
        {
            byte[] first = ThemeDraft.Wallpapers.FirstOrDefault(w => w != null);
            WallpaperImage.Load(first, delegate(BitmapSource image)
            {
                Wallpaper.Source = image;
                WallpaperShade.Visibility = image != null ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        // Greys, then twelve hues from dark to light; boxed, as above.
        private static List<object> SwatchColours()
        {
            List<object> colours = new List<object>();
            for (int i = 0; i < 12; i++)
            {
                byte v = (byte)(i * 255 / 11);
                colours.Add(Color.FromArgb(0xFF, v, v, v));
            }
            double[,] shades = { { 1, 0.25 }, { 1, 0.45 }, { 0.9, 0.65 }, { 0.8, 0.85 }, { 0.55, 1 }, { 0.25, 1 } };
            for (int s = 0; s < shades.GetLength(0); s++)
            {
                for (int h = 0; h < 12; h++)
                {
                    colours.Add(FromHsv(h * 30, shades[s, 0], shades[s, 1]));
                }
            }
            return colours;
        }

        private static Color FromHsv(double hue, double saturation, double value)
        {
            double c = value * saturation;
            double x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
            double m = value - c;
            double r = 0, g = 0, b = 0;
            switch ((int)(hue / 60) % 6)
            {
                case 0: r = c; g = x; break;
                case 1: r = x; g = c; break;
                case 2: g = c; b = x; break;
                case 3: g = x; b = c; break;
                case 4: r = x; b = c; break;
                default: r = c; b = x; break;
            }
            return Color.FromArgb(0xFF, (byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        private static int Distance(Color a, Color b)
        {
            int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
            return dr * dr + dg * dg + db * db;
        }

        private static string Hex(Color c)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }

        // A square of the colour, framed in white when selected.
        private sealed class SwatchTemplate : TileTemplateBase
        {
            public SwatchTemplate()
            {
                Grow = 1.15;
            }

            public override FrameworkElement Create(object item)
            {
                Grid tile = Finish(NewTile(new SolidColorBrush((Color)item)));
                ((Border)tile.Children[tile.Children.Count - 1]).BorderBrush = new SolidColorBrush(Colors.White);
                return tile;
            }
        }
    }
}
