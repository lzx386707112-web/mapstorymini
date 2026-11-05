#!/usr/bin/env python3
"""Audit existing-project V/HEXA scope; never modify runtime skills or IMG files."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / "Assets/Resources/UI/JobTree.json"
CATALOG = ROOT.parent / "docs/tms282-skill/data/skills-5th6th.json"
OUT = ROOT.parent / "docs/tms282-skill/data/project-advanced-scope.json"


def main():
    project = json.loads(PROJECT.read_text())
    catalog = json.loads(CATALOG.read_text())
    jobs = sorted((j for j in project["jobs"] if j["tier"] == 4), key=lambda j: j["id"])
    rows, all_skills = [], set()
    for job in jobs:
        key = str(job["id"])
        row = {"job": job["id"], "name": job["name"]}
        for prefix, layout_key, core_key in (("v", "vCoreLayout", "vCore"),
                                              ("hexa", "hexaLayout", "hexaCore")):
            # Missing groups are errors, never silently replace a project profession.
            layout = catalog[layout_key][key]
            ids = sorted({int(c) for group in layout.values() for c in group})
            skills = sorted({int(s) for c in ids for field in ("connectSkill", "subSkill")
                             for s in catalog[core_key][str(c)].get(field, [])})
            missing = [s for s in skills if str(s) not in catalog["skills"]]
            row[prefix] = {"groups": layout, "coreCount": len(ids), "directSkills": skills,
                           "missingDefinitions": missing}
            all_skills.update(skills)
        rows.append(row)
    report = {
        "inputs": {str(p.relative_to(ROOT.parent)): hashlib.sha256(p.read_bytes()).hexdigest()
                   for p in (PROJECT, CATALOG)},
        "scope": "Existing project tier-4 professions only; not all TMS282 professions",
        "jobCount": len(rows), "jobs": rows,
        "directSkillCount": len(all_skills),
        "directSkills": sorted(all_skills),
        "boundaries": [
            "Direct connectSkill/subSkill mapping only, NOT a complete executable skill closure",
            "Catalogue omits multiAttackInfo and loses nested effect metadata; re-export raw IMG before migration",
            "Old four-job skill IDs cannot be mapped to TMS282 IDs by a numeric threshold",
            "No runtime changes; no quest flow, core UI or combat behavior validated",
        ],
    }
    OUT.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n")
    print(f"jobs={len(rows)} directSkills={len(all_skills)} -> {OUT}")
    for row in rows:
        print(f"{row['job']} {row['name']}: V={row['v']['coreCount']} HEXA={row['hexa']['coreCount']}")


if __name__ == "__main__":
    main()
