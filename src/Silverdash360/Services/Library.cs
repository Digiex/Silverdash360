using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Silverdash360.Core;
using Silverdash360.Model;

namespace Silverdash360.Services
{
    /// <summary>
    /// Every title on the console: the content found on its storage, joined
    /// with the signed-in profile's played titles. Loads in the background and
    /// raises <see cref="Changed"/> on the UI thread when done.
    /// </summary>
    public sealed class Library
    {
        private readonly IContentSource source;
        private readonly ProfileService profiles;
        private readonly LibraryCache cache;
        private readonly ExecutableScanner executables;
        // Pauses between steps of a background refresh, in milliseconds. A
        // refresh with nothing cached runs flat out; one behind a library
        // already on screen leaves the console to the UI.
        private const int PaceEnumeration = 40;
        private const int PaceHeader = 60;
        private const int PaceExecutable = 15;
        private const int PaceSave = 40;
        // Behind the recent titles of a first run, headers are read with a
        // shorter pause: there are many and nothing else is on screen yet.
        private const int PaceHeaderBehindRecent = 10;
        // The recent titles a first run puts up before reading everything.
        private const int RecentFirst = 12;

        private LoadResult current = LoadResult.Empty;

        public Library(IContentSource source, ProfileService profiles, LibraryCache cache, ExecutableScanner executables)
        {
            this.source = source;
            this.profiles = profiles;
            this.cache = cache;
            this.executables = executables;
            Classifiers = new List<ITitleClassifier>
            {
                new VideoOnlyClassifier(), new KnownAppsClassifier(), new FolderClassifier(), new ContentClassifier(),
            };
        }

        /// <summary>Asked in order to tell games from apps.</summary>
        public List<ITitleClassifier> Classifiers { get; private set; }

        public event EventHandler Changed;

        /// <summary>How far a refresh has got, on the UI thread.</summary>
        public event EventHandler<LibraryProgressEventArgs> Progress;

        /// <summary>The refresh's latest step, or null when none is running.</summary>
        public LibraryProgress CurrentProgress { get; private set; }

        public bool IsLoading { get; private set; }
        public bool IsLoaded { get; private set; }

        /// <summary>The profile the played titles came from, or null.</summary>
        public Gamer Gamer { get; private set; }

        // The lists are sorted once, in the background, when the library is
        // read: with thousands of titles, sorting on every look slowed the UI.
        public IList<TitleEntry> Titles { get { return current.Titles; } }
        public IList<TitleEntry> Games { get { return current.Games; } }
        public IList<TitleEntry> Apps { get { return current.Apps; } }

        /// <summary>Played titles, most recent first, then installed ones.</summary>
        public IList<TitleEntry> Recent { get { return current.Recent; } }

        public IList<ContentItem> AllContent { get { return current.Content; } }

        public TitleEntry Find(uint titleId)
        {
            TitleEntry title;
            return current.ById.TryGetValue(titleId, out title) ? title : null;
        }

        /// <summary>The title with a <see cref="TitleEntry.Key"/>.</summary>
        public TitleEntry Find(string key)
        {
            TitleEntry title;
            return current.ByKey.TryGetValue(key, out title) ? title : null;
        }

        /// <summary>
        /// Games and apps whose name, publisher or title ID contains every
        /// word of the query, those starting with the first word first, then
        /// by name.
        /// </summary>
        public List<TitleEntry> Search(string query)
        {
            string[] words = (query ?? "").ToLowerInvariant()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return current.Search.Find(words);
        }

        /// <summary>Reads the library again in the background, using the cache.</summary>
        public void Refresh()
        {
            Refresh(false);
        }

        /// <summary>
        /// Reads the library again in the background. A full refresh forgets
        /// the cache and reads every package header again; otherwise only new
        /// packages are read, and on the first refresh the cached library is
        /// shown while that happens.
        /// </summary>
        public void Refresh(bool full)
        {
            if (IsLoading)
            {
                return;
            }
            IsLoading = true;
            Background.Run(() => Load(full), delegate(LoadResult result, Exception error)
            {
                IsLoading = false;
                Publish(result);
                OnProgress(null);
            });
        }

