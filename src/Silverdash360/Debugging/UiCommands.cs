using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Xbox.Controls;
using Silverdash360.Controls;
using Silverdash360.Model;
using Silverdash360.Modules;
using Silverdash360.Pages;

namespace Silverdash360.Debugging
{
    /// <summary>
    /// Driving the dashboard and looking at what it shows. A controller's
    /// button events can't be made from code, so ui.press does what the
    /// focused grid, the hub or the frame does for the button.
    /// </summary>
    public sealed class UiCommands : IDebugCommands
    {
        private static readonly Dictionary<string, GamePadButton> Buttons = new Dictionary<string, GamePadButton>
        {
            { "up", GamePadButton.DPadUp }, { "down", GamePadButton.DPadDown },
            { "left", GamePadButton.DPadLeft }, { "right", GamePadButton.DPadRight },
            { "a", GamePadButton.A }, { "b", GamePadButton.B }, { "x", GamePadButton.X }, { "y", GamePadButton.Y },
            { "lb", GamePadButton.LeftShoulder }, { "rb", GamePadButton.RightShoulder },
            { "start", GamePadButton.Start }, { "back", GamePadButton.Back },
        };

        private readonly DashboardContext context;

        public UiCommands(DashboardContext context)
        {
            this.context = context;
        }

        private static XboxApplicationFrame Frame { get { return App.Frame; } }

        public void Register(DebugCommandSet commands)
        {
            commands.Add("ui.state", "", "The page, the hub's section, and the focused control and selection.", r => UiThread.Invoke(() => State()));
            commands.Add("ui.section", "<index|header>", "Shows a hub section.", Section);
            commands.Add("ui.navigate", "<hub|search|storage|title <key>>", "Goes to a page.", Navigate);
            commands.Add("ui.back", "", "Goes back a page.", r =>
            {
                UiThread.Invoke(() => Back());
                return WaitForPage();
            });
            commands.Add("ui.press", "<" + string.Join("|", Buttons.Keys.ToArray()) + "> [count]",
                         "Does what pressing a button does, count times a frame apart.", Press);
            commands.Add("ui.fps", "[ms]", "Frames drawn over a time (2000 ms by default).", Fps);
            commands.Add("ui.scroll", "<steps> [up|down|left|right]",
                         "Moves the focused grid's selection a step a frame and times the frames.", Scroll);
        }

        private JsonObject State()
        {
            JsonObject state = new JsonObject
            {
                { "page", Frame.Content != null ? Frame.Content.GetType().Name : null },
                { "uri", Frame.CurrentSource != null ? Frame.CurrentSource.OriginalString : null },
                { "canGoBack", Frame.CanGoBack },
                { "section", HubPage.CurrentSection },
                { "sectionHeader", App.Modules[HubPage.CurrentSection].Header },
                { "sections", App.Modules.Select(m => m.Header).ToList() },
            };
            object focused = FocusManager.GetFocusedElement();
            state.Add("focused", focused != null ? focused.GetType().Name : null);
            TileGrid grid = FocusedGrid();
            if (grid != null)
            {
                object item = grid.SelectedItem;
                TitleEntry title = item as TitleEntry;
                state.Add("grid", new JsonObject
                {
                    { "items", grid.Items.Count },
                    { "selected", grid.SelectedIndex },
                    { "selectedItem", title != null ? (object)LibraryCommands.TitleSummary(title) : item != null ? item.ToString() : null },
                });
            }
            return state;
        }

        private object Section(DebugRequest request)
        {
            string which = request.Arg(0, "index or header", true);
            UiThread.Invoke(() =>
            {
                HubPage hub = Hub();
                int index;
                if (!int.TryParse(which, out index))
                {
                    // Silverlight 3's List has no FindIndex.
                    IDashboardModule module = App.Modules.FirstOrDefault(m => string.Equals(m.Header, which, StringComparison.OrdinalIgnoreCase));
                    index = module != null ? App.Modules.IndexOf(module) : -1;
                }
                if (index < 0 || index >= App.Modules.Count)
                {
                    throw new DebugException("No section " + which + ".");
                }
                hub.ShowSection(index);
            });
            return Settled();
        }

        private object Navigate(DebugRequest request)
        {
            string page = request.Arg(0, "page", true).ToLowerInvariant();
            UiThread.Invoke(() =>
            {
                switch (page)
                {
                    case "hub": context.Navigate(new Uri("/Pages/HubPage.xaml", UriKind.Relative)); break;
                    case "search": context.OpenSearch(); break;
                    case "storage": context.OpenStorage(); break;
                    case "title": context.OpenTitle(LibraryCommands.Find(context, request.Arg(1, "title", true))); break;
                    default: throw new DebugException("No page " + page + ".");
                }
            });
            return WaitForPage();
        }

        private static void Back()
        {
            if (!Frame.CanGoBack)
            {
                throw new DebugException("There's no page to go back to.");
            }
            Frame.GoBack();
        }

        private object Press(DebugRequest request)
        {
            string name = request.Arg(0, "button", true).ToLowerInvariant();
            GamePadButton button;
            if (!Buttons.TryGetValue(name, out button))
            {
                throw new DebugException("No button " + name + ".");
            }
            int count = Math.Max(1, request.Int(1, 1));
            int handled = 0;
            for (int i = 0; i < count; i++)
            {
                if (UiThread.Invoke(() => PressOnce(button)))
                {
                    handled++;
                }
                // A frame between presses, as a held button repeats.
                Thread.Sleep(20);
            }
            JsonObject result = Settled();
            result.Add("handled", handled);
            return result;
        }

