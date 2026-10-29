#!/usr/bin/env python3
"""Execute original STANCE mask and received-hit probability branch."""
import argparse,json,struct
from unicorn import UC_HOOK_CODE
from unicorn.x86_const import UC_X86_REG_ESP,UC_X86_REG_EBP,UC_X86_REG_EBX
from native_hidden_contact_oracle import machine
from native_movement_oracle import SHA

def run(u,stance,limit,roll):
    frame=0x1008000
    def put(a,v):u.mem_write(a,struct.pack('<I',v&0xffffffff))
    put(frame-0x1c,0x1001000);put(frame-0x28,roll)
    put(0x1000144,limit);put(0x10036ec,stance)
    u.mem_write(0x437a0c,bytes.fromhex('b800000001c3'))
    u.mem_write(0x416563,bytes.fromhex('8b4424048b00c3'))
    u.reg_write(UC_X86_REG_ESP,frame-0x200)
    u.reg_write(UC_X86_REG_EBP,frame);u.reg_write(UC_X86_REG_EBX,0)
    result=[]
    def stop(uc,a,size,user):
        if a in (0x958bc3,0x958b0d):
            result.append(bool(struct.unpack('<I',uc.mem_read(frame-0x58,4))[0]));uc.emu_stop()
    hook=u.hook_add(UC_HOOK_CODE,stop)
    u.emu_start(0x958af1,0x958c52,count=200)
    u.hook_del(hook)
    assert len(result)==1
    return result[0]

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('exe');p.add_argument('--output',required=True);a=p.parse_args()
    u=machine(a.exe)
    u.emu_start(0x790cd4,0x790cfe,count=10000)
    mask=list(struct.unpack('<IIII',u.mem_read(0xbefd70,16)))
    assert mask==[0,0,16,0]
    cases=[]
    for stance in (-1,0,1,50,99,100,101):
        for limit in (0,2):
            for roll in (0,1,49,50,98,99):
                result=run(u,stance,limit,roll)
                assert result==((limit&2)==0 and roll<max(0,min(100,stance)))
                cases.append({'stance':stance,'fieldLimit':limit,'roll':roll,'resist':result})
    with open(a.output,'w') as f:json.dump({'sha256':SHA,'mask':mask,'cases':cases},f,separators=(',',':'))
    print(len(cases),'original x86 STANCE branches and mask passed')
