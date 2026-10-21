#!/usr/bin/env python3
"""Native starter-town weapon shops, item/mob stats and Bishop summon frames.
Read-only source snapshot; unsupported inventory categories are not sold locally.
"""
import hashlib
import json
import re
from pathlib import Path
import wzmap
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets/Resources'
SERVER = Path(wzmap.DATA).parents[1] / 'gms-server'
SQL = SERVER / 'src/main/resources/db/migration/V1.0.55__shopitems_insert_data.sql'
SHOPS = {11000: '新手村', 1001000: '明珠港', 1011000: '射手村', 1021000: '勇士部落', 1031000: '魔法密林'}
TYPES = {130: ('sword',4),131: ('axe',4.4),132: ('mace',4.4),133: ('dagger',4),137: ('wand',3.6),138: ('staff',3.6),140: ('greatsword',4.6),141: ('greataxe',4.8),142: ('greatmace',4.8),143: ('spear',5),144: ('polearm',5),145: ('bow',3.4),146: ('crossbow',3.6),147: ('claw',3.6),148: ('knuckle',4.8),149: ('gun',3.6)}

def scalar(node):
    return {c.name:c.value for c in node.children() if isinstance(getattr(c,'value',None),(int,float,str))}

def export():
    rows = [dict(zip(('row','shop','item','price','pitch','position'),map(int,r))) for r in
            re.findall(r'\((\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+),\s*(\d+)\)', SQL.read_text())]
    selected = [r for r in rows if r['shop'] in SHOPS and r['item']//10000 in TYPES]
    items=[]
    strings=wzmap._wz('String/Eqp')
    for iid in sorted({r['item'] for r in selected}|{1332000}):
        info=scalar(wzmap._wz(f'Character/Weapon/{iid:08}').get('info'))
        name=strings.get(f'Eqp/Weapon/{iid}/name')
        kind,mult=TYPES[iid//10000]
        items.append({'id':iid,'name':getattr(name,'value',str(iid)),'kind':kind,'multiplier':mult,'stats':info})
    mobs=[]
    for key,iid in {'snail':100100,'slime':210100,'mushroom':1210102,'greenmush':1110100,'pig':1210100,'mushmom':6130101,'slimeking':9300003}.items():
        mobs.append({'key':key,'id':iid,'stats':scalar(wzmap._wz(f'Mob/{iid:07}').get('info'))})
    summon={}
    rel='Skill/232'; base='skill/2321020/summon'
    decoded=[]
    for action in ('summoned','stand','fly','attack1','die'):
        for frame in wzmap._wz(rel).get(f'{base}/{action}').children():
            if not frame.name.isdigit(): continue
            im=wzmap._canvas(rel,f'{base}/{action}/{frame.name}')
            decoded.append((action,frame,im,frame.get('origin').value))
    # Original origins can lie outside the canvas. A common padded sheet preserves
    # those anchors without clamping a Unity sprite pivot or shifting the summon.
    left=min(0,*(-o[0] for _,_,_,o in decoded)); top=min(0,*(-o[1] for _,_,_,o in decoded))
    right=max(0,*(im.width-o[0] for _,_,im,o in decoded)); bottom=max(0,*(im.height-o[1] for _,_,im,o in decoded))
    for action,frame,im,origin in decoded:
        file=f'NativeSummon/libra/{action}_{frame.name}.png'
        dest=OUT/'Art'/file.replace('.png','.bytes'); dest.parent.mkdir(parents=True,exist_ok=True)
        canvas=Image.new('RGBA',(right-left,bottom-top))
        canvas.alpha_composite(im,(-origin[0]-left,-origin[1]-top))
        canvas.save(dest,format='PNG')
        summon.setdefault(action,[]).append({'file':file,'origin':[-left,-top],'sourceOrigin':list(origin),
                                            'delay':abs(frame.get('delay').value)/1000})
    info=wzmap._wz(rel).get(base+'/attack1/info')
    range_node=info.get('range')
    summon['info']={'after':info.get('attackAfter').value/1000,'count':info.get('mobCount').value,
                    'lt':list(range_node.get('lt').value),'rb':list(range_node.get('rb').value)}
    data={'source':str(SQL.relative_to(SERVER)),'sha256':hashlib.sha256(SQL.read_bytes()).hexdigest(),
          'shops':[{'id':sid,'name':name,'rows':sorted([r for r in selected if r['shop']==sid],key=lambda r:-r['position'])} for sid,name in SHOPS.items()],
          'items':items,'mobs':mobs,'summon':summon}
    (OUT/'GameplayCombat.json').write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n')
    print(f'Exported {len(items)} items, {len(selected)} shop rows, {len(mobs)} mobs, {sum(len(v) for k,v in summon.items() if k!="info")} summon frames')

if __name__=='__main__': export()
