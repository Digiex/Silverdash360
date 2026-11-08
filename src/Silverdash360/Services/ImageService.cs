using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Silverdash360.Core;
using Silverdash360.Model;
using Silverdash360.Native;
using Silverdash360.Services.Files;
using Silverlight360;

namespace Silverdash360.Services
{
    /// <summary>
    /// Art for titles and profiles: box art and backdrops from the Xbox
    /// Marketplace (<see cref="MarketplaceArt"/>), the thumbnails in package
    /// headers, the game tiles a profile keeps for the titles it played, and
    /// gamer pictures. Art is read when a tile asks for it, which with the
    /// grids' virtualizing is the tiles on screen. Images are read in the
    /// background and decoded on the UI thread when the user is quiet
    /// (<see cref="UiIdle"/>), newest first, and the most recently used are
    /// kept, up to <see cref="MaxImages"/> of them and <see cref="MaxPixels"/>
    /// pixels in all.
    /// </summary>
    public sealed class ImageService
    {
        // XamReadTile tile types.
        public const uint TileAchievement = 0;
        public const uint TileGameIcon = 1;
        public const uint TileGamerPicture = 2;

        // The image ID of a title's own image in its achievement data.
        public const ulong TitleImageId = 0x8000;

        /// <summary>Decoded images kept: a few screens of tiles, in the console's small memory.</summary>
        public const int MaxImages = 120;

        /// <summary>
        /// Decoded pixels kept, at four bytes each: about 60 box fronts, or
        /// fewer with a backdrop or two.
        /// </summary>
        public const int MaxPixels = 4 * 1024 * 1024;

        // Requests waiting for the worker. Scrolling fast through thousands
        // of titles asks for far more art than can be read; the newest
        // requests (the tiles on screen now) go first and the oldest are
        // dropped.
        private const int MaxQueued = 40;

        private sealed class Request
        {
            public string Key;
            public Func<byte[]> Read;
        }

        // A worker reading requests, newest first. The art on the console's
        // drives and the Marketplace's have one each, so a download waiting
        // for an answer, as one does with no internet until it times out,
        // doesn't hold up the pictures that are there already.
        private sealed class Reader
        {
            public readonly object QueueLock = new object();
            public readonly LinkedList<Request> Queue = new LinkedList<Request>();
            public readonly AutoResetEvent Queued = new AutoResetEvent(false);
            public string Name;
            public Thread Worker;
            public volatile bool Reading;

            public bool Busy
            {
                get
                {
                    lock (QueueLock)
                    {
                        return Reading || Queue.Count > 0;
                    }
                }
            }
        }

        private readonly Reader local = new Reader { Name = "Images" };
        private readonly Reader online = new Reader { Name = "Marketplace images" };

        private readonly IContentSource source;
        private readonly Dictionary<string, ImageSource> cache = new Dictionary<string, ImageSource>();
        private readonly Dictionary<string, int> pixels = new Dictionary<string, int>();
        private int keptPixels;
        private readonly LinkedList<string> recent = new LinkedList<string>();
        private readonly Dictionary<string, List<Action<ImageSource>>> pending = new Dictionary<string, List<Action<ImageSource>>>();

        // Images read and waiting to be decoded, on the UI thread.
        private readonly LinkedList<KeyValuePair<string, byte[]>> decodes = new LinkedList<KeyValuePair<string, byte[]>>();
        private bool decoding;
        private int firstWaiting;

        // Images are shown a batch at a time: a frame that changes the screen
        // costs far more to draw than the decode, so while the reader has more
        // coming, read images wait for this many more, or this long.
        private const int BatchSize = 12;
        private const int BatchWaitMs = 300;

        public ImageService(IContentSource source)
        {
            this.source = source;
        }

        /// <summary>Art the user gave titles kept as folders, which comes first; set by the app.</summary>
        public CustomArt Custom { get; set; }

        /// <summary>Images asked for and not shown yet; on the UI thread.</summary>
        public int Pending { get { return pending.Count; } }

        /// <summary>The title's art; the callback isn't called when there is none.</summary>
        public void TitleArt(TitleEntry title, uint userIndex, Action<ImageSource> done)
        {
            Get("title:" + title.Key, () => TitlePng(title, userIndex), done);
        }

        /// <summary>
        /// The title's box front, the user's or the Marketplace's; the
        /// callback isn't called when there's none.
        /// </summary>
        public void BoxArt(TitleEntry title, Action<ImageSource> done)
        {
            ArtImage("boxart:", title, CustomArt.BoxArt, MarketplaceArt.BoxArt, done);
        }

        /// <summary>
        /// The title's backdrop, the user's or the Marketplace's; the
        /// callback isn't called when there's none.
        /// </summary>
        public void Backdrop(TitleEntry title, Action<ImageSource> done)
        {
            ArtImage("backdrop:", title, CustomArt.Background, MarketplaceArt.Background, done);
        }

