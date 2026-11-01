using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Microsoft.Xbox.Controls;
using Silverdash360.Controls;
using Silverdash360.Core;
using Silverdash360.Model;
using Silverdash360.Modules;
using Silverdash360.Services;

namespace Silverdash360.Pages
{
    /// <summary>
    /// A title's details: art, description, what's stored for it and its
    /// achievements, with actions on the left. Opened with ?id=TITLEID.
    /// </summary>
    public partial class TitlePage : XboxApplicationPage
    {
        private static int lastTab;

        private TitleEntry title;
        private readonly List<TextBlock> tabHeaders = new List<TextBlock>();
        private readonly List<Tab> tabs = new List<Tab>();
        private int tab;

        private sealed class Tab
        {
            public string Header;
            public Func<List<object>> Items;
            public Func<object, RowData> Row;
            public string Empty;
        }

        private sealed class ActionEntry
        {
            public string Text;
            public Action Run;
        }

        public TitlePage()
        {
            InitializeComponent();
            Motion.UsePageTransitions(this);
            Actions.Direction = ScrollDirection.Vertical;
            Actions.Lines = 1;
            Actions.TileWidth = 300;
            Actions.TileHeight = 56;
            Actions.Spacing = 8;
            Actions.TileTemplate = new RowTemplate(o => new RowData { Title = ((ActionEntry)o).Text }) { IconSize = 0 };
            Actions.ItemInvoked += delegate(object sender, ItemEventArgs e) { ((ActionEntry)e.Item).Run(); };
            Actions.EdgeReached += delegate(object sender, EdgeEventArgs e)
            {
                if (e.Edge == Edge.Right && Details.Items.Count > 0)
                {
                    Details.Focus();
                    e.Handled = true;
                }
            };

            Details.Direction = ScrollDirection.Vertical;
            Details.Lines = 1;
            Details.TileWidth = 0;
            Details.TileHeight = 72;
            Details.Spacing = 8;
            Details.EdgeReached += delegate(object sender, EdgeEventArgs e)
            {
                if (e.Edge == Edge.Left)
                {
                    Actions.Focus();
                    e.Handled = true;
                }
            };
            GamePadButtonDown += delegate(object sender, GamePadButtonEventArgs e)
            {
                if (e.Button == GamePadButton.LeftShoulder || e.Button == GamePadButton.RightShoulder)
                {
                    SwitchTab(e.Button == GamePadButton.RightShoulder ? 1 : -1);
                    e.Handled = true;
                }
            };
            App.Context.Status += OnStatus;
            Unloaded += delegate { App.Context.Status -= OnStatus; };
        }

        // The Marketplace's backdrops are made to sit behind text; the title's
        // own art, standing in for one, is kept fainter.
        private const double MarketplaceBackdropOpacity = 0.3;
        // A box front's width in the 300-pixel frame, at its 219 by 300.
        private const double BoxFrontWidth = 219;

        private static DashboardContext Context { get { return App.Context; } }

        // Who made it, where it's from and its description, with what the
        // Marketplace adds when it has the title.
        private void ShowInfo(TitleDetails details)
        {
            ContentItem launch = title.LaunchItem;
            List<string> info = new List<string>();
            string publisher = title.Publisher.Length > 0 ? title.Publisher : details != null ? details.Publisher : "";
            if (publisher.Length > 0) info.Add(publisher);
            if (details != null && details.Developer.Length > 0 && details.Developer != publisher) info.Add(details.Developer);
            if (details != null && details.Genre.Length > 0) info.Add(details.Genre);
            if (details != null && details.ReleaseDate.Length >= 4) info.Add(details.ReleaseDate.Substring(0, 4));
            info.Add(title.Source);
            if (launch != null && launch.Metadata != null) info.Add("version " + launch.Metadata.VersionText);
            info.Add("title " + title.TitleIdText);
            InfoText.Text = string.Join("  ·  ", info.ToArray());
            // The Marketplace's description is the store's, usually the
            // fuller one.
            DescriptionText.Text = details != null && details.Description.Length > title.Description.Length
                ? details.Description : title.Description;
        }

        private void OnStatus(object sender, StatusEventArgs e)
        {
            StatusText.Text = e.Text;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            string key;
            if (!NavigationContext.QueryString.TryGetValue("key", out key) ||
                (title = Context.Library.Find(Uri.UnescapeDataString(key))) == null)
            {
                NameText.Text = "Title not found";
                return;
            }
            ShowTitle();
            FocusHelper.Focus(Actions);
            // Its saves and add-ons as they are now: a start doesn't list them.
            string titleKey = title.Key;
            Context.Library.RefreshTitle(title, delegate
            {
                TitleEntry updated = Context.Library.Find(titleKey);
                if (updated != null && updated != title)
                {
                    title = updated;
                    contentItems = null;
                    if (tabs.Count > tab && tabs[tab].Header == "content")
                    {
                        ShowTab(tab);
                    }
                    else
                    {
                        SortContent();
                    }
                }
            });
        }

