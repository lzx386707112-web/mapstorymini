#!/usr/bin/env python3
"""Import the TMS 282 android (robot) + heart + appearance-template resources
into this project's runtime Data tree.

Per AGENTS rule 11 every IMG is re-encoded from the 282 BMS key to the GMS key
this project's reader (Assets/Scripts/Wz/ImgFile.cs) decrypts, and pixel formats
are asserted to be ARGB4444/RGB565/ARGB1555 only (the formats ImgFile.Rgba can
decode).

Sources of the seed set:
  Character/Android/*.img         268 robot bodies + 56 hearts (hearts overwrite
                                  the 56 empty beidou shells already present)
  Etc/Android/*.img               248 appearance templates (pure data, no pixels)
  robot appearance pools          any hair/face/skin/default-clothing id a
                                  template references but this project lacks

Every seed is expanded to its `_outlink` closure, because 282 keeps character
pixels in a sibling `_Canvas/` tree.

Usage:
  python3 tools/ms282/import_androids.py            # dry run, report only
  python3 tools/ms282/import_androids.py --write     # copy + register in NativeAssetIndex
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/ms282"))
sys.path.insert(0, str(ROOT.parent.parent / "BeiDou-Server/tool/wz-python"))

import bms_to_gms as codec  # noqa: E402
from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzCanvasProperty  # noqa: E402

DEFAULT_SRC = Path.home() / "Documents/mxd/MapleStory/Data"
DEFAULT_DEST = ROOT.parent / "MapleDashResources/Data"
INDEX_PATH = ROOT / "Assets/Resources/NativeAssetIndex.json"
REPORT_PATH = ROOT.parent / "docs/tms282-skill/data/android-import-plan.tsv"
TEMPLATES_JSON = ROOT / "Assets/Resources/Bot/AndroidTemplates.json"
SUPPORTED_FORMATS = {1, 2, 513}

# 1662303/1662304 是 `androidAni` 型机器人：外观走 8bpp（format 4098）预渲染动画帧，
# 不是纸娃娃部件合成，282 只有这两件（模板 237/238）。ImgFile.Rgba 不支持 8bpp，
# 导入后一旦装备就会抛异常，所以整件跳过（含其 _Canvas）。见 11 文档 §5 风险表。
EXCLUDED = {"Character/Android/01662303.img", "Character/Android/01662304.img"}

# 模板 basic 节点名 -> Character 子目录
BASIC_FOLDERS = {
    "accessory": "Accessory", "longcoat": "Longcoat", "shoes": "Shoes",
    "cap": "Cap", "cape": "Cape", "gloves": "Glove", "glove": "Glove",
    "eyeAccessory": "EyeAccessory", "faceAccessory": "FaceAccessory",
}


def children(node):
    return list(node.children()) if node is not None else []


def sub(node, name):
    for child in children(node):
        if str(child.name) == name:
            return child
    return None


def ints(node):
    out = []
    for child in children(node):
        try:
            out.append(int(getattr(child, "value", None)))
        except (TypeError, ValueError):
            continue
    return out


def read_templates(src: Path) -> dict:
    """Parse every Etc/Android template: appearance pools for the importer, plus
    the full record (基本信息/默认穿着/随机池/事件台词) for the runtime JSON."""
    pools = {"hair": set(), "face": set(), "skin": set()}
    basics = {folder: set() for folder in BASIC_FOLDERS.values()}
    templates, records = 0, {}
    for path in sorted((src / "Etc/Android").glob("*.img")):
        image = WzImage.from_bytes(path.read_bytes(), key=WzKey.for_region("BMS"), name=path.name)
        image.parse()
        if image.truncated:
            raise ValueError(f"truncated template {path}")
        info, costume, basic, action = (sub(image.root, key) for key in ("info", "costume", "basic", "action"))

        def scalars(node):
            out = {}
            for child in children(node):
                value = getattr(child, "value", None)
                if isinstance(value, (int, str)) and not isinstance(value, bool):
                    out[str(child.name)] = value
            return out

        actions = {}
        for group in children(action):
            entries = []
            for variant in children(group):
                entry = scalars(variant)
                if entry:
                    entries.append(entry)
            if entries:
                actions[str(group.name)] = entries

        records[str(int(path.stem))] = {
            "info": scalars(info),
            "basic": scalars(basic),
            "skin": ints(sub(costume, "skin")),
            "hair": ints(sub(costume, "hair")),
            "face": ints(sub(costume, "face")),
            "actions": actions,
        }
        pools["hair"].update(ints(sub(costume, "hair")))
        pools["face"].update(ints(sub(costume, "face")))
        pools["skin"].update(ints(sub(costume, "skin")))
        for child in children(basic):
            folder = BASIC_FOLDERS.get(str(child.name))
            if folder:
                try:
                    basics[folder].add(int(getattr(child, "value", None)))
                except (TypeError, ValueError):
                    pass
        templates += 1
    skins = {"Character/%08d.img" % i for i in pools["skin"]}
    equipment = {f"Character/{folder}/{i:08d}.img" for folder, ids in basics.items() for i in ids}
    return {
        "templates": templates,
        "records": records,
        "hair": {"Character/Hair/%08d.img" % i for i in pools["hair"]},
        "face": {"Character/Face/%08d.img" % i for i in pools["face"]},
        "skin": skins,
        "equipment": equipment,
    }


def outlinks(src: Path, rel: str) -> list[str]:
    """Relative IMG paths this file's `_outlink` nodes point at."""
    image = WzImage.from_bytes((src / rel).read_bytes(), key=WzKey.for_region("BMS"), name=Path(rel).name)
    image.parse()
    found = set()

    def walk(node):
        for name in ("_outlink", "_inlink"):
            link = node.get(name) if hasattr(node, "get") else None
            value = getattr(link, "value", None)
            if isinstance(value, str) and ".img/" in value:
                found.add(value.split(".img/", 1)[0] + ".img")
        for child in children(node):
            walk(child)

    walk(image.root)
    return sorted(found - {rel})


