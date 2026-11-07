using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Silverdash360.Core;
using Silverdash360.Native;
using Silverdash360.Services.Ftp;
using Silverlight360;

namespace Silverdash360.Services.Media
{
    /// <summary>
    /// A small HTTP/1.1 client on the console's plain sockets, for media
    /// servers on the local network (their descriptions and their SOAP
    /// actions) and the Xbox Marketplace's images and catalog. Servers are
    /// named by IPv4 address, as DLNA servers name themselves, or by a host
    /// name the console's DNS looks up. It runs on the calling thread only:
    /// the platform's own HttpWebRequest works on the UI thread, which a
    /// slow server then holds up.
    /// </summary>
    public static class Http
    {
        public const int Timeout = 15000;

        /// <summary>
        /// For testing, from the debug server's net.offline: how a request
        /// to the internet fails, as on a console without it. "dns": its
        /// name doesn't resolve, as with no network or no DNS server;
        /// "noroute": it goes to an address nothing answers, as when the
        /// router has no internet. Null for the real thing. Servers named by
        /// an address, as media servers on the network are, are left alone.
        /// </summary>
        public static string OfflineMode { get; set; }

        // What a request goes to instead under OfflineMode: a name the DNS
        // can't know (.invalid is reserved for that), and an address in a
        // private range that nothing on the network has.
        private const string NoSuchHost = "silverdash-offline.invalid";
        private const string NoRouteAddress = "10.255.255.1";

        /// <summary>The URL a request goes to under <see cref="OfflineMode"/>.</summary>
        public static Uri Offline(Uri url)
        {
            string mode = OfflineMode;
            if (mode == null || NativeSocket.ParseAddress(url.Host) != 0)
            {
                return url;
            }
            UriBuilder offline = new UriBuilder(url) { Host = mode == "dns" ? NoSuchHost : NoRouteAddress };
            return offline.Uri;
        }

        public sealed class Response : IDisposable
        {
            internal SocketReader Reader;

            public int Status;
            public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public string Header(string name)
            {
                string value;
                return Headers.TryGetValue(name, out value) ? value : null;
            }

            /// <summary>The body's length, or -1 when the server didn't say.</summary>
            public long ContentLength
            {
                get
                {
                    long length;
                    return long.TryParse(Header("Content-Length"), NumberStyles.Integer, CultureInfo.InvariantCulture, out length)
                        ? length : -1;
                }
            }

            /// <summary>The whole body as text, chunked or not.</summary>
            public string ReadText()
            {
                byte[] bytes = ReadBytes();
                return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
            }

            /// <summary>The whole body, chunked or not.</summary>
            public byte[] ReadBytes()
            {
                MemoryStream body = new MemoryStream();
                byte[] block = new byte[0x4000];
                if (string.Equals(Header("Transfer-Encoding"), "chunked", StringComparison.OrdinalIgnoreCase))
                {
                    while (true)
                    {
                        string size = Reader.ReadLine();
                        int semicolon = size.IndexOf(';');
                        int chunk = int.Parse(semicolon < 0 ? size.Trim() : size.Substring(0, semicolon).Trim(),
                                              NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        if (chunk == 0)
                        {
                            break;
                        }
                        Copy(body, block, chunk);
                        Reader.ReadLine();
                    }
                }
                else
                {
                    long length = ContentLength;
                    if (length >= 0)
                    {
                        Copy(body, block, length);
                    }
                    else
                    {
                        int got;
                        while ((got = Reader.Read(block, 0, block.Length)) > 0)
                        {
                            body.Write(block, 0, got);
                        }
                    }
                }
                return body.ToArray();
            }

            private void Copy(MemoryStream body, byte[] block, long count)
            {
                while (count > 0)
                {
                    int got = Reader.Read(block, 0, (int)Math.Min(block.Length, count));
                    if (got <= 0)
                    {
                        throw new IOException("The server closed the connection early.");
                    }
                    body.Write(block, 0, got);
                    count -= got;
                }
            }

            public void Dispose()
            {
                Reader.Dispose();
            }
        }

