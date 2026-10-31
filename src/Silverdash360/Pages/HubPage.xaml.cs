using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using System.Windows.Threading;
using Microsoft.Xbox.Controls;
using Silverdash360.Controls;
using Silverdash360.Modules;
using Silverdash360.Services;

namespace Silverdash360.Pages
{
    /// <summary>
    /// The dashboard's main screen: the sections from <see cref="ModuleRegistry"/>
    /// side by side, switched with LB/RB or by moving past a section's edge.
    /// The sections live in <see cref="App"/>, since every navigation makes a
    /// new page.
    /// </summary>
    public partial class HubPage : XboxApplicationPage
    {
        private static int current;

        // Sections the library changed under since they were last refreshed.
        // Kept with the sections, which outlive the page.
        private static readonly Dictionary<IDashboardModule, bool> stale = new Dictionary<IDashboardModule, bool>();

        // The theme the sections were last drawn with.
        private static int themeVersion;

        private readonly DispatcherTimer clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        private readonly DispatcherTimer wallpaperDelay = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Motion.SectionMs + 30),
        };
        private int shownWallpaper = -1;
        private readonly List<TextBlock> headers = new List<TextBlock>();
        private readonly Storyboard sliding = new Storyboard();
        private readonly Storyboard sectionSlide = new Storyboard();

        public HubPage()
        {
            InitializeComponent();
            Motion.UsePageTransitions(this);
            clock.Tick += delegate { ShowClock(); };
            wallpaperDelay.Tick += delegate { ShowWallpaper(true); };
            GamePadButtonDown += OnButtonDown;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private static DashboardContext Context { get { return App.Context; } }
        private static IList<IDashboardModule> Modules { get { return App.Modules; } }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            foreach (IDashboardModule module in Modules)
            {
                TextBlock header = Theme.TextBlock(module.Header, 26, Theme.SubText);
                header.Margin = new Thickness(0, 0, 30, 0);
                headers.Add(header);
                Headers.Children.Add(header);
                module.EdgeReached += OnModuleEdge;
            }
            Context.Status += OnStatus;
            Context.Library.Changed += OnLibraryChanged;
            Context.Library.Progress += OnLibraryProgress;
            Context.Themes.Changed += OnThemeChanged;
            Context.LegendChanged += OnLegendChanged;
            if (themeVersion != Context.Themes.Version)
            {
                MarkAllStale();
            }
            ShowProgress(Context.Library.CurrentProgress);
            ShowGamer();
            ShowClock();
            clock.Start();
            Show(current, 0);
        }

        // The sections outlive the page, so let go of them for the next one.
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            clock.Stop();
            foreach (IDashboardModule module in Modules)
            {
                module.EdgeReached -= OnModuleEdge;
            }
            Context.Status -= OnStatus;
            Context.Library.Changed -= OnLibraryChanged;
            Context.Library.Progress -= OnLibraryProgress;
            Context.Themes.Changed -= OnThemeChanged;
            Context.LegendChanged -= OnLegendChanged;
            sliding.Stop();
            wallpaperDelay.Stop();
            SectionHost.Content = null;
            Wallpaper.Source = null;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (SectionHost.Content != null)
            {
                Modules[current].Activate();
            }
        }

        private void OnLibraryProgress(object sender, LibraryProgressEventArgs e)
        {
            ShowProgress(e.Progress);
        }

        // A bar filled to how far the refresh is, or a block sliding along it
        // while that isn't known; hidden when no refresh runs.
        private void ShowProgress(LibraryProgress progress)
        {
            if (progress == null)
            {
                sliding.Stop();
                ProgressPanel.Visibility = Visibility.Collapsed;
                StatusText.Visibility = Visibility.Visible;
                return;
            }
            ProgressPanel.Visibility = Visibility.Visible;
            StatusText.Visibility = Visibility.Collapsed;
            ProgressText.Text = progress.Text;
            if (progress.Fraction.HasValue)
            {
                sliding.Stop();
                ProgressSlide.X = 0;
                ProgressFill.Width = ProgressTrack.Width * Math.Max(0, Math.Min(1, progress.Fraction.Value));
                return;
            }
            if (sliding.Children.Count == 0)
            {
                DoubleAnimation slide = new DoubleAnimation
                {
                    From = -60,
                    To = ProgressTrack.Width,
                    Duration = TimeSpan.FromSeconds(1.2),
                    RepeatBehavior = RepeatBehavior.Forever,
                };
                Storyboard.SetTarget(slide, ProgressSlide);
                Storyboard.SetTargetProperty(slide, new PropertyPath("X"));
                sliding.Children.Add(slide);
            }
            if (ProgressFill.Width != 60)
            {
                ProgressFill.Width = 60;
                sliding.Begin();
            }
        }

        // Only the section on screen is refreshed now; the others when they're
        // shown, so a change doesn't build tiles nobody sees.
        private void OnLibraryChanged(object sender, EventArgs e)
        {
            foreach (IDashboardModule module in Modules)
            {
                stale[module] = true;
            }
            RefreshIfStale(Modules[current]);
            ShowGamer();
            Modules[current].Activate();
        }

        // The sections show the theme, settings by name, so they're redrawn.
        private void OnThemeChanged(object sender, EventArgs e)
        {
            MarkAllStale();
            RefreshIfStale(Modules[current]);
            shownWallpaper = -1;
            ShowWallpaper(true);
        }

        private static void MarkAllStale()
        {
            themeVersion = Context.Themes.Version;
            foreach (IDashboardModule module in Modules)
            {
                stale[module] = true;
            }
        }

        private static void RefreshIfStale(IDashboardModule module)
        {
            bool isStale;
            if (stale.TryGetValue(module, out isStale) && isStale)
            {
                stale[module] = false;
                module.Refresh();
            }
        }

        private void OnStatus(object sender, StatusEventArgs e)
        {
            StatusText.Text = e.Text;
        }

        private void OnModuleEdge(object sender, EdgeEventArgs e)
        {
            if (e.Edge == Edge.Left && current > 0)
            {
                Show(current - 1, -1);
                e.Handled = true;
            }
            else if (e.Edge == Edge.Right && current < Modules.Count - 1)
            {
                Show(current + 1, 1);
                e.Handled = true;
            }
        }

        /// <summary>The index in <see cref="App.Modules"/> of the section shown.</summary>
        public static int CurrentSection { get { return current; } }

        private void OnButtonDown(object sender, GamePadButtonEventArgs e)
        {
            e.Handled = Press(e.Button);
        }

        /// <summary>Does what a press of the button does on the hub; false when it does nothing.</summary>
        public bool Press(GamePadButton button)
        {
            if (button == GamePadButton.LeftShoulder)
            {
                Show((current + Modules.Count - 1) % Modules.Count, -1);
                return true;
            }
            if (button == GamePadButton.RightShoulder)
            {
                Show((current + 1) % Modules.Count, 1);
                return true;
            }
            return false;
        }

        /// <summary>Shows a section, sliding it in from the side it's on.</summary>
        public void ShowSection(int index)
        {
            Show(index, Math.Sign(index - current));
        }

        private void OnLegendChanged(object sender, EventArgs e)
        {
            ShowLegend();
        }

        private void ShowLegend()
        {
            LegendText.Text = Modules[current].Legend + "   LB RB sections";
        }

        // Slides the section in from the side it comes from.
        private void Show(int index, int direction)
        {
            current = Math.Max(0, Math.Min(Modules.Count - 1, index));
            IDashboardModule module = Modules[current];
            for (int i = 0; i < headers.Count; i++)
            {
                headers[i].Foreground = i == current ? Theme.Text : Theme.SubText;
                headers[i].FontWeight = i == current ? FontWeights.Bold : FontWeights.Normal;
            }
            FrameworkElement view = module.View;
            RefreshIfStale(module);
            ContentControl oldHost = view.Parent as ContentControl;
            if (oldHost != null && oldHost != SectionHost)
            {
                oldHost.Content = null;
            }
            SectionHost.Content = view;
            ShowLegend();
            StatusText.Text = Context.Library.IsLoading ? "Reading the console's content..." : "";
            if (direction != 0)
            {
                Slide(direction);
            }
            module.Activate();
            ShowWallpaper(direction == 0);
        }

        // Each section has a wallpaper of its own, as each blade had one in
        // the console's first dashboard. One that isn't decoded is decoded
        // once the section has slid in, so the decode doesn't stall the slide.
        private void ShowWallpaper(bool now)
        {
            wallpaperDelay.Stop();
            ThemeService themes = Context.Themes;
            if (themes.Current == null || themes.Current.Wallpapers.Count == 0)
            {
                Wallpaper.Source = null;
                WallpaperShade.Visibility = Visibility.Collapsed;
                shownWallpaper = -1;
                return;
            }
            int index = current % themes.Current.Wallpapers.Count;
            if (index == shownWallpaper)
            {
                return;
            }
            if (!now && !themes.IsDecoded(index))
            {
                wallpaperDelay.Start();
                return;
            }
            Wallpaper.Source = themes.Wallpaper(index);
            WallpaperShade.Visibility = Wallpaper.Source != null ? Visibility.Visible : Visibility.Collapsed;
            shownWallpaper = index;
        }

        // A slide alone by default: fading the whole section redraws all of
        // it on every frame, which the console feels.
        private void Slide(int direction)
        {
            sectionSlide.Stop();
            sectionSlide.Children.Clear();
            DoubleAnimation slide = new DoubleAnimation
            {
                From = Motion.SectionDistance * direction,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(Motion.SectionMs),
                EasingFunction = Motion.EaseOut(),
            };
            Storyboard.SetTarget(slide, SectionSlide);
            Storyboard.SetTargetProperty(slide, new PropertyPath("X"));
            sectionSlide.Children.Add(slide);
            if (Motion.SectionFade)
            {
                DoubleAnimation fade = new DoubleAnimation { From = 0.2, To = 1, Duration = slide.Duration };
                Storyboard.SetTarget(fade, SectionHost);
                Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
                sectionSlide.Children.Add(fade);
            }
            sectionSlide.Begin();
        }

        private void ShowGamer()
        {
            var gamer = Context.Library.Gamer;
            GamerPicture.Source = null;
            GamerInitials.Text = "";
            if (gamer == null)
            {
                GamertagText.Text = Context.Library.IsLoaded ? "Not signed in" : "";
                GamerscoreText.Text = "";
                return;
            }
            GamertagText.Text = gamer.Gamertag;
            GamerscoreText.Text = gamer.Gamerscore + " G";
            GamerInitials.Text = Theme.Initials(gamer.Gamertag);
            Context.Images.GamerPicture(gamer, source =>
            {
                GamerPicture.Source = source;
                GamerInitials.Text = "";
            });
        }

        private void ShowClock()
        {
            ClockText.Text = DateTime.Now.ToString("t");
        }
    }
}
