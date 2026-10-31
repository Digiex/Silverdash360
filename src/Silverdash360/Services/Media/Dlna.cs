using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Silverdash360.Core;
using Silverdash360.Native;
using Silverdash360.Services.Ftp;

namespace Silverdash360.Services.Media
{
    /// <summary>
    /// Finds DLNA and UPnP media servers on the local network: an SSDP
    /// M-SEARCH to the multicast group, and the LOCATION of each answer.
    /// </summary>
    public static class Ssdp
    {
        private const uint Group = 0xEFFFFFFA; // 239.255.255.250
        private const int Port = 1900;
        public const string MediaServer = "urn:schemas-upnp-org:device:MediaServer:1";

        /// <summary>The description URLs of the media servers that answer within the time.</summary>
        public static List<Uri> Search(int milliseconds)
        {
            List<Uri> found = new List<Uri>();
            foreach (string answer in Ask(MediaServer, milliseconds, null))
            {
                Uri location = Location(answer);
                if (location != null && !found.Contains(location))
                {
                    found.Add(location);
                }
            }
            return found;
        }

        /// <summary>
        /// Every answer to an M-SEARCH for a search target (ssdp:all for any
        /// device) within the time; from, when given, hears who answered.
        /// </summary>
        public static List<string> Ask(string target, int milliseconds, Action<uint, int> from)
        {
            List<string> answers = new List<string>();
            using (NativeSocket socket = NativeSocket.Udp())
            using (NativeBuffer buffer = new NativeBuffer(0x2000))
            {
                byte[] search = Encoding.UTF8.GetBytes(
                    "M-SEARCH * HTTP/1.1\r\n" +
                    "HOST: 239.255.255.250:1900\r\n" +
                    "MAN: \"ssdp:discover\"\r\n" +
                    "MX: 2\r\n" +
                    "ST: " + target + "\r\n" +
                    "USER-AGENT: Silverdash360 UPnP/1.0 DLNADOC/1.50\r\n\r\n");
                // Twice: a datagram can go missing.
                socket.SendTo(search, Group, Port);
                socket.SendTo(search, Group, Port);
                int deadline = Environment.TickCount + milliseconds;
                while (true)
                {
                    int left = deadline - Environment.TickCount;
                    if (left <= 0 || !socket.WaitReadable(left))
                    {
                        break;
                    }
                    uint ip;
                    int port;
                    int received = socket.ReceiveFrom(buffer, out ip, out port);
                    if (from != null)
                    {
                        from(ip, port);
                    }
                    answers.Add(Encoding.UTF8.GetString(buffer.Bytes, 0, received));
                }
            }
            return answers;
        }

