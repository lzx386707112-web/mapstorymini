#!/usr/bin/env python3
"""导出技能震屏（`trembleEffect`）表。

282 里「施放时抖屏」有两种写法，**必须都读**：

    trembleEffect     = { force, time, delay?, reduction?, remote? }            单段（56 条）
    trembleEffectList = { 0: {...}, 1: {...}, ... }                             多段（37 条）
                        每段自带 delay/force/time/reduction，按 delay 依次起震

只读单段的话，37 条多段技能（暴风系列、连续打击）一次都不震。

| 字段 | 含义 |
|---|---|
| `force` | 震动强度（原端点阵振幅，实测 5~12） |
| `time` | 持续毫秒（390~900） |
| `delay` | 起播延迟毫秒（缺省 0） |
| `reduction` | 衰减系数（实测恒 1.0，逐帧线性衰减） |
| `remote` | 是否远处也震（0 = 只影响本地镜头，单机无差别） |

以前这 56 条**完全没接**：技能放出来只有特效和伤害，镜头一点不动，重击感全丢。
项目已有震屏通道 `HitFeel.Shake(amplitude, seconds)`（怪物 attack<N>/info/tremble 就在用），
所以这里只导出参数，运行时按 force 换算出世界单位振幅后调它。

产物：Assets/Resources/NativeSkillTremble.json
    skills/<skillId> = { waves: [{force,time,delay,reduction}, ...] }
    source.* = 出处

用法：python3 tools/ms282/export_skill_tremble.py
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
OUT = os.path.join(ROOT, "Assets", "Resources", "NativeSkillTremble.json")


def value(node):
    try:
        v = node.value
    except Exception:
        return None
    return list(v) if isinstance(v, tuple) else v


def as_int(node, fallback=0):
    v = value(node)
    try:
        return int(str(v).strip())
    except (TypeError, ValueError):
        return fallback


def wave_of(node):
    """把一段 trembleEffect / trembleEffectList/<n> 读成一条 wave；缺关键字段返回 None。"""
    force = as_int(node.get("force"))
    time_ms = as_int(node.get("time"))
    if force <= 0 or time_ms <= 0:
        return None
    return {
        "force": force,
        "time": time_ms,
        "delay": as_int(node.get("delay")),
        # reduction 是系数（实测 0.8~1.0），原样存，运行时按 <=1 当系数
        "reduction": value(node.get("reduction")) if node.get("reduction") is not None else 1.0,
    }


def main():
    skills, problems, images = {}, [], 0
    for path in sorted(glob.glob(os.path.join(DATA, "*.img"))):
        name = os.path.basename(path)
        try:
            img = WzImage.from_file(path, key=WzKey.for_region("GMS"), name=name)
            img.parse()
        except Exception as error:                      # noqa: BLE001
            problems.append("%s 读取失败：%s" % (name, error))
            continue
        images += 1
        root = img.root.get("skill")
        if root is None:
            continue
        for child in root.children():
            if not child.name.isdigit():
                continue
            waves = []
            single = child.get("trembleEffect")
            if single is not None:
                wave = wave_of(single)
                if wave is None:
                    problems.append("%s 的 trembleEffect 缺 force/time" % child.name)
                else:
                    waves.append(wave)
            listed = child.get("trembleEffectList")
            if listed is not None:
                for item in listed.children():
                    wave = wave_of(item)
                    if wave is None:
                        problems.append("%s 的 trembleEffectList/%s 缺 force/time"
                                        % (child.name, item.name))
                        continue
                    waves.append(wave)
            if not waves:
                continue
            # 按起播延迟排序：多段震屏是「先小后大」的节奏，乱序会全挤在同一帧。
            waves.sort(key=lambda row: row["delay"])
            skills[child.name] = {"waves": waves}

    payload = {
        "source": {
            "nodes": "Skill/<img>.img/skill/<id>/trembleEffect 或 trembleEffectList/<n>"
                     "，字段 {force,time,delay,reduction,remote}",
            "usage": "每段按自己的 delay 起震：HitFeel.Shake(force / Art.PPU, time / 1000)",
            "note": "force 是原端点阵振幅，换算口径与怪物震屏 TrembleAmplitude=30f/Art.PPU 一致",
        },
        "skills": skills,
    }
    with open(OUT, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, ensure_ascii=False, indent=1)

    forces = sorted({wave["force"] for row in skills.values() for wave in row["waves"]})
    multi = sum(1 for row in skills.values() if len(row["waves"]) > 1)
    print("扫描 %d 个 IMG，震屏技能 %d 个（多段 %d 个），force 取值 %s"
          % (images, len(skills), multi, forces))
    for skill_id, row in list(skills.items())[:5]:
        print("  %s  段数=%d  %s" % (skill_id, len(row["waves"]), row["waves"][:2]))
    if problems:
        print("问题 %d 条：" % len(problems))
        for line in problems[:10]:
            print("  " + line)
    print("写入 " + os.path.relpath(OUT, REPO))
    return 0 if not problems else 1


if __name__ == "__main__":
    raise SystemExit(main())
