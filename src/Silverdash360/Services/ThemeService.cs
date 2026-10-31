using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media.Imaging;
using Silverdash360.Controls;
using Silverdash360.Core;
using Silverdash360.Model;
using Silverdash360.Native;
using Silverdash360.Services.Files;

namespace Silverdash360.Services
{
    /// <summary>
    /// A theme package as the console's dashboard installs it (content type
    /// 0x30000), read for what Silverdash can show of it: the wallpapers.
    /// </summary>
    public sealed class DashTheme
    {
        public DashTheme(ContentItem package)
        {
            Package = package;
            Wallpapers = new List<byte[]>();
        }

        public ContentItem Package { get; private set; }
        public string Name { get { return Package.DisplayName; } }

        /// <summary>
        /// The wallpapers, as stored (PNG or JPEG). The first dashboard's
        /// themes have one per blade; later ones keep four too, beside the
        /// layered art of the avatar scene, which Silverdash doesn't use.
        /// </summary>
        public List<byte[]> Wallpapers { get; private set; }

        /// <summary>
        /// For a theme from before the 2008 dashboard, the blade style it
        /// picks (DashStyle); -1 otherwise.
        /// </summary>
        public int BladeStyle { get; set; }

        /// <summary>
        /// For a later theme, the colour of the avatar scene it picks
        /// (SphereColor in parameters.ini); -1 otherwise.
        /// </summary>
        public int SphereColor { get; set; }

        /// <summary>Made for the 2008 or a later dashboard: it has a parameters.ini.</summary>
        public bool IsSceneTheme { get { return SphereColor >= 0; } }
    }

    /// <summary>
    /// Picks and loads the dashboard's theme. By default it's the one the
    /// signed-in profile chose in the console's dashboard; settings can pick
    /// another installed theme, or none. The pick is kept in the title's
    /// storage. Loading a theme sets <see cref="Theme"/>'s colours from its
    /// first wallpaper; the hub shows the wallpapers.
    /// </summary>
    public sealed class ThemeService
    {
        /// <summary>Follow the console dashboard's theme for the signed-in profile.</summary>
        public const string FollowDashboard = "dashboard";

        /// <summary>Silverdash's own colours and no wallpaper.</summary>
        public const string NoTheme = "silverdash";

        private const string PackagePrefix = "package:";
        private const string SettingFile = "theme.txt";

        // Where the console's dashboard keeps the profile's theme: a file in
        // the profile's package, 0x144 bytes of XCONTENT_DATA_INTERNAL with
        // the package's file name at 0x108 and its title at 0x140. A name
        // starting dashpack:// is one of the dashboard's built-in themes.
        private const string ProfileMount = "sdprof";
        private const string ThemeChoiceFile = "ThematicSkin";
        private const int ThemeChoiceSize = 0x144;
        private const string BuiltInPrefix = "dashpack://";
        private const uint DashboardTitleId = 0xFFFE07D1;

        private const string ThemeMount = "sdtheme";
        private const uint OpenExisting = 3;
        private const int WallpaperCount = 4;
        private const int MaxFileSize = 4 * 1024 * 1024;
        private const int ReadBlock = 0x10000;

        // Decoded wallpapers kept: the one shown and the one before, as a
        // decoded wallpaper is several megabytes.
        private const int MaxDecoded = 2;

        private readonly Library library;
        // Most recently used last. A list of a class rather than collections
        // keyed by int: a generic over a value type is compiled for it on the
        // console's runtime, and LinkedList<int> there broke the next pages
        // opened.
        private readonly List<DecodedWallpaper> decoded = new List<DecodedWallpaper>();
        private readonly List<Action> waiting = new List<Action>();
        private string setting;
        private string loadedKey;
        private ulong loadedForXuid;
        private bool loaded;
        private bool loading;
        private bool loadAgain;

        public ThemeService(Library library)
        {
            this.library = library;
            library.Changed += delegate { Load(false); };
        }

        /// <summary>The loaded theme, or null for Silverdash's own look.</summary>
        public DashTheme Current { get; private set; }

        /// <summary>Counts the themes shown, so a page can tell one changed while it was away.</summary>
        public int Version { get; private set; }

