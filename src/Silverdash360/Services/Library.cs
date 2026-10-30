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
        private List<TitleEntry> titles = new List<TitleEntry>();

        public Library(IContentSource source, ProfileService profiles)
        {
            this.source = source;
            this.profiles = profiles;
            Classifiers = new List<ITitleClassifier> { new KnownAppsClassifier(), new ContentClassifier() };
        }

        /// <summary>Asked in order to tell games from apps.</summary>
        public List<ITitleClassifier> Classifiers { get; private set; }

        public event EventHandler Changed;

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

        /// <summary>Reads everything again in the background.</summary>
        public void Refresh()
        {
            if (IsLoading)
            {
                return;
            }
            IsLoading = true;
            Background.Run(() => Load(), delegate(LoadResult result, Exception error)
            {
                IsLoading = false;
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
            });
        }

        private sealed class LoadResult
        {
            public List<TitleEntry> Titles;
            public Gamer Gamer;
        }

        private LoadResult Load()
        {
            DateTime started = DateTime.UtcNow;
            Gamer gamer = profiles.SignedIn().FirstOrDefault();

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

            // Common content (games, apps, add-ons) is stored under XUID 0, the
            // profile's own (saves) under its XUID.
            List<ContentItem> content = source.Enumerate(0);
            if (gamer != null)
            {
                content.AddRange(source.Enumerate(gamer.Xuid));
            }
            foreach (ContentItem item in content)
            {
                if (item.TitleId == 0)
                {
                    continue;
                }
                // Headers have the names, description, publisher and art.
                // Saves have little worth the read.
                if (item.Type != ContentType.SavedGame)
                {
                    item.Metadata = source.ReadMetadata(item);
                }
                entry(item.TitleId).Content.Add(item);
            }

            if (gamer != null)
            {
                foreach (PlayedTitle played in profiles.PlayedTitles(gamer))
                {
                    entry(played.TitleId).Played = played;
                }
            }

            foreach (TitleEntry t in byId.Values)
            {
                t.Name = NameOf(t);
                t.Kind = Classify(t);
            }
            Log.Write("Library: {0} titles from {1} packages in {2} ms", byId.Count, content.Count,
                      (int)(DateTime.UtcNow - started).TotalMilliseconds);
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
}
