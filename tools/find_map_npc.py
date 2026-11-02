#!/usr/bin/env python3
"""扫描全部地图 IMG 的 life 节点，找指定 NPC id（默认 9000019 RPS Admin）在哪些地图出生。

用法: python3 tools/find_map_npc.py [npc_id]
"""
import sys
from pathlib import Path

WZPY = Path('/Users/lizixian/Documents/mxd/Beidou-Server/tool/wz-python')
DATA = Path('/Users/lizixian/Documents/mxd/mxd-android/MapleDashResources/Data/Map/Map')

sys.path.insert(0, str(WZPY))
from wzpy import WzImage, WzKey  # noqa: E402


def val(x):
    return getattr(x, 'value', None) if x is not None else None


def main():
    target = str(sys.argv[1]) if len(sys.argv) > 1 else '9000019'
    hits = []
    for shard in sorted(DATA.iterdir()):
        if not shard.is_dir():
            continue
        for p in sorted(shard.glob('*.img')):
            try:
                img = WzImage.from_bytes(p.read_bytes(), key=WzKey.for_region('GMS'), name=p.stem)
                img.parse()
                life = img.get('life')
                if life is None:
                    continue
                for item in life.children():
                    if item is None:
                        continue
                    if str(val(item.get('type'))) != 'n':
                        continue
                    if str(val(item.get('id'))) == target:
                        hits.append('%s/%s' % (shard.name, p.stem))
                        print('HIT', shard.name, p.stem, flush=True)
            except Exception:
                pass
    print('total hits:', len(hits))


if __name__ == '__main__':
    main()
