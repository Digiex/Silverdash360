using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Silverdash360.Core;
using Silverdash360.Model;
using Silverdash360.Services.Files;
using Silverdash360.Services.Media;

namespace Silverdash360.Services
{
    /// <summary>
    /// A title's art and details from the Xbox Marketplace, where the console's
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
        private const string DetailsUrl =
            "http://catalog.xboxlive.com/Catalog/Catalog.asmx/Query?methodName=FindGames" +
            "&Names=Locale&Values=en-US&Names=LegalLocale&Values=en-US&Names=Store&Values=1" +
            "&Names=PageSize&Values=1&Names=PageNum&Values=1&Names=DetailView&Values=5" +
            "&Names=OfferFilterLevel&Values=1&Names=MediaIds&Values=66acd000-77fe-1000-9115-d802{0:x8}" +
            "&Names=UserTypes&Values=2&Names=MediaTypes&Values=1&Names=MediaTypes&Values=21" +
            "&Names=MediaTypes&Values=23&Names=MediaTypes&Values=37&Names=MediaTypes&Values=46";
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

        private static readonly object stateLock = new object();
        // One request at a time: the art worker and the cache's thread both
        // download.
        private static readonly object downloadLock = new object();
        private static bool offline;
        private static int offlineSince;

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
            return Covers(titleId) ? Get(titleId, file, string.Format(ImageUrl, titleId, file)) : null;
        }

        /// <summary>The title's details, kept or downloaded; null when there are none.</summary>
        public static TitleDetails ReadDetails(uint titleId)
        {
            byte[] xml = Covers(titleId) ? Get(titleId, Details, string.Format(DetailsUrl, titleId)) : null;
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
            string folder = Folder + "/" + titleId.ToString("X8") + "/";
            if (AppStorage.Stat(folder + CompleteMarker) != null)
            {
                return true;
            }
            byte[] xml = Get(titleId, Details, string.Format(DetailsUrl, titleId));
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
                Get(titleId, file, url);
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
                    return offline;
                }
            }
        }

        private static byte[] Get(uint titleId, string file, string url)
        {
            string path = Folder + "/" + titleId.ToString("X8") + "/" + file;
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
                    if (offline && Environment.TickCount - offlineSince < OfflinePauseMs)
                    {
                        return null;
                    }
                    offline = false;
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
                    offline = true;
                    offlineSince = Environment.TickCount;
                    Log.Write("Marketplace: {0} for {1}; trying again in {2} s", status, url, OfflinePauseMs / 1000);
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
            if (file != Details)
            {
                return body.Length > 0;
            }
            return Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 512)).IndexOf("<live:totalItems>0<", StringComparison.Ordinal) < 0;
        }

        // A GET on this thread, over the dashboard's own HTTP client: the
        // platform's HttpWebRequest does its work on the UI thread, which a
        // slow reply then holds up. One redirect is followed.
        private static byte[] Download(Uri url, out int status)
        {
            lock (downloadLock)
            {
                InFlight = url.ToString();
                Activity.Art = "sending " + url;
                int start = Environment.TickCount;
                try
                {
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
                    Log.Write("Marketplace: {0} failed: {1}", url, e.Message);
                    status = 0;
                    return null;
                }
                finally
                {
                    int ms = Environment.TickCount - start;
                    if (ms > SlowMs)
                    {
                        Log.Write("Marketplace: {0} took {1} ms", url, ms);
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
