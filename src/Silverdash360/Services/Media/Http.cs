using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Silverdash360.Native;
using Silverdash360.Services.Ftp;

namespace Silverdash360.Services.Media
{
    /// <summary>
    /// A small HTTP/1.1 client on the console's plain sockets, for media
    /// servers on the local network: their descriptions and their SOAP
    /// actions. Servers are named by IPv4 address, as DLNA servers name
    /// themselves.
    /// </summary>
    public static class Http
    {
        public const int Timeout = 15000;

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
                byte[] bytes = body.ToArray();
                return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
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
            uint ip = NativeSocket.ParseAddress(url.Host);
            if (ip == 0)
            {
                throw new IOException("Only servers named by IP address can be reached: " + url.Host + ".");
            }
            NativeSocket socket = NativeSocket.Connect(ip, url.Port);
            SocketReader reader = new SocketReader(socket);
            try
            {
                StringBuilder request = new StringBuilder();
                // Silverlight 3's Uri has no PathAndQuery.
                request.Append(method).Append(' ').Append(url.AbsolutePath).Append(url.Query).Append(" HTTP/1.1\r\n");
                request.Append("Host: ").Append(url.Host).Append(':').Append(url.Port).Append("\r\n");
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
