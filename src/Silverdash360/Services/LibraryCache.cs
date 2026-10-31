using System;
using System.Collections.Generic;
using System.IO;
using Silverdash360.Core;
using Silverdash360.Model;

namespace Silverdash360.Services
{
    /// <summary>
    /// Keeps what was read from the console's packages (their records and
    /// header metadata) and the executables found, so the next start shows the
    /// library at once and reads only the headers of new packages. It lives
    /// in the app's storage (<see cref="AppStorage"/>). Records
    /// are written with their name fields cut at the end of the names and
    /// thumbnails aren't kept, so with thousands of packages it stays around
    /// half a kilobyte a package. Call it from a background thread: storage
    /// blocks.
    ///
    /// The packages are kept in sections, read in order and handed over one
    /// at a time, so a start can show the first screens before it has read
    /// the rest: the console takes seconds to read thousands of packages.
    /// </summary>
    public sealed class LibraryCache
    {
        private const string FileName = "library.bin";
        private const uint Magic = 0x53443343; // "SD3C"
        private const int FormatVersion = 9;
        // Without the packages' disc numbers.
        private const int NoDiscsVersion = 8;
        // Without what's inside homebrew packages; still read, so a new
        // build doesn't read every header again.
        private const int NoInsideVersion = 7;
        // Without the app check's byte either.
        private const int NoAppCheckVersion = 6;
        // Packages, then executables, in one section, and no app check.
        private const int UnsectionedVersion = 5;
        // After each package, what's known of its header. Older files wrote a
        // bool, so 0 and 1 mean what they did there.
        private const byte HeaderNotRead = 0;
        private const byte HeaderMetadata = 1;
        private const int BreatheEvery = 64;
        private const byte HeaderWithoutMetadata = 2;

        /// <summary>A package's identity: where it's stored and its file name.</summary>
        public static string KeyOf(ContentItem item)
        {
            return string.Format("{0:X16}/{1:X8}/{2:X8}/{3:X8}/{4}", item.Xuid, item.DeviceId, item.TitleId, item.Type,
                                 item.FileName);
        }

        /// <summary>What the cache holds.</summary>
        public sealed class Contents
        {
            public List<ContentItem> Content = new List<ContentItem>();
            public List<ExecutableItem> Executables = new List<ExecutableItem>();
            /// <summary>Whether every section has been read.</summary>
            public bool Complete;
            /// <summary>Read from an older format, which a save brings up to date.</summary>
            public bool Outdated;
        }

        /// <summary>
        /// The cached packages with their metadata, and the executables found.
        /// section, when given, is called after each section with what has
        /// been read so far, the last time with <see cref="Contents.Complete"/>.
        /// </summary>
        public Contents Load(Action<Contents> section)
        {
            Contents contents = new Contents();
            try
            {
                byte[] bytes = AppStorage.ReadBytes(FileName);
                if (bytes == null)
                {
                    return contents;
                }
                contents = Deserialize(new MemoryStream(bytes), section);
                Log.Write("Library cache: {0} packages, {1} executables", contents.Content.Count,
                          contents.Executables.Count);
            }
            catch (Exception e)
            {
                // A damaged or old cache only costs a slower start.
                Log.Write("Library cache unreadable: {0}", e);
                contents = new Contents();
            }
            return contents;
        }

        /// <summary>Reads what <see cref="Serialize"/> wrote; empty for another format version.</summary>
        public static Contents Deserialize(Stream bytes, Action<Contents> section)
        {
            Contents contents = new Contents();
            BinaryReader reader = new BinaryReader(bytes);
            if (reader.ReadUInt32() != Magic)
            {
                return contents;
            }
            int version = reader.ReadInt32();
            if (version == UnsectionedVersion)
            {
                contents.Outdated = true;
                ReadPackages(reader, contents, false, false, false);
                ReadExecutables(reader, contents);
                contents.Complete = true;
                if (section != null)
                {
                    section(contents);
                }
                return contents;
            }
            if (version != FormatVersion && version != NoDiscsVersion && version != NoInsideVersion &&
                version != NoAppCheckVersion)
            {
                return contents;
            }
            bool appCheck = version != NoAppCheckVersion;
            bool inside = version == FormatVersion || version == NoDiscsVersion;
            bool discs = version == FormatVersion;
            contents.Outdated = !discs;
            ReadExecutables(reader, contents);
            int sections = reader.ReadInt32();
            for (int s = 0; s < sections; s++)
            {
                ReadPackages(reader, contents, appCheck, inside, discs);
                contents.Complete = s == sections - 1;
                if (section != null)
                {
                    section(contents);
                }
            }
            contents.Complete = true;
            return contents;
        }

