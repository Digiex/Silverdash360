using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Xbox.Controls;
using Silverdash360.Controls;
using Silverdash360.Core;
#if DEBUG_SERVER
using Silverdash360.Debugging;
#endif
using Silverdash360.Modules;
using Silverdash360.Services;
using Silverdash360.Services.Files;
using Silverdash360.Services.Ftp;
using Silverdash360.Services.Media;

namespace Silverdash360
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        /// <summary>The services and navigation the sections and pages share.</summary>
        public static DashboardContext Context { get; private set; }

        /// <summary>The hub's sections, kept for the whole run.</summary>
        public static IList<IDashboardModule> Modules { get; private set; }

        /// <summary>The frame the pages are shown in.</summary>
        public static XboxApplicationFrame Frame { get; private set; }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            LogFile.Start();
            Log.Write("Starting");
#if DEBUG_SERVER
            if (StartPendingInstall())
            {
                return;
            }
#endif
            StartDashboard(null);
        }

        private MarketplaceArtCache artCache;

        // In host when given, else as the root visual.
        private void StartDashboard(Panel host)
        {
            ProfileService profiles = new ProfileService();
            XamContentSource content = new XamContentSource();
            XboxFileSystem files = new XboxFileSystem();
            Library library = new Library(content, profiles, new LibraryCache(), new ExecutableScanner(files));
            artCache = new MarketplaceArtCache(library);
            MediaService media = new MediaService(files);
            media.Downloads = new DownloadedVideoSource(library, files, media.LocalServer);
            Context = new DashboardContext(library, profiles, new ImageService(content), new SystemService(),
                                           new FtpServer(files), media, new ThemeService(library));
            Context.Ftp.Message += text => Background.OnUi(() => Context.ShowStatus(text));
            Modules = ModuleRegistry.CreateAll(Context);

            // XboxApplicationFrame also starts GamerServices and plays the
            // page transitions.
            XboxApplicationFrame frame = new XboxApplicationFrame();
            Frame = frame;
            Context.Navigate = uri => frame.Navigate(uri);
            frame.Source = new Uri("/Pages/HubPage.xaml", UriKind.Relative);
            frame.GamePadButtonDown += delegate(object s, GamePadButtonEventArgs args)
            {
                // B goes back. The hub is the first page and stays.
                if (args.Button == GamePadButton.B && frame.CanGoBack)
                {
                    frame.GoBack();
                    args.Handled = true;
                }
            };
            if (host != null)
            {
                host.Children.Clear();
                host.Children.Add(frame);
            }
            else
            {
                RootVisual = frame;
            }
            Settings.Load();
            Context.Library.Refresh();
#if DEBUG_SERVER
            // On by default for now; see the DebugServer build property.
            DebugServer.StartDefault(Context);
            Heartbeat.Start();
#endif
        }

#if DEBUG_SERVER
        // A build tools/deploy.py staged: copies it into the app's folder and
        // starts it from there. On a failure the dashboard starts here, so
        // the debug server can tell what went wrong.
        private bool StartPendingInstall()
        {
            SelfInstall.Plan plan;
            try
            {
                plan = SelfInstall.Pending();
            }
            catch (Exception e)
            {
                Log.Write("Install marker unreadable: {0}", e.Message);
                return false;
            }
            if (plan == null)
            {
                SelfInstall.ClearStarted();
                return false;
            }
            // The root visual can be set only once: a panel the dashboard can
            // go into if the install fails.
            Grid root = new Grid { Background = new SolidColorBrush(Colors.Black) };
            TextBlock status = Theme.TextBlock("Installing an update...", 28, Theme.Text);
            status.HorizontalAlignment = HorizontalAlignment.Center;
            status.VerticalAlignment = VerticalAlignment.Center;
            root.Children.Add(status);
            RootVisual = root;
            new Thread(delegate()
            {
                string error = SelfInstall.Run(plan, text => Background.OnUi(() => status.Text = text));
                Background.OnUi(delegate
                {
                    StartDashboard(root);
                    Context.ShowStatus("Update failed: " + error);
                });
            }) { IsBackground = true, Name = "Install" }.Start();
            return true;
        }

#endif
        private void Application_UnhandledException(object sender, ApplicationUnhandledExceptionEventArgs e)
        {
            // A dashboard keeps running: log it and show it in the footer.
            Log.Write("Unhandled: {0}", e.ExceptionObject);
            e.Handled = true;
            if (Context != null)
            {
                Context.ShowStatus("Something went wrong: " + e.ExceptionObject.Message);
            }
        }
    }
}
