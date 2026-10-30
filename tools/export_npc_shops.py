#!/usr/bin/env python3
"""Replay original shop migrations into the portable ordinary-shop seed."""
import hashlib
import json
import re
import sqlite3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MIGRATIONS = ROOT.parent.parent / 'Beidou-Server/gms-server/src/main/resources/db/migration'

def export():
    db = sqlite3.connect(':memory:')
    db.execute('CREATE TABLE shops (shopid INTEGER PRIMARY KEY, npcid INTEGER)')
    db.execute('CREATE TABLE shopitems (shopitemid INTEGER PRIMARY KEY AUTOINCREMENT, shopid INTEGER, itemid INTEGER, price INTEGER, pitch INTEGER DEFAULT 0, position INTEGER)')
    # Native MySQL schema starts generated stock IDs at 20047.
    db.execute("INSERT INTO sqlite_sequence(name,seq) VALUES ('shopitems',20046)")
    sources = {}
    version = lambda p: tuple(map(int, p.name.split('__')[0][1:].split('.')))
    for path in sorted(MIGRATIONS.glob('V*__*.sql'), key=version):
        sql = re.sub(r'/\*.*?\*/|--[^\n]*|#[^\n]*', '', path.read_text(), flags=re.S)
        for raw in sql.split(';'):
            statement = raw.strip()
            ordinary = re.match(r'(?:INSERT\s+(?:IGNORE\s+)?INTO|UPDATE|DELETE\s+FROM)\s+`?(shops|shopitems)`?\b', statement, re.I)
            cleanup = re.match(r'DELETE target FROM `shopitems` AS target INNER JOIN `([^`]+)` AS removed ON (.*)', statement, re.I | re.S)
            temporary = re.match(r'(?:CREATE TEMPORARY TABLE|INSERT INTO|DROP TEMPORARY TABLE)\s+`_removed_', statement, re.I)
            if not (ordinary or cleanup or temporary):
                continue
            sources[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
            if cleanup:
                table, condition = cleanup.groups()
                statement = f'DELETE FROM shopitems AS target WHERE EXISTS (SELECT 1 FROM `{table}` AS removed WHERE {condition})'
            else:
                statement = re.sub(r'ON DUPLICATE KEY UPDATE.*', '', statement, flags=re.I | re.S)
                statement = re.sub(r'INSERT IGNORE INTO', 'INSERT OR IGNORE INTO', statement, flags=re.I)
                statement = re.sub(r'^INSERT INTO `?shops`?', 'INSERT OR REPLACE INTO shops', statement, flags=re.I)
                statement = statement.replace('CREATE TEMPORARY TABLE', 'CREATE TABLE').replace('DROP TEMPORARY TABLE', 'DROP TABLE')
            try:
                db.execute(statement)
            except Exception as error:
                raise RuntimeError(f'{path.name}: {statement[:160]}') from error
    result = {'sources': sources}
    for table in ['shops', 'shopitems']:
        cursor = db.execute(f'SELECT * FROM {table}')
        keys = [d[0] for d in cursor.description]
        result[table] = [dict(zip(keys, row)) for row in cursor]
    output = ROOT / 'Assets/Resources/Shop/NpcShops.json'
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, ensure_ascii=False, separators=(',', ':')) + '\n')
    print(f"{len(result['shops'])} shops, {len(result['shopitems'])} stock rows -> {output}")

if __name__ == '__main__':
    export()
