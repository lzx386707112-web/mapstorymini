#!/usr/bin/env python3
"""Bake `_outlink` stubs into self-contained IMG files and trim unused branches.

Runtime contract (Assets/Scripts/Wz/ImgFile.cs + WzAssets.Canvas):

* A canvas whose pixels live in another file carries an `_outlink` string
  child; WzAssets follows it at runtime — every hop costs a file open, and
  the pixel file must ship alongside the structure file. The 282 client
  splits every window this way (`UI/X.img` -> `UI/_Canvas/X.img`), which is
  why `UI/_Canvas/Enchant.img` is 215 MB for a handful of drawn branches.
* ImgFile.cs decodes only raw-zlib bitmaps (format 1/2/513) or listWz
  chunks — never PNG payloads.

This module resolves every `_outlink` of the kept branches against the
pixel files, embeds the real pixels (payload bytes copied verbatim), drops
branches the game never draws, and re-serializes the structure file with
wzpy's `encode_image_body` — the same writer `import_enhancement_ui.py`
already uses for `UI/Enchant.img`.

Validation is built in: `RuntimeImgMirror` re-implements ImgFile.cs byte
for byte (string crypto, property tags, canvas decode), so every produced
file is re-parsed with the runtime's own logic and every canvas RGBA is
compared against wzpy's independent decode of the source pixels.
"""

from __future__ import annotations

import posixpath
import struct
import sys
import zlib
from pathlib import Path

WZPY = Path('/Users/lizixian/Documents/mxd/Beidou-Server/tool/wz-python')
if str(WZPY) not in sys.path:
    sys.path.insert(0, str(WZPY))

import numpy as np  # noqa: E402
import texture2ddecoder as t2d  # noqa: E402

from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.canvas import _read_canvas_bytes, decode_canvas  # noqa: E402
from wzpy.properties import WzCanvasProperty, WzSubProperty  # noqa: E402
from wzpy.writer import encode_image_body  # noqa: E402

LINK_CHILDREN = ('_outlink', '_inlink')

# 282 的 UI/Effect 像素文件里有块压缩画布：2050=BC3(DXT5)、4098=BC7。
# wzpy 的 decode_canvas 不认这两种，ImgFile.cs 也不认 —— 所以烘焙时顺手把
# 它们解成 RGBA 再压回 ARGB4444（format 1），这也是本项目所有 282 素材的落地口径。
BLOCK_DECODERS = {2050: t2d.decode_bc3, 4098: t2d.decode_bc7}
NATIVE_FORMATS = (1, 2, 513)


def decode_rgba(canvas: WzCanvasProperty, img: WzImage) -> np.ndarray:
    """画布 → RGBA（支持块压缩）。282 的块压缩载荷缺 zlib adler32 尾，
    必须跳 2 字节 + 裸 deflate，和 .NET 侧一致。"""
    with img.wz_file.reader_lock:
        payload = _read_canvas_bytes(canvas)
    if canvas.format in BLOCK_DECODERS:
        inflater = zlib.decompressobj(-15)
        data = inflater.decompress(payload[2:]) + inflater.flush()
        raw = BLOCK_DECODERS[canvas.format](data, canvas.width, canvas.height)
        return np.frombuffer(raw, dtype=np.uint8).reshape((canvas.height, canvas.width, 4))
    return np.array(decode_canvas(canvas).convert("RGBA"))


def encode_argb4444(rgba: np.ndarray) -> bytes:
    """ImgFile.cs format 1 布局：byte0=(G<<4)|B，byte1=(A<<4)|R。"""
    r = (rgba[:, :, 0] >> 4).astype(np.uint8)
    g = (rgba[:, :, 1] >> 4).astype(np.uint8)
    b = (rgba[:, :, 2] >> 4).astype(np.uint8)
    a = (rgba[:, :, 3] >> 4).astype(np.uint8)
    packed = np.stack([((g << 4) | b).astype(np.uint8), ((a << 4) | r).astype(np.uint8)], axis=-1)
    return zlib.compress(packed.tobytes(), 6)


