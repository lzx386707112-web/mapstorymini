"""Inspect the local x64 client; addresses are RVAs, never reuse another build's VAs.

Requires pefile and capstone. Uses the raw .pdata section even if the protected
image has stripped its exception-directory entry. This does not unpack the EXE.
"""
import argparse
import hashlib
import struct

import capstone
import pefile


def pool_string(pe, string_id):
    """This exact SHA256's fallback pool (13031B0 -> 1305280/13053F0).

    The 16-byte key is rotated left as one big-endian bit string by the
    record's first byte. The nonzero-preserving XOR excludes its terminator.
    Runtime overrides from 74A1F0 are not present in the on-disk fallback.
    """
    if not 0 <= string_id < 0x538f:
        raise ValueError("string id outside this build's pool")
    base = pe.OPTIONAL_HEADER.ImageBase
    pointer = struct.unpack("<Q", pe.get_data(0x9a8bb20 + string_id * 8, 8))[0]
    off = pe.get_offset_from_rva(pointer - base)
    data = pe.__data__
    rotation = data[off] & 127
    key = int.from_bytes(pe.get_data(0x8393c60, 16), "big")
    key = ((key << rotation) | (key >> (128 - rotation))) & ((1 << 128) - 1)
    key = key.to_bytes(16, "big")
    end = data.find(b"\0", off + 1)
    if end < 0:
        raise ValueError("unterminated pool record")
    return bytes((value ^ key[i % 16]) or key[i % 16]
                 for i, value in enumerate(data[off + 1:end])).decode("utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("exe")
    parser.add_argument("--function", type=lambda s: int(s, 0))
    parser.add_argument("--length", type=lambda s: int(s, 0), help="explicit byte limit for a leaf without .pdata")
    parser.add_argument("--refs", type=lambda s: int(s, 0))
    parser.add_argument("--string-id", action="append", type=lambda s: int(s, 0), default=[])
    parser.add_argument("--strings-containing")
    args = parser.parse_args()
    pe = pefile.PE(args.exe, fast_load=True)
    data = pe.__data__
    base = pe.OPTIONAL_HEADER.ImageBase
    if pe.FILE_HEADER.Machine != 0x8664:
        parser.error("expected an x64 PE")
    print("SHA256", hashlib.sha256(data).hexdigest(), "ImageBase", hex(base))
    if args.string_id or args.strings_containing is not None:
        if hashlib.sha256(data).hexdigest() != "def89cf3c51b5d196b90776d64a68898d722c26f8ce14f97f8c3930e9c4cfc27":
            parser.error("pool addresses are only verified for the documented local 282 build")
        ids = range(0x538f) if args.strings_containing is not None else args.string_id
        for string_id in ids:
            value = pool_string(pe, string_id)
            if args.strings_containing is None or args.strings_containing.casefold() in value.casefold():
                print(f"String {string_id:#x}: {value!r}")
    if args.function is not None:
        pdata = next(s for s in pe.sections if s.Name.startswith(b".pdata")).get_data()
        bounds = []
        for i in range(0, len(pdata) - 11, 12):
            lo, hi, _ = struct.unpack_from("<III", pdata, i)
            if lo <= args.function < hi:
                bounds.append((lo, hi))
        if args.length is not None:
            lo, hi = args.function, args.function + args.length
        elif len(bounds) == 1:
            lo, hi = bounds[0]
        else:
            parser.error("no unique raw .pdata function range; use --length for an explicit bounded read")
        print("Function RVA", hex(lo), hex(hi))
        off = pe.get_offset_from_rva(lo)
        decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
        for ins in decoder.disasm(data[off:off + hi - lo], base + lo):
            print(f"{ins.address:x}  {ins.mnemonic:8} {ins.op_str}")
    if args.refs is not None:
        target = base + args.refs
        for section in pe.sections:
            raw = section.get_data()
            if section.Characteristics & 0x20000000:
                # Direct calls and common RIP-relative LEA/MOV operands. Candidates
                # still require instruction-boundary and caller validation.
                for opcode, size, displacement in ((b"\xe8", 5, 1),
                                                   (b"\x48\x8d", 7, 3),
                                                   (b"\x48\x8b", 7, 3)):
                    start = 0
                    while (at := raw.find(opcode, start)) >= 0:
                        start = at + 1
                        if at + size > len(raw):
                            break
                        if size == 7 and raw[at + 2] & 0xc7 != 0x05:
                            continue
                        rva = section.VirtualAddress + at
                        dest = base + rva + size + struct.unpack_from("<i", raw, at + displacement)[0]
                        if dest == target:
                            print("Code candidate RVA", hex(rva), opcode.hex())
            else:
                start = 0
                while (at := raw.find(struct.pack("<Q", target), start)) >= 0:
                    start = at + 8
                    print("Pointer RVA", hex(section.VirtualAddress + at))


if __name__ == "__main__":
    main()
