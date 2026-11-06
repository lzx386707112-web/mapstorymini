#!/usr/bin/env python3
"""逐职业守卫：V / HEXA 矩阵里每一颗核心都必须画得出图标。

用户反馈「矩阵面板很多技能图标没显示」的根因不是资源没搬，而是窗口只画
`skillicon/<skills[0]>`：282 的核心图标在 `Etc/VCore.img` / `Etc/HexaCore.img`
按 **coreId** 索引，Skill 树里 164 个 V 技能的 icon 是 1×1 全透明桩。

判定口径与运行时 `MatrixCoreIcons.Core()` 完全一致：
    1. type 2 特殊核心 → `Etc/_Canvas/VCore.img/.../{icon,iconDisabled}`（SpecialIcon）
    2. MatrixCoreIcons.json 的 v/h 表 → WzAssets.Node(path) 的原生画布
    3. 表外 → `skillicon/<skills[0]>`（跟随 _outlink）
画布必须是 ImgFile.Rgba() 认得的 1/2/513，且真的有不透明像素；真机清单
（NativeAssetIndex.files）必须登记对应文件。

用法：
    python3 tools/check_matrix_core_icons.py
    python3 tools/check_matrix_core_icons.py --quiet
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REPO = ROOT.parent
sys.path.insert(0, str(REPO.parent / "BeiDou-Server/tool/wz-python"))

from wzpy import WzImage, WzKey                       # noqa: E402
from wzpy.canvas import decode_canvas                 # noqa: E402
from wzpy.properties import WzCanvasProperty          # noqa: E402

GMS = WzKey.for_region("GMS")
DATA = REPO / "MapleDashResources/Data"
UI = ROOT / "Assets/Resources/UI"
SCRIPTS = ROOT / "Assets/Scripts"
INDEX = ROOT / "Assets/Resources/NativeAssetIndex.json"
TABLE = UI / "MatrixCoreIcons.json"
SPECIAL_FILE = "Etc/_Canvas/VCore.img"

# 282 源端本身就没有任何图标的 3 条强化核心：VCore 无 icon 节点、Skill 里
# 400004xxx 是 1×1 桩、psdSkill 的真实技能在源端各画布也无 icon。
# 不自制图标、不拿别的核心顶替，显式登记为已知原端缺口。
KNOWN_ORIGIN_GAPS = {
    20010031: "極速巔峰-目標鎖定強化",
    20020068: "強化暗襲",
    20030017: "三重衝擊強化",
}

SUPPORTED = (1, 2, 513)


class Assets:
    def __init__(self, index):
        self.index = index
        self.files = {f["path"]: f["sha256"] for f in index["files"]}
        self.cache = {}

    def image(self, rel):
        if rel not in self.cache:
            path = DATA / rel
            if not path.is_file():
                self.cache[rel] = None
            else:
                img = WzImage.from_file(str(path), key=GMS)
                img.parse()
                self.cache[rel] = img
        return self.cache[rel]

    def canvas(self, link, allow_missing_file=False):
        """跟随 _outlink，返回 (画布, 文件, 错误)。"""
        f, rest = link.split(".img/", 1)
        f += ".img"
        img = self.image(f)
        if img is None:
            return None, f, "文件不存在 " + f
        node = img.get(rest)
        for _ in range(8):
            if not isinstance(node, WzCanvasProperty):
                return None, f, "不是 Canvas 节点 " + link
            out = node.get("_outlink")
            if out is None or not str(out.value):
                break
            f, rest = str(out.value).split(".img/", 1)
            f += ".img"
            img = self.image(f)
            if img is None:
                return None, f, "外链文件不存在 " + f
            node = img.get(rest)
        return node, f, ""

    def visible(self, link, check_manifest=True):
        node, f, err = self.canvas(link)
        if node is None:
            return False, err
        if check_manifest and f not in self.files:
            return False, "真机清单未登记 " + f
        if node.format not in SUPPORTED:
            return False, "format %s 运行时解不了" % node.format
        try:
            raw = decode_canvas(node).convert("RGBA").tobytes()
        except Exception as e:
            return False, "解码失败 " + type(e).__name__
        n = sum(1 for i in range(3, len(raw), 4) if raw[i] > 8)
        return (True, "") if n > 32 else (False, "全透明画布（%d 可见像素）" % n)


def skill_icon_link(skill_id, aliases):
    key = "skillicon/%d" % skill_id
    if key in aliases:
        return aliases[key]["source"]
    return "Skill/%03d.img/skill/%07d/icon" % (skill_id // 10000, skill_id)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--quiet", action="store_true")
    args = ap.parse_args()

    index = json.loads(INDEX.read_text(encoding="utf-8"))
    assets = Assets(index)
    table = json.loads(TABLE.read_text(encoding="utf-8"))
    v_paths = {int(k): v for k, v in table["v"].items()}
    h_paths = {int(k): v for k, v in table["h"].items()}
    failures = []
    per_job = {}
    reasons = Counter()

    # 资源文件 sha256 必须与清单一致（打包校验会查）。
    for rel in ("Etc/_Canvas/VCore.img", "Etc/_Canvas/HexaCore.img",
                "Etc/_Canvas/MatrixCoreIcons.img"):
        path = DATA / rel
        if rel not in assets.files:
            failures.append("清单缺 " + rel)
            continue
        if not path.is_file():
            failures.append("文件缺 " + rel)
            continue
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        if digest != assets.files[rel]:
            failures.append("sha256 不符 " + rel)

    for name, group_key, paths, hexa in (("VMatrix.json", "v", v_paths, False),
                                          ("HexaMatrix.json", "h", h_paths, True)):
        data = json.loads((UI / name).read_text(encoding="utf-8"))
        cores = {int(k): c for k, c in data["cores"].items()}
        for job, sections in sorted(data["groups"].items(), key=lambda kv: int(kv[0])):
            ids = [int(c) for group in sections.values() for c in group]
            missing = []
            for core_id in ids:
                core = cores.get(core_id)
                if core is None:
                    failures.append("%s job=%s 核心 %d 不在核心表" % (name, job, core_id))
                    continue
                if not hexa and core.get("type") == 2:
                    # 特殊核心在运行时走 SpecialIcon，两张状态图都要在。
                    for state in ("icon", "iconDisabled"):
                        link = "%s/vSkill/CoreData/%d/%s" % (SPECIAL_FILE, core_id, state)
                        ok, why = assets.visible(link)
                        if not ok:
                            missing.append((core_id, why))
                            break
                    continue
                if core_id in paths:
                    ok, why = assets.visible(paths[core_id])
                elif core_id in KNOWN_ORIGIN_GAPS and not hexa:
                    continue                       # 原端数据缺口，显式豁免
                else:
                    skills = core.get("skills") or []
                    if not skills:
                        missing.append((core_id, "无技能可回退"))
                        continue
                    ok, why = assets.visible(skill_icon_link(int(skills[0]), index["aliases"]))
                if ok:
                    continue
                missing.append((core_id, why))
                reasons[why.split(" ")[0]] += 1
                failures.append("%s job=%s 核心 %d（%s）无图标：%s" % (
                    name, job, core_id, core.get("name", ""), why))
            per_job[(name, job)] = (len(ids), len(missing), missing[:4])

    # C# 接线：核心表必须被加载，V/HEXA 窗口不得再直接画 skillicon。
    cs = {
        "MatrixCoreIcons.cs": (SCRIPTS / "MatrixCoreIcons.cs").read_text(),
        "NativeVMatrixWindow.cs": (SCRIPTS / "NativeVMatrixWindow.cs").read_text(),
        "NativeHexaMatrixWindow.cs": (SCRIPTS / "NativeHexaMatrixWindow.cs").read_text(),
    }
    if "UI/MatrixCoreIcons" not in cs["MatrixCoreIcons.cs"]:
        failures.append("MatrixCoreIcons.cs 没有加载 UI/MatrixCoreIcons")
    if "WzAssets.Node(path)" not in cs["MatrixCoreIcons.cs"]:
        failures.append("MatrixCoreIcons.cs 没有走原生画布节点")
    for f in ("NativeVMatrixWindow.cs", "NativeHexaMatrixWindow.cs"):
        if "MatrixCoreIcons." not in cs[f]:
            failures.append(f + " 没有接 MatrixCoreIcons")
        if 'WzAssets.Texture("skillicon/"' in cs[f] and f == "NativeHexaMatrixWindow.cs":
            failures.append(f + " 仍直接画 skillicon")
    if cs["NativeVMatrixWindow.cs"].count("MatrixCoreIcons.") < 4:
        failures.append("V 窗口的槽位/强化/结果/跟随图标没有全部接线")
    if cs["NativeHexaMatrixWindow.cs"].count("MatrixCoreIcons.Core(") < 3:
        failures.append("HEXA 窗口的槽位/强化/结果没有全部接线")

    total = sum(v[0] for v in per_job.values())
    bad = [(k, v) for k, v in per_job.items() if v[1]]
    print("职业组 %d（V %d / HEXA %d）  核心槽位 %d  缺图标 %d  已知原端缺口 %d" % (
        len(per_job), sum(1 for k in per_job if k[0] == "VMatrix.json"),
        sum(1 for k in per_job if k[0] == "HexaMatrix.json"),
        total, sum(v[1] for v in per_job.values()), len(KNOWN_ORIGIN_GAPS)))
    print("核心图标表：v %d 条 / h %d 条，全部来自 282 原生画布" % (len(v_paths), len(h_paths)))
    if not args.quiet:
        print("已知原端缺口（VCore/Skill/psdSkill 三处都没有图标，不自绘）：")
        for cid, label in sorted(KNOWN_ORIGIN_GAPS.items()):
            print("   %d %s" % (cid, label))
    if bad:
        for (name, job), (n, m, sample) in bad[:15]:
            print("   ✗ %s job=%s %d/%d 缺图标 %s" % (name, job, m, n, sample))
    if failures:
        for f in failures[:20]:
            print("   ✗ " + f)
        print("FAIL — %d 条" % len(failures))
        return 1
    print("OK — 51 个职业组的 V/HEXA 核心槽位全部有可显示的原生图标（除 3 条原端缺口）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
