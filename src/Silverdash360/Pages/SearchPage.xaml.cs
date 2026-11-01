using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Navigation;
using Microsoft.Xbox.Controls;
using Silverdash360.Controls;
using Silverdash360.Model;
using Silverdash360.Services;

namespace Silverdash360.Pages
{
    /// <summary>
    /// Finds games and apps as the user types. The query is kept, so coming
    /// back from a title shows the same results.
    /// </summary>
    public partial class SearchPage : XboxApplicationPage
    {
        private static string lastQuery = "";

        /// <summary>The text the page opens with, as if typed.</summary>
        public static void Prefill(string text)
        {
            lastQuery = text;
        }

        public SearchPage()
        {
            InitializeComponent();
            Motion.UsePageTransitions(this);
            Results.Direction = ScrollDirection.Vertical;
            Results.Lines = 1;
            Results.TileWidth = 0;
            Results.TileHeight = 80;
            Results.Spacing = 10;
            Results.TileTemplate = new RowTemplate(o => Row((TitleEntry)o));
            Results.ItemInvoked += delegate(object sender, ItemEventArgs e) { App.Context.OpenTitle((TitleEntry)e.Item); };
            Results.EdgeReached += delegate(object sender, EdgeEventArgs e)
            {
                if (e.Edge == Edge.Left)
                {
                    Keyboard.Focus();
                    e.Handled = true;
                }
            };
            Keyboard.EdgeReached += delegate(object sender, EdgeEventArgs e)
            {
                if (e.Edge == Edge.Right && Results.Items.Count > 0)
                {
                    Results.Focus();
                    e.Handled = true;
                }
            };
            Keyboard.TextChanged += delegate { Search(); };
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Keyboard.Text = lastQuery;
            FocusHelper.Focus(Keyboard);
        }

        private void Search()
        {
            lastQuery = Keyboard.Text;
            QueryText.Text = lastQuery;
            QueryHint.Visibility = lastQuery.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            List<TitleEntry> found = App.Context.Library.Search(lastQuery);
            Results.EmptyText = lastQuery.Trim().Length == 0 ? "" : "Nothing matches \"" + lastQuery.Trim() + "\".";
            Results.Items = found.Cast<object>().ToList();
            CountText.Text = lastQuery.Trim().Length == 0 ? "" : found.Count == 1 ? "1 result" : found.Count + " results";
        }

        private static RowData Row(TitleEntry title)
        {
            return new RowData
            {
                Title = title.Name,
                Subtitle = (title.Kind == TitleKind.App ? "App" : "Game") + "  ·  " + CoverTileTemplate.Subtitle(title),
                Detail = SystemService.FormatSize(title.TotalSize),
                ArtSeed = title.TitleId,
                Art = done => App.Context.Images.TitleArt(title, App.Context.UserIndex, done),
                Dimmed = !title.IsInstalled,
            };
        }
    }
}
