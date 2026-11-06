#!/usr/bin/env python3
"""Build a portable resource pack from the external native Data directory.

资源包可以按大小切成多个**互相独立**的分包（默认每包 2 GiB）：

    MapleDashResources.part01.zip
    MapleDashResources.part02.zip
    ...

每个分包都是结构完整的 ZIP（手机端逐包解压即可，不需要先拼接成整包，
因此单个分包不会被 Android/Mono 的 ZIP64 大文件解析拖累）：

    - 每个分包根目录都有 resource-pack.json，写明 format/kind/index/count；
    - 只有第 1 包（part01）带 resource-manifest.json（资源清单）；
    - 增量补丁包另在第 1 包带 resource-patch.json 标记。

不切分包时行为不变：只输出 MapleDashResources.zip（仍附带 resource-pack.json，
老版本 APK 不认识这个额外条目，它只会遍历 Data/，因此保持兼容）。
"""
import argparse
import hashlib
import json
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED

ROOT = Path(__file__).resolve().parents[1]
INFO_ENTRY = "resource-pack.json"
MANIFEST_ENTRY = "resource-manifest.json"
PATCH_ENTRY = "resource-patch.json"
# 收盘时还要追写 resource-pack.json 和 ZIP 中央目录：5.6 万条目约 5.6 MiB，
# 这里留 16 MiB 余量，保证最终文件仍然小于 part-size。
TAIL_RESERVE = 16 * 1024 * 1024
COMPRESS_LEVEL = 1


def parse_size(text):
    """接受 2G / 512M / 2147483648 / 0（0 表示不切分包）。"""
    value = str(text).strip().lower().replace("ib", "").replace("i", "")
    multiplier = 1
    if value.endswith("g"):
        multiplier, value = 1024 ** 3, value[:-1]
    elif value.endswith("m"):
        multiplier, value = 1024 ** 2, value[:-1]
    elif value.endswith("k"):
        multiplier, value = 1024, value[:-1]
    return int(float(value) * multiplier)


