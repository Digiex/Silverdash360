using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Silverdash360.Core;
using Silverdash360.Model;
using Silverdash360.Services.Files;
using Silverdash360.Services.Media;
using Silverlight360.Https;

namespace Silverdash360.Services
{
    /// <summary>
    /// A title's art and details. Box art, the background, the banner and
    /// the tile come first from x360db (github.com/xenia-manager/x360db), an
    /// archive of the Marketplace's art served over HTTPS, through
    /// <see cref="HttpsClient"/>: the platform's HttpWebRequest knows only
    /// the console's old certificate authorities and works on the UI thread;
    /// it's kept as x360db-{file} beside the Marketplace's. An original Xbox
    /// game x360db doesn't have gets its box art from xdb
    /// (github.com/xemu-project/xdb), kept as xdb-{file}. Art already kept
    /// from the Marketplace is shown as it is. Without them, and
    /// for everything else, the Xbox Marketplace, where the console's
    /// own dashboard gets them: images from download.xbox.com, at
    /// content/images/{media ID}/{locale}/{file}, and the details from the
    /// catalog's FindGames query. A title's media ID is
    /// 66acd000-77fe-1000-9115-d802 and its title ID, in lower case (the
    /// server doesn't find the upper-case form); the server returns the same
    /// image for any locale.
    ///
    /// What's downloaded is kept under Art/{title ID} in the app's storage
    /// and read from there after. A file the server doesn't have leaves a
    /// .missing marker, asked again after <see cref="RecheckDays"/> days.
    /// <see cref="CacheAll"/> keeps every image the catalog lists for a
    /// title, screenshots too, so they're there offline; only what's shown
    /// is read into memory.
    /// Call it from a background thread: it blocks on the disk and the
    /// network.
    ///
    /// An original Xbox game's art is under the title ID of its Xbox
    /// Originals release, not the game's own, and the console's dashboard
    /// shows none for these. The catalog's list of Xbox Originals names each
    /// release's images after the game's own ID, which maps one to the
    /// other; the map is kept in Art/XboxOriginals.map.
    /// </summary>
    public static class MarketplaceArt
    {
        /// <summary>The box front, 219 by 300.</summary>
        public const string BoxArt = "boxartlg.jpg";
        /// <summary>The backdrop the dashboard shows behind a title, 1280 by 720.</summary>
        public const string Background = "background.jpg";
        /// <summary>The FindGames reply, kept as it came.</summary>
        public const string Details = "details.xml";

