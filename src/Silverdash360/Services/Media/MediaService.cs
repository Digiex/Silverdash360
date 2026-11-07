using System;
using System.Collections.Generic;
using System.Linq;
using Silverdash360.Core;
using Silverdash360.Native;
using Silverdash360.Services.Files;
using Silverlight360;

namespace Silverdash360.Services.Media
{
    public enum DiscKind
    {
        None,
        /// <summary>A DVD movie, which the console's DVD player plays.</summary>
        Video,
        Game,
        OriginalXboxGame,
        /// <summary>A disc of files, browsed like a drive.</summary>
        Data,
    }

    /// <summary>
    /// The media the console can reach: its drives, the media servers on the
    /// network and the disc in the drive, and what's playing.
    /// </summary>
    public sealed class MediaService
    {
        private const int SearchTime = 3000;
        // XamLoaderLaunchTitleEx flag: start whatever disc is in the tray, as
        // the console's dashboard does for a movie or an audio CD.
        private const uint LaunchDisc = 2;

        private readonly XboxFileSystem files;
        private List<DlnaMediaSource> servers = new List<DlnaMediaSource>();

        public MediaService(XboxFileSystem files)
        {
            this.files = files;
            LocalServer = new LocalMediaServer(files);
        }

        /// <summary>Serves files on the drives to the player.</summary>
        public LocalMediaServer LocalServer { get; private set; }

        /// <summary>The servers found changed, or a search started or ended; on the UI thread.</summary>
        public event EventHandler ServersChanged;

        public bool IsSearching { get; private set; }
        public DateTime? LastSearch { get; private set; }

        public IList<DlnaMediaSource> Servers { get { return servers; } }

        /// <summary>Videos downloaded from Xbox Live; set by the app once the library exists.</summary>
        public DownloadedVideoSource Downloads { get; set; }

        /// <summary>The drives present, each a source; the disc only when it holds files.</summary>
        public List<IMediaSource> Drives()
        {
            DiscKind disc = Disc();
            return files.AvailableDrives
                .Where(d => !d.System && (d.Name != "Dvd" || disc == DiscKind.Data))
                .Select(d => (IMediaSource)new DriveMediaSource(files, d, LocalServer))
                .ToList();
        }

        /// <summary>A source by its <see cref="IMediaSource.Key"/>, or null.</summary>
        public IMediaSource Find(string key)
        {
            IMediaSource drive = files.Drives.Select(d => (IMediaSource)new DriveMediaSource(files, d, LocalServer))
                .FirstOrDefault(s => s.Key == key);
            if (drive != null)
            {
                return drive;
            }
            if (Downloads != null && Downloads.Key == key)
            {
                return Downloads;
            }
            return servers.FirstOrDefault(s => s.Key == key);
        }

