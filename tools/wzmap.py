#!/usr/bin/env python3
"""把 Map/Map/<id>.img 按原版坐标 1:1 渲染成图片。

MapleStory 的地图是「图层 + 瓦片 + 摆件」拼出来的：
    back   —— 远景（bS 背景集 + 视差 rx/ry + 平铺 type）
    0..8   —— 9 个深度层，每层有 tile（瓦片，tS 地形集 + u 分组 + no 编号）
              和 obj（摆件，oS 物件集 / l0 / l1 / l2）
    foothold —— 站立面（只用来确定「地面线」）

坐标：地图坐标 y 向下，单位是像素。这里不做任何缩放，
渲染结果 1px = 游戏内 1px，交给 Unity 按 PPU 换算即可。
"""
from __future__ import annotations

import os
import sys
from typing import Dict, List, Optional, Tuple

WZPY = "/Users/lizixian/Documents/mxd/BeiDou-Server/tool/wz-python"
DATA = "/Users/lizixian/Documents/mxd/BeiDou-Server/clien/Data"
sys.path.insert(0, WZPY)

from PIL import Image  # noqa: E402
from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.canvas import decode_canvas  # noqa: E402
from wzpy.properties import WzCanvasProperty, WzSubProperty  # noqa: E402

KEY = WzKey.for_region("GMS")

_img_cache: Dict[str, WzImage] = {}
_canvas_cache: Dict[Tuple[str, str], Image.Image] = {}
_origin_cache: Dict[Tuple[str, str, int], Tuple[int, int]] = {}


def _wz(rel: str) -> WzImage:
    """rel 形如 'Map/Map/Map1/104030001' 或 'Map/Tile/grassySoil'。"""
    if rel in _img_cache:
        return _img_cache[rel]
    path = os.path.join(DATA, rel + ".img")
    img = WzImage.from_bytes(open(path, "rb").read(), key=KEY, name=os.path.basename(rel))
    img.parse()
    _img_cache[rel] = img
    return img


def _canvas(rel: str, path: str) -> Optional[Image.Image]:
    key = (rel, path)
    if key in _canvas_cache:
        return _canvas_cache[key]
    node = _wz(rel).get(path)
    # 注意：WzCanvasProperty 继承自 WzSubProperty，必须先判 canvas，
    # 否则会被当成「动画容器」而取到 origin/z 这些子属性
    if not isinstance(node, WzCanvasProperty) and isinstance(node, WzSubProperty):
        kids = [c for c in node.children() if isinstance(c, WzCanvasProperty)]
        node = kids[0] if kids else None
    if not isinstance(node, WzCanvasProperty):
        _canvas_cache[key] = None
        return None
    if node.width <= 1 or node.height <= 1:
        _canvas_cache[key] = None
        return None
    im = decode_canvas(node, region="GMS").convert("RGBA")
    _canvas_cache[key] = im
    return im


def _num(node, name, default=0):
    if node is None:
        return default
    c = node.get(name)
    if c is None:
        return default
    v = getattr(c, "value", default)
    try:
        return int(v)
    except Exception:
        return default


def _vec(node, name):
    if node is None:
        return (0, 0)
    c = node.get(name)
    if c is None:
        return (0, 0)
    return (int(getattr(c, "x", 0)), int(getattr(c, "y", 0)))


def _paste_back(canvas: Image.Image, im: Image.Image, px: int, py: int,
                flip: bool, w: int, h: int) -> None:
    """把远景层的一份拷贝贴上去（flip=True 时左右镜像，用来藏平铺接缝）。"""
    if px > w or py > h or px + im.width < 0 or py + im.height < 0:
        return
    if flip:
        im = im.transpose(Image.FLIP_LEFT_RIGHT)
    canvas.alpha_composite(im, (px, py))


