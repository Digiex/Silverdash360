using System;
using System.IO;
using Silverdash360.Native;

namespace Silverdash360.Model
{
    /// <summary>
    /// One package on a storage device, from the 0x200-byte
    /// XCONTENT_DATA_INTERNAL record the content enumerator returns. The record
    /// is kept as it is: metadata and launch calls take it back.
    /// </summary>
    public sealed class ContentItem
    {
        public const int RecordSize = 0x200;

        public ContentItem(byte[] record)
        {
            Record = record;
            DeviceId = BigEndian.U32(record, 0x00);
            Type = BigEndian.U32(record, 0x04);
            TitleId = BigEndian.U32(record, 0x140);
        }

        // The rest is decoded when first asked for: a library has thousands of
        // packages, most only ever counted, and the console interprets app
        // code, where every call costs.
        private string displayName;
        private string fileName;
        private string titleName;
        private bool numbersRead;
        private ulong xuid;
        private long size;
        private DateTime? created;
        private uint flags;

        public byte[] Record { get; private set; }
        public uint DeviceId { get; private set; }
        public uint Type { get; private set; }
        public uint TitleId { get; private set; }

        public string DisplayName
        {
            get { return displayName ?? (displayName = BigEndian.Utf16(Record, 0x08, 0x80)); }
        }

        public string FileName
        {
            get { return fileName ?? (fileName = BigEndian.Ansi(Record, 0x108, 42)); }
        }

        public string TitleName
        {
            get { return titleName ?? (titleName = BigEndian.Utf16(Record, 0x158, 0x40)); }
        }

        public ulong Xuid { get { ReadNumbers(); return xuid; } }
        public long Size { get { ReadNumbers(); return size; } }
        public DateTime? Created { get { ReadNumbers(); return created; } }
        public uint Flags { get { ReadNumbers(); return flags; } }

        private void ReadNumbers()
        {
            if (numbersRead)
            {
                return;
            }
            xuid = BigEndian.U64(Record, 0x138);
            size = (long)BigEndian.U64(Record, 0x148);
            created = Core.FileTimes.Utc(BigEndian.U64(Record, 0x150));
            flags = BigEndian.U32(Record, 0x1FC);
            numbersRead = true;
        }

        /// <summary>The package header was read, whether or not it had metadata.</summary>
        public bool HeaderRead { get; set; }

        // What a look inside an add-on package found: some apps, such as
        // Video Kinect, come as add-ons holding their default.xex.
        public const byte AppNotChecked = 0;
        public const byte AppNone = 1;
        public const byte AppInside = 2;

        /// <summary>Whether the package holds an app: one of the App constants.</summary>
        public byte AppCheck { get; set; }

        // What a look inside a homebrew package (title ID C0DE...) found: a
        // QuickBoot shortcut, which holds a config.ini naming the executable
        // it starts, or an app packaged whole, such as XeXMenu.
        public const byte InsideNotChecked = 0;
        public const byte InsideNothing = 1;
        public const byte InsideShortcut = 2;
        public const byte InsideExecutable = 3;

        /// <summary>One of the Inside constants.</summary>
        public byte Inside { get; set; }

        /// <summary>
        /// A shortcut's executable, as QuickBoot writes it: from the root of
        /// a drive, such as \Games\Some Game\default.xex.
        /// </summary>
        public string ShortcutTarget { get; set; }

        /// <summary>For a packaged app, its default.xex's title ID and size.</summary>
        public uint InsideTitleId { get; set; }
        public long InsideSize { get; set; }

        /// <summary>A homebrew package, which the look inside is for.</summary>
        public bool IsHomebrewPackage
        {
            get { return (TitleId >> 16) == 0xC0DE; }
        }

        /// <summary>Starts a title: a launchable type, or an add-on holding an app.</summary>
        public bool IsLaunchable
        {
            get { return ContentType.IsLaunchable(Type) || AppCheck == AppInside; }
        }

