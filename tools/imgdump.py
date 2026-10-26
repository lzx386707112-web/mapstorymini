#!/usr/bin/env python3
"""Dump IMG node trees via the source client's wzpy oracle (read-only).

  imgdump.py <img-relative-to-Data> [node/path] [--depth N] [--data DIR]

Prints canvas size and origin, numeric/string values, UOL targets.
"""
import argparse
import sys
from pathlib import Path

WZPY = Path('/Users/lizixian/Documents/mxd/Beidou-Server/tool/wz-python')
DEFAULT_DATA = Path('/Users/lizixian/Documents/mxd/mxd-android/MapleDashResources/Data')

sys.path.insert(0, str(WZPY))
from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzCanvasProperty, WzUolProperty  # noqa: E402


def describe(node):
    kind = type(node).__name__
    extra = ''
    if isinstance(node, WzCanvasProperty):
        origin = node.get('origin')
        o = getattr(origin, 'value', (0, 0)) if origin is not None else (0, 0)
        extra = ' %dx%d fmt=%s origin=%s' % (node.width, node.height, node.format, o)
    elif isinstance(node, WzUolProperty):
        extra = ' -> %s' % node.value
    else:
        v = getattr(node, 'value', None)
        if v is not None:
            extra = ' = %r' % (v,)
    return '%s [%s]%s' % (node.name, kind, extra)


def walk(node, depth, max_depth, indent=0):
    print('  ' * indent + describe(node))
    if depth >= max_depth or not hasattr(node, 'children'):
        return
    for child in node.children():
        walk(child, depth + 1, max_depth, indent + 1)


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('img')
    ap.add_argument('path', nargs='?', default='')
    ap.add_argument('--depth', type=int, default=4)
    ap.add_argument('--data', type=Path, default=DEFAULT_DATA)
    args = ap.parse_args()

    raw = (args.data / args.img).read_bytes()
    img = WzImage.from_bytes(raw, key=WzKey.for_region('GMS'), name=args.img)
    img.parse()
    if img.truncated or img.parse_warnings:
        print('WARN truncated=%s %s' % (img.truncated, img.parse_warnings), file=sys.stderr)

    node = img.root if not args.path else img.get(args.path)
    if node is None:
        print('NOT FOUND', args.path)
        return 1
    walk(node, 0, args.depth)
    return 0


if __name__ == '__main__':
    sys.exit(main())
