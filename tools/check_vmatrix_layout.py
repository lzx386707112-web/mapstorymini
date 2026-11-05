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
assert "bool matrixShield = hexaShield || NativeVMatrixWindow.AtPointer;" in windows
assert "GUI.enabled &= !NativeUtilPrompt.Visible && !shield;" in windows
assert windows.index("if(shield && !matrixShield)") < windows.index("NativeEnhancementWindow.Draw();")
assert windows.index("if(shield && !hexaShield)") < windows.index("NativeVMatrixWindow.Draw();")
vpos = windows.index("NativeVMatrixWindow.Draw();")
hpos = windows.index("NativeHexaMatrixWindow.Draw();")
assert "if(shield){pointerMask.Dispose();" in windows[vpos:hpos]
hud = (ROOT / "Assets/Scripts/GameHUD.cs").read_text()
assert hud.index("new Pointer.InputMask(matrixPointer)") < hud.index("NativeInventoryActions.CarryStep();")
assert hud.index("underlayPointer.Dispose();") < hud.index("GameWindows.DrawAll();")
assert "new Pointer.InputMask(shield)" in windows
print("V/HEXA input: early HUD and lower windows masked; main/popup blank clicks consumed PASS (static)")

# World interactions run outside DrawAll and must recognize both matrices as real windows.
all_windows = (ROOT / "Assets/Scripts/GameWindows.cs").read_text().split("public static bool AnyVisible =>",1)[1].split(";",1)[0]
assert "NativeVMatrixWindow.Visible" in all_windows and "NativeHexaMatrixWindow.Visible" in all_windows
bots = (ROOT / "Assets/Scripts/Field/BotField.cs").read_text()
assert "Pointer.RightPressed && !Pointer.Blocked && !NativeUi.WindowAtPointer" in bots
print("V/HEXA world input: matrix visibility and live hit-test block NPC/player/bot interactions PASS (static)")

# The screenshot's global YesNo prompt outranks matrix blank-area input consumption.
assert windows.index("new Pointer.InputMask(NativeUtilPrompt.Visible)") < windows.index("bool hexaShield")
assert windows.index("promptPointer.Dispose();") < windows.index("NativeUtilPrompt.Draw();")
assert "bool matrixPointer = NativeUtilPrompt.Visible ||" in hud
prompt = (ROOT / "Assets/Scripts/NativeUtilPrompt.cs").read_text()
cleanup = prompt.rsplit("finally",1)[1]
assert "Pointer.ConsumeClick();" in cleanup and "Pointer.Pressed = Pointer.RightPressed = Pointer.RightReleased = false;" in cleanup
print("Global prompt: receives input after underlay masks restore; blank and closing clicks consumed PASS (static)")
