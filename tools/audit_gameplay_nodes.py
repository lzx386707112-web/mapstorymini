#!/usr/bin/env python3
"""Stream every original skill/map/mob IMG; inventory semantic node families.

This is a data inventory, not proof that a node's behavior is implemented.
Numeric indices are normalized; counts are files containing the node family.
"""
import gc
import hashlib
import json
from collections import defaultdict
from pathlib import Path
import wzmap
from wzpy import WzImage
from wzpy.properties import WzUolProperty

ROOT = Path(__file__).resolve().parents[1]


def inventory(kind, paths):
    families = defaultdict(lambda: {'files': 0, 'examples': []})
    sources, errors, records = {}, [], []
    for index, path in enumerate(paths):
        relative = path.relative_to(wzmap.DATA).as_posix()
        raw = path.read_bytes()
        sources[relative] = hashlib.sha256(raw).hexdigest()
        try:
            image = WzImage.from_bytes(raw, key=wzmap.KEY, name=path.stem)
            image.parse()
            if kind == 'skills' and image.get('skill') is not None:
                for skill in image.get('skill').children():
                    if not skill.name.isdigit():
                        continue
                    levels = skill.get('level')
                    fields = set()
                    if levels is not None:
                        for level in levels.children():
                            fields.update(c.name for c in level.children())
                    records.append({'id': int(skill.name), 'source': relative,
                                    'rootNodes': sorted(c.name for c in skill.children()),
                                    'levelFields': sorted(fields)})
            found = set()

            def walk(node, prefix='', depth=0):
                for child in node.children():
                    name = '*' if child.name.isdigit() else child.name
                    key = prefix + '/' + name if prefix else name
                    found.add(key)
                    # Canvas metadata belongs to rendering, UOL is a reference:
                    # do not expand links and conflate authored and inherited nodes.
                    if not isinstance(child, WzUolProperty) and depth < 7:
                        walk(child, key, depth + 1)
            walk(image)
            for key in found:
                entry = families[key]
                entry['files'] += 1
                if len(entry['examples']) < 3:
                    entry['examples'].append(relative)
            del image
        except Exception as error:
            errors.append({'file': relative, 'error': str(error)})
        if index % 250 == 0:
            gc.collect()
    print(kind, len(sources), 'files,', len(families), 'families,', len(errors), 'errors', flush=True)
    return {'sources': sources, 'families': dict(sorted(families.items())), 'errors': errors,
            **({'skills': records} if kind == 'skills' else {})}


def main():
    data = Path(wzmap.DATA)
    result = {
        'scope': 'Authored paths, numeric components normalized to *, depth <= 8; UOL targets not expanded. Counts are files, not runtime support.',
        'skills': inventory('skills', sorted((data / 'Skill').glob('*.img'))),
        'maps': inventory('maps', sorted((data / 'Map/Map').glob('Map*/*.img'))),
        'mobs': inventory('mobs', sorted((data / 'Mob').glob('*.img'))),
    }
    server = data.parents[1] / 'gms-server/src/main/java/org/gms'
    logic = ['client/SkillFactory.java', 'client/Character.java', 'server/StatEffect.java',
             'server/maps/MapFactory.java', 'server/life/LifeFactory.java', 'server/life/MobSkill.java',
             'net/server/channel/handlers/AbstractDealDamageHandler.java']
    result['logicSources'] = {p: hashlib.sha256((server / p).read_bytes()).hexdigest() for p in logic}
    result['clientBinary'] = {
        'file': 'clien/BeiDou.exe',
        'sha256': hashlib.sha256((data.parent / 'BeiDou.exe').read_bytes()).hexdigest(),
        'note': 'Identity only; no new EXE control-flow conclusions in this audit.',
    }
    target = ROOT / 'tools/native_gameplay_nodes.json'
    target.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    print(target, flush=True)


if __name__ == '__main__':
    main()
