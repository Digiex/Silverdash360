using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Silverdash360.Model;
using Silverdash360.Modules;
using Silverdash360.Native;
using Silverdash360.Services;
using Silverdash360.Services.Files;
using Silverdash360.Services.Ftp;
using Silverlight360;
using Silverlight360.Https;

namespace Silverdash360.Debugging
{
    /// <summary>
    /// The console's own functions, called the way the dashboard calls them
    /// but straight from the command, for checking what the system returns.
    /// Everything here only reads.
    /// </summary>
    public sealed class ApiCommands : IDebugCommands
    {
        private const int MaxListed = 500;
        private const int MaxRead = 4096;

        private readonly DashboardContext context;

        public ApiCommands(DashboardContext context)
        {
            this.context = context;
        }

        private XboxFileSystem Files { get { return context.Ftp.Files; } }

        public void Register(DebugCommandSet commands)
        {
            commands.Add("xam.system", "", "XamGetSystemVersion, XamGetCurrentTitleId and XGetLanguage, raw.", r => new JsonObject
            {
                { "systemVersion", Xam.XamGetSystemVersion().ToString("X8") },
                { "titleId", Xam.XamGetCurrentTitleId().ToString("X8") },
                { "language", Xam.XGetLanguage() },
            });
            commands.Add("xam.signin", "", "Each controller's sign-in state, and the profiles signed in.", r => Signin());
            commands.Add("xam.content", "[xuid]", "Enumerates every package for common content (XUID 0) or a profile.", Content);
            commands.Add("xam.metadata", "<contentIndex>", "Reads a package header (as library.content numbers them).", Metadata);
            commands.Add("xam.tile", "<titleId> [type] [imageId]", "XamReadTile: type 1 (game icon) and image 0x8000 by default.", Tile);
            commands.Add("marketplace", "<titleId> [file]", "A title's Marketplace art (boxartlg.jpg by default) and details, kept or downloaded.", Marketplace);
            commands.Add("xam.played", "", "The signed-in profile's played titles.", r => Played());
            commands.Add("xam.achievements", "<titleId>", "The signed-in profile's achievements for a title.", Achievements);
            commands.Add("net.address", "", "The console's address from XNetGetTitleXnAddr.", r => Address());
            commands.Add("net.fetch", "<url> [timeoutMs] [accept]", "A GET through the platform's HttpWebRequest (HTTPS too): the status, or the error.", Fetch);
            commands.Add("net.https", "<url> [timeoutSeconds]", "A GET through Silverlight360.Https (TLS 1.2 and 1.3): the status, TLS, size and time, or the error.", Https);
            commands.Add("net.dropin", "[http]", "Sends the platform's https:// requests (and with http, http://) through Silverlight360.Https's HttpsWebRequest, from now until the dashboard starts again; net.fetch and net.webclient then use it.",
                         r => HttpsRequestCreator.Register(r.Arg(0, "") == "http"));
            commands.Add("net.webclient", "<url> [timeoutMs] [body]", "A WebClient.DownloadStringAsync, or with a body UploadStringAsync (a POST): the length, the start, or the error.", WebClientGet);
            commands.Add("fs.drives", "", "The drives the dashboard knows and which are present.", r => Drives());
            commands.Add("fs.list", "<path>", "A folder's entries, such as /Hdd1/Content.", List);
            commands.Add("fs.device", "<devicePath>", "A folder's entries by the kernel's name, such as \\Device\\Harddisk0\\SystemExtPartition.",
                         r => Files.ListDevice(r.Arg(0, "devicePath", true)).Take(MaxListed).Select(e => Entry(e)).ToList());
            commands.Add("fs.stat", "<path>", "A file or folder's size, kind and time.", Stat);
            commands.Add("fs.read", "<path> [bytes] [offset]", "Bytes of a file, in hex (at most " + MaxRead + ").", Read);
            commands.Add("xconfig.get", "<category> <setting> <size>", "An XConfig setting's bytes, in hex; numbers in hex.", r =>
            {
                byte[] value = XConfig.Get(Convert.ToUInt16(r.Arg(0, "category", true), 16), Convert.ToUInt16(r.Arg(1, "setting", true), 16),
                                           Convert.ToInt32(r.Arg(2, "size", true), 16));
                return value == null ? null : BitConverter.ToString(value).Replace("-", "");
            });
            commands.Add("xconfig.av", "", "The AV cable and the display's discovery data.", r => new JsonObject
            {
                { "avPack", XConfig.AVPack() },
                { "discovery", XConfig.DisplayDiscovery() != null ? BitConverter.ToString(XConfig.DisplayDiscovery()).Replace("-", "") : null },
            });
        }

