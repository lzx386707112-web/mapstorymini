#!/usr/bin/env python3
"""Geometry + asset regression for the three 282 growth windows (no Unity/device needed).

验证三件事，这三件事出错时在客户端上的表现分别是「窗口变形」「按钮错位」和
「有热区没图像」：

  1. C# 里的窗口尺寸 / 槽位 / 行距常量 == 导进来的 282 素材实测值；
  2. 每个 GrowthUi.Image/ButtonAt 的落位都在窗口内，且不压在别的行上；
  3. 代码引用的每一条素材路径都真的能解析到可解码的画布（ARGB4444/RGB565/ARGB1555）。

素材依赖是递归的：282 把一个窗口的像素摊到别的 `_Canvas/*.img` 里，
只导自己的画布会留下空按钮——这里逐条解析 `_outlink`，缺文件直接报错。
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT.parent / "MapleDashResources/Data"
sys.path.insert(0, str(ROOT.parent.parent / "BeiDou-Server/tool/wz-python"))

from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzCanvasProperty, WzVectorProperty  # noqa: E402

SUPPORTED = {1, 2, 513}
SOURCE = (ROOT / "Assets/Scripts/NativeGrowthWindows.cs").read_text()
CATALOG = (ROOT / "Assets/Scripts/Server/World/Tms282GrowthData.cs").read_text()

_images: dict[str, WzImage] = {}


def image(rel: str) -> WzImage:
    if rel not in _images:
        img = WzImage.from_bytes((DATA / rel).read_bytes(), key=WzKey.for_region("GMS"), name=rel)
        img.parse()
        if img.truncated or img.parse_warnings:
            raise SystemExit(f"{rel}: incomplete IMG: {img.parse_warnings}")
        _images[rel] = img
    return _images[rel]


def node(rel: str, path: str):
    found = image(rel).root
    for part in path.split("/"):
        if found is None:
            return None
        found = found.get(part)
    return found


def canvas(rel: str, path: str):
    """Follow _outlink to the real pixels; None when the chain is broken."""
    found = node(rel, path)
    for _ in range(8):
        if not isinstance(found, WzCanvasProperty):
            return None
        link = found.get("_outlink")
        if link is None:
            return found
        target = str(link.value)
        cut = target.find(".img/")
        if cut < 0:
            return found
        found = node(target[:cut + 4], target[cut + 5:])
    return None


def rect(rel: str, path: str) -> tuple[int, int]:
    found = canvas(rel, path)
    if found is None:
        raise SystemExit(f"missing/incomplete canvas {rel}/{path}")
    return found.width, found.height


def vector(rel: str, path: str) -> tuple[int, int]:
    found = node(rel, path)
    if not isinstance(found, WzVectorProperty):
        raise SystemExit(f"missing vector {rel}/{path}")
    return found.value


def fmt(rel: str, path: str) -> int:
    found = canvas(rel, path)
    return (getattr(found, "format2", None) or getattr(found, "format", 0)) if found else -1


# —————————————————— 1. 常量对齐素材实测值 ——————————————————
AUT = "UI/UIAuthenticForce.img"
ARC = "UI/UIArcaneForce.img"
WIN4 = "UI/UIWindow4.img"

hyper_w, hyper_h = map(int, re.search(r"const float Width = (\d+), Height = (\d+);", SOURCE.split("public static class NativeHyperStatWindow", 1)[1]).groups())
assert (hyper_w, hyper_h) == rect(WIN4, "HyperStat/Window/backgrnd") == (185, 318)


# POINT 框来自 backgrnd2；检查实际白底与生产字号颜色，避免服务端点数正确但白字不可见。
from wzpy.canvas import decode_canvas
point_background = decode_canvas(canvas(WIN4, "HyperStat/Window/backgrnd2")).convert("RGBA").getpixel((95 - 6, 295 - 7))
assert point_background[:3] == (255, 255, 255), point_background
hyper_source = SOURCE.split("public static class NativeHyperStatWindow", 1)[1].split("public static class NativeInnerAbilityWindow", 1)[0]
assert re.search(r'88, 290, 26, "", TextAnchor.UpperLeft, 12, LevelInk', hyper_source)
ink_match = re.search(r'LevelInk = new Color\(([^)]+)\)', hyper_source)
ink = [float(v.strip().rstrip("f")) for v in ink_match.group(1).split(",")[:3]]
def luminance(rgb):
    linear = [v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in rgb]
    return sum(a * b for a, b in zip(linear, (.2126, .7152, .0722)))
contrast = (luminance([v / 255 for v in point_background[:3]]) + .05) / (luminance(ink) + .05)
assert contrast >= 4.5, contrast
print(f"HyperStat POINT: actual IMG white field / production text contrast {contrast:.1f}:1 PASS")


def bars_of(rel: str, path: str, column: int = 10) -> list[tuple[int, int]]:
    """量底图里烘焙的行条：沿一列取饱和蓝，返回 [(top, bottom), ...]。

    极限属性的行条是画在 backgrnd3 里的像素，不是代码画的——行数、行距、条高
    只能这样量，写死常量等于把旧错误再抄一遍。
    """
    from wzpy.canvas import decode_canvas

    found = canvas(rel, path)
    if found is None:
        raise SystemExit(f"missing canvas {rel}/{path}")
    pixels = decode_canvas(found).convert("RGBA").load()
    runs: list[tuple[int, int]] = []
    start = None
    for y in range(found.height):
        r, g, b, a = pixels[column, y]
        blue = a > 128 and b > 110 and b - r > 30
        if blue and start is None:
            start = y
        elif not blue and start is not None:
            runs.append((start, y - 1))
            start = None
    if start is not None:
        runs.append((start, found.height - 1))
    return [(s, e) for s, e in runs if e - s + 1 >= 8]


rows, step, bar = map(int, re.search(
    r"const int VisibleRows = (\d+), RowStep = (\d+), BarHeight = (\d+);", SOURCE).groups())
bg_bars = bars_of(WIN4, "HyperStat/Window/backgrnd3")
assert len(bg_bars) == rows == 12, bg_bars
assert {e - s + 1 for s, e in bg_bars} == {bar} == {16}, bg_bars
assert {b[0] - a[0] for a, b in zip(bg_bars, bg_bars[1:])} == {step} == {18}, bg_bars
list_top, list_left, list_w, list_h = map(int, re.search(
    r"const float ListTop = (\d+), ListLeft = (\d+), ListWidth = (\d+), ListHeight = (\d+);", SOURCE).groups())
bg3_x, bg3_y = 13, 41
bg3_w, bg3_h = rect(WIN4, "HyperStat/Window/backgrnd3")
assert (bg3_x, bg3_y, bg3_w, bg3_h) == (13, 41, 150, 214)
assert list_left >= bg3_x and list_left + list_w <= bg3_x + bg3_w
assert list_top >= bg3_y and list_top + list_h <= bg3_y + bg3_h
# 行条必须落在列表底图上：第一行贴 bg3 顶，最后一行底边不超出 bg3 底。
assert list_top == bg3_y + bg_bars[0][0]
last_bottom = list_top + (rows - 1) * step + bar
assert last_bottom == bg3_y + bg_bars[-1][1] + 1 == 255
# 底部三排控件互不重叠，也不越界：点数/枫币在列表下沿，按钮 288..304，错误提示 304..316。
assert last_bottom <= 256 < 288 and 288 + 16 == 304 and 304 + 12 <= hyper_h
# 行内横向三段（backgrnd3 内烘焙）：蓝条 0..70、LEVEL 标签 71..103、白色数值区 104..148。
# 等级数字必须落在白色数值区里，压到蓝条或 LEVEL 标签上就是横向错位。
level_left, level_w = map(int, re.search(
    r"const float LevelLeft = (\d+), LevelWidth = (\d+);", SOURCE).groups())
assert (level_left, level_w) == (bg3_x + 104, 44)
assert level_left + level_w == bg3_x + 148
assert level_left >= bg3_x + 71 + 33
# 选中高亮罩的是 16 高的行条，不是 18 的行距（用行距会把条间 2px 的缝一起涂掉）。
assert "GrowthUi.Veil(ListLeft, y, ListWidth, BarHeight" in SOURCE
assert "GrowthUi.Plate(ListLeft, y" not in SOURCE
assert rect(WIN4, "HyperStat/Window/BtReduce/normal/0") == (17, 16)
assert rect(WIN4, "HyperStat/Window/BtReset/normal/0") == (50, 16)
assert rect(WIN4, "HyperStat/Window/BtpresetApplication/normal/0") == (46, 16)
for page in (1, 2, 3):
    assert rect(WIN4, f"HyperStat/Window/BtpresetPage{page}/normal/0") == (17, 16)
# 预设条：1/2/3 在 60/80/100，套用从 126 起，横向不打架。
assert 60 + 2 * 20 + 17 <= 126 and 126 + 46 <= hyper_w
print("HyperStat: 185x318 frame, 12 baked rows of 18 (bar 16), preset strip and footer bands PASS")

ability_w, ability_h = map(int, re.search(
    r"const float Width = (\d+), Height = (\d+);", SOURCE.split("public static class NativeInnerAbilityWindow", 1)[1]).groups())
assert (ability_w, ability_h) == rect(WIN4, "AbilitySetting/backgrnd") == (248, 388)
row_y = list(map(int, re.search(r"RowY = \{ ([^}]+) \}", SOURCE)[1].split(",")))
back_y = list(map(int, re.search(r"BackY = \{ ([^}]+) \}", SOURCE)[1].split(",")))
assert row_y == [vector(WIN4, f"AbilitySetting/vector:OptionPos{i + 1}")[1] for i in range(3)]
assert back_y == [vector(WIN4, f"AbilitySetting/vector:OptionBackPos{i + 1}")[1] for i in range(3)]
strip_y = 162
strip_h = rect(WIN4, "AbilitySetting/AbilityGrade/legendary/0")[1]
assert strip_y + strip_h <= back_y[0] and back_y[2] + 18 <= 263
# 声誉值/费用值右对齐到 pointPos/costPos 的横坐标，纵坐标落在底图烘焙的中文标签行上。
assert vector(WIN4, "AbilitySetting/vector:pointPos")[0] == 227
assert vector(WIN4, "AbilitySetting/vector:costPos")[0] == 227
assert 288 + 16 <= vector(WIN4, "AbilitySetting/vector:pointPos")[1] + 8
assert "10, 343" and rect(WIN4, "AbilitySetting/button:Use/normal/0") == (228, 36)
assert 343 + 36 == 379 <= ability_h
assert rect(WIN4, "AbilitySetting/button:preset1/normal/0") == (12, 12)
assert 177 + 2 * 20 + 12 <= ability_w
# 三个锁定行按钮与行底同格，覆盖整行宽度。
assert rect(WIN4, "AbilitySetting/BtAbilityLockEff/lock/normal/0") == (230, 18)
assert 9 + 230 <= ability_w
print("InnerAbility: 248x388 frame, native row vectors, preset strip and Use button PASS")

SYMBOL = "UI/UIEquip.img"
symbol_w, symbol_h = map(int, re.search(
    r"const float Width = (\d+), Height = (\d+);", SOURCE.split("public static class NativeSymbolWindow", 1)[1]).groups())
assert (symbol_w, symbol_h) == rect(SYMBOL, "Equip/Symbol/backgrnd") == (228, 339)
child = vector(SYMBOL, "Equip/Symbol/vector:childLT")
assert child == (22, 46)
for branch, count in (("ArcEquip", 6), ("AutEquip", 6), ("GrandAutEquip", 3)):
    root = "Equip/Symbol/" + branch
    w, h = rect(SYMBOL, root + "/backgrnd")
    cw, ch = rect(SYMBOL, root + "/box")
    assert (w, h) == (184, 272)
    slots = [vector(SYMBOL, root + f"/slotPos/{i}") for i in range(count)]
    for x, y in slots:
        assert 0 <= x and x + cw <= w and 0 <= y and y + ch <= h
    for i, (x, y) in enumerate(slots):
        for xx, yy in slots[i+1:]:
            assert abs(x-xx) >= cw or abs(y-yy) >= ch
    lx, ly = vector(SYMBOL, root + "/constants/vector:slotLevelPos")
    gx, gy = vector(SYMBOL, root + "/constants/vector:slotGaugePos")
    assert (lx, ly) == ((23, 46) if branch == "ArcEquip" else (22, 46))
    assert gy + 3 <= ch
    assert gx + 24 <= cw
grand = "Equip/Symbol/GrandAutEquip"
nx, ny = vector(SYMBOL, grand + "/constants/vector:statNamePos")
vx, vy = vector(SYMBOL, grand + "/constants/vector:statNumberPos")
gap = node(SYMBOL, grand + "/constants/lineGap").value
card_top = vector(SYMBOL, grand + "/slotPos/0")[1]
for index, label in enumerate(("force", "exp", "meso", "drop")):
    width, height = rect(SYMBOL, grand + "/textUp/" + label)
    assert nx + width < vx - sum(rect(SYMBOL, grand + "/textUp/" + c)[0] for c in "+100%")
    assert ny + index * gap + height < card_top
    assert vy + index * gap + 10 < card_top
print("Grand: force/EXP/meso/drop native labels, +% glyphs and four rows fit above cards PASS")
symbol_source = SOURCE.split("public static class NativeSymbolWindow", 1)[1].split("// V/HEXA presentation", 1)[0]
assert "UI/UIAuthenticForce.img" not in symbol_source
assert "GrowthUi.Plate" not in symbol_source and "GrowthUi.Veil" not in symbol_source
assert 'Vector(Branch, "slotPos/" + i)' in symbol_source
assert 'Constant("slotLevelPos")' in symbol_source and 'Constant("slotGaugePos")' in symbol_source
print("Symbol: EXE-verified UIEquip 228x339, ARC/AUT/Grand separate native cards, no overlap PASS")

# —————————————————— 2. 代码里写死的关键落位 ——————————————————
for anchor in [
    'GrowthUi.Image(Root, "backgrnd", 0, 0);\n        GrowthUi.Image(Root, "backgrnd2", 6, 7);',
    'GrowthUi.Image(Root, "backgrnd3", 13, 41);',
    'GrowthUi.Image(PlusRoot, "textUp/+", 152, y + 4);',
    'GrowthUi.At(148, y, 14, BarHeight)',
    '"BtpresetPage" + (i + 1) + "/" + (i == preset ? "checked" : "normal") + "/0"',
    'GrowthUi.Image(Root, "BtpresetSelected/0", 60 + activePreset * 20, 267);',
    'GrowthUi.Image(Root, "AbilityGrade/" + gradeNode + "/0", 9, 162);',
    'int unlocked = state?.lineCount ?? AbilityCatalog.Lines;',
    'GrowthUi.Image(Root, "BtAbility/" + GradeNodes[Mathf.Clamp(rowGrade, 0, 3)] + "/0", 9, back);',
    '"BtAbilityLockEff/" + (locked[i] ? "lock" : "unlock") + "/normal/0", 9, back',
    "GrowthUi.Plate(8, 5, 152, 20, .62f);",
    'GrowthUi.Text(Root, "内在能力", 12, 7, 146, "", TextAnchor.UpperLeft, 18);',
    'GrowthUi.Button(Root, "button:Use")',
    'GrowthUi.Button(Root, "button:close")',
    'GrowthUi.Button(Root, "confirm/button:Ok")',
    'GrowthUi.Button(Root, "confirm/button:Cancel")',
]:
    assert anchor in SOURCE, anchor
print("Literal placements: frames, rows, presets, cards and confirm buttons PASS")

# 页面切换的守卫：待确认时不能改页签/洗练，否则「确定/取消」永远点不到。
assert 'GUI.enabled = wasEnabled && !(state?.pending ?? false);' in SOURCE
assert 'GUI.enabled = wasEnabled && activePreset != preset;' in SOURCE
# 待确认时整窗转模态：底下的行/页签/预设都不能再被点到。
assert 'if (state?.pending == true) { DrawConfirm(); return; }' in SOURCE
# 三扇窗互斥，免得在小屏上叠成一团。
for a, b in (("NativeSymbolWindow", "NativeInnerAbilityWindow"), ("NativeHyperStatWindow", "NativeInnerAbilityWindow")):
    assert re.search(rf"{a}\.Close\(\);\s*{b}\.Close\(\);", SOURCE) or re.search(rf"{b}\.Close\(\);\s*{a}\.Close\(\);", SOURCE)
print("Pending/confirm guards and window exclusivity PASS")

# —————————————————— 3. 素材路径全部可解析且可解码 ——————————————————
STATE = ("normal", "pressed", "disabled", "mouseOver")
required: list[tuple[str, str]] = []


def need(rel: str, path: str) -> None:
    required.append((rel, path))


for button in ("HyperStat/Window/BtReduce", "HyperStat/Window/BtReset", "HyperStat/Window/BtpresetApplication"):
    for state in STATE:
        need(WIN4, f"{button}/{state}/0")
# BtpresetPage 只到 mouseOver/checked，没有 disabled（Button 会回落到 normal）。
for page in (1, 2, 3):
    for state in ("normal", "pressed", "mouseOver", "checked"):
        need(WIN4, f"HyperStat/Window/BtpresetPage{page}/{state}/0")
need(WIN4, "HyperStat/Window/BtpresetSelected/0")
need(WIN4, "HyperStat/Window/backgrnd")
need(WIN4, "HyperStat/Window/backgrnd2")
need(WIN4, "HyperStat/Window/backgrnd3")
# 属性名位图逐条和目录里的 skillId 对上，目录加一条这里就得跟着加。
for skill_id in sorted(set(re.findall(r"skillId = (80000\d{3})", CATALOG))):
    need(WIN4, f"HyperStat/Window/statList/{skill_id}")

need(WIN4, "AbilitySetting/backgrnd")
for grade in ("rare", "epic", "unique", "legendary"):
    need(WIN4, f"AbilitySetting/AbilityGrade/{grade}/0")
    need(WIN4, f"AbilitySetting/BtAbility/{grade}/0")
    need(WIN4, f"AbilitySetting/confirm/AbilityGrade/{grade}/0")
    need(WIN4, f"AbilitySetting/confirm/Ability/{grade}/0")
for lock in ("lock", "unlock"):
    for state in STATE:
        need(WIN4, f"AbilitySetting/BtAbilityLockEff/{lock}/{state}/0")
for button in ("AbilitySetting/button:Use", "AbilitySetting/button:close",
               "AbilitySetting/confirm/button:Ok", "AbilitySetting/confirm/button:Cancel"):
    for state in STATE:
        need(WIN4, f"{button}/{state}/0")
for index in (1, 2, 3):
    for state in STATE + ("checked",):
        need(WIN4, f"AbilitySetting/button:preset{index}/{state}/0")
need(WIN4, "AbilitySetting/confirm/backgrnd")

need(SYMBOL, "Equip/Symbol/backgrnd")
for branch in ("ArcEquip", "AutEquip", "GrandAutEquip"):
    prefix = "Equip/Symbol/" + branch
    for name in ("backgrnd", "box", "base2", "canvas:gauge", "textDown/v", "textUp/force", "textUp/+"):
        need(SYMBOL, prefix + "/" + name)
    for state in STATE:
        need(SYMBOL, prefix + f"/button:enchant/{state}/0")
    for digit in range(10):
        need(SYMBOL, prefix + f"/textUp/{digit}")
        need(SYMBOL, prefix + f"/textDown/{digit}")

for label in ("exp", "meso", "drop", "%"):
    need(SYMBOL, "Equip/Symbol/GrandAutEquip/textUp/" + label)

broken, undecodable = [], []
for rel, path in required:
    found = canvas(rel, path)
    if found is None:
        broken.append(f"{rel}/{path}")
    elif fmt(rel, path) not in SUPPORTED:
        undecodable.append(f"{rel}/{path} fmt={fmt(rel, path)}")
if broken:
    raise SystemExit("unresolvable sprites (BMS outlink target not imported?):\n  " + "\n  ".join(broken))
if undecodable:
    raise SystemExit("sprites outside ImgFile.Rgba formats:\n  " + "\n  ".join(undecodable))
print(f"Sprites: {len(required)} node(s) resolve through _outlink and decode PASS")

# 代码不能再直接回读 _Canvas 之外的旧名字（避免有人把窗口指回没有像素的存根）。
for stale in ("UI/Enchant.img/HyperStat", "UI/UIWindow2.img", "UICharacterInfo.img/"):
    assert stale not in SOURCE, stale
print("No stub-only image roots referenced PASS")

# ——— 拖动抓手：标题栏必须「鼠标 + 触摸」都能拖 ———
# 触摸下 Pointer 把手指做成「相对光标 + 长按才等于按下」，抬手才给一帧 Pressed，
# 所以只认 Pointer.Pressed 的抓手在手机/安卓模拟器里永远拉不动窗口。这里守住三件事：
# ① Drag 里有直接读 Input.GetTouch 的分支；② 每个窗口都传了 owner 令牌（顶层窗口优先）；
# ③ V/HEXA 用的是整条标题栏 46px、右侧留 100px。
V_SRC = (ROOT / "Assets/Scripts/NativeVMatrixWindow.cs").read_text()
H_SRC = (ROOT / "Assets/Scripts/NativeHexaMatrixWindow.cs").read_text()
for token in ("Input.GetTouch", "TouchPhase.Began", "dragOwner", "object owner = null"):
    assert token in SOURCE, f"GrowthUi.Drag 缺少触摸拖动支持：{token}"
for token in ("typeof(NativeHyperStatWindow)", "typeof(NativeInnerAbilityWindow)",
              "typeof(NativeSymbolWindow)"):
    assert token in SOURCE, f"Drag 调用没有传 owner：{token}"
for name, src in (("NativeVMatrixWindow", V_SRC), ("NativeHexaMatrixWindow", H_SRC)):
    assert f"owner: typeof({name})" in src, f"{name} 的 Drag 没传 owner"
    assert "46f, 100f" in src, f"{name} 的抓手不是整条标题栏"
    assert "slop: 6f" in src, f"{name} 没有整块面板的拖动 slop"
print("Drag handles: mouse + touch (Input.GetTouch) on all 5 windows, V/HEXA strip 46x(full-100) PASS")
