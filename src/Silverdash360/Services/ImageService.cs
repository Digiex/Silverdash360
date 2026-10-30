using System;
using System.Collections.Generic;
using System.IO;
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
    /// PNGs are read in the background, decoded on the UI thread and kept.
    /// </summary>
    public sealed class ImageService
    {
        // XamReadTile tile types.
        private const uint TileGameIcon = 1;
        private const uint TileGamerPicture = 2;

        private readonly Dictionary<string, ImageSource> cache = new Dictionary<string, ImageSource>();
        private readonly Dictionary<string, List<Action<ImageSource>>> pending = new Dictionary<string, List<Action<ImageSource>>>();

        /// <summary>The title's art; the callback isn't called when there is none.</summary>
        public void TitleArt(TitleEntry title, uint userIndex, Action<ImageSource> done)
        {
            Get("title:" + title.TitleIdText, () => TitlePng(title, userIndex), done);
        }

        /// <summary>A package's own thumbnail, such as an add-on's.</summary>
        public void ContentArt(ContentItem item, Action<ImageSource> done)
        {
            Get("content:" + item.TitleId.ToString("X8") + "/" + item.FileName,
                () => item.Metadata != null ? item.Metadata.ThumbnailPng ?? item.Metadata.TitleThumbnailPng : null, done);
        }

        public void GamerPicture(Gamer gamer, Action<ImageSource> done)
        {
            Get("gamer:" + gamer.XuidText, () => ReadTile(TileGamerPicture, 0, gamer.UserIndex), done);
        }

        private static byte[] TitlePng(TitleEntry title, uint userIndex)
        {
            ContentItem launch = title.LaunchItem;
            if (launch != null && launch.Metadata != null)
            {
                byte[] png = launch.Metadata.TitleThumbnailPng ?? launch.Metadata.ThumbnailPng;
                if (png != null)
                {
                    return png;
                }
            }
            // The tile the profile has for a played game, then any package's.
            byte[] tile = title.Played != null ? ReadTile(TileGameIcon, title.TitleId, userIndex) : null;
            if (tile != null)
            {
                return tile;
            }
            foreach (ContentItem item in title.Content)
            {
                if (item.Metadata != null && (item.Metadata.TitleThumbnailPng ?? item.Metadata.ThumbnailPng) != null)
                {
                    return item.Metadata.TitleThumbnailPng ?? item.Metadata.ThumbnailPng;
                }
            }
            return null;
        }

        private static byte[] ReadTile(uint type, uint titleId, uint userIndex)
        {
            using (NativeBuffer size = new NativeBuffer(4))
            {
                // A null buffer asks for the size.
                Xam.XamReadTile(type, titleId, 0, userIndex, 0, size.Address, 0);
                uint length = size.U32(0);
                if (length == 0 || length > 0x100000)
                {
                    return null;
                }
                using (NativeBuffer png = new NativeBuffer((int)length))
                {
                    size.SetU32(0, length);
                    if (Xam.XamReadTile(type, titleId, 0, userIndex, png.Address, size.Address, 0) != Xam.ErrorSuccess)
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
                cache[key] = image;
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

        /// <summary>Forgets everything, for when the library is read again.</summary>
        public void Clear()
        {
            cache.Clear();
        }
    }
}
