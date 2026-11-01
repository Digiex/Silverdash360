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
    /// 0x30000), or one of the dashboard's built-in themes, read for what
    /// Silverdash can show of it: the wallpapers.
    /// </summary>
    public sealed class DashTheme
    {
        public DashTheme(ContentItem package)
        {
            Package = package;
            BuiltIn = -1;
            Wallpapers = new List<byte[]>();
        }

        /// <summary>The dashboard's built-in theme with this number.</summary>
        public DashTheme(int builtIn)
        {
            BuiltIn = builtIn;
            Wallpapers = new List<byte[]>();
        }

        /// <summary>The theme package, or null for a built-in theme.</summary>
        public ContentItem Package { get; private set; }

        /// <summary>The number of a built-in theme (its ThemeN folder), or -1.</summary>
        public int BuiltIn { get; private set; }

        public string Name
        {
            get
            {
                return Package != null ? Package.DisplayName
                    : BuiltIn >= 0 ? ThemeService.BuiltInName(BuiltIn) : SavedName ?? ThemeService.CustomThemeName;
            }
        }

        /// <summary>The name kept with a theme read from the cache before its package was found.</summary>
        public string SavedName { get; set; }

        private string id;

        /// <summary>
        /// Which theme it is: package:TITLEID/filename, as the console's
        /// dashboard names a package, or builtin:N.
        /// </summary>
        public string Id
        {
            get
            {
                return id ?? (Package != null ? PackageId(Package.TitleId, Package.FileName) : ThemeService.SettingFor(BuiltIn));
            }
            set { id = value; }
        }

        public static string PackageId(uint titleId, string fileName)
        {
            return "package:" + titleId.ToString("X8") + "/" + fileName.ToLowerInvariant();
        }

        /// <summary>
        /// For each wallpaper that's raw pixels (<see cref="WallpaperImage"/>),
        /// its pixels made ready in the background; null otherwise.
        /// </summary>
        public WallpaperImage.Pixels[] RawPixels { get; set; }

        /// <summary>The colours worked out from the first wallpaper, once they are.</summary>
        public Theme.Palette Palette { get; set; }

        /// <summary>The colours were worked out, even if to none.</summary>
        public bool PaletteKnown { get; set; }

        /// <summary>The cache folder it's kept in.</summary>
        public string CacheFolder { get; set; }

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
    /// Colours without a wallpaper, after the styles of the first Xbox 360
    /// dashboard's blades: its default and Carbon. Its blades were light,
    /// and Silverdash's text white, so the light ones are deepened. The page
    /// background is the band behind its blades, dark or steel blue at the
    /// edges and lighter in the middle; the default's pale blue middle is
    /// toned down for the text. Each blade had a colour of its own, and so
    /// do the sections after home: apps the Marketplace blade's, me Xbox
    /// Live's, games, media and settings (System) their own.
    /// </summary>
    public sealed class ColourScheme
    {
        public string Id;
        public string Name;
        public Theme.Palette Palette;

        public static List<ColourScheme> All()
        {
            return new List<ColourScheme>
            {
                new ColourScheme
                {
                    Id = "xbox360",
                    Name = "Blades (Default)",
                    Palette = new Theme.Palette
                    {
                        Accent = Rgb(0xD4, 0xE8, 0xFF),
                        SubText = Rgb(0xC8, 0xC8, 0xC8),
                        Panel = Rgb(0x32, 0x3B, 0x45),
                        PanelHighlight = Rgb(0x43, 0x4F, 0x5C),
                        Background = new[] { Rgb(0x70, 0x84, 0x99), Rgb(0x9C, 0xB0, 0xC6), Rgb(0x70, 0x84, 0x99) },
                        BackgroundOffsets = new[] { 0.04, 0.47, 1.0 },
                        // The blades' colours, a little deeper for white text.
                        Sections = Blades(0.8,
                                          new[] { Rgb(0xEB, 0x41, 0x23), Rgb(0xEB, 0x73, 0x2C), Rgb(0xEB, 0x41, 0x23) },
                                          new[] { Rgb(0xFF, 0x6E, 0x00), Rgb(0xEB, 0xB9, 0x46), Rgb(0xFF, 0x66, 0x00) },
                                          new[] { Rgb(0x0D, 0xB4, 0x64), Rgb(0xA0, 0xDC, 0x2D), Rgb(0x14, 0xB4, 0x5A) },
                                          new[] { Rgb(0x23, 0x23, 0xBE), Rgb(0x82, 0xB4, 0xFF), Rgb(0x28, 0x3C, 0xBE) },
                                          new[] { Rgb(0x28, 0x1E, 0x82), Rgb(0xBE, 0x96, 0xE1), Rgb(0x28, 0x1E, 0x78) }),
                    },
                },
                new ColourScheme
                {
                    Id = "carbon",
                    Name = "Blades (Carbon)",
                    Palette = new Theme.Palette
                    {
                        Accent = Rgb(0xEB, 0xEB, 0xEB),
                        SubText = Rgb(0x96, 0x96, 0x96),
                        Panel = Rgb(0x32, 0x32, 0x32),
                        PanelHighlight = Rgb(0x50, 0x50, 0x50),
                        Background = new[] { Rgb(0x0F, 0x0F, 0x0F), Rgb(0x55, 0x55, 0x55), Rgb(0x0F, 0x0F, 0x0F) },
                        BackgroundOffsets = new[] { 0.04, 0.52, 1.0 },
                        Sections = Blades(1,
                                          new[] { Rgb(0x0F, 0x0F, 0x0F), Rgb(0x7E, 0x18, 0x0F), Rgb(0x0F, 0x0F, 0x0F) },
                                          new[] { Rgb(0x26, 0x0F, 0x0F), Rgb(0x7E, 0x64, 0x0F), Rgb(0x26, 0x0F, 0x0F) },
                                          new[] { Rgb(0x0F, 0x0F, 0x0F), Rgb(0x32, 0x5A, 0x0F), Rgb(0x0F, 0x0F, 0x0F) },
                                          new[] { Rgb(0x0F, 0x0F, 0x28), Rgb(0x0F, 0x50, 0x64), Rgb(0x0F, 0x0F, 0x28) },
                                          new[] { Rgb(0x0F, 0x0F, 0x0F), Rgb(0x4B, 0x3C, 0x64), Rgb(0x32, 0x32, 0x32) }),
                    },
                },
            };
        }

        // The sections' backgrounds from the blades' colours, left to right:
        // Marketplace, Xbox Live, Games, Media, System; each colour scaled.
        // The skins keep them Marketplace, then System back to Xbox Live;
        // Xbox Live orange, Games green and Media blue, as the dashboard
        // showed them.
        private static Dictionary<string, System.Windows.Media.Color[]> Blades(double scale,
            System.Windows.Media.Color[] marketplace, System.Windows.Media.Color[] live, System.Windows.Media.Color[] games,
            System.Windows.Media.Color[] media, System.Windows.Media.Color[] system)
        {
            return new Dictionary<string, System.Windows.Media.Color[]>
            {
                { "apps", Scale(marketplace, scale) },
                { "me", Scale(live, scale) },
                { "games", Scale(games, scale) },
                { "media", Scale(media, scale) },
                { "settings", Scale(system, scale) },
            };
        }

        private static System.Windows.Media.Color[] Scale(System.Windows.Media.Color[] colors, double scale)
        {
            System.Windows.Media.Color[] scaled = new System.Windows.Media.Color[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                scaled[i] = Rgb((byte)(colors[i].R * scale), (byte)(colors[i].G * scale), (byte)(colors[i].B * scale));
            }
            return scaled;
        }

        private static System.Windows.Media.Color Rgb(byte r, byte g, byte b)
        {
            return System.Windows.Media.Color.FromArgb(0xFF, r, g, b);
        }
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

        /// <summary>
        /// The signed-in profile's own theme from the theme editor, or the
        /// one saved for nobody signed in; the console dashboard's theme
        /// for one that has none.
        /// </summary>
        public const string CustomTheme = "custom";

        public const string CustomThemeName = "Custom theme";

        private const string PackagePrefix = "package:";
        private const string BuiltInPrefix = "builtin:";
        private const string SchemePrefix = "scheme:";
        private const string SettingFile = "theme.txt";

        // Where the console's dashboard keeps the profile's theme: a file in
        // the profile's package, 0x144 bytes of XCONTENT_DATA_INTERNAL with
        // the package's file name at 0x108 and its title at 0x140. A name
        // dashpack://ThemeN is the dashboard's built-in theme N.
        private const string ProfileMount = "sdprof";
        private const string ThemeChoiceFile = "ThematicSkin";
        private const int ThemeChoiceSize = 0x144;
        private const string DashPackPrefix = "dashpack://Theme";
        private const uint DashboardTitleId = 0xFFFE07D1;

        // The built-in themes are in the dashboard's DashPack, a package in
        // the system extension partition, each in a ThemeN folder laid out
        // as a theme package is. A console keeps it in the partition's
        // content folder, as content is kept on the hard drive (common
        // content of title FFFE07DF, type 0x8000); xenia's sysext folder
        // has it at its root.
        private static readonly string[] DashPackPaths =
        {
            @"\Device\Harddisk0\SystemExtPartition\Content\0000000000000000\FFFE07DF\00008000\FFFE07DF00000006",
            @"\Device\Harddisk0\SystemExtPartition\FFFE07DF00000006",
        };
        private const string PackMount = "sdpack";

        // The theme in use, kept per profile so a start doesn't mount its
        // package, read its wallpapers and work out its colours again:
        // CurrentTheme\<offline XUID>\theme.txt and Wallpaper1 to 4.
        private const string CacheRoot = "CurrentTheme/";
        // A custom theme, kept as the cache keeps one:
        // CustomTheme\<offline XUID or Nobody>\theme.txt and Wallpaper1 to 4.
        private const string CustomRoot = "CustomTheme/";
        // Themes saved from the editor for everyone on the console:
        // SavedThemes\<id>\, listed with their names in SavedThemes\index.txt.
        private const string SavedRoot = "SavedThemes/";
        private const string SavedIndex = "SavedThemes/index.txt";
        public const string SavedPrefix = "saved:";
        private const string CacheMeta = "theme.txt";
        private const uint OpenPackageFlags = 0x43;
        private const int MaxBuiltIn = 16;

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
        // The library changed while a theme was read: a package that was
        // missing may be in now.
        private bool checkAgain;
        // The built-in themes' numbers, read once; null until then.
        private volatile List<int> builtInThemes;

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
        /// own built-in ones, which couldn't be read.
        /// </summary>
        public bool DashboardThemeIsBuiltIn { get; private set; }

        /// <summary>
        /// When following the console's dashboard: the file name of the
        /// theme package it picks that isn't installed, such as a theme an
        /// older dashboard had; null otherwise. The dashboard shows its first
        /// theme then.
        /// </summary>
        public string DashboardThemeMissing { get; private set; }

        /// <summary>
        /// The dashboard's built-in themes by number; empty until
        /// <see cref="FindBuiltInThemes"/> has read them, or when there are none.
        /// </summary>
        public List<int> BuiltInThemes
        {
            get { return builtInThemes ?? new List<int>(); }
        }

        /// <summary>
        /// Reads which built-in themes the dashboard has, once, in the
        /// background; done is called on the UI thread when they're new.
        /// </summary>
        public void FindBuiltInThemes(Action done)
        {
            if (builtInThemes != null)
            {
                return;
            }
            Background.Run(() => FindBuiltIn(), delegate(List<int> found, Exception error)
            {
                builtInThemes = found ?? new List<int>();
                done();
            });
        }

        /// <summary>As <see cref="FindBuiltInThemes"/>, on this thread.</summary>
        public void FindBuiltInThemesNow()
        {
            if (builtInThemes == null)
            {
                builtInThemes = FindBuiltIn();
            }
        }

        /// <summary>A built-in theme's name, by its number.</summary>
        public static string BuiltInName(int number)
        {
            // The dashboard lists its own Xbox 360 theme first, which isn't
            // in the DashPack, then these.
            string[] names = { "Spectrum", "Day", "Night" };
            return number >= 0 && number < names.Length ? names[number] : "Xbox Dashboard theme " + (number + 1);
        }

        public static string SettingFor(int builtIn)
        {
            return BuiltInPrefix + builtIn;
        }

        public static string SettingFor(ColourScheme scheme)
        {
            return SchemePrefix + scheme.Id;
        }

        /// <summary>The colour scheme in use, or null.</summary>
        public ColourScheme CurrentScheme { get; private set; }

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
            Background.Run(() => { AppStorage.WriteText(SettingFile, text); return 0; }, (r, e) => { });
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
                checkAgain |= !force;
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
            // Showing a theme decodes a wallpaper and redraws everything, so it
            // waits until the user stops moving.
            Background.Run(() => Read(gamer, installed, current, partial), (result, error) => UiIdle.Post(delegate
            {
                loading = false;
                // A theme from the cache whose package isn't in yet is read
                // again once the library is complete.
                loaded = !partial || (result != null && result.Theme != null && !result.Unverified);
                loadedForXuid = xuid;
                if (result != null && !result.Unchanged)
                {
                    DashboardThemeIsBuiltIn = result.BuiltIn;
                    DashboardThemeMissing = result.Missing;
                    loadedKey = result.Unverified ? null : result.Theme != null ? result.Theme.Id
                        : result.Scheme != null ? SettingFor(result.Scheme) : null;
                    // The cache's theme again, now its package is found: it's
                    // on screen already.
                    bool shown = result.FromCache && result.Theme != null && Current != null &&
                                 Current.Id == result.Theme.Id;
                    if (!shown)
                    {
                        Show(result.Theme, result.Scheme);
                    }
                }
                if (loadAgain)
                {
                    loadAgain = false;
                    checkAgain = false;
                    Load(true);
                    return;
                }
                if (checkAgain)
                {
                    checkAgain = false;
                    Load(false);
                    if (loading)
                    {
                        return;
                    }
                }
                Done();
            }));
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
        public BitmapSource Wallpaper(int index)
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
                WallpaperImage.Pixels pixels = theme.RawPixels != null ? theme.RawPixels[index] : null;
                wallpaper = new DecodedWallpaper { Index = index, Image = WallpaperImage.Create(theme.Wallpapers[index], pixels) };
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
            public BitmapSource Image;
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
            string name = Current != null ? Current.Name : CurrentScheme != null ? CurrentScheme.Name : "Silverdash";
            if (Setting == FollowDashboard)
            {
                if (Current == null && DashboardThemeIsBuiltIn)
                {
                    return "Silverdash (the Xbox Dashboard's built-in theme couldn't be read)";
                }
                if (Current == null && DashboardThemeMissing != null)
                {
                    return "Silverdash (the Xbox Dashboard's theme " + DashboardThemeMissing + " isn't installed)";
                }
                return name + " (from the Xbox Dashboard)";
            }
            return name;
        }

        private sealed class LoadResult
        {
            public DashTheme Theme;
            public bool BuiltIn;
            public string Missing;
            public ColourScheme Scheme;
            public bool Unchanged;
            public bool FromCache;
            /// <summary>From the cache before its package was found.</summary>
            public bool Unverified;
        }

        // On a background thread: which package is picked, and its art
        // unless it's the one loaded.
        private LoadResult Read(Gamer gamer, List<ContentItem> installed, string loadedPackage, bool partial)
        {
            if (setting == null)
            {
                string saved = AppStorage.ReadText(SettingFile);
                setting = string.IsNullOrEmpty(saved) ? FollowDashboard : saved.Trim();
            }
            LoadResult result = new LoadResult();
            ContentItem package = null;
            int builtIn = -1;
            string id = null;
            bool follow = Setting == FollowDashboard;
            if (Setting == CustomTheme)
            {
                id = CustomId(gamer);
                if (id == loadedPackage && loadedPackage != null)
                {
                    result.Unchanged = true;
                    return result;
                }
                result.Theme = ReadCache(CustomRoot + Owner(gamer) + "/", id, null);
                if (result.Theme != null)
                {
                    result.FromCache = true;
                    PrepareRaw(result.Theme);
                    return result;
                }
                // Nothing saved for this profile: the dashboard's theme.
                follow = true;
                id = null;
            }
            if (follow)
            {
                package = DashboardChoice(gamer, installed, out builtIn, out result.Missing, out id);
            }
            else if (Setting.StartsWith(PackagePrefix))
            {
                package = FindPackage(installed, Setting, out id);
                if (package == null && !partial)
                {
                    Log.Write("Theme {0} isn't installed any more", Setting);
                }
            }
            else if (Setting.StartsWith(SavedPrefix))
            {
                id = Setting;
                if (id == loadedPackage && loadedPackage != null)
                {
                    result.Unchanged = true;
                    return result;
                }
                result.Theme = ReadCache(SavedRoot + Setting.Substring(SavedPrefix.Length) + "/", id, null);
                if (result.Theme != null)
                {
                    result.FromCache = true;
                    PrepareRaw(result.Theme);
                }
                else
                {
                    Log.Write("Saved theme {0} isn't there any more", Setting);
                }
                return result;
            }
            else if (Setting.StartsWith(BuiltInPrefix))
            {
                int.TryParse(Setting.Substring(BuiltInPrefix.Length), out builtIn);
                id = SettingFor(builtIn);
            }
            else if (Setting.StartsWith(SchemePrefix))
            {
                result.Scheme = ColourScheme.All().FirstOrDefault(s => SettingFor(s) == Setting);
                id = result.Scheme != null ? SettingFor(result.Scheme) : null;
            }
            if (id == loadedPackage && loadedPackage != null)
            {
                result.Unchanged = true;
                return result;
            }
            if (result.Scheme != null || id == null)
            {
                return result;
            }
            // The kept theme, when it's the one picked: a theme's files don't
            // change, so its name says enough. While a start hasn't found
            // every package, it's used before its package is found.
            bool readable = package != null || builtIn >= 0;
            string folder = CacheFolder(gamer);
            if (readable || partial)
            {
                result.Theme = ReadCache(folder, id, package);
            }
            if (result.Theme != null)
            {
                result.FromCache = true;
                result.Unverified = !readable;
                result.Missing = null;
            }
            else if (package != null)
            {
                result.Theme = ReadTheme(package);
            }
            else if (builtIn >= 0)
            {
                result.Theme = ReadBuiltIn(builtIn);
                result.BuiltIn = result.Theme == null;
            }
            if (result.Theme != null && !result.FromCache)
            {
                result.Theme.CacheFolder = folder;
                WriteCache(result.Theme);
            }
            if (result.Theme != null)
            {
                PrepareRaw(result.Theme);
            }
            return result;
        }

        // A picture theme the dashboard made keeps its wallpaper as raw
        // pixels: they're made ready here, in the background, so showing it
        // doesn't hold up the UI.
        private void PrepareRaw(DashTheme theme)
        {
            // The theme on screen read again, as once its package is found:
            // its pixels are ready already.
            DashTheme shown = Current;
            if (shown != null && shown.Id == theme.Id && shown.RawPixels != null &&
                shown.Wallpapers.Count == theme.Wallpapers.Count)
            {
                theme.RawPixels = shown.RawPixels;
                return;
            }
            WallpaperImage.Pixels[] pixels = new WallpaperImage.Pixels[theme.Wallpapers.Count];
            bool any = false;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (WallpaperImage.IsRaw(theme.Wallpapers[i]))
                {
                    int started = Environment.TickCount;
                    pixels[i] = WallpaperImage.ToPixels(theme.Wallpapers[i]);
                    any |= pixels[i] != null;
                    Log.Write("Theme {0}: wallpaper {1} is raw pixels, made ready in {2} ms", theme.Name, i + 1,
                              Environment.TickCount - started);
                }
            }
            theme.RawPixels = any ? pixels : null;
        }

        private static string CacheFolder(Gamer gamer)
        {
            return CacheRoot + Owner(gamer) + "/";
        }

        private static string Owner(Gamer gamer)
        {
            return gamer != null ? gamer.OfflineXuid.ToString("X16") : "Nobody";
        }

        private static string CustomId(Gamer gamer)
        {
            return CustomTheme + ":" + Owner(gamer);
        }

        // The installed package a package: setting picks, by its title and
        // file name: whose it is can read differently from when it was
        // picked (a profile's package was once taken for the console's).
        // id is the theme's, found or not, or null for a setting that
        // doesn't parse.
        private static ContentItem FindPackage(List<ContentItem> installed, string choice, out string id)
        {
            // owner/device/title/type/file
            string[] parts = choice.Substring(PackagePrefix.Length).Split('/');
            uint titleId;
            if (parts.Length < 5 || !uint.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out titleId))
            {
                id = null;
                return null;
            }
            string fileName = parts[4];
            id = DashTheme.PackageId(titleId, fileName);
            return installed.FirstOrDefault(t => t.TitleId == titleId &&
                                                 string.Equals(t.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>A theme saved from the editor for everyone on the console.</summary>
        public sealed class SavedTheme
        {
            public string Id;
            public string Name;

            public string Setting { get { return SavedPrefix + Id; } }
        }

        private List<SavedTheme> saved;

        /// <summary>The themes saved for everyone, by name. Reads a small file the first time.</summary>
        public List<SavedTheme> SavedThemes()
        {
            if (saved == null)
            {
                saved = new List<SavedTheme>();
                foreach (string line in (AppStorage.ReadText(SavedIndex) ?? "").Split('\n'))
                {
                    int tab = line.IndexOf('\t');
                    if (tab > 0)
                    {
                        saved.Add(new SavedTheme { Id = line.Substring(0, tab), Name = line.Substring(tab + 1).TrimEnd('\r') });
                    }
                }
            }
            return saved.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void WriteSavedIndex()
        {
            StringBuilder text = new StringBuilder();
            foreach (SavedTheme theme in saved)
            {
                text.Append(theme.Id).Append('\t').Append(theme.Name.Replace('\n', ' ')).Append('\n');
            }
            string contents = text.ToString();
            Background.Run(() => { AppStorage.WriteText(SavedIndex, contents); return 0; }, (r, e) => { });
        }

        /// <summary>
        /// Saves the editor's theme under a name for everyone on the
        /// console, and uses it; done is called on the UI thread once it's shown.
        /// </summary>
        public void SaveThemeAs(string name, Theme.Palette palette, List<byte[]> wallpapers, Action done)
        {
            SavedThemes();
            SavedTheme entry = new SavedTheme { Id = DateTime.UtcNow.Ticks.ToString("X"), Name = name };
            DashTheme theme = new DashTheme(-1)
            {
                Id = entry.Setting,
                SavedName = name,
                CacheFolder = SavedRoot + entry.Id + "/",
                Palette = palette,
                PaletteKnown = true,
                SphereColor = -1,
                BladeStyle = -1,
            };
            theme.Wallpapers.AddRange(wallpapers);
            Background.Run(() =>
            {
                WriteCache(theme);
                Log.Write("Theme {0} saved for everyone: {1} wallpapers", name, wallpapers.Count);
                return 0;
            }, delegate
            {
                saved.Add(entry);
                WriteSavedIndex();
                Choose(entry.Setting, done);
            });
        }

        /// <summary>
        /// Deletes a theme saved for everyone; one in use gives way to the
        /// console dashboard's theme. done is called on the UI thread.
        /// </summary>
        public void DeleteSavedTheme(SavedTheme theme, Action done)
        {
            SavedThemes();
            saved = saved.Where(s => s.Id != theme.Id).ToList();
            WriteSavedIndex();
            string folder = SavedRoot + theme.Id + "/";
            Background.Run(() =>
            {
                for (int i = 1; i <= MaxWallpaperSlots; i++)
                {
                    AppStorage.Delete(folder + "Wallpaper" + i);
                }
                AppStorage.Delete(folder + CacheMeta);
                Log.Write("Saved theme {0} deleted", theme.Name);
                return 0;
            }, delegate
            {
                if (Setting == theme.Setting)
                {
                    Choose(FollowDashboard, done);
                }
                else
                {
                    done();
                }
            });
        }

        // Wallpapers a theme keeps at most.
        private const int MaxWallpaperSlots = 4;

        /// <summary>Whether the signed-in profile, or nobody signed in, has a custom theme saved.</summary>
        public bool HasCustomTheme()
        {
            Gamer gamer = library.Gamer;
            return AppStorage.Stat(CustomRoot + Owner(gamer) + "/" + CacheMeta) != null;
        }

        /// <summary>
        /// Saves a custom theme for the profile signed in, or nobody signed
        /// in, and for nobody signed in too when asked, and uses it. done is
        /// called on the UI thread once it's shown.
        /// </summary>
        public void SaveCustomTheme(Theme.Palette palette, List<byte[]> wallpapers, bool forNobodyToo, Action done)
        {
            Gamer gamer = library.Gamer;
            List<Gamer> owners = new List<Gamer> { gamer };
            if (forNobodyToo && gamer != null)
            {
                owners.Add(null);
            }
            Background.Run(() =>
            {
                foreach (Gamer owner in owners)
                {
                    DashTheme theme = new DashTheme(-1)
                    {
                        Id = CustomId(owner),
                        SavedName = CustomThemeName,
                        CacheFolder = CustomRoot + Owner(owner) + "/",
                        Palette = palette,
                        PaletteKnown = true,
                        SphereColor = -1,
                        BladeStyle = -1,
                    };
                    theme.Wallpapers.AddRange(wallpapers);
                    WriteCache(theme);
                }
                Log.Write("Custom theme saved for {0}{1}: {2} wallpapers", gamer != null ? gamer.Gamertag : "nobody signed in",
                          owners.Count > 1 ? " and nobody signed in" : "", wallpapers.Count);
                return 0;
            }, (r, e) => Choose(CustomTheme, done));
        }

        /// <summary>
        /// Another theme's wallpapers, as a setting names it (a package or a
        /// built-in theme), read in the background; done is given them on
        /// the UI thread, empty when it can't be read.
        /// </summary>
        public void ReadWallpapers(string choice, Action<List<byte[]>> done)
        {
            List<ContentItem> installed = Installed();
            Background.Run(() =>
            {
                DashTheme theme = null;
                if (choice.StartsWith(BuiltInPrefix))
                {
                    int number;
                    if (int.TryParse(choice.Substring(BuiltInPrefix.Length), out number))
                    {
                        theme = ReadBuiltIn(number);
                    }
                }
                else if (choice.StartsWith(PackagePrefix))
                {
                    string id;
                    ContentItem package = FindPackage(installed, choice, out id);
                    if (package != null)
                    {
                        theme = ReadTheme(package);
                    }
                }
                else if (choice.StartsWith(SavedPrefix))
                {
                    theme = ReadCache(SavedRoot + choice.Substring(SavedPrefix.Length) + "/", choice, null);
                }
                return theme != null ? theme.Wallpapers : new List<byte[]>();
            }, (wallpapers, error) => done(wallpapers ?? new List<byte[]>()));
        }

        // The kept theme when it's id; null otherwise.
        private static DashTheme ReadCache(string folder, string id, ContentItem package)
        {
            Dictionary<string, string> meta = ParseMeta(AppStorage.ReadText(folder + CacheMeta));
            string value;
            if (!meta.TryGetValue("id", out value) || value != id)
            {
                return null;
            }
            int builtIn = MetaInt(meta, "builtIn", -1);
            DashTheme theme = package != null ? new DashTheme(package) : new DashTheme(builtIn);
            theme.Id = id;
            theme.CacheFolder = folder;
            theme.SphereColor = MetaInt(meta, "sphereColor", -1);
            theme.BladeStyle = MetaInt(meta, "bladeStyle", -1);
            if (meta.TryGetValue("name", out value))
            {
                theme.SavedName = value;
            }
            int count = MetaInt(meta, "wallpapers", 0);
            for (int i = 1; i <= count; i++)
            {
                byte[] wallpaper = AppStorage.ReadBytes(folder + "Wallpaper" + i);
                if (wallpaper == null)
                {
                    return null;
                }
                theme.Wallpapers.Add(wallpaper);
            }
            // "colours": "palette" was kept as none for wallpapers that didn't
            // decode then, raw ones among them, and is worked out again.
            if (meta.TryGetValue("colours", out value))
            {
                theme.PaletteKnown = true;
                theme.Palette = ParsePalette(value);
            }
            Log.Write("Theme {0} from the cache: {1} wallpapers{2}", theme.Name, count,
                      theme.PaletteKnown ? ", its colours" : "");
            return theme;
        }

        // Keeps the theme's wallpapers, then what says which theme they are,
        // so a cache cut short isn't taken for the theme.
        private static void WriteCache(DashTheme theme)
        {
            for (int i = 0; i < theme.Wallpapers.Count; i++)
            {
                AppStorage.WriteBytes(theme.CacheFolder + "Wallpaper" + (i + 1), theme.Wallpapers[i], null);
            }
            WriteMeta(theme);
        }

        private static void WriteMeta(DashTheme theme)
        {
            StringBuilder text = new StringBuilder();
            text.Append("id=").Append(theme.Id).Append('\n');
            text.Append("name=").Append(theme.Name).Append('\n');
            text.Append("builtIn=").Append(theme.BuiltIn).Append('\n');
            text.Append("sphereColor=").Append(theme.SphereColor).Append('\n');
            text.Append("bladeStyle=").Append(theme.BladeStyle).Append('\n');
            text.Append("wallpapers=").Append(theme.Wallpapers.Count).Append('\n');
            if (theme.PaletteKnown)
            {
                text.Append("colours=").Append(FormatPalette(theme.Palette)).Append('\n');
            }
            AppStorage.WriteText(theme.CacheFolder + CacheMeta, text.ToString());
        }

        private static Dictionary<string, string> ParseMeta(string text)
        {
            Dictionary<string, string> meta = new Dictionary<string, string>();
            foreach (string line in (text ?? "").Split('\n'))
            {
                int equals = line.IndexOf('=');
                if (equals > 0)
                {
                    meta[line.Substring(0, equals)] = line.Substring(equals + 1).TrimEnd('\r');
                }
            }
            return meta;
        }

        private static int MetaInt(Dictionary<string, string> meta, string key, int fallback)
        {
            string value;
            int number;
            return meta.TryGetValue(key, out value) && int.TryParse(value, out number) ? number : fallback;
        }

        // The colours as AARRGGBB words: accent, sub text, panel, panel
        // highlight and the background's; "none" for a wallpaper with too
        // little colour, which keeps Silverdash's.
        private static string FormatPalette(Theme.Palette palette)
        {
            if (palette == null)
            {
                return "none";
            }
            List<System.Windows.Media.Color> colors = new List<System.Windows.Media.Color>
            {
                palette.Accent, palette.SubText, palette.Panel, palette.PanelHighlight,
            };
            colors.AddRange(palette.Background);
            return string.Join(" ", colors.Select(c => string.Format("{0:X2}{1:X2}{2:X2}{3:X2}", c.A, c.R, c.G, c.B)).ToArray());
        }

        private static Theme.Palette ParsePalette(string text)
        {
            string[] words = text.Split(' ');
            if (words.Length < 5)
            {
                return null;
            }
            System.Windows.Media.Color[] colors = new System.Windows.Media.Color[words.Length];
            for (int i = 0; i < words.Length; i++)
            {
                uint argb;
                if (!uint.TryParse(words[i], System.Globalization.NumberStyles.HexNumber, null, out argb))
                {
                    return null;
                }
                colors[i] = System.Windows.Media.Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            }
            return new Theme.Palette
            {
                Accent = colors[0],
                SubText = colors[1],
                Panel = colors[2],
                PanelHighlight = colors[3],
                Background = colors.Skip(4).ToArray(),
            };
        }

        // Mounts the dashboard's DashPack, from wherever the console keeps it.
        private static bool OpenDashPack()
        {
            foreach (string path in DashPackPaths)
            {
                if (Xam.XamContentOpenFile(0xFF, PackMount, path, OpenPackageFlags, 0, 0, 0) == Xam.ErrorSuccess)
                {
                    return true;
                }
            }
            Log.Write("The Xbox Dashboard's DashPack, with its built-in themes, isn't there");
            return false;
        }

        // The built-in themes' numbers: the DashPack's ThemeN folders that
        // have a wallpaper, counting up from 0.
        private static List<int> FindBuiltIn()
        {
            List<int> found = new List<int>();
            if (!OpenDashPack())
            {
                return found;
            }
            try
            {
                for (int n = 0; n < MaxBuiltIn; n++)
                {
                    if (ReadFile(@"\??\" + PackMount + @":\Theme" + n + @"\Wallpaper1", 8) == null)
                    {
                        break;
                    }
                    found.Add(n);
                }
            }
            finally
            {
                Xam.XamContentClose(PackMount, 0);
            }
            Log.Write("The Xbox Dashboard has {0} built-in themes", found.Count);
            return found;
        }

        // Reads one of the dashboard's built-in themes from its DashPack.
        private static DashTheme ReadBuiltIn(int number)
        {
            if (!OpenDashPack())
            {
                return null;
            }
            DashTheme theme = new DashTheme(number) { BladeStyle = -1, SphereColor = -1 };
            try
            {
                ReadThemeFiles(theme, @"\??\" + PackMount + @":\Theme" + number + @"\");
            }
            finally
            {
                Xam.XamContentClose(PackMount, 0);
            }
            return theme.Wallpapers.Count > 0 ? theme : null;
        }

        // The installed theme the console's dashboard shows for the profile,
        // or builtIn the number of its built-in one.
        private static ContentItem DashboardChoice(Gamer gamer, List<ContentItem> installed, out int builtIn,
                                                   out string missing, out string id)
        {
            builtIn = -1;
            missing = null;
            id = null;
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
            if (fileName.StartsWith(DashPackPrefix, StringComparison.OrdinalIgnoreCase))
            {
                Log.Write("The Xbox Dashboard's theme for {0} is its built-in {1}", gamer.Gamertag, fileName);
                int number;
                if (int.TryParse(fileName.Substring(DashPackPrefix.Length), out number))
                {
                    builtIn = number;
                    id = SettingFor(number);
                }
                return null;
            }
            ContentItem package = installed.FirstOrDefault(
                t => t.TitleId == titleId && string.Equals(t.FileName, fileName, StringComparison.OrdinalIgnoreCase));
            Log.Write("The Xbox Dashboard's theme for {0} is {1:X8}/{2}{3}", gamer.Gamertag, titleId, fileName,
                      package != null ? "" : ", which isn't installed");
            if (package == null)
            {
                missing = fileName;
            }
            id = DashTheme.PackageId(titleId, fileName);
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
                    ReadThemeFiles(theme, @"\??\" + ThemeMount + @":\");
                }
                finally
                {
                    Xam.XamContentClose(ThemeMount, 0);
                }
            }
            return theme;
        }

        // A theme's wallpapers and style from the folder at root.
        private static void ReadThemeFiles(DashTheme theme, string root)
        {
            // The names' case varies between themes; the console's names are
            // case-insensitive.
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
            Log.Write("Theme {0}: {1} wallpapers, {2}", theme.Name, theme.Wallpapers.Count,
                      theme.IsSceneTheme ? "scene colour " + theme.SphereColor : "blade style " + theme.BladeStyle);
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

        // On the UI thread: the theme's colours, from its first wallpaper,
        // or a colour scheme's.
        private void Show(DashTheme theme, ColourScheme scheme)
        {
            Current = theme;
            CurrentScheme = theme == null ? scheme : null;
            Version++;
            decoded.Clear();
            Theme.Palette palette = CurrentScheme != null ? CurrentScheme.Palette : theme != null ? theme.Palette : null;
            // A theme's colours are worked out from its first wallpaper once,
            // then kept with it.
            if (theme != null && !theme.PaletteKnown)
            {
                BitmapSource first = Wallpaper(0);
                if (first != null)
                {
                    palette = Theme.PaletteFrom(first);
                }
                theme.Palette = palette;
                // A wallpaper that didn't decode says nothing of the colours:
                // they're worked out again next time.
                theme.PaletteKnown = first != null || theme.Wallpapers.Count == 0;
                if (theme.CacheFolder != null)
                {
                    Background.Run(() => { WriteMeta(theme); return 0; }, (r, e) => { });
                }
            }
            Theme.Apply(palette ?? Theme.DefaultPalette(), palette == null);
            Log.Write("Showing theme {0}", theme != null ? theme.Name : CurrentScheme != null ? CurrentScheme.Name : "Silverdash");
            if (Changed != null)
            {
                Changed(this, EventArgs.Empty);
            }
        }

    }
}
