using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
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
    /// Picks a wallpaper for the slot the theme editor asked for
    /// (<see cref="ThemeDraft.Slot"/>): none, a picture on the console's
    /// drives (the files page picking), or one of a theme's, the dashboard's
    /// built-in themes and the installed ones. With ?theme= it lists that
    /// theme's wallpapers, each shown behind the list once the user stops.
    /// </summary>
    public partial class WallpaperPickerPage : XboxApplicationPage
    {
        // Showing a wallpaper decodes it: it waits until the user stops.
        private const int PreviewQuietMs = 400;

        private sealed class Choice
        {
            public string Title;
            public string Subtitle;
            public Action Run;
            public byte[] Wallpaper;
        }

        private string theme;
        private bool previewWaiting;

        public WallpaperPickerPage()
        {
            InitializeComponent();
            Motion.UsePageTransitions(this);
            Items.Direction = ScrollDirection.Vertical;
            Items.Lines = 1;
            Items.TileWidth = 0;
            Items.TileHeight = 72;
            Items.Spacing = 8;
            Items.TileTemplate = new RowTemplate(o => new RowData { Title = ((Choice)o).Title, Subtitle = ((Choice)o).Subtitle })
            {
                IconSize = 0,
            };
            Items.ItemInvoked += delegate(object sender, ItemEventArgs e) { ((Choice)e.Item).Run(); };
            Items.SelectionChanged += delegate { PreviewLater(); };
        }

        private static DashboardContext Context { get { return App.Context; } }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            // A wallpaper was picked on a page this one opened: back to the
            // editor, once this navigation is over.
            if (ThemeDraft.Picked)
            {
                Silverdash360.Core.Background.OnUi(GoBack);
                return;
            }
            string query;
            theme = NavigationContext.QueryString.TryGetValue("theme", out query) ? query : null;
            string name;
            NavigationContext.QueryString.TryGetValue("name", out name);
            TitleText.Text = theme == null ? "Wallpaper " + (ThemeDraft.Slot + 1) : name;
            FocusHelper.Focus(Items);
            if (theme == null)
            {
                ShowSources();
                Context.Themes.FindBuiltInThemes(ShowSources);
            }
            else
            {
                ShowThemeWallpapers();
            }
        }

        // None, a picture, or a theme to take one from.
        private void ShowSources()
        {
            int selected = Math.Max(0, Items.SelectedIndex);
            List<Choice> choices = new List<Choice>
            {
                new Choice { Title = "No wallpaper", Subtitle = "The background colours alone", Run = () => Pick(null) },
                new Choice
                {
                    Title = "A picture on the console's drives",
                    Subtitle = "JPEG or PNG, up to " + ThemeDraft.MaxPictureSize / (1024 * 1024) + " MB",
                    Run = () => Context.OpenFiles("/", true),
                },
            };
            ThemeService themes = Context.Themes;
            foreach (int builtIn in themes.BuiltInThemes)
            {
                string setting = ThemeService.SettingFor(builtIn);
                string name = ThemeService.BuiltInName(builtIn);
                choices.Add(new Choice
                {
                    Title = name,
                    Subtitle = "Built into the Xbox Dashboard",
                    Run = () => Context.OpenWallpaperPicker(setting, name),
                });
            }
            foreach (ContentItem package in themes.Installed())
            {
                string setting = ThemeService.SettingFor(package);
                string name = package.DisplayName.Length > 0 ? package.DisplayName : package.FileName;
                choices.Add(new Choice
                {
                    Title = name,
                    Subtitle = package.TitleName,
                    Run = () => Context.OpenWallpaperPicker(setting, name),
                });
            }
            Items.Items = choices.Cast<object>().ToList();
            Items.SelectedIndex = Math.Min(selected, choices.Count - 1);
        }

        private void ShowThemeWallpapers()
        {
            KindText.Text = "wallpaper " + (ThemeDraft.Slot + 1) + " from";
            Items.EmptyText = "Reading the theme...";
            Items.Items = null;
            Context.Themes.ReadWallpapers(theme, delegate(List<byte[]> wallpapers)
            {
                Items.EmptyText = "This theme has no wallpapers Silverdash can show.";
                List<Choice> choices = new List<Choice>();
                for (int i = 0; i < wallpapers.Count; i++)
                {
                    byte[] wallpaper = wallpapers[i];
                    choices.Add(new Choice
                    {
                        Title = "Wallpaper " + (i + 1),
                        Subtitle = wallpaper.Length / 1024 + " KB",
                        Wallpaper = wallpaper,
                        Run = () => Pick(wallpaper),
                    });
                }
                Items.Items = choices.Cast<object>().ToList();
                PreviewLater();
            });
        }

        private void Pick(byte[] wallpaper)
        {
            ThemeDraft.Pick(wallpaper);
            GoBack();
        }

        private void PreviewLater()
        {
            if (previewWaiting)
            {
                return;
            }
            previewWaiting = true;
            UiIdle.Run(() =>
            {
                previewWaiting = false;
                Choice choice = Items.SelectedItem as Choice;
                ShowPreview(choice != null ? choice.Wallpaper : null);
                return false;
            }, PreviewQuietMs);
        }

        private void ShowPreview(byte[] wallpaper)
        {
            BitmapImage image = null;
            if (wallpaper != null)
            {
                try
                {
                    image = new BitmapImage();
                    image.SetSource(new MemoryStream(wallpaper));
                }
                catch (Exception e)
                {
                    Log.Write("Couldn't decode a wallpaper: {0}", e.Message);
                    image = null;
                }
            }
            Preview.Source = image;
            PreviewShade.Visibility = image != null ? Visibility.Visible : Visibility.Collapsed;
        }

        private void GoBack()
        {
            if (NavigationService.CanGoBack)
            {
                NavigationService.GoBack();
            }
        }
    }
}
