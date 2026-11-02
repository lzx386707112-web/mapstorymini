#!/usr/bin/env python3
"""Enumerate every original map life boss; report original respawn and HP-tag data."""
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
    n = node.get(path) if node is not None else None
    return getattr(n, 'value', default) if n is not None else default

def children(node): return node.children() if node is not None else []
bosses = {}
for path in sorted((DEFAULT_DATA/'Mob').glob('*.img')):
    image=load(path); info=image.get('info')
    if value(info,'boss'):
        bosses[int(path.stem)] = {'hp':value(info,'maxHP'), 'hpTagColor':value(info,'hpTagColor'), 'hpTagBgcolor':value(info,'hpTagBgcolor')}
rows=[]; errors=[]; maps=0; linked=[]
for path in sorted((DEFAULT_DATA/'Map/Map').glob('Map*/*.img')):
    maps+=1
    try:
        image=load(path); link=value(image.root,'info/link',None)
        if link is not None:
            linked.append({'map':int(path.stem),'link':int(link)})
            continue
        for life in children(image.get('life')):
            if value(life,'type','')!='m': continue
            mob=int(value(life,'id'))
            if mob in bosses:
                rows.append({'map':int(path.stem),'mob':mob,'mobTime':value(life,'mobTime'), 'x':value(life,'x'),'y':value(life,'y'), **bosses[mob]})
    except Exception as e: errors.append({'file':str(path.relative_to(DEFAULT_DATA)), 'error':str(e)})
by_map={}
for row in rows: by_map.setdefault(row['map'],[]).append(row)
links={row['map']:row['link'] for row in linked}
for map_id in links:
    target=links[map_id]; seen={map_id}
    while target in links and target not in seen:
        seen.add(target); target=links[target]
    for row in by_map.get(target,[]): rows.append({**row,'map':map_id,'linkedLifeFrom':target})
out=Path('docs/wild-boss-map-audit.json')
out.write_text(json.dumps({'mapsScanned':maps,'bossTemplates':len(bosses),'bossSpawns':rows,'linkedMaps':linked,'errors':errors},ensure_ascii=False,indent=2)+'\n')
print(json.dumps({'maps':maps,'bossTemplates':len(bosses),'bossSpawns':len(rows),'linkedMaps':len(linked),'errors':len(errors),'report':str(out)}))
