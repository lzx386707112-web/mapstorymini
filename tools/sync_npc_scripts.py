#!/usr/bin/env python3
"""把源端 NPC / 任务脚本同步进客户端。

数据来源（优先级从高到低，空文件视为缺失）：
    BeiDou-Server/gms-server/scripts-zh-CN/{npc,quest}/*.js
    BeiDou-Server/gms-server/scripts/{npc,quest}/*.js

输出两份，运行期都能读到：
    1) MapleDashResources/Data/scripts/{npc,quest}/{id}.js   —— 编辑器/桌面直接读文件，方便改
    2) MapleDash/Assets/Resources/Scripts/npc_scripts.json   —— 打包进 Resources，
       Android / WebGL 走这一份（不依赖 /sdcard 上是否安装了脚本文件）

脚本按原样复制，不做任何改写：源端行为要一致，就不能在同步阶段动手脚。
"""
import argparse
import json
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]          # MapleDash/
REPO = ROOT.parent                                   # mxd-android/
DEFAULT_SERVER = REPO.parent / "BeiDou-Server"
KINDS = ("npc", "quest")


def decode(path: Path) -> str:
    """源端脚本编码混杂：多数是 UTF-8，少数老脚本是 GBK（中文注释/对白）。

    按 utf-8 → gb18030 顺序严格尝试，都不行才用替换字符兜底。以前一律用
    errors="replace" 会把 GBK 中文变成 U+FFFD，进游戏就是一堆乱码对白。
    """
    raw = path.read_bytes()
    for enc in ("utf-8-sig", "gb18030"):
        try:
            return raw.decode(enc)
        except UnicodeDecodeError:
            continue
    return raw.decode("utf-8", errors="replace")


def read_scripts(base: Path, kind: str):
    folder = base / kind
    if not folder.is_dir():
        return {}
    out = {}
    for path in folder.glob("*.js"):
        stem = path.stem
        # 以 "-" 开头的（如 quest/-31069.js）在 Quest.wz 里查无此 id，是 TMS 遗留内容，
        # 客户端永远走不到；其余自定义名（gachapon、changeName、坐骑领取…）由
        # cm.openNpc(id, 名字) 一类调用触发，必须一起带上。
        if stem.startswith("-"):
            continue
        try:
            text = decode(path)
        except OSError:
            continue
        if text.strip():
            out[stem] = text
    return out


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--server", type=Path, default=DEFAULT_SERVER)
    parser.add_argument("--resources", type=Path, default=REPO / "MapleDashResources")
    parser.add_argument("--assets", type=Path, default=ROOT / "Assets")
    parser.add_argument("--clean", action="store_true", help="删除目标目录里已不在源端的脚本")
    args = parser.parse_args()

    zh_root = args.server / "gms-server" / "scripts-zh-CN"
    en_root = args.server / "gms-server" / "scripts"
    if not zh_root.is_dir() and not en_root.is_dir():
        raise SystemExit(f"找不到源端脚本目录: {zh_root}")

    packed = {}
    total_bytes = 0
    for kind in KINDS:
        en = read_scripts(en_root, kind)
        zh = read_scripts(zh_root, kind)
        merged = dict(en)                       # 英文兜底
        merged.update(zh)                       # 中文覆盖
        if not merged:
            continue

        dest = args.resources / "Data" / "scripts" / kind
        dest.mkdir(parents=True, exist_ok=True)
        if args.clean:
            for old in dest.glob("*.js"):
                if old.stem not in merged:
                    old.unlink()
        for key, text in merged.items():
            (dest / f"{key}.js").write_text(text, encoding="utf-8")
            packed[f"{kind}/{key}"] = text
            total_bytes += len(text.encode("utf-8"))

        print(f"{kind}: zh={len(zh)} en={len(en)} -> {len(merged)}  中文覆盖 "
              f"{len(set(zh) & set(en))}/{len(en)}")

    out_dir = args.assets / "Resources" / "Scripts"
    out_dir.mkdir(parents=True, exist_ok=True)
    out = out_dir / "npc_scripts.json"
    out.write_text(json.dumps(packed, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"scripts={len(packed)} packed={out.stat().st_size/1048576:.2f} MiB -> {out}")


if __name__ == "__main__":
    main()
