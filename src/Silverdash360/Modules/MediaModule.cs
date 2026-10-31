using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Silverdash360.Controls;
using Silverdash360.Core;
using Silverdash360.Services.Media;

namespace Silverdash360.Modules
{
    /// <summary>
    /// Music, videos and pictures: the disc, the drives and the media servers
    /// on the network, like the console's media section.
    /// </summary>
    public sealed class MediaModule : DashboardModule
    {
        private TileGrid list;
        private bool searched;
        private List<IMediaSource> drives = new List<IMediaSource>();
        private DiscKind disc;

        public MediaModule(DashboardContext context) : base(context)
        {
            context.Media.ServersChanged += delegate { Show(); };
        }

        public override string Header { get { return "media"; } }
        public override int Order { get { return 35; } }

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
                TileHeight = 72,
                Spacing = 10,
                TileTemplate = new RowTemplate(o =>
                {
                    Entry e = (Entry)o;
                    return new RowData { Title = e.Title, Subtitle = e.Detail, ArtSeed = (uint)e.Title.GetHashCode(), Dimmed = e.Run == null };
                }) { IconSize = 52 },
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
            // A disc or a USB drive may have come or gone since.
            Refresh();
            if (!searched)
            {
                searched = true;
                Context.Media.Search();
            }
        }

        /// <summary>Reads the drives and the disc again, off the UI thread: a disc can take a while to spin up.</summary>
        public override void Refresh()
        {
            Background.Run(() =>
            {
                List<IMediaSource> found = Context.Media.Drives();
                DiscKind kind = Context.Media.Disc();
                return new KeyValuePair<List<IMediaSource>, DiscKind>(found, kind);
            }, delegate(KeyValuePair<List<IMediaSource>, DiscKind> result, Exception error)
            {
                if (error == null)
                {
                    drives = result.Key;
                    disc = result.Value;
                }
                Show();
            });
        }

        private void Show()
        {
            if (list == null)
            {
                return;
            }
            MediaService media = Context.Media;
            List<Entry> entries = new List<Entry>();
            string discText = DiscText(disc);
            if (discText != null)
            {
                entries.Add(new Entry { Title = "Play disc", Detail = discText, Run = PlayDisc });
            }
            foreach (IMediaSource drive in drives)
            {
                IMediaSource source = drive;
                entries.Add(new Entry { Title = drive.Name, Detail = "Music, videos and pictures", Run = () => Context.OpenMedia(source, null) });
            }
            int videos = media.Downloads != null ? media.Downloads.Packages().Count : 0;
            if (videos > 0)
            {
                IMediaSource source = media.Downloads;
                entries.Add(new Entry
                {
                    Title = source.Name,
                    Detail = videos + (videos == 1 ? " video" : " videos") + " from Xbox Live",
                    Run = () => Context.OpenMedia(source, null),
                });
            }
            foreach (DlnaMediaSource server in media.Servers)
            {
                IMediaSource source = server;
                entries.Add(new Entry { Title = server.Name, Detail = server.Description, Run = () => Context.OpenMedia(source, null) });
            }
            entries.Add(media.IsSearching
                ? new Entry { Title = "Looking for media servers...", Detail = "Windows Media Player, Plex, Kodi and other DLNA servers" }
                : new Entry
                {
                    Title = "Look for media servers",
                    Detail = media.Servers.Count == 0 ? "None found on the network" : media.Servers.Count + " found",
                    Run = media.Search,
                });
            int selected = Math.Max(0, list.SelectedIndex);
            list.Items = entries.Cast<object>().ToList();
            list.SelectedIndex = Math.Min(selected, entries.Count - 1);
        }

        private static string DiscText(DiscKind kind)
        {
            switch (kind)
            {
                case DiscKind.Video: return "DVD movie";
                case DiscKind.Game: return "Xbox 360 game";
                case DiscKind.OriginalXboxGame: return "Xbox game";
                default: return null;
            }
        }

        private void PlayDisc()
        {
            Context.ShowStatus("Starting the disc...");
            string error = Context.Media.PlayDisc();
            if (error != null)
            {
                Context.ShowStatus(error);
            }
        }
    }
}
