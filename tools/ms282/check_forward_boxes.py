#!/usr/bin/env python3
"""正向判定框（`lt.x>=0 && rb.x>0`）守卫。

全库有框技能里绝大多数是「左朝向负框」：`InRange` 统一取 `-max.x..-min.x`
镜像到身前。但有一小撮技能把框写成正向（`lt.x=0, rb.x>0`），继续镜像会把它们
判到角色**身后**，表现是「特效播了、`[Hit] 选中=0`」。

本守卫：
  1. 从 IMG 重新扫出全库正框清单（不写死 id）；
  2. 校验运行时 `NativeSkillDefinition.ForwardBox` 的判据与两处 `InRange` 都接了它；
  3. 打印清单与条数，条数变化时报警（新增源端资源带来新正框时会被看见）。

用法：python3 tools/ms282/check_forward_boxes.py
"""
import glob
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
REPO = os.path.dirname(ROOT)
sys.path.insert(0, os.path.expanduser("~/Documents/mxd/BeiDou-Server/tool/wz-python"))

from wzpy import WzImage, WzKey  # noqa: E402

DATA = os.path.join(REPO, "MapleDashResources", "Data", "Skill")

# 首次接这条口径时的实测清单：全库 962 个有框技能 / 4 个正框。
EXPECTED = {"400051018", "400051019", "400051020", "5101002"}


def kids(node):
    if node is None:
        return []
    try:
        return list(node.children())
    except (TypeError, AttributeError):
        return []


def vec(value):
    if isinstance(value, tuple) and len(value) >= 2:
        try:
            return int(value[0]), int(value[1])
        except (TypeError, ValueError):
            return None
    return None


def scan():
    positive, framed = set(), 0
    for path in sorted(glob.glob(os.path.join(DATA, "*.img"))):
        img = WzImage.from_file(path, key=WzKey.for_region("GMS"), name=os.path.basename(path))
        img.parse()
        skill = img.root.get("skill")
        if skill is None:
            continue
        for child in kids(skill):
            if not child.name.isdigit():
                continue
            level = child.get("level")
            common = child.get("common")
            host = None
            if level is not None:
                rows = kids(level)
                if rows:
                    host = rows[0]
            if host is None:
                host = common
            lt = rb = None
            for field in kids(host):
                if field.name == "lt":
                    lt = vec(getattr(field, "value", None))
                elif field.name == "rb":
                    rb = vec(getattr(field, "value", None))
            if lt is None or rb is None:
                continue
            framed += 1
            if lt[0] >= 0 and rb[0] > 0:
                positive.add(child.name)
    return positive, framed


def main():
    positive, framed = scan()
    problems = []
    if positive != EXPECTED:
        problems.append("正框清单变了：实测 %s，登记 %s" % (sorted(positive), sorted(EXPECTED)))

    catalog = open(os.path.join(ROOT, "Assets", "Scripts", "NativeSkillCatalog.cs"), encoding="utf-8").read()
    system = open(os.path.join(ROOT, "Assets", "Scripts", "SkillSystem.cs"), encoding="utf-8").read()
    if "ForwardBox = bounds.Length == 0 && HasBounds && Min.x >= -0.0001f && Max.x > Min.x" not in catalog:
        problems.append("NativeSkillCatalog 没有按主框判据算 ForwardBox")
    if "bool forward = d.ForwardBox && d.Kind != \"Summon\"" not in system:
        problems.append("InRange(Vector3) 没有用 ForwardBox（Summon 要排除）")
    if "if (d.ForwardBox)" not in system:
        problems.append("InRange(Component) 的 SelectionBounds 重叠判定没有用 ForwardBox")

    print("有框技能 %d 条，正框 %d 条：%s" % (framed, len(positive), sorted(positive)))
    if problems:
        print("不一致 %d 条：" % len(problems))
        for line in problems:
            print("  " + line)
        return 1
    print("✓ 正向框两处判定都接了 ForwardBox，清单与源端一致")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
