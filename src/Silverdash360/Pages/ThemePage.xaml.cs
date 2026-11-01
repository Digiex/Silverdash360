using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Navigation;
using Microsoft.Xbox.Controls;
using Silverdash360.Controls;
using Silverdash360.Model;
using Silverdash360.Services;

namespace Silverdash360.Pages
{
    /// <summary>
    /// Picks the dashboard's theme: the one the console's dashboard uses for
    /// the profile, Silverdash's own, one built into the console's dashboard,
    /// or any installed theme package.
    /// </summary>
    public partial class ThemePage : XboxApplicationPage
    {
        private sealed class Choice
        {
            public string Setting;
            public string Title;
            public string Subtitle;
            public ContentItem Package;
        }

        public ThemePage()
        {
            InitializeComponent();
            Motion.UsePageTransitions(this);
            Items.Direction = ScrollDirection.Vertical;
            Items.Lines = 1;
            Items.TileWidth = 0;
            Items.TileHeight = 72;
            Items.Spacing = 8;
            Items.TileTemplate = new RowTemplate(o => Row((Choice)o));
            Items.ItemInvoked += delegate(object sender, ItemEventArgs e)
            {
                Choice choice = (Choice)e.Item;
                StatusText.Text = "Loading " + choice.Title + "...";
                Themes.Choose(choice.Setting, delegate
                {
                    StatusText.Text = "";
                    Show();
                });
            };
            Items.SelectionChanged += delegate
            {
                Choice choice = Items.SelectedItem as Choice;
                StatusText.Text = choice != null && choice.Package != null
                    ? choice.Package.TitleId.ToString("X8") + "  ·  " + choice.Package.FileName : "";
            };
        }

        private static ThemeService Themes { get { return App.Context.Themes; } }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Show();
            FocusHelper.Focus(Items);
            // The built-in themes are read when they're first wanted.
            Themes.FindBuiltInThemes(Show);
        }

        private void Show()
        {
            CurrentText.Text = Themes.Describe();
            List<Choice> choices = new List<Choice>
            {
                new Choice
                {
                    Setting = ThemeService.FollowDashboard,
                    Title = "Same as the Xbox Dashboard",
                    Subtitle = "The theme the profile signed in uses there",
                },
                new Choice { Setting = ThemeService.NoTheme, Title = "Silverdash", Subtitle = "No wallpaper, the usual colours" },
            };
            foreach (ColourScheme scheme in ColourScheme.All())
            {
                choices.Add(new Choice
                {
                    Setting = ThemeService.SettingFor(scheme),
                    Title = scheme.Name,
                    Subtitle = "Colours of the first Xbox 360 dashboard, no wallpaper",
                });
            }
            foreach (int builtIn in Themes.BuiltInThemes)
            {
                choices.Add(new Choice
                {
                    Setting = ThemeService.SettingFor(builtIn),
                    Title = ThemeService.BuiltInName(builtIn),
                    Subtitle = "Built into the Xbox Dashboard",
                });
            }
            foreach (ContentItem package in Themes.Installed())
            {
                choices.Add(new Choice
                {
                    Setting = ThemeService.SettingFor(package),
                    Title = package.DisplayName.Length > 0 ? package.DisplayName : package.FileName,
                    Subtitle = package.TitleName,
                    Package = package,
                });
            }
            Choice picked = choices.FirstOrDefault(c => c.Setting == Themes.Setting);
            int selected = picked != null ? choices.IndexOf(picked) : 0;
            Items.Items = choices.Cast<object>().ToList();
            Items.SelectedIndex = selected;
        }

        private static RowData Row(Choice choice)
        {
            RowData row = new RowData
            {
                Title = choice.Title,
                Subtitle = choice.Subtitle,
                Detail = choice.Setting == Themes.Setting ? "in use" : "",
                ArtSeed = choice.Package != null ? choice.Package.TitleId : 0,
            };
            // A theme package's thumbnail is a preview of the theme.
            if (choice.Package != null)
            {
                row.Art = done => App.Context.Images.ContentArt(choice.Package, done);
            }
            return row;
        }
    }
}
