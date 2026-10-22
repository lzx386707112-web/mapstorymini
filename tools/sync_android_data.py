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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--zip", action="store_true", help="transfer ZIP for in-game installation instead of expanded Data")
    parser.add_argument("--resources", type=Path, default=ROOT.parent / "MapleDashResources")
    parser.add_argument("--adb", default=str(Path.home() / "Library/Android/sdk/platform-tools/adb"))
    args = parser.parse_args()
    if args.zip:
        archive = args.resources / "MapleDashResources.zip"
        if not archive.is_file():
            raise FileNotFoundError(f"Build the resource pack first: {archive}")
        remote = "/sdcard/mapstory"
        subprocess.run([args.adb, "shell", "mkdir", "-p", remote], check=True)
        subprocess.run([args.adb, "push", str(archive), remote + "/MapleDashResources.zip"], check=True)
        print("ZIP transferred. Start the game; resource installation begins automatically.")
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
