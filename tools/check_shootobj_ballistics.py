"""守 shootobj（282 Actor 化发射物）的数据与速度口径。

v 的单位是「像素 / 逻辑帧（60fps）」，速度 = v * 60 px/s。判据见
docs/tms282-skill/23-shootobj-ballistics.md：飞行时长必须与 layerList 的
b* 飞行层同量级；若按「像素/100ms」解释会放大 6 倍以上，直接 FAIL。

同时守 C# 读取点：SkillVisuals 必须走 NativeShootObj 分支，
NativeShootObj 必须按 v*60 换算 —— 改回瞬发或改错单位都会 FAIL。

用法：
    cd MapleDash
    rtk proxy python3 tools/check_shootobj_ballistics.py [--verbose]
"""
import argparse
import re
import sys
from pathlib import Path

sys.path.insert(0, '/Users/lizixian/Documents/mxd/BeiDou-Server/tool/wz-python')
from wzpy import WzImage, WzKey                      # noqa: E402
from wzpy.properties import (WzCanvasProperty,       # noqa: E402
                             WzUolProperty)

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT.parent / 'MapleDashResources/Data/Skill'
FPS = 60.0
# 飞行时长 / b 层动画时长 的可接受区间。b 层是循环播的：动画播完即命中的约等于 1，
# 长飞弹（1500px 的慢速弹幕）会绕十几圈，所以上限放到 15；而把 v 当成「像素/100ms」
# 会让比值放大到 60 以上，仍然拦得住。
RATIO_MIN, RATIO_MAX = 0.05, 15.0

_cache = {}


def root(name):
    if name not in _cache:
        path = DATA / name
        if not path.is_file():
            _cache[name] = None
        else:
            img = WzImage.from_bytes(path.read_bytes(), key=WzKey.for_region('GMS'))
            img.parse()
            _cache[name] = img.root
    return _cache[name]


def kids(node):
    if node is None:
        return []
    try:
        return list(node.children())
    except AttributeError:
        return []


def res(node):
    for _ in range(32):
        if node is None:
            return None
        if isinstance(node, WzUolProperty):
            node = node.parent.get(node.value) if node.parent else None
        else:
            return node
    return None


def num(node, name, fallback=None):
    node = res(node)
    if node is None:
        return fallback
    child = res(node.get(name))
    if child is None:
        return fallback
    value = getattr(child, 'value', fallback)
    # 原端有些数值存成字符串（startDelay / maxMobCount），统一成 float。
    try:
        return float(value)
    except (TypeError, ValueError):
        return fallback


def vec(node):
    node = res(node)
    if node is None:
        return None
    if hasattr(node, 'value') and isinstance(node.value, tuple):
        return node.value
    x, y = num(node, 'x'), num(node, 'y')
    if x is None and y is None:
        return None
    return (x or 0, y or 0)


def layer_ms(layer):
    """层动画总时长：数字帧的 delay 之和（缺省 60ms）。"""
    node = res(layer)
    if node is None:
        return 0, 0
    frames = [k for k in kids(node) if k.name.isdigit()]
    total = 0
    for frame in frames:
        total += num(frame, 'delay', 60) or 60
    return total, len(frames)


