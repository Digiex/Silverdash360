"""Strips the title out of an executable's header, as homebrew executables have none.

    python tools/strip-xex.py src/Silverdash360/bin/Release/xbox/default.xex

The package's default.xex is the donor app's, so it declares the donor's
title: its title ID, version and media ID (the execution info header). Signed
in to Live, the console checks for an update to that title when it starts,
and a title started again within seconds of starting got the donor's title
update installed every time. Without the execution info the console has no
title to check. The dashboard keeps its files in its own folder instead of
the title's storage (Services/AppStorage.cs).

The header is rewritten in place: the execution info's entry goes from the
header directory and the header digest is computed again. The digest is
SHA-1 over the header from the end of the security info's fixed part to the
image, then from the start of the file to the security info's signature.
Running it again on a stripped file changes nothing. Tools/Build.ps1 runs it
on the package after each build; the SDK's donor copy is left as it is.
"""

import hashlib
import struct
import sys

MAGIC = b'XEX2'
EXECUTION_INFO = 0x00040006
# In the security info: the header digest, and where the digested part of it starts.
HEADER_DIGEST = 0x164
DIGESTED_FROM = 0x17C
SIGNED_FROM = 8


def header_digest(data, security, image):
    sha = hashlib.sha1()
    sha.update(data[security + DIGESTED_FROM:image])
    sha.update(data[:security + SIGNED_FROM])
    return sha.digest()


def strip(data):
    """The header without the execution info; None when it has none."""
    data = bytearray(data)
    magic, _, image, _, security, count = struct.unpack_from('>4sIIIII', data, 0)
    if magic != MAGIC:
        raise ValueError('not an XEX2 executable')
    if bytes(data[security + HEADER_DIGEST:security + HEADER_DIGEST + 20]) != header_digest(data, security, image):
        raise ValueError("the header digest doesn't match the header")
    entries = [struct.unpack_from('>II', data, 24 + 8 * i) for i in range(count)]
    kept = [e for e in entries if e[0] != EXECUTION_INFO]
    if len(kept) == len(entries):
        return None
    for key, value in entries:
        if key == EXECUTION_INFO:
            # Its data, 24 bytes, goes too; nothing else points into it.
            data[value:value + 24] = bytes(24)
    struct.pack_into('>I', data, 20, len(kept))
    for i, (key, value) in enumerate(kept):
        struct.pack_into('>II', data, 24 + 8 * i, key, value)
    data[24 + 8 * len(kept):24 + 8 * count] = bytes(8 * (count - len(kept)))
    data[security + HEADER_DIGEST:security + HEADER_DIGEST + 20] = header_digest(data, security, image)
    return bytes(data)


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    path = sys.argv[1]
    with open(path, 'rb') as f:
        data = f.read()
    stripped = strip(data)
    if stripped is None:
        print('%s declares no title' % path)
        return
    with open(path, 'wb') as f:
        f.write(stripped)
    print('Stripped the title from %s' % path)


if __name__ == '__main__':
    main()