        private const string ImageUrl = "http://download.xbox.com/content/images/66acd000-77fe-1000-9115-d802{0:x8}/1033/{1}";
        // x360db's own copies, by title ID in upper case, on GitHub Pages.
        private const string X360dbUrl = "https://xenia-manager.github.io/x360db/titles/{0:X8}/artwork/{1}";
        private const string X360dbPrefix = "x360db-";
        // The Marketplace's names for the images x360db keeps, and its names for them.
        private static readonly Dictionary<string, string> X360dbFiles = new Dictionary<string, string>
        {
            { BoxArt, "boxart.jpg" }, { Background, "background.jpg" }, { "banner.png", "banner.png" }, { "tile.png", "icon.png" },
        };
        // xdb (github.com/xemu-project/xdb): original Xbox games' covers, by
        // the game's own title ID; the thumbnail, 256 by 365, is box art's size.
        private const string XdbUrl = "https://raw.githubusercontent.com/xemu-project/xdb/main/titles/{0}/{1}/cover_front_thumbnail.jpg";
        private const string XdbPrefix = "xdb-";
        private const string DetailsUrl =
            "http://catalog.xboxlive.com/Catalog/Catalog.asmx/Query?methodName=FindGames" +
            "&Names=Locale&Values=en-US&Names=LegalLocale&Values=en-US&Names=Store&Values=1" +
            "&Names=PageSize&Values=1&Names=PageNum&Values=1&Names=DetailView&Values=5" +
            "&Names=OfferFilterLevel&Values=1&Names=MediaIds&Values=66acd000-77fe-1000-9115-d802{0:x8}" +
            "&Names=UserTypes&Values=2&Names=MediaTypes&Values=1&Names=MediaTypes&Values=21" +
            "&Names=MediaTypes&Values=23&Names=MediaTypes&Values=37&Names=MediaTypes&Values=46";
        // Every Xbox Originals release (media type 21), 82 of them.
        private const string OriginalsUrl =
            "http://catalog.xboxlive.com/Catalog/Catalog.asmx/Query?methodName=FindGames" +
            "&Names=Locale&Values=en-US&Names=LegalLocale&Values=en-US&Names=Store&Values=1" +
            "&Names=PageSize&Values=200&Names=PageNum&Values=1&Names=DetailView&Values=5" +
            "&Names=OfferFilterLevel&Values=1&Names=UserTypes&Values=2&Names=MediaTypes&Values=21";
        private const string OriginalsFolder = "XboxOriginals";
        private const string OriginalsList = "catalog.xml";
        private const string OriginalsMap = "Art/XboxOriginals.map";
        // A release's images under the compatibility title, named after the
        // game's own title ID and an image number.
        private const string OriginalImage = "t.fffe07d2/marketplace/0/";
        private const string Folder = "Art";
        private const string MissingSuffix = ".missing";
        // Left in a title's folder once every image the catalog lists is kept.
        private const string CompleteMarker = ".complete";
        private const string ImagePrefix = "http://download.xbox.com/content/images/";
        private const string LiveNamespace = "http://www.live.com/marketplace";
        // The server's stock "XBOX LIVE" box fronts, large and small, which
        // it sends for a title it has no box art for, the same in every
        // locale: by size, then SHA-1. They count as no art.
        private static readonly int[] PlaceholderSizes = { 7095, 2339 };
        private static readonly string[] PlaceholderHashes =
        {
            "7f058cdad2d6ddece27f4cb2b79150aeea7542a0", "2755da067c3d905c9734c08fbf711c6138dc6af9",
        };

        // Kept for every title even when the catalog doesn't list them.
        private static readonly string[] StandardImages =
        {
            "tile.png", "banner.png", Background, "boxartsm.jpg", BoxArt,
        };
        private const int RecheckDays = 7;
        // A download that takes longer is logged.
        private const int SlowMs = 3000;
        // After a request that didn't reach the server, none are made for a
        // while, so the art worker isn't held by one timeout per tile.
        private const int OfflinePauseMs = 120000;

        // A server art comes from, and whether it was last found unreachable;
        // each pauses on its own, so one being down doesn't stop the other.
        private sealed class Source
        {
            public string Name;
            public bool Offline;
            public int OfflineSince;
        }

        private static readonly Source Marketplace = new Source { Name = "Marketplace" };
        private static readonly Source X360db = new Source { Name = "x360db" };
        private static readonly Source Xdb = new Source { Name = "xdb" };

        private static readonly object stateLock = new object();
        // One request at a time: the art worker and the cache's thread both
        // download.
        private static readonly object downloadLock = new object();
        // For HTTPS, made at the first such request; one, as requests are.
        private static HttpsClient https;
        // Original Xbox title IDs to their Xbox Originals releases' ones; null
        // until read or downloaded.
        private static readonly object originalsLock = new object();
        private static Dictionary<uint, uint> originals;

        /// <summary>
        /// Whether the Marketplace can have the title: not homebrew (FFFF and
        /// C0DE) or system (FFFE) titles, which aren't asked about at all.
        /// </summary>
        public static bool Covers(uint titleId)
        {
            return titleId != 0 && !TitleEntry.IsSystemTitle(titleId) && !TitleEntry.IsHomebrewTitle(titleId);
        }

        /// <summary>One of the title's images, kept or downloaded; null when there's none.</summary>
        public static byte[] Image(uint titleId, string file)
        {
            if (!Covers(titleId))
            {
                return null;
            }
            // An original Xbox game is in x360db as the Marketplace has it,
            // under its Xbox Originals release; one that isn't has its cover
            // from xdb, under its own ID.
            uint game = titleId;
            titleId = ReleaseOf(titleId);
            // Art already kept from the Marketplace stays; x360db is for
            // what isn't kept yet.
            byte[] image = Kept(titleId, file) ?? FromX360db(titleId, file) ?? FromXdb(game, file);
            if (image != null)
            {
                return image;
            }
            return Get(Marketplace, titleId, file, string.Format(ImageUrl, titleId, file));
        }

