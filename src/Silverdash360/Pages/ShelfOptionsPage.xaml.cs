using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Navigation;
using Microsoft.Xbox.Controls;
using Silverdash360.Controls;
using Silverdash360.Model;
using Silverdash360.Modules;

namespace Silverdash360.Pages
{
    /// <summary>
    /// A shelf's filters, order and view: which kinds of title it shows, from
    /// which storage, sorted how, as covers or a list. A steps a setting on,
    /// X back; the shelf follows at once and keeps them.
    /// </summary>
    public partial class ShelfOptionsPage : XboxApplicationPage
    {
        private sealed class Setting
        {
            public string Title;
            public Func<List<ShelfOptions.Choice>> Choices;
            public Func<string> Get;
            public Action<string> Set;
        }

        private LibraryModule shelf;
        private List<Setting> settings = new List<Setting>();

        public ShelfOptionsPage()
        {
            InitializeComponent();
            Motion.UsePageTransitions(this);
            Items.Direction = ScrollDirection.Vertical;
            Items.Lines = 1;
            Items.TileWidth = 0;
            Items.TileHeight = 72;
            Items.Spacing = 8;
            Items.TileTemplate = new RowTemplate(o => Row((Setting)o)) { IconSize = 0 };
            Items.ItemInvoked += delegate(object sender, ItemEventArgs e) { Step((Setting)e.Item, 1); };
            Items.ItemButton += delegate(object sender, ItemEventArgs e)
            {
                if (e.Button == GamePadButton.X && e.Item != null)
                {
                    Step((Setting)e.Item, -1);
                    e.Handled = true;
                }
            };
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            string name;
            NavigationContext.QueryString.TryGetValue("shelf", out name);
            shelf = App.Modules.OfType<LibraryModule>().FirstOrDefault(m => m.Header == name);
            if (shelf == null)
            {
                return;
            }
            ShelfOptions options = shelf.Options;
            settings = new List<Setting>
            {
                new Setting
                {
                    Title = "Show",
                    Choices = () => options.ShowChoices(shelf.AllTitles),
                    Get = () => options.Show,
                    Set = v => options.Show = v,
                },
                new Setting
                {
                    Title = "Stored on",
                    Choices = () => options.LocationChoices(shelf.AllTitles),
                    Get = () => options.Location,
                    Set = v => options.Location = v,
                },
                new Setting
                {
                    Title = "Sort by",
                    Choices = () => ShelfOptions.Sorts,
                    Get = () => options.Sort,
                    Set = v => options.Sort = v,
                },
                new Setting
                {
                    Title = "View",
                    Choices = () => new List<ShelfOptions.Choice>
                    {
                        new ShelfOptions.Choice { Key = "covers", Name = "Covers" },
                        new ShelfOptions.Choice { Key = "list", Name = "List" },
                    },
                    Get = () => options.ListView ? "list" : "covers",
                    Set = v => options.ListView = v == "list",
                },
            };
            Show(0);
            FocusHelper.Focus(Items);
        }

        // Moves a setting to the next or previous of its choices.
        private void Step(Setting setting, int direction)
        {
            List<ShelfOptions.Choice> choices = setting.Choices();
            string current = setting.Get();
            ShelfOptions.Choice chosen = choices.FirstOrDefault(c => c.Key == current);
            int index = chosen != null ? choices.IndexOf(chosen) : -1;
            index = ((index < 0 ? 0 : index) + direction + choices.Count) % choices.Count;
            setting.Set(choices[index].Key);
            shelf.OptionsChanged();
            Show(Items.SelectedIndex);
        }

        private void Show(int selected)
        {
            ShelfText.Text = shelf.Header;
            int shown = shelf.Options.Apply(shelf.AllTitles).Count;
            CountText.Text = shown + (shown == 1 ? " title" : " titles");
            Items.Items = settings.Cast<object>().ToList();
            Items.SelectedIndex = Math.Max(0, selected);
        }

        private static RowData Row(Setting setting)
        {
            ShelfOptions.Choice choice = setting.Choices().FirstOrDefault(c => c.Key == setting.Get());
            return new RowData { Title = setting.Title, Detail = choice != null ? choice.Name : setting.Get() };
        }
    }
}
