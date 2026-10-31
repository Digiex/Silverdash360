using System;
using System.Collections.Generic;
using System.Linq;
using Silverdash360.Core;
using Silverdash360.Model;
using Silverdash360.Native;
using Silverdash360.Services.Files;

namespace Silverdash360.Services.Media
{
    /// <summary>
    /// Videos downloaded from Xbox Live: movies, TV episodes, trailers and a
    /// game's videos, which the console keeps as packages under the title they
    /// came with. Playing one mounts its package, as the console's dashboard
    /// does, and serves the video inside to the player.
    /// </summary>
    public sealed class DownloadedVideoSource : IMediaSource
    {
        private const string MountName = "sdvideo";
        private const string DriveName = "VideoPackage";
        private const uint OpenExisting = 3;

        private readonly Library library;
        private readonly XboxFileSystem files;
        private readonly LocalMediaServer server;
        private readonly object mountLock = new object();
        private string mounted;

        public DownloadedVideoSource(Library library, XboxFileSystem files, LocalMediaServer server)
        {
            this.library = library;
            this.files = files;
            this.server = server;
            files.Drives.Add(new Drive(DriveName, @"\??\" + MountName + ":", true) { Listed = false });
        }

        public string Key { get { return "downloads"; } }
        public string Name { get { return "Downloaded videos"; } }
        public string Description { get { return "Movies, TV and videos from Xbox Live"; } }

        /// <summary>The video packages, read from the library.</summary>
        public List<ContentItem> Packages()
        {
            return library.AllContent.Where(c => ContentType.IsVideo(c.Type) && !c.IsPartial && !c.IsCorrupt).ToList();
        }

        public List<MediaEntry> List(MediaEntry folder)
        {
            if (folder != null)
            {
                return new List<MediaEntry>();
            }
            return Packages()
                .Select(c => new MediaEntry
                {
                    Source = this,
                    Id = LibraryCache.KeyOf(c),
                    Name = c.DisplayName.Length > 0 ? c.DisplayName : c.FileName,
                    Kind = MediaKind.Video,
                    Size = c.BestSize,
                    Album = c.Metadata != null && c.Metadata.TitleName.Length > 0 ? c.Metadata.TitleName : c.TitleName,
                    Package = c,
                })
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Mounts the package, closing the one mounted before, and gives the
        /// first video in it through the dashboard's media server.
        /// </summary>
        public Uri Locate(MediaEntry entry)
        {
            ContentItem package = Packages().FirstOrDefault(c => LibraryCache.KeyOf(c) == entry.Id);
            if (package == null)
            {
                throw new InvalidOperationException(entry.Name + " isn't on the console any more.");
            }
            lock (mountLock)
            {
                if (mounted != entry.Id)
                {
                    if (mounted != null)
                    {
                        Xam.XamContentClose(MountName, 0);
                        mounted = null;
                    }
                    using (NativeBuffer record = new NativeBuffer(package.Record))
                    {
                        uint result = Xam.XamContentCreateInternal(MountName, record.Address, OpenExisting, 0, 0, 0, 0, 0);
                        if (result != Xam.ErrorSuccess)
                        {
                            throw new InvalidOperationException("The video's package didn't open: " + result);
                        }
                    }
                    mounted = entry.Id;
                }
                string video = FindVideo("/" + DriveName, 2);
                if (video == null)
                {
                    Log.Write("No video in {0}", package);
                    throw new InvalidOperationException("No video the console plays in " + entry.Name + ".");
                }
                return server.UrlFor(video);
            }
        }

        // The first file the player takes, in the folder or one below it.
        private string FindVideo(string folder, int depth)
        {
            List<FileEntry> entries = files.List(folder);
            foreach (FileEntry file in entries.Where(e => !e.IsDirectory))
            {
                if (MediaFormats.OfFile(file.Name) == MediaKind.Video)
                {
                    return XboxFileSystem.Combine(folder, file.Name);
                }
            }
            if (depth > 1)
            {
                foreach (FileEntry child in entries.Where(e => e.IsDirectory))
                {
                    string video = FindVideo(XboxFileSystem.Combine(folder, child.Name), depth - 1);
                    if (video != null)
                    {
                        return video;
                    }
                }
            }
            return null;
        }
    }
}
