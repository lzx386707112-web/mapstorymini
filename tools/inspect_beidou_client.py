#!/usr/bin/env python3
"""Read-only PE/string-pool inspection. Requires pefile and capstone.

Virtual addresses and string IDs apply to the inspected client, not all builds.
Prints the executable SHA256 so findings can be tied to an exact input.
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path

import capstone
import pefile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("exe", type=Path)
    parser.add_argument("--start", type=lambda x: int(x, 0))
    parser.add_argument("--end", type=lambda x: int(x, 0))
    parser.add_argument("--xref", type=lambda x: int(x, 0))
    parser.add_argument("--strings", type=Path, help="write decoded string pool JSON")
    args = parser.parse_args()
    raw = args.exe.read_bytes()
    print("SHA256", hashlib.sha256(raw).hexdigest())
    pe = pefile.PE(data=raw)
    base = pe.OPTIONAL_HEADER.ImageBase
    data = pe.get_memory_mapped_image()
    text = next(s for s in pe.sections if s.Name.rstrip(b"\0") == b".text")
    if args.strings:
        key = int.from_bytes(data[0xB001EC-base:0xB001EC-base+16], "big")
        decoded = {}
        for i in range(0x161A):
            offset = struct.unpack_from("<I", data, 0xBDC9D4-base+i*4)[0]-base
            shift = data[offset] % 128
            rotated = ((key << shift) | (key >> (128-shift))) & ((1 << 128)-1)
            secret = rotated.to_bytes(16, "big")
            end = data.index(0, offset+1)
            plain = bytes((v ^ secret[j % 16]) or v for j, v in enumerate(data[offset+1:end]))
            decoded[i] = plain.decode("cp949", errors="replace")
        args.strings.write_text(json.dumps(decoded, ensure_ascii=False, indent=2), encoding="utf-8")
    if args.xref is not None:
        needle = struct.pack("<I", args.xref)
        start = text.VirtualAddress
        end = start + text.Misc_VirtualSize
        while (found := data.find(needle, start, end)) >= 0:
            print(f"DWORD reference operand: {base+found:#x} (verify instruction boundary)")
            start = found+4
    if args.start is not None:
        if args.end is None or args.end <= args.start:
            parser.error("--start requires a greater --end")
        decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
        decoder.skipdata = True
        for instruction in decoder.disasm(data[args.start-base:args.end-base], args.start):
            print(f"{instruction.address:08x} {instruction.mnemonic:8} {instruction.op_str}")


if __name__ == "__main__":
    main()
