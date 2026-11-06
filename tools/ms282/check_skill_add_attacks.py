#!/usr/bin/env python3
"""自动追加攻击（`addAttack`）表守卫。

校验：
  1. 表里的自动链集合 = 全库 `addAttack{isAuto=1}` 的技能（含 `level/<n>` 下的写法）；
  2. 每条 `next` 指向的是真实存在且 `invisible=1` 的隐藏技能（否则技能窗能按出来，按一次打两遍）；
  3. 非 `isAuto` 的写法全部登记在 `deferred`（不允许静默漏掉）；
  4. C# 侧接线：`PlayAddAttack` 只在 `BeginCast` 调一次（覆盖 replay / buff / 召唤 /
     弹道 / 形态技能所有分支），`Cast`/`ProjectileCast` 末尾**不得**重复调用。

用法：python3 tools/ms282/check_skill_add_attacks.py
"""
import glob
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
REPO = os.path.dirname(ROOT)
sys.path.insert(0, os.path.expanduser("~/Documents/mxd/BeiDou-Server/tool/wz-python"))

from wzpy import WzImage, WzKey  # noqa: E402

DATA = os.path.join(REPO, "MapleDashResources", "Data", "Skill")
TABLE = os.path.join(ROOT, "Assets", "Resources", "NativeSkillAddAttacks.json")


def kids(node):
    try:
        return list(node.children())
    except TypeError:
        return []


def int_of(node, name, default=0):
    child = node.get(name) if node is not None else None
    try:
        return int(child.value) if child is not None and child.value is not None else default
    except (TypeError, ValueError):
        return default


def scan():
    auto, manual, invisible = {}, set(), {}
    for path in sorted(glob.glob(os.path.join(DATA, "*.img"))):
        name = os.path.basename(path)
        img = WzImage.from_file(path, key=WzKey.for_region("GMS"), name=name)
        img.parse()
        root = img.root.get("skill")
        if root is None:
            continue
        for child in root.children():
            if not child.name.isdigit():
                continue
            invisible[child.name] = int_of(child, "invisible") == 1
            hosts = [child]
            level = child.get("level")
            if level is not None:
                hosts.extend(kids(level))
            for host in hosts:
                node = host.get("addAttack")
                if node is None:
                    continue
                target = int_of(node, "skill")
                if target <= 0 or target == int(child.name):
                    continue
                if int_of(node, "isAuto") == 1:
                    auto.setdefault(child.name, target)
                else:
                    manual.add(child.name)
    return auto, manual, invisible


def main():
    table = json.load(open(TABLE, encoding="utf-8"))
    skills, deferred = table["skills"], table.get("deferred", {})
    auto, manual, invisible = scan()
    problems = []

    missing = sorted(set(auto) - set(skills), key=int)
    extra = sorted(set(skills) - set(auto), key=int)
    if missing:
        problems.append("表里缺 %d 条：%s" % (len(missing), missing[:6]))
    if extra:
        problems.append("表里多出 %d 条端上没有的：%s" % (len(extra), extra[:6]))
    for skill_id, next_id in auto.items():
        row = skills.get(skill_id)
        if row and int(row["next"]) != next_id:
            problems.append("%s 指向 %s，端上是 %s" % (skill_id, row["next"], next_id))
        if str(next_id) not in invisible:
            problems.append("%s 的追加技能 %s 在 IMG 里不存在" % (skill_id, next_id))
        elif not invisible[str(next_id)]:
            problems.append("%s 的追加技能 %s 不是 invisible" % (skill_id, next_id))
    unregistered = sorted(manual - set(deferred), key=int)
    if unregistered:
        problems.append("取消链没登记：%s" % unregistered[:8])

    system = open(os.path.join(ROOT, "Assets", "Scripts", "SkillSystem.cs"), encoding="utf-8").read()
    visuals = open(os.path.join(ROOT, "Assets", "Scripts", "SkillVisuals.cs"), encoding="utf-8").read()
    runtime = os.path.join(ROOT, "Assets", "Scripts", "NativeSkillAddAttacks.cs")
    if not os.path.exists(runtime):
        problems.append("缺 Assets/Scripts/NativeSkillAddAttacks.cs")
    else:
        body = open(runtime, encoding="utf-8").read()
        if "BeginCast(def, true)" not in body:
            problems.append("追加段没有用 BeginCast(def, true)（会重复扣 MP/冷却）")
        if "SkillProgress.LevelOf(link.Next)" not in body:
            problems.append("追加段没有继承主技能等级（隐藏技能 LevelOf 恒 0）")
        if "AddAttacks.TryGetValue(link.Next, out var nextLink)" not in body:
            problems.append("链没有显式接续：追加段走 continuousShot 捷径会提前 return，三段链会断在第二段")
    if system.count("PlayAddAttack(s);") != 1:
        problems.append("BeginCast 里必须且只能调一次 PlayAddAttack（漏一次会丢分支，多一次会重复补段）")
    if ("bool replay = BeginReplay" not in system or
            system.index("PlayAddAttack(s);") < system.index("bool replay = BeginReplay")):
        problems.append("PlayAddAttack 必须紧跟 BeginReplay（后面的分支都会提前 return）")
    if "PlayAddAttack" in visuals:
        problems.append("ProjectileCast/Cast 里重复调 PlayAddAttack（一次施法会补两段）")

    print("追加攻击：自动 %d 条 / 取消链待逆向 %d 条（表 %d / 端 %d）"
          % (len(auto), len(manual), len(skills), len(auto)))
    if problems:
        print("不一致 %d 条：" % len(problems))
        for line in problems[:15]:
            print("  " + line)
        return 1
    print("✓ 表与商业端一致，运行时接线齐全")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