        private sealed class LoadResult
        {
            public static readonly LoadResult Empty = new LoadResult
            {
                Titles = new List<TitleEntry>(),
                Games = new List<TitleEntry>(),
                Apps = new List<TitleEntry>(),
                Recent = new List<TitleEntry>(),
                Content = new List<ContentItem>(),
                ById = new Dictionary<uint, TitleEntry>(),
                ByKey = new Dictionary<string, TitleEntry>(),
                Search = SearchIndex.Empty,
            };

            public List<TitleEntry> Titles;
            public List<TitleEntry> Games;
            public List<TitleEntry> Apps;
            public List<TitleEntry> Recent;
            public List<ContentItem> Content;
            public Dictionary<uint, TitleEntry> ById;
            public Dictionary<string, TitleEntry> ByKey;
            public SearchIndex Search;
            public Gamer Gamer;
        }

        // A null result is a refresh that found what's shown already.
        private void Publish(LoadResult result)
        {
            if (result == null)
            {
                return;
            }
            current = result;
            Gamer = result.Gamer;
            IsLoaded = true;
            if (Changed != null)
            {
                Changed(this, EventArgs.Empty);
            }
        }

        // On the UI thread; null when the refresh is over.
        private void OnProgress(LibraryProgress progress)
        {
            CurrentProgress = progress;
            if (Progress != null)
            {
                Progress(this, new LibraryProgressEventArgs(progress));
            }
        }

        private void Report(string text, double? fraction)
        {
            LibraryProgress progress = new LibraryProgress { Text = text, Fraction = fraction };
            Background.OnUi(() => OnProgress(progress));
        }

