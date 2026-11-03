#!/usr/bin/env python3
"""Transcode TMS 282 (BMS-keyed) standalone IMG files into the GMS-keyed layout
this project's runtime reader (Assets/Scripts/Wz/ImgFile.cs) consumes.

Both keys derive from the same user key; only the string payloads differ:
BMS uses a zero IV (plain XOR mask), GMS an AES-derived keystream. Payload
lengths are identical, so the file is rewritten in place byte-for-byte, which
keeps every indirected string offset valid.

AGENTS rule 11 (282 resources must be re-encoded to the beidou layout).
"""
from __future__ import annotations

import argparse
import struct
import sys
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT.parent.parent / "BeiDou-Server/tool/wz-python"))
from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzCanvasProperty  # noqa: E402


class UnsupportedImg(ValueError):
    pass


@dataclass
class StringSite:
    offset: int
    length: int
    unicode: bool


class Walker:
    """Structural scanner mirroring ImgFile.cs `Properties`/`Extended` order.

    Returns every encrypted string payload range (property names, extended-type
    names, string values, UOL targets) so the caller can re-key them.
    """

    def __init__(self, buf: bytes):
        self.buf = buf
        self.sites: dict[int, StringSite] = {}   # 同一 payload 可被多处 offset 引用，按位置去重后只转一次
        self.markers: dict[int, int] = {}
        self.ext_types: dict[str, int] = {}

    # -- primitives -------------------------------------------------------
    def _i8(self, p: int) -> tuple[int, int]:
        return struct.unpack_from("<b", self.buf, p)[0], p + 1

    def _i32(self, p: int) -> tuple[int, int]:
        return struct.unpack_from("<i", self.buf, p)[0], p + 4

    def _u32(self, p: int) -> tuple[int, int]:
        return struct.unpack_from("<I", self.buf, p)[0], p + 4

    def _cint(self, p: int) -> tuple[int, int]:
        v, p = self._i8(p)
        return self._i32(p) if v == -128 else (v, p)

    def _clong(self, p: int) -> tuple[int, int]:
        v, p = self._i8(p)
        return (struct.unpack_from("<q", self.buf, p)[0], p + 8) if v == -128 else (v, p)

    def _string(self, p: int) -> tuple[str, int]:
        sign, p = self._i8(p)
        if sign == 0:
            return "", p
        unicode = sign > 0
        if sign in (-128, 127):
            length, p = self._i32(p)
        else:
            length = abs(sign)
        n = length * (2 if unicode else 1)
        self.sites.setdefault(p, StringSite(p, n, unicode))
        raw = bytearray(self.buf[p:p + n])
        if len(raw) != n:
            raise UnsupportedImg(f"truncated string payload at 0x{p:X}")
        for i in range(length):
            if unicode:
                mask = (0xAAAA + i) & 0xFFFF
                raw[2 * i] ^= mask & 0xFF
                raw[2 * i + 1] ^= mask >> 8
            else:
                raw[i] ^= (0xAA + i) & 0xFF
        text = raw.decode("utf-16-le", "replace") if unicode else raw.decode("cp1252", "replace")
        return text, p + n

    def _sblock(self, p: int) -> tuple[str, int]:
        marker = self.buf[p]
        self.markers[marker] = self.markers.get(marker, 0) + 1
        p += 1
        if marker in (0x00, 0x73):
            return self._string(p)
        if marker in (0x01, 0x1B):
            offset, p = self._u32(p)
            return self._string(offset)[0], p
        raise UnsupportedImg(f"string-block marker 0x{marker:02X} at 0x{p - 1:X}")

    # -- structure --------------------------------------------------------
    def properties(self, p: int) -> int:
        count, p = self._cint(p)
        for _ in range(count):
            _, p = self._sblock(p)
            tag = self.buf[p]
            p += 1
            if tag == 0:
                continue
            if tag in (2, 11):
                p += 2
            elif tag in (3, 19):
                _, p = self._cint(p)
            elif tag == 20:
                _, p = self._clong(p)
            elif tag == 4:
                sub = self.buf[p]
                p += 1
                if sub == 0x80:
                    p += 4
            elif tag == 5:
                p += 8
            elif tag == 8:
                _, p = self._sblock(p)
            elif tag == 9:
                size, p = self._u32(p)
                end = p + size
                p = self.extended(p)
                if p > end:
                    raise UnsupportedImg(f"extended block overflow at 0x{p:X}")
                p = end
            else:
                raise UnsupportedImg(f"property tag {tag} at 0x{p - 1:X}")
        return p

    def extended(self, p: int) -> int:
        ext_type, p = self._sblock(p)
        self.ext_types[ext_type] = self.ext_types.get(ext_type, 0) + 1
        if ext_type == "Property":
            p += 2
            return self.properties(p)
        if ext_type == "Shape2D#Convex2D":
            count, p = self._cint(p)
            for _ in range(count):
                _, p = self._sblock(p)
                _, p = self._cint(p)
                _, p = self._cint(p)
            return p
        if ext_type == "Shape2D#Vector2D":
            _, p = self._cint(p)
            _, p = self._cint(p)
            return p
        if ext_type == "UOL":
            p += 1
            _, p = self._sblock(p)
            return p
        if ext_type == "Canvas":
            p += 1                                  # reserved
            has_children = self.buf[p]
            p += 1
            if has_children == 1:
                p += 2
                p = self.properties(p)
            _, p = self._cint(p)                    # width
            _, p = self._cint(p)                    # height
            _, p = self._cint(p)                    # format
            p += 1                                  # format2
            p += 4                                  # reserved
            p += 4                                  # declared length
            p += 1                                  # filler
            return p                                # callers advance to block end
        if ext_type in ("Sound", "Sound_DX8", "Canvas#Video", "RawData"):
            return p
        raise UnsupportedImg(f"extended type {ext_type!r} at 0x{p:X}")

    def scan(self) -> "Walker":
        if self.buf[0] != 0x73:
            raise UnsupportedImg("missing 0x73 image header")
        name, p = self._string(1)
        if name != "Property":
            raise UnsupportedImg(f"unexpected image header {name!r}")
        p += 2                                       # version
        p = self.properties(p)
        if p != len(self.buf):
            # Trailing bytes mean the walk lost sync; a wrong rewrite would
            # corrupt pixels, so refuse rather than guess.
            raise UnsupportedImg(f"walk ended at 0x{p:X}, file is 0x{len(self.buf):X}")
        return self


