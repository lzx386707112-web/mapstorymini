#!/usr/bin/env python3
"""Spine 2.1.27 参考渲染器（M2B 的像素级对照基准，不是运行时）。

282 客户端的 boss 开场全部是 **spine 2.1.27**（不是 3.8/4.x，spine-unity 4.x 加载不了），
所以 Unity 侧要自研播放器。这个脚本先用 Python 把同一份三件套渲染出来，
Unity 播放器的每一帧都拿它对像素——比对着 json 猜快得多。

已验证（2026-11-05，bossWill/intro_spine）：
* `.atlas` 的 page 名与 region 名都在缩进 0，靠「下一行是不是 size:」区分，不能按缩进切；
* 骨骼 world transform 直接可算，但 **json 是 y-up、画布是 y-down，必须翻转 y**；
* `type: mesh` 的 `uvs` 是 **0..1 归一化**，要乘页面尺寸；`hull`/`edges` 只用于编辑器点选，渲染忽略；
* 多个 anim 段（'1'/'2'/'3'）共用一份 slot 表，未 keyed 的 slot 会按 setup pose 显示，
  所以「把所有 slot 都画」会看到各段角色叠在一起——分段渲染要按 attachment 时间轴过滤。

用法：
  python3 tools/ms282/spine21_preview.py <dir> [anim] [time] [--scale 0.55]
  # <dir> 需含 skeleton.json / skeleton.atlas / page.png

局限（M2B 还要补的）：`ffd`（deform）未应用、`draworder` 未应用、权重网格未支持。
"""
import json
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

DEG = math.pi / 180.0


# ── atlas ──────────────────────────────────────────────────────────────
def parse_atlas(text):
    """atlas 格式：page 名与 region 名都在缩进 0，属性在缩进 2。
    page 名后跟 size/format/filter，region 名后跟 rotate/xy —— 用下一行判断类型。"""
    lines = text.splitlines()
    pages = {}
    cur_page = None
    cur_region = None
    for i, raw in enumerate(lines):
        if not raw.strip():
            cur_region = None
            continue
        stripped = raw.strip()
        indent = len(raw) - len(raw.lstrip())
        if indent == 0 and ":" not in stripped:
            nxt = ""
            for j in range(i + 1, min(i + 4, len(lines))):
                if lines[j].strip():
                    nxt = lines[j].strip()
                    break
            if nxt.startswith("size:"):
                cur_page = {"name": stripped, "regions": {}, "size": None, "px": {}}
                pages[stripped] = cur_page
                cur_region = None
            else:
                cur_page["regions"][stripped] = {}
                cur_region = cur_page["regions"][stripped]
            continue
        if ":" not in stripped:
            continue
        k, v = stripped.split(":", 1)
        k, v = k.strip(), v.strip()
        if cur_region is None:
            if k == "size":
                w, h = v.split(",")
                cur_page["size"] = (int(w), int(h))
            else:
                cur_page["px"][k] = v
        else:
            if k in ("xy", "size", "orig", "offset"):
                cur_region[k] = tuple(int(x) for x in v.split(","))
            elif k == "rotate":
                cur_region[k] = v.lower() in ("true", "90")
            elif k == "index":
                cur_region[k] = int(v)
            else:
                cur_region[k] = v
    return pages


def mat_mul(a, b):
    return (
        a[0] * b[0] + a[2] * b[1],
        a[1] * b[0] + a[3] * b[1],
        a[0] * b[2] + a[2] * b[3],
        a[1] * b[2] + a[3] * b[3],
        a[0] * b[4] + a[2] * b[5] + a[4],
        a[1] * b[4] + a[3] * b[5] + a[5],
    )


def mat_apply(m, x, y):
    return (m[0] * x + m[2] * y + m[4], m[1] * x + m[3] * y + m[5])


def bone_local(b):
    rot = b.get("rotation", 0.0) * DEG
    sx = b.get("scaleX", 1.0)
    sy = b.get("scaleY", 1.0)
    c, s = math.cos(rot), math.sin(rot)
    return (c * sx, s * sx, -s * sy, c * sy, b.get("x", 0.0), b.get("y", 0.0))


