#!/usr/bin/env python3
"""五六转技能「引用 + 特效覆盖」逐技能回归。

守三件事：

R 引用：JobTree 里 17 个五六转职业的每个技能，都能在
  `Skill/<id//10000>.img/skill/<id>` 找到定义节点，且每个 Canvas 叶子
  （含 UOL / `_outlink` 链）最终落到一张真实画布。资源拆分后定义侧只剩
  1x1 桩 + `_outlink`，这里按运行时 `WzAssets.Canvas` 的解析顺序复核。

E 特效：把技能下的每个一级分支按「运行时怎么处理」归类：
  - PLAYED    SkillVisuals 会播（prepare / effect* / special* / hit* /
              affected / tile / ball / mob / screen*）
  - ELSEWHERE 由别的系统接管（summon → NativeSummon，keydown* →
              SkillHeldInput 蓄力路径，movement → 三条特例）
  - META      纯数据节点（info / req / common / icon…）
  - GAP       有画布但运行时没人播 —— 这就是漏层

G 缺口：GAP 里除了 ACCEPTED 白名单（附理由）之外，一律判 FAIL。
  新增 282 资源若带出新的特效分支，会先炸在这里，而不是上线后看不见。

T 类型：attack 技能必须有表现层（否则是纯空气伤害）；buff 技能允许只有
  `affected`；summon 必须有 `summon`。同时核对每个职业的技能数与类型分布，
  方便人工按职业过一遍。

    cd MapleDash && rtk proxy python3 tools/check_skill_vfx_coverage.py
    rtk proxy python3 tools/check_skill_vfx_coverage.py --jobs 313,323,413,213
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from collections import Counter, OrderedDict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]          # MapleDash/
DATA = ROOT.parent / "MapleDashResources/Data"
SKILL = DATA / "Skill"
TREE = ROOT / "Assets/Resources/UI/JobTree.json"
sys.path.insert(0, "/Users/lizixian/Documents/mxd/BeiDou-Server/tool/wz-python")

from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzCanvasProperty, WzUolProperty  # noqa: E402

GMS = WzKey.for_region("GMS")

# 运行时真正会播的一级分支。数字后缀的兄弟分支（effect1/effect2、special0、
# hit2、keydown0…）由 NativeSkillVfx 的族枚举统一接，见 vfx_families。
PLAYED = {"prepare", "affected", "tile", "ball", "shootobj", "mob", "background", "effect", "effect0",
          "special", "hit",
          # SkillVisuals.PlaySegmentedLayer / PlayCustomEffect / PlayFieldLoop / Afterimage
          "effect_ple", "frameEffect", "customEffect", "finish",
          "start", "repeat", "end", "afterimage",
          # Impact 交替播 hit / hit2；ShowComboNumber 播 number / number2
          "hit2", "number", "number2",
          # BallPart 在 ball/flipBall 之间逐发交替（3141004 箭座VI）
          "flipBall",
          # NativeArrowMode 播 mode/0|1（魔幻箭筒VI 箭镞切换）
          "mode"}
# 与 NativeSkillVfx.Layers 的族枚举一致：这些数字后缀兄弟分支现在会播。
PLAYED_FAMILY = re.compile(r"^(effect[1-9]\d*|special\d+|prepare\d+|"
                           r"effect_ple\d+|frameEffect\d+|"
                           r"start\d+|repeat\d+|end\d+)$")
ELSEWHERE = {"summon", "movement"}
META = {"info", "info2", "req", "common", "action", "level", "icon",
        "iconMouseOver", "iconDisabled", "weapon", "weapon2", "weapon3", "weapon4",
        "origin", "invisible", "psd", "psdSkill", "skillList", "finalAttack",
        "preloadEff", "holding", "ascent", "SecondAtom", "applyHyper",
        "multiAttackInfo", "additional_process", "processtype",
        "isBeneficial", "elemAttr", "elemAttr2", "cancelableSkillID",
        "limitSamePosCount", "notCoolDown", "notCooltimeReset", "notIncBuffDuration",
        "notRemoved", "alertTime", "extraSkillInfo", "hideBullet", "weaponItem",
        "skillType", "isSequenceOn", "syncSummonedBuffIcon", "maintainSplitSummoned",
        "showSummonedBuffIcon", "footholdInstallSummoned", "footholdAffectedArea",
        "trembleEffect", "trembleEffectList", "waveEffectInfo", "distortionEffectInfo",
        "effectFlash", "effectSelfCloneList", "effect_ple", "effect_ple0",
        "pairskill", "repeat", "repeat0", "start", "start0", "end", "end0",
        "finish", "flipBall", "mode", "frameEffect", "removeEffect",
        "secondaryStatFreezeExcept", "bossClearOff", "ignoreSpecialCore",
        "addAttack", "resetByUnionCharTag", "notResetDarkSight",
        "resetDarkSightOnShoot", "ignoreResetDarkSightOnAttack",
        "normalAttackAtBulletEmpty", "applyHighSkillSummonBonusTime",
        "canNotStealableSkill", "isExceptSetLastAttack", "afterimage"}

# 有画布、但本项目明确不接的分支（附理由，守卫会按条数复查）。
ACCEPTED = {
    "effectFlash": "白闪整屏层（localOnly），未接",
    "effectSelfCloneList": "分身施法层（actionType 驱动），未接",
}

_roots: dict[str, object] = {}


def root_of(rel: str):
    if rel in _roots:
        return _roots[rel]
    path = DATA / rel
    node = None
    if path.is_file():
        img = WzImage.from_bytes(path.read_bytes(), key=GMS, name=path.name)
        img.parse()
        node = img.root
    if len(_roots) > 24:
        _roots.clear()
    _roots[rel] = node
    return node


def kids(node):
    try:
        return list(node.children())
    except TypeError:
        return []


def resolve(node, depth=0):
    for _ in range(32):
        if node is None or depth > 24:
            return None
        if isinstance(node, WzUolProperty):
            node = node.parent.get(node.value) if node.parent else None
            continue
        return node
    return None


def int_field(node, path, default=0):
    child = resolve(node.get(path)) if node is not None else None
    try:
        return int(child.value) if child is not None and child.value is not None else default
    except (TypeError, ValueError):
        return default


def canvas_or_none(node, depth=0):
    """按 WzAssets.Canvas 的顺序解 UOL / _inlink / _outlink。"""
    node = resolve(node)
    if node is None or depth > 24:
        return None
    link = None
    for name in ("_inlink", "_outlink"):
        child = node.get(name)
        if child is not None and child.value:
            link = str(child.value)
            break
    if link is None:
        return node if isinstance(node, WzCanvasProperty) else None
    if link.startswith("Skill/_Canvas/") or ".img/" in link:
        cut = link.find(".img/")
        if cut < 0:
            return None
        target_root = root_of(link[:cut + 4])
        if target_root is None:
            return None
        return canvas_or_none(target_root.get(link[cut + 5:]), depth + 1)
    # _inlink 是同一张 img 内的路径
    top = node
    while top.parent is not None:
        top = top.parent
    return canvas_or_none(top.get(link), depth + 1)


def count_canvas(node, depth=0):
    """返回 (可解析画布数, 断链数)。"""
    node = resolve(node)
    if node is None or depth > 12:
        return 0, 0
    if isinstance(node, WzCanvasProperty):
        return (1, 0) if canvas_or_none(node) is not None else (0, 1)
    ok = bad = 0
    for child in kids(node):
        a, b = count_canvas(child, depth + 1)
        ok += a
        bad += b
    return ok, bad


def branch_class(name):
    # 白名单优先：这些分支确实有画布，但本项目按理由不接，单列成「已知」。
    if name in ACCEPTED:
        return "ACCEPTED"
    if name in PLAYED or name.startswith("screen"):
        return "PLAYED"
    if PLAYED_FAMILY.match(name):
        return "PLAYED"
    if name in ELSEWHERE:
        return "ELSEWHERE"
    if name in META:
        return "META"
    if name.startswith("keydown") or name.startswith("keydownend"):
        return "ELSEWHERE"
    return "GAP"


def load_skills(jobs_filter):
    tree = json.loads(TREE.read_text(encoding="utf-8"))
    skills = tree["skills"]
    out = []
    for job in tree["jobs"]:
        if job["tier"] < 5:
            continue
        if jobs_filter and job["id"] not in jobs_filter:
            continue
        for sid in job["skills"]:
            data = skills.get(str(sid))
            if not data:
                continue
            out.append((job, sid, data))
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--jobs", help="只查这些职业 id，逗号分隔")
    ap.add_argument("--verbose", action="store_true", help="逐技能打印分支明细")
    args = ap.parse_args()
    jobs_filter = {int(x) for x in args.jobs.split(",")} if args.jobs else None

    entries = load_skills(jobs_filter)
    if not entries:
        print("没有可查的技能")
        return 1

    problems: list[str] = []
    gaps: "OrderedDict[str, list[str]]" = OrderedDict()
    per_job = OrderedDict()
    refs_bad: list[str] = []
    canvas_total = 0
    type_counter = Counter()
    empty_attack: list[str] = []
    mastery_empty: list[str] = []
    rapid_skills: list[str] = []
    charge: list[str] = []
    charge_no_end: list[str] = []

    for job, sid, data in entries:
        img_name = f"{sid // 10000}.img"
        root = root_of(f"Skill/{img_name}")
        if root is None:
            refs_bad.append(f"{sid} 缺 {img_name}")
            continue
        node = root.get(f"skill/{sid}")
        if node is None:
            refs_bad.append(f"{sid} 在 {img_name} 里没有 skill/{sid}")
            continue

        branches: "OrderedDict[str, int]" = OrderedDict()
        for child in kids(node):
            if child.name in ("icon", "iconMouseOver", "iconDisabled", "level"):
                continue
            ok, bad = count_canvas(child)
            canvas_total += ok
            if bad:
                refs_bad.append(f"{sid}/{child.name} 有 {bad} 张画布解不开")
            branches[child.name] = ok
        level = node.get("level")
        if level is not None:
            for lv in kids(level):
                for child in kids(lv):
                    if child.name in ("icon", "iconMouseOver", "iconDisabled"):
                        continue
                    ok, bad2 = count_canvas(child)
                    canvas_total += ok
                    if bad2:
                        refs_bad.append(f"{sid}/level/{lv.name}/{child.name} 有 {bad2} 张画布解不开")
                    branches[child.name] = max(branches.get(child.name, 0), ok)

        played = [b for b, n in branches.items() if n and branch_class(b) == "PLAYED"]
        art_branches = {b: n for b, n in branches.items() if n}
        for name, count in art_branches.items():
            cls = branch_class(name)
            if cls in ("GAP", "ACCEPTED"):
                gaps.setdefault(name, []).append(f"{job['name']}/{data['name'][:14]}({sid})")
        # 蓄力/连射：有 keydown 层就必须能被按住的路径接住。
        # 源端 info/rapidAttack=1 走连射；没有标记的是蓄力（按住攒、松手施放一次）。
        if any(b.startswith("keydown") for b in art_branches):
            if int_field(node, "info/rapidAttack"):
                rapid_skills.append(f"{data['name']}({sid})")
            else:
                charge.append(f"{job['name']}/{data['name']}({sid})")
                # 蓄力路径靠 keydownend 收手：缺了就只能一直按着、没有释放表现。
                if not any(b.startswith("keydownend") for b in art_branches):
                    charge_no_end.append(f"{job['name']}/{data['name']}({sid})")

        kind = data.get("type") or "?"
        type_counter[kind] += 1
        if kind == "attack" and not played:
            if data.get("hexa"):
                # HEXA 精通节点（暗影蝙蝠VI/日月力量…）只强化既有技能，本身不可施放，
                # 源端就没给美术，属正常。
                mastery_empty.append(f"{data['name']}({sid})")
            else:
                empty_attack.append(f"{job['name']}/{data['name']}({sid})")

        per_job.setdefault(job["id"], {"name": job["name"], "n": 0, "gap": 0})
        per_job[job["id"]]["n"] += 1
        if any(branch_class(b) == "GAP" for b in art_branches):
            per_job[job["id"]]["gap"] += 1
        if args.verbose:
            shown = ", ".join(f"{b}:{n}" for b, n in art_branches.items())
            print(f"  {job['id']:>5} {sid:<10} {data['name'][:16]:<18} {kind:<8} {shown}")

    print(f"R 引用: {len(entries)} 个五六转技能，定义缺失 {len(refs_bad)}，"
          f"画布 {canvas_total} 张 {'PASS' if not refs_bad else 'FAIL'}")
    if refs_bad:
        problems.append(f"引用断裂 {len(refs_bad)} 处：" + "、".join(refs_bad[:5]))

    print("\n  职业            技能  有缺口")
    for job_id, info in per_job.items():
        print(f"  {job_id:>5} {info['name'][:12]:<12} {info['n']:>4} {info['gap']:>5}")

    print(f"\nT 类型: " + " / ".join(f"{k} {v}" for k, v in type_counter.most_common()))
    if empty_attack:
        problems.append(f"{len(empty_attack)} 个 attack 技能没有任何已播放分支："
                        + "、".join(empty_attack[:5]))
        print(f"  空攻击（无表现层）{len(empty_attack)} 个 FAIL")
    else:
        print(f"  空攻击（无表现层）0 个 PASS"
              f"（另有 HEXA 精通节点 {len(mastery_empty)} 个，源端本就无美术）")
    print(f"  连射（rapidAttack=1，按住逐发）{len(rapid_skills)} 个")
    if charge:
        print(f"  蓄力（无 rapidAttack，按住攒、松手施放一次）{len(charge)} 个: {'、'.join(charge)}")
    if charge_no_end:
        print(f"  蓄力缺 keydownend {len(charge_no_end)} 个 FAIL: {'、'.join(charge_no_end)}")

    unaccepted = {k: v for k, v in gaps.items() if k not in ACCEPTED}
    accepted = {k: v for k, v in gaps.items() if k in ACCEPTED}
    print(f"\nG 未接入分支: {len(gaps)} 类 / {sum(len(v) for v in gaps.values())} 处，"
          f"其中已白名单 {len(accepted)} 类")
    for name, hits in gaps.items():
        tag = "已知" if name in ACCEPTED else "缺口"
        print(f"  [{tag}] {name:<20} {len(hits):>3} 例: {'、'.join(hits[:3])}")
        if name in ACCEPTED:
            print(f"          理由: {ACCEPTED[name]}")
    if unaccepted:
        problems.append(f"{len(unaccepted)} 类特效分支运行时没接：" + "、".join(unaccepted))
    if charge_no_end:
        problems.append(f"{len(charge_no_end)} 个蓄力技能缺 keydownend，松手没有释放表现")

    problems.extend(check_runtime())
    problems.extend(check_arrow_mode())
    return finish(problems)


def _ints(node):
    """把 WZ 容器节点的子项读成 int 列表（skillList / additional_process 这类）。"""
    node = resolve(node) if node is not None else None
    if node is None or not hasattr(node, "children"):
        return []
    out = []
    for child in node.children():
        item = resolve(child)
        try:
            out.append(int(item.value))
        except Exception:
            continue
    return out


def check_arrow_mode() -> list[str]:
    """箭镞切换（3141005）的资源形状：两套模式动画，各自 ≥8 帧且画布不断链。"""
    issues: list[str] = []
    root = root_of("Skill/314.img")
    if root is None:
        return ["缺少 Skill/314.img，箭镞切换无法播放"]
    skill = root.get("skill/3141005")
    mode = resolve(skill.get("mode")) if skill is not None else None
    common = resolve(skill.get("common")) if skill is not None else None
    if mode is None:
        return ["3141005 没有 mode 分支（箭镞切换特效丢失）"]
    sets = kids(mode)
    if len(sets) < 2:
        issues.append(f"3141005/mode 只有 {len(sets)} 套，魔法箭与吸血箭应为 2 套")
    for group in sets:
        frames = [f for f in kids(group) if str(getattr(f, "name", "")).isdigit()]
        if len(frames) < 8:
            issues.append(f"3141005/mode/{group.name} 只有 {len(frames)} 帧（<8，切换动画会一闪而过）")
        for frame in frames:
            if canvas_or_none(frame) is None:
                issues.append(f"3141005/mode/{group.name}/{frame.name} 画布断链")
                break
    # 命中表现：魔法箭/吸血箭的 hit 至少各一张，否则触发时看不见。
    # hit 常带一层变体壳（hit/0/0..5），交给 count_canvas 递归解析。
    for fx, label in ((3140010, "魔法箭"), (3140011, "吸血箭")):
        fx_root = root_of(f"Skill/{fx // 10000}.img")
        fx_skill = fx_root.get(f"skill/{fx}") if fx_root is not None else None
        hit = resolve(fx_skill.get("hit")) if fx_skill is not None else None
        ok = count_canvas(hit)[0] if hit is not None else 0
        if ok == 0:
            issues.append(f"{label}（{fx}）没有可解析的 hit，箭镞触发时没有命中表现")

    # —— 箭筒 / 满弹爆发的数值来源 ——
    # 3141005、3140006、3140010、3140011 都是「只有 common、没有 level」的技能，
    # SkillBook.Level() 对它们返回 null。数值必须走 Common() 的公式求值，
    # 否则 u/w/s/t 全是 0，箭模式命中时永远掷不出触发（这个坑真实踩过）。
    for key in ("u", "w", "s", "t"):
        field = resolve(common.get(key)) if common is not None else None
        if field is None:
            issues.append(f"3141005.common/{key} 缺失，箭筒概率/伤害读不到")

    # 公式能不能真求出来。SkillBook.Common 只认「常量 / a+b*x」，源端公式一换形态
    # （比如改成 a+b*x+c*x*x）就地静默失效 —— 箭模式会一声不响地永不触发。
    def eval_common(text, level):
        expr = str(text).replace("x", str(level))
        if not set(expr) <= set("0123456789+-*/.() "):
            return None
        try:
            return float(eval(expr, {"__builtins__": {}}, {}))  # noqa: S307 — 字符集已白名单
        except Exception:                                   # noqa: BLE001
            return None

    s_node = resolve(common.get("s")) if common is not None else None
    if s_node is not None:
        value = eval_common(getattr(s_node, "value", None), 1)
        if value is None or not 200 <= value <= 2000:
            issues.append(f"3141005.s = {getattr(s_node, 'value', None)!r} 在 L1 求值异常（{value}），"
                          "箭筒伤害不可用（SkillBook.Common 的公式口径对不上）")
    # 满弹爆发 3140006：invisible 的纯判定技能，攻击框与伤害全在 common 上。
    # 3140006 不是「满弹爆发」，它是 3141005 的魔法箭判定体：
    # damage 与 3141005.common/s 完全同源（都是 292+5*x），归属由这条同源关系确定。
    body = root.get("skill/3140006")
    body_common = resolve(body.get("common")) if body is not None else None
    if body_common is None:
        issues.append("缺少 3140006（魔幻箭筒VI 的魔法箭判定体）")
    else:
        for key in ("damage", "lt", "rb"):
            if resolve(body_common.get(key)) is None:
                issues.append(f"3140006.common/{key} 缺失，魔法箭的伤害/攻击框无法结算")

    # QuiverFullBurst 属于 3141009「追加魔幻箭筒」：装填满 w2 时自动发动。
    # 它的判定体是 skillList 里显式列出的 3140010 / 3140011。
    append = root.get("skill/3141009")
    append_common = resolve(append.get("common")) if append is not None else None
    if append_common is None:
        issues.append("缺少 3141009 追加魔幻箭筒（装填 / 全弹发射的宿主）")
    else:
        for key in ("time", "s", "dummyStr", "w", "w2"):
            if resolve(append_common.get(key)) is None:
                issues.append(f"3141009.common/{key} 缺失，装填上限或自动发动阈值读不到")
        listed = _ints(append.get("skillList"))
        for need in (3140010, 3140011):
            if need not in listed:
                issues.append(f"3141009.skillList 没列 {need}，追加发射的判定体归属断了")

    # —— C# 侧：数值求值 / 满弹爆发 / 持久化 ——
    book = (ROOT / "Assets/Scripts/SkillBook.cs").read_text(encoding="utf-8")
    if "public static float Common(" not in book or "EvalCommon(" not in book:
        issues.append("SkillBook 没有 common 公式求值（3141005/3140006 只有 common 没有 level）")
    arrow = (ROOT / "Assets/Scripts/NativeArrowMode.cs").read_text(encoding="utf-8")
    for need, why in (
        ('SkillBook.Common(Toggle, "s"', "箭筒伤害% 必须走 Common 求值"),
        ("MagicBody = 3140006", "魔法箭判定体必须是 3140006"),
        ('SkillBook.Common(MagicBody, "damage"', "魔法箭伤害必须读 3140006"),
        ("NativeQuiverBurst.Load(", "箭镞触发后必须装填追加箭筒"),
        ("NativeQuiverBurst.TryFire(", "命中必须尝试追加发射"),
        ("public static void Sync()", "箭镞模式必须能从存档恢复"),
        ("NativeArrowModeService.Set", "箭镞模式必须落库"),
    ):
        if need not in arrow:
            issues.append(f"NativeArrowMode 缺少 {why}（{need}）")

    # —— 存档链路：Schema 列 → DAO → 服务 → 角色刷新点 ——
    schema = (ROOT / "Assets/Scripts/Server/Db/Schema.sql").read_text(encoding="utf-8")
    if "quiverMode" not in schema:
        issues.append("Schema.sql 没有 quiverMode 列（DbStore 只持久化声明过的列，落盘即丢）")
    dao = (ROOT / "Assets/Scripts/Server/Db/GrowthDao.cs").read_text(encoding="utf-8")
    if dao.count("quiverMode") < 2:
        issues.append("GrowthDao 没有读写 quiverMode")
    if not (ROOT / "Assets/Scripts/Server/World/NativeArrowModeService.cs").is_file():
        issues.append("缺少 NativeArrowModeService（箭镞模式没有落库入口）")
    session = (ROOT / "Assets/Scripts/Client/GameSession.cs").read_text(encoding="utf-8")
    if "NativeArrowMode.Sync()" not in session:
        issues.append("GameSession 换角色后没调 NativeArrowMode.Sync，箭镞模式不会跟着角色走")

    # 运行时读的是 Resources/Server/Db/Schema.txt（PlatformRes 先走 Resources.Load，
    # 离线 server-check 才落到 Scripts/Server/Db/Schema.sql）。两份一旦分叉，
    # 改 .sql 在真机上等于没改 —— quiverMode 会「落盘即丢」，正是这个坑。
    schema_txt = ROOT / "Assets/Resources/Server/Db/Schema.txt"
    schema_sql = ROOT / "Assets/Scripts/Server/Db/Schema.sql"
    if schema_txt.is_file() and schema_sql.is_file():
        if schema_txt.read_text(encoding="utf-8") != schema_sql.read_text(encoding="utf-8"):
            issues.append("Schema.txt 与 Schema.sql 不一致（运行时只读 .txt，改了 .sql 等于没改）")

    print(f"\nR3 箭镞切换: {'PASS' if not issues else 'FAIL'}")
    return issues


def check_runtime() -> list[str]:
    """读取点守卫：把硬写 effect/effect0 的老写法钉死，防止回退。"""
    issues = []
    vfx = (ROOT / "Assets/Scripts/NativeSkillVfx.cs").read_text(encoding="utf-8")
    visuals = (ROOT / "Assets/Scripts/SkillVisuals.cs").read_text(encoding="utf-8")
    held = (ROOT / "Assets/Scripts/SkillHeldInput.cs").read_text(encoding="utf-8")
    system = (ROOT / "Assets/Scripts/SkillSystem.cs").read_text(encoding="utf-8")
    if "static List<string> Layers(" not in vfx or "static bool Numbered(" not in vfx:
        issues.append("NativeSkillVfx 不再提供 Layers/Numbered 族枚举")
    if "RapidAttack(" not in vfx or "info/rapidAttack" not in vfx:
        issues.append("NativeSkillVfx 不再读源端 info/rapidAttack")
    if 'Layers(s.vfx,"effect")' not in visuals or 'Layers(s.vfx,"special")' not in visuals:
        issues.append("SkillVisuals 又退回写死的 effect/effect0 与 special")
    if 'Layers(s.vfx,"specialAffected")' not in visuals:
        issues.append("SkillVisuals 丢失 specialAffected 后续演出（神聖團結翅膀等）")
    if 'Layers(definition.vfx,"keydown")' not in held:
        issues.append("SkillHeldInput 不再枚举 keydown 族")
    if "NativeSkillVfx.RapidAttack" not in system:
        issues.append("SkillSystem 不再用 rapidAttack 判定连射")
    # 蓄力型是独立路径，不能退回连射（否则每 100ms 打一发）。
    if "StartHeld(s,boundHeldAction.Value,true)" not in system:
        issues.append("SkillSystem 不再区分蓄力与连射")
    if "heldCharging" not in held or "if(heldCharging)return" not in held:
        issues.append("SkillHeldInput 蓄力型又回到逐发发弹")
    # 新增的几类层各有一个读取点，缺一个就整类静默丢失。
    for needle, note in (
        ('PlaySegmentedLayer(s,branch', "effect_ple / frameEffect 三段层"),
        ('PlayCustomEffect(s,', "customEffect"),
        ('PlayFieldLoop(s,origin,facing)', "start / repeat / end 放置循环"),
        ('StartCoroutine(Afterimage(s,facing))', "afterimage 残影"),
        ('NativeSkillVfx.Parts(s.vfx,"finish")', "finish 收尾"),
        ('NativeSkillVfx.Parts(key,"hit2")', "hit2 第二段命中"),
        ('NativeSkillVfx.Icons(s.vfx,"number")', "number 连击数字"),
    ):
        if needle not in visuals:
            issues.append(f"SkillVisuals 不再接 {note}")
    for needle, note in (
        ("static List<NativeVfxClip> Icons(", "Icons 数字图集"),
        ("static void ReadIcons(", "number/icon 读取"),
        ("static List<string> SubBranches(", "frameEffect 子分支枚举"),
        ('branch == "number"', "number 不走动画帧路径"),
    ):
        if needle not in vfx:
            issues.append(f"NativeSkillVfx 不再提供 {note}")
    # 箭镞切换（3141005）：模式层、切换入口、命中触发、被动放行四条缺一不可。
    arrow = ROOT / "Assets/Scripts/NativeArrowMode.cs"
    book = (ROOT / "Assets/Scripts/SkillBook.cs").read_text(encoding="utf-8")
    arrow_text = arrow.read_text(encoding="utf-8") if arrow.is_file() else ""
    if not arrow_text:
        issues.append("缺少 NativeArrowMode（魔幻箭筒VI 箭镞切换）")
    else:
        if '"mode/" + (mode == Magic ? 0 : 1)' not in arrow_text:
            issues.append("NativeArrowMode 不再播 mode/0|1 切换特效")
        if "const int Toggle = 3141005" not in arrow_text or "const int MagicShotFx = 3140010" not in arrow_text:
            issues.append("NativeArrowMode 的 3141005/3140010 绑定被改动")
        if "NativeArrowMode.OnHit(p, targets[0]" not in system:
            issues.append("SkillSystem.HitAt 不再触发箭镞")
    if "NativeArrowMode.IsToggle(skillId)" not in system:
        issues.append("SkillSystem.TryCastSkill 不再分流箭镞切换")
    if "NativeArrowMode.IsToggle(skillId)" not in book:
        issues.append("SkillCast 不再放行被动形态的箭镞切换技")
    print(f"\nR2 读取点: {'PASS' if not issues else 'FAIL'}")
    return issues


def finish(problems: list[str]) -> int:
    if problems:
        print(f"\n✗ {len(problems)} 个问题：")
        for item in problems:
            print("   -", item)
        return 1
    print("\nOK — 引用完整、特效分支全覆盖（缺口均在白名单内）")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
