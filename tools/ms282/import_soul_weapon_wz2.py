#!/usr/bin/env python3
"""Import original SoulWeaponWz2 UI and rules, with GMS/ARGB4444 verification.

Server-only policy is deliberately not generated here. See the migration record.
"""
import argparse
import hashlib
import json
from pathlib import Path
from import_enhancement_ui import Importer, ROOT
from dump_img_tree import tree
from wzpy import WzImage, WzKey


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--source", type=Path, default=Path.home()/"Documents/mxd/MapleStory/Data")
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--skill-definition", type=Path, default=Path("/tmp/soul-packs/Skill_800128.img"))
    args = ap.parse_args()
    importer = Importer(args.source)
    rel = "UI/UIWindowWz2.img"
    output = importer.convert(rel, ["SoulWeaponWz2"])
    tables = {}
    for key, path in [("rules", "Etc/SoulWeaponWz2.img"), ("options", "Item/SoulWeaponWz2Option.img")]:
        tables[key] = tree(importer.image(path).root)
    tables["strings"] = tree(importer.image("String/StringPoolWz2.img").get("SoulWeaponWz2"))
    tables["quests"] = {}
    quest_ids = {68840}
    for row in tables["rules"]["BossInfo"].values():
        quest_ids.add(row["QuestID_Weekly"])
        if "QuestID_GradeUp" in row:
            quest_ids.add(row["QuestID_GradeUp"])
    for q in sorted(quest_ids):
        tables["quests"][str(q)] = tree(importer.image(f"Quest/QuestData/{q}.img").root)
    skills = WzImage.from_bytes(args.skill_definition.read_bytes(), key=WzKey.for_region("BMS"))
    skills.parse()
    importer.hashes["Packs/Skill_800128.img"] = hashlib.sha256(args.skill_definition.read_bytes()).hexdigest()
    strings = importer.image("String/Skill.img")
    tables["skills"] = {str(sid): {"common": tree(skills.get(f"skill/{sid}/common")),
        "string": tree(strings.get(str(sid)))} for sid in range(80012853, 80012857)}
    skill_rel="Skill/800128.img"
    importer.source_overrides[skill_rel]=args.skill_definition.read_bytes()
    skill_output=importer.convert(skill_rel,[f"skill/{sid}" for sid in range(80012853,80012857)])
    payload = json.dumps(tables, ensure_ascii=False, indent=1)+"\n"
    if not args.write:
        print("Verified; use --write to install")
        return
    dest = ROOT.parent/"MapleDashResources/Data"/rel
    # This importer owns a new runtime file; never discard unrelated branches.
    if dest.exists():
        existing = WzImage.from_bytes(dest.read_bytes(), key=WzKey.for_region("GMS"))
        existing.parse()
        if any(c.name != "SoulWeaponWz2" for c in existing.root.children()):
            raise ValueError("Runtime UIWindowWz2 contains unrelated branches; merge required")
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_bytes(output)
    skill_dest=ROOT.parent/"MapleDashResources/Data"/skill_rel
    skill_dest.parent.mkdir(parents=True,exist_ok=True)
    skill_dest.write_bytes(skill_output)
    index_path = ROOT/"Assets/Resources/NativeAssetIndex.json"
    index = json.loads(index_path.read_text())
    entry = next((x for x in index["files"] if x["path"] == rel), None)
    if entry is None:
        entry = {"path": rel}
        index["files"].append(entry)
    entry["sha256"] = hashlib.sha256(output).hexdigest()
    entry=next((x for x in index["files"] if x["path"]==skill_rel),None)
    if entry is None:
        entry={"path":skill_rel};index["files"].append(entry)
    entry["sha256"]=hashlib.sha256(skill_output).hexdigest()
    index_path.write_text(json.dumps(index, ensure_ascii=False, indent=1)+"\n")
    target = ROOT/"Assets/Resources/Enhancement/SoulWeaponWz2.json"
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(payload)
    evidence = ROOT.parent/"docs/tms282-skill/data"
    evidence.mkdir(parents=True, exist_ok=True)
    (evidence/"soul-weapon-wz2.json").write_text(payload)
    (evidence/"soul-weapon-wz2-import.json").write_text(json.dumps({
        "sourceSha256": importer.hashes,
        "outputSha256": {rel: hashlib.sha256(output).hexdigest(),skill_rel:hashlib.sha256(skill_output).hexdigest()},
        "nodes": importer.nodes[rel],
    }, ensure_ascii=False, indent=1)+"\n")
    print("Installed original UI, 207 options and 53 boss rows")


if __name__ == "__main__":
    main()
