using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Navigation;
using System.Windows.Threading;
using Microsoft.Xbox.Controls;
using Silverdash360.Controls;
using Silverdash360.Core;
using Silverdash360.Modules;
using Silverdash360.Native;
using Silverdash360.Services;

namespace Silverdash360.Pages
{
    /// <summary>
    /// The Xbox Dashboard's Console Settings, page by page as it has them
    /// (<see cref="ConsoleSettings"/>). Without a query, its categories;
    /// with ?group=, a category's items or a page an item opens; with
    /// ?setting=, a setting's choices; with ?trial=1, a display setting just
    /// tried, kept only if A is pressed in time.
    /// </summary>
    public partial class SystemSettingsPage : XboxApplicationPage, ISettingHost
    {
        private sealed class Category
        {
            public string Name;
            public string Description;
        }

        private sealed class Answer
        {
            public string Title;
            public string Detail;
            public Action Do;
        }

        private List<ConsoleSetting> settings;
        private string group;
        private ConsoleSetting chosen;
        private ConsoleSettings.Trial trial;
        private DispatcherTimer countdown;
        private int secondsLeft;
        private bool busy;

        public SystemSettingsPage()
        {
            InitializeComponent();
            Motion.UsePageTransitions(this);
            Rows.Direction = ScrollDirection.Vertical;
            Rows.Lines = 1;
            Rows.TileWidth = 0;
            Rows.TileHeight = 72;
            Rows.Spacing = 8;
            Rows.TileTemplate = new RowTemplate(Describe) { IconSize = 0 };
            Rows.ItemInvoked += delegate(object sender, ItemEventArgs e) { Invoke(e.Item); };
        }

        private static DashboardContext Context { get { return App.Context; } }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            NetworkSettings.Changed += OnNetworkChanged;
            if (NavigationContext.QueryString.ContainsKey("trial"))
            {
                ShowTrial();
            }
            else
            {
                NavigationContext.QueryString.TryGetValue("group", out group);
                string title;
                if (NavigationContext.QueryString.TryGetValue("setting", out title))
                {
                    chosen = Load().FirstOrDefault(s => s.Title == title);
                }
                Show();
            }
            FocusHelper.Focus(Rows);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            NetworkSettings.Changed -= OnNetworkChanged;
            if (countdown != null)
            {
                countdown.Stop();
            }
            // B leaves a trial as not kept.
            if (trial != null && !busy)
            {
                EndTrial(false);
            }
        }

        private List<ConsoleSetting> Load()
        {
            try
            {
                settings = ConsoleSettings.All();
            }
            catch (Exception error)
            {
                Log.Write("Console settings can't be read: {0}", error);
                settings = new List<ConsoleSetting>();
                Context.ShowStatus("The console's settings can't be read: " + error.Message);
            }
            return settings;
        }

        /// <summary>Shows the page as the settings are now, keeping the row selected.</summary>
        public void Refresh()
        {
            Load();
            if (chosen != null)
            {
                chosen = settings.FirstOrDefault(s => s.Title == chosen.Title) ?? chosen;
            }
            Show();
        }

        private void Show()
        {
            if (settings == null)
            {
                Load();
            }
            int selected = Rows.SelectedIndex;
            List<object> rows = new List<object>();
            if (chosen != null)
            {
                TitleText.Text = chosen.Title;
                IntroText.Text = chosen.Description + ". " + EffectText(chosen.Effect);
                rows.AddRange(chosen.Choices.Cast<object>());
                if (selected < 0)
                {
                    SettingChoice current = Safe(() => chosen.Current());
                    selected = current != null ? chosen.Choices.IndexOf(current) : 0;
                }
            }
            else if (group != null)
            {
                TitleText.Text = group;
                KeyValuePair<string, string> category = ConsoleSettings.Categories.FirstOrDefault(c => c.Key == group);
                IntroText.Text = category.Value ?? "";
                if (group == ConsoleSettings.TvSizePage)
                {
                    IntroText.Text = "Your TV is " + Safe(() => settings.First(s => s.Opens == group).ValueText()) + ".";
                }
                rows.AddRange(settings.Where(s => s.Group == group).Cast<object>());
            }
            else
            {
                TitleText.Text = "System settings";
                IntroText.Text = "The console's own settings, as the Xbox Dashboard's Console Settings have them.";
                foreach (KeyValuePair<string, string> category in ConsoleSettings.Categories)
                {
                    if (settings.Any(s => s.Group == category.Key))
                    {
                        rows.Add(new Category { Name = category.Key, Description = category.Value });
                    }
                }
            }
            Rows.Items = rows;
            Rows.SelectedIndex = Math.Max(0, Math.Min(selected, rows.Count - 1));
        }

