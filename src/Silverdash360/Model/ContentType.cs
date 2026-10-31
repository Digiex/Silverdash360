using System.Collections.Generic;

namespace Silverdash360.Model
{
    /// <summary>What a package holds, from the folder it's stored in.</summary>
    public static class ContentType
    {
        public const uint SavedGame = 0x00000001;
        public const uint MarketplaceContent = 0x00000002;
        public const uint Publisher = 0x00000003;
        public const uint InstalledGame = 0x00004000;
        public const uint XboxOriginalGame = 0x00005000;
        public const uint GameOnDemand = 0x00007000;
        public const uint AvatarItem = 0x00009000;
        public const uint Profile = 0x00010000;
        public const uint GamerPicture = 0x00020000;
        public const uint Theme = 0x00030000;
        public const uint StorageDownload = 0x00050000;
        public const uint XboxSavedGame = 0x00060000;
        public const uint XboxDownload = 0x00070000;
        public const uint GameDemo = 0x00080000;
        public const uint Video = 0x00090000;
        public const uint GameTitle = 0x000A0000;
        public const uint Installer = 0x000B0000;
        public const uint GameTrailer = 0x000C0000;
        public const uint ArcadeTitle = 0x000D0000;
        public const uint Xna = 0x000E0000;
        public const uint LicenseStore = 0x000F0000;
        public const uint Movie = 0x00100000;
        public const uint Tv = 0x00200000;
        public const uint MusicVideo = 0x00300000;
        public const uint GameVideo = 0x00400000;
        public const uint PodcastVideo = 0x00500000;
        public const uint ViralVideo = 0x00600000;
        public const uint CommunityGame = 0x02000000;

        /// <summary>Every type, for enumerating all content.</summary>
        public const uint Any = 0xFFFFFFFF;

        private static readonly Dictionary<uint, string> Names = new Dictionary<uint, string>
        {
            { SavedGame, "Saved game" },
            { MarketplaceContent, "Add-on" },
            { Publisher, "Publisher content" },
            { InstalledGame, "Installed game" },
            { XboxOriginalGame, "Original Xbox game" },
            { GameOnDemand, "Games on Demand" },
            { AvatarItem, "Avatar item" },
            { Profile, "Profile" },
            { GamerPicture, "Gamer picture" },
            { Theme, "Theme" },
            { StorageDownload, "Download" },
            { XboxSavedGame, "Original Xbox save" },
            { XboxDownload, "Original Xbox download" },
            { GameDemo, "Demo" },
            { Video, "Video" },
            { GameTitle, "Game" },
            { Installer, "Title update" },
            { GameTrailer, "Trailer" },
            { ArcadeTitle, "Arcade game" },
            { Xna, "XNA game" },
            { LicenseStore, "License" },
            { Movie, "Movie" },
            { Tv, "TV episode" },
            { MusicVideo, "Music video" },
            { GameVideo, "Game video" },
            { PodcastVideo, "Podcast" },
            { ViralVideo, "Video" },
            { CommunityGame, "Indie game" },
        };

        public static string NameOf(uint type)
        {
            string name;
            return Names.TryGetValue(type, out name) ? name : "Content " + type.ToString("X8");
        }

        /// <summary>
        /// Videos downloaded from Xbox Live: movies, TV, music videos and a
        /// game's trailers and features. The console's dashboard lists them
        /// under its videos, not with the titles they're filed under.
        /// </summary>
        public static bool IsVideo(uint type)
        {
            switch (type)
            {
                case Video:
                case GameTrailer:
                case Movie:
                case Tv:
                case MusicVideo:
                case GameVideo:
                case PodcastVideo:
                case ViralVideo:
                    return true;
            }
            return false;
        }

        /// <summary>Content that starts a title of its own.</summary>
        public static bool IsLaunchable(uint type)
        {
            switch (type)
            {
                case GameOnDemand:
                case ArcadeTitle:
                case GameDemo:
                case GameTitle:
                case Xna:
                case CommunityGame:
                    return true;
            }
            return false;
        }
    }
}
