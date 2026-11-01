using System;
using System.IO;
using System.Windows.Media.Imaging;
using Silverdash360.Core;
using Silverdash360.Native;

namespace Silverdash360.Services
{
    /// <summary>
    /// A theme's wallpaper as stored: a JPEG or PNG, or, in a theme the Xbox
    /// Dashboard makes from a picture, raw pixels: " RGB", the width and
    /// height as big-endian words, then three bytes a pixel, red, green and
    /// blue, a row at a time (800 by 600 from the dashboard, with a byte
    /// left over at the end). The platform decodes only the first kind, so
    /// raw pixels are turned into a WriteableBitmap's: in the background,
    /// as that's a loop over every pixel in app code, which the console
    /// interprets, then copied in on the UI thread.
    /// </summary>
    public static class WallpaperImage
    {
        private const int RawHeader = 12;
        private const int MaxRawSide = 4096;

        public static bool IsRaw(byte[] bytes)
        {
            return bytes != null && bytes.Length >= RawHeader && bytes[0] == 0x20 && bytes[1] == (byte)'R' &&
                   bytes[2] == (byte)'G' && bytes[3] == (byte)'B';
        }

        /// <summary>Raw pixels made ready to show.</summary>
        public sealed class Pixels
        {
            public int Width;
            public int Height;
            public int[] Argb;
        }

        /// <summary>
        /// A raw wallpaper's pixels as a WriteableBitmap takes them, on any
        /// thread; null for one that isn't raw or is cut short.
        /// </summary>
        public static Pixels ToPixels(byte[] bytes)
        {
            if (!IsRaw(bytes))
            {
                return null;
            }
            int width = (int)BigEndian.U32(bytes, 4);
            int height = (int)BigEndian.U32(bytes, 8);
            if (width <= 0 || height <= 0 || width > MaxRawSide || height > MaxRawSide ||
                bytes.Length < RawHeader + (long)width * height * 3)
            {
                Log.Write("A raw wallpaper of {0} by {1} in {2} bytes doesn't add up", width, height, bytes.Length);
                return null;
            }
            int count = width * height;
            int[] argb = new int[count];
            int at = RawHeader;
            for (int i = 0; i < count; i++)
            {
                argb[i] = unchecked((int)0xFF000000) | (bytes[at] << 16) | (bytes[at + 1] << 8) | bytes[at + 2];
                at += 3;
            }
            return new Pixels { Width = width, Height = height, Argb = argb };
        }

        /// <summary>
        /// The wallpaper as an image, on the UI thread: a raw one from its
        /// pixels (made here when not given), the others decoded; null when
        /// it can't be shown.
        /// </summary>
        public static BitmapSource Create(byte[] bytes, Pixels pixels)
        {
            try
            {
                if (IsRaw(bytes))
                {
                    pixels = pixels ?? ToPixels(bytes);
                    if (pixels == null)
                    {
                        return null;
                    }
                    WriteableBitmap bitmap = new WriteableBitmap(pixels.Width, pixels.Height);
                    Array.Copy(pixels.Argb, bitmap.Pixels, pixels.Argb.Length);
                    bitmap.Invalidate();
                    return bitmap;
                }
                BitmapImage image = new BitmapImage();
                image.SetSource(new MemoryStream(bytes));
                return image;
            }
            catch (Exception e)
            {
                Log.Write("Couldn't decode a wallpaper: {0}", e.Message);
                return null;
            }
        }

        /// <summary>
        /// The wallpaper as an image for done, on the UI thread, a raw one's
        /// pixels made in the background first.
        /// </summary>
        public static void Load(byte[] bytes, Action<BitmapSource> done)
        {
            if (!IsRaw(bytes))
            {
                done(bytes != null ? Create(bytes, null) : null);
                return;
            }
            Background.Run(() => ToPixels(bytes), delegate(Pixels pixels, Exception error)
            {
                done(pixels != null ? Create(bytes, pixels) : null);
            });
        }
    }
}