        private static Uri Location(string answer)
        {
            foreach (string line in answer.Split('\n'))
            {
                int colon = line.IndexOf(':');
                if (colon > 0 && string.Equals(line.Substring(0, colon).Trim(), "LOCATION", StringComparison.OrdinalIgnoreCase))
                {
                    Uri uri;
                    return Uri.TryCreate(line.Substring(colon + 1).Trim(), UriKind.Absolute, out uri) ? uri : null;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// A media server's ContentDirectory, browsed with SOAP Browse actions;
    /// its folders and media come back as DIDL-Lite.
    /// </summary>
    public sealed class DlnaMediaSource : IMediaSource
    {
        private const int PageSize = 200;
        private const int MaxEntries = 5000;
        private const string ContentDirectory = "urn:schemas-upnp-org:service:ContentDirectory:1";

        private static readonly XNamespace Device = "urn:schemas-upnp-org:device-1-0";
        private static readonly XNamespace Didl = "urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/";
        private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
        private static readonly XNamespace Upnp = "urn:schemas-upnp-org:metadata-1-0/upnp/";

        private readonly Uri control;

        private DlnaMediaSource(Uri location, string name, string udn, Uri control)
        {
            Location = location;
            Name = name;
            Udn = udn;
            this.control = control;
        }

        public Uri Location { get; private set; }
        public string Udn { get; private set; }
        public string Key { get { return "dlna:" + Udn; } }
        public string Name { get; private set; }
        public string Description { get { return "Media server, " + Location.Host; } }

        /// <summary>Reads a server's description; null when it has no ContentDirectory.</summary>
        public static DlnaMediaSource Describe(Uri location)
        {
            XDocument description = XDocument.Parse(Http.GetText(location));
            XElement root = description.Root;
            XElement device = root.Element(Device + "device");
            if (device == null)
            {
                return null;
            }
            XElement service = device.Descendants(Device + "service")
                .FirstOrDefault(s => ((string)s.Element(Device + "serviceType") ?? "").StartsWith("urn:schemas-upnp-org:service:ContentDirectory:"));
            string controlUrl = service != null ? (string)service.Element(Device + "controlURL") : null;
            if (controlUrl == null)
            {
                return null;
            }
            string urlBase = (string)root.Element(Device + "URLBase");
            Uri baseUri = urlBase != null ? new Uri(urlBase, UriKind.Absolute) : location;
            string name = (string)device.Element(Device + "friendlyName") ?? location.Host;
            string udn = (string)device.Element(Device + "UDN") ?? location.ToString();
            return new DlnaMediaSource(location, name.Trim(), udn.Trim(), new Uri(baseUri, controlUrl.Trim()));
        }

        public List<MediaEntry> List(MediaEntry folder)
        {
            string id = folder != null ? folder.Id : "0";
            List<MediaEntry> entries = new List<MediaEntry>();
            int start = 0;
            while (entries.Count < MaxEntries)
            {
                int returned, total;
                string didl = Browse(id, start, out returned, out total);
                Parse(didl, entries);
                start += returned;
                if (returned == 0 || start >= total)
                {
                    break;
                }
            }
            // The server's order is kept: albums list tracks in track order.
            return entries;
        }

        /// <summary>The server's own URL: the player fetches from it directly.</summary>
        public Uri Locate(MediaEntry entry)
        {
            return entry.Url;
        }

        private string Browse(string id, int start, out int returned, out int total)
        {
            string body =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                "<s:Body><u:Browse xmlns:u=\"" + ContentDirectory + "\">" +
                "<ObjectID>" + Escape(id) + "</ObjectID>" +
                "<BrowseFlag>BrowseDirectChildren</BrowseFlag>" +
                "<Filter>*</Filter>" +
                "<StartingIndex>" + start.ToString(CultureInfo.InvariantCulture) + "</StartingIndex>" +
                "<RequestedCount>" + PageSize.ToString(CultureInfo.InvariantCulture) + "</RequestedCount>" +
                "<SortCriteria></SortCriteria>" +
                "</u:Browse></s:Body></s:Envelope>";
            Dictionary<string, string> headers = new Dictionary<string, string>
            {
                { "Content-Type", "text/xml; charset=\"utf-8\"" },
                { "SOAPACTION", "\"" + ContentDirectory + "#Browse\"" },
            };
            string text;
            int status;
            using (Http.Response response = Http.Send("POST", control, headers, Encoding.UTF8.GetBytes(body)))
            {
                status = response.Status;
                text = response.ReadText();
            }
            if (status != 200)
            {
                throw new IOException(Name + " refused to list a folder (" + status + ").");
            }
            XDocument answer = XDocument.Parse(text);
            XElement result = answer.Descendants().FirstOrDefault(e => e.Name.LocalName == "Result");
            returned = Number(answer, "NumberReturned");
            total = Number(answer, "TotalMatches");
            return result != null ? result.Value : "";
        }

        private static int Number(XDocument answer, string name)
        {
            XElement element = answer.Descendants().FirstOrDefault(e => e.Name.LocalName == name);
            int value;
            return element != null && int.TryParse(element.Value, out value) ? value : 0;
        }

        private void Parse(string didl, List<MediaEntry> entries)
        {
            if (didl.Length == 0)
            {
                return;
            }
            XElement root = XDocument.Parse(didl).Root;
            foreach (XElement node in root.Elements())
            {
                string id = (string)node.Attribute("id");
                string title = (string)node.Element(Dc + "title") ?? "";
                if (node.Name == Didl + "container")
                {
                    entries.Add(new MediaEntry { Source = this, Id = id, Name = title, Kind = MediaKind.Folder });
                    continue;
                }
                if (node.Name != Didl + "item")
                {
                    continue;
                }
                MediaEntry item = Item(node, id, title);
                if (item != null)
                {
                    entries.Add(item);
                }
            }
        }

        // An item with a resource the console plays, or null.
        private MediaEntry Item(XElement node, string id, string title)
        {
            string upnpClass = (string)node.Element(Upnp + "class") ?? "";
            foreach (XElement res in node.Elements(Didl + "res"))
            {
                // protocolInfo: protocol:network:contentFormat:additionalInfo
                string[] info = ((string)res.Attribute("protocolInfo") ?? "").Split(':');
                if (info.Length < 3 || info[0] != "http-get")
                {
                    continue;
                }
                MediaKind? kind = MediaFormats.OfType(info[2]);
                Uri url;
                if (!kind.HasValue || !Uri.TryCreate(res.Value.Trim(), UriKind.Absolute, out url))
                {
                    continue;
                }
                MediaEntry entry = new MediaEntry
                {
                    Source = this, Id = id, Name = title, Kind = kind.Value, Url = url,
                    Artist = (string)node.Element(Upnp + "artist") ?? (string)node.Element(Dc + "creator"),
                    Album = (string)node.Element(Upnp + "album"),
                };
                long size;
                if (long.TryParse((string)res.Attribute("size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out size))
                {
                    entry.Size = size;
                }
                TimeSpan duration;
                string durationText = (string)res.Attribute("duration");
                if (durationText != null && TimeSpan.TryParse(durationText.Split('.')[0], out duration))
                {
                    entry.Duration = duration;
                }
                Uri art;
                if (Uri.TryCreate((string)node.Element(Upnp + "albumArtURI") ?? "", UriKind.Absolute, out art))
                {
                    entry.ArtUrl = art;
                }
                return entry;
            }
            if (upnpClass.Length > 0)
            {
                Log.Write("DLNA: nothing playable for {0} ({1})", title, upnpClass);
            }
            return null;
        }

        private static string Escape(string text)
        {
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
