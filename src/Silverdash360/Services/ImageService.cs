using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Silverdash360.Core;
using Silverdash360.Model;
using Silverdash360.Native;

namespace Silverdash360.Services
{
    /// <summary>
    /// Art for titles and profiles: the thumbnails in package headers, the
    /// game tiles a profile keeps for the titles it played, and gamer pictures.
    /// Art is read when a tile asks for it, which with the grids' virtualizing
    /// is the tiles on screen. PNGs are read in the background, decoded on the
    /// UI thread, and the most recently used <see cref="MaxImages"/> are kept.
    /// </summary>
    public sealed class ImageService
    {
        // XamReadTile tile types.
        private const uint TileAchievement = 0;
        private const uint TileGameIcon = 1;
        private const uint TileGamerPicture = 2;

        // The image ID of a title's own image in its achievement data.
        private const ulong TitleImageId = 0x8000;

        public const int MaxImages = 300;

        private readonly IContentSource source;
        private readonly Dictionary<string, ImageSource> cache = new Dictionary<string, ImageSource>();
        private readonly LinkedList<string> recent = new LinkedList<string>();
        private readonly Dictionary<string, List<Action<ImageSource>>> pending = new Dictionary<string, List<Action<ImageSource>>>();

        public ImageService(IContentSource source)
        {
            this.source = source;
        }

        /// <summary>The title's art; the callback isn't called when there is none.</summary>
        public void TitleArt(TitleEntry title, uint userIndex, Action<ImageSource> done)
        {
            Get("title:" + title.TitleIdText, () => TitlePng(title, userIndex), done);
        }

        /// <summary>A package's own thumbnail, such as an add-on's.</summary>
        public void ContentArt(ContentItem item, Action<ImageSource> done)
        {
            Get("content:" + item.TitleId.ToString("X8") + "/" + item.FileName, () => PackagePng(item, false), done);
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
            ContentItem launch = title.LaunchItem;
            byte[] png = launch != null ? PackagePng(launch, true) : null;
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

        private static byte[] ReadTile(uint type, uint titleId, ulong imageId, uint userIndex)
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
            Background.Run(read, delegate(byte[] png, Exception error)
            {
                image = Decode(png);
                Keep(key, image);
                List<Action<ImageSource>> callbacks = pending[key];
                pending.Remove(key);
                if (image != null)
                {
                    foreach (Action<ImageSource> callback in callbacks)
                    {
                        callback(image);
                    }
                }
            });
        }

        private static ImageSource Decode(byte[] png)
        {
            if (png == null || png.Length < 8)
            {
                return null;
            }
            try
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.SetSource(new MemoryStream(png));
                return bitmap;
            }
            catch (Exception e)
            {
                Log.Write("Couldn't decode an image: {0}", e.Message);
                return null;
            }
        }

        // Keeps an image, dropping the least recently used past the limit.
        private void Keep(string key, ImageSource image)
        {
            cache[key] = image;
            recent.Remove(key);
            recent.AddLast(key);
            while (recent.Count > MaxImages)
            {
                cache.Remove(recent.First.Value);
                recent.RemoveFirst();
            }
        }

        /// <summary>Forgets everything, for when the library is read again.</summary>
        public void Clear()
        {
            cache.Clear();
            recent.Clear();
        }
    }
}