        /// <summary>Looks for media servers in the background, unless a search is running.</summary>
        public void Search()
        {
            if (IsSearching)
            {
                return;
            }
            IsSearching = true;
            Changed();
            Background.Run(() =>
            {
                List<DlnaMediaSource> found = new List<DlnaMediaSource>();
                foreach (Uri location in Ssdp.Search(SearchTime))
                {
                    try
                    {
                        DlnaMediaSource server = DlnaMediaSource.Describe(location);
                        if (server != null && !found.Any(s => s.Udn == server.Udn))
                        {
                            server.Relay = LocalServer;
                            found.Add(server);
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Write("Media server at {0} unreadable: {1}", location, e.Message);
                    }
                }
                Log.Write("Media servers: {0} found", found.Count);
                return found.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }, delegate(List<DlnaMediaSource> found, Exception error)
            {
                IsSearching = false;
                LastSearch = DateTime.Now;
                if (found != null)
                {
                    // Servers added by hand stay unless the search found them too.
                    servers = found.Concat(servers.Where(s => !found.Any(f => f.Udn == s.Udn)))
                                   .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
                }
                Changed();
            });
        }

        /// <summary>
        /// Adds a media server by the URL of its description, for a network
        /// that doesn't pass the multicast search (some routers and firewalls
        /// don't). Blocks; returns the server.
        /// </summary>
        public DlnaMediaSource AddServer(Uri location)
        {
            DlnaMediaSource server = DlnaMediaSource.Describe(location);
            if (server == null)
            {
                throw new System.IO.IOException(location + " isn't a media server.");
            }
            server.Relay = LocalServer;
            Background.OnUi(() =>
            {
                servers = servers.Where(s => s.Udn != server.Udn).Concat(new[] { server })
                                 .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
                Changed();
            });
            return server;
        }

        private void Changed()
        {
            if (ServersChanged != null)
            {
                ServersChanged(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// XamLoaderGetDvdTrayState when the tray is closed and the disc read
        /// (as a console reports it; xenia has other values).
        /// </summary>
        public const uint TrayReady = 0;

        /// <summary>
        /// What's in the disc drive, from the files on it; None unless the
        /// tray is closed on a disc that's been read. A read while the tray
        /// moves fails, and the console then says the disc is unreadable and
        /// ends the title.
        /// </summary>
        public DiscKind Disc()
        {
            if (Xam.XamLoaderGetDvdTrayState() != TrayReady)
            {
                return DiscKind.None;
            }
            try
            {
                if (files.Stat("/Dvd/video_ts") != null)
                {
                    return DiscKind.Video;
                }
                if (files.Stat("/Dvd/default.xex") != null)
                {
                    return DiscKind.Game;
                }
                if (files.Stat("/Dvd/default.xbe") != null)
                {
                    return DiscKind.OriginalXboxGame;
                }
                return files.AvailableDrives.Any(d => d.Name == "Dvd") ? DiscKind.Data : DiscKind.None;
            }
            catch (Exception)
            {
                return DiscKind.None;
            }
        }

        /// <summary>
        /// Hands the disc to the console: a game starts, and a movie (or an
        /// audio CD, which shows no files) plays in the console's own player.
        /// Returns only when that failed.
        /// </summary>
        public string PlayDisc()
        {
            DiscKind disc = Disc();
            Log.Write("Playing the disc ({0})", disc);
            if (disc == DiscKind.Game)
            {
                Xam.XamLoaderLaunchTitleEx(@"\Device\Cdrom0\default.xex", @"\Device\Cdrom0", null, 0);
            }
            else
            {
                Xam.XamLoaderLaunchTitleEx(null, null, null, LaunchDisc);
            }
            return "The console didn't start the disc.";
        }

        /// <summary>
        /// Points the player at the console's own address in place of the
        /// loopback one, for when the loopback address doesn't reach the
        /// dashboard's media server; false when that's done already.
        /// </summary>
        public bool UseConsoleAddress()
        {
            if (LocalServer.Host != "127.0.0.1")
            {
                return false;
            }
            using (NativeBuffer address = new NativeBuffer(0x24))
            {
                Net.XNetGetTitleXnAddr(Net.CallerTitle, address.Address);
                uint ip = address.U32(0);
                if (ip == 0)
                {
                    return false;
                }
                LocalServer.Host = Ftp.NativeSocket.FormatAddress(ip);
            }
            Log.Write("Media server: the player uses {0}", LocalServer.Host);
            return true;
        }

        /// <summary>What's playing or shown: entries of one kind, and which.</summary>
        public List<MediaEntry> Queue { get; private set; }
        public int Position { get; set; }

        public MediaEntry Current
        {
            get { return Queue != null && Position >= 0 && Position < Queue.Count ? Queue[Position] : null; }
        }

        /// <summary>Plays or shows an entry, with the others of its kind in the folder after it.</summary>
        public void Start(IList<MediaEntry> folder, MediaEntry entry)
        {
            Queue = folder.Where(e => e.Kind == entry.Kind).ToList();
            Position = Math.Max(0, Queue.IndexOf(entry));
        }
    }
}
