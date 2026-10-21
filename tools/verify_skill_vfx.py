#!/usr/bin/env python3
"""Compare every exported padded frame with its decoded native canvas."""
import json
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw
from export_skill_vfx import ROOT, OUT, resolve, scalar, wzmap, decode_canvas

def verify():
    data=json.loads((OUT/'GameplaySkillVfx.json').read_text())
    count=0; previews=[]
    for skill in data['skills']:
        for part,clip in skill['clips'].items():
            for frame in clip['frames']:
                rel,path=frame['source'].split('.img/',1)
                source=resolve(wzmap._wz(rel).get(path))
                image=decode_canvas(source,region='GMS').convert('RGBA')
                values=scalar(source)
                assert list(image.size)==frame['size']
                assert values.get('origin',[0,0])==frame['sourceOrigin']
                assert abs(values.get('delay',100))/1000==frame['delay']
                actual=Image.open(OUT/'Art'/frame['file'].replace('.png','.bytes')).convert('RGBA')
                expected=Image.new('RGBA',actual.size)
                expected.alpha_composite(image,(frame['origin'][0]-frame['sourceOrigin'][0],frame['origin'][1]-frame['sourceOrigin'][1]))
                assert ImageChops.difference(expected,actual).getbbox() is None,frame['source']
                count+=1
            # Peak-area frame of every branch, on common dark background.
            f=max(clip['frames'],key=lambda x:x['size'][0]*x['size'][1])
            im=Image.open(OUT/'Art'/f['file'].replace('.png','.bytes')).convert('RGBA')
            im=im.crop(im.getbbox());im.thumbnail((220,155))
            previews.append((skill['key']+'/'+part,im))
    sheet=Image.new('RGB',(1000,200*((len(previews)+3)//4)),(25,29,39));draw=ImageDraw.Draw(sheet)
    for i,(label,im) in enumerate(previews):
        x=(i%4)*250;y=(i//4)*200
        draw.text((x+8,y+8),label,fill='white');sheet.paste(im,(x+15,y+35),im)
    dest=ROOT/'docs/skill-vfx-contact-sheet.jpg';sheet.save(dest,quality=90)
    print(f'PASS {len(data["skills"])} IDs / {len(previews)} clips / {count} native pixel, origin and delay comparisons')
if __name__=='__main__':verify()
