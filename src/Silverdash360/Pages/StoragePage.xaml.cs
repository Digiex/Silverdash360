using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using Microsoft.Xbox.Controls;
using Silverdash360.Controls;
using Silverdash360.Model;
using Silverdash360.Services;

namespace Silverdash360.Pages
{
    /// <summary>
    /// Everything stored on the console, largest first, filtered by kind with
    /// LB/RB, like the console's system storage screen.
    /// </summary>
    public partial class StoragePage : XboxApplicationPage
    {
        private static int lastFilter;

        private sealed class Filter
        {
            public string Name;
            public Func<ContentItem, bool> Matches;
        }

        private readonly List<Filter> filters = new List<Filter>
        {
            new Filter { Name = "all", Matches = c => true },
            new Filter { Name = "games", Matches = c => ContentType.IsLaunchable(c.Type) && Kind(c) == TitleKind.Game },
            new Filter { Name = "apps", Matches = c => ContentType.IsLaunchable(c.Type) && Kind(c) == TitleKind.App },
            new Filter { Name = "add-ons", Matches = c => c.Type == ContentType.MarketplaceContent },
            new Filter { Name = "updates", Matches = c => c.Type == ContentType.Installer },
            new Filter { Name = "saves", Matches = c => c.Type == ContentType.SavedGame },
            new Filter { Name = "media", Matches = c => c.Type >= ContentType.Video && c.Type != ContentType.Installer && !ContentType.IsLaunchable(c.Type) },
        };

        private readonly List<TextBlock> filterHeaders = new List<TextBlock>();
        private int filter;

        public StoragePage()
        {
            InitializeComponent();
            Items.Direction = ScrollDirection.Vertical;
            Items.Lines = 1;
            Items.TileWidth = 0;
            Items.TileHeight = 72;
            Items.Spacing = 8;
            Items.TileTemplate = new RowTemplate(o => Row((ContentItem)o));
            Items.ItemInvoked += delegate(object sender, ItemEventArgs e)
            {
                ContentItem item = (ContentItem)e.Item;
                if (App.Context.Library.Find(item.TitleId) != null)
                {
                    App.Context.OpenTitle(App.Context.Library.Find(item.TitleId));
                }
            };
            Items.SelectionChanged += delegate
            {
                ContentItem item = Items.SelectedItem as ContentItem;
                StatusText.Text = item != null ? item.TitleId.ToString("X8") + "  ·  " + item.FileName : "";
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

        private static TitleKind Kind(ContentItem item)
        {
            TitleEntry title = App.Context.Library.Find(item.TitleId);
            return title != null ? title.Kind : TitleKind.Other;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ShowFilter(lastFilter);
            Dispatcher.BeginInvoke(() => Items.Focus());
        }

        private void ShowFilter(int index)
        {
            filter = lastFilter = index;
            for (int i = 0; i < filterHeaders.Count; i++)
            {
                filterHeaders[i].Foreground = i == index ? Theme.Text : Theme.SubText;
                filterHeaders[i].FontWeight = i == index ? FontWeights.Bold : FontWeights.Normal;
            }
            List<ContentItem> items = App.Context.Library.AllContent.Where(filters[index].Matches)
                                         .OrderByDescending(c => c.BestSize).ToList();
            long total = items.Sum(c => c.BestSize);
            TotalText.Text = string.Format("{0} in {1} {2}", total > 0 ? SystemService.FormatSize(total) : "0 B",
                                           items.Count, items.Count == 1 ? "item" : "items");
            Items.EmptyText = "Nothing here.";
            Items.Items = items.Cast<object>().ToList();
        }

        private static RowData Row(ContentItem item)
        {
            TitleEntry title = App.Context.Library.Find(item.TitleId);
            string name = item.DisplayName.Length > 0 ? item.DisplayName : item.FileName;
            return new RowData
            {
                Title = name,
                Subtitle = item.TypeName + (title != null && title.Name != name ? "  ·  " + title.Name : ""),
                Detail = SystemService.FormatSize(item.BestSize),
                ArtSeed = item.TitleId,
                Art = done => App.Context.Images.ContentArt(item, done),
            };
        }
    }
}
