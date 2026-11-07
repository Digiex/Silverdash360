using System.Collections.Generic;

namespace Silverdash360.Controls
{
    /// <summary>
    /// The row a page's list had selected when it was left, selected again
    /// when B comes back to it, as the Xbox Dashboard does; a page opened
    /// afresh starts at the top. Pages are made again on each visit, so the
    /// rows are kept here, by the page's address. Every page opened afresh
    /// goes through <see cref="Modules.DashboardContext.Navigate"/>, which
    /// notes the page; going back doesn't.
    /// </summary>
    public static class BackSelection
    {
        private static readonly Dictionary<string, int> left = new Dictionary<string, int>();
        // Pages being opened afresh, by address, until they arrive.
        private static readonly List<string> opening = new List<string>();

        /// <summary>Marks a page about to be navigated to as opened afresh.</summary>
        public static void Opening(string page)
        {
            opening.Add(page);
        }

        /// <summary>
        /// On a page's OnNavigatedTo: the row it had when it was left if B
        /// brought it back, else -1.
        /// </summary>
        public static int Arrived(string page)
        {
            int row;
            if (!opening.Remove(page) && left.TryGetValue(page, out row))
            {
                return row;
            }
            left.Remove(page);
            return -1;
        }

        /// <summary>On a page's OnNavigatedFrom: the row its list has.</summary>
        public static void Leaving(string page, TileGrid grid)
        {
            if (page != null && grid.SelectedIndex >= 0)
            {
                left[page] = grid.SelectedIndex;
            }
        }

        /// <summary>
        /// Selects the row to come back to once the list has its items; true
        /// once it has (or there's none), so it's done once.
        /// </summary>
        public static bool Restore(TileGrid grid, ref int row)
        {
            if (row < 0)
            {
                return true;
            }
            if (grid.Items == null || grid.Items.Count == 0)
            {
                return false;
            }
            grid.SelectedIndex = System.Math.Min(row, grid.Items.Count - 1);
            row = -1;
            return true;
        }
    }
}
