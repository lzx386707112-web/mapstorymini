#!/usr/bin/env python3
"""Geometry regression for the shared enhancement canvas (no Unity/device required)."""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
source = (ROOT / "Assets/Scripts/NativeEnhancementWindow.cs").read_text()
nodes = {n["path"]: n for n in json.loads((ROOT.parent / "docs/tms282-skill/data/enhancement-ui-nodes.json").read_text())["UI/Enchant.img"]}
height = int(re.search(r"Height=(\d+)", source)[1])
assert "static float S=>" in source and "Height=>" not in source
assert "var tab=RawAt(window,13,66+i*62,120,59)" in source
assert "WzAssets.Texture(tabNode)" in source  # literal IMG paths must use Node, not the alias overload
assert "GUI.DrawTexture(tab,tabTexture)" in source  # origin must not move the drawn tab away from its hitbox
assert "At(r,x+11,y-33,64,64)" in source
assert "RawAt(ChoiceArea,side*244,0,234,255)" in source
for page, name in [("starForce", "Star"), ("potential", "Potential"), ("additionalPotential", "Potential"), ("traceTransfer", "Trace")]:
    cuts = list(map(int, re.search(name + r"Cuts=\{([^}]+)\}", source)[1].split(",")))
    bands = list(zip(cuts[::2], cuts[1::2]))
    def y(v):
        return v - sum(max(0, min(v - a, b - a)) for a, b in bands)
    assert y(720) == height
    kept = [row for row in range(720) if y(row + 1) > y(row)]
    assert len(kept) == height and len({y(row) for row in kept}) == height  # exactly one source row per output row
    assert nodes[page + "/backgrnd"]["width"] == 418
    # Full icon frame, name, confirmation and all material cells retain their size.
    for top, bottom in [(55, 167), (184, 204), (675, 710)]:
        assert y(bottom) - y(top) == bottom - top, (page, top, bottom)
    if page != "starForce":
        grid_top = {"potential": 492, "additionalPotential": 461, "traceTransfer": 489}[page]
        assert y(grid_top + 83) - y(grid_top) == 83
    else:
        assert y(382) - y(318) == 64  # four complete stat rows, remaining rows scroll
    anchors = ["item/vector:itemIconBefore", "item/vector:itemIconAfter"] if page == "traceTransfer" else ["item/vector:itemIcon"]
    for anchor in anchors:
        x, v = nodes[page + "/" + anchor]["value"]
        expected_x = {81: 124, 251: 294, 166: 209}[x]
        assert x + 11 + 32 == expected_x and v - 33 + 32 == 111
    for screen_w, screen_h in [(640, 360), (1280, 720), (2340, 1080), (1080, 2340)]:
        scale = min(2, (screen_w - 24) / 565, screen_h * .8 / height)
        assert 565 * scale <= screen_w - 24 and height * scale <= screen_h * .8 + .001
    print(page + ": frame, equipment, materials, footer and viewport bounds PASS")
for i in range(3):
    assert 66 + i * 62 + 59 < 66 + (i + 1) * 62
print("Four tabs: disjoint drawn rectangles and hitboxes PASS")

assert "WzAssets.Upload(joined" in source and "Buffer.BlockCopy(pixels" in source
assert "if(Pointer.Pressed&&RawAt(window,0,0,Width-30,28)" in source
assert "NativeUi.ClampWindowPosition(Pointer.Pos/S-grab" in source
assert "else {dragging=false;Pointer.ConsumeClick();}" in source
print("Direct tab node lookup, joined image rows and title drag guards PASS")
