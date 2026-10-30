using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Silverdash360.Controls;
using Silverdash360.Model;

namespace Silverdash360.Modules
{
    /// <summary>
    /// The signed-in profile's gamer card and the titles it played with their
    /// achievement progress. A opens a title, Y signs in or switches profile,
    /// X shows friends.
    /// </summary>
    public sealed class ProfileModule : DashboardModule
    {
        private Image picture;
        private TextBlock initials;
        private TextBlock gamertag;
        private TextBlock gamerscore;
        private TextBlock details;
        private TextBlock motto;
        private TileGrid played;

        public ProfileModule(DashboardContext context) : base(context)
        {
        }

        public override string Header { get { return "me"; } }
        public override int Order { get { return 40; } }
        public override string Legend { get { return "A details   X friends   Y sign in"; } }

        protected override FrameworkElement CreateView()
        {
            Grid view = new Grid();
            view.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
            view.ColumnDefinitions.Add(new ColumnDefinition());

            StackPanel card = new StackPanel { Margin = new Thickness(0, 0, 32, 0) };
            Grid frame = new Grid { Width = 128, Height = 128, HorizontalAlignment = HorizontalAlignment.Left, Background = Theme.Panel };
            initials = Theme.TextBlock("", 52, Theme.Text);
            initials.HorizontalAlignment = HorizontalAlignment.Center;
            initials.VerticalAlignment = VerticalAlignment.Center;
            frame.Children.Add(initials);
            picture = new Image { Stretch = Stretch.UniformToFill };
            frame.Children.Add(picture);
            card.Children.Add(frame);
            gamertag = Theme.TextBlock("", 38, Theme.Text);
            gamertag.Margin = new Thickness(0, 16, 0, 0);
            card.Children.Add(gamertag);
            gamerscore = Theme.TextBlock("", 26, Theme.Accent);
            card.Children.Add(gamerscore);
            details = Theme.TextBlock("", 18, Theme.SubText);
            details.TextWrapping = TextWrapping.Wrap;
            details.Margin = new Thickness(0, 12, 0, 0);
            card.Children.Add(details);
            motto = Theme.TextBlock("", 18, Theme.Text);
            motto.TextWrapping = TextWrapping.Wrap;
            motto.FontStyle = FontStyles.Italic;
            motto.Margin = new Thickness(0, 12, 0, 0);
            card.Children.Add(motto);
            view.Children.Add(card);

            played = new TileGrid
            {
                Direction = ScrollDirection.Vertical,
                Lines = 1,
                TileWidth = 760,
                TileHeight = 76,
                Spacing = 10,
                TileTemplate = new RowTemplate(o => Row((TitleEntry)o)),
            };
            Grid.SetColumn(played, 1);
            view.Children.Add(played);

            ForwardEdges(played, Edge.Left, Edge.Right);
            played.ItemInvoked += delegate(object sender, ItemEventArgs e) { Context.OpenTitle((TitleEntry)e.Item); };
            played.ItemButton += delegate(object sender, ItemEventArgs e)
            {
                Gamer gamer = Context.Library.Gamer;
                if (e.Button == GamePadButton.Y)
                {
                    Context.Profiles.ShowSignin();
                    e.Handled = true;
                }
                else if (e.Button == GamePadButton.X && gamer != null)
                {
                    Context.Profiles.ShowFriends(gamer);
                    e.Handled = true;
                }
            };
            return view;
        }

        private RowData Row(TitleEntry title)
        {
            PlayedTitle p = title.Played;
            return new RowData
            {
                Title = title.Name,
                Subtitle = string.Format("{0} of {1} achievements{2}", p.AchievementsUnlocked, p.AchievementsTotal,
                                         p.LastPlayed.HasValue ? "  ·  last played " + p.LastPlayed.Value.ToString("d") : ""),
                Detail = string.Format("{0} / {1} G", p.GamerscoreEarned, p.GamerscoreTotal),
                Progress = p.Progress,
                ArtSeed = title.TitleId,
                Art = done => Context.Images.TitleArt(title, Context.UserIndex, done),
            };
        }

        public override void Activate()
        {
            Focus(played);
        }

        public override void Refresh()
        {
            if (played == null)
            {
                return;
            }
            Gamer gamer = Context.Library.Gamer;
            picture.Source = null;
            if (gamer == null)
            {
                initials.Text = "?";
                gamertag.Text = "Not signed in";
                gamerscore.Text = "";
                details.Text = "Press Y to sign in to a profile.";
                motto.Text = "";
                played.EmptyText = "";
                played.Items = null;
                return;
            }
            initials.Text = Theme.Initials(gamer.Gamertag);
            gamertag.Text = gamer.Gamertag;
            gamerscore.Text = gamer.Gamerscore + " G";
            details.Text = string.Format("{0} achievements  ·  {1} titles played\n{2}{3}",
                                         gamer.AchievementsEarned, gamer.TitlesPlayed,
                                         gamer.State == SigninState.SignedInToLive ? "Xbox LIVE" : "Offline profile",
                                         gamer.ZoneName.Length > 0 ? "  ·  " + gamer.ZoneName : "");
            motto.Text = gamer.Motto ?? "";
            Context.Images.GamerPicture(gamer, source =>
            {
                picture.Source = source;
                initials.Text = "";
            });
            played.EmptyText = "No played titles yet.";
            played.Items = Context.Library.Titles.Where(t => t.Played != null)
                                  .OrderByDescending(t => t.LastPlayed)
                                  .Cast<object>().ToList();
        }
    }
}