        /// <summary>Sends a request and reads the status line and headers; the body is left to read.</summary>
        public static Response Send(string method, Uri url, IDictionary<string, string> headers, byte[] body)
        {
            url = Offline(url);
            uint ip = NativeSocket.Resolve(url.Host);
            Activity.Art = "connecting to " + url.Host;
            NativeSocket socket = NativeSocket.Connect(ip, url.Port);
            SocketReader reader = new SocketReader(socket);
            try
            {
                StringBuilder request = new StringBuilder();
                // Silverlight 3's Uri has no PathAndQuery.
                request.Append(method).Append(' ').Append(url.AbsolutePath).Append(url.Query).Append(" HTTP/1.1\r\n");
                // Without the default port: content delivery networks can
                // refuse a host they don't know with it.
                request.Append("Host: ").Append(url.Host);
                if (url.Port != 80)
                {
                    request.Append(':').Append(url.Port);
                }
                request.Append("\r\n");
                request.Append("User-Agent: Silverdash360 UPnP/1.0 DLNADOC/1.50\r\n");
                request.Append("Connection: close\r\n");
                if (headers != null)
                {
                    foreach (KeyValuePair<string, string> header in headers)
                    {
                        request.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
                    }
                }
                if (body != null)
                {
                    request.Append("Content-Length: ").Append(body.Length).Append("\r\n");
                }
                request.Append("\r\n");
                socket.Send(request.ToString());
                if (body != null && body.Length > 0)
                {
                    using (NativeBuffer buffer = new NativeBuffer(body))
                    {
                        socket.Send(buffer, body.Length);
                    }
                }
                Response response = new Response { Reader = reader };
                string status = reader.ReadLine();
                string[] parts = status.Split(' ');
                if (parts.Length < 2 || !parts[0].StartsWith("HTTP/") || !int.TryParse(parts[1], out response.Status))
                {
                    throw new IOException("Not an HTTP response: " + status);
                }
                string line;
                while ((line = reader.ReadLine()).Length > 0)
                {
                    int colon = line.IndexOf(':');
                    if (colon > 0)
                    {
                        response.Headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
                    }
                }
                return response;
            }
            catch
            {
                reader.Dispose();
                throw;
            }
        }

        /// <summary>A GET's body as text; throws for a status other than 200.</summary>
        public static string GetText(Uri url)
        {
            using (Response response = Send("GET", url, null, null))
            {
                if (response.Status != 200)
                {
                    throw new IOException(url + " answered " + response.Status + ".");
                }
                return response.ReadText();
            }
        }
    }

    /// <summary>Reads lines and bytes from a socket through a buffer.</summary>
    internal sealed class SocketReader : IDisposable
    {
        private readonly NativeSocket socket;
        private readonly NativeBuffer buffer = new NativeBuffer(0x10000);
        private int start;
        private int end;

        public SocketReader(NativeSocket socket)
        {
            this.socket = socket;
        }

        private bool Fill()
        {
            if (!socket.WaitReadable(Http.Timeout))
            {
                throw new IOException("The server didn't answer.");
            }
            start = 0;
            end = socket.Receive(buffer, buffer.Length);
            return end > 0;
        }

        /// <summary>A line without its CRLF; throws at the end of the stream.</summary>
        public string ReadLine()
        {
            StringBuilder line = new StringBuilder();
            while (true)
            {
                if (start == end && !Fill())
                {
                    throw new IOException("The server closed the connection.");
                }
                int newline = Array.IndexOf(buffer.Bytes, (byte)'\n', start, end - start);
                int stop = newline < 0 ? end : newline;
                line.Append(Encoding.UTF8.GetString(buffer.Bytes, start, stop - start));
                if (newline >= 0)
                {
                    start = newline + 1;
                    return line.ToString().TrimEnd('\r');
                }
                start = end;
            }
        }

        /// <summary>Up to count bytes; 0 at the end of the stream.</summary>
        public int Read(byte[] target, int offset, int count)
        {
            if (start == end && !Fill())
            {
                return 0;
            }
            int take = Math.Min(count, end - start);
            Buffer.BlockCopy(buffer.Bytes, start, target, offset, take);
            start += take;
            return take;
        }

        public void Dispose()
        {
            socket.Dispose();
            buffer.Dispose();
        }
    }
}
