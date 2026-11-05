#!/usr/bin/env python3
"""枚举北斗四转 Skill/<job>.img 里混入的 TMS 五/六转技能，产出删除清单。

判定规则（全部可复核，无猜测）：
  A. bound   —— 冒险家 12 系沿用 SkillBook.ExplorerAdvancedSkills.IsAdvanced 的
                四转末项上界（项目既定口径，当初拆"五转"tab 就用它）。
  B. marker  —— desc 含「兼容迁移」标记（当初 TMS 五/六转迁移写入时的痕迹）。
  C. id56    —— id 命中 282 五/六转目录 skills-5th6th.json（1618 条）。
  D. name56  —— 技能名（繁简归一后）命中 282 五/六转技能名集合。
  E. in282   —— id 出现在 282 全部 Skill_*.ms 解包产物（信息项，不单独定删除）。
  F. in282_4 —— id 出现在 282 同职业 Skill_<job>.img（四转段延续，保留依据）。

  删除 = A ∪ B ∪ C ∪ D。未命中任何规则且 F 为假的骑士团 id 落入 review，
  供人工逐条判定（脚本不猜）。

同时输出：
  - 每个 id 在运行时引用面（JobTree/JobSkills/GameplaySkills/GameplaySkillVfx/
    NativeSkillReplays/NativePresentation/NativeAssetIndex/*.cs）的出现次数
    （带数字边界，防止 1121020 匹配到 11210200）。
  - 全部 Skill/*.img 中 id ≥ 30000000 的异常项（捕捉散落在外部 img 的 V 共通技能）。

输出 docs/legacy-advanced-deletion.json。
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]          # MapleDash/
REPO = ROOT.parent                                   # mxd-android/
DATA = Path('/Users/lizixian/Documents/mxd/BeiDou-Server/clien/Data')
PACKS = Path('/tmp/packs')
sys.path.insert(0, '/Users/lizixian/Documents/mxd/BeiDou-Server/tool/wz-python')

from wzpy import WzImage, WzKey  # noqa: E402

from zhconv import convert  # noqa: E402

GMS = WzKey.for_region('GMS')
BMS = WzKey.for_region('BMS')

JOBS = [112, 122, 132, 212, 222, 232, 312, 322, 412, 422, 512, 522,
        1112, 1212, 1312, 1412, 1512]
# SkillBook.ExplorerAdvancedSkills.IsAdvanced 的四转末项（冒险家 12 系）。
BOUNDS = {112: 1121011, 122: 1221012, 132: 1321010, 212: 2121008,
          222: 2221008, 232: 2321009, 312: 3121009, 322: 3221008,
          412: 4121009, 422: 4221008, 512: 5121010, 522: 5221010}

# 运行时引用面（相对 ROOT/REPO；文本文件，按 id 词边界统计出现次数）
REFERENCE_FILES = [
    ROOT / 'Assets/Resources/UI/JobTree.json',
    ROOT / 'Assets/Resources/UI/JobSkills.json',
    ROOT / 'Assets/Resources/GameplaySkills.json',
    ROOT / 'Assets/Resources/GameplaySkillVfx.json',
    ROOT / 'Assets/Resources/NativeSkillReplays.json',
    ROOT / 'Assets/Resources/NativeSkillVideos.json',
    ROOT / 'Assets/Resources/NativePresentation.json',
    ROOT / 'Assets/Resources/NativeAssetIndex.json',
]
CS_DIR = ROOT / 'Assets/Scripts'


# 名字带「VI」后缀 = TMS 六转技能（282 侧同义：12121054 鳳凰爆裂、14141503 冥河指令等）。
VI_PATTERN = re.compile(r'\sVI(?:[:：]|$)')
# 人工终审的 3 个 V 链组件（2026-11-04 复核，依据：父技能 name56 已判删 + desc 同链）：
#   11121009 日月分裂   —— 靈魂蝕日(400011088)组件，父 11121008 已 name56 判删
#   11121011 冥河破     —— desc「黄泉十字」= TMS 黃泉十字斬链（11121052+ 同链）
#   11121012 宇宙之花   —— desc「完整銀河」= 銀河星爆(11141500)同链
OVERRIDES = {
    11121009: 'v-chain:靈魂蝕日(400011088)组件，父11121008已判删',
    11121011: 'v-chain:黃泉十字斬(desc黄泉十字)',
    11121012: 'v-chain:銀河星爆(11141500)同链(desc完整銀河)',
}


def wz(path: Path, key, name: str) -> WzImage:
    return WzImage.from_bytes(path.read_bytes(), key=key, name=name)


def collect_job_skills(job: int) -> list[dict]:
    img = wz(DATA / f'Skill/{job}.img', GMS, f'{job}.img')
    root = img.get('skill')
    out = []
    for node in root.children():
        if not node.name.isdigit():
            continue
        sid = int(node.name)
        name_node = None
        for key in (str(sid), f'{sid:07d}'):
            n = strings.get(key)
            if n is not None and n.get('name') is not None:
                name_node = n
                break
        name = name_node.get('name').value if name_node is not None else None
        desc = name_node.get('desc').value if name_node is not None and name_node.get('desc') is not None else None
        out.append({'id': sid, 'name': name or '', 'desc': desc or ''})
    return out


strings = wz(DATA / 'String/Skill.img', GMS, 'Skill.img')


def main():
    # —— 282 五/六转目录 ——
    cat = json.loads((REPO / 'docs/tms282-skill/data/skills-5th6th.json').read_text())
    ids_56 = set(cat['skills'].keys())
    names_56 = {convert(v.get('name') or '', 'zh-cn') for v in cat['skills'].values() if v.get('name')}

    # —— 282 全量 id 集合 + 同职业四转段集合 ——
    all_282: set[int] = set()
    job_282: dict[int, set[int]] = {}
    for pack in sorted(PACKS.glob('Skill_*.img')):
        img = wz(pack, BMS, pack.name)
        root = img.get('skill')
        if root is None:
            continue
        ids = {int(n.name) for n in root.children() if n.name.isdigit()}
        all_282 |= ids
        m = re.fullmatch(r'Skill_(\d+)\.img', pack.name)
        if m and int(m.group(1)) in JOBS:
            job_282[int(m.group(1))] = ids

    # —— 逐职业分类 ——
    entries = []
    for job in JOBS:
        for s in collect_job_skills(job):
            sid, name, desc = s['id'], s['name'], s['desc']
            rules = []
            if job in BOUNDS and sid > BOUNDS[job]:
                rules.append('bound')
            if '兼容迁移' in desc or '五/六转' in desc:
                rules.append('marker')
            if str(sid) in ids_56:
                rules.append('id56')
            # 括号注记（烈炎爆发（火狮））与空白不参与名字比对
            clean_name = re.sub(r'[（(].*?[)）]', '', name or '').strip()
            if clean_name and convert(clean_name, 'zh-cn') in names_56:
                rules.append('name56')
            if VI_PATTERN.search(name or ''):
                rules.append('vi-suffix')
            if sid in OVERRIDES:
                rules.append('override:' + OVERRIDES[sid])
            in282 = sid in all_282
            in282_4 = sid in job_282.get(job, set())
            # 282 骑士团段是重制版换代 id（同号不同技能），in282_4 不构成保留证据，
            # 只作信息记录（2026-11-04 复核：282 12121001=極致熾烈 vs 北斗=烈炎爆发）。
            verdict = 'delete' if rules else ('keep' if job in BOUNDS else 'review')
            entries.append({'job': job, 'id': sid, 'name': name, 'desc': desc[:60],
                            'rules': rules, 'in282': in282, 'in282SameJob': in282_4,
                            'verdict': verdict})

    # —— review 项收编：名字前缀命中同职业已判删的父技能 ——
    # 这些全是 V/VI 组件（「暗影投掷：上段飞镖」等），父技能都被 marker/name56 判删。
    by_job_delete: dict[int, list[dict]] = {}
    for e in entries:
        if e['verdict'] == 'delete':
            by_job_delete.setdefault(e['job'], []).append(e)
    for e in entries:
        if e['verdict'] != 'review':
            continue
        base = (e['name'] or '').split('：')[0].strip()
        base = re.sub(r'[（(].*?[)）]', '', base).strip()
        parent = None
        for p in by_job_delete.get(e['job'], []):
            if base and p['name'] and p['name'].split('：')[0].strip() == base:
                parent = p
                break
        if parent is not None:
            e['rules'].append('parent-deleted')
            e['parent'] = parent['id']
            e['verdict'] = 'delete'
    # 收编后仍处 review 的（名字前缀找不到父技能的）保持 review，人工定夺。

    delete = [e for e in entries if e['verdict'] == 'delete']
    review = [e for e in entries if e['verdict'] == 'review']

    # 一致性检查：冒险家按 bound 保留的 id 不应命中 marker/id56/name56
    for e in entries:
        if e['verdict'] == 'keep' and (set(e['rules']) - {'bound'}):
            e['warn'] = 'kept-but-rule-hit'

    # —— 引用面统计 ——
    texts: list[tuple[str, str]] = []
    for f in REFERENCE_FILES:
        if f.exists():
            texts.append((str(f.relative_to(REPO)), f.read_text(encoding='utf-8', errors='ignore')))
    for cs in sorted(CS_DIR.rglob('*.cs')):
        texts.append((str(cs.relative_to(REPO)), cs.read_text(encoding='utf-8', errors='ignore')))
    interesting = delete + review
    id_pat = {e['id']: re.compile(rf'(?<!\d){e["id"]}(?!\d)') for e in interesting}
    for e in interesting:
        e['refs'] = {path: n for path, text in texts
                     if (n := len(id_pat[e['id']].findall(text)))}

    # —— 全库 ≥30000000 异常 id 扫描 ——
    stray = {}
    for img_path in sorted((DATA / 'Skill').glob('*.img')):
        img = wz(img_path, GMS, img_path.name)
        root = img.get('skill')
        if root is None:
            continue
        hi = sorted(int(n.name) for n in root.children()
                    if n.name.isdigit() and int(n.name) >= 30000000)
        if hi:
            stray[img_path.name] = hi

    report = {
        'bounds': {str(k): v for k, v in BOUNDS.items()},
        'summary': {
            'total': len(entries),
            'delete': len(delete),
            'review': len(review),
            'keptWithRuleHit': sum(1 for e in entries if e.get('warn')),
            'strayHighIds': {k: len(v) for k, v in stray.items()},
        },
        'delete': delete,
        'review': review,
        'entries': entries,
        'strayHighIds': stray,
        'note': 'delete = bound ∪ marker ∪ id56 ∪ name56；review 仅骑士团且 282 同职业四转段无此 id，须人工判定。',
    }
    out = REPO / 'docs/legacy-advanced-deletion.json'
    out.write_text(json.dumps(report, ensure_ascii=False, indent=1) + '\n')
    print(f"total={len(entries)} delete={len(delete)} review={len(review)} "
          f"keptWithRuleHit={report['summary']['keptWithRuleHit']} strayJobs={len(stray)}")
    print('-> ' + str(out))
    for e in review:
        print(f"REVIEW {e['job']} {e['id']} {e['name']} | {e['desc'][:40]} | in282={e['in282']}")
    for e in entries:
        if e.get('warn'):
            print(f"WARN keep {e['job']} {e['id']} {e['name']} rules={e['rules']}")


if __name__ == '__main__':
    main()
