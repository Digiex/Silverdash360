using System;
using System.Collections.Generic;
using System.Linq;

namespace Silverdash360.Model
{
    /// <summary>What a title shows up as in the dashboard.</summary>
    public enum TitleKind
    {
        Game,
        App,
        /// <summary>Content of a title that isn't installed, such as saves.</summary>
        Other,
    }

    /// <summary>
    /// A game or app: everything stored for one title ID, plus what the
    /// signed-in profile knows about it (played, achievements).
    /// </summary>
    public sealed class TitleEntry
    {
        public TitleEntry(uint titleId) : this(titleId, titleId.ToString("X8"))
        {
        }

        /// <summary>
        /// A title with a key of its own, for executables that share a title
        /// ID with other ones (homebrew often does) but are separate titles.
        /// </summary>
        public TitleEntry(uint titleId, string key)
        {
            TitleId = titleId;
            Key = key;
            Content = new List<ContentItem>();
            Executables = new List<ExecutableItem>();
        }

        public uint TitleId { get; private set; }

        /// <summary>What pages find the title by: its title ID, or for a separate executable its path.</summary>
        public string Key { get; private set; }

        public string Name { get; set; }
        public TitleKind Kind { get; set; }
        public List<ContentItem> Content { get; private set; }

        /// <summary>Executables of the title in a drive's Apps or Games folder.</summary>
        public List<ExecutableItem> Executables { get; private set; }

        /// <summary>The executable that starts the title when no package does.</summary>
        public ExecutableItem Executable
        {
            get { return LaunchItem == null ? Executables.FirstOrDefault() : null; }
        }

        /// <summary>Where the title starts from, for showing: a package type or a folder.</summary>
        public string Source
        {
            get
            {
                ContentItem launch = LaunchItem;
                if (launch != null)
                {
                    return launch.TypeName;
                }
                ExecutableItem executable = Executables.FirstOrDefault();
                return executable != null ? executable.Location : "Not installed";
            }
        }

        /// <summary>From the profile's played titles, when it has been played.</summary>
        public PlayedTitle Played { get; set; }

        private bool launchItemFound;
        private ContentItem launchItem;
        private string searchText;

        /// <summary>
        /// The package that starts the title, if it's installed. Worked out
        /// once: it's asked for a lot, and the content doesn't change after
        /// the library is built.
        /// </summary>
        public ContentItem LaunchItem
        {
            get
            {
                if (!launchItemFound)
                {
                    // The first of the most preferred kind; a plain loop, as a
                    // title can have hundreds of packages.
                    int best = int.MaxValue;
                    foreach (ContentItem item in Content)
                    {
                        if (ContentType.IsLaunchable(item.Type) && !item.IsCorrupt)
                        {
                            int priority = LaunchPriority(item.Type);
                            if (priority < best)
                            {
                                best = priority;
                                launchItem = item;
                            }
                        }
                    }
                    launchItemFound = true;
                }
                return launchItem;
            }
        }

        /// <summary>The name, publisher and title ID in lower case, for search.</summary>
        public string SearchText
        {
            get
            {
                if (searchText == null)
                {
                    searchText = ((Name ?? "") + " " + Publisher + " " + TitleIdText).ToLowerInvariant();
                }
                return searchText;
            }
        }

        public bool IsInstalled { get { return LaunchItem != null || Executables.Count > 0; } }

        public long TotalSize { get { return Content.Sum(c => c.BestSize); } }

        public DateTime? LastPlayed { get { return Played != null ? Played.LastPlayed : null; } }

        public string Publisher
        {
            get
            {
                ContentItem item = LaunchItem ?? Content.FirstOrDefault(c => c.Metadata != null);
                return item != null && item.Metadata != null ? item.Metadata.Publisher : "";
            }
        }

        public string Description
        {
            get
            {
                ContentItem item = LaunchItem;
                return item != null && item.Metadata != null ? item.Metadata.Description : "";
            }
        }

        public string TitleIdText { get { return TitleId.ToString("X8"); } }

        // A full game before a demo of it.
        private static int LaunchPriority(uint type)
        {
            switch (type)
            {
                case ContentType.GameOnDemand: return 0;
                case ContentType.ArcadeTitle: return 1;
                case ContentType.GameTitle: return 2;
                case ContentType.CommunityGame: return 3;
                case ContentType.Xna: return 4;
                default: return 9;
            }
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
