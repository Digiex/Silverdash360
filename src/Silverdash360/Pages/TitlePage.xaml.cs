using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
                    ShowTab((tab + (e.Button == GamePadButton.RightShoulder ? 1 : tabs.Count - 1)) % tabs.Count);
                    e.Handled = true;
                }
            };
            App.Context.Status += OnStatus;
            Unloaded += delegate { App.Context.Status -= OnStatus; };
        }

        private static DashboardContext Context { get { return App.Context; } }

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
        }

        private void ShowTitle()
        {
            NameText.Text = title.Name;
            ArtFrame.Background = Theme.PlaceholderBrush(title.TitleId);
            ArtInitials.Text = Theme.Initials(title.Name);
            Context.Images.TitleArt(title, Context.UserIndex, source =>
            {
                Art.Source = source;
                Backdrop.Source = source;
                ArtInitials.Text = "";
            });

            ContentItem launch = title.LaunchItem;
            List<string> info = new List<string>();
            if (title.Publisher.Length > 0) info.Add(title.Publisher);
            info.Add(title.Source);
            if (launch != null && launch.Metadata != null) info.Add("version " + launch.Metadata.VersionText);
            info.Add("title " + title.TitleIdText);
            InfoText.Text = string.Join("  ·  ", info.ToArray());

            List<string> stats = new List<string>();
            if (title.Played != null)
            {
                stats.Add(string.Format("{0} / {1} G", title.Played.GamerscoreEarned, title.Played.GamerscoreTotal));
                stats.Add(string.Format("{0} of {1} achievements", title.Played.AchievementsUnlocked, title.Played.AchievementsTotal));
                if (title.LastPlayed.HasValue) stats.Add("last played " + title.LastPlayed.Value.ToString("d"));
            }
            if (title.TotalSize > 0) stats.Add(SystemService.FormatSize(title.TotalSize) + " on this console");
            StatsText.Text = string.Join("  ·  ", stats.ToArray());
            DescriptionText.Text = title.Description;

            List<ActionEntry> actions = new List<ActionEntry>();
            if (title.IsInstalled)
            {
                actions.Add(new ActionEntry { Text = "Play", Run = () => Context.Launch(title) });
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
                Items = () => title.Executables.Cast<object>()
                                   .Concat(title.Content.OrderBy(c => c.Type).ThenBy(c => c.DisplayName).Cast<object>())
                                   .ToList(),
                Row = o => o is ExecutableItem ? ExecutableRow((ExecutableItem)o) : ContentRow((ContentItem)o),
            });
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

        private void ShowTab(int index)
        {
            if (index < 0)
            {
                return;
            }
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
