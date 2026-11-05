#!/usr/bin/env python3
"""Import the original 282 equipment tooltip skin, GMS encoding / ARGB4444."""
import hashlib
import json
from pathlib import Path
from import_enhancement_ui import Importer, ROOT

SECTIONS = ["Item/Equip/frame/common", "Item/Equip/itemIcon", "Item/Equip/font",
            "Item/Equip/space", "Item/Equip/textIcon/starForce", "Item/Equip/textIcon/potential",
            "Item/Equip/textIcon/additionalPotential", "Item/Equip/textIcon/soulWeaponWz2"]

def main():
    importer = Importer(Path.home()/"Documents/mxd/MapleStory/Data")
    rel = "UI/UIToolTip.img"
    data = importer.convert(rel, SECTIONS)
    target = ROOT.parent/"MapleDashResources/Data"/rel
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(data)
    # This tiny skin is required by the new renderer. Ship the identical original IMG
    # with the APK as well, so code and skin cannot be installed at different versions.
    bundled = ROOT/"Assets/Resources/UI/EquipmentTooltip.bytes"
    bundled.parent.mkdir(parents=True, exist_ok=True)
    bundled.write_bytes(data)
    index_path = ROOT/"Assets/Resources/NativeAssetIndex.json"
    index = json.loads(index_path.read_text())
    entry = next((x for x in index["files"] if x["path"] == rel), None)
    if entry is None:
        entry = {"path": rel}
        index["files"].append(entry)
    entry["sha256"] = hashlib.sha256(data).hexdigest()
    manifest = json.dumps(index, ensure_ascii=False, indent=1)+"\n"
    index_path.write_text(manifest)
    # Resource pack/sync uses the external manifest too, not only the APK index.
    (ROOT.parent/"MapleDashResources/resource-manifest.json").write_text(manifest)
    report = ROOT.parent/"docs/tms282-skill/data/equipment-tooltip-import.json"
    report.write_text(json.dumps({"sourceSha256": importer.hashes, "sections": SECTIONS,
        "outputSha256": entry["sha256"], "format": "ARGB4444", "encoding": "GMS",
        "nodes": importer.nodes[rel]}, ensure_ascii=False, indent=1)+"\n")

if __name__ == "__main__":
    main()
