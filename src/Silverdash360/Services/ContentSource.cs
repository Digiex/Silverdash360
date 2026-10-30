using System;
using System.Collections.Generic;
using System.Threading;
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
        /// 0). found, when given, hears the running count; pace, when given,
        /// is called between batches so a background refresh can yield.
        /// </summary>
        List<ContentItem> Enumerate(ulong xuid, Action<int> found, Action pace);

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
        private const int PaceEvery = 32;

        // The console's enumerator gathers content as it goes, and a call can
        // return nothing before it has finished; only "no more files" ends it.
        private const int MaxEmptyCalls = 200;
        private const int EmptyCallWait = 25;

        // One pinned buffer for every metadata read. A new one per read made
        // 38 KB of garbage per package, and with thousands of packages the
        // collections that followed stalled the UI.
        private readonly object metadataLock = new object();
        private NativeBuffer metadataBuffer;

        public List<ContentItem> Enumerate(ulong xuid, Action<int> found, Action pace)
        {
            List<ContentItem> items = new List<ContentItem>();
            uint handle;
            uint result = Xam.XamContentAggregateCreateEnumerator(xuid, 0, ContentType.Any, 0, out handle);
            if (result != Xam.ErrorSuccess)
            {
                Log.Write("XamContentAggregateCreateEnumerator({0:X16}) failed: {1}", xuid, result);
                return items;
            }
            int empty = 0;
            try
            {
                using (NativeBuffer buffer = new NativeBuffer(ContentItem.RecordSize * ItemsPerCall))
                {
                    while (true)
                    {
                        uint count;
                        result = Xam.XamEnumerate(handle, 0, buffer.Address, (uint)buffer.Length, out count, 0);
                        if (result != Xam.ErrorSuccess)
                        {
                            break;
                        }
                        if (count == 0)
                        {
                            if (++empty > MaxEmptyCalls)
                            {
                                break;
                            }
                            Thread.Sleep(EmptyCallWait);
                            continue;
                        }
                        empty = 0;
                        for (int i = 0; i < count; i++)
                        {
                            items.Add(new ContentItem(buffer.Slice(i * ContentItem.RecordSize, ContentItem.RecordSize)));
                        }
                        if (found != null && items.Count % 25 < count)
                        {
                            found(items.Count);
                        }
                        // A call can return a single item, so pace by items.
                        if (pace != null && items.Count % PaceEvery < count)
                        {
                            pace();
                        }
                    }
                }
            }
            finally
            {
                Kernel.NtClose(handle);
            }
            Log.Write("Found {0} packages for {1:X16}; the enumeration ended with {2}{3}", items.Count, xuid, result,
                      empty > MaxEmptyCalls ? " after waiting for more" : "");
            return items;
        }

        public ContentMetadata ReadMetadata(ContentItem item, bool thumbnails)
        {
            lock (metadataLock)
            {
                if (metadataBuffer == null)
                {
                    metadataBuffer = new NativeBuffer(ContentItem.RecordSize + ContentMetadata.Size);
                }
                NativeBuffer buffer = metadataBuffer;
                Buffer.BlockCopy(item.Record, 0, buffer.Bytes, 0, ContentItem.RecordSize);
                uint result = Xam.XamContentGetMetaDataInternal(buffer.Address, buffer[ContentItem.RecordSize], 0);
                if (result != Xam.ErrorSuccess)
                {
                    Log.Write("No metadata for {0}: {1}", item, result);
                    return null;
                }
                return new ContentMetadata(buffer.Bytes, ContentItem.RecordSize, thumbnails);
            }
        }
    }
}
