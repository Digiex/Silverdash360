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


def png(width, height, argb):
    """A PNG of the pixels, four bytes each: alpha, red, green, blue. The
    colours are premultiplied, as drawn on black, so alpha is left out."""
    rows = bytearray()
    stride = width * 4
    for y in range(height):
        row = argb[y * stride:(y + 1) * stride]
        rows.append(0)
        rgb = bytearray(width * 3)
        rgb[0::3] = row[1::4]
        rgb[1::3] = row[2::4]
        rgb[2::3] = row[3::4]
        rows += rgb

    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0)) +
            chunk(b'IDAT', zlib.compress(bytes(rows), 6)) + chunk(b'IEND', b''))


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    parser.add_argument('address', help='The console, host or host:port of the debug server.')
    parser.add_argument('out', nargs='?', help='The PNG to write (screenshot-<time>.png by default).')
    args = parser.parse_args()
    host, port = parse_address(args.address)
    client = DebugClient(host, port, timeout=60)
    shot = client.call('ui.screenshot')
    server = client.call('ftp.start')
    ftp = ftplib.FTP()
    ftp.connect(host, server['port'], timeout=60)
    ftp.login(server['user'], server['password'])
    ftp.set_pasv(True)
    data = io.BytesIO()
    try:
        ftp.retrbinary('RETR ' + shot['path'], data.write)
    finally:
        ftp.quit()
    argb = data.getvalue()
    width, height = shot['width'], shot['height']
    if len(argb) != width * height * 4:
        sys.exit('%s has %d bytes, not %d for %dx%d' % (shot['path'], len(argb), width * height * 4, width, height))
    out = args.out or time.strftime('screenshot-%Y%m%d-%H%M%S.png')
    with open(out, 'wb') as f:
        f.write(png(width, height, argb))
    print('%s: %dx%d, drawn in %d ms, written in %d ms' % (out, width, height, shot['drawMs'], shot['ms']))


if __name__ == '__main__':
    main()