        // xdb's cover of an original Xbox game, as its box art; null otherwise.
        private static byte[] FromXdb(uint titleId, string file)
        {
            return file == BoxArt && TitleEntry.IsOriginalXboxTitleId(titleId)
                ? Get(Xdb, titleId.ToString("X8"), XdbPrefix + file, XdbCoverUrl(titleId))
                : null;
        }

        // xdb's folders are the title ID's publisher letters, then its game
        // number in decimal: 4D530002 is MS/002.
        private static string XdbCoverUrl(uint titleId)
        {
            string publisher = new string(new[] { (char)(titleId >> 24), (char)((titleId >> 16) & 0xFF) });
            return string.Format(XdbUrl, publisher, (titleId & 0xFFFF).ToString("D3"));
        }

        // The Marketplace's copy of a file, if it's kept; nothing is downloaded.
        private static byte[] Kept(uint titleId, string file)
        {
            byte[] kept = AppStorage.ReadBytes(Folder + "/" + titleId.ToString("X8") + "/" + file);
            return kept != null && kept.Length > 0 && !IsPlaceholder(kept) ? kept : null;
        }

        // x360db's copy of one of the images it keeps, under the title ID given; null otherwise.
        private static byte[] FromX360db(uint titleId, string file)
        {
            string name;
            return X360dbFiles.TryGetValue(file, out name)
                ? Get(X360db, titleId.ToString("X8"), X360dbPrefix + file, string.Format(X360dbUrl, titleId, name))
                : null;
        }

        /// <summary>The title's details, kept or downloaded; null when there are none.</summary>
        public static TitleDetails ReadDetails(uint titleId)
        {
            if (!Covers(titleId))
            {
                return null;
            }
            titleId = ReleaseOf(titleId);
            byte[] xml = Get(Marketplace, titleId, Details, string.Format(DetailsUrl, titleId));
            if (xml == null)
            {
                return null;
            }
            try
            {
                return TitleDetails.Parse(XDocument.Load(new MemoryStream(xml)));
            }
            catch (Exception e)
            {
                Log.Write("Marketplace details for {0:X8} unreadable: {1}", titleId, e.Message);
                return null;
            }
        }

