using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Silverdash360.Controls
{
    /// <summary>
    /// A letter grid for typing with the controller. A types the selected key,
    /// X deletes, Y adds a space. <see cref="TextChanged"/> follows every key,
    /// so results can update as the user types.
    /// </summary>
    public sealed class OnScreenKeyboard : TileGrid
    {
        private const string Space = "space";
        private const string Delete = "del";
        private const string Clear = "clear";

        private string text = "";

        public OnScreenKeyboard()
        {
            Direction = ScrollDirection.Vertical;
            Lines = 10;
            TileWidth = 50;
            TileHeight = 50;
            Spacing = 6;
            LeadTiles = 0;
            TileTemplate = new KeyTemplate();
            List<object> keys = new List<object>();
            foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")
            {
                keys.Add(c.ToString());
            }
            keys.Add(Space);
            keys.Add(Delete);
            keys.Add(Clear);
            Items = keys;
            ItemInvoked += delegate(object sender, ItemEventArgs e) { Press((string)e.Item); };
            ItemButton += delegate(object sender, ItemEventArgs e)
            {
                if (e.Button == GamePadButton.X)
                {
                    Press(Delete);
                    e.Handled = true;
                }
                else if (e.Button == GamePadButton.Y)
                {
                    Press(Space);
                    e.Handled = true;
                }
            };
        }

        public event EventHandler TextChanged;

        public string Text
        {
            get { return text; }
            set
            {
                text = value ?? "";
                if (TextChanged != null)
                {
                    TextChanged(this, EventArgs.Empty);
                }
            }
        }

        private void Press(string key)
        {
            switch (key)
            {
                case Space: Text = text + " "; break;
                case Delete: Text = text.Length > 0 ? text.Substring(0, text.Length - 1) : ""; break;
                case Clear: Text = ""; break;
                default: Text = text + key; break;
            }
        }

        private sealed class KeyTemplate : TileTemplateBase
        {
            public KeyTemplate()
            {
                Grow = 1.1;
            }

            public override FrameworkElement Create(object item)
            {
                string key = (string)item;
                Grid tile = NewTile(Theme.Panel);
                TextBlock label = Theme.TextBlock(key, key.Length == 1 ? 24 : 14, Theme.Text);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.VerticalAlignment = VerticalAlignment.Center;
                tile.Children.Add(label);
                return Finish(tile);
            }

            protected override void OnSelected(FrameworkElement tile, bool selected)
            {
                ((Grid)tile).Background = selected ? Theme.Accent : Theme.Panel;
            }
        }
    }
}
