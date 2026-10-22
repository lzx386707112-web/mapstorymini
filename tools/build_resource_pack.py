#!/usr/bin/env python3
"""Build a portable ZIP64 resource pack from the external native Data directory."""
import argparse
import hashlib
import json
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--resources", type=Path, default=ROOT.parent / "MapleDashResources")
    args = parser.parse_args()
    manifest = (ROOT / "Assets/Resources/NativeAssetIndex.json").read_bytes()
    index = json.loads(manifest)
    output = args.resources / "MapleDashResources.zip"
    pending = output.with_suffix(".zip.partial")
    with ZipFile(pending, "w", compression=ZIP_DEFLATED, compresslevel=1, allowZip64=True) as archive:
        archive.writestr("resource-manifest.json", manifest)
        for i, entry in enumerate(index["files"]):
            source = args.resources / "Data" / entry["path"]
            if hashlib.sha256(source.read_bytes()).hexdigest() != entry["sha256"]:
                raise ValueError(f"Resource hash mismatch: {source}")
            archive.write(source, "Data/" + entry["path"])
            if i % 5000 == 0:
                print(f"Packing {i}/{len(index['files'])}", flush=True)
    pending.replace(output)
    (args.resources / "resource-manifest.json").write_bytes(manifest)
    print(f"Resource pack: {output} ({output.stat().st_size / 1024**3:.2f} GiB)")


if __name__ == "__main__":
    main()