        // A title with an art folder of its own is kept by its key, as
        // homebrew often shares a title ID; others by the ID their art is under.
        private void ArtImage(string kind, TitleEntry title, string customFile, string marketplaceFile,
                              Action<ImageSource> done)
        {
            uint artId = title.ArtTitleId;
            if (Custom != null && CustomArt.FolderOf(title) != null)
            {
                Get(kind + title.Key, () => Custom.Image(title, customFile) ?? MarketplaceArt.Image(artId, marketplaceFile),
                    done, online);
                return;
            }
            Get(kind + artId.ToString("X8"), () => MarketplaceArt.Image(artId, marketplaceFile), done, online);
        }

        /// <summary>A package's own thumbnail, such as an add-on's.</summary>
        public void ContentArt(ContentItem item, Action<ImageSource> done)
        {
            Get("content:" + item.TitleId.ToString("X8") + "/" + item.FileName, () => PackagePng(item, false), done);
        }

        /// <summary>The thumbnail in a package file's header, for the file browser.</summary>
        public void PackageThumbnail(XboxFileSystem files, string path, Action<ImageSource> done)
        {
            Get("file:" + path, () => PackageHeader.ReadThumbnail(files, path), done);
        }

        public void GamerPicture(Gamer gamer, Action<ImageSource> done)
        {
            Get("gamer:" + gamer.XuidText, () => ReadTile(TileGamerPicture, 0, 0, gamer.UserIndex), done);
        }

        /// <summary>
        /// An achievement's icon, from the profile's data for the title. The
        /// profile has the icons of unlocked achievements.
        /// </summary>
        public void AchievementArt(uint titleId, Achievement achievement, uint userIndex, Action<ImageSource> done)
        {
            Get(string.Format("achievement:{0:X8}/{1}", titleId, achievement.ImageId),
                () => ReadTile(TileAchievement, titleId, achievement.ImageId, userIndex), done);
        }

        private byte[] TitlePng(TitleEntry title, uint userIndex)
        {
            // The icon the user gave it, then the package's.
            byte[] png = Custom != null ? Custom.Image(title, CustomArt.Icon) : null;
            if (png != null)
            {
                return png;
            }
            ContentItem launch = title.LaunchItem;
            png = launch != null ? PackagePng(launch, true) : null;
            if (png != null)
            {
                return png;
            }
            // The tile the profile has for a played game, then another
            // package's art. Saves rarely have art worth a read.
            png = title.Played != null ? ReadTile(TileGameIcon, title.TitleId, TitleImageId, userIndex) : null;
            if (png != null)
            {
                return png;
            }
            ContentItem other = title.Content.FirstOrDefault(c => c != launch && c.Type != ContentType.SavedGame);
            return other != null ? PackagePng(other, true) : null;
        }

        // A package's thumbnail, preferring the title's or its own.
        private byte[] PackagePng(ContentItem item, bool titleFirst)
        {
            ContentMetadata metadata = source.ReadMetadata(item, true);
            if (metadata == null)
            {
                return null;
            }
            return titleFirst ? metadata.TitleThumbnailPng ?? metadata.ThumbnailPng
                              : metadata.ThumbnailPng ?? metadata.TitleThumbnailPng;
        }

        /// <summary>A tile's PNG from XamReadTile, or null.</summary>
        public static byte[] ReadTile(uint type, uint titleId, ulong imageId, uint userIndex)
        {
            using (NativeBuffer size = new NativeBuffer(4))
            {
                // A null buffer asks for the size.
                Xam.XamReadTile(type, titleId, imageId, userIndex, 0, size.Address, 0);
                uint length = size.U32(0);
                if (length == 0 || length > 0x100000)
                {
                    return null;
                }
                using (NativeBuffer png = new NativeBuffer((int)length))
                {
                    size.SetU32(0, length);
                    if (Xam.XamReadTile(type, titleId, imageId, userIndex, png.Address, size.Address, 0) != Xam.ErrorSuccess)
                    {
                        return null;
                    }
                    return png.Slice(0, (int)Math.Min(length, size.U32(0)));
                }
            }
        }

        private void Get(string key, Func<byte[]> read, Action<ImageSource> done)
        {
            Get(key, read, done, local);
        }

        private void Get(string key, Func<byte[]> read, Action<ImageSource> done, Reader reader)
        {
            ImageSource image;
            if (cache.TryGetValue(key, out image))
            {
                recent.Remove(key);
                recent.AddLast(key);
                if (image != null)
                {
                    done(image);
                }
                return;
            }
            List<Action<ImageSource>> waiting;
            if (pending.TryGetValue(key, out waiting))
            {
                waiting.Add(done);
                return;
            }
            pending.Add(key, new List<Action<ImageSource>> { done });
            Enqueue(new Request { Key = key, Read = read }, reader);
        }

        // On the UI thread, when the worker has read a PNG: it's decoded when
        // the user is quiet. Past MaxQueued the oldest are dropped, as reads
        // are, to be asked for again if their tiles come back.
        private void Arrived(string key, byte[] png)
        {
            if (!pending.ContainsKey(key))
            {
                return;
            }
            if (decodes.Count == 0)
            {
                firstWaiting = Environment.TickCount;
            }
            decodes.AddLast(new KeyValuePair<string, byte[]>(key, png));
            while (decodes.Count > MaxQueued)
            {
                pending.Remove(decodes.First.Value.Key);
                decodes.RemoveFirst();
            }
            if (!decoding)
            {
                decoding = true;
                UiIdle.Run(DecodeNext, UiIdle.QuietMs);
            }
        }