        private void ShowTitle()
        {
            NameText.Text = title.Name;
            ArtFrame.Background = null;
            ArtInitials.Text = "";
            // The kind's cover, a box front's shape in the square frame,
            // with the title's icon; the box front goes over it.
            Covers.Cover cover = Covers.For(title, 1.3);
            cover.Root.Width = BoxFrontWidth;
            ArtFrame.Children.Insert(0, cover.Root);
            bool hasBackdrop = false;
            Context.Images.TitleArt(title, Context.UserIndex, source =>
            {
                cover.SetIcon(source);
                if (!hasBackdrop && Backdrop.Source == null)
                {
                    Backdrop.Source = source;
                }
            });
            Context.Images.BoxArt(title, source =>
            {
                Art.Source = source;
                cover.Root.Visibility = Visibility.Collapsed;
                if (!hasBackdrop)
                {
                    Backdrop.Source = source;
                }
            });
            Context.Images.Backdrop(title, source =>
            {
                hasBackdrop = true;
                Backdrop.Source = source;
                Backdrop.Opacity = MarketplaceBackdropOpacity;
            });
            ShowInfo(null);
            TitleEntry shown = title;
            Core.Background.Run(() => MarketplaceArt.ReadDetails(shown.TitleId), delegate(TitleDetails details, Exception error)
            {
                if (details != null && title == shown)
                {
                    ShowInfo(details);
                }
            });

            List<string> stats = new List<string>();
            if (title.Played != null)
            {
                stats.Add(string.Format("{0} / {1} G", title.Played.GamerscoreEarned, title.Played.GamerscoreTotal));
                stats.Add(string.Format("{0} of {1} achievements", title.Played.AchievementsUnlocked, title.Played.AchievementsTotal));
                if (title.LastPlayed.HasValue) stats.Add("last played " + title.LastPlayed.Value.ToString("d"));
            }
            if (title.TotalSize > 0) stats.Add(SystemService.FormatSize(title.TotalSize) + " on this console");
            StatsText.Text = string.Join("  ·  ", stats.ToArray());

            List<ActionEntry> actions = new List<ActionEntry>();
            // The title's disc in the drive, whether it's installed or not.
            TitleEntry inDrive = Context.Discs.AsTitle(Context.Library);
            if (inDrive != null && inDrive.TitleId != 0 && inDrive.TitleId == title.TitleId)
            {
                actions.Add(new ActionEntry { Text = "Play from disc", Run = () => Context.OpenDisc(inDrive) });
            }
            if (title.IsInstalled)
            {
                actions.Add(new ActionEntry { Text = "Play", Run = () => Context.Launch(title) });
                foreach (ContentItem disc in title.OtherDiscs)
                {
                    ContentItem start = disc;
                    actions.Add(new ActionEntry
                    {
                        Text = "Play disc " + disc.Metadata.DiscNumber,
                        Run = () => Context.ShowStatus(Context.System.Launch(start) ?? ""),
                    });
                }
            }
            if (title.Played != null && Context.Library.Gamer != null)
            {
                actions.Add(new ActionEntry
                {
                    Text = "Achievements",
                    Run = () => { ShowTab(IndexOfTab("achievements")); Details.Focus(); },
                });
            }
            actions.Add(new ActionEntry
            {
                Text = "Stored content",
                Run = () => { ShowTab(IndexOfTab("content")); Details.Focus(); },
            });
            Actions.Items = actions.Cast<object>().ToList();

            BuildTabs();
        }

        private void BuildTabs()
        {
            if (title.Played != null && Context.Library.Gamer != null)
            {
                List<Achievement> achievements = null;
                tabs.Add(new Tab
                {
                    Header = "achievements",
                    Empty = "Reading achievements...",
                    Items = () => achievements != null ? achievements.Cast<object>().ToList() : LoadAchievements(a => achievements = a),
                    Row = o => AchievementRow((Achievement)o),
                });
            }
            tabs.Add(new Tab
            {
                Header = "content",
                Empty = "Nothing is stored for this title.",
                Items = () => contentItems ?? SortContent(),
                Row = o => o is ExecutableItem ? ExecutableRow((ExecutableItem)o) : ContentRow((ContentItem)o),
            });
            SortContent();
            foreach (Tab t in tabs)
            {
                TextBlock header = Theme.TextBlock(t.Header, 24, Theme.SubText);
                header.Margin = new Thickness(0, 0, 28, 0);
                tabHeaders.Add(header);
                Tabs.Children.Add(header);
            }
            ShowTab(Math.Min(lastTab, tabs.Count - 1));
        }

