#!/usr/bin/env python3
"""Extract weekly gemstone requirements/text; never substitute QuestInfo rewards for generic Act.

The user approved rebuilding the missing q34151s/e execution. The generated resource
feeds the native quest UI; generated constants feed the authoritative weekly ledger.
"""
import json
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path.home() / "Documents/mxd/TMS282.5/data/wz282/Quest.wz/QuestData"


def node(root, path):
    for name in path.split("/"):
        root = next(child for child in root if child.get("name") == name)
    return root


def value(root, path):
    return node(root, path).get("value")


def main():
    weekly = ET.parse(SOURCE / "34151.img.xml").getroot()
    daily = ET.parse(SOURCE / "34129.img.xml").getroot()
    info = node(weekly, "QuestInfo")
    fields = {
        "id": 34151, "npc": int(value(weekly, "Check/0/npc")),
        "level": int(value(weekly, "Check/0/lvmin")),
        "prerequisite": int(value(weekly, "Check/0/quest/0/id")),
        "required": int(value(weekly, "Check/1/infoex/0/value")),
        "dailyRequired": int(value(daily, "Check/1/infoex/0/value")),
        "rewardItem": int(value(weekly, "QuestInfo/reward/item/0/id")),
        "rewardCount": int(value(weekly, "QuestInfo/reward/item/0/count")),
        "name": value(info, "name"), "info": [value(info, str(i)) for i in range(3)],
        "demand": value(info, "demandSummary"), "reward": value(info, "rewardSummary"),
    }
    tasks = []
    for quest in range(34130, 34151):
        check = node(ET.parse(SOURCE / f"{quest}.img.xml").getroot(), "Check/1")
        if any(child.get("name") in ("mob", "item") for child in check):
            tasks.append(quest)
    fields["dailyIds"] = tasks
    (ROOT / "Assets/Resources/Quests/weekly-gemstone.json").write_text(
        json.dumps(fields, ensure_ascii=False, indent=2) + "\n")
    code = "// Generated from 282 QuestData/34151 and 34129; see tools/ms282/export_weekly_gemstone.py.\npublic static class WeeklyGemstoneData\n{\n"
    for key in ("id", "npc", "level", "prerequisite", "required", "dailyRequired", "rewardItem", "rewardCount"):
        code += f"    public const int {key[0].upper() + key[1:]} = {fields[key]};\n"
    code += "    public static readonly int[] DailyIds = { " + ", ".join(map(str, tasks)) + " };\n}\n"
    (ROOT / "Assets/Scripts/Server/World/WeeklyGemstoneData.cs").write_text(code)
    print(f"weekly {fields['id']}: {fields['required']} investigations, {len(tasks)} daily tasks, reward {fields['rewardItem']} x {fields['rewardCount']}")


if __name__ == "__main__":
    main()
