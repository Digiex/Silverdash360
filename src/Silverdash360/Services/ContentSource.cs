using System;
using System.Collections.Generic;
using Silverdash360.Core;
using Silverdash360.Model;
using Silverdash360.Native;

namespace Silverdash360.Services
{
    /// <summary>Where the dashboard gets the packages on the console from.</summary>
    public interface IContentSource
    {
        /// <summary>
        /// Every package of every title, for the user or common content (XUID
        /// 0). found, when given, hears the running count.
        /// </summary>
        List<ContentItem> Enumerate(ulong xuid, Action<int> found);

        /// <summary>The package header's metadata, or null; with its thumbnails when asked for.</summary>
        ContentMetadata ReadMetadata(ContentItem item, bool thumbnails);
    }

    /// <summary>
    /// Lists content the way the console's dashboard does: one aggregate
    /// enumerator over every title and type, with the package metadata read
    /// per item.
    /// </summary>
    public sealed class XamContentSource : IContentSource
    {
        private const int ItemsPerCall = 16;

        public List<ContentItem> Enumerate(ulong xuid, Action<int> found)
        {
            List<ContentItem> items = new List<ContentItem>();
            uint handle;
            uint result = Xam.XamContentAggregateCreateEnumerator(xuid, 0, ContentType.Any, 0, out handle);
            if (result != Xam.ErrorSuccess)
            {
                Log.Write("XamContentAggregateCreateEnumerator({0:X16}) failed: {1}", xuid, result);
                return items;
            }
            try
            {
                using (NativeBuffer buffer = new NativeBuffer(ContentItem.RecordSize * ItemsPerCall))
                {
                    while (true)
                    {
                        uint count;
                        result = Xam.XamEnumerate(handle, 0, buffer.Address, (uint)buffer.Length, out count, 0);
                        if (result != Xam.ErrorSuccess || count == 0)
                        {
                            break;
                        }
                        for (int i = 0; i < count; i++)
                        {
                            items.Add(new ContentItem(buffer.Slice(i * ContentItem.RecordSize, ContentItem.RecordSize)));
                        }
                        if (found != null && items.Count % 25 < count)
                        {
                            found(items.Count);
                        }
                    }
                }
            }
            finally
            {
                Kernel.NtClose(handle);
            }
            Log.Write("Found {0} packages for {1:X16}", items.Count, xuid);
            return items;
        }

        public ContentMetadata ReadMetadata(ContentItem item, bool thumbnails)
        {
            using (NativeBuffer record = new NativeBuffer(item.Record))
            using (NativeBuffer metadata = new NativeBuffer(ContentMetadata.Size))
            {
                uint result = Xam.XamContentGetMetaDataInternal(record.Address, metadata.Address, 0);
                if (result != Xam.ErrorSuccess)
                {
                    Log.Write("No metadata for {0}: {1}", item, result);
                    return null;
                }
                return new ContentMetadata(metadata.Bytes, thumbnails);
            }
        }
    }
}