        /// <summary>Filled in from the package header when it has been read.</summary>
        public ContentMetadata Metadata { get; set; }

        /// <summary>Only partly downloaded or copied.</summary>
        public bool IsPartial { get { return (Flags & 1) != 0; } }
        public bool IsCorrupt { get { return (Flags & 2) != 0; } }

        public string TypeName { get { return ContentType.NameOf(Type); } }

        public long BestSize
        {
            get
            {
                if (Size > 0 || Metadata == null)
                {
                    return Size;
                }
                return Metadata.ContentSize > 0 ? Metadata.ContentSize : Metadata.DataSize;
            }
        }

        public override string ToString()
        {
            return string.Format("{0:X8}/{1:X8}/{2} \"{3}\"", TitleId, Type, FileName, DisplayName);
        }

        /// <summary>
        /// Writes the record with its three name fields cut at the end of
        /// their names: the names fill a fraction of their fields, which are
        /// most of the 512 bytes. (A loop over the bytes looking for zeros
        /// would be simpler, but app code is interpreted on the console and
        /// that loop cost about a millisecond a record.)
        /// </summary>
        public void WriteRecord(BinaryWriter w)
        {
            int fileNameBytes = Array.IndexOf(Record, (byte)0, 0x108, 42);
            w.Write(Record, 0x000, 0x08);
            WriteField(w, 0x008, DisplayName.Length * 2);
            WriteField(w, 0x108, fileNameBytes < 0 ? 42 : fileNameBytes - 0x108);
            w.Write(Record, 0x132, 0x158 - 0x132);
            WriteField(w, 0x158, TitleName.Length * 2);
            w.Write(Record, 0x1D8, RecordSize - 0x1D8);
        }

        public static ContentItem ReadRecord(BinaryReader r)
        {
            byte[] record = new byte[RecordSize];
            ReadPart(r, record, 0x000, 0x08);
            ReadField(r, record, 0x008, 0x100);
            ReadField(r, record, 0x108, 42);
            ReadPart(r, record, 0x132, 0x158 - 0x132);
            ReadField(r, record, 0x158, 0x80);
            ReadPart(r, record, 0x1D8, RecordSize - 0x1D8);
            return new ContentItem(record);
        }

        private void WriteField(BinaryWriter w, int offset, int bytes)
        {
            w.Write((ushort)bytes);
            w.Write(Record, offset, bytes);
        }

        private static void ReadField(BinaryReader r, byte[] record, int offset, int size)
        {
            int bytes = r.ReadUInt16();
            if (bytes > size)
            {
                throw new IOException("A cached record is damaged.");
            }
            ReadPart(r, record, offset, bytes);
        }

        private static void ReadPart(BinaryReader r, byte[] record, int offset, int bytes)
        {
            if (r.Read(record, offset, bytes) != bytes)
            {
                throw new IOException("A cached record is cut short.");
            }
        }
    }

    /// <summary>
    /// The metadata in a package header (offset 0x344 on, 0x93D6 bytes):
    /// names, description, publisher and the two thumbnails. The thumbnails
    /// are kept only when asked for: there can be a thousand packages, and
    /// art is read again for the few on screen.
    /// </summary>
    public sealed class ContentMetadata
    {
        public const int Size = 0x93D8;

        private const int DisplayNames = 0x411 - 0x344;
        private const int Descriptions = 0xD11 - 0x344;
        private const int PublisherName = 0x1611 - 0x344;
        private const int TitleNameOffset = 0x1691 - 0x344;
        private const int ThumbnailSize = 0x1712 - 0x344;
        private const int TitleThumbnailSize = 0x1716 - 0x344;
        private const int Thumbnail = 0x171A - 0x344;
        private const int TitleThumbnail = 0x571A - 0x344;
        private const int ThumbnailMax = 0x4000;