class GameMap:
    def __init__(self, map_id: str, sub: str = "Map1"):
        self.id = map_id
        self.rel = f"Map/Map/{sub}/{map_id}"
        self.img = _wz(self.rel)

        self.backs: List[dict] = []
        b = self.img.get("back")
        if b:
            for n in b.children():
                self.backs.append(dict(
                    no=n.name,
                    set=str(getattr(n.get("bS"), "value", "")),
                    idx=_num(n, "no"),
                    x=_num(n, "x"), y=_num(n, "y"),
                    rx=_num(n, "rx"), ry=_num(n, "ry"),
                    cx=_num(n, "cx"), cy=_num(n, "cy"),
                    type=_num(n, "type"),
                    front=_num(n, "front"),
                    a=_num(n, "a", 255),
                ))

        self.tiles: List[dict] = []
        self.objs: List[dict] = []
        for li in range(9):
            lay = self.img.get(str(li))
            if lay is None:
                continue
            info = lay.get("info")
            tS = str(getattr(info.get("tS"), "value", "")) if info is not None and info.get("tS") is not None else ""
            t = lay.get("tile")
            if t:
                for n in t.children():
                    self.tiles.append(dict(
                        layer=li, set=tS,
                        u=str(getattr(n.get("u"), "value", "")),
                        no=_num(n, "no"),
                        x=_num(n, "x"), y=_num(n, "y"),
                        z=_num(n, "zM") * 100,
                    ))
            o = lay.get("obj")
            if o:
                for n in o.children():
                    self.objs.append(dict(
                        layer=li,
                        set=str(getattr(n.get("oS"), "value", "")),
                        l0=str(getattr(n.get("l0"), "value", "")),
                        l1=str(getattr(n.get("l1"), "value", "")),
                        l2=str(getattr(n.get("l2"), "value", "")),
                        x=_num(n, "x"), y=_num(n, "y"),
                        z=_num(n, "z"),
                        flip=_num(n, "f"),
                    ))

        self.footholds: List[dict] = []
        fh = self.img.get("foothold")
        if fh:
            for l in fh.children():
                for g in l.children():
                    for f in g.children():
                        self.footholds.append(dict(
                            layer=int(l.name),
                            x1=_num(f, "x1"), y1=_num(f, "y1"),
                            x2=_num(f, "x2"), y2=_num(f, "y2"),
                        ))

    # —— 地图范围 ——
    def bounds(self):
        xs, ys = [], []
        for t in self.tiles:
            xs.append(t["x"]); ys.append(t["y"])
        for o in self.objs:
            xs.append(o["x"]); ys.append(o["y"])
        if not xs:
            return (0, 0, 0, 0)
        return (min(xs), min(ys), max(xs), max(ys))

    def flat_runs(self, min_len=0):
        """返回同一条水平地面线的连续区段 [(y, layer, [(x1,x2)...])]"""
        groups: Dict[Tuple[int, int], List[Tuple[int, int]]] = {}
        for f in self.footholds:
            if f["y1"] != f["y2"]:
                continue
            k = (f["y1"], f["layer"])
            groups.setdefault(k, []).append((min(f["x1"], f["x2"]), max(f["x1"], f["x2"])))
        out = []
        for (y, layer), rs in groups.items():
            rs.sort()
            merged: List[List[int]] = []
            for a, b in rs:
                if merged and a <= merged[-1][1] + 1:
                    merged[-1][1] = max(merged[-1][1], b)
                else:
                    merged.append([a, b])
            span = sum(b - a for a, b in merged)
            if span >= min_len:
                out.append(dict(y=y, layer=layer, span=span, runs=[tuple(m) for m in merged],
                                x1=merged[0][0], x2=merged[-1][1]))
        out.sort(key=lambda d: -d["span"])
        return out

    # —— 渲染 ——
    def _back_image(self, bk) -> Optional[Image.Image]:
        rel = "Map/Back/" + bk["set"]
        # Back 集有两种排布：back/<no>（静态）与 ani/<no>（动画，取第 0 帧）
        for path in (f"back/{bk['idx']}", f"ani/{bk['idx']}", str(bk["idx"])):
            im = _canvas(rel, path)
            if im is not None:
                return im
        return None

    def _back_origin(self, bk) -> Tuple[int, int]:
        rel = "Map/Back/" + bk["set"]
        for path in (f"back/{bk['idx']}", f"ani/{bk['idx']}"):
            n = _wz(rel).get(path)
            if n is not None and n.get("origin") is not None:
                o = n.get("origin")
                return int(getattr(o, "x", 0)), int(getattr(o, "y", 0))
        return 0, 0

    def _tile_image(self, t) -> Optional[Image.Image]:
        return _canvas("Map/Tile/" + t["set"], f"{t['u']}/{t['no']}")

    def _tile_origin(self, t) -> Tuple[int, int]:
        """瓦片的锚点偏移（相对图片左上角，y 向下）。

        这里是最容易搞错的地方：
            bsc  origin=(0,0)   size 90x60  → 锚点在左上角，地面块往「下」长
            enH0 origin=(0,38)  size 90x38  → 锚点在左下角，草皮往「上」长

        也就是说 bsc 的 y 是地面块的顶边、enH0 的 y 是草皮的下沿。
        早期版本把所有瓦片统一按左下角摆，整层地面往上偏了 60px，
        结果角色站在 foothold 上时看起来埋在土里。
        """
        key = (t["set"], t["u"], t["no"])
        if key in _origin_cache:
            return _origin_cache[key]
        rel = "Map/Tile/" + t["set"]
        n = _wz(rel).get(f"{t['u']}/{t['no']}")
        o = _vec(n, "origin") if n is not None else (0, 0)
        _origin_cache[key] = o
        return o

    def _obj_image(self, o):
        rel = "Map/Obj/" + o["set"]
        path = f"{o['l0']}/{o['l1']}/{o['l2']}/0"
        node = _wz(rel).get(path)
        if node is None:
            path = f"{o['l0']}/{o['l1']}/{o['l2']}"
            node = _wz(rel).get(path)
            if isinstance(node, WzSubProperty):
                kids = [c for c in node.children() if isinstance(c, WzCanvasProperty)]
                node = kids[0] if kids else None
        if not isinstance(node, WzCanvasProperty):
            return None
        im = _canvas(rel, node.path if hasattr(node, "path") else path)
        if im is None:
            # 直接解码（路径里含帧号时 _canvas 走不到）
            im = decode_canvas(node, region="GMS").convert("RGBA")
        ox, oy = _vec(node, "origin")
        return im, ox, oy

    def render(self, x0: int, y0: int, w: int, h: int,
               layers: Tuple[int, ...] = tuple(range(9)),
               with_backs: bool = True, front_backs: bool = False,
               ground_y: int = None, cam_above: int = 92,
               skip_backs: Tuple[int, ...] = ()) -> Image.Image:
        """ground_y：地面线在地图坐标里的 y（取 foothold）。
        背景的 x/y 是「相对屏幕中心」的偏移（原版就是这么存的），
        所以要先换算到地图坐标：my = ground_y + (back.y - origin.y) - cam_above，
        cam_above 是摄像机中心高出地面线的像素数。

        瓦片和摆件都按各自的 origin 摆（origin 是从锚点到图片左上角的偏移），
        这样 bsc（锚点在顶边）和 enH0（锚点在下沿）才会像原版一样咬合。
        """
        canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))

        if with_backs and ground_y is not None:
            for bk in self.backs:
                if bool(bk["front"]) != front_backs:
                    continue
                if bk["idx"] in skip_backs:
                    continue
                im = self._back_image(bk)
                if im is None:
                    continue
                ox, oy = self._back_origin(bk)
                if bk["a"] < 255:
                    im = im.copy()
                    alpha = im.split()[3].point(lambda v, a=bk["a"]: v * a // 255)
                    im.putalpha(alpha)
                step_x = bk["cx"] if bk["cx"] > 0 else im.width
                step_y = bk["cy"] if bk["cy"] > 0 else im.height
                t = bk["type"]
                rep_x = t in (1, 3, 4, 6, 7)
                rep_y = t in (2, 3, 5, 6, 7)

                base_x = bk["x"] - ox
                base_y = ground_y + (bk["y"] - oy) - cam_above

                i0 = int((x0 - base_x) // step_x) - 1 if rep_x else 0
                i1 = int((x0 + w - base_x) // step_x) + 1 if rep_x else 0
                j0 = int((y0 - base_y) // step_y) - 1 if rep_y else 0
                j1 = int((y0 + h - base_y) // step_y) + 1 if rep_y else 0

                for i in range(i0, i1 + 1):
                    for j in range(j0, j1 + 1):
                        px = base_x + i * step_x - x0
                        py = base_y + j * step_y - y0
                        if px > w or py > h or px + im.width < 0 or py + im.height < 0:
                            continue
                        canvas.alpha_composite(im, (px, py))

                # 不重复、但又比窗口窄的「大远景」（<50% 窗口宽的摆件不算）：
                # 原版这类层 rx≈0，是贴着屏幕的，镜头怎么移它都盖满画面。
                # 静态切片里没有视差，就左右镜像平铺补满，免得切出一半云一半纯蓝。
                if not rep_x and im.width >= w * 0.5 and (base_x > x0 or base_x + im.width < x0 + w):
                    k = 0
                    x = base_x
                    while x < x0 + w:
                        if k > 0:
                            _paste_back(canvas, im, x - x0, base_y - y0, k % 2 == 1, w, h)
                        k += 1
                        x = base_x + k * im.width
                    k = 0
                    x = base_x
                    while x > x0:
                        k += 1
                        x = base_x - k * im.width
                        _paste_back(canvas, im, x - x0, base_y - y0, k % 2 == 1, w, h)

        # 瓦片 + 摆件：同层按 z 排序，瓦片先画
        items = []
        for t in self.tiles:
            if t["layer"] not in layers:
                continue
            items.append((t["layer"], t["z"], 0, "tile", t))
        for o in self.objs:
            if o["layer"] not in layers:
                continue
            items.append((o["layer"], o["z"], 1, "obj", o))
        items.sort(key=lambda it: (it[0], it[1], it[2]))

        for _, _, _, kind, d in items:
            if kind == "tile":
                im = self._tile_image(d)
                if im is None:
                    continue
                ox, oy = self._tile_origin(d)
                px = d["x"] - ox - x0
                py = d["y"] - oy - y0
            else:
                got = self._obj_image(d)
                if got is None:
                    continue
                im, ox, oy = got
                px = d["x"] - ox - x0
                py = d["y"] - oy - y0
                if d["flip"]:
                    im = im.transpose(Image.FLIP_LEFT_RIGHT)
                    px = d["x"] - (im.width - ox) - x0
            if px > w or py > h or px + im.width < 0 or py + im.height < 0:
                continue
            canvas.alpha_composite(im, (px, py))
        return canvas


def find_map_sub(map_id: str) -> str:
    for d in [f"Map{i}" for i in range(10)]:
        if os.path.exists(os.path.join(DATA, "Map/Map", d, map_id + ".img")):
            return d
    raise FileNotFoundError(map_id)


if __name__ == "__main__":
    import argparse
    ap = argparse.ArgumentParser()
    ap.add_argument("map_id")
    ap.add_argument("--x0", type=int, default=0)
    ap.add_argument("--y0", type=int, default=0)
    ap.add_argument("--w", type=int, default=900)
    ap.add_argument("--h", type=int, default=520)
    ap.add_argument("--out", default="/tmp/map_crop.png")
    a = ap.parse_args()
    gm = GameMap(a.map_id, find_map_sub(a.map_id))
    print("backs", len(gm.backs), "tiles", len(gm.tiles), "objs", len(gm.objs), "bounds", gm.bounds())
    for r in gm.flat_runs(400)[:5]:
        print("  flat", r["y"], "layer", r["layer"], "span", r["span"], "x", r["x1"], "..", r["x2"])
    im = gm.render(a.x0, a.y0, a.w, a.h)
    im.save(a.out)
    print("saved", a.out, im.size)
