using System;
using System.Threading;
using Silverdash360.Core;
using Silverdash360.Model;
using Silverdash360.Native;
using Silverdash360.Services.Files;
using Silverdash360.Services.Media;

namespace Silverdash360.Services
{
    /// <summary>
    /// The disc drive: what's in it, followed by asking the console for the
    /// tray's state every couple of seconds on a thread of its own and
    /// looking at the disc's files once the tray has closed on one, and
    /// opening and closing the tray.
    /// </summary>
    public sealed class DiscService
    {
        // XamLoaderGetDvdTrayState as a console reports it (not as xenia has
        // it): 0 closed with the disc read (MediaService.TrayReady), 2 open;
        // while it closes it reports 1, then 4 as it reads the disc.
        private const uint TrayOpen = 2;
        private const int PollMs = 2000;
        private const string DiscExecutable = "/Dvd/default.xex";
        private const string DiscDevice = @"\Device\Cdrom0";

        private readonly XboxFileSystem files;
        private readonly MediaService media;
        private uint tray = uint.MaxValue;

        public DiscService(XboxFileSystem files, MediaService media)
        {
            this.files = files;
            this.media = media;
        }

        /// <summary>What's in the drive, None while the tray is open or empty.</summary>
        public DiscKind Kind { get; private set; }

        /// <summary>A game disc's title ID, from its executable; 0 otherwise.</summary>
        public uint TitleId { get; private set; }

        public bool IsTrayOpen { get { return tray == TrayOpen; } }

        /// <summary>What's in the drive changed; on the UI thread.</summary>
        public event EventHandler Changed;

        public void Start()
        {
            new Thread(Poll) { IsBackground = true, Name = "Disc drive" }.Start();
        }

        /// <summary>Opens the tray, or closes it when it's open; on the UI thread.</summary>
        public void ToggleTray()
        {
            bool open = !IsTrayOpen;
            Log.Write("{0} the disc tray", open ? "Opening" : "Closing");
            Kernel.HalOpenCloseODDTray(open ? 1u : 0u);
            // The disc goes from the lists at once, not at the next look,
            // so it isn't started as it leaves.
            if (open && Kind != DiscKind.None)
            {
                Kind = DiscKind.None;
                TitleId = 0;
                if (Changed != null)
                {
                    Changed(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>Whether the tray is closed on a disc that's been read, so it can be started.</summary>
        public bool IsReady
        {
            get { return Xam.XamLoaderGetDvdTrayState() == MediaService.TrayReady; }
        }

        /// <summary>
        /// The disc as a title for the lists: a game disc started from its
        /// executable, or another disc, which the home page opens itself.
        /// Null with no disc in.
        /// </summary>
        public TitleEntry AsTitle(Library library)
        {
            if (Kind == DiscKind.None)
            {
                return null;
            }
            TitleEntry disc = new TitleEntry(TitleId, "disc") { IsDisc = true };
            TitleEntry known = TitleId != 0 ? library.Find(TitleId) : null;
            if (known != null)
            {
                disc.Played = known.Played;
            }
            if (Kind == DiscKind.Game)
            {
                disc.Executables.Add(new ExecutableItem
                {
                    Path = DiscExecutable,
                    DevicePath = DiscDevice + @"\default.xex",
                    DeviceFolder = DiscDevice,
                    Name = "Game disc",
                    TitleId = TitleId,
                });
            }
            disc.Name = known != null ? known.Name : Name;
            disc.Kind = TitleKind.Game;
            return disc;
        }

        // From the Marketplace's details for a game the library doesn't know.
        private string Name { get; set; }

        private void Poll()
        {
            while (true)
            {
                try
                {
                    uint state = Xam.XamLoaderGetDvdTrayState();
                    if (state != tray)
                    {
                        tray = state;
                        Look();
                    }
                }
                catch (Exception e)
                {
                    Log.Write("Couldn't look at the disc drive: {0}", e.Message);
                }
                Thread.Sleep(PollMs);
            }
        }

        // What the disc is, once the tray has closed on it.
        private void Look()
        {
            DiscKind kind = tray == MediaService.TrayReady ? media.Disc() : DiscKind.None;
            uint titleId = 0;
            string name = null;
            if (kind == DiscKind.Game)
            {
                using (KernelFile executable = files.OpenRead(DiscExecutable))
                {
                    titleId = ExecutableScanner.TitleIdOf(executable);
                }
                TitleDetails details = MarketplaceArt.ReadDetails(titleId);
                name = details != null && details.Name.Length > 0 ? details.Name : null;
            }
            string fallback = kind == DiscKind.Game ? "Game disc"
                : kind == DiscKind.OriginalXboxGame ? "Original Xbox disc"
                : kind == DiscKind.Video ? "DVD" : "Disc";
            Background.OnUi(delegate
            {
                bool changed = kind != Kind || titleId != TitleId;
                Kind = kind;
                TitleId = titleId;
                Name = name ?? fallback;
                if (changed)
                {
                    Log.Write("Disc: {0}{1}", kind, titleId != 0 ? " " + titleId.ToString("X8") : "");
                    if (Changed != null)
                    {
                        Changed(this, EventArgs.Empty);
                    }
                }
            });
        }
    }
}
