using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Silverdash360.Controls
{
    /// <summary>Makes the visuals for a <see cref="TileGrid"/>'s items.</summary>
    public interface ITileTemplate
    {
        FrameworkElement Create(object item);

        /// <summary>Shows or hides the selection on a tile made by <see cref="Create"/>.</summary>
        void SetSelected(FrameworkElement tile, bool selected, bool focused);
    }

    public enum ScrollDirection
    {
        /// <summary>Rows of tiles that scroll sideways, like a cover view.</summary>
        Horizontal,
        /// <summary>Columns of tiles that scroll down, like a list.</summary>
        Vertical,
    }

    public enum Edge { Left, Right, Top, Bottom }

    public class EdgeEventArgs : EventArgs
    {
        public EdgeEventArgs(Edge edge) { Edge = edge; }
        public Edge Edge { get; private set; }
        public bool Handled { get; set; }
    }

    public class ItemEventArgs : EventArgs
    {
        public ItemEventArgs(object item, GamePadButton button) { Item = item; Button = button; }
        public object Item { get; private set; }
        public GamePadButton Button { get; private set; }
        public bool Handled { get; set; }
    }

    /// <summary>
    /// A grid of tiles moved through with the D-pad and the left stick. It takes
    /// focus as one control and draws the selection itself, and only makes
    /// tiles for the part that's on screen, so large libraries stay fast.
    /// A opens the selected item (<see cref="ItemInvoked"/>); other buttons
    /// are offered to the page through <see cref="ItemButton"/>.
    /// </summary>
    public class TileGrid : UserControl
    {
        private const double StickThreshold = 0.55;
        private static readonly Duration ScrollTime = new Duration(TimeSpan.FromMilliseconds(180));

        private readonly Canvas viewport = new Canvas();
        private readonly Canvas surface = new Canvas();
        private readonly TranslateTransform scroll = new TranslateTransform();
        private readonly TextBlock emptyText = new TextBlock();
        private readonly Dictionary<int, FrameworkElement> realized = new Dictionary<int, FrameworkElement>();
        private readonly Storyboard scrollAnimation = new Storyboard();
        private readonly DoubleAnimation scrollStep = new DoubleAnimation();

        private IList<object> items = new List<object>();
        private int selectedIndex = -1;
        private double offset;
        private bool focused;
        private double padOutside;
        private int stickDirection;
        private DateTime stickLast;
        private int stickRepeats;

        public TileGrid()
        {
            IsTabStop = true;
            TileWidth = 200;
            TileHeight = 200;
            Spacing = 16;
            Lines = 1;
            Direction = ScrollDirection.Horizontal;
            LeadTiles = 1;

            viewport.Clip = new RectangleGeometry();
            surface.RenderTransform = scroll;
            surface.CacheMode = new BitmapCache();
            viewport.Children.Add(surface);
            emptyText.FontSize = 24;
            emptyText.Opacity = 0.7;
            emptyText.Foreground = new SolidColorBrush(Colors.White);
            emptyText.TextWrapping = TextWrapping.Wrap;
            viewport.Children.Add(emptyText);
            Content = viewport;

            scrollStep.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            scrollStep.Duration = ScrollTime;
            Storyboard.SetTarget(scrollStep, scroll);
            scrollAnimation.Children.Add(scrollStep);

            SizeChanged += delegate
            {
                viewport.Width = ActualWidth;
                viewport.Height = ActualHeight;
                UpdateClip();
                emptyText.Width = ActualWidth;
                if (TileWidth <= 0)
                {
                    // Tiles as wide as the grid follow its width.
                    DropTiles();
                }
                Realize();
            };
            GotFocus += delegate { focused = true; RefreshSelection(); };
            LostFocus += delegate { focused = false; RefreshSelection(); };
            GamePadButtonDown += OnButtonDown;
            GamePadThumbStickMoved += OnThumbStickMoved;
        }

        /// <summary>Room around the tiles for a selected tile to grow into.</summary>
        public double PadOutside
        {
            get { return padOutside; }
            set
            {
                padOutside = value;
                UpdateClip();
            }
        }

        private void UpdateClip()
        {
            ((RectangleGeometry)viewport.Clip).Rect = new Rect(-padOutside, -padOutside,
                ActualWidth + 2 * padOutside, ActualHeight + 2 * padOutside);
        }

        /// <summary>Tile width; 0 makes a one-column list as wide as the grid.</summary>
        public double TileWidth { get; set; }

        private double Width0
        {
            get { return TileWidth > 0 ? TileWidth : Math.Max(1, ActualWidth); }
        }
        public double TileHeight { get; set; }
        public double Spacing { get; set; }

        /// <summary>Rows when scrolling sideways, columns when scrolling down.</summary>
        public int Lines { get; set; }

        public ScrollDirection Direction { get; set; }

        /// <summary>How many tiles to keep visible past the selection when scrolling.</summary>
        public int LeadTiles { get; set; }

        public ITileTemplate TileTemplate { get; set; }

        public string EmptyText
        {
            get { return emptyText.Text; }
            set { emptyText.Text = value; }
        }

        public event EventHandler SelectionChanged;
        public event EventHandler<ItemEventArgs> ItemInvoked;
        /// <summary>A button other than A or the D-pad, with the selected item.</summary>
        public event EventHandler<ItemEventArgs> ItemButton;
        /// <summary>The D-pad pushed past the edge, for moving to another control.</summary>
        public event EventHandler<EdgeEventArgs> EdgeReached;

        public IList<object> Items
        {
            get { return items; }
            set
            {
                items = value ?? new List<object>();
                DropTiles();
                emptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                selectedIndex = -1;
                offset = 0;
                scrollAnimation.Stop();
                scroll.X = scroll.Y = 0;
                if (items.Count > 0)
                {
                    Select(0, false);
                }
                Realize();
            }
        }

        private void DropTiles()
        {
            foreach (FrameworkElement tile in realized.Values)
            {
                surface.Children.Remove(tile);
            }
            realized.Clear();
        }

        public int SelectedIndex
        {
            get { return selectedIndex; }
            set { Select(value, false); }
        }

        public object SelectedItem
        {
            get { return selectedIndex >= 0 && selectedIndex < items.Count ? items[selectedIndex] : null; }
        }

        /// <summary>Selects an item without animating, such as when coming back to a page.</summary>
        public void SelectItem(object item)
        {
            int index = items.IndexOf(item);
            if (index >= 0)
            {
                Select(index, false);
            }
        }

        private double StepX { get { return Width0 + Spacing; } }
        private double StepY { get { return TileHeight + Spacing; } }

        private int LineCount
        {
            get { return (items.Count + Lines - 1) / Lines; }
        }

        private Point PositionOf(int index)
        {
            int line = index / Lines;
            int within = index % Lines;
            return Direction == ScrollDirection.Horizontal
                ? new Point(line * StepX, within * StepY)
                : new Point(within * StepX, line * StepY);
        }

        private void Select(int index, bool animate)
        {
            if (items.Count == 0)
            {
                return;
            }
            index = Math.Max(0, Math.Min(items.Count - 1, index));
            int previous = selectedIndex;
            selectedIndex = index;
            ScrollTo(index, animate);
            FrameworkElement tile;
            if (previous >= 0 && realized.TryGetValue(previous, out tile))
            {
                TileTemplate.SetSelected(tile, false, focused);
                Canvas.SetZIndex(tile, 0);
            }
            if (realized.TryGetValue(index, out tile))
            {
                TileTemplate.SetSelected(tile, true, focused);
                Canvas.SetZIndex(tile, 1);
            }
            if (previous != index && SelectionChanged != null)
            {
                SelectionChanged(this, EventArgs.Empty);
            }
        }

        private void RefreshSelection()
        {
            FrameworkElement tile;
            if (selectedIndex >= 0 && realized.TryGetValue(selectedIndex, out tile))
            {
                TileTemplate.SetSelected(tile, true, focused);
            }
        }

        // Keeps LeadTiles tiles visible past the selection where there are any.
        private void ScrollTo(int index, bool animate)
        {
            bool horizontal = Direction == ScrollDirection.Horizontal;
            double step = horizontal ? StepX : StepY;
            double size = horizontal ? Width0 : TileHeight;
            double view = horizontal ? ActualWidth : ActualHeight;
            if (view <= 0)
            {
                return;
            }
            double position = (index / Lines) * step;
            double total = LineCount * step - Spacing;
            double lead = LeadTiles * step;
            double target = offset;
            if (position - lead < target)
            {
                target = position - lead;
            }
            else if (position + size + lead > target + view)
            {
                target = position + size + lead - view;
            }
            target = Math.Max(0, Math.Min(target, Math.Max(0, total - view)));
            if (target == offset)
            {
                Realize();
                return;
            }
            double from = offset;
            offset = target;
            Realize(Math.Min(from, target), Math.Max(from, target));
            scrollAnimation.Stop();
            if (horizontal)
            {
                scroll.X = -from;
                Storyboard.SetTargetProperty(scrollStep, new PropertyPath("X"));
            }
            else
            {
                scroll.Y = -from;
                Storyboard.SetTargetProperty(scrollStep, new PropertyPath("Y"));
            }
            if (!animate)
            {
                if (horizontal) scroll.X = -target; else scroll.Y = -target;
                return;
            }
            scrollStep.From = -from;
            scrollStep.To = -target;
            scrollAnimation.Begin();
        }

        private void Realize()
        {
            Realize(offset, offset);
        }

        // Makes the tiles between the two scroll offsets and a screen around
        // them, and drops the rest.
        private void Realize(double fromOffset, double toOffset)
        {
            if (TileTemplate == null || items.Count == 0)
            {
                return;
            }
            bool horizontal = Direction == ScrollDirection.Horizontal;
            double step = horizontal ? StepX : StepY;
            double view = horizontal ? ActualWidth : ActualHeight;
            if (view <= 0)
            {
                view = horizontal ? 1280 : 720;
            }
            int firstLine = Math.Max(0, (int)Math.Floor((fromOffset - step) / step));
            int lastLine = Math.Min(LineCount - 1, (int)Math.Ceiling((toOffset + view + step) / step));
            int first = firstLine * Lines;
            int last = Math.Min(items.Count - 1, (lastLine + 1) * Lines - 1);

            List<int> drop = new List<int>();
            foreach (int index in realized.Keys)
            {
                if (index < first || index > last)
                {
                    drop.Add(index);
                }
            }
            foreach (int index in drop)
            {
                surface.Children.Remove(realized[index]);
                realized.Remove(index);
            }
            for (int index = first; index <= last; index++)
            {
                if (realized.ContainsKey(index))
                {
                    continue;
                }
                FrameworkElement tile = TileTemplate.Create(items[index]);
                tile.Width = Width0;
                tile.Height = TileHeight;
                Point at = PositionOf(index);
                Canvas.SetLeft(tile, at.X);
                Canvas.SetTop(tile, at.Y);
                TileTemplate.SetSelected(tile, index == selectedIndex, focused);
                Canvas.SetZIndex(tile, index == selectedIndex ? 1 : 0);
                surface.Children.Add(tile);
                realized.Add(index, tile);
            }
        }

        private void OnButtonDown(object sender, GamePadButtonEventArgs e)
        {
            e.Handled = Press(e.Button);
        }

        /// <summary>Does what a press of the button does; false when nothing took it.</summary>
        public bool Press(GamePadButton button)
        {
            switch (button)
            {
                case GamePadButton.DPadLeft: return Move(-1, 0);
                case GamePadButton.DPadRight: return Move(1, 0);
                case GamePadButton.DPadUp: return Move(0, -1);
                case GamePadButton.DPadDown: return Move(0, 1);
                case GamePadButton.A:
                    if (SelectedItem != null && ItemInvoked != null)
                    {
                        ItemInvoked(this, new ItemEventArgs(SelectedItem, button));
                        return true;
                    }
                    return false;
                default:
                    if (ItemButton != null)
                    {
                        ItemEventArgs args = new ItemEventArgs(SelectedItem, button);
                        ItemButton(this, args);
                        return args.Handled;
                    }
                    return false;
            }
        }

        // One move per push, then repeats that speed up while it's held.
        private void OnThumbStickMoved(object sender, GamePadThumbStickMovedEventArgs e)
        {
            if (e.Side != GamePadSide.Left)
            {
                return;
            }
            int direction = 0;
            if (Math.Abs(e.XPosition) >= Math.Abs(e.YPosition))
            {
                if (e.XPosition > StickThreshold) direction = 2;
                else if (e.XPosition < -StickThreshold) direction = 1;
            }
            else
            {
                if (e.YPosition > StickThreshold) direction = 3;
                else if (e.YPosition < -StickThreshold) direction = 4;
            }
            if (direction == 0)
            {
                stickDirection = 0;
                return;
            }
            DateTime now = DateTime.Now;
            if (direction == stickDirection)
            {
                double wait = stickRepeats == 0 ? 400 : Math.Max(70, 160 - stickRepeats * 15);
                if ((now - stickLast).TotalMilliseconds < wait)
                {
                    return;
                }
                stickRepeats++;
            }
            else
            {
                stickRepeats = 0;
            }
            stickDirection = direction;
            stickLast = now;
            switch (direction)
            {
                case 1: e.Handled = Move(-1, 0); break;
                case 2: e.Handled = Move(1, 0); break;
                case 3: e.Handled = Move(0, -1); break;
                case 4: e.Handled = Move(0, 1); break;
            }
        }

        /// <summary>Moves the selection by tiles on screen; false at an edge nobody took.</summary>
        public bool Move(int dx, int dy)
        {
            if (items.Count == 0)
            {
                return RaiseEdge(dx < 0 ? Edge.Left : dx > 0 ? Edge.Right : dy < 0 ? Edge.Top : Edge.Bottom);
            }
            // Along the scroll direction a step is a whole line of tiles.
            bool horizontal = Direction == ScrollDirection.Horizontal;
            int along = horizontal ? dx : dy;
            int across = horizontal ? dy : dx;
            int line = selectedIndex / Lines;
            int within = selectedIndex % Lines;
            if (along != 0)
            {
                int target = (line + along) * Lines + within;
                if (line + along < 0 || line + along >= LineCount)
                {
                    return RaiseEdge(horizontal ? (along < 0 ? Edge.Left : Edge.Right) : (along < 0 ? Edge.Top : Edge.Bottom));
                }
                Select(Math.Min(target, items.Count - 1), true);
                return true;
            }
            int next = within + across;
            if (next < 0 || next >= Lines || line * Lines + next >= items.Count)
            {
                return RaiseEdge(horizontal ? (across < 0 ? Edge.Top : Edge.Bottom) : (across < 0 ? Edge.Left : Edge.Right));
            }
            Select(line * Lines + next, true);
            return true;
        }

        private bool RaiseEdge(Edge edge)
        {
            if (EdgeReached == null)
            {
                return false;
            }
            EdgeEventArgs args = new EdgeEventArgs(edge);
            EdgeReached(this, args);
            return args.Handled;
        }
    }
}
