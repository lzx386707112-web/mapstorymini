#!/usr/bin/env python3
"""Validate a resource pack (single ZIP or split parts) before shipping it to the phone.

检查项：
  - 结构：根目录有 resource-manifest.json、文件入口一律是 Data/<相对路径>、没有顶层包裹目录；
  - 分包：每个分包都能打开、都带 resource-pack.json、包号从 1 连续到 count、包数与磁盘一致；
  - 体积：每个分包不超过上限（默认 2 GiB）；
  - 完整性：清单里的每个文件都在某个分包里，且没有清单外的多余文件；
  - 一致性：各分包声明的 manifestSha256 与实际清单内容相同。

默认不读文件内容（9 GiB 全读一遍太慢）；加 --deep 会逐条解压校验 CRC/长度。
"""
import argparse
import hashlib
import json
import sys
from pathlib import Path
from zipfile import ZipFile, BadZipFile

GiB = 1024 ** 3
INFO_ENTRY = "resource-pack.json"
MANIFEST_ENTRY = "resource-manifest.json"
PATCH_ENTRY = "resource-patch.json"


def parse_size(text):
    value = str(text).strip().lower().replace("ib", "").replace("i", "")
    multiplier = 1
    if value.endswith("g"):
        multiplier, value = GiB, value[:-1]
    elif value.endswith("m"):
        multiplier, value = 1024 ** 2, value[:-1]
    elif value.endswith("k"):
        multiplier, value = 1024, value[:-1]
    return int(float(value) * multiplier)


def _numbered(resources, prefix):
    """<prefix>NN.zip -> {包号: 路径}"""
    found = {}
    for path in resources.glob(prefix + "*.zip"):
        suffix = path.name[len(prefix):-len(".zip")]
        if suffix.isdigit():
            found[int(suffix)] = path
    return found


def discover(resources, patch=False):
    """返回 (模式, 按包号排序的包路径)。默认先找全量包，再退回增量补丁包。"""
    order = [("MapleDashResources.patch", True)] if patch else [
        ("MapleDashResources", False), ("MapleDashResources.patch", True)]
    for stem, is_patch in order:
        parts = _numbered(resources, stem + ".part")
        if parts:
            return ("split-patch" if is_patch else "split"), [parts[i] for i in sorted(parts)]
        single = resources / (stem + ".zip")
        if single.is_file():
            return ("single-patch" if is_patch else "single"), [single]
    return None, []


