using System.Collections.Generic;
using System.Threading;
using Silverdash360.Core;
using Silverdash360.Model;

namespace Silverdash360.Services
{
    /// <summary>
    /// Keeps the Marketplace's images for every title in the library, for use
    /// offline: after each refresh, a background thread goes through the
    /// titles, recently played first, and has <see cref="MarketplaceArt"/>
    /// keep what it hasn't yet. It stops when the server can't be reached
    /// and starts again after the next refresh.
    /// </summary>
    public sealed class MarketplaceArtCache
    {
        // Between titles, so the downloads leave the console to the UI.
        private const int PauseMs = 250;

        private readonly Library library;
        private bool running;

        public MarketplaceArtCache(Library library)
        {
            this.library = library;
            library.Progress += delegate
            {
                if (library.IsLoaded && !library.IsLoading && library.CurrentProgress == null)
                {
                    Start();
                }
            };
        }

        // On the UI thread.
        private void Start()
        {
            if (running)
            {
                return;
            }
            List<uint> titleIds = new List<uint>();
            Dictionary<uint, bool> listed = new Dictionary<uint, bool>();
            foreach (IList<TitleEntry> list in new[] { library.Recent, library.Games, library.Apps })
            {
                foreach (TitleEntry title in list)
                {
                    uint titleId = title.ArtTitleId;
                    if (MarketplaceArt.Covers(titleId) && !listed.ContainsKey(titleId))
                    {
                        listed[titleId] = true;
                        titleIds.Add(titleId);
                    }
                }
            }
            running = true;
            new Thread(delegate()
            {
                int done = 0;
                foreach (uint titleId in titleIds)
                {
                    UiIdle.WaitWhileMoving(UiIdle.BackgroundQuietMs);
                    if (!MarketplaceArt.CacheAll(titleId))
                    {
                        break;
                    }
                    done++;
                    Thread.Sleep(PauseMs);
                }
                Log.Write("Marketplace art kept for {0} of {1} titles", done, titleIds.Count);
                Activity.Art = "idle";
                Background.OnUi(() => running = false);
            }) { IsBackground = true, Name = "Marketplace art" }.Start();
        }
    }
}