        // A batch of the images read, newest first, as they're likely on
        // screen.
        private bool DecodeNext()
        {
            if (decodes.Count == 0)
            {
                decoding = false;
                return false;
            }
            if (ReaderBusy && decodes.Count < BatchSize && Environment.TickCount - firstWaiting < BatchWaitMs)
            {
                return true;
            }
            while (decodes.Count > 0 && UiIdle.HasTime)
            {
                KeyValuePair<string, byte[]> next = decodes.Last.Value;
                decodes.RemoveLast();
                Finish(next.Key, next.Value);
            }
            firstWaiting = Environment.TickCount;
            decoding = decodes.Count > 0;
            return decoding;
        }

        private bool ReaderBusy
        {
            get { return local.Busy || online.Busy; }
        }

        private void Finish(string key, byte[] png)
        {
            List<Action<ImageSource>> callbacks;
            if (!pending.TryGetValue(key, out callbacks))
            {
                return;
            }
            pending.Remove(key);
            ImageSource image = Decode(key, png);
            Keep(key, image);
            if (image != null)
            {
                foreach (Action<ImageSource> callback in callbacks)
                {
                    callback(image);
                }
            }
        }

        private void Enqueue(Request request, Reader reader)
        {
            List<string> dropped = new List<string>();
            lock (reader.QueueLock)
            {
                reader.Queue.AddLast(request);
                while (reader.Queue.Count > MaxQueued)
                {
                    dropped.Add(reader.Queue.First.Value.Key);
                    reader.Queue.RemoveFirst();
                }
                if (reader.Worker == null)
                {
                    reader.Worker = new Thread(() => Work(reader)) { IsBackground = true, Name = reader.Name };
                    reader.Worker.Start();
                }
            }
            // Asked for again, a dropped image is read then.
            foreach (string key in dropped)
            {
                pending.Remove(key);
            }
            reader.Queued.Set();
        }

        // A reader's worker, newest request first.
        private void Work(Reader reader)
        {
            while (true)
            {
                Request request = null;
                lock (reader.QueueLock)
                {
                    if (reader.Queue.Count > 0)
                    {
                        request = reader.Queue.Last.Value;
                        reader.Queue.RemoveLast();
                    }
                    reader.Reading = request != null;
                }
                if (request == null)
                {
                    Activity.Images = "idle";
                    reader.Queued.WaitOne();
                    continue;
                }
                byte[] png = null;
                Activity.Images = request.Key;
                try
                {
                    png = request.Read();
                }
                catch (Exception e)
                {
                    Log.Write("Couldn't read art for {0}: {1}", request.Key, e.Message);
                }
                Activity.Images = request.Key + " (done)";
                Background.OnUi(() => Arrived(request.Key, png));
            }
        }

        // Icons up to this size are checked for having anything to show.
        private const int CheckedPixels = 128 * 128;

        // A PNG or a JPEG; null for an icon with nothing to show, such as
        // the fully transparent thumbnail some packages carry.
        private static ImageSource Decode(string key, byte[] png)
        {
            if (png == null || png.Length < 8)
            {
                return null;
            }
            try
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.SetSource(new MemoryStream(png));
                if (bitmap.PixelWidth * bitmap.PixelHeight <= CheckedPixels && IsBlank(bitmap))
                {
                    return null;
                }
                return bitmap;
            }
            catch (Exception e)
            {
                Log.Write("Couldn't decode the image for {0}: {1}", key, e.Message);
                return null;
            }
        }

        // No pixel that isn't fully transparent. Every pixel is looked at, so
        // an icon with only a little drawn on it isn't taken for blank.
        private static bool IsBlank(BitmapSource bitmap)
        {
            int[] pixels = new WriteableBitmap(bitmap).Pixels;
            for (int i = 0; i < pixels.Length; i++)
            {
                if ((pixels[i] & unchecked((int)0xFF000000)) != 0)
                {
                    return false;
                }
            }
            return true;
        }

        // Keeps an image, dropping the least recently used past the limits.
        private void Keep(string key, ImageSource image)
        {
            Forget(key);
            BitmapSource bitmap = image as BitmapSource;
            int size = bitmap != null ? bitmap.PixelWidth * bitmap.PixelHeight : 0;
            cache[key] = image;
            pixels[key] = size;
            keptPixels += size;
            recent.AddLast(key);
            while (recent.Count > 1 && (recent.Count > MaxImages || keptPixels > MaxPixels))
            {
                Forget(recent.First.Value);
            }
        }

        private void Forget(string key)
        {
            int size;
            if (pixels.TryGetValue(key, out size))
            {
                keptPixels -= size;
                pixels.Remove(key);
            }
            cache.Remove(key);
            recent.Remove(key);
        }

        /// <summary>Forgets everything, for when the library is read again.</summary>
        public void Clear()
        {
            cache.Clear();
            pixels.Clear();
            keptPixels = 0;
            recent.Clear();
        }
    }
}
