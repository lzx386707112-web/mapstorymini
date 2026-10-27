#!/usr/bin/env python3
"""Original weather source tables. Does not generate or restyle artwork."""
import hashlib,json,re
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
SRC=ROOT.parent.parent/'Beidou-Server/tool/client-debug/weather-compat/src'
SERVER=ROOT.parent.parent/'Beidou-Server/gms-server/src/main/java/org/gms/server/weather'
def numbers(s):
    return [int(n,16) if '0x' in n else float(n) if '.' in n else int(n) for n in re.findall(r'-?0x[0-9A-Fa-f]+|-?(?:\d*\.)?\d+',s)]
def export():
    palettes=[]
    for name,body in re.findall(r'\{ L"([^"]+)",([^}]+)\}',(SRC/'weather_palettes.inc').read_text()):
        palettes.append(dict(name=name,colors=numbers(body)))
    profiles=[]
    for name,body in re.findall(r'\{ L"([^"]+)",([^}]+)\}',(SRC/'weather_profiles.inc').read_text()):
        profiles.append(dict(name=name,values=numbers(body)))
    region=(SERVER/'WeatherRegion.java').read_text().split('private final')[0]
    regions=[]
    for name,forced,weights,tint in re.findall(r'(\w+)\((?:WeatherProfile\.(\w+)|weights\(([^)]+)\)), (0x[0-9A-F]+)\)',region):
        regions.append(dict(name=name,forced=next((i for i,p in enumerate(profiles) if p['name'].upper()==forced),-1),weights=numbers(weights) if weights else [1,0,0,0,0,0,0,0,0],tint=int(tint,16)))
    s=(SRC/'weatherfx.cpp').read_text()
    keys=['no','x','y','rx','ry','type','cx','cy','a','ani']
    def entries(name):
        m=re.search(r'const BACKENTRY '+name+r'\[\] = \{(.*?)\n\};',s,re.S)
        if m: rows=re.findall(r'\{([^}]+)\}',m[1])
        else: rows=[re.search(r'const BACKENTRY '+name+r'\s*=\s*\{([^}]+)\}',s)[1]]
        return [dict(zip(keys,numbers(r))) for r in rows]
    layers=[]
    for group,bank,array,dy,front in [('sky','nightDesert','g_back0',-100,False),('sky','MapleMoonSky','g_back1a',-100,False),('dust','nightDesert','g_aDust',-100,False),('night','nightDesert','g_aNight',0,False),('rainbow','WeatherRainbow','g_rainbow',0,False),('cloud','nightDesert','g_aClouds',0,False),('rain','ancientForest','g_aRain',0,True),('fog','WeatherFog','g_aFog',0,True),('snow','WeatherSnow','g_aSnow',0,False)]:
        for i,e in enumerate(entries(array)):
            e['y']+=dy
            e.update(group=group,bank=bank,front=front or group=='snow' and i==1)
            # Native inject fills only these pitches from the Canvas dimensions.
            e['sizePitch']=group in ('sky','cloud','fog')
            layers.append(e)
    sky=[dict(bank=b.lower(),no=int(n)) for b,n in re.findall(r'\{ L"([^"]+)",\s*(\d+) \}',(SRC/'weather_skytable.inc').read_text())]
    excluded=[]
    for line in (SRC/'weather_nosky_maps.inc').read_text().splitlines():
        line=line.split('//')[0]
        excluded.extend(numbers(line))
    config=(ROOT.parent.parent/'Beidou-Server/clien/config.ini').read_text(errors='replace')
    enabled=re.search(r'enableWeatherSystem\s*=\s*(\w+)',config)[1].lower()=='true'
    data=dict(clientEnabled=enabled,palettes=palettes,profiles=profiles,regions=regions,skyBases=sky,excluded=excluded,layers=layers)
    assert len(regions)==27 and len(palettes)==27 and len(profiles)==9 and len(layers)==21
    (ROOT/'Assets/Resources/NativeWeather.json').write_text(json.dumps(data,separators=(',',':'))+'\n')
    files=[SRC/x for x in ['weather.cpp','weatherfx.cpp','weatherwind.cpp','weather_profiles.inc','weather_palettes.inc','weather_skytable.inc','weather_nosky_maps.inc','skillback.cpp']]+list(SERVER.glob('Weather*.java'))
    (ROOT/'docs/weather-source.json').write_text(json.dumps({str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in files},indent=2)+'\n')
    print(f'weather: 9 profiles, 27 regions/palettes, {len(sky)} sky bases, {len(excluded)} exclusions, {len(layers)} sheets; default enabled={enabled}')
if __name__=='__main__':export()