def scan():
    skills = []
    for img in sorted(DATA.glob('*.img')):
        tree = root(img.name)
        node = res(tree.get('skill')) if tree is not None else None
        for entry in kids(node):
            shoot = res(entry.get('shootobj'))
            if shoot is None:
                continue
            skills.append((int(entry.name), shoot))
    return skills


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--verbose', action='store_true')
    args = parser.parse_args()

    problems = []
    rows = []
    for skill_id, shoot in scan():
        info = res(shoot.get('info'))
        start = vec(info.get('start')) if info is not None else None
        body = vec(info.get('bodyWH')) if info is not None else None
        start_delay = num(info, 'startDelay', 0) or 0
        info_speed = num(info, 'v', 0) or 0
        max_mob = num(info, 'maxMobCount', 1) or 1
        # 命中预算族：这三个字段决定「一支弹幕能打几下」。
        limit_each = num(info, 'hitLimitEachMob', 0) or 0
        limit_every = num(info, 'hitLimitEveryMob', 0) or 0
        total_hit = num(info, 'totalHitCount', 0) or 0
        multi_attack = num(info, 'multiAttackCount', 1) or 1
        use_up = num(info, 'useUpWhenMaxMobCount', 0) or 0
        delete_alpha_delay = num(info, 'deleteAlphaDelay', 0) or 0
        # 有些弹道把速度写在 groupbuild 上（12120023 只有 vmin/vmax）。
        group_vmin = num(info, 'groupbuild/vmin', 0) or 0
        # 禁咒之符只有 v + angleList，落点由角度决定，没有 pos 是正常的。
        angle_node = res(info.get('angleList')) if info is not None else None
        has_angles = len(kids(angle_node)) > 0

        paths = []
        move = res(shoot.get('moveList'))
        for path in kids(move):
            points = []
            for point in kids(res(path)):
                node = res(point)
                points.append({'pos': vec(node.get('pos')) if node is not None else None,
                               'v': num(point, 'v', 0) or 0,
                               'delay': num(point, 'delay', 0) or 0})
            if points:
                paths.append((path.name, points))

        layers = {}
        layer_list = res(shoot.get('layerList'))
        for layer in kids(layer_list):
            layers[layer.name] = layer_ms(layer)

        if not paths and info_speed <= 0 and group_vmin <= 0:
            problems.append(f'{skill_id}: 既没有 moveList 也没有速度，弹道无法驱动')
            continue
        # 没有 layerList 的（400041017 / 4141000）是纯逻辑弹，原端本来就不画。

        # 逐路径算：flight 是纯飞行时间，life 加上段间的 delay。
        # 与 b* 飞行层比对只用 flight —— delay 段是原地停留，不该拉长比例。
        body_ms = max((ms for name, (ms, _) in layers.items() if name.startswith('b')), default=0)
        flights = []
        for name, points in paths:
            flight = 0.0
            cursor = start or (0, 0)
            moved = False
            for index, point in enumerate(points):
                speed = point['v'] if point['v'] > 0 else group_vmin
                if speed <= 0:
                    # v=0 有两种合法写法：段首 (0,0) 锚点（不移动也不等待），
                    # 以及带 delay 的原地停留段。只有整条路径「既不动也不等」才是坏数据。
                    continue
                pos = point['pos'] or cursor
                dist = ((pos[0] - cursor[0]) ** 2 + (pos[1] - cursor[1]) ** 2) ** 0.5
                if dist <= 0:
                    # angleList 弹道的落点由角度决定，pos 缺省是正常的。
                    if has_angles:
                        continue
                    problems.append(f'{skill_id}/{name}: v>0 但目标点与当前点重合')
                    continue
                flight += dist / (speed * FPS) * 1000.0
                cursor = pos
                moved = True
            if not moved and not any(p['delay'] > 0 for p in points) and not has_angles:
                problems.append(f'{skill_id}/{name}: 整条路径既没有速度也没有等待')
            flights.append(flight)

        longest = max(flights) if flights else 0.0
        if body_ms > 0 and longest > 0:
            ratio = longest / body_ms
            if not RATIO_MIN <= ratio <= RATIO_MAX:
                problems.append(f'{skill_id}: 飞行 {longest:.0f}ms 与 b 层 {body_ms}ms 不成比例'
                                f'（{ratio:.2f}，说明 v 的单位或路径读错了）')

        rows.append({'id': skill_id, 'start': start, 'body': body, 'start_delay': start_delay,
                     'info_speed': info_speed, 'max_mob': max_mob, 'paths': paths,
                     'layers': layers, 'flight': longest, 'body_ms': body_ms,
                     'limit_each': limit_each, 'limit_every': limit_every,
                     'total_hit': total_hit, 'multi_attack': multi_attack,
                     'use_up': use_up, 'delete_alpha_delay': delete_alpha_delay})

    # ---- C# 读取点 ---------------------------------------------------------
    visuals = (ROOT / 'Assets/Scripts/SkillVisuals.cs').read_text(encoding='utf-8')
    shoot_cs = (ROOT / 'Assets/Scripts/NativeShootObj.cs')
    if not shoot_cs.is_file():
        problems.append('缺少 Assets/Scripts/NativeShootObj.cs')
    else:
        source = shoot_cs.read_text(encoding='utf-8')
        if 'FramesPerSecond = 60f' not in source:
            problems.append('NativeShootObj 的 FramesPerSecond 不是 60 —— v 的单位会被改错')
        for need in ('shootobj', 'moveList', 'layerList', 'bodyWH', 'startDelay'):
            if need not in source:
                problems.append(f'NativeShootObj 没读 {need}')
        # 命中预算族：不读这些字段，弹道就退回「一怪一次、一穿即停」的老行为。
        for field in ('hitLimitEachMob', 'hitLimitEveryMob', 'multiAttackCount',
                      'multiAttackHitDelay', 'totalHitCount', 'maxMobCount',
                      'useUpWhenMaxMobCount', 'noHitWhenMaxCount'):
            if field not in source:
                problems.append(f'NativeShootObj 没读 {field}')
        for field in ('rotateAttack', 'rorateWhenFlip', 'startAlphaDelay', 'startOffset',
                      'triggerSpecialCore', 'multiStartPos', 'pickDir3Way', 'pickDir5Way',
                      'sprayType'):
            if field not in source:
                problems.append(f'NativeShootObj 没读 {field}')
        for member in ('PerSweep', 'PerMobLimit', 'HitBudget'):
            if member not in source:
                problems.append(f'NativeShootObj 缺少命中预算口径 {member}')
    # SkillVisuals 侧：这些名字一被删就说明某一项语义被回退了。
    for need in ('PerMobLimit', 'useUpWhenMaxMobCount', 'multiAttackCount', 'MultiStrike',
                 'deleteAlphaDelayMs', 'StartShootFade', 'chaseTarget', 'rotateAttack',
                 'NearestShootTarget', 'SpecialCoreHit'):
        if need not in visuals:
            problems.append(f'SkillVisuals 没有用 {need}')
    if 'Dictionary<int,int> struck' not in visuals:
        problems.append('ShootStrike 的 struck 退回 HashSet —— 单怪多段命中会失效')
    if 'NativeShootObj.Has(s.native.Id)' not in visuals:
        problems.append('SkillVisuals.UsesProjectile 没有把 shootobj 计入远程弹道')
    if 'shootobj/layerList/' not in visuals:
        problems.append('SkillVisuals 没有播放 shootobj/layerList 层')
    if not re.search(r'ShootObjData\.Speed\(', visuals):
        problems.append('SkillVisuals 没有用 ShootObjData.Speed 换算速度')

    # ---- 输出 -------------------------------------------------------------
    print(f'shootobj 技能 {len(rows)} 个')
    budget = [r for r in rows if r['limit_each'] or r['limit_every'] or r['total_hit'] or r['multi_attack'] > 1]
    if budget:
        detail = '、'.join(
            f"{r['id']}(每怪 {max(r['limit_each'], r['limit_every']) or 1}"
            f"{' / 总 ' + str(int(r['total_hit'])) if r['total_hit'] else ''}"
            f"{' / ' + str(int(r['multi_attack'])) + ' 段' if r['multi_attack'] > 1 else ''})"
            for r in budget)
        print(f'命中预算族 {len(budget)} 个：{detail}')
    else:
        print('命中预算族 0 个（全是「一怪一次」，与 282 数据不符就该查读取）')
    early_fade = [r for r in rows if r['delete_alpha_delay'] < 0]
    print(f'deleteAlphaDelay 负值（结束前渐隐）{len(early_fade)} 个；'
          f'useUpWhenMaxMobCount {sum(1 for r in rows if r["use_up"])} 个')
    print(f"{'技能':<10}{'起手':>6}{'飞行':>9}{'b层':>8}{'路径':>6}{'层':>4}  说明")
    for row in rows:
        note = '无层(纯逻辑弹)' if not row['layers'] else ''
        if row['body_ms'] and row['flight']:
            note = f"{row['flight'] / row['body_ms']:.2f}x b层"
        print(f"{row['id']:<10}{row['start_delay']:>6.0f}{row['flight']:>8.0f}ms"
              f"{row['body_ms']:>7.0f}ms{len(row['paths']):>6}{len(row['layers']):>4}  {note}")
        if args.verbose:
            for name, points in row['paths']:
                detail = ', '.join(f"({p['pos'][0]},{p['pos'][1]}) v={p['v']} d={p['delay']}"
                                   for p in points)
                print(f'    {name}: {detail}')
            print('    layers: ' + ', '.join(f'{n}({ms}ms)' for n, (ms, _) in row['layers'].items()))

    if problems:
        print(f'\n✗ {len(problems)} 个问题：')
        for item in problems:
            print('   -', item)
        return 1
    print('\nOK — shootobj 数据完整、速度口径为 v*60 px/s、运行时读取点齐全')
    return 0


if __name__ == '__main__':
    sys.exit(main())