def collect(src: Path, seeds: dict[str, str]) -> tuple[list[str], dict[str, list[str]], list[str]]:
    """Expand seeds through `_outlink`; returns ordered files, missing, reasons."""
    order, seen, missing = [], set(), []
    reasons: dict[str, list[str]] = {}
    queue = list(seeds)
    while queue:
        rel = queue.pop(0)
        if rel in seen:
            reasons[rel].append(seeds.get(rel, "dependency"))
            continue
        if not (src / rel).is_file():
            missing.append(rel)
            continue
        seen.add(rel)
        order.append(rel)
        reasons[rel] = [seeds.get(rel, "dependency")]
        for target in outlinks(src, rel):
            if target in seen:
                reasons[target].append(f"outlink:{rel}")
                continue
            reasons.setdefault(target, []).append(f"outlink:{rel}")
            seeds.setdefault(target, f"outlink:{rel}")
            queue.append(target)
    return order, reasons, missing


def verify_imported(raw: bytes, data: bytes, rel: str, strict: bool) -> tuple[Counter, list[str]]:
    """Structural equality against the BMS original plus pixel-format support.

    ``strict`` is set for the robot/heart files themselves: every canvas there is
    reached by the android renderer, so an unsupported format has to fail the
    import. Appearance dependencies (hair/face/skin/clothing) carry unrelated
    canvases (e.g. `PBTuTu` previews at format 2050) that the puppet never reads,
    so those only warn.
    """
    codec.verify(raw, data, Path(rel).name)
    image = WzImage.from_bytes(data, key=WzKey.for_region("GMS"), name=Path(rel).name)
    image.parse()
    if image.truncated or image.parse_warnings:
        raise ValueError(f"{rel}: transcoded image reports {image.parse_warnings}")
    formats, unsupported = Counter(), []

    def walk(node, path):
        if isinstance(node, WzCanvasProperty):
            fmt = node.format + node.format2
            formats[fmt] += 1
            if fmt not in SUPPORTED_FORMATS:
                unsupported.append(f"{path}/{node.name}")
                if strict:
                    raise ValueError(f"{rel}: canvas format {fmt} is not decodable")
        for child in children(node):
            walk(child, path + "/" + str(node.name))

    walk(image.root, "")
    return formats, unsupported