def gms_key(n: int) -> bytes:
    key = WzKey.for_region("GMS")
    key.ensure(n)
    return bytes(key._data[:n])


def transcode(buf: bytes) -> tuple[bytes, Walker]:
    walker = Walker(buf).scan()
    out = bytearray(buf)
    key = gms_key(max((s.length for s in walker.sites.values()), default=0))
    for site in walker.sites.values():
        # Source payload is plaintext^mask (BMS zero key); re-XOR with the GMS
        # keystream to reach the layout ImgFile.cs decrypts.
        for i in range(site.length):
            out[site.offset + i] ^= key[i]
    return bytes(out), walker


def verify(original: bytes, transcoded: bytes, label: str) -> None:
    bms = WzImage.from_bytes(original, key=WzKey.for_region("BMS"), name=label)
    bms.parse()
    gms = WzImage.from_bytes(transcoded, key=WzKey.for_region("GMS"), name=label)
    gms.parse()
    if gms.truncated:
        raise UnsupportedImg(f"{label}: transcoded image does not parse")

    def compare(a, b, path):
        if str(a.name) != str(b.name):
            raise UnsupportedImg(f"{label}: name {path}: {a.name!r} != {b.name!r}")
        kind = type(a).__name__
        if kind != type(b).__name__:
            raise UnsupportedImg(f"{label}: kind {path}: {kind} != {type(b).__name__}")
        if isinstance(a, WzCanvasProperty):
            signature = (a.width, a.height, a.format, a.format2, a._png_length)
            other = (b.width, b.height, b.format, b.format2, b._png_length)
            if signature != other:
                raise UnsupportedImg(f"{label}: canvas {path}: {signature} != {other}")
        elif getattr(a, "value", None) is not None or getattr(b, "value", None) is not None:
            if a.value != b.value:
                raise UnsupportedImg(f"{label}: value {path}: {a.value!r} != {b.value!r}")
        kids_a, kids_b = list(a.children()), list(b.children())
        if len(kids_a) != len(kids_b):
            raise UnsupportedImg(f"{label}: child count {path}: {len(kids_a)} != {len(kids_b)}")
        for ca, cb in zip(kids_a, kids_b):
            compare(ca, cb, path + "/" + str(ca.name))

    compare(bms.root, gms.root, "")


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("src", type=Path)
    ap.add_argument("--out", type=Path)
    ap.add_argument("--check", action="store_true", help="verify only, do not write")
    args = ap.parse_args()
    raw = args.src.read_bytes()
    data, walker = transcode(raw)
    verify(raw, data, args.src.name)
    print(f"{args.src.name}: {len(walker.sites)} strings, markers {walker.markers}, "
          f"types {sorted(walker.ext_types)}")
    if not args.check:
        out = args.out or args.src.with_suffix(".gms.img")
        out.write_bytes(data)
        print("wrote", out, len(data), "bytes (input", len(raw), ")")


if __name__ == "__main__":
    main()
