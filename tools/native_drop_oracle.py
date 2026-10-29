#!/usr/bin/env python3
"""Execute the unmodified BeiDou pickup interpolation/arc arithmetic (0x44191F..0x441974)."""
import argparse
import json
import struct
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32
from unicorn.x86_const import UC_X86_REG_EBP, UC_X86_REG_ESP, UC_X86_REG_EBX, UC_X86_REG_EIP, UC_X86_REG_EAX
from native_movement_oracle import image, SHA


def pickup(data, sx, sy, tx, ty, ms):
    u = Uc(UC_ARCH_X86, UC_MODE_32)
    u.mem_map(0x400000, (len(data) + 4095) & ~4095)
    u.mem_write(0x400000, data)
    u.mem_map(0x1000000, 0x20000)
    bp = 0x101f000
    for offset, value in [(-0x24, sx), (-0x28, sy), (-0x14, tx), (-0x18, ty), (-0x2c, 0x1000000)]:
        u.mem_write(bp + offset, struct.pack('<i', value))
    u.reg_write(UC_X86_REG_EBP, bp)
    u.reg_write(UC_X86_REG_ESP, bp - 0x100)
    u.reg_write(UC_X86_REG_EBX, ms)
    u.emu_start(0x44191f, 0x441974, count=100)
    assert u.reg_read(UC_X86_REG_EIP) == 0x441974
    x = struct.unpack('<i', u.mem_read(bp - 0x24, 4))[0]
    y = u.reg_read(UC_X86_REG_EBX)
    if y >= 0x80000000:
        y -= 0x100000000
    return x, y


def flight(data, sx, sy, tx, ty, ms):
    u = Uc(UC_ARCH_X86, UC_MODE_32)
    u.mem_map(0x400000, (len(data) + 4095) & ~4095)
    u.mem_write(0x400000, data)
    u.mem_map(0x1000000, 0x20000)
    # Replace only the secure-int getter; flight, rounding and CRT conversion stay original.
    u.mem_write(0x42873d, bytes.fromhex('8b01c3'))
    bp, drop = 0x101f000, 0x1000000
    for offset, value in [(0, sy), (0xc, sx), (0x68, ty), (0x74, tx), (0x4c, ms - 30), (0x50, 1000)]:
        u.mem_write(drop + offset, struct.pack('<i', value))
    u.mem_write(bp - 0x14, struct.pack('<I', drop))
    u.mem_write(bp - 0x28, struct.pack('<d', 400))
    u.reg_write(UC_X86_REG_EBP, bp)
    u.reg_write(UC_X86_REG_ESP, bp - 0x200)
    u.reg_write(UC_X86_REG_EAX, drop)
    u.emu_start(0x505117, 0x505229, count=1000)
    assert u.reg_read(UC_X86_REG_EIP) == 0x505229
    return (struct.unpack('<i', u.mem_read(bp - 0x1c, 4))[0],
            struct.unpack('<i', u.mem_read(bp - 0x24, 4))[0])


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('exe')
    p.add_argument('--output', required=True)
    a = p.parse_args()
    data = image(a.exe)
    cases = []
    for sx, sy in [(0, 0), (-125, -23), (480, 137)]:
        for tx, ty in [(-24, -32), (0, 0), (501, 99)]:
            for ms in [0, 1, 30, 120, 350, 420, 500, 699]:
                x, y = pickup(data, sx, sy, tx, ty, ms)
                cases.append(dict(sx=sx, sy=sy, tx=tx, ty=ty, ms=ms, x=x, y=y))
    flights = []
    for sx, sy in [(0, 0), (-125, -23), (480, 137)]:
        for tx, ty in [(-24, 180), (0, 0), (501, 200)]:
            for ms in [30, 120, 300, 510, 750, 990]:
                # Compare only state 1 before a landing/state transition, with its 1000ms duration.
                if sy > ty:
                    continue
                x, y = flight(data, sx, sy, tx, ty, ms)
                flights.append(dict(sx=sx, sy=sy, tx=tx, ty=ty, ms=ms, x=x, y=y))
    with open(a.output, 'w') as f:
        json.dump(dict(sha256=SHA, cases=cases, flights=flights), f, separators=(',', ':'))
    print('Executed original pickup arithmetic:', len(cases), 'cases')
    print('Executed original flight arithmetic:', len(flights), 'cases')
