"""A small DLNA media server, for testing the dashboard's media servers.

    python tests/dlna_server.py path/to/media
    python tests/dlna_server.py path/to/media --name "Test server" --port 8200

It answers SSDP searches for media servers, describes itself, lists the
folder's subfolders and media through the ContentDirectory Browse action, and
serves the files with byte ranges, as Windows Media Player, Plex or Kodi do.
Nothing else: no search, no transcoding, no thumbnails. Python 3, nothing to
install. Stop it with Ctrl+C.
"""

import argparse
import mimetypes
import os
import socket
import struct
import sys
import threading
import uuid
from html import escape
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import quote, unquote
from xml.etree import ElementTree

SSDP_GROUP = '239.255.255.250'
SSDP_PORT = 1900
MEDIA_SERVER = 'urn:schemas-upnp-org:device:MediaServer:1'
CONTENT_DIRECTORY = 'urn:schemas-upnp-org:service:ContentDirectory:1'

TYPES = {
    '.mp3': ('audio/mpeg', 'object.item.audioItem.musicTrack'),
    '.wma': ('audio/x-ms-wma', 'object.item.audioItem.musicTrack'),
    '.m4a': ('audio/mp4', 'object.item.audioItem.musicTrack'),
    '.wmv': ('video/x-ms-wmv', 'object.item.videoItem'),
    '.mp4': ('video/mp4', 'object.item.videoItem'),
    '.m4v': ('video/mp4', 'object.item.videoItem'),
    '.jpg': ('image/jpeg', 'object.item.imageItem.photo'),
    '.jpeg': ('image/jpeg', 'object.item.imageItem.photo'),
    '.png': ('image/png', 'object.item.imageItem.photo'),
}


def local_address_for(peer):
    """The address of this machine that a peer reaches it on."""
    probe = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        probe.connect((peer, 9))
        return probe.getsockname()[0]
    except OSError:
        return '127.0.0.1'
    finally:
        probe.close()


class Server:
    def __init__(self, root, name, port):
        self.root = os.path.abspath(root)
        self.name = name
        self.port = port
        self.udn = 'uuid:' + str(uuid.uuid5(uuid.NAMESPACE_URL, 'silverdash360-test:' + self.root))

    def path_of(self, object_id):
        """A folder or file for an object ID: "0" is the root, others are paths below it."""
        relative = '' if object_id == '0' else object_id
        path = os.path.normpath(os.path.join(self.root, relative))
        if path != self.root and not path.startswith(self.root + os.sep):
            raise ValueError('outside the folder')
        return path

    def didl(self, object_id, host, start, count):
        folder = self.path_of(object_id)
        names = sorted(os.listdir(folder), key=str.lower)
        children = [n for n in names if os.path.isdir(os.path.join(folder, n))]
        children += [n for n in names if os.path.splitext(n)[1].lower() in TYPES]
        total = len(children)
        parts = ['<DIDL-Lite xmlns="urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/" '
                 'xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:upnp="urn:schemas-upnp-org:metadata-1-0/upnp/">']
        for name in children[start:start + count if count else None]:
            path = os.path.join(folder, name)
            child_id = os.path.relpath(path, self.root).replace(os.sep, '/')
            if os.path.isdir(path):
                parts.append('<container id="%s" parentID="%s" restricted="1" childCount="%d"><dc:title>%s</dc:title>'
                             '<upnp:class>object.container.storageFolder</upnp:class></container>' % (
                                 escape(child_id), escape(object_id), len(os.listdir(path)), escape(name)))
                continue
            mime, upnp_class = TYPES[os.path.splitext(name)[1].lower()]
            url = 'http://%s:%d/media/%s' % (host, self.port, quote(child_id))
            parts.append('<item id="%s" parentID="%s" restricted="1"><dc:title>%s</dc:title><upnp:class>%s</upnp:class>'
                         '<upnp:artist>Silverdash360 tests</upnp:artist>'
                         '<res protocolInfo="http-get:*:%s:DLNA.ORG_OP=01" size="%d">%s</res></item>' % (
                             escape(child_id), escape(object_id), escape(os.path.splitext(name)[0]), upnp_class, mime,
                             os.path.getsize(path), escape(url)))
        parts.append('</DIDL-Lite>')
        return ''.join(parts), len(children[start:start + count if count else None]), total

    def description(self):
        return ('<?xml version="1.0"?><root xmlns="urn:schemas-upnp-org:device-1-0"><specVersion><major>1</major><minor>0</minor>'
                '</specVersion><device><deviceType>%s</deviceType><friendlyName>%s</friendlyName>'
                '<manufacturer>Silverdash360</manufacturer><modelName>Test server</modelName><UDN>%s</UDN>'
                '<serviceList><service><serviceType>%s</serviceType><serviceId>urn:upnp-org:serviceId:ContentDirectory</serviceId>'
                '<SCPDURL>/cds.xml</SCPDURL><controlURL>/cds/control</controlURL><eventSubURL>/cds/event</eventSubURL></service>'
                '</serviceList></device></root>' % (MEDIA_SERVER, escape(self.name), self.udn, CONTENT_DIRECTORY))

    def ssdp(self):
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM, socket.IPPROTO_UDP)
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        sock.bind(('', SSDP_PORT))
        membership = struct.pack('4s4s', socket.inet_aton(SSDP_GROUP), socket.inet_aton('0.0.0.0'))
        sock.setsockopt(socket.IPPROTO_IP, socket.IP_ADD_MEMBERSHIP, membership)
        while True:
            data, peer = sock.recvfrom(4096)
            text = data.decode('utf-8', 'replace')
            if not text.startswith('M-SEARCH'):
                continue
            target = ''
            for line in text.split('\r\n'):
                if line.lower().startswith('st:'):
                    target = line[3:].strip()
            if target not in ('ssdp:all', 'upnp:rootdevice', MEDIA_SERVER):
                continue
            host = local_address_for(peer[0])
            answer = ('HTTP/1.1 200 OK\r\nCACHE-CONTROL: max-age=1800\r\nEXT:\r\n'
                      'LOCATION: http://%s:%d/description.xml\r\nSERVER: Python UPnP/1.0 Silverdash360-test/1.0\r\n'
                      'ST: %s\r\nUSN: %s::%s\r\n\r\n' % (host, self.port, MEDIA_SERVER, self.udn, MEDIA_SERVER))
            sock.sendto(answer.encode('utf-8'), peer)
            print('SSDP search from %s:%d, answered with %s' % (peer[0], peer[1], host), flush=True)