        private object Signin()
        {
            List<JsonObject> users = new List<JsonObject>();
            for (uint i = 0; i < Xam.MaxUsers; i++)
            {
                users.Add(new JsonObject { { "index", i }, { "state", Xam.XamUserGetSigninState(i) } });
            }
            return new JsonObject
            {
                { "users", users },
                { "signedIn", context.Profiles.SignedIn().Select(g => new JsonObject
                    {
                        { "index", g.UserIndex },
                        { "xuid", g.Xuid.ToString("X16") },
                        { "gamertag", g.Gamertag },
                        { "state", g.State.ToString() },
                        { "gamerscore", g.Gamerscore },
                    }).ToList()
                },
            };
        }

        private object Content(DebugRequest request)
        {
            ulong xuid = request.Args.Count > 0 ? Convert.ToUInt64(request.Args[0], 16) : 0;
            int start = Environment.TickCount;
            uint type = request.Args.Count > 1 ? Convert.ToUInt32(request.Args[1], 16) : ContentType.Any;
            uint title = request.Args.Count > 2 ? Convert.ToUInt32(request.Args[2], 16) : 0;
            List<ContentItem> items = new XamContentSource().EnumerateType(xuid, type, title);
            return new JsonObject
            {
                { "xuid", xuid.ToString("X16") },
                { "enumerateMs", Environment.TickCount - start },
                { "count", items.Count },
                { "titles", items.Select(c => c.TitleId).Distinct().Count() },
                { "byType", items.GroupBy(c => c.TypeName).Select(g => new JsonObject { { "type", g.Key }, { "count", g.Count() } }).ToList() },
            };
        }

        private object Metadata(DebugRequest request)
        {
            int index = request.Int(0, 0);
            IList<ContentItem> content = context.Library.AllContent;
            if (index < 0 || index >= content.Count)
            {
                throw new DebugException("No package " + index + "; the library has " + content.Count + ".");
            }
            ContentItem item = content[index];
            int start = Environment.TickCount;
            ContentMetadata m = new XamContentSource().ReadMetadata(item, true);
            JsonObject result = new JsonObject { { "readMs", Environment.TickCount - start }, { "item", LibraryCommands.ContentSummary(item) } };
            if (m == null)
            {
                throw new DebugException("XamContentGetMetaDataInternal failed for " + item + ".");
            }
            result.Add("contentType", m.ContentType.ToString("X8"));
            result.Add("titleId", m.TitleId.ToString("X8"));
            result.Add("titleName", m.TitleName);
            result.Add("displayName", m.DisplayName);
            result.Add("publisher", m.Publisher);
            result.Add("version", m.VersionText);
            result.Add("thumbnail", Png(m.ThumbnailPng));
            result.Add("titleThumbnail", Png(m.TitleThumbnailPng));
            return result;
        }

        private object Tile(DebugRequest request)
        {
            uint titleId = request.Hex(0, "title ID");
            uint type = (uint)request.Int(1, (int)ImageService.TileGameIcon);
            ulong imageId = request.Args.Count > 2 ? Convert.ToUInt64(request.Args[2], 16) : ImageService.TitleImageId;
            int start = Environment.TickCount;
            byte[] png = ImageService.ReadTile(type, titleId, imageId, context.UserIndex);
            JsonObject result = Png(png);
            result.Add("readMs", Environment.TickCount - start);
            return result;
        }

        private object Marketplace(DebugRequest request)
        {
            uint titleId = request.Hex(0, "title ID");
            string file = request.Args.Count > 1 ? request.Args[1] : MarketplaceArt.BoxArt;
            int start = Environment.TickCount;
            byte[] image = MarketplaceArt.Image(titleId, file);
            int imageMs = Environment.TickCount - start;
            start = Environment.TickCount;
            TitleDetails details = MarketplaceArt.ReadDetails(titleId);
            return new JsonObject
            {
                { "file", file },
                { "bytes", image != null ? image.Length : -1 },
                { "imageMs", imageMs },
                { "detailsMs", Environment.TickCount - start },
                { "name", details != null ? details.Name : null },
                { "publisher", details != null ? details.Publisher : null },
                { "developer", details != null ? details.Developer : null },
                { "genre", details != null ? details.Genre : null },
                { "released", details != null ? details.ReleaseDate : null },
                { "description", details != null ? details.Description : null },
            };
        }

