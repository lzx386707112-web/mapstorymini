"""Import the EXE-verified 282 Equip/Symbol subtree, preserving other UIEquip nodes."""
import argparse
from import_growth_ui import DEFAULT_SRC, DEFAULT_DEST, load_source, register
from import_growth_items import rgba_snapshot, verify
from img_bake import PixelSource, bake_tree, serialize
from wzpy import WzImage, WzKey


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--write', action='store_true')
    args = ap.parse_args()
    rel = 'UI/UIEquip.img'
    donor = load_source(DEFAULT_SRC / rel)
    symbol = donor.root.get('Equip/Symbol')
    assert symbol is not None
    target = DEFAULT_DEST / rel
    if target.exists():
        image = WzImage.from_bytes(target.read_bytes(), key=WzKey.for_region('GMS'))
        image.parse()
    else:
        image = donor
        image.root._children = {'Equip': donor.root.get('Equip')}
        image.root.get('Equip')._children = {'Symbol': symbol}
    image.root.get('Equip')._children['Symbol'] = symbol
    stats = {'baked': 0, 'pixels': 0}
    bake_tree(symbol, PixelSource(DEFAULT_DEST, WzKey.for_region('GMS'), fallback_root=DEFAULT_SRC), stats)
    baseline = rgba_snapshot(image)
    data = serialize(image)
    assert not verify(image, data, rel, baseline)
    print(stats, len(data))
    if args.write:
        target.write_bytes(data)
        register([rel], DEFAULT_DEST)


if __name__ == '__main__':
    main()
