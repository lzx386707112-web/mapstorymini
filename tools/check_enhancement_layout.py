#!/usr/bin/env python3
"""Geometry regression for the shared enhancement canvas (no Unity/device required)."""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
source = (ROOT / "Assets/Scripts/NativeEnhancementWindow.cs").read_text()
nodes = {n["path"]: n for n in json.loads((ROOT.parent / "docs/tms282-skill/data/enhancement-ui-nodes.json").read_text())["UI/Enchant.img"]}
# 星火/灵魂两个页签是完整 418x720（无可裁空白），其余四个压到 620。
wide_height, classic_height = map(int, re.search(r"Height=>page>=4\?(\d+):(\d+)", source).groups())
height = classic_height
assert "static float S=>" in source
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
for i in range(5):
    assert 66 + i * 62 + 59 < 66 + (i + 1) * 62
# 282 整理后六页签（含星火/灵魂）全部内嵌在 Enchant.img，单一素材根、无分体画布。
assert 'const string Root="UI/Enchant.img/";' in source
assert 'WzAssets.Node(Root+Pages[i]+"/tab/"' in source
# 新页签没有可裁区间，Y 恒等；外框改用 720。
assert "page>=4?NoCuts:PotentialCuts" in source
assert wide_height == 720 and classic_height == 620
print("Six tabs: disjoint drawn rectangles, per-tab art root and hitboxes PASS")

assert "WzAssets.Upload(joined" in source and "Buffer.BlockCopy(pixels" in source
assert "if(Pointer.Pressed&&RawAt(window,0,0,Width-30,28)" in source
assert "NativeUi.ClampWindowPosition(Pointer.Pos/S-grab" in source
assert "else {dragging=false;Pointer.ConsumeClick();}" in source
print("Direct tab node lookup, joined image rows and title drag guards PASS")

# Native diff images include the star glyph and their own precise original origin.
assert 'Image(r,diff+"/before/"+equipment.starLevel)' in source
assert 'Image(r,diff+"/after/"+(equipment.starLevel+1))' in source
assert 'if(star?.ok==true && star.quote!=null)' in source
assert 'WzAssets.Node(Root+path+"/"+state+"/0")' in source
assert 'key.ToUpperInvariant()' not in source
star_cuts=list(map(int,re.search(r"StarCuts=\{([^}]+)\}",source)[1].split(',')))
def star_y(v):
    return v-sum(max(0,min(v-a,b-a)) for a,b in zip(star_cuts[::2],star_cuts[1::2]))
assert star_y(601)-star_y(579)==22  # safeguard button remains fully clickable after compaction
for top in (473,489,505):
    assert star_y(top+16)-star_y(top)==16
assert star_y(306)<star_y(318)  # native 23px before/after images do not cover the stat rows
print("Star diff, probability rows, direct button sprites and safeguard hitbox PASS")