        /// <summary>Parses the metadata starting at offset o of a buffer.</summary>
        public ContentMetadata(byte[] m, int o, bool thumbnails)
        {
            ContentType = BigEndian.U32(m, o + 0x00);
            MetadataVersion = BigEndian.U32(m, o + 0x04);
            ContentSize = (long)BigEndian.U64(m, o + 0x08);
            MediaId = BigEndian.U32(m, o + 0x10);
            Version = BigEndian.U32(m, o + 0x14);
            BaseVersion = BigEndian.U32(m, o + 0x18);
            TitleId = BigEndian.U32(m, o + 0x1C);
            DataSize = (long)BigEndian.U64(m, o + 0x3A1 - 0x344);
            DisplayName = FirstLanguage(m, o + DisplayNames);
            Description = FirstLanguage(m, o + Descriptions);
            Publisher = BigEndian.Utf16(m, o + PublisherName, 0x40);
            TitleName = BigEndian.Utf16(m, o + TitleNameOffset, 0x40);
            if (thumbnails)
            {
                ThumbnailPng = Png(m, o + Thumbnail, BigEndian.U32(m, o + ThumbnailSize));
                TitleThumbnailPng = Png(m, o + TitleThumbnail, BigEndian.U32(m, o + TitleThumbnailSize));
            }
        }

        public uint ContentType { get; private set; }
        public uint MetadataVersion { get; private set; }
        public long ContentSize { get; private set; }
        public uint MediaId { get; private set; }
        public uint Version { get; private set; }
        public uint BaseVersion { get; private set; }
        public uint TitleId { get; private set; }
        /// <summary>The combined size of an SVOD package's data files.</summary>
        public long DataSize { get; private set; }
        public string DisplayName { get; private set; }
        public string Description { get; private set; }
        public string Publisher { get; private set; }
        public string TitleName { get; private set; }
        public byte[] ThumbnailPng { get; private set; }
        public byte[] TitleThumbnailPng { get; private set; }

        public string VersionText
        {
            get { return string.Format("{0}.{1}.{2}.{3}", Version >> 28, (Version >> 24) & 0xF, (Version >> 8) & 0xFFFF, Version & 0xFF); }
        }

        private ContentMetadata()
        {
        }

        /// <summary>Writes the fields the dashboard uses but the thumbnails, for the library cache.</summary>
        public void Write(BinaryWriter w)
        {
            w.Write(ContentType);
            w.Write(MetadataVersion);
            w.Write(ContentSize);
            w.Write(MediaId);
            w.Write(Version);
            w.Write(BaseVersion);
            w.Write(TitleId);
            w.Write(DataSize);
            w.Write(DisplayName);
            w.Write(Description);
            w.Write(Publisher);
            w.Write(TitleName);
        }

        public static ContentMetadata Read(BinaryReader r)
        {
            return new ContentMetadata
            {
                ContentType = r.ReadUInt32(),
                MetadataVersion = r.ReadUInt32(),
                ContentSize = r.ReadInt64(),
                MediaId = r.ReadUInt32(),
                Version = r.ReadUInt32(),
                BaseVersion = r.ReadUInt32(),
                TitleId = r.ReadUInt32(),
                DataSize = r.ReadInt64(),
                DisplayName = r.ReadString(),
                Description = r.ReadString(),
                Publisher = r.ReadString(),
                TitleName = r.ReadString(),
            };
        }

        // English first, then the first language that has text. The header
        // has 9 languages of 0x80 characters each.
        private static string FirstLanguage(byte[] m, int offset)
        {
            for (int i = 0; i < 9; i++)
            {
                string s = BigEndian.Utf16(m, offset + i * 0x100, 0x80);
                if (s.Length > 0)
                {
                    return s;
                }
            }
            return "";
        }

        private static byte[] Png(byte[] m, int offset, uint length)
        {
            if (length == 0 || length > ThumbnailMax || offset + length > m.Length)
            {
                return null;
            }
            byte[] png = new byte[length];
            Buffer.BlockCopy(m, offset, png, 0, (int)length);
            return png;
        }
    }
}
