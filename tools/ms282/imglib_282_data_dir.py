# -*- coding: utf-8 -*-
# Python port of MapleDash ImgFile.cs (GMS standalone IMG reader)
import struct

class Key:
    def __init__(self):
        seed = bytes([0x13,0x08,0x06,0xb4,0x1b,0x0f,0x33,0x52])
        aes_key = bytearray(32)
        for i in range(8): aes_key[i*4] = seed[i]
        from Crypto.Cipher import AES
        self.cipher = AES.new(bytes(aes_key), AES.MODE_ECB)
        self.key = bytearray()
        self.iv = bytes([0x4d,0x23,0xc7,0x2b])

    def byte(self, index):
        while len(self.key) <= index:
            block = bytearray(16)
            if len(self.key) == 0:
                for j in range(16): block[j] = self.iv[j % 4]
            else:
                block = self.key[-16:]
            self.key.extend(self.cipher.encrypt(bytes(block)))
        return self.key[index]

KEY = Key()

CP1252_HIGH = [0x20AC,0x0081,0x201A,0x0192,0x201E,0x2026,0x2020,0x2021,
               0x02C6,0x2030,0x0160,0x2039,0x0152,0x008D,0x017D,0x008F,
               0x0090,0x2018,0x2019,0x201C,0x201D,0x2022,0x2013,0x2014,
               0x02DC,0x2122,0x0161,0x203A,0x0153,0x009D,0x017E,0x0178]

class Node:
    __slots__ = ("name","kind","value","children","x","y","width","height","fmt","parent")
    def __init__(self, name="", parent=None):
        self.name=name; self.kind=""; self.value=None; self.children=[]
        self.x=0; self.y=0; self.width=0; self.height=0; self.fmt=0; self.parent=parent
    def get(self, path):
        n = self
        for part in path.split("/"):
            if part=="" or part==".": continue
            if part=="..": n = n.parent if n else None; continue
            n = n.try_resolve() if n else None
            if n is None: return None
            n = next((c for c in n.children if c.name==part), None)
        return n
    def intv(self, path, fallback=0):
        v = self.get(path)
        if v is None or v.value is None: return fallback
        try: return int(v.value)
        except: return fallback
    def text(self, path, fallback=""):
        v = self.get(path)
        return v.value if (v and isinstance(v.value,str)) else fallback
    def try_resolve(self):
        n = self
        for _ in range(64):
            if n is None: return None
            if n.kind != "UOL": return n
            n = n.parent.get(n.value) if n.parent else None
        return None
    def __repr__(self): return f"<{self.kind}:{self.name}={self.value!r} n={len(self.children)}>"

class Img:
    def __init__(self, path):
        with open(path,"rb") as f: self.buf = f.read()
        self.pos = 0
        if self.read_byte() != 0x73 or self.read_string() != "Property":
            raise ValueError("not standalone img: "+path)
        self.read_u16()
        self.root = Node(path.rsplit("/",1)[-1])
        self.properties(self.root)

    def byte_(self): return self.buf[self.pos]
    def read_byte(self):
        b = self.buf[self.pos]; self.pos += 1; return b
    def read_u16(self):
        v = struct.unpack_from("<H", self.buf, self.pos)[0]; self.pos += 2; return v
    def read_u32(self):
        v = struct.unpack_from("<I", self.buf, self.pos)[0]; self.pos += 4; return v
    def read_i32(self):
        v = struct.unpack_from("<i", self.buf, self.pos)[0]; self.pos += 4; return v
    def read_f32(self):
        v = struct.unpack_from("<f", self.buf, self.pos)[0]; self.pos += 4; return v
    def read_f64(self):
        v = struct.unpack_from("<d", self.buf, self.pos)[0]; self.pos += 8; return v
    def read_i64(self):
        v = struct.unpack_from("<q", self.buf, self.pos)[0]; self.pos += 8; return v
    def read_int(self):
        n = struct.unpack_from("<b", self.buf, self.pos)[0]; self.pos += 1
        if n == -128: return self.read_i32()
        return n
    def read_long(self):
        n = struct.unpack_from("<b", self.buf, self.pos)[0]; self.pos += 1
        if n == -128: return self.read_i64()
        return n

    def read_string(self):
        sign = struct.unpack_from("<b", self.buf, self.pos)[0]; self.pos += 1
        if sign == 0: return ""
        unicode_ = sign > 0
        length = self.read_i32() if sign in (-128,127) else abs(sign)
        end = self.pos + length*(2 if unicode_ else 1)
        raw = bytearray(self.buf[self.pos:end]); self.pos = end
        if unicode_:
            for i in range(length):
                mask = (0xaaaa + i) & 0xffff
                raw[2*i] ^= mask & 0xff
                raw[2*i+1] ^= mask >> 8
            return raw.decode("utf-16-le", errors="replace")
        for i in range(length):
            raw[i] ^= (0xaa + i) & 0xff
        return bytes(raw).decode("cp1252", errors="replace")

    def string_block(self):
        marker = self.read_byte()
        if marker == 0 or marker == 0x73: return self.read_string()
        if marker != 1 and marker != 0x1b: raise ValueError(f"bad string marker {marker} @ {self.pos-1}")
        offset = self.read_u32()
        keep = self.pos
        self.pos = offset
        v = self.read_string()
        self.pos = keep
        return v

    def properties(self, parent):
        count = self.read_int()
        for _ in range(count):
            name = self.string_block()
            n = Node(name, parent)
            tag = self.read_byte()
            if tag == 0: pass
            elif tag in (2,11): n.value = self.read_i16s()
            elif tag in (3,19): n.value = self.read_int()
            elif tag == 20: n.value = self.read_long()
            elif tag == 4: n.value = self.read_f32() if self.read_byte()==128 else 0.0
            elif tag == 5: n.value = self.read_f64()
            elif tag == 8: n.value = self.string_block()
            elif tag == 9:
                size = self.read_u32()
                end = self.pos + size
                self.extended(n, end)
                self.pos = end
            else: raise ValueError(f"bad tag {tag} @ {self.pos-1}")
            parent.children.append(n)
        return parent

    def read_i16s(self):
        v = struct.unpack_from("<h", self.buf, self.pos)[0]; self.pos += 2; return v

    def extended(self, n, end):
        n.kind = self.string_block()
        if n.kind == "Property":
            self.read_u16(); self.properties(n)
        elif n.kind == "Shape2D#Convex2D":
            cnt = self.read_int()
            for i in range(cnt):
                p = Node(str(i), n)
                p.kind = self.string_block()
                p.x = self.read_int(); p.y = self.read_int()
                n.children.append(p)
        elif n.kind == "Shape2D#Vector2D":
            n.x = self.read_int(); n.y = self.read_int()
        elif n.kind == "UOL":
            self.read_byte(); n.value = self.string_block()
        elif n.kind == "Canvas":
            self.read_byte()
            if self.read_byte() == 1:
                self.read_u16(); self.properties(n)
            n.width = self.read_int(); n.height = self.read_int()
            n.fmt = self.read_int() + self.read_byte()
            self.pos += 9  # i32 i32 byte
        elif n.kind in ("Sound_DX8","Sound","Canvas#Video","RawData"):
            pass
        else: raise ValueError("ext type "+n.kind)
