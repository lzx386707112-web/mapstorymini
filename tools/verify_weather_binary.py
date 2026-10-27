#!/usr/bin/env python3
"""Verify migrated source tables against the deployed DLL, SHA gated."""
import hashlib,json,re,struct
from pathlib import Path
import pefile
from export_weather import SRC,ROOT,numbers
p=ROOT.parent.parent/'Beidou-Server/clien/BeiDouWeatherCompat.dll'
assert hashlib.sha256(p.read_bytes()).hexdigest()=='31e8e486c2f339ff1d90d4a4bc6509ab92c779e11f84a9d9d235941399aeffcc'
pe=pefile.PE(str(p));base=pe.OPTIONAL_HEADER.ImageBase;mem=pe.get_memory_mapped_image()
data=json.loads((ROOT/'Assets/Resources/NativeWeather.json').read_text())
def wide(pointer):
    start=pointer-base;end=start
    while mem[end:end+2]!=b'\0\0':end+=2
    return mem[start:end].decode('utf-16le')
def profile(row):
    v=row['values'];return struct.pack('<ffiBBBxffB3x',*v)
start=mem.find(profile(data['profiles'][0]))-4
assert start>=0
for i,row in enumerate(data['profiles']):
    at=start+i*32
    assert mem[at+4:at+32]==profile(row),row
    assert wide(struct.unpack_from('<I',mem,at)[0])==row['name']
print('profiles',hex(base+start),'9 exact rows')
# Name pointer + six RGB bytes + alignment = 12 bytes per Palette.
row=data['palettes'][0];start=mem.find(bytes(row['colors'])+b'\0\0')-4
assert start>=0
for i,row in enumerate(data['palettes']):
    at=start+i*12
    assert mem[at+4:at+10]==bytes(row['colors'])
    assert wide(struct.unpack_from('<I',mem,at)[0])==row['name']
print('palettes',hex(base+start),'27 exact rows')
s=(SRC/'weatherfx.cpp').read_text()
for name in ['g_back0','g_back1a','g_aDust','g_aNight','g_rainbow','g_aClouds','g_aRain','g_aFog','g_aSnow']:
    m=re.search(r'const BACKENTRY '+name+r'\[\] = \{(.*?)\n\};',s,re.S)
    rows=re.findall(r'\{([^}]+)\}',m[1]) if m else [re.search(r'const BACKENTRY '+name+r'\s*=\s*\{([^}]+)\}',s)[1]]
    values=[n for row in rows for n in numbers(row)]
    at=mem.find(struct.pack('<'+'i'*len(values),*values));assert at>=0,name
    print(name,hex(base+at),len(rows),'exact entries')
print('WEATHER_BINARY_TABLES_PASS')
