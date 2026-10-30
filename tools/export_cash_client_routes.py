#!/usr/bin/env python3
"""Execute the original pure cash-item classifiers in an isolated x86 emulator.
Requires pefile and unicorn. No game process or source file is modified.
"""
import argparse, hashlib, json, struct
from pathlib import Path
import pefile
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32
from unicorn.x86_const import UC_X86_REG_ESP, UC_X86_REG_EAX
ap=argparse.ArgumentParser(description=__doc__)
ap.add_argument("exe",type=Path)
args=ap.parse_args()
identity=hashlib.sha256(args.exe.read_bytes()).hexdigest()
assert identity=="1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2", "Classifier addresses belong to another binary"
p=pefile.PE(str(args.exe));u=Uc(UC_ARCH_X86,UC_MODE_32)
u.mem_map(0x400000,0x1000000);u.mem_write(0x400000,p.get_memory_mapped_image());u.mem_map(0x2000000,0x10000)
def classify(item,fn):
    u.reg_write(UC_X86_REG_ESP,0x2008000)
    u.mem_write(0x2008000,struct.pack("<II",0x2009000,item))
    u.emu_start(fn,0x2009000,count=1000)
    return u.reg_read(UC_X86_REG_EAX)
root=Path(__file__).resolve().parents[1]
rows=json.loads((root/"docs/inventory-function-audit.json").read_text())["items"]
result={"sha256":identity,"base":0x400000,"classifier":0x48645b,"cashDispatcherClassifier":0x4863d5,"items":{}}
for row in rows:
    if row["category"]!="Cash":continue
    item=row["id"]
    result["items"][str(item)]={"type":classify(item,0x48645b),"cashType":classify(item,0x4863d5)}
(root/"docs/cash-client-routes.json").write_text(json.dumps(result,ensure_ascii=False,indent=2)+"\n")
print("Original cash classifiers:",len(result["items"]))
