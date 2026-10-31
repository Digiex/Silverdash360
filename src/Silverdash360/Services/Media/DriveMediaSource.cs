using System;
using System.Collections.Generic;
using System.Linq;
using Silverdash360.Services.Files;

namespace Silverdash360.Services.Media
{
    /// <summary>The media on one of the console's drives, browsed by folder.</summary>
    public sealed class DriveMediaSource : IMediaSource
    {
        private readonly XboxFileSystem files;
        private readonly Drive drive;
        private readonly LocalMediaServer server;

        public DriveMediaSource(XboxFileSystem files, Drive drive, LocalMediaServer server)
        {
            this.files = files;
            this.drive = drive;
            this.server = server;
        }

        public string Key { get { return "drive:" + drive.Name; } }

        public string Name
        {
            get { return NameOf(drive.Name); }
        }

        /// <summary>What a drive of the file tree is called, such as "Hard drive" for Hdd1.</summary>
        public static string NameOf(string drive)
        {
            switch (drive)
            {
                case "Hdd1": return "Hard drive";
                case "Dvd": return "Disc";
                default:
                    // Usb0 is the first USB drive: USB storage 1.
                    int number;
                    if (drive.StartsWith("Usb") && int.TryParse(drive.Substring(3), out number))
                    {
                        return "USB storage " + (number + 1);
                    }
                    return drive.StartsWith("Mu") ? "Memory unit" : drive;
            }
        }

        public string Description { get { return "/" + drive.Name; } }

        public List<MediaEntry> List(MediaEntry folder)
        {
            string path = folder != null ? folder.Id : "/" + drive.Name;
            List<MediaEntry> entries = new List<MediaEntry>();
            foreach (FileEntry entry in files.List(path))
            {
                string child = XboxFileSystem.Combine(path, entry.Name);
                if (entry.IsDirectory)
                {
                    entries.Add(new MediaEntry { Source = this, Id = child, Name = entry.Name, Kind = MediaKind.Folder });
                    continue;
                }
                MediaKind? kind = MediaFormats.OfFile(entry.Name);
                if (kind.HasValue)
                {
                    entries.Add(new MediaEntry { Source = this, Id = child, Name = entry.Name, Kind = kind.Value, Size = entry.Size });
                }
            }
            // Folders first, each group by name; a key sort, not a comparison lambda.
            return entries.OrderBy(e => e.IsFolder ? 0 : 1).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The file through the dashboard's own media server.</summary>
        public Uri Locate(MediaEntry entry)
        {
            return server.UrlFor(entry.Id);
        }
    }
}
