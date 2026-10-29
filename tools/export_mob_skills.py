#!/usr/bin/env python3
"""Export the source-side monster skill data (read-only WZ oracle).

Two halves, both from the original GMS083 client `Data` folder:

1. ``Skill/MobSkill.img`` -- every ``<type>/level/<n>`` entry with all scalar
   fields plus which effect containers exist.  This is the authoritative
   definition of a mob skill (the server's MobSkillFactory reads the same file).
2. ``Mob/*.img`` -- per-mob ``info`` flags, ``info/skill/<n>`` rows and
   ``info/attack/<n>/info`` rows.  Only authored nodes are read; UOL targets are
   resolved because the client resolves them before use.

Output: tools/native_mob_skills.json (inventory + per-file SHA256 so a later
run can tell whether the source changed).  This file proves *data*, not runtime
behaviour; the behaviour evidence lives in
.agents/skills/beidou-client-reverse/references/monster-skills.md.
"""
import hashlib
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(Path('/Users/lizixian/Documents/mxd/Beidou-Server/tool/wz-python')))

from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzCanvasProperty, WzUolProperty  # noqa: E402

DATA = Path('/Users/lizixian/Documents/mxd/mxd-android/MapleDashResources/Data')
OUT = ROOT / 'tools' / 'native_mob_skills.json'

# info keys that the client template loader reads by name (see
# 0x67D5A0..0x67F400 in BeiDou.exe).  Kept explicit so a renamed node shows up
# as a difference instead of being silently dropped.
INFO_SCALARS = [
    'bodyAttack', 'boss', 'hideHP', 'hideName', 'hideLevel', 'level', 'maxHP',
    'maxMP', 'speed', 'flySpeed', 'chaseSpeed', 'PADamage', 'PDDamage',
    'MADamage', 'MDDamage', 'acc', 'eva', 'exp', 'pushed', 'elemAttr',
    'hpRecovery', 'mpRecovery', 'undead', 'pickUp', 'firstAttack', 'fs',
    'invincible', 'disable', 'noFlip', 'notAttack', 'HPgaugeHide',
    'AngerGauge', 'ChargeCount', 'damagedByMob', 'dropItemPeriod',
    'damagedBySelectedMob', 'damagedBySelectedSkill', 'banType', 'link',
    'removeAfter', 'fixedDamage', 'onlyNormalAttack', 'summonType',
    'publicReward', 'rareItemDropLevel', 'hpTagColor', 'hpTagBgcolor',
    'mobType', 'skeleton',
]
INFO_NODES = ['selfDestruction', 'ban', 'revive', 'skill', 'onDie',
              'onHit', 'onBomb', 'damagedByMob', 'damagedBySelectedMob',
              'damagedBySelectedSkill', 'link']
SKILL_ROW = ['skill', 'level', 'action', 'effectAfter']
ATTACK_SCALARS = [
    'type', 'conMP', 'attackAfter', 'effectAfter', 'magic', 'elemAttr',
    'deadlyAttack', 'doFirst', 'PADamage', 'MADamage', 'fixDamR',
    'fixedDamage', 'disease', 'level', 'mpBurn', 'areaWarning',
    'areaAttack', 'range', 'tremble', 'rush', 'knockback', 'effect',
    # 0x67FF6F-0x67FFB4 / 0x6804A6-0x6804EB / 0x68009E-0x6800E3
    'inactive', 'specialAttack', 'jumpAttack', 'attach', 'attachfacing',
]
# range 容器里的数字子节点。Client-side these land in one struct region
# (see monster-skills.md §2.6): rect = lt/rb, circle = sp/r, wave = start/
# areaCount/attackCount.  Exported so the audit can assert the type mapping
# without a second WZ pass.
RANGE_NUMERIC = ['start', 'areaCount', 'attackCount', 'r']
ATTACK_NODES = ['effect', 'effect0', 'effect1', 'effect2', 'hit', 'ball',
                'range', 'areaWarning', 'areaAttack', 'screen']