        /// <summary>A theme is being read.</summary>
        public bool IsLoading { get { return loading; } }

        /// <summary>What's picked: <see cref="FollowDashboard"/>, <see cref="NoTheme"/> or a package.</summary>
        public string Setting { get { return setting ?? FollowDashboard; } }

        /// <summary>
        /// When following the console's dashboard: its theme was one of its
        /// own built-in ones, which Silverdash can't show.
        /// </summary>
        public bool DashboardThemeIsBuiltIn { get; private set; }

        /// <summary>A theme was loaded or let go, on the UI thread.</summary>
        public event EventHandler Changed;

        /// <summary>The installed themes, by name.</summary>
        public List<ContentItem> Installed()
        {
            Dictionary<string, ContentItem> themes = new Dictionary<string, ContentItem>();
            foreach (ContentItem item in library.AllContent)
            {
                if (item.Type == ContentType.Theme && !item.IsPartial && !item.IsCorrupt)
                {
                    themes[LibraryCache.KeyOf(item)] = item;
                }
            }
            return themes.Values.OrderBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string SettingFor(ContentItem package)
        {
            return PackagePrefix + LibraryCache.KeyOf(package);
        }

        /// <summary>
        /// Picks a theme and loads it; the pick is saved in the background.
        /// done, when given, is called on the UI thread once it's loaded.
        /// </summary>
        public void Choose(string choice, Action done)
        {
            setting = choice;
            string text = choice;
            Background.Run(() => { TitleStorage.WriteText(SettingFile, text); return 0; }, (r, e) => { });
            if (done != null)
            {
                waiting.Add(done);
            }
            Load(true);
        }

        /// <summary>
        /// Loads the theme that's picked, in the background, unless it's the
        /// one loaded. The library calls it when it changes, as the profile
        /// signed in decides the console dashboard's theme.
        /// </summary>
        public void Load(bool force)
        {
            if (!library.IsLoaded)
            {
                Done();
                return;
            }
            // While a start shows the saved library a section at a time, a
            // theme package can be missing only for now.
            bool partial = !library.IsComplete;
            if (loading)
            {
                loadAgain |= force;
                return;
            }
            // The console's dashboard keeps a theme per profile, so another
            // profile can mean another theme.
            Gamer gamer = library.Gamer;
            ulong xuid = gamer != null ? gamer.Xuid : 0;
            if (!force && loaded && xuid == loadedForXuid)
            {
                return;
            }
            loading = true;
            List<ContentItem> installed = Installed();
            string current = force ? null : loadedKey;
            Background.Run(() => Read(gamer, installed, current), delegate(LoadResult result, Exception error)
            {
                loading = false;
                loaded = !partial || (result != null && result.Theme != null);
                loadedForXuid = xuid;
                if (result != null && !result.Unchanged)
                {
                    DashboardThemeIsBuiltIn = result.BuiltIn;
                    loadedKey = result.Theme != null ? LibraryCache.KeyOf(result.Theme.Package) : null;
                    Show(result.Theme);
                }
                if (loadAgain)
                {
                    loadAgain = false;
                    Load(true);
                    return;
                }
                Done();
            });
        }

        // Tells the callers of Choose that their theme is in.
        private void Done()
        {
            List<Action> callbacks = new List<Action>(waiting);
            waiting.Clear();
            foreach (Action callback in callbacks)
            {
                callback();
            }
        }

        /// <summary>A wallpaper decoded, keeping the latest few; null when there's none or it doesn't decode.</summary>
        public BitmapImage Wallpaper(int index)
        {
            DashTheme theme = Current;
            if (theme == null || theme.Wallpapers.Count == 0)
            {
                return null;
            }
            index %= theme.Wallpapers.Count;
            DecodedWallpaper wallpaper = FindDecoded(index);
            if (wallpaper == null)
            {
                wallpaper = new DecodedWallpaper { Index = index, Image = Decode(theme.Wallpapers[index]) };
            }
            decoded.Remove(wallpaper);
            decoded.Add(wallpaper);
            while (decoded.Count > MaxDecoded)
            {
                decoded.RemoveAt(0);
            }
            return wallpaper.Image;
        }

        private sealed class DecodedWallpaper
        {
            public int Index;
            public BitmapImage Image;
        }

        private DecodedWallpaper FindDecoded(int index)
        {
            foreach (DecodedWallpaper wallpaper in decoded)
            {
                if (wallpaper.Index == index)
                {
                    return wallpaper;
                }
            }
            return null;
        }

        /// <summary>Whether <see cref="Wallpaper"/> has the wallpaper decoded already.</summary>
        public bool IsDecoded(int index)
        {
            DashTheme theme = Current;
            return theme != null && theme.Wallpapers.Count > 0 && FindDecoded(index % theme.Wallpapers.Count) != null;
        }

        /// <summary>What the theme is, for settings.</summary>
        public string Describe()
        {
            string name = Current != null ? Current.Name : "Silverdash";
            if (Setting == FollowDashboard)
            {
                if (Current == null && DashboardThemeIsBuiltIn)
                {
                    return "Silverdash (the Xbox Dashboard's theme is a built-in one)";
                }
                return name + " (from the Xbox Dashboard)";
            }
            return name;
        }

        private sealed class LoadResult
        {
            public DashTheme Theme;
            public bool BuiltIn;
            public bool Unchanged;
        }

        // On a background thread: which package is picked, and its art
        // unless it's the one loaded.
        private LoadResult Read(Gamer gamer, List<ContentItem> installed, string loadedPackage)
        {
            if (setting == null)
            {
                string saved = TitleStorage.ReadText(SettingFile);
                setting = string.IsNullOrEmpty(saved) ? FollowDashboard : saved.Trim();
            }
            LoadResult result = new LoadResult();
            ContentItem package = null;
            if (Setting == FollowDashboard)
            {
                bool builtIn;
                package = DashboardChoice(gamer, installed, out builtIn);
                result.BuiltIn = builtIn;
            }
            else if (Setting.StartsWith(PackagePrefix))
            {
                string wanted = Setting.Substring(PackagePrefix.Length);
                package = installed.FirstOrDefault(t => LibraryCache.KeyOf(t) == wanted);
                if (package == null)
                {
                    Log.Write("Theme {0} isn't installed any more", wanted);
                }
            }
            string key = package != null ? LibraryCache.KeyOf(package) : null;
            if (key == loadedPackage && loadedPackage != null)
            {
                result.Unchanged = true;
            }
            else if (package != null)
            {
                result.Theme = ReadTheme(package);
            }
            return result;
        }

        // The installed theme the console's dashboard shows for the profile.
        private static ContentItem DashboardChoice(Gamer gamer, List<ContentItem> installed, out bool builtIn)
        {
            builtIn = false;
            if (gamer == null)
            {
                return null;
            }
            byte[] choice;
            uint result = Xam.XamProfileOpen(gamer.OfflineXuid, ProfileMount, 0, 0);
            if (result != Xam.ErrorSuccess)
            {
                Log.Write("Couldn't open the profile of {0} for its theme: {1}", gamer.Gamertag, result);
                return null;
            }
            try
            {
                choice = ReadFile(@"\??\" + ProfileMount + @":\" + ThemeChoiceFile, ThemeChoiceSize);
            }
            finally
            {
                Xam.XamProfileClose(ProfileMount);
            }
            if (choice == null || choice.Length < ThemeChoiceSize)
            {
                Log.Write("{0} has no theme picked in the Xbox Dashboard", gamer.Gamertag);
                return null;
            }
            string fileName = BigEndian.Ansi(choice, 0x108, 42);
            uint titleId = BigEndian.U32(choice, 0x140);
            if (titleId == 0)
            {
                titleId = DashboardTitleId;
            }
            if (fileName.StartsWith(BuiltInPrefix, StringComparison.OrdinalIgnoreCase))
            {
                Log.Write("The Xbox Dashboard's theme for {0} is its built-in {1}", gamer.Gamertag, fileName);
                builtIn = true;
                return null;
            }
            ContentItem package = installed.FirstOrDefault(
                t => t.TitleId == titleId && string.Equals(t.FileName, fileName, StringComparison.OrdinalIgnoreCase));
            Log.Write("The Xbox Dashboard's theme for {0} is {1:X8}/{2}{3}", gamer.Gamertag, titleId, fileName,
                      package != null ? "" : ", which isn't installed");
            return package;
        }

        // Mounts the package and reads what Silverdash shows of it.
        private static DashTheme ReadTheme(ContentItem package)
        {
            DashTheme theme = new DashTheme(package) { BladeStyle = -1, SphereColor = -1 };
            using (NativeBuffer record = new NativeBuffer(package.Record))
            {
                uint result = Xam.XamContentCreateInternal(ThemeMount, record.Address, OpenExisting, 0, 0, 0, 0, 0);
                if (result != Xam.ErrorSuccess)
                {
                    Log.Write("Couldn't open the theme {0}: {1}", package, result);
                    return null;
                }
                try
                {
                    string root = @"\??\" + ThemeMount + @":\";
                    // The names' case varies between themes; the console's
                    // names are case-insensitive.
                    for (int i = 1; i <= WallpaperCount; i++)
                    {
                        byte[] wallpaper = ReadFile(root + "Wallpaper" + i, MaxFileSize);
                        if (wallpaper != null && wallpaper.Length > 8)
                        {
                            theme.Wallpapers.Add(wallpaper);
                        }
                    }
                    byte[] style = ReadFile(root + "DashStyle", 4);
                    byte[] parameters = ReadFile(root + "parameters.ini", 0x1000);
                    if (parameters != null)
                    {
                        theme.SphereColor = SphereColorOf(Encoding.UTF8.GetString(parameters, 0, parameters.Length));
                    }
                    else if (style != null && style.Length == 4)
                    {
                        theme.BladeStyle = (int)BigEndian.U32(style, 0);
                    }
                }
                finally
                {
                    Xam.XamContentClose(ThemeMount, 0);
                }
            }
            Log.Write("Theme {0}: {1} wallpapers, {2}", theme.Name, theme.Wallpapers.Count,
                      theme.IsSceneTheme ? "scene colour " + theme.SphereColor : "blade style " + theme.BladeStyle);
            return theme;
        }

        private static int SphereColorOf(string ini)
        {
            foreach (string line in ini.Split('\n'))
            {
                string[] pair = line.Split('=');
                int value;
                if (pair.Length == 2 && pair[0].Trim().Equals("SphereColor", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(pair[1].Trim(), out value))
                {
                    return value;
                }
            }
            // A parameters.ini without one still makes it a later theme.
            return 0;
        }

        // A file's bytes, up to a limit; null when it isn't there.
        private static byte[] ReadFile(string device, int limit)
        {
            KernelFile file;
            if (KernelFile.TryOpen(device, Kernel.GenericRead | Kernel.Synchronize, Kernel.FileShareRead,
                                   Kernel.FileOpen, Kernel.FileNonDirectoryFile, out file) != Kernel.StatusSuccess)
            {
                return null;
            }
            using (file)
            using (NativeBuffer block = new NativeBuffer(Math.Min(limit, ReadBlock)))
            {
                MemoryStream bytes = new MemoryStream();
                int got;
                while (bytes.Length < limit && (got = file.Read(block, (int)Math.Min(block.Length, limit - bytes.Length), bytes.Length)) > 0)
                {
                    bytes.Write(block.Bytes, 0, got);
                }
                return bytes.ToArray();
            }
        }

        // On the UI thread: the theme's colours, from its first wallpaper.
        private void Show(DashTheme theme)
        {
            Current = theme;
            Version++;
            decoded.Clear();
            Theme.Palette palette = null;
            BitmapImage first = Wallpaper(0);
            if (first != null)
            {
                palette = Theme.PaletteFrom(first);
            }
            Theme.Apply(palette ?? Theme.DefaultPalette(), palette == null);
            Log.Write("Showing theme {0}", theme != null ? theme.Name : "Silverdash");
            if (Changed != null)
            {
                Changed(this, EventArgs.Empty);
            }
        }

        private static BitmapImage Decode(byte[] bytes)
        {
            try
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.SetSource(new MemoryStream(bytes));
                return bitmap;
            }
            catch (Exception e)
            {
                Log.Write("Couldn't decode a wallpaper: {0}", e.Message);
                return null;
            }
        }
    }
}
