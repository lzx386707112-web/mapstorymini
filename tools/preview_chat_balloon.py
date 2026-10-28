"""复刻 BotField.Measure + BalloonBody 的新口径（宽度自适应），
用真 WZ 的 UI/ChatBalloon.img/0 切图离线渲出对照图。

C# 里正文宽度的算法（改后）：
    innerW = Max(12, Ceil(widestLine))      // 最宽那行的实际像素宽，不做步进取整
    innerH = Max(14, lines * lineHeight)
    w = innerW + 12, h = innerH + 12        // 四周内边距固定 6px
    rect.x = anchor.x - w/2                 // 整体以人物头顶为居中
    tailX  = rect.x + (w - 13)/2            // 尾巴在底边居中
"""
import sys
import math
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, '/Users/lizixian/Documents/mxd/mxd-android/MapleDash/tools')
import wzmap

MAX_CHARS = 11
BORDER = 6
FONT_PATH = '/Users/lizixian/Documents/mxd/mxd-android/MapleDash/Assets/Resources/Fonts/ui.ttf'
FONT_PX = 12
PIECES = ['nw', 'ne', 'sw', 'se', 'n', 's', 'w', 'e', 'c', 'arrow']

font = ImageFont.truetype(FONT_PATH, FONT_PX)
ASC, DESC = font.getmetrics()          # 12px: ascent=13 / descent=4 → 行盒 17
LINE_H = max(14, ASC + DESC)           # 与 C# 的 Mathf.Max(14, style.lineHeight) 同口径


def art():
    out = {}
    for name in PIECES:
        im = wzmap._canvas('UI/ChatBalloon', '0/' + name)
        if im is None:
            raise SystemExit('missing piece ' + name)
        out[name] = im
    return out


def text_width(s):
    return font.getlength(s)


def measure(text, mode='fill'):
    """与 C# Measure 同序：11 字符折行（空格不占位）。
    mode='fill' 填满 11 字硬断（当前实现）；mode='word' 退到空格处断（旧实现，对照用）。"""
    lines, widest, start = [], 0.0, 0
    while start < len(text):
        end, count, last_space = start, 0, -1
        while end < len(text) and text[end] != '\n':
            c = text[end]
            if c != ' ' and count + 1 > MAX_CHARS:
                if mode == 'word' and last_space > start:
                    end = last_space
                break
            if c == ' ':
                last_space = end
            else:
                count += 1
            end += 1
        if end == start and text[start] != '\n':
            end = start + 1
        line = text[start:end].strip()
        if line:
            lines.append(line)
            widest = max(widest, text_width(line))
        start = end + 1 if end < len(text) and text[end] == '\n' else end
    if not lines:
        lines = ['']
    inner_w = max(12.0, math.ceil(widest))
    inner_h = max(14, len(lines) * LINE_H)
    return lines, inner_w, inner_h


def body_tex(pieces, inner_w, inner_h):
    """九宫格先合成整图（Blit 自下而上，tx % sw 平铺 + 裁切）。"""
    w, h = int(inner_w) + 2 * BORDER, int(inner_h) + 2 * BORDER
    dst = Image.new('RGBA', (w, h), (0, 0, 0, 0))

    def blit(name, x, top, bw, bh):
        src = pieces[name]
        if bw <= 0 or bh <= 0:
            return
        tile = Image.new('RGBA', (bw, bh), (0, 0, 0, 0))
        sw, sh = src.size
        for ty in range(bh):
            for tx in range(bw):
                tile.putpixel((tx, ty), src.getpixel((tx % sw, ty % sh)))
        dst.alpha_composite(tile, (x, top))

    blit('nw', 0, 0, BORDER, BORDER)
    blit('ne', w - BORDER, 0, BORDER, BORDER)
    blit('sw', 0, h - BORDER, BORDER, BORDER)
    blit('se', w - BORDER, h - BORDER, BORDER, BORDER)
    blit('n', BORDER, 0, int(inner_w), BORDER)
    blit('s', BORDER, h - BORDER, int(inner_w), BORDER)
    blit('w', 0, BORDER, BORDER, int(inner_h))
    blit('e', w - BORDER, BORDER, BORDER, int(inner_h))
    blit('c', BORDER, BORDER, int(inner_w), int(inner_h))
    return dst


