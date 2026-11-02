#!/usr/bin/env python3
"""Compare every fourth-job/extended skill level with the installed source IMG.

Includes hidden V/VI replay components. Resource parity is not a claim that
every original client state machine has been ported.
"""
import hashlib
import json
from pathlib import Path

import export_jobs as source
from audit_skill_nodes import branches

ROOT = Path(__file__).resolve().parents[1]


def action_values(node):
    if node is None:
        return None
    value = source.num(node)
    if value is not None:
        return value
    return {child.name: action_values(child) for child in node.children()}


def main():
    tree = json.loads((ROOT / 'Assets/Resources/UI/JobTree.json').read_text())
    replays = json.loads((ROOT / 'Assets/Resources/NativeSkillReplays.json').read_text())
    videos = json.loads((ROOT / 'Assets/Resources/NativeSkillVideos.json').read_text())
    errors, entries, hashes = [], [], {}
    for job in tree['jobs']:
        if job['tier'] != 4:
            continue
        jid = job['id']
        filename = f'Skill/{jid:03d}.img'
        original = source.DATA / filename
        copied = ROOT.parent / 'MapleDashResources/Data' / filename
        digest = hashlib.sha256(original.read_bytes()).hexdigest()
        hashes[filename] = digest
        if not copied.exists() or hashlib.sha256(copied.read_bytes()).hexdigest() != digest:
            errors.append(f'{filename}: copied IMG differs')
        nodes = source.wz(f'Skill/{jid:03d}').get('skill')
        for node in nodes.children():
            sid = int(node.name)
            exported = tree['skills'].get(str(sid))
            rows = source.level_rows(node)
            if not rows:
                continue
            if exported is None:
                errors.append(f'{sid}: absent skill data')
                continue
            levels = {str(r['level']): {k: v for k, v in r.items() if k != 'level'} for r in rows}
            if exported['levels'] != levels:
                errors.append(f'{sid}: level parameters differ')
            if sid not in job['skills']:
                errors.append(f'{sid}: absent panel row')
            clips = {k: n for part in branches(node).values() for k, n in part.items()}
            stages = replays.get(str(sid), [])
            for stage in stages:
                if str(stage['skill']) not in tree['skills']:
                    errors.append(f'{sid}: absent replay component {stage["skill"]}')
            video = videos.get(str(sid))
            if video and not (ROOT.parent / 'MapleDashResources/Data' / video).exists():
                errors.append(f'{sid}: missing MCV {video}')
            entries.append(dict(id=sid, name=exported['name'], job=jid,
                levelCount=len(levels), levels=levels,
                action=action_values(node.get('action')), visualClips=clips,
                video=video, replayStages=stages,
                expectedMagic=jid in (212, 222, 232, 1212),
                hasMad=any('mad' in r for r in levels.values())))
    report = dict(sourceHashes=hashes, skillCount=len(entries),
        levelCount=sum(e['levelCount'] for e in entries),
        physicalSkillsWithMad=[e['id'] for e in entries if e['hasMad'] and not e['expectedMagic']],
        errors=errors, skills=entries,
        boundary='IMG/list/level/clip inventory and replay references only; dedicated motion/state machines require runtime evidence.')
    (ROOT / 'docs/advanced-skill-audit-2026-11-01.json').write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    print(f'{len(hashes)} jobs / {report["skillCount"]} skills / {report["levelCount"]} levels; '
          f'{len(report["physicalSkillsWithMad"])} physical skills also declare mad; errors={len(errors)}')
    for error in errors:
        print(error)
    if errors:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
