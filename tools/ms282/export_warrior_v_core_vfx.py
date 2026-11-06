#!/usr/bin/env python3
"""导出 282「战士职业群五转职业核心」技能特效（施法 + 怪物受击）完整图片。

数据源与依据
------------
* 默认 `--source 282`：**直接读 TMS 282 客户端原端数据**，不经过项目烘焙资源。
  - 定义层：`/tmp/packs/Skill_40001.img`（源端 Packs 解包，BMS 密钥，
    与项目 `Data/Skill/40001.img` 逐帧元数据已核对一致）。
    帧都是 1x1 占位画布，**元数据（origin/delay/z/a0/a1）只在占位上**。
  - 真画布：`~/Documents/mxd/MapleStory/Data/Skill/_Canvas/40001.img`
    （282 原端整包，BMS 密钥，原始像素未烘焙）。
* `--source project`：改从项目拆片 `Data/Skill/_Canvas/40001/<skill>.img`
  （ARGB4444 烘焙）取像素，用于交叉核对项目资源本身有无偏差。
* 源端里存在成片「1x1 且无 outlink」的空帧（时间对齐占位），
  导出成 1x1 透明 PNG 并保留在序列时间轴里，不删、不当成错误。
* 层语义：`docs/tms282-skill/04-vfx-playback.md`、`22/24-skill-vfx-*.md`，
  以及运行时 `Assets/Scripts/SkillVisuals.cs :: CastVisuals / Impact`。

输出（默认 OUT=/Users/lizixian/Documents/mxd/282-warrior-vfx-export）
---------------------------------------------------------------------
  frames/<sid>/<原节点路径>.png   全部逐帧原始画布（一张不少，含 UOL 展开）
  sheets/<sid>_cast_pNN.png       施法侧全帧接触表
  sheets/<sid>_hit_pNN.png        受击侧全帧接触表
  timeline/<sid>_cast.png         施法特效按时间轴叠加的完整演出
  timeline/<sid>_cast_summon.png  召唤物各状态完整演出（若有）
  timeline/<sid>_hit.png          hit/hit2 每个命中变体的完整演出
  timeline/<sid>_hit_overlay.png  affected/specialAffected/mob 等怪物侧演出
  manifest.json                   逐帧 origin/delay/z/outlink 与分类
  index.html                      按职业 / 核心分组的可浏览总览

用法
----
  python3 tools/ms282/export_warrior_v_core_vfx.py --only 400011003   # 单技能样张
  python3 tools/ms282/export_warrior_v_core_vfx.py                     # 全量 157 条（源端像素）
  python3 tools/ms282/export_warrior_v_core_vfx.py --source project --out /tmp/cmp
      # 同一批技能改用项目烘焙分片导出，用于核对项目资源本身有无偏差
"""
from __future__ import annotations

import argparse
import collections
import html
import json
import math
import os
import posixpath
import re
import sys
import traceback
from collections import OrderedDict

sys.path.insert(0, os.path.expanduser(
    "~/Documents/mxd/BeiDou-Server/tool/wz-python"))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.canvas import decode_canvas  # noqa: E402
from wzpy.properties import WzCanvasProperty, WzSubProperty, WzUolProperty  # noqa: E402
from PIL import Image, ImageDraw, ImageFont  # noqa: E402

try:
    import img_bake  # 项目同款 BC3/BC7/DXT3 解码器（读 282 原端块压缩画布）
except Exception as _exc:  # noqa: BLE001
    img_bake = None

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(os.path.dirname(HERE))          # .../MapleDash
WORKSPACE = os.path.dirname(PROJECT)                       # .../mxd-android
DATA = os.path.join(WORKSPACE, "MapleDashResources", "Data")
DEF_IMG = os.path.join(DATA, "Skill", "40001.img")
SRC_DEF = "/tmp/packs/Skill_40001.img"
SRC_CANVAS = os.path.expanduser(
    "~/Documents/mxd/MapleStory/Data/Skill/_Canvas/40001.img")
SRC_CANVAS_DIR = os.path.dirname(SRC_CANVAS)
CATALOG = os.path.join(WORKSPACE, "docs", "tms282-skill", "data", "skills-5th6th.json")
VMATRIX = os.path.join(PROJECT, "Assets", "Resources", "UI", "VMatrixSkills.json")
JOBDOC = os.path.join(WORKSPACE, "docs", "tms282-skill", "05-job-inventory.md")
DEFAULT_OUT = os.path.expanduser("~/Documents/mxd/282-warrior-vfx-export")

# ---- 层分类：与 SkillVisuals.CastVisuals / Impact 的读取点一致 -----------------
# 逐前缀匹配（名字后跟纯数字也算同族，如 effect2 / special0 / screen3）。
CAST_STEMS = (
    "effect", "special", "screen", "prepare", "keydown", "keydownend", "finish",
    "effectFlash", "afterimage", "start", "repeat", "end", "summon", "ball",
    "flipBall", "shootobj", "tile", "mob", "rush", "effect_ple", "frameEffect",
    "customEffect", "affected", "specialAffected", "mob0", "mob1", "mob2",
    "tile0", "ball0", "ball1", "prepare0", "finish0", "keydown0", "keydownend0",
)
HIT_STEMS = (
    "hit", "hit2", "affected", "affected0", "specialAffected", "mob", "mob0",
    "mob1", "mob2", "tile", "tile0", "number", "number2", "ball", "ball0",
    "shootobj",
)
NON_VISUAL_HINT = (
    "additional_process", "skillList", "finalAttack", "multiAttackInfo",
    "trembleEffect", "changeSkill", "extraSkillInfo", "variableNumList",
    "SecondAtom", "addAttack", "avatarAction", "actionList", "range",
    "cancelableSkillID", "icon", "iconMouseOver", "iconDisabled", "info",
    "info2", "common", "level", "req", "psd", "psdSkill", "CameraWork",
)


def stem_of(name):
    return re.sub(r"\d+$", "", name)


def in_stems(name, stems):
    s = stem_of(name)
    return s in stems or name in stems


def is_cast(name):
    return in_stems(name, CAST_STEMS) and not name.startswith("hit")


def is_hit(name):
    return in_stems(name, HIT_STEMS)


def kids(node):
    try:
        return list(node.children())
    except (TypeError, AttributeError):
        return []


def get(node, name):
    for c in kids(node):
        if c.name == name:
            return c
    return None


def value_of(node, name, fallback=None):
    p = get(node, name)
    return getattr(p, "value", fallback) if p is not None else fallback


def as_int(v, fallback=0):
    try:
        return int(v)
    except (TypeError, ValueError):
        return fallback


def origin_of(canvas):
    p = get(canvas, "origin")
    if p is not None and hasattr(p, "x"):
        return (int(p.x), int(p.y))
    return (0, 0)


