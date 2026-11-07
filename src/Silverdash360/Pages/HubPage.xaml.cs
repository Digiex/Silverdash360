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
using Silverdash360.Core;
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

        // Sections whose view has been made.
        private static readonly List<IDashboardModule> built = new List<IDashboardModule>();

        // Sections are built and refreshed ahead of being shown once the user
        // has left the controller alone this long: building one takes the UI
        // thread for a while, and it's better spent when nobody is waiting.
        private const int PrepareQuietMs = 1500;
        private bool preparing;
        private bool refreshing;

        // Shown in place of a section that hasn't been built yet, until the
        // user stops on it, so passing it on the way to another builds and
        // draws nothing. It takes focus so LB and RB still reach the hub.
        private readonly ContentControl waitingView = new ContentControl { IsTabStop = true };
        private const int BuildQuietMs = 300;
        private bool buildWaiting;

        private readonly DispatcherTimer clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };

        // Every so often the logo turns into Digiex's for a few seconds.
        private readonly DispatcherTimer brandTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        private readonly Storyboard brandSwap = new Storyboard();
        private const double BrandFadeSeconds = 0.6;
        private const double BrandShownSeconds = 10;
        // The swap waits for the user to leave the controller alone this long.
        private const int BrandQuietMs = 1000;
        private bool brandWaiting;

        /// <summary>How much of the Silverdash360 logo shows, 0 to 1: less while Digiex's is in.</summary>
        public double LogoShown { get { return SilverdashLogo.Opacity; } }

        // A Release build looks for an update once a start, when the user has
        // left the controller alone this long. Debug builds, which tools\deploy.py
        // puts on a console, don't: the settings page and the debug server can.
        private const int UpdateCheckQuietMs = 3000;
        private static bool updateChecked;
        private int shownWallpaper = -1;
        private bool wallpaperWaiting;
        private bool wallpapersDecoding;
        private readonly Storyboard wallpaperFade = new Storyboard();

        // A wallpaper's decode waits this long after the last move, past the
        // slide and a quick run of LB and RB.
        private const int WallpaperQuietMs = 400;
        private readonly List<TextBlock> headers = new List<TextBlock>();
        private readonly Storyboard sliding = new Storyboard();
        private readonly Storyboard sectionSlide = new Storyboard();

        public HubPage()
        {
            InitializeComponent();
            wallpaperFade.Completed += OnWallpaperFaded;
            Motion.UsePageTransitions(this);
            clock.Tick += delegate { ShowClock(); };
            brandTimer.Tick += delegate { SwapLogo(); };
            Headers.SizeChanged += delegate { CentreLogo(); };
            LogoHost.SizeChanged += delegate { CentreLogo(); };
            DigiexImage.ImageFailed += (s, e) => Log.Write("The Digiex logo didn't load: {0}", e.ErrorException.Message);
            MakeBrandSwap();
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
            brandTimer.Start();
            Show(current, 0);
            Settings.Loaded += OnSettingsLoaded;
            OfferWelcome();
            CheckDisplayTrial();
            CheckForUpdate();
            if (Context.Updates.Offered != null)
            {
                OfferUpdate();
            }
            Prepare();
        }

        // The sections outlive the page, so let go of them for the next one.
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            clock.Stop();
            brandTimer.Stop();
            brandSwap.Stop();
            foreach (IDashboardModule module in Modules)
            {
                module.EdgeReached -= OnModuleEdge;
            }
            Context.Status -= OnStatus;
            Context.Library.Changed -= OnLibraryChanged;
            Context.Library.Progress -= OnLibraryProgress;
            Context.Themes.Changed -= OnThemeChanged;
            Context.LegendChanged -= OnLegendChanged;
            Settings.Loaded -= OnSettingsLoaded;
            sliding.Stop();
            SectionHost.Content = null;
            SetWallpaper(null, false);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (SectionHost.Content == waitingView)
            {
                FocusHelper.Focus(waitingView);
            }
            else if (SectionHost.Content != null)
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

        // The sections are refreshed when the user is quiet, the one on
        // screen first.
        private void OnLibraryChanged(object sender, EventArgs e)
        {
            foreach (IDashboardModule module in Modules)
            {
                stale[module] = true;
            }
            ShowGamer();
            Prepare();
        }

        private void Prepare()
        {
            if (!refreshing)
            {
                refreshing = true;
                UiIdle.Run(RefreshShown, UiIdle.BackgroundQuietMs);
            }
            if (!preparing)
            {
                preparing = true;
                UiIdle.Run(PrepareNext, PrepareQuietMs);
            }
        }

        private bool RefreshShown()
        {
            refreshing = false;
            IDashboardModule shown = Modules[current];
            if (SectionHost.Content != null && SectionHost.Content != waitingView && IsStale(shown))
            {
                RefreshIfStale(shown);
                shown.Activate();
            }
            return false;
        }

        // Builds or refreshes one section a step; false when all are ready.
        // Not while the library is loading: the start is when memory is
        // tightest, and the sections would be refreshed again when it's done.
        private bool PrepareNext()
        {
            if (SectionHost.Content != null && !Context.Library.IsLoading)
            {
                foreach (IDashboardModule module in Modules)
                {
                    if (!built.Contains(module))
                    {
                        Build(module);
                        return true;
                    }
                    if (IsStale(module) && module != Modules[current])
                    {
                        RefreshIfStale(module);
                        return true;
                    }
                }
            }
            preparing = false;
            return false;
        }

        // The section's view, made with what's there now if it's new.
        private static FrameworkElement Build(IDashboardModule module)
        {
            FrameworkElement view = module.View;
            if (!built.Contains(module))
            {
                built.Add(module);
                stale[module] = false;
            }
            return view;
        }

        private static bool IsStale(IDashboardModule module)
        {
            bool isStale;
            return stale.TryGetValue(module, out isStale) && isStale;
        }

        // The sections show the theme, settings by name, so they're redrawn.
        private void OnThemeChanged(object sender, EventArgs e)
        {
            // Each a step of its own, as the user may move between them.
            MarkAllStale();
            shownWallpaper = -1;
            ShowWallpaper(false);
            Prepare();
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

        // A move at a section's left or right edge goes to the next section
        // only as a press of its own, this long after the move before it:
        // holding the D-pad or the stick, or pressing it again and again to
        // run through a shelf, stops at its end. The bumpers switch at once.
        private const int EdgeCooldownMs = 400;

        private void OnModuleEdge(object sender, EdgeEventArgs e)
        {
            if ((e.Edge == Edge.Left || e.Edge == Edge.Right) && e.SincePreviousMove < EdgeCooldownMs)
            {
                e.Handled = true;
                return;
            }
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

        // Moves the logo so its middle is on the section names' middle: the
        // names sit at the bottom of the header, the logo in its centre.
        private void CentreLogo()
        {
            if (Headers.ActualHeight <= 0 || LogoHost.ActualHeight <= 0)
            {
                return;
            }
            Point names = Headers.TransformToVisual(LogoHost).Transform(new Point(0, Headers.ActualHeight / 2));
            LogoShift.Y += names.Y - LogoHost.ActualHeight / 2;
        }

        private void CheckForUpdate()
        {
#if !DEBUG_SERVER
            if (updateChecked || BuildInfo.Changed)
            {
                return;
            }
            updateChecked = true;
            UiIdle.Run(() =>
            {
                Context.Updates.Check(UpdateService.Current, false, delegate(UpdateService.Release release, string why)
                {
                    if (release != null)
                    {
                        Context.Updates.Offered = release;
                        OfferUpdate();
                    }
                });
                return false;
            }, UpdateCheckQuietMs);
#endif
        }

        private static bool trialChecked;

        // Started again from System settings, Silverdash360 goes back to the
        // page it was on, from the Settings section, so B comes back the way
        // it went. A display setting it was started again in is asked about
        // at once, before anything else: it's put back unless kept within
        // seconds.
        private void CheckDisplayTrial()
        {
            if (trialChecked)
            {
                return;
            }
            trialChecked = true;
            bool pending = false;
            Core.Background.Run(() =>
            {
                string page = ConsoleSettings.TakeReturn();
                ConsoleSettings.Trial trial = ConsoleSettings.PendingTrial();
                pending = trial != null;
                return page ?? (pending ? trial.Setting.Group : null);
            }, delegate(string page, Exception error)
            {
                if (error != null)
                {
                    Log.Write("A display setting on trial can't be read: {0}", error.Message);
                }
                else if (page != null)
                {
                    for (int i = 0; i < Modules.Count; i++)
                    {
                        if (Modules[i] is SettingsModule && i != current)
                        {
                            Show(i, 0);
                        }
                    }
                    SystemSettingsPage.ResumeAt(page, pending);
                    Context.OpenSystemSettings();
                }
            });
        }

        private void OnSettingsLoaded(object sender, EventArgs e)
        {
            OfferWelcome();
        }

        // On a new install, or after an update that added basic settings,
        // the welcome screen opens once the settings are read; an update
        // found meanwhile waits for it.
        private void OfferWelcome()
        {
            UiIdle.Run(() =>
            {
                if (SectionHost.Content != null && BasicSettings.WelcomeDue(Context))
                {
                    Context.OpenWelcome();
                }
                return false;
            }, UiIdle.QuietMs);
        }

        // Shows the update found once the user is quiet on the hub; on
        // another page, the hub shows it when it's back.
        private void OfferUpdate()
        {
            UiIdle.Run(() =>
            {
                if (SectionHost.Content != null && Context.Updates.Offered != null &&
                    !BasicSettings.WelcomeDue(Context))
                {
                    Context.OpenUpdate();
                }
                return false;
            }, UiIdle.QuietMs);
        }

        // Fades the Silverdash360 logo out and the Digiex one in, holds it,
        // and fades back.
        private void MakeBrandSwap()
        {
            double fade = BrandFadeSeconds;
            double back = fade + BrandShownSeconds;
            brandSwap.Children.Add(Fade(SilverdashLogo, new[] { 0, fade, back, back + fade }, new double[] { 1, 0, 0, 1 }));
            brandSwap.Children.Add(Fade(DigiexLogo, new[] { 0, fade, back, back + fade }, new double[] { 0, 1, 1, 0 }));
        }

        private static DoubleAnimationUsingKeyFrames Fade(UIElement target, double[] seconds, double[] opacities)
        {
            DoubleAnimationUsingKeyFrames animation = new DoubleAnimationUsingKeyFrames();
            for (int i = 0; i < seconds.Length; i++)
            {
                animation.KeyFrames.Add(new LinearDoubleKeyFrame
                {
                    KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds[i])),
                    Value = opacities[i],
                });
            }
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, new PropertyPath("Opacity"));
            return animation;
        }

        // Once the user is quiet, so the fade doesn't cost a scroll frames.
        private void SwapLogo()
        {
            if (brandWaiting)
            {
                return;
            }
            brandWaiting = true;
            UiIdle.Run(() =>
            {
                brandWaiting = false;
                if (SectionHost.Content != null)
                {
                    brandSwap.Begin();
                }
                return false;
            }, BrandQuietMs);
        }

        // Slides the section in from the side it comes from.
        private void Show(int index, int direction)
        {
            UiIdle.Input();
            current = Math.Max(0, Math.Min(Modules.Count - 1, index));
            IDashboardModule module = Modules[current];
            for (int i = 0; i < headers.Count; i++)
            {
                headers[i].Foreground = i == current ? Theme.Text : Theme.SubText;
                headers[i].FontWeight = i == current ? FontWeights.Bold : FontWeights.Normal;
            }
            bool ready = built.Contains(module);
            Host(ready ? module.View : waitingView);
            ShowLegend();
            StatusText.Text = Context.Library.IsLoading ? "Reading the console's content..." : "";
            if (direction != 0)
            {
                Slide(direction);
            }
            if (ready)
            {
                module.Activate();
                // A section the library changed under is shown as it was and
                // refreshed when the user stops.
                if (IsStale(module))
                {
                    Prepare();
                }
            }
            else
            {
                FocusHelper.Focus(waitingView);
                WaitToBuild();
            }
            ShowWallpaper(direction == 0);
        }

        private void Host(FrameworkElement view)
        {
            ContentControl oldHost = view.Parent as ContentControl;
            if (oldHost != null && oldHost != SectionHost)
            {
                oldHost.Content = null;
            }
            SectionHost.Content = view;
        }

        // Builds the section on screen once the user stops on it.
        private void WaitToBuild()
        {
            if (buildWaiting)
            {
                return;
            }
            buildWaiting = true;
            UiIdle.Run(() =>
            {
                buildWaiting = false;
                if (SectionHost.Content == waitingView)
                {
                    IDashboardModule module = Modules[current];
                    Host(Build(module));
                    RefreshIfStale(module);
                    module.Activate();
                }
                return false;
            }, BuildQuietMs);
        }

        // Each section has a wallpaper of its own, as each blade had one in
        // the console's first dashboard. One that isn't decoded is decoded
        // once the user stops moving, so the decode doesn't hold up the slide
        // or the next press.
        private void ShowWallpaper(bool now)
        {
            ThemeService themes = Context.Themes;
            if (themes.Current == null || themes.Current.Wallpapers.Count == 0)
            {
                SetWallpaper(null, false);
                shownWallpaper = -1;
                // Without a wallpaper, a colour scheme can colour the section
                // as a blade was; that too waits for the user to stop.
                if (now)
                {
                    Theme.ShowSection(Modules[current].Header);
                }
                else if (!wallpaperWaiting)
                {
                    wallpaperWaiting = true;
                    UiIdle.Run(() =>
                    {
                        wallpaperWaiting = false;
                        if (SectionHost.Content != null)
                        {
                            ShowWallpaper(true);
                        }
                        return false;
                    }, WallpaperQuietMs);
                }
                return;
            }
            int index = ThemeService.WallpaperSlot(Modules[current].Header, current, themes.Current.Wallpapers.Count);
            if (index == shownWallpaper)
            {
                return;
            }
            if (!now && !themes.IsDecoded(index))
            {
                if (!wallpaperWaiting)
                {
                    wallpaperWaiting = true;
                    UiIdle.Run(() =>
                    {
                        wallpaperWaiting = false;
                        if (SectionHost.Content != null)
                        {
                            ShowWallpaper(true);
                        }
                        return false;
                    }, WallpaperQuietMs);
                }
                return;
            }
            SetWallpaper(themes.Wallpaper(index), shownWallpaper >= 0);
            shownWallpaper = index;
            DecodeWallpapers();
        }

        // Shows a wallpaper, fading it in over the one before when asked.
        private void SetWallpaper(ImageSource source, bool fade)
        {
            // A fade under way ends at once where it was going.
            wallpaperFade.Stop();
            wallpaperFade.Children.Clear();
            if (WallpaperFade.Source != null)
            {
                Wallpaper.Source = WallpaperFade.Source;
            }
            WallpaperFade.Source = null;
            WallpaperFade.Opacity = 0;
            WallpaperShade.Visibility = source != null ? Visibility.Visible : Visibility.Collapsed;
            if (!fade || source == null || Wallpaper.Source == null || Motion.WallpaperFadeMs <= 0)
            {
                Wallpaper.Source = source;
                return;
            }
            WallpaperFade.Source = source;
            DoubleAnimation animation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(Motion.WallpaperFadeMs),
            };
            Storyboard.SetTarget(animation, WallpaperFade);
            Storyboard.SetTargetProperty(animation, new PropertyPath("Opacity"));
            wallpaperFade.Children.Add(animation);
            wallpaperFade.Begin();
        }

        private void OnWallpaperFaded(object sender, EventArgs e)
        {
            SetWallpaper(WallpaperFade.Source ?? Wallpaper.Source, false);
        }

        // The theme's other wallpapers decoded while the user is quiet, one a
        // frame, so moving to their sections shows them at once.
        private void DecodeWallpapers()
        {
            if (wallpapersDecoding)
            {
                return;
            }
            wallpapersDecoding = true;
            UiIdle.Run(() =>
            {
                bool more = SectionHost.Content != null && Context.Themes.DecodeNext();
                wallpapersDecoding = more;
                return more;
            }, WallpaperQuietMs);
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
