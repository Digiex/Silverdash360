using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Xna.Framework.Storage;
using Silverdash360.Core;
using Silverdash360.Model;

namespace Silverdash360.Services
{
    /// <summary>
    /// Keeps what was read from the console's packages (their records and
    /// header metadata) and the executables found, so the next start shows the
    /// library at once and reads only the headers of new packages. It lives
    /// in the title's storage container. Records are written without their
    /// runs of zeros and thumbnails aren't kept, so with thousands of packages
    /// it stays around half a kilobyte a package. Call it from a background
    /// thread: storage blocks.
    /// </summary>
    public sealed class LibraryCache
    {
        private const string ContainerName = "Silverdash360";
        private const string FileName = "library.bin";
        private const uint Magic = 0x53443343; // "SD3C"
        private const int FormatVersion = 4;
        private const int BlockSize = 0x10000;
        private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(60);

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
        }

        /// <summary>The cached packages with their metadata, and the executables found.</summary>
        public Contents Load()
        {
            Contents contents = new Contents();
            try
            {
                MemoryStream bytes = new MemoryStream();
                using (StorageContainer container = Open())
                {
                    if (container == null || !container.FileExists(FileName))
                    {
                        return contents;
                    }
                    // Read to the end: a container stream's Length can count
                    // more than the file holds.
                    using (Stream stream = container.OpenFile(FileName, FileMode.Open))
                    {
                        byte[] block = new byte[BlockSize];
                        int got;
                        while ((got = stream.Read(block, 0, block.Length)) > 0)
                        {
                            bytes.Write(block, 0, got);
                        }
                    }
                }
                bytes.Position = 0;
                using (BinaryReader reader = new BinaryReader(bytes))
                {
                    if (reader.ReadUInt32() != Magic || reader.ReadInt32() != FormatVersion)
                    {
                        return contents;
                    }
                    int count = reader.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        ContentItem item = ContentItem.ReadRecord(reader);
                        if (reader.ReadBoolean())
                        {
                            item.Metadata = ContentMetadata.Read(reader);
                        }
                        contents.Content.Add(item);
                    }
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

        /// <summary>
        /// Saves the library. pace, when given, is called between blocks so a
        /// background save leaves the console to the UI.
        /// </summary>
        public void Save(IList<ContentItem> items, IList<ExecutableItem> executables, Action pace)
        {
            try
            {
                MemoryStream buffer = new MemoryStream();
                BinaryWriter writer = new BinaryWriter(buffer);
                writer.Write(Magic);
                writer.Write(FormatVersion);
                writer.Write(items.Count);
                foreach (ContentItem item in items)
                {
                    item.WriteRecord(writer);
                    writer.Write(item.Metadata != null);
                    if (item.Metadata != null)
                    {
                        item.Metadata.Write(writer);
                    }
                }
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
                writer.Flush();
                byte[] bytes = buffer.ToArray();
                using (StorageContainer container = Open())
                {
                    if (container == null)
                    {
                        return;
                    }
                    using (Stream stream = container.CreateFile(FileName))
                    {
                        for (int at = 0; at < bytes.Length; at += BlockSize)
                        {
                            stream.Write(bytes, at, Math.Min(BlockSize, bytes.Length - at));
                            if (pace != null)
                            {
                                pace();
                            }
                        }
                    }
                }
                Log.Write("Library cache saved: {0} packages, {1} executables, {2} bytes", items.Count,
                          executables.Count, bytes.Length);
            }
            catch (Exception e)
            {
                Log.Write("Library cache not saved: {0}", e.Message);
            }
        }

        /// <summary>Forgets the cache, so the next refresh reads every header again.</summary>
        public void Clear()
        {
            try
            {
                using (StorageContainer container = Open())
                {
                    if (container != null && container.FileExists(FileName))
                    {
                        container.DeleteFile(FileName);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Write("Library cache not cleared: {0}", e.Message);
            }
        }

        // The title's storage container, or null when there's no storage.
        private static StorageContainer Open()
        {
            StorageDevice device = new StorageDevice();
            IAsyncResult opening = device.BeginOpenContainer(ContainerName, ContainerName, null, null);
            // Polled rather than waited on: the wait handle could report a
            // timeout at once while the open went on.
            DateTime giveUp = DateTime.UtcNow + OpenTimeout;
            while (!opening.IsCompleted)
            {
                if (DateTime.UtcNow > giveUp)
                {
                    Log.Write("Library cache: the storage container didn't open");
                    return null;
                }
                Thread.Sleep(20);
            }
            return device.EndOpenContainer(opening);
        }
    }
}
