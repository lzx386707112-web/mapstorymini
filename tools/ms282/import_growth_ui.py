#!/usr/bin/env python3
"""Import the TMS 282 growth-system UI windows into this project's runtime Data tree.

Three windows are needed by the migration (see docs/tms282-growth-systems.md):

  UI/UIArcaneForce.img      ArcEquip   -> 祕法符文(ARC)徽章窗
  UI/UIAuthenticForce.img   AutEquip   -> 真實符文(AUT)徽章窗
  UI/UIWindow4.img          HyperStat/Window -> 极限属性窗, AbilitySetting -> 内在能力窗

The 282 client splits every window into structure (`UI/<Name>.img`, canvas
stubs with `_outlink`) and pixels (`UI/_Canvas/<Name>.img`, hundreds of MB
across ~50 files). This importer does NOT reproduce that split: it bakes the
pixels of the drawn branches straight into the structure file and trims the
branches this client never draws (see img_bake.py / consolidate_ui.py).
Output = one self-contained file per window, verified canvas-by-canvas
against an ImgFile.cs byte-mirror before anything is written.

`UI/Enchant.img` (six enhancement tabs incl. bonusStat/soulWeapon) is NOT
imported here — it is produced by `import_enhancement_ui.py`, which inlines
its own closure with ARGB4444 re-encoding.

Per AGENTS rule 11 each IMG is re-encoded from the 282 BMS key to the GMS
key this project's reader decrypts.

Usage:
  python3 tools/ms282/import_growth_ui.py            # dry run, verify only
  python3 tools/ms282/import_growth_ui.py --write     # write + register in NativeAssetIndex
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/ms282"))
sys.path.insert(0, str(ROOT.parent.parent / "BeiDou-Server/tool/wz-python"))

import bms_to_gms as codec  # noqa: E402
from img_bake import (  # noqa: E402
    PixelSource, RuntimeImgMirror, bake_tree, check_canvas_pixels,
    expected_tree_from, serialize, trim_root, verify_file, _mismatch_with_pixels,
)
from wzpy import WzImage, WzKey  # noqa: E402

DEFAULT_SRC = Path.home() / "Documents/mxd/MapleStory/Data"
DEFAULT_DEST = ROOT.parent / "MapleDashResources/Data"
INDEX_PATH = ROOT / "Assets/Resources/NativeAssetIndex.json"

# (源相对路径, 保留分支或 None=全部, 丢弃的子树)。与 consolidate_ui.py 的 JOBS 保持一致。
#
# VMatrixUI / HexaMatrixUI 是五六转重做新增的两扇窗（docs/tms282-skill/20-v-hexa-panel.md）：
# 顶层分支照原样保留窗口本体与弹窗，只丢掉三个挂在 Effect/particle.img 上的粒子，
# 那三块是纯装饰、且会把整条粒子序列拉进包里（几 MB 却一个像素都用不到）。
WINDOWS = [
    ("UI/UIArcaneForce.img", ["ArcEquip"], []),
    ("UI/UIAuthenticForce.img", ["AutEquip"], []),
    ("UI/UIWindow4.img", ["HyperStat", "AbilitySetting"], []),
    ("UI/VMatrixUI.img",
     ["core", "main", "vSkillUpgradePopUp", "specialCorePopUp", "coreGemStoneOpen", "vPointTransform"],
     ["main/vMatrixMain", "main/vMatrixSpecialCoreOff", "main/vMatrixSpecialCoreOn"]),
    ("UI/HexaMatrixUI.img",
     ["core", "main", "hexaSkill", "hexaSkillPopUp", "hexaStat", "hexaStatPopUp",
      "solErdaMini", "solErdaTransform"],
     ["main/incMainSolErdaEffect", "main/incExpSolErdaEffect"]),
]


def drop_paths(root, paths: list[str]) -> list[str]:
    """按 a/b/c 路径删子树（trim_root 只处理顶层，粒子和特效都在第二层）。"""
    dropped = []
    for path in paths:
        parts = path.split("/")
        node = root
        for part in parts[:-1]:
            node = node._children.get(part) if node is not None else None
        if node is not None and parts[-1] in node._children:
            del node._children[parts[-1]]
            dropped.append(path)
    return dropped


def register(paths: list[str], dest: Path) -> tuple[int, int]:
    native = json.loads(INDEX_PATH.read_bytes())
    files = native["files"]
    by_path = {entry["path"]: entry for entry in files}
    added = updated = 0
    for rel in paths:
        digest = hashlib.sha256((dest / rel).read_bytes()).hexdigest()
        current = by_path.get(rel)
        if current is None:
            entry = {"path": rel, "sha256": digest}
            by_path[rel] = entry
            files.append(entry)
            added += 1
        elif current.get("sha256") != digest:
            current["sha256"] = digest
            updated += 1
    INDEX_PATH.write_text(json.dumps(native, ensure_ascii=False, indent=1) + "\n")
    return added, updated


def load_source(path: Path) -> WzImage:
    data, _ = codec.transcode(path.read_bytes())
    image = WzImage.from_bytes(data, key=WzKey.for_region("GMS"), name=path.name)
    image.parse()
    if image.truncated or image.parse_warnings:
        raise SystemExit(f"{path.name}: incomplete IMG: {image.parse_warnings}")
    return image


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--src", type=Path, default=DEFAULT_SRC)
    ap.add_argument("--dest", type=Path, default=DEFAULT_DEST)
    ap.add_argument("--write", action="store_true", help="write files and register them")
    args = ap.parse_args()
    src, dest = args.src.resolve(), args.dest.resolve()
    if not (src / "UI").is_dir():
        raise SystemExit(f"282 data tree not found: {src}")

    key = WzKey.for_region("GMS")
    pixels = PixelSource(dest, key, fallback_root=src)
    failures = 0
    outputs: dict[str, bytes] = {}

    for rel, keep, drops in WINDOWS:
        source = src / rel
        if not source.is_file():
            raise SystemExit(f"missing source {rel}")
        image = load_source(source)
        dropped = trim_root(image.root, keep)
        dropped += drop_paths(image.root, drops)
        stats = {"baked": 0, "pixels": 0}
        bake_tree(image.root, pixels, stats)
        data = serialize(image)
        expected = expected_tree_from(image)
        mirror = RuntimeImgMirror(data, key)
        problems = verify_file(data, expected, key, mirror=mirror)
        struct_problems: list[str] = []
        _mismatch_with_pixels(expected, mirror.root, "<root>", struct_problems)
        problems.extend(p for p in struct_problems if p not in problems)
        checked = check_canvas_pixels(expected, mirror.root, problems)
        size_old = source.stat().st_size
        print(f"{rel}: keep {keep} drop {drops or '-'}  baked {stats['baked']} canvas(es) "
              f"({stats['pixels'] / 1048576:.2f} MiB pixels)")
        print(f"  {size_old / 1048576:.1f} MiB source (structure+pixels) -> "
              f"{len(data) / 1048576:.2f} MiB self-contained; "
              f"{checked} canvas(es) pixel-identical, {len(problems)} problem(s)")
        for p in problems[:10]:
            print(f"    ! {p}")
        if problems:
            failures += 1
            continue
        outputs[rel] = data

    if failures:
        print("FAILURES — nothing written")
        raise SystemExit(1)
    if not args.write:
        print("dry run; pass --write to write and register")
        return

    for rel, data in outputs.items():
        target = dest / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
    added, updated = register(list(outputs), dest)
    print(f"wrote {len(outputs)} file(s); index added {added}, updated {updated}")


if __name__ == "__main__":
    main()
