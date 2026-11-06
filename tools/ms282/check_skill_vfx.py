#!/usr/bin/env python3
"""技能特效完整性守卫。

检查项目 `MapleDashResources/Data/Skill` 里每个技能节点引用的画布是否真的能拿到像素。

背景（2026-11-05）：282 的 `Skill/<file>.img` 只留 1x1 占位画布 + `_outlink`，
真画布在同目录 `Skill/_Canvas/<file>/<技能id>.img` 里，而且**元数据（origin/delay/z/a0/a1）
只写在占位节点上，目标画布是光板**。所以这里要判两件事：

  1. 像素在不在：outlink 目标文件 / 目标节点 / 目标画布尺寸。
  2. 元数据在哪：占位节点是否带 origin/delay，防止运行时按 100ms/原点播放。

用法：
  python3 tools/ms282/check_skill_vfx.py            # 全量摘要
  python3 tools/ms282/check_skill_vfx.py --list     # 打印问题技能明细
  python3 tools/ms282/check_skill_vfx.py 400011066  # 只看指定技能
"""
import json
import os
import sys

sys.path.insert(0, os.path.expanduser(
    "~/Documents/mxd/BeiDou-Server/tool/wz-python"))
from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzCanvasProperty, WzSubProperty  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DATA = os.path.join(os.path.dirname(ROOT), "MapleDashResources", "Data")
SKILL_DIR = os.path.join(DATA, "Skill")

# 只算「视觉层」：这些分支名（含编号族 effect0..1、special0..3）才会被运行时播放。
VISUAL_STEMS = (
    "effect", "special", "screen", "tile", "ball", "hit", "mob", "finish",
    "prepare", "keydown", "keydownend", "repeat", "affected", "afterimage",
    "frameEffect", "customEffect", "start", "end", "loop", "movement", "shootobj",
)


def is_visual(name):
    for stem in VISUAL_STEMS:
        if name == stem:
            return True
        if name.startswith(stem) and name[len(stem):].isdigit():
            return True
    return name.startswith("effect_") or name.startswith("special") or name.startswith("screen")


def kids(node):
    # wzpy 的 children() 返回的是一次性迭代器：递归时再调一次会把父循环的流吃掉，
    # 所以这里必须立刻落成 list。
    try:
        return list(node.children())
    except TypeError:
        return []


def get(node, name):
    for child in kids(node):
        if child.name == name:
            return child
    return None


_img_cache = {}


def open_img(path):
    if path in _img_cache:
        return _img_cache[path]
    try:
        img = WzImage.from_file(path, key=WzKey.for_region("GMS"))
    except Exception as exc:  # noqa: BLE001 - 资源坏了要报出来，不是崩掉整轮扫描
        _img_cache[path] = exc
        return exc
    _img_cache[path] = img
    return img


def resolve_outlink(link):
    """`Skill/_Canvas/40001/400011066.img/skill/400011066/effect/0`
    → (存在的 image, 内部路径, 画布节点 or 错误串)"""
    split = link.find(".img/")
    if split < 0:
        return None, None, "outlink 没有 .img/ 边界"
    rel = link[:split + 4]
    inner = link[split + 5:]
    path = os.path.join(DATA, rel)
    if not os.path.isfile(path):
        return rel, inner, "缺文件 " + rel
    img = open_img(path)
    if isinstance(img, Exception):
        return rel, inner, "读不出 " + rel + " (" + str(img) + ")"
    node = get(img.root, inner.split("/")[0])
    for part in inner.split("/")[1:]:
        if node is None:
            break
        node = get(node, part)
    if node is None:
        return rel, inner, "缺节点 " + inner
    return rel, inner, node


def text_prop(node, name):
    for prop in kids(node):
        if prop.name == name:
            return str(getattr(prop, "value", "") or "")
    return ""


