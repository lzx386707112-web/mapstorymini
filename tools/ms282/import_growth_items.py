#!/usr/bin/env python3
"""Import the TMS 282 growth-system item resources into the runtime Data tree.

Two systems, two very different merge shapes:

* **轮回星火** (flame) — 2048716~2048799 live inside `Item/Consume/0204.img`,
  a shard this project already ships from GMS083 (777 items). Overwriting the
  shard would drop the 40 nodes only GMS083 has (02040045, 02040333, ...), so
  the flame nodes are merged *into* the existing tree instead. Their
  `info/icon` canvases are 1x1 `_outlink` stubs into
  `Item/Consume/_Canvas/0204.img`, which this project does not ship.
* **符文徽章** (symbol) — 282 keeps ARC/AUT under `Character/ArcaneForce/`
  and `Character/AuthenticForce/`, neither of which exists in GMS083. Whole
  files, same stub/`_Canvas` split.

Both are handled the same way at the pixel level: resolve every `_outlink`,
bake the real bitmap inline as ARGB4444 (format 1 — the only thing
`ImgFile.Rgba` decodes), and drop the link child. The result is a
self-contained structure file; no `_Canvas` tree has to ship.

Per AGENTS rule 11 every emitted IMG is written in the GMS key
`Assets/Scripts/Wz/ImgFile.cs` decrypts. The 282 sources are BMS-keyed, so
each file is transcoded (`bms_to_gms`) before it is parsed, which also gives
the serializer a GMS reader to encrypt strings with.

Usage:
  python3 tools/ms282/import_growth_items.py              # dry run, plan + verify
  python3 tools/ms282/import_growth_items.py --write      # write + register in NativeAssetIndex
  python3 tools/ms282/import_growth_items.py --only flame
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/ms282"))
sys.path.insert(0, str(ROOT.parent.parent / "BeiDou-Server/tool/wz-python"))

import bms_to_gms as codec  # noqa: E402
from import_androids import DEFAULT_DEST, DEFAULT_SRC, register  # noqa: E402
from img_bake import (  # noqa: E402
    PixelSource, RuntimeImgMirror, _mismatch_with_pixels, bake_tree,
    canvas_rgba_of, expected_tree_from, serialize, verify_file,
)
from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzCanvasProperty, WzSubProperty  # noqa: E402

GMS = WzKey.for_region("GMS")

# —————————————————————————— 轮回星火 ——————————————————————————

FLAME_REL = "Item/Consume/0204.img"
# 282 的星火卷轴段（強化/永遠的/黑色/深淵…）。上限 250 级装备、exGrade 阶数都在 info 里。
FLAME_LO, FLAME_HI = 2048716, 2048799
# 测试必用的几件：強力/永遠的、黑色/深淵（二选一确认框）。
FLAME_REQUIRED = (2048716, 2048717, 2048720, 2048753, 2048760, 2048761, 2048794)

# —————————————————————————— 符文徽章 ——————————————————————————
# 1712000-06 ARC（祕法符文）、1713000-05 AUT（真實符文）、1714000-01 Grand（豪華真實符文）。
# 客户端的 ARC 分片只有 7 件（没有 1712007），与 SymbolCatalog 的 14 条一致。
SYMBOL_FILES = (
    ["Character/ArcaneForce/0171%04d.img" % (2000 + i) for i in range(7)]
    + ["Character/AuthenticForce/0171%04d.img" % (3000 + i) for i in range(6)]
    + ["Character/AuthenticForce/0171%04d.img" % (4000 + i) for i in range(2)]
)


def load_282(src: Path, rel: str) -> WzImage:
    """Parse a 282 file after re-keying it to GMS (what the runtime reads)."""
    data, _ = codec.transcode((src / rel).read_bytes())
    image = WzImage.from_bytes(data, key=GMS, name=Path(rel).name)
    image.parse()
    return image


def canvas_paths(root: WzSubProperty, prefix: str = "") -> list[str]:
    out = []
    for child in root.children():
        path = f"{prefix}{child.name}"
        if isinstance(child, WzCanvasProperty):
            out.append(path)
        elif isinstance(child, WzSubProperty):
            out.extend(canvas_paths(child, path + "/"))
    return out


def assert_pixels(image: WzImage, names: list[str]) -> int:
    """Every named canvas must carry a decodable bitmap bigger than the 1x1 stub."""
    checked = 0
    for name in names:
        node = image.root.get(name)
        if node is None:
            raise SystemExit(f"{image.name}: canvas {name} missing")
        if node.get("_outlink") is not None or node.get("_inlink") is not None:
            raise SystemExit(f"{image.name}/{name}: link survived baking")
        if node.width <= 1 or node.height <= 1:
            raise SystemExit(f"{image.name}/{name}: still a {node.width}x{node.height} stub")
        checked += 1
    return checked


def rgba_snapshot(image: WzImage) -> dict[str, bytes]:
    """wzpy-side RGBA per canvas, taken *before* serialization.

    `encode_image_body` consumes the lazy payload (`_png_length` drops to 0),
    so `img_bake.check_canvas_pixels` silently skips everything except the
    canvases it just baked when called afterwards. Snapshot first, diff last.
    """
    out: dict[str, bytes] = {}

    def walk(node: WzSubProperty, prefix: str) -> None:
        for child in node.children():
            path = f"{prefix}/{child.name}" if prefix else child.name
            if isinstance(child, WzCanvasProperty):
                if child.get("_outlink") is not None or child.get("_inlink") is not None:
                    continue
                try:
                    out[path] = canvas_rgba_of(child)
                except Exception:                      # noqa: BLE001 - empty stub
                    continue
            elif isinstance(child, WzSubProperty):
                walk(child, path)

    walk(image.root, "")
    return out


def verify(image: WzImage, data: bytes, label: str,
           baseline: dict[str, bytes] | None = None) -> list[str]:
    expected = expected_tree_from(image)
    mirror = RuntimeImgMirror(data, GMS)
    problems = verify_file(data, expected, GMS, mirror=mirror)
    structural: list[str] = []
    _mismatch_with_pixels(expected, mirror.root, "<root>", structural)
    problems.extend(p for p in structural if p not in problems)

    actual: dict[str, bytes] = {}
    stack = [("", child) for child in mirror.root["children"]]
    while stack:
        path, node = stack.pop()
        # 路径必须带上节点自身的名字（root 的孩子 path=''，之后以 '/' 续），
        # 与 rgba_snapshot 的 "info/icon" 口径一致；check_canvas_pixels 里
        # 两侧同错的 flat() 掩盖了这个差别，这里不能沿用。
        full = f"{path}{node['name']}"
        if node.get("kind") == "Canvas":
            actual[full] = node.get("rgba")
        for child in node.get("children", []):
            stack.append((full + "/", child))

    compared = 0
    for path, want in (baseline or {}).items():
        got = actual.get(path)
        if got is None:
            problems.append(f"{path}: canvas missing after rewrite")
        elif len(want) != len(got):
            problems.append(f"{path}: pixel length {len(want)} != {len(got)}")
        elif want != got:
            diff = sum(1 for x, y in zip(want, got) if x != y)
            problems.append(f"{path}: pixels differ ({diff}/{len(want)} bytes)")
        else:
            compared += 1

    print(f"  {label}: {compared} canvas(es) pixel-identical, {len(problems)} problem(s)")
    for problem in problems[:12]:
        print(f"    ! {problem}")
    return problems


def import_flames(src: Path, dest: Path, write: bool) -> int:
    print(f"== {FLAME_REL}  (merge {FLAME_LO}..{FLAME_HI})")
    target = dest / FLAME_REL
    if not target.is_file():
        raise SystemExit(f"project shard missing: {target}")

    project = WzImage.from_bytes(target.read_bytes(), key=GMS, name="0204.img")
    project.parse()
    before = {child.name for child in project.root.children()}

    donor = load_282(src, FLAME_REL)
    flame = [child for child in donor.root.children()
             if child.name.isdigit() and FLAME_LO <= int(child.name) <= FLAME_HI]
    if not flame:
        raise SystemExit("282 shard has no flame items — upstream layout changed")
    missing = [str(i) for i in FLAME_REQUIRED if f"{i:08d}" not in {c.name for c in flame}]
    if missing:
        raise SystemExit(f"required flame items missing in 282 shard: {missing}")

    # Idempotent: drop any previous merge, then append the donor nodes.
    for child in flame:
        project.root._children.pop(child.name, None)
    stats = {"baked": 0, "pixels": 0}
    for child in flame:
        project.root._children[child.name] = child
        bake_tree(child, PixelSource(dest, GMS, fallback_root=src), stats)
    print(f"  merged {len(flame)} node(s), baked {stats['baked']} canvas(es), "
          f"{stats['pixels'] / 1048576:.2f} MiB pixels")

    kept = before & {c.name for c in project.root.children()}
    lost = before - kept
    if lost:
        raise SystemExit(f"GMS083-only nodes would be lost: {sorted(lost)}")

    for index in FLAME_REQUIRED:
        node = project.root.get(f"{index:08d}")
        icon = node.get("info/icon") if node is not None else None
        if icon is None or icon.width <= 1:
            raise SystemExit(f"{index}: icon did not bake")
    assert_pixels(project, [f"{c.name}/info/icon" for c in flame])

    baseline = rgba_snapshot(project)
    data = serialize(project)
    print(f"  size {(target.stat().st_size) / 1024:.0f} -> {len(data) / 1024:.0f} KiB")
    problems = verify(project, data, "0204.img", baseline)
    if problems:
        print("  FAILED — nothing written")
        return 1
    print(f"  flame canvas(es) verified: {len(flame) * 2}")
    if write:
        target.write_bytes(data)
        print(f"  wrote {FLAME_REL} ({len(data)} bytes)")
    return 0


def import_symbols(src: Path, dest: Path, write: bool) -> int:
    print(f"== 符文徽章  ({len(SYMBOL_FILES)} files)")
    source = PixelSource(dest, GMS, fallback_root=src)
    results: dict[str, bytes] = {}
    failures = 0
    for rel in SYMBOL_FILES:
        if not (src / rel).is_file():
            print(f"  {rel}: missing in the 282 tree")
            failures += 1
            continue
        image = load_282(src, rel)
        stats = {"baked": 0, "pixels": 0}
        bake_tree(image.root, source, stats)
        icons = canvas_paths(image.root)
        if "info/icon" not in icons:
            print(f"  {rel}: no info/icon")
            failures += 1
            continue
        if stats["baked"] == 0:
            print(f"  {rel}: nothing baked (already inline?)")
        try:
            assert_pixels(image, ["info/icon", "info/iconRaw"])
        except SystemExit as error:
            print(f"  {error}")
            failures += 1
            continue
        baseline = rgba_snapshot(image)
        data = serialize(image)
        problems = verify(image, data, Path(rel).name, baseline)
        if problems:
            failures += 1
            continue
        results[rel] = data

    if failures:
        print(f"  FAILED: {failures} file(s)")
        return 1
    if write:
        for rel, data in results.items():
            path = dest / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        added, updated = register(list(results), dest)
        print(f"  NativeAssetIndex.json: {added} added, {updated} refreshed, "
              f"{len(results)} total")
    else:
        print(f"  dry run: {len(results)} file(s) ready "
              f"({sum(len(v) for v in results.values()) / 1024:.1f} KiB)")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--src", type=Path, default=DEFAULT_SRC)
    ap.add_argument("--dest", type=Path, default=DEFAULT_DEST)
    ap.add_argument("--only", choices=("flame", "symbol"), help="run a single system")
    ap.add_argument("--write", action="store_true", help="write files + refresh NativeAssetIndex.json")
    args = ap.parse_args()
    src, dest = args.src.resolve(), args.dest.resolve()

    failures = 0
    if args.only in (None, "flame"):
        failures += import_flames(src, dest, args.write)
    if args.only in (None, "symbol"):
        failures += import_symbols(src, dest, args.write)
    if not args.write:
        print("dry run; pass --write to copy into", dest)
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
