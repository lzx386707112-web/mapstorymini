#!/usr/bin/env python3
"""Compare C# raw IMG decoding with the existing wzpy decoder (not baked PNGs).

Requires the original client's wz-python and Unity's bundled Mono. Writes only
into a temporary directory. Checks all Canvas nodes in dependencies of 3 maps.
"""
import argparse
import hashlib
import os
from pathlib import Path
import subprocess
import sys
import tempfile

HARNESS = r"""
using System;
using System.Security.Cryptography;
using MapleDash.Wz;
class CompareImg {
    static void Walk(ImgNode n, string path) {
        if (n.Kind == "Canvas") {
            byte[] rgba;
            try { rgba = n.Rgba(); } catch (Exception e) { throw new Exception(path, e); }
            using (var sha = SHA256.Create()) {
                Console.WriteLine(path + "\t" + n.Width + "\t" + n.Height + "\t" + n.Format + "\t" +
                    BitConverter.ToString(sha.ComputeHash(rgba)).Replace("-", "").ToLowerInvariant());
            }
        }
        foreach(var c in n.Children) Walk(c, path + "/" + c.Name);
    }
    static void Main(string[] args) {
        foreach(var path in args) using(var img = new ImgFile(path)) Walk(img.Root, path);
    }
}
"""


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--data", required=True, type=Path)
    p.add_argument("--wzpy", required=True, type=Path, help="directory containing wzpy package")
    p.add_argument("--unity", type=Path, default=Path("/Applications/Unity/Unity.app"))
    p.add_argument("--img", action="append", default=[], help="additional IMG path relative to Data")
    args = p.parse_args()
    sys.path.insert(0, str(args.wzpy))
    from wzpy import WzImage, WzKey
    from wzpy.properties import WzCanvasProperty
    from wzpy.canvas import decode_canvas
    key = WzKey.for_region("GMS")
    data = args.data.resolve()
    images = {}

    def image(relative):
        if relative not in images:
            img = WzImage.from_bytes((data / relative).read_bytes(), key=key, name=relative)
            img.parse()
            if img.truncated or img.parse_warnings:
                raise RuntimeError(f"Oracle IMG parse incomplete: {relative}: {img.parse_warnings}")
            images[relative] = img
        return images[relative]

    def children(node):
        return node.children() if node is not None else []

    def value(node, name, default=""):
        v = node.get(name)
        return getattr(v, "value", default)

    for extra in args.img: image(extra)

    for map_id in (0, 40001, 1000000):
        img = image(f"Map/Map/Map{map_id // 100000000}/{map_id:09}.img")
        for layer in range(9):
            n = img.get(str(layer))
            if n is None:
                continue
            tiles = children(n.get("tile"))
            if tiles:
                image(f"Map/Tile/{value(n.get('info'), 'tS')}.img")
            for obj in children(n.get("obj")):
                image(f"Map/Obj/{value(obj, 'oS')}.img")
        for back in children(img.get("back")):
            if value(back, "bS"):
                image(f"Map/Back/{value(back, 'bS')}.img")
        for life in children(img.get("life")):
            if value(life, "type") == "n" and not value(life, "hide", 0):
                npc = image(f"Npc/{int(value(life, 'id')):07}.img")
                link = value(npc.get("info"), "link")
                if link:
                    image(f"Npc/{int(link):07}.img")
    image("Map/MapHelper.img")
    image("UI/Logo.img")  # Includes RGB565 (513), absent from the three sample maps.
    expected = {}
    formats = {}

    def walk(node, path):
        if isinstance(node, WzCanvasProperty):
            rgba = decode_canvas(node, region="GMS").convert("RGBA").tobytes()
            fmt = node.format + node.format2
            expected[path] = f"{node.width}\t{node.height}\t{fmt}\t{hashlib.sha256(rgba).hexdigest()}"
            formats[fmt] = formats.get(fmt, 0) + 1
        for c in children(node):
            walk(c, path + "/" + c.name)

    for relative, img in images.items():
        walk(img.root, str(data / relative))
    repo = Path(__file__).resolve().parents[1]
    mono = args.unity / "Contents/MonoBleedingEdge/bin"
    with tempfile.TemporaryDirectory(prefix="mapledash-img-") as tmp:
        source = Path(tmp) / "Compare.cs"
        source.write_text(HARNESS)
        exe = Path(tmp) / "Compare.exe"
        subprocess.run([str(mono / "mcs"), f"-out:{exe}", str(repo / "Assets/Scripts/Wz/ImgFile.cs"), str(source)], check=True)
        env = dict(os.environ)
        env["DYLD_LIBRARY_PATH"] = str(args.unity / "Contents/Frameworks/MonoBleedingEdge/MonoEmbedRuntime/osx")
        result = subprocess.run([str(mono / "mono"), str(exe), *(str(data / f) for f in images)], capture_output=True, text=True, env=env)
        if result.returncode:
            raise RuntimeError(result.stderr)
    actual = dict(line.split("\t", 1) for line in result.stdout.splitlines())
    if expected != actual:
        mismatch = [k for k in expected.keys() | actual.keys() if expected.get(k) != actual.get(k)]
        raise AssertionError(f"{len(mismatch)} Canvas mismatches: {mismatch[:10]}")
    print(f"PASS: {len(images)} original IMG files, {len(expected)} Canvas pixel hashes identical; formats={formats}")


if __name__ == "__main__":
    main()
