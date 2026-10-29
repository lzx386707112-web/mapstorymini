#!/usr/bin/env python3
"""Execute original physical INVINCIBLE damage multiplier, before integer conversion."""
import argparse,json,struct,math
from unicorn.x86_const import UC_X86_REG_ESP,UC_X86_REG_EBP
from native_hidden_contact_oracle import machine
from native_movement_oracle import SHA

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('exe');p.add_argument('--output',required=True);a=p.parse_args()
    u=machine(a.exe);frame=0x1008000
    u.reg_write(UC_X86_REG_ESP,frame)
    u.emu_start(0x78df2c,0x78df56,count=10000)
    mask=list(struct.unpack('<IIII',u.mem_read(0xbefe70,16)))
    assert mask==[0,0,0,32768]
    u.mem_write(0x416563,bytes.fromhex('8b4424048b00c3'))
    cases=[]
    for damage in (1.,100.,123.75,99999.9):
        for percent in (0,1,15,30,100):
            u.mem_write(frame+0x14,struct.pack('<I',0x1001000))
            u.mem_write(0x1001294,struct.pack('<i',percent))
            u.mem_write(frame-0x20,struct.pack('<d',damage))
            u.reg_write(UC_X86_REG_ESP,frame-0x200);u.reg_write(UC_X86_REG_EBP,frame)
            u.emu_start(0x7933b2,0x7933e9,count=100)
            result=struct.unpack('<d',u.mem_read(frame-0x20,8))[0]
            assert math.isclose(result,damage-percent*damage*.01,abs_tol=1e-10)
            cases.append({'damage':damage,'percent':percent,'result':result})
    with open(a.output,'w') as f:json.dump({'sha256':SHA,'mask':mask,'cases':cases},f,separators=(',',':'))
    print(len(cases),'original x86 physical INVINCIBLE multipliers passed')