def register(paths: list[str], dest: Path) -> tuple[int, int]:
    """Append/refresh `files` entries; keeps the checked-in indent=1 layout."""
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


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--src", type=Path, default=DEFAULT_SRC)
    ap.add_argument("--dest", type=Path, default=DEFAULT_DEST)
    ap.add_argument("--write", action="store_true", help="copy files and register them in NativeAssetIndex.json")
    ap.add_argument("--report", type=Path, default=REPORT_PATH)
    args = ap.parse_args()
    src, dest = args.src.resolve(), args.dest.resolve()
    if not (src / "Etc/Android").is_dir():
        raise SystemExit(f"282 data tree not found: {src}")

    import csv

    seeds: dict[str, str] = {}
    for folder in ("Character/Android", "Etc/Android"):
        for path in sorted((src / folder).glob("*.img")):
            if path.stem.isdigit():
                rel = f"{folder}/{path.name}"
                if rel in EXCLUDED:
                    continue
                seeds[rel] = "robot/heart" if "Character" in folder else "template"

    pools = read_templates(src)
    appearance = {"hair": 0, "face": 0, "skin": 0, "equipment": 0}
    for key in ("hair", "face", "skin", "equipment"):
        if key == "skin":
            # 282 的皮肤主体画布（Character/_Canvas/0000NNNN.img）每个 ~10 MiB，其中
            # 只有 stand/walk 分支是机器人用得到的，其余是 Genos/PBTuTu 等无关动画，
            # 而本文档无法在不重写 WZ 节点树的前提下裁剪。缺的 3 个肤色改由运行时
            # 兜底到项目已有的 2000（肤色差异，视觉影响可忽略），省下 40 MiB。
            continue
        for rel in sorted(pools[key]):
            if (dest / rel).is_file():
                continue                      # 项目已有同 id 资源，优先用已转码的 GMS 版本
            if (src / rel).is_file():
                seeds[rel] = f"appearance/{key}"
                appearance[key] += 1

    print(f"templates {pools['templates']}: "
          f"need hair {len(pools['hair'])} face {len(pools['face'])} skin {len(pools['skin'])} "
          f"default-clothing {len(pools['equipment'])}")
    print(f"appearance gaps to import: {appearance}")

    order, reasons, missing = collect(src, seeds)
    print(f"seed set {len(seeds)}, with _outlink closure {len(order)} files, missing {len(missing)}")
    if missing:
        print("missing in 282 tree (skipped):")
        for rel in missing[:20]:
            print("  ", rel)

    rows, total_bytes, formats, failures, warnings = [], 0, Counter(), [], []
    for rel in order:
        raw = (src / rel).read_bytes()
        strict = rel.startswith("Character/Android/")
        try:
            data, walker = codec.transcode(raw)
            file_formats, unsupported = verify_imported(raw, data, rel, strict)
            formats.update(file_formats)
            if unsupported:
                warnings.append((rel, len(unsupported), sorted({w.rsplit("/", 0)[0] for w in unsupported})[:1]))
        except Exception as error:                      # noqa: BLE001 - report and continue
            failures.append((rel, f"{type(error).__name__}: {error}"))
            continue
        total_bytes += len(data)
        rows.append({
            "path": rel, "bytes": len(data), "strings": len(walker.sites),
            "status": "overwrite" if (dest / rel).is_file() else "new",
            "reason": ";".join(sorted(set(reasons.get(rel, []))))[:160],
        })

    print(f"transcoded {len(rows)} files, {total_bytes / 1048576:.2f} MiB, pixel formats {dict(formats)}")
    if warnings:
        print(f"unsupported canvases kept in {len(warnings)} appearance files (never read by the puppet):")
        for rel, count, sample in warnings[:10]:
            print(f"   {rel}: {count} canvases, e.g. {sample[0] if sample else ''}")
    if failures:
        print(f"FAILED {len(failures)}:")
        for rel, message in failures[:20]:
            print("  ", rel, message)
        raise SystemExit(1)

    args.report.parent.mkdir(parents=True, exist_ok=True)
    with args.report.open("w", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=["path", "bytes", "strings", "status", "reason"], delimiter="\t")
        writer.writeheader()
        writer.writerows(rows)
    print("report:", args.report)

    if not args.write:
        print("dry run; pass --write to copy into", dest)
        return

    for row in rows:
        rel = row["path"]
        target = dest / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        data, _ = codec.transcode((src / rel).read_bytes())
        target.write_bytes(data)
    added, updated = register([row["path"] for row in rows], dest)
    print(f"wrote {len(rows)} files; NativeAssetIndex files +{added} refreshed {updated} "
          f"(total {len(json.loads(INDEX_PATH.read_bytes())['files'])})")

    # 外观模板池要落成 JSON：服务端层（Assets/Scripts/Server）是纯 C#，只能像
    # Bot/Items.json 那样读文本资源，不能解析 IMG。
    TEMPLATES_JSON.parent.mkdir(parents=True, exist_ok=True)
    TEMPLATES_JSON.write_text(json.dumps({"version": 1, "templates": pools["records"]},
                                         ensure_ascii=False, separators=(",", ":")) + "\n")
    print(f"templates json: {TEMPLATES_JSON} "
          f"({TEMPLATES_JSON.stat().st_size / 1024:.0f} KiB, {len(pools['records'])} templates)")


if __name__ == "__main__":
    main()
