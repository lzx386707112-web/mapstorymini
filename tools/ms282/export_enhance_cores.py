#!/usr/bin/env python3
"""导出「五/六转强化核心 → 被强化技能」的加成表。

282 的强化核心（VMatrix.json 里 type=1，共 306 个）本身不施放任何技能，
它连的是 `400004xxx` 包装技能：包装技能 `info/type=50`、`common` 只有四个加成字段，
真正的目标写在 `psdSkill` 里：

    400004008 狂暴攻擊強化  psdSkill = 1121008 / 1120017 / 1141000 / 1141001
                common = damR_5th=2*x, targetPlus_5th=log20(x),
                         ignoreMobpdpR=log40(x)*20, bdR=log70(x)*20

也就是说「装上强化核心」= 给 psdSkill 里那些既有技能加
最终伤害%、目标数、无视防御%、Boss 伤害%。项目此前把强化核心判成
「只提升伤害、不能绑快捷栏」，但**这条提升从未结算**（代码里搜不到 damR_5th），
所以 306 个核心装了等于没装。

另外还有**第二类同样结构的包装技能**：六转 HEXA 强化核心连的 `500004xxx`
（`info/type=50`、`psdSkill` 同上、`common` 只有 `damR_6th`，全库 65 条，覆盖 138 个技能）：

    500004006 強化聖十字架  psdSkill = 400011072
                common = damR_6th = 10+x+5*log10(x)+5*log20(x)+10*log30(x)   (lv1=11 .. lv30=60)

两者的差别只有「等级从哪读」：V 强化核心读 `VMatrixLevels`（面板快照 / 服务端
vmatrix_cores），HEXA 强化读 `VMatrixLevels` 的 hexa 段（HexaMatrix 快照 +
`NativeMatrixSkills.Sync` 写进 skills 表的 500004xxx）。所以统一放一张表，用 `s`
字段区分等级来源。

产物：Assets/Resources/NativeSkillEnhance.json
    cores/<coreId> = {w, n, max, t[], s(0=V强化/1=HEXA), lv/[[damR,targetPlus,ignorePdp,boss],...]}
    bySkill/<skillId> = [coreId...]
运行时由 Assets/Scripts/NativeSkillEnhance.cs 按包装技能的等级求和。

用法：python3 tools/ms282/export_enhance_cores.py
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))          # MapleDash/
REPO = os.path.dirname(ROOT)                            # mxd-android/
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.expanduser("~/Documents/mxd/BeiDou-Server/tool/wz-python"))

from export_advanced_skills import eval_formula, level_rows  # noqa: E402

from wzpy import WzImage, WzKey  # noqa: E402

DATA = os.path.join(REPO, "MapleDashResources", "Data", "Skill")
MATRIX = os.path.join(ROOT, "Assets", "Resources", "UI", "VMatrix.json")
OUT = os.path.join(ROOT, "Assets", "Resources", "NativeSkillEnhance.json")

# 每级数组的列顺序，运行时 NativeSkillEnhance 按同一顺序读
LEVEL_KEYS = ("damR_5th", "targetPlus_5th", "ignoreMobpdpR", "bdR")
# HEXA 强化只有最终伤害一列，其余三列固定 0（列序不变，运行时不用分支）
HEXA_KEYS = ("damR_6th",)

_cache = {}


def all_skills():
    """遍历 Skill/*.img 的全部技能节点（id 段不限于 4xxxx）。"""
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


def level_array(fields, keys, max_level):
    """按 1..maxLevel 展开成 [[...], ...]；缺字段写 0（表里不出现空值）。"""
    levels = []
    for level in range(1, max_level + 1):
        row = []
        for raw in keys:
            formula = fields.get(raw)
            if formula is None:
                row.append(0)
                continue
            value = eval_formula(str(formula), level)
            row.append(int(round(value)) if value is not None else 0)
        row += [0] * (len(LEVEL_KEYS) - len(row))
        levels.append(row)
    return levels


def skill_node(skill_id):
    """按 `Skill/<img>.img/skill/<id>` 取节点。项目树是商业端的 GMS 等长 XOR 版，字段一致。"""
    image = skill_id // 10000
    path = os.path.join(DATA, "%03d.img" % image)
    if image not in _cache:
        img = WzImage.from_file(path, key=WzKey.for_region("GMS"), name="%03d.img" % image)
        img.parse()
        _cache[image] = img.root.get("skill")
    root = _cache[image]
    return root.get(str(skill_id)) if root is not None else None


def truthy(node):  # noqa: ARG001
    return node is not None


def main():
    matrix = json.load(open(MATRIX, encoding="utf-8"))
    cores = matrix["cores"]
    out_cores, by_skill, problems = {}, {}, []
    skill_names = {}
    skills_path = os.path.join(ROOT, "Assets", "Resources", "UI", "VMatrixSkills.json")
    if os.path.exists(skills_path):
        for key, rec in json.load(open(skills_path, encoding="utf-8")).get("skills", {}).items():
            skill_names[key] = rec.get("name") or ""

    # —— 第一类：V 强化核心（VMatrix.json type=1）→ 400004xxx 包装技能 ——
    for core_id, core in sorted(cores.items(), key=lambda kv: int(kv[0])):
        if core.get("type") != 1:
            continue
        skills = core.get("skills") or []
        if not skills:
            problems.append("%s 强化核心没有连接技能" % core_id)
            continue
        wrapper = int(skills[0])
        node = skill_node(wrapper)
        if node is None:
            problems.append("%s 的包装技能 %d 在项目 IMG 里没有节点" % (core_id, wrapper))
            continue
        psd = node.get("psdSkill")
        targets = [int(child.name) for child in psd.children() if child.name.isdigit()] if psd is not None else []
        if not targets:
            problems.append("%s 包装技能 %d 的 psdSkill 为空" % (core_id, wrapper))
            continue
        common = node.get("common")
        # level_rows 返回 (maxLevel, {field: 公式/字面量})
        common_max, fields = level_rows(common) if common is not None else (0, {})
        max_level = int(core.get("maxLevel") or 0) or int(common_max or 0)
        if max_level <= 0:
            problems.append("%s 包装技能 %d 没有 maxLevel" % (core_id, wrapper))
            continue

        # 每级压成一条 [damR, targetPlus, ignorePdp, boss]：306 核 x 60 级写成对象会到 1.7MB，
        # 既拖慢导入又没必要。数组顺序见 LEVEL_KEYS。
        out_cores[core_id] = {
            "w": wrapper,
            "n": core.get("name") or "",
            "max": max_level,
            "t": targets,
            "s": 0,
            "lv": level_array(fields, LEVEL_KEYS, max_level),
        }
        for target in targets:
            by_skill.setdefault(str(target), []).append(int(core_id))

    # —— 第二类：HEXA 强化（500004xxx 包装技能，common 只有 damR_6th）——
    hexa = 0
    for wrapper, node in all_skills():
        common = node.get("common")
        if common is None or common.get("damR_6th") is None:
            continue
        info = node.get("info")
        typ = None
        if info is not None and info.get("type") is not None:
            typ = getattr(info.get("type"), "value", None)
        if typ not in (50, "50"):
            problems.append("%d 带 damR_6th 但 info/type=%s≠50" % (wrapper, typ))
            continue
        psd = node.get("psdSkill")
        targets = [int(child.name) for child in psd.children() if child.name.isdigit()] if psd is not None else []
        if not targets:
            problems.append("%d 的 psdSkill 为空" % wrapper)
            continue
        common_max, fields = level_rows(common)
        max_level = int(common_max or 0)
        if max_level <= 0:
            problems.append("%d 没有 maxLevel" % wrapper)
            continue
        out_cores[str(wrapper)] = {
            "w": wrapper,
            "n": skill_names.get(str(wrapper), ""),
            "max": max_level,
            "t": targets,
            "s": 1,
            "lv": level_array(fields, HEXA_KEYS, max_level),
        }
        for target in targets:
            by_skill.setdefault(str(target), []).append(wrapper)
        hexa += 1

    payload = {
        "source": {
            "matrix": "Assets/Resources/UI/VMatrix.json (type=1 强化核心)",
            "nodes": "Skill/<img>.img/skill/<wrapper>/psdSkill + common{damR_5th,targetPlus_5th,ignoreMobpdpR,bdR}",
            "hexa": "Skill/50000.img + 50001..50005.img 的 damR_6th 包装技能（info/type=50）",
            "note": "s=0 等级取 VMatrixLevels；s=1 等级取 HEXA 核心等级（同样镜像进 VMatrixLevels）",
        },
        "cores": out_cores,
        "bySkill": by_skill,
    }
    with open(OUT, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, ensure_ascii=False, indent=1)

    covered = len(by_skill)
    v_count = len(out_cores) - hexa
    with_damage = sum(1 for spec in out_cores.values() if spec["lv"][0][0])
    print("强化核心 %d 个（V %d + HEXA %d），覆盖被强化技能 %d 个，%d 个核心带最终伤害"
          % (len(out_cores), v_count, hexa, covered, with_damage))
    for core_id, spec in out_cores.items():
        if spec["w"] == 400004008:
            print("样例 %s %s → %s  Lv30=%s  Lv60=%s"
                  % (core_id, spec["n"], spec["t"], spec["lv"][29], spec["lv"][59]))
            break
    for core_id, spec in out_cores.items():
        if spec["w"] == 500004006:
            print("样例 %s %s → %s  Lv1=%s  Lv30=%s"
                  % (core_id, spec["n"], spec["t"], spec["lv"][0], spec["lv"][29]))
            break
    if problems:
        print("问题 %d 条：" % len(problems))
        for line in problems[:20]:
            print("  " + line)
    print("写入 " + os.path.relpath(OUT, REPO))
    return 0 if not problems else 1


if __name__ == "__main__":
    raise SystemExit(main())
