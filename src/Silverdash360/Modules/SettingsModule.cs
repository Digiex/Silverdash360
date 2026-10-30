using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Silverdash360.Controls;
using Silverdash360.Model;
using Silverdash360.Services;

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
                new Entry { Title = "Storage", Detail = SystemService.FormatSize(content.Sum(c => c.BestSize)) + " in " + content.Count + " items", Run = Context.OpenStorage },
                new Entry { Title = "Search", Detail = "Find a game or app", Run = Context.OpenSearch },
                new Entry { Title = "Refresh library", Detail = library.IsLoading ? "Reading..." : "Read the console's content again", Run = library.Refresh },
                new Entry { Title = "Sign in", Detail = library.Gamer != null ? "Signed in as " + library.Gamer.Gamertag : "Not signed in", Run = Context.Profiles.ShowSignin },
                new Entry { Title = "System version", Detail = Safe(() => system.SystemVersion) },
                new Entry { Title = "Language", Detail = Safe(() => system.Language) },
                new Entry { Title = "Console time", Detail = Safe(() => system.ConsoleTime.ToString("g")) },
                new Entry { Title = "Running as title", Detail = Safe(() => system.CurrentTitleId) },
                new Entry { Title = "About Silverdash360", Detail = "github.com/Digiex/Silverdash360" },
                new Entry { Title = "Built with Silverlight360", Detail = "github.com/Digiex/Silverlight360" },
                new Entry { Title = "Return to the Xbox Dashboard", Detail = "Exit Silverdash", Run = system.ExitToDashboard },
            };
            int selected = Math.Max(0, list.SelectedIndex);
            list.Items = entries.Cast<object>().ToList();
            list.SelectedIndex = selected;
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
