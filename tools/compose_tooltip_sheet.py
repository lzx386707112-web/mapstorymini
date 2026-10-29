#!/usr/bin/env python3
"""把 TooltipPreview 抓的整屏图裁成悬浮框、放大、拼成一张对照长图。

输入：<dir>/meta.jsonl + <dir>/NN-key.png（Unity ScreenCapture 输出）
输出：<out>/tooltip-compare.png + <out>/index.html

为什么要裁：整屏 1280x720 里悬浮框只占 236x300 左右，直接在聊天里看
根本看不清字号和行距（用户反馈过「下方属性描述文字很小」）。这里按
ItemTooltip 记下的真实外框裁切，再 2x 最近邻放大，字边缘不发虚。
"""

import argparse
import glob
import html
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont

FONT_CANDIDATES = [
    "/System/Library/Fonts/STHeiti Medium.ttc",
    "/System/Library/Fonts/Supplemental/Songti.ttc",
    "/Library/Fonts/Arial Unicode.ttf",
]


def load_font(size):
    for path in FONT_CANDIDATES:
        if os.path.exists(path):
            try:
                return ImageFont.truetype(path, size)
            except Exception:
                continue
    return ImageFont.load_default()


def load_meta(directory):
    path = os.path.join(directory, "meta.jsonl")
    items = []
    if not os.path.exists(path):
        return items
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue
            try:
                items.append(json.loads(line))
            except json.JSONDecodeError:
                print("跳过无法解析的行：" + line[:80], file=sys.stderr)
    return items


def crop_tooltip(image, meta, pad=6):
    """按 meta 里的 rect 裁切；含 setRect 时把套装面板并进来。

    两种坐标口径：
      unit=norm（新）—— 0..1 相对 UiKit 虚拟画布，直接乘图片尺寸。
      screen（旧）—— Unity GUI 像素坐标，按 图片尺寸/记录的 Screen 尺寸 等比换算。
    norm 更可靠：编辑器里 Screen.width/height 会随 GameView 窗口抖动（录到过 1206x20）。
    """
    if meta.get("unit") == "norm":
        def to_px(r):
            return (r[0] * image.width, r[1] * image.height,
                    r[2] * image.width, r[3] * image.height)
    else:
        screen = meta.get("screen") or [image.width, image.height]
        sx = image.width / float(screen[0]) if screen[0] else 1.0
        sy = image.height / float(screen[1]) if screen[1] else 1.0

        def to_px(r):
            return (r[0] * sx, r[1] * sy, r[2] * sx, r[3] * sy)

    rects = [to_px(meta["rect"])]
    if meta.get("setRect"):
        rects.append(to_px(meta["setRect"]))

    x0 = min(r[0] for r in rects) - pad
    y0 = min(r[1] for r in rects) - pad
    x1 = max(r[0] + r[2] for r in rects) + pad
    y1 = max(r[1] + r[3] for r in rects) + pad

    x0 = max(0, int(round(x0)))
    y0 = max(0, int(round(y0)))
    x1 = min(image.width, int(round(x1)))
    y1 = min(image.height, int(round(y1)))
    if x1 <= x0 or y1 <= y0:
        return None
    return image.crop((x0, y0, x1, y1))