def scan(path, deep, problems):
    result = {"path": path, "size": path.stat().st_size, "data": set(),
              "manifest": None, "info": None, "patch": False}
    try:
        with ZipFile(path) as archive:
            names = archive.namelist()
            if MANIFEST_ENTRY in names:
                result["manifest"] = archive.read(MANIFEST_ENTRY)
            result["patch"] = PATCH_ENTRY in names
            result["data"] = {n for n in names if n.startswith("Data/") and not n.endswith("/")}
            if INFO_ENTRY in names:
                result["info"] = json.loads(archive.read(INFO_ENTRY))
            if deep:
                for entry in archive.infolist():
                    if entry.is_dir():
                        continue
                    with archive.open(entry) as stream:
                        while stream.read(1 << 20):
                            pass
    except BadZipFile as error:
        problems.append("%s 不是有效 ZIP：%s" % (path.name, error))
    except (OSError, ValueError) as error:
        problems.append("%s 读取失败：%s" % (path.name, error))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--resources", type=Path,
                        default=Path(__file__).resolve().parents[2] / "MapleDashResources")
    parser.add_argument("--limit", default="2G", help="单个分包大小上限，默认 2G；0 表示不检查")
    parser.add_argument("--patch", action="store_true", help="校验增量补丁包（只查变更文件，不要求清单全覆盖）")
    parser.add_argument("--deep", action="store_true", help="逐条解压校验内容（很慢）")
    args = parser.parse_args()
    limit = parse_size(args.limit)

    mode, paths = discover(args.resources, args.patch)
    if not paths:
        print("找不到资源包：%s 下没有 %s" % (args.resources,
              "MapleDashResources.patch.zip / .patch.partNN.zip" if args.patch
              else "MapleDashResources.zip / .partNN.zip"))
        return 1
    split = mode.startswith("split")
    is_patch = mode.endswith("patch")
    print("资源包模式: %s，磁盘上 %d 个文件" % (mode, len(paths)))

    problems = []
    parts = [scan(path, args.deep, problems) for path in paths]
    for part in parts:
        note = "清单" if part["manifest"] is not None else ""
        note += "+补丁标记" if part["patch"] else ""
        print("  %-34s %8.2f GiB  Data 条目 %5d  %s"
              % (part["path"].name, part["size"] / GiB, len(part["data"]), note))
        if limit and part["size"] > limit:
            problems.append("%s 超过上限（%.2f GiB > %.2f GiB）"
                            % (part["path"].name, part["size"] / GiB, limit / GiB))

    # —— 分包元数据 ——
    if split:
        if any(part["info"] is None for part in parts):
            problems.append("有分包缺少 %s（不是分包脚本产出的，或传输时被覆盖）" % INFO_ENTRY)
        else:
            counts = {part["info"].get("count") for part in parts}
            if len(counts) != 1:
                problems.append("各分包声明的包数不一致：%s" % sorted(counts))
            else:
                count = counts.pop()
                if count != len(paths):
                    problems.append("分包声明 %d 包，磁盘上实际 %d 包（缺包或旧包残留）"
                                    % (count, len(paths)))
            indexes = [part["info"].get("index") for part in parts]
            if sorted(indexes) != list(range(1, len(paths) + 1)):
                problems.append("包号不是 1..%d 连续：%s" % (len(paths), indexes))
            kinds = {part["info"].get("kind") for part in parts}
            if len(kinds) != 1:
                problems.append("各分包声明的类型不一致：%s" % sorted(kinds))
            elif kinds != {"patch" if is_patch else "full"}:
                problems.append("分包声明的类型与文件名不符：%s（应为 %s）"
                                % (sorted(kinds), "patch" if is_patch else "full"))
            shas = {part["info"].get("manifestSha256") for part in parts}
            if len(shas) != 1:
                problems.append("各分包声明的清单摘要不一致（混了不同版本的包）")
        holders = [part["path"].name for part in parts if part["manifest"] is not None]
        if len(holders) != 1:
            problems.append("分包里带清单的文件应恰好 1 个，实际 %s" % (holders or "0 个"))
        elif holders[0] != paths[0].name:
            problems.append("清单应该在包号最小的 %s 里，实际在 %s" % (paths[0].name, holders[0]))

    # —— 清单与 Data 条目对齐 ——
    manifest = next((part["manifest"] for part in parts if part["manifest"] is not None), None)
    if manifest is None:
        problems.append("找不到 %s：手机端第一步就会报『资源包缺少清单』" % MANIFEST_ENTRY)
    else:
        index = json.loads(manifest)
        expected = {"Data/" + entry["path"] for entry in index["files"]}
        found = set()
        duplicated = []
        for part in parts:
            overlap = found & part["data"]
            if overlap:
                duplicated.append("%s 重复 %d 个" % (part["path"].name, len(overlap)))
            found |= part["data"]
        if duplicated:
            problems.append("文件在多个分包里重复：%s" % "；".join(duplicated))
        missing, extra = expected - found, found - expected
        if is_patch:
            # 补丁只带新增/修改文件，缺的那些就是未变化的旧文件，不算错。
            print("补丁变更条目: %d（清单共 %d 条，其余为未变化文件）" % (len(found), len(expected)))
        else:
            print("清单条目: %d，各分包实际合计: %d" % (len(expected), len(found)))
            if missing:
                problems.append("清单里有 %d 个文件不在任何分包中，例如 %s"
                                % (len(missing), sorted(missing)[:5]))
        if extra:
            problems.append("分包里有 %d 个文件不在清单中，例如 %s"
                            % (len(extra), sorted(extra)[:5]))
        digest = hashlib.sha256(manifest).hexdigest()
        declared = {part["info"].get("manifestSha256") for part in parts if part["info"]}
        if declared and declared != {digest}:
            problems.append("分包声明的 manifestSha256 与实际清单不一致")
        wrapped = sorted(n for n in found if n.startswith("Data/MapleDashResources/"))
        if wrapped:
            problems.append("出现顶层包裹目录 MapleDashResources/（手机端解压后路径全错），例如 %s"
                            % wrapped[:3])
        if is_patch:
            marked = [part["path"].name for part in parts if part["patch"]]
            if marked != [paths[0].name]:
                problems.append("增量包缺少 %s 标记，或标记不在第 1 包：%s"
                                % (PATCH_ENTRY, marked or "0 个"))

    if problems:
        print("\n校验失败：")
        for problem in problems:
            print("  - " + problem)
        return 1
    print("\n校验通过")
    return 0


if __name__ == "__main__":
    sys.exit(main())
