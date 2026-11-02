#!/usr/bin/env python3
"""全量核对职业称呼与能力值窗显示（只读）。

  audit_job_names.py           打印核对表
  audit_job_names.py --check   发现问题时返回码 1（供回归用）

三处名字必须一致（骑士团的 (N转) 后缀在 UI 侧是故意去掉的，比较时忽略）：
  1. Assets/Scripts/Npc/JobEnum.cs                —— 逐条来自源端 Job.java + i18n
  2. Assets/Resources/UI/JobTree.json jobNames    —— 游戏内实际显示（JobDefs.NameOf）
  3. gms-server/.../i18n/message_zh_CN.properties —— 源端权威中文名 job.name.<id>

能力值窗（CUIStat::Draw 0x8C5BD3 → 0x8C7FB6）取图：
  UI/UIWindow.img/Stat/Job/main/<jobId/100>       11 高，画在 (62,53)
  UI/UIWindow.img/Stat/Job/sub/<jobId/100>/<jobId> 10 高，画在 (62,64)
没有对应图（骑士团 1100+、GM 9xx、233…）时退回一行文本 JobDefs.NameOf(jobId)。
"""
import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]          # MapleDash/
SRC = ROOT.parents[1] / "Beidou-Server"             # ~/Documents/mxd/Beidou-Server
I18N = SRC / "gms-server/src/main/resources/i18n/message_zh_CN.properties"
DATA = ROOT.parent / "MapleDashResources/Data"
WZPY = SRC / "tool/wz-python"

SUFFIX = re.compile(r"\(\d转\)$")


def norm(name):
    """比较用：统一括号、去掉骑士团的 (N转) 后缀。"""
    return SUFFIX.sub("", name.replace("（", "(").replace("）", ")")).strip()


def load_jobenum():
    src = (ROOT / "Assets/Scripts/Npc/JobEnum.cs").read_text(encoding="utf-8")
    m = re.search(r'static readonly string\[\] table =\s*\{(.*?)\};', src, re.S)
    if not m:
        raise SystemExit("JobEnum.cs 里找不到 table")
    return {int(i): n for _, i, n in re.findall(r'"([A-Z0-9_]+)\|(-?\d+)\|([^"]*)"', m.group(1))}


def load_jobtree():
    d = json.loads((ROOT / "Assets/Resources/UI/JobTree.json").read_text(encoding="utf-8"))
    return {int(k): v for k, v in d["jobNames"].items()}, d["jobs"]


def load_i18n():
    out = {}
    for line in I18N.read_text(encoding="utf-8").splitlines():
        m = re.match(r"job\.name\.(\d+)\s*=\s*(.*)$", line.strip())
        if not m:
            continue
        raw = m.group(2)
        # properties 里中文是 \uXXXX 转义，直接替换比 encode/decode 稳（避免真中文混排被破坏）
        raw = re.sub(r"\\u([0-9a-fA-F]{4})", lambda mm: chr(int(mm.group(1), 16)), raw)
        out[int(m.group(1))] = raw.strip()
    return out


def load_stat_job_nodes():
    """返回 (main 节点集合, sub/<group>/<jobId> 集合)；缺资源时返回空集合。"""
    try:
        sys.path.insert(0, str(WZPY))
        from wzpy import WzImage, WzKey  # noqa: E402
    except Exception as e:  # noqa: BLE001
        print(f"! 无法加载 wzpy（{e}），跳过素材核对")
        return None, None
    p = DATA / "UI/UIWindow.img"
    img = WzImage.from_bytes(p.read_bytes(), key=WzKey.for_region("GMS"), name="UI/UIWindow.img")
    img.parse()
    job = img.get("Stat/Job")
    mains = {c.name for c in job.get("main").children()}
    subs = set()
    for grp in job.get("sub").children():
        for c in grp.children():
            subs.add((grp.name, c.name))
    return mains, subs


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="有差异时返回 1")
    args = ap.parse_args()

    enum_names = load_jobenum()
    tree_names, jobs = load_jobtree()
    i18n = load_i18n()
    mains, subs = load_stat_job_nodes()

    problems = []

    print("== 1) JobTree（游戏内显示） vs 源端 i18n ==")
    for job_id in sorted(tree_names):
        want = i18n.get(job_id)
        got = tree_names[job_id]
        if want is None:
            print(f"  {job_id:<5} {got:<10} ! i18n 缺 job.name.{job_id}")
            problems.append(f"i18n 缺 {job_id}")
        elif norm(got) != norm(want):
            print(f"  {job_id:<5} {got:<10} 应为 {want}")
            problems.append(f"JobTree {job_id} {got} != {want}")

    print("== 2) JobTree vs JobEnum（脚本侧常量） ==")
    for job_id in sorted(set(tree_names) & set(enum_names)):
        if norm(tree_names[job_id]) != norm(enum_names[job_id]):
            print(f"  {job_id:<5} JobTree={tree_names[job_id]} JobEnum={enum_names[job_id]}")
            problems.append(f"JobEnum {job_id} 与 JobTree 不一致")

    print("== 3) jobs[].name 与 jobNames 一致 ==")
    for j in jobs:
        if j.get("name") != tree_names.get(j["id"]):
            print(f"  {j['id']:<5} jobs[].name={j.get('name')} jobNames={tree_names.get(j['id'])}")
            problems.append(f"jobs[].name {j['id']} 不一致")

    if mains is not None:
        print("== 4) 能力值窗会显示什么 ==")
        text_fallback = []
        for job_id in sorted(tree_names):
            grp = str(job_id // 100)
            has = (grp, str(job_id)) in subs
            main_ok = grp in mains
            if not has or not main_ok:
                text_fallback.append(job_id)
                why = "缺 sub" if not has else "缺 main"
                print(f"  {job_id:<5} {tree_names[job_id]:<10} 文本兜底（{why}/{grp}）")
        print(f"  图显示 {len(tree_names) - len(text_fallback)} 个 / 文本兜底 {len(text_fallback)} 个")
    else:
        print("== 4) 能力值窗会显示什么：跳过 ==")

    print()
    if problems:
        print(f"发现 {len(problems)} 处称呼不一致")
        return 1 if args.check else 0
    print("职业称呼全部一致（骑士团 (N转) 后缀按 UI 口径忽略）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
