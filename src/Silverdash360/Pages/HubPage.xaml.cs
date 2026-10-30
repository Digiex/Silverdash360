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

        private readonly DispatcherTimer clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        private readonly List<TextBlock> headers = new List<TextBlock>();

        public HubPage()
        {
            InitializeComponent();
            clock.Tick += delegate { ShowClock(); };
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
            SectionHost.Content = null;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (SectionHost.Content != null)
            {
                Modules[current].Activate();
            }
        }

        private void OnLibraryChanged(object sender, EventArgs e)
        {
            foreach (IDashboardModule module in Modules)
            {
                module.Refresh();
            }
            ShowGamer();
            Modules[current].Activate();
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

        private void OnButtonDown(object sender, GamePadButtonEventArgs e)
        {
            if (e.Button == GamePadButton.LeftShoulder)
            {
                Show((current + Modules.Count - 1) % Modules.Count, -1);
                e.Handled = true;
            }
            else if (e.Button == GamePadButton.RightShoulder)
            {
                Show((current + 1) % Modules.Count, 1);
                e.Handled = true;
            }
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
            ContentControl oldHost = view.Parent as ContentControl;
            if (oldHost != null && oldHost != SectionHost)
            {
                oldHost.Content = null;
            }
            SectionHost.Content = view;
            LegendText.Text = module.Legend + "   LB RB sections";
            StatusText.Text = Context.Library.IsLoading ? "Reading the console's content..." : "";
            if (direction != 0)
            {
                Slide(direction);
            }
            module.Activate();
        }

        private void Slide(int direction)
        {
            Storyboard story = new Storyboard();
            DoubleAnimation slide = new DoubleAnimation
            {
                From = 60 * direction,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(slide, SectionSlide);
            Storyboard.SetTargetProperty(slide, new PropertyPath("X"));
            DoubleAnimation fade = new DoubleAnimation { From = 0.2, To = 1, Duration = TimeSpan.FromMilliseconds(220) };
            Storyboard.SetTarget(fade, SectionHost);
            Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            story.Children.Add(slide);
            story.Children.Add(fade);
            story.Begin();
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
