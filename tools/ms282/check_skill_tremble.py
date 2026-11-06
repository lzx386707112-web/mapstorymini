#!/usr/bin/env python3
"""技能震屏（`trembleEffect`）表守卫。

校验：
  1. 表里的技能集合 = 全库带 `trembleEffect` 或 `trembleEffectList` 的技能；
  2. 每条 wave 的 `force` / `time` / `delay` 与 IMG 一致（含多段的段数）；
  3. C# 侧接线：`PlayTremble(s)` 在 `Cast` 里、`HitFeel.Shake` 被调用、
     振幅按 `force / Art.PPU` 换算（和怪物震屏 `TrembleAmplitude` 同一口径）。

用法：python3 tools/ms282/check_skill_tremble.py
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
TABLE = os.path.join(ROOT, "Assets", "Resources", "NativeSkillTremble.json")


def value(node):
    try:
        v = node.value
    except Exception:
        return None
    return list(v) if isinstance(v, tuple) else v


def as_int(node):
    try:
        return int(str(value(node)).strip())
    except (TypeError, ValueError):
        return 0


def main():
    table = json.load(open(TABLE, encoding="utf-8"))["skills"]
    problems = []
    truth = {}
    for path in sorted(glob.glob(os.path.join(DATA, "*.img"))):
        name = os.path.basename(path)
        try:
            img = WzImage.from_file(path, key=WzKey.for_region("GMS"), name=name)
            img.parse()
        except Exception as error:                        # noqa: BLE001
            problems.append("%s 读取失败：%s" % (name, error))
            continue
        root = img.root.get("skill")
        if root is None:
            continue
        for child in root.children():
            if not child.name.isdigit():
                continue
            waves = []
            single = child.get("trembleEffect")
            if single is not None:
                waves.append(single)
            listed = child.get("trembleEffectList")
            if listed is not None:
                waves.extend(list(listed.children()))
            rows = [{"force": as_int(n.get("force")), "time": as_int(n.get("time")),
                     "delay": as_int(n.get("delay"))} for n in waves]
            rows = [r for r in rows if r["force"] > 0 and r["time"] > 0]
            if rows:
                rows.sort(key=lambda r: r["delay"])
                truth[child.name] = rows

    missing = sorted(set(truth) - set(table), key=int)
    extra = sorted(set(table) - set(truth), key=int)
    if missing:
        problems.append("表里缺 %d 条：%s" % (len(missing), missing[:6]))
    if extra:
        problems.append("表里多出 %d 条：%s" % (len(extra), extra[:6]))

    multi = 0
    for skill_id, row in table.items():
        want = truth.get(skill_id)
        if want is None:
            continue
        got = row.get("waves") or []
        if len(got) != len(want):
            problems.append("%s 段数：表=%d 端=%d" % (skill_id, len(got), len(want)))
            continue
        if len(want) > 1:
            multi += 1
        for index, wave in enumerate(want):
            for key in ("force", "time", "delay"):
                if int(got[index].get(key) or 0) != wave[key]:
                    problems.append("%s 第 %d 段 %s：表=%s 端=%s"
                                    % (skill_id, index + 1, key, got[index].get(key), wave[key]))

    system = open(os.path.join(ROOT, "Assets", "Scripts", "SkillSystem.cs"), encoding="utf-8").read()
    if "PlayTremble(s);" not in system:
        problems.append("SkillSystem.Cast 没有调 PlayTremble（技能震屏不会播）")
    runtime = os.path.join(ROOT, "Assets", "Scripts", "NativeSkillTremble.cs")
    if not os.path.exists(runtime):
        problems.append("缺 Assets/Scripts/NativeSkillTremble.cs")
    else:
        body = open(runtime, encoding="utf-8").read()
        if "HitFeel.Shake(" not in body:
            problems.append("NativeSkillTremble 没有调用 HitFeel.Shake")
        if "wave.Force / Art.PPU" not in body:
            problems.append("振幅没有按 force / Art.PPU 换算（与怪物震屏口径不一致）")
        if "foreach (var wave in spec.Waves) StartCoroutine(TrembleAfter(wave));" not in body:
            problems.append("多段震屏没有逐段起协程（只播第一段）")

    print("震屏技能：表 %d 条 / 端 %d 条（多段 %d 个）" % (len(table), len(truth), multi))
    forces = sorted({int(w.get("force") or 0) for r in table.values() for w in (r.get("waves") or [])})
    print("force 分布：%s" % forces)
    if problems:
        print("不一致 %d 条：" % len(problems))
        for line in problems[:15]:
            print("  " + line)
        return 1
    print("✓ 表与商业端一致，运行时接线齐全")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
