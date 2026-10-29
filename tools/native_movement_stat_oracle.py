#!/usr/bin/env python3
"""Run verified BeiDou ordinary movement stat clamps/getters against controlled inputs.
Only secured storage wrappers are replaced; attribute arithmetic runs in x86.
"""
import argparse
import json
import struct
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32
from unicorn.x86_const import UC_X86_REG_EBP, UC_X86_REG_ESP, UC_X86_REG_ESI, UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EAX
from native_movement_oracle import image, SHA


def run(exe, equipment=0, passive=0, temporary=0, jumpEquipment=0, jumpTemporary=0, forced=0):
    data = image(exe)
    u = Uc(UC_ARCH_X86, UC_MODE_32)
    u.mem_map(0x400000, (len(data)+4095)&~4095)
    u.mem_write(0x400000, data)
    u.mem_map(0x1000000, 0x20000)
    def integer(a, v): u.mem_write(a, struct.pack('<I', v & 0xffffffff))
    def real(a, v): u.mem_write(a, struct.pack('<d', v))
    for address, code in {
        0x416563: '8b4424048b00c3',       # cdecl secured int read
        0x4165b1: '890a31c0c3',           # fastcall secured int write (checksum unused)
        0x725194: '8bc1c3',               # stat value holder access
        0x6724fc: 'dd01c3',               # secured double read
        0x500d61: 'dd442404dd19c20800',   # secured double write, thiscall ret8
    }.items(): u.mem_write(address, bytes.fromhex(code))
    stat, context, user, param, holder, stack = [0x1000000+n*0x2000 for n in range(6)]
    ebp=stack+0x1000
    integer(ebp+8,140)
    integer(stat+0x150,100+equipment+passive)
    integer(stat+0x180,100+jumpEquipment)
    u.reg_write(UC_X86_REG_EBP,ebp)
    u.reg_write(UC_X86_REG_ESP,ebp-0x100)
    u.reg_write(UC_X86_REG_ESI,stat)
    u.reg_write(UC_X86_REG_EBX,100)
    u.emu_start(0x78074a,0x7807a8,count=1000)
    integer(stat+0x15c,temporary); integer(holder,temporary)
    integer(stat+0xcac,holder)
    integer(stat+0x18c,jumpTemporary); integer(holder+0x10,jumpTemporary)
    integer(stat+0xcb4,holder+0x10)
    def getter(start):
        u.reg_write(UC_X86_REG_ECX,stat)
        u.reg_write(UC_X86_REG_ESP,ebp-0x100)
        integer(ebp-0x100,0x1001000)
        u.emu_start(start,0x1001000,count=1000)
        return u.reg_read(UC_X86_REG_EAX)
    speed=getter(0x8c457c); jump=getter(0x8c45e8)
    integer(ebp-0x20,context); integer(ebp-0x10,forced or speed); integer(ebp-0x14,jump)
    integer(context+0x2e9c,0)  # ordinary default speed cap
    integer(user+0x2b64,param)
    u.reg_write(UC_X86_REG_ESI,user)
    u.reg_write(UC_X86_REG_ESP,ebp-0x100)
    u.emu_start(0x94d8f1,0x94d9c3,count=2000)
    def decoded(a): return struct.unpack('<d',u.mem_read(a,8))[0]
    return {'walk':decoded(param+0x24),'jump':decoded(param+0x48)}


if __name__ == '__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('exe'); parser.add_argument('--output',required=True)
    args=parser.parse_args()
    cases=[]
    for equipment in [-20,0,10,60]:
        for passive in [0,20]:
            for temporary in [-50,0,2,40]:
                for jumpEquipment,jumpTemporary in [(-10,10),(0,0),(10,20)]:
                    for forced in [0,80]:
                        inputs=dict(equipment=equipment,passive=passive,temporary=temporary,
                                    jumpEquipment=jumpEquipment,jumpTemporary=jumpTemporary,forced=forced)
                        cases.append({'input':inputs,'output':run(args.exe,**inputs)})
    with open(args.output,'w') as output: json.dump({'sha256':SHA,'cases':cases},output,separators=(',',':'))
    print(len(cases),'original x86 movement stat cases')
