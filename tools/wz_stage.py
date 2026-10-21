#!/usr/bin/env python3
"""Export original WZ map geometry and independent native background entries.

python3 tools/wz_stage.py --unity
Requires Pillow and the local client's wz-python parser (see wzmap.py).
Intermediate PNGs stay in Library/WzStage. No client files are modified.
"""
import argparse
import json
from pathlib import Path
import wzmap

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "Library/WzStage"
UNITY = ROOT / "Assets/Resources/Art"
# Anchors preserve the existing chapter's spawn coordinates, not camera limits.
STAGES = {
    "harbor": ("104010001", -630, 215),
    "forest": ("100010000", 675, 455),
    "cliff": ("101030100", 945, 2205),
    "nest": ("106020300", 585, 35),
}


def export(theme, sync):
    map_id, gx, gy = STAGES[theme]
    gm = wzmap.GameMap(map_id, wzmap.find_map_sub(map_id))
    info = gm.img.get("info")
    xs = [f[k] for f in gm.footholds for k in ("x1", "x2")]
    ys = [f[k] for f in gm.footholds for k in ("y1", "y2")]
    # BeiDou.exe CMapLoadable::RestoreViewRange (0x00641EF1):
    # explicit VR values, else field-space bounds with -20/-60/+20/+100.
    # Camera insets by half the logical viewport at runtime.
    defaults = [min(xs)-20, min(ys)-60, max(xs)+20, max(ys)+100]
    vr = [wzmap._num(info, key, default) for key, default in zip(
        ("VRLeft", "VRTop", "VRRight", "VRBottom"), defaults)]
    x0, y0, x2, y2 = vr
    # Keep enough raster data for the native midpoint rule on undersized maps.
    w, h = max(x2-x0, 1366), max(y2-y0, 768)
    x0 -= (w-(x2-x0))//2
    y0 -= (h-(y2-y0))//2
    if w > 4096 or h > 4096:
        raise ValueError(f"{theme}: map needs texture chunking: {w}x{h}")

    def save(im, rel):
        path = ART / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        im.save(path)
        if sync:
            dst = UNITY / Path(rel).with_suffix(".bytes")
            dst.parent.mkdir(parents=True, exist_ok=True)
            dst.write_bytes(path.read_bytes())
        return rel

    body = gm.render(x0, y0, w, h, with_backs=False)
    files = {"mid": save(body, f"map/stage/{theme}_mid.png")}
    backgrounds = []
    for entry in sorted(gm.backs, key=lambda b: int(b["no"])):
        im = gm._back_image(entry)
        if im is None:
            raise ValueError(f"Missing background: {theme} {entry}")
        backgrounds.append(dict(entry,
            file=save(im, f"map/stage/{theme}_bg{entry['no']}.png"),
            origin=gm._back_origin(entry), size=im.size))
    return dict(map=map_id, px=[x0,y0,w,h], ground=[gx,gy],
                view=vr, walk=[min(xs),max(xs)], sky=0,
                footholds=[[f[k] for k in ("x1","y1","x2","y2")]
                           for f in gm.footholds],
                files=files, orders=dict(back=-800,mid=-400,front=600),
                backgrounds=backgrounds)


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("theme", nargs="?", default="all", choices=["all",*STAGES])
    ap.add_argument("--unity", action="store_true")
    args = ap.parse_args()
    manifest = json.loads((UNITY / "manifest.json").read_text())
    for theme in STAGES if args.theme == "all" else [args.theme]:
        stage = export(theme, args.unity)
        manifest["stages"][theme] = stage
        print(theme, "view=", stage["view"], "raster=", stage["px"],
              "backgrounds=", len(stage["backgrounds"]))
    ART.mkdir(parents=True, exist_ok=True)
    (ART / "manifest.json").write_text(json.dumps(manifest,ensure_ascii=False,indent=1))
    if args.unity:
        (UNITY / "manifest.json").write_text(json.dumps(manifest,ensure_ascii=False,indent=1))


if __name__ == "__main__":
    main()
