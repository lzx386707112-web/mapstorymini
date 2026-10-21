#!/usr/bin/env python3
"""Export read-only native skill levels for the existing twenty skill slots.
Client WZ supplies levels; server StatEffect / handlers supply behavior classification.
No source assets are changed. Run from any working directory.
"""
import json
import re
import hashlib
from pathlib import Path
import wzmap

ROOT = Path(__file__).resolve().parents[1]
KINDS = {"crush": "Dispel", "rage": "Buff", "magnet": "Pull", "libra": "Summon"}

def export():
    manifest = json.loads((ROOT / "Assets/Resources/Art/manifest.json").read_text())
    manifest["skills"]["illusion_slash"] = {"id": "1121021"}
    manifest["skills"]["illusion_explosion"] = {"id": "1121022"}
    skills = []
    for key, entry in manifest["skills"].items():
        sid = entry["id"]
        node = wzmap._wz("Skill/" + str(int(sid) // 10000).zfill(3)).get("skill/" + sid)
        levels = []
        for level in node.get("level").children():
            values = {"level": int(level.name)}
            for child in level.children():
                value = getattr(child, "value", None)
                if isinstance(value, (int, float, str)):
                    values[child.name] = value
                elif child.name in ("lt", "rb"):
                    values[child.name] = list(value)
            levels.append(values)
        interval = 0.0
        attack = node.get("summon/attack1")
        if attack:
            interval = sum(abs(getattr(frame.get("delay"), "value", 0)) for frame in attack.children()
                           if frame.name.isdigit()) / 1000.0
            after = node.get("summon/attack1/info/attackAfter")
            interval += getattr(after, "value", 0) / 1000.0
        skills.append({"element": getattr(node.get("elemAttr"),"value",""), "summonInterval": interval, "key": key, "id": int(sid), "kind": KINDS.get(key, "Attack"),
                       "magic": key in ("firearrow", "doom", "chain", "starfall", "holyarrow", "holy", "ray", "libra"),
                       "levels": sorted(levels, key=lambda v: v["level"])})
    target = ROOT / "Assets/Resources/GameplaySkills.json"
    target.write_text(json.dumps({"skills": skills}, ensure_ascii=False, indent=2) + "\n")
    server = Path(wzmap.DATA).parents[1] / "gms-server"
    sql_path = server / "src/main/resources/db/migration/V1.0.51__drop_data_insert_old_data.sql"
    sql = sql_path.read_text()
    meso_rows = {}
    for row in re.findall(r"\((\d+),\s*0,\s*(\d+),\s*(\d+),\s*0,\s*(\d+)\)", sql):
        mob, minimum, maximum, chance = map(int, row)
        meso_rows[mob] = {"minimum": minimum, "maximum": maximum, "chance": chance}
    mobs = []
    ids = {"snail": 100100, "slime": 210100, "mushroom": 1210102, "greenmush": 1110100,
           "pig": 1210100, "mushmom": 6130101, "slimeking": 9300003}
    for key, mob_id in ids.items():
        info = wzmap._wz("Mob/" + str(mob_id).zfill(7)).get("info")
        mobs.append({"key": key, "id": mob_id, "exp": getattr(info.get("exp"), "value", 0),
                     "meso": meso_rows.get(mob_id, {"minimum": 0, "maximum": 0, "chance": 0})})
    (ROOT / "Assets/Resources/GameplayRewards.json").write_text(json.dumps({
        "source": str(sql_path.relative_to(server)), "sha256": hashlib.sha256(sql_path.read_bytes()).hexdigest(),
        "mobs": mobs}, indent=2) + "\n")
    print(f"Exported {len(skills)} skills / {sum(len(s['levels']) for s in skills)} levels to {target}")

if __name__ == "__main__":
    export()