class SplitWriter:
    """按大小上限顺序写若干个独立 ZIP；分包的清单只写进第 1 包。"""

    def __init__(self, output, part_size, kind, header_entries):
        self.output = output
        self.part_size = part_size          # 0 表示不切分包
        self.kind = kind
        self.header = list(header_entries)  # [(arcname, bytes)]，只写进第 1 包
        self.parts = []                     # [最终文件名]
        self.index = 0
        self.raw = None
        self.archive = None
        self.data_entries = 0               # 当前分包已经写入的 Data/ 条目数

    # —— 路径与命名 ——
    @property
    def stem(self):
        name = self.output.name
        return name[:-4] if name.lower().endswith(".zip") else name

    def path_for(self, index):
        if not self.part_size:
            return self.output
        return self.output.with_name("%s.part%02d.zip" % (self.stem, index))

    # —— 分包切换 ——
    def open_part(self):
        self.close_part()
        self.index += 1
        final = self.path_for(self.index)
        temp = final.with_name(final.name + ".partial")
        if temp.exists():
            temp.unlink()
        self.temp = temp
        self.raw = open(temp, "wb")
        self.archive = ZipFile(self.raw, "w", compression=ZIP_DEFLATED, compresslevel=COMPRESS_LEVEL,
                              allowZip64=True)
        if self.index == 1:
            for arcname, payload in self.header:
                self.archive.writestr(arcname, payload)
        self.data_entries = 0
        self.parts.append(final)

    def close_part(self):
        if self.archive is None:
            return
        self.archive.close()
        self.raw.close()
        self.archive = self.raw = None

    # —— 写入 ——
    def tail_reserve(self):
        if not self.part_size:
            return 0
        return min(TAIL_RESERVE, max(64 * 1024, self.part_size // 16))

    def need_rollover(self, size):
        if not self.part_size or self.data_entries == 0:
            return False
        # 用未压缩大小当压缩后大小的上界估计，避免最后一个文件把分包顶穿。
        return self.raw.tell() + self.tail_reserve() + size >= self.part_size

    def add_file(self, source, arcname, size):
        if self.need_rollover(size):
            self.open_part()
        self.archive.write(source, arcname)
        self.data_entries += 1

    def finish(self, manifest_sha256, total_files):
        self.close_part()
        total = len(self.parts)
        for number, final in enumerate(self.parts, start=1):
            temp = final.with_name(final.name + ".partial")
            info = {
                "format": 1,
                "kind": self.kind,
                "index": number,
                "count": total,
                "partSize": self.part_size or 0,
                "manifestSha256": manifest_sha256,
                "totalFiles": total_files,
            }
            with ZipFile(temp, "a", compression=ZIP_DEFLATED, compresslevel=COMPRESS_LEVEL,
                         allowZip64=True) as archive:
                archive.writestr(INFO_ENTRY, json.dumps(info, separators=(",", ":"), ensure_ascii=False))
            if self.part_size and temp.stat().st_size > self.part_size:
                raise SystemExit("分包 %s 超过上限 %.2f GiB：单个资源文件可能大于上限，请调大 --part-size"
                                 % (final.name, self.part_size / 1024 ** 3))
            if final.exists():
                final.unlink()
            temp.replace(final)
        # 只切成一段时用整包名（MapleDashResources.zip / .patch.zip）：
        # 没超上限就没必要叫 part01，手机端和 resUpdate.json 仍按单包处理。
        if total == 1 and self.parts[0] != self.output:
            if self.output.exists():
                self.output.unlink()
            self.parts[0].replace(self.output)
            self.parts = [self.output]


def remove_previous(output):
    """清掉上一次的产物：留着旧分包会让「包数」和磁盘上的文件对不上。"""
    stem = output.name[:-4] if output.name.lower().endswith(".zip") else output.name
    for path in output.parent.glob(stem + ".part*.zip"):
        path.unlink()
    for path in output.parent.glob(stem + ".part*.zip.partial"):
        path.unlink()
    for path in (output, output.with_name(output.name + ".partial")):
        if path.exists():
            path.unlink()


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--resources", type=Path, default=ROOT.parent / "MapleDashResources")
    parser.add_argument("--base", type=Path, help="Previous resource-manifest.json; emit a delta pack")
    parser.add_argument("--part-size", default="2G",
                        help="单个分包大小上限，默认 2G；传 0 表示不切分包")
    args = parser.parse_args()

    part_size = parse_size(args.part_size)
    manifest = (ROOT / "Assets/Resources/NativeAssetIndex.json").read_bytes()
    index = json.loads(manifest)
    baseline = args.base.read_bytes() if args.base else None
    previous = {f["path"]: f["sha256"] for f in json.loads(baseline)["files"]} if baseline else {}
    changed = [f for f in index["files"] if previous.get(f["path"]) != f["sha256"]]
    output = args.resources / ("MapleDashResources.patch.zip" if baseline else "MapleDashResources.zip")
    remove_previous(output)

    header = [(MANIFEST_ENTRY, manifest)]
    if baseline:
        header.append((PATCH_ENTRY, json.dumps({"format": 1,
            "baseManifestSha256": hashlib.sha256(baseline).hexdigest()}).encode("utf-8")))
    writer = SplitWriter(output, part_size, "patch" if baseline else "full", header)
    writer.open_part()
    for i, entry in enumerate(changed):
        source = args.resources / "Data" / entry["path"]
        if hashlib.sha256(source.read_bytes()).hexdigest() != entry["sha256"]:
            raise ValueError("Resource hash mismatch: %s" % source)
        writer.add_file(source, "Data/" + entry["path"], source.stat().st_size)
        if i % 5000 == 0:
            print("Packing %d/%d" % (i, len(index["files"])), flush=True)
    writer.finish(hashlib.sha256(manifest).hexdigest(), len(index["files"]))

    # A separate snapshot avoids silently replacing the caller's baseline.
    snapshot = output.with_suffix(".manifest.json")
    snapshot.write_bytes(manifest)
    if not baseline:
        (args.resources / "resource-manifest.json").write_bytes(manifest)
    print("Packed %d/%d files into %d part(s); snapshot: %s"
          % (len(changed), len(index["files"]), len(writer.parts), snapshot))
    for path in writer.parts:
        print("  %-40s %8.2f GiB" % (path.name, path.stat().st_size / 1024 ** 3))
    if part_size:
        print("Part limit: %.2f GiB" % (part_size / 1024 ** 3))


if __name__ == "__main__":
    main()