def audit_skill(skill_node, skill_id):
    """统计视觉层的像素与元数据。

    返回 dict:
      frames/empty        视觉层画布帧数 / 拿不到像素的帧数
      stub_meta           元数据写在占位上（运行时必须从占位读，否则 origin/delay 全丢）
      numbers/number_empty  number 图集张数 / 其中没像素的
      problems            明细
    """
    stats = {"frames": 0, "empty": 0, "stub_meta": 0,
             "numbers": 0, "number_empty": 0, "empty_top": set(),
             "branch": {}}
    problems = []

    def branch_stat(branch):
        # branch 是「分支/帧名」，统计要落到分支上，否则每帧各成一组、永远「全空」。
        stem = "/".join(branch.split("/")[:-1]) or branch
        entry = stats["branch"].setdefault(stem, [0, 0])
        entry[1] += 1
        return entry

    def canvas(child, branch, counts_as_number=False):
        link = text_prop(child, "_outlink")
        inlink = text_prop(child, "_inlink")
        if counts_as_number:
            stats["numbers"] += 1
        else:
            stats["frames"] += 1
        entry = branch_stat(branch)
        if not link and not inlink and child.width <= 1 and child.height <= 1:
            entry[0] += 1
            if counts_as_number:
                stats["number_empty"] += 1
            else:
                stats["empty"] += 1
                stats["empty_top"].add(branch.split("/")[0])
            problems.append(branch + " → 1x1 且没有 outlink/inlink（没有任何像素）")
            return
        if link:
            _, _, target = resolve_outlink(link)
            if isinstance(target, str):
                entry[0] += 1
                if counts_as_number:
                    stats["number_empty"] += 1
                else:
                    stats["empty"] += 1
                problems.append(branch + " → " + target)
                return
            if target.width <= 1 and target.height <= 1:
                entry[0] += 1
                if counts_as_number:
                    stats["number_empty"] += 1
                else:
                    stats["empty"] += 1
                problems.append(branch + " → 目标画布仍是 1x1 占位")
                return
        if not counts_as_number and (get(child, "origin") is not None or get(child, "delay") is not None):
            stats["stub_meta"] += 1

    def walk(node, path):
        for child in kids(node):
            name = child.name
            if not isinstance(child, WzCanvasProperty):
                if not isinstance(child, WzSubProperty):
                    continue
                if path == "" and (name.startswith("icon") or not is_visual(name)):
                    continue
                walk(child, path + "/" + name if path else name)
                continue
            branch = path + "/" + name if path else name
            if not path:
                continue
            canvas(child, branch, path.split("/")[0] in ("number", "number2"))

    # 顶层：只进视觉分支；level/N 是对 common 的逐级覆盖，也按同样规则进。
    for child in kids(skill_node):
        name = child.name
        if name == "level":
            for level in kids(child):
                if isinstance(level, WzSubProperty):
                    walk(level, "")
            continue
        if not isinstance(child, WzSubProperty) or name.startswith("icon"):
            continue
        if not is_visual(name) and name not in ("number", "number2"):
            continue
        walk(child, name)
    return stats, problems


SOURCE_282 = os.path.expanduser("~/Documents/mxd/MapleStory/Data/Skill/_Canvas")


def source_has_pixels(file_no, skill_id, branch):
    """源端 282 `_Canvas/<file>.img`（BMS 密钥整包）里同技能同分支有没有真像素。

    返回 True（有像素）/ False（源端也空或没有这个分支）/ None（源端没有这个技能）。
    """
    path = os.path.join(SOURCE_282, "%s.img" % file_no)
    if not os.path.isfile(path):
        return None
    if path not in _img_cache:
        try:
            _img_cache[path] = WzImage.from_file(path, key=WzKey.for_region("BMS"))
        except Exception as exc:  # noqa: BLE001
            _img_cache[path] = exc
    img = _img_cache[path]
    if isinstance(img, Exception):
        return None
    skill_root = get(img.root, "skill")
    if skill_root is None:
        return None
    node = get(skill_root, "%07d" % skill_id)
    if node is None:
        return None
    branch_node = get(node, branch)
    if branch_node is None:
        return False
    for frame in kids(branch_node):
        if not isinstance(frame, WzCanvasProperty):
            continue
        return frame.width > 1 or frame.height > 1
    return False


def load_skill_ids():
    out = []
    ui = os.path.join(ROOT, "Assets", "Resources", "UI", "VMatrixSkills.json")
    with open(ui, encoding="utf-8") as handle:
        data = json.load(handle)
    out.extend(int(k) for k in data["skills"])
    jt = os.path.join(ROOT, "Assets", "Resources", "UI", "JobTree.json")
    if os.path.isfile(jt):
        with open(jt, encoding="utf-8") as handle:
            text = handle.read()
        # 轻量：只认 7~9 位数字的技能 id
        import re
        for token in set(re.findall(r'"(\d{7,9})"', text)):
            value = int(token)
            if value >= 1000000:
                out.append(value)
    return sorted(set(out))


