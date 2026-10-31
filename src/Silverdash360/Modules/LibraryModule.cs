using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Silverdash360.Controls;
using Silverdash360.Model;
using Silverdash360.Services;

namespace Silverdash360.Modules
{
    /// <summary>
    /// A shelf of titles (games or apps) with a cover view and a list view.
    /// A opens a title's details, X searches, Y switches the view, Back
    /// opens the shelf's filters and order, and Start starts the selected
    /// title straight away. The view and filters are remembered.
    /// </summary>
    public sealed class LibraryModule : DashboardModule
    {
        private readonly string header;
        private readonly int order;
        private readonly Func<Library, IEnumerable<TitleEntry>> select;
        private readonly string emptyText;
        private readonly ShelfOptions options;
        private TileGrid grid;
        private bool listView;
        private TitleEntry lastSelected;

        public LibraryModule(DashboardContext context, string header, int order,
                             Func<Library, IEnumerable<TitleEntry>> select, string emptyText)
            : base(context)
        {
            this.header = header;
            this.order = order;
            this.select = select;
            this.emptyText = emptyText;
            options = new ShelfOptions(header);
            Settings.Loaded += delegate { OptionsChanged(); };
        }

        /// <summary>The shelf's filters, order and view.</summary>
        public ShelfOptions Options { get { return options; } }

        /// <summary>Every title the shelf has, before its filters.</summary>
        public IList<TitleEntry> AllTitles { get { return select(Context.Library).ToList(); } }

        /// <summary>Shows the shelf again after its options changed.</summary>
        public void OptionsChanged()
        {
            if (grid == null)
            {
                return;
            }
            if (listView != options.ListView)
            {
                ApplyView();
            }
            Refresh();
        }

        public override string Header { get { return header; } }
        public override int Order { get { return order; } }

        public override string Legend
        {
            get { return "A details   START play   X search   Y " + (listView ? "covers" : "list") + "   BACK filter"; }
        }

        protected override FrameworkElement CreateView()
        {
            grid = new TileGrid();
            ApplyView();
            ForwardEdges(grid, Edge.Left, Edge.Right);
            grid.ItemInvoked += delegate(object sender, ItemEventArgs e)
            {
                TitleEntry title = (TitleEntry)e.Item;
                if (options.APlays)
                {
                    Context.Play(title);
                }
                else
                {
                    Context.OpenTitle(title);
                }
            };
            grid.SelectionChanged += delegate
            {
                lastSelected = grid.SelectedItem as TitleEntry;
                if (lastSelected != null)
                {
                    Context.ShowStatus(Describe(lastSelected));
                }
            };
            grid.ItemButton += delegate(object sender, ItemEventArgs e)
            {
                switch (e.Button)
                {
                    case GamePadButton.X:
                        Context.OpenSearch();
                        e.Handled = true;
                        break;
                    case GamePadButton.Y:
                        options.ListView = !listView;
                        ApplyView();
                        Refresh();
                        Focus(grid);
                        e.Handled = true;
                        break;
                    case GamePadButton.Back:
                        Context.OpenShelfOptions(header);
                        e.Handled = true;
                        break;
                    case GamePadButton.Start:
                        if (e.Item != null && options.APlays)
                        {
                            Context.ShowDetails((TitleEntry)e.Item);
                        }
                        else if (e.Item != null)
                        {
                            Context.Launch((TitleEntry)e.Item);
                        }
                        e.Handled = true;
                        break;
                }
            };
            return grid;
        }

        private void ApplyView()
        {
            listView = options.ListView;
            if (listView)
            {
                grid.Direction = ScrollDirection.Vertical;
                grid.Lines = 2;
                grid.TileWidth = 560;
                grid.TileHeight = 84;
                grid.Spacing = 12;
                grid.LeadTiles = 1;
                grid.PadOutside = 6;
                grid.Margin = new Thickness(0);
                grid.TileTemplate = new RowTemplate(o => Row((TitleEntry)o));
            }
            else
            {
                grid.Direction = ScrollDirection.Horizontal;
                grid.Lines = 2;
                grid.TileWidth = 176;
                grid.TileHeight = 232;
                grid.Spacing = 18;
                grid.LeadTiles = 1;
                grid.PadOutside = 24;
                grid.Margin = Theme.GrowRoom;
                grid.TileTemplate = new CoverTileTemplate(Context.Images, () => Context.UserIndex);
            }
        }

        private RowData Row(TitleEntry title)
        {
            return new RowData
            {
                Title = title.Name,
                Subtitle = Subtitle(title),
                Detail = SystemService.FormatSize(title.TotalSize),
                Progress = title.Played != null && title.Played.GamerscoreTotal > 0 ? title.Played.Progress : (double?)null,
                ArtSeed = title.TitleId,
                Art = done => Context.Images.TitleArt(title, Context.UserIndex, done),
                Dimmed = !title.IsInstalled,
            };
        }

        private static string Subtitle(TitleEntry title)
        {
            string text = title.Publisher;
            string state = CoverTileTemplate.Subtitle(title);
            return string.IsNullOrEmpty(text) ? state : text + "  ·  " + state;
        }

        private static string Describe(TitleEntry title)
        {
            string text = title.Name;
            if (title.LastPlayed.HasValue)
            {
                text += "  ·  last played " + title.LastPlayed.Value.ToString("d");
            }
            return text;
        }

        public override void Activate()
        {
            Focus(grid);
        }

        public override void Refresh()
        {
            if (grid == null)
            {
                return;
            }
            grid.EmptyText = !Context.Library.IsLoaded ? "Looking for content..."
                : options.IsFiltered ? "Nothing here matches the filter. BACK changes it." : emptyText;
            List<object> titles = options.Apply(select(Context.Library)).Cast<object>().ToList();
            grid.Items = titles;
            if (lastSelected != null)
            {
                TitleEntry same = titles.Cast<TitleEntry>().FirstOrDefault(t => t.TitleId == lastSelected.TitleId);
                if (same != null)
                {
                    grid.SelectItem(same);
                }
            }
        }
    }
}
