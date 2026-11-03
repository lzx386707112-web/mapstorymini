#!/usr/bin/env python3
"""Audit 282 packed monster defense fields without inventing absent values.

Input is MsUnpack's flattened Mob_<seven-digit-id>.img extraction directory.
Missing fields remain null, distinct from explicit zero. No runtime cutover.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT.parent.parent / 'Beidou-Server/tool/wz-python'))
from wzpy import WzImage, WzKey


def export(directory, overlay):
    rows = {}
    for path in sorted(directory.glob('Mob_*.img')):
        ident = path.stem.removeprefix('Mob_')
        if not ident.isdecimal():
            continue
        raw = path.read_bytes()
        image = WzImage.from_bytes(raw, key=WzKey.for_region('BMS'), name=path.name)
        root = image.parse_partial(only=frozenset({'info'}))
        if image.truncated or image.parse_warnings:
            raise ValueError(f'{path}: {image.parse_warnings}')
        info = root.get('info')
        if info is None:
            raise ValueError(f'{path}: missing info')
        values = {p.name: p.value for p in info.children()}
        rows[str(int(ident))] = {
            'sha256': hashlib.sha256(raw).hexdigest(),
            **{k: values.get(k) for k in ('PDRate', 'MDRate', 'link', 'level', 'boss')},
        }
    overrides = []
    for entry in json.loads(overlay.read_text())['entries']:
        parts = entry['path'].split('\\')
        if len(parts) != 4 or parts[0].lstrip('-') != 'Mob' or parts[2] != 'info':
            continue
        if parts[3] not in ('PDRate', 'MDRate'):
            continue
        overrides.append(entry)
        # Preserve the original packed value as evidence. Overlay semantics are
        # recorded separately until the client overlay application is verified.
    return {'schemaVersion': 1, 'monsters': rows, 'defenseOverlayEntries': overrides}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, required=True)
    parser.add_argument('--overlay', type=Path, default=ROOT.parent/'docs/tms282-skill/data/data-overlay-index.json')
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--runtime-out', type=Path)
    args = parser.parse_args()
    result = export(args.input, args.overlay)
    if not result['monsters']:
        raise ValueError('No monster templates found')
    args.out.write_text(json.dumps(result, ensure_ascii=False, sort_keys=True, indent=1)+'\n')
    rows = result['monsters']
    if args.runtime_out:
        ids = set()
        for rel in ('Field/FieldMobs.json', 'GameplayCombat.json'):
            ids.update(str(m['id']) for m in json.loads((ROOT/'Assets/Resources'/rel).read_text())['mobs'])
        ids.update(str(int(p.stem)) for p in (ROOT.parent/'MapleDashResources/Data/Mob').glob('*.img') if p.stem.isdecimal())
        rates = {i: {k: row[k] if row[k] is not None else 0 for k in ('PDRate', 'MDRate')} for i, row in rows.items()}
        legacy = sorted(ids-rows.keys(), key=int)
        for i in legacy:
            rates[i] = {'PDRate': 10, 'MDRate': 10}
        for entry in result['defenseOverlayEntries']:
            parts = entry['path'].split('\\')
            ident = str(int(parts[1].removesuffix('.img')))
            # Current overlay agrees exactly with the packed fields. Do not
            # silently guess overlay precedence if this changes in another build.
            if rates[ident][parts[3]] != entry['value']:
                raise ValueError('Unverified defense overlay conflict: '+entry['path'])
        payload = {'schemaVersion': 1, 'missingFieldDefault': 0, 'legacyDefault': 10,
                   'legacyTemplateIds': legacy, 'monsters': rates}
        args.runtime_out.parent.mkdir(parents=True, exist_ok=True)
        args.runtime_out.write_text(json.dumps(payload, ensure_ascii=False, sort_keys=True, separators=(',', ':'))+'\n')
    print(json.dumps({'monsters': len(rows), 'missingFields': {
        k: sum(v[k] is None for v in rows.values()) for k in ('PDRate', 'MDRate')
    }, 'overlayEntries': result['defenseOverlayEntries']}, ensure_ascii=False))


if __name__ == '__main__':
    main()