def handler_for(server):
    class Handler(BaseHTTPRequestHandler):
        protocol_version = 'HTTP/1.1'

        def log_message(self, fmt, *args):
            print('%s %s' % (self.address_string(), fmt % args), flush=True)

        def send_text(self, text, content_type='text/xml; charset="utf-8"', status=200):
            body = text.encode('utf-8')
            self.send_response(status)
            self.send_header('Content-Type', content_type)
            self.send_header('Content-Length', str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            if self.path == '/description.xml':
                self.send_text(server.description())
            elif self.path.startswith('/media/'):
                self.send_media(unquote(self.path[len('/media/'):]))
            else:
                self.send_error(404)

        def do_POST(self):
            if self.path != '/cds/control':
                self.send_error(404)
                return
            body = self.rfile.read(int(self.headers.get('Content-Length', 0))).decode('utf-8')
            fields = {e.tag.split('}')[-1]: (e.text or '') for e in ElementTree.fromstring(body).iter()}
            host = self.headers.get('Host', '127.0.0.1').split(':')[0]
            try:
                result, returned, total = server.didl(fields.get('ObjectID', '0'), host,
                                                      int(fields.get('StartingIndex') or 0), int(fields.get('RequestedCount') or 0))
            except (OSError, ValueError):
                self.send_text('<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><s:Fault>'
                               '<faultstring>UPnPError</faultstring></s:Fault></s:Body></s:Envelope>', status=500)
                return
            self.send_text('<?xml version="1.0"?><s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/" '
                           's:encodingStyle="http://schemas.xmlsoap.org/soap/encoding/"><s:Body>'
                           '<u:BrowseResponse xmlns:u="%s"><Result>%s</Result><NumberReturned>%d</NumberReturned>'
                           '<TotalMatches>%d</TotalMatches><UpdateID>1</UpdateID></u:BrowseResponse></s:Body></s:Envelope>' % (
                               CONTENT_DIRECTORY, escape(result), returned, total))

        def send_media(self, object_id):
            try:
                path = server.path_of(object_id)
                size = os.path.getsize(path)
            except (OSError, ValueError):
                self.send_error(404)
                return
            start, end = 0, size - 1
            ranged = self.headers.get('Range', '')
            if ranged.startswith('bytes='):
                first, _, last = ranged[6:].partition('-')
                start = int(first) if first else 0
                end = int(last) if last else size - 1
            if start >= size:
                self.send_response(416)
                self.send_header('Content-Range', 'bytes */%d' % size)
                self.send_header('Content-Length', '0')
                self.end_headers()
                return
            self.send_response(206 if ranged else 200)
            self.send_header('Content-Type', mimetypes.guess_type(path)[0] or TYPES.get(os.path.splitext(path)[1].lower(), ('application/octet-stream',))[0])
            self.send_header('Content-Length', str(end - start + 1))
            self.send_header('Accept-Ranges', 'bytes')
            if ranged:
                self.send_header('Content-Range', 'bytes %d-%d/%d' % (start, end, size))
            self.end_headers()
            with open(path, 'rb') as f:
                f.seek(start)
                left = end - start + 1
                try:
                    while left > 0:
                        block = f.read(min(0x10000, left))
                        if not block:
                            break
                        self.wfile.write(block)
                        left -= len(block)
                except (ConnectionError, OSError):
                    pass  # The player moved on.

    return Handler


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('folder', help='The folder of media to serve.')
    parser.add_argument('--name', default='Silverdash360 test server', help='The name the dashboard shows.')
    parser.add_argument('--port', type=int, default=8200, help='The HTTP port.')
    args = parser.parse_args()
    if not os.path.isdir(args.folder):
        parser.error('No folder %s.' % args.folder)
    server = Server(args.folder, args.name, args.port)
    threading.Thread(target=server.ssdp, daemon=True).start()
    http = ThreadingHTTPServer(('', args.port), handler_for(server))
    print('Serving %s as "%s" on port %d' % (server.root, args.name, args.port), flush=True)
    try:
        http.serve_forever()
    except KeyboardInterrupt:
        pass
    return 0


if __name__ == '__main__':
    sys.exit(main())