def natural_key(text):
    return [int(t) if t.isdigit() else t.lower() for t in re.split(r"(\d+)", text)]


# ---- 真画布加载 ---------------------------------------------------------------
class PixelLoader:
    """mode=282：源端 BMS 整包原始像素；mode=project：项目 ARGB4444 烘焙分片。

    源端整包是懒加载的 680MB WzImage，按 `skill/<sid>/<帧路径>` 定位画布；
    outlink 的 inner 路径优先（可能跨技能引用），找不到再按帧路径回退。
    """

    def __init__(self, mode="282", limit=3):
        self.mode = mode
        self.imgs = OrderedDict()
        self.pixels = OrderedDict()
        self.limit = limit
        self._src_roots = OrderedDict()   # shard -> (WzImage, skill 根节点)

    def _image(self, path):
        if path in self.imgs:
            self.imgs.move_to_end(path)
            return self.imgs[path]
        img = WzImage.from_file(path, key=WzKey.for_region("GMS"))
        self.imgs[path] = img
        self.imgs.move_to_end(path)
        while len(self.imgs) > self.limit:
            self.imgs.popitem(last=False)
        return img

    @staticmethod
    def _navigate(root, inner):
        node = root
        for part in inner.split("/"):
            if node is None:
                return None
            node = get(node, part)
        return node

    def source_root(self, shard="40001"):
        """按分片名打开 282 原端 `_Canvas/<shard>.img`（BMS key）。

        少数战士核心会引用别的整包（000.img / 10000.img），不能只开 40001。
        """
        if shard not in self._src_roots:
            path = os.path.join(SRC_CANVAS_DIR, shard + ".img")
            if not os.path.isfile(path):
                return None
            img = WzImage.from_file(path, key=WzKey.for_region("BMS"))
            root = get(img.root, "skill")
            if root is None:
                return None
            self._src_roots[shard] = (img, root)
            self._src_roots.move_to_end(shard)
            while len(self._src_roots) > 4:
                self._src_roots.popitem(last=False)
        else:
            self._src_roots.move_to_end(shard)
        return self._src_roots[shard]

    def node(self, link):
        """project 模式：按 outlink 打开拆片。"""
        split = link.find(".img/")
        if split < 0:
            return None, "outlink 无 .img/ 边界: " + link
        rel = link[:split + 4]
        inner = link[split + 5:]
        path = os.path.join(DATA, rel)
        if not os.path.isfile(path):
            return None, "缺文件 " + rel
        node = self._navigate(self._image(path).root, inner)
        if node is None:
            return None, "缺节点 " + inner
        return node, None

    def pixels_of(self, node, cache_key, src_img=None):
        if cache_key in self.pixels:
            self.pixels.move_to_end(cache_key)
            return self.pixels[cache_key]
        # 282 原端画布里有 2050=BC3 / 4098=BC7 / 1026=DXT3，wzpy 只认前两种之外的
        # 原生格式；img_bake.decode_rgba 是项目烘焙同一套解码器，能拿到原始色深。
        if src_img is not None and img_bake is not None and \
                getattr(node, "format", 0) not in (1, 2, 513):
            arr = img_bake.decode_rgba(node, src_img)
            im = Image.fromarray(arr, "RGBA")
        else:
            data = decode_canvas(node)
            if isinstance(data, Image.Image):
                im = data.convert("RGBA")
            else:
                rgba, w, h = data
                im = Image.frombytes("RGBA", (w, h), rgba)
        self.pixels[cache_key] = im
        self.pixels.move_to_end(cache_key)
        while len(self.pixels) > 64:
            self.pixels.popitem(last=False)
        return im

    @staticmethod
    def project_candidates(f, sid):
        """把 282 的 outlink 写成项目拆片路径的若干候选（含整包形式转拆片）。"""
        out = []
        if f.link:
            out.append(f.link)
            if ".img/" in f.link:
                head, inner = f.link.split(".img/", 1)
                parts = head.split("/")
                if len(parts) == 3:            # Skill/_Canvas/<shard>（整包形式）
                    shard = parts[2]
                    target = inner.split("/")[1] if inner.startswith("skill/") else sid
                    for t in dict.fromkeys((target, sid)):
                        out.append("Skill/_Canvas/%s/%s.img/%s" % (shard, t, inner))
        out.append("Skill/_Canvas/40001/%s.img/skill/%s/%s" % (sid, sid, f.path))
        return list(dict.fromkeys(out))

    def pixels_for_frame(self, f, sid):
        """-> (image|None, source, error|None)。

        返回 (None, "source-empty-placeholder", None) 表示源端本身就是空帧。
        """
        errors = []
        if self.mode == "282":
            # source_root() 已经是整包里的 `skill` 根节点，inner 要去掉这层前缀。
            def strip_skill(p):
                return p[len("skill/"):] if p.startswith("skill/") else p

            candidates = []       # (shard, inner)
            if f.link and ".img/" in f.link:
                head, inner = f.link.split(".img/", 1)
                shard = head.rsplit("/", 1)[-1]
                candidates.append((shard, strip_skill(inner)))
            candidates.append(("40001", "%s/%s" % (sid, f.path)))
            for shard, inner in candidates:
                pair = self.source_root(shard)
                if pair is None:
                    continue
                src_img, root = pair
                node = self._navigate(root, inner)
                if not isinstance(node, WzCanvasProperty) or node.width <= 1:
                    continue
                try:
                    return (self.pixels_of(node, "SRC:%s:%s" % (shard, inner), src_img),
                            "282:%s:%s" % (shard, inner), None)
                except Exception as exc:  # noqa: BLE001
                    errors.append("282 解码失败 %s/%s: %s" % (shard, inner, exc))
            # 源端找不到/解不开 → 回退项目拆片。定义层里的 outlink 有两种写法：
            #   Skill/_Canvas/40001/<sid>.img/skill/...（拆片，直接可用）
            #   Skill/_Canvas/40001.img/skill/...      （整包，项目里要换成拆片）
            for cand in self.project_candidates(f, sid):
                node, err = self.node(cand)
                if err or not isinstance(node, WzCanvasProperty) or node.width <= 1:
                    continue
                try:
                    return (self.pixels_of(node, "PRJ:" + cand),
                            "project-fallback:" + cand, "; ".join(errors) or None)
                except Exception as exc:  # noqa: BLE001
                    errors.append("project 解码失败 %s: %s" % (cand, exc))
            if errors:
                return None, "", "; ".join(errors)
            return None, "source-empty-placeholder", None

        # project 模式
        try:
            if f.link:
                node, err = self.node(f.link)
                if err:
                    return None, "", err
                return (self.pixels_of(node, "PRJ:" + f.link), "project:" + f.link, None)
            if f.inlink:
                target = f.inlink
                if not target.startswith("Skill/"):
                    target = "Skill/40001.img/skill/%s/%s" % (
                        sid, posixpath.normpath(posixpath.join(f.parent, target)))
                node, err = self.node(target)
                if err:
                    return None, "", err
                return (self.pixels_of(node, "PRJ:" + target), "project:" + target, None)
            if f.inline and f.node is not None:
                return (self.pixels_of(f.node, "inline:%s:%s" % (sid, f.path)),
                        "project-inline", None)
        except Exception as exc:  # noqa: BLE001
            return None, "", "解码失败: %s" % exc
        return None, "source-empty-placeholder", None


