using System;
using System.Collections.Generic;

namespace Silverdash360.Services.Media
{
    public enum MediaKind
    {
        Folder,
        Music,
        Video,
        Picture,
    }

    /// <summary>A folder or a piece of media in a <see cref="IMediaSource"/>.</summary>
    public sealed class MediaEntry
    {
        public IMediaSource Source;
        /// <summary>The source's name for it: a path on a drive, an object ID on a media server.</summary>
        public string Id;
        public string Name;
        public MediaKind Kind;
        /// <summary>In bytes, or -1 when not known.</summary>
        public long Size = -1;
        public string Artist;
        public string Album;
        public TimeSpan? Duration;
        /// <summary>Where a media server serves it.</summary>
        public Uri Url;
        /// <summary>
        /// For media the console can't play, the format it's in, such as MKV;
        /// it's listed so a folder of it isn't empty, but not played.
        /// </summary>
        public string Unplayable;

        /// <summary>The package it's in, for a downloaded video: its thumbnail is the entry's icon.</summary>
        public Model.ContentItem Package;

        /// <summary>A media server's picture for it, such as an album cover.</summary>
        public Uri ArtUrl;

        public bool IsFolder { get { return Kind == MediaKind.Folder; } }

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// Where media comes from: a drive, or a media server on the network.
    /// Calls block, so make them off the UI thread.
    /// </summary>
    public interface IMediaSource
    {
        /// <summary>Unique among the sources, for pages to find it by.</summary>
        string Key { get; }
        string Name { get; }
        /// <summary>What it is, such as "Hard drive" or "Media server".</summary>
        string Description { get; }

        /// <summary>A folder's entries, or the top level's for null.</summary>
        List<MediaEntry> List(MediaEntry folder);

        /// <summary>
        /// Where the player and image decoder fetch the entry from: the
        /// console's MediaElement plays only from a URI.
        /// </summary>
        Uri Locate(MediaEntry entry);
    }

    /// <summary>What the console plays, by file name and by MIME type.</summary>
    public static class MediaFormats
    {
        // What the platform's player decodes (with the console's codecs), and
        // what Silverlight's image decoder reads.
        private static readonly Dictionary<string, MediaKind> Extensions = new Dictionary<string, MediaKind>(StringComparer.OrdinalIgnoreCase)
        {
            { ".mp3", MediaKind.Music }, { ".wma", MediaKind.Music }, { ".m4a", MediaKind.Music },
            { ".wmv", MediaKind.Video }, { ".asf", MediaKind.Video }, { ".mp4", MediaKind.Video },
            { ".m4v", MediaKind.Video }, { ".mov", MediaKind.Video },
            { ".jpg", MediaKind.Picture }, { ".jpeg", MediaKind.Picture }, { ".png", MediaKind.Picture },
        };

        private static readonly Dictionary<string, MediaKind> Types = new Dictionary<string, MediaKind>(StringComparer.OrdinalIgnoreCase)
        {
            { "audio/mpeg", MediaKind.Music }, { "audio/mp3", MediaKind.Music }, { "audio/x-ms-wma", MediaKind.Music },
            { "audio/mp4", MediaKind.Music }, { "audio/x-m4a", MediaKind.Music }, { "audio/aac", MediaKind.Music },
            { "video/x-ms-wmv", MediaKind.Video }, { "video/x-ms-asf", MediaKind.Video }, { "video/mp4", MediaKind.Video },
            { "video/x-m4v", MediaKind.Video }, { "video/quicktime", MediaKind.Video },
            { "image/jpeg", MediaKind.Picture }, { "image/png", MediaKind.Picture },
        };

        /// <summary>The kind of a file the console plays, or null.</summary>
        public static MediaKind? OfFile(string name)
        {
            int dot = name.LastIndexOf('.');
            MediaKind kind;
            return dot >= 0 && Extensions.TryGetValue(name.Substring(dot), out kind) ? kind : (MediaKind?)null;
        }

        /// <summary>The kind of a MIME type the console plays, or null.</summary>
        public static MediaKind? OfType(string mime)
        {
            MediaKind kind;
            return mime != null && Types.TryGetValue(mime.Split(';')[0].Trim(), out kind) ? kind : (MediaKind?)null;
        }

        public static string NameOf(MediaKind kind)
        {
            switch (kind)
            {
                case MediaKind.Music: return "Music";
                case MediaKind.Video: return "Video";
                case MediaKind.Picture: return "Picture";
                default: return "Folder";
            }
        }
    }
}