class PixelSource:
    """Lazy cache of parsed pixel imgs (`UI/_Canvas/*.img` et al.).

    Files missing from the project (the 282 client splits pixels across ~50
    `_Canvas` files and only a few were ever imported) are transcoded on the
    fly from the 282 source tree, BMS key -> GMS key, and never written to
    the project — they only feed pixels into the baked output.
    """

    def __init__(self, data_root: Path, key: WzKey,
                 fallback_root: Path | None = None):
        self.data_root = data_root
        self.key = key
        self.fallback_root = fallback_root
        self.cache: dict[str, WzImage] = {}

    def img(self, rel: str) -> WzImage:
        if rel not in self.cache:
            path = self.data_root / rel
            if path.is_file():
                img = WzImage.from_bytes(path.read_bytes(), key=self.key, name=rel)
            elif self.fallback_root is not None and (self.fallback_root / rel).is_file():
                import bms_to_gms
                data, _ = bms_to_gms.transcode((self.fallback_root / rel).read_bytes())
                img = WzImage.from_bytes(data, key=self.key, name=rel)
            else:
                raise FileNotFoundError(f'pixel img missing: {rel}')
            img.parse()
            self.cache[rel] = img
        return self.cache[rel]

    def resolve_outlink(self, link: str) -> tuple[WzCanvasProperty, WzImage, str]:
        """`UI/_Canvas/Enchant.img/bonusStat/btSave/normal/0` -> (canvas, img, path)."""
        split = link.index('.img/')
        img_rel = link[:split + 4]
        path = link[split + 5:]
        img = self.img(img_rel)
        node = img.root.get(path)
        if node is None:
            raise KeyError(f'outlink target not found: {link}')
        return node, img, path


def follow_to_pixels(node: WzCanvasProperty, img: WzImage, path: str,
                     source: PixelSource) -> tuple[WzCanvasProperty, WzImage, str]:
    """Follow _outlink/_inlink chains until a canvas with real pixels."""
    for _ in range(64):
        out = node.get('_outlink')
        if out is not None and str(out.value):
            node, img, path = source.resolve_outlink(str(out.value))
            continue
        ink = node.get('_inlink')
        if ink is not None and str(ink.value):
            target = posixpath.normpath(
                posixpath.join(posixpath.dirname(path), str(ink.value)))
            node = img.root.get(target)
            if node is None:
                raise KeyError(f'_inlink target not found: {target}')
            path = target
            continue
        return node, img, path
    raise RuntimeError('link cycle while baking')


def bake_canvas(stub: WzCanvasProperty, source: PixelSource, stats: dict) -> None:
    """Replace a stub canvas's identity with the pixel owner it points at.

    The stub keeps its place in the tree; width/height/format and the raw
    payload come from the resolved owner, `_outlink`/`_inlink` children are
    stripped, and the owner's scalar children (origin, delay, ...) are
    merged in without overwriting what the stub already had.
    """
    owner, owner_img, owner_path = None, None, None
    # follow_to_pixels needs the stub's own outlink; start from it directly.
    cur, cur_img, cur_path = stub, None, ''
    link = str(stub.get('_outlink').value) if stub.get('_outlink') is not None else ''
    if link:
        cur, cur_img, cur_path = source.resolve_outlink(link)
        cur, cur_img, cur_path = follow_to_pixels(cur, cur_img, cur_path, source)
    else:
        raise ValueError('bake_canvas on a canvas without _outlink')

    rgba = decode_rgba(cur, cur_img)

    stub.width, stub.height = cur.width, cur.height
    if cur.format in NATIVE_FORMATS:
        with cur_img.wz_file.reader_lock:
            stub._png_data = _read_canvas_bytes(cur)
        stub.format, stub.format2 = cur.format, cur.format2
    else:
        # 块压缩：运行时解不了，落盘前重编码成 ARGB4444。
        stub.format, stub.format2 = 1, 0
        stub._png_data = encode_argb4444(rgba)

    keep = {c.name: c for c in stub.children() if c.name not in LINK_CHILDREN}
    merged = []
    for child in cur.children():
        if child.name in LINK_CHILDREN or child.name in keep:
            continue
        merged.append(child)
    merged.extend(keep.values())
    # WzSubProperty stores children in the `_children` dict (insertion order
    # is the serialization order).
    stub._children.clear()
    for child in merged:
        stub._children[child.name] = child
    stats['baked'] += 1
    stats['pixels'] += cur.width * cur.height * 4


