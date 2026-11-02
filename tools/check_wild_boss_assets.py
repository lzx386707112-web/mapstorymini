#!/usr/bin/env python3
"""Check every wild boss template: does client-side art exist, is Mob/<id>.img a link,
and does it carry the stats/map fields the runtime needs to actually draw it."""
import json, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from imgdump import WzImage, WzKey, DEFAULT_DATA

key = WzKey.for_region('GMS')
def load(path):
    image = WzImage.from_bytes(path.read_bytes(), key=key, name=path.name)
    image.parse()
    return image

def value(node, path, default=0):
    for part in path.split('/'):
        node = node.get(part) if node is not None else None
    return getattr(node, 'value', default) if node is not None else default

audit = json.load(open(Path(__file__).resolve().parent.parent / 'docs/wild-boss-map-audit.json'))
rows = audit['bossSpawns']

# only "wild" maps: drop obvious instances/events by field markers later; keep all for now
mobids = sorted({r['mob'] for r in rows})
print('boss templates in wild maps:', len(mobids))

missing, links, noart, nostathp, ok = [], [], [], [], []
for mob in mobids:
    p = DEFAULT_DATA / 'Mob' / (str(mob).zfill(7) + '.img')
    if not p.exists():
        missing.append(mob); continue
    try:
        image = load(p)
    except Exception as e:
        missing.append((mob, str(e))); continue
    info = image.get('info')
    link = value(info, 'link', None)
    if link is not None:
        links.append((mob, link)); continue
    names = set()
    for n in (image.root.children() if image.root is not None else []):
        names.add(n.name)
    # art-bearing animation nodes the client needs to render anything
    artnodes = [n for n in ('stand', 'move', 'fly', 'attack1', 'die1', 'dead') if n in names]
    hp = value(info, 'maxHP', None)
    if not artnodes:
        noart.append((mob, sorted(names)[:8]))
    elif hp is None:
        nostathp.append((mob, sorted(names)[:8]))
    else:
        ok.append(mob)

print('missing img file :', len(missing), missing[:10])
print('info/link (alias):', len(links), links[:10])
print('no render node   :', len(noart), noart[:10])
print('no maxHP in info :', len(nostathp), nostathp[:10])
print('healthy          :', len(ok))
json.dump({'missing': missing, 'links': links, 'noart': noart, 'nostathp': nostathp},
          open(Path(__file__).resolve().parent.parent / 'docs/wild-boss-asset-check.json', 'w'),
          ensure_ascii=False, indent=2)
