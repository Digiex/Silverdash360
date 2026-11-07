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
        /// <summary>
        /// The title ID the console files original Xbox packages under when it
        /// doesn't know the game's own; each is a title of its own.
        /// </summary>
        public const uint XboxCompatibilityTitleId = 0xFFFE07D2;

        /// <summary>
        /// The console's own titles (the dashboard, Xbox compatibility):
        /// IDs starting FFFE. Homebrew such as XeLL launchers can declare one.
        /// </summary>
        public static bool IsSystemTitle(uint titleId)
        {
            return (titleId >> 16) == 0xFFFE;
        }

        /// <summary>
        /// IDs homebrew gives itself: FFFF, and C0DE for homebrew shortcuts
        /// such as XeXMenu's. The Marketplace has nothing for them.
        /// </summary>
        public static bool IsHomebrewTitle(uint titleId)
        {
            uint prefix = titleId >> 16;
            return prefix == HomebrewPrefix || prefix == HomebrewShortcutPrefix;
        }

        public const uint HomebrewPrefix = 0xFFFF;
        public const uint HomebrewShortcutPrefix = 0xC0DE;

        /// <summary>
        /// An original Xbox game's title ID, by the rule of the console's
        /// XamIsXbox1TitleId: a game number below 2000 (Xbox 360 games
        /// number from 2000).
        /// </summary>
        public static bool IsOriginalXboxTitleId(uint titleId)
        {
            return titleId != 0 && !IsSystemTitle(titleId) && (titleId & 0x7FFF) < 0x7D0;
        }

        public TitleEntry(uint titleId) : this(titleId, titleId.ToString("X8"))
        {
        }

        /// <summary>
        /// A title with a key of its own, for executables that share a title
        /// ID with other ones (homebrew often does) but are separate titles,
        /// and original Xbox packages filed under
        /// <see cref="XboxCompatibilityTitleId"/>.
        /// </summary>
        public TitleEntry(uint titleId, string key)
        {
            TitleId = titleId;
            Key = key;
            Content = new List<ContentItem>();
            Executables = new List<ExecutableItem>();
        }

        public uint TitleId { get; private set; }

        /// <summary>
        /// The title ID to look up art by: an original Xbox package filed
        /// under <see cref="XboxCompatibilityTitleId"/> has the game's own in
        /// its header.
        /// </summary>
        public uint ArtTitleId
        {
            get
            {
                if (TitleId == XboxCompatibilityTitleId)
                {
                    ContentItem item = LaunchItem ?? Content.FirstOrDefault();
                    if (item != null && item.Metadata != null && item.Metadata.TitleId != 0)
                    {
                        return item.Metadata.TitleId;
                    }
                }
                return TitleId;
            }
        }

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
                if (IsDisc)
                {
                    return "Disc";
                }
                ContentItem launch = LaunchItem;
                if (launch != null)
                {
                    // An app's package is often a Games on Demand one.
                    return Kind == TitleKind.App ? "App" : launch.TypeName;
                }
                ExecutableItem executable = Executables.FirstOrDefault();
                return executable != null ? executable.Location : "Not installed";
            }
        }

        /// <summary>The disc in the drive, which the home page shows first.</summary>
        public bool IsDisc { get; set; }

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
                    // The newest of the most preferred kind; a plain loop, as
                    // a title can have hundreds of packages.
                    int best = int.MaxValue;
                    foreach (ContentItem item in Content)
                    {
                        if (item.IsLaunchable && !item.IsCorrupt)
                        {
                            // Disc 1 first among a game's discs.
                            int priority = LaunchPriority(item.Type) * 256 +
                                           (item.Metadata != null && item.Metadata.IsOneOfDiscs ? item.Metadata.DiscNumber : 0);
                            if (priority < best || (priority == best && Newer(item, launchItem)))
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

        /// <summary>Works the launch package out again, after the content changed.</summary>
        public void ResetLaunchItem()
        {
            launchItemFound = false;
            launchItem = null;
        }

        /// <summary>The other discs of a game on several, by disc number.</summary>
        public List<ContentItem> OtherDiscs
        {
            get
            {
                ContentItem launch = LaunchItem;
                if (launch == null || launch.Metadata == null || !launch.Metadata.IsOneOfDiscs)
                {
                    return new List<ContentItem>();
                }
                Dictionary<byte, ContentItem> discs = new Dictionary<byte, ContentItem>();
                foreach (ContentItem item in Content)
                {
                    if (item != launch && item.Type == launch.Type && item.IsLaunchable && item.Metadata != null &&
                        item.Metadata.IsOneOfDiscs && item.Metadata.DiscNumber != launch.Metadata.DiscNumber &&
                        !discs.ContainsKey(item.Metadata.DiscNumber))
                    {
                        discs[item.Metadata.DiscNumber] = item;
                    }
                }
                return discs.Values.OrderBy(c => c.Metadata.DiscNumber).ToList();
            }
        }

        /// <summary>
        /// Older versions of the game the title starts, as an arcade game
        /// has when an older package is kept beside a newer one: one per
        /// media ID, newest first.
        /// </summary>
        public List<ContentItem> OlderVersions
        {
            get
            {
                ContentItem launch = LaunchItem;
                if (launch == null || launch.Metadata == null || launch.Metadata.IsOneOfDiscs)
                {
                    return new List<ContentItem>();
                }
                Dictionary<uint, ContentItem> versions = new Dictionary<uint, ContentItem>();
                foreach (ContentItem item in Content)
                {
                    if (item != launch && item.Type == launch.Type && item.IsLaunchable && !item.IsCorrupt &&
                        item.Metadata != null && !item.Metadata.IsOneOfDiscs &&
                        item.Metadata.MediaId != launch.Metadata.MediaId &&
                        item.Metadata.Version < launch.Metadata.Version &&
                        !versions.ContainsKey(item.Metadata.MediaId))
                    {
                        versions[item.Metadata.MediaId] = item;
                    }
                }
                return versions.Values.OrderByDescending(c => c.Metadata.Version).ToList();
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

        public bool IsInstalled { get { return IsDisc || LaunchItem != null || Executables.Count > 0; } }

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

        // A package of a later version than the one picked so far, as an
        // arcade game's newer package is.
        private static bool Newer(ContentItem item, ContentItem picked)
        {
            return picked != null && item.Metadata != null && picked.Metadata != null &&
                   item.Metadata.Version > picked.Metadata.Version;
        }

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
                case ContentType.XboxOriginalGame: return 5;
                case ContentType.MarketplaceContent: return 6;
                default: return 9;
            }
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