        private LoadResult Load(bool full)
        {
            DateTime started = DateTime.UtcNow;
            Gamer gamer = profiles.SignedIn().FirstOrDefault();
            List<PlayedTitle> played = gamer != null ? profiles.PlayedTitles(gamer) : new List<PlayedTitle>();

            // What earlier runs read from package and executable headers.
            Dictionary<string, ContentMetadata> known = new Dictionary<string, ContentMetadata>();
            Dictionary<string, ExecutableItem> knownExecutables = new Dictionary<string, ExecutableItem>();
            int cachedCount = 0;
            bool shown = IsLoaded;
            if (full)
            {
                cache.Clear();
            }
            else
            {
                Report("Opening the saved library...", null);
                LibraryCache.Contents cached = cache.Load();
                foreach (ContentItem item in cached.Content)
                {
                    if (item.Metadata != null)
                    {
                        known[LibraryCache.KeyOf(item)] = item.Metadata;
                    }
                }
                foreach (ExecutableItem item in cached.Executables)
                {
                    knownExecutables[item.Path] = item;
                }
                cachedCount = cached.Content.Count;
                if (cached.Content.Count > 0 || cached.Executables.Count > 0)
                {
                    if (!IsLoaded)
                    {
                        LoadResult quick = Build(cached.Content, cached.Executables, gamer, played);
                        Background.OnUi(() => Publish(quick));
                    }
                    shown = true;
                }
            }

            // With nothing saved, as on a first run, the profile and its most
            // recent titles go up first, so there's something to start while
            // everything else is read.
            bool recentShown = false;
            if (!shown)
            {
                LoadResult recent = ReadRecentFirst(gamer, played);
                Background.OnUi(() => Publish(recent));
                recentShown = true;
            }

            // With the library on screen, the rest of the refresh can take its
            // time; the console is a lot slower than a PC running xenia.
            Action pace = shown ? () => Thread.Sleep(PaceEnumeration) : (Action)null;
            Action paceHeader = shown ? () => Thread.Sleep(PaceHeader)
                : recentShown ? () => Thread.Sleep(PaceHeaderBehindRecent) : (Action)null;
            Action paceExecutable = shown ? () => Thread.Sleep(PaceExecutable) : (Action)null;
            Action paceSave = shown ? () => Thread.Sleep(PaceSave) : (Action)null;

            // Common content (games, apps, add-ons) is stored under XUID 0, the
            // profile's own (saves, its themes) under its offline XUID.
            Report("Looking for content...", null);
            List<ContentItem> content = source.Enumerate(0, count => Report("Looking for content... " + count + " found", null), pace);
            if (gamer != null)
            {
                content.AddRange(source.Enumerate(gamer.OfflineXuid, null, pace));
            }
            // Once each: a package reported twice would show twice.
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            content = content.Where(c =>
            {
                string key = LibraryCache.KeyOf(c);
                if (c.TitleId == 0 || seen.ContainsKey(key))
                {
                    return false;
                }
                seen[key] = true;
                return true;
            }).ToList();

            Report("Looking in the Apps and Games folders...", null);
            List<ExecutableItem> found = executables.Scan(knownExecutables, paceExecutable);

            // Headers have the names, description, publisher and art. Saves
            // have little worth the read.
            List<ContentItem> unread = new List<ContentItem>();
            foreach (ContentItem item in content.Where(c => c.Type != ContentType.SavedGame))
            {
                ContentMetadata metadata;
                if (known.TryGetValue(LibraryCache.KeyOf(item), out metadata))
                {
                    item.Metadata = metadata;
                }
                else
                {
                    unread.Add(item);
                }
            }
            int reported = -1;
            for (int i = 0; i < unread.Count; i++)
            {
                int percent = (i * 100) / unread.Count;
                if (percent != reported)
                {
                    reported = percent;
                    Report(string.Format("Reading {0} of {1} packages...", i + 1, unread.Count), (double)i / unread.Count);
                }
                unread[i].Metadata = source.ReadMetadata(unread[i], false);
                if (paceHeader != null)
                {
                    paceHeader();
                }
            }

            // Saved whenever the set of packages or executables changed.
            bool executablesChanged = found.Count != knownExecutables.Count ||
                                      found.Any(e => !knownExecutables.ContainsKey(e.Path) ||
                                                     knownExecutables[e.Path].TitleId != e.TitleId);
            bool changed = unread.Count > 0 || content.Count != cachedCount ||
                           known.Count != content.Count(c => c.Metadata != null) || executablesChanged;
            if (changed)
            {
                Report("Saving the library...", 1);
                cache.Save(content, found, paceSave);
            }
            Log.Write("Library: {0} packages, {1} headers read, {2} executables, {3}, in {4} ms", content.Count,
                      unread.Count, found.Count, changed ? "changed" : "unchanged",
                      (int)(DateTime.UtcNow - started).TotalMilliseconds);
            // What's on screen came from the same packages: nothing to redo.
            return changed || !shown ? Build(content, found, gamer, played) : null;
        }

        // The profile's played titles, with the packages that start the most
        // recent ones, read title by title.
        private LoadResult ReadRecentFirst(Gamer gamer, List<PlayedTitle> played)
        {
            List<ContentItem> content = new List<ContentItem>();
            foreach (PlayedTitle title in played.Where(p => p.LastPlayed.HasValue)
                                                 .OrderByDescending(p => p.LastPlayed.Value)
                                                 .Take(RecentFirst))
            {
                Report("Looking for " + title.Name + "...", null);
                foreach (ContentItem item in source.EnumerateTitle(0, title.TitleId))
                {
                    if (ContentType.IsLaunchable(item.Type))
                    {
                        item.Metadata = source.ReadMetadata(item, false);
                        content.Add(item);
                    }
                }
            }
            Log.Write("Showing {0} played titles, {1} packages to start them, before the rest", played.Count,
                      content.Count);
            return Build(content, new List<ExecutableItem>(), gamer, played);
        }

