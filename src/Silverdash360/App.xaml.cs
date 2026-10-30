using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Microsoft.Xbox.Controls;
using Silverdash360.Core;
using Silverdash360.Modules;
using Silverdash360.Services;
using Silverdash360.Services.Files;
using Silverdash360.Services.Ftp;

namespace Silverdash360
{
    public partial class App : Application
    {
        private XboxApplicationFrame frame;

        public App()
        {
            InitializeComponent();
        }

        /// <summary>The services and navigation the sections and pages share.</summary>
        public static DashboardContext Context { get; private set; }

        /// <summary>The hub's sections, kept for the whole run.</summary>
        public static IList<IDashboardModule> Modules { get; private set; }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            Log.Write("Starting");
            ProfileService profiles = new ProfileService();
            XamContentSource content = new XamContentSource();
            XboxFileSystem files = new XboxFileSystem();
            Context = new DashboardContext(new Library(content, profiles, new LibraryCache(), new ExecutableScanner(files)),
                                           profiles, new ImageService(content), new SystemService(), new FtpServer(files));
            Context.Ftp.Message += text => Background.OnUi(() => Context.ShowStatus(text));
            Modules = ModuleRegistry.CreateAll(Context);

            // XboxApplicationFrame also starts GamerServices and plays the
            // page transitions.
            frame = new XboxApplicationFrame();
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
            RootVisual = frame;
            Context.Library.Refresh();
        }

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
