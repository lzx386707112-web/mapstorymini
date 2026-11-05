#!/usr/bin/env python3
"""五六转技能「可玩性」全维度回归（特效之外的 15 个面）。

`check_skill_vfx_coverage.py` 只回答「这一层有没有人播」。本脚本回答
「播出来的东西对不对」：范围、方向、位置、施法时长、节点时序、弹道、
追怪、连射/蓄力、召唤与召唤物动作、飘字段数、暴击、节点可解析、
单次施放的特效实例数（性能）、对怪效果层、施放时的人物动作。

    cd MapleDash && rtk proxy python3 tools/check_advanced_skill_playback.py
    rtk proxy python3 tools/check_advanced_skill_playback.py --jobs 313,323 --verbose

FAIL 是确定性错误（方向反了、召唤缺动作、段数为 0…）。
WARN 是需要人工确认的形态差异（默认 300px 射程、首帧 origin 偏移过大…），
不阻塞，但会逐条列出来方便按技能过一遍。
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import sys
from collections import Counter, OrderedDict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
COVER = ROOT / "tools/check_skill_vfx_coverage.py"
_spec = importlib.util.spec_from_file_location("skill_coverage", COVER)
cov = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(cov)

kids = cov.kids
resolve = cov.resolve
int_field = cov.int_field
canvas_or_none = cov.canvas_or_none
count_canvas = cov.count_canvas
branch_class = cov.branch_class
load_skills = cov.load_skills
root_of = cov.root_of
TREE = cov.TREE

SHOOTOBJ_CACHE: dict[int, object] = {}


def num(node, path, default=0):
    return int_field(node, path, default)


def vec(node, path):
    child = resolve(node.get(path)) if node is not None else None
    if child is None or not isinstance(child.value, tuple):
        return None
    return list(child.value)


def level_row(data, level=1):
    levels = data.get("levels") or {}
    row = levels.get(str(level))
    if row is None:
        row = levels.get(str(max((int(k) for k in levels), default=1)))
    return row or {}


def node_of(job, sid):
    root = root_of(f"Skill/{sid // 10000}.img")
    return root.get(f"skill/{sid}") if root is not None else None


def frame_ms(node, depth=0):
    """返回 (该层最长的单次播放时长 ms, 画布张数)。

    数字子节点是同一动画的帧（时长相加）；非数字子节点是并列变体/子层
    （时长取最大值，帧数相加），与运行时随机取一个变体的口径一致。
    """
    node = resolve(node)
    if node is None or depth > 10:
        return 0, 0
    if canvas_or_none(node) is not None and node.__class__.__name__ == "WzCanvasProperty":
        return max(1, abs(num(node, "delay", 100))), 1
    frames = [c for c in kids(node) if str(c.name).isdigit()]
    if frames:
        total = 0
        canvas = 0
        for frame in frames:
            delay = max(1, abs(int_field(frame, "delay", 100)))
            total += delay
            ok, _ = count_canvas(frame)
            canvas += ok
        return total, canvas
    longest = 0
    canvas = 0
    for child in kids(node):
        ms, n = frame_ms(child, depth + 1)
        longest = max(longest, ms)
        canvas += n
    return longest, canvas


def first_origin(node):
    node = resolve(node)
    seen = 0
    while node is not None and seen < 4:
        frames = [c for c in kids(node) if str(c.name).isdigit()]
        if not frames:
            if canvas_or_none(node) is not None:
                o = resolve(node.get("origin"))
                return list(o.value) if o is not None and isinstance(o.value, tuple) else None
            return None
        node = resolve(frames[0])
        seen += 1
    return None


def shootobj_paths(sid):
    if sid in SHOOTOBJ_CACHE:
        return SHOOTOBJ_CACHE[sid]
    node = node_of(None, sid)
    data = None
    shoot = resolve(node.get("shootobj")) if node is not None else None
    if shoot is not None:
        # 运行时的读取口径是 shootobj/info/*（NativeShootObj.Read）。
        info = resolve(shoot.get("info")) or shoot
        data = {
            "has": True,
            "hasMove": resolve(shoot.get("moveList")) is not None,
            "start": vec(info, "start"),
            "bodyWH": vec(info, "bodyWH"),
            "maxMob": num(info, "maxMobCount", 1),
            "chase": num(info, "chaseTarget"),
            "layers": resolve(shoot.get("layerList")) is not None,
            "angles": resolve(info.get("angleList")) is not None,
        }
    SHOOTOBJ_CACHE[sid] = data
    return data


def replay_lines(row):
    try:
        return max(1, max(int(row.get("attackCount", 0) or 0), int(row.get("bulletCount", 0) or 0)))
    except (TypeError, ValueError):
        return 1


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--jobs", help="只查这些职业 id，逗号分隔")
    ap.add_argument("--verbose", action="store_true", help="逐技能打印一行")
    args = ap.parse_args()
    jobs_filter = {int(x) for x in args.jobs.split(",")} if args.jobs else None

    entries = load_skills(jobs_filter)
    fails: list[str] = []
    warns: list[str] = []
    rows = []
    stat = Counter()

    for job, sid, data in entries:
        node = node_of(job, sid)
        if node is None:
            fails.append(f"{data['name']}({sid}) 定义节点缺失")
            continue
        row = level_row(data)
        kind = data.get("type") or "?"
        art = {}
        for child in kids(node):
            if child.name in ("icon", "iconMouseOver", "iconDisabled", "level"):
                continue
            ok, _ = count_canvas(child)
            if ok:
                art[child.name] = ok
        level = node.get("level")
        if level is not None:
            for lv in kids(level):
                for child in kids(lv):
                    if child.name in ("icon", "iconMouseOver", "iconDisabled"):
                        continue
                    ok, _ = count_canvas(child)
                    if ok:
                        art[child.name] = max(art.get(child.name, 0), ok)

        played = [b for b in art if branch_class(b) == "PLAYED"]
        lt, rb = vec(node, "level/1/lt") or vec(node, "lt"), vec(node, "level/1/rb") or vec(node, "rb")
        # 等级分支可能只写在 level/N 下，回退到实际行数据
        if lt is None and isinstance(row.get("lt"), list):
            lt = row["lt"]
        if rb is None and isinstance(row.get("rb"), list):
            rb = row["rb"]
        rng = row.get("range")
        try:
            rng = int(rng) if rng is not None else 0
        except (TypeError, ValueError):
            rng = 0
        shoot = shootobj_paths(sid)

        # —— 范围 ——
        has_bounds = bool(lt and rb)
        # info/skillType=3：源端「无固定攻击框」标记，运行时按相机宽度判定（NativeSkillDefinition.FullScreen）。
        full_screen = not has_bounds and num(node, "skillType") == 3
        if kind == "attack":
            stat["attack"] += 1
            # invisible=1 是子技能（由母技能/被动触发，没有独立施法动作），
            # 源端不给攻击框，判定跟随触发者或母技能，不算缺范围。
            hidden = bool(data.get("invisible"))
            if full_screen:
                stat["full_screen"] += 1
            elif hidden:
                stat["sub_skill"] += 1
            elif not has_bounds and rng <= 0 and not (shoot and shoot["has"]):
                stat["range_default"] += 1
                warns.append(f"{data['name']}({sid}) 没有 lt/rb、range、shootobj，射程退回默认 300px")
        # —— 方向 ——
        # WZ 口径：角色在原点，身前是负 x。rb.x < 0 是「贴着身体的近身段留空」，
        # 合法（狙杀之击VI -960..-60）。真正反了的是左边界跑到右边界右侧，
        # 或者整段都落在身后（lt.x>0 且 rb.x>0）。
        if has_bounds:
            if lt[0] > rb[0]:
                fails.append(f"{data['name']}({sid}) 攻击框左右反了 lt={lt} rb={rb}")
            elif lt[0] > 0 and rb[0] > 0:
                fails.append(f"{data['name']}({sid}) 攻击框整段在身后 lt={lt} rb={rb}")
            if lt[1] > rb[1]:
                fails.append(f"{data['name']}({sid}) 攻击框 y 上下界反了 lt={lt} rb={rb}")
            if lt[0] == 0 and rb[0] == 0:
                fails.append(f"{data['name']}({sid}) 攻击框 x 宽度为 0 lt={lt} rb={rb}")
        # —— 位置 ——
        # screen 层按相机尺寸铺满、hit 层是怪身上的大位移落点，两者的 origin 本来就大。
        for branch in played:
            if branch.startswith("screen") or branch.startswith("hit") or branch.startswith("mob"):
                continue
            lv = resolve(node.get("level/1/" + branch))
            origin = first_origin(node.get(branch)) or first_origin(lv)
            if origin and (abs(origin[0]) > 1200 or abs(origin[1]) > 1200):
                warns.append(f"{data['name']}({sid}) {branch} 首帧 origin={origin} 偏移过大")
        # —— 施法时长 / 时序 ——
        prep_ms = 0
        for branch in art:
            if branch.startswith("prepare"):
                prep_ms = max(prep_ms, frame_ms(node.get(branch))[0])
        pattern = resolve(node.get("pattern")) if node.get("pattern") is not None else None
        effect_ms = 0
        effect_instances = 0
        delayed = []
        for branch in played:
            if branch.startswith("screen"):
                continue
            ms, canvases = frame_ms(node.get(branch))
            effect_ms = max(effect_ms, ms)
            effect_instances += canvases
            h = [c for c in kids(resolve(node.get(branch))) if str(c.name).isdigit()] if resolve(node.get(branch)) is not None else []
            if len(h) >= 2:
                first = max(1, abs(int_field(h[0], "delay", 100)))
                rest = sum(max(1, abs(int_field(f, "delay", 100))) for f in h[1:]) / (len(h) - 1)
                if first > 150 and first > rest * 2:
                    delayed.append((branch, first))
        if prep_ms > 3000:
            warns.append(f"{data['name']}({sid}) 起手层 {prep_ms}ms，超过 3 秒")
        if effect_ms > 12000:
            warns.append(f"{data['name']}({sid}) 单层特效 {effect_ms}ms，超过 12 秒")
        for branch, delay in delayed:
            if delay > 3000:
                warns.append(f"{data['name']}({sid}) {branch} 首帧 {delay}ms 才出现（确认是有意延迟）")
        if effect_instances > 400:
            stat["heavy"] += 1
            warns.append(f"{data['name']}({sid}) 单次施放铺 {effect_instances} 张画布，注意性能")
        # —— 弹道 / 追怪 ——
        has_ball = "ball" in art or "flipBall" in art
        # 有 shootobj 的技能走 Actor 弹道，射程由 moveList 决定，ball 只是陪衬。
        if has_ball and not has_bounds and rng <= 0 and not (shoot and shoot["has"]):
            stat["ball_default_range"] += 1
            warns.append(f"{data['name']}({sid}) 有 ball 但没有攻击框/range，弹道按 300px 飞")
        if shoot and shoot["has"]:
            stat["shootobj"] += 1
            if not shoot["hasMove"] and not shoot["layers"]:
                warns.append(f"{data['name']}({sid}) shootobj 既无 moveList 也无 layerList（纯逻辑弹）")
            if shoot["bodyWH"] is None:
                warns.append(f"{data['name']}({sid}) shootobj 缺 bodyWH，命中框为 0（只能靠落点兜底）")
        # —— 连射 / 蓄力 ——
        has_keydown = any(b.startswith("keydown") for b in art)
        rapid = num(node, "info/rapidAttack")
        if has_keydown and not rapid and not any(b.startswith("keydownend") for b in art):
            fails.append(f"{data['name']}({sid}) 有 keydown 却没有 keydownend，松手无收手表现")
        if rapid:
            stat["rapid"] += 1
        elif has_keydown:
            stat["charge"] += 1
        # —— 召唤与召唤物动作 ——
        if kind == "summon":
            summon = resolve(node.get("summon"))
            if summon is None:
                fails.append(f"{data['name']}({sid}) summon 技能没有 summon 节点")
            else:
                acts = {c.name for c in kids(summon)}
                stat["summon"] += 1
                if not any(a.startswith("stand") for a in acts):
                    fails.append(f"{data['name']}({sid}) 召唤物缺 stand（现有 {sorted(acts)}）")
                if not any(a.startswith("die") for a in acts):
                    fails.append(f"{data['name']}({sid}) 召唤物缺 die（现有 {sorted(acts)}）")
                # 攻击动作族：attack* / skill*（船员、风之圣谕）/ fly（蝙蝠扑击）。
                # 领域型召唤物（剧毒领域）靠 tile 铺地结算，本来就不该有攻击动作。
                attackish = any(a.startswith(("attack", "skill", "fly")) for a in acts)
                if not attackish:
                    if any(b.startswith("tile") for b in art):
                        stat["field_summon"] += 1
                    elif row.get("damage"):
                        warns.append(f"{data['name']}({sid}) 召唤物有伤害但无攻击动作"
                                     f"（现有 {sorted(acts)}），运行时不会出手")
        # —— 飘字段数 / 暴击 ——
        lines = replay_lines(row)
        if kind == "attack" and not row.get("damage") and not row.get("fixdamage") and not row.get("attackCount"):
            warns.append(f"{data['name']}({sid}) attack 技能没有 damage/attackCount（可能只是状态型）")
        crit = row.get("cr") or row.get("criticaldamage") or num(node, "common/cr")
        if crit:
            stat["crit"] += 1
        # —— 对怪效果层 ——
        if "mob" in art:
            stat["mob_status"] += 1
        if "hit" not in art and "hit2" not in art and kind == "attack" and played:
            warns.append(f"{data['name']}({sid}) 有表现层但没有 hit/hit2，命中时怪身上没有火花")
        # —— 施放动作 ——
        action = resolve(node.get("action")) or resolve(node.get("level/1/action"))
        if action is None:
            stat["action_default"] += 1

        rows.append((job, sid, data, kind, played, lt, rb, rng, lines, effect_ms, prep_ms, effect_instances))
        if args.verbose:
            bounds = f"{lt}->{rb}" if lt and rb else ("range " + str(rng) if rng else "默认")
            print(f"  {sid:<10} {data['name'][:14]:<16} {kind:<7} 框{bounds:<18} "
                  f"段{lines} 特效{effect_ms}ms 起手{prep_ms}ms 层{len(played)} 图{effect_instances}")
            if shoot and shoot["has"]:
                print(f"             shootobj start={shoot['start']} bodyWH={shoot['bodyWH']} "
                      f"maxMob={shoot['maxMob']} chase={shoot['chase']} layers={shoot['layers']}")

    print(f"技能 {len(entries)} 个：attack {stat['attack']}、summon {stat['summon']}、"
          f"连射 {stat['rapid']}、蓄力 {stat['charge']}、shootobj {stat['shootobj']}、"
          f"全屏(skillType=3) {stat['full_screen']}、带 mob 状态层 {stat['mob_status']}、"
          f"带暴击字段 {stat['crit']}")
    print(f"默认射程 {stat['range_default']} / 默认弹道 {stat['ball_default_range']} / "
          f"重特效 {stat['heavy']} / 无独立施法动作 {stat['action_default']} / 子技能 {stat['sub_skill']}")
    heavy = sorted(rows, key=lambda r: -r[11])[:5]
    if heavy and heavy[0][11] > 0:
        print("  单次施放画布最多的 5 个：" + "、".join(f"{r[2]['name']}({r[1]}) {r[11]} 张" for r in heavy))
    longest = sorted(rows, key=lambda r: -r[9])[:5]
    if longest and longest[0][9] > 0:
        print("  单层特效最长的 5 个：" + "、".join(f"{r[2]['name']}({r[1]}) {r[9] / 1000:.1f}s" for r in longest))

    print(f"\nFAIL {len(fails)} 条")
    for item in fails[:40]:
        print("  -", item)
    print(f"WARN {len(warns)} 条")
    for item in warns[:60]:
        print("  -", item)
    if len(warns) > 60:
        print(f"  … 另有 {len(warns) - 60} 条，用 --jobs 分职业看")
    return 1 if fails else 0


if __name__ == "__main__":
    raise SystemExit(main())
