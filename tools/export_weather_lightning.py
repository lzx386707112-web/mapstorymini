#!/usr/bin/env python3
"""Mechanical port of original weatherfx.cpp lightning equations, no Unity dependency."""
import json,re
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
SRC=ROOT.parent.parent/'Beidou-Server/tool/client-debug/weather-compat/src/weatherfx.cpp'
def export():
    s=SRC.read_text();body=s[s.index('static unsigned int LightningHash'):s.index('void WeatherFx::TickLightning')]
    body=body.replace('Weather::PaletteId()', 'palette').replace('LightningPatternForRegion()', 'LightningPatternForRegion(int palette)')
    body=body.replace('const LightningPattern&','LightningPattern').replace('const LightningPattern','LightningPattern')
    body=body.replace('const unsigned int','uint').replace('unsigned int','uint').replace('const float','float')
    body=body.replace('LightningPulse(uint ageMs, uint seed)','Evaluate(uint ageMs, uint seed, int palette)').replace('static float Evaluate','public static float Evaluate')
    body=body.replace('LightningPatternForRegion(int palette);','LightningPatternForRegion(palette);')
    body=re.sub(r'return \{([^}]+)\};',r'return new LightningPattern(\1);',body)
    palettes=json.loads((ROOT/'Assets/Resources/NativeWeather.json').read_text())['palettes']
    for i,p in enumerate(palettes):body=body.replace('Weather::PALETTE_'+p['name'].upper()+':',str(i)+':')
    kinds=['CLASSIC','HENESYS','COASTAL','FOREST','URBAN','CELESTIAL','ALPINE','ARID']
    # Store kind as integer: no platform or enum size dependence.
    for i,k in enumerate(kinds):body=body.replace('LIGHTNING_'+k,str(i))
    body=body.replace('LightningKind kind','int kind')
    head='// Generated from original weatherfx.cpp by tools/export_weather_lightning.py.\n// Re-run the exporter when the source SHA in docs/weather-source.json changes.\npublic static class NativeWeatherLightning\n{\n    readonly struct LightningPattern\n    {\n        public readonly uint windowMs, earliestMs, pulseMs, quietEvery, clusterEvery;\n        public readonly float minBrightness;\n        public readonly int kind;\n        public LightningPattern(uint w,uint e,uint p,uint q,uint c,float b,int k)\n        {windowMs=w;earliestMs=e;pulseMs=p;quietEvery=q;clusterEvery=c;minBrightness=b;kind=k;}\n    }\n'
    (ROOT/'Assets/Scripts/NativeWeatherLightning.cs').write_text(head+body+'}\n')
if __name__=='__main__':export()
