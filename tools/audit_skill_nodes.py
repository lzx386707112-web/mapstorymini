#!/usr/bin/env python3
"""Independent wzpy inventory of every original skill, including level-local art."""
import hashlib
import json
from collections import defaultdict
from pathlib import Path
import wzmap
from wzpy.properties import WzCanvasProperty, WzUolProperty
from wzpy.wz_package import _follow_uol

ROOT = Path(__file__).resolve().parents[1]


def resolved(node):
    return _follow_uol(node) if isinstance(node, WzUolProperty) else node


def clips(node, path, ancestors=()):
    node = resolved(node)
    if node is None or id(node) in ancestors:
        return {}
    if isinstance(node, WzCanvasProperty):
        return {path: 1}
    children = list(node.children())
    frames = [c for c in children if c.name.isdigit() and isinstance(resolved(c), WzCanvasProperty)]
    result = {path: len(frames)} if frames else {}
    for child in children:
        if not isinstance(resolved(child), WzCanvasProperty):
            result.update(clips(child, path + '/' + child.name, ancestors + (id(node),)))
    return result


def branches(node):
    return {c.name: clips(c, c.name) for c in node.children()
            if c.name not in ('level', 'action') and not c.name.startswith('icon')}


def main():
    cases, sources = [], {}
    for file in sorted(Path(wzmap.DATA, 'Skill').glob('*.img')):
        source = 'Skill/' + file.stem
        root = wzmap._wz(source).get('skill')
        if root is None:
            continue
        sources[source + '.img'] = hashlib.sha256(file.read_bytes()).hexdigest()
        for skill in root.children():
            base = branches(skill)
            levels = skill.get('level')
            level_list = list(levels.children()) if levels else []
            if not level_list:
                level_list = [None]
            for level in level_list:
                override = branches(level) if level is not None else {}
                # Root-only levels share art. Check the ends and every level
                # that declares visual branches; never duplicate level-local art.
                if level not in (level_list[0], level_list[-1]) and not any(override.values()):
                    continue
                selected = dict(base)
                selected.update(override)
                flat = {}
                for part in selected.values():
                    flat.update(part)
                cases.append({'id': int(skill.name), 'level': int(level.name) if level else 1, 'clips': flat})
    out = {'sources': sources, 'skillCount': len({c['id'] for c in cases}), 'cases': cases}
    by_branch = defaultdict(set)
    for case in cases:
        for path in case['clips']:
            by_branch[path.split('/')[0]].add(case['id'])
    out['visualBranches'] = {k: sorted(v) for k, v in sorted(by_branch.items())}
    (ROOT / 'tools/native_skill_nodes.json').write_text(json.dumps(out, ensure_ascii=False, indent=2) + '\n')
    normal = {'effect', 'effect0', 'prepare', 'affected', 'hit', 'ball', 'special', 'tile',
              'mob', 'screen', 'screenEffect', 'effectScreen', 'background'}
    report = ['# 原技能节点全量检查', '',
              f'源 Skill IMG 中共 {out["skillCount"]} 个技能，选取 {len(cases)} 个等级样本，'
              f'{sum(len(c["clips"]) for c in cases)} 段动画。', '',
              '复现：`rtk proxy python3 tools/audit_skill_nodes.py`，再执行 Unity `NativeSkillNodeChecks.Run`。', '',
              '源文件 SHA256、全部技能/等级/分支/预期帧数及分支对应技能清单见 '
              '[native_skill_nodes.json](../tools/native_skill_nodes.json)。抽查等级覆盖首尾以及每个含自身视觉节点的等级；'
              '共享根节点的中间等级不重复生成样本。', '',
              '公共解析按技能 ID + 等级缓存，选择 level/N 下同名分支，否则使用技能根分支；'
              '递归解析 UOL 和 Canvas，不将 icon、action 或全部 level 当作动画。'
              '既有通用管线接入施法、弹道与命中；已核实特殊技能沿用原有调度。', '',
              '**节点可解析不代表特殊技能状态机已迁移。** 下表“专用状态待核实”只表示该分支已纳入资源解析，'
              '没有把 keydown/end、召唤、武器残影或条件特效全部当作施法时同时播放。'
              '通用管线的旧弹速/缺省射程/出弹延迟仍属于已记载的移植参数，不是新增 EXE 结论。', '',
              '| 视觉根分支 | 技能数 | 调度边界 |', '|---|---:|---|']
    for branch, ids in sorted(by_branch.items()):
        report.append(f'| `{branch}` | {len(ids)} | '
                      + ('既有通用/专项管线；未逐技能实机验收' if branch in normal else '已解析；专用状态待核实') + ' |')
    (ROOT / 'docs/skill-node-audit.md').write_text('\n'.join(report) + '\n')
    print('Skills:', out['skillCount'], 'level cases:', len(cases),
          'clip references:', sum(len(c['clips']) for c in cases))
    print('Three Snails:', [c for c in cases if c['id'] == 1000])


if __name__ == '__main__':
    main()
