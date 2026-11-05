"""Verify the trimmed UI/Enchant.img still carries every node the C# UI resolves.

Paths below mirror NativeEnhancementWindow.cs / StarForceStars.cs / ItemTooltip.cs.
A path absent from BOTH the 282 source layout image and the trimmed output is a
pre-existing gap (the UI already fell back before this trim) and is reported, not failed.
A path present in the source but missing from the output is a trim regression -> FAIL.

Run: python3 docs/enchant-img-verify.py
"""
import os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
sys.path.insert(0, os.path.expanduser("~/Documents/mxd/Beidou-Server/tool/wz-python"))
from wzpy import WzImage, WzKey, detect_region_from_img  # noqa


def load(path, region=None):
    raw = open(path, "rb").read()
    key = WzKey.for_region(region or detect_region_from_img(raw))
    img = WzImage.from_bytes(raw, key=key, name=os.path.basename(path))
    img.parse()
    return img, len(raw)


out, out_size = load(os.path.join(ROOT, "MapleDashResources/Data/UI/Enchant.img"))
src, _ = load(os.path.expanduser("~/Documents/mxd/MapleStory/Data/UI/Enchant.img"), "BMS")

PAGES = ["starForce", "potential", "additionalPotential", "traceTransfer"]
needed = [
    "common/backgrnd", "common/font:meso",
    "common/commonButton/button:close/normal/0",
    "common/commonButton/button:close/mouseOver/0",
    "common/commonButton/button:close/pressed/0",
    "common/commonButton/button:close/disabled/0",
]
for p in PAGES:
    needed += [f"{p}/tab/checked/0", f"{p}/tab/normal/0", f"{p}/backgrnd",
               f"{p}/button:confirm/normal/0", f"{p}/button:confirm/mouseOver/0",
               f"{p}/button:confirm/pressed/0", f"{p}/button:confirm/disabled/0",
               f"{p}/font:stat"]
needed += [
    # star force page layers
    "starForce/layer:waitEquip", "starForce/layer:waitEquipMeso", "starForce/layer:summaryBox",
    "starForce/layer:diffBox", "starForce/layer:statBox", "starForce/layer:probBox",
    "starForce/layer:costMesoBox", "starForce/layer:chanceTime", "starForce/layer:superior",
    "starForce/font:statValue", "starForce/font:probName", "starForce/font:probNormal",
    "starForce/font:statName", "starForce/costMeso/font:costMesoNormal",
    "starForce/item/font:itemName",
    # safeguard checkbox
    "starForce/button_protectDestroy/button/normal/0",
    "starForce/button_protectDestroy/button/mouseOver/0",
    "starForce/button_protectDestroy/button/pressed/0",
    "starForce/button_protectDestroy/button/disabled/0",
    "starForce/button_protectDestroy/checked", "starForce/button_protectDestroy/checkedAndDisabled",
    "starForce/button_protectDestroy/unchecked",
    # star row (starForce/summaryStar + vector:summary); the tooltip uses the same icons
    "starForce/vector:summary",
    "starForce/summaryStar/normal", "starForce/summaryStar/blue", "starForce/summaryStar/empty",
    "starForce/summaryStar/space/X1", "starForce/summaryStar/space/X5",
    "starForce/summaryStar/space/Y",
    # potential / bonus pages
    "potential/layer:statBox", "potential/layer:waitEquip",
    "additionalPotential/layer:statBox", "additionalPotential/layer:waitEquip",
    "additionalPotential/layer:costInvenBox",
    "traceTransfer/layer:onEquip", "traceTransfer/layer:waitEquip",
]
for p in ("potential", "additionalPotential", "traceTransfer"):
    needed += [f"{p}/costItem/layer:costItemBox", f"{p}/costItem/font:costItem",
               f"{p}/costItem/inven/selected"]
for p in ("potential", "additionalPotential"):
    for g in ("Rare", "Epic", "Unique", "Legendary"):
        needed.append(f"{p}/summaryBox/layer:summary{g}")
    for leaf in ("canvas:baseBefore", "canvas:baseAfter", "layer:mouseOver", "detail/font:detail"):
        needed.append(f"fullScreen_{p}/choiceBox/{leaf}")
    for g in ("rare", "epic", "unique", "legendary"):
        needed.append(f"fullScreen_{p}/choiceBox/title/canvas:title_{g}")
# Growth tabs must be checked too; the previous four-tab audit missed broken material paths.
for p in ("bonusStat", "soulWeapon"):
    needed += [f"{p}/tab/checked/0", f"{p}/tab/normal/0", f"{p}/backgrnd",
               f"{p}/layer:waitEquip", f"{p}/layer:summaryBox", f"{p}/item/font:itemName"]
    buttons = ("confirm", "confirm1") if p == "bonusStat" else ("confirmEnchanter", "confirmSoul")
    for button in buttons:
        for state in ("normal", "mouseOver", "pressed", "disabled"):
            needed.append(f"{p}/button:{button}/{state}/0")
needed += ["bonusStat/costItem/layer:costItemBox", "bonusStat/costItem/inven/selected",
           "bonusStat/costItem/font:costItem", "bonusStat/tab:cost/normal/0", "bonusStat/tab:cost/selected/1",
           "bonusStat/layer:statBox", "bonusStat/layer:atkPowBox", "bonusStat/font:statNormal",
           "soulWeapon/layer:costInvenBox", "soulWeapon/costInven/selected", "soulWeapon/font:costName",
           "soulWeapon/detailEnchanter/layer:detailEnchanterBox", "soulWeapon/detailEnchanter/font:probDetail",
           "soulWeapon/detailSoul/layer:detailSoulBox", "soulWeapon/detailSoul/font:statNormal"]
# diff canvases: before/<star> + after/<star+1>, both normal and superior(blue) tones
for tone in ("normal", "blue"):
    needed.append(f"starForce/diff/{tone}/layer:arrow")
    for star in range(0, 31):
        needed.append(f"starForce/diff/{tone}/before/{star}")
        needed.append(f"starForce/diff/{tone}/after/{star}")

failed, gaps = [], []
for p in needed:
    if out.root.get(p) is not None:
        continue
    (gaps if src.root.get(p) is None else failed).append(p)

print(f"output {out_size} bytes; checked {len(needed)} paths")
if gaps:
    print(f"pre-existing gaps (also absent from 282 source, unchanged): {len(gaps)}")
for p in failed:
    print("  TRIM REGRESSION:", p)
if failed:
    sys.exit(1)
print("PASS: no node used by the enhancement UI was lost")