MOBSKILL_SCALARS = [
    'mpCon', 'interval', 'time', 'prop', 'x', 'y', 'hp', 'count', 'limit',
    'direction', 'summonEffect', 'elemAttr', 'level', 'lt', 'rb', 'bulletCount',
    'attackAfter', 'effectAfter',
]


def load(relative):
    raw = (DATA / relative).read_bytes()
    image = WzImage.from_bytes(raw, key=WzKey.for_region('GMS'), name=Path(relative).stem)
    image.parse()
    return image, hashlib.sha256(raw).hexdigest()


def number(node):
    value = getattr(node, 'value', None)
    if isinstance(value, bool):
        return int(value)
    if isinstance(value, (int, float, str)):
        return value
    return None


def scalars(node, names):
    out = {}
    if node is None:
        return out
    for name in names:
        child = node.get(name)
        if child is None:
            continue
        value = number(child)
        if value is not None:
            out[name] = value
    return out


def has_frames(node):
    """Number of numeric canvas frames under a container (UOL resolved)."""
    if node is None:
        return 0
    count = 0
    for child in node.children():
        if not child.name.isdigit():
            continue
        if isinstance(child, WzUolProperty):
            count += 1
        elif isinstance(child, WzCanvasProperty):
            count += 1
        else:
            count += 1
    return count


def node_shape(node):
    """Describe containers without dumping pixels: frame count + scalar meta."""
    if node is None:
        return None
    shape = {'frames': has_frames(node)}
    meta = scalars(node, ['origin', 'delay', 'z', 'pos', 'repeat', 'effectType',
                          'effectDistance', 'randomPos', 'fix', 'layer'])
    if meta:
        shape['meta'] = meta
    return shape


def export_mobskill():
    image, sha = load('Skill/MobSkill.img')
    types = {}
    for entry in image.root.children():
        if not entry.name.isdigit():
            continue
        levels = entry.get('level')
        rows = {}
        if levels is not None:
            for level in levels.children():
                if not level.name.isdigit():
                    continue
                row = scalars(level, MOBSKILL_SCALARS)
                # Every authored child name, so the audit can assert the client-read
                # whitelist (0x7627C7..0x7632A8) against what the data actually has.
                row['nodes'] = sorted(child.name for child in level.children()
                                      if not child.name.isdigit())
                row['summons'] = []
                for child in level.children():
                    if child.name.isdigit():
                        value = number(child)
                        if value is not None:
                            row['summons'].append(value)
                row['containers'] = {name: node_shape(level.get(name))
                                     for name in ('affected', 'mob', 'tile', 'effect',
                                                  'hit', 'ball', 'screen', 'areaWarning',
                                                  'areaAttack', 'summon')
                                     if level.get(name) is not None}
                rows[level.name] = row
        top = {'levels': sorted(int(k) for k in rows), 'rows': rows}
        for name in ('info', 'condition', 'action'):
            child = entry.get(name)
            if child is not None:
                top[name] = scalars(child, ['interval', 'time', 'prop', 'hp'])
        types[entry.name] = top
    return sha, types


