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
        public TitleEntry(uint titleId)
        {
            TitleId = titleId;
            Content = new List<ContentItem>();
        }

        public uint TitleId { get; private set; }
        public string Name { get; set; }
        public TitleKind Kind { get; set; }
        public List<ContentItem> Content { get; private set; }

        /// <summary>From the profile's played titles, when it has been played.</summary>
        public PlayedTitle Played { get; set; }

        /// <summary>The package that starts the title, if it's installed.</summary>
        public ContentItem LaunchItem
        {
            get
            {
                return Content.Where(c => ContentType.IsLaunchable(c.Type) && !c.IsCorrupt)
                              .OrderBy(c => LaunchPriority(c.Type))
                              .FirstOrDefault();
            }
        }

        public bool IsInstalled { get { return LaunchItem != null; } }

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
