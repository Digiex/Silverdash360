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
            commands.Add("xam.played", "", "The signed-in profile's played titles.", r => Played());
            commands.Add("xam.achievements", "<titleId>", "The signed-in profile's achievements for a title.", Achievements);
            commands.Add("net.address", "", "The console's address from XNetGetTitleXnAddr.", r => Address());
            commands.Add("fs.drives", "", "The drives the dashboard knows and which are present.", r => Drives());
            commands.Add("fs.list", "<path>", "A folder's entries, such as /Hdd1/Content.", List);
            commands.Add("fs.stat", "<path>", "A file or folder's size, kind and time.", Stat);
            commands.Add("fs.read", "<path> [bytes] [offset]", "Bytes of a file, in hex (at most " + MaxRead + ").", Read);
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
            List<ContentItem> items = new XamContentSource().Enumerate(xuid, null, null);
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
