#!/usr/bin/env python3
"""Export original SoloMapling CSV, YAML, binary movement and text resources for IL2CPP.

Requires PyYAML. YAML is parsed at export time; runtime only reads JSON.
Original resource paths, text and SHA256 are retained for source comparison.
This does not alter source scripts, images or game rules.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_SOURCE = ROOT.parent.parent / "BeiDou-Server/gms-server/src/main/java/soloMapling"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--out", type=Path, default=ROOT / "Assets/Resources/Bot/SoloMapling.json")
    parser.add_argument("--verify-only", action="store_true", help="Check packed bytes and hashes against original files without writing")
    args = parser.parse_args()
    if args.verify_only:
        resources = json.loads(args.out.read_text(encoding="utf-8"))["resources"]
        source_paths = {
            "soloMapling/" + p.relative_to(args.source).as_posix(): p
            for p in args.source.rglob("*")
            if p.is_file() and p.suffix in {".csv", ".yaml", ".txt", ".bin"}
            and not p.relative_to(args.source).as_posix().startswith("Documents/")
        }
        if resources.keys() != source_paths.keys():
            raise ValueError("Resource file set differs from source")
        for name, path in source_paths.items():
            raw = path.read_bytes()
            entry = resources[name]
            if base64.b64decode(entry["base64"], validate=True) != raw or hashlib.sha256(raw).hexdigest() != entry["sha256"]:
                raise ValueError("Original byte/hash mismatch: " + name)
            if path.suffix == ".yaml":
                try:
                    decoded = raw.decode("utf-8-sig")
                except UnicodeDecodeError:
                    if "data" in entry:
                        raise ValueError("Binary YAML must not contain invented parsed data: " + name)
                else:
                    if entry.get("data") != yaml.load(decoded, Loader=yaml.BaseLoader):
                        raise ValueError("YamlBeans scalar/structure mismatch: " + name)
        print(f"Verified {len(resources)} original resources byte-for-byte, SHA256 and YAML scalar semantics")
        return
    resources = {}
    for path in sorted(args.source.rglob("*")):
        if not path.is_file() or path.suffix not in {".csv", ".yaml", ".txt", ".bin"}:
            continue
        rel = path.relative_to(args.source).as_posix()
        if rel.startswith("Documents/"):
            continue
        raw = path.read_bytes()
        # Keep exact bytes too: decoded text alone would discard a UTF-8 BOM.
        entry = {"sha256": hashlib.sha256(raw).hexdigest(), "base64": base64.b64encode(raw).decode("ascii")}
        if path.suffix == ".bin":
            resources["soloMapling/" + rel] = entry
            continue
        try:
            text = raw.decode("utf-8-sig")
        except UnicodeDecodeError:
            entry["error"] = "Source resource is not valid UTF-8; original bytes preserved, restoration required"
            print(f"SOURCE ERROR: {rel} is not UTF-8; awaiting original resource restoration")
        else:
            entry["text"] = text
            if path.suffix == ".yaml":
                # YamlReader.read() without a declared type returns scalar Strings.
                # SafeLoader's YAML 1.1 integers corrupt zero-prefixed Maple IDs
                # (01002026 becomes octal 263190); consumers parse decimal explicitly.
                entry["data"] = yaml.load(text, Loader=yaml.BaseLoader)
        resources["soloMapling/" + rel] = entry
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps({"version": 1, "resources": resources}, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
    print(f"Exported {len(resources)} original resources to {args.out}")


if __name__ == "__main__":
    main()
