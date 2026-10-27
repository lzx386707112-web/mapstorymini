#!/usr/bin/env python3
"""Reproducible ARM64 Android VP8/VP9 decoder plugin (NDK clang, API 23)."""
import argparse
import hashlib
import os
from pathlib import Path
import subprocess
import tarfile
import urllib.request

VERSION="1.15.2"
SHA256="26fcd3db88045dee380e581862a6ef106f49b74b6396ee95c2993a260b4636aa"
p=argparse.ArgumentParser()
p.add_argument("--platform",choices=["android","macos"],default="android")
p.add_argument("--ndk",type=Path)
p.add_argument("--cache",type=Path,default=Path.home()/".cache/mapledash-mcv")
args=p.parse_args()
root=Path(__file__).resolve().parents[2]
cache=args.cache.resolve();cache.mkdir(parents=True,exist_ok=True)
archive=cache/f"libvpx-{VERSION}.tar.gz"
if not archive.exists():
    urllib.request.urlretrieve(f"https://github.com/webmproject/libvpx/archive/refs/tags/v{VERSION}.tar.gz",archive)
if hashlib.sha256(archive.read_bytes()).hexdigest()!=SHA256:
    raise SystemExit("libvpx archive SHA256 mismatch")
source=cache/f"libvpx-{VERSION}"
if not source.exists():
    with tarfile.open(archive) as package: package.extractall(cache,filter="data")
common=["--enable-pic","--disable-shared","--disable-examples","--disable-tools","--disable-docs",
        "--disable-unit-tests","--disable-vp8-encoder","--disable-vp9-encoder"]
if args.platform == "macos":
    outputs=[]
    for arch in ("arm64","x86_64"):
        build=cache/("mac-"+arch);build.mkdir(exist_ok=True)
        env=os.environ.copy()
        env.update(CC="clang -arch "+arch,CXX="clang++ -arch "+arch,LD="clang -arch "+arch)
        subprocess.run([str(source/"configure"),"--target=generic-gnu",*common],cwd=build,env=env,check=True)
        subprocess.run(["make","-j8"],cwd=build,env=env,check=True)
        output=build/"libmapledash_mcv.dylib";outputs.append(str(output))
        subprocess.run(["clang++","-arch",arch,"-std=c++17","-O2","-dynamiclib","-fvisibility=hidden",
            "-I"+str(source),"-I"+str(build),str(root/"Native/Mcv/mcv_decoder.cpp"),str(build/"libvpx.a"),
            "-o",str(output)],check=True)
    out=root/"Assets/Plugins/macOS/libmapledash_mcv.dylib";out.parent.mkdir(parents=True,exist_ok=True)
    subprocess.run(["lipo","-create",*outputs,"-output",str(out)],check=True)
    (root/"Native/Mcv/LICENSE.libvpx").write_bytes((source/"LICENSE").read_bytes())
    print("MACOS_MCV_PLUGIN_BUILT",out)
    raise SystemExit(0)
if args.ndk is None: p.error("--ndk is required for Android")
build=cache/"android-arm64";build.mkdir(exist_ok=True)
prebuilt=args.ndk.resolve()/"toolchains/llvm/prebuilt"
tool=next(prebuilt.glob("darwin-*/bin"),None) or next(prebuilt.glob("linux-*/bin"),None)
if tool is None: raise SystemExit("NDK LLVM host tools not found")
env=os.environ.copy()
env.update(CC=str(tool/"aarch64-linux-android23-clang"),CXX=str(tool/"aarch64-linux-android23-clang++"),
           AR=str(tool/"llvm-ar"),AS=str(tool/"clang"),LD=str(tool/"aarch64-linux-android23-clang"),
           STRIP=str(tool/"llvm-strip"),NM=str(tool/"llvm-nm"),RANLIB=str(tool/"llvm-ranlib"))
subprocess.run([str(source/"configure"),"--target=arm64-android-gcc","--enable-pic","--disable-shared",
    "--disable-examples","--disable-tools","--disable-docs","--disable-unit-tests",
    "--disable-vp8-encoder","--disable-vp9-encoder"],cwd=build,env=env,check=True)
subprocess.run(["make","-j8"],cwd=build,env=env,check=True)
out=root/"Assets/Plugins/Android/arm64-v8a/libmapledash_mcv.so"
out.parent.mkdir(parents=True,exist_ok=True)
subprocess.run([env["CXX"],"-std=c++17","-O2","-fPIC","-shared","-fvisibility=hidden",
    "-static-libstdc++","-Wl,-soname,libmapledash_mcv.so","-Wl,-z,max-page-size=16384","-Wl,-z,common-page-size=16384",
    "-I"+str(source),"-I"+str(build),str(root/"Native/Mcv/mcv_decoder.cpp"),str(build/"libvpx.a"),
    "-o",str(out)],check=True)
subprocess.run([env["STRIP"],str(out)],check=True)
(root/"Native/Mcv/LICENSE.libvpx").write_bytes((source/"LICENSE").read_bytes())
print("ANDROID_MCV_PLUGIN_BUILT",out)
