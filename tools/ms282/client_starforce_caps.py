"""Verified cap function A08930, TMS282 client; see docs/tms282-skill/08 §19.

Extract static item triples and Destiny overrides from the exact binary, and
merge the client's InstanceTableWZ2 data. No server item-cap fallback.
"""
import hashlib
import struct
import sys
from pathlib import Path

CLIENT_SHA = "def89cf3c51b5d196b90776d64a68898d722c26f8ce14f97f8c3930e9c4cfc27"


def export_caps(client):
    import capstone
    import pefile
    sys.path.insert(0, str(Path.home() / "Documents/mxd/Beidou-Server/tool/wz-python"))
    from wzpy import WzImage, WzKey
    client = Path(client)
    raw = (client / "MapleStory.exe").read_bytes()
    if hashlib.sha256(raw).hexdigest() != CLIENT_SHA:
        raise ValueError("Unverified client binary: recheck A08930 before exporting")
    pe = pefile.PE(data=raw, fast_load=True)
    base = pe.OPTIONAL_HEADER.ImageBase
    decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    decoder.detail = True

    def instructions(lo, hi):
        return list(decoder.disasm(pe.get_data(lo, hi - lo), base + lo))

    def lea_target(ins):
        return ins.address + ins.size + ins.operands[1].mem.disp - base

    def membership_triple(rva):
        code = instructions(rva, rva + 0x2f)
        start = next(lea_target(i) for i in code if i.mnemonic == "lea" and i.op_str.startswith("rcx, [rip"))
        end = next(lea_target(i) for i in code if i.mnemonic == "lea" and i.op_str.startswith("rbx, [rip"))
        assert end - start == 12
        assert any(i.mnemonic == "call" and i.operands[0].imm == base + 0x78db310 for i in code)
        return start

    code = instructions(0xaf82b0, 0xaf8ad4)
    triples = []
    for i in code:
        if i.mnemonic == "lea" and i.op_str.startswith("rax, [rip"):
            triples.append(lea_target(i))
        if i.mnemonic == "call" and i.operands[0].imm != base + 0x78db310:
            # Each helper is exactly find(start, start+12, itemId), not a range.
            start = membership_triple(i.operands[0].imm - base)
            following = next(j for j in code if j.address > i.address and j.mnemonic == "lea")
            assert lea_target(following) == start
    # Last helper selects this triple or a zero fallback with CMOVNE.
    triples.append(membership_triple(0xaf7bb0))
    forced30 = {membership_triple(rva) for rva in (0xaf7190, 0xaf7af0, 0xaf7bb0)}
    caps = {}
    for address in triples:
        ids = struct.unpack("<III", pe.get_data(address, 12))
        assert all(1000000 <= item < 2000000 for item in ids)
        for index, item in enumerate(ids):
            assert item not in caps
            caps[item] = 30 if address in forced30 else (15, 20, 30)[index]
    # Sequential overrides in A08AC5..A08BD3 (second override wins).
    caps.update({1092113: 26, 1354309: 26})
    caps.update({1342090: 27, 1342104: 27, 1354309: 27})
    destiny = set()
    for i in instructions(0xaf4310, 0xaf44cd):
        if i.mnemonic == "cmp" and i.op_str.startswith("ecx, "):
            destiny.add(i.operands[1].imm)
    destiny.update((0x12f432, 0x12f433))  # LEA + unsigned <=1
    table = client / "Data/Etc/InstanceTableWZ2.img"
    image = WzImage.from_bytes(table.read_bytes(), key=WzKey.for_region("BMS"))
    image.parse()
    assert not image.truncated
    node = next(x for x in image.root.children() if x.name == "is_destiny_2nd_weapon")
    group = next(x for x in node.children() if x.name == "0")
    destiny.update(int(x.value) for x in group.children())
    for item in destiny:
        caps[item] = 25
    return {
        "sourceSha256": {"MapleStory.exe": CLIENT_SHA,
                         "Etc/InstanceTableWZ2.img": hashlib.sha256(table.read_bytes()).hexdigest()},
        "functionRva": "0xA08930",
        "levelMinimums": [0, 95, 108, 118, 128, 138],
        "normal": [5, 8, 10, 15, 20, 30],
        "superior": [3, 5, 8, 10, 12, 15],
        "itemCaps": {str(k): v for k, v in sorted(caps.items())},
    }
