#!/usr/bin/env python3
"""Extract the two original ambient loops, preserving MP3 payload bytes."""
import hashlib
import json
from pathlib import Path
import export_sound

def main():
    index=json.loads(export_sound.INDEX.read_text())
    manifest={}
    for name in ('rain','wind'):
        key='Weather/'+name
        entry=export_sound.export(key,key,key+'.mp3')
        assert entry is not None, key
        index[key]=entry
        payload=export_sound.RES/(key+'.mp3')
        manifest[key]=dict(entry,sha256=hashlib.sha256(payload.read_bytes()).hexdigest())
    export_sound.INDEX.write_text(json.dumps(index,ensure_ascii=False,indent=1,sort_keys=True)+'\n')
    root=Path(__file__).resolve().parents[1]
    for relative in ('Sound/Weather.img','Effect/WeatherParticles.img','Effect/WeatherAccum.img','Effect/WeatherPuddle.img','Effect/WeatherSplash.img','Item/Cash/0512.img'):
        file=root.parent.parent/'Beidou-Server/clien/Data'/relative
        manifest[relative]=dict(sha256=hashlib.sha256(file.read_bytes()).hexdigest())
    (root/'docs/weather-effects-source.json').write_text(json.dumps(manifest,indent=2)+'\n')

if __name__=='__main__':main()
