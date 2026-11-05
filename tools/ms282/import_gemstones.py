"""Merge the real 282 VCore gemstone item nodes/icons/strings; keep unrelated items."""
import argparse
import hashlib
import json
import xml.etree.ElementTree as ET
from collections import defaultdict
from import_growth_ui import DEFAULT_SRC, DEFAULT_DEST, load_source
from import_growth_items import rgba_snapshot, verify
from import_androids import register
from img_bake import PixelSource, RuntimeImgMirror, bake_tree, serialize
from wzpy import WzImage, WzKey
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--write', action='store_true')
    args = ap.parse_args()
    table = json.loads((ROOT / 'Assets/Resources/UI/VMatrix.json').read_text())
    ids = {i for g in table['gemstones'] for i in g['itemIds']}
    catalog_path = ROOT / 'Assets/Resources/Bot/Items.json'
    catalog = json.loads(catalog_path.read_text())
    strings = ET.parse(ROOT.parent.parent / 'TMS282.5/data/wz282/String.wz/Consume.img.xml').getroot()
    names = {int(n.attrib['name']): {c.attrib['name']: c.attrib.get('value', '') for c in n}
             for n in strings if n.attrib.get('name', '').isdigit() and int(n.attrib['name']) in ids}
    grouped = defaultdict(list)
    for i in ids:
        grouped[f'Item/Consume/{i // 10000:04d}.img'].append(i)
    outputs = {}
    for rel, items in grouped.items():
        target = DEFAULT_DEST / rel
        donor = load_source(DEFAULT_SRC / rel)
        if target.exists():
            image = WzImage.from_bytes(target.read_bytes(), key=WzKey.for_region('GMS'))
            image.parse()
        else:
            image = donor
            image.root._children = {f'{i:08d}': donor.root.get(f'{i:08d}') for i in items}
        for i in items:
            node = donor.root.get(f'{i:08d}')
            assert node is not None and i in names, i
            image.root._children[node.name] = node
            bake_tree(node, PixelSource(DEFAULT_DEST, WzKey.for_region('GMS'), fallback_root=DEFAULT_SRC), {'baked': 0, 'pixels': 0})
            info = node.get('info')
            catalog['items'][str(i)] = {'source': rel, 'info': {c.name: c.value for c in info.children() if isinstance(c.value, (int, float, str))}}
            catalog['names'][str(i)] = names[i]['name']
            catalog['descriptions'][str(i)] = '\\n'.join(names[i][k] for k in ('desc', 'desc_leftalign') if names[i].get(k))
        baseline = rgba_snapshot(image)
        data = serialize(image)
        mirror = RuntimeImgMirror(data, WzKey.for_region('GMS'))
        actual = {}
        def walk(n, prefix=''):
            for c in n.get('children', []):
                path = prefix + c['name']
                if c.get('kind') == 'Canvas' and path in baseline:
                    actual[path] = mirror.rgba(c)
                walk(c, path + '/')
        walk(mirror.root)
        # Existing unrelated items may retain valid outlinks. Do not rewrite them;
        # compare all inline pixels and require both newly imported gemstone icons.
        assert all(actual.get(k) == v for k, v in baseline.items()), rel
        for i in items:
            for icon in ('icon', 'iconRaw'):
                assert actual.get(f'{i:08d}/info/{icon}'), (i, icon)
        print(rel, len(baseline), 'inline canvases pixel-identical')
        outputs[rel] = data
        catalog['sources'][rel] = hashlib.sha256(data).hexdigest()
    if args.write:
        for rel, data in outputs.items():
            (DEFAULT_DEST / rel).write_bytes(data)
        register(list(outputs), DEFAULT_DEST)
        catalog_path.write_text(json.dumps(catalog, ensure_ascii=False, separators=(',', ':')) + '\n')
    print(f'{len(ids)} gemstones verified; write={args.write}')


if __name__ == '__main__':
    main()