def main(argv):
    only = [int(a) for a in argv if a.isdigit()]
    verbose = "--list" in argv
    vs282 = "--vs282" in argv
    ids = only or load_skill_ids()

    by_file = {}
    for skill_id in ids:
        by_file.setdefault(skill_id // 10000, []).append(skill_id)

    total_skills = 0
    total_frames = 0
    total_empty = 0
    total_stub_meta = 0
    total_numbers = 0
    total_number_empty = 0
    broken = []
    missing = []
    empty_skills = []
    vs_missing, vs_also_empty, vs_absent = [], [], []
    dead_branches = []

    for file_no, group in sorted(by_file.items()):
        path = os.path.join(SKILL_DIR, "%03d.img" % file_no)
        if not os.path.isfile(path):
            missing.extend(group)
            continue
        img = open_img(path)
        if isinstance(img, Exception):
            missing.extend(group)
            continue
        skill_root = get(img.root, "skill")
        if skill_root is None:
            missing.extend(group)
            continue
        by_name = {child.name: child for child in kids(skill_root)}
        for skill_id in group:
            node = by_name.get("%07d" % skill_id)
            if node is None:
                missing.append(skill_id)
                continue
            total_skills += 1
            stats, problems = audit_skill(node, skill_id)
            total_frames += stats["frames"]
            total_empty += stats["empty"]
            total_stub_meta += stats["stub_meta"]
            total_numbers += stats["numbers"]
            total_number_empty += stats["number_empty"]
            if stats["empty"]:
                empty_skills.append((skill_id, stats["empty"], stats["frames"]))
                broken.append((skill_id, problems))
            # 只有「整族所有帧都没像素」才算真缺一层；
            # 单帧空占位（首帧透明、末帧占位）在 282 里本来就存在，不该算缺失。
            whole = [(b, v) for b, v in stats["branch"].items()
                     if v[1] > 0 and v[0] == v[1] and not b.startswith("number")]
            for branch, _ in whole:
                dead_branches.append((skill_id, branch))
            if vs282:
                for branch, _ in whole:
                    top = branch.split("/")[0]
                    has = source_has_pixels("%03d" % file_no, skill_id, top)
                    if has is True:
                        vs_missing.append((skill_id, branch))
                    elif has is None:
                        vs_absent.append((skill_id, branch))
                    else:
                        vs_also_empty.append((skill_id, branch))

    print("扫描技能 %d 条（请求 %d），视觉层画布 %d 张" % (total_skills, len(ids), total_frames))
    print("  缺技能节点/文件      : %d" % len(missing))
    print("  拿不到像素的帧       : %d（涉及 %d 条技能）" % (total_empty, len(empty_skills)))
    print("  整族没有像素的分支   : %d 处 / %d 条技能"
          % (len(dead_branches), len({s for s, _ in dead_branches})))
    print("  元数据只在占位上的帧 : %d" % total_stub_meta)
    print("  number 图集          : %d 张，其中没像素 %d" % (total_numbers, total_number_empty))
    if missing:
        print("  缺节点: " + ", ".join(str(i) for i in missing[:20])
              + (" …" if len(missing) > 20 else ""))
    if vs282:
        print("")
        print("  与源端 282 _Canvas 对照（按顶层分支）")
        print("    源端有像素、项目是空层 : %d 处 / %d 条技能"
              % (len(vs_missing), len({s for s, _ in vs_missing})))
        print("    源端也是空层           : %d 处" % len(vs_also_empty))
        print("    源端没这个技能/分支    : %d 处" % len(vs_absent))
        for skill_id, branch in vs_missing[:40]:
            print("      %d  %s" % (skill_id, branch))
        if len(vs_missing) > 40:
            print("      … 共 %d 处" % len(vs_missing))
    if verbose:
        for skill_id, problems in broken:
            print("\n== %d" % skill_id)
            for line in problems[:20]:
                print("   " + line)
            if len(problems) > 20:
                print("   … 共 %d 处" % len(problems))
    # 硬错误只有「技能节点/文件缺失」。空层在 282 里本来就成片存在（源端也空），
    # 不作为失败条件；要判「我们比源端少」必须显式跑 --vs282。
    return 0 if not missing else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
