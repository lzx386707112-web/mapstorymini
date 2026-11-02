#!/usr/bin/env python3
"""把 MsUnpack 解出的 282 客户端原始 img 转成 wz282 同款 HaRepacker XML。

  python3 ms282_img2xml.py <in_dir> <out_xml_dir> [name...]

- 密钥：BMS（零 IV，现代 64 位客户端），与 wzpy detect_region 一致。
- canvas/sound 媒体节点丢弃（与套件 manifest "mediaDropped" 口径一致），
  非媒体子节点（如 attack1/info、uol）全部保留。
"""
import os
import sys
from xml.sax.saxutils import escape, quoteattr

WZPY = '/Users/lizixian/Documents/mxd/Beidou-Server/tool/wz-python'
sys.path.insert(0, WZPY)

from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import (  # noqa: E402
    WzSubProperty, WzIntProperty, WzStringProperty, WzFloatProperty,
    WzLongProperty, WzShortProperty, WzVectorProperty, WzUolProperty,
    WzCanvasProperty, WzSoundProperty, WzNullProperty,
)

MEDIA = (WzCanvasProperty, WzSoundProperty)


def fmt_float(v):
    s = repr(float(v))
    return s if '.' in s else s + '.0'


def emit(node, indent, out):
    pad = '    ' * indent
    name = escape(str(node.name))
    cls = type(node).__name__
    if isinstance(node, MEDIA):
        return  # 媒体丢弃
    if isinstance(node, WzSubProperty):
        out.append('%s<imgdir name=%s>' % (pad, quoteattr(name)))
        for c in node.children():
            emit(c, indent + 1, out)
        out.append('%s</imgdir>' % pad)
    elif isinstance(node, WzIntProperty):
        out.append('%s<int name=%s value="%d" />' % (pad, quoteattr(name), int(node.value)))
    elif isinstance(node, WzShortProperty):
        out.append('%s<int name=%s value="%d" />' % (pad, quoteattr(name), int(node.value)))
    elif isinstance(node, WzLongProperty):
        out.append('%s<long name=%s value="%d" />' % (pad, quoteattr(name), int(node.value)))
    elif isinstance(node, WzFloatProperty):
        out.append('%s<float name=%s value="%s" />' % (pad, quoteattr(name), fmt_float(node.value)))
    elif isinstance(node, WzStringProperty):
        out.append('%s<string name=%s value=%s />' % (pad, quoteattr(name), quoteattr(str(node.value))))
    elif isinstance(node, WzVectorProperty):
        v = getattr(node, 'value', (0, 0))
        out.append('%s<vector name=%s x="%d" y="%d" value="%d, %d" />' %
                   (pad, quoteattr(name), int(v[0]), int(v[1]), int(v[0]), int(v[1])))
    elif isinstance(node, WzUolProperty):
        out.append('%s<uol name=%s value=%s />' % (pad, quoteattr(name), quoteattr(str(node.value))))
    elif isinstance(node, WzNullProperty):
        out.append('%s<null name=%s />' % (pad, quoteattr(name)))
    else:
        v = getattr(node, 'value', None)
        out.append('%s<!-- unknown %s name=%s value=%r -->' % (pad, cls, name, v))


def convert(path, out_dir):
    base = os.path.basename(path)
    img_id = base.replace('.img', '').replace('Mob_', '')
    raw = open(path, 'rb').read()
    img = WzImage.from_bytes(raw, key=WzKey.for_region('BMS'), name=img_id + '.img')
    img.parse()
    if img.truncated:
        raise RuntimeError('truncated: ' + path)
    out = ['<?xml version="1.0" encoding="UTF-8" standalone="yes"?>']
    emit(img.root, 0, out)
    xml = '\n'.join(out) + '\n'
    dst = os.path.join(out_dir, img_id + '.img.xml')
    with open(dst, 'w', encoding='utf-8') as f:
        f.write(xml)
    return dst, len(xml), xml.count('<imgdir'), xml.count('<int')


def main():
    in_dir, out_dir = sys.argv[1], sys.argv[2]
    os.makedirs(out_dir, exist_ok=True)
    names = sys.argv[3:]
    files = sorted(os.listdir(in_dir))
    for fn in files:
        if not fn.endswith('.img'):
            continue
        mob_id = fn.replace('.img', '').replace('Mob_', '')
        if names and mob_id not in names:
            continue
        dst, size, n_imgdir, n_int = convert(os.path.join(in_dir, fn), out_dir)
        print('%s -> %s (%d bytes, %d imgdir, %d int)' % (fn, dst, size, n_imgdir, n_int))


if __name__ == '__main__':
    main()
