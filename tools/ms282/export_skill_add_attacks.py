#!/usr/bin/env python3
"""导出「追加攻击」技能链（`addAttack`）。

282 里有的技能放一次会再自动补一段攻击 —— 这一段是**独立技能节点**
（`invisible=1`，不在技能窗里），由主技能的 `addAttack` 指过来：

    14141000 五连投掷VI  common{bulletCount=4, damage=276+4*x}
      addAttack{ skill=14141001, isAuto=1 }
    14141001 五连投掷VI  invisible=1  common{damage=1104+16*x, ball...}

主技能只有 4 发、1104% 那一发就是 `addAttack` 指的隐藏技能。项目此前完全没读
`addAttack`（C# 里搜不到），所以这两个技能**少一整段伤害和特效**（五连投掷只剩四连）。

`addAttack` 有两种写法，本表只接**语义确定**的那种：

| 写法 | 语义 | 本表 |
|---|---|---|
| `{skill, isAuto=1}` | 主技能结算完自动补这一段 | ✅ `auto: 1` |
| `{skill, reqCount, pattern, skipActionFrame, skillPlus{...}}` | 按「第 N 段攻击 / 指定前置技能」触发的取消链 | ⛔ 记进 `deferred`，逐条逆向后再接 |

产物：Assets/Resources/NativeSkillAddAttacks.json
    skills/<skillId> = {next, auto, delay}
    deferred/<skillId> = 原因

用法：python3 tools/ms282/export_skill_add_attacks.py
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
OUT = os.path.join(ROOT, "Assets", "Resources", "NativeSkillAddAttacks.json")


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


def main():
    skills, deferred, problems = {}, {}, []
    invisible = {}
    images = 0
    for path in sorted(glob.glob(os.path.join(DATA, "*.img"))):
        name = os.path.basename(path)
        try:
            img = WzImage.from_file(path, key=WzKey.for_region("GMS"), name=name)
            img.parse()
        except Exception as error:                       # noqa: BLE001
            problems.append("%s 读取失败：%s" % (name, error))
            continue
        images += 1
        root = img.root.get("skill")
        if root is None:
            continue
        for child in root.children():
            if not child.name.isdigit():
                continue
            invisible[child.name] = int_of(child, "invisible") == 1
            hosts = [(child, "skill")]
            level = child.get("level")
            if level is not None:
                for row in kids(level):
                    hosts.append((row, "level/" + row.name))
            for host, where in hosts:
                node = host.get("addAttack")
                if node is None:
                    continue
                target = int_of(node, "skill")
                if target <= 0:
                    problems.append("%s/%s 的 addAttack 没有 skill 字段" % (child.name, where))
                    continue
                if target == int(child.name):
                    problems.append("%s/%s 的 addAttack 指向自己" % (child.name, where))
                    continue
                if int_of(node, "isAuto") == 1:
                    entry = {
                        "next": target,
                        "auto": 1,
                        "delay": int_of(node, "delay"),
                    }
                    # 同一技能在 skill 与 level 上各写一份时，以 skill 级为准（先写入者）。
                    skills.setdefault(child.name, entry)
                else:
                    keys = ",".join(k.name for k in kids(node))
                    deferred[child.name] = {
                        "next": target,
                        "why": "按段数/前置技能触发的取消链（%s），语义未逆向" % (keys or "无附加字段"),
                    }

    # 自动追加的那一段必须是隐藏技能：否则它自己也能被按出来，会出现「按一次打两遍」。
    for skill_id, entry in skills.items():
        target = str(entry["next"])
        if target not in invisible:
            problems.append("%s 的 addAttack 指向不存在的技能 %s" % (skill_id, entry["next"]))
            continue
        if not invisible[target]:
            problems.append("%s 的追加技能 %s 不是 invisible（会被技能窗按到）" % (skill_id, entry["next"]))

    payload = {
        "source": {
            "nodes": "Skill/<img>.img/skill/<id>[/level/<n>]/addAttack",
            "note": "isAuto=1 ⇒ 主技能结算后自动补一段；其余写法是取消链，未接",
        },
        "skills": skills,
        "deferred": deferred,
    }
    with open(OUT, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, ensure_ascii=False, indent=1)

    print("扫描 %d 个 IMG：自动追加 %d 条，取消链待逆向 %d 条"
          % (images, len(skills), len(deferred)))
    for skill_id, entry in sorted(skills.items()):
        print("  %s → %s（delay=%dms）" % (skill_id, entry["next"], entry["delay"]))
    if deferred:
        print("  待逆向：" + "、".join(sorted(deferred)))
    if problems:
        print("问题 %d 条：" % len(problems))
        for line in problems[:10]:
            print("  " + line)
    print("写入 " + os.path.relpath(OUT, REPO))
    return 0 if not problems else 1


if __name__ == "__main__":
    raise SystemExit(main())