def bake_tree(node: WzSubProperty, source: PixelSource, stats: dict,
              prefix: str = '') -> None:
    for child in list(node.children()):
        path = f'{prefix}/{child.name}' if prefix else child.name
        if isinstance(child, WzCanvasProperty):
            if child.get('_outlink') is not None:
                bake_canvas(child, source, stats)
        elif isinstance(child, WzSubProperty):
            bake_tree(child, source, stats, path)


def trim_root(root: WzSubProperty, keep: list[str] | None) -> list[str]:
    """Drop top-level branches not in `keep` (None keeps everything)."""
    if keep is None:
        return list(root._children)
    dropped = []
    for name in list(root._children):
        if name not in keep:
            del root._children[name]
            dropped.append(name)
    return dropped


def serialize(img: WzImage) -> bytes:
    return encode_image_body(img, img.wz_file.reader)


# ———————————————————————————— ImgFile.cs 镜像校验器 ————————————————————————————

class MirrorError(AssertionError):
    pass


class RuntimeImgMirror:
    """Byte-exact re-implementation of Assets/Scripts/Wz/ImgFile.cs.

    Independent of wzpy's parser on purpose: if the written bytes drift
    from what the Unity runtime accepts, this class fails first.
    """

    _CP1252_HIGH = (
        '€\x81‚ƒ„…†‡ˆ‰Š‹Œ\x8dŽ\x8f\x90‘’“”•–—˜™š›œ\x9džŸ')

    def __init__(self, data: bytes, key: WzKey):
        self.d = data
        self.p = 0
        self.key = key
        if self.u8() != 0x73 or self.read_string() != 'Property':
            raise MirrorError('expected GMS standalone IMG header')
        self.u16()  # version, ignored by the runtime
        self.root = {'name': '<root>', 'kind': 'Property', 'children': self.properties()}

    # — primitives —
    def u8(self) -> int:
        v = self.d[self.p]
        self.p += 1
        return v

    def i8(self) -> int:
        v = self.u8()
        return v - 256 if v >= 128 else v

    def u16(self) -> int:
        v = struct.unpack_from('<H', self.d, self.p)[0]
        self.p += 2
        return v

    def i32(self) -> int:
        v = struct.unpack_from('<i', self.d, self.p)[0]
        self.p += 4
        return v

    def u32(self) -> int:
        v = struct.unpack_from('<I', self.d, self.p)[0]
        self.p += 4
        return v

    def f32(self) -> float:
        v = struct.unpack_from('<f', self.d, self.p)[0]
        self.p += 4
        return v

    def f64(self) -> float:
        v = struct.unpack_from('<d', self.d, self.p)[0]
        self.p += 8
        return v

    def cint(self) -> int:
        n = self.i8()
        return self.i32() if n == -128 else n

    # — strings (ImgFile.cs ReadString/StringBlock) —
    def read_string(self) -> str:
        sign = self.i8()
        if sign == 0:
            return ''
        unicode = sign > 0
        length = self.i32() if sign in (-128, 127) else abs(sign)
        if length < 0 or length * (2 if unicode else 1) > len(self.d) - self.p:
            raise MirrorError('IMG string length out of range')
        raw = self.d[self.p:self.p + length * (2 if unicode else 1)]
        self.p += len(raw)
        if unicode:
            out = bytearray(length * 2)
            for i in range(length):
                mask = (0xaaaa + i) & 0xFFFF
                out[2 * i] = raw[2 * i] ^ (mask & 0xFF) ^ self.key[2 * i]
                out[2 * i + 1] = raw[2 * i + 1] ^ (mask >> 8) ^ self.key[2 * i + 1]
            return out.decode('utf-16-le')
        out = bytearray(length)
        for i in range(length):
            out[i] = raw[i] ^ ((0xaa + i) & 0xFF) ^ self.key[i]
        return bytes(out).decode('cp1252')

    def _cp1252(self, raw: bytes) -> str:
        chars = []
        for b in raw:
            if 0x80 <= b <= 0x9F:
                chars.append(self._CP1252_HIGH[b - 0x80])
            else:
                chars.append(chr(b))
        return ''.join(chars)

    def string_block(self) -> str:
        marker = self.u8()
        if marker in (0, 0x73):
            return self.read_string()
        if marker in (1, 0x1B):
            offset = self.u32()
            keep = self.p
            self.p = offset
            value = self.read_string()
            self.p = keep
            return value
        raise MirrorError(f'IMG string marker {marker}')

    # — property tree —
    def properties(self) -> list[dict]:
        count = self.cint()
        if count < 0 or count > len(self.d):
            raise MirrorError('IMG property count')
        out = []
        for _ in range(count):
            name = self.string_block()
            tag = self.u8()
            node: dict = {'name': name, 'kind': 'Null', 'children': []}
            if tag in (2, 11):
                node['kind'] = 'Short'
                node['value'] = struct.unpack_from('<h', self.d, self.p)[0]
                self.p += 2
            elif tag in (3, 19):
                node['kind'] = 'Int'
                node['value'] = self.cint()
            elif tag == 20:
                v = self.i8()
                node['kind'] = 'Long'
                node['value'] = struct.unpack_from('<q', self.d, self.p)[0] if v == -128 else v
                if v == -128:
                    self.p += 8
            elif tag == 4:
                node['kind'] = 'Float'
                node['value'] = self.f32() if self.u8() == 0x80 else 0.0
            elif tag == 5:
                node['kind'] = 'Double'
                node['value'] = self.f64()
            elif tag == 8:
                node['kind'] = 'String'
                node['value'] = self.string_block()
            elif tag == 9:
                size = self.u32()
                end = self.p + size
                if end > len(self.d):
                    raise MirrorError('IMG block overflow')
                self.extended(node, end)
                if self.p > end:
                    raise MirrorError('IMG block overrun')
                self.p = end
            else:
                raise MirrorError(f'IMG property tag {tag}')
            out.append(node)
        return out

    def extended(self, node: dict, end: int) -> None:
        node['kind'] = self.string_block()
        if node['kind'] == 'Property':
            self.u16()
            node['children'] = self.properties()
        elif node['kind'] == 'Shape2D#Convex2D':
            count = self.cint()
            for i in range(count):
                point = {'name': str(i), 'kind': self.string_block(), 'children': []}
                if point['kind'] != 'Shape2D#Vector2D':
                    raise MirrorError('Convex child kind')
                point['x'], point['y'] = self.cint(), self.cint()
                node['children'].append(point)
        elif node['kind'] == 'Shape2D#Vector2D':
            node['x'], node['y'] = self.cint(), self.cint()
        elif node['kind'] == 'UOL':
            self.u8()
            node['value'] = self.string_block()
        elif node['kind'] == 'Canvas':
            self.u8()
            if self.u8() == 1:
                self.u16()
                node['children'] = self.properties()
            node['width'] = self.cint()
            node['height'] = self.cint()
            node['format'] = self.cint() + self.u8()
            self.i32()
            self.i32()
            self.u8()
            node['pixels'] = self.d[self.p:end]
        elif node['kind'] in ('Sound_DX8', 'Sound'):
            self.u8()
            self.cint()  # audio length
            self.cint()  # ms
            node['sound'] = True
            self.p = end
        elif node['kind'] in ('Canvas#Video', 'RawData'):
            node['raw_length'] = end - self.p
            self.p = end
        else:
            raise MirrorError(f'IMG extended type {node["kind"]}')

    # — canvas decode (ImgNode.Rgba) —
    def rgba(self, node: dict) -> bytes:
        compressed = node['pixels']
        if len(compressed) < 2:
            raise MirrorError('empty canvas')
        if compressed[0] != 0x78 or ((compressed[0] << 8) + compressed[1]) % 31 != 0:
            # listWz chunks are XORed with the raw keystream (no position mask).
            raw = bytearray()
            src = compressed
            pos = 0
            while pos < len(src):
                length = struct.unpack_from('<i', src, pos)[0]
                pos += 4
                if length <= 0 or pos + length > len(src):
                    raise MirrorError('invalid listWz chunk')
                chunk = src[pos:pos + length]
                pos += length
                raw += bytes(c ^ self.key[i] for i, c in enumerate(chunk))
            compressed = bytes(raw)
        # .NET DeflateStream (ImgFile.cs) = raw deflate after the 2-byte
        # zlib header, adler32 never verified — mirror that exactly; several
        # 282 payloads ship without a valid checksum.
        inflater = zlib.decompressobj(-15)
        raw = inflater.decompress(compressed[2:]) + inflater.flush()
        width, height, fmt = node['width'], node['height'], node['format']
        stride = 4 if fmt == 2 else 2
        if fmt not in (1, 2, 513):
            raise MirrorError(f'canvas format {fmt}')
        count = width * height
        if len(raw) < count * stride:
            raise MirrorError('pixel length mismatch')
        out = bytearray(count * 4)
        for i in range(count):
            s, d = i * stride, i * 4
            if fmt == 2:
                out[d], out[d + 1], out[d + 2], out[d + 3] = raw[s + 2], raw[s + 1], raw[s], raw[s + 3]
            elif fmt == 1:
                out[d] = (raw[s + 1] & 15) * 17
                out[d + 1] = (raw[s] >> 4) * 17
                out[d + 2] = (raw[s] & 15) * 17
                out[d + 3] = (raw[s + 1] >> 4) * 17
            else:
                v = raw[s] | raw[s + 1] << 8
                out[d] = ((v >> 11) & 31) * 8
                out[d + 1] = ((v >> 5) & 63) * 4
                out[d + 2] = (v & 31) * 8
                out[d + 3] = 255
        return bytes(out)


