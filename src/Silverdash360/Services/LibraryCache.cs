using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework.Storage;
using Silverdash360.Core;
using Silverdash360.Model;

namespace Silverdash360.Services
{
    /// <summary>
    /// Keeps what was read from the console's packages (their records and
    /// header metadata) in the title's storage container, so the next start
    /// shows the library at once and reads only the headers of new packages.
    /// Call it from a background thread: storage blocks.
    /// </summary>
    public sealed class LibraryCache
    {
        private const string ContainerName = "Silverdash360";
        private const string FileName = "library.bin";
        private const uint Magic = 0x53443343; // "SD3C"
        private const int FormatVersion = 1;
        private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(15);

        /// <summary>A package's identity: where it's stored and its file name.</summary>
        public static string KeyOf(ContentItem item)
        {
            return string.Format("{0:X16}/{1:X8}/{2:X8}/{3:X8}/{4}", item.Xuid, item.DeviceId, item.TitleId, item.Type,
                                 item.FileName);
        }

        /// <summary>The cached packages, with their metadata, or an empty list.</summary>
        public List<ContentItem> Load()
        {
            List<ContentItem> items = new List<ContentItem>();
            try
            {
                using (StorageContainer container = Open())
                {
                    if (container == null || !container.FileExists(FileName))
                    {
                        return items;
                    }
                    using (Stream stream = container.OpenFile(FileName, FileMode.Open))
                    using (BinaryReader reader = new BinaryReader(stream))
                    {
                        if (reader.ReadUInt32() != Magic || reader.ReadInt32() != FormatVersion)
                        {
                            return items;
                        }
                        int count = reader.ReadInt32();
                        for (int i = 0; i < count; i++)
                        {
                            ContentItem item = new ContentItem(reader.ReadBytes(ContentItem.RecordSize));
                            if (reader.ReadBoolean())
                            {
                                item.Metadata = ContentMetadata.Read(reader);
                            }
                            items.Add(item);
                        }
                    }
                }
                Log.Write("Library cache: {0} packages", items.Count);
            }
            catch (Exception e)
            {
                // A damaged or old cache only costs a slower start.
                Log.Write("Library cache unreadable: {0}", e.Message);
                items.Clear();
            }
            return items;
        }

        public void Save(IList<ContentItem> items)
        {
            try
            {
                using (StorageContainer container = Open())
                {
                    if (container == null)
                    {
                        return;
                    }
                    using (Stream stream = container.CreateFile(FileName))
                    using (BinaryWriter writer = new BinaryWriter(stream))
                    {
                        writer.Write(Magic);
                        writer.Write(FormatVersion);
                        writer.Write(items.Count);
                        foreach (ContentItem item in items)
                        {
                            writer.Write(item.Record);
                            writer.Write(item.Metadata != null);
                            if (item.Metadata != null)
                            {
                                item.Metadata.Write(writer);
                            }
                        }
                    }
                }
                Log.Write("Library cache saved: {0} packages", items.Count);
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
            if (!opening.AsyncWaitHandle.WaitOne((int)OpenTimeout.TotalMilliseconds))
            {
                Log.Write("Library cache: the storage container didn't open");
                return null;
            }
            return device.EndOpenContainer(opening);
        }
    }
}
