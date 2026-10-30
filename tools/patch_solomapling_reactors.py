#!/usr/bin/env python3
"""Incrementally add the `reactors` array to selected map shards.

Why not just rerun export_solomapling_maps.py? It walks all ~6336 Map.img files
(a few minutes of WZ parsing and a 40MB rewrite) just to add one field. This
script parses the reactor node of the handful of maps that actually need it and
patches the existing shard JSON in place, leaving every other field byte-for-byte
semantically identical.

The field layout matches what export_solomapling_maps.py now emits:
    reactors = [{'id': <reactor data id>, 'x': .., 'y': .., 'reactorTime': .., 'name': ..}]

Usage:
    python3 tools/patch_solomapling_reactors.py                # patch Maple Island and OPQ
    python3 tools/patch_solomapling_reactors.py --maps 920010000 920010400
"""
import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT.parent.parent / 'BeiDou-Server/tool/wz-python'))
from wzpy import WzImage, WzKey

# OPQ 四张副本图（源端 OPQConstants）：920010000 云堆 21 个、920010100 塔 7 个、
# 920010400 唱片箱 9 个、920011200 出口图 0 个（仍写入空数组，表示「这张图已导出、确实没有反应堆」）。
# 源端 OPQConstants / OPQOrchestrator 不再引用别的带反应堆地图：200080101 招募大厅与
# 200080100 售票处都没有 reactor 节点。
# 皮奥所在彩虹村：原图的五个 0002001 箱子，不能只导出组队副本的反应堆。
DEFAULT_MAPS = ('1000000', '920010000', '920010100', '920010400', '920011200')


def parse_reactors(path, key):
    img = WzImage.from_bytes(path.read_bytes(), key=key, name=path.name)
    tree = img.parse_partial(only=frozenset({'reactor'}))
    if img.truncated or img.parse_warnings:
        raise ValueError(f'Incomplete reactor data: {path}: {img.parse_warnings}')
    node = tree.get('reactor')
    if node is None:
        return []
    out = []
    for child in node.children():
        scalars = {c.name: c.value for c in child.children()
                   if isinstance(getattr(c, 'value', None), (int, float, str))}
        raw_id = str(scalars.get('id', ''))
        if not raw_id.lstrip('-').isdigit():
            continue
        out.append(dict(id=int(raw_id), x=int(scalars.get('x', 0)), y=int(scalars.get('y', 0)),
                        reactorTime=int(scalars.get('reactorTime', -1)), name=str(scalars.get('name', ''))))
    return out


def export_templates(data, ids, target):
    # Incremental map updates must retain templates used by other shards.
    templates = json.loads(target.read_text()) if target.exists() else {}
    key = WzKey.for_region('GMS')
    for rid in sorted(ids):
        path = data / 'Reactor' / (str(rid).zfill(7) + '.img')
        img = WzImage.from_bytes(path.read_bytes(), key=key, name=path.name)
        tree = img.parse()
        if img.truncated or img.parse_warnings:
            raise ValueError(f'Incomplete template: {path}')
        states = {}
        area = None
        for state in tree.children():
            if not state.name.isdigit():
                continue
            event = state.get('event')
            rows = []
            if event:
                for row in event.children():
                    if not row.name.isdigit():
                        continue
                    def val(name, default=0):
                        n = row.get(name)
                        return n.value if n is not None else default
                    entry = dict(type=int(val('type')), next=int(val('state')),
                                 item=int(val('0')), quantity=int(val('1', 1)))
                    if entry['type'] == 100 and area is None:
                        area = list(val('lt')) + list(val('rb'))
                    rows.append(entry)
            states[state.name] = rows
        templates[str(rid)] = dict(states=states, area=area)
    target.write_text(json.dumps(templates, ensure_ascii=False, separators=(',', ':')) + '\n')


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--data', type=Path, default=ROOT.parent / 'MapleDashResources/Data')
    ap.add_argument('--out', type=Path, default=ROOT / 'Assets/Resources/Bot/Maps.json')
    ap.add_argument('--maps', nargs='*', default=list(DEFAULT_MAPS))
    args = ap.parse_args()
    key = WzKey.for_region('GMS')
    shard_dir = args.out.with_suffix('')

    if not args.out.exists():
        raise SystemExit(f'Missing map index: {args.out} — run export_solomapling_maps.py first')

    # 一张分片可能包含多张目标图，先按分片归组再一次读写，避免同一文件被覆盖两次。
    per_shard = {}
    template_ids = {2202004}
    for map_id in (str(int(m)) for m in args.maps):
        hits = sorted((args.data / 'Map/Map').rglob(map_id.zfill(9) + '.img'))
        if not hits:
            raise SystemExit(f'Map image not found: {map_id}')
        reactors = parse_reactors(hits[0], key)
        template_ids.update(r["id"] for r in reactors)
        shard = shard_dir / (str(int(map_id) // 1000000) + '.json')
        if not shard.exists():
            raise SystemExit(f'Missing shard: {shard}')
        per_shard.setdefault(shard, []).append((map_id, reactors))

    for shard, updates in per_shard.items():
        data = json.loads(shard.read_text())
        for map_id, reactors in updates:
            if map_id not in data:
                raise SystemExit(f'Map {map_id} is not inside {shard.name}')
            data[map_id]['reactors'] = reactors
            print(f'{map_id}: {len(reactors)} reactors -> {shard.relative_to(ROOT)}')
        shard.write_text(json.dumps(data, ensure_ascii=False, separators=(',', ':')) + '\n')

    export_templates(args.data, template_ids, ROOT / "Assets/Resources/Bot/ReactorTemplates.json")

    # This task migrates Maple Island's box table; retain other migrated tables.
    # V1.0.59 repeats questid=1008 for both quest items; later migrations do not
    # change these eight rows. Keep probabilities in SQL, never in runtime code.
    if 2001 in template_ids:
        sql = ROOT.parent.parent / 'Beidou-Server/gms-server/src/main/resources/db/migration/V1.0.58__reactordrops_insert_data.sql'
        rows = re.findall(r'\(\s*\d+\s*,\s*2001\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(-?\d+)\s*\)', sql.read_text())
        if len(rows) != 8:
            raise ValueError('Maple Island reactor drop table changed; review SQL migrations')
        target = ROOT / 'Assets/Resources/Bot/ReactorDrops.json'
        drops = json.loads(target.read_text()) if target.exists() else {}
        drops['2001'] = [dict(item=int(item), chance=int(chance), quest=int(quest))
                         for item, chance, quest in rows]
        target.write_text(json.dumps(drops, separators=(',', ':')) + '\n')


if __name__ == '__main__':
    main()
