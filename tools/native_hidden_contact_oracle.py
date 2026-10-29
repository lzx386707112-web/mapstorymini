#!/usr/bin/env python3
"""Execute original hidden-contact mask constructors and candidate filter."""
import argparse,json,struct
from unicorn import Uc,UC_ARCH_X86,UC_MODE_32,UC_HOOK_CODE
from unicorn.x86_const import UC_X86_REG_ESP,UC_X86_REG_EBP,UC_X86_REG_ESI,UC_X86_REG_EBX
from native_movement_oracle import image,SHA


def machine(exe):
    u=Uc(UC_ARCH_X86,UC_MODE_32);data=image(exe)
    u.mem_map(0x400000,(len(data)+4095)&~4095);u.mem_write(0x400000,data)
    u.mem_map(0x1000000,0x10000);u.reg_write(UC_X86_REG_ESP,0x1008000)
    return u


def run(exe,mob,hidden,fieldLimit):
    u=machine(exe);frame=0x1008000
    def put(a,v):u.mem_write(a,struct.pack('<I',v))
    put(frame+0xc,int(hidden));put(0x1000144,fieldLimit);put(0x100217c,mob)
    u.mem_write(0x437a0c,bytes.fromhex('b800000001c3')) # field getter
    u.mem_write(0x4e8152,bytes.fromhex('8b4424048b00c3')) # secured mob id getter
    u.reg_write(UC_X86_REG_EBP,frame);u.reg_write(UC_X86_REG_ESI,0x1002000)
    u.reg_write(UC_X86_REG_EBX,0);u.reg_write(UC_X86_REG_ESP,frame-0x100)
    result=[]
    def stop(uc,a,size,user):
        if a in (0x678cf3,0x678ea0): result.append(a==0x678cf3);uc.emu_stop()
    u.hook_add(UC_HOOK_CODE,stop);u.emu_start(0x678cba,0x678ebc,count=500)
    assert len(result)==1
    return result[0]


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('exe');p.add_argument('--output',required=True);a=p.parse_args()
    u=machine(a.exe);masks={}
    for name,start,end,address in [('DARKSIGHT',0x78dd55,0x78dd7f,0xbefec0),('WIND_WALK',0x791274,0x79129e,0xbefbd0)]:
        u.reg_write(UC_X86_REG_ESP,0x1008000);u.emu_start(start,end,count=10000)
        masks[name]=list(struct.unpack('<IIII',u.mem_read(address,16)))
    assert masks=={'DARKSIGHT':[0,0,0,1024],'WIND_WALK':[0,4,0,0]}
    cases=[]
    for mob in (100100,9400594):
        for hidden in (False,True):
            for fieldLimit in (0,2):
                allowed=run(a.exe,mob,hidden,fieldLimit)
                assert allowed==(not hidden or mob==9400594 or bool(fieldLimit&2))
                cases.append({'input':{'mob':mob,'hidden':hidden,'fieldLimit':fieldLimit},'candidate':allowed})
    with open(a.output,'w') as f:json.dump({'sha256':SHA,'masks':masks,'cases':cases},f,separators=(',',':'))
    print(len(cases),'original x86 hidden-contact branches and two original masks')
