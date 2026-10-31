using System;

namespace Silverdash360.Model
{
    /// <summary>A signed-in profile.</summary>
    public sealed class Gamer
    {
        public uint UserIndex { get; set; }
        public ulong Xuid { get; set; }

        /// <summary>
        /// The profile's own XUID, which its content and its package are
        /// stored under; the same as <see cref="Xuid"/> unless it's signed in
        /// to LIVE.
        /// </summary>
        public ulong OfflineXuid { get; set; }
        public string Gamertag { get; set; }
        public SigninState State { get; set; }
        public int Gamerscore { get; set; }
        public int AchievementsEarned { get; set; }
        public int TitlesPlayed { get; set; }
        public string Motto { get; set; }
        public int Zone { get; set; }

        public string ZoneName
        {
            get
            {
                switch (Zone)
                {
                    case 1: return "Recreation";
                    case 2: return "Pro";
                    case 3: return "Family";
                    case 4: return "Underground";
                    default: return "";
                }
            }
        }

        public string XuidText { get { return Xuid.ToString("X16"); } }
    }

    public enum SigninState
    {
        NotSignedIn = 0,
        SignedInLocally = 1,
        SignedInToLive = 2,
    }

    /// <summary>A title in the profile's played list.</summary>
    public sealed class PlayedTitle
    {
        public uint TitleId { get; set; }
        public string Name { get; set; }
        public int AchievementsTotal { get; set; }
        public int AchievementsUnlocked { get; set; }
        public int GamerscoreTotal { get; set; }
        public int GamerscoreEarned { get; set; }
        public DateTime? LastPlayed { get; set; }

        public double Progress
        {
            get { return GamerscoreTotal > 0 ? (double)GamerscoreEarned / GamerscoreTotal : 0; }
        }
    }

    /// <summary>An achievement of a title, for one profile.</summary>
    public sealed class Achievement
    {
        public uint Id { get; set; }
        public string Label { get; set; }
        public string Description { get; set; }
        public string Unachieved { get; set; }
        public uint ImageId { get; set; }
        public int Gamerscore { get; set; }
        public DateTime? Unlocked { get; set; }
        public uint Flags { get; set; }

        public bool IsUnlocked { get { return (Flags & 0x00020000) != 0 || (Flags & 0x00010000) != 0; } }

        /// <summary>Secret achievements show only their locked text.</summary>
        public bool IsSecret { get { return (Flags & 0x8) == 0; } }
    }
}