# ---- 目录 / 字体 -------------------------------------------------------------
_FONT_CANDIDATES = (
    "/System/Library/Fonts/PingFang.ttc",
    "/System/Library/Fonts/STHeiti Medium.ttc",
    "/System/Library/Fonts/Supplemental/Arial Unicode.ttf",
    "/System/Library/Fonts/Helvetica.ttc",
)


def font(size):
    for path in _FONT_CANDIDATES:
        if os.path.isfile(path):
            try:
                return ImageFont.truetype(path, size)
            except OSError:
                continue
    return ImageFont.load_default()


SMALL = font(13)
LABEL = font(16)
TITLE = font(22)
BG = (22, 24, 30, 255)
FG = (232, 236, 244, 255)
MUTED = (150, 160, 178, 255)
ACCENT = (110, 190, 255, 255)


def checker(size, cell=12):
    im = Image.new("RGBA", size, (38, 40, 48, 255))
    d = ImageDraw.Draw(im)
    for y in range(0, size[1], cell):
        for x in range(0, size[0], cell):
            if (x // cell + y // cell) % 2:
                d.rectangle((x, y, x + cell - 1, y + cell - 1), fill=(52, 55, 66, 255))
    return im


def fit_sprite(im, box_w, box_h):
    w, h = im.size
    if w <= 0 or h <= 0:
        return im
    scale = min(box_w / w, box_h / h, 1.0)
    if scale < 1.0:
        im = im.resize((max(1, int(w * scale)), max(1, int(h * scale))), Image.LANCZOS)
    return im


# ---- 目录数据：名字 / 职业 / 核心 ---------------------------------------------
def load_catalog():
    doc = json.load(open(CATALOG, encoding="utf-8"))
    vm = json.load(open(VMATRIX, encoding="utf-8"))
    vcore = doc["vCore"]
    layout = doc["vCoreLayout"]

    jobs = {}
    if os.path.isfile(JOBDOC):
        for line in open(JOBDOC, encoding="utf-8"):
            m = re.match(r"^### (\d+) · (.+?)\s*$", line)
            if m:
                jobs[m.group(1)] = m.group(2).strip()

    simple = {}
    for k, v in vm["skills"].items():
        if v.get("name"):
            simple[str(k)] = v["name"]
    for k, v in vm["names"].items():
        if v.get("name"):
            simple.setdefault(str(k), v["name"])
    trad = {k: v.get("name", "") for k, v in doc["skills"].items()}

    # names 只是 282 的名字字典（1099 条），不是「项目已实现」；
    # 真正接入的是 VMatrixSkills.skills（战士群 19 条 V 核心）。
    enabled = set(vm["skills"].keys())
    core_of = {}          # skill id -> [(coreId, coreName, job, group)]
    warrior_jobs = collections.OrderedDict()
    for job, groups in layout.items():
        rows = []
        for group in ("skill", "common"):
            for cid in groups.get(group, []):
                entry = vcore.get(str(cid))
                if not entry:
                    continue
                for sid in (list(entry.get("connectSkill", [])) +
                            list(entry.get("subSkill", []))):
                    sid = str(sid)
                    if not (sid.startswith("400011") or sid.startswith("400010")):
                        continue
                    core_of.setdefault(sid, []).append(
                        {"core": cid, "coreName": entry.get("name", ""),
                         "job": job, "group": group})
                    rows.append(sid)
        if rows:
            warrior_jobs[job] = sorted(set(rows))
    return {
        "simple": simple, "trad": trad, "jobs": jobs, "core_of": core_of,
        "warrior_jobs": warrior_jobs, "enabled": enabled,
        "vcore": vcore,
    }


# ---- 定义层 → 帧清单 ----------------------------------------------------------
class Frame:
    __slots__ = ("path", "branch", "parent", "index", "origin", "delay", "z",
                 "a0", "a1", "link", "inlink", "uol", "inline", "node", "image",
                 "size", "error", "groups", "source", "empty")

    def __init__(self, path, branch):
        self.path = path
        self.branch = branch
        self.parent = path.rsplit("/", 1)[0] if "/" in path else ""
        self.index = path.rsplit("/", 1)[-1]
        self.origin = (0, 0)
        self.delay = 60
        self.z = 0
        self.a0 = None
        self.a1 = None
        self.link = None
        self.inlink = None
        self.uol = None
        self.inline = False
        self.node = None
        self.image = None
        self.size = (0, 0)
        self.error = None
        self.groups = []
        self.source = ""
        self.empty = False


def collect_frames(skill_node, skill_id, loader, scalars):
    """遍历定义层，返回 (frames, uol_specs, stats)。标量参数落进 scalars。"""
    frames = []
    uol_specs = []

    def walk(node, path):
        for child in kids(node):
            cname = child.name
            cpath = (path + "/" + cname) if path else cname
            if isinstance(child, WzCanvasProperty):
                f = Frame(cpath, path.split("/")[0] if path else cname)
                f.origin = origin_of(child)
                f.delay = as_int(value_of(child, "delay", 60), 60)
                f.z = as_int(value_of(child, "z", 0), 0)
                f.a0 = value_of(child, "a0", None)
                f.a1 = value_of(child, "a1", None)
                link = value_of(child, "_outlink", None)
                inlink = value_of(child, "_inlink", None)
                if link:
                    f.link = str(link)
                if inlink:
                    f.inlink = str(inlink)
                if child.width > 1 or child.height > 1:
                    f.inline = True
                    f.node = child
                frames.append(f)
                continue
            if isinstance(child, WzUolProperty):
                # UOL 是「同一技能内指向另一帧」的引用；展开成独立一帧，
                # 保证 stand/0 这类节点在导出里也有图，不丢路径。
                f = Frame(cpath, path.split("/")[0] if path else cname)
                f.uol = str(getattr(child, "value", "") or "")
                f.delay = 60
                frames.append(f)
                uol_specs.append((f, path, f.uol))
                continue
            if isinstance(child, WzSubProperty):
                scalars[cpath] = {}
                for prop in kids(child):
                    if isinstance(prop, (WzCanvasProperty, WzSubProperty, WzUolProperty)):
                        continue
                    val = getattr(prop, "value", None)
                    if val is not None:
                        scalars[cpath][prop.name] = str(val)
                walk(child, cpath)
                continue
            # 标量（层参数）记录到父路径上
            val = getattr(child, "value", None)
            if val is not None:
                scalars.setdefault(path or "(skill)", {})[cname] = str(val)

    for top in kids(skill_node):
        if not isinstance(top, WzSubProperty):
            continue
        walk(top, top.name)
    return frames, uol_specs


def resolve_frames(frames, uol_specs, loader, skill_id):
    sid = "%07d" % skill_id
    by_path = {f.path: f for f in frames}

    def cross_lookup(target, depth=0):
        """跨技能 UOL：400011059 的 hit/0/0 指向 ../../../400011058/hit/0/0。

        目标节点在定义层的另一个技能下，要从 def_root 取它的画布与元数据；
        目标自身也可能是 UOL，继续跟链。
        """
        if depth > 8:
            return None
        target = re.sub(r"^(?:\.\./)+", "", target)
        parts = target.split("/")
        if parts[0].isdigit() and len(parts[0]) >= 7:
            sid2, rel = parts[0], "/".join(parts[1:])
        else:
            sid2, rel = sid, target
        root = getattr(loader, "def_root", None)
        if root is None:
            return None
        node = loader._navigate(root, sid2 + "/" + rel)
        if node is None:
            return None
        if isinstance(node, WzUolProperty):
            base = posixpath.dirname(sid2 + "/" + rel)
            return cross_lookup(posixpath.normpath(
                posixpath.join(base, str(getattr(node, "value", "")))), depth + 1)
        if not isinstance(node, WzCanvasProperty):
            return None
        tf = Frame(rel, rel.split("/")[0])
        tf.origin = origin_of(node)
        tf.delay = as_int(value_of(node, "delay", 60), 60)
        tf.z = as_int(value_of(node, "z", 0), 0)
        link = value_of(node, "_outlink", None)
        inlink = value_of(node, "_inlink", None)
        tf.link = str(link) if link else None
        tf.inlink = str(inlink) if inlink else None
        if node.width > 1 or node.height > 1:
            tf.inline, tf.node = True, node
        image, source, error = loader.pixels_for_frame(tf, sid2)
        tf.image, tf.source, tf.error = image, source, error
        tf.empty = source == "source-empty-placeholder"
        if image is not None:
            tf.size = image.size
        return (tf, target)
    # 1) 逐帧取真像素（源端整包优先，源端空帧标 empty，不报错）
    for f in frames:
        if f.uol is not None:
            continue
        image, source, error = loader.pixels_for_frame(f, sid)
        f.image = image
        f.source = source
        f.error = error
        f.empty = source == "source-empty-placeholder"
        if f.image is not None:
            f.size = f.image.size
    # 2) UOL 指向同技能内另一帧（../move/0），或跨技能引用（../../../400011058/hit/0/0）
    for f, parent, raw in uol_specs:
        original = posixpath.normpath(posixpath.join(parent, raw))
        target = original
        ref = by_path.get(target)
        hops = 0
        while ref is not None and ref.uol is not None and hops < 8:
            target = posixpath.normpath(posixpath.join(ref.parent, ref.uol))
            ref = by_path.get(target)
            hops += 1
        if ref is None:
            got = cross_lookup(original)
            if got is None:
                f.error = "UOL 解不开: %s -> %s" % (raw, original)
                continue
            ref, target = got
        f.image = ref.image
        f.size = ref.size
        f.empty = ref.empty
        f.error = ref.error
        f.source = "uol->" + target
        f.origin = ref.origin
        f.delay = ref.delay
        f.z = ref.z
    return by_path


def classify(frames):
    for f in frames:
        top = f.branch
        if top in NON_VISUAL_HINT or top.startswith("icon"):
            f.groups = ["reference"] if top.startswith("icon") else ["data"]
            continue
        groups = []
        if is_cast(top):
            groups.append("cast")
        if is_hit(top):
            groups.append("hit")
        if not groups:
            groups.append("other")
        f.groups = groups


# ---- 序列（一层 / 一组 / 一个状态 = 一条时间轴）--------------------------------
class Sequence:
    def __init__(self, path, frames):
        self.path = path
        self.frames = sorted(frames, key=lambda f: natural_key(f.index))
        self.times = []
        t = 0
        for f in self.frames:
            d = max(1, f.delay)
            self.times.append((t, t + d, f))
            t += d
        self.end = t
        # C# NativeVfxClip.StartDelay：首帧明显长于后续帧 = 整层晚出现
        self.start_delay = 0
        if len(self.frames) >= 2:
            first = max(1, self.frames[0].delay)
            rest = sum(max(1, x.delay) for x in self.frames[1:]) / (len(self.frames) - 1)
            if first > 150 and first > rest * 2:
                self.start_delay = first
        self.duration = self.end + self.start_delay

    def frame_at(self, at):
        t = at - self.start_delay
        if t < 0 or t >= self.end:
            return None
        for a, b, f in self.times:
            if a <= t < b:
                return f
        return None


def sequences_of(frames):
    # 空帧必须留在序列里：282 用「1x1 空帧 + delay」做时间对齐占位
    # （例：神圣团结 effect/0..15 全空，真图从 16 起），删掉会把后续帧提前。
    groups = collections.OrderedDict()
    for f in frames:
        groups.setdefault(f.parent, []).append(f)
    return [Sequence(p, fs) for p, fs in groups.items()
            if any(x.image is not None for x in fs)]


def union_bounds(seqs):
    xs, ys = [], []
    for seq in seqs:
        for f in seq.frames:
            if f.image is None:
                continue
            w, h = f.size
            ox, oy = f.origin
            xs.extend((-ox, w - ox))
            ys.extend((-oy, h - oy))
    if not xs:
        return (0, 0, 1, 1)
    return (min(xs), min(ys), max(xs), max(ys))


def render_state(seqs, at, canvas_size, scale, center, background=True):
    im = checker(canvas_size) if background else Image.new("RGBA", canvas_size, (0, 0, 0, 0))
    for seq in seqs:
        f = seq.frame_at(at)
        if f is None or f.image is None:
            continue
        sprite = f.image
        if scale != 1.0:
            sprite = sprite.resize(
                (max(1, int(sprite.width * scale)), max(1, int(sprite.height * scale))),
                Image.LANCZOS)
        ox, oy = f.origin
        x = int(center[0] - ox * scale)
        y = int(center[1] - oy * scale)
        # paste 会自动裁剪越界部分（大屏特效常见负坐标），alpha_composite 不会。
        im.paste(sprite, (x, y), sprite)
    return im


def sample_times(seqs, limit=24):
    """按「帧切换密度」加权取时间点：演出密集段多取，长等待段少取。

    完整逐帧图在 frames/ 与 sheets/，这里是时间轴概览采样。
    """
    weight = collections.defaultdict(int)
    weight[0.0] += 1
    for seq in seqs:
        weight[float(seq.start_delay)] += 1
        for a, _, _ in seq.times:
            weight[float(a + seq.start_delay)] += 1
        weight[float(seq.duration)] += 1
    merged = []
    for t in sorted(weight):
        if merged and t - merged[-1][0] < 40:
            merged[-1][1] += weight[t]
        else:
            merged.append([t, weight[t]])
    if len(merged) <= limit:
        return [t for t, _ in merged]
    # 按累积帧切换数均匀取，密集段自然获得更多采样点；强制保留首尾时刻。
    total = sum(w for _, w in merged)
    targets = [total * i / (limit - 1) for i in range(limit)]
    picked, acc, idx = [], 0.0, 0
    for target in targets:
        while idx < len(merged) - 1 and acc + merged[idx][1] < target:
            acc += merged[idx][1]
            idx += 1
        picked.append(merged[idx][0])
    picked[0], picked[-1] = merged[0][0], merged[-1][0]
    return sorted(set(picked))


def montage(title, tiles, cols=4, tile_size=(400, 300)):
    if not tiles:
        return None
    w, h = tile_size
    head = 34
    label_h = 24
    rows = math.ceil(len(tiles) / cols)
    sheet = Image.new("RGBA", (w * cols, head + rows * (h + label_h)), BG)
    d = ImageDraw.Draw(sheet)
    d.text((14, 6), title, font=TITLE, fill=FG)
    for i, (caption, tile) in enumerate(tiles):
        x, y = (i % cols) * w, head + (i // cols) * (h + label_h)
        if tile.width != w or tile.height != h:
            tile = fit_sprite(tile, w, h)
        bg = Image.new("RGBA", (w, h), (28, 30, 38, 255))
        bg.alpha_composite(tile, ((w - tile.width) // 2, (h - tile.height) // 2))
        sheet.alpha_composite(bg, (x, y))
        d.text((x + 8, y + h + 4), caption, font=SMALL, fill=MUTED)
    return sheet


def make_overlay_timeline(name, seqs, out_dir, tag, limit=24, center_kind="feet"):
    if not seqs:
        return None
    minx, miny, maxx, maxy = union_bounds(seqs)
    bw, bh = maxx - minx, maxy - miny
    box = (400, 280)
    scale = min(1.0, (box[0] - 24) / max(1, bw), (box[1] - 24) / max(1, bh))
    center = ((box[0] // 2) - int((minx + maxx) / 2 * scale),
              (box[1] - 40) if center_kind == "feet" else box[1] // 2)
    # 让所有图都落在画布内：按 union 中心重算锚点
    center = (int(box[0] / 2 - (minx + maxx) / 2 * scale),
              int(box[1] / 2 - (miny + maxy) / 2 * scale))
    times = sample_times(seqs, limit)
    tiles = []
    for at in times:
        tile = render_state(seqs, at, box, scale, center)
        tiles.append(("t=%.2fs" % (at / 1000.0), tile))
    sheet = montage(name + "  施法/场地合成  %d 层 / %d 采样" % (len(seqs), len(times)),
                    tiles, cols=4, tile_size=box)
    if sheet is None:
        return None
    path = os.path.join(out_dir, "timeline", "%s_%s.png" % (tag[0], tag[1]))
    sheet.convert("RGB").save(path, optimize=True)
    return path


def make_variant_timeline(name, seqs, out_dir, sid, suffix="hit", max_rows=12, per_row=6):
    """hit/<变体>、summon/<状态>：每行一条独立序列，展示完整变体池。"""
    if not seqs:
        return []
    paths = []
    seqs_sorted = sorted(seqs, key=lambda s: natural_key(s.path))
    for page in range(math.ceil(len(seqs_sorted) / max_rows)):
        chunk = seqs_sorted[page * max_rows:(page + 1) * max_rows]
        tiles = []
        for seq in chunk:
            minx, miny, maxx, maxy = union_bounds([seq])
            bw, bh = maxx - minx, maxy - miny
            box = (360, 240)
            scale = min(1.0, (box[0] - 20) / max(1, bw), (box[1] - 20) / max(1, bh))
            center = (int(box[0] / 2 - (minx + maxx) / 2 * scale),
                      int(box[1] / 2 - (miny + maxy) / 2 * scale))
            marks = [float(seq.start_delay + a) for a, _, _ in seq.times]
            if not marks:
                marks = [0.0]
            if len(marks) > per_row:
                step = (len(marks) - 1) / (per_row - 1)
                marks = [marks[int(round(i * step))] for i in range(per_row)]
            for at in marks:
                tile = render_state([seq], at, box, scale, center)
                tiles.append(("%s  %.2fs" % (seq.path, at / 1000.0), tile))
        sheet = montage(name + "  %s 变体/状态 %d-%d" %
                        (suffix, page * max_rows + 1, page * max_rows + len(chunk)),
                        tiles, cols=per_row, tile_size=(360, 240))
        if sheet is None:
            continue
        path = os.path.join(out_dir, "timeline",
                            "%s_%s_p%02d.png" % (sid, suffix, page + 1))
        sheet.convert("RGB").save(path, optimize=True)
        paths.append(path)
    return paths


# ---- 接触表：完整逐帧（分页）--------------------------------------------------
def make_sheet(name, frames, out_dir, filename, per_page=30, cols=5):
    if not frames:
        return []
    paths = []
    cell_w, cell_h, label_h = 172, 128, 34
    ordered = sorted(frames, key=lambda f: (natural_key(f.branch), natural_key(f.path)))
    for page in range(math.ceil(len(ordered) / per_page)):
        chunk = ordered[page * per_page:(page + 1) * per_page]
        rows = math.ceil(len(chunk) / cols)
        head = 34
        sheet = Image.new("RGBA", (cell_w * cols, head + rows * (cell_h + label_h)), BG)
        d = ImageDraw.Draw(sheet)
        d.text((12, 5), "%s  [%s]  第 %d/%d 页" %
               (name, filename.replace(".png", ""), page + 1,
                math.ceil(len(ordered) / per_page)), font=LABEL, fill=FG)
        for i, f in enumerate(chunk):
            x, y = (i % cols) * cell_w, head + (i // cols) * (cell_h + label_h)
            tile = checker((cell_w, cell_h), 10)
            if f.image is not None:
                sprite = fit_sprite(f.image, cell_w - 8, cell_h - 8)
                tile.alpha_composite(sprite, ((cell_w - sprite.width) // 2,
                                              (cell_h - sprite.height) // 2))
            else:
                label = "源端空帧" if f.empty else "解不开: %s" % (f.error or "")[:14]
                ImageDraw.Draw(tile).text((10, 50), label, font=LABEL,
                                         fill=MUTED if f.empty else (255, 90, 90, 255))
            sheet.alpha_composite(tile, (x, y))
            d.text((x + 4, y + cell_h + 1), f.path[-30:], font=SMALL, fill=ACCENT)
            d.text((x + 4, y + cell_h + 17),
                   "%dx%d  d=%d  o=(%d,%d)" %
                   (f.size[0], f.size[1], f.delay, f.origin[0], f.origin[1]),
                   font=SMALL, fill=MUTED)
        path = os.path.join(out_dir, "sheets", filename.replace(".png", "_p%02d.png" % (page + 1)))
        sheet.convert("RGB").save(path, optimize=True)
        paths.append(path)
    return paths


# ---- 单个技能 ----------------------------------------------------------------
def export_skill(skill_id, node, loader, cat, out_dir, args):
    sid = "%07d" % skill_id
    if args.merge:
        # 增量补导时清掉该技能旧的接触表/时间轴，避免分页数量变化后留下旧图。
        for sub in ("sheets", "timeline"):
            folder = os.path.join(out_dir, sub)
            if not os.path.isdir(folder):
                continue
            for fn in os.listdir(folder):
                if fn.startswith(sid + "_") and fn.endswith(".png"):
                    os.remove(os.path.join(folder, fn))
    scalars = {}
    frames, uols = collect_frames(node, skill_id, loader, scalars)
    resolve_frames(frames, uols, loader, skill_id)
    classify(frames)

    name = cat["simple"].get(sid) or cat["trad"].get(sid) or ""
    if not name:
        cores = cat["core_of"].get(sid, [])
        name = cores[0]["coreName"] if cores else "(内部技能体)"
    cores = cat["core_of"].get(sid, [])
    jobs = sorted({c["job"] for c in cores}, key=lambda x: int(x) if x.isdigit() else 0)

    # 1) 逐帧原始 PNG（全部，一张不少）
    frame_dir = os.path.join(out_dir, "frames", sid)
    os.makedirs(frame_dir, exist_ok=True)
    out_frames = 0
    for f in frames:
        rel = f.path.replace("/", os.sep) + ".png"
        path = os.path.join(frame_dir, rel)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        if f.image is not None:
            f.image.save(path, optimize=True)
            out_frames += 1
        else:
            # 源端本来就是 1x1 空画布：忠实导出成 1x1 全透明 PNG，
            # 路径一张不少，且不把「时间对齐占位」误画成错误方块。
            Image.new("RGBA", (1, 1), (0, 0, 0, 0)).save(path)

    cast_frames = [f for f in frames if "cast" in f.groups]
    hit_frames = [f for f in frames if "hit" in f.groups]
    other_frames = [f for f in frames if not ({"cast", "hit"} & set(f.groups))
                    and f.groups != ["data"]]

    sheet_cast = make_sheet("%s %s" % (sid, name), cast_frames, out_dir,
                            "%s_cast.png" % sid) if not args.no_sheets else []
    sheet_hit = make_sheet("%s %s" % (sid, name), hit_frames, out_dir,
                           "%s_hit.png" % sid) if not args.no_sheets else []
    sheet_other = make_sheet("%s %s 其它" % (sid, name), other_frames, out_dir,
                             "%s_other.png" % sid) if not args.no_sheets else []

    timelines = {}
    if not args.no_timeline:
        def seqs_for(pred, exclude=()):
            # 不要把空帧过滤掉：它们承载 delay，是源端的时间对齐占位。
            return sequences_of([f for f in frames if pred(f)])

        # 施法侧：非 summon 状态层叠加
        overlay = [s for s in seqs_for(lambda f: "cast" in f.groups and
                                       not f.branch.startswith("summon"))
                   if s.frames]
        p = make_overlay_timeline("%s %s 施法" % (sid, name), overlay, out_dir,
                                  (sid, "cast"), center_kind="feet")
        if p:
            timelines["cast"] = os.path.relpath(p, out_dir)
        # 召唤物各状态（每个状态一行）
        sm = make_variant_timeline("%s %s" % (sid, name),
                                   [s for s in seqs_for(lambda f: f.branch.startswith("summon"))
                                    if s.frames],
                                   out_dir, sid, "cast_summon", max_rows=10, per_row=6)
        if sm:
            timelines["cast_summon"] = [os.path.relpath(x, out_dir) for x in sm]
        # 受击侧：hit/hit2 变体池（每条完整一行）
        hv = make_variant_timeline("%s %s 怪物被击中" % (sid, name),
                                   [s for s in seqs_for(lambda f: f.branch.startswith("hit"))
                                    if s.frames],
                                   out_dir, sid, "hit", max_rows=12, per_row=6)
        if hv:
            timelines["hit"] = [os.path.relpath(x, out_dir) for x in hv]
        # 怪物身上/场地的持续层
        ho = [s for s in seqs_for(lambda f: "hit" in f.groups and
                                  not f.branch.startswith("hit"))
              if s.frames]
        p = make_overlay_timeline("%s %s 怪物侧" % (sid, name), ho, out_dir,
                                  (sid, "hit_overlay"), center_kind="mob")
        if p:
            timelines["hit_overlay"] = os.path.relpath(p, out_dir)

    branch_stats = collections.OrderedDict()
    for f in frames:
        e = branch_stats.setdefault(f.branch, {"frames": 0, "groups": f.groups,
                                               "with_pixels": 0, "errors": 0})
        e["frames"] += 1
        if f.image is not None:
            e["with_pixels"] += 1
        if f.error:
            e["errors"] += 1

    manifest = {
        "id": sid,
        "name": name,
        "name_trad": cat["trad"].get(sid, ""),
        "jobs": [{"code": j, "name": cat["jobs"].get(j, "")} for j in jobs],
        "cores": cores,
        "in_project": sid in cat["enabled"],
        "source_mode": args.source,
        "frames_total": len(frames),
        "frames_with_pixels": sum(1 for f in frames if f.image is not None),
        "frames_source_empty": sum(1 for f in frames if f.empty),
        "frames_project_fallback": sum(1 for f in frames
                                       if f.source.startswith("project-fallback")),
        "frames_failed": sum(1 for f in frames if f.error),
        "cast_frames": len(cast_frames),
        "hit_frames": len(hit_frames),
        "branches": branch_stats,
        "scalars": scalars,
        "sheets": {
            "cast": [os.path.relpath(x, out_dir) for x in sheet_cast],
            "hit": [os.path.relpath(x, out_dir) for x in sheet_hit],
            "other": [os.path.relpath(x, out_dir) for x in sheet_other],
        },
        "timeline": timelines,
        "frames": [{
            "path": f.path, "file": "frames/%s/%s.png" % (sid, f.path),
            "size": list(f.size), "origin": list(f.origin), "delay_ms": f.delay,
            "z": f.z, "a0": f.a0, "a1": f.a1, "source": f.source,
            "outlink": f.link, "inlink": f.inlink, "uol": f.uol,
            "groups": f.groups, "source_empty": f.empty, "error": f.error,
        } for f in frames],
    }
    return manifest


# ---- 索引 --------------------------------------------------------------------
def build_index(manifests, out_dir, cat):
    def esc(x):
        return html.escape(str(x))

    by_job = collections.OrderedDict()
    for m in manifests:
        keys = [j["code"] for j in m["jobs"]] or ["(未归属)"]
        for k in keys:
            by_job.setdefault(k, []).append(m)

    order = {"112": 0, "122": 1, "132": 2, "1112": 3, "2112": 4, "3112": 5,
             "3122": 6, "5112": 7, "6112": 8, "10112": 9, "4112": 10,
             "15112": 11, "15512": 12, "17512": 13}
    job_keys = sorted(by_job, key=lambda k: (order.get(k, 99), int(k) if k.isdigit() else 9999))

    parts = ["""<!doctype html><html lang="zh-CN"><head><meta charset="utf-8">
<title>282 战士五转职业核心 · 技能特效完整导出</title>
<style>
:root{color-scheme:dark}
body{background:#14161c;color:#e8ecf4;font:14px/1.6 -apple-system,BlinkMacSystemFont,"PingFang SC","Microsoft YaHei",sans-serif;margin:0;padding:28px 34px}
h1{font-size:24px;margin:0 0 6px}h2{font-size:18px;margin:30px 0 10px;border-bottom:1px solid #333a48;padding-bottom:6px}
p{color:#a8b2c4;max-width:1100px}
code{background:#242833;padding:1px 5px;border-radius:4px;color:#9fd0ff}
.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(310px,1fr));gap:14px}
.card{background:#1e222c;border:1px solid #313848;border-radius:10px;padding:12px;overflow:hidden}
.card h3{margin:0 0 4px;font-size:15px}.meta{color:#9aa5b8;font-size:12px;margin-bottom:8px}
.card img{width:100%;background:#171a21;border:1px solid #2b3140;border-radius:6px;display:block;margin-bottom:6px}
.badge{display:inline-block;font-size:11px;padding:1px 6px;border-radius:4px;margin-right:4px}
.on{background:#1d4630;color:#8ee2ac}.off{background:#3a2a2a;color:#e0a2a2}
a{color:#7cc0ff;text-decoration:none}a:hover{text-decoration:underline}
.links{font-size:12px;line-height:1.9}
.stat{background:#1b202b;border:1px solid #2f3748;border-radius:8px;padding:12px 16px;display:inline-block;margin:0 10px 10px 0}
.stat b{font-size:20px;color:#9fd0ff}
</style></head><body>
<h1>282 战士职业群 · 五转职业核心技能特效</h1>
<p>像素<b>直接取自 TMS 282 客户端原端</b>：定义层 <code>/tmp/packs/Skill_40001.img</code>
（BMS 密钥，占位帧上带 origin/delay/z），真画布
<code>~/Documents/mxd/MapleStory/Data/Skill/_Canvas/40001.img</code>（282 原始整包，未烘焙）。
元数据已与项目 <code>Data/Skill/40001.img</code> 逐帧核对一致。
每一张画布都导出成原始分辨率 PNG，按原节点路径存放；逐帧 origin/delay/z/outlink 见
<a href="manifest.json">manifest.json</a>。用于与当前项目里实际播放的特效逐帧对比。</p>
<p>源端里成片的「1x1 空帧」是<b>时间对齐占位</b>（例如神圣团结 effect/0..15 全空、真图从 16 起），
已导出为 1x1 透明 PNG 并保留在序列时间轴里，不删除、不算错误。接触表里标「源端空帧」。</p>
<p><b>怎么对照：</b>「施法时间轴」= 运行时 <code>SkillVisuals.CastVisuals</code> 会播的层（effect*/special*/screen*/prepare*/keydown*/finish*/tile/ball/summon…）按 delay 与 origin 叠加的完整演出；
「受击时间轴」= <code>Impact</code> 的 hit/hit2 变体池 + affected/specialAffected/mob 等挂在怪物身上的层。
hit 的每一组是<b>独立命中变体</b>，不是连续动画，所以按变体逐行展示。
接触表里是全部帧（含每帧尺寸/delay/锚点），原始分辨率帧图在 <code>frames/&lt;技能id&gt;/&lt;层路径&gt;.png</code>。
没有职业归属的条目是各核心的<b>内部攻击体 / 子技能 / 召唤物状态</b>，它们同样有独立特效，
已按完整帧导出，排在最后一组。</p>
"""]

    total_frames = sum(m["frames_total"] for m in manifests)
    total_px = sum(m["frames_with_pixels"] for m in manifests)
    total_fail = sum(m["frames_failed"] for m in manifests)
    total_empty = sum(m["frames_source_empty"] for m in manifests)
    total_fb = sum(m["frames_project_fallback"] for m in manifests)
    enabled = sum(1 for m in manifests if m["in_project"])
    parts.append('<div>')
    for label, v in (("技能", len(manifests)), ("画布帧", total_frames),
                     ("有像素", total_px), ("源端空占位", total_empty),
                     ("回退项目画布", total_fb), ("解不开", total_fail),
                     ("项目当前已接入", enabled)):
        parts.append('<div class="stat">%s <b>%s</b></div>' % (label, v))
    parts.append('</div>')

    quick = [m for m in manifests if m["in_project"]]
    if quick:
        parts.append('<h2>项目当前 V 矩阵已接入（%d 条，优先对比）</h2>' % len(quick))
        parts.append('<p>' + " · ".join(
            '<a href="#s%s">%s <code>%s</code></a>' %
            (esc(m["id"]), esc(m["name"]), esc(m["id"])) for m in quick) + '</p>')

    for job in job_keys:
        rows = sorted(by_job[job], key=lambda m: m["id"])
        jname = cat["jobs"].get(job, "")
        parts.append('<h2>%s · %s （%d 条）</h2>' %
                     (esc(job), esc(jname), len(rows)))
        parts.append('<div class="grid">')
        for m in rows:
            casts = m["timeline"].get("cast")
            hits = m["timeline"].get("hit") or []
            preview = casts or (hits[0] if hits else None)
            core = m["cores"][0] if m["cores"] else {}
            parts.append('<div class="card" id="s%s">' % esc(m["id"]))
            parts.append('<h3>%s <span style="color:#7f8ba0">%s</span></h3>' %
                         (esc(m["name"]), esc(m["id"])))
            parts.append('<div class="meta">核心 %s %s · %d 帧（施法 %d / 受击 %d）'
                         '<span class="badge %s">%s</span></div>' %
                         (esc(core.get("core", "—")), esc(core.get("coreName", "")),
                          m["frames_total"], m["cast_frames"], m["hit_frames"],
                          "on" if m["in_project"] else "off",
                          "项目已接入" if m["in_project"] else "项目未接入"))
            if preview:
                parts.append('<a href="%s"><img loading="lazy" src="%s" alt="%s"></a>' %
                             (esc(preview), esc(preview), esc(m["name"])))
            links = []
            if casts:
                links.append('<a href="%s">施法时间轴</a>' % esc(casts))
            if m["timeline"].get("cast_summon"):
                links.append('<a href="%s">召唤状态</a>' %
                             esc(m["timeline"]["cast_summon"][0]))
            if hits:
                links.append('<a href="%s">受击时间轴</a>' % esc(hits[0]))
            if m["timeline"].get("hit_overlay"):
                links.append('<a href="%s">怪物侧叠加</a>' % esc(m["timeline"]["hit_overlay"]))
            for kind, key in (("施法帧表", "cast"), ("受击帧表", "hit"), ("其它帧表", "other")):
                if m["sheets"].get(key):
                    links.append('<a href="%s">%s(%d页)</a>' %
                                 (esc(m["sheets"][key][0]), kind, len(m["sheets"][key])))
            links.append('<a href="frames/%s/">逐帧 PNG 目录</a>' % esc(m["id"]))
            parts.append('<div class="links">%s</div>' % " · ".join(links))
            parts.append('</div>')
        parts.append('</div>')
    parts.append('</body></html>')
    path = os.path.join(out_dir, "index.html")
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(parts))
    return path


# ---- main ---------------------------------------------------------------------
def main(argv):
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=DEFAULT_OUT)
    ap.add_argument("--only", default="", help="只导出这些技能 id，逗号分隔")
    ap.add_argument("--source", choices=("282", "project"), default="282",
                    help="282=源端 BMS 原始像素（默认）；project=项目 ARGB4444 烘焙分片")
    ap.add_argument("--def-img", default="", help="覆盖定义层 img 路径")
    ap.add_argument("--no-sheets", action="store_true")
    ap.add_argument("--no-timeline", action="store_true")
    ap.add_argument("--limit", type=int, default=0, help="只处理前 N 条（调试）")
    ap.add_argument("--merge", action="store_true",
                    help="配合 --only 增量补导：把新结果合并回已有 manifest.json / index.html")
    args = ap.parse_args(argv)

    out_dir = os.path.abspath(args.out)
    for sub in ("frames", "sheets", "timeline"):
        os.makedirs(os.path.join(out_dir, sub), exist_ok=True)

    if args.source == "282" and not os.path.isfile(SRC_CANVAS):
        raise SystemExit("缺源端画布整包: " + SRC_CANVAS)

    cat = load_catalog()
    loader = PixelLoader(mode=args.source)
    if args.def_img:
        def_path = args.def_img
        def_key = "BMS" if "packs" in def_path else "GMS"
    elif args.source == "282" and os.path.isfile(SRC_DEF):
        def_path, def_key = SRC_DEF, "BMS"
    else:
        def_path, def_key = DEF_IMG, "GMS"
    print("定义层: %s (%s)   像素源: %s" %
          (def_path, def_key, args.source))
    defimg = WzImage.from_file(def_path, key=WzKey.for_region(def_key))
    skill_root = get(defimg.root, "skill")
    loader.def_root = skill_root          # 供跨技能 UOL 解析
    nodes = {c.name: c for c in kids(skill_root) if isinstance(c, WzSubProperty)}

    wanted = [x.strip() for x in args.only.split(",") if x.strip()]
    ids = wanted or sorted(nodes)
    if args.limit:
        ids = ids[:args.limit]

    manifests = []
    failures = []
    for i, sid in enumerate(ids, 1):
        node = nodes.get(sid)
        if node is None:
            failures.append((sid, "定义层缺技能节点"))
            continue
        try:
            m = export_skill(int(sid), node, loader, cat, out_dir, args)
        except Exception:  # noqa: BLE001
            failures.append((sid, traceback.format_exc(limit=4)))
            continue
        manifests.append(m)
        print("[%3d/%3d] %s %-16s 帧 %4d (施法 %3d / 受击 %3d) "
              "空帧 %3d 回退 %2d 失败 %d" %
              (i, len(ids), sid, m["name"], m["frames_total"],
               m["cast_frames"], m["hit_frames"], m["frames_source_empty"],
               m["frames_project_fallback"], m["frames_failed"]))
        sys.stdout.flush()

    manifest_path = os.path.join(out_dir, "manifest.json")
    if args.merge and args.only and os.path.isfile(manifest_path):
        new_ids = {m["id"] for m in manifests}
        with open(manifest_path, encoding="utf-8") as f:
            old = json.load(f)
        merged = {s["id"]: s for s in old.get("skills", [])}
        for m in manifests:
            merged[m["id"]] = m
        order = [s["id"] for s in old.get("skills", [])]
        for m in manifests:
            if m["id"] not in order:
                order.append(m["id"])
        failures = [x for x in old.get("failures", []) if x[0] not in new_ids] + failures
        manifests = [merged[i] for i in order if i in merged]
        print("增量合并后技能数:", len(manifests))

    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump({"source": {
            "definition": def_path + " (%s key)" % def_key,
            "canvas": (SRC_CANVAS + " (TMS 282 原端整包, BMS key, 未烘焙)"
                       if args.source == "282" else
                       "MapleDashResources/Data/Skill/_Canvas/40001/*.img (ARGB4444 烘焙)"),
            "source_mode": args.source,
            "layer_rules": "docs/tms282-skill/04-vfx-playback.md + "
                           "SkillVisuals.CastVisuals/Impact",
        }, "skills": manifests, "failures": failures}, f,
            ensure_ascii=False, indent=1)
    index = build_index(manifests, out_dir, cat)
    total = sum(m["frames_total"] for m in manifests)
    print("\n技能 %d 条，画布帧 %d 张（源端空占位 %d，回退项目画布 %d，解不开 %d），"
          "技能级失败 %d 条" %
          (len(manifests), total,
           sum(m["frames_source_empty"] for m in manifests),
           sum(m["frames_project_fallback"] for m in manifests),
           sum(m["frames_failed"] for m in manifests), len(failures)))
    print("索引:", index)
    print("输出:", out_dir)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