def sample(timeline, time, default):
    """2.1 时间轴：list of {time, ...}; 线性/贝塞尔/stepped。"""
    if not timeline:
        return default
    if time <= timeline[0]["time"]:
        return values_of(timeline[0])
    last = timeline[-1]
    if time >= last["time"]:
        return values_of(last)
    for i in range(len(timeline) - 1):
        a, b = timeline[i], timeline[i + 1]
        if not (a["time"] <= time < b["time"]):
            continue
        span = b["time"] - a["time"]
        p = 0.0 if span <= 0 else (time - a["time"]) / span
        if a.get("curve") == "stepped":
            p = 0.0
        elif isinstance(a.get("curve"), list) and len(a["curve"]) == 4:
            p = bezier(p, *a["curve"])
        elif isinstance(a.get("curve"), list) and len(a["curve"]) > 4:
            # 逐点曲线（2.1 少见）：退化为线性
            pass
        va, vb = values_of(a), values_of(b)
        if not va or isinstance(va[0], str):
            return va
        return tuple(x + (y - x) * p for x, y in zip(va, vb))
    return default


def values_of(key):
    if "x" in key:
        return (key.get("x", 0.0), key.get("y", 0.0))
    for name in ("angle", "value", "color"):
        if name in key:
            v = key[name]
            return (v,) if not isinstance(v, (list, tuple)) else tuple(v)
    return ()


def bezier(p, cx1, cy1, cx2, cy2):
    # 解 x(t)=p 的 t，再取 y
    lo, hi = 0.0, 1.0
    for _ in range(20):
        t = (lo + hi) / 2
        x = 3 * (1 - t) ** 2 * t * cx1 + 3 * (1 - t) * t * t * cx2 + t ** 3
        if x < p:
            lo = t
        else:
            hi = t
    t = (lo + hi) / 2
    return 3 * (1 - t) ** 2 * t * cy1 + 3 * (1 - t) * t * t * cy2 + t ** 3


class Skeleton:
    def __init__(self, data):
        self.d = data
        self.bones = {b["name"]: b for b in data.get("bones", [])}
        self.slots = data.get("slots", [])
        self.skins = data.get("skins", {})
        self.anims = data.get("animations", {})
        self.meta = data.get("skeleton", {})

    def attachments(self, skin_name):
        skin = self.skins.get(skin_name) or {}
        if not skin and self.skins:
            skin = list(self.skins.values())[0]
        return skin

    def pose(self, anim_name, time, skin_name=None):
        """返回 slot 列表 -> (bone_matrix, attachment, alpha)。"""
        anim = self.anims.get(anim_name, {})
        bone_tl = anim.get("bones", {})
        slot_tl = anim.get("slots", {})

        world = {}
        alpha = {}

        def solve(name):
            if name in world:
                return world[name]
            b = self.bones[name]
            parent = b.get("parent")
            m = bone_local(b)
            if "rotation" in anim.get("bones", {}).get(name, {}) or True:
                pass
            rot_tl = bone_tl.get(name, {}).get("rotate")
            tr_tl = bone_tl.get(name, {}).get("translate")
            sc_tl = bone_tl.get(name, {}).get("scale")
            rot = sample(rot_tl, time, (b.get("rotation", 0.0),))[0]
            tr = sample(tr_tl, time, (b.get("x", 0.0), b.get("y", 0.0)))
            sc = sample(sc_tl, time, (b.get("scaleX", 1.0), b.get("scaleY", 1.0)))
            loc = bone_local(
                {"rotation": rot, "scaleX": sc[0], "scaleY": sc[1], "x": tr[0], "y": tr[1]}
            )
            if parent:
                world[name] = mat_mul(solve(parent), loc)
            else:
                world[name] = loc
            return world[name]

        for b in self.bones:
            solve(b)

        skin = self.attachments(skin_name)
        out = []
        for slot in self.slots:
            name = slot["name"]
            att_name = slot.get("attachment")
            att_tl = slot_tl.get(name, {})
            if "attachment" in att_tl:
                seq = att_tl["attachment"]
                if time >= 0:
                    cur = att_name
                    for k in seq:
                        if k["time"] <= time:
                            cur = k.get("name")
                    att_name = cur
            base_color = slot.get("color", "ffffffff")
            a = int(base_color[6:8], 16) / 255.0 if len(base_color) >= 8 else 1.0
            if "color" in att_tl:
                col = sample(att_tl["color"], time, ())
                if col:
                    c = col[0]
                    if isinstance(c, str) and len(c) >= 8:
                        a = int(c[6:8], 16) / 255.0
            att = (skin.get(name) or {}).get(att_name) if att_name else None
            if att is not None and "name" not in att:
                att = dict(att, name=att_name)
            out.append((slot, world[slot["bone"]], att, a))
        return out


