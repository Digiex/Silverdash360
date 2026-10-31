using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Navigation;
using Microsoft.Xbox.Controls;
using Silverdash360.Controls;
using Silverdash360.Services;

namespace Silverdash360.Pages
{
    /// <summary>
    /// Offers the update a check found: update now, later (B too), or skip
    /// this version, which isn't offered again. Updating downloads and
    /// installs it here and starts the new version; B waits meanwhile.
    /// </summary>
    public partial class UpdatePage : XboxApplicationPage
    {
        // Lines of the release notes shown; the rest are counted.
        private const int NotesLines = 12;

        private sealed class Choice
        {
            public string Title;
            public string Detail;
            public Action Run;
        }

        private UpdateService.Release release;
        private bool installing;

        public UpdatePage()
        {
            InitializeComponent();
            Motion.UsePageTransitions(this);
            Actions.Direction = ScrollDirection.Vertical;
            Actions.Lines = 1;
            Actions.TileWidth = 0;
            Actions.TileHeight = 72;
            Actions.Spacing = 8;
            Actions.TileTemplate = new RowTemplate(o => new RowData { Title = ((Choice)o).Title, Subtitle = ((Choice)o).Detail })
            {
                IconSize = 0,
            };
            Actions.ItemInvoked += delegate(object sender, ItemEventArgs e)
            {
                if (!installing)
                {
                    ((Choice)e.Item).Run();
                }
            };
            GamePadButtonDown += delegate(object sender, GamePadButtonEventArgs e)
            {
                // No going back from an install half done.
                if (installing && e.Button == GamePadButton.B)
                {
                    e.Handled = true;
                }
            };
        }

        private static UpdateService Updates { get { return App.Context.Updates; } }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            release = Updates.Offered;
            if (release == null)
            {
                NameText.Text = "No update";
                Actions.Items = null;
                return;
            }
            NameText.Text = release.Name + " is out";
            CurrentText.Text = "You have " + BuildInfo.Version + ".";
            NotesText.Text = Notes(release.Notes);
            Actions.Items = new List<object>
            {
                new Choice { Title = "Update now", Detail = "Download it, install it and start it", Run = Update },
                new Choice { Title = "Later", Detail = "Ask again the next time Silverdash starts", Run = Later },
                new Choice { Title = "Skip this version", Detail = "Don't ask about " + release.Version + " again", Run = Skip },
            };
            FocusHelper.Focus(Actions);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            // Leaving it unanswered is later.
            if (!installing)
            {
                Updates.Offered = null;
            }
        }

        private void Update()
        {
            installing = true;
            Legend.Text = "";
            Updates.Install(release,
                            text => StatusText.Text = text,
                            problem =>
                            {
                                installing = false;
                                Legend.Text = "A select   B later";
                                StatusText.Text = problem;
                            });
        }

        private void Later()
        {
            Updates.Offered = null;
            GoBack();
        }

        private void Skip()
        {
            Updates.Skip(release);
            Updates.Offered = null;
            GoBack();
        }

        private void GoBack()
        {
            if (NavigationService.CanGoBack)
            {
                NavigationService.GoBack();
            }
        }

        // The notes as plain text: headings and list marks of the Markdown
        // tools/Release.ps1 writes made plain, cut to a screen.
        private static string Notes(string markdown)
        {
            List<string> lines = new List<string>();
            foreach (string raw in markdown.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("#"))
                {
                    line = line.TrimStart('#').Trim();
                }
                else if (line.StartsWith("- "))
                {
                    line = "• " + line.Substring(2);
                }
                if (line.Length > 0)
                {
                    lines.Add(line);
                }
            }
            if (lines.Count > NotesLines)
            {
                int more = lines.Count - NotesLines + 1;
                lines = lines.Take(NotesLines - 1).ToList();
                lines.Add("and " + more + " more");
            }
            return string.Join("\n", lines.ToArray());
        }
    }
}
