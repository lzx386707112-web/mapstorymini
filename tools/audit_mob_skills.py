#!/usr/bin/env python3
"""Assert the mob-skill data invariants that NativeMobController depends on.

Reads tools/native_mob_skills.json (produced by export_mob_skills.py) and checks
the *source side* rules that the runtime ports.  Every rule below is quoted from
the Java server or from LifeFactory's WZ read order; nothing here is inferred
from observed gameplay.

Exit code 0 = every invariant holds, 1 = a violation (printed).
"""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / 'tools' / 'native_mob_skills.json'

# MobSkillType.java -- the only ids getMonsterStats accepts.
MOBSKILL_TYPES = {
    100, 101, 102, 103, 110, 111, 112, 113, 114, 115,
    120, 121, 122, 123, 124, 125, 126, 127, 128, 129, 131, 132, 133,
    134, 135, 136, 138,
    140, 141, 142, 143, 144, 145, 146,
    150, 151, 152, 153, 154, 155, 156, 157,
    170, 171, 172, 174, 176, 177,
    183, 184, 185, 186, 187, 188, 189, 190, 191,
    200, 201, 202, 203, 214, 215,
}
# MobSkill.applyEffect has no branch for these, so the source does nothing.
NO_EFFECT_BRANCH = {105, 115, 134, 135, 136, 138, 142, 146, 171, 172, 169}
# MobSkill.applyDisease targets (SEAL..UNDEAD).
DISEASE_TYPES = {120, 121, 122, 123, 124, 125, 126, 128, 132, 133}
# MobSkillType.from() rejects anything outside 100..215 before the enum lookup.
ENUM_RANGE = (100, 215)
# Skill ids used by Mob/*/info/skill that MobSkillType does not define. The source
# throws on these, so the affected mobs (9309201, 9460022/3/4) never load.
EXPECTED_UNSUPPORTED_IDS = {105, 169}
# Mob/*/info/skill rows pointing at a MobSkill.img level that does not exist.
# MobSkillFactory.getMobSkillOrThrow throws on these too.
EXPECTED_MISSING_LEVELS = {
    '8810103->132/10', '8810105->135/5', '8810106->134/5', '8810106->135/3',
    '8890034->128/128', '9300394->115/2', '9400923->123/36',
    '9460022->169/1', '9460023->169/4', '9460024->169/2', '9460024->169/3',
    '9500390->110/10', '9500391->110/10', '9500392->110/10',
}
# Mobs whose only content is info/link; maxHP is absent so the server sees 0.
EXPECTED_ZERO_HP = {'9501016'}
# Mobs whose info/skill makes getMonsterStats throw (either orElseThrow site), so
# the source server cannot spawn them at all. NativeMobController logs and skips
# the bad row instead -- a deliberate divergence, recorded here so a data change
# shows up as a diff instead of silently growing.
EXPECTED_UNLOADABLE = {
    '8810103', '8810105', '8810106', '8890034', '9300394', '9309201',
    '9400923', '9460022', '9460023', '9460024', '9500390', '9500391', '9500392',
}

failures = []
notes = []


def check(condition, message):
    if not condition:
        failures.append(message)
    return condition


