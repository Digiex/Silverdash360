using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Navigation;
using Microsoft.Xbox.Controls;
using Silverdash360.Controls;
using Silverdash360.Modules;

namespace Silverdash360.Pages
{
    /// <summary>
    /// Welcomes a new install with the basic settings, and comes back after
    /// an update that adds some, those marked new. Continue or B goes on to
    /// the hub; either way it isn't shown again until settings are added.
    /// </summary>
    public partial class WelcomePage : XboxApplicationPage
    {
        private sealed class Continue
        {
        }

        // The welcome version seen before this showing, for what's new.
        private int seen;

        public WelcomePage()
        {
            InitializeComponent();
            Motion.UsePageTransitions(this);
            Rows.Direction = ScrollDirection.Vertical;
            Rows.Lines = 1;
            Rows.TileWidth = 0;
            Rows.TileHeight = 72;
            Rows.Spacing = 8;
            Rows.TileTemplate = new RowTemplate(Describe) { IconSize = 0 };
            Rows.ItemInvoked += delegate(object sender, ItemEventArgs e)
            {
                BasicSetting setting = e.Item as BasicSetting;
                if (setting != null)
                {
                    setting.Change(Show);
                }
                else
                {
                    GoBack();
                }
            };
        }

        private static DashboardContext Context { get { return App.Context; } }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            seen = BasicSettings.Seen;
            IntroText.Text = seen < 0
                ? "A few settings to start with. Settings has them too, to change later."
                : "This version has new settings, marked new. Settings has them too, to change later.";
            Show();
            FocusHelper.Focus(Rows);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            BasicSettings.MarkSeen(Context);
        }

        // Shows the settings as they are now, keeping the row selected. The
        // FTP server's user and password show once it's set to start.
        private void Show()
        {
            int selected = Rows.SelectedIndex;
            List<object> rows = BasicSettings.All(Context).Where(s => s.Shown == null || s.Shown()).Cast<object>().ToList();
            rows.Add(new Continue());
            Rows.Items = rows;
            Rows.SelectedIndex = Math.Max(0, Math.Min(selected, rows.Count - 1));
        }

        private RowData Describe(object item)
        {
            BasicSetting setting = item as BasicSetting;
            if (setting == null)
            {
                return new RowData { Title = "Continue", Subtitle = "Go to Silverdash360" };
            }
            return new RowData
            {
                Title = setting.Title,
                Subtitle = setting.Description,
                Detail = setting.Value(),
                New = BasicSettings.IsNew(setting, seen),
            };
        }

        private void GoBack()
        {
            if (NavigationService.CanGoBack)
            {
                NavigationService.GoBack();
            }
        }
    }
}
