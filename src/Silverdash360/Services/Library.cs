using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
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
                new VideoOnlyClassifier(), new ShortcutClassifier(), new KnownAppsClassifier(), new NoAchievementsClassifier(), new FolderClassifier(),
                new ContentClassifier(),
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

        /// <summary>
        /// Every package is in: false while a start shows the saved library
        /// a section at a time.
        /// </summary>
        public bool IsComplete { get; private set; }

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
            Background.Run(() => Load(full), Loaded);
        }

        // Shows what a load read. It read the profile signed in when it
        // started: a change since is read now, and a load the change broke
        // is read again whole.
        private void Loaded(LoadResult result, Exception error)
        {
            IsLoading = false;
            if (!profileReading)
            {
                profileFirst = false;
            }
            Activity.Library = "idle";
            Publish(result);
            OnProgress(null);
            if (profileChangedWhileLoading)
            {
                profileChangedWhileLoading = false;
                if (error != null)
                {
                    Refresh(false);
                }
                else
                {
                    LoadProfile();
                }
            }
        }

        // A sign-out and a sign-in, as switching profiles is, come this far
        // apart or less; the profile is read once they're both in.
        private const int ProfileSettleMs = 1000;
        private DispatcherTimer profileTimer;
        private bool profileChangedWhileLoading;

        /// <summary>
        /// A profile signed in or out, as from the Guide; on the UI thread.
        /// The profile now signed in is shown with its played titles and its
        /// own packages (saves, its themes), with the console's packages as
        /// they were read: not every package again. A refresh under way read
        /// the profile from before, so it's done after that.
        /// </summary>
        public void ProfileChanged()
        {
            Log.Write("Library: a profile signed in or out{0}", IsLoading ? ", during a refresh" : "");
            // The refresh stands aside until the profile is shown.
            if (IsLoading)
            {
                profileFirst = true;
            }
            if (profileTimer == null)
            {
                profileTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ProfileSettleMs) };
                profileTimer.Tick += delegate
                {
                    profileTimer.Stop();
                    LoadProfile();
                };
            }
            profileTimer.Stop();
            profileTimer.Start();
        }

        private void LoadProfile()
        {
            if (IsLoading)
            {
                // The refresh takes up the profile signed in now before it
                // ends; a long one, as reading thousands of packages is, is
                // overtaken meanwhile from what's on screen.
                profileChangedWhileLoading = true;
                if (IsLoaded && !profileReading)
                {
                    profileReading = true;
                    profileFirst = true;
                    LoadResult shown = current;
                    Background.Run(() => ReadProfile(shown), delegate(LoadResult result, Exception error)
                    {
                        profileReading = false;
                        profileFirst = false;
                        // A refresh that ended meanwhile has the newer library.
                        if (IsLoading)
                        {
                            Publish(result);
                        }
                    });
                }
                else if (!IsLoaded)
                {
                    // The first load takes it up itself.
                    profileFirst = false;
                }
                return;
            }
            profileFirst = false;
            if (!IsLoaded)
            {
                // The first load reads the profile signed in.
                return;
            }
            IsLoading = true;
            LoadResult before = current;
            Background.Run(() => ReadProfile(before), Loaded);
        }

        private bool profileReading;

        // A profile signed in or out during a refresh: the refresh pauses
        // at its next step until the profile is shown, so reading it isn't
        // slowed by reading thousands of packages at the same time. The
        // thread reading the profile doesn't wait on itself.
        private static volatile bool profileFirst;
        private static volatile int profileThread;
        private const int ProfileFirstMaxMs = 30000;

        private static void WaitForProfile()
        {
            int started = Environment.TickCount;
            while (profileFirst && Thread.CurrentThread.ManagedThreadId != profileThread &&
                   Environment.TickCount - started < ProfileFirstMaxMs)
            {
                Thread.Sleep(50);
            }
        }

        // The library as it was with the profile signed in now; null when
        // it's the same one.
        private LoadResult ReadProfile(LoadResult before)
        {
            profileThread = Thread.CurrentThread.ManagedThreadId;
            DateTime started = DateTime.UtcNow;
            Gamer gamer = profiles.SignedIn().FirstOrDefault();
            ulong xuid = gamer != null ? gamer.Xuid : 0;
            ulong was = before.Gamer != null ? before.Gamer.Xuid : 0;
            if (xuid == was)
            {
                return null;
            }
            Report("Reading the profile...", null);
            List<PlayedTitle> played = gamer != null ? profiles.PlayedTitles(gamer) : new List<PlayedTitle>();
            // The console's packages are under XUID 0, a profile's under its
            // offline XUID.
            List<ContentItem> content = before.Content.Where(c => c.Owner == 0).ToList();
            if (gamer != null)
            {
                content.AddRange(ReadOwn(gamer, Pace(0)));
            }
            LoadResult built = Build(content, before.Executables, gamer, played);
            Log.Write("Library: now for {0}, {1} packages of its own, in {2} ms",
                      gamer != null ? gamer.Gamertag : "nobody signed in", content.Count(c => c.Owner != 0),
                      (int)(DateTime.UtcNow - started).TotalMilliseconds);
            return built;
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
            public bool Complete = true;
            /// <summary>The executables found in the Apps and Games folders.</summary>
            public List<ExecutableItem> Executables = new List<ExecutableItem>();
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
            IsComplete = result.Complete;
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
            Activity.Library = text;
            LibraryProgress progress = new LibraryProgress { Text = text, Fraction = fraction };
            Background.OnUi(() => OnProgress(progress));
        }

        private LoadResult Load(bool full)
        {
            DateTime started = DateTime.UtcNow;
            Gamer gamer = profiles.SignedIn().FirstOrDefault();
            List<PlayedTitle> played = gamer != null ? profiles.PlayedTitles(gamer) : new List<PlayedTitle>();

            // What earlier runs read from package and executable headers: of
            // the console's packages, kept whoever is signed in, and of the
            // profile's own, kept with the profile.
            LibraryCache profileCache = gamer != null ? LibraryCache.ForProfile(gamer.OfflineXuid) : null;
            Dictionary<string, ContentMetadata> known = new Dictionary<string, ContentMetadata>();
            Dictionary<string, byte> knownApps = new Dictionary<string, byte>();
            Dictionary<string, ContentItem> knownInside = new Dictionary<string, ContentItem>();
            Dictionary<string, ExecutableItem> knownExecutables = new Dictionary<string, ExecutableItem>();
            List<ContentItem> sharedCached = new List<ContentItem>();
            List<ContentItem> ownCached = new List<ContentItem>();
            bool outdated = false;
            bool shown = IsLoaded;
            if (full)
            {
                cache.Clear();
                if (profileCache != null)
                {
                    profileCache.Clear();
                }
            }
            else
            {
                Report("Opening the saved library...", null);
                // The profile's packages are few: they go up with the first
                // section, its themes among them.
                if (profileCache != null)
                {
                    ownCached = Owned(profileCache.Load(null).Content, gamer.OfflineXuid);
                }
                // On a start, each section goes up as soon as it's read: the
                // first screens' titles, then every title, then the rest.
                bool showSections = !IsLoaded;
                LibraryCache.Contents cached = cache.Load(section =>
                {
                    if (!showSections || (section.Content.Count == 0 && section.Executables.Count == 0))
                    {
                        return;
                    }
                    int building = Environment.TickCount;
                    // A file saved before profiles had their own has the
                    // packages of whoever was signed in then: those its
                    // records say are a profile's aren't shown, and the rest
                    // go when the console's are listed again.
                    List<ContentItem> packages = section.Content.Where(c => c.Xuid == 0).Concat(ownCached).ToList();
                    LoadResult partial = Build(packages, section.Executables, gamer, played);
                    partial.Complete = section.Complete;
                    Log.Write("Library: showing {0} saved packages{1}, built in {2} ms", packages.Count,
                              section.Complete ? "" : " so far", Environment.TickCount - building);
                    Background.OnUi(() => Publish(partial));
                });
                sharedCached = cached.Content.Where(c => c.Xuid == 0).ToList();
                // That file is saved again with the console's packages alone.
                outdated = cached.Outdated || sharedCached.Count != cached.Content.Count;
                foreach (ContentItem item in cached.Content.Concat(ownCached))
                {
                    // A header read before without metadata, such as an avatar
                    // item's, isn't read again on every start.
                    if (item.Metadata != null || item.HeaderRead)
                    {
                        known[LibraryCache.KeyOf(item)] = item.Metadata;
                    }
                    if (item.AppCheck != ContentItem.AppNotChecked)
                    {
                        knownApps[LibraryCache.KeyOf(item)] = item.AppCheck;
                    }
                    if (item.Inside != ContentItem.InsideNotChecked)
                    {
                        knownInside[LibraryCache.KeyOf(item)] = item;
                    }
                }
                foreach (ExecutableItem item in cached.Executables)
                {
                    knownExecutables[item.Path] = item;
                }
                if (sharedCached.Count > 0 || cached.Executables.Count > 0)
                {
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
            // time; the console is a lot slower than a PC running xenia. It
            // stops while the user is moving either way, so scrolling isn't
            // slowed by it.
            Action pace = Pace(shown ? PaceEnumeration : 0);
            Action paceHeader = Pace(shown ? PaceHeader : recentShown ? PaceHeaderBehindRecent : 0);
            Action paceExecutable = Pace(shown ? PaceExecutable : 0);
            Action paceSave = Pace(shown ? PaceSave : 0);

            // Common content (games, apps, add-ons) is stored under XUID 0, the
            // profile's own (saves, its themes) under its offline XUID.
            Report("Looking for content...", null);
            List<ContentItem> content = source.Enumerate(0, count => Report("Looking for content... " + count + " found", null), pace);
            if (gamer != null)
            {
                content.AddRange(Owned(source.Enumerate(gamer.OfflineXuid, null, pace), gamer.OfflineXuid));
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
                // A launchable package kept from before discs were read is
                // read again, once, for its disc number.
                if (known.TryGetValue(LibraryCache.KeyOf(item), out metadata) &&
                    (metadata == null || metadata.DiscsKnown || !ContentType.IsLaunchable(item.Type)))
                {
                    item.Metadata = metadata;
                    item.HeaderRead = true;
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
                unread[i].HeaderRead = true;
                if (paceHeader != null)
                {
                    paceHeader();
                }
            }

            int appsChecked = CheckForApps(content, knownApps, paceHeader) + LookInsideHomebrew(content, knownInside, paceHeader);

            // A profile signed in or out while this ran: the result is for
            // the one signed in now, its own packages and played titles in
            // place of the other's, so it doesn't bring that one back.
            Gamer now = profiles.SignedIn().FirstOrDefault();
            if ((now != null ? now.Xuid : 0) != (gamer != null ? gamer.Xuid : 0))
            {
                Log.Write("Library: the profile changed during the refresh, to {0}",
                          now != null ? now.Gamertag : "nobody signed in");
                gamer = now;
                played = now != null ? profiles.PlayedTitles(now) : new List<PlayedTitle>();
                content = content.Where(c => c.Owner == 0).ToList();
                if (now != null)
                {
                    content.AddRange(ReadOwn(now, pace));
                }
                // ReadOwn saved the new profile's file; the other's stays as it was.
                profileCache = null;
            }

            // Each file is saved whenever its packages changed, the console's
            // also when the executables did.
            bool executablesChanged = found.Count != knownExecutables.Count ||
                                      found.Any(e => !knownExecutables.ContainsKey(e.Path) ||
                                                     knownExecutables[e.Path].TitleId != e.TitleId);
            List<ContentItem> sharedContent = content.Where(c => c.Owner == 0).ToList();
            List<ContentItem> ownContent = content.Where(c => c.Owner != 0).ToList();
            bool sharedChanged = outdated || appsChecked > 0 || executablesChanged ||
                                 Differs(sharedContent, sharedCached, unread.Count(c => c.Owner == 0));
            bool ownChanged = profileCache != null && Differs(ownContent, ownCached, unread.Count(c => c.Owner != 0));
            bool changed = sharedChanged || ownChanged;
            // What's on screen came from the same packages: nothing to redo.
            LoadResult built = changed || !shown ? Build(content, found, gamer, played) : null;
            if (sharedChanged)
            {
                Report("Saving the library...", 1);
                List<IList<ContentItem>> sections = CacheSections(built)
                    .Select(s => (IList<ContentItem>)s.Where(c => c.Owner == 0).ToList()).ToList();
                cache.Save(sections, found, paceSave);
            }
            if (ownChanged)
            {
                profileCache.Save(new List<IList<ContentItem>> { ownContent }, new List<ExecutableItem>(), paceSave);
            }
            Log.Write("Library: {0} packages, {1} headers read, {2} executables, {3}, in {4} ms", content.Count,
                      unread.Count, found.Count, changed ? "changed" : "unchanged",
                      (int)(DateTime.UtcNow - started).TotalMilliseconds);
            return built;
        }

        // A profile's own packages, with what was read of them when it was
        // last here; its file is saved again when they changed.
        private List<ContentItem> ReadOwn(Gamer gamer, Action pace)
        {
            LibraryCache profileCache = LibraryCache.ForProfile(gamer.OfflineXuid);
            List<ContentItem> saved = Owned(profileCache.Load(null).Content, gamer.OfflineXuid);
            Dictionary<string, ContentItem> known = new Dictionary<string, ContentItem>();
            foreach (ContentItem item in saved)
            {
                known[LibraryCache.KeyOf(item)] = item;
            }
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            List<ContentItem> own = new List<ContentItem>();
            int read = 0;
            foreach (ContentItem item in Owned(source.Enumerate(gamer.OfflineXuid, null, pace), gamer.OfflineXuid))
            {
                string key = LibraryCache.KeyOf(item);
                if (item.TitleId == 0 || seen.ContainsKey(key))
                {
                    continue;
                }
                seen[key] = true;
                if (item.Type != ContentType.SavedGame)
                {
                    ContentItem kept;
                    if (known.TryGetValue(key, out kept) && (kept.Metadata != null || kept.HeaderRead))
                    {
                        item.Metadata = kept.Metadata;
                    }
                    else
                    {
                        item.Metadata = source.ReadMetadata(item, false);
                        read++;
                    }
                    item.HeaderRead = true;
                }
                own.Add(item);
            }
            if (Differs(own, saved, read))
            {
                profileCache.Save(new List<IList<ContentItem>> { own }, new List<ExecutableItem>(), pace);
            }
            return own;
        }

        // Marks packages as a profile's own.
        private static List<ContentItem> Owned(List<ContentItem> packages, ulong owner)
        {
            foreach (ContentItem item in packages)
            {
                item.Owner = owner;
            }
            return packages;
        }

        // Whether packages differ from what was saved of them: others, or
        // headers read since.
        private static bool Differs(List<ContentItem> now, List<ContentItem> saved, int read)
        {
            return read > 0 || now.Count != saved.Count ||
                   now.Count(c => c.HeaderRead) != saved.Count(c => c.Metadata != null || c.HeaderRead);
        }

        // Looks inside the add-on packages of titles with nothing else to
        // start them, for an app that came as an add-on (Video Kinect does).
        // Each package is looked in once; the answer is saved with it.
        // Returns how many were looked in.
        // Building the titles takes seconds over thousands of packages: every
        // so many, it waits while the user moves.
        private const int BreatheEvery = 64;

        private static void Breathe(int step)
        {
            if (step % BreatheEvery == 0)
            {
                UiIdle.WaitWhileMoving(UiIdle.BackgroundQuietMs);
                WaitForProfile();
            }
        }

        // A pause between steps of the refresh, and a wait while the user
        // moves or a profile is read.
        private static Action Pace(int ms)
        {
            return delegate
            {
                if (ms > 0)
                {
                    Thread.Sleep(ms);
                }
                UiIdle.WaitWhileMoving(UiIdle.BackgroundQuietMs);
                WaitForProfile();
            };
        }

        private int CheckForApps(List<ContentItem> content, Dictionary<string, byte> knownApps, Action pace)
        {
            Dictionary<uint, bool> startable = new Dictionary<uint, bool>();
            foreach (ContentItem item in content)
            {
                byte check;
                if (knownApps.TryGetValue(LibraryCache.KeyOf(item), out check))
                {
                    item.AppCheck = check;
                }
                if (item.IsLaunchable || item.Type == ContentType.InstalledGame)
                {
                    startable[item.TitleId] = true;
                }
            }
            int checkedCount = 0;
            foreach (ContentItem item in content)
            {
                if (item.Type != ContentType.MarketplaceContent || item.AppCheck != ContentItem.AppNotChecked ||
                    startable.ContainsKey(item.TitleId) || item.IsCorrupt || item.IsPartial)
                {
                    continue;
                }
                if (checkedCount == 0)
                {
                    Report("Looking for apps in add-ons...", null);
                }
                item.AppCheck = source.HoldsApp(item) ? ContentItem.AppInside : ContentItem.AppNone;
                checkedCount++;
                if (item.AppCheck == ContentItem.AppInside)
                {
                    Log.Write("Library: {0} holds an app", item);
                    startable[item.TitleId] = true;
                }
                if (pace != null)
                {
                    pace();
                }
            }
            return checkedCount;
        }

        // Opens each homebrew package once to see whether it's a QuickBoot
        // shortcut or an app packaged whole; the answer is saved with it.
        // Returns how many were opened.
        private int LookInsideHomebrew(List<ContentItem> content, Dictionary<string, ContentItem> known, Action pace)
        {
            int opened = 0;
            foreach (ContentItem item in content)
            {
                if (!item.IsHomebrewPackage || item.IsCorrupt || item.IsPartial)
                {
                    continue;
                }
                ContentItem before;
                if (known.TryGetValue(LibraryCache.KeyOf(item), out before))
                {
                    item.Inside = before.Inside;
                    item.ShortcutTarget = before.ShortcutTarget;
                    item.InsideTitleId = before.InsideTitleId;
                    item.InsideSize = before.InsideSize;
                    continue;
                }
                if (opened == 0)
                {
                    Report("Looking in homebrew packages...", null);
                }
                source.LookInside(item);
                opened++;
                if (pace != null)
                {
                    pace();
                }
            }
            return opened;
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
            LoadResult recent = Build(content, new List<ExecutableItem>(), gamer, played);
            recent.Complete = false;
            return recent;
        }

        // The titles of each list a start shows first.
        private const int FirstScreenTitles = 24;

        // The packages in the order a start wants them: what makes the titles
        // on the first screens, then what makes every other title, then the
        // rest (saves, avatar items, add-ons and the like), which only the
        // storage and media pages and a title's details need.
        private static IList<IList<ContentItem>> CacheSections(LoadResult library)
        {
            Dictionary<ContentItem, bool> placed = new Dictionary<ContentItem, bool>();
            List<ContentItem> first = new List<ContentItem>();
            // The themes, few and small, so the dashboard's theme is there at once.
            foreach (ContentItem item in library.Content)
            {
                if (item.Type == ContentType.Theme)
                {
                    placed[item] = true;
                    first.Add(item);
                }
            }
            foreach (IList<TitleEntry> list in new[] { library.Recent, library.Games, library.Apps })
            {
                for (int i = 0; i < list.Count && i < FirstScreenTitles; i++)
                {
                    AddTitleMakers(list[i], first, placed);
                }
            }
            List<ContentItem> titles = new List<ContentItem>();
            foreach (TitleEntry title in library.Titles)
            {
                AddTitleMakers(title, titles, placed);
            }
            List<ContentItem> rest = new List<ContentItem>();
            foreach (ContentItem item in library.Content)
            {
                if (!placed.ContainsKey(item))
                {
                    rest.Add(item);
                }
            }
            return new List<IList<ContentItem>> { first, titles, rest };
        }

        // What a title is made of for the lists: the packages that start or
        // install it, else the one it takes its name from.
        private static void AddTitleMakers(TitleEntry title, List<ContentItem> section,
                                           Dictionary<ContentItem, bool> placed)
        {
            ContentItem named = null;
            bool any = false;
            foreach (ContentItem item in title.Content)
            {
                uint type = item.Type;
                if (item.IsLaunchable || type == ContentType.InstalledGame)
                {
                    any = true;
                    if (!placed.ContainsKey(item))
                    {
                        placed[item] = true;
                        section.Add(item);
                    }
                }
                else if (named == null && item.Metadata != null && item.Metadata.TitleName.Length > 0)
                {
                    named = item;
                }
            }
            if (!any)
            {
                ContentItem maker = named ?? (title.Content.Count > 0 ? title.Content[0] : null);
                if (maker != null && !placed.ContainsKey(maker))
                {
                    placed[maker] = true;
                    section.Add(maker);
                }
            }
        }

        // A homebrew package that starts an executable the scan found, a
        // shortcut to it or a package of the same one, is left out: the
        // executable's title shows it once. The rest are titles of their own.
        private static void AddHomebrewPackages(List<ContentItem> homebrew, IEnumerable<ExecutableItem> found,
                                                List<TitleEntry> all)
        {
            foreach (ContentItem item in homebrew)
            {
                if (FindExecutable(item, found) != null)
                {
                    continue;
                }
                TitleEntry own = new TitleEntry(item.TitleId, "pkg:" + item.DeviceId.ToString("X8") + "/" + item.FileName);
                own.Content.Add(item);
                all.Add(own);
            }
        }

        // The executable a homebrew package stands for. A shortcut names it
        // from the root of a drive without saying which: QuickBoot puts
        // shortcuts on the hard drive, so that's looked at first. A packaged
        // app matches an executable with its title ID and size: homebrew
        // shares title IDs, so the ID alone isn't enough.
        private static ExecutableItem FindExecutable(ContentItem item, IEnumerable<ExecutableItem> found)
        {
            ExecutableItem match = null;
            foreach (ExecutableItem executable in found)
            {
                bool same;
                if (item.Inside == ContentItem.InsideShortcut)
                {
                    string path = executable.Path;
                    int slash = path.IndexOf('/', 1);
                    same = slash > 0 && string.Equals(path.Substring(slash).Replace('/', '\\'), item.ShortcutTarget,
                                                      StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    same = item.Inside == ContentItem.InsideExecutable && executable.TitleId == item.InsideTitleId &&
                           executable.Size == item.InsideSize;
                }
                if (same && (match == null || executable.Path.StartsWith(HardDrivePrefix, StringComparison.OrdinalIgnoreCase)))
                {
                    match = executable;
                }
            }
            return match;
        }

        private const string HardDrivePrefix = "/Hdd1/";

        // Titles whose packages are separate games under one title ID, as the
        // Lips games are, become a title each: single-disc packages with
        // different media IDs. A game on several discs stays one title, which
        // starts disc 1. The one the profile played (or the first by name)
        // keeps the saves, add-ons and played stats.
        private static void SplitGames(List<TitleEntry> all)
        {
            int count = all.Count;
            for (int i = 0; i < count; i++)
            {
                TitleEntry title = all[i];
                ContentItem launch = title.LaunchItem;
                if (launch == null || launch.Metadata == null)
                {
                    continue;
                }
                // One package per media ID, of the kind that starts the title.
                Dictionary<uint, ContentItem> games = new Dictionary<uint, ContentItem>();
                foreach (ContentItem item in title.Content)
                {
                    if (item.Type == launch.Type && item.IsLaunchable && !item.IsCorrupt && item.Metadata != null &&
                        !item.Metadata.IsOneOfDiscs && !games.ContainsKey(item.Metadata.MediaId))
                    {
                        games[item.Metadata.MediaId] = item;
                    }
                }
                bool discs = title.Content.Any(c => c.Type == launch.Type && c.Metadata != null && c.Metadata.IsOneOfDiscs);
                if (games.Count + (discs ? 1 : 0) < 2)
                {
                    continue;
                }
                List<ContentItem> others = games.Values.OrderBy(c => c.Metadata.TitleName, StringComparer.OrdinalIgnoreCase).ToList();
                ContentItem main = discs ? null : others.FirstOrDefault(
                    c => title.Played != null && string.Equals(c.Metadata.TitleName, title.Played.Name, StringComparison.OrdinalIgnoreCase))
                    ?? others[0];
                foreach (ContentItem item in others)
                {
                    if (item == main)
                    {
                        continue;
                    }
                    // The package and any copies of it on other drives.
                    TitleEntry own = new TitleEntry(title.TitleId, title.TitleIdText + "/" + item.Metadata.MediaId.ToString("X8"));
                    foreach (ContentItem copy in title.Content.Where(c => c.Type == launch.Type && c.Metadata != null &&
                                                                        c.Metadata.MediaId == item.Metadata.MediaId).ToList())
                    {
                        title.Content.Remove(copy);
                        own.Content.Add(copy);
                    }
                    all.Add(own);
                }
                title.ResetLaunchItem();
            }
        }

        // The title with an ID, made when it's first asked for.
        private static TitleEntry TitleFor(Dictionary<uint, TitleEntry> byId, uint id)
        {
            TitleEntry title;
            if (!byId.TryGetValue(id, out title))
            {
                title = new TitleEntry(id);
                byId.Add(id, title);
            }
            return title;
        }

        // Titles from packages, executables and the profile's played titles.
        // Plain loops: the console interprets app code, where each call of a
        // lambda costs, and this runs over thousands of packages.
        private LoadResult Build(IEnumerable<ContentItem> content, IEnumerable<ExecutableItem> found, Gamer gamer,
                                 List<PlayedTitle> played)
        {
            Dictionary<uint, TitleEntry> byId = new Dictionary<uint, TitleEntry>();
            List<ContentItem> allContent = new List<ContentItem>();
            // Original Xbox games the console filed under its compatibility
            // title rather than their own: one title each.
            List<TitleEntry> separate = new List<TitleEntry>();
            // Homebrew packages: QuickBoot shortcuts and packaged apps all
            // share a title ID (C0DE9999), so each is a title of its own.
            List<ContentItem> homebrew = new List<ContentItem>();
            int step = 0;
            foreach (ContentItem item in content)
            {
                Breathe(++step);
                uint id = item.TitleId;
                if (id == 0)
                {
                    continue;
                }
                allContent.Add(item);
                if (item.IsHomebrewPackage && ContentType.IsLaunchable(item.Type))
                {
                    homebrew.Add(item);
                    continue;
                }
                if (id == TitleEntry.XboxCompatibilityTitleId && item.Type == ContentType.XboxOriginalGame)
                {
                    TitleEntry own = new TitleEntry(id, "xbox:" + item.DeviceId.ToString("X8") + "/" + item.FileName);
                    own.Content.Add(item);
                    separate.Add(own);
                    continue;
                }
                TitleFor(byId, id).Content.Add(item);
            }
            foreach (PlayedTitle p in played)
            {
                TitleFor(byId, p.TitleId).Played = p;
            }

            // An executable joins the title with its ID, which brings its
            // saves, played stats and art. Homebrew often shares a title ID,
            // declares a system one (the dashboard's) or has none, so one
            // that would join another executable's title or a system title
            // is a title of its own.
            List<TitleEntry> all = byId.Values.ToList();
            all.AddRange(separate);
            Dictionary<uint, bool> fromExecutables = new Dictionary<uint, bool>();
            foreach (ExecutableItem item in found)
            {
                TitleEntry t;
                if (item.TitleId != 0 && !TitleEntry.IsSystemTitle(item.TitleId) &&
                    !fromExecutables.ContainsKey(item.TitleId))
                {
                    bool existed = byId.ContainsKey(item.TitleId);
                    t = TitleFor(byId, item.TitleId);
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
            AddHomebrewPackages(homebrew, found, all);
            SplitGames(all);

            List<TitleEntry> games = new List<TitleEntry>();
            List<TitleEntry> apps = new List<TitleEntry>();
            List<TitleEntry> recent = new List<TitleEntry>();
            List<TitleEntry> listed = new List<TitleEntry>();
            foreach (TitleEntry t in all)
            {
                Breathe(++step);
                t.Name = NameOf(t);
                t.Kind = Classify(t);
                if (t.Kind == TitleKind.Other)
                {
                    continue;
                }
                listed.Add(t);
                (t.Kind == TitleKind.Game ? games : apps).Add(t);
                if (t.LastPlayed != null || t.IsInstalled)
                {
                    recent.Add(t);
                }
            }
            LoadResult result = new LoadResult
            {
                Titles = all,
                Games = Sorted(games),
                Apps = Sorted(apps),
                Recent = recent.OrderByDescending(t => t.LastPlayed.HasValue ? t.LastPlayed.Value.Ticks : 0)
                               .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                               .ToList(),
                Content = allContent,
                ById = new Dictionary<uint, TitleEntry>(),
                ByKey = new Dictionary<string, TitleEntry>(),
                Gamer = gamer,
                Executables = found.ToList(),
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
            result.Search = new SearchIndex(listed.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase));
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
            string name = PackageNameOf(t);
            ContentItem launch = t.LaunchItem;
            return launch != null && launch.Metadata != null && launch.Metadata.IsOneOfDiscs ? WithoutDisc(name) : name;
        }

        // A game on several discs is named after disc 1's package, such as
        // "Kinect Nat Geo TV - Disc 1": the title is the game, not the disc.
        private static string WithoutDisc(string name)
        {
            int at = name.LastIndexOf("disc", StringComparison.OrdinalIgnoreCase);
            if (at <= 0 || name.Substring(at + 4).Trim(' ', ')', ']').Length == 0 ||
                !name.Substring(at + 4).Trim(' ', ')', ']').All(char.IsDigit))
            {
                return name;
            }
            return name.Substring(0, at).TrimEnd(' ', '-', ':', '(', '[', ',');
        }

        private static string PackageNameOf(TitleEntry t)
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
