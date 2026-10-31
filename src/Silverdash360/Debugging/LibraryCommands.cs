using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Silverdash360.Model;
using Silverdash360.Modules;
using Silverdash360.Services;

namespace Silverdash360.Debugging
{
    /// <summary>The library the dashboard shows: titles, content, search, refreshing.</summary>
    public sealed class LibraryCommands : IDebugCommands
    {
        private readonly DashboardContext context;

        public LibraryCommands(DashboardContext context)
        {
            this.context = context;
        }

        private Library Library { get { return context.Library; } }

        public void Register(DebugCommandSet commands)
        {
            commands.Add("library", "", "Whether it's loaded or loading, and how many titles and packages.", r => Status());
            commands.Add("library.titles", "[all|games|apps|recent] [offset] [count]", "Titles in the order the sections show them.", Titles);
            commands.Add("library.title", "<key|titleId>", "A title with its content and executables.", Title);
            commands.Add("library.content", "[offset] [count]", "Packages, with their header metadata.", Content);
            commands.Add("library.search", "<query>", "What search finds, best first.", Search);
            commands.Add("library.refresh", "[full]", "Starts a refresh; full reads every header again.", Refresh);
            commands.Add("library.wait", "[timeoutMs]", "Waits for a refresh to end.", Wait);
            commands.Add("cache.load", "", "Reads the library cache from storage, as at a start.", r => CacheLoad());
        }

        private JsonObject Status()
        {
            LibraryProgress progress = Library.CurrentProgress;
            return new JsonObject
            {
                { "loaded", Library.IsLoaded },
                { "loading", Library.IsLoading },
                { "progress", progress != null ? progress.Text : null },
                { "fraction", progress != null ? progress.Fraction : null },
                { "titles", Library.Titles.Count },
                { "games", Library.Games.Count },
                { "apps", Library.Apps.Count },
                { "recent", Library.Recent.Count },
                { "content", Library.AllContent.Count },
                { "gamer", Library.Gamer != null ? Library.Gamer.Gamertag : null },
            };
        }

        private object Titles(DebugRequest request)
        {
            string which = request.Arg(0, "all");
            IList<TitleEntry> titles;
            switch (which)
            {
                case "all": titles = Library.Titles; break;
                case "games": titles = Library.Games; break;
                case "apps": titles = Library.Apps; break;
                case "recent": titles = Library.Recent; break;
                default: throw new DebugException("Not a list: " + which);
            }
            int offset = request.Int(1, 0);
            int count = request.Int(2, 50);
            return new JsonObject
            {
                { "total", titles.Count },
                { "offset", offset },
                { "titles", titles.Skip(offset).Take(count).Select(t => TitleSummary(t)).ToList() },
            };
        }

        private object Title(DebugRequest request)
        {
            TitleEntry title = Find(context, request.Arg(0, "key or title ID", true));
            JsonObject result = TitleSummary(title);
            result.Add("publisher", title.Publisher);
            result.Add("description", title.Description);
            result.Add("lastPlayed", title.LastPlayed);
            result.Add("played", title.Played != null ? new JsonObject
            {
                { "achievements", title.Played.AchievementsUnlocked },
                { "achievementsTotal", title.Played.AchievementsTotal },
                { "gamerscore", title.Played.GamerscoreEarned },
                { "gamerscoreTotal", title.Played.GamerscoreTotal },
            } : null);
            result.Add("content", title.Content.Select(c => ContentSummary(c)).ToList());
            result.Add("executables", title.Executables.Select(e => new JsonObject
            {
                { "path", e.Path }, { "devicePath", e.DevicePath }, { "titleId", e.TitleId.ToString("X8") }, { "size", e.Size },
            }).ToList());
            return result;
        }

        private object Content(DebugRequest request)
        {
            IList<ContentItem> content = Library.AllContent;
            int offset = request.Int(0, 0);
            return new JsonObject
            {
                { "total", content.Count },
                { "offset", offset },
                { "content", content.Skip(offset).Take(request.Int(1, 50)).Select(c => ContentSummary(c)).ToList() },
            };
        }

        private object Search(DebugRequest request)
        {
            // The words, with any quotes the client put around them taken off.
            string query = string.Join(" ", request.Args.ToArray());
            int start = Environment.TickCount;
            List<TitleEntry> found = Library.Search(query);
            return new JsonObject
            {
                { "query", query },
                { "searchMs", Environment.TickCount - start },
                { "total", found.Count },
                { "titles", found.Take(50).Select(t => TitleSummary(t)).ToList() },
            };
        }

        private object Refresh(DebugRequest request)
        {
            bool full = request.Arg(0, "") == "full";
            bool wasLoading = Library.IsLoading;
            UiThread.Invoke(() => Library.Refresh(full));
            return new JsonObject { { "started", !wasLoading }, { "full", full } };
        }

        private object Wait(DebugRequest request)
        {
            int timeout = request.Int(0, 120000);
            int start = Environment.TickCount;
            while (Library.IsLoading)
            {
                if (Environment.TickCount - start > timeout)
                {
                    throw new DebugException("Still loading after " + timeout + " ms.");
                }
                Thread.Sleep(50);
            }
            JsonObject status = Status();
            status.Add("waitedMs", Environment.TickCount - start);
            return status;
        }

        private static object CacheLoad()
        {
            int start = Environment.TickCount;
            LibraryCache.Contents contents = new LibraryCache().Load();
            return new JsonObject
            {
                { "loadMs", Environment.TickCount - start },
                { "content", contents.Content.Count },
                { "withMetadata", contents.Content.Count(c => c.Metadata != null) },
                { "executables", contents.Executables.Count },
            };
        }

        /// <summary>A title by its key, or by its title ID in hex.</summary>
        public static TitleEntry Find(DashboardContext context, string keyOrId)
        {
            TitleEntry title = context.Library.Find(keyOrId);
            uint id;
            if (title == null && uint.TryParse(keyOrId.StartsWith("0x") ? keyOrId.Substring(2) : keyOrId,
                                               System.Globalization.NumberStyles.HexNumber, null, out id))
            {
                title = context.Library.Find(id);
            }
            if (title == null)
            {
                throw new DebugException("No title " + keyOrId + ".");
            }
            return title;
        }

        public static JsonObject TitleSummary(TitleEntry t)
        {
            return new JsonObject
            {
                { "key", t.Key },
                { "titleId", t.TitleIdText },
                { "name", t.Name },
                { "kind", t.Kind.ToString() },
                { "installed", t.IsInstalled },
                { "source", t.Source },
                { "content", t.Content.Count },
                { "executables", t.Executables.Count },
                { "size", t.TotalSize },
            };
        }

        public static JsonObject ContentSummary(ContentItem c)
        {
            ContentMetadata m = c.Metadata;
            return new JsonObject
            {
                { "titleId", c.TitleId.ToString("X8") },
                { "type", c.Type.ToString("X8") },
                { "typeName", c.TypeName },
                { "device", c.DeviceId.ToString("X8") },
                { "xuid", c.Xuid.ToString("X16") },
                { "fileName", c.FileName },
                { "displayName", c.DisplayName },
                { "size", c.BestSize },
                { "partial", c.IsPartial },
                { "corrupt", c.IsCorrupt },
                { "metadata", m == null ? null : new JsonObject
                    {
                        { "titleName", m.TitleName },
                        { "displayName", m.DisplayName },
                        { "publisher", m.Publisher },
                        { "version", m.VersionText },
                        { "mediaId", m.MediaId.ToString("X8") },
                        { "contentSize", m.ContentSize },
                    }
                },
            };
        }
    }
}
