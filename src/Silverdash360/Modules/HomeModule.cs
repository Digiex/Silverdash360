using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Silverdash360.Controls;
using Silverdash360.Model;

namespace Silverdash360.Modules
{
    /// <summary>
    /// The first section: the titles played most recently as large tiles, and
    /// the apps below them. Up and down move between the two rows.
    /// </summary>
    public sealed class HomeModule : DashboardModule
    {
        private const int RecentCount = 12;

        private TileGrid recent;
        private TileGrid apps;
        private TextBlock summary;

        public HomeModule(DashboardContext context) : base(context)
        {
        }

        public override string Header { get { return "home"; } }
        public override int Order { get { return 10; } }
        public override string Legend { get { return "A details   START play   X search"; } }

        protected override FrameworkElement CreateView()
        {
            Grid view = new Grid();
            view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(300) });
            view.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            view.RowDefinitions.Add(new RowDefinition { Height = new GridLength(140) });

            Grid recentHeader = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            recentHeader.Children.Add(Theme.TextBlock("jump back in", 22, Theme.SubText));
            summary = Theme.TextBlock("", 18, Theme.SubText);
            summary.HorizontalAlignment = HorizontalAlignment.Right;
            recentHeader.Children.Add(summary);
            view.Children.Add(recentHeader);

            recent = new TileGrid
            {
                Lines = 1,
                TileWidth = 214,
                Margin = Theme.GrowRoom,
                TileHeight = 286,
                Spacing = 20,
                PadOutside = 24,
                TileTemplate = new CoverTileTemplate(Context.Images, () => Context.UserIndex),
            };
            Grid.SetRow(recent, 1);
            view.Children.Add(recent);

            TextBlock appsHeader = Theme.TextBlock("apps", 22, Theme.SubText);
            appsHeader.Margin = new Thickness(0, 14, 0, 10);
            Grid.SetRow(appsHeader, 2);
            view.Children.Add(appsHeader);

            apps = new TileGrid
            {
                Lines = 1,
                TileWidth = 104,
                Margin = Theme.GrowRoom,
                TileHeight = 104,
                Spacing = 14,
                PadOutside = 12,
                TileTemplate = new RowTemplate(o => new RowData
                {
                    Title = ((TitleEntry)o).Name,
                    ArtSeed = ((TitleEntry)o).TitleId,
                    Art = done => Context.Images.TitleArt((TitleEntry)o, Context.UserIndex, done),
                }) { IconSize = 88 },
            };
            Grid.SetRow(apps, 3);
            view.Children.Add(apps);

            foreach (TileGrid grid in new[] { recent, apps })
            {
                ForwardEdges(grid, Edge.Left, Edge.Right);
                // One selection on the page: the row in use's.
                grid.ShowsSelectionUnfocused = false;
                grid.ItemInvoked += delegate(object sender, ItemEventArgs e) { Open((TitleEntry)e.Item); };
                // The footer names what's selected in the row in use; the
                // other row's selection changes when the library does.
                grid.SelectionChanged += delegate(object sender, System.EventArgs e)
                {
                    if (((TileGrid)sender).HasFocus)
                    {
                        ShowSelected((TileGrid)sender);
                    }
                };
                grid.GotFocus += delegate(object sender, RoutedEventArgs e) { ShowSelected((TileGrid)sender); };
                grid.ItemButton += delegate(object sender, ItemEventArgs e)
                {
                    if (e.Button == GamePadButton.X)
                    {
                        Context.OpenSearch();
                        e.Handled = true;
                    }
                    else if (e.Button == GamePadButton.Y)
                    {
                        Context.ShowStatus(Context.Discs.IsTrayOpen ? "Closing the disc tray" : "Opening the disc tray");
                        Context.Discs.ToggleTray();
                        e.Handled = true;
                    }
                    else if (e.Button == GamePadButton.Start && e.Item != null)
                    {
                        TitleEntry title = (TitleEntry)e.Item;
                        if (title.IsDisc)
                        {
                            Context.OpenDisc(title);
                        }
                        else
                        {
                            Context.Launch(title);
                        }
                        e.Handled = true;
                    }
                };
            }
            recent.EdgeReached += delegate(object sender, EdgeEventArgs e)
            {
                if (e.Edge == Edge.Bottom && apps.Items.Count > 0)
                {
                    Focus(apps);
                    e.Handled = true;
                }
            };
            apps.EdgeReached += delegate(object sender, EdgeEventArgs e)
            {
                if (e.Edge == Edge.Top && recent.Items.Count > 0)
                {
                    Focus(recent);
                    e.Handled = true;
                }
            };
            Context.Discs.Changed += delegate { Refresh(); };
            return view;
        }

        // A disc opens as it is (a game starts); a title shows its page.
        private void Open(TitleEntry title)
        {
            if (title.IsDisc)
            {
                Context.OpenDisc(title);
            }
            else
            {
                Context.OpenTitle(title);
            }
        }

        private void ShowSelected(TileGrid grid)
        {
            TitleEntry title = grid.SelectedItem as TitleEntry;
            if (title != null)
            {
                Context.ShowStatus(title.Name);
            }
        }

        public override void Activate()
        {
            Focus(recent.Items.Count > 0 || apps.Items.Count == 0 ? recent : apps);
        }

        public override void Refresh()
        {
            if (recent == null)
            {
                return;
            }
            var library = Context.Library;
            recent.EmptyText = library.IsLoaded ? "Nothing played yet. Your games and apps are in the next sections." : "Looking for content...";
            // The disc in the drive first.
            List<object> items = new List<object>();
            TitleEntry disc = Context.Discs.AsTitle(library);
            if (disc != null)
            {
                items.Add(disc);
            }
            items.AddRange(library.Recent.Take(RecentCount).Cast<object>());
            recent.Items = items;
            apps.Items = library.Apps.Where(a => a.IsInstalled).Cast<object>().ToList();
            summary.Text = library.IsLoaded
                ? string.Format("{0} games  ·  {1} apps", library.Games.Count(), library.Apps.Count())
                : "";
        }
    }
}
