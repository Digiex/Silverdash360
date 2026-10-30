using System;
using System.Collections.Generic;
using System.Linq;
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
        private List<TitleEntry> titles = new List<TitleEntry>();

        public Library(IContentSource source, ProfileService profiles, LibraryCache cache)
        {
            this.source = source;
            this.profiles = profiles;
            this.cache = cache;
            Classifiers = new List<ITitleClassifier> { new KnownAppsClassifier(), new ContentClassifier() };
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

        public IList<TitleEntry> Titles { get { return titles; } }

        public IEnumerable<TitleEntry> Games
        {
            get { return Sorted(titles.Where(t => t.Kind == TitleKind.Game)); }
        }

        public IEnumerable<TitleEntry> Apps
        {
            get { return Sorted(titles.Where(t => t.Kind == TitleKind.App)); }
        }

        /// <summary>Played titles, most recent first, then installed ones.</summary>
        public IEnumerable<TitleEntry> Recent
        {
            get
            {
                return titles.Where(t => t.Kind != TitleKind.Other && (t.LastPlayed != null || t.IsInstalled))
                             .OrderByDescending(t => t.LastPlayed ?? DateTime.MinValue)
                             .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase);
            }
        }

        public IEnumerable<ContentItem> AllContent
        {
            get { return titles.SelectMany(t => t.Content); }
        }

        public TitleEntry Find(uint titleId)
        {
            return titles.FirstOrDefault(t => t.TitleId == titleId);
        }

        /// <summary>
        /// Titles whose name, publisher or title ID contains every word of the
        /// query, best matches (name starts with it) first.
        /// </summary>
        public List<TitleEntry> Search(string query)
        {
            string[] words = (query ?? "").ToLowerInvariant()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                return new List<TitleEntry>();
            }
            return titles
                .Where(t => t.Kind != TitleKind.Other)
                .Select(t => new { Title = t, Text = (t.Name + " " + t.Publisher + " " + t.TitleIdText).ToLowerInvariant() })
                .Where(x => words.All(w => x.Text.Contains(w)))
                .OrderBy(x => x.Title.Name.ToLowerInvariant().StartsWith(words[0]) ? 0 : 1)
                .ThenBy(x => x.Title.Name, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Title)
                .ToList();
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
            public List<TitleEntry> Titles;
            public Gamer Gamer;
        }

        private void Publish(LoadResult result)
        {
            if (result != null)
            {
                titles = result.Titles;
                Gamer = result.Gamer;
                IsLoaded = true;
            }
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

            // What earlier runs read from package headers.
            Dictionary<string, ContentMetadata> known = new Dictionary<string, ContentMetadata>();
            if (full)
            {
                cache.Clear();
            }
            else
            {
                Report("Opening the saved library...", null);
                List<ContentItem> cached = cache.Load();
                foreach (ContentItem item in cached)
                {
                    if (item.Metadata != null)
                    {
                        known[LibraryCache.KeyOf(item)] = item.Metadata;
                    }
                }
                if (!IsLoaded && cached.Count > 0)
                {
                    LoadResult quick = Build(cached, gamer, played);
                    Background.OnUi(() => Publish(quick));
                }
            }

            // Common content (games, apps, add-ons) is stored under XUID 0, the
            // profile's own (saves) under its XUID.
            Report("Looking for content...", null);
            List<ContentItem> content = source.Enumerate(0, found => Report("Looking for content... " + found + " found", null));
            if (gamer != null)
            {
                content.AddRange(source.Enumerate(gamer.Xuid, null));
            }
            content = content.Where(c => c.TitleId != 0).ToList();

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
                unread[i].Metadata = source.ReadMetadata(unread[i]);
            }

            // Saved whenever the set of packages changed.
            if (unread.Count > 0 || known.Count != content.Count(c => c.Metadata != null))
            {
                Report("Saving the library...", 1);
                cache.Save(content);
            }
            Log.Write("Library: {0} packages, {1} headers read, in {2} ms", content.Count, unread.Count,
                      (int)(DateTime.UtcNow - started).TotalMilliseconds);
            return Build(content, gamer, played);
        }

        // Titles from packages and the profile's played titles.
        private LoadResult Build(IEnumerable<ContentItem> content, Gamer gamer, List<PlayedTitle> played)
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
            foreach (TitleEntry t in byId.Values)
            {
                t.Name = NameOf(t);
                t.Kind = Classify(t);
            }
            return new LoadResult { Titles = byId.Values.ToList(), Gamer = gamer };
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
            return "Title " + t.TitleIdText;
        }

        private static IEnumerable<TitleEntry> Sorted(IEnumerable<TitleEntry> source)
        {
            return source.OrderByDescending(t => t.IsInstalled)
                         .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase);
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