        private int IndexOfTab(string header)
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                if (tabs[i].Header == header)
                {
                    return i;
                }
            }
            return -1;
        }

        // The content tab's rows, sorted once: a game can have hundreds of
        // add-ons, and sorting them on every visit to the tab held up the
        // page for seconds on a console.
        private List<object> contentItems;
        private bool sortingContent;

        // Sorts the title's packages in the background, by type and then
        // name (ordinal: the culture's comparison is much slower), and shows
        // them when the content tab is open; empty meanwhile.
        private List<object> SortContent()
        {
            if (!sortingContent && contentItems == null)
            {
                sortingContent = true;
                TitleEntry sorting = title;
                Core.Background.Run(() =>
                {
                    List<object> rows = new List<object>();
                    rows.AddRange(sorting.Executables.Cast<object>());
                    rows.AddRange(sorting.Content.OrderBy(c => c.Type)
                                         .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
                                         .Cast<object>());
                    return rows;
                }, delegate(List<object> rows, Exception error)
                {
                    sortingContent = false;
                    if (sorting != title)
                    {
                        // The title changed meanwhile: its own sort follows.
                        SortContent();
                        return;
                    }
                    contentItems = rows ?? new List<object>();
                    if (tabs.Count > tab && tabs[tab].Header == "content")
                    {
                        Details.EmptyText = tabs[tab].Empty;
                        ShowTab(tab);
                    }
                });
            }
            if (tabs.Count > tab && tabs[tab].Header == "content")
            {
                Details.EmptyText = "Reading...";
            }
            return new List<object>();
        }

        /// <summary>The next tab (1) or the one before (-1), as RB and LB show them.</summary>
        public void SwitchTab(int direction)
        {
            if (tabs.Count > 0)
            {
                ShowTab((tab + (direction > 0 ? 1 : tabs.Count - 1)) % tabs.Count);
            }
        }

        private void ShowTab(int index)
        {
            if (index < 0)
            {
                return;
            }
            int started = Environment.TickCount;
            tab = lastTab = index;
            for (int i = 0; i < tabHeaders.Count; i++)
            {
                tabHeaders[i].Foreground = i == index ? Theme.Text : Theme.SubText;
                tabHeaders[i].FontWeight = i == index ? FontWeights.Bold : FontWeights.Normal;
            }
            Tab t = tabs[index];
            Details.EmptyText = t.Empty;
            Details.TileTemplate = new RowTemplate(t.Row);
            Details.Items = t.Items();
            Log.Write("Title page: the {0} tab in {1} ms", t.Header, Environment.TickCount - started);
        }

        // Reads them in the background and shows them when the tab is still open.
        private List<object> LoadAchievements(Action<List<Achievement>> keep)
        {
            Gamer gamer = Context.Library.Gamer;
            uint titleId = title.TitleId;
            Core.Background.Run(() => Context.Profiles.Achievements(gamer, titleId), delegate(List<Achievement> result, Exception error)
            {
                keep(result ?? new List<Achievement>());
                if (tabs[tab].Header == "achievements")
                {
                    Details.EmptyText = "This title has no achievements.";
                    ShowTab(tab);
                }
            });
            return new List<object>();
        }

        private RowData AchievementRow(Achievement a)
        {
            bool unlocked = a.IsUnlocked;
            string text = unlocked ? a.Description : (a.IsSecret ? "Keep playing to unlock it." : a.Unachieved.Length > 0 ? a.Unachieved : a.Description);
            return new RowData
            {
                Title = unlocked || !a.IsSecret ? a.Label : "Secret achievement",
                Subtitle = unlocked && a.Unlocked.HasValue ? text + "  ·  unlocked " + a.Unlocked.Value.ToString("d") : text,
                Detail = a.Gamerscore + " G",
                ArtSeed = a.Id + title.TitleId,
                Art = unlocked ? (Action<Action<ImageSource>>)(done => Context.Images.AchievementArt(title.TitleId, a, Context.UserIndex, done)) : null,
                Locked = !unlocked,
                Dimmed = !unlocked,
            };
        }

        private RowData ExecutableRow(ExecutableItem item)
        {
            return new RowData
            {
                Title = item.Name,
                Subtitle = "Executable  ·  " + item.Path.TrimStart('/').Replace('/', '\\'),
                Detail = SystemService.FormatSize(item.Size),
                ArtSeed = item.TitleId ^ (uint)item.Path.Length,
            };
        }

        private RowData ContentRow(ContentItem item)
        {
            string name = item.DisplayName.Length > 0 ? item.DisplayName : item.FileName;
            return new RowData
            {
                Title = name,
                Subtitle = item.TypeName + (item.IsPartial ? "  ·  partly downloaded" : "") + (item.IsCorrupt ? "  ·  damaged" : ""),
                Detail = SystemService.FormatSize(item.BestSize),
                ArtSeed = item.Type ^ title.TitleId,
                Art = done => Context.Images.ContentArt(item, done),
            };
        }
    }
}