def _mismatch(a, b, path: str, problems: list[str]) -> None:
    if type(a) is not type(b):
        problems.append(f'{path}: kind {a!r} != {b!r}')
    elif isinstance(a, dict):
        if a.get('name') != b.get('name'):
            problems.append(f'{path}: name {a.get("name")} != {b.get("name")}')
        for field in ('kind', 'value', 'width', 'height', 'format', 'x', 'y'):
            if field in a or field in b:
                av, bv = a.get(field), b.get(field)
                if field in ('value',) and isinstance(av, float) and isinstance(bv, float):
                    if av is None or bv is None or abs(av - bv) > 1e-6:
                        problems.append(f'{path}: {field} {av!r} != {bv!r}')
                elif av != bv:
                    problems.append(f'{path}: {field} {av!r} != {bv!r}')
        ac, bc = a.get('children') or [], b.get('children') or []
        if len(ac) != len(bc):
            problems.append(f'{path}: child count {len(ac)} != {len(bc)}')
        for i, (ca, cb) in enumerate(zip(ac, bc)):
            _mismatch(ca, cb, f'{path}/{ca.get("name", i)}', problems)


def verify_file(new_bytes: bytes, expected_root: dict, key: WzKey,
                mirror: 'RuntimeImgMirror | None' = None) -> list[str]:
    """Parse `new_bytes` with the runtime mirror and diff against `expected_root`.

    Canvas pixels are compared by decoded RGBA (wzpy decode on the expected
    side, the mirror's own decoder on the new side). Pass `mirror` to reuse
    one parsed instance across verify + pixel diff.
    """
    problems: list[str] = []
    if mirror is None:
        mirror = RuntimeImgMirror(new_bytes, key)
    _mismatch(expected_root, mirror.root, '<root>', problems)
    # Every canvas in the new file must decode and must not carry links.
    stack = [('', child) for child in mirror.root['children']]
    canvases = 0
    while stack:
        path, node = stack.pop()
        kind = node.get('kind')
        if kind == 'Canvas':
            canvases += 1
            names = {c['name'] for c in node.get('children', [])}
            if names & {'_outlink', '_inlink'}:
                problems.append(f'{path}: link child survived baking: {names}')
            try:
                if len(node['pixels']) < 2:
                    problems.append(f'{path}: empty payload')
                else:
                    node['rgba'] = mirror.rgba(node)
            except MirrorError as exc:
                problems.append(f'{path}: {exc}')
        for child in node.get('children', []):
            stack.append((f'{path}/{child["name"]}', child))
    print(f'  mirror: {canvases} canvas(es) decoded via ImgFile.cs logic')
    return problems


