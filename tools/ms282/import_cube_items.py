#!/usr/bin/env python3
"""Merge 282 cube and supported potential-scroll item trees and names; preserve unrelated entries."""
import argparse
import hashlib
import io
import json
from pathlib import Path

from import_enhancement_ui import Importer, ROOT, WzImage, WzKey, WzBinaryReader, encode_image_body
from wzpy.properties import WzCanvasProperty, WzUolProperty
from wzpy.canvas import _decompress


def is_cube(item_id):
    # 50624xx are anvils/appearance items; 50628xx are ability circulators.
    return (item_id in (2049400, 2049401) or 5062000 <= item_id < 5063000 and item_id//100 not in (50624,50628)
            or 2711000 <= item_id < 2712000 or 2730000 <= item_id < 2740000)


def fingerprint(node):
    pixels = None
    if isinstance(node, WzCanvasProperty):
        pixels = (node.width, node.height, node.format, node.format2,
                  hashlib.sha256(_decompress(node, WzKey.for_region('GMS'))).hexdigest())
    return (type(node).__name__, node.name, node.value, pixels,
            tuple(fingerprint(child) for child in node.children()))


def resolve(root, path, seen=None):
    seen = set() if seen is None else seen
    if path in seen:
        raise ValueError('Cyclic icon UOL: '+path)
    seen.add(path)
    node = root
    parts = path.split('/')
    for i, part in enumerate(parts):
        node = node.parent if part == '..' else node if part in ('', '.') else node.get(part)
        if node is None:
            raise ValueError('Missing icon path: '+path)
        if isinstance(node, WzUolProperty):
            return resolve(root, '/'.join(parts[:i]+[node.value]+parts[i+1:]), seen)
    return node


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=Path.home()/'Documents/mxd/MapleStory/Data')
    parser.add_argument('--dest', type=Path, default=ROOT.parent/'MapleDashResources/Data')
    parser.add_argument('--write', action='store_true')
    args = parser.parse_args()
    importer = Importer(args.source)
    outputs, selected, before = {}, {}, {}
    for rel in ('Item/Cash/0506.img', 'Item/Consume/0271.img', 'Item/Consume/0273.img', 'Item/Consume/0204.img',
                'String/Cash.img', 'String/Consume.img'):
        source = importer.image(rel)
        names = [node.name for node in source.root.children() if node.name.isdigit() and is_cube(int(node.name))]
        if not names:
            raise ValueError('No cube entries: '+rel)
        data = importer.convert(rel, names)
        converted = WzImage.from_bytes(data, key=WzKey.for_region('GMS'), name=rel)
        converted.parse()
        target = args.dest/rel
        preserved = {}
        if target.exists():
            raw = target.read_bytes()
            before[rel] = hashlib.sha256(raw).hexdigest()
            merged = WzImage.from_bytes(raw, key=WzKey.for_region('GMS'), name=rel)
            merged.parse()
            if merged.truncated or merged.parse_warnings:
                raise ValueError('Cannot safely merge existing tree: '+rel)
            preserved = {node.name: fingerprint(node) for node in merged.root.children() if node.name not in names}
            for node in converted.root.children():
                merged.root.add(node)
            data = encode_image_body(merged, WzBinaryReader(io.BytesIO(), WzKey.for_region('GMS')))
        result = WzImage.from_bytes(data, key=WzKey.for_region('GMS'), name=rel)
        result.parse()
        if result.truncated or result.parse_warnings or any(result.root.get(name) is None for name in names):
            raise ValueError('Merged tree failed verification: '+rel)
        for name, expected in preserved.items():
            if fingerprint(result.root.get(name)) != expected:
                raise ValueError('Unrelated entry changed: '+rel+'/'+name)
        if rel.startswith('Item/'):
            for name in names:
                icon = resolve(result.root, name+'/info/icon')
                if not isinstance(icon, WzCanvasProperty) or icon.format+icon.format2 != 1:
                    raise ValueError('Cube icon not ARGB4444: '+rel+'/'+name)
        outputs[rel], selected[rel] = data, names
    if args.write:
        for rel, data in outputs.items():
            path = args.dest/rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        index_path = ROOT/'Assets/Resources/NativeAssetIndex.json'
        index = json.loads(index_path.read_text())
        for rel, data in outputs.items():
            entry = next((row for row in index['files'] if row['path'] == rel), None)
            if entry is None:
                entry = {'path': rel}
                index['files'].append(entry)
            entry['sha256'] = hashlib.sha256(data).hexdigest()
        index_path.write_text(json.dumps(index, ensure_ascii=False, indent=1)+'\n')
        catalog_path = ROOT/'Assets/Resources/Bot/Items.json'
        catalog = json.loads(catalog_path.read_text())
        for rel, names in selected.items():
            source = importer.image(rel)
            for name in names:
                item_id = str(int(name))
                node = source.root.get(name)
                if rel.startswith('Item/'):
                    info = node.get('info')
                    catalog['items'][item_id] = {'source': rel, 'info': {
                        child.name: child.value for child in info.children()
                        if isinstance(child.value, (int, float, str))}}
                else:
                    for field, section in (('name', 'names'), ('desc', 'descriptions')):
                        prop = node.get(field)
                        if prop is not None:
                            catalog.setdefault(section, {})[item_id] = prop.value
            catalog['sources'][rel] = hashlib.sha256(outputs[rel]).hexdigest()
        catalog_path.write_text(json.dumps(catalog, ensure_ascii=False, separators=(',', ':'))+'\n')
        report = ROOT.parent/'docs/tms282-skill/data/cube-item-import.json'
        report.write_text(json.dumps({'sourceSha256': importer.hashes, 'beforeSha256': before,
            'outputSha256': {rel: hashlib.sha256(data).hexdigest() for rel, data in outputs.items()},
            'selected': selected, 'canvases': importer.count, 'encoding': 'GMS', 'importedPixelFormat': 'ARGB4444'}, indent=2)+'\n')
    print('PASS:', {rel: len(names) for rel, names in selected.items()}, 'installed=', args.write)


if __name__ == '__main__':
    main()
