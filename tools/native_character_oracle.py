#!/usr/bin/env python3
"""Run BeiDou's contact impulse arithmetic; only secure-value wrappers are replaced.

Stops before the native integer conversion (0x9B239B); that conversion is
recorded separately in the reverse reference. No game files are modified.
"""
import argparse
import json
import struct
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32
from unicorn.x86_const import UC_X86_REG_ESP, UC_X86_REG_ECX, UC_X86_REG_EIP, UC_X86_REG_EBP
from native_movement_oracle import image, SHA


def run(exe, vx, vy, ix, iy):
    data = image(exe)
    u = Uc(UC_ARCH_X86, UC_MODE_32)
    u.mem_map(0x400000, (len(data) + 4095) & ~4095)
    u.mem_write(0x400000, data)
    u.mem_map(0x1000000, 0x20000)
    for address, patch in {
        0x539338: "8b442404dd00c3",  # secured double reader, cdecl
        0x5393b6: "dd442404dd1931c0c20800",  # secured double writer, thiscall
        0x4b1fe1: "31c0c3",  # no ladder in this fixture
    }.items():
        u.mem_write(address, bytes.fromhex(patch))
    actor = 0x1000000
    for offset, value in [(0x50, vx), (0x68, vy), (0x190, ix), (0x198, iy)]:
        u.mem_write(actor + offset, struct.pack("<d", value))
    u.reg_write(UC_X86_REG_ECX, actor)
    u.reg_write(UC_X86_REG_ESP, 0x101f000)
    u.emu_start(0x9b222a, 0x9b239b, count=1000)
    if u.reg_read(UC_X86_REG_EIP) != 0x9b239b:
        raise RuntimeError("Native impulse did not reach verified exit")
    return {key: struct.unpack("<d", u.mem_read(actor + offset, 8))[0]
            for key, offset in [("vx", 0x50), ("vy", 0x68)]}


def line_time(exe, skill, line, base=5000):
    data = image(exe)
    u = Uc(UC_ARCH_X86, UC_MODE_32)
    u.mem_map(0x400000, (len(data) + 4095) & ~4095)
    u.mem_write(0x400000, data)
    u.mem_map(0x1000000, 0x20000)
    bp = 0x101f000
    for offset, value in [(0xc, skill), (0x10, base), (0x24, line)]:
        u.mem_write(bp + offset, struct.pack("<I", value))
    u.reg_write(UC_X86_REG_EBP, bp)
    u.reg_write(UC_X86_REG_ESP, bp - 0x100)
    u.emu_start(0x66b0a2, 0x66b13c, count=1000)
    if u.reg_read(UC_X86_REG_EIP) != 0x66b13c:
        raise RuntimeError("Native damage schedule did not reach verified exit")
    return u.reg_read(UC_X86_REG_ECX)


if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("exe")
    p.add_argument("--output", required=True)
    a = p.parse_args()
    cases = []
    for ix in [-270, 0, 270]:
        for vx in [-555.75, -270, -125.5, 0, 125.5, 270, 555.75]:
            for vy in [-555.75, -270, 0, 670.5]:
                values = dict(vx=vx, vy=vy, ix=ix, iy=-270)
                cases.append(dict(input=values, output=run(a.exe, **values)))
    timing = [dict(skill=skill, line=line, base=5000, due=line_time(a.exe, skill, line))
              for skill in [0, 5101004, 15101003, 5121007, 15111004, 22171002, 3111006, 3211006, 13111001]
              for line in range(6)]
    with open(a.output, "w") as f:
        json.dump(dict(sha256=SHA, cases=cases, timing=timing), f, separators=(",", ":"))
    print("Executed original contact impulse:", len(cases), "cases")
    print("Executed original damage schedule:", len(timing), "cases")