def _mismatch_with_pixels(a, b, path: str, problems: list[str]) -> None:
    """Like _mismatch, but skips pixel-level fields (RGBA compared separately)."""
    if isinstance(a, dict) and a.get('kind') == 'Canvas' and isinstance(b, dict) \
            and b.get('kind') == 'Canvas':
        for field in ('width', 'height', 'format'):
            if a.get(field) != b.get(field):
                problems.append(f'{path}: {field} {a.get(field)} != {b.get(field)}')
        ac = [c for c in a.get('children') or []]
        bc = [c for c in b.get('children') or []]
        if len(ac) != len(bc):
            problems.append(f'{path}: child count {len(ac)} != {len(bc)}')
        for ca, cb in zip(ac, bc):
            _mismatch_with_pixels(ca, cb, f'{path}/{ca.get("name")}', problems)
        return
    _mismatch(a, b, path, problems)


def expected_tree_from(img: WzImage) -> dict:
    """wzpy tree -> plain dicts mirroring the runtime mirror's shape.

    Kind names are normalized to what ImgFile.cs/writer emit on disk:
    wzpy calls a container "SubProperty" and a vector "Vector"; the file
    format calls them "Property" and "Shape2D#Vector2D".
    """
    kind_map = {'SubProperty': 'Property', 'Vector': 'Shape2D#Vector2D'}
    scalars = {'Short', 'Int', 'Long', 'Float', 'Double', 'String', 'UOL'}

    def conv(prop):
        name = prop.name
        raw = getattr(prop, 'type_name', '')
        node: dict = {'name': name, 'kind': kind_map.get(raw, raw), 'children': []}
        if isinstance(prop, WzCanvasProperty):
            node['width'], node['height'] = prop.width, prop.height
            node['format'] = prop.format + prop.format2
            node['_canvas'] = prop
            for child in prop.children():
                node['children'].append(conv(child))
        elif isinstance(prop, WzSubProperty):
            for child in prop.children():
                node['children'].append(conv(child))
        else:
            if raw in scalars:
                node['value'] = getattr(prop, 'value', None)
            if node['kind'] == 'Shape2D#Vector2D':
                node['x'], node['y'] = prop.x, prop.y
        return node
    root_children = [conv(c) for c in img.root.children()]
    return {'name': '<root>', 'kind': 'Property', 'children': root_children}


