using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using Microsoft.Xbox.Controls;
using Silverdash360.Controls;
using Silverdash360.Core;
using Silverdash360.Services.Media;

namespace Silverdash360.Pages
{
    /// <summary>
    /// Shows the pictures queued in <see cref="MediaService"/>: left and right
    /// go through them, A starts or stops a slideshow, B goes back. Pictures
    /// are fetched by URL like the player's media.
    /// </summary>
    public partial class PicturePage : XboxApplicationPage
    {
        // Silverlight decodes a picture whole, at full size, into the
        // console's small memory: larger files aren't tried.
        private const long MaxBytes = 24L << 20;
        private static readonly TimeSpan SlideTime = TimeSpan.FromSeconds(5);

        private readonly DispatcherTimer slideshow = new DispatcherTimer { Interval = SlideTime };
        private bool retried;

        public PicturePage()
        {
            InitializeComponent();
            Motion.UsePageTransitions(this);
            slideshow.Tick += delegate { Step(1); };
            GamePadButtonDown += OnButtonDown;
            Picture.ImageFailed += delegate(object sender, ExceptionRoutedEventArgs e)
            {
                MediaEntry entry = Media.Current;
                Log.Write("Picture failed: {0}: {1}", entry, e.ErrorException != null ? e.ErrorException.Message : "");
                if (!retried && entry != null && entry.Url == null && Media.UseConsoleAddress())
                {
                    retried = true;
                    Load(entry);
                    return;
                }
                StatusText.Text = "This picture can't be shown.   " + Legend();
            };
        }

        private static MediaService Media { get { return App.Context.Media; } }

        /// <summary>The page while it's shown, for the debug server.</summary>
        public static PicturePage Current { get; private set; }

        /// <summary>The picture's size once decoded, or empty.</summary>
        public Size PictureSize
        {
            get
            {
                BitmapImage image = Picture.Source as BitmapImage;
                return image != null ? new Size(image.PixelWidth, image.PixelHeight) : new Size();
            }
        }

        public string Status { get { return StatusText.Text; } }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Current = this;
            Show();
            FocusHelper.Focus(Holder);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (Current == this)
            {
                Current = null;
            }
            slideshow.Stop();
            Picture.Source = null;
        }

        private void OnButtonDown(object sender, GamePadButtonEventArgs e)
        {
            switch (e.Button)
            {
                case GamePadButton.DPadLeft:
                    Step(-1);
                    e.Handled = true;
                    break;
                case GamePadButton.DPadRight:
                    Step(1);
                    e.Handled = true;
                    break;
                case GamePadButton.A:
                    if (slideshow.IsEnabled)
                    {
                        slideshow.Stop();
                    }
                    else
                    {
                        slideshow.Start();
                    }
                    StatusText.Text = Legend();
                    e.Handled = true;
                    break;
            }
        }

        public void Step(int step)
        {
            if (Media.Queue == null || Media.Queue.Count == 0)
            {
                return;
            }
            Media.Position = (Media.Position + step + Media.Queue.Count) % Media.Queue.Count;
            Show();
        }

        private string Legend()
        {
            return string.Format("{0} of {1}   LEFT RIGHT next   A {2} slideshow   B back", Media.Position + 1,
                                 Media.Queue != null ? Media.Queue.Count : 0, slideshow.IsEnabled ? "stop" : "start");
        }

        private void Show()
        {
            MediaEntry entry = Media.Current;
            if (entry == null)
            {
                StatusText.Text = "Nothing to show.";
                return;
            }
            NameText.Text = entry.Name;
            if (entry.Size > MaxBytes)
            {
                Picture.Source = null;
                StatusText.Text = "It's too large to show.   " + Legend();
                return;
            }
            retried = false;
            Load(entry);
        }

        private void Load(MediaEntry entry)
        {
            StatusText.Text = Legend();
            try
            {
                BitmapImage image = new BitmapImage();
                image.UriSource = entry.Source.Locate(entry);
                Picture.Source = image;
            }
            catch (Exception e)
            {
                Picture.Source = null;
                StatusText.Text = "This picture can't be shown: " + e.Message + "   " + Legend();
            }
        }
    }
}
