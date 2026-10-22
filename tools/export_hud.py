#!/usr/bin/env python3
"""Re-export native HUD/menu/key configuration additions, without changing client data.
Uses the same GMS WZ reader and data root as wzmap.py. Requires Pillow.
"""
from pathlib import Path
import wzmap

OUT = Path(__file__).resolve().parents[1] / "Assets/Resources/Art/ui"


def save(source, node, target):
    image = wzmap._canvas(source, node)
    if image is None:
        raise ValueError(f"Missing WZ canvas: {source}/{node}")
    target = OUT / target
    target.parent.mkdir(parents=True, exist_ok=True)
    image.save(target, format="PNG")


def identity_plate():
    """Reference-matched identity frame; preserve the client's LV. lettering.

    The supplied reference uses a silver frame absent from this WZ version.
    Reconstruct only this frame, not the text, digits or gauge artwork.
    """
    from PIL import Image, ImageDraw
    scale = 4
    plate = Image.new("RGBA", (210 * scale, 35 * scale))
    def rounded(bounds, radius, color):
        ImageDraw.Draw(plate).rounded_rectangle(tuple(int(v * scale) for v in bounds),
                                                radius=radius * scale, fill=color)
    rounded((6, 1, 209, 34), 4, (100, 119, 127, 255))
    rounded((7, 2, 208, 33), 3, (197, 207, 209, 255))
    rounded((8, 3, 207, 32), 2, (43, 54, 59, 255))
    # Top sheen and the dark, separate level block.
    draw = ImageDraw.Draw(plate)
    for y in range(4 * scale, 11 * scale):
        t = (y / scale - 4) / 7
        shade = tuple(round(a + (b - a) * t) for a, b in zip((94, 105, 110), (43, 54, 59)))
        draw.line((10 * scale, y, 205 * scale, y), fill=shade + (255,))
    rounded((9, 4, 81, 32), 3, (28, 35, 38, 255))
    native = Image.open(OUT / "statusbar/bar_lv.bytes").convert("RGBA")
    lettering = native.crop((8, 9, 32, 26))
    # Remove the old black capsule background, retaining antialiasing.
    lettering.putdata([(255, 255, 255, max(0, min(255, round((min(r,g,b)-65)*255/190))))
                       for r,g,b,a in lettering.getdata()])
    lettering = lettering.resize((24 * scale, 17 * scale), Image.Resampling.NEAREST)
    plate.alpha_composite(lettering, (15 * scale, 9 * scale))
    plate.resize((210, 35), Image.Resampling.LANCZOS).save(OUT / "statusbar/identity_plate.bytes", format="PNG")


def main():
    identity_plate()
    for name in ("BtClose", "BtHelp", "BtOK", "BtCancel", "BtDefault", "BtDelete", "BtQuickSlot"):
        for state in ("normal", "mouseOver", "pressed", "disabled"):
            save("UI/UIWindow", f"KeyConfig/{name}/{state}/0", f"keyconfig/{name}_{state}.bytes")
    save("UI/UIWindow", "KeyConfig/quickslotConfig/backgrnd", "keyconfig/quickslot_backgrnd.bytes")
    for name in ("backgrnd2", "chatTarget", "box"):
        save("UI/StatusBar", f"base/{name}", f"bar/{name}_full.bytes")
    save("UI/Basic", "YesNo/backgrnd", "keyconfig/confirm_backgrnd.bytes")
    for button, name in (("BtYes", "yes"), ("BtNo", "no")):
        for state in ("normal", "mouseOver", "pressed"):
            save("UI/Basic", f"{button}/{state}/0", f"keyconfig/confirm_{name}_{state}.bytes")
    save("UI/StatusBar", "base/quickSlot26", "bar/quickSlot26.bytes")
    for root in ("GameMenu", "ShortCut"):
        for child in wzmap._wz("UI/UIWindow").get(root).children():
            if hasattr(child, "width"):
                save("UI/UIWindow", f"{root}/{child.name}", f"{root.lower()}/{child.name}.bytes")
            elif child.name.startswith("Bt"):
                for state in ("normal", "mouseOver", "pressed", "disabled"):
                    save("UI/UIWindow", f"{root}/{child.name}/{state}/0", f"{root.lower()}/{child.name}_{state}.bytes")
    print("Native HUD additions exported to", OUT)


if __name__ == "__main__":
    main()
