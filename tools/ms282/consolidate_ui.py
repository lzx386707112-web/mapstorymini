#!/usr/bin/env python3
"""Consolidate the 282 UI window files: bake `_outlink` stubs, trim dead branches.

Before:  UI/X.img (structure, canvases are 1x1 stubs with _outlink)
         UI/_Canvas/X.img (all pixels, hundreds of MB, mostly branches this
         client never draws)
After:   UI/X.img — self-contained, only the branches the game draws.

  consolidate_ui.py            dry run: plan, bake, verify, report sizes
  consolidate_ui.py --write    also write the files, delete the pixel files
                               and duplicate imgs, update NativeAssetIndex

The heavy lifting (link resolution, serialization, ImgFile.cs-mirror
verification) lives in img_bake.py.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from img_bake import (  # noqa: E402
    PixelSource, RuntimeImgMirror, bake_tree, check_canvas_pixels,
    expected_tree_from, serialize, trim_root, verify_file, _mismatch_with_pixels,
)

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT.parent / 'MapleDashResources' / 'Data'
INDEX = ROOT / 'Assets' / 'Resources' / 'NativeAssetIndex.json'

from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzUolProperty  # noqa: E402

# (structure file, branches to keep or None for all, merge target or None)
JOBS = [
    ('UI/UIWindow4.img', ['HyperStat', 'AbilitySetting'], None),
    ('UI/UIArcaneForce.img', None, None),
    ('UI/UIAuthenticForce.img', ['AutEquip'], None),
]

# Deleted with --write: the pixel files whose consumers are all consolidated,
# the structure file merged into UI/Enchant.img by import_enhancement_ui.py,
# and a byte-identical duplicate with zero references.
DELETIONS = [
    'UI/_Canvas/Enchant.img',
    'UI/_Canvas/UIWindow4.img',
    'UI/_Canvas/UIArcaneForce.img',
    'UI/_Canvas/UIAuthenticForce.img',
    'UI/_Canvas/BattleSimulation.img',
    'UI/_Canvas/UICharacterInfo.img',
    'UI/Enchant282.img',
    'UI/Login2.img',
]


def uol_audit(mirror_root: dict, watched: tuple[str, ...]) -> list[str]:
    """UOLs inside the runtime-drawn subtrees must still resolve after trimming."""
    problems = []

    def resolve(parent_path: str, value: str):
        base = posixpath.dirname(parent_path) if '/' in parent_path else ''
        return posixpath.normpath(posixpath.join(base, value)).lstrip('/')

    def get(root: dict, path: str):
        node = root
        for part in path.split('/'):
            if part in ('', '.', '..'):
                continue
            node = next((c for c in node.get('children', []) if c['name'] == part), None)
            if node is None:
                return None
        return node

    stack = [('', child) for child in mirror_root['children']]
    while stack:
        path, node = stack.pop()
        for child in node.get('children', []):
            stack.append((f'{path}/{child["name"]}', child))
        if node.get('kind') != 'UOL':
            continue
        target = resolve(path, str(node.get('value')))
        full = f'{target}' if not target.startswith('/') else target[1:]
        if get(mirror_root, full) is None and any(
                f'{w}/' in f'{path}/' or path.startswith(w) for w in watched):
            problems.append(f'UOL {path} -> {node.get("value")} no longer resolves')
    return problems


import posixpath  # noqa: E402


def consolidate(write: bool) -> int:
    key = WzKey.for_region('GMS')
    source = PixelSource(DEST, key,
                         fallback_root=Path.home() / 'Documents/mxd/MapleStory/Data')
    failures = 0
    results: dict[str, bytes] = {}

    for rel, keep, _merge in JOBS:
        print(f'== {rel} (keep {keep or "all"})')
        img = WzImage.from_bytes((DEST / rel).read_bytes(), key=key, name=rel)
        img.parse()
        dropped = trim_root(img.root, keep)
        if dropped:
            print(f'  dropped branches: {", ".join(dropped)}')
        stats = {'baked': 0, 'pixels': 0}
        bake_tree(img.root, source, stats)
        print(f'  baked {stats["baked"]} canvas(es), {stats["pixels"] / 1048576:.2f} MiB pixels')

        expected = expected_tree_from(img)
        data = serialize(img)
        old = (DEST / rel).stat().st_size
        print(f'  size {old / 1048576:.2f} -> {len(data) / 1048576:.2f} MiB')

        mirror = RuntimeImgMirror(data, key)
        problems = verify_file(data, expected, key, mirror=mirror)
        # Structure compare ignores payload bytes (compared as RGBA next);
        # keep the problem list readable by using the tolerant variant.
        struct_problems: list[str] = []
        _mismatch_with_pixels(expected, mirror.root, '<root>', struct_problems)
        problems.extend(p for p in struct_problems if p not in problems)
        watched = tuple(keep) if keep else tuple(
            c['name'] for c in expected['children'])
        problems.extend(uol_audit(mirror.root, watched))
        checked = check_canvas_pixels(expected, mirror.root, problems)
        print(f'  verified: {checked} canvas(es) pixel-identical, '
              f'{len(problems)} problem(s)')
        for p in problems[:12]:
            print(f'    ! {p}')
        if problems:
            failures += 1
            continue
        results[rel] = data

    if failures or not write:
        print('dry run complete' if not write else 'FAILURES — nothing written')
        return 1 if failures else 0

    for rel, data in results.items():
        (DEST / rel).write_bytes(data)
        print(f'wrote {rel} ({len(data)} bytes)')

    # Guard the deletions: no img outside the consolidated set may still
    # point into the deletion list.
    consumers = scan_consumers()
    consolidated = {rel for rel, _, _ in JOBS} | {'UI/Enchant282.img'}
    trouble = {c: refs for c, refs in consumers.items() if c not in consolidated}
    if trouble:
        print('REFUSING deletions, unexpected consumers:', trouble)
        return 1
    for rel in DELETIONS:
        path = DEST / rel
        if path.is_file():
            path.unlink()
            print(f'deleted {rel}')
    canvas_dir = DEST / 'UI' / '_Canvas'
    if canvas_dir.is_dir() and not any(canvas_dir.iterdir()):
        canvas_dir.rmdir()
        print('removed empty UI/_Canvas/')

    update_index(results)
    return 0


def scan_consumers() -> dict[str, set[str]]:
    """Which imgs hold `_outlink` strings into the deletion set (recent + UI)."""
    from wzpy.properties import WzCanvasProperty
    key = WzKey.for_region('GMS')
    cutoff = time.mktime(time.strptime('2026-11-01', '%Y-%m-%d'))
    candidates = [p for p in DEST.rglob('*.img')
                  if p.stat().st_mtime >= cutoff or
                  p.relative_to(DEST).as_posix().startswith('UI/')]
    deletion_files = {d for d in DELETIONS}
    consumers: dict[str, set[str]] = {}
    for p in candidates:
        rel = p.relative_to(DEST).as_posix()
        if rel in deletion_files or rel in {j for j, _, _ in JOBS}:
            continue
        try:
            img = WzImage.from_bytes(p.read_bytes(), key=key, name=rel)
            img.parse()
        except Exception:
            continue
        refs: set[str] = set()

        def walk(node):
            for c in node.children():
                if isinstance(c, WzCanvasProperty):
                    link = c.get('_outlink')
                    if link is not None:
                        target = str(link.value).split('.img/')[0] + '.img'
                        if target in deletion_files:
                            refs.add(target)
                elif c.children():
                    walk(c)
        walk(img.root)
        if refs:
            consumers[rel] = refs
    return consumers


def update_index(results: dict[str, bytes]) -> None:
    index = json.loads(INDEX.read_text())
    entries = index['files'] if isinstance(index, dict) and 'files' in index else index
    by_path = {e['path']: e for e in entries}
    for rel in results:
        digest = hashlib.sha256((DEST / rel).read_bytes()).hexdigest()
        if rel in by_path:
            by_path[rel]['sha256'] = digest
        else:
            entries.append({'path': rel, 'sha256': digest})
            by_path[rel] = entries[-1]
    removed = []
    for rel in DELETIONS:
        if rel in by_path:
            entries.remove(by_path[rel])
            del by_path[rel]
            removed.append(rel)
    INDEX.write_text(json.dumps(index, indent=1) + '\n')
    print(f'index: refreshed {len(results)}, removed {len(removed)}: {removed}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--write', action='store_true')
    raise SystemExit(consolidate(parser.parse_args().write))