def render(pieces, text, scale=2.75, mode='fill'):
    lines, inner_w, inner_h = measure(text, mode)
    w, h = inner_w + 2 * BORDER, inner_h + 2 * BORDER
    art_w, art_h = int(w), int(h)

    arrow = pieces['arrow']
    tail_dy = arrow.height - BORDER          # rect.y = headY - (13-6) - h

    pad = 26
    canvas_w = max(art_w + pad * 2, 120)
    canvas_h = int(tail_dy + art_h + pad * 2 + 20)
    sheet = Image.new('RGBA', (canvas_w, canvas_h), (46, 52, 62, 255))
    draw = ImageDraw.Draw(sheet)

    head = (canvas_w // 2, canvas_h - pad - 6)   # 头顶锚点
    rect_x = head[0] - w / 2
    rect_y = head[1] - tail_dy - h
    tail_x = rect_x + (w - arrow.width) / 2

    sheet.alpha_composite(body_tex(pieces, inner_w, inner_h), (int(round(rect_x)), int(round(rect_y))))
    sheet.alpha_composite(arrow, (int(round(tail_x)), int(round(rect_y + h - BORDER))))
    for i, line in enumerate(lines):
        tw = text_width(line)
        lx = rect_x + BORDER + (inner_w - tw) / 2       # MiddleCenter
        # GUI.Label(MiddleCenter) 居中的是「行盒」（ascent+descent），不是墨迹：
        # 行盒顶 = 正文顶 + i*行高，基线 = 行盒顶 + ascent。
        ly = rect_y + BORDER + i * LINE_H + ASC
        draw.text((lx, ly), line, font=font, fill=(0, 0, 0, 255), anchor='ls')
    # 人物头顶参考点（虚线十字）
    draw.ellipse([head[0] - 4, head[1] - 4, head[0] + 4, head[1] + 4], outline=(255, 90, 90, 255))
    draw.line([head[0], head[1] + 4, head[0], head[1] + 16], fill=(255, 90, 90, 255))

    sheet = sheet.resize((int(sheet.width * scale), int(sheet.height * scale)), Image.NEAREST)
    return sheet, lines, w, h


CASES = [
    '云雪安: 你好',
    'ieskk: 11222',
    '北糖鱼: 今天自由市场人真多啊，谁要组队',
    '小号: 收装备',
]

MODES = [('fill', 'A 填满 11 字硬断（当前实现）'), ('word', 'B 退到空格处断（旧实现，对照）')]

if __name__ == '__main__':
    pieces = art()
    scale = 2.2
    for mode, title in MODES:
        print('==', title)
        for text in CASES:
            lines, inner_w, inner_h = measure(text, mode)
            print('  %-22s 正文 %dx%d 行数=%d %s' % (text, inner_w, inner_h, len(lines), lines))
    cells = []
    for mode, title in MODES:
        col = []
        for text in CASES:
            sheet, lines, w, h = render(pieces, text, scale, mode)
            col.append(sheet)
        cells.append(col)
    pad = 16
    col_w = max(max(c.width for c in col) for col in cells)
    col_h = [sum(c.height for c in col) + pad * (len(col) - 1) for col in cells]
    total_w = col_w * 2 + pad * 3
    total_h = max(col_h) + pad * 2
    out = Image.new('RGBA', (total_w, total_h), (30, 34, 40, 255))
    for ci, col in enumerate(cells):
        x = pad + ci * (col_w + pad)
        y = pad
        for cell in col:
            out.alpha_composite(cell, (x + (col_w - cell.width) // 2, y))
            y += cell.height + pad
    out.save('/tmp/balloon_autowidth.png')
    print('-> /tmp/balloon_autowidth.png', out.size)
