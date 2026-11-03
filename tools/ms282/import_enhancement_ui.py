#!/usr/bin/env python3
"""Import original 282 enhancement UI, resolve pixel links, encode GMS/ARGB4444.

No artwork is generated. Original layout/animation properties remain intact.
Default is validation only; --write installs the verified images and asset index.
"""
import argparse
import hashlib
import io
import json
from pathlib import Path
import sys
import zlib
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT.parent.parent / "Beidou-Server/tool/wz-python"))
from wzpy import WzImage, WzKey
from wzpy.properties import WzCanvasProperty, WzUolProperty, WzSoundProperty
from wzpy.canvas import decode_canvas, _encode_pixels, _decompress
from wzpy.reader import WzBinaryReader
from wzpy.writer import encode_image_body, _read_sound_payload

SECTIONS = {
    "UI/Enchant.img": ["common", "none", "starForce", "potential", "additionalPotential", "traceTransfer",
                       "fullScreen_starForce", "fullScreen_potential", "fullScreen_additionalPotential",
                       "effect/starForce", "effect/potential", "effect/addtionalPotential",
                       "sound/starForce", "sound/potential", "sound/additionalPotential", "costItemID",
                       "particle/tab_yellow", "particle/tab_blue", "particle/itemIcon_waitEquip_yellow",
                       "particle/itemIcon_onEquip_yellow", "particle/itemIcon_waitEquip_blue",
                       "particle/itemIcon_onEquip_blue", "particle/fullScreen_potential_highlight"],
    "UI/UICubeWz2.img": None,
}


def encode_argb4444(image):
    # Same high-nibble truncation and BG/AR byte order as wzpy fmt=1.
    rgba = np.asarray(image.convert("RGBA"), dtype=np.uint8) >> 4
    out = np.empty((image.height, image.width, 2), dtype=np.uint8)
    out[:, :, 0] = rgba[:, :, 2] | (rgba[:, :, 1] << 4)
    out[:, :, 1] = rgba[:, :, 0] | (rgba[:, :, 3] << 4)
    return zlib.compress(out.tobytes(), 6)


class Importer:
    def __init__(self, source):
        self.source = source
        self.images = {}
        self.hashes = {}
        self.pixel_cache = {}
        self.count = 0
        self.nodes = {}

    def image(self, rel):
        if rel not in self.images:
            raw = (self.source / rel).read_bytes()
            self.hashes[rel] = hashlib.sha256(raw).hexdigest()
            img = WzImage.from_bytes(raw, key=WzKey.for_region("BMS"), name=rel)
            img.parse()
            if img.truncated or img.parse_warnings:
                raise ValueError(f"Incomplete source: {rel}: {img.parse_warnings}")
            self.images[rel] = img
        return self.images[rel]

    def node(self, rel, path, seen=None):
        seen = set() if seen is None else seen
        key = (rel, path)
        if key in seen:
            raise ValueError(f"Cyclic link: {key}")
        seen.add(key)
        node = self.image(rel).root
        parts = path.split("/")
        for index, part in enumerate(parts):
            if not part or part == ".":
                continue
            node = node.parent if part == ".." else node.get(part)
            if node is None:
                raise ValueError(f"Missing node: {rel}/{path}")
            if isinstance(node, WzUolProperty):
                parent = "/".join(parts[:index])
                rest = "/".join(parts[index+1:])
                return self.node(rel, parent + "/" + node.value + "/" + rest, seen)
        if isinstance(node, WzUolProperty):
            parent = path.rsplit("/", 1)[0] if "/" in path else ""
            return self.node(rel, parent + "/" + node.value, seen)
        return rel, node

    def pixels(self, rel, node, seen=None):
        seen = set() if seen is None else seen
        key = (rel, id(node))
        if key in seen:
            raise ValueError("Cyclic canvas link")
        seen.add(key)
        for name in ("_outlink", "_inlink"):
            link = node.get(name)
            if link is None:
                continue
            path = link.value
            target_rel = rel
            if name == "_outlink":
                target_rel, path = path.split(".img/", 1)
                target_rel += ".img"
            target_rel, target = self.node(target_rel, path)
            return self.pixels(target_rel, target, seen)
        if not isinstance(node, WzCanvasProperty):
            raise ValueError("Pixel link does not target Canvas")
        if key not in self.pixel_cache:
            image = decode_canvas(node, region="BMS")
            self.pixel_cache[key] = (image.width, image.height, encode_argb4444(image))
        return self.pixel_cache[key]

    def convert(self, rel, sections):
        self.image(rel)
        # Keep the cached source tree intact: links may target nodes outside selected UI branches.
        image = WzImage.from_bytes((self.source / rel).read_bytes(), key=WzKey.for_region("BMS"), name=rel)
        image.parse()
        # Stage converted pixels only after every link has been resolved from pristine originals.
        staged = []

        def walk(node):
            if isinstance(node, WzCanvasProperty):
                staged.append((node, self.pixels(rel, node)))
                if len(staged) % 250 == 0:
                    print(f"{rel}: converted {len(staged)} canvases", flush=True)
            for child in node.children():
                walk(child)

        if sections is not None:
            keep = set(sections)

            def prune(node, path=""):
                for child in list(node.children()):
                    child_path = (path + "/" + child.name).lstrip("/")
                    if child_path in keep or any(child_path.startswith(p + "/") for p in keep):
                        continue
                    if any(p.startswith(child_path + "/") for p in keep):
                        prune(child, child_path)
                    else:
                        del node._children[child.name]

            for section in sections:
                self.node(rel, section)  # Refuse missing roots; don't silently ship partial UI.
            prune(image.root)
        walk(image.root)
        for node, (width, height, payload) in staged:
            node.width, node.height, node.format, node.format2 = width, height, 1, 0
            node._png_data = payload
            node._png_length = len(payload)
            node._children.pop("_outlink", None)
            node._children.pop("_inlink", None)
        data = encode_image_body(image, WzBinaryReader(io.BytesIO(), WzKey.for_region("GMS")))
        verified = WzImage.from_bytes(data, key=WzKey.for_region("GMS"), name=rel)
        verified.parse()
        if verified.truncated or verified.parse_warnings:
            raise ValueError(f"Output parse failure: {rel}")

        def compare(a, b):
            if a.name != b.name or type(a) is not type(b) or getattr(a,"value",None) != getattr(b,"value",None):
                raise ValueError(f"Property mismatch: {rel}/{a.name}")
            if isinstance(a, WzCanvasProperty):
                if (a.width,a.height,1) != (b.width,b.height,b.format+b.format2):
                    raise ValueError("Canvas dimensions/ARGB4444 mismatch")
                raw = _decompress(b, WzKey.for_region("GMS"))
                if zlib.decompress(a._png_data) != raw:
                    raise ValueError("ARGB4444 pixel payload mismatch")
                if len(raw) != a.width*a.height*2:
                    raise ValueError("ARGB4444 pixel length mismatch")
            if isinstance(a, WzSoundProperty):
                if (a.length_ms,a.header,_read_sound_payload(a)) != (b.length_ms,b.header,_read_sound_payload(b)):
                    raise ValueError("Sound payload/header mismatch")
            ca, cb = a.children(), b.children()
            if len(ca) != len(cb):
                raise ValueError("Child count mismatch")
            for x,y in zip(ca,cb):
                compare(x,y)

        compare(image.root, verified.root)
        nodes = []

        def describe(node, path=""):
            row = {"path": path, "type": type(node).__name__}
            if isinstance(node, WzCanvasProperty):
                row.update(width=node.width, height=node.height, format=node.format+node.format2)
            elif node.value is not None:
                row["value"] = node.value
            nodes.append(row)
            for child in node.children():
                describe(child, (path+"/"+child.name).lstrip("/"))

        describe(verified.root)
        self.nodes[rel] = nodes
        self.count += len(staged)
        print(f"{rel}: {len(staged)} original canvases -> ARGB4444; {len(data)} bytes", flush=True)
        return data


