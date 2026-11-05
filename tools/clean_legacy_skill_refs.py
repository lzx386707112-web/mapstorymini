#!/usr/bin/env python3
"""清掉旧 TMS 五/六转技能在所有运行时数据文件里的引用。

输入：docs/legacy-advanced-deletion.json（audit_legacy_advanced.py 的产物）
技能像素已由 prune_legacy_skills.py 从 Skill/<job>.img 移除；本工具处理
引用面，避免运行时查到已不存在的 id / 节点：

  UI/JobSkills.json        技能书 tab 列表 + skills 条目（旧 4 转页签里的五六转）
  GameplaySkills.json      演示技能定义（rupture/illusion/berserk/storm/libra…）
  GameplaySkillVfx.json    同 key 的表现层
  NativePresentation.json  skills 展示条目
  NativeSkillReplays.json  整份都是旧五六转重放数据
  NativeSkillVideos.json   旧五六转 MCV 影片 + 磁盘文件
  NativeBuffContract.json  statups 键 / summons 列表
  NativeAssetIndex.json    aliases（skillicon/*、NativeSkillVfx/*）+ skills 映射 +
                           files 清单（被改写的 Skill img 重算 sha256、删除的 mcv 移除）

不碰 Bot/Items.json —— 那里是道具 id，与技能 id 有同号不同物（5121014 是现金道具）。

用法：
    python3 tools/clean_legacy_skill_refs.py --check
    python3 tools/clean_legacy_skill_refs.py
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]           # MapleDash/
REPO = ROOT.parent
RES = ROOT / 'Assets/Resources'
DELETION = REPO / 'docs/legacy-advanced-deletion.json'
MCV_DIR = REPO / 'MapleDashResources/Data/Video'

STYLE = {  # 保持原文件排版风格
    'GameplaySkills.json': ('indent', 2),
    'GameplaySkillVfx.json': ('indent', 2),
    'NativeSkillVideos.json': ('indent', 2),
    'NativePresentation.json': ('compact', 0),
    'NativeSkillReplays.json': ('compact', 0),
    'UI/JobSkills.json': ('indent', 1),
    'NativeAssetIndex.json': ('indent', 1),
    'NativeBuffContract.json': ('indent', 2),
}


def load_ids() -> set[int]:
    data = json.loads(DELETION.read_text())
    if data['review']:
        raise SystemExit('删除清单还有 review 项，先定论')
    return {e['id'] for e in data['delete']}


def dump(path: Path, data, style: tuple[str, int]) -> str:
    kind, n = style
    text = (json.dumps(data, ensure_ascii=False, separators=(',', ':')) if kind == 'compact'
            else json.dumps(data, ensure_ascii=False, indent=n))
    return text + '\n'


def hits(value, ids: set[int]) -> bool:
    """键/路径里是否出现被删技能 id（数字边界）。"""
    return any(f'/{i}' in str(value) or str(value).endswith(str(i)) or
               re.search(rf'(?<!\d){i}(?!\d)', str(value)) for i in ids)


def clean_asset_index(index: dict, ids: set[int], changed_imgs: dict[str, str],
                      removed_videos: list[str]) -> dict:
    before = {k: len(v) for k, v in index.items() if isinstance(v, (list, dict))}

    index['aliases'] = {k: v for k, v in index['aliases'].items()
                        if not (hits(k, ids) or hits(v.get('source', ''), ids))}
    index['skills'] = {k: v for k, v in index['skills'].items() if v not in ids}

    drop_files = {v.split('/', 1)[-1] for v in removed_videos}      # Video/xxx.mcv
    kept = []
    for entry in index['files']:
        p = entry['path']
        if p.startswith('Video/') and p.split('/', 1)[1] in drop_files:
            continue
        if p in changed_imgs:
            entry = {'path': p, 'sha256': changed_imgs[p]}
        kept.append(entry)
    index['files'] = kept

    print('  aliases %d -> %d' % (before['aliases'], len(index['aliases'])))
    print('  skills  %d -> %d' % (before['skills'], len(index['skills'])))
    print('  files   %d -> %d' % (before['files'], len(index['files'])))
    return index


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--check', action='store_true')
    args = ap.parse_args()
    ids = load_ids()
    pending: dict[Path, str] = {}
    removed_videos: list[str] = []

    # 1) UI/JobSkills.json —— 技能书页签 + 技能条目
    p = RES / 'UI/JobSkills.json'
    data = json.loads(p.read_text())
    n0 = sum(len(t['skills']) for j in data['jobs'] for t in j['tabs'])
    for line in data['jobs']:
        for tab in line['tabs']:
            tab['skills'] = [x for x in tab['skills'] if x not in ids]
    n1 = sum(len(t['skills']) for j in data['jobs'] for t in j['tabs'])
    s0 = len(data['skills'])
    data['skills'] = {k: v for k, v in data['skills'].items() if int(k) not in ids}
    print(f'JobSkills: tab 技能 {n0} -> {n1}，skills {s0} -> {len(data["skills"])}')
    pending[p] = dump(p, data, STYLE['UI/JobSkills.json'])

    # 2) GameplaySkills / GameplaySkillVfx / NativePresentation —— 按 id / key 删
    p = RES / 'GameplaySkills.json'
    data = json.loads(p.read_text())
    dead = [s for s in data['skills'] if s.get('id') in ids]
    keys = {s['key'] for s in dead}
    data['skills'] = [s for s in data['skills'] if s.get('id') not in ids]
    print(f'GameplaySkills: -{len(dead)} -> {len(data["skills"])} (keys {sorted(keys)})')
    pending[p] = dump(p, data, STYLE['GameplaySkills.json'])

    p = RES / 'GameplaySkillVfx.json'
    data = json.loads(p.read_text())
    data['skills'] = [s for s in data['skills']
                      if s.get('id') not in ids and s.get('key') not in keys]
    print(f'GameplaySkillVfx: -> {len(data["skills"])}')
    pending[p] = dump(p, data, STYLE['GameplaySkillVfx.json'])

    p = RES / 'NativePresentation.json'
    data = json.loads(p.read_text())
    if 'skills' in data:
        before = len(data['skills'])
        data['skills'] = {k: v for k, v in data['skills'].items()
                          if int(v.get('id', 0)) not in ids}
        print(f'NativePresentation.skills: {before} -> {len(data["skills"])}')
    pending[p] = dump(p, data, STYLE['NativePresentation.json'])

    # 3) 重放 / 影片 —— 键全是被删技能
    p = RES / 'NativeSkillReplays.json'
    data = json.loads(p.read_text())
    data = {k: v for k, v in data.items() if int(k) not in ids}
    print(f'NativeSkillReplays: -> {len(data)}')
    pending[p] = dump(p, data, STYLE['NativeSkillReplays.json'])

    p = RES / 'NativeSkillVideos.json'
    data = json.loads(p.read_text())
    dead_videos = {k: v for k, v in data.items() if int(k) in ids}
    data = {k: v for k, v in data.items() if int(k) not in ids}
    removed_videos = sorted(set(dead_videos.values()))
    print(f'NativeSkillVideos: -{len(dead_videos)} -> {len(data)}，影片 {len(removed_videos)} 个')
    pending[p] = dump(p, data, STYLE['NativeSkillVideos.json'])

    # 4) NativeBuffContract —— statups 键 / summons 列表
    p = RES / 'NativeBuffContract.json'
    data = json.loads(p.read_text())
    before = len(data.get('statups', {}))
    data['statups'] = {k: v for k, v in data.get('statups', {}).items() if int(k) not in ids}
    data['summons'] = [x for x in data.get('summons', [])
                       if not (isinstance(x, int) and x in ids)
                       and not (isinstance(x, dict) and x.get('skill') in ids)]
    print(f'NativeBuffContract: statups {before} -> {len(data["statups"])}, '
          f'summons -> {len(data["summons"])}')
    pending[p] = dump(p, data, STYLE['NativeBuffContract.json'])

    # 5) NativeAssetIndex —— aliases/skills/files（Skill img 已由 prune 改写）
    changed = {}
    for job in sorted({e['job'] for e in json.loads(DELETION.read_text())['delete']}):
        rel = f'Skill/{job}.img'
        f = REPO / 'MapleDashResources/Data' / rel
        changed[rel] = hashlib.sha256(f.read_bytes()).hexdigest()
    p = RES / 'NativeAssetIndex.json'
    data = clean_asset_index(json.loads(p.read_text()), ids, changed, removed_videos)
    pending[p] = dump(p, data, STYLE['NativeAssetIndex.json'])

    if args.check:
        print('check mode: 未写入')
        return
    for path, text in pending.items():
        path.write_text(text, encoding='utf-8')
    print('写入', len(pending), '个文件')

    # 6) 删除旧五六转 MCV（只删 entries 独占引用、且确实存在的文件）
    removed = 0
    for rel in removed_videos:
        f = MCV_DIR / Path(rel).name
        if f.exists():
            f.unlink()
            removed += 1
    print(f'删除 MCV {removed} 个（{MCV_DIR}）')


if __name__ == '__main__':
    main()