        private static string EffectText(SettingEffect effect)
        {
            switch (effect)
            {
                case SettingEffect.RestartDashboard: return "Silverdash360 starts again to take it into use.";
                case SettingEffect.TestDisplay: return "Silverdash360 starts again in it, and goes back unless you keep it.";
                case SettingEffect.RestartConsole: return "The console restarts to take it into use.";
                case SettingEffect.NextStart: return "It's taken into use the next time the console starts.";
                case SettingEffect.Draft: return "";
                default: return "It's taken into use at once.";
            }
        }

        private static T Safe<T>(Func<T> read) where T : class
        {
            try
            {
                return read();
            }
            catch (Exception e)
            {
                Log.Write("A console setting can't be read: {0}", e.Message);
                return null;
            }
        }

        private RowData Describe(object item)
        {
            Category category = item as Category;
            if (category != null)
            {
                // What the Xbox Dashboard shows beside it: the values of its items.
                string values = string.Join(", ", settings.Where(s => s.Group == category.Name && s.Opens == null)
                                                          .Take(2).Select(s => Safe(() => s.ValueText())).Where(v => !string.IsNullOrEmpty(v)).ToArray());
                return new RowData { Title = category.Name, Subtitle = category.Description, Detail = values };
            }
            ConsoleSetting setting = item as ConsoleSetting;
            if (setting != null)
            {
                string disabled = Safe(() => setting.Disabled != null ? setting.Disabled() : null);
                return new RowData
                {
                    Title = setting.Title,
                    Subtitle = disabled ?? setting.Description,
                    Detail = Safe(() => setting.ValueText()),
                    Dimmed = disabled != null,
                };
            }
            SettingChoice choice = item as SettingChoice;
            if (choice != null)
            {
                SettingChoice current = Safe(() => chosen.Current());
                return new RowData
                {
                    Title = choice.Name,
                    Detail = current == choice ? "Current" : choice.Label ?? "",
                    Dimmed = choice.Unavailable,
                };
            }
            Answer answer = (Answer)item;
            return new RowData { Title = answer.Title, Detail = answer.Detail };
        }

        private void Invoke(object item)
        {
            if (busy)
            {
                return;
            }
            Category category = item as Category;
            if (category != null)
            {
                // A category with one setting is its choices, as Auto-Play is.
                List<ConsoleSetting> items = settings.Where(s => s.Group == category.Name).ToList();
                Open(items.Count == 1 && items[0].Choices != null ? "setting=" + Uri.EscapeDataString(items[0].Title)
                                                                  : "group=" + Uri.EscapeDataString(category.Name));
                return;
            }
            ConsoleSetting setting = item as ConsoleSetting;
            if (setting != null)
            {
                string disabled = Safe(() => setting.Disabled != null ? setting.Disabled() : null);
                if (disabled != null)
                {
                    Context.ShowStatus(disabled);
                }
                else if (setting.Run != null)
                {
                    setting.Run(this);
                }
                else if (setting.Opens != null)
                {
                    if (setting.OnOpen != null)
                    {
                        Safe(() => { setting.OnOpen(); return ""; });
                    }
                    Open("group=" + Uri.EscapeDataString(setting.Opens));
                }
                else
                {
                    Open("setting=" + Uri.EscapeDataString(setting.Title));
                }
                return;
            }
            SettingChoice choice = item as SettingChoice;
            if (choice != null)
            {
                Choose(choice);
                return;
            }
            Answer answer = item as Answer;
            if (answer != null)
            {
                answer.Do();
            }
        }

        private static void Open(string query)
        {
            Context.Navigate(new Uri("/Pages/SystemSettingsPage.xaml?" + query, UriKind.Relative));
        }

        // Takes the choice and does what it needs to take it into use, once
        // its question, if it has one, is answered yes.
        private void Choose(SettingChoice choice)
        {
            if (choice.Unavailable)
            {
                Context.ShowStatus("Your display doesn't take " + choice.Name + ".");
                return;
            }
            if (choice.Run != null)
            {
                choice.Run(this);
                return;
            }
            SettingChoice current = Safe(() => chosen.Current());
            if (current != null && current.Value == choice.Value)
            {
                Back();
                return;
            }
            if (chosen.Confirm != null)
            {
                // The Xbox Dashboard's message box: Yes and No, No first in
                // focus, and nothing written on No.
                TitleText.Text = chosen.Title;
                IntroText.Text = chosen.Confirm;
                Rows.Items = new List<object>
                {
                    new Answer { Title = "Yes", Do = () => Take(choice) },
                    new Answer { Title = "No", Do = Back },
                };
                Rows.SelectedIndex = 1;
                return;
            }
            Take(choice);
        }

