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
            DisplayName = BigEndian.Utf16(record, 0x08, 0x80);
            FileName = BigEndian.Ansi(record, 0x108, 42);
            Xuid = BigEndian.U64(record, 0x138);
            TitleId = BigEndian.U32(record, 0x140);
            Size = (long)BigEndian.U64(record, 0x148);
            ulong created = BigEndian.U64(record, 0x150);
            Created = created != 0 && created < (ulong)DateTime.MaxValue.ToFileTimeUtc()
                ? DateTime.FromFileTimeUtc((long)created) : (DateTime?)null;
            TitleName = BigEndian.Utf16(record, 0x158, 0x40);
            Flags = BigEndian.U32(record, 0x1FC);
        }

        public byte[] Record { get; private set; }
        public uint DeviceId { get; private set; }
        public uint Type { get; private set; }
        public string DisplayName { get; private set; }
        public string FileName { get; private set; }
        public ulong Xuid { get; private set; }
        public uint TitleId { get; private set; }
        public long Size { get; private set; }
        public DateTime? Created { get; private set; }
        public string TitleName { get; private set; }
        public uint Flags { get; private set; }

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
        /// Writes the record with its runs of zeros left out: names fill a
        /// fraction of their fields, so most of the 512 bytes are zeros.
        /// </summary>
        public void WriteRecord(BinaryWriter w)
        {
            byte[] r = Record;
            int at = 0;
            while (at < r.Length)
            {
                int zeros = 0;
                while (at + zeros < r.Length && r[at + zeros] == 0)
                {
                    zeros++;
                }
                int bytes = 0;
                while (at + zeros + bytes < r.Length && r[at + zeros + bytes] != 0)
                {
                    bytes++;
                }
                w.Write((ushort)zeros);
                w.Write((ushort)bytes);
                w.Write(r, at + zeros, bytes);
                at += zeros + bytes;
            }
        }

        public static ContentItem ReadRecord(BinaryReader r)
        {
            byte[] record = new byte[RecordSize];
            int at = 0;
            while (at < RecordSize)
            {
                at += r.ReadUInt16();
                int bytes = r.ReadUInt16();
                if (at + bytes > RecordSize)
                {
                    throw new IOException("A cached record is damaged.");
                }
                r.Read(record, at, bytes);
                at += bytes;
            }
            return new ContentItem(record);
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
