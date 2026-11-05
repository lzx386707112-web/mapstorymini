#!/usr/bin/env python3
"""守住「旧 TMS 五/六转技能已从四转 Skill img 与运行时数据里清除」这条线。

回归点：
  1. MapleDashResources/Data/Skill/<17 个四转职业>.img 不再含删除清单里的 id；
  2. 运行时数据文件里不再出现被删 id（按数字边界；sha256 / 道具 id 等误命中除外）；
  3. NativeAssetIndex 的 files 里 Skill img 的 sha256 与磁盘一致（改写后必须同步）；
  4. 删除清单里的 MCV 影片不再出现在 Video/ 目录。

清单唯一来源：docs/legacy-advanced-deletion.json（audit_legacy_advanced.py 产出）。

豁免：282 闭包（docs/tms282-skill/data/skill-closure.json）里出现的同号 id。
旧删除清单是按 **TMS 265 语义**判定的「旧迁移件」，而 282 在同样的 id 上换了技能
（如 1121015 烈焰翔斩、12121001 极致炽烈），V/HEXA 闭包导入后这些节点是**合法重迁件**。
所以：`deleted - closure` 才是真正禁止回潮的集合，`deleted ∩ closure` 单列为重迁放行。

用法：python3 tools/check_legacy_skill_removal.py
"""
from __future__ import annotations

import hashlib
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REPO = ROOT.parent
RES = ROOT / 'Assets/Resources'
DATA = REPO / 'MapleDashResources/Data'
DELETION = REPO / 'docs/legacy-advanced-deletion.json'
CLOSURE = REPO / 'docs/tms282-skill/data/skill-closure.json'

sys.path.insert(0, '/Users/lizixian/Documents/mxd/BeiDou-Server/tool/wz-python')
from wzpy import WzImage, WzKey  # noqa: E402


def closure_ids() -> set[int]:
    """282 闭包（V/HEXA 核心连接技能 + 引用链）里的全部技能 id。"""
    document = json.loads(CLOSURE.read_text(encoding='utf-8'))
    return {int(sid) for job in document['jobs'].values() for sid in job['closure']}

# 数据面必须干净的文件；Bot/Items.json 故意不在列表内（道具 id 与技能 id 有同号）。
DATA_FILES = [
    'UI/JobTree.json',
    'UI/JobSkills.json',
    'GameplaySkills.json',
    'GameplaySkillVfx.json',
    'NativePresentation.json',
    'NativeSkillReplays.json',
    'NativeSkillVideos.json',
    'NativeBuffContract.json',
    'NativeAssetIndex.json',
]


def main() -> int:
    data = json.loads(DELETION.read_text())
    deleted = data['delete']
    reissued = closure_ids()
    all_ids = sorted({e['id'] for e in deleted})
    ids = [i for i in all_ids if i not in reissued]     # 真正禁止回潮的集合
    readded = sorted(set(all_ids) & reissued)            # 282 重迁件（放行）
    jobs = sorted({e['job'] for e in deleted})
    pat = re.compile(r'(?<!\d)(' + '|'.join(map(str, ids)) + r')(?!\d)')
    problems: list[str] = []

    # 1) img 节点
    for job in jobs:
        path = DATA / f'Skill/{job}.img'
        img = WzImage.from_bytes(path.read_bytes(), key=WzKey.for_region('GMS'), name=path.name)
        have = {int(n.name) for n in img.get('skill').children() if n.name.isdigit()}
        left = sorted(have & {e['id'] for e in deleted if e['job'] == job and e['id'] not in reissued})
        if left:
            problems.append(f'Skill/{job}.img 仍含 {left[:6]}')

    # 2) 数据文件（sha256 行会被误命中，先剔除十六进制串）
    hex64 = re.compile(r'\b[0-9a-f]{64}\b')
    prereq = 0
    for rel in DATA_FILES:
        text = (RES / rel).read_text(encoding='utf-8', errors='ignore')
        if rel == 'UI/JobTree.json':
            # 技能表键与职业的 skills/allSkills 是运行时真正的引用面，必须干净。
            # `req` 是「六转要求五转满级」的**前置声明**，指向的五转技能要按 282
            # 重新迁移回来（见 docs/tms282-skill/17-…），所以刻意不在这里拦截。
            tree = json.loads(text)
            scope = {'skills': {k: v.get('name') for k, v in tree['skills'].items()},
                     'jobs': [{'id': j['id'], 'skills': j.get('skills'),
                               'allSkills': j.get('allSkills')} for j in tree['jobs']]}
            hits = [m.group(0) for m in pat.finditer(json.dumps(scope, ensure_ascii=False))]
            prereq = sum(1 for s in tree['skills'].values()
                         for pair in (s.get('req') or []) if str(pair[0]) in set(map(str, ids)))
        elif rel == 'NativeAssetIndex.json':
            # 索引的 files[].path 里混着 Npc/2121012.img 这类同号路径，只查技能相关的两张表。
            index = json.loads(text)
            scope = json.dumps({'aliases': index['aliases'], 'skills': index['skills']},
                               ensure_ascii=False)
            hits = [m.group(0) for m in pat.finditer(scope)]
        else:
            hits = [m.group(0) for m in pat.finditer(hex64.sub('', text))]
        if hits:
            problems.append(f'{rel} 仍引用 {sorted(set(hits))[:6]}')

    # 3) 索引 sha256
    index = json.loads((RES / 'NativeAssetIndex.json').read_text())
    sha = {e['path']: e['sha256'] for e in index['files']}
    for job in jobs:
        rel = f'Skill/{job}.img'
        if rel not in sha:
            problems.append(f'索引缺 {rel}')
            continue
        actual = hashlib.sha256((DATA / rel).read_bytes()).hexdigest()
        if sha[rel] != actual:
            problems.append(f'{rel} sha256 与索引不一致')

    # 4) 影片
    videos = json.loads((RES / 'NativeSkillVideos.json').read_text())
    if videos:
        problems.append(f'NativeSkillVideos 仍有 {len(videos)} 条')
    for f in (DATA / 'Video').glob('explorer-*.mcv'):
        problems.append(f'遗留旧技能影片 {f.name}')

    if problems:
        print('FAIL')
        for p in problems:
            print('  ' + p)
        return 1
    print(f'PASS jobs={len(jobs)} deleted_ids={len(all_ids)} 282重迁={len(readded)} '
          f'禁止回潮={len(ids)} files={len(DATA_FILES)} skill_imgs={len(jobs)} 待重迁五转前置={prereq}')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
