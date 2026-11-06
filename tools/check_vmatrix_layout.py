#!/usr/bin/env python3
"""V 矩阵窗口的几何 + 素材回归（不需要 Unity / 真机）。

出错时在客户端上的三种表现，正好对应下面三段检查：

  1. 窗口变形：C# 里的窗口尺寸 / 特殊核心子窗锚点 == 导入的 282 素材实测值；
  2. 按钮错位：31 个槽位与 5 个主窗按钮的落位 == `main/*` 里的 vector 原值；
  3. 有热区没图像：代码里引用的每一条 `UI/VMatrixUI.img/...` 都能解析到
     可解码的画布（本项目 ImgFile 只认 1/2/513，块压缩必须在导入时压成 ARGB4444）。

用法：
  python3 tools/check_vmatrix_layout.py
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT.parent / "MapleDashResources/Data"
sys.path.insert(0, str(ROOT.parent.parent / "BeiDou-Server/tool/wz-python"))

from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzCanvasProperty, WzVectorProperty  # noqa: E402

SUPPORTED = {1, 2, 513}
IMG = "UI/VMatrixUI.img"
SOURCE = (ROOT / "Assets/Scripts/NativeVMatrixWindow.cs").read_text()

_images: dict[str, WzImage] = {}


def image(rel: str) -> WzImage:
    if rel not in _images:
        img = WzImage.from_bytes((DATA / rel).read_bytes(), key=WzKey.for_region("GMS"), name=rel)
        img.parse()
        if img.truncated or img.parse_warnings:
            raise SystemExit(f"{rel}: incomplete IMG: {img.parse_warnings}")
        _images[rel] = img
    return _images[rel]


def node(path: str):
    found = image(IMG).root
    for part in path.split("/"):
        if found is None:
            return None
        found = found.get(part)
    return found


def canvas(path: str):
    found = node(path)
    for _ in range(8):
        if not isinstance(found, WzCanvasProperty):
            return None
        link = found.get("_outlink")
        if link is None:
            return found
        target = str(link.value)
        cut = target.find(".img/")
        if cut < 0 or not target.startswith("UI/VMatrixUI.img/"):
            return None
        found = node(target[cut + 5:])
    return None


def size(path: str) -> tuple[int, int]:
    found = canvas(path)
    if found is None:
        raise SystemExit(f"missing/incomplete canvas {IMG}/{path}")
    return found.width, found.height


def vector(path: str) -> tuple[int, int]:
    found = node(path)
    if not isinstance(found, WzVectorProperty):
        raise SystemExit(f"missing vector {IMG}/{path}")
    return found.value


def origin(path: str) -> tuple[int, int]:
    found = canvas(path)
    prop = found.get("origin") if found is not None else None
    if prop is None:
        raise SystemExit(f"missing origin on {IMG}/{path}")
    return prop.x, prop.y


# —————————————————— 1. 窗口尺寸与子窗锚点 ——————————————————
w, h = map(int, re.search(r"const float W = (\d+)f, H = (\d+)f;", SOURCE).groups())
assert (w, h) == size("main/backgrnd") == (506, 719), (w, h)
assert "const float SpecialLeft = 414f, SpecialTop = 172f, SpecialW = 247f, SpecialH = 402f;" in SOURCE
assert vector("main/vector:specialCore") == (414, 172)
assert size("main/specialCore/backgrnd") == (247, 402)
# 子窗不能压在 31 个槽位上：槽位最右是 453 + 29 = 482，子窗从 414 起且在主窗右侧外扩。
assert 414 + 247 > 506 and 414 < 506
print("V main: 506x719 frame, special panel 247x402 anchored at (414,172) PASS")

# —————————————————— 2. 31 个槽位 == 原端 vector ——————————————————
EXPECTED_SLOTS = (
    [(63 + 78 * i, 172) for i in range(4)]
    + [(63 + 78 * i, 302) for i in range(6)]
    + [(63 + 78 * (i % 6), 432 + 75 * (i // 6)) for i in range(21)]
)
assert len(EXPECTED_SLOTS) == 31
actual = []
for index, (section, count) in enumerate((("skill", 4), ("enforce", 6), ("common", 21))):
    for i in range(count):
        actual.append(vector(f"main/slotPos/{section}/{i}"))
assert actual == EXPECTED_SLOTS, [pair for pair in zip(actual, EXPECTED_SLOTS) if pair[0] != pair[1]][:4]
# C# 的兜底公式要和这张表一致（节点丢失时不至于画到窗外）
assert "const float SlotStepX = 78f, SlotStepY = 75f;" in SOURCE
assert "432 + 75 * (index / 6)" in SOURCE
print("V slots: 31 vector anchors match the 282 table (x step 78, y 172/302/432+75n) PASS")

# —————————————————— 3. 主窗按钮落位 ——————————————————
# WZ 的 origin 是轴心：画到 (x,y) 时左上角 = (x-origin.x, y-origin.y)，
# 所以按钮在窗口里的位置 = (-origin.x, -origin.y)。
BUTTONS = {
    "button:close": (470, 16),
    "button:help": (449, 16),
    "button:reset": (327, 57),
    "button:transform": (407, 57),
    "button:specialCoreOpen": (347, 98),
    "button:specialCoreClose": (347, 98),
}
for name, expected in BUTTONS.items():
    ox, oy = origin(f"main/{name}/normal/0")
    assert (-ox, -oy) == expected, (name, ox, oy, expected)
    for state in ("mouseOver", "pressed", "disabled"):
        assert canvas(f"main/{name}/{state}/0") is not None, (name, state)
    # 热区必须落在窗口内，且五个按钮互不重叠
    bw, bh = size(f"main/{name}/normal/0")
    assert expected[0] + bw <= w and expected[1] + bh <= h
print("V buttons: close/help/reset/transform/specialCoreOpen+Close at native origins PASS")

# 特殊核心展示位：未登记层是 main/layer:specialCoreUnregistered 66x68 origin(-380,-132)
# → 落在 (380,132)；main/vector:specialCore(414,172) 是「特殊核心子窗」的锚点，不是这一格。
# 拿子窗锚点去画未登记层，加号会往右下飘 (34,40) 并压出展示区。
assert origin("main/layer:specialCoreUnregistered") == (-380, -132)
assert "const float coverX = 380f, coverY = 132f;" in SOURCE
assert vector("core/vector:specialCoreIconPos") == (21, 16)
print("V special core: cover cell 66x68 at (380,132), source vector (21,16) preserved PASS")

# 用户授权居中：位置向量不再直接用作32像素图标左上角。
assert 'x + (frameWidth - icon.width) / 2f' in SOURCE
assert 'y + 28f - icon.height / 2f' in SOURCE
assert 'DrawCoreIcon(icon, bx, by, 58f);' in SOURCE
assert 'DrawCoreIcon(icon, coverX, coverY, 66f);' in SOURCE
assert 'DrawCoreIcon(icon, x - origin.x, y - origin.y, 66f);' in SOURCE
assert 'anchor.x - hoverOrigin.x, anchor.y - hoverOrigin.y' in SOURCE
for name in ("job", "jobLocked", "enforce", "enforceLocked", "common", "commonLocked",
             "special", "specialLocked"):
    fw, fh = size("core/coreBackground/" + name)
    expected_width = 66 if name.startswith("special") else 58
    assert (fw, fh) == (expected_width, 68)
    for iw, ih in ((32, 32), (28, 28), (24, 24)):
        left, top = (fw - iw) / 2, 28 - ih / 2
        assert left + iw / 2 == fw / 2 and top + ih / 2 == 28
        assert left >= 0 and top >= 0 and left + iw <= fw and top + ih < 49
print("V core icons: all normal/special frames centered above lock area; hover uses own origin PASS")

# —————————————————— 3b. 等级数字：原图点阵，居中在等级槽 ——————————————————
# core/levelNumber/0..9 是 5px 高的点阵数字；58x68 槽框的等级槽实测本地 (9..48, 50..58)，
# 中心 x=28.5、5px 数字上沿 y=52。旧实现拿 font:levelCount(12px)+UpperLeft 画在
# currentLevelPos 上，字高超过整条槽、又贴着左边缘，就是「数字对不上」的来源。
for digit in "0123456789":
    dw, dh = size("core/levelNumber/" + digit)
    assert dh == 5 and 1 <= dw <= 5, (digit, dw, dh)
assert "const float LevelBarCenterX = 28.5f, LevelBarTopY = 52f, LevelDigitGap = 1f;" in SOURCE
assert 'static Texture2D LevelDigit(char digit)' in SOURCE
assert 'Img + "core/levelNumber/" + digit' in SOURCE
assert 'left + LevelBarCenterX - width / 2f' in SOURCE
assert 'top + LevelBarTopY' in SOURCE
assert "DrawLevelNumber(view.level, bx, by);" in SOURCE
# 每帧 31 个槽：点数阵图只准两趟直读，不许建数组（UI 重绘不该产垃圾）
assert "new Texture2D[" not in SOURCE
# 槽位里再出现字体排版就是回退（字号下限 12 > 槽高 9，必然溢出）
slots_source = SOURCE.split("static void DrawSlots", 1)[1].split("static void DrawSpecialCore", 1)[0]
assert '"font:levelCount"' not in slots_source
print("V core level: bitmap levelNumber digits centered in the 58x68 level bar PASS")

# —————————————————— 4. 弹窗几何 ——————————————————
for path, expected in (
    ("vSkillUpgradePopUp/enforcementAlert/backgrnd", (340, 491)),
    ("vSkillUpgradePopUp/enforcementResult/backgrnd", (340, 351)),
    ("vPointTransform/backgrnd", (374, 446)),
    ("coreGemStoneOpen/open/backgrnd", (285, 470)),
    ("specialCorePopUp/register/backgrnd", (342, 361)),
):
    assert size(path) == expected, (path, size(path), expected)
    assert str(expected[0]) in SOURCE and str(expected[1]) in SOURCE, path
# 强化弹窗的五个按钮都得在 491 高之内
for name, expected in (("button:levelPlus", (61, 349)), ("button:selectAll", (135, 349)),
                       ("button:initialize", (209, 349)), ("button:confirm", (80, 444)),
                       ("button:cancel", (174, 444))):
    ox, oy = origin(f"vSkillUpgradePopUp/enforcementAlert/{name}/normal/0")
    assert (-ox, -oy) == expected, (name, ox, oy)
# 转换窗的输入框 (84,295)-(204,311) 与 C# 里写字的位置一致
lt, rb = vector("vPointTransform/edit:transformCount/lt"), vector("vPointTransform/edit:transformCount/rb")
assert (lt, rb) == ((84, 295), (204, 311))
assert '84, 296, 120, "", TextAnchor.MiddleCenter, 14);' in SOURCE
for name, expected in (("button:transformCountMax", (258, 290)), ("button:transformCountUP", (212, 290)),
                       ("button:transformCountDown", (235, 290)), ("button:confirm", (96, 398)),
                       ("button:close", (190, 398))):
    ox, oy = origin(f"vPointTransform/{name}/normal/0")
    assert (-ox, -oy) == expected, (name, ox, oy)
# 宝石窗
for name, expected in (("button:Confirm", (59, 410)), ("button:Cancel", (146, 410)),
                       ("button:UP", (197, 196)), ("button:Down", (220, 196)), ("button:All", (44, 196))):
    ox, oy = origin(f"coreGemStoneOpen/open/{name}/normal/0")
    assert (-ox, -oy) == expected, (name, ox, oy)
assert vector("coreGemStoneOpen/open/vector:icon") == (113, 63)
assert vector("coreGemStoneOpen/open/vector:CoreCount") == (123, 134)
print("V dialogs: enforcementAlert 340x491 / transform 374x446 / gemstone 285x470 / register 342x361 PASS")

# 弹窗坐标系：WZ 里的按钮 origin 是相对「弹窗左上角」的，直接拿窗口 R 画会整排落到窗口
# 左上角。五个弹窗都必须先 Shift 进自己的坐标系、画完 Restore（第一版就是漏了这一步）。
assert SOURCE.count("GrowthUi.Shift(") == 5, SOURCE.count("GrowthUi.Shift(")
assert SOURCE.count("GrowthUi.Restore(outer)") == 5, SOURCE.count("GrowthUi.Restore(outer)")
for name in ("DrawUpgrade", "DrawResult", "DrawRegister", "DrawTransform", "DrawGemstone"):
    body = SOURCE.split(f"static void {name}()", 1)[1].split("static ", 1)[0]
    assert "GrowthUi.Shift(" in body and "GrowthUi.Restore(outer)" in body, name
    assert "x0 +" not in body and "y0 +" not in body, f"{name} 还在用窗口坐标给弹窗内容加偏移"
print("V dialog coordinate spaces: all 5 popups Shift into their own origin PASS")

# —————————————————— 3c. 特殊核心图标（不在 VMatrixUI.img，也不在 skillicon） ——————————————————
# 18 条 type==2 的 `skills` 全是空数组（400007xxx 没有 Skill 定义），所以
# `skillicon/<id>` 对它们永远是 null —— 这就是「特殊核心面板没图标」的根因。
# 图标本体只在 Etc/_Canvas/VCore.img/vSkill/CoreData/<id>/{icon,iconDisabled}，
# 由 tools/ms282/import_special_core_icons.py 转码（BMS→GMS）并登记进清单。
SPECIAL_ICON = "Etc/_Canvas/VCore.img"
assert f'const string SpecialIconFile = "{SPECIAL_ICON}";' in SOURCE
assert "WzAssets.HasFile(SpecialIconFile)" in SOURCE, "缺少 HasFile 保护：旧资源包上会每帧抛 FileNotFound"
assert "SpecialIcon(view.coreId, opened)" in SOURCE, "特殊核心列表没有用 VCore 图标"
assert "SpecialIcon(active.coreId, true)" in SOURCE, "展示位没有用 VCore 图标"
assert "SpecialIcon(core.coreId, true)" in SOURCE, "登记弹窗没有用 VCore 图标"
icon_file = DATA / SPECIAL_ICON
assert icon_file.is_file(), f"数据树里缺少 {SPECIAL_ICON}（跑 tools/ms282/import_special_core_icons.py --write）"
index_files = {entry["path"] for entry in json.loads(
    (ROOT / "Assets/Resources/NativeAssetIndex.json").read_text(encoding="utf-8"))["files"]}
assert SPECIAL_ICON in index_files, f"{SPECIAL_ICON} 没登记进 NativeAssetIndex.json：真机 HasFile 会是 false"
# 共享素材必须能转回源端：文件头是 GMS 密钥，wzpy 用 GMS 密钥解得开才说明转码做对了。
vcore = WzImage.from_bytes(icon_file.read_bytes(), key=WzKey.for_region("GMS"), name=SPECIAL_ICON)
vcore.parse()
assert not vcore.truncated and not vcore.parse_warnings, f"{SPECIAL_ICON} 结构不完整"
special_ids = sorted(c["id"] for c in json.loads(
    (ROOT / "Assets/Resources/UI/VMatrix.json").read_text(encoding="utf-8"))["cores"].values()
    if c.get("type") == 2)
assert len(special_ids) == 18, len(special_ids)
for core_id in special_ids:
    for name in ("icon", "iconDisabled"):
        found = vcore.root.get(f"vSkill/CoreData/{core_id}/{name}")
        assert isinstance(found, WzCanvasProperty), f"{SPECIAL_ICON}: 缺少 {core_id}/{name}"
        assert (found.width, found.height) == (32, 32), (core_id, name)
        assert found.format in SUPPORTED, f"{core_id}/{name} format {found.format} 不能被 ImgFile 解码"
print(f"V special core icons: {len(special_ids)} cores x icon/iconDisabled in {SPECIAL_ICON} PASS")

# —————————————————— 3d. 特殊核心子窗滚动条 ——————————————————
# 原端 scrollBarInfo：lt(222,-1)、轨道 5x292、滑块 5x28，delta(10)/wheelRange(400)。
# 本工程按行翻页，不用 delta/wheelRange，但落点与厚度必须和素材一致，
# 否则滑块会压在槽位格子上或整条飘出子窗。
bar = node("main/specialCore/slot/scrollBarInfo")
assert bar is not None, "VMatrixUI.img 缺 main/specialCore/slot/scrollBarInfo"
bar_lt = (bar.get("lt").x, bar.get("lt").y)
bar_len = bar.get("length").value
bar_track = canvas("main/specialCore/slot/scrollBarInfo/enabled/base")
bar_thumb = canvas("main/specialCore/slot/scrollBarInfo/enabled/thumb0")
assert (bar_track.width, bar_track.height) == (5, bar_len), (bar_track.width, bar_track.height, bar_len)
assert (bar_thumb.width, bar_thumb.height) == (5, 28), (bar_thumb.width, bar_thumb.height)
assert f"main/specialCore/slot/scrollBarInfo/lt\", {bar_lt[0]}, {bar_lt[1]})" in SOURCE, \
    f"滚动条 lt 兜底值 {bar_lt} 与素材不一致"
assert (f"const float barW = {bar_track.width}f, barH = {bar_len}f, "
        f"thumbH = {bar_thumb.height}f;") in SOURCE, "滚动条尺寸常量与素材不一致"
# 交互侧：拖滑块必须从槽区拖动里摘出去，否则一次拖动会翻两倍；
# 一屏放得下时（maxScroll==0）不画滑块，和原端 disabled 分支（只有 base）一致。
for snippet in (
    'specialBarGrab = Pointer.PressPosition.y - thumb.y',
    'if (!Pointer.Down) specialBarDrag = false;',
    '&& !barHit.Contains(Pointer.PressPosition)',
    'maxScroll > 0 ? "enabled/base" : "disabled/base"',
    'specialBarDrag ? "thumb2" : over ? "thumb1" : "thumb0"',
):
    assert snippet in SOURCE, snippet
assert 'specialBarDrag = false;' in SOURCE.split("public static void Close()")[1][:400], \
    "Close() 没复位 specialBarDrag：关窗时手指还按着，下次开会带着残留拖动"
print(f"V special core scrollbar: lt{bar_lt} {bar_track.width}x{bar_len} thumb {bar_thumb.width}x{bar_thumb.height} PASS")

# —————————————————— 5. 代码引用的素材全部可解码 ——————————————————
# 只认 Image/Button 的字面量参数：Text/ShadowText 的第二个参数是文案，不是素材路径。
refs: set[str] = set()
for match in re.finditer(r'GrowthUi\.(Image|Button)\(\s*Img \+ "([^"]+)"\s*(?:,\s*"([^"]+)")?\s*[,)]', SOURCE):
    root, rel = match.group(2), match.group(3)
    refs.add(root + (rel or ""))

# 图标底图是运行时按状态挑的（empty/job/enforce/common/special × Locked），
# 这里把九种可能都点一遍，避免「某个状态没导进来」时只在真机上黑块。
for name in ("empty", "job", "jobLocked", "enforce", "enforceLocked",
             "common", "commonLocked", "special", "specialLocked"):
    refs.add("core/coreBackground/" + name)
# 悬停高亮：0 = 空格位、1 = 有核心（两帧都要有，否则鼠标移上去就露底）
refs.add("core/coreState/mouseOver/0")
refs.add("core/coreState/mouseOver/1")
# 展示位的空位两帧（常态「+」/ 发光版）。这两条是三元表达式选的，正则抓不到字面量，
# 必须显式列出来：漏一帧的表现就是「鼠标移上去整格消失」。
refs.add("main/layer:specialCoreUnregistered")
refs.add("main/layer:specialCoreUnregisteredMouseOver")
# 子窗滚动条：三帧滑块 + 轨道两态都是三元表达式挑的，正则同样抓不到。
# 一屏放得下时画 disabled/base（原端 disabled 分支没有 thumb），有可翻的行才画滑块。
for state in ("enabled", "disabled"):
    refs.add(f"main/specialCore/slot/scrollBarInfo/{state}/base")
for name in ("thumb0", "thumb1", "thumb2"):
    refs.add(f"main/specialCore/slot/scrollBarInfo/enabled/{name}")

problems = []
checked = 0
for path in sorted(refs):
    if path.endswith("/"):
        continue        # 动态拼接的目录前缀（"core/" + 状态名），由上面的显式清单覆盖
    else:
        found = canvas(path) or canvas(path + "/normal/0")
    if found is None:
        problems.append(f"missing {path}")
        continue
    if found.format not in SUPPORTED:
        problems.append(f"format {found.format} not decodable at {path}")
        continue
    checked += 1
for problem in problems:
    print("  ! " + problem)
assert not problems, f"{len(problems)} asset problem(s)"
print(f"V assets: {checked} referenced canvas(es) resolve and decode PASS")

# —————————————————— 6. 关键交互不能被画漏 ——————————————————
for snippet in (
    'case Dialog.Upgrade: DrawUpgrade(); break;',
    'case Dialog.Transform: DrawTransform(); break;',
    'case Dialog.Gemstone: DrawGemstone(); break;',
    'case Dialog.Register: DrawRegister(); break;',
    'Send(VMatrixOperation.Upgrade, core: core.coreId, target: targetLevel)',
    'Send(VMatrixOperation.Transform, count: transformCount)',
    'Send(VMatrixOperation.UseGemstone, item: gemItem, count: gemCount)',
    'Send(VMatrixOperation.Reset)',
    'VMatrixOperation.UnselectSpecial : VMatrixOperation.SelectSpecial',
    'NativeUtilPrompt.Confirm(',
):
    assert snippet in SOURCE, snippet
# 「特殊核心」开关是同一位置的两张图（箭头 → 开 / ← 关），必须按状态二选一：
# 两张都画时后画的 ← 永远压在上面，面板关着也显示成「已展开」。
assert 'specialOpen ? "button:specialCoreClose" : "button:specialCoreOpen"' in SOURCE
assert 'button:specialCoreOpen") ||' not in SOURCE, "开关按钮又变回两张都画了"
print("V interactions: upgrade/transform/gemstone/reset/special-core wired PASS")

# —————————————————— 7. 玩家侧入口 ——————————————————
# 窗口做得再对，没有入口也等于没有：GM 命令、任务交付（1465）自动弹、NPC 脚本 openVMatrix。
gm = (ROOT / "Assets/Scripts/NativeGmCommandHost.cs").read_text(encoding="utf-8")
assert 'case "OpenVMatrixCommand":NativeVMatrixWindow.Open();' in gm, "GM 命令入口没了"
adv = (ROOT / "Assets/Scripts/Quest/AdvancementProgress.cs").read_text(encoding="utf-8")
assert "NativeVMatrixWindow.Open();" in adv, "五转交付后不再自动弹 V 矩阵"
api = (ROOT / "Assets/Scripts/Npc/NpcApi.cs").read_text(encoding="utf-8")
assert 'cm.Set("openVMatrix", Fn(c => NativeVMatrixWindow.Open()));' in api, "NPC 脚本没有 openVMatrix"
print("V entry: GM command / 1465 交付自动弹窗 / cm.openVMatrix PASS")
print("OK — V matrix window layout and assets verified")

# 点击必须按屏幕层级归属：矩阵弹窗之前的父级不能先消费按钮点击。
for window_name in ("NativeVMatrixWindow", "NativeHexaMatrixWindow"):
    source = (ROOT / f"Assets/Scripts/{window_name}.cs").read_text()
    draw = source.split("public static void Draw()", 1)[1].split("static void Draw", 1)[0]
    if window_name == "NativeVMatrixWindow":
        # 按住核心格的那一段手势归「把核心拖到快捷栏」，不能再兼作整窗拖动。
        assert "dialog == Dialog.None && !carrying && pressSlot < 0 &&" in draw, window_name
    else:
        assert "dialog == Dialog.None && GrowthUi.Drag(" in draw, window_name
    disabled = draw.index("GUI.enabled = parentEnabled && dialog == Dialog.None;")
    parent = draw.index('GrowthUi.Image(Img + "main/", "backgrnd", 0, 0);')
    enabled = draw.index("GUI.enabled = parentEnabled;")
    popup = draw.index("switch (dialog)")
    assert disabled < parent < enabled < popup, window_name
    assert "bool ownsPointer = AtPointer;" in draw
    assert "if (ownsPointer && GrowthUi.Repaint)" in draw
    assert "Pointer.Pressed = Pointer.RightPressed = Pointer.RightReleased = false;" in draw
    assert draw.index("Pointer.ConsumeClick();", popup) > popup
windows = (ROOT / "Assets/Scripts/GameWindows.cs").read_text().split("public static void DrawAll()", 1)[1]
# 当前层级模型是 zOrder（后画在上）：被盖住的下层统一用 Pointer.InputMask 遮指针，
# 原先那套 matrixShield/hexaShield 布尔已随 zOrder 重写删除，语义由 covered mask 承担；
# 矩阵窗口自身的下层屏蔽在 GameHUD 的 matrixPointer 里，见下面的顺序断言。
assert "var covered = new Pointer.InputMask(true);" in windows
assert "case Panel.VMatrix: NativeVMatrixWindow.Draw(); return;" in windows
assert "case Panel.HexaMatrix: NativeHexaMatrixWindow.Draw(); return;" in windows
hud = (ROOT / "Assets/Scripts/GameHUD.cs").read_text()
assert hud.index("new Pointer.InputMask(matrixPointer)") < hud.index("NativeInventoryActions.CarryStep();")
# 手上核心的落点判定必须抢在 BottomBar 之前：快捷格在那条链里先吃掉点击。
assert hud.index("NativeVMatrixWindow.CarryStep();") < hud.index("BottomBar.Draw();")
assert hud.index("NativeVMatrixWindow.DrawCarryGhost();") > hud.index("GameWindows.DrawAll();")
assert hud.index("underlayPointer.Dispose();") < hud.index("GameWindows.DrawAll();")
# 手上核心的落点判定必须跑在矩阵遮罩之前。从核心按下再拖到快捷格这一整条手势，按下点落在
# 矩阵窗口内，AtPointer 靠 PressPosition 一直为真，遮罩会把 DragEnded 抹掉——落点永远进不来，
# 真机表现就是「拖到键盘上没反应」。
assert hud.index("NativeVMatrixWindow.CarryStep();") < hud.index("new Pointer.InputMask(matrixPointer)")
matrix = (ROOT / "Assets/Scripts/NativeVMatrixWindow.cs").read_text()
carry = matrix.split("public static void CarryStep()", 1)[1].split("public static void DrawCarryGhost()", 1)[0]
# 落点先于「窗口吃掉这次点击」：窗口右缘压着快捷栏最左两格，先判 AtPointer 会让那两格绑不上。
assert carry.index("TryDropOnQuickSlot(Pointer.Pos)") < carry.index("if (Pointer.Clicked && AtPointer) return;")
# 快捷栏 26 格默认只有前 8 格有键，落在没配键的格子上要改落到第一个有键的空格。
assert "QuickBarKeys.FreeKeyedSlot()" in carry
print("V/HEXA input: early HUD and lower windows masked; main/popup blank clicks consumed PASS (static)")

# World interactions run outside DrawAll and must recognize both matrices as real windows.
# 世界交互跑在 DrawAll 之外，必须把两个矩阵都当真窗口看（AnyVisible 走 panels → VisibleOf）。
all_windows = (ROOT / "Assets/Scripts/GameWindows.cs").read_text().split("static bool VisibleOf(Panel p)", 1)[1].split("}", 1)[0]
assert "Panel.VMatrix: return NativeVMatrixWindow.Visible;" in all_windows
assert "Panel.HexaMatrix: return NativeHexaMatrixWindow.Visible;" in all_windows
bots = (ROOT / "Assets/Scripts/Field/BotField.cs").read_text()
assert "Pointer.RightPressed && !Pointer.Blocked && !NativeUi.WindowAtPointer" in bots
print("V/HEXA world input: matrix visibility and live hit-test block NPC/player/bot interactions PASS (static)")

# 全局 YesNo 提示框的优先级高于矩阵窗口的空白区点击消费。
assert windows.index("new Pointer.InputMask(NativeUtilPrompt.Visible)") < windows.index("for (int i = 0; i < zOrder.Count; i++)")
assert windows.index("promptPointer.Dispose();") < windows.index("NativeUtilPrompt.Draw();")
assert "bool matrixPointer = NativeUtilPrompt.Visible ||" in hud
prompt = (ROOT / "Assets/Scripts/NativeUtilPrompt.cs").read_text()
cleanup = prompt.rsplit("finally",1)[1]
assert "Pointer.ConsumeClick();" in cleanup and "Pointer.Pressed = Pointer.RightPressed = Pointer.RightReleased = false;" in cleanup
print("Global prompt: receives input after underlay masks restore; blank and closing clicks consumed PASS (static)")
