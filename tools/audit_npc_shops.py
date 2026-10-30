#!/usr/bin/env python3
"""Audit original shop bindings and every authored map NPC placement (read-only).

Requires wzpy, like imgdump.py. Output describes data and static runtime paths;
it is not a Unity/Android acceptance test.
"""
import argparse
import gc
import hashlib
import json
import re
import sqlite3
from pathlib import Path

from imgdump import DEFAULT_DATA
from wzpy import WzImage, WzKey

ROOT = Path(__file__).resolve().parents[1]
KEY = WzKey.for_region('GMS')


def load(path):
    image = WzImage.from_bytes(path.read_bytes(), key=KEY, name=path.name)
    image.parse()
    if image.truncated or image.parse_warnings:
        raise ValueError(f'{path}: incomplete parse: {image.parse_warnings}')
    return image


def value(node, path, default=None):
    child = node.get(path) if node is not None else None
    return getattr(child, 'value', default) if child is not None else default


def children(node):
    return list(node.children()) if node is not None else []


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--data', type=Path, default=DEFAULT_DATA)
    parser.add_argument('--server', type=Path, default=ROOT.parent.parent / 'Beidou-Server/gms-server')
    parser.add_argument('--out', type=Path, default=ROOT / 'docs/npc-shop-audit.json')
    args = parser.parse_args()
    migration = args.server / 'src/main/resources/db/migration'
    stock_sql = migration / 'V1.0.55__shopitems_insert_data.sql'
    # Replay every migration touching shops, including later regional shops
    # and casino NPC changes. Never confuse item tuples with shop bindings.
    sql_db = sqlite3.connect(':memory:')
    sql_db.execute('CREATE TABLE shops (shopid INTEGER PRIMARY KEY, npcid INTEGER)')
    binding_sources = []
    versions = lambda path: tuple(map(int, path.name.split('__')[0][1:].split('.')))
    for path in sorted(migration.glob('V*__*.sql'), key=versions):
        sql = re.sub(r'/\*.*?\*/|--[^\n]*|#[^\n]*', '', path.read_text(), flags=re.S)
        for statement in sql.split(';'):
            statement = statement.strip()
            if not re.match(r'(?:INSERT\s+(?:IGNORE\s+)?INTO|UPDATE|DELETE\s+FROM)\s+`?shops`?\b', statement, re.I):
                continue
            if path not in binding_sources:
                binding_sources.append(path)
            statement = re.sub(r'ON DUPLICATE KEY UPDATE.*', '', statement, flags=re.I | re.S)
            statement = re.sub(r'^INSERT\s+(?:IGNORE\s+)?INTO', 'INSERT OR REPLACE INTO', statement, flags=re.I)
            sql_db.execute(statement)
    shop_rows = list(sql_db.execute('SELECT shopid,npcid FROM shops ORDER BY shopid'))
    sql_db.close()
    shops = {}
    for shop, npc in shop_rows:
        shops.setdefault(npc, []).append(shop)
    names = load(args.data / 'String/Npc.img')
    # Include merchant-labelled NPCs absent from shops as data gaps, without
    # inventing a shop or interpreting a manufacturing NPC as a retail shop.
    merchant_pattern = r'杂货|武器商人|防具|防御武器|药水商人|药水销售|武器销售|贩卖武器'
    for npc in children(names.root):
        if re.search(merchant_pattern, value(npc, 'func', '')):
            shops.setdefault(int(npc.name), [])
    packed = json.loads((ROOT / 'Assets/Resources/Scripts/npc_scripts.json').read_text())
    entries = {}
    errors = []
    for npc, shop_ids in shops.items():
        name = names.get(str(npc))
        record = {'npc': npc, 'shopIds': shop_ids, 'name': value(name, 'name', ''),
                  'function': value(name, 'func', ''), 'placements': []}
        try:
            info = load(args.data / f'Npc/{npc:07d}.img').get('info')
            scripts = [value(n, 'script', '') for n in children(info.get('script') if info else None)]
            candidates = []
            for script in scripts[:1]:
                candidates.extend([f'npc/{script}', f'BeiDouSpecial/{script}'])
            candidates.append(f'npc/{npc}')
            script = next((key for key in candidates if packed.get(key)), None)
            source_key, source_path = None, None
            for key in candidates:
                source_path = next((args.server / folder / (key + '.js')
                    for folder in ['scripts-zh-CN', 'scripts']
                    if (args.server / folder / (key + '.js')).is_file()), None)
                if source_path is not None:
                    source_key = key
                    break
            record.update(scripts=scripts, resolvedPackedScript=script,
                          resolvedSourceScript=source_key,
                          sourcePackMatch=(source_key == script and
                              (source_path is None or source_path.read_text(encoding='utf-8-sig') == packed.get(script, '').lstrip('\ufeff'))),
                          speak=bool(children(info.get('speak') if info else None)),
                          mapleTV=value(info, 'MapleTV', 0) == 1)
            record['currentNoQuestPath'] = ('script' if script else 'silent-return' if record['speak'] or record['mapleTV'] else 'missing-script-toast')
            record['scriptOpenShopCalls'] = re.findall(r'openShopNPC\s*\(\s*(\d+)', packed.get(script, ''))
        except Exception as error:
            errors.append({'npc': npc, 'error': str(error)})
        entries[npc] = record
    paths = sorted((args.data / 'Map/Map').glob('Map*/*.img'))
    for index, path in enumerate(paths):
        try:
            image = load(path)
            link = value(image, 'info/link')
            if link is not None:
                target = str(link).zfill(9)
                image = load(args.data / f'Map/Map/Map{int(target) // 100000000}/{target}.img')
            footholds = {}
            for layer in children(image.get('foothold')):
                for group in children(layer):
                    for fh in children(group):
                        footholds[int(fh.name)] = (int(layer.name), int(group.name))
            for life in children(image.get('life')):
                if value(life, 'type') != 'n':
                    continue
                npc = int(value(life, 'id', 0))
                if npc not in entries:
                    continue
                fh = int(value(life, 'fh', 0))
                entries[npc]['placements'].append({'map': int(path.stem), 'life': life.name,
                    'x': value(life, 'x'), 'y': value(life, 'y'), 'fh': fh,
                    'footholdLayerGroup': footholds.get(fh), 'hide': value(life, 'hide', 0)})
            del image
        except Exception as error:
            errors.append({'map': path.stem, 'error': str(error)})
        if index % 500 == 0:
            gc.collect()
            print(f'maps {index}/{len(paths)}', flush=True)
    result = {'scope': 'All shops in original SQL; all map IMG NPC life placements, including linked maps. Static no-quest dispatch path; no runtime click or render acceptance.',
              'sources': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in binding_sources + [stock_sql]},
              'mapFiles': len(paths), 'shopRows': len(shop_rows),
              'shops': sorted((r for r in entries.values() if r['shopIds']), key=lambda x: x['npc']),
              'merchantLabelsWithoutShop': sorted((r for r in entries.values() if not r['shopIds']), key=lambda x: x['npc']),
              'errors': errors}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    from collections import Counter
    print(json.dumps({'shopRows': len(shop_rows), 'shopNpcs': len(result['shops']),
                      'merchantLabelsWithoutShop': len(result['merchantLabelsWithoutShop']),
                      'shopNpcPlacements': sum(len(x['placements']) for x in result['shops']),
                      'extraMerchantPlacements': sum(len(x['placements']) for x in result['merchantLabelsWithoutShop']),
                      'shopNpcPaths': dict(Counter(x.get('currentNoQuestPath', 'error') for x in result['shops'])),
                      'errors': len(errors)}, ensure_ascii=False))


if __name__ == '__main__':
    main()
