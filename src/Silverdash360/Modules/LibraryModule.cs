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
    /// title straight away. The view and filters are remembered. Sorted by
    /// name, a long shelf has each letter head its titles; A on a letter
    /// shows the letters alone, to jump through it, and A on one again shows
    /// all.
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
        private string lastLetter;
        private bool lettersOnly;

        // Letters head the titles only in a shelf at least this many screens
        // long: in a shorter one they take room and save little scrolling.
        private const int LetterScreens = 4;

        // A letter heading the titles that start with it; "#" for digits and
        // symbols.
        private sealed class LetterGroup
        {
            public string Letter;
            public int Count;
        }

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
            get
            {
                string a = grid != null && grid.SelectedItem is LetterGroup
                    ? (lettersOnly ? "A show " + header : "A letters only")
                    : options.APlays ? "A play   START details" : "A details   START play";
                return a + "   X search   Y " + (listView ? "covers" : "list") + "   BACK options";
            }
        }

        protected override FrameworkElement CreateView()
        {
            grid = new TileGrid();
            ApplyView();
            ForwardEdges(grid, Edge.Left, Edge.Right);
            grid.ItemInvoked += delegate(object sender, ItemEventArgs e)
            {
                LetterGroup group = e.Item as LetterGroup;
                if (group != null)
                {
                    ToggleLetters(group);
                    return;
                }
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
                bool wasLetter = lastLetter != null;
                LetterGroup group = grid.SelectedItem as LetterGroup;
                lastSelected = grid.SelectedItem as TitleEntry;
                lastLetter = group != null ? group.Letter : null;
                if (lastSelected != null)
                {
                    Context.ShowStatus(Describe(lastSelected));
                }
                else if (group != null)
                {
                    Context.ShowStatus(group.Letter + "  ·  " + CountText(group.Count));
                }
                if (wasLetter != (group != null))
                {
                    Context.RefreshLegend();
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
                        Context.RefreshLegend();
                        e.Handled = true;
                        break;
                    case GamePadButton.Back:
                        Context.OpenShelfOptions(header);
                        e.Handled = true;
                        break;
                    case GamePadButton.Start:
                        TitleEntry title = e.Item as TitleEntry;
                        if (title != null && options.APlays)
                        {
                            Context.ShowDetails(title);
                        }
                        else if (title != null)
                        {
                            Context.Launch(title);
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
                grid.TileTemplate = new RowTemplate(o => o is LetterGroup ? LetterRow((LetterGroup)o) : Row((TitleEntry)o));
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
                CoverTileTemplate covers = new CoverTileTemplate(Context.Images, () => Context.UserIndex);
                LetterTileTemplate letters = new LetterTileTemplate(o => ((LetterGroup)o).Letter,
                                                                    o => CountText(((LetterGroup)o).Count));
                grid.TileTemplate = new MixedTileTemplate(o => o is LetterGroup ? (ITileTemplate)letters : covers);
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

        private RowData LetterRow(LetterGroup group)
        {
            return new RowData { Title = group.Letter, Subtitle = CountText(group.Count) };
        }

        // "12 games", "1 app".
        private string CountText(int count)
        {
            return count + " " + (count == 1 ? header.TrimEnd('s') : header);
        }

        // The letter a title goes under.
        private static string LetterOf(string name)
        {
            char first = string.IsNullOrEmpty(name) ? ' ' : name[0];
            return char.IsLetter(first) ? char.ToUpperInvariant(first).ToString() : "#";
        }

        // The titles as shown: sorted by name in a long shelf, each letter
        // before its titles, or the letters alone.
        private List<object> Arrange(List<TitleEntry> titles)
        {
            if (options.Sort != "name" || titles.Count < LetterScreens * grid.ItemsOnScreen)
            {
                lettersOnly = false;
                lastLetter = null;
                return titles.Cast<object>().ToList();
            }
            List<object> items = new List<object>();
            foreach (IGrouping<string, TitleEntry> letter in titles.GroupBy(t => LetterOf(t.Name))
                         .OrderBy(g => g.Key == "#" ? "" : g.Key, StringComparer.Ordinal))
            {
                items.Add(new LetterGroup { Letter = letter.Key, Count = letter.Count() });
                if (!lettersOnly)
                {
                    items.AddRange(letter.Cast<object>());
                }
            }
            return items;
        }

        // Shows the letters alone, or everything again, with the letter where
        // it was on screen.
        private void ToggleLetters(LetterGroup group)
        {
            double place = grid.PlaceOf(grid.SelectedIndex);
            lettersOnly = !lettersOnly;
            List<object> items = Arrange(options.Apply(select(Context.Library)));
            int at = items.IndexOf(items.OfType<LetterGroup>().FirstOrDefault(g => g.Letter == group.Letter));
            grid.ShowItems(items, Math.Max(0, at), place);
            lastLetter = group.Letter;
            Context.RefreshLegend();
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
            string letter = lastLetter;
            List<object> items = Arrange(options.Apply(select(Context.Library)));
            grid.Items = items;
            if (lastSelected != null)
            {
                TitleEntry same = items.OfType<TitleEntry>().FirstOrDefault(t => t.TitleId == lastSelected.TitleId);
                if (same != null)
                {
                    grid.SelectItem(same);
                }
            }
            else if (letter != null)
            {
                LetterGroup same = items.OfType<LetterGroup>().FirstOrDefault(g => g.Letter == letter);
                if (same != null)
                {
                    grid.SelectItem(same);
                }
            }
        }
    }
}