        // The focused grid first, then the hub, then the frame's B for back,
        // as the button's event would go.
        private bool PressOnce(GamePadButton button)
        {
            TileGrid grid = FocusedGrid();
            if (grid != null && grid.Press(button))
            {
                return true;
            }
            HubPage hub = Frame.Content as HubPage;
            if (hub != null && hub.Press(button))
            {
                return true;
            }
            if (button == GamePadButton.B && Frame.CanGoBack)
            {
                Frame.GoBack();
                return true;
            }
            return false;
        }

        private object Fps(DebugRequest request)
        {
            int duration = Math.Max(100, request.Int(0, 2000));
            FrameTimer timer = new FrameTimer();
            UiThread.Invoke(() => timer.Start());
            Thread.Sleep(duration);
            UiThread.Invoke(() => timer.Stop());
            return timer.Result(duration);
        }

        private object Scroll(DebugRequest request)
        {
            int steps = Math.Max(1, request.Int(0, 50));
            string direction = request.Arg(1, "right");
            GamePadButton button;
            if (!Buttons.TryGetValue(direction, out button))
            {
                throw new DebugException("No direction " + direction + ".");
            }
            int start = UiThread.Invoke(() =>
            {
                TileGrid grid = FocusedGrid();
                if (grid == null)
                {
                    throw new DebugException("No grid has focus.");
                }
                return grid.SelectedIndex;
            });
            FrameTimer timer = new FrameTimer { Step = () => FocusedGrid() != null && FocusedGrid().Press(button), Steps = steps };
            int began = Environment.TickCount;
            UiThread.Invoke(() => timer.Start());
            while (!timer.Finished && Environment.TickCount - began < 60000)
            {
                Thread.Sleep(20);
            }
            // Let the last animation settle into the frame times.
            Thread.Sleep(300);
            UiThread.Invoke(() => timer.Stop());
            JsonObject result = timer.Result(Environment.TickCount - began);
            result.Add("from", start);
            result.Add("to", UiThread.Invoke(() => FocusedGrid() != null ? FocusedGrid().SelectedIndex : -1));
            return result;
        }

        private JsonObject WaitForPage()
        {
            // Navigation finishes on a later frame.
            Thread.Sleep(100);
            int start = Environment.TickCount;
            while (UiThread.Invoke(() => Frame.Content == null) && Environment.TickCount - start < 5000)
            {
                Thread.Sleep(50);
            }
            Thread.Sleep(200);
            return Settled();
        }

        // The state once something has focus: the dashboard gives focus a
        // few frames after a page or section is shown.
        private JsonObject Settled()
        {
            int start = Environment.TickCount;
            JsonObject state;
            while (true)
            {
                Thread.Sleep(50);
                state = UiThread.Invoke(() => State());
                if (UiThread.Invoke(() => FocusManager.GetFocusedElement() != null) || Environment.TickCount - start > 1500)
                {
                    return state;
                }
            }
        }

        private static HubPage Hub()
        {
            HubPage hub = Frame.Content as HubPage;
            if (hub == null)
            {
                throw new DebugException("The hub isn't shown.");
            }
            return hub;
        }

        private static TileGrid FocusedGrid()
        {
            DependencyObject element = FocusManager.GetFocusedElement() as DependencyObject;
            while (element != null && !(element is TileGrid))
            {
                element = VisualTreeHelper.GetParent(element);
            }
            return (TileGrid)element;
        }

        /// <summary>Counts frames and times the gaps between them, on the UI thread.</summary>
        private sealed class FrameTimer
        {
            private readonly List<int> gaps = new List<int>();
            private int last;
            private int stepped;
            private volatile bool finished;

            /// <summary>Done once a frame until it returns false or Steps are done.</summary>
            public Func<bool> Step;
            public int Steps;

            public bool Finished { get { return finished; } }

            public void Start()
            {
                last = Environment.TickCount;
                CompositionTarget.Rendering += OnRendering;
            }

            public void Stop()
            {
                CompositionTarget.Rendering -= OnRendering;
                finished = true;
            }

            private void OnRendering(object sender, EventArgs e)
            {
                int now = Environment.TickCount;
                gaps.Add(now - last);
                last = now;
                if (Step != null && !finished)
                {
                    if (stepped >= Steps || !Step())
                    {
                        finished = true;
                    }
                    else
                    {
                        stepped++;
                    }
                }
            }

            public JsonObject Result(int milliseconds)
            {
                List<int> sorted = gaps.OrderBy(g => g).ToList();
                return new JsonObject
                {
                    { "ms", milliseconds },
                    { "frames", gaps.Count },
                    { "fps", milliseconds > 0 ? gaps.Count * 1000.0 / milliseconds : 0 },
                    { "steps", stepped },
                    { "frameMsAverage", gaps.Count > 0 ? gaps.Average() : 0 },
                    { "frameMsMedian", sorted.Count > 0 ? sorted[sorted.Count / 2] : 0 },
                    { "frameMs95", sorted.Count > 0 ? sorted[Math.Min(sorted.Count - 1, sorted.Count * 95 / 100)] : 0 },
                    { "frameMsMax", sorted.Count > 0 ? sorted[sorted.Count - 1] : 0 },
                    { "framesOver50ms", gaps.Count(g => g > 50) },
                };
            }
        }
    }
}
