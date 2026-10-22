#!/usr/bin/env python3
"""Export every visual branch for current skill slots, including UOL frames.
Preserve source paths, origins, delays, alpha and branch metadata. Source is read-only.
"""
import hashlib
import json
from pathlib import Path
from PIL import Image
import wzmap
from wzpy.properties import WzCanvasProperty, WzSubProperty, WzUolProperty
from wzpy.wz_package import _follow_uol
from wzpy.canvas import decode_canvas

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'Assets/Resources'
PARTS=('effect','effect0','prepare','affected','hit','ball','special','tile','mob','screen','screenEffect','effectScreen','background')

def resolve(n):
    return _follow_uol(n) if isinstance(n,WzUolProperty) else n

def scalar(n):
    result={}
    for c in n.children():
        if isinstance(c,WzUolProperty):continue
        v=getattr(c,'value',None)
        if isinstance(v,(str,int,float)):result[c.name]=v
        elif isinstance(v,tuple) and len(v)==2:result[c.name]=list(v)
    return result

def export():
    manifest=json.loads((OUT/'NativePresentation.json').read_text())
    slots={k:int(v['id']) for k,v in manifest['skills'].items()}
    slots.update(illusion_slash=1121021,illusion_explosion=1121022)
    result=[]; audit=[]
    for key,sid in slots.items():
        rel=f'Skill/{sid//10000:03}'; source=wzmap._wz(rel).get(f'skill/{sid}')
        clips={}; metadata={}
        def walk(original,path,source_img=None,source_path=None):
            source_img=source_img or rel
            source_path=source_path or f"skill/{sid}/{path}"
            node=resolve(original)
            if node is None:raise ValueError(f'Unresolved UOL: {key}/{path}')
            children=list(node.children()); meta=scalar(node)
            metadata[path]=meta
            frames=[]
            for child in children:
                c=resolve(child)
                if child.name.isdigit() and isinstance(c,WzCanvasProperty):
                    image=decode_canvas(c,region='GMS').convert('RGBA')
                    values=scalar(c); origin=values.get('origin',[0,0])
                    frames.append((int(child.name),image,values,origin))
            if frames:
                frames.sort(key=lambda f:f[0])
                left=min(0,*(-o[0] for _,_,_,o in frames));top=min(0,*(-o[1] for _,_,_,o in frames))
                right=max(0,*(im.width-o[0] for _,im,_,o in frames));bottom=max(0,*(im.height-o[1] for _,im,_,o in frames))
                exported=[]
                for index,image,values,origin in frames:
                    canvas=Image.new('RGBA',(max(1,right-left),max(1,bottom-top)))
                    canvas.alpha_composite(image,(-origin[0]-left,-origin[1]-top))
                    file=f'NativeSkillVfx/{key}/{path}/{index}.png'
                    target=OUT/'Art'/file.replace('.png','.bytes');target.parent.mkdir(parents=True,exist_ok=True)
                    canvas.save(target,format='PNG')
                    exported.append({'file':file,'source':f'{source_img}.img/{source_path}/{index}',
                                     'origin':[-left,-top],'sourceOrigin':origin,'size':list(image.size),
                                     'delay':abs(values.get('delay',100))/1000,
                                     'hasDelay':'delay' in values,'a0':values.get('a0',255),'a1':values.get('a1',255),
                                     'z':values.get('z',meta.get('z',0))})
                clips[path]={'frames':exported,'meta':meta}
            for child in children:
                c=resolve(child)
                if isinstance(c,WzSubProperty) and not isinstance(c,WzCanvasProperty):walk(child,path+'/'+child.name,source_img,source_path+'/'+child.name)
        for name in PARTS:
            n=source.get(name)
            if n is not None:walk(n,name)
        # CloseRangeDamageHandler explicitly broadcasts this Map/Effect field layer.
        if key=='rupture':
            walk(wzmap._wz('Map/Effect').get('customSkill/deathFault/full'),'screen',
                 'Map/Effect','customSkill/deathFault/full')
        # Summon actor art is separately exported; include its attack hit visuals here.
        data={'key':key,'id':sid,'clips':clips,'metadata':metadata,
              'sourceSha256':hashlib.sha256((Path(wzmap.DATA)/(rel+'.img')).read_bytes()).hexdigest()}
        if key=='rupture':
            data['fieldEffectSource']='CloseRangeDamageHandler.DEATH_FAULT_FIELD_EFFECT'
            data['fieldEffectSha256']=hashlib.sha256((Path(wzmap.DATA)/'Map/Effect.img').read_bytes()).hexdigest()
        result.append(data)
        audit.append(f"| {key} | {sid} | "+'、'.join(f'`{p}` ({len(c["frames"])})' for p,c in clips.items())+' |')
        print(key,len(clips),sum(len(c['frames']) for c in clips.values()))
    (OUT/'GameplaySkillVfx.json').write_text(json.dumps({'skills':result},ensure_ascii=False,indent=2)+'\n')
    doc=ROOT/'docs/skill-vfx-audit.md'
    notes='\n## 播放接入'+doc.read_text().split('\n## 播放接入',1)[1] if doc.exists() and '\n## 播放接入' in doc.read_text() else ''
    doc.write_text('# 技能特效逐项资源核对\n\n由 `tools/export_skill_vfx.py` 从原客户端 IMG 生成；括号为帧数，包含 UOL 引用帧和透明占位帧。\n\n| 槽位 | 原技能 ID | 完整视觉分支 |\n|---|---|---|\n'+'\n'.join(audit)+'\n'+notes)

if __name__=='__main__':export()
