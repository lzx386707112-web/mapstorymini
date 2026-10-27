#!/usr/bin/env python3
"""Decode real MCV frames through the port and original BGRA conversion oracle."""
import ctypes as c
import struct
import subprocess
import tempfile
from pathlib import Path

root = Path(__file__).resolve().parents[2]
source = root.parents[1] / "Beidou-Server"
original = (source / "tool/client-video/BeiDouVideo.cpp").read_text()
start = original.index("    static bool IsPlanar420(")
end = original.index("    bool UploadFrameLocked(", start)
port = (root / "Native/Mcv/mcv_decoder.cpp").read_text()
a = port.index("    static bool IsPlanar420(")
b = port.index("\n}\n#define API", a)

def load(path):
    lib = c.CDLL(str(path))
    lib.mcv_create.argtypes = [c.c_uint32]*3+[c.c_int]
    lib.mcv_create.restype = c.c_void_p
    lib.mcv_destroy.argtypes = [c.c_void_p]
    lib.mcv_decode.argtypes = [c.c_void_p,c.c_void_p]+[c.c_uint32]*5+[c.c_void_p,c.c_uint32]
    return lib

with tempfile.TemporaryDirectory() as tmp:
    cpp = Path(tmp)/"oracle.cpp"
    cpp.write_text(port[:a]+original[start:end].replace("kBgraBytesPerPixel","4")+port[b:])
    dylib = Path(tmp)/"oracle.dylib"
    subprocess.run(["clang++","-std=c++17","-O2","-dynamiclib","-I/opt/homebrew/opt/libvpx/include",str(cpp),"/opt/homebrew/opt/libvpx/lib/libvpx.a","-o",str(dylib)],check=True)
    libs = [load(root/"Assets/Plugins/macOS/libmapledash_mcv.dylib"),load(dylib)]
    frames=0
    files=sorted((source/"clien/Data/Video").glob("*.mcv"))
    for path in files:
        data=path.read_bytes()
        header=struct.unpack_from("<H",data,6)[0]
        codec=struct.unpack_from("<I",data,8)[0]^0xA5A5A5A5
        w,h,count=struct.unpack_from("<HHI",data,12)
        flags=data[20]
        base=header+count*(8+(8 if flags&1 else 0)+(4 if flags&2 else 0)+(8 if flags&4 else 0))
        handles=[lib.mcv_create(codec,w,h,flags&1) for lib in libs]
        assert all(handles),path
        outputs=[(c.c_ubyte*(w*h*4))() for _ in libs]
        try:
            # First eight frames exercise inter-frame state without decoding all long cinematics.
            for i in range(min(count,8)):
                co,cs=struct.unpack_from("<II",data,header+i*8)
                ao,az=struct.unpack_from("<II",data,header+count*8+i*8) if flags&1 else (0,0)
                for lib,handle,out in zip(libs,handles,outputs):
                    assert lib.mcv_decode(handle,data,len(data),base+co,cs,base+ao if az else 0,az,out,len(out)),(path,i)
                rgba,bgra=map(bytes,outputs)
                assert rgba[0::4]==bgra[2::4] and rgba[1::4]==bgra[1::4] and rgba[2::4]==bgra[0::4] and rgba[3::4]==bgra[3::4],(path,i)
                frames+=1
        finally:
            for lib,handle in zip(libs,handles): lib.mcv_destroy(handle)
    print(f"MCV_DECODE_PASS files={len(files)} frames={frames} original_BGRA_oracle=exact_RGBA")
