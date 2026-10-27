#!/usr/bin/env python3
"""Compare C# port against unmodified extracted source C++ equations."""
import json,subprocess,tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT.parent.parent/'Beidou-Server/tool/client-debug/weather-compat/src/weatherfx.cpp'
s=SOURCE.read_text();body=s[s.index('enum LightningKind'):s.index('void WeatherFx::TickLightning')]
palettes=json.loads((ROOT/'Assets/Resources/NativeWeather.json').read_text())['palettes']
head='#include <cstdio>\n#include <cstdint>\nnamespace Weather { int palette; int PaletteId(){return palette;} enum P {'+','.join('PALETTE_'+p['name'].upper()+'='+str(i) for i,p in enumerate(palettes))+'}; }\n'
main=r"""
int main(int argc,char**argv){FILE*f=fopen(argv[1],"wb");uint32_t random=0x12345678;
for(int p=0;p<27;p++){Weather::palette=p;for(uint32_t a=0;a<1000000;a+=37){random^=random<<13;random^=random>>17;random^=random<<5;float level=LightningPulse(a,random);fwrite(&p,4,1,f);fwrite(&a,4,1,f);fwrite(&random,4,1,f);fwrite(&level,4,1,f);}}fclose(f);}
"""
with tempfile.TemporaryDirectory() as d:
    p=Path(d);(p/'oracle.cpp').write_text(head+body+main)
    subprocess.run(['clang++','-std=c++17','-O2',str(p/'oracle.cpp'),'-o',str(p/'oracle')],check=True)
    subprocess.run([str(p/'oracle'),str(p/'vectors.bin')],check=True)
    subprocess.run(['dotnet','run','--project',str(ROOT/'tools/dll-state-check/StateCheck.csproj'),'--',str(ROOT/'Assets/Resources/NativeWeather.json'),str(p/'vectors.bin')],check=True,cwd=ROOT)
