using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Silverdash360.Controls;
#if DEBUG_SERVER
using Silverdash360.Debugging;
#endif
using Silverdash360.Model;
using Silverdash360.Services;
using Silverdash360.Services.Ftp;

namespace Silverdash360.Modules
{
    /// <summary>Console information and system actions.</summary>
    public sealed class SettingsModule : DashboardModule
    {
        private TileGrid list;

        public SettingsModule(DashboardContext context) : base(context)
        {
        }

        public override string Header { get { return "settings"; } }
        public override int Order { get { return 50; } }

        private sealed class Entry
        {
            public string Title;
            public string Detail;
            public Action Run;
        }

        protected override FrameworkElement CreateView()
        {
            list = new TileGrid
            {
                Direction = ScrollDirection.Vertical,
                Lines = 2,
                TileWidth = 566,
                TileHeight = 64,
                Spacing = 10,
                TileTemplate = new RowTemplate(o =>
                {
                    Entry e = (Entry)o;
                    return new RowData { Title = e.Title, Detail = e.Detail, Dimmed = false };
                }) { IconSize = 0 },
            };
            ForwardEdges(list, Edge.Left, Edge.Right);
            list.ItemInvoked += delegate(object sender, ItemEventArgs e)
            {
                Entry entry = (Entry)e.Item;
                if (entry.Run != null)
                {
                    entry.Run();
                }
            };
            return list;
        }

        public override void Activate()
        {
            Focus(list);
        }

        public override void Refresh()
        {
            if (list == null)
            {
                return;
            }
            SystemService system = Context.System;
            Library library = Context.Library;
            List<ContentItem> content = library.AllContent.ToList();
            List<Entry> entries = new List<Entry>
            {
                new Entry { Title = "Theme", Detail = Context.Themes.Describe(), Run = Context.OpenThemes },
                new Entry { Title = "Storage", Detail = SystemService.FormatSize(content.Sum(c => c.BestSize)) + " in " + content.Count + " items", Run = Context.OpenStorage },
                new Entry { Title = "Search", Detail = "Find a game or app", Run = Context.OpenSearch },
                new Entry { Title = "File browser", Detail = "Every file on the console's drives", Run = () => Context.OpenFiles("/") },
                new Entry { Title = "FTP server", Detail = FtpDetail(), Run = ToggleFtp },
                new Entry { Title = "Refresh library", Detail = library.IsLoading ? "Reading..." : "Read every package again", Run = () => library.Refresh(true) },
                new Entry { Title = "Sign in", Detail = library.Gamer != null ? "Signed in as " + library.Gamer.Gamertag : "Not signed in", Run = Context.Profiles.ShowSignin },
                new Entry { Title = "System version", Detail = Safe(() => system.SystemVersion) },
                new Entry { Title = "Language", Detail = Safe(() => system.Language) },
                new Entry { Title = "Console time", Detail = Safe(() => system.ConsoleTime.ToString("g")) },
                new Entry { Title = "Running as title", Detail = Safe(() => system.CurrentTitleId) },
                new Entry { Title = "Running from", Detail = Safe(() => AppFolder.Path(Context.Ftp.Files) ?? AppFolder.DevicePath() ?? "unknown") },
                new Entry { Title = "About Silverdash360", Detail = "github.com/Digiex/Silverdash360" },
                new Entry { Title = "Built with Silverlight360", Detail = "github.com/Digiex/Silverlight360" },
                new Entry { Title = "Return to the Xbox Dashboard", Detail = "Exit Silverdash", Run = system.ExitToDashboard },
            };
#if DEBUG_SERVER
            entries.Insert(3, new Entry { Title = "Debug server", Detail = DebugDetail() });
#endif
            int selected = Math.Max(0, list.SelectedIndex);
            list.Items = entries.Cast<object>().ToList();
            list.SelectedIndex = selected;
        }

        private string FtpDetail()
        {
            FtpServer ftp = Context.Ftp;
            if (!ftp.IsRunning)
            {
                return "Off. Press A to share the console's drives.";
            }
            return string.Format("ftp://{0}:{1}  user {2}  password {3}", ftp.Address ?? "this console",
                                 ftp.ListeningPort, FtpServer.UserName, ftp.Password);
        }

#if DEBUG_SERVER
        private string DebugDetail()
        {
            DebugServer server = DebugServer.Current;
            if (server == null || !server.IsRunning)
            {
                return "Not running";
            }
            return string.Format("{0}:{1}, {2} connected. A development build.", Context.Ftp.Address ?? "this console",
                                 server.ListeningPort, server.ClientCount);
        }

#endif
        private void ToggleFtp()
        {
            try
            {
                if (Context.Ftp.IsRunning)
                {
                    Context.Ftp.Stop();
                }
                else
                {
                    Context.Ftp.Start();
                }
            }
            catch (Exception e)
            {
                Context.ShowStatus("The FTP server didn't start: " + e.Message);
            }
            Refresh();
        }

        private static string Safe(Func<string> read)
        {
            try
            {
                return read();
            }
            catch (Exception e)
            {
                return "unavailable (" + e.GetType().Name + ")";
            }
        }
    }
}
