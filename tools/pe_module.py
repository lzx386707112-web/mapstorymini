"""Recursive-descent disassembler for ANY 32-bit PE (exe or dll).

Handles per-module ImageBase; jump tables resolved from the module's own
data section. Needed because the BeiDou client ships several Compat DLLs
that hook into the exe -- their hook bodies name the exe addresses.
"""
import pefile, capstone, struct, sys, os

class Mod:
    def __init__(self, path):
        self.path = path
        self.pe = pefile.PE(path, fast_load=True)
        self.data = open(path, "rb").read()
        self.IB = self.pe.OPTIONAL_HEADER.ImageBase
        self.secs = []
        for s in self.pe.sections:
            va = self.IB + s.VirtualAddress
            self.secs.append((s.Name.rstrip(b"\0").decode(errors="replace"),
                              va, va + s.Misc_VirtualSize, s.PointerToRawData, s.SizeOfRawData))
        texts = [s for s in self.secs if s[0].startswith(".text")]
        if not texts:
            texts = [s for s in self.secs if s[4] and s[1] < s[2]]
        self.TEXT_VA, self.TEXT_END = texts[0][1], texts[0][2]
        self.md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
        self.md.detail = True

    def off(self, va):
        rva = va - self.IB
        for name, lo, hi, praw, rsz in self.secs:
            if lo <= va < hi:
                return praw + (va - lo)
        raise ValueError("va %08x outside sections" % va)

    def rd(self, va, n):
        o = self.off(va)
        return self.data[o:o+n]

    def rd32(self, va):
        return struct.unpack("<I", self.rd(va, 4))[0]

    def in_text(self, va):
        return self.TEXT_VA <= va < self.TEXT_END

    def in_data(self, va):
        for name, lo, hi, praw, rsz in self.secs:
            if lo <= va < hi:
                return name
        return None

    def decode(self, va):
        for ins in self.md.disasm(self.rd(va, 16), va, 1):
            return ins
        return None

    def jt(self, ins):
        try:
            ops = ins.operands
        except Exception:
            return []
        for o in ops:
            if o.type == capstone.x86.X86_OP_MEM and o.mem.index != 0 and o.mem.base == 0:
                tbl = o.mem.disp & 0xffffffff
                if not self.in_data(tbl):
                    return []
                out = []
                cap = 1024 if self.in_data(tbl) == ".rdata" else 512
                for i in range(cap):
                    try:
                        t = self.rd32(tbl + i * 4)
                    except Exception:
                        break
                    if self.in_text(t):
                        out.append(t)
                    else:
                        break
                return out
        return []

    TERM = {"ret", "retf", "iretd", "iret", "hlt", "ud2"}

    def walk(self, entry, limit=0x20000):
        seen, work, guard = {}, [entry], 0
        while work:
            va = work.pop()
            while True:
                if va in seen or not self.in_text(va):
                    break
                ins = self.decode(va)
                if ins is None:
                    break
                seen[va] = ins
                m = ins.mnemonic
                guard += 1
                if guard > limit * 8:
                    work = []
                    break
                if m in self.TERM:
                    break
                nxt = ins.address + ins.size
                try:
                    ops = ins.operands
                except Exception:
                    ops = []
                if m == "jmp":
                    if ops and ops[0].type == capstone.x86.X86_OP_IMM:
                        t = ops[0].imm & 0xffffffff
                        if self.in_text(t):
                            va = t
                            continue
                    for t in self.jt(ins):
                        work.append(t)
                    break
                if m.startswith("j") or m in ("loop", "jecxz"):
                    if ops and ops[0].type == capstone.x86.X86_OP_IMM:
                        t = ops[0].imm & 0xffffffff
                        if self.in_text(t):
                            work.append(t)
                    va = nxt
                    continue
                va = nxt
        return seen

    def insns(self, entry):
        return sorted((v for v in self.walk(entry).values() if v), key=lambda i: i.address)

    def dump(self, entry, lo=None, hi=None):
        items = self.insns(entry)
        last = 0
        for ins in items:
            if lo is not None and ins.address < lo:
                continue
            if hi is not None and ins.address >= hi:
                break
            if last and ins.address > last + 16:
                print("        ...")
            print("%08x  %-8s %s" % (ins.address, ins.mnemonic, ins.op_str))
            last = ins.address + ins.size

    def strings(self, minlen=4):
        out = []
        cur = b""
        start = 0
        for i, b in enumerate(self.data):
            if 0x20 <= b < 0x7f:
                if not cur:
                    start = i
                cur += bytes([b])
            else:
                if len(cur) >= minlen:
                    out.append((start, cur.decode("ascii")))
                cur = b""
        if len(cur) >= minlen:
            out.append((start, cur.decode("ascii")))
        return out


EXE_RANGE = (0x400000, 0xA00000)


def exports(m):
    """[(name, VA)] -- export addresses in the directory are RVAs."""
    pe = m.pe
    try:
        pe.parse_data_directories(
            directories=[pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_EXPORT']])
    except Exception:
        return []
    if not hasattr(pe, "DIRECTORY_ENTRY_EXPORT"):
        return []
    return [((e.name.decode(errors="replace") if e.name else "ord%d" % e.ordinal),
             m.IB + e.address)
            for e in pe.DIRECTORY_ENTRY_EXPORT.symbols]


def hook_targets(m, name):
    """Collect addresses a Compat-DLL hook jumps back to.

    Two shapes appear in these patches:
      push <exe addr>; ret            -> trampoline back into the original
      mov eax, <exe addr>; call eax   -> call into the exe directly
    Both name a location inside BeiDou.exe, which is the whole point: the client
    ships patchers that tell us where the interesting code lives.
    """
    out = set()
    for ins in m.insns(name):
        try:
            ops = ins.operands
        except Exception:
            continue
        if ins.mnemonic in ("push", "mov") and ops and ops[0].type == capstone.x86.X86_OP_IMM:
            v = ops[0].imm & 0xffffffff
            if EXE_RANGE[0] <= v < EXE_RANGE[1]:
                out.add(v)
    return sorted(out)


def report(path):
    m = Mod(path)
    print("# %s  base=%08x  text=%08x..%08x" % (os.path.basename(path), m.IB, m.TEXT_VA, m.TEXT_END))
    exp = exports(m)
    if exp:
        print("exports:")
        for n, va in exp:
            tgt = hook_targets(m, va)
            extra = ("  -> exe " + ", ".join("%08x" % t for t in tgt)) if tgt else ""
            print("   %-42s %08x%s" % (n, va, extra))


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1].endswith(".dll") and len(sys.argv) == 2:
        report(sys.argv[1])
        sys.exit(0)
    m = Mod(sys.argv[1])
    entry = int(sys.argv[2], 16) if len(sys.argv) > 2 else m.TEXT_VA
    lo = int(sys.argv[3], 16) if len(sys.argv) > 3 else None
    hi = int(sys.argv[4], 16) if len(sys.argv) > 4 else None
    print("# %s base=%08x text=%08x..%08x" % (os.path.basename(m.path), m.IB, m.TEXT_VA, m.TEXT_END))
    m.dump(entry, lo, hi)