def canvas_rgba_of(prop: WzCanvasProperty) -> bytes:
    """wzpy's own decode — the independent side of the pixel comparison."""
    return decode_canvas(prop).tobytes()


def check_canvas_pixels(expected_root: dict, mirror_root: dict,
                        problems: list[str]) -> int:
    """Diff decoded RGBA for every canvas (expected: wzpy, actual: mirror)."""
    def flat(root):
        out = {}
        stack = [('', child) for child in root['children']]
        while stack:
            path, node = stack.pop()
            if node.get('kind') == 'Canvas':
                out[path] = node
            for child in node.get('children', []):
                stack.append((f'{path}/{child["name"]}', child))
        return out
    exp, act = flat(expected_root), flat(mirror_root)
    if set(exp) != set(act):
        only_e = sorted(set(exp) - set(act))[:6]
        only_a = sorted(set(act) - set(exp))[:6]
        problems.append(f'canvas set differs: missing={only_e} extra={only_a}')
        return 0
    checked = 0
    for path, node in exp.items():
        prop = node.get('_canvas')
        if prop is None or prop._png_length == 0 and prop._png_data is None:
            continue
        want = canvas_rgba_of(prop)
        got = act[path].get('rgba')
        if got is None:
            continue  # already reported by verify_file
        if len(want) != len(got) or want != got:
            diff = sum(1 for x, y in zip(want, got) if x != y)
            problems.append(f'{path}: pixels differ ({diff}/{len(want)} bytes)')
        checked += 1
    return checked
