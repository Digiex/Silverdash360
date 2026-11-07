using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Silverdash360.Controls;
using Silverdash360.Native;
using Silverdash360.Services;
using Silverdash360.Services.Files;
using Silverlight360;

namespace Silverdash360.Pages
{
    /// <summary>
    /// The theme the editor is making, shared with the pages it opens to
    /// pick a wallpaper: the colours and a wallpaper for each of four slots
    /// (null for none). A pick sets <see cref="Picked"/>; each page that was
    /// opened for it goes back when it sees that, until the editor is shown.
    /// </summary>
    public static class ThemeDraft
    {
        public const int Slots = 4;

        // A picture larger than this is likely a photo too large to decode
        // on the console.
        public const int MaxPictureSize = 4 * 1024 * 1024;
        private const int ReadBlock = 0x10000;

        public static Theme.Palette Palette { get; private set; }
        public static byte[][] Wallpapers { get; private set; }

        /// <summary>The colours shown when the editor opened, to put back on leaving it unsaved.</summary>
        public static Theme.Palette Original { get; private set; }
        public static bool OriginalIsDefault { get; private set; }

        /// <summary>The editor was opened anew: it starts from the theme shown.</summary>
        public static bool Fresh { get; private set; }

        /// <summary>The editor's row selected when it opened a picker, to come back to.</summary>
        public static int Row { get; set; }

        /// <summary>The wallpaper slot a picker fills.</summary>
        public static int Slot { get; set; }

        /// <summary>A wallpaper was picked: the pages opened for it go back.</summary>
        public static bool Picked { get; private set; }

        /// <summary>Starts from the colours and wallpapers shown now.</summary>
        public static void Start(DashTheme current)
        {
            Original = Theme.Applied;
            OriginalIsDefault = Theme.IsDefault;
            Palette = Original.Copy();
            Wallpapers = new byte[Slots][];
            if (current != null)
            {
                for (int i = 0; i < Slots && i < current.Wallpapers.Count; i++)
                {
                    Wallpapers[i] = current.Wallpapers[i];
                }
            }
            Fresh = true;
            Picked = false;
            Row = 0;
        }

        /// <summary>The editor has taken the fresh start.</summary>
        public static void Opened()
        {
            Fresh = false;
        }

        public static void Pick(byte[] wallpaper)
        {
            Wallpapers[Slot] = wallpaper;
            Picked = true;
        }

        /// <summary>The editor is back: nothing more to go back for.</summary>
        public static void PickTaken()
        {
            Picked = false;
        }

        /// <summary>The wallpapers set, in slot order, without the empty slots.</summary>
        public static List<byte[]> SetWallpapers()
        {
            return Wallpapers.Where(w => w != null).ToList();
        }

        /// <summary>Whether a file is a picture the console can show as a wallpaper.</summary>
        public static bool IsPicture(string name)
        {
            string lower = name.ToLowerInvariant();
            return lower.EndsWith(".jpg") || lower.EndsWith(".jpeg") || lower.EndsWith(".png");
        }

        /// <summary>A picture file's bytes, on a background thread; throws when it's too large.</summary>
        public static byte[] ReadPicture(XboxFileSystem files, string path, long size)
        {
            if (size > MaxPictureSize)
            {
                throw new IOException("The picture is larger than " + MaxPictureSize / (1024 * 1024) + " MB.");
            }
            using (KernelFile file = files.OpenRead(path))
            using (NativeBuffer block = new NativeBuffer(ReadBlock))
            {
                MemoryStream bytes = new MemoryStream();
                int got;
                while (bytes.Length < MaxPictureSize && (got = file.Read(block, block.Length, bytes.Length)) > 0)
                {
                    bytes.Write(block.Bytes, 0, got);
                }
                return bytes.ToArray();
            }
        }
    }
}