        /// <summary>
        /// Keeps every image the catalog lists for the title, and the
        /// standard ones. False when it stopped because the server couldn't
        /// be reached; what was kept stays, and the next call goes on.
        /// </summary>
        public static bool CacheAll(uint titleId)
        {
            if (!Covers(titleId))
            {
                return true;
            }
            uint game = titleId;
            titleId = ReleaseOf(titleId);
            string folder = Folder + "/" + titleId.ToString("X8") + "/";
            if (AppStorage.Stat(folder + CompleteMarker) != null)
            {
                return true;
            }
            foreach (string file in X360dbFiles.Keys)
            {
                if (Kept(titleId, file) == null && FromX360db(titleId, file) == null && file == BoxArt)
                {
                    FromXdb(game, file);
                }
            }
            byte[] xml = Get(Marketplace, titleId, Details, string.Format(DetailsUrl, titleId));
            if (IsOffline)
            {
                return false;
            }
            List<string> urls = new List<string>();
            if (xml != null)
            {
                try
                {
                    XNamespace live = LiveNamespace;
                    foreach (XElement url in XDocument.Load(new MemoryStream(xml)).Descendants(live + "fileUrl"))
                    {
                        string value = url.Value.Trim();
                        if (value.StartsWith(ImagePrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            urls.Add(value);
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Write("Marketplace details for {0:X8} unreadable: {1}", titleId, e.Message);
                }
            }
            foreach (string file in StandardImages)
            {
                urls.Add(string.Format(ImageUrl, titleId, file));
            }
            // A file is named as on the server; the same name in another
            // locale's folder is the same image.
            Dictionary<string, bool> kept = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (string url in urls)
            {
                string file = url.Substring(url.LastIndexOf('/') + 1);
                if (file.Length == 0 || kept.ContainsKey(file))
                {
                    continue;
                }
                kept[file] = true;
                Get(Marketplace, titleId, file, url);
                if (IsOffline)
                {
                    return false;
                }
            }
            AppStorage.WriteBytes(folder + CompleteMarker, new byte[0], null);
            return true;
        }

        private static bool IsOffline
        {
            get
            {
                lock (stateLock)
                {
                    return Marketplace.Offline;
                }
            }
        }

        /// <summary>
        /// The title ID the Marketplace has a title's art under: for an
        /// original Xbox game, its Xbox Originals release's, when it had one.
        /// </summary>
        public static uint ReleaseOf(uint titleId)
        {
            if (!TitleEntry.IsOriginalXboxTitleId(titleId))
            {
                return titleId;
            }
            Dictionary<uint, uint> map = Originals();
            uint release;
            return map != null && map.TryGetValue(titleId, out release) ? release : titleId;
        }

        // The map of original Xbox games to their releases, read or made from
        // the catalog's list; null while that can't be had.
        private static Dictionary<uint, uint> Originals()
        {
            lock (originalsLock)
            {
                if (originals != null)
                {
                    return originals;
                }
                byte[] kept = AppStorage.ReadBytes(OriginalsMap);
                if (kept != null && kept.Length > 0)
                {
                    originals = ReadMap(kept);
                    return originals;
                }
                byte[] list = Get(Marketplace, OriginalsFolder, OriginalsList, OriginalsUrl);
                if (list == null)
                {
                    return null;
                }
                originals = MapReleases(Encoding.UTF8.GetString(list, 0, list.Length));
                StringBuilder text = new StringBuilder();
                foreach (KeyValuePair<uint, uint> pair in originals)
                {
                    text.AppendFormat("{0:X8}={1:X8}\n", pair.Key, pair.Value);
                }
                AppStorage.WriteBytes(OriginalsMap, Encoding.UTF8.GetBytes(text.ToString()), null);
                // The list is large and only the map is read again.
                AppStorage.Delete(Folder + "/" + OriginalsFolder + "/" + OriginalsList);
                Log.Write("Marketplace: {0} original Xbox games have Xbox Originals art", originals.Count);
                return originals;
            }
        }

        private static Dictionary<uint, uint> ReadMap(byte[] text)
        {
            Dictionary<uint, uint> map = new Dictionary<uint, uint>();
            foreach (string line in Encoding.UTF8.GetString(text, 0, text.Length).Split('\n'))
            {
                string[] parts = line.Trim().Split('=');
                uint game, release;
                if (parts.Length == 2 && uint.TryParse(parts[0], NumberStyles.HexNumber, null, out game) &&
                    uint.TryParse(parts[1], NumberStyles.HexNumber, null, out release))
                {
                    map[game] = release;
                }
            }
            return map;
        }

        // Each release in the catalog's list with the games whose IDs name
        // its images. An ID under several releases goes to the one from the
        // game's publisher (the first four digits): some releases show other
        // publishers' images too. Several from the publisher are the same
        // game listed twice; with none, it isn't known whose game it is.
        private static Dictionary<uint, uint> MapReleases(string list)
        {
            Dictionary<uint, List<uint>> candidates = new Dictionary<uint, List<uint>>();
            foreach (string entry in list.Split(new[] { "<entry" }, StringSplitOptions.None).Skip(1))
            {
                uint release;
                if (!uint.TryParse(Between(entry, "<live:titleId>", "</live:titleId>"), out release) || release == 0)
                {
                    continue;
                }
                for (int at = entry.IndexOf(OriginalImage, StringComparison.OrdinalIgnoreCase); at >= 0;
                     at = entry.IndexOf(OriginalImage, at + 1, StringComparison.OrdinalIgnoreCase))
                {
                    int start = at + OriginalImage.Length;
                    uint game;
                    if (start + 8 > entry.Length ||
                        !uint.TryParse(entry.Substring(start, 8), NumberStyles.HexNumber, null, out game) ||
                        !TitleEntry.IsOriginalXboxTitleId(game))
                    {
                        continue;
                    }
                    List<uint> releases;
                    if (!candidates.TryGetValue(game, out releases))
                    {
                        candidates[game] = releases = new List<uint>();
                    }
                    if (!releases.Contains(release))
                    {
                        releases.Add(release);
                    }
                }
            }
            Dictionary<uint, uint> map = new Dictionary<uint, uint>();
            foreach (KeyValuePair<uint, List<uint>> pair in candidates)
            {
                List<uint> releases = pair.Value;
                if (releases.Count > 1)
                {
                    releases = releases.Where(r => r >> 16 == pair.Key >> 16).ToList();
                }
                if (releases.Count > 0)
                {
                    map[pair.Key] = releases[0];
                }
            }
            return map;
        }

        private static string Between(string text, string open, string close)
        {
            int start = text.IndexOf(open, StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }
            start += open.Length;
            int end = text.IndexOf(close, start, StringComparison.Ordinal);
            return end < 0 ? null : text.Substring(start, end - start);
        }

        private static byte[] Get(Source source, uint titleId, string file, string url)
        {
            return Get(source, titleId.ToString("X8"), file, url);
        }

        private static byte[] Get(Source source, string name, string file, string url)
        {
            string path = Folder + "/" + name + "/" + file;
            byte[] kept = AppStorage.ReadBytes(path);
            if (kept != null && kept.Length > 0)
            {
                if (!IsPlaceholder(kept))
                {
                    return kept;
                }
                // Kept before placeholders were told apart.
                AppStorage.Delete(path);
                AppStorage.WriteBytes(path + MissingSuffix, new byte[0], null);
                return null;
            }
            Activity.Art = "checking " + path;
            FileEntry missing = AppStorage.Stat(path + MissingSuffix);
            if (missing != null && (DateTime.Now - missing.Modified).TotalDays < RecheckDays)
            {
                return null;
            }
            int status;
            byte[] body;
            // One download at a time. Whether the Marketplace is answering is
            // looked at once it's this one's turn: a download that waited
            // behind one that found no internet doesn't wait for its own
            // answer as long again.
            lock (downloadLock)
            {
                lock (stateLock)
                {
                    if (source.Offline && Environment.TickCount - source.OfflineSince < OfflinePauseMs)
                    {
                        return null;
                    }
                    source.Offline = false;
                }
                body = Download(new Uri(url), out status);
            }
            if (body != null && status == 200 && (!IsComplete(file, body) || IsPlaceholder(body)))
            {
                // The catalog answers an unknown title with an empty feed,
                // the image server with its stock box front.
                status = 404;
            }
            else if (body != null && status == 200)
            {
                Activity.Art = "saving " + path;
                AppStorage.WriteBytes(path, body, null);
                if (missing != null)
                {
                    AppStorage.Delete(path + MissingSuffix);
                }
                return body;
            }
            lock (stateLock)
            {
                if (status == 404)
                {
                    AppStorage.WriteBytes(path + MissingSuffix, new byte[0], null);
                }
                else
                {
                    source.Offline = true;
                    source.OfflineSince = Environment.TickCount;
                    Log.Write("{0}: {1} for {2}; trying again in {3} s", source.Name, status, url, OfflinePauseMs / 1000);
                }
            }
            return null;
        }

        private static bool IsPlaceholder(byte[] body)
        {
            if (Array.IndexOf(PlaceholderSizes, body.Length) < 0)
            {
                return false;
            }
            byte[] digest = new SHA1Managed().ComputeHash(body);
            StringBuilder hex = new StringBuilder(digest.Length * 2);
            foreach (byte b in digest)
            {
                hex.Append(b.ToString("x2"));
            }
            return Array.IndexOf(PlaceholderHashes, hex.ToString()) >= 0;
        }

        // Whether a reply has what was asked for: the catalog's feed for an
        // unknown title is empty.
        private static bool IsComplete(string file, byte[] body)
        {
            if (file != Details && file != OriginalsList)
            {
                return body.Length > 0;
            }
            return Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 512)).IndexOf("<live:totalItems>0<", StringComparison.Ordinal) < 0;
        }

        // A GET on this thread, over the dashboard's own HTTP client: the
        // platform's HttpWebRequest does its work on the UI thread, which a
        // slow reply then holds up. One redirect is followed. HTTPS goes
        // through Silverlight360.Https.
        private static byte[] Download(Uri url, out int status)
        {
            lock (downloadLock)
            {
                InFlight = url.ToString();
                Activity.Art = "sending " + url;
                int start = Environment.TickCount;
                try
                {
                    if (url.Scheme == "https")
                    {
                        if (https == null)
                        {
                            https = new HttpsClient { ConnectTimeoutSeconds = 15, TimeoutSeconds = 30 };
                        }
                        HttpsResponse response = https.Get(url.ToString());
                        status = response.Status;
                        return status == 200 ? response.Body : null;
                    }
                    for (int redirects = 0; ; redirects++)
                    {
                        using (Http.Response response = Http.Send("GET", url, null, null))
                        {
                            status = response.Status;
                            Activity.Art = "reading " + status + " " + url;
                            string location = response.Header("Location");
                            if ((status == 301 || status == 302) && location != null && redirects == 0)
                            {
                                url = new Uri(url, location);
                                continue;
                            }
                            return status == 200 ? response.ReadBytes() : null;
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Write("Art: {0} failed: {1}", url, e.Message);
                    status = 0;
                    return null;
                }
                finally
                {
                    int ms = Environment.TickCount - start;
                    if (ms > SlowMs)
                    {
                        Log.Write("Art: {0} took {1} ms", url, ms);
                    }
                    InFlight = null;
                    Activity.Art = "downloaded " + url;
                }
            }
        }

        /// <summary>The URL being downloaded now, or null, for the heartbeat's log line.</summary>
        public static string InFlight { get; private set; }
    }

    /// <summary>What the Marketplace says about a title.</summary>
    public sealed class TitleDetails
    {
        private static readonly XNamespace Live = "http://www.live.com/marketplace";

        public string Name = "";
        public string Description = "";
        public string Developer = "";
        public string Publisher = "";
        public string Genre = "";
        /// <summary>The release date as the catalog gives it, yyyy-mm-dd, or empty.</summary>
        public string ReleaseDate = "";

        internal static TitleDetails Parse(XDocument feed)
        {
            XElement media = feed.Descendants(Live + "media").FirstOrDefault();
            if (media == null)
            {
                return null;
            }
            TitleDetails details = new TitleDetails
            {
                Name = Text(media, "fullTitle"),
                Description = Text(media, "description"),
                Developer = Text(media, "developer"),
                Publisher = Text(media, "publisher"),
                ReleaseDate = Text(media, "releaseDate"),
            };
            if (details.Description.Length == 0)
            {
                details.Description = Text(media, "reducedDescription");
            }
            details.Description = PlainText(details.Description);
            if (details.ReleaseDate.Length >= 10)
            {
                details.ReleaseDate = details.ReleaseDate.Substring(0, 10);
            }
            // The first category that isn't a group of them, such as "Game
            // Genres" or "Xbox LIVE Games", is the genre.
            foreach (XElement category in feed.Descendants(Live + "category"))
            {
                string name = Text(category, "name");
                if (name.Length > 0 && !name.EndsWith("Games") && !name.EndsWith("Genres"))
                {
                    details.Genre = name;
                    break;
                }
            }
            return details;
        }

        private static string Text(XElement parent, string name)
        {
            XElement element = parent.Element(Live + name);
            return element != null ? element.Value.Trim() : "";
        }

        // Descriptions can carry a little HTML: line breaks and lists.
        private static string PlainText(string text)
        {
            StringBuilder plain = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                if (text[i] == '<')
                {
                    int end = text.IndexOf('>', i);
                    if (end < 0)
                    {
                        break;
                    }
                    string tag = text.Substring(i + 1, end - i - 1).Trim().ToLowerInvariant();
                    if (tag.StartsWith("br") || tag == "/p" || tag == "/li" || tag == "/div")
                    {
                        plain.Append('\n');
                    }
                    else if (tag == "li")
                    {
                        plain.Append("\u2022 ");
                    }
                    i = end + 1;
                    continue;
                }
                plain.Append(text[i]);
                i++;
            }
            return plain.ToString().Replace("&amp;", "&").Replace("&quot;", "\"").Replace("&#39;", "'").Trim();
        }
    }
}
