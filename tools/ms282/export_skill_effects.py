#!/usr/bin/env python3
"""导出技能「属性被动」与「时效增益」的字段语义表 → Assets/Resources/NativeSkillEffects.json

问题背景（2026-11）
    282 有一批技能把加成写在 `common` 的百分比字段上（indieDamR / indieIgnoreMobpdpR /
    basicStatUp / strX / mhpX …）。这些字段既不是 `pad`/`mad` 这类 NativeBuffs 认识的
    flat 属性，也没有任何 C# 消费方，于是：

      · `type=buff` 的技能按下后 `SkillStats` 返回空 ⇒ `ApplySkill` 直接 return false
        ⇒ 不生效、连 buff 图标都不显示（V/HEXA 里 49 条）；
      · 更严重的是 `[被动效果：…]` 那一段：它是**学会即永久生效**的属性，与持续时间无关，
        却跟着技能一起被当成 buff，所以从来没人加过（全库 65 条带这个括号）。

数据来源（只读）
    MapleStory/Data/String/Skill.img   —— 中文说明（BMS 密钥），被动段靠它自证
    Assets/Resources/UI/{VMatrixSkills,AdvancedSkills,JobTree}.json —— 技能 id 全集

判据（本项目铁律：字段语义看 h 不看字段名猜）
    `[被动效果：…]` 括号内的 `#字段` = 永久被动；括号外的是「#time 秒内」的时效增益。
    同一字段跨技能含义不同的（单字母 x/y/z/q/u/v/w）一律不猜，登记进 skipped 等人工。

落盘结构
    fields   —— 字段 → setItemBonus 键数组（唯一事实源；C# 只按这张表写 set，不再硬编码）
    passive  —— <id> → 该技能被动词段的字段名
    skipped  —— <id> → 跳过原因（机制说明 / 单字母 / 无消费方）

用法
    python3 tools/ms282/export_skill_effects.py            # 生成 + 校验
    python3 tools/ms282/export_skill_effects.py --check    # 只打印摘要，不写文件
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]          # MapleDash/
sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(ROOT.parent / "BeiDou-Server/tool/wz-python"))

from export_advanced_skills import (  # noqa: E402
    PACKS, eval_formula, simplify, strings, text_of,
)
from wzpy import WzImage, WzKey  # noqa: E402

BMS = WzKey.for_region("BMS")
OUT = ROOT / "Assets/Resources/NativeSkillEffects.json"
SKILL_TABLES = ("VMatrixSkills", "AdvancedSkills", "JobTree")

# 字段 → setItemBonus 键。键名就是 AdminSetItem.ApplyStat / ApplyDamage / ApplyRate 读的那几个，
# 所以注入后 EquipLoadout 的 (base+flat)×(100+pct)/100 与 CombatRules 的乘区自动生效，
# 不需要在伤害管线里为技能增益再开一条分支。
#
# 右边每个条目都在 h 里被写过（注释引原文），没有一条是按字段名猜的。
FIELDS: dict[str, list[str]] = {
    # —— 四维 / HP ——
    "strX":        ["STR"],           # 400001001「[被动效果：全属性增加#strX]」
    "dexX":        ["DEX"],           # 同上（同一组，四维各一份）
    "intX":        ["INT"],
    "lukX":        ["LUK"],
    "mhpX":        ["HP"],            # 400011066「最大HP增加#mhpX」（无 %，绝对值）
    "mhpR":        ["HPPct"],         # 1001003「最大HP增加#mhpR%」
    "basicStatUp": ["STRPct", "DEXPct", "INTPct", "LUKPct"],
    #                                ^ 枫叶祝福「直接投入AP的所有能力值增加#basicStatUp%」
    "indieStatRBasic": ["STRPct", "DEXPct", "INTPct", "LUKPct"],
    #                                ^ 海盗旗帜「AP直接投资的所有能力值 #indieStatRBasic%增加」
    # —— 攻魔 ——
    "padX":        ["PAD"],           # 400001007「攻击力、魔力 #padX增加」（无 %）
    "madX":        ["MAD"],
    "indiePad":    ["PAD"],           # 1141006「增加攻击力#indiePad」（无 %）
    "indiePadR":   ["PADPct"],
    "indieMadR":   ["MADPct"],        # 500001021「魔力#indieMadR%增加」
    # —— 防御 ——
    "pddR":        ["PDDPct"],        # 3111005「防御力增加#pddR%」
    # —— 伤害乘区（CombatRules / AdminSetItem 已经在读这些键）——
    "indieDamR":   ["Damage"],        # 400001042「伤害增加#indieDamR%」
    "nbdR":        ["NormalDamage"],  # 400041008「对一般怪物攻击时伤害#nbdR%增加」
    "indiePMdR":   ["FinalDamage"],   # 400011000「最终伤害值增加#indiePMdR%」
    "pdR":         ["FinalDamage"],   # 4221007「增加最终伤害#pdR%」
    "indieIgnoreMobpdpR": ["IgnoreDefense"],  # 400011000「无视怪物防御率 #…%」
    "ignoreMobpdpR":      ["IgnoreDefense"],  # 1240001「追加无视怪物防御率#…%」
    # —— 暴击（ApplySpecialCore 单独转成 float）——
    "indieCr":     ["CriticalChance"],# 1141006「爆击几率#indieCr%」
    "cr":          ["CriticalChance"],# 400031055「额外爆击几率#cr%」
    # —— 状态抗性 ——
    # 不参与 (base+flat)×(100+pct)/100 的四维结算：消费方是 Field/NativeMobDebuffs，
    # 施加异常状态时按百分比抵抗（原端 GetAsrR 的语义）。
    #   asrR      —— 括号内的永久被动（400001004 实用的战斗命令，1..6%）
    #   indieAsrR —— 括号外的时效增益（400011066 钢铁之躯「在#time秒内…提升状态异常抗性」）
    # 两者都落 setItemBonus / buff 的 ASR 键，NativeMobDebuffs 相加后判定。
    "asrR":        ["ASR"],
    "indieAsrR":   ["ASR"],
    # —— 倍率 ——
    "expR":        ["ExpRate"],       # 500001000「经验值获得量增加#expR%」
}

# 认得但本工程没有消费方：写进 JSON 只会变成没人读的死键，先登记不映射。
# 2026-11-05：asrR / indieAsrR 已接上 Field/NativeMobDebuffs 的抵抗判定，从本表移出。
UNCONSUMED: dict[str, str] = {}

# 单字母字段是全局表碰不得的地方：同一个 `x` 在 400001002 是爆击几率、在 400001003 是
# 最大HP%。所以这类只能**逐条**登记，而且每条都在 h 里写明了语义（下面逐行引原文）。
# 形状：技能 id → { 字段名: [setItemBonus 键…] }
SKILL_ALIAS: dict[int, dict[str, list[str]]] = {
    # 「经验值增加#x%，掉宝率增加#v%」
    400001020: {"x": ["ExpRate"], "v": ["DropRate"]},
    # 「#time秒内，伤害增加#q%」
    400001043: {"q": ["Damage"]},
    # 「#time秒内，最终伤害增加#u%」
    400011003: {"u": ["FinalDamage"]},
    # 「使用自身攻击技能时…最终伤害增加#z%」（开关技能）
    400021000: {"z": ["FinalDamage"]},
    # 「#time秒内在领域中提升自身最终伤害#w%」
    400021003: {"w": ["FinalDamage"]},
    # 「#time秒内武器纯攻击力增加#x%」（y 是持续时间结束后的副作用，不在此映射）
    400051033: {"x": ["PADPct"]},
    # 「直接投資AP，所有能力值增加#s%及无视怪物防御率增加#w%…攻击力增加#x」（x 无 %）
    500001036: {"s": ["STRPct", "DEXPct", "INTPct", "LUKPct"], "w": ["IgnoreDefense"], "x": ["PAD"]},
    500001037: {"s": ["STRPct", "DEXPct", "INTPct", "LUKPct"], "w": ["IgnoreDefense"], "x": ["PAD"]},
    # 11141501 是银河星爆(11141500)的子状态 id，自己没有 h；
    # 语义来自母技能 h「使用時#w2秒內最終傷害增加#q2%」，状态体的 y 与 q2 逐级相等
    # （lv1=5 → lv30=10）。状态由 NativeSkillStates 在母技能施放时挂上。
    11141501: {"y": ["FinalDamage"]},
}


GRAY = "c"        # #c 是颜色码，不是字段
PASSIVE_BR = re.compile(r"\[被动效果[：:]([^\]]*)\]", re.S)
MECHANICAL = re.compile(r"#(?:c)?被动效果#")     # 「#c被动效果#：…」是机制说明，不含属性字段
FIELD_REF = re.compile(r"#([A-Za-z][A-Za-z0-9_]*)")

# 同一个字段写在不同技能里覆盖面不同，h 靠字段名前的描述区分：
#   400001001「全属性增加#strX」      → 四维都加
#   400011066「力量增加#strX」        → 只加力量
#   400001007「攻击力、魔力 #padX增加」→ 攻魔都加
#   400041032「攻击力增加#padX」      → 只加攻击
# 所以不能把 strX 一刀切成四维，要取字段名前面那段文字做判据。
WIDE = {
    "strX": (("全属性", "所有能力"), ["STR", "DEX", "INT", "LUK"]),
    "padX": (("魔力",), ["PAD", "MAD"]),
}


def context_of(text: str, name: str) -> str:
    """取字段名引用前的一小段文字，用于判断这个字段覆盖几个属性。"""
    at = text.find("#" + name)
    return text[max(0, at - 12):at].replace("\n", "") if at >= 0 else ""


def expand(name: str, text: str) -> list[str]:
    keys = list(FIELDS.get(name, []))
    rule = WIDE.get(name)
    if rule is None:
        return keys
    keywords, wider = rule
    if any(word in context_of(text, name) for word in keywords):
        return wider
    return keys



def skill_ids() -> list[int]:
    ids: set[int] = set()
    for name in SKILL_TABLES:
        path = ROOT / f"Assets/Resources/UI/{name}.json"
        if not path.is_file():
            continue
        ids |= {int(k) for k in json.loads(path.read_text(encoding="utf-8"))["skills"]}
    return sorted(ids)


def scalar_fields(node) -> dict:
    """把 common 子树里的标量取出来（公式保持字符串）。"""
    out: dict = {}
    if node is None:
        return out
    for child in node.children():
        value = getattr(child, "value", None)
        if value is not None and not hasattr(value, "children"):
            out[child.name] = value
    return out


def pack_commons(wanted: set[int]) -> dict[int, dict]:
    """扫 282 packs，只留给 wanted 里技能 id 的 common 字段。

    不用 UI JSON 的 levels 行：被动词段大量写在 common 里（如 1121000 的
    basicStatUp = u(x/2)），而 JobTree 的 levels 是 level/N 直读，根本看不到这些字段。
    另外 packs 与 String.wz 同属 282 编号体系，h 才对得上号 —— JobTree 里有一部分
    四转技能是 GMS083 的编号，和 282 撞号（4221007 在 282 是「瞬步连击」、
    在 JobTree 是「一出双击」），拿 282 的 h 去判它们会判错技能。
    """
    result: dict[int, dict] = {}
    if not PACKS.is_dir():
        return result
    for path in sorted(PACKS.glob("Skill_*.img")):
        try:
            image = WzImage.from_bytes(path.read_bytes(), key=BMS, name=path.name)
        except Exception:
            continue
        root = image.get("skill")
        if root is None:
            continue
        for node in root.children():
            if not node.name.isdigit():
                continue
            sid = int(node.name)
            if sid in wanted:
                result[sid] = scalar_fields(node.get("common"))
    return result


def expand_values(levels: dict, common: dict, name: str, max_level: int) -> list[int]:
    """按 1..maxLevel 展开一个字段。

    优先级 level/N > common：282 里 level/N 是对 common 的逐级覆盖
    （400001001 的 strX 在 level/N 是 6、在 common 只剩个占位 1），
    只有 basicStatUp 这类完全写在 common 的公式字段才回退过去。
    """
    if any(name in (levels.get(str(level)) or {}) for level in range(1, max_level + 1)):
        return [int((levels.get(str(level)) or {}).get(name) or 0) for level in range(1, max_level + 1)]
    raw = common.get(name)
    if raw is None:
        return []
    values: list[int] = []
    for level in range(1, max_level + 1):
        try:
            values.append(int(eval_formula(raw, level)) if isinstance(raw, str) else int(raw))
        except Exception:
            values.append(0)
    return values



def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true", help="只打印摘要，不写文件")
    args = parser.parse_args()

    strings_ = strings()
    passive: dict[str, dict[str, list[int]]] = {}
    passive_fields: dict[str, list[str]] = {}
    skipped: dict[str, str] = {}
    seen_fields: set[str] = set()

    max_levels: dict[int, int] = {}
    ui_levels: dict[int, dict] = {}
    for name in SKILL_TABLES:
        path = ROOT / f"Assets/Resources/UI/{name}.json"
        if not path.is_file():
            continue
        for key, skill in json.loads(path.read_text(encoding="utf-8"))["skills"].items():
            sid = int(key)
            max_levels.setdefault(sid, int(skill.get("maxLevel") or 0))
            ui_levels.setdefault(sid, skill.get("levels") or {})

    # 第一遍：只读 h 文案，定位被动词段及其字段（不碰 WZ，快）。
    # 单字母字段是「同名字段跨技能含义不同」的重灾区（400001002 的 x=爆击几率、
    # 400001003 的 x=最大HP%），只能逐条人工核对，不进 FIELDS。
    wanted: dict[int, tuple[list[str], str]] = {}
    for sid in skill_ids():
        node = strings_.get(str(sid))
        h = simplify(text_of(node, "h")) if node else ""
        if not h or "被动效果" not in h:
            continue
        match = PASSIVE_BR.search(h)
        if match is None:
            # 「#c被动效果#：…」纯机制说明（铁锤/烙印/符咒/瞄准），没有属性字段可提。
            if MECHANICAL.search(h):
                skipped[str(sid)] = "机制说明（#c被动效果# 格式），无属性字段"
            else:
                skipped[str(sid)] = "括号未闭合，无法定位被动词段"
            continue
        body = match.group(1)
        wanted[sid] = ([n for n in FIELD_REF.findall(body) if n != GRAY], body)

    # 第二遍：按 282 packs 的 common 逐级展开数值（公式在导出期求值，运行时不再解析）。
    commons = pack_commons(set(wanted))
    for sid, (names, body) in sorted(wanted.items()):
        common = commons.get(sid)
        declared = max(int((common or {}).get("maxLevel") or 0), max_levels.get(sid, 0))
        usable: dict[str, list[int]] = {}
        dropped: list[str] = []
        for name in names:
            if name not in FIELDS:
                dropped.append(name)
                continue
            values = expand_values(ui_levels.get(sid, {}), common or {}, name, declared) if declared > 0 else []
            if not values or not any(values):
                dropped.append(f"{name}（common 无此字段）")
                continue
            for key in expand(name, body):
                usable[key] = values
            seen_fields.add(name)
        if usable:
            passive[str(sid)] = dict(sorted(usable.items()))
            # 字段名单独留一份：运行时用它把「永久被动」从「时效增益」里排除
            # （同一个字段可能两边都出现，只有被动侧的该被 AddPassive 收走）。
            passive_fields[str(sid)] = sorted({name for name in names if name in FIELDS})
        if dropped:
            reason = "、".join(f"{n}（{UNCONSUMED.get(n, '语义未自证')}）" for n in sorted(set(dropped)))
            skipped[str(sid)] = ("部分跳过：" + reason) if usable else ("全部跳过：" + reason)

    summary = {
        "被动技能": len(passive),
        "跳过技能": len(skipped),
        "覆盖字段": sorted(seen_fields),
    }
    print("== 技能效果字段表 ==")
    for key, value in summary.items():
        print(f"  {key}: {value}")
    print(f"  跳过明细 {len(skipped)} 条（详见 JSON.skipped）")

    if args.check:
        return 0

    payload = {
        "fields": FIELDS,
        "unconsumed": UNCONSUMED,
        "alias": {str(k): v for k, v in sorted(SKILL_ALIAS.items())},
        "passive": passive,
        "passiveFields": passive_fields,
        "skipped": skipped,
    }
    OUT.write_text(json.dumps(payload, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"  已写入 {OUT.relative_to(ROOT)}（{OUT.stat().st_size / 1024:.1f} KiB）")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
