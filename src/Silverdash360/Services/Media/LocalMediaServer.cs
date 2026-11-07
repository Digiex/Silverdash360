using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Silverdash360.Core;
using Silverdash360.Native;
using Silverdash360.Services.Files;
using Silverdash360.Services.Ftp;
using Silverlight360;

namespace Silverdash360.Services.Media
{
    /// <summary>
    /// Serves files on the console's drives over HTTP, for the media player:
    /// the platform's MediaElement plays only from a URI (SetSource with a
    /// stream isn't implemented on the console), so a file on a drive is
    /// given to it as http://127.0.0.1:port/token/path. Byte ranges are
    /// served, so the player can seek. Each URL carries a token made up at
    /// start, so another machine can't read the drives through it.
    /// </summary>
    public sealed class LocalMediaServer
    {
        public const int Port = 4602;
        private const int BlockSize = 0x10000;

        private static readonly Dictionary<string, string> Types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".mp3", "audio/mpeg" }, { ".wma", "audio/x-ms-wma" }, { ".m4a", "audio/mp4" },
            { ".wmv", "video/x-ms-wmv" }, { ".asf", "video/x-ms-asf" }, { ".mp4", "video/mp4" },
            { ".m4v", "video/mp4" }, { ".mov", "video/quicktime" },
            { ".jpg", "image/jpeg" }, { ".jpeg", "image/jpeg" }, { ".png", "image/png" },
        };

        private readonly XboxFileSystem files;
        private readonly object sync = new object();
        private readonly string token;
        private NativeSocket listener;

        public LocalMediaServer(XboxFileSystem files)
        {
            this.files = files;
            Random random = new Random();
            token = random.Next(0x10000000, int.MaxValue).ToString("x8") + random.Next(0x10000000, int.MaxValue).ToString("x8");
            Host = "127.0.0.1";
        }

        /// <summary>
        /// The address the player is pointed at: the loopback address, or the
        /// console's own address where loopback doesn't reach the server.
        /// </summary>
        public string Host { get; set; }

        /// <summary>The URL of a file, a path in the file system's tree; starts the server if need be.</summary>
        public Uri UrlFor(string path)
        {
            Start();
            StringBuilder url = new StringBuilder("http://").Append(Host).Append(':').Append(Port).Append('/').Append(token);
            foreach (string part in path.Split('/'))
            {
                if (part.Length > 0)
                {
                    url.Append('/').Append(Uri.EscapeDataString(part));
                }
            }
            return new Uri(url.ToString(), UriKind.Absolute);
        }

        /// <summary>
        /// A URL for media on another machine, such as a media server's: the
        /// player is pointed here and the server fetches it. The console's
        /// player won't play from another machine on the network itself (it
        /// fails at once with 4001), only from the console.
        /// </summary>
        public Uri RelayFor(Uri remote)
        {
            Start();
            // The remote URL in hex as one path segment (escaped, its slashes
            // could come back as slashes), then its file name: the player
            // goes by the name's extension.
            string name = remote.AbsolutePath.Substring(remote.AbsolutePath.LastIndexOf('/') + 1);
            StringBuilder hex = new StringBuilder();
            foreach (byte b in Encoding.UTF8.GetBytes(remote.ToString()))
            {
                hex.Append(b.ToString("x2"));
            }
            return new Uri("http://" + Host + ":" + Port + "/" + token + RelayPrefix + hex + "/" +
                           Uri.EscapeDataString(name.Length > 0 ? name : "media"), UriKind.Absolute);
        }

        private const string RelayPrefix = "/relay/";

        private static string FromHex(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
        }

        private void Start()
        {
            lock (sync)
            {
                if (listener != null)
                {
                    return;
                }
                listener = NativeSocket.Listen(Port, 4);
            }
            new Thread(Accept) { IsBackground = true, Name = "Media server" }.Start();
            Log.Write("Media server listening on port {0}", Port);
        }

        private void Accept()
        {
            while (true)
            {
                NativeSocket client;
                try
                {
                    client = listener.Accept();
                }
                catch (Exception e)
                {
                    Log.Write("Media server stopped: {0}", e.Message);
                    lock (sync)
                    {
                        listener = null;
                    }
                    return;
                }
                new Thread(Serve) { IsBackground = true, Name = "Media request" }.Start(client);
            }
        }

        private void Serve(object state)
        {
            NativeSocket client = (NativeSocket)state;
            SocketReader reader = new SocketReader(client);
            try
            {
                string request = reader.ReadLine();
                Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string line;
                while ((line = reader.ReadLine()).Length > 0)
                {
                    int colon = line.IndexOf(':');
                    if (colon > 0)
                    {
                        headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
                    }
                }
                string[] parts = request.Split(' ');
                if (parts.Length < 2 || (parts[0] != "GET" && parts[0] != "HEAD"))
                {
                    Reply(client, "405 Method Not Allowed", null);
                    return;
                }
                if (parts[1].StartsWith("/" + token + RelayPrefix))
                {
                    string asked;
                    headers.TryGetValue("Range", out asked);
                    string hex = parts[1].Substring(token.Length + 1 + RelayPrefix.Length);
                    int slash = hex.IndexOf('/');
                    Relay(client, new Uri(FromHex(slash >= 0 ? hex.Substring(0, slash) : hex), UriKind.Absolute), parts[0], asked);
                    return;
                }
                string path = PathOf(parts[1]);
                FileEntry entry = path != null ? files.Stat(path) : null;
                if (entry == null || entry.IsDirectory)
                {
                    Reply(client, "404 Not Found", null);
                    return;
                }
                string range;
                headers.TryGetValue("Range", out range);
                Send(client, path, entry.Size, range, parts[0] == "HEAD");
            }
            catch (Exception e)
            {
                // A player that has what it wants closes the connection.
                Log.Write("Media request ended: {0}", e is IOException ? e.Message : e.ToString());
            }
            finally
            {
                reader.Dispose();
            }
        }

        // The file a request's path names, or null for one without the token.
        private string PathOf(string target)
        {
            string prefix = "/" + token + "/";
            if (!target.StartsWith(prefix))
            {
                return null;
            }
            StringBuilder path = new StringBuilder();
            foreach (string part in target.Substring(prefix.Length).Split('?')[0].Split('/'))
            {
                string name = Uri.UnescapeDataString(part);
                if (name.Length == 0 || name == "." || name == "..")
                {
                    continue;
                }
                path.Append('/').Append(name);
            }
            return path.Length > 0 ? path.ToString() : null;
        }

        private void Send(NativeSocket client, string path, long size, string range, bool headOnly)
        {
            long start = 0, end = size - 1;
            bool partial = false;
            if (range != null && range.StartsWith("bytes="))
            {
                string[] bounds = range.Substring(6).Split(',')[0].Split('-');
                long value;
                if (bounds[0].Length > 0 && long.TryParse(bounds[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                {
                    start = value;
                    if (bounds.Length > 1 && long.TryParse(bounds[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                    {
                        end = Math.Min(value, size - 1);
                    }
                }
                else if (bounds.Length > 1 && long.TryParse(bounds[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                {
                    // bytes=-n: the last n bytes.
                    start = Math.Max(0, size - value);
                }
                partial = true;
            }
            if (start >= size && size > 0)
            {
                Reply(client, "416 Range Not Satisfiable", "Content-Range: bytes */" + size + "\r\n");
                return;
            }
            string type;
            int dot = path.LastIndexOf('.');
            if (dot < 0 || !Types.TryGetValue(path.Substring(dot), out type))
            {
                type = "application/octet-stream";
            }
            StringBuilder header = new StringBuilder();
            header.Append("HTTP/1.1 ").Append(partial ? "206 Partial Content" : "200 OK").Append("\r\n");
            header.Append("Content-Type: ").Append(type).Append("\r\n");
            header.Append("Content-Length: ").Append(end - start + 1).Append("\r\n");
            header.Append("Accept-Ranges: bytes\r\n");
            if (partial)
            {
                header.Append("Content-Range: bytes ").Append(start).Append('-').Append(end).Append('/').Append(size).Append("\r\n");
            }
            header.Append("Connection: close\r\n\r\n");
            client.Send(header.ToString());
            if (headOnly)
            {
                return;
            }
            // Straight from the drive to the socket through one pinned buffer.
            using (KernelFile file = files.OpenRead(path))
            using (NativeBuffer block = new NativeBuffer(BlockSize))
            {
                long at = start;
                while (at <= end)
                {
                    int read = file.Read(block, (int)Math.Min(BlockSize, end - at + 1), at);
                    if (read <= 0)
                    {
                        break;
                    }
                    client.Send(block, read);
                    at += read;
                }
            }
        }

        // Fetches the remote media with the player's range and hands the
        // answer over as it comes.
        private static void Relay(NativeSocket client, Uri remote, string method, string range)
        {
            Dictionary<string, string> asked = new Dictionary<string, string>();
            if (range != null)
            {
                asked["Range"] = range;
            }
            using (Http.Response response = Http.Send(method, remote, asked, null))
            {
                StringBuilder header = new StringBuilder();
                header.Append("HTTP/1.1 ").Append(response.Status).Append(' ').Append(StatusText(response.Status)).Append("\r\n");
                foreach (string name in RelayedHeaders)
                {
                    string value = response.Header(name);
                    if (value != null)
                    {
                        header.Append(name).Append(": ").Append(value).Append("\r\n");
                    }
                }
                header.Append("Connection: close\r\n\r\n");
                client.Send(header.ToString());
                if (method == "HEAD")
                {
                    return;
                }
                long left = response.ContentLength;
                using (NativeBuffer block = new NativeBuffer(BlockSize))
                {
                    while (left != 0)
                    {
                        int want = left < 0 ? BlockSize : (int)Math.Min(BlockSize, left);
                        int got = response.Reader.Read(block.Bytes, 0, want);
                        if (got <= 0)
                        {
                            break;
                        }
                        client.Send(block, got);
                        if (left > 0)
                        {
                            left -= got;
                        }
                    }
                }
            }
        }

        private static readonly string[] RelayedHeaders =
        {
            "Content-Type", "Content-Length", "Content-Range", "Accept-Ranges", "Transfer-Encoding",
        };

        private static string StatusText(int status)
        {
            switch (status)
            {
                case 200: return "OK";
                case 206: return "Partial Content";
                case 404: return "Not Found";
                case 416: return "Range Not Satisfiable";
                default: return "Status";
            }
        }

        private static void Reply(NativeSocket client, string status, string headers)
        {
            client.Send("HTTP/1.1 " + status + "\r\n" + (headers ?? "") + "Content-Length: 0\r\nConnection: close\r\n\r\n");
        }
    }
}
