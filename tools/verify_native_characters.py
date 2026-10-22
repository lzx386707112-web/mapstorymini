#!/usr/bin/env python3
"""Compare runtime avatar snapshots (NativeAssetChecks) against source wzpy composition."""
import argparse
import json
import sys
from pathlib import Path
from PIL import Image, ImageChops


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--data", type=Path, required=True)
    p.add_argument("--wzpy", type=Path, required=True)
    args = p.parse_args()
    sys.path.insert(0, str(args.wzpy))
    from wzpy import open_wz, WzImage, WzKey
    from wzpy.character import CharacterRenderer, DEFAULT_EAR_TYPE
    root = Path(__file__).resolve().parents[1]
    snapshots = root / "Library/NativeAssetChecks/avatars"
    records = json.loads((snapshots / "frames.json").read_text())
    recipes = json.loads((root / "Assets/Resources/NativeAssetIndex.json").read_text())["characters"]
    renderer = CharacterRenderer(open_wz(str(args.data / "Character"), region="GMS"))
    body = WzImage.from_file(str(args.data / "Character/00002000.img"), key=WzKey.for_region("GMS"))
    verified = 0
    for row in records:
        key, pose, frame = row["frame"].removeprefix("avatar://").split("/")
        ids = recipes[key]["ids"]
        frame = int(frame)
        for _ in range(64):
            n = body.get(f"{pose}/{frame}")
            action = n.get("action") if n is not None else None
            if action is None:
                break
            pose = action.value
            frame = getattr(n.get("frame"), "value", 0)
        weapon = renderer._open_part(ids[-1])
        if weapon.get(pose) is None and pose.startswith("swingO") and weapon.get(pose.replace("swingO", "swingT", 1)) is not None:
            pose = pose.replace("swingO", "swingT", 1)
        hidden, slots, tokens = renderer._cap_hair_filter(ids)
        parts = renderer._build_placements(ids, pose, DEFAULT_EAR_TYPE, hidden, slots, tokens, frame)
        base = next((part for part in parts if part.category == "Body" and part.name == "body"), None)
        dx, dy = base.map_anchors.get("navel", (0, 0)) if base else (0, 0)
        oracle = renderer._render_placements(parts, bbox=(-240-dx, -240-dy, 240-dx, 240-dy)) if parts else Image.new("RGBA", (480, 480))
        native = Image.new("RGBA", (480, 480))
        image = Image.open(snapshots / row["file"]).convert("RGBA")
        native.alpha_composite(image, (round(240-row["originX"]), round(240-row["originY"])))
        # Compare all channels; ImageChops.getbbox on RGBA alone ignores RGB-only differences.
        difference = ImageChops.difference(oracle, native)
        if any(channel.getbbox() is not None for channel in difference.split()):
            oracle.save(snapshots / "oracle-failure.png")
            native.save(snapshots / "native-failure.png")
            raise AssertionError(row["frame"])
        verified += 1
    print(f"PASS: {verified} avatar frames, all RGBA pixels and feet-relative placement identical to source compositor")


if __name__ == "__main__": main()
