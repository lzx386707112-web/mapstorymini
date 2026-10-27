#!/usr/bin/env python3
"""Export original map navigation geometry without image assets or invented bounds."""
import argparse
import hashlib
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT.parent.parent / 'BeiDou-Server/tool/wz-python'))
from wzpy import WzImage, WzKey


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--data', type=Path, default=ROOT.parent / 'MapleDashResources/Data')
    ap.add_argument('--out', type=Path, default=ROOT / 'Assets/Resources/Bot/Maps.json')
    args = ap.parse_args()
    key = WzKey.for_region('GMS')
    maps = {}
    def scalars(node):
        return {} if node is None else {c.name: c.value for c in node.children()
                                       if isinstance(getattr(c, 'value', None), (int, float, str))}
    paths = sorted((args.data / 'Map/Map').rglob('*.img'))
    for index, path in enumerate(paths):
        if not path.stem.isdigit():
            continue
        raw = path.read_bytes()
        img = WzImage.from_bytes(raw, key=key, name=path.name)
        tree = img.parse_partial(only=frozenset({'info', 'miniMap', 'foothold', 'portal', 'ladderRope', 'life'}))
        if img.truncated or img.parse_warnings:
            raise ValueError(f'Incomplete map geometry: {path}: {img.parse_warnings}')
        footholds = []
        fh = tree.get('foothold')
        if fh is not None:
            for layer in fh.children():
                for group in layer.children():
                    for segment in group.children():
                        footholds.append(dict(id=int(segment.name), layer=int(layer.name), group=int(group.name), **scalars(segment)))
        def entries(name):
            node = tree.get(name)
            return [] if node is None else [dict({'id': int(c.name)}, **scalars(c)) for c in node.children()]
        maps[str(int(path.stem))] = dict(info=scalars(tree.get('info')), miniMap=scalars(tree.get('miniMap')),
                                        footholds=footholds, portals=entries('portal'), ropes=entries('ladderRope'),
                                        npcs=[n for n in entries('life') if n.get('type') == 'n'],
                                        source=path.relative_to(args.data).as_posix(), sha256=hashlib.sha256(raw).hexdigest())
        if index % 500 == 0:
            print(f'Map geometry scanned: {index}/{len(paths)}', flush=True)
    if not maps:
        raise ValueError('No original map files found')
    # Android只解析当前地图所在分组，避免一次展开全部40MB几何为对象树。
    shards = {}
    for map_id, geometry in maps.items():
        shards.setdefault(str(int(map_id) // 1000000), {})[map_id] = geometry
    shard_dir = args.out.with_suffix('')
    shard_dir.mkdir(parents=True, exist_ok=True)
    for group, entries in shards.items():
        (shard_dir / (group + '.json')).write_text(json.dumps(entries, ensure_ascii=False, separators=(',', ':')) + '\n')
    for stale in shard_dir.glob('*.json'):
        if stale.stem not in shards:
            stale.unlink()
    index = {map_id: 'Bot/Maps/' + str(int(map_id) // 1000000) for map_id in maps}
    neighbors = {}
    for map_id, geometry in maps.items():
        link = str(geometry['info'].get('link', ''))
        linked = maps.get(str(int(link))) if link and link.isdigit() else geometry
        portals = linked['portals'] if linked else []
        neighbors[map_id] = sorted({int(p.get('tm', 999999999)) for p in portals
                                    if int(p.get('tm', 999999999)) not in (999999999, int(map_id))
                                    and int(p.get('pt', 0)) != 6 and not p.get('script', '')})
    args.out.write_text(json.dumps(dict(version=2, maps=index, neighbors=neighbors), ensure_ascii=False, separators=(',', ':')) + '\n')
    print(f'Exported {len(maps)} original navigation maps')


if __name__ == '__main__':
    main()
