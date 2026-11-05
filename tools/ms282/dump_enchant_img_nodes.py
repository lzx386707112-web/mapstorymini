"""Audit en enchant IMG: list node tree with canvas byte sizes, grouped by subtree."""
import os, sys, collections

HERE = os.path.dirname(os.path.abspath(__file__))
WZPY = os.path.expanduser("~/Documents/mxd/Beidou-Server/tool/wz-python")
sys.path.insert(0, WZPY)
from wzpy import WzImage, WzKey, detect_region_from_img  # noqa
from wzpy.properties import WzCanvasProperty, WzSubProperty, WzSoundProperty  # noqa

path = sys.argv[1]
with open(path, "rb") as f:
    data = f.read()
region = detect_region_from_img(data)
print("region:", region, "filesize:", len(data))
img = WzImage.from_bytes(data, key=WzKey.for_region(region), name=os.path.basename(path))
img.parse()
print("truncated:", img.truncated)

rows = []      # (path, bytes)
subtotal = collections.Counter()   # depth-2 group -> bytes


def walk(node, prefix, depth):
    for ch in node.children():
        p = prefix + "/" + ch.name if prefix else ch.name
        if isinstance(ch, WzCanvasProperty):
            rows.append((p, ch._png_length, ch.width, ch.height, ch.format))
        elif isinstance(ch, WzSoundProperty):
            rows.append((p, ch._data_length, 0, 0, -1))
        walk(ch, p, depth + 1)
        if isinstance(ch, WzSubProperty):
            pass


walk(img, "", 0)
total = sum(r[1] for r in rows)
print("canvas count:", len(rows), "payload total:", total, f"({total/1048576:.1f} MB)")

# group by top-level and 2nd level
g1 = collections.Counter()
g2 = collections.Counter()
for p, ln, *_ in rows:
    parts = p.split("/")
    g1[parts[0]] += ln
    if len(parts) > 1:
        g2["/".join(parts[:2])] += ln
print("\n== top level ==")
for k, v in g1.most_common():
    print(f"{v/1048576:8.2f} MB  {v*100.0/total:5.1f}%  {k}")
print("\n== 2nd level ==")
for k, v in g2.most_common(40):
    print(f"{v/1048576:8.2f} MB  {v*100.0/total:5.1f}%  {k}")

out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(HERE, "enchant-img-nodes.txt")
with open(out, "w", encoding="utf-8") as f:
    for p, ln, w, h, fmt in sorted(rows):
        f.write(f"{ln:>9} {w:>5}x{h:<5} fmt{fmt:<3} {p}\n")
print("\nwrote", out)
