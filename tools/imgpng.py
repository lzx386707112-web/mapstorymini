#!/usr/bin/env python3
"""Export a Canvas node from an IMG to PNG (read-only), scaled up for inspection.

  imgpng.py <img-relative-to-Data> <node/path> <out.png> [--scale N]
"""
import argparse
import struct
import sys
import zlib
from pathlib import Path

WZPY = Path('/Users/lizixian/Documents/mxd/Beidou-Server/tool/wz-python')
DEFAULT_DATA = Path('/Users/lizixian/Documents/mxd/mxd-android/MapleDashResources/Data')

sys.path.insert(0, str(WZPY))
from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.canvas import decode_canvas  # noqa: E402
from wzpy.properties import WzCanvasProperty  # noqa: E402


def png(path, width, height, rgba, scale=1):
    rows = []
    for y in range(height):
        row = bytearray()
        for _ in range(scale):
            row = bytearray()
            break
    raw = bytearray()
    for y in range(height):
        line = bytearray()
        for x in range(width):
            i = (y * width + x) * 4
            px = rgba[i:i + 4]
            line += px * scale
        for _ in range(scale):
            raw += b'\x00' + line
    def chunk(tag, data):
        c = struct.pack('>I', len(data)) + tag + data
        return c + struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF)
    out = b'\x89PNG\r\n\x1a\n'
    out += chunk(b'IHDR', struct.pack('>IIBBBBB', width * scale, height * scale, 8, 6, 0, 0, 0))
    out += chunk(b'IDAT', zlib.compress(bytes(raw), 9))
    out += chunk(b'IEND', b'')
    Path(path).write_bytes(out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('img')
    ap.add_argument('node')
    ap.add_argument('out')
    ap.add_argument('--scale', type=int, default=2)
    ap.add_argument('--data', type=Path, default=DEFAULT_DATA)
    args = ap.parse_args()
    img = WzImage.from_bytes((args.data / args.img).read_bytes(), key=WzKey.for_region('GMS'), name=args.img)
    img.parse()
    node = img.get(args.node)
    if node is None or not isinstance(node, WzCanvasProperty):
        print('NOT A CANVAS', args.node)
        return 1
    w, h = node.width, node.height
    rgba = decode_canvas(node)
    if hasattr(rgba, 'convert'):
        rgba = rgba.convert('RGBA')
    if not isinstance(rgba, (bytes, bytearray)):
        rgba = rgba.tobytes()
    png(args.out, w, h, rgba, args.scale)
    print('%s %dx%d -> %s' % (args.node, w, h, args.out))
    return 0


if __name__ == '__main__':
    sys.exit(main())
