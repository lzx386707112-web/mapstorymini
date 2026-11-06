#!/usr/bin/env python3
"""强化核心加成表守卫（V 强化核心 + HEXA 强化两类）。

校验四件事：
  1. 表里的核心集合正好等于「VMatrix type=1」∪「全库带 damR_6th 的包装技能」；
  2. 每条表项的 wrapper / targets / levels 与商业端包装技能节点一致
     （`psdSkill` 名单、`common{damR_5th,targetPlus_5th,ignoreMobpdpR,bdR}` 或 `damR_6th` 逐级展开）；
  3. C# 侧确实接了线（`NativeSkillEnhance.Bonus` 在 CombatRules 里被调用，
     目标数在 SkillSystem 的 HitAt 里被加上）；
  4. HEXA 等级链路存在：`VMatrixLevels.CaptureHexa` 被 HEXA 窗口调用，
     且运行时取等级不走 `SkillProgress.LevelOf`（包装技能不在 JobTree 里，会恒 0）。

用法：python3 tools/ms282/check_enhance_cores.py
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
REPO = os.path.dirname(ROOT)
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.expanduser("~/Documents/mxd/BeiDou-Server/tool/wz-python"))

from export_advanced_skills import eval_formula, level_rows  # noqa: E402
from wzpy import WzImage, WzKey  # noqa: E402

DATA = os.path.join(REPO, "MapleDashResources", "Data", "Skill")
MATRIX = os.path.join(ROOT, "Assets", "Resources", "UI", "VMatrix.json")
TABLE = os.path.join(ROOT, "Assets", "Resources", "NativeSkillEnhance.json")

_cache = {}


def skill_node(skill_id):
    image = skill_id // 10000
    if image not in _cache:
        path = os.path.join(DATA, "%03d.img" % image)
        img = WzImage.from_file(path, key=WzKey.for_region("GMS"), name="%03d.img" % image)
        img.parse()
        _cache[image] = img.root.get("skill")
    root = _cache[image]
    return root.get(str(skill_id)) if root is not None else None


def all_skills():
    import glob
    for path in sorted(glob.glob(os.path.join(DATA, "*.img"))):
        name = os.path.basename(path)
        try:
            image = int(name[:-4])
        except ValueError:
            continue
        if image not in _cache:
            img = WzImage.from_file(path, key=WzKey.for_region("GMS"), name=name)
            img.parse()
            _cache[image] = img.root.get("skill")
        root = _cache[image]
        if root is None:
            continue
        for child in root.children():
            if child.name.isdigit():
                yield int(child.name), child


def main():
    matrix = json.load(open(MATRIX, encoding="utf-8"))["cores"]
    table = json.load(open(TABLE, encoding="utf-8"))
    cores, by_skill = table["cores"], table["bySkill"]
    problems = []

    want = {cid for cid, c in matrix.items() if c.get("type") == 1}
    hexa = set()
    for wrapper, node in all_skills():
        common = node.get("common")
        if common is not None and common.get("damR_6th") is not None:
            hexa.add(str(wrapper))
    missing = sorted(want - set(cores), key=int)
    missing_hexa = sorted(hexa - set(cores), key=int)
    extra = sorted(set(cores) - want - hexa, key=int)
    if missing:
        problems.append("表里缺 %d 个 V 强化核心：%s" % (len(missing), missing[:5]))
    if missing_hexa:
        problems.append("表里缺 %d 个 HEXA 强化：%s" % (len(missing_hexa), missing_hexa[:5]))
    if extra:
        problems.append("表里多出 %d 个来路不明的核心：%s" % (len(extra), extra[:5]))

    checked = 0
    V_KEYS = ("damR_5th", "targetPlus_5th", "ignoreMobpdpR", "bdR")  # = 数组列顺序
    H_KEYS = ("damR_6th",)
    for core_id, spec in cores.items():
        keys = H_KEYS if int(spec.get("s") or 0) == 1 else V_KEYS
        if int(spec.get("s") or 0) == 1 and core_id not in hexa:
            problems.append("%s 标成 HEXA 但端上没有 damR_6th" % core_id)
        if int(spec.get("s") or 0) == 0 and core_id not in want:
            problems.append("%s 标成 V 强化但 VMatrix 里不是 type=1" % core_id)
        wrapper = spec["w"]
        node = skill_node(wrapper)
        if node is None:
            problems.append("%s 的包装技能 %d 没有节点" % (core_id, wrapper))
            continue
        psd = node.get("psdSkill")
        truth = sorted(int(c.name) for c in psd.children() if c.name.isdigit()) if psd is not None else []
        if sorted(spec["t"]) != truth:
            problems.append("%s 的 psdSkill 名单不一致：表=%s 端=%s" % (core_id, spec["t"], truth))
        common = node.get("common")
        max_level, fields = level_rows(common) if common is not None else (0, {})
        levels = spec["lv"]
        if len(levels) != spec["max"]:
            problems.append("%s 等级数组长度 %d ≠ max %d" % (core_id, len(levels), spec["max"]))
        for level in sorted({1, max_level, spec["max"]}):
            if level < 1 or level > len(levels):
                problems.append("%s 缺 %s 级" % (core_id, level))
                continue
            row = levels[level - 1]
            # HEXA 只有 damR_6th 一列，其余三列必须是 0（数组列序不变，运行时不用分支）
            expect = list(keys) + [None] * (len(V_KEYS) - len(keys))
            for column, raw in enumerate(expect):
                formula = None if raw is None else fields.get(raw)
                expected = 0 if formula is None else int(round(eval_formula(str(formula), level) or 0))
                if row[column] != expected:
                    problems.append("%s %s 级 %s：表=%s 端=%s"
                                    % (core_id, level, raw or "（必须为 0）", row[column], expected))
        checked += 1
        # bySkill 反向索引必须包含每条目标
        for target in spec["t"]:
            if int(core_id) not in by_skill.get(str(target), []):
                problems.append("bySkill[%d] 缺 %s" % (target, core_id))

    # C# 接线
    combat = open(os.path.join(ROOT, "Assets", "Scripts", "CombatRules.cs"), encoding="utf-8").read()
    system = open(os.path.join(ROOT, "Assets", "Scripts", "SkillSystem.cs"), encoding="utf-8").read()
    runtime = os.path.join(ROOT, "Assets", "Scripts", "NativeSkillEnhance.cs")
    if not os.path.exists(runtime):
        problems.append("缺 Assets/Scripts/NativeSkillEnhance.cs")
    if "NativeSkillEnhance.Bonus(" not in combat:
        problems.append("CombatRules 没有调用 NativeSkillEnhance.Bonus")
    if "NativeSkillEnhance.TargetPlus(" not in system:
        problems.append("SkillSystem.HitAt 没有把 targetPlus 加进目标数")
    if "enhanceFinal" not in combat or "enhanceIgnore" not in combat:
        problems.append("CombatRules 没有把最终伤害/无视防御接进结算")
    if os.path.exists(runtime):
        body = open(runtime, encoding="utf-8").read()
        if 'Art.Id(core, "w")' not in body:
            problems.append("NativeSkillEnhance 取 wrapper 没有走 Art.Id（float 会把 400004008 舍成 400004000）")
        if "Art.Int(core, \"w\")" in body:
            problems.append("NativeSkillEnhance 用 Art.Int 取 wrapper（id 精度会丢）")
        # 只看真正的代码行：注释里出现这个字样是说明「为什么不用它」，不算违规
        code = [line for line in body.splitlines()
                if "SkillProgress.LevelOf" in line and not line.strip().startswith("//")]
        if code:
            problems.append("NativeSkillEnhance 用 SkillProgress.LevelOf 取包装技能等级"
                            "（它先过 SkillBook.Get，包装技能不在 JobTree 里 ⇒ 恒 0）")
    hexa_window = os.path.join(ROOT, "Assets", "Scripts", "NativeHexaMatrixWindow.cs")
    if not os.path.exists(hexa_window):
        problems.append("缺 Assets/Scripts/NativeHexaMatrixWindow.cs")
    elif "VMatrixLevels.CaptureHexa(" not in open(hexa_window, encoding="utf-8").read():
        problems.append("HEXA 窗口没有把核心等级镜像进 VMatrixLevels（damR_6th 会恒为 0）")
    skillbook = open(os.path.join(ROOT, "Assets", "Scripts", "SkillBook.cs"), encoding="utf-8").read()
    if "public static void CaptureHexa(" not in skillbook:
        problems.append("VMatrixLevels 缺 CaptureHexa")
    # V 与 HEXA 必须是两张表：Capture 是「整份重建」语义，共用一张表会互相清空
    if "matrixLevels" not in skillbook or "hexaLevels" not in skillbook:
        problems.append("VMatrixLevels 没有把 V / HEXA 分成两张表（Capture 会把对方清掉）")
    # 登录预热：不打开面板的玩家也必须有核心等级，否则加成永远 0
    session = open(os.path.join(ROOT, "Assets", "Scripts", "Client", "GameSession.cs"),
                   encoding="utf-8").read()
    if "RefreshMatrixLevels();" not in session:
        problems.append("GameSession.SelectChar 没有预热矩阵等级（不开面板就吃不到强化核心加成）")
    if "VMatrixLevels.CaptureHexa(hexa" not in session:
        problems.append("GameSession 预热没有灌 HEXA 等级")

    print("强化核心 %d 个（V %d + HEXA %d，端 %d + %d），逐条校验 %d 个，覆盖技能 %d 个"
          % (len(cores), len(want), len(hexa), len(want), len(hexa), checked, len(by_skill)))
    print("接线：CombatRules %s / SkillSystem %s"
          % ("✓" if "NativeSkillEnhance.Bonus(" in combat else "✗",
             "✓" if "NativeSkillEnhance.TargetPlus(" in system else "✗"))
    if problems:
        print("不一致 %d 条：" % len(problems))
        for line in problems[:20]:
            print("  " + line)
        return 1
    print("✓ 表与商业端一致，C# 接线齐全")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
