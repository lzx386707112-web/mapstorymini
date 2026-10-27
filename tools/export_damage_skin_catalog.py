#!/usr/bin/env python3
"""Copy the server's authoritative damage-skin ids/names, without inventing ids."""
import argparse
import json
import re
import sys
from pathlib import Path


def main():
    project = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=project.parent.parent / 'Beidou-Server')
    parser.add_argument('--validate-glyphs', action='store_true', help='Parse every skin using the source WZ reader')
    args = parser.parse_args()
    java = (args.source / 'gms-server/src/main/java/org/gms/server/DamageSkinCatalog.java').read_text()
    ids = [int(x) for x in re.search(r'IDS = \{([^}]+)\}', java).group(1).split(',')]
    names = re.findall(r'"((?:[^"\\]|\\.)*)"', re.search(r'NAMES = \{(.*?)\n    \};', java, re.S).group(1))
    names = [json.loads('"' + x + '"') for x in names]
    if len(ids) != len(names) or ids != sorted(set(ids)):
        raise ValueError('Invalid source catalog')
    data = project.parent / 'MapleDashResources/Data'
    missing = [i for i in ids if not (data / f'Effect/DamageSkin/{i}.img').is_file()]
    if missing:
        raise FileNotFoundError(f'Missing source skin IMG files: {missing}')
    if args.validate_glyphs:
        sys.path.insert(0, str(args.source / 'tool/wz-python'))
        from wzpy import WzImage, WzKey
        from wzpy.properties import WzCanvasProperty
        for skin in ids:
            path = data / f'Effect/DamageSkin/{skin}.img'
            image = WzImage.from_bytes(path.read_bytes(), key=WzKey.for_region('GMS'), name=path.name)
            image.parse()
            if image.truncated or image.parse_warnings:
                raise ValueError(f'Invalid skin IMG: {skin}: {image.parse_warnings}')
            for group in ['NoRed0', 'NoRed1', 'NoCri0', 'NoCri1']:
                for digit in range(10):
                    node = image.get(f'{group}/{digit}')
                    if not isinstance(node, WzCanvasProperty):
                        raise ValueError(f'Missing direct Canvas: {skin}/{group}/{digit}')
        print(f'Validated {len(ids) * 40} source digit Canvas nodes')
    target = project / 'Assets/Resources/DamageSkinCatalog.json'
    target.write_text(json.dumps(dict(zip(map(str, ids), names)), ensure_ascii=False, indent=2) + '\n')
    print(f'{len(ids)} skins; every IMG present; wrote {target}')


if __name__ == '__main__':
    main()
