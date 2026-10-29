#!/usr/bin/env python3
"""Execute original BeiDou x86 ground/air integrators against controlled physics inputs.
Requires pefile and unicorn. Only secured-value storage/access and allocator wrappers
are substituted; arithmetic/branches/integration run from the original EXE.
"""
import argparse, hashlib, json, math, struct
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
from unicorn.x86_const import UC_X86_REG_EBP, UC_X86_REG_ESP, UC_X86_REG_ESI, UC_X86_REG_EIP, UC_X86_REG_ECX
import pefile
from functools import lru_cache

@lru_cache(maxsize=1)
def image(exe):
    raw = open(exe, "rb").read()
    if hashlib.sha256(raw).hexdigest() != SHA: raise ValueError("Unverified EXE identity")
    return pefile.PE(data=raw).get_memory_mapped_image()

SHA = "1b91cf5bd0f88eb2324bfdec39a7329eab970e863cbb253ee209b2fc6df134b2"

def run(exe, *, force=0, drag=1, slope=0, h=0, vx=0, vy=0, ms=8, air=False, flight=False, vertical_input=0, fly=1):
    data = image(exe)
    u = Uc(UC_ARCH_X86, UC_MODE_32)
    u.mem_map(0x400000, (len(data) + 4095) & ~4095)
    u.mem_write(0x400000, data)
    u.mem_map(0x1000000, 0x20000)
    def i(a,v): u.mem_write(a, struct.pack("<I",v & 0xffffffff))
    def d(a,v): u.mem_write(a, struct.pack("<d",v))
    # Substitute the encrypted-value wrappers, preserving calling conventions/x87 stack.
    patches = {
        0x6724fc: "dd01c3", # decoded secured double at this
        0x539338: "8b442404dd00c3", # decoded double at first cdecl argument
        0x5393b6: "dd442404dd1931c0c20800", # write double, ret8
        0x416563: "8b4424048b00c3", # decoded secured int
        0x7046ac: "31c0c3", # not swimming
        0x9b4aec: "dd442404dd198bc1c20800", # temporary secured double constructor
        0x4031ed: "c20400", # free temporary secured storage
        0x96c1d3: "b801000000c3", # ordinary airborne branch
    }
    if flight: patches[0x96c1d3] = "31c0c3"
    for a,b in patches.items(): u.mem_write(a, bytes.fromhex(b))
    movement, fh, attr, actor, field, physics = [0x1000000 + n*0x1000 for n in range(6)]
    i(0xbebfa0, field); i(field+8,physics)
    values=[140000,125,80000,60000,120,100000,10000,.01,120000,140,120000,200,2000,670,555,2,.05,.9,35]
    for n,v in enumerate(values): d(physics+n*8,v)
    i(movement+0x110,fh); i(fh+0x28,attr)
    d(fh+0x30, math.sqrt(1-slope*slope)); d(fh+0x38,slope)
    d(attr+0xc,1);d(attr+0x18,drag);d(attr+0x24,force)
    i(movement+0x1a0,field+0x100)
    for offset in [0,0xc,0x18,0x24]: d(field+0x100+int(offset),1)
    i(movement+0x1a8,actor)
    for offset,v in [(0xc,100),(0x18,1),(0x24,1),(0x30,1),(0x3c,.9),(0x48,1),(0x54,1),(0x60,1),(0x6c,1),(0x78,0),(0x84,0)]:d(actor+offset,v)
    if flight:
        i(movement,0x1007000); i(0x1007008,0x9b1087)
        d(actor+0x78,1); d(actor+0x84,fly)
    i(movement+0x15c,vertical_input)
    i(movement+0x150,h)
    d(movement+0xe0,0);d(movement+0xf8,vx)
    d(movement+0x20,0);d(movement+0x38,0);d(movement+0x50,vx);d(movement+0x68,vy)
    # Enter after SEH prologue. Original body writes locals and performs all arithmetic.
    ebp=0x101f000
    u.reg_write(UC_X86_REG_EBP,ebp);u.reg_write(UC_X86_REG_ESP,ebp-0x100)
    u.reg_write(UC_X86_REG_ESI,movement);u.reg_write(UC_X86_REG_ECX,movement)
    i(ebp+8,ms)
    start,end=(0x9b2c46,0x9b34c1) if air or flight else (0x9b2402,0x9b2bb7)
    if air or flight:
        # Stop after final secured vertical velocity write (before SEH epilogue).
        end=0x9b34c1
    def stop(uc,a,size,data):
        if a==end:uc.emu_stop()
    u.hook_add(UC_HOOK_CODE,stop)
    u.emu_start(start,end,count=30000)
    if u.reg_read(UC_X86_REG_EIP) != end: raise RuntimeError("Native integrator did not reach verified exit")
    def get(a):return struct.unpack("<d",u.mem_read(a,8))[0]
    return dict(position=get(movement+(0x20 if air or flight else 0xe0)),velocity=get(movement+(0x50 if air or flight else 0xf8)),vertical=get(movement+0x68),y=get(movement+0x38))

if __name__ == "__main__":
    p=argparse.ArgumentParser();p.add_argument("exe");p.add_argument("--output");a=p.parse_args()
    cases=[]
    for force in [0,-1,1,-1.5]:
        for drag in [1,.1,2]:
            for slope in [0,.2,-.2,.95,-.95]:
                for h in [-1,0,1]:
                    for vx in [-300,-125,0,125,300]:
                        args=dict(force=force,drag=drag,slope=slope,h=h,vx=vx,ms=8)
                        cases.append(dict(input=args,output=run(a.exe,**args)))
    for h in [-1,0,1]:
        for vx in [-300,-125,0,125,300]:
            for vy in [-555,0,670]:
                args=dict(h=h,vx=vx,vy=vy,ms=8,air=True)
                cases.append(dict(input=args,output=run(a.exe,**args)))
    for fly in [.1,.5,1,1.4]:
        for h in [-1,0,1]:
            for vertical_input in [-1,0,1]:
                for vx in [-300,0,300]:
                    for vy in [-300,0,60,150,300]:
                        args=dict(h=h,vertical_input=vertical_input,vx=vx,vy=vy,fly=fly,ms=8,flight=True)
                        cases.append(dict(input=args,output=run(a.exe,**args)))
    result=json.dumps(dict(sha256=SHA,cases=cases),separators=(",",":"))
    if a.output:open(a.output,"w").write(result)
    else:print(result)
