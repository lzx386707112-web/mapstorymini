#!/usr/bin/env python3
"""核心技能覆盖率守卫：V / HEXA 矩阵里能滚出来的每一个技能，客户端都解析得了。

为什么需要它：VMatrixSkills.json 只导 17 个已迁移职业组的「可施放技能」，其余技能靠
SkillBook.Get() 直读 Skill/<img>.img 兜底（common 公式展开成等级行）。两处任何一个缺口，
玩家看到的就是「这个核心连接的技能 NNNN 不在已开放职业的技能表里」——核心能强化、
却绑不上快捷栏也放不出来。这里把「哪些技能该能解析」钉死，缺一个就报出来。

判定口径与运行时一致：
    JobTree.json 的 skills  → SkillBook.byId（四转/六转那条线）
    VMatrixSkills.json 的 skills → SkillBook.byId（V/HEXA 导出线）
    都不在 → Skill/&lt;id/10000:03d&gt;.img 里 skill/&lt;id&gt; 必须有 level 行，或有 common 且 maxLevel&gt;0

另外查名字：项目自带的 String/Skill.img 只到四转，五转技能名来自 VMatrixSkills 的 names 段，
缺了 UI 只能显示数字 id。

用法：
    python3 tools/check_matrix_skill_coverage.py          # 全量
    python3 tools/check_matrix_skill_coverage.py --quiet  # 只报结论
"""
import argparse
import json
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]          # MapleDash/
REPO = ROOT.parent
DATA = REPO / "MapleDashResources/Data"
UI = ROOT / "Assets/Resources/UI"
sys.path.insert(0, str(REPO.parent / "BeiDou-Server/tool/wz-python"))

from wzpy import WzImage, WzKey  # noqa: E402

GMS = WzKey.for_region("GMS")

# 客户端 JobTree 里真实存在的职业（四转 ID）。矩阵表按 51 个职业组建组，但惡魔/精靈/
# 幻影/凱內西斯这些组客户端压根练不到，它们的 Skill img 也不在 MapleDashResources 里。
def client_jobs() -> set[int]:
    tree = json.loads((UI / "JobTree.json").read_text(encoding="utf-8"))
    return {job["id"] for job in tree["jobs"]}


def matrix_groups(path: Path) -> dict[int, set[int]]:
    table = json.loads(path.read_text(encoding="utf-8"))
    cores = {int(k): v for k, v in table["cores"].items()}
    result: dict[int, set[int]] = {}
    for job, sections in table["groups"].items():
        ids: set[int] = set()
        for core_ids in sections.values():
            ids.update(int(c) for c in core_ids)
        result[int(job)] = {s for c in ids for s in (cores[c].get("skills") or []) + (cores[c].get("subSkills") or [])}
    return result


class Definitions:
    def __init__(self):
        self.cache = {}

    def node(self, skill_id: int):
        img = skill_id // 10000
        if img not in self.cache:
            path = DATA / f"Skill/{img:03d}.img"
            self.cache[img] = WzImage.from_file(str(path), key=GMS) if path.is_file() else None
        root = self.cache[img]
        return None if root is None else root.root.get(f"skill/{skill_id}")

    def resolvable(self, skill_id: int):
        node = self.node(skill_id)
        if node is None:
            return False, "没有 Skill img 节点"
        if node.get("level") is not None:
            return True, ""
        common = node.get("common")
        if common is None:
            return False, "只有节点，没有 level 也没有 common"
        value = common.get("maxLevel")
        max_level = int(getattr(value, "value", 0) or 0) if value is not None else 0
        if max_level <= 0:
            return False, "common 里没有 maxLevel"
        return True, ""


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--quiet", action="store_true")
    args = ap.parse_args()

    tree = json.loads((UI / "JobTree.json").read_text(encoding="utf-8"))
    known = {int(k) for k in tree["skills"]}
    exported = json.loads((UI / "VMatrixSkills.json").read_text(encoding="utf-8"))
    known.update(int(k) for k in exported["skills"])
    names = {int(k): v.get("name", "") for k, v in exported.get("names", {}).items()}
    names.update({int(k): v.get("name", "") for k, v in exported["skills"].items()})

    supported = client_jobs()
    defs = Definitions()
    failures, others, checked = [], [], set()

    for table in ("VMatrix.json", "HexaMatrix.json"):
        groups = matrix_groups(UI / table)
        for job, skills in sorted(groups.items()):
            for skill_id in sorted(skills):
                if skill_id in checked:
                    continue
                checked.add(skill_id)
                if skill_id in known:
                    ok, why = True, ""
                else:
                    ok, why = defs.resolvable(skill_id)
                if ok:
                    continue
                # 客户端 JobTree 里没有的职业组（惡魔/精靈/凱內西斯…）连 Skill img 都没搬过来，
                # 玩家练不到，只记账不判失败；已支持职业组缺一条就是真有玩家会撞上。
                (failures if job in supported else others).append(
                    f"{table} job={job} 技能 {skill_id} 解析不了：{why}")

    # 名字：客户端支持的职业组必须全有；未支持职业组（imgs 都没有）不追究。
    nameless = []
    for table in ("VMatrix.json", "HexaMatrix.json"):
        for job, skills in sorted(matrix_groups(UI / table).items()):
            if job not in supported:
                continue
            for skill_id in sorted(skills):
                if names.get(skill_id):
                    continue
                nameless.append(f"{table} job={job} 技能 {skill_id} 没有中文名")

    supported_skills = {s for table in ("VMatrix.json", "HexaMatrix.json")
                        for job, skills in matrix_groups(UI / table).items() if job in supported
                        for s in skills}
    print(f"核心技能 {len(checked)} 条（支持职业 {len(supported_skills)} 条 / 职业组 "
          f"{len(supported)} 有效）  解析失败 {len(failures)}  缺名字 {len(nameless)}"
          f"  未迁移职业组不可解析 {len(others)}（客户端练不到，不计失败）")
    if not args.quiet:
        for item in failures[:20]:
            print("  ✗ " + item)
        for item in nameless[:20]:
            print("  ✗ " + item)
    if failures:
        raise SystemExit(1)
    print("OK — 每个核心技能都能被 SkillBook 解析")


if __name__ == "__main__":
    main()
