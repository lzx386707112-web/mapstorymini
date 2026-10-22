#!/usr/bin/env python3
"""Copy the complete unchanged Data directory and index native resource aliases.
No PNG/atlas/character frame export. Legacy UI aliases are bootstrapped by exact
pixel matching; the resulting index contains source paths, never pixel data.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import sys
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "Assets/Resources/Art"


def is_backup(path):
    return any(part in (".DS_Store", "Thumbs.db") or part.startswith("._") or part.endswith("~") or any(token.startswith("bak") or token == "backup" or token.endswith("-bak")
               for token in part.lower().split(".")[1:]) for part in Path(path).parts)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=ROOT.parent / "MapleDashResources")
    parser.add_argument("--wzpy", type=Path, required=True)
    args = parser.parse_args()
    sys.path.insert(0, str(args.wzpy))
    sys.path.insert(0, str(ROOT / "tools"))
    from wzpy import WzImage, WzKey
    from wzpy.properties import WzCanvasProperty
    from wzpy.canvas import decode_canvas
    from wzpy.character import CATEGORY_DIR, category_for_id, _DEFAULT_ZMAP, _BACK_FACING_Z_OVERRIDE, _BACK_FACING_ABSOLUTE_Z, _OVER_CAP_REMAP
    from wz_actions import CHARS, HAIR_EXTRA, SKILL_KEYS
    data = args.data.resolve()
    manifest = json.loads((ART / "manifest.json").read_text())
    images = {}
    missing_outlinks = set()
    index_path = ROOT / "Assets/Resources/NativeAssetIndex.json"
    aliases = json.loads(index_path.read_text())["aliases"] if index_path.exists() else {}
    def image(rel):
        if rel not in images:
            obj = WzImage.from_bytes((data / rel).read_bytes(), key=WzKey.for_region("GMS"), name=rel)
            obj.parse()
            if obj.truncated or obj.parse_warnings:
                raise ValueError(f"Incomplete IMG {rel}: {obj.parse_warnings}")
            images[rel] = obj
        return images[rel]
    def add(alias, source):
        if ".img/" not in source:
            raise ValueError(source)
        rel, node = source.split(".img/", 1)
        if image(rel + ".img").get(node) is None:
            raise ValueError(f"Missing source {source}")
        aliases[str(Path(alias).with_suffix(""))] = {"source": source}
    def children(n):
        return n.children() if n is not None else []
    def value(n, k, default=""):
        return getattr(n.get(k), "value", default) if n is not None else default

    for entry in aliases.values():
        image(entry["source"].split(".img/", 1)[0] + ".img")

    # Resource choices are game configuration; frame pixels/anchors/timings remain in IMG.
    characters = {}
    unavailable = set()
    for key, cfg in CHARS.items():
        hairs = list(dict.fromkeys([cfg["ids"][3], *HAIR_EXTRA]))
        for i, hair in enumerate(hairs):
            ids = list(cfg["ids"]); ids[3] = hair
            characters[f"{key}_h{i}"] = {"ids": ids, "attack": cfg["atk"], "skills": SKILL_KEYS[key]}
            for eid in ids:
                folder = CATEGORY_DIR[category_for_id(eid)]
                rel = "Character/" + (folder + "/" if folder else "") + eid + ".img"
                if (data / rel).exists(): image(rel)
                else:
                    unavailable.add(rel)
                    characters[f"{key}_h{i}"]["ids"] = [e for e in characters[f"{key}_h{i}"]["ids"] if e != eid]
    print("Unavailable equipment in existing choices:", sorted(unavailable), flush=True)
    image("zmap.img"); image("Map/Physics.img")
    mobs = {"snail": 100100, "slime": 210100, "mushroom": 1210102, "greenmush": 1110100,
            "pig": 1210100, "mushmom": 6130101, "slimeking": 9300003}
    for key in manifest["mobs"]:
        if key.isdigit(): mobs[key] = int(key)
    for iid in mobs.values():
        image(f"Mob/{iid:07}.img")

    # All field and chapter maps; traverse original resource references.
    fields = json.loads((ROOT / "Assets/Resources/Field/FieldMaps.json").read_text())["maps"]
    map_ids = {m["id"] for m in fields} | {int(s["map"]) for s in manifest["stages"].values()}
    for iid in sorted(map_ids):
        m = image(f"Map/Map/Map{iid // 100000000}/{iid:09}.img")
        for layer in range(9):
            n = m.get(str(layer))
            if children(n.get("tile") if n else None): image(f"Map/Tile/{value(n.get('info'), 'tS')}.img")
            for obj in children(n.get("obj") if n else None): image(f"Map/Obj/{value(obj, 'oS')}.img")
        for back in children(m.get("back")):
            if value(back, "bS"): image(f"Map/Back/{value(back, 'bS')}.img")
        for life in children(m.get("life")):
            if value(life, "type") == "n" and not value(life, "hide", 0):
                image(f"Npc/{int(value(life, 'id')):07}.img")
    image("Map/MapHelper.img")

    for key, skill in manifest["skills"].items(): image(f"Skill/{int(skill['id']) // 10000:03}.img")
    for icon in (ART / "skillicon").glob("*.bytes"):
        iid = int(icon.stem)
        add(str(icon.relative_to(ART)), f"Skill/{iid // 10000:03}.img/skill/{iid}/icon")
    vfx = json.loads((ROOT / "Assets/Resources/GameplaySkillVfx.json").read_text())
    for skill in vfx["skills"]:
        for clip in skill["clips"].values():
            for frame in clip["frames"]: add(frame["file"], frame["source"])
    combat = json.loads((ROOT / "Assets/Resources/GameplayCombat.json").read_text())
    libra = manifest["skills"]["libra"]["id"]
    for action, frames in combat["summon"].items():
        if not isinstance(frames, list): continue
        for i, frame in enumerate(frames): add(frame["file"], f"Skill/{int(libra)//10000:03}.img/skill/{libra}/summon/{action}/{i}")
    items = json.loads((ROOT / "Assets/Resources/Field/FieldDrops.json").read_text())["items"]
    for iid, item in items.items():
        if not item.get("icon"): continue
        if item["cat"] == "Equip":
            folder = CATEGORY_DIR[category_for_id(iid)]
            rel = f"Character/{folder}/{int(iid):08}.img"; node = "info"
        else:
            rel = f"Item/{item['cat']}/{int(iid)//10000:04}.img"; node = f"{int(iid):08}/info"
        img = image(rel)
        for icon in ("iconRaw", "icon"):
            if img.get(node + "/" + icon) is not None:
                add(item["icon"], rel + "/" + node + "/" + icon); break
        else: raise ValueError(f"Missing item icon {iid}")

    # Match existing UI aliases and damage glyphs to original canvases. This is a
    # one-time migration bridge; normal runtime follows the stored source paths.
    targets = {}
    sizes = set()
    def digest(im):
        im = im.convert("RGBA")
        return (im.size, hashlib.sha256(im.tobytes()).hexdigest())
    for group in ("ui", "fx"):
        for path in (ART / group).rglob("*.bytes"):
            im = Image.open(path).convert("RGBA"); sizes.add(im.size)
            targets.setdefault(digest(im), []).append(str(path.relative_to(ART).with_suffix("")))
    found = set()
    for rel in ("UI/Basic.img", "UI/StatusBar.img", "UI/UIWindow.img", "UI/Login.img", "Effect/BasicEff.img"):
        print("Indexing", rel, flush=True)
        def walk(n, path):
            if isinstance(n, WzCanvasProperty) and (n.width, n.height) in sizes:
                key = digest(decode_canvas(n, region="GMS"))
                for alias in targets.get(key, []):
                    if alias not in aliases:
                        aliases[alias] = {"source": rel + "/" + path}; found.add(alias)
            for c in children(n): walk(c, path + "/" + c.name if path else c.name)
        walk(image(rel).root, "")
    # A handful of HUD names refer to rectangular slices of native panels.
    import numpy as np
    crop_sources = ["base/backgrnd", "base/backgrnd2", "gauge/bar", "gauge/graduation"]
    unresolved = [(alias, key) for key, group in targets.items() for alias in group if alias not in aliases]
    for alias, _ in unresolved:
        if alias == "ui/statusbar/identity_plate": continue  # removed: bespoke non-native silver overlay
        target = np.asarray(Image.open(ART / (alias + ".bytes")).convert("RGBA"))
        th, tw = target.shape[:2]
        for node in crop_sources:
            native = np.asarray(decode_canvas(image("UI/StatusBar.img").get(node), region="GMS").convert("RGBA"))
            nh, nw = native.shape[:2]
            if nh < th or nw < tw: continue
            # Prefer an opaque, distinctive anchor to avoid thousands of transparent matches.
            samples = [(y,x) for y in (0,th//2,th-1) for x in (0,tw//2,tw-1)]
            ay,ax = max(samples, key=lambda q: int(target[q][3]))
            candidates = np.argwhere(np.all(native[ay:nh-th+ay+1, ax:nw-tw+ax+1] == target[ay,ax],axis=2))
            match = next(((int(x),int(y)) for y,x in candidates if np.array_equal(native[y:y+th,x:x+tw],target)),None)
            if match:
                aliases[alias] = {"source":"UI/StatusBar.img/"+node,"crop":[*match,tw,th]}; break
    unmatched = sorted(alias for group in targets.values() for alias in group if alias not in aliases and alias != "ui/statusbar/identity_plate")
    (ROOT / "Library/NativeAssets-unmatched.json").write_text(json.dumps(unmatched, indent=2))
    print("Unmatched UI/FX aliases:", unmatched, flush=True)
    if unmatched: raise ValueError("Native UI source mapping incomplete")

    # Follow file-level NPC/mob links and external Canvas dependencies to closure.
    scanned = set()
    while scanned != set(images):
        for rel in list(images.keys() - scanned):
            img = images[rel]; scanned.add(rel)
            link = value(img.get("info"), "link")
            if link and rel.startswith(("Npc/", "Mob/")):
                image(rel.split("/")[0] + "/" + f"{int(link):07}.img")
            def links(n):
                out = value(n, "_outlink")
                if out and ".img/" in out:
                    rel = out.split(".img/", 1)[0] + ".img"
                    if (data / rel).exists(): image(rel)
                    else: missing_outlinks.add(out)
                for c in children(n): links(c)
            links(img.root)
    # Copy Data resources and companion files; editor backups are not runtime assets.
    source_files = sorted(p.relative_to(data).as_posix() for p in data.rglob("*") if p.is_file() and not is_backup(p.relative_to(data)))
    index = {"packageMode": "complete-data", "unavailableOutlinks": sorted(missing_outlinks), "unavailableEquipment": sorted(unavailable), "characters": characters, "mobs": mobs, "skills": {k: int(v["id"]) for k,v in manifest["skills"].items()},
             "aliases": aliases, "zOrder": list(_DEFAULT_ZMAP), "backZ": _BACK_FACING_Z_OVERRIDE,
             "backAbsoluteZ": _BACK_FACING_ABSOLUTE_Z, "overCapRemap": {k: v[0] for k,v in _OVER_CAP_REMAP.items()},
             "files": [{"path": rel, "sha256": hashlib.sha256((data/rel).read_bytes()).hexdigest()} for rel in source_files]}
    dest = args.output.resolve() / "Data"
    for rel in source_files:
        target = dest / rel; target.parent.mkdir(parents=True, exist_ok=True)
        if not target.exists() or hashlib.sha256(target.read_bytes()).digest() != hashlib.sha256((data/rel).read_bytes()).digest(): shutil.copyfile(data/rel, target)
    (ROOT / "Assets/Resources/NativeAssetIndex.json").write_text(json.dumps(index, ensure_ascii=False, separators=(",", ":")))
    (args.output.resolve() / "resource-manifest.json").write_text(json.dumps(index, ensure_ascii=False, separators=(",", ":")))
    print(f"Packaged {len(source_files)} unchanged Data files, {len(aliases)} aliases, {sum((data/f).stat().st_size for f in source_files)/1048576:.1f} MiB", flush=True)


if __name__ == "__main__": main()
