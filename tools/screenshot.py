"""Saves what Silverdash360 shows as a PNG.

    python tools/screenshot.py 192.168.1.20
    python tools/screenshot.py 192.168.1.20 menu.png

The dashboard (a build with the debug server) draws its screen into a file
in its data folder (ui.screenshot), and this copies it off with the
dashboard's FTP server. The Guide, the console's notifications and video
the player shows itself aren't drawn.
"""

import argparse
import ftplib
import io
import os
import struct
import sys
import time
import zlib

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'tests'))
from debugclient import DebugClient, parse_address  # noqa: E402


class Ftp:
    """The dashboard's FTP server, started for the copy."""

    def __init__(self, client, host):
        server = client.call('ftp.start')
        self.ftp = ftplib.FTP()
        self.ftp.connect(host, server['port'], timeout=60)
        self.ftp.login(server['user'], server['password'])
        self.ftp.set_pasv(True)

    def read(self, path):
        data = io.BytesIO()
        self.ftp.retrbinary('RETR ' + path, data.write)
        return data.getvalue()

    def close(self):
        try:
            self.ftp.quit()
        except (OSError, ftplib.Error):
            self.ftp.close()


class Shot:
    """A screenshot the dashboard drew: its pixels, four bytes each, alpha,
    red, green and blue, premultiplied, as drawn on black."""

    def __init__(self, reply, argb):
        self.width = reply['width']
        self.height = reply['height']
        self.reply = reply
        if len(argb) != self.width * self.height * 4:
            raise ValueError('%s has %d bytes, not %d for %dx%d' % (
                reply['path'], len(argb), self.width * self.height * 4, self.width, self.height))
        # A plane a colour, alpha left out.
        self.planes = [argb[1::4], argb[2::4], argb[3::4]]

    def scaled(self, width, height):
        """Shrunk to three quarters (1280x720 to 960x540), each pixel the
        average of what it covers; any other size is refused."""
        if (width * 4, height * 4) != (self.width * 3, self.height * 3):
            raise ValueError('Only three quarters is supported: %dx%d to %dx%d' % (self.width, self.height, width, height))
        planes = [_down(_across(p), self.width * 3 // 4) for p in self.planes]
        shot = Shot.__new__(Shot)
        shot.width, shot.height, shot.reply, shot.planes = width, height, self.reply, planes
        return shot

    def png(self):
        rows = bytearray()
        stride = self.width
        r, g, b = self.planes
        for y in range(self.height):
            at = y * stride
            rgb = bytearray(stride * 3)
            rgb[0::3] = r[at:at + stride]
            rgb[1::3] = g[at:at + stride]
            rgb[2::3] = b[at:at + stride]
            rows.append(0)
            rows += rgb

        def chunk(kind, data):
            return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
        return (b'\x89PNG\r\n\x1a\n' +
                chunk(b'IHDR', struct.pack('>IIBBBBB', self.width, self.height, 8, 2, 0, 0, 0)) +
                chunk(b'IDAT', zlib.compress(bytes(rows), 9)) + chunk(b'IEND', b''))


# Four pixels into three: each new one covers a pixel and a third of the
# next, so it's a quarter and three quarters, or half and half, of two.
def _three_of_four(a, b, c, d):
    out = bytearray(len(a) * 3)
    out[0::3] = bytes((3 * x + y + 2) >> 2 for x, y in zip(a, b))
    out[1::3] = bytes((x + y + 1) >> 1 for x, y in zip(b, c))
    out[2::3] = bytes((x + 3 * y + 2) >> 2 for x, y in zip(c, d))
    return out


def _across(plane):
    # A row's width is a multiple of four, so the groups stay in their rows.
    return _three_of_four(plane[0::4], plane[1::4], plane[2::4], plane[3::4])


def _down(plane, width):
    rows = [plane[i:i + width] for i in range(0, len(plane), width)]
    out = bytearray()
    for i in range(0, len(rows), 4):
        a, b, c, d = rows[i:i + 4]
        three = _three_of_four(a, b, c, d)
        # _three_of_four interleaves; rows want to stay whole.
        out += three[0::3] + three[1::3] + three[2::3]
    return bytes(out)


def take(client, name=None):
    """Has the dashboard draw its screen; the reply names the file."""
    return client.call('ui.screenshot', *([name] if name else []))


def fetch(ftp, reply):
    return Shot(reply, ftp.read(reply['path']))


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    parser.add_argument('address', help='The console, host or host:port of the debug server.')
    parser.add_argument('out', nargs='?', help='The PNG to write (screenshot-<time>.png by default).')
    args = parser.parse_args()
    host, port = parse_address(args.address)
    client = DebugClient(host, port, timeout=60)
    reply = take(client)
    ftp = Ftp(client, host)
    try:
        shot = fetch(ftp, reply)
    except ValueError as e:
        sys.exit(str(e))
    finally:
        ftp.close()
    out = args.out or time.strftime('screenshot-%Y%m%d-%H%M%S.png')
    with open(out, 'wb') as f:
        f.write(shot.png())
    print('%s: %dx%d, drawn in %d ms, written in %d ms' % (out, shot.width, shot.height, reply['drawMs'], reply['ms']))


if __name__ == '__main__':
    main()