        private object Played()
        {
            Gamer gamer = SignedIn();
            int start = Environment.TickCount;
            List<PlayedTitle> titles = context.Profiles.PlayedTitles(gamer);
            return new JsonObject
            {
                { "readMs", Environment.TickCount - start },
                { "count", titles.Count },
                { "titles", titles.Take(20).Select(t => new JsonObject
                    {
                        { "titleId", t.TitleId.ToString("X8") },
                        { "name", t.Name },
                        { "achievements", t.AchievementsUnlocked },
                        { "achievementsTotal", t.AchievementsTotal },
                        { "lastPlayed", t.LastPlayed },
                    }).ToList()
                },
            };
        }

        private object Achievements(DebugRequest request)
        {
            uint titleId = request.Hex(0, "title ID");
            Gamer gamer = SignedIn();
            int start = Environment.TickCount;
            List<Achievement> achievements = context.Profiles.Achievements(gamer, titleId);
            return new JsonObject
            {
                { "readMs", Environment.TickCount - start },
                { "count", achievements.Count },
                { "unlocked", achievements.Count(a => a.IsUnlocked) },
                { "achievements", achievements.Take(50).Select(a => new JsonObject
                    {
                        { "id", a.Id }, { "label", a.Label }, { "gamerscore", a.Gamerscore },
                        { "unlocked", a.IsUnlocked }, { "imageId", a.ImageId },
                    }).ToList()
                },
            };
        }

        // The platform's HttpWebRequest, made and answered on the UI thread;
        // this thread polls for the answer.
        private static object WebClientGet(DebugRequest request)
        {
            Uri url = new Uri(request.Arg(0, "url", true), UriKind.Absolute);
            int timeout = request.Int(1, 20000);
            string upload = request.Arg(2, null);
            JsonObject result = new JsonObject { { "url", url.ToString() } };
            System.Threading.ManualResetEvent done = new System.Threading.ManualResetEvent(false);
            int start = Environment.TickCount;
            UiThread.Invoke(() =>
            {
                System.Net.WebClient web = new System.Net.WebClient();
                Action<Exception, string> finished = delegate(Exception error, string text)
                {
                    if (error != null)
                    {
                        result.Add("error", error.GetType().Name + ": " + error.Message);
                        System.Net.WebException web2 = error as System.Net.WebException;
                        System.Net.HttpWebResponse failed = web2 != null ? web2.Response as System.Net.HttpWebResponse : null;
                        if (failed != null)
                        {
                            result.Add("status", (int)failed.StatusCode);
                        }
                    }
                    else
                    {
                        result.Add("length", text.Length);
                        result.Add("start", text.Length > 400 ? text.Substring(0, 400) : text);
                        result.Add("contentType", web.ResponseHeaders != null ? web.ResponseHeaders["Content-Type"] : null);
                    }
                    result.Add("ms", Environment.TickCount - start);
                    done.Set();
                };
                if (upload == null)
                {
                    web.DownloadStringCompleted += (sender, e) => finished(e.Error, e.Error == null ? e.Result : null);
                    web.DownloadStringAsync(url);
                }
                else
                {
                    web.Headers["Content-Type"] = "text/plain";
                    web.UploadStringCompleted += (sender, e) => finished(e.Error, e.Error == null ? e.Result : null);
                    web.UploadStringAsync(url, "POST", upload);
                }
                return 0;
            });
            if (!done.WaitOne(timeout))
            {
                result.Add("error", "no answer in " + timeout + " ms");
            }
            return result;
        }

        private static object Https(DebugRequest request)
        {
            string url = request.Arg(0, "url", true);
            JsonObject result = new JsonObject { { "url", url } };
            try
            {
                using (HttpsClient client = new HttpsClient { TimeoutSeconds = request.Int(1, 30) })
                {
                    HttpsResponse response = client.Get(url);
                    result.Add("status", response.Status);
                    result.Add("contentType", response.Header("Content-Type"));
                    result.Add("length", response.Body.Length);
                    result.Add("tls", response.TlsVersion);
                    result.Add("cipher", response.Cipher);
                    result.Add("ms", response.ElapsedMilliseconds);
                    result.Add("heapPeak", HttpsClient.HeapPeak);
                }
            }
            catch (Exception e)
            {
                result.Add("error", e.GetType().Name + ": " + e.Message);
            }
            return result;
        }

