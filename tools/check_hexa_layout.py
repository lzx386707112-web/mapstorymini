#!/usr/bin/env python3
"""HEXA 矩阵窗口的几何 + 素材回归（不需要 Unity / 真机）。

和 check_vmatrix_layout.py 同一套路，四段断言：

  1. 窗口与子窗区：862x624 主窗、childwnd (20,87)-(850,603)、两页 176x35 页签；
  2. 槽位：技能页 18 格、属性页 6 格，坐标 == 282 的 slotPos 原值；
  3. 子窗/弹窗：两个 stat 子窗 + 索尔艾尔达小窗 + 兑换窗 + 强化弹窗的尺寸与按钮落位；
  4. 素材：C# 引用的每一条画布都能解析且可解码（块压缩必须在导入时转 ARGB4444）。

用法：
  python3 tools/check_hexa_layout.py
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
IMG = "UI/HexaMatrixUI.img"
SOURCE = (ROOT / "Assets/Scripts/NativeHexaMatrixWindow.cs").read_text()

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
        if cut < 0 or not target.startswith("UI/HexaMatrixUI.img/"):
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


def float_const(name: str) -> float:
    # 允许 `const float W = 862f, H = 624f;` 这种一行声明多个常量的写法
    match = re.search(rf"(?:const float |,\s*){name} = (-?\d+)f", SOURCE)
    if not match:
        raise SystemExit(f"C# missing const {name}")
    return float(match.group(1))


# —————————————————— 1. 主窗 / 子窗区 / 页签 ——————————————————
w, h = float_const("W"), float_const("H")
assert (w, h) == size("main/backgrnd") == (862, 624), (w, h)
lt, rb = vector("main/vector:childwnd_lt"), vector("main/vector:childwnd_rb")
assert lt == (20, 87) and rb == (850, 603), (lt, rb)
assert (float_const("ChildX"), float_const("ChildY")) == lt
assert (float_const("ChildW"), float_const("ChildH")) == (rb[0] - lt[0], rb[1] - lt[1]) == (830, 516)

# 页签两页 176x35、间隔 4（tab:mainTab/tabSpace）
assert node("main/tab:mainTab/tabSpace").value == 4
for i, expected in enumerate(((20, 50), (200, 50))):
    ox, oy = origin(f"main/tab:mainTab/selected/{i}")
    assert (-ox, -oy) == expected, (i, ox, oy)
for i in range(2):
    assert size(f"main/tab:mainTab/normal/{i}") == (176, 35)
assert (float_const("TabX"), float_const("TabW"), float_const("TabSpace")) == (20, 176, 4)
assert float_const("TabY") in (50, 51)
print("H main: 862x624 frame, child 830x516 at (20,87), two 176x35 tabs at (20,50)/(200,50) PASS")

# —————————————————— 2. 槽位 ——————————————————
SKILL_SLOTS = {
    "skill": [(204, 181), (134, 181), (169, 121), (99, 121), (134, 61), (64, 61)],
    "mastery": [(302, 181), (337, 121), (407, 121), (442, 61)],
    "enforce": [(204, 325), (169, 385), (99, 385), (64, 445)],
    "common": [(302, 325), (337, 385), (407, 385), (442, 445)],
}
for section, expected in SKILL_SLOTS.items():
    actual = [vector(f"hexaSkill/slotPos/{section}/{i}") for i in range(len(expected))]
    assert actual == expected, (section, actual)
assert sum(len(v) for v in SKILL_SLOTS.values()) == 18
# C# 里 18 格的段顺序与每段格数
assert 'static readonly string[] Sections = { "skill", "mastery", "enforce", "common" };' in SOURCE
assert 'static readonly int[] SectionSlots = { 6, 4, 4, 4 };' in SOURCE
assert 'static readonly int[] SectionGroup = { 2, 0, 1, 3 };' in SOURCE

STAT_SLOTS = [(163, 74), (237, 117), (237, 203), (163, 246), (88, 203), (88, 117)]
assert [vector(f"hexaStat/slotPos/{i}") for i in range(6)] == STAT_SLOTS
# 六边形：三档核心 × 两套预设
assert "for (int slot = 0; slot < 6; slot++)" in SOURCE
assert "int index = slot / 2, slotPreset = slot % 2;" in SOURCE
print("H slots: 18 skill-page anchors + 6 stat-page anchors match the 282 tables PASS")

# 六边形底盘：18 格底是 hexaSkill/layer:slot（446x462, origin(-30,-22) → (30,22)），
# hexaSkill/backgrnd 里只有紫色光效，漏画这一层整页就是一片空紫。
assert size("hexaSkill/layer:slot") == (446, 462)
assert 'GrowthUi.Image(Img + "hexaSkill/", "layer:slot", 30, 22)' in SOURCE

# 坐标系口径：hexaSkill/vector:descLT(540,196)、mainCount(608,92)、subCount(726,92)
# 都超过子窗 506 宽，是主窗坐标；必须在 Shift 之外画，否则整块右下偏 (20,87)。
draw = SOURCE.split("public static void Draw()", 1)[1].split("static void DrawSkillDetail", 1)[0]
assert draw.index("DrawResources();") < draw.index("GrowthUi.Shift("), "资源计数落进了子窗坐标系"
assert draw.index("DrawSkillDetail();") > draw.index("GrowthUi.Restore(outer)"), "详情层落进了子窗坐标系"
print("H frames: 18-hex base layer + main-window coordinate space for detail/resource PASS")

# —————————————————— 3. 主窗按钮 / 详情层 / 子窗 / 弹窗 ——————————————————
# WZ origin 是轴心：落位 = (-origin.x, -origin.y)
BUTTONS = {
    "button:close": (822, 10),
    "button:help": (801, 10),
    "button:solErdaTransform": (752, 64),
}
for name, expected in BUTTONS.items():
    ox, oy = origin(f"main/{name}/normal/0")
    assert (-ox, -oy) == expected, (name, ox, oy)
    bw, bh = size(f"main/{name}/normal/0")
    assert expected[0] + bw <= w and expected[1] + bh <= h, name

# 详情层 310x364 @(512,142)：origin 是 (-512,-142)
assert (float_const("DetailX"), float_const("DetailY")) == (512, 142)
assert (float_const("DetailW"), float_const("DetailH")) == size("hexaSkill/layer:default") == (310, 364)
for layer in ("enforceCouple", "enforceSingleMain", "enforceSingleSub",
              "activateCouple", "activateSingleMain", "activateSingleSub",
              "activateCoupleWithReq", "activateSingleMainWithReq", "activateSingleSubWithReq"):
    assert size(f"hexaSkill/layer:{layer}") == (310, 364), layer
assert size("hexaSkill/layer:enforceCompleted") == (283, 144)
for name, expected in (("button:activation", (593, 455)), ("button:enforcement", (593, 455)),
                       ("button:solErdaMini", (793, 109))):
    ox, oy = origin(f"hexaSkill/{name}/normal/0")
    assert (-ox, -oy) == expected, (name, ox, oy)
    bw, bh = size(f"hexaSkill/{name}/normal/0")
    assert expected[0] + bw <= 830 and expected[1] + bh <= 516, name

# 属性页两个子窗贴在子窗区右侧（496 / 489 宽）
assert size("hexaStat/backgrnd") == (822, 543)
assert size("hexaStat/activation_wnd/backgrnd") == (496, 506)
assert size("hexaStat/enforcement_wnd/backgrnd") == (489, 506)
assert (float_const("ActivationW"), float_const("ActivationH")) == (496, 506)
assert (float_const("EnforceW"), float_const("EnforceH")) == (489, 506)
# 两个子窗都锚在 hexaStat/vector:childwnd2_lt (332,1)。hexaStat 底图只有 822 宽，
# 496 宽的子窗贴上去会溢出 6px 到子窗区边界（20+830=850）以内——原端就是这样，不是 bug。
assert (float_const("ActivationX"), float_const("EnforceX")) == (332, 332)
assert float_const("ActivationX") + 496 <= 830 and float_const("EnforceX") + 489 <= 830
ox, oy = origin("hexaStat/activation_wnd/button:activation/normal/0")
assert (-ox, -oy) == (171, 455), (ox, oy)
# 两颗预设卡 152x146，间隔 6，从 (90,29) 起
assert node("hexaStat/enforcement_wnd/tab:coreSlot/tabSpace").value == 6
assert size("hexaStat/enforcement_wnd/tab:coreSlot/selected/0") == (152, 146)
for i, expected in enumerate(((90, 30), (249, 30))):
    ox, oy = origin(f"hexaStat/enforcement_wnd/tab:coreSlot/normal/{i}")
    assert (-ox, -oy) == expected, (i, ox, oy)
for name, expected in (("button:apply", (247, 455)), ("button:apply2", (171, 455)),
                       ("button:change", (12, 471)), ("button:enforcement", (136, 455)),
                       ("button:save", (376, 471)), ("button:reset", (427, 471))):
    ox, oy = origin(f"hexaStat/enforcement_wnd/{name}/normal/0")
    assert (-ox, -oy) == expected, (name, ox, oy)
    bw, bh = size(f"hexaStat/enforcement_wnd/{name}/normal/0")
    assert expected[0] + bw <= 489 and expected[1] + bh <= 506, name

# 索尔艾尔达小窗
assert size("solErdaMini/backgrnd") == (158, 58)
assert (float_const("W") - 158 - 55) == 649  # C# 里贴按钮左侧的硬编码常量
for path, expected in (("solErdaMini/layer:gauge", (65, 7)), ("solErdaMini/layer:iconMain", (37, 37)),
                       ("solErdaMini/layer:iconSub", (35, 33)), ("solErdaMini/button:close/normal/0", (7, 7))):
    assert size(path) == expected, path
ox, oy = origin("solErdaMini/button:close/normal/0")
assert (-ox, -oy) == (144, 7), (ox, oy)

# 兑换窗 408x492
assert size("solErdaTransform/backgrnd") == (408, 492)
assert "GrowthUi.Shift((W - 408) / 2, (H - 492) / 2, 408, 492)" in SOURCE
for i, expected in enumerate(((26, 185), (206, 185))):
    ox, oy = origin(f"solErdaTransform/tab:item/selected/{i + 1}")
    assert (-ox, -oy) == expected, (i, ox, oy)
for name, expected in (("button:confirm", (94, 422)), ("button:close", (206, 422)),
                       ("button:transformCountMax", (96, 317)), ("button:transformCountUP", (268, 317)),
                       ("button:transformCountDown", (290, 317))):
    ox, oy = origin(f"solErdaTransform/{name}/normal/0")
    assert (-ox, -oy) == expected, (name, ox, oy)
    bw, bh = size(f"solErdaTransform/{name}/normal/0")
    assert expected[0] + bw <= 408 and expected[1] + bh <= 492, name

# 强化弹窗 348x514
assert size("hexaSkillPopUp/enforcementAlert/backgrnd") == (348, 514)
assert "GrowthUi.Shift((W - 348) / 2, (H - 514) / 2, 348, 514)" in SOURCE
for name, expected in (("button:levelPlus", (66, 353)), ("button:selectAll", (138, 353)),
                       ("button:initialize", (210, 353)), ("button:confirm", (64, 444)),
                       ("button:cancel", (176, 444))):
    ox, oy = origin(f"hexaSkillPopUp/enforcementAlert/{name}/normal/0")
    assert (-ox, -oy) == expected, (name, ox, oy)
    bw, bh = size(f"hexaSkillPopUp/enforcementAlert/{name}/normal/0")
    assert expected[0] + bw <= 348 and expected[1] + bh <= 514, name
for path, expected in (("hexaSkillPopUp/enforcementAlert/layer:coreCover", (76, 86)),
                       ("hexaSkillPopUp/enforcementAlert/layer:deficiencyAlert", (250, 33))):
    assert size(path) == expected, path
print("H windows: detail 310x364, stat 496x506 & 489x506, mini 158x58,"
      " transform 408x492, upgrade 348x514 PASS")

# —————————————————— 4. 代码引用的素材全部可解码 ——————————————————
refs: set[str] = set()
for match in re.finditer(r'GrowthUi\.(Image|Button)\(\s*Img \+ "([^"]+)"\s*(?:,\s*"([^"]+)")?\s*[,)]', SOURCE):
    root, rel = match.group(2), match.group(3)
    refs.add(root + (rel or ""))

# 动态拼接的分支：运行时按段名 / 状态 / 页号挑，这里把每一种都点一遍。
for name in ("skill", "mastery", "enforce", "common", "stat"):
    refs.add("core/coreBackground/" + name)
    refs.add("core/coreState/unreleased/" + name)
for i in range(6):
    refs.add("core/coreState/activatable/" + str(i))
for page in ("normal", "selected"):
    for i in range(2):
        refs.add(f"main/tab:mainTab/{page}/{i}")
        refs.add(f"hexaStat/enforcement_wnd/tab:coreSlot/{page}/{i}")
    for i in range(1, 3):
        refs.add(f"solErdaTransform/tab:item/{page}/{i}")
for layer in ("default", "enforceCouple", "enforceSingleMain", "enforceSingleSub", "enforceCompleted",
              "activateCouple", "activateSingleMain", "activateSingleSub",
              "activateCoupleWithReq", "activateSingleMainWithReq", "activateSingleSubWithReq"):
    refs.add("hexaSkill/layer:" + layer)
for layer in (0, 1):
    refs.add(f"solErdaTransform/detailLayer/layer:{layer}")
refs.add("core/coreState/selected/0")

# 新属性弹窗与三条强化结果的动态资源分支也必须全部可解码。
for popup in ("activationSet","statChange"):
    refs.add(f"hexaStatPopUp/{popup}/backgrnd")
    for part in range(3): refs.add(f"hexaStatPopUp/{popup}/comboBoxPad/combo:main/normal/{part}")
    for button in (("activation","cancel") if popup=="activationSet" else ("change","cancel")):
        refs.add(f"hexaStatPopUp/{popup}/button:{button}/normal/0")
for i in range(6): refs.add(f"hexaStatPopUp/statChange/layer:slot{i+1}")
refs.add("hexaStatPopUp/statChange/canvas:disabled")
refs.add("hexaStatPopUp/activationSet/layer:coreCover")
for i in range(11): refs.add(f"hexaStat/enforcement_wnd/enchantResult/imgLevel/{i}")
for gauge in ("imgMainGauge","imgAddGauge"):
    for i in range(1,11): refs.add(f"hexaStat/enforcement_wnd/enchantResult/{gauge}/{i}")
assert size("hexaStatPopUp/activationSet/backgrnd")== (450,699)
assert size("hexaStatPopUp/statChange/backgrnd")== (788,688)
assert "Pointer.HitsWindow(ModalRect())" in SOURCE

# Restored result dialogs and animated core outlines are dynamic paths in C#.
for popup in ("activationResult", "enforcementResult"):
    root = f"hexaSkillPopUp/{popup}/"
    for result_image in ("backgrnd", "layer:coreCover"):
        refs.add(root + result_image)
    refs.add(root + ("button:confirm" if popup == "activationResult" else "button:cancel") + "/normal/0")
    assert size(root+"backgrnd") == (348,414)
root = "hexaStatPopUp/enforceResult/"
for result_image in ("backgrnd", "layer:coreCover", "canvas:mainSuccess", "canvas:success", "button:confirm/normal/0"):
    refs.add(root+result_image)
assert size(root+"backgrnd") == (528,489)
for i in range(6): refs.add(f"core/coreState/enforceable/{i}")

problems = []
checked = 0
for path in sorted(refs):
    if path.endswith("/"):
        continue        # 目录前缀（"core/coreBackground/" + 段名），已由上面显式清单覆盖
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
print(f"H assets: {checked} referenced canvas(es) resolve and decode PASS")

# —————————————————— 5. 关键交互不能被画漏 ——————————————————
for snippet in (
    'GrowthUi.Shift(ChildX, ChildY, ChildW, ChildH)',
    'Send(HexaOperation.Activate, core: core.coreId)',
    'Send(HexaOperation.Upgrade, core: core.coreId, target: upgradeTarget)',
    'change ? HexaOperation.ChangeStat : HexaOperation.SetStat',
    'Send(HexaOperation.SaveStat, core: statCore)',
    'Send(HexaOperation.EnforceStat, core: statCore, presetIndex: preset)',
    'Send(HexaOperation.Reset, core: statCore, presetIndex: preset)',
    'Send(HexaOperation.Preset, core: statCore, presetIndex: 0)',
    'Send(HexaOperation.Preset, core: statCore, presetIndex: 1)',
    'GameSession.Net.HexaConvert(new HexaConvertRequest',
    'NativeHexaMatrixService.UpgradeCost(',
    'NativeHexaMatrixService.ActivateCost(',
    'NativeUtilPrompt.Confirm(',
):
    assert snippet in SOURCE, snippet
# 弹窗与子窗都必须切到自己的坐标系、画完复原
# 弹窗与子窗都必须切到自己的坐标系、画完复原（4 个子窗 + 强化弹窗 + 兑换窗 = 5 处；
# 文件头注释里那句 GrowthUi.Shift(20,87,830,516) 不算，所以只数 `var outer =`）
assert len(re.findall(r"var outer\s*=\s*GrowthUi.Shift\(",SOURCE)) == SOURCE.count("GrowthUi.Restore(outer)") == 8
for name in ("DrawActivationWindow", "DrawEnforcementWindow", "DrawUpgrade", "DrawTransform", "DrawSkillResult", "DrawStatResult"):
    body = SOURCE.split(f"static void {name}()", 1)[1].split("\n    static ", 1)[0]
    assert "GrowthUi.Shift(" in body and "GrowthUi.Restore(outer)" in body, name
print("H interactions: activate/upgrade/stat/enforce/reset/preset/convert wired PASS")

# 玩家侧入口：GM 命令、38624 交付（同时标记 38625 完成）自动弹窗、NPC 脚本 openHexaMatrix。
ROOT = Path(__file__).resolve().parents[1]
gm = (ROOT / "Assets/Scripts/NativeGmCommandHost.cs").read_text(encoding="utf-8")
assert 'case "OpenHexaMatrixCommand":NativeHexaMatrixWindow.Open();' in gm, "GM 命令入口没了"
adv = (ROOT / "Assets/Scripts/Quest/AdvancementProgress.cs").read_text(encoding="utf-8")
assert "{ CompleteHexaMatrix(); NativeHexaMatrixWindow.Open(); }" in adv, "38624 交付后不再自动弹 HEXA"
api = (ROOT / "Assets/Scripts/Npc/NpcApi.cs").read_text(encoding="utf-8")
assert 'cm.Set("openHexaMatrix", Fn(c => NativeHexaMatrixWindow.Open()));' in api, "NPC 脚本没有 openHexaMatrix"
print("H entry: GM command / 38624 交付自动弹窗 / cm.openHexaMatrix PASS")
print("OK — HEXA matrix window layout and assets verified")