        private void Take(SettingChoice choice)
        {
            try
            {
                if (chosen.Effect == SettingEffect.TestDisplay)
                {
                    ConsoleSettings.StartTrial(chosen, choice);
                }
                else
                {
                    chosen.Write(choice.Value);
                    if (chosen.Effect != SettingEffect.Draft)
                    {
                        Log.Write("Console setting {0}: {1} ({2:X8})", chosen.Title, choice.Name, choice.Value);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Write("Console setting {0} wasn't changed: {1}", chosen.Title, e);
                Context.ShowStatus(chosen.Title + " wasn't changed: " + e.Message);
                return;
            }
            switch (chosen.Effect)
            {
                case SettingEffect.RestartDashboard:
                case SettingEffect.TestDisplay:
                    Restart("Starting Silverdash360 again...", SystemService.RestartDashboard);
                    break;
                case SettingEffect.RestartConsole:
                    Restart("Restarting the console...", XConfig.Restart);
                    break;
                case SettingEffect.NextStart:
                    Context.ShowStatus(chosen.Title + ": " + choice.Name + ", from the next time the console starts.");
                    Back();
                    break;
                case SettingEffect.Draft:
                    Back();
                    break;
                default:
                    Context.ShowStatus(chosen.Title + ": " + choice.Name + ".");
                    Back();
                    break;
            }
        }

        // Says so, lets the screen show it, then restarts in the background.
        private void Restart(string text, Action restart)
        {
            busy = true;
            IntroText.Text = text;
            Context.ShowStatus(text);
            new Thread(delegate()
            {
                Thread.Sleep(500);
                restart();
            }) { IsBackground = true, Name = "Restart" }.Start();
        }

        public void Ask(string title, string description, string text, Action<string> done)
        {
            BasicSettings.Ask(Context, title, description, text, done);
        }

        public void Status(string text)
        {
            Context.ShowStatus(text);
        }

        public void Confirm(string title, string text, Action yes)
        {
            TitleText.Text = title;
            IntroText.Text = text;
            Rows.Items = new List<object>
            {
                new Answer { Title = "Yes", Do = yes },
                new Answer { Title = "No", Do = Show },
            };
            Rows.SelectedIndex = 1;
        }

        private void OnNetworkChanged()
        {
            // Not while a question is up: it would go.
            if (!(Rows.SelectedItem is Answer))
            {
                Refresh();
            }
        }

        public void Back()
        {
            if (NavigationService.CanGoBack)
            {
                NavigationService.GoBack();
            }
        }

        // A display setting just tried: kept with A on Keep, put back after
        // TrialSeconds or with Go back, as the Xbox Dashboard does, since a
        // display that can't show it shows nothing to press A on.
        private void ShowTrial()
        {
            trial = ConsoleSettings.PendingTrial();
            if (trial == null)
            {
                Back();
                return;
            }
            SettingChoice now = trial.Setting.Choices.FirstOrDefault(c => c.Value == trial.New);
            SettingChoice before = trial.Setting.Choices.FirstOrDefault(c => c.Value == trial.Old);
            TitleText.Text = "Display Settings";
            Legend.Text = "A select";
            Rows.Items = new List<object>
            {
                new Answer { Title = "Yes, keep the new settings", Detail = now != null ? now.Name : "", Do = () => EndTrial(true) },
                new Answer { Title = "No, go back", Detail = before != null ? before.Name : "", Do = () => EndTrial(false) },
            };
            Rows.SelectedIndex = 0;
            secondsLeft = ConsoleSettings.TrialSeconds;
            ShowCountdown();
            countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            countdown.Tick += delegate
            {
                secondsLeft--;
                ShowCountdown();
                if (secondsLeft <= 0)
                {
                    EndTrial(false);
                }
            };
            countdown.Start();
        }

        private void ShowCountdown()
        {
            IntroText.Text = string.Format("Your display settings have changed. Do you want to keep the new settings? " +
                                           "They go back in {0} s.", Math.Max(0, secondsLeft));
        }

        private void EndTrial(bool keep)
        {
            if (busy)
            {
                return;
            }
            countdown.Stop();
            if (keep)
            {
                try
                {
                    ConsoleSettings.KeepTrial(trial);
                }
                catch (Exception e)
                {
                    Log.Write("Console setting {0}: the timing wasn't kept: {1}", trial.Setting.Title, e);
                }
                Context.ShowStatus(trial.Setting.Title + " kept.");
                trial = null;
                Back();
                return;
            }
            bool retrying = false;
            try
            {
                retrying = ConsoleSettings.RevertTrial(trial);
            }
            catch (Exception e)
            {
                Log.Write("Console setting {0} wasn't put back: {1}", trial.Setting.Title, e);
            }
            Restart(retrying ? "Trying another way to show it, starting Silverdash360 again..."
                             : "Going back and starting Silverdash360 again...", SystemService.RestartDashboard);
        }
    }
}
