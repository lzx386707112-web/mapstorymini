#!/usr/bin/env python3
"""守住「五/六转任务链挂在真实可到达的资源上」这条线。

AdvancedQuestChecks（server-check）只校验 Quests/advanced.json 自身的形状：
id 唯一、门槛、前置链、纯对话可交付。它读不到 WZ，所以查不出最致命的一类错——
**任务注册得好好的，但它挂的 NPC / 地图 / 分类在本项目里根本不存在**，
玩家永远接不到任务，五转（1465）与六转（38625）两道门禁就都开不了。

回归点：
  1. 每个任务的 `area` 在 Etc/QuestCategory.img 里存在且不是 'empty'
     （任务窗的分组标题就是 QuestCategory[area]，缺了会是空白标题）；
  2. 每个任务的 NPC 有 Npc/<id>.img 与 String/Npc.img/<id>/name
     （没有 img → 对话弹不出来；没有 name → 整个对话窗没有名字）；
  3. 每个 NPC 至少在时间神殿（Map/Map/Map2/2700*.img）某张图的 life 里
     type='n' 且 hide=0 —— 否则只是「数据里有」，玩家点不到；
  4. 链头 1460/38601 的等级门槛与 V/HEXA 门禁一致（200 / 260）；
  5. 入口链路完整：時間神殿整片区域在本项目里没有任何「主世界直达」的普通门，
     全靠脚本门串联（裂缝 272000000 的 timeCrack → 272030000 → 270000000 →
     270010000 → 270010100 的 timeQuest，且 3501/31165 两道原端任务门）。
     这条链缺任何一环，旁观者就永远见不到——所以把它一起钉住：
       a) 270010100 有 script='timeQuest' 的 in00 门、272000000 有 script='timeCrack' 的 crack00 门；
       b) npc_scripts.json 里 portal/timeQuest 与 portal/timeCrack 都能解析到；
       c) timeQuest 引用的 3501..3505 与 3005427.js 门禁的 31165 在 Quest/Check.img 里存在；
       d) 从裂缝落点 272030000 出发，纯 portal 图能走到每个 NPC 所在图。

用法：python3 tools/check_advanced_quests.py
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REPO = ROOT.parent
DATA = REPO / 'MapleDashResources/Data'
QUESTS = ROOT / 'Assets/Resources/Quests/advanced.json'
CATEGORY = DATA / 'Etc/QuestCategory.img'
MAP_DIR = DATA / 'Map/Map/Map2'

sys.path.insert(0, '/Users/lizixian/Documents/mxd/BeiDou-Server/tool/wz-python')
from wzpy import WzImage, WzKey  # noqa: E402

KEY = WzKey.for_region('GMS')


def load(path: Path) -> WzImage:
    img = WzImage.from_bytes(path.read_bytes(), key=KEY, name=path.name)
    img.parse()
    return img


def value(node):
    return getattr(node, 'value', None)


def main() -> int:
    records = json.loads(QUESTS.read_text(encoding='utf-8'))['quests']

    # 1. 分类：任务窗分组标题的唯一来源
    category = load(CATEGORY)
    areas = {}
    for child in category.root.children():
        areas[int(child.name)] = value(child)
    bad = sorted({r['area'] for r in records if areas.get(r['area'], 'empty') == 'empty'})
    assert not bad, f'QuestCategory.img 没有可用分类: {bad}'
    used = sorted({r['area'] for r in records})
    print('Q category: area %s -> %s PASS'
          % (used, ', '.join(areas[a] for a in used)))

    # 2. NPC 资源与名字
    npcs = sorted({r['npc'] for r in records})
    names = load(DATA / 'String/Npc.img')
    label = {}
    for npc in npcs:
        img = DATA / f'Npc/{npc}.img'
        assert img.exists(), f'缺 NPC 资源 Npc/{npc}.img'
        node = names.get(str(npc))
        assert node is not None, f'String/Npc.img 没有 {npc}'
        name = node.Text('name') if hasattr(node, 'Text') else value(node.get('name'))
        assert name and name.strip(), f'String/Npc.img/{npc} 没有 name'
        label[npc] = name
    print('Q npc: %s PASS' % ', '.join(f'{npc} {label[npc]}' for npc in npcs))

    # 3. NPC 真的站在图里（life/type='n'/hide=0）
    placed = {npc: [] for npc in npcs}
    for path in sorted(MAP_DIR.glob('2700*.img')):
        life = load(path).get('life')
        if life is None:
            continue
        for entry in life.children():
            if value(entry.get('type')) != 'n':
                continue
            try:
                npc = int(value(entry.get('id')))
            except (TypeError, ValueError):
                continue
            if npc in placed and value(entry.get('hide')) in (None, 0):
                placed[npc].append(int(path.stem))
    missing = [npc for npc, maps in placed.items() if not maps]
    assert not missing, f'这些 NPC 不在时间神殿任何地图的 life 里: {missing}'
    for npc in npcs:
        print('Q placement: %s 站在 %s PASS' % (npc, ', '.join(map(str, sorted(set(placed[npc]))))))

    # 4. 门禁一致：V 矩阵要 1465、HEXA 要 38625，链头等级必须对上
    by_id = {r['id']: r for r in records}
    assert 1465 in by_id and 38625 in by_id, '门禁任务 1465 / 38625 不在表内'
    assert by_id[1460]['level'] == 200, by_id[1460]['level']
    assert by_id[38601]['level'] == 260, by_id[38601]['level']
    print('Q gates: 1460 Lv200 -> 1465 -> 38601 Lv260 -> 38625 PASS')

    # 4b. 对白三段 must 与任务状态对齐：没有 Quest/Say.img 时，
    # NpcSession.BeginDataQuest 接取取 info[0]、交付取 info[len-1]，
    # QuestWindow 读 stateInfo[状态]，所以 info 必须至少三段且「进行中」≠「完成」，
    # 否则交付时会念一遍进行中的台词（旧实现固定取 info[1] 就踩过这个坑）。
    for r in records:
        info = r.get('info') or []
        assert len(info) >= 3, f"{r['id']} 的对白少于三段：{len(info)}"
        assert info[1] != info[2], f"{r['id']} 的「进行中」与「完成」对白相同"
        assert info[0] != info[2], f"{r['id']} 的「接取」与「完成」对白相同"
    print('Q dialogue: %d 条任务都是 接取/进行中/完成 三段且互不相同 PASS' % len(records))

    # 5. 入口链路：時間神殿整片区域没有「主世界直达」的普通门，全靠脚本门串联。
    gate = load(DATA / 'Quest/Check.img')
    for quest in (31165, 3500, 3501, 3502, 3503, 3504, 3505):
        assert gate.get(str(quest)) is not None, f'Quest/Check.img 缺门禁任务 {quest}'

    portals = {}
    for path in sorted(MAP_DIR.glob('2700*.img')) + sorted(MAP_DIR.glob('2720*.img')):
        node = load(path).get('portal')
        edges = []
        for entry in (node.children() if node is not None else []):
            edges.append((value(entry.get('pn')) or '', value(entry.get('tm')),
                          value(entry.get('script')) or ''))
        portals[int(path.stem)] = edges
    has = lambda mp, name, script: any(pn == name and sc == script and tm == 999999999
                                       for pn, tm, sc in portals.get(mp, []))
    assert has(272000000, 'crack00', 'timeCrack'), '272000000 缺裂缝脚本门 crack00/timeCrack'
    assert has(270010100, 'in00', 'timeQuest'), '270010100 缺神殿脚本门 in00/timeQuest'
    packs = json.loads((ROOT / 'Assets/Resources/Scripts/npc_scripts.json').read_text(encoding='utf-8'))
    for script in ('timeCrack', 'timeQuest'):
        body = packs.get('portal/' + script) or ''
        assert 'function enter' in body, f'portal/{script} 缺失或没有 enter'
    assert 'isQuestCompleted(3500 + map)' in packs['portal/timeQuest'] \
        and 'warp(mapid + 10' in packs['portal/timeQuest'], \
        'portal/timeQuest 不再按「3500+map 完成 → mapid+10」送人，入口链需要重新核对'

    # 普通门串联：洞穴落点 → 時間神殿大厅 → 記憶之路 → 神殿深处
    def walk(start):
        seen, queue = {start}, [start]
        while queue:
            node = queue.pop(0)
            for _, tm, sc in portals.get(node, []):
                if sc or tm in (None, 999999999) or tm not in portals or tm in seen:
                    continue
                seen.add(tm)
                queue.append(tm)
        return seen
    stem = walk(272030000)
    assert 270010100 in stem, '从裂缝落点 272030000 走普通门到不了 270010100（記憶之路入口）'
    # timeQuest 对 map==1（270010100）送 mapid+10 = 270010110；再走普通门进旁觀者所在的支线房间
    assert any(tm == 270010111 and not sc for _, tm, sc in portals.get(270010110, [])), \
        '270010110 没有普通门通向 NPC 所在图 270010111'
    npc_maps = {}
    for npc in npcs:
        assert 270010111 in placed[npc], f'{npc} 不在入口链终点 270010111 的 life 里'
        npc_maps[npc] = 270010111
    print('Q entry: 31165 裂缝 -> 272000000/timeCrack -> 272030000 -> ... -> 270010100 '
          '-> 3501 timeQuest -> 270010110 -> 270010111 PASS')

    # 6. 头顶灯泡：282 QuestInfo.img 里 autoStart=1 的才有通知图标。
    # 本项目 8 条（38611「繼續進行轉職」旁支未收录），链头 1460 / 38601 必须在其中，
    # 否则 200 级 / 260 级到点时角色头顶不会冒灯泡，玩家根本不知道该去转职。
    auto_start = sorted(r['id'] for r in records if r.get('autoStart') == 1)
    expected = sorted([1460, 1461, 1465, 1478, 1481, 38601, 38624, 38625])
    assert auto_start == expected, f'灯泡候选与 282 QuestInfo.autoStart 不一致: {auto_start} != {expected}'
    assert 1460 in auto_start and 38601 in auto_start, '五转/六转链头必须出灯泡'
    print('Q bulb: autoStart=%s（链头 1460/38601 在内）PASS' % auto_start)

    # 7. 主菜单入口（9900001.js 的「日常功能」）：
    #    41 = 五转技能面板（V 矩阵），42 = 六转技能面板（HEXA 矩阵）。
    #    必须「未达等级/未完成转职时隐藏、GM 恒定可见」，并与服务端豁免口径一致。
    menu = packs.get('npc/9900001') or ''
    assert menu, 'npc/9900001 不在脚本包里'
    assert 'function 构建矩阵入口' in menu, '9900001.js 缺少 构建矩阵入口'
    for token in ('cm.isQuestCompleted(1465)', 'cm.isQuestCompleted(38625)',
                  '等级 >= 200', '等级 >= 260', 'cm.getPlayer().isGM()'):
        assert token in menu, f'9900001.js 入口条件缺少 {token}'
    assert 'cm.openVMatrix()' in menu and 'cm.openHexaMatrix()' in menu, \
        '9900001.js 缺 cm.openVMatrix / cm.openHexaMatrix 调用'
    for case, name in (('41', '五转技能'), ('42', '六转技能')):
        assert f'case {case}' in menu and name in menu, f'9900001.js 缺入口 {case} {name}'
    # 入口行只能挂在「日常功能」分区里（角色成长区已有独立的技能中心，不要混）。
    # 用分区标题切，不能拿裸文案切——文件头注释里也写着「日常功能 / 每日日常」。
    daily = menu.split('分区标题("日常功能")', 1)[1].split('分区标题("每日日常")', 1)[0]
    assert '构建矩阵入口' in daily, '矩阵入口不在「日常功能」分区'
    print('Q menu: 日常功能 41 五转技能 / 42 六转技能（200+1465 / 260+38625，GM 直显）PASS')

    print('OK — 五/六转任务链的 NPC、地图、分类、门禁、灯泡与主菜单入口全部对齐 282')
    return 0


if __name__ == '__main__':
    sys.exit(main())
