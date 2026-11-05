#!/usr/bin/env python3
"""Read-only source audit for the project's TMS282 advancement migration.

Exports evidence, never edits the source client/server or gameplay implementation.
Lua decoding follows orange-wz WzLuaProperty (CMS IV, even on BMS IMG trees).
"""
import argparse
import hashlib
import io
import json
from pathlib import Path
import struct
import sys
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[2]
MXD = ROOT.parent.parent
sys.path.insert(0, str(MXD / "Beidou-Server/tool/wz-python"))
from wzpy import WzImage, WzKey
from wzpy.reader import WzBinaryReader

QUESTS = sorted(set(range(1460, 1466)) | {1478, 1481} | set(range(38601, 38626)))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def tree(node):
    children = node.children()
    return {c.name: tree(c) for c in children} if children else node.value


def bool_constant(raw, target):
    """Read a JVM field ConstantValue; verifies deployed code independently of Java source."""
    if raw[:4] != b"\xca\xfe\xba\xbe":
        raise ValueError("Not a Java class")
    pos, pool = 10, [None]
    count = int.from_bytes(raw[8:10], "big")
    while len(pool) < count:
        tag = raw[pos]
        pos += 1
        if tag == 1:
            size = int.from_bytes(raw[pos:pos + 2], "big")
            pos += 2
            value = raw[pos:pos + size].decode("utf-8", "replace")
        else:
            size = {3: 4, 4: 4, 5: 8, 6: 8, 7: 2, 8: 2, 9: 4, 10: 4,
                    11: 4, 12: 4, 15: 3, 16: 2, 17: 4, 18: 4, 19: 2, 20: 2}[tag]
            value = int.from_bytes(raw[pos:pos + size], "big")
        pos += size
        pool.append(value)
        if tag in (5, 6):
            pool.append(None)
    pos += 6  # access flags, this class, super class
    interfaces = int.from_bytes(raw[pos:pos + 2], "big")
    pos += 2 + interfaces * 2
    fields = int.from_bytes(raw[pos:pos + 2], "big")
    pos += 2
    for _ in range(fields):
        _, name, desc, attrs = struct.unpack_from(">HHHH", raw, pos)
        pos += 8
        for _ in range(attrs):
            attr, size = struct.unpack_from(">HI", raw, pos)
            pos += 6
            value = raw[pos:pos + size]
            pos += size
            if pool[name] == target and pool[desc] == "Z" and pool[attr] == "ConstantValue":
                return bool(pool[int.from_bytes(value, "big")])
    raise ValueError(f"No boolean ConstantValue for {target}")


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--client", type=Path, default=MXD / "MapleStory")
    ap.add_argument("--server", type=Path, default=MXD / "TMS282.5")
    ap.add_argument("--scan-all-maps", action="store_true")
    args = ap.parse_args()
    data = args.client / "Data"
    outdir = ROOT.parent / "docs/tms282-skill/data/advanced-quest-evidence"
    outdir.mkdir(parents=True, exist_ok=True)
    hashes = {}

    def image(rel, only=None):
        path = data / rel
        hashes[rel] = digest(path)
        img = WzImage.from_file(str(path), key=WzKey.for_region("BMS"))
        # Do not let wzpy's non-Property => empty-root fallback hide unsupported containers.
        with path.open("rb") as stream:
            if stream.read(1) != b"s":
                raise ValueError(f"Not a Property IMG: {rel}")
        node = img.parse_partial(only=frozenset(only)) if only else img.parse()
        if img.truncated or img.parse_warnings:
            raise ValueError(f"Incomplete IMG {rel}: {img.parse_warnings}")
        return node

    quests, scripts = {}, set()
    for qid in QUESTS:
        rel = f"Quest/QuestData/{qid}.img"
        if not (data / rel).exists():
            quests[str(qid)] = {"missingImage": True}
            continue
        node = image(rel)
        quests[str(qid)] = tree(node)
        for phase in ("0", "1"):
            check = node.get("Check").get(phase)
            if check:
                for key in ("startscript", "endscript"):
                    script = check.get(key)
                    if script and script.value:
                        scripts.add(("quest", script.value))

    # An explicit NPC anchor scan is reproducible; a focused rerun avoids rescanning 29k unrelated maps.
    stone_ids = range(2636387, 2636394)
    consume = image("Item/Consume/0263.img", [f"{sid:08}" for sid in stone_ids])
    strings = image("String/Consume.img", [str(sid) for sid in stone_ids])
    stones = {}
    for sid in stone_ids:
        node = consume.get(f"{sid:08}")
        stones[str(sid)] = {"item": tree(node), "string": tree(strings.get(str(sid)))}
        script = node.get("spec").get("script")
        if script and script.value:
            scripts.add(("npc", script.value))

    npcs = {}
    for nid in range(1540941, 1540945):
        node = image(f"Npc/{nid:07}.img", ["info"])
        npcs[str(nid)] = tree(node)
        info = node.get("info")
        script_nodes = info.get("script") if info else None
        for entry in script_nodes.children() if script_nodes else []:
            script = entry.get("script")
            if script and script.value:
                scripts.add(("npc", script.value))

    anchors = {270010111, 100000201, 400000001, 105300000, 450000000, 450000010, 450000020} | set(range(993217480, 993217486))
    scanned = 0
    if args.scan_all_maps:
        anchors = set()
        targets = {str(x) for x in range(3006370, 3006390)} | {"2140001"} | {str(x) for x in range(1540941, 1540945)}
        for path in sorted((data / "Map/Map").rglob("*.img")):
            if not path.stem.isdecimal():
                continue
            scanned += 1
            img = WzImage.from_file(str(path), key=WzKey.for_region("BMS"))
            root = img.parse_partial(only=frozenset(["life"]))
            if img.truncated or img.parse_warnings:
                raise ValueError(f"Incomplete map scan: {path}")
            life = root.get("life")
            if life and any(n.get("id") and str(n.get("id").value) in targets for n in life.children()):
                anchors.add(int(path.stem))
    graph = image("Map/Map/Graph.img")
    graph_nodes = {n.name: n for group in graph.children() for n in group.children()}
    # Public legacy scripts suggest intermediate fields; inspect current-client data independently.
    pending, maps = set(anchors) | {450000100, 450000110, 450000120}, {}
    while pending:
        mid = pending.pop()
        if str(mid) in maps:
            continue
        rel = f"Map/Map/Map{mid // 100000000}/{mid}.img"
        root = image(rel, ["info", "life", "portal"])
        rec = tree(root)
        gn = graph_nodes.get(str(mid))
        rec["graph"] = tree(gn) if gn else None
        maps[str(mid)] = rec
        info = root.get("info")
        for field in ("onUserEnter", "onFirstUserEnter"):
            n = info.get(field) if info else None
            if n and n.value:
                scripts.add(("field", n.value))
        portal = root.get("portal")
        for n in portal.children() if portal else []:
            script = n.get("script")
            if script and script.value:
                scripts.add(("portal", script.value))
        portals = gn.get("portal") if gn else None
        for n in portals.children() if portals else []:
            dest = n.get("targetMap")
            # Only the local story graph closure, never claim graph alternatives establish branch conditions.
            if dest and str(dest.value).startswith("993217") and str(dest.value) not in maps:
                pending.add(int(dest.value))

    lua = {}
    for path in sorted((data / "Etc/Script").glob("*.lua")):
        raw = path.read_bytes()
        reader = WzBinaryReader(io.BytesIO(raw), WzKey.for_region("BMS"))
        if reader.read_byte() != 1:
            raise ValueError(f"Unsupported Lua header {path}")
        length = reader.read_compressed_int()
        if len(raw) != length + 6:
            raise ValueError(f"Unexpected Lua length {path}")
        key = WzKey.for_region("EMS").slice(length)  # CMS IV B9 7D 63 E9, orange-wz nomenclature
        decoded = bytes(a ^ b for a, b in zip(raw[-length:], key))
        text = decoded.decode("utf-8-sig")
        (outdir / (path.name + ".txt")).write_bytes(decoded)
        lua[path.name] = {"sourceSha256": digest(path), "decodedSha256": hashlib.sha256(decoded).hexdigest(),
                          "bytes": length, "lines": len(text.splitlines()),
                          "questScriptHits": [name for _, name in sorted(scripts) if name in text]}

    effects = {}
    for rel in ("Effect/Direction35.img", "Effect/Direction36.img"):
        node = image(rel).get("6thSkill_story")
        effects[rel] = {"groups": [c.name for c in node.children()]} if node else None

    script_files = sorted(str(p.relative_to(args.server)) for p in (args.server / "data/scripts").rglob("*") if p.is_file())
    missing = [{"type": typ, "name": name} for typ, name in sorted(scripts)
               if not any((args.server / f"data/scripts/{typ}/{name}{ext}").is_file() for ext in (".py", ".js"))]
    jar = args.server / "bin/maplestory-282.5-local-obfuscated.jar"
    jar_hits = {}
    with ZipFile(jar) as z:
        names = z.namelist()
        game_scripts = [n for n in names if n.endswith((".py", ".js", ".lua"))
                        and not n.startswith(("Lib/", "org/", "com/", "jdk/", "META-INF/"))]
        enabled = bool_constant(z.read("net/swordie/ms/scripts/ScriptManagerImpl.class"), "GAME_SCRIPTS_ENABLED")
        needles = {name: name.encode() for _, name in scripts}
        for name in names:
            if name.endswith(".class") and name.startswith("net/swordie/"):
                raw = z.read(name)
                for script, needle in needles.items():
                    if needle in raw:
                        jar_hits.setdefault(script, []).append(name)
    source_root = args.server / "source/src/main/java/net/swordie/ms"
    source_evidence = [args.server / "套件驗證.txt", args.server / "先看我.txt",
                       source_root / "ServerConstants.java", source_root / "scripts/ScriptManagerImpl.java",
                       source_root / "handlers/login/tms282/Tms282QuestFlow.java",
                       source_root / "handlers/script/QuestHandler.java", source_root / "enums/QuestStatus.java"]
    report = {"clientExeSha256": digest(args.client / "MapleStory.exe"),
              "serverJarSha256": digest(jar), "resourceSha256": hashes,
              "serverEvidenceSha256": {str(p.relative_to(args.server)): digest(p) for p in source_evidence},
              "quests": quests, "npcs": npcs, "questStones": stones,
              "npcAnchorScanCount": scanned, "npcAnchorMaps": sorted(anchors),
              "maps": maps, "effects": effects, "lua": lua,
              "serverScriptFiles": script_files, "missingScriptFiles": missing,
              "jarGameScriptResources": game_scripts, "jarLiteralScriptHits": jar_hits,
              "jarGameScriptsEnabled": enabled,
              "boundary": "Graph candidates and assets do not establish script branching, dialog or scene ordering; no gameplay migration performed"}
    path = outdir / "audit.json"
    path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n")
    present = sum(not q.get("missingImage", False) for q in quests.values())
    print(f"questImages={present}/{len(quests)} anchorMaps={len(anchors)} graphClosureMaps={len(maps)}")
    print(f"scriptReferences={len(scripts)} missingFiles={len(missing)} jarGameScriptsEnabled={enabled}")
    print(f"jarGameScriptResources={len(game_scripts)} literalScriptHits={len(jar_hits)}")
    print(path)


if __name__ == "__main__":
    main()
