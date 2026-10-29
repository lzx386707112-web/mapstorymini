#!/usr/bin/env python3
"""Execute BeiDou flash jump impulse arithmetic; does not model admission/reset gates."""
import argparse
import json
import struct
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32
from unicorn.x86_const import UC_X86_REG_EBP, UC_X86_REG_ESP, UC_X86_REG_ESI, UC_X86_REG_EDI, UC_X86_REG_EBX, UC_X86_REG_ECX
from native_movement_oracle import image, SHA


def run(exe, skill, level, left):
    data=image(exe)
    u=Uc(UC_ARCH_X86,UC_MODE_32)
    u.mem_map(0x400000,(len(data)+4095)&~4095); u.mem_write(0x400000,data)
    u.mem_map(0x1000000,0x10000)
    def put(a,v): u.mem_write(a,struct.pack('<I',v&0xffffffff))
    def signed(v): return v if v<0x80000000 else v-0x100000000
    user,move,stack=0x1000000,0x1002000,0x1008000
    put(stack-0x10,skill);put(stack+0xc,level);put(user+0x570,int(left))
    # Only movement action setter is stubbed; original branches/division/shifts execute.
    u.mem_write(0x52ef17,bytes.fromhex('c20400'))
    for reg,value in [(UC_X86_REG_EBP,stack),(UC_X86_REG_ESP,stack-0x100),
                      (UC_X86_REG_EDI,user),(UC_X86_REG_ESI,move),(UC_X86_REG_EBX,4111006)]:
        u.reg_write(reg,value)
    u.emu_start(0x96bfca,0x96c038,count=500)
    x=struct.unpack('<i',u.mem_read(stack+0xc,4))[0]
    y=signed(u.reg_read(UC_X86_REG_ECX))
    return {'x':x,'y':y}


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('exe');parser.add_argument('--output',required=True)
    args=parser.parse_args();cases=[]
    for skill in (4111006,14101004,11101005):
        for level in range(1,31):
            for left in (False,True):
                value=run(args.exe,skill,level,left)
                tier=(level*(2 if skill==11101005 else 1))//4
                assert value=={'x':(-1 if left else 1)*(350+40*tier),'y':-250-20*tier}
                cases.append({'input':{'skill':skill,'level':level,'left':left},'output':value})
    with open(args.output,'w') as output:json.dump({'sha256':SHA,'cases':cases},output,separators=(',',':'))
    print(len(cases),'original x86 flash jump impulse cases')
