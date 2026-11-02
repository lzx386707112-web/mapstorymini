#!/usr/bin/env python3
"""复算「野外 Boss 出生点是否落在地图可视范围 VR 内」。

完全复刻 FieldDirector 的两条公式：
  1) CalcPointBelow(src P->үес calcPointBelow): y = min{ fh.y(x) | fh.y(x) >= life.y }，找不到则返回 life.y
  2) DropToFoothold 再 -1
然后用 info/vr 判定是否落在可视矩形内（对应日志里那条 "Boss 出生点在可视范围之外"）。

不同于 check_wild_boss_assets.py（只查素材是否存在），这里查的是「刷出来了也在地图外面」
——这正是玩家"去对应地图看都没发现"最常见的形态。
"""
import sys, json, os
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from imgdump import WzImage, WzKey, DEFAULT_DATA

KEY = WzKey.for_region('GMS')
_cache = {}


def load(path):
    key = str(path)
    if key not in _cache:
        im = WzImage.from_bytes(Path(path).read_bytes(), key=KEY, name=Path(path).name)
        im.parse()
        _cache[key] = im
    return _cache[key]


def val(node, default=None):
    return getattr(node, 'value', default) if node is not None else default


def collect(out, node, prefix, minxz=None, maxxz=None, minyz=None, maxyz=None):
    """递归收集 info/vr 或 life.Group."""
    pass


def map_path(mid):
    return DEFAULT_DATA / 'Map' / 'Map' / ('Map%d' % (mid // 100000000)) / ('%09d.img' % mid)


def footholds_of(mid):
    p = map_path(mid)
    if not p.exists():
        return None
    im = load(p)
    root = im.root
    node = root.get('foothold')
    out = []
    if node is None:
        return out
    for layer in node.children():
        for grp in layer.children():
            for fh in grp.children():
                x1 = val(fh.get('x1')); x2 = val(fh.get('x2'))
                y1 = val(fh.get('y1')); y2 = val(fh.get('y2'))
                if None not in (x1, x2, y1, y2):
                    out.append((x1, y1, x2, y2))
    return out


def vr_of(mid):
    p = map_path(mid)
    if not p.exists():
        return None
    info = load(p).get('info')
    vr = info.get('vr') if info is not None else None
    if vr is None:
        return None
    return (val(vr.get('left'), 0), val(vr.get('top'), 0),
            val(vr.get('right'), 0), val(vr.get('bottom'), 0))


def info_of(mid):
    p = map_path(mid)
    if not p.exists():
        return None
    info = load(p).get('info')
    return {'town': val(info.get('town'), 0), 'link': val(info.get('link'))}


def lives_of(mid):
    p = map_path(mid)
    if not p.exists():
        return None
    im = load(p)
    node = im.root.get('life')
    out = []
    if node is None:
        return out
    for life in node.children():
        if val(life.get('type'), '') != 'm':
            continue
        out.append({
            'id': val(life.get('id'), 0),
            'x': val(life.get('x'), 0),
            'y': val(life.get('y'), 0),
            'fh': val(life.get('fh'), 0),
            'mobTime': val(life.get('mobTime'), 0),
            'rx0': val(life.get('rx0'), 0),
            'rx1': val(life.get('rx1'), 0),
        })
    return out


NUMX = 'mobTime'


def drop_to_foothold(fhs, mapX, mapY):
    """复刻 FieldDirector.DropToFoothold（≡ MapleMap.calcPointBelow）。"""
    best = mapY
    found = False
    for x1, y1, x2, y2 in fhs:
        minX, maxX = min(x1, x2), max(x1, x2)
        if mapX < minX or mapX > maxX:
            continue
        t = 0.0 if x2 == x1 else (mapX - x1) / (x2 - x1)
        yAtX = y1 + (y2 - y1) * t
        if yAtX >= mapY and (not found or yAtX < best):
            best = yAtX
            found = True
    return best


def main():
    audit_path = Path(__file__).parent.parent / 'docs' / 'wild-boss-map-audit.json'
    if not audit_path.exists():
        print('缺失 %s，请先生成全量 Boss 审计数据' % audit_path)
        return 2
    audit = json.load(open(audit_path))
    rows = audit['bossSpawns']
    assets = json.load(open(Path(__file__).parent.parent / 'docs' / 'wild-boss-asset-check.json'))
    bosses = set(m[0] for m in assets['links']) if assets.get('links') else set()

    # (map, mob) 去重
    combos = {}
    for r in rows:
        combos.setdefault((r['map'], r['mob']), r)
    combos = list(combos.values())
    print('野外/全员 Boss spawn 组合：%d' % len(combos))

    ok = outside = nodata = 0
    bad_rows = []
    for r in combos:
        mid, mob = r['map'], r['mob']
        inf = info_of(mid)
        if inf is None:
            nodata += 1
            continue
        vr = vr_of(mid)
        if vr is None:
            nodata += 1
            continue
        lives = lives_of(mid)
        hit = [lv for lv in lives if lv['id'] == mob]
        if not hit:
            nodata += 1
            continue
        fhs = footholds_of(mid)
        lv = hit[0]
        y = drop_to_foothold(fhs, lv['x'], lv['y']) - 1.0
        left, top, right, bottom = vr
        inside = left <= lv['x'] <= right and top <= y <= bottom
        if inside:
            ok += 1
        else:
            outside += 1
            bad_rows.append((mid, mob, lv['x'], lv['y'], y, left, top, right, bottom,
                             'town=%s' % inf['town'], 'link=%s' % inf['link']))

    print()
    print('出生点在 VR 内 : %d' % ok)
    print('出生点在 VR 外 : %d  <- 玩家永远找不到' % outside)
    print('无足够数据     : %d' % nodata)
    if outside:
        print()
        print('VR 外的样本（前 25）：')
        print('%-11s %-9s %-8s %-8s %-8s %s' % ('map', 'mob', 'life.x', 'life.y', '落点y', 'VR'))
        for b in sorted(bad_rows)[:25]:
            print('%-11d %-9d %-8s %-8s %-8s x[%s,%s] y[%s,%s]' % b[:9])
        json.dump([{'map': b[0], 'mob': b[1], 'lifeX': b[2], 'lifeY': b[3], 'dropY': b[4],
                    'vr': {'left': b[5], 'top': b[6], 'right': b[7], 'bottom': b[8]}}
                   for b in bad_rows],
                  open(str(audit_path.parent / 'wild-boss-outside-vr.json'), 'w'),
                  ensure_ascii=False, indent=1)
        print()
        print('已写出 %s' % (audit_path.parent / 'wild-boss-outside-vr.json'))
    return 0


if __name__ == '__main__':
    sys.exit(main())
