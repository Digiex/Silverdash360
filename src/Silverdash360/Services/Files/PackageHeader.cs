using System;
using Silverdash360.Core;
using Silverdash360.Native;

namespace Silverdash360.Services.Files
{
    /// <summary>
    /// What a content package's header says, read straight from the file: an
    /// STFS package (CON, LIVE or PIRS) starts with its magic, and its header
    /// holds the content type, title ID, display name and thumbnail at fixed
    /// offsets. For browsing files; the library reads packages through xam.
    /// </summary>
    public sealed class PackageHeader
    {
        private const int ContentTypeAt = 0x344;
        private const int TitleIdAt = 0x360;
        private const int DisplayNameAt = 0x411;
        private const int DisplayNameSize = 0x80;
        private const int ThumbnailSizeAt = 0x1712;
        private const int ThumbnailAt = 0x171A;
        private const int ThumbnailMax = 0x3D00;
        // Enough of the header for the name; the thumbnail is read when asked for.
        private const int NamePart = DisplayNameAt + DisplayNameSize;
        // The smallest package: its header, rounded up to a block.
        public const long MinimumSize = 0xA000;

        public uint ContentType;
        public uint TitleId;
        public string DisplayName;

        /// <summary>The header of a package file, or null for any other file.</summary>
        public static PackageHeader Read(XboxFileSystem files, string path)
        {
            try
            {
                using (KernelFile file = files.OpenRead(path))
                using (NativeBuffer buffer = new NativeBuffer(NamePart))
                {
                    if (file.Read(buffer, NamePart, 0) < NamePart || !IsMagic(buffer.Bytes))
                    {
                        return null;
                    }
                    return new PackageHeader
                    {
                        ContentType = buffer.U32(ContentTypeAt),
                        TitleId = buffer.U32(TitleIdAt),
                        DisplayName = buffer.Utf16(DisplayNameAt, DisplayNameSize / 2),
                    };
                }
            }
            catch (Exception e)
            {
                Log.Write("Couldn't read {0}: {1}", path, e.Message);
                return null;
            }
        }

        /// <summary>The thumbnail in a package's header, a PNG, or null.</summary>
        public static byte[] ReadThumbnail(XboxFileSystem files, string path)
        {
            using (KernelFile file = files.OpenRead(path))
            using (NativeBuffer buffer = new NativeBuffer(ThumbnailAt + ThumbnailMax))
            {
                int read = file.Read(buffer, buffer.Length, 0);
                if (read < ThumbnailAt || !IsMagic(buffer.Bytes))
                {
                    return null;
                }
                int size = (int)buffer.U32(ThumbnailSizeAt);
                if (size <= 0 || size > ThumbnailMax || ThumbnailAt + size > read)
                {
                    return null;
                }
                return buffer.Slice(ThumbnailAt, size);
            }
        }

        private static bool IsMagic(byte[] bytes)
        {
            string magic = new string(new[] { (char)bytes[0], (char)bytes[1], (char)bytes[2], (char)bytes[3] });
            return magic == "CON " || magic == "LIVE" || magic == "PIRS";
        }
    }
}
