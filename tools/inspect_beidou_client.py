#!/usr/bin/env python3
"""Read-only PE/string-pool inspection. Requires pefile and capstone.

Virtual addresses and string IDs apply to the inspected client, not all builds.
Prints the executable SHA256 so findings can be tied to an exact input.

Linear sweep (--start/--end) is fine for small leaf functions, but VC++ code
with switch tables desyncs it: instruction boundaries drift and every later
offset looks impossible. --fn follows real control flow (jcc, jmp, jump tables)
and is the only mode safe for the large parser/executor functions.
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path

import capstone
import pefile

GET_STRING = 0x79E805  # push <id>; push <dst>; call GET_STRING


def _decode(data, base, va):
    dec = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
    dec.detail = True
    for ins in dec.disasm(data[va-base:va-base+16], va, 1):
        return ins
    return None


def _jump_table(data, base, ins):
    try:
        ops = ins.operands
    except Exception:
        return []
    for o in ops:
        if o.type == capstone.x86.X86_OP_MEM and o.mem.index and not o.mem.base:
            table = o.mem.disp & 0xFFFFFFFF
            out = []
            for i in range(512):
                try:
                    tgt = struct.unpack_from("<I", data, table-base+i*4)[0]
                    data[tgt-base]
                except Exception:
                    break
                if not (0x400000 <= tgt < 0x10000000):
                    break
                out.append(tgt)
            return out
    return []


def walk(data, base, entry, low, high):
    """Breadth-first over basic blocks; returns instructions sorted by address."""
    text_lo, text_hi = low, high
    seen = {}
    work = [entry]
    while work:
        va = work.pop()
        while text_lo <= va < text_hi:
            if va in seen:
                break
            ins = _decode(data, base, va)
            if ins is None:
                break
            seen[va] = ins
            mnem = ins.mnemonic
            if mnem in ("ret", "retf", "iretd", "iret", "hlt", "ud2"):
                break
            nxt = ins.address + ins.size
            try:
                ops = ins.operands
            except Exception:
                ops = []
            imm = ops[0].imm & 0xFFFFFFFF if ops and ops[0].type == capstone.x86.X86_OP_IMM else None
            if mnem == "jmp":
                for t in _jump_table(data, base, ins):
                    work.append(t)
                if imm is not None and text_lo <= imm < text_hi:
                    va = imm
                    continue
                break
            if mnem.startswith("j") or mnem in ("loop", "jecxz"):
                if imm is not None and text_lo <= imm < text_hi:
                    work.append(imm)
                va = nxt
                continue
            va = nxt
    return sorted(seen.values(), key=lambda i: i.address)


def dump_function(data, base, text_lo, text_hi, entry, gte, lt):
    insns = walk(data, base, entry, text_lo, text_hi)
    print(f"# function {entry:#x}: {len(insns)} instructions, "
          f"{insns[0].address:#x}..{insns[-1].address:#x}")
    gap = None
    for ins in insns:
        if gap is not None and ins.address > gap:
            print(f"        !! uncovered {gap:#x}..{ins.address:#x}")
        gap = ins.address + ins.size
        if gte is not None and ins.address < gte:
            continue
        if lt is not None and ins.address >= lt:
            continue
        print(f"{ins.address:08x} {ins.mnemonic:8} {ins.op_str}")


def dump_fields(data, base, text_lo, text_hi, entry, pool, window):
    """Map each WZ field name read by the function to the store that follows it."""
    insns = walk(data, base, entry, text_lo, text_hi)
    marks = []
    for i, ins in enumerate(insns):
        if ins.mnemonic == "call" and ins.op_str == hex(GET_STRING) and i >= 2:
            src = insns[i-2]
            if src.mnemonic == "push" and src.op_str.startswith("0x"):
                sid = int(src.op_str, 16)
                name = pool.get(str(sid)) if pool else None
                marks.append((i, sid, name or f"<id {sid}>", src.address))
    for k, (idx, sid, name, va) in enumerate(marks):
        end = marks[k+1][0] if k+1 < len(marks) else min(idx+window, len(insns))
        stores = [ins for ins in insns[idx:end]
                  if ins.mnemonic == "mov" and ins.op_str.count(",") == 1
                  and ins.op_str.split(",")[0].strip().startswith("dword ptr [")
                  and not ins.op_str.split(",")[0].strip().startswith("dword ptr [ebp")]
        head = f"{sid:6d} {name:<40} @{va:08x}"
        if not stores:
            print(head + "  -> (no direct store)")
        for j, st in enumerate(stores[:4]):
            print((head if j == 0 else " " * len(head)) + f"  -> {st.op_str.split(',')[0].strip():<26} @{st.address:08x}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("exe", type=Path)
    parser.add_argument("--start", type=lambda x: int(x, 0))
    parser.add_argument("--end", type=lambda x: int(x, 0))
    parser.add_argument("--xref", type=lambda x: int(x, 0))
    parser.add_argument("--callers", type=lambda x: int(x, 0), help="list relative CALL candidates; verify surrounding instruction boundaries")
    parser.add_argument("--strings", type=Path, help="write decoded string pool JSON")
    parser.add_argument("--pool", type=Path, help="string pool JSON for --fields name resolution")
    parser.add_argument("--fn", type=lambda x: int(x, 0), help="recursive-descent disassembly from a function entry (survives switch tables)")
    parser.add_argument("--fields", type=lambda x: int(x, 0), help="map WZ field names read by a parser function to the struct stores that follow")
    parser.add_argument("--window", type=int, default=40, help="instruction lookahead per field for --fields")
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
    if args.callers is not None:
        first = text.VirtualAddress
        last = first + text.Misc_VirtualSize - 4
        for offset in range(first, last):
            if data[offset] == 0xE8 and base + offset + 5 + struct.unpack_from("<i", data, offset + 1)[0] == args.callers:
                print(f"Relative CALL candidate: {base+offset:#x} -> {args.callers:#x} (verify instruction boundary)")
    if args.fn is not None or args.fields is not None:
        text_lo = base + text.VirtualAddress
        text_hi = text_lo + text.Misc_VirtualSize
        if args.fields is not None:
            pool = json.loads(args.pool.read_text(encoding="utf-8")) if args.pool else None
            dump_fields(data, base, text_lo, text_hi, args.fields, pool, args.window)
        if args.fn is not None:
            dump_function(data, base, text_lo, text_hi, args.fn, args.start, args.end)
    elif args.start is not None:
        if args.end is None or args.end <= args.start:
            parser.error("--start requires a greater --end")
        print("# linear sweep: instruction boundaries drift after a switch table; use --fn for large functions")
        decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
        decoder.skipdata = True
        for instruction in decoder.disasm(data[args.start-base:args.end-base], args.start):
            print(f"{instruction.address:08x} {instruction.mnemonic:8} {instruction.op_str}")


if __name__ == "__main__":
    main()
