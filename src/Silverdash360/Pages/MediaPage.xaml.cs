using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using Microsoft.Xbox.Controls;
using Silverdash360.Controls;
using Silverdash360.Core;
using Silverdash360.Services;
using Silverdash360.Services.Media;

namespace Silverdash360.Pages
{
    /// <summary>
    /// A folder of a media source: its folders, then its music, videos and
    /// pictures, filtered with LB/RB. A opens a folder, or plays the media
    /// with the rest of its kind in the folder queued after it.
    /// </summary>
    public partial class MediaPage : XboxApplicationPage
    {
        private const int CachedFolders = 8;

        private static int lastFilter;
        // The last folders listed, so coming back from the player doesn't ask
        // a media server again.
        private static readonly List<KeyValuePair<string, List<MediaEntry>>> cache =
            new List<KeyValuePair<string, List<MediaEntry>>>();

        private sealed class Filter
        {
            public string Name;
            public MediaKind? Kind;
        }

        private readonly List<Filter> filters = new List<Filter>
        {
            new Filter { Name = "all" },
            new Filter { Name = "music", Kind = MediaKind.Music },
            new Filter { Name = "videos", Kind = MediaKind.Video },
            new Filter { Name = "pictures", Kind = MediaKind.Picture },
        };

        private readonly List<TextBlock> filterHeaders = new List<TextBlock>();
        private IMediaSource source;
        private MediaEntry folder;
        private List<MediaEntry> entries = new List<MediaEntry>();
        private int filter;
        private bool loading;

        public MediaPage()
        {
            InitializeComponent();
            Motion.UsePageTransitions(this);
            Items.Direction = ScrollDirection.Vertical;
            Items.Lines = 1;
            Items.TileWidth = 0;
            Items.TileHeight = 72;
            Items.Spacing = 8;
            Items.TileTemplate = new RowTemplate(o => Row((MediaEntry)o)) { IconSize = 52 };
            Items.ItemInvoked += delegate(object sender, ItemEventArgs e) { Open((MediaEntry)e.Item); };
            Items.SelectionChanged += delegate
            {
                MediaEntry entry = Items.SelectedItem as MediaEntry;
                StatusText.Text = entry == null ? "" : entry.Size > 0 ? SystemService.FormatSize(entry.Size) : "";
            };
            GamePadButtonDown += delegate(object sender, GamePadButtonEventArgs e)
            {
                if (e.Button == GamePadButton.LeftShoulder || e.Button == GamePadButton.RightShoulder)
                {
                    ShowFilter((filter + (e.Button == GamePadButton.RightShoulder ? 1 : filters.Count - 1)) % filters.Count);
                    e.Handled = true;
                }
            };
            foreach (Filter f in filters)
            {
                TextBlock header = Theme.TextBlock(f.Name, 24, Theme.SubText);
                header.Margin = new Thickness(0, 0, 28, 0);
                filterHeaders.Add(header);
                Filters.Children.Add(header);
            }
        }

        private string pageKey;
        private int backRow = -1;

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            pageKey = e.Uri.OriginalString;
            backRow = BackSelection.Arrived(pageKey);
            IDictionary<string, string> query = NavigationContext.QueryString;
            string key, id, name;
            query.TryGetValue("source", out key);
            query.TryGetValue("id", out id);
            query.TryGetValue("name", out name);
            source = key != null ? App.Context.Media.Find(key) : null;
            FocusHelper.Focus(Items);
            if (source == null)
            {
                SourceText.Text = "media";
                FolderText.Text = "Not available";
                Items.EmptyText = "This drive or media server isn't there any more.";
                Items.Items = null;
                return;
            }
            folder = id != null ? new MediaEntry { Source = source, Id = id, Name = name, Kind = MediaKind.Folder } : null;
            SourceText.Text = source.Name;
            FolderText.Text = folder != null ? folder.Name : source.Name;
            filter = lastFilter;
            Load(key + "|" + id);
            App.Context.Library.Changed += LibraryChanged;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            BackSelection.Leaving(pageKey, Items);
            App.Context.Library.Changed -= LibraryChanged;
        }

        // The downloaded videos come from the library, which may still be
        // reading the drives when the page opens.
        private void LibraryChanged(object sender, EventArgs e)
        {
            if (source is DownloadedVideoSource)
            {
                Load(null);
            }
        }

