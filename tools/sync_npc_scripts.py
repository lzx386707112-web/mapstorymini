#!/usr/bin/env python3
"""同步源端全部 JS：保存逐字节原件，并生成中文优先的运行时脚本包。

不修改脚本逻辑、不筛除负数 ID/模板/空文件。原件保存在 Data/script-sources/，
运行时 UTF-8 文本保存在 Data/scripts/ 与 Resources/Scripts/npc_scripts.json。
"""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REPO = ROOT.parent
DEFAULT_SERVER = REPO.parent / "BeiDou-Server"


def decode(raw: bytes):
    for encoding in ("utf-8-sig", "gb18030"):
        try:
            return raw.decode(encoding), encoding
        except UnicodeDecodeError:
            pass
    raise ValueError("源脚本既不是 UTF-8，也不是 GB18030；禁止替换字符后继续打包")


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def write_if_changed(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    if not path.exists() or path.read_bytes() != data:
        path.write_bytes(data)


def collect(server):
    originals, effective = {}, {}
    for language in ("scripts", "scripts-zh-CN"):
        base = server / "gms-server" / language
        for path in sorted(base.rglob("*.js")):
            relative = path.relative_to(base).as_posix()
            raw = path.read_bytes()
            text, encoding = decode(raw)
            record = {"source": language + "/" + relative, "sha256": digest(raw),
                      "encoding": encoding, "bytes": len(raw)}
            originals[record["source"]] = (raw, record)
            # 与 AbstractScriptManager 一致：中文文件存在就优先，包括空文件。
            effective[relative[:-3]] = (text, record)
    # 用户授权的本地覆盖（截图重建/明确批准的源脚本修复），独立保留原件。
    for path in sorted((ROOT / "content/script-overrides").rglob("*.js")):
        relative = path.relative_to(ROOT / "content/script-overrides").as_posix()
        raw = path.read_bytes()
        text, encoding = decode(raw)
        effective[relative[:-3]] = (text, {"source": "screenshot-overrides/" + relative,
            "sha256": digest(raw), "encoding": encoding, "bytes": len(raw)})
    return originals, effective


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--server", type=Path, default=DEFAULT_SERVER)
    parser.add_argument("--resources", type=Path, default=REPO / "MapleDashResources")
    parser.add_argument("--assets", type=Path, default=ROOT / "Assets")
    parser.add_argument("--clean", action="store_true", help="清理同步目录中源端已删除的 JS")
    parser.add_argument("--check", action="store_true", help="只核对全部原件、文本、脚本包和清单")
    args = parser.parse_args()
    originals, effective = collect(args.server)
    if not originals:
        raise SystemExit("找不到源端 JS：" + str(args.server))

    data = args.resources / "Data"
    outputs = {}
    for key, (raw, _) in originals.items():
        outputs[data / "script-sources" / key] = raw
    packed, records = {}, {}
    for key, (text, record) in effective.items():
        packed[key] = text
        raw = text.encode("utf-8")
        outputs[data / "scripts" / (key + ".js")] = raw
        records[key] = dict(record, textSha256=digest(raw))
    manifest = {"schemaVersion": 1, "originalCount": len(originals), "scriptCount": len(packed),
                "originals": {k: v[1] for k, v in originals.items()}, "scripts": records}
    bundle = args.assets / "Resources" / "Scripts"
    for path, obj in ((bundle / "npc_scripts.json", packed), (bundle / "script_manifest.json", manifest)):
        outputs[path] = (json.dumps(obj, ensure_ascii=False, sort_keys=True, separators=(",", ":")) + "\n").encode("utf-8")

    stale = []
    for folder in (data / "scripts", data / "script-sources"):
        stale.extend(p for p in folder.rglob("*.js") if p not in outputs)
    if args.check:
        bad = [str(p) for p, content in outputs.items() if not p.is_file() or p.read_bytes() != content]
        bad.extend(str(p) + " (已从源端删除)" for p in stale)
        if bad:
            raise SystemExit("脚本同步不一致：\n" + "\n".join(bad))
    else:
        for path, content in outputs.items():
            write_if_changed(path, content)
        if args.clean:
            for path in stale:
                path.unlink()
    counts = Counter(k.split("/")[0] if "/" in k else "(root)" for k in packed)
    print("originals=" + str(len(originals)) + " effective=" + str(len(packed)) + " categories=" + str(dict(sorted(counts.items()))))
    print("全部原件/运行文本/脚本包/哈希清单一致" if args.check else "已同步全部 JS（原件保持字节一致，运行文本仅转换编码）")


if __name__ == "__main__":
    main()