def quad_from_region(att, region):
    w = att.get("width", region.get("size", (0, 0))[0])
    h = att.get("height", region.get("size", (0, 0))[1])
    ox = att.get("x", 0.0)
    oy = att.get("y", 0.0)
    rot = att.get("rotation", 0.0) * DEG
    sx = att.get("scaleX", 1.0)
    sy = att.get("scaleY", 1.0)
    hw, hh = w / 2 * sx, h / 2 * sy
    pts = [(-hw, -hh), (hw, -hh), (hw, hh), (-hw, hh)]
    c, s = math.cos(rot), math.sin(rot)
    return [(c * px - s * py + ox, s * px + c * py + oy) for px, py in pts]


def uv_from_region(region):
    x, y = region["xy"]
    w, h = region["size"]
    ow, oh = region.get("orig", (w, h))
    offx, offy = region.get("offset", (0, 0))
    # 纹理坐标（top-left 为 uv 原点，atlas 的 xy 已是左上角）
    return [(x, y), (x + w, y), (x + w, y + h), (x, y + h)]


def paste_quad(canvas, page, dst_quad, src_quad, alpha):
    """把 atlas 上的四边形映射到画布目标四边形（仿射近似）。"""
    (sx0, sy0), (sx1, sy1), _, (sx3, sy3) = src_quad
    (dx0, dy0), (dx1, dy1), _, (dx3, dy3) = dst_quad
    # 源矩形 -> 目标平行四边形 的仿射系数
    ax = dx1 - dx0
    ay = dy1 - dy0
    bx = dx3 - dx0
    by = dy3 - dy0
    sw = sx1 - sx0
    sh = sy3 - sy0
    if sw == 0 or sh == 0:
        return
    a = ax / sw
    b = bx / sh
    c = ay / sw
    d = by / sh
    e = dx0 - a * sx0 - b * sy0
    f = dy0 - c * sx0 - d * sy0
    crop = page.crop((int(sx0), int(sy0), int(sx1), int(sy3)))
    if alpha < 1.0:
        r, g, bb, al = crop.split()
        al = al.point(lambda v: int(v * alpha))
        crop = Image.merge("RGBA", (r, g, bb, al))
    # 目标包围盒
    xs = [p[0] for p in dst_quad]
    ys = [p[1] for p in dst_quad]
    bx0, bx1 = int(math.floor(min(xs))), int(math.ceil(max(xs)))
    by0, by1 = int(math.floor(min(ys))), int(math.ceil(max(ys)))
    bw, bh = bx1 - bx0, by1 - by0
    if bw <= 0 or bh <= 0:
        return
    inv = (a * d - b * c)
    if abs(inv) < 1e-9:
        return
    # 目标像素 -> 源像素 的反变换
    ia = d / inv
    ib = -b / inv
    ic = -c / inv
    id_ = a / inv
    ie = (b * f - d * e) / inv - (0) * 0
    ie = (-ia * e - ib * f)
    if_ = (-ic * e - id_ * f)
    warped = crop.transform(
        (bw, bh),
        Image.AFFINE,
        (ia, ib, -ia * bx0 - ib * by0 + (e * 0), ic, id_, -ic * bx0 - id_ * by0),
        resample=Image.BILINEAR,
    )
    # 上面简化有偏，改用完整矩阵：dst = A*src + t  =>  src = A^-1 (dst - t)
    ia = d / inv
    ib = -b / inv
    ic = -c / inv
    id_ = a / inv
    cx = -(ia * e + ib * f)
    cy = -(ic * e + id_ * f)
    warped = crop.transform(
        (bw, bh), Image.AFFINE, (ia, ib, ia * bx0 + ib * by0 + cx, ic, id_, ic * bx0 + id_ * by0 + cy),
        resample=Image.BILINEAR,
    )
    mask = Image.new("L", (bw, bh), 0)
    ImageDraw.Draw(mask).polygon([(p[0] - bx0, p[1] - by0) for p in dst_quad], fill=255)
    canvas.paste(warped, (bx0, by0), mask)


