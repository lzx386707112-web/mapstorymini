#!/usr/bin/env python3
"""Execute CUserLocal secondary-interface flash-jump reset branch."""
import argparse,itertools,json,struct
from unicorn import UC_HOOK_CODE
from unicorn.x86_const import UC_X86_REG_ESP,UC_X86_REG_EBP,UC_X86_REG_EDI,UC_X86_REG_EBX
from native_hidden_contact_oracle import machine
from native_movement_oracle import SHA

def run(exe,used,support,rope,ladder):
    u=machine(exe);user,move,frame=0x1000000,0x1004000,0x1008000
    def put(a,v):u.mem_write(a,struct.pack('<I',v))
    put(user+0x2b58,int(used));put(move+0x110,int(support));put(frame+0x14,move)
    u.mem_write(0x705309,b'\xb8'+struct.pack('<I',int(rope))+b'\xc3')
    u.mem_write(0x705343,b'\xb8'+struct.pack('<I',int(ladder))+b'\xc3')
    for reg,value in [(UC_X86_REG_ESP,frame-0x100),(UC_X86_REG_EBP,frame),
                      (UC_X86_REG_EDI,user+4),(UC_X86_REG_EBX,0)]:u.reg_write(reg,value)
    u.emu_start(0x9506f6,0x950725,count=100)
    return struct.unpack('<I',u.mem_read(user+0x2b58,4))[0]

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('exe');p.add_argument('--output',required=True);a=p.parse_args();cases=[]
    for used,support,rope,ladder in itertools.product((False,True),repeat=4):
        output=run(a.exe,used,support,rope,ladder)
        assert output==int(used and not(support or rope or ladder))
        cases.append({'used':used,'support':support,'rope':rope,'ladder':ladder,'output':output})
    with open(a.output,'w') as f:json.dump({'sha256':SHA,'cases':cases},f,separators=(',',':'))
    print(len(cases),'original x86 secondary-interface flash-jump reset branches passed')
