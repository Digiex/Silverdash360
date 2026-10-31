using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using Microsoft.Xbox.Controls;
using Silverdash360.Controls;
using Silverdash360.Core;
using Silverdash360.Services.Media;

namespace Silverdash360.Pages
{
    /// <summary>
    /// Plays the music or video queued in <see cref="MediaService"/>, one
    /// entry after another. MediaTransport handles the controller (A pauses,
    /// the triggers wind, B leaves); X and Y go to the previous and next
    /// entry. The player fetches the media by URL: from a media server, or
    /// from the dashboard's own server for a file on a drive.
    /// </summary>
    public partial class PlayerPage : XboxApplicationPage
    {
        private readonly Storyboard fadeInfo = new Storyboard();
        private bool retried;

        public PlayerPage()
        {
            InitializeComponent();
            Motion.UsePageTransitions(this);
            DoubleAnimation fade = new DoubleAnimation
            {
                BeginTime = TimeSpan.FromSeconds(4),
                To = 0,
                Duration = TimeSpan.FromMilliseconds(600),
            };
            Storyboard.SetTarget(fade, Info);
            Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            fadeInfo.Children.Add(fade);
            Player.MediaEnded += delegate { Next(1, false); };
            Player.MediaFailed += delegate(object sender, ExceptionRoutedEventArgs e)
            {
                Log.Write("Media failed: {0} from {1}: {2}", Media.Current, Player.Source,
                          e.ErrorException != null ? e.ErrorException.Message : "");
                // A file from a drive, which loopback may not have reached:
                // once more, through the console's own address.
                MediaEntry entry = Media.Current;
                if (!retried && entry != null && entry.Url == null && Media.UseConsoleAddress())
                {
                    retried = true;
                    Play(entry);
                    return;
                }
                ShowError("This can't be played" + (e.ErrorException != null ? ": " + e.ErrorException.Message : "."));
            };
            Player.CurrentStateChanged += delegate { Log.Write("Player: {0} is {1}", Media.Current, Player.CurrentState); };
            // X and Y aren't transport buttons, so they come up to the page.
            GamePadButtonDown += OnButtonDown;
        }

        /// <summary>The page while it's shown, for the debug server.</summary>
        public static PlayerPage Current { get; private set; }

        private static MediaService Media { get { return App.Context.Media; } }

        public MediaElement Element { get { return Player; } }

        /// <summary>Why the current entry didn't play, or null.</summary>
        public string Error { get { return ErrorText.Text.Length > 0 ? ErrorText.Text : null; } }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Current = this;
            PlayCurrent();
            Dispatcher.BeginInvoke(delegate { Transport.Focus(); });
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (Current == this)
            {
                Current = null;
            }
            Player.Stop();
            Player.Source = null;
        }

        private void OnButtonDown(object sender, GamePadButtonEventArgs e)
        {
            if (e.Button == GamePadButton.X)
            {
                Next(-1, true);
                e.Handled = true;
            }
            else if (e.Button == GamePadButton.Y)
            {
                Next(1, true);
                e.Handled = true;
            }
        }

        /// <summary>Moves along the queue; at its end, stops there unless asked to wrap.</summary>
        public void Next(int step, bool wrap)
        {
            if (Media.Queue == null || Media.Queue.Count == 0)
            {
                return;
            }
            int next = Media.Position + step;
            if (next < 0 || next >= Media.Queue.Count)
            {
                if (!wrap)
                {
                    QueueText.Text = "End of the queue.   X previous   Y next";
                    return;
                }
                next = (next + Media.Queue.Count) % Media.Queue.Count;
            }
            Media.Position = next;
            PlayCurrent();
        }

        private void PlayCurrent()
        {
            MediaEntry entry = Media.Current;
            if (entry == null)
            {
                ShowError("Nothing to play.");
                return;
            }
            bool music = entry.Kind == MediaKind.Music;
            KindText.Text = music ? "music" : "video";
            TitleText.Text = entry.Name;
            ArtistText.Text = string.Join("  ·  ", new[] { entry.Artist, entry.Album }.Where(s => !string.IsNullOrEmpty(s)).ToArray());
            QueueText.Text = string.Format("{0} of {1}   X previous   Y next", Media.Position + 1, Media.Queue.Count);
            ErrorText.Text = "";
            // Music keeps its details on screen; a video's fade away.
            fadeInfo.Stop();
            Info.Opacity = 1;
            Info.VerticalAlignment = music ? VerticalAlignment.Center : VerticalAlignment.Top;
            if (!music)
            {
                fadeInfo.Begin();
            }
            Transport.MediaAssetId = entry.Source.Key + "|" + entry.Id;
            retried = false;
            Play(entry);
        }

        private void Play(MediaEntry entry)
        {
            Player.Stop();
            try
            {
                Player.Source = entry.Source.Locate(entry);
            }
            catch (Exception e)
            {
                ShowError("Couldn't open it: " + e.Message);
            }
        }

        private void ShowError(string text)
        {
            fadeInfo.Stop();
            Info.Opacity = 1;
            ErrorText.Text = text;
        }

        private void Transport_Back(object sender, EventArgs e)
        {
            if (NavigationService.CanGoBack)
            {
                NavigationService.GoBack();
            }
        }
    }
}
