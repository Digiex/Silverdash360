using System.Collections.Generic;
using System.Linq;
using Silverdash360.Model;

namespace Silverdash360.Services
{
    /// <summary>
    /// Decides whether a title is a game or an app. Packages don't say, so
    /// classifiers are asked in order and the first answer wins; add one to
    /// <see cref="Library.Classifiers"/> to recognise more apps.
    /// </summary>
    public interface ITitleClassifier
    {
        /// <summary>The kind, or null to leave it to the next classifier.</summary>
        TitleKind? Classify(TitleEntry title);
    }

    /// <summary>The Xbox 360 video, music and other apps released for the console.</summary>
    public sealed class KnownAppsClassifier : ITitleClassifier
    {
        private static readonly Dictionary<uint, bool> Apps = new uint[]
        {
            0x304807D1, 0x304E07D1, 0x314107D1, 0x314107D2, 0x324807D1, 0x325A07D1, 0x333207D1,
            0x333707D1, 0x344107D1, 0x353507D1, 0x354907D1, 0x355407D1, 0x365507D1, 0x365A07D1,
            0x384B07D1, 0x395007D1, 0x395507D1, 0x395507D3, 0x413907D1, 0x423107D1, 0x423107D2,
            0x423607D1, 0x423607D3, 0x423707D1, 0x423907D1, 0x443407D1, 0x443807D1, 0x444607D1,
            0x463007D1, 0x464207D1, 0x464207D2, 0x464207D3, 0x464207D4, 0x464D07D1, 0x465807D1,
            0x473007D1, 0x475A07D1, 0x483907D1, 0x484707D1, 0x485307D1, 0x495507D1, 0x4A3707D1,
            0x4C4B07D3, 0x4D4807D2, 0x4D5207D1, 0x4D530A02, 0x4D530A1B, 0x4D530A5E, 0x4D530A6F,
            0x4E5707D1, 0x505207D1, 0x523207D9, 0x523207DB, 0x523207DF, 0x524A07D1, 0x533107D1,
            0x533607D1, 0x544807D1, 0x544A07D1, 0x545707D1, 0x563807D1, 0x564A07D1, 0x574307D1,
            0x574407D1, 0x583207D1, 0x584807D5, 0x584807DC, 0x584807E1, 0x584807E3, 0x584807EC,
            0x584807F0, 0x584807F7, 0x58480860, 0x58480861, 0x5848086A, 0x5848086C, 0x5848087C,
            0x593507D1, 0x594A07D1, 0x594C07D1, 0x594C07D2, 0x594E07D1, 0x595807D1,
            // The console's own media titles.
            0x5848085B, 0x58480880,
        }.Distinct().ToDictionary(id => id, id => true);

        public TitleKind? Classify(TitleEntry title)
        {
            return Apps.ContainsKey(title.TitleId) ? TitleKind.App : (TitleKind?)null;
        }
    }

    /// <summary>Titles found only as executables are apps or games by the folder they're in.</summary>
    public sealed class FolderClassifier : ITitleClassifier
    {
        public TitleKind? Classify(TitleEntry title)
        {
            ExecutableItem executable = title.Executable;
            if (executable == null)
            {
                return null;
            }
            return executable.IsApp ? TitleKind.App : TitleKind.Game;
        }
    }

    /// <summary>Games by what's installed or played.</summary>
    public sealed class ContentClassifier : ITitleClassifier
    {
        public TitleKind? Classify(TitleEntry title)
        {
            if (title.IsInstalled || title.Played != null)
            {
                return TitleKind.Game;
            }
            if (title.Content.Any(c => c.Type == ContentType.InstalledGame || c.Type == ContentType.XboxOriginalGame))
            {
                return TitleKind.Game;
            }
            return TitleKind.Other;
        }
    }
}
