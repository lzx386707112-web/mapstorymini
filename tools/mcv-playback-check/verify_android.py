#!/usr/bin/env python3
"""Compare complete decoded RGBA streams on Android ARM64 and the host. No APK/DB changes."""
import argparse
import os
from pathlib import Path
import subprocess
import tempfile

p=argparse.ArgumentParser()
p.add_argument("--ndk",type=Path,required=True)
p.add_argument("--adb",type=Path,required=True)
a=p.parse_args()
root=Path(__file__).resolve().parents[2]
source=root.parents[1]/"Beidou-Server"
tools=next((a.ndk/"toolchains/llvm/prebuilt").glob("darwin-*/bin"))
def run(args,**kw):
    return subprocess.run([str(x) for x in args],check=True,**kw)
remote=f"/data/local/tmp/mapledash-mcv-verify-{os.getpid()}"
files=[source/"clien/Data/Video"/name for name in ("galaxy-star-burst.mcv","lucid-bomb.mcv")]
with tempfile.TemporaryDirectory() as tmp:
    native=Path(tmp)/"check"
    host=Path(tmp)/"host"
    common=["-std=c++17","-O2","-I"+str(source/"tool/client-video"),root/"tools/mcv-playback-check/native_check.cpp",source/"tool/client-video/McvFormat.cpp"]
    lib=root/"Assets/Plugins/Android/arm64-v8a/libmapledash_mcv.so"
    run([tools/"aarch64-linux-android23-clang++",*common,"-static-libstdc++",lib,"-Wl,-rpath,$ORIGIN","-o",native])
    hostlib=root/"Assets/Plugins/macOS/libmapledash_mcv.dylib"
    run(["clang++",*common,hostlib,"-o",host])
    run([a.adb,"shell","mkdir",remote])
    try:
        run([a.adb,"push",native,lib,*files,remote+"/"],capture_output=True)
        run([a.adb,"shell","chmod","755",remote+"/check"])
        for file in files:
            expected=run([host,file],capture_output=True,text=True).stdout.strip()
            actual=run([a.adb,"shell",remote+"/check",remote+"/"+file.name],capture_output=True,text=True).stdout.strip()
            if actual!=expected: raise AssertionError((file.name,expected,actual))
            print(file.name,actual)
    finally:
        run([a.adb,"shell","rm","-f",remote+"/check",remote+"/libmapledash_mcv.so",*[remote+"/"+file.name for file in files]])
        run([a.adb,"shell","rmdir",remote])
print("MCV_ANDROID_HOST_PIXEL_HASH_PASS")
