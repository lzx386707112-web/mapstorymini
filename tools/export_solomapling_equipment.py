#!/usr/bin/env python3
"""Preserve original equipment cache ranges and fashion-script pool data."""
import hashlib
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT.parent.parent / "BeiDou-Server/gms-server"


def main():
    base = SOURCE / "src/main/java/soloMapling"
    cache = base / "itemPool/EquipMetadataCache.java"
    nx = base / "ArtificialPlayer/BotDecoratorSystem/NXItemPool.java"
    fashion = SOURCE / "scripts-zh-CN/BeiDouSpecial/时尚点装.js"
    cache_text, nx_text = cache.read_text(), nx.read_text()
    ranges = [[name, int(lo), int(hi)] for name, lo, hi in re.findall(
        r'EQUIP_RANGES.put\(EquipType\.(\w+),\s*new int\[\]\{(\d+), (\d+)\}', cache_text)]
    def ring_ranges(text, name):
        block = re.search(name + r'\s*=\s*\{(.*?)\n    \};', text, re.S)[1]
        return [[int(lo), int(hi)] for lo, hi in re.findall(r'\{(\d+), (\d+)\}', block)]
    fallback_block = nx_text.split('FALLBACK_ITEMS = Map.ofEntries(', 1)[1].split('\n    );', 1)[0]
    fallback = {name: [int(n) for n in re.findall(r'\d+', block)] for name, block in re.findall(
        r'Map.entry\("(\w+)", new int\[\]\{(.*?)\}', fallback_block, re.S)}
    script = '\n'.join(line.split('//', 1)[0] for line in fashion.read_text().splitlines())
    categories = dict(wq='weapons', yf='tops', kz='bottoms', tz='overalls', st='gloves', xz='shoes', sp='rings', mz='caps', dm='caps')
    pools = {}
    for name, block in re.findall(r'var\s+(\w+)\s*=\s*Array\((.*?)\);', script, re.S):
        if name in categories:
            pools.setdefault(categories[name], []).extend(int(n) for n in re.findall(r'Array\s*\(\s*(\d{7})\s*,', block))
    if len(ranges) != 28 or len(fallback) != 7 or not pools:
        raise ValueError('Original equipment source structure changed')
    result = dict(ranges=ranges, extraRingRanges=ring_ranges(cache_text, 'EXTRA_RING_RANGES'),
                  showcaseRingRanges=ring_ranges(nx_text, 'SHOWCASE_RING_RANGES'), fallback=fallback,
                  fashion=pools, sources={str(p.relative_to(SOURCE)): hashlib.sha256(p.read_bytes()).hexdigest()
                                         for p in (cache, nx, fashion)})
    target = ROOT / 'Assets/Resources/Bot/EquipmentPools.json'
    target.write_text(json.dumps(result, ensure_ascii=False, separators=(',', ':')) + '\n')
    print(f'Exported {len(ranges)} ranges and {sum(map(len, pools.values()))} original fashion entries')


if __name__ == '__main__':
    main()