def paste_tri(canvas, page, dst_tri, src_tri):
    (sx0, sy0), (sx1, sy1), (sx2, sy2) = src_tri
    (dx0, dy0), (dx1, dy1), (dx2, dy2) = dst_tri
    S = np.array([[sx1 - sx0, sx2 - sx0], [sy1 - sy0, sy2 - sy0]], dtype=float)
    if abs(np.linalg.det(S)) < 1e-9:
        return
    D = np.array([[dx1 - dx0, dx2 - dx0], [dy1 - dy0, dy2 - dy0]], dtype=float)
    A = D @ np.linalg.inv(S)          # src -> dst 的 2x2
    t = np.array([dx0, dy0]) - A @ np.array([sx0, sy0])
    xs = [dx0, dx1, dx2]
    ys = [dy0, dy1, dy2]
    bx0, by0 = int(math.floor(min(xs))), int(math.floor(min(ys)))
    bx1, by1 = int(math.ceil(max(xs))), int(math.ceil(max(ys)))
    bw, bh = bx1 - bx0, by1 - by0
    if bw <= 0 or bh <= 0 or bw > page.size[0] * 4 or bh > page.size[1] * 4:
        return
    inv = np.linalg.inv(A)
    # 目标像素 -> 源像素
    c = (-inv @ t) + inv @ np.array([bx0, by0])
    coeffs = (inv[0, 0], inv[0, 1], c[0], inv[1, 0], inv[1, 1], c[1])
    if abs(coeffs[0]) > 50 or abs(coeffs[3]) > 50:
        return
    warped = page.transform((bw, bh), Image.AFFINE, coeffs, resample=Image.BILINEAR)
    mask = Image.new("L", (bw, bh), 0)
    ImageDraw.Draw(mask).polygon([(dx0 - bx0, dy0 - by0), (dx1 - bx0, dy1 - by0), (dx2 - bx0, dy2 - by0)], fill=255)
    canvas.paste(warped, (bx0, by0), mask)


def render(skel, atlas, page_img, anim, time, size, bg=(24, 26, 32, 255), offset=(0, 0), scale=1.0):
    canvas = Image.new("RGBA", size, bg)
    import os
    fy = -1.0  # spine json 是 y-up，渲染画布是 y-down
    view = (scale, 0.0, 0.0, scale * fy, offset[0], offset[1])
    slots = skel.pose(anim, time)
    skipped = 0
    drawn = 0
    for slot, bone_m, att, alpha in slots:
        bone_m = mat_mul(view, bone_m)
        if not att:
            continue
        if att.get("type") == "mesh":
            verts = att.get("vertices") or []
            uvs = att.get("uvs") or []
            tris = att.get("triangles") or []
            if len(verts) != len(uvs) or not tris:
                skipped += 1
                continue
            pw, ph = page_img.size
            world = [mat_apply(bone_m, verts[i], verts[i + 1]) for i in range(0, len(verts), 2)]
            uv = [(uvs[i] * pw, uvs[i + 1] * ph) for i in range(0, len(uvs), 2)]
            for ti in range(0, len(tris) - 2, 3):
                a, b, c = tris[ti], tris[ti + 1], tris[ti + 2]
                paste_tri(canvas, page_img, (world[a], world[b], world[c]), (uv[a], uv[b], uv[c]))
            drawn += 1
            continue
        key = att.get("path") or att.get("name") or ""
        region = None
        for p in atlas.values():
            if key in p["regions"]:
                region = p["regions"][key]
                break
        if region is None:
            skipped += 1
            continue
        local = quad_from_region(att, region)
        dst = [mat_apply(bone_m, x, y) for x, y in local]
        src = uv_from_region(region)
        paste_quad(canvas, page_img, dst, src, alpha)
        drawn += 1
    return canvas, drawn, skipped


def main():
    base = Path(sys.argv[1])
    anim = sys.argv[2] if len(sys.argv) > 2 else "1"
    time = float(sys.argv[3]) if len(sys.argv) > 3 else 0.0
    data = json.loads((base / "skeleton.json").read_text())
    atlas = parse_atlas((base / "skeleton.atlas").read_text())
    page_img = Image.open(base / "page.png").convert("RGBA")
    skel = Skeleton(data)
    print("spine", skel.meta.get("spine"), "bones", len(skel.bones), "slots", len(skel.slots),
          "anims", list(skel.anims), "atlas pages", list(atlas))
    size = (1400, 1200)
    out, drawn, skipped = render(skel, atlas, page_img, anim, time, size, offset=(700, 600), scale=0.55)
    out.save(base / f"frame_{anim}_{time:.2f}.png")
    print("drawn", drawn, "skipped", skipped, "->", base / f"frame_{anim}_{time:.2f}.png")


if __name__ == "__main__":
    main()