        private static void ReadPackages(BinaryReader reader, Contents contents, bool appCheck, bool inside, bool discs)
        {
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                // Thousands of records take seconds: they wait while the user moves.
                if (i % BreatheEvery == 0)
                {
                    UiIdle.WaitWhileMoving(UiIdle.BackgroundQuietMs);
                }
                ContentItem item = ContentItem.ReadRecord(reader);
                byte header = reader.ReadByte();
                if (header == HeaderMetadata)
                {
                    item.Metadata = ContentMetadata.Read(reader, discs);
                }
                item.HeaderRead = header != HeaderNotRead;
                if (appCheck)
                {
                    item.AppCheck = reader.ReadByte();
                }
                if (inside)
                {
                    item.Inside = reader.ReadByte();
                    if (item.Inside == ContentItem.InsideShortcut)
                    {
                        item.ShortcutTarget = reader.ReadString();
                    }
                    else if (item.Inside == ContentItem.InsideExecutable)
                    {
                        item.InsideTitleId = reader.ReadUInt32();
                        item.InsideSize = reader.ReadInt64();
                    }
                }
                contents.Content.Add(item);
            }
        }

        private static void ReadExecutables(BinaryReader reader, Contents contents)
        {
            int executables = reader.ReadInt32();
            for (int i = 0; i < executables; i++)
            {
                contents.Executables.Add(new ExecutableItem
                {
                    Path = reader.ReadString(),
                    DevicePath = reader.ReadString(),
                    DeviceFolder = reader.ReadString(),
                    Name = reader.ReadString(),
                    Collection = reader.ReadString(),
                    Drive = reader.ReadString(),
                    TitleId = reader.ReadUInt32(),
                    Size = reader.ReadInt64(),
                    Modified = new DateTime(reader.ReadInt64()),
                });
            }
        }

        /// <summary>
        /// Saves the library, its packages in the sections given. pace, when
        /// given, is called between blocks so a background save leaves the
        /// console to the UI.
        /// </summary>
        public void Save(IList<IList<ContentItem>> sections, IList<ExecutableItem> executables, Action pace)
        {
            try
            {
                byte[] bytes = Serialize(sections, executables);
                if (!AppStorage.WriteBytes(FileName, bytes, pace))
                {
                    return;
                }
                int packages = 0;
                foreach (IList<ContentItem> section in sections)
                {
                    packages += section.Count;
                }
                Log.Write("Library cache saved: {0} packages in {1} sections, {2} executables, {3} bytes", packages,
                          sections.Count, executables.Count, bytes.Length);
            }
            catch (Exception e)
            {
                Log.Write("Library cache not saved: {0}", e.Message);
            }
        }

        /// <summary>The cache file's bytes for the library.</summary>
        public static byte[] Serialize(IList<IList<ContentItem>> sections, IList<ExecutableItem> executables)
        {
            MemoryStream buffer = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(buffer);
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(executables.Count);
            foreach (ExecutableItem item in executables)
            {
                writer.Write(item.Path);
                writer.Write(item.DevicePath);
                writer.Write(item.DeviceFolder);
                writer.Write(item.Name);
                writer.Write(item.Collection);
                writer.Write(item.Drive);
                writer.Write(item.TitleId);
                writer.Write(item.Size);
                writer.Write(item.Modified.Ticks);
            }
            writer.Write(sections.Count);
            foreach (IList<ContentItem> section in sections)
            {
                writer.Write(section.Count);
                foreach (ContentItem item in section)
                {
                    item.WriteRecord(writer);
                    writer.Write(item.Metadata != null ? HeaderMetadata
                                 : item.HeaderRead ? HeaderWithoutMetadata : HeaderNotRead);
                    if (item.Metadata != null)
                    {
                        item.Metadata.Write(writer);
                    }
                    writer.Write(item.AppCheck);
                    writer.Write(item.Inside);
                    if (item.Inside == ContentItem.InsideShortcut)
                    {
                        writer.Write(item.ShortcutTarget ?? "");
                    }
                    else if (item.Inside == ContentItem.InsideExecutable)
                    {
                        writer.Write(item.InsideTitleId);
                        writer.Write(item.InsideSize);
                    }
                }
            }
            writer.Flush();
            return buffer.ToArray();
        }

        /// <summary>Forgets the cache, so the next refresh reads every header again.</summary>
        public void Clear()
        {
            AppStorage.Delete(FileName);
        }
    }
}
