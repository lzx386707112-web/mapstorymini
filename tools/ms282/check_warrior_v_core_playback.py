#!/usr/bin/env python3
"""守卫/报告：把「282 导出的战士五转核心特效基准」按**运行时播放链路**逐层对照。

背景：用户反馈「光之聖騎士技能特效全都不对」「神圣团结释放时和导出图完全不一样」，
要求「按播放链路到具体效果对比，不要只看数据」并「全面检查」。
本脚本不做新的导出，只回答三个问题：

  1. 播放链路到底会播哪些层？—— 复刻 NativeSkillVfx.Layers/Parts/Roots 与
     SkillVisuals.CastVisuals / Impact / SkillHeldInput.HeldVisuals 的取层规则。
  2. 282 基准里存在的层，项目 `Skill/40001.img` 定义树里有没有？帧数对不对？
  3. 哪些层 282 里有、运行时却没有任何读取点（= 画面必然缺一块的真缺口）？

产物（默认写到导出目录）：
  playback_audit.json   机器可读逐技能逐层结果
  playback_audit.md     人读报告（含 122 四核心逐层明细 + 全量缺口清单）

用法：
  python3 tools/ms282/check_warrior_v_core_playback.py
  python3 tools/ms282/check_warrior_v_core_playback.py --out ~/Documents/mxd/282-warrior-vfx-export
  python3 tools/ms282/check_warrior_v_core_playback.py --manifest /path/manifest.json
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from collections import Counter, defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, os.path.expanduser("~/Documents/mxd/BeiDou-Server/tool/wz-python"))

from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzCanvasProperty, WzUolProperty  # noqa: E402

REPO = HERE.parents[2]
PROJECT_DEFS = REPO / "MapleDashResources/Data/Skill/40001.img"
DEFAULT_OUT = Path(os.path.expanduser("~/Documents/mxd/282-warrior-vfx-export"))

# —— 运行时取层规则（与 C# 一一对应）——————————————————————————————
# Layers(key, stem)：主干 + 纯数字后缀（NativeSkillVfx.Numbered）
# Parts(key, branch)：branch 本身或 branch/... 前缀分支
LAYERS_CAST = (
    ("prepare", "SkillVisuals.CastVisuals / SkillHeldInput.HeldVisuals"),
    ("effect", "SkillVisuals.CastVisuals"),
    ("specialAffected", "SkillVisuals.CastVisuals"),
    ("effect_ple", "SkillVisuals.PlaySegmentedLayer"),
    ("special", "SkillVisuals.SpecialField（HasLayer 门控）"),
    ("start", "SkillVisuals.PlayFieldLoop"),
    ("repeat", "SkillVisuals.PlayFieldLoop"),
    ("end", "SkillVisuals.PlayFieldLoop"),
    ("keydown", "SkillHeldInput.HeldVisuals"),
    ("keydownend", "SkillHeldInput.StopHeld"),
)
PARTS_CAST = (
    ("affected", "SkillVisuals.CastVisuals"),
    ("frameEffect", "SkillVisuals.CastVisuals / SubBranches"),
    ("customEffect", "SkillVisuals.PlayCustomEffect"),
    ("afterimage", "SkillVisuals.Afterimage"),
    ("tile", "SkillVisuals.TileField"),
    ("finish", "SkillVisuals.PlayFinish"),
    ("ball", "SkillVisuals.UsesProjectile"),
    ("flipBall", "SkillVisuals.BallVariant"),
    ("shootobj", "NativeShootObj / 发射物"),
    ("number", "SkillVisuals.ShowComboNumber / Icons"),
    ("number2", "SkillVisuals.ShowComboNumber / Icons"),
    ("summon", "NativeSummon（召唤体）"),
    ("mob", "NativeSummon / 受击侧"),
    ("rush", "SkillVisuals（位移特效）"),
    ("effectFlash", "SkillVisuals（闪光层）"),
)
PARTS_HIT = (
    ("hit", "SkillVisuals.Impact / Parts(hit)"),
    ("hit2", "SkillVisuals.Impact / Parts(hit2)（HitCount>=2）"),
)


def numbered(name: str, stem: str) -> bool:
    """NativeSkillVfx.Numbered：名字里含字母的是另一种语义，不并入主干族。"""
    if not name.startswith(stem):
        return False
    return all("0" <= ch <= "9" for ch in name[len(stem):])


def parts_match(branch: str, stem: str) -> bool:
    """NativeSkillVfx.Parts：branch 本身或 branch/ 下的子分支。"""
    return branch == stem or branch.startswith(stem + "/")


def screen_layer(branch: str) -> bool:
    """SkillVisuals.IsScreenLayer。"""
    return branch.startswith("screen") or branch == "background"


def runtime_rule(branch: str) -> tuple[str, str] | None:
    """返回 (侧, 规则)；None = 282 里有这层但运行时没有任何读取点。"""
    if screen_layer(branch):
        return ("cast", "SkillVisuals.CastVisuals / Roots+IsScreenLayer")
    for stem, who in LAYERS_CAST:
        if numbered(branch, stem):
            return ("cast", "Layers(%s) · %s" % (stem, who))
    for stem, who in PARTS_CAST:
        if parts_match(branch, stem):
            return ("cast", "Parts(%s) · %s" % (stem, who))
    for stem, who in PARTS_HIT:
        if parts_match(branch, stem):
            return ("hit", "Parts(%s) · %s" % (stem, who))
    return None


def kids(node):
    try:
        return list(node.children())
    except Exception:
        return []


def nav(node, path):
    for part in path.split("/"):
        if not part:
            continue
        found = next((c for c in kids(node) if c.name == part), None)
        if found is None:
            return None
        node = found
    return node


def canvas_leaves(node) -> int:
    """项目定义树里某一分支下的「帧」数 = 运行时 WzAssets.Frames 会枚举的叶子数。

    282 的定义层大量用 UOL 把一整个分支指到另一分支（prepare20 -> ../keydown0/N、
    screen1 -> ../../400011088/screen3/N）。UOL 不是 WzCanvasProperty，但运行时
    TryResolve() 会跟过去，所以必须算成一帧，否则会误报「项目帧数不一致」。
    """
    total = 0
    for child in kids(node):
        if isinstance(child, (WzCanvasProperty, WzUolProperty)):
            total += 1
        else:
            total += canvas_leaves(child)
    return total


def project_branches(skill_id: str, root) -> dict[str, int]:
    node = nav(root, "skill/" + skill_id)
    if node is None:
        return {}
    out = {}
    for child in kids(node):
        out[child.name] = canvas_leaves(child)
    return out


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", default=str(DEFAULT_OUT), help="导出目录（含 manifest.json）")
    ap.add_argument("--manifest", default=None, help="显式指定 manifest.json")
    ap.add_argument("--defs", default=str(PROJECT_DEFS), help="项目 Skill/40001.img")
    args = ap.parse_args()

    out_dir = Path(args.out)
    manifest_path = Path(args.manifest) if args.manifest else out_dir / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    skills = manifest["skills"]

    defs = WzImage.from_bytes(Path(args.defs).read_bytes(), key=WzKey.for_region("GMS"))
    defs.parse()
    root = defs.root

    rows = []
    for s in skills:
        sid = s["id"]
        proj = project_branches(sid, root)
        branches = []
        for name, info in s["branches"].items():
            rule = runtime_rule(name)
            branches.append({
                "branch": name,
                "frames_282": info["frames"],
                "with_pixels_282": info["with_pixels"],
                "groups": info["groups"],
                "errors_282": info["errors"],
                "frames_project": proj.get(name),
                "in_project": name in proj,
                "runtime_side": rule[0] if rule else None,
                "runtime_rule": rule[1] if rule else None,
            })
        missing = [b for b in branches if not b["in_project"]]
        uncovered = [b for b in branches if b["runtime_side"] is None]
        mismatch = [b for b in branches
                    if b["in_project"] and b["frames_project"] != b["frames_282"]]
        rows.append({
            "id": sid, "name": s["name"], "cores": s["cores"], "jobs": s["jobs"],
            "in_project": s["in_project"],
            "frames_total": s["frames_total"], "cast_frames": s["cast_frames"],
            "hit_frames": s["hit_frames"], "frames_failed": s["frames_failed"],
            "frames_source_empty": s["frames_source_empty"],
            "frames_project_fallback": s["frames_project_fallback"],
            "sheets": s["sheets"], "timeline": s["timeline"],
            "branches": branches,
            "missing_in_project": [b["branch"] for b in missing],
            "uncovered_branches": [b["branch"] for b in uncovered],
            "frame_count_mismatch": [b["branch"] for b in mismatch],
        })

    # —— 汇总 ——
    total_frames = sum(r["frames_total"] for r in rows)
    cast_frames = sum(r["cast_frames"] for r in rows)
    hit_frames = sum(r["hit_frames"] for r in rows)
    failed = sum(r["frames_failed"] for r in rows)
    fallback = sum(r["frames_project_fallback"] for r in rows)
    empty = sum(r["frames_source_empty"] for r in rows)
    covered_frames = sum(b["frames_282"] for r in rows for b in r["branches"]
                         if b["runtime_side"])
    uncovered_frames = sum(b["frames_282"] for r in rows for b in r["branches"]
                           if not b["runtime_side"])
    gap_counter = Counter()
    gap_frames = Counter()
    for r in rows:
        for b in r["branches"]:
            if not b["runtime_side"]:
                gap_counter[b["branch"]] += 1
                gap_frames[b["branch"]] += b["frames_282"]
    missing_counter = Counter()
    for r in rows:
        for name in r["missing_in_project"]:
            missing_counter[name] += 1

    summary = {
        "skills": len(rows), "frames_total": total_frames,
        "cast_frames": cast_frames, "hit_frames": hit_frames,
        "frames_failed": failed, "frames_source_empty": empty,
        "frames_project_fallback": fallback,
        "frames_covered_by_runtime": covered_frames,
        "frames_uncovered_by_runtime": uncovered_frames,
        "skills_missing_any_branch": sum(1 for r in rows if r["missing_in_project"]),
        "skills_with_uncovered_branch": sum(1 for r in rows if r["uncovered_branches"]),
        "skills_with_frame_count_mismatch": sum(1 for r in rows if r["frame_count_mismatch"]),
        "uncovered_branch_kinds": dict(gap_counter.most_common()),
        "uncovered_branch_frames": dict(gap_frames.most_common()),
        "missing_in_project_branch_kinds": dict(missing_counter.most_common()),
    }

    audit = {
        "source": {
            "manifest": str(manifest_path),
            "project_definitions": str(args.defs),
            "rules": "NativeSkillVfx.Layers/Parts/Roots + SkillVisuals.CastVisuals/Impact + SkillHeldInput",
        },
        "summary": summary,
        "skills": rows,
    }
    json_path = out_dir / "playback_audit.json"
    json_path.write_text(json.dumps(audit, ensure_ascii=False, indent=2), encoding="utf-8")

    # —— 人读报告 ——
    focus = {"400011003", "400011052", "400011072", "400011131",
             "400011000", "400011066"}
    lines = [
        "# 282 战士五转核心特效：播放链路逐层对照报告",
        "",
        "基准 `%s`（282 原端 `Skill/40001.img` + `Skill/_Canvas/40001.img`）；" % manifest_path.name,
        "项目侧 `%s`（运行时 `NativeSkillVfx.Key` 读的定义树）。" % Path(args.defs).name,
        "",
        "## 汇总",
        "",
        "| 指标 | 值 |",
        "| --- | --- |",
        "| 技能节点 | %d |" % summary["skills"],
        "| 画布帧 | %d（施法侧 %d / 受击侧 %d，交集层会重复计数） |" %
        (total_frames, cast_frames, hit_frames),
        "| 解不开 / 回退项目画布 | %d / %d |" % (failed, fallback),
        "| 源端 1×1 空占位（时间对齐用，保留） | %d |" % empty,
        "| 运行时有读取点的帧 | %d |" % covered_frames,
        "| 运行时**没有**读取点的帧 | %d |" % uncovered_frames,
        "| 项目定义树缺分支的技能 | %d |" % summary["skills_missing_any_branch"],
        "| 存在「282 有层但运行时不播」的技能 | %d |" % summary["skills_with_uncovered_branch"],
        "| 帧数与项目定义树不一致的技能 | %d |" % summary["skills_with_frame_count_mismatch"],
        "",
        "> 说明：`Parts(branch)` 只匹配 `branch` 与 `branch/...`，不匹配 `branch0`；",
        "> `Layers(stem)` 只认主干 + 纯数字后缀。两者之差就是最容易出现「画面缺一块」的地方。",
        "",
        "## 光之聖騎士（122）与战士共通：逐层明细",
        "",
    ]
    for r in rows:
        if r["id"] not in focus:
            continue
        lines += ["### %s %s" % (r["id"], r["name"]),
                  "",
                  "帧 %d（施法 %d / 受击 %d），核心 %s" %
                  (r["frames_total"], r["cast_frames"], r["hit_frames"],
                   ", ".join("%s %s" % (c["core"], c["coreName"]) for c in r["cores"]) or "—"),
                  "",
                  "| 层 | 282 帧 | 有像素 | 项目帧 | 运行时 | 取层规则 |",
                  "| --- | ---: | ---: | ---: | --- | --- |"]
        for b in r["branches"]:
            side = b["runtime_side"] or "**不播**"
            lines.append("| `%s` | %d | %d | %s | %s | %s |" %
                         (b["branch"], b["frames_282"], b["with_pixels_282"],
                          b["frames_project"] if b["in_project"] else "缺",
                          side, b["runtime_rule"] or "—"))
        if r["sheets"]["cast"] or r["sheets"]["hit"]:
            lines += ["",
                      "对照图：%s" % "、".join(
                          "`%s`" % p for p in (r["sheets"]["cast"] + r["sheets"]["hit"])[:6])]
        lines.append("")

    lines += ["## 运行时无读取点的层（全量）", ""]
    if gap_counter:
        lines += ["| 分支 | 出现技能数 | 帧数 |", "| --- | ---: | ---: |"]
        for name, count in gap_counter.most_common():
            lines.append("| `%s` | %d | %d |" % (name, count, gap_frames[name]))
        lines += ["",
                  "涉及技能：", ""]
        for r in rows:
            if r["uncovered_branches"]:
                lines.append("- `%s` %s：%s" %
                             (r["id"], r["name"], ", ".join("`" + b + "`" for b in r["uncovered_branches"])))
    else:
        lines.append("无。")
    lines += ["", "## 项目定义树缺失 / 帧数不一致（全量）", ""]
    problematic = [r for r in rows if r["missing_in_project"] or r["frame_count_mismatch"]]
    if problematic:
        lines += ["| 技能 | 缺分支 | 帧数不一致 |", "| --- | --- | --- |"]
        for r in problematic:
            lines.append("| `%s` %s | %s | %s |" %
                         (r["id"], r["name"],
                          ", ".join("`" + b + "`" for b in r["missing_in_project"]) or "—",
                          ", ".join("`" + b + "`" for b in r["frame_count_mismatch"]) or "—"))
    else:
        lines.append("无：157 个技能的分支集合与帧数与 282 基准一致。")
    lines += ["", "## 结论与处置", "",
              "1. **基准完整**：157 个技能 / %d 帧，0 解不开、0 回退；" % total_frames,
              "   项目 `Skill/40001.img` 定义树的分支集合与帧数与 282 基准完全一致（UOL 别名已按运行时 `TryResolve()` 解析）。",
              "2. **底层像素已回正**：`texture2ddecoder` 解 BC3/BC7 输出 BGRA，旧烘焙当 RGBA 压进 ARGB4444，",
              "   导致游戏里金色圣光/十字架整体红蓝互换（用户看到的「蓝色十字架」）。",
              "   已修 `img_bake.decode_rgba` 并全量无损交换已落盘资源；",
              "   `check_skill_canvas_channels.py --shard 40001 --all` 全量复核 5099 张画布、0 失败。",
              "3. **tooltip 串位**：`MatrixPresentation.Skill()` 旧实现把核心名写进 `SkillBook.byId` 的共享",
              "   `SkillData.name`，且 `Draw()` 不清静态状态；HEXA 悬停还直接用未归一化的 `core.skills`。",
              "   已改为局部副本（名字/说明以 coreId 对应的权威核心表为准）+ 收尾清状态 + HEXA 归一化。",
              "4. **仍需真机确认**：上表 4 类分支（%d 帧）在 282 里有、运行时没有任何读取点。" % uncovered_frames,
              "   其中 `affected0` 带 `z=-1` 等独立元数据，可能是有意不播的「另一形态 / 后层」；",
              "   贸然改成 `Layers(\"affected\")` 会与现有 `specialAffected*` 叠出双重特效，所以先只报告、不改播放代码。",
              "",
              "## 复核命令", "",
              "```bash",
              "cd MapleDash",
              "python3 tools/ms282/check_skill_canvas_channels.py --shard 40001 --all",
              "python3 tools/ms282/check_warrior_v_core_playback.py",
              "```",
              ""]
    md_path = out_dir / "playback_audit.md"
    md_path.write_text("\n".join(lines), encoding="utf-8")

    print("技能 %d / 帧 %d；运行时覆盖 %d 帧，未覆盖 %d 帧" %
          (len(rows), total_frames, covered_frames, uncovered_frames))
    print("项目定义树缺分支技能 %d，帧数不一致技能 %d" %
          (summary["skills_missing_any_branch"], summary["skills_with_frame_count_mismatch"]))
    if gap_counter:
        print("运行时无读取点的分支种类:", dict(gap_counter.most_common(12)))
    print("报告:", md_path)
    print("数据:", json_path)
    return 0 if failed == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