        // Titles from packages, executables and the profile's played titles.
        private LoadResult Build(IEnumerable<ContentItem> content, IEnumerable<ExecutableItem> found, Gamer gamer,
                                 List<PlayedTitle> played)
        {
            Dictionary<uint, TitleEntry> byId = new Dictionary<uint, TitleEntry>();
            Func<uint, TitleEntry> entry = id =>
            {
                TitleEntry t;
                if (!byId.TryGetValue(id, out t))
                {
                    t = new TitleEntry(id);
                    byId.Add(id, t);
                }
                return t;
            };
            foreach (ContentItem item in content)
            {
                if (item.TitleId != 0)
                {
                    entry(item.TitleId).Content.Add(item);
                }
            }
            foreach (PlayedTitle p in played)
            {
                entry(p.TitleId).Played = p;
            }

            // An executable joins the title with its ID, which brings its
            // saves, played stats and art. Homebrew often shares a title ID
            // or has none, so one that would join another executable's title
            // is a title of its own.
            List<TitleEntry> all = byId.Values.ToList();
            Dictionary<uint, bool> fromExecutables = new Dictionary<uint, bool>();
            foreach (ExecutableItem item in found)
            {
                TitleEntry t;
                if (item.TitleId != 0 && !fromExecutables.ContainsKey(item.TitleId))
                {
                    bool existed = byId.ContainsKey(item.TitleId);
                    t = entry(item.TitleId);
                    if (!existed)
                    {
                        all.Add(t);
                        fromExecutables[item.TitleId] = true;
                    }
                }
                else
                {
                    t = new TitleEntry(item.TitleId, "xex:" + item.Path);
                    all.Add(t);
                }
                t.Executables.Add(item);
            }

            foreach (TitleEntry t in all)
            {
                t.Name = NameOf(t);
                t.Kind = Classify(t);
            }
            LoadResult result = new LoadResult
            {
                Titles = all,
                Games = Sorted(all.Where(t => t.Kind == TitleKind.Game)),
                Apps = Sorted(all.Where(t => t.Kind == TitleKind.App)),
                Recent = all.Where(t => t.Kind != TitleKind.Other && (t.LastPlayed != null || t.IsInstalled))
                            .OrderByDescending(t => t.LastPlayed ?? DateTime.MinValue)
                            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                            .ToList(),
                Content = all.SelectMany(t => t.Content).ToList(),
                ById = new Dictionary<uint, TitleEntry>(),
                ByKey = new Dictionary<string, TitleEntry>(),
                Gamer = gamer,
            };
            foreach (TitleEntry t in all)
            {
                if (!result.ById.ContainsKey(t.TitleId))
                {
                    result.ById[t.TitleId] = t;
                }
                result.ByKey[t.Key] = t;
            }
            // Made now, in the background, rather than on the first search.
            result.Search = new SearchIndex(all.Where(t => t.Kind != TitleKind.Other)
                                               .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase));
            return result;
        }

        private TitleKind Classify(TitleEntry title)
        {
            foreach (ITitleClassifier classifier in Classifiers)
            {
                TitleKind? kind = classifier.Classify(title);
                if (kind.HasValue)
                {
                    return kind.Value;
                }
            }
            return TitleKind.Other;
        }

        // The name the title gives itself, from the profile or a package.
        private static string NameOf(TitleEntry t)
        {
            if (t.Played != null && t.Played.Name.Length > 0)
            {
                return t.Played.Name;
            }
            ContentItem launch = t.LaunchItem;
            foreach (ContentItem item in launch != null ? new[] { launch }.Concat(t.Content) : t.Content)
            {
                if (item.Metadata != null && item.Metadata.TitleName.Length > 0)
                {
                    return item.Metadata.TitleName;
                }
                if (item.TitleName.Length > 0)
                {
                    return item.TitleName;
                }
            }
            if (launch != null && launch.DisplayName.Length > 0)
            {
                return launch.DisplayName;
            }
            if (t.Executables.Count > 0)
            {
                return t.Executables[0].Name;
            }
            return "Title " + t.TitleIdText;
        }

        private static List<TitleEntry> Sorted(IEnumerable<TitleEntry> source)
        {
            return source.OrderByDescending(t => t.IsInstalled)
                         .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                         .ToList();
        }
    }

    /// <summary>A step of a library refresh.</summary>
    public sealed class LibraryProgress
    {
        public string Text;
        /// <summary>0 to 1, or null while the amount of work isn't known.</summary>
        public double? Fraction;
    }

    public class LibraryProgressEventArgs : EventArgs
    {
        public LibraryProgressEventArgs(LibraryProgress progress) { Progress = progress; }

        /// <summary>The step, or null when the refresh is over.</summary>
        public LibraryProgress Progress { get; private set; }
    }
}
