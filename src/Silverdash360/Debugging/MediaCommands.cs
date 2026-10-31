using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using Silverdash360.Modules;
using Silverdash360.Pages;
using Silverdash360.Services.Media;

namespace Silverdash360.Debugging
{
    /// <summary>The media section: its sources, folders, playing and what the player is doing.</summary>
    public sealed class MediaCommands : IDebugCommands
    {
        private readonly DashboardContext context;

        public MediaCommands(DashboardContext context)
        {
            this.context = context;
        }

        private MediaService Media { get { return context.Media; } }

        public void Register(DebugCommandSet commands)
        {
            commands.Add("media.sources", "", "The drives with media, the disc and the media servers found.", r => Sources());
            commands.Add("media.search", "[timeoutMs]", "Looks for media servers and waits for the search to end.", Search);
            commands.Add("media.add", "<descriptionUrl>", "Adds a media server by the URL of its description.", r =>
            {
                Uri location;
                if (!Uri.TryCreate(r.Arg(0, "URL", true), UriKind.Absolute, out location))
                {
                    throw new DebugException("Not a URL.");
                }
                DlnaMediaSource server = Media.AddServer(location);
                // It's listed on the UI thread.
                int start = Environment.TickCount;
                while (Media.Find(server.Key) == null && Environment.TickCount - start < 5000)
                {
                    Thread.Sleep(20);
                }
                return Source(server);
            });
            commands.Add("media.list", "<source> [id]", "A folder of a source (its top level without an ID).", List);
            commands.Add("media.play", "<source> <folderId|-> <index>", "Plays or shows an entry of a folder, as A on it does.", Play);
            commands.Add("media.state", "", "What the player or picture viewer is doing.", r => UiThread.Invoke(() => State()));
            commands.Add("media.next", "[step]", "Moves along the queue, as Y and X do.", Next);
        }

        private JsonObject Sources()
        {
            return new JsonObject
            {
                { "disc", Media.Disc().ToString() },
                { "drives", Media.Drives().Select(s => Source(s)).ToList() },
                { "servers", Media.Servers.Select(s => Source(s)).ToList() },
                { "searching", Media.IsSearching },
                { "lastSearch", Media.LastSearch },
            };
        }

        private static JsonObject Source(IMediaSource source)
        {
            return new JsonObject { { "key", source.Key }, { "name", source.Name }, { "description", source.Description } };
        }

        private object Search(DebugRequest request)
        {
            int timeout = request.Int(0, 15000);
            UiThread.Invoke(() => Media.Search());
            int start = Environment.TickCount;
            Thread.Sleep(100);
            while (Media.IsSearching && Environment.TickCount - start < timeout)
            {
                Thread.Sleep(50);
            }
            JsonObject result = Sources();
            result.Add("searchMs", Environment.TickCount - start);
            return result;
        }

        private IMediaSource FindSource(DebugRequest request)
        {
            string key = request.Arg(0, "source", true);
            IMediaSource source = Media.Find(key);
            if (source == null)
            {
                throw new DebugException("No source " + key + ". \"media.sources\" lists them.");
            }
            return source;
        }

        private static MediaEntry Folder(IMediaSource source, string id)
        {
            return id == null || id == "-" ? null : new MediaEntry { Source = source, Id = id, Name = id, Kind = MediaKind.Folder };
        }

        private object List(DebugRequest request)
        {
            IMediaSource source = FindSource(request);
            int start = Environment.TickCount;
            List<MediaEntry> entries = source.List(Folder(source, request.Arg(1, null)));
            return new JsonObject
            {
                { "listMs", Environment.TickCount - start },
                { "count", entries.Count },
                { "entries", entries.Take(500).Select(e => Entry(e)).ToList() },
            };
        }

        private object Play(DebugRequest request)
        {
            IMediaSource source = FindSource(request);
            List<MediaEntry> entries = source.List(Folder(source, request.Arg(1, "folder ID or -", true)));
            int index = request.Int(2, 0);
            if (index < 0 || index >= entries.Count || entries[index].IsFolder)
            {
                throw new DebugException("Entry " + index + " isn't media.");
            }
            UiThread.Invoke(() => context.PlayMedia(entries, entries[index]));
            Thread.Sleep(600);
            return UiThread.Invoke(() => State());
        }

        private object Next(DebugRequest request)
        {
            int step = request.Int(0, 1);
            UiThread.Invoke(() =>
            {
                if (PlayerPage.Current == null)
                {
                    throw new DebugException("The player isn't shown.");
                }
                PlayerPage.Current.Next(step, true);
            });
            Thread.Sleep(300);
            return UiThread.Invoke(() => State());
        }

        private JsonObject State()
        {
            MediaEntry current = Media.Current;
            JsonObject state = new JsonObject
            {
                { "page", App.Frame.Content != null ? App.Frame.Content.GetType().Name : null },
                { "current", current != null ? Entry(current) : null },
                { "position", Media.Position },
                { "queue", Media.Queue != null ? Media.Queue.Count : 0 },
            };
            PlayerPage player = PlayerPage.Current;
            if (player != null)
            {
                MediaElement element = player.Element;
                state.Add("player", new JsonObject
                {
                    { "state", element.CurrentState.ToString() },
                    { "positionMs", (long)element.Position.TotalMilliseconds },
                    { "durationMs", element.NaturalDuration.HasTimeSpan ? (long?)element.NaturalDuration.TimeSpan.TotalMilliseconds : null },
                    { "bufferingProgress", element.BufferingProgress },
                    { "error", player.Error },
                });
            }
            PicturePage picture = PicturePage.Current;
            if (picture != null)
            {
                state.Add("picture", new JsonObject
                {
                    { "width", picture.PictureSize.Width },
                    { "height", picture.PictureSize.Height },
                    { "status", picture.Status },
                });
            }
            state.Add("url", current != null ? current.Source.Locate(current).ToString() : null);
            return state;
        }

        private static JsonObject Entry(MediaEntry e)
        {
            return new JsonObject
            {
                { "id", e.Id },
                { "name", e.Name },
                { "kind", e.Kind.ToString() },
                { "size", e.Size },
                { "artist", e.Artist },
                { "album", e.Album },
                { "durationMs", e.Duration.HasValue ? (long?)e.Duration.Value.TotalMilliseconds : null },
                { "url", e.Url != null ? e.Url.ToString() : null },
            };
        }
    }
}