def main():
    if not DATA.exists():
        print('missing %s -- run tools/export_mob_skills.py first' % DATA)
        return 1
    data = DATA.read_text(encoding='utf-8')
    blob = json.loads(data)
    types = blob['mobSkillTypes']
    # Mob/Mob.img is a stray non-monster file; it has no info node and no id.
    mobs = {k: v for k, v in blob['mobs'].items() if k.isdigit()}

    # ---- 1. every declared skill id must resolve in MobSkillType -------------
    unsupported = {}
    out_of_range = {}
    for mob_id, record in mobs.items():
        for row in record.get('skills', []):
            sid = int(row['skill'])
            if not (ENUM_RANGE[0] <= sid <= ENUM_RANGE[1]):
                out_of_range.setdefault(sid, []).append(mob_id)
            elif sid not in MOBSKILL_TYPES:
                unsupported.setdefault(sid, []).append(mob_id)
    # LifeFactory:596 calls MobSkillType.from(id).orElseThrow(), so an unknown id
    # aborts getMonsterStats: those mobs do not exist in the source server at all.
    check(set(unsupported) == EXPECTED_UNSUPPORTED_IDS,
          'unexpected MobSkillType ids: %s' % sorted(unsupported))
    check(not out_of_range, 'skill ids outside 100..215: %s' % sorted(out_of_range))
    notes.append('mob skills that abort LifeFactory (orElseThrow): %s'
                 % {k: len(v) for k, v in sorted(unsupported.items())})

    # ---- 2. every (type, level) must exist in MobSkill.img ------------------
    # Same outcome as rule 1: getMobSkillOrThrow throws while loading the mob, so
    # these 14 rows belong to mobs the source can never spawn.  NativeMobController
    # logs and skips them instead of aborting the whole mob.
    missing_level = []
    for mob_id, record in mobs.items():
        for row in record.get('skills', []):
            sid, level = str(row['skill']), str(row.get('level'))
            entry = types.get(sid)
            if entry is None or level not in entry['rows']:
                missing_level.append('%s->%s/%s' % (mob_id, sid, level))
    check(set(missing_level) == EXPECTED_MISSING_LEVELS,
          'MobSkillFactory would throw for unexpected refs: %s'
          % (sorted(set(missing_level) - EXPECTED_MISSING_LEVELS)[:8],))
    notes.append('refs to a non-existent MobSkill level (source cannot load those mobs): %d'
                 % len(missing_level))

    # ---- 2b. the exact set of mobs the source can never load ---------------
    unloadable = set()
    for mob_id, record in mobs.items():
        for row in record.get('skills', []):
            sid, level = int(row['skill']), str(row.get('level'))
            entry = types.get(str(sid))
            if sid not in MOBSKILL_TYPES or entry is None or level not in entry['rows']:
                unloadable.add(mob_id)
    check(unloadable == EXPECTED_UNLOADABLE,
          'unloadable mob set changed: +%s -%s'
          % (sorted(unloadable - EXPECTED_UNLOADABLE), sorted(EXPECTED_UNLOADABLE - unloadable)))
    # 105 is authored in MobSkill.img but missing from the Java enum, so the WZ
    # presence check alone is not enough -- both sites have to be checked.
    check('105' in types and 105 not in MOBSKILL_TYPES,
          '105 changed status: MobSkill.img has it, MobSkillType must not')
    notes.append('mobs the source server can never load: %d %s'
                 % (len(unloadable), sorted(unloadable)))

    # ---- 3. skill<action> root container drives the cast delay --------------
    # LifeFactory:599 reads monsterData.getChildByPath("skill"+action) and sums
    # every child's delay with a 0 default; a missing root means animationTime 0,
    # i.e. MoveLifeHandler:136 applies the effect immediately.
    used_rows = set()
    for record in mobs.values():
        for row in record.get('skills', []):
            used_rows.add((str(row['skill']), str(row.get('level'))))
    no_range = 0
    zero_delay = 0
    for sid, level in sorted(used_rows):
        entry = types.get(sid)
        if entry is None or level not in entry['rows']:
            continue
        row = entry['rows'][level]
        if not ('lt' in row and 'rb' in row):
            no_range += 1
    for mob_id, record in mobs.items():
        body = set(record.get('body', []))
        for index, row in enumerate(record.get('skills', [])):
            action = row.get('action', index + 1)
            if ('skill%s' % action) not in body:
                zero_delay += 1
    notes.append('skill rows without lt+rb (single-target branch): %d/%d'
                 % (no_range, len(used_rows)))
    notes.append('skill rows whose skill<action> root is missing (immediate cast): %d' % zero_delay)

    # ---- 4. hp gate and chance roll actually matter -------------------------
    hp_zero = 0
    hp_partial = 0
    prop_partial = 0
    total_rows = 0
    for entry in types.values():
        for row in entry['rows'].values():
            total_rows += 1
            hp = row.get('hp', 100)
            if hp <= 0:
                hp_zero += 1   # MobSkillFactory:97-99 rewrites it to 100
            elif hp < 100:
                hp_partial += 1
            if row.get('prop', 100) != 100:
                prop_partial += 1
    notes.append('MobSkill rows: %d, hp<=0 (rewritten to 100): %d, 0<hp<100: %d'
                 % (total_rows, hp_zero, hp_partial))
    notes.append('MobSkill rows with prop != 100 (makeChanceResult gate): %d' % prop_partial)

    # ---- 5. attack rows carry the fields the hit path reads ----------------
    attack_total = 0
    with_after = 0
    with_fix = 0
    with_deadly = 0
    off_disease = {}
    for mob_id, record in mobs.items():
        for attack in record.get('attacks', []):
            attack_total += 1
            if 'attackAfter' in attack:
                with_after += 1
            if attack.get('fixDamR'):
                with_fix += 1
            if attack.get('fixedDamage'):
                failures.append('attack %s/%s authors fixedDamage, which the source never reads'
                                % (mob_id, attack['slot']))
            if 'deadlyAttack' in attack:
                with_deadly += 1
            disease = int(attack.get('disease', 0) or 0)
            # TakeDamageHandler:198 runs the full applyEffect for this id, not only a
            # disease. 127/131/135/136/187 are real MobSkillTypes, so they are legal
            # there; 137 is not in the enum and resolves to nothing.
            if disease and disease not in MOBSKILL_TYPES:
                off_disease[disease] = off_disease.get(disease, 0) + 1
    check(set(off_disease) <= {137},
          'attack disease ids outside MobSkillType: %s' % (off_disease,))
    notes.append('attack disease ids that resolve to no MobSkill: %s' % (off_disease,))
    notes.append('attack rows: %d, attackAfter present: %d, fixDamR: %d, deadlyAttack: %d'
                 % (attack_total, with_after, with_fix, with_deadly))
    notes.append('attack disease ids used: %s'
                 % sorted({int(a.get('disease')) for r in mobs.values()
                           for a in r.get('attacks', []) if a.get('disease')}))

    # ---- 6. hp percentage must not divide by zero --------------------------
    # MoveLifeHandler:172 divides by maxHP; a 0 HP template would produce NaN.
    zero_hp = {m for m, r in mobs.items() if int(r.get('info', {}).get('maxHP', 0) or 0) <= 0}
    check(zero_hp == EXPECTED_ZERO_HP,
          'mobs with maxHP<=0 would break the HP%% gate: %s' % (sorted(zero_hp),))
    notes.append('mobs with maxHP<=0 (link-only stubs): %s' % sorted(zero_hp))

    # ---- 7. noFlip must be readable for the attack range mirror -------------
    # AbstractDealDamageHandler:1396-1398 mirrors only when noFlip is 0 and the
    # mob faces right.
    no_flip = sum(1 for r in mobs.values() if int(r.get('info', {}).get('noFlip', 0) or 0) != 0)
    notes.append('mobs with info/noFlip != 0 (range never mirrored): %d' % no_flip)

    # ---- 8. MobSkill level nodes: client whitelist vs authored data ---------
    # The v83 loader (0x7627C7..0x7632A8) reads only these names.  Everything else
    # in the data is dead, so a new node appearing here means someone edited the WZ
    # with a different (newer) tool and the client will silently ignore it.
    client_read = {
        'hp', 'conMP', 'time', 'interval', 'prop', 'x', 'y', 'count', 'direction',
        'elemAttr', 'lt', 'rb', 'effect', 'hit', 'mob', 'affected', 'tile',
        'summonEffect', 'limit',
    }
    # Authored but never read by the client and never read by the Java server.
    # mpCon is the name the *server* (and the WZ data) uses; the client loader asks
    # for "conMP" instead (string id 5620), so it never sees a mob-skill MP cost.
    known_unread = {
        'info', 'mob0', 'tremble', 'screen', 'lua', 'summonOnce', 'fieldScript',
        'linkHP', 'afterEffect', 'rangeGap', 'affectedPhase', 'affected_after',
        'HP', 'head', 'face', 'bossHeal', 'targetType', 'exchangeAttack', 'inteval',
        'bombInfo', 'mpCon', 'action', 'condition',
    }
    seen_nodes = {}
    direction_rows = []
    screen_rows = []
    for type_id, entry in types.items():
        for level, row in entry['rows'].items():
            for name in row.get('nodes', []):
                seen_nodes[name] = seen_nodes.get(name, 0) + 1
                if name == 'direction':
                    direction_rows.append('%s/%s' % (type_id, level))
                if name == 'screen':
                    screen_rows.append('%s/%s' % (type_id, level))
    unknown = sorted(set(seen_nodes) - client_read - known_unread)
    check(not unknown, 'MobSkill levels use node names outside the known set: %s' % unknown)
    # The client asks for conMP here but the data always spells it mpCon.
    check('conMP' not in seen_nodes,
          'a MobSkill level now authors conMP -- the client would finally see it, '
          'which changes the "client never reads mob MP cost" note')
    # direction is in the client's read list but no level authors it.
    check(not direction_rows,
          'a MobSkill level now authors direction (client reads it, was always 0): %s'
          % direction_rows[:5])
    # screen exists only on 176/1..6 and is never loaded (see §3.5).
    check(sorted(screen_rows) == ['176/%d' % i for i in range(1, 7)],
          'MobSkill screen moved: %s' % sorted(screen_rows))
    notes.append('MobSkill level nodes authored but never loaded: %s'
                 % {k: v for k, v in sorted(seen_nodes.items())
                    if k not in client_read})

    # ---- 9. attack areas come in two mutually exclusive shapes -------------
    # Rect (range/lt+rb) and point+radius (range/sp+r).  NativeMobController only
    # understands the rect form, so the size of the gap has to stay visible.
    rect = circle = both = sp_without_r = 0
    range_children = {}
    for record in mobs.values():
        for attack in record.get('attacks', []):
            names = attack.get('rangeNodes')
            if not names:
                continue
            for name in names:
                range_children[name] = range_children.get(name, 0) + 1
            s = set(names)
            has_rect = 'lt' in s and 'rb' in s
            has_circle = 'sp' in s and 'r' in s
            rect += has_rect
            circle += has_circle
            both += has_rect and has_circle
            sp_without_r += 'sp' in s and 'r' not in s
    check(both <= 2, 'rect and circle areas overlap on %d attacks (expected <=2)' % both)
    notes.append('attack areas: rect %d, point+radius %d, both %d, sp without r %d'
                 % (rect, circle, both, sp_without_r))
    notes.append('attack range children: %s' % dict(sorted(range_children.items())))

    # ---- 10. attack `type` predicts the shape and the effect chain --------
    # BeiDou.exe keeps lt/rb, sp/r and start/areaCount/attackCount in one field
    # block of the 0xA4-byte attack struct and dispatches on `type`.  The data
    # follows that split exactly, so type is the field NativeMobController should
    # key extra behaviour on (projectile, telegraphed wave) rather than guessing
    # from the presence of a container.
    circle_types = {}
    wave_without_warning = []
    wave_not_rect = []
    attackcount_over = []
    stray_inactive = []
    special_attack = []
    for mob_id, record in mobs.items():
        for attack in record.get('attacks', []):
            names = set(attack.get('rangeNodes') or [])
            containers = attack.get('containers') or {}
            kind = attack.get('type')
            if attack.get('inactive'):
                stray_inactive.append('%s/%s' % (mob_id, attack['slot']))
            if attack.get('specialAttack'):
                special_attack.append('%s/%s' % (mob_id, attack['slot']))
            has_rect = 'lt' in names and 'rb' in names
            has_circle = 'sp' in names and 'r' in names
            if has_circle and not has_rect:
                circle_types[kind] = circle_types.get(kind, 0) + 1
            if 'areaCount' in names:
                if 'areaWarning' not in containers:
                    wave_without_warning.append('%s/%s' % (mob_id, attack['slot']))
                if not has_rect:
                    wave_not_rect.append('%s/%s' % (mob_id, attack['slot']))
                values = attack.get('rangeValues') or {}
                if values.get('attackCount', 0) > values.get('areaCount', 0):
                    attackcount_over.append('%s/%s' % (mob_id, attack['slot']))
    check(set(circle_types) <= {1, 2, None},
          'a circle-only (sp+r) attack is no longer type 1/2 -- the type->shape '
          'mapping in monster-skills.md §2.7 is stale: %s' % circle_types)
    check(len(wave_without_warning) <= 2,
          'multi-wave attacks without an areaWarning container: %s' % wave_without_warning[:5])
    check(not wave_not_rect,
          'multi-wave attack without lt/rb (no area to spread over): %s' % wave_not_rect[:5])
    check(not attackcount_over,
          'attackCount exceeds areaCount: %s' % attackcount_over[:5])
    # No attack row authors `inactive`, so the client gate at 0x66A7A2 never fires.
    check(not stray_inactive,
          'attack rows now author inactive=1 -- the client would skip them: %s'
          % stray_inactive[:5])
    notes.append('circle-only attacks by type: %s' % dict(sorted(
        (str(k), v) for k, v in circle_types.items())))
    notes.append('attack rows with specialAttack: %s' % (special_attack or 'none'))

    for line in notes:
        print('note: ' + line)
    if failures:
        for line in failures:
            print('FAIL: ' + line)
        return 1
    print('OK: mob skill data invariants hold (mobs=%d, types=%d)' % (len(mobs), len(types)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