def export_mobs():
    files = {}
    mob_dir = DATA / 'Mob'
    for path in sorted(mob_dir.glob('*.img')):
        relative = 'Mob/' + path.name
        image, sha = load(relative)
        info = image.get('info')
        record = {'sha256': sha}
        if info is not None:
            record['info'] = scalars(info, INFO_SCALARS)
            record['presence'] = {name: True for name in INFO_NODES if info.get(name) is not None}
            destruction = info.get('selfDestruction')
            if destruction is not None:
                record['selfDestruction'] = scalars(destruction, ['hp', 'action', 'removeAfter'])
            ban = info.get('ban')
            if ban is not None:
                record['ban'] = scalars(ban, ['banType', 'banMsg', 'banMsgType'])
            revive = info.get('revive')
            if revive is not None:
                record['revive'] = [number(c) for c in revive.children() if number(c) is not None]
            skills = []
            table = info.get('skill')
            if table is not None:
                for row in table.children():
                    entry = scalars(row, SKILL_ROW)
                    if 'skill' in entry:
                        skills.append(entry)
            record['skills'] = skills
        # Mob/<id>.img/attack<N> -- body animation with its own info node.
        attacks = []
        for child in image.root.children():
            if not child.name.startswith('attack') or child.name == 'attack':
                continue
            inner = child.get('info')
            entry = {'slot': child.name}
            if inner is not None:
                entry.update(scalars(inner, ATTACK_SCALARS))
                entry['containers'] = {name: node_shape(inner.get(name))
                                       for name in ATTACK_NODES if inner.get(name) is not None}
                # Attack areas are not always rectangles: sp+r is a point+radius form
                # and lt2/rb2 is a second rectangle.  Record the child names so the
                # audit can see them without a second WZ pass.
                rng = inner.get('range')
                if rng is not None:
                    entry['rangeNodes'] = sorted(g.name for g in rng.children())
                    values = {}
                    for point in ('lt', 'rb', 'sp'):
                        node = rng.get(point)
                        xy = getattr(node, 'value', None) if node is not None else None
                        if isinstance(xy, (tuple, list)) and len(xy) == 2:
                            values[point] = [int(xy[0]), int(xy[1])]
                    for name in RANGE_NUMERIC:
                        value = number(rng.get(name))
                        if value is not None:
                            values[name] = value
                    if values:
                        entry['rangeValues'] = values
            attacks.append(entry)
        record['attacks'] = attacks
        # body animation containers actually authored on this mob
        record['body'] = sorted(c.name for c in image.root.children()
                                if not c.name.isdigit() and c.name != 'info')
        files[path.stem] = record
    return files


def main():
    skill_sha, types = export_mobskill()
    mobs = export_mobs()

    mob_skill_usage = {}
    flag_counts = {}
    attack_nodes = {}
    mobility = {'none': 0, 'move': 0, 'move+jump': 0, 'fly': 0}
    for mob_id, record in mobs.items():
        for entry in record.get('skills', []):
            key = '%s/%s' % (entry['skill'], entry.get('level'))
            mob_skill_usage.setdefault(key, []).append(mob_id)
        for name in record.get('presence', {}):
            flag_counts[name] = flag_counts.get(name, 0) + 1
        for attack in record.get('attacks', []):
            for name in attack.get('containers', {}):
                attack_nodes[name] = attack_nodes.get(name, 0) + 1
        body = set(record.get('body', []))
        if 'fly' in body:
            mobility['fly'] += 1
        elif 'move' in body and 'jump' in body:
            mobility['move+jump'] += 1
        elif 'move' in body:
            mobility['move'] += 1
        else:
            mobility['none'] += 1

    result = {
        '_note': 'Source data inventory. Presence of a node is not proof of runtime consumption.',
        'dataRoot': str(DATA),
        'mobSkillImgSha256': skill_sha,
        'mobSkillTypes': types,
        'mobs': mobs,
        'summary': {
            'mobFiles': len(mobs),
            'mobSkillTypesUsed': len({k.split('/')[0] for k in mob_skill_usage}),
            'mobSkillEntriesUsed': len(mob_skill_usage),
            'mobSkillUsage': {k: sorted(v) for k, v in sorted(mob_skill_usage.items())},
            'infoFlagFiles': dict(sorted(flag_counts.items())),
            'attackContainerFiles': dict(sorted(attack_nodes.items())),
            'mobility': mobility,
        },
    }
    OUT.write_text(json.dumps(result, ensure_ascii=False, indent=1), encoding='utf-8')
    print('mobSkillImg', skill_sha)
    print('types', len(types), 'mobs', len(mobs))
    print('skill usage entries', len(mob_skill_usage))
    print('attack containers', attack_nodes)
    print('mobility', mobility)
    print('wrote', OUT)


if __name__ == '__main__':
    main()