def build(items, src_dir, out_path, scale=2, columns=2):
    font_title = load_font(22)
    font_note = load_font(15)

    cards = []
    for index, meta in enumerate(items, 1):
        path = os.path.join(src_dir, meta["file"])
        if not os.path.exists(path):
            print("缺图 " + path, file=sys.stderr)
            continue
        image = Image.open(path).convert("RGB")
        box = crop_tooltip(image, meta)
        if box is None:
            print("裁切区域为空：" + meta["file"], file=sys.stderr)
            continue
        w, h = box.width * scale, box.height * scale
        zoomed = box.resize((w, h), Image.NEAREST)
        note = "%s  ·  %dx%d" % (meta["file"], box.width, box.height)
        cards.append((meta.get("title") or meta["key"], note, zoomed, meta, index))

    if not cards:
        print("没有可用卡片", file=sys.stderr)
        return False

    gap = 18
    label_h = 52
    col_w = max(c[2].width for c in cards)
    # 行高按列内最大卡片算，避免同一行留白被下一行吃掉
    rows = [cards[i:i + columns] for i in range(0, len(cards), columns)]
    row_heights = [max(c[2].height for c in row) + label_h + gap for row in rows]

    width = columns * col_w + (columns + 1) * gap
    height = sum(row_heights) + gap
    sheet = Image.new("RGB", (width, height), (24, 27, 32))
    draw = ImageDraw.Draw(sheet)

    y = gap
    for row, row_h in zip(rows, row_heights):
        x = gap
        for title, note, image, _meta, _index in row:
            draw.text((x, y), title, font=font_title, fill=(240, 242, 246))
            draw.text((x, y + 26), note, font=font_note, fill=(140, 148, 160))
            sheet.paste(image, (x, y + label_h))
            x += col_w + gap
        y += row_h

    # 顶部再压一条说明，避免别人拿到图不知道是什么
    banner_h = 56
    out = Image.new("RGB", (width, height + banner_h), (24, 27, 32))
    out.paste(sheet, (0, banner_h))
    d2 = ImageDraw.Draw(out)
    d2.text((gap, 14), "冒险岛 MapleDash · 装备悬浮框布局对照（真实存档 + 真实 WZ 素材，2x 放大）",
            font=load_font(20), fill=(255, 216, 61))
    d2.line([(0, banner_h - 1), (width, banner_h - 1)], fill=(56, 62, 72))

    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    out.save(out_path)

    # 附带一个可点开的 HTML：大图 + 原始整屏
    html_path = os.path.join(os.path.dirname(os.path.abspath(out_path)), "index.html")
    cells = []
    for title, note, _image, meta, index in cards:
        original = meta["file"]
        cells.append(
            '<figure><figcaption>%s<small>%s</small></figcaption>'
            '<img src="%s" alt="%s" loading="lazy">'
            '<p class="orig"><a href="%s">原始整屏</a></p></figure>'
            % (html.escape(title), html.escape(note), html.escape(original),
               html.escape(title), html.escape(original)))
    with open(html_path, "w", encoding="utf-8") as handle:
        handle.write(
            "<!DOCTYPE html><html lang=\"zh-CN\"><meta charset=\"utf-8\">"
            "<title>悬浮框对照</title><style>"
            "body{margin:0;padding:24px;background:#181b20;color:#e8ebf0;"
            "font:14px/1.6 -apple-system,'PingFang SC','Microsoft YaHei',sans-serif}"
            "h1{font-size:18px;margin:0 0 16px;color:#ffd83d}"
            "figure{margin:0 0 24px;background:#1e2228;border:1px solid #2c323c;border-radius:8px;padding:14px}"
            "figcaption{font-size:15px;margin-bottom:10px}"
            "figcaption small{display:block;color:#8c94a0;font-size:12px;margin-top:4px}"
            "img{max-width:100%;image-rendering:pixelated;background:#181b20}"
            ".orig{margin:8px 0 0;font-size:12px}.orig a{color:#8ab4f8}"
            "</style><h1>冒险岛 MapleDash · 装备悬浮框布局对照</h1>"
            + "".join(cells) + "</html>")

    print("已生成 %s（%dx%d，%d 张）" % (out_path, out.width, out.height, len(cards)))
    print("已生成 %s" % html_path)
    return True


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("src", nargs="?", default="/tmp/mapledash-tooltip")
    parser.add_argument("-o", "--out", default=None)
    parser.add_argument("--scale", type=int, default=2)
    parser.add_argument("--columns", type=int, default=2)
    args = parser.parse_args()

    items = load_meta(args.src)
    if not items:
        print("没有 meta.jsonl 或里面没有有效记录", file=sys.stderr)
        return 1
    out = args.out or os.path.join(args.src, "tooltip-compare.png")
    return 0 if build(items, args.src, out, args.scale, args.columns) else 1


if __name__ == "__main__":
    sys.exit(main())