        private static object Fetch(DebugRequest request)
        {
            Uri url = new Uri(request.Arg(0, "url", true), UriKind.Absolute);
            int timeout = request.Int(1, 20000);
            string accept = request.Arg(2, null);
            JsonObject result = new JsonObject { { "url", url.ToString() } };
            bool done = false;
            int start = Environment.TickCount;
            UiThread.Invoke(() =>
            {
                System.Net.HttpWebRequest web = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                if (accept != null)
                {
                    web.Accept = accept;
                }
                web.BeginGetResponse(answer =>
                {
                    try
                    {
                        using (System.Net.HttpWebResponse response = (System.Net.HttpWebResponse)web.EndGetResponse(answer))
                        using (System.IO.StreamReader reader = new System.IO.StreamReader(response.GetResponseStream()))
                        {
                            string body = reader.ReadToEnd();
                            result.Add("status", (int)response.StatusCode);
                            // HttpsWebResponse once net.dropin has run.
                            result.Add("type", response.GetType().Name);
                            result.Add("contentType", response.ContentType);
                            result.Add("length", body.Length);
                            result.Add("start", body.Length > 300 ? body.Substring(0, 300) : body);
                        }
                    }
                    catch (Exception e)
                    {
                        result.Add("error", e.GetType().Name + ": " + e.Message);
                        if (e.InnerException != null)
                        {
                            result.Add("inner", e.InnerException.GetType().Name + ": " + e.InnerException.Message);
                        }
                    }
                    result.Add("ms", Environment.TickCount - start);
                    done = true;
                }, null);
                return 0;
            });
            while (!done && Environment.TickCount - start < timeout)
            {
                System.Threading.Thread.Sleep(50);
            }
            if (!done)
            {
                result.Add("error", "No answer in " + timeout + " ms");
            }
            return result;
        }

        private object Address()
        {
            using (NativeBuffer address = new NativeBuffer(0x24))
            {
                uint status = Net.XNetGetTitleXnAddr(Net.CallerTitle, address.Address);
                uint ip = address.U32(0);
                return new JsonObject
                {
                    { "status", status.ToString("X8") },
                    { "address", ip != 0 ? NativeSocket.FormatAddress(ip) : null },
                };
            }
        }

        private object Drives()
        {
            List<Drive> available = Files.AvailableDrives.ToList();
            return Files.Drives.Select(d => new JsonObject
            {
                { "name", d.Name },
                { "devicePath", d.DevicePath },
                { "readOnly", d.ReadOnly },
                { "present", available.Contains(d) },
            }).ToList();
        }

        private object List(DebugRequest request)
        {
            string path = request.Arg(0, "path", true);
            int start = Environment.TickCount;
            List<FileEntry> entries = Files.List(path);
            return new JsonObject
            {
                { "path", path },
                { "listMs", Environment.TickCount - start },
                { "count", entries.Count },
                { "entries", entries.Take(MaxListed).Select(e => Entry(e)).ToList() },
            };
        }

        private object Stat(DebugRequest request)
        {
            string path = request.Arg(0, "path", true);
            FileEntry entry = Files.Stat(path);
            if (entry == null)
            {
                throw new DebugException("Nothing at " + path + ".");
            }
            return Entry(entry);
        }

        private object Read(DebugRequest request)
        {
            string path = request.Arg(0, "path", true);
            int count = Math.Max(1, Math.Min(MaxRead, request.Int(1, 64)));
            long offset = request.Int(2, 0);
            using (KernelFile file = Files.OpenRead(path))
            using (NativeBuffer buffer = new NativeBuffer(count))
            {
                int start = Environment.TickCount;
                int read = file.Read(buffer, count, offset);
                return new JsonObject
                {
                    { "path", path },
                    { "offset", offset },
                    { "read", read },
                    { "readMs", Environment.TickCount - start },
                    { "hex", BitConverter.ToString(buffer.Bytes, 0, read).Replace("-", "") },
                    { "ascii", Ascii(buffer.Bytes, read) },
                };
            }
        }

        private Gamer SignedIn()
        {
            Gamer gamer = context.Profiles.SignedIn().FirstOrDefault();
            if (gamer == null)
            {
                throw new DebugException("Nobody is signed in.");
            }
            return gamer;
        }

        private static JsonObject Entry(FileEntry e)
        {
            return new JsonObject { { "name", e.Name }, { "directory", e.IsDirectory }, { "size", e.Size }, { "modified", e.Modified } };
        }

        private static JsonObject Png(byte[] png)
        {
            bool valid = png != null && png.Length > 8 && png[0] == 0x89 && png[1] == (byte)'P' && png[2] == (byte)'N' && png[3] == (byte)'G';
            return new JsonObject { { "bytes", png != null ? png.Length : 0 }, { "png", valid } };
        }

        private static string Ascii(byte[] bytes, int count)
        {
            StringBuilder s = new StringBuilder(count);
            for (int i = 0; i < count; i++)
            {
                byte b = bytes[i];
                s.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
            }
            return s.ToString();
        }
    }
}
