#!/usr/bin/env python3
"""Sync the complete indexed native Data separately from the Android APK."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def resource_pack_files(resources):
    """按包号返回要传的资源包：有分包用分包，没有分包退回单包。"""
    parts = {}
    for path in resources.glob("MapleDashResources.part*.zip"):
        suffix = path.name[len("MapleDashResources.part"):-len(".zip")]
        if suffix.isdigit():
            parts[int(suffix)] = path
    if parts:
        return [parts[i] for i in sorted(parts)]
    single = resources / "MapleDashResources.zip"
    if single.is_file():
        return [single]
    raise FileNotFoundError(f"先打包资源：{single}（或用分包 MapleDashResources.part01.zip…）")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--zip", action="store_true", help="transfer ZIP for in-game installation instead of expanded Data")
    parser.add_argument("--resources", type=Path, default=ROOT.parent / "MapleDashResources")
    parser.add_argument("--adb", default=str(Path.home() / "Library/Android/sdk/platform-tools/adb"))
    args = parser.parse_args()
    if args.zip:
        archives = resource_pack_files(args.resources)
        remote = "/sdcard/mapstory"
        subprocess.run([args.adb, "shell", "mkdir", "-p", remote], check=True)
        total = sum(path.stat().st_size for path in archives)
        print(f"传输 {len(archives)} 个资源包，合计 {total / 1024 ** 3:.2f} GiB", flush=True)
        for path in archives:
            print(f"  {path.name}  {path.stat().st_size / 1024 ** 3:.2f} GiB", flush=True)
            subprocess.run([args.adb, "push", str(path), f"{remote}/{path.name}"], check=True)
        # 清掉上一次留下、这次不在清单里的包：包数对不上时手机端会直接拒绝解压。
        listing = subprocess.run([args.adb, "shell", "ls", remote],
                                 capture_output=True, text=True, check=True).stdout.split()
        wanted = {path.name for path in archives}
        stale = [name for name in listing
                 if (name == "MapleDashResources.zip" or name.startswith("MapleDashResources.part"))
                 and name.endswith(".zip") and name not in wanted]
        for name in stale:
            print(f"  删除旧包 {name}", flush=True)
            subprocess.run([args.adb, "shell", "rm", "-f", f"{remote}/{name}"], check=True)
        print("资源包已传输。启动游戏后会自动逐包解压；缺包会在开始解压前报错。")
        return
    index_path = ROOT / "Assets/Resources/NativeAssetIndex.json"
    index_bytes = index_path.read_bytes()
    files = json.loads(index_bytes)["files"]
    source = args.resources / "Data"
    stage = ROOT / "Library/AndroidNativeData/Data"
    # Hard links avoid duplicating 7 GB while excluding Unity .meta and backup files.
    for entry in files:
        rel = Path(entry["path"])
        if rel.is_absolute() or ".." in rel.parts:
            raise ValueError(f"Invalid indexed path: {rel}")
        src, dst = source / rel, stage / rel
        if not src.is_file():
            raise FileNotFoundError(src)
        dst.parent.mkdir(parents=True, exist_ok=True)
        if dst.exists() and os.path.samefile(src, dst):
            continue
        if dst.exists():
            dst.unlink()
        os.link(src, dst)
    adb = args.adb
    remote = "/sdcard/mapstory"
    def run(*command):
        subprocess.run([adb, *command], check=True)
    run("shell", "am", "force-stop", "com.mapledash.game")
    run("shell", "mkdir", "-p", remote)
    run("shell", "rm", "-f", remote + "/NativeAssetIndex.sha256")
    print(f"Syncing {len(files)} native resource files (first transfer about 6.8 GiB)…", flush=True)
    run("push", "--sync", str(stage), remote + "/")
    # Publish a receipt only after the entire sync succeeds. The app checks it at boot.
    with tempfile.TemporaryDirectory() as tmp:
        receipt = Path(tmp) / "NativeAssetIndex.sha256"
        receipt.write_text(hashlib.sha256(index_bytes).hexdigest() + "\n")
        run("push", str(receipt), remote + "/NativeAssetIndex.sha256")
    print("Native Data sync complete.", flush=True)


if __name__ == "__main__":
    main()