def main():
    from PIL import Image
    sample = Image.frombytes("RGBA", (256, 256), bytes(
        channel for x in range(256) for y in range(256) for channel in (x, y, 255-x, 255-y)))
    if zlib.decompress(encode_argb4444(sample)) != _encode_pixels(sample, 1, 256, 256):
        raise ValueError("ARGB4444 encoder differs from source tool")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path.home()/"Documents/mxd/MapleStory/Data")
    parser.add_argument("--dest", type=Path, default=ROOT.parent/"MapleDashResources/Data")
    parser.add_argument("--write", action="store_true")
    args = parser.parse_args()
    importer = Importer(args.source)
    outputs = {rel: importer.convert(rel, sections) for rel, sections in SECTIONS.items()}
    sections=dict(SECTIONS)
    # Sound nodes in the UI are string references, not embedded Sound_DX8.
    sound_refs={row['value'] for rows in importer.nodes.values() for row in rows
                if isinstance(row.get('value'),str) and row['value'].startswith('Sound/') and '.img/' in row['value']}
    for reference in sorted(sound_refs):
        rel,path=reference.split('.img/',1);rel+='.img'
        sections.setdefault(rel,[]).append(path)
    for rel in sorted(set(sections)-set(SECTIONS)):
        sections[rel]=sorted(set(sections[rel]))
        outputs[rel]=importer.convert(rel,sections[rel])
    if args.write:
        for rel,data in outputs.items():
            target=args.dest/rel
            target.parent.mkdir(parents=True,exist_ok=True)
            target.write_bytes(data)
        index_path=ROOT/"Assets/Resources/NativeAssetIndex.json"
        index=json.loads(index_path.read_text())
        for rel,data in outputs.items():
            entry=next((x for x in index["files"] if x["path"]==rel),None)
            if entry is None:
                entry={"path":rel};index["files"].append(entry)
            entry["sha256"]=hashlib.sha256(data).hexdigest()
        index_path.write_text(json.dumps(index,ensure_ascii=False,indent=1)+"\n")
        report=ROOT.parent/"docs/tms282-skill/data/enhancement-ui-import.json"
        report.write_text(json.dumps({"sourceSha256":importer.hashes,"sections":sections,
            "outputSha256":{k:hashlib.sha256(v).hexdigest() for k,v in outputs.items()},
            "canvases":importer.count,"format":"ARGB4444","encoding":"GMS"},indent=2)+"\n")
        report.with_name("enhancement-ui-nodes.json").write_text(json.dumps(importer.nodes,ensure_ascii=False,indent=1)+"\n")
    print(f"PASS: {importer.count} canvases verified; installed={args.write}")


if __name__ == "__main__":
    main()