        private void Load(string cacheKey)
        {
            // Only a media server's folders are kept: the console's own are
            // quick to list again, and change as the library does.
            if (!(source is DlnaMediaSource))
            {
                cacheKey = null;
            }
            List<MediaEntry> cached = cache.Where(c => c.Key == cacheKey).Select(c => c.Value).FirstOrDefault();
            if (cached != null)
            {
                entries = cached;
                ShowFilter(filter);
                return;
            }
            loading = true;
            entries = new List<MediaEntry>();
            ShowFilter(filter);
            IMediaSource listing = source;
            MediaEntry at = folder;
            Silverdash360.Core.Background.Run(() => listing.List(at), delegate(List<MediaEntry> result, Exception error)
            {
                loading = false;
                if (error != null)
                {
                    Items.EmptyText = "Couldn't read this folder: " + error.Message;
                    Items.Items = null;
                    return;
                }
                entries = result;
                if (cacheKey != null)
                {
                    cache.Add(new KeyValuePair<string, List<MediaEntry>>(cacheKey, result));
                    if (cache.Count > CachedFolders)
                    {
                        cache.RemoveAt(0);
                    }
                }
                ShowFilter(filter);
            });
        }

        private void ShowFilter(int index)
        {
            filter = lastFilter = index;
            for (int i = 0; i < filterHeaders.Count; i++)
            {
                filterHeaders[i].Foreground = i == index ? Theme.Text : Theme.SubText;
                filterHeaders[i].FontWeight = i == index ? FontWeights.Bold : FontWeights.Normal;
            }
            MediaKind? kind = filters[index].Kind;
            List<MediaEntry> shown = entries.Where(e => e.IsFolder || kind == null || e.Kind == kind).ToList();
            Items.EmptyText = loading ? "Loading..."
                : kind == null ? "No music, videos or pictures here." : "No " + filters[index].Name + " here.";
            Items.Items = shown.Cast<object>().ToList();
            // Once the folder is listed; while loading it has no rows.
            if (!loading)
            {
                BackSelection.Restore(Items, ref backRow);
            }
        }

        private void Open(MediaEntry entry)
        {
            if (entry.IsFolder)
            {
                App.Context.OpenMedia(source, entry);
                return;
            }
            if (entry.Unplayable != null)
            {
                StatusText.Text = "The Xbox 360 can't play " + entry.Unplayable + " files.";
                return;
            }
            List<MediaEntry> shown = Items.Items.Cast<MediaEntry>().Where(e => e.Unplayable == null).ToList();
            App.Context.PlayMedia(shown, entry);
        }

        private static RowData Row(MediaEntry entry)
        {
            List<string> details = new List<string>();
            if (!entry.IsFolder)
            {
                details.Add(entry.Unplayable != null ? entry.Unplayable + ", which the Xbox 360 can't play"
                                                     : MediaFormats.NameOf(entry.Kind));
            }
            if (!string.IsNullOrEmpty(entry.Artist))
            {
                details.Add(entry.Artist);
            }
            if (!string.IsNullOrEmpty(entry.Album))
            {
                details.Add(entry.Album);
            }
            return new RowData
            {
                Title = entry.Name,
                Subtitle = entry.IsFolder ? "Folder" : string.Join("  ·  ", details.ToArray()),
                Detail = entry.Duration.HasValue ? FormatTime(entry.Duration.Value)
                    : entry.Size > 0 ? SystemService.FormatSize(entry.Size) : "",
                ArtSeed = (uint)entry.Name.GetHashCode(),
                Dimmed = entry.Unplayable != null,
                // A downloaded video's own thumbnail, as the console's dashboard shows it.
                Art = entry.Package != null ? (Action<Action<ImageSource>>)(done => App.Context.Images.ContentArt(entry.Package, done)) : null,
            };
        }

        public static string FormatTime(TimeSpan time)
        {
            return time.TotalHours >= 1
                ? string.Format("{0}:{1:00}:{2:00}", (int)time.TotalHours, time.Minutes, time.Seconds)
                : string.Format("{0}:{1:00}", time.Minutes, time.Seconds);
        }
    }
}
