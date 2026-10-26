#!/usr/bin/env python3
"""Extract control-creation calls and their pushed args from a VA range (read-only).

Prints, for every `call dword ptr [eax+N]` / direct call, the constant pushes that
precede it, so UI constructors' (id, x, y, w, h) tuples can be read off quickly.
"""
import argparse
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path('/Users/lizixian/Documents/mxd/mxd-android/MapleDash')
EXE = Path('/Users/lizixian/Documents/mxd/Beidou-Server/clien/BeiDou.exe')
PY = '/tmp/mapledash-reverse-venv/bin/python'

LINE = re.compile(r'^([0-9a-f]{8}) (\S+)\s*(.*)$')


def disasm(start, end):
    out = subprocess.run([PY, str(ROOT / 'tools/inspect_beidou_client.py'), str(EXE),
                          '--start', hex(start), '--end', hex(end)],
                         capture_output=True, text=True).stdout
    rows = []
    for line in out.split('\n'):
        m = LINE.match(line.strip())
        if m:
            rows.append((int(m.group(1), 16), m.group(2), m.group(3).strip()))
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('start', type=lambda s: int(s, 0))
    ap.add_argument('end', type=lambda s: int(s, 0))
    ap.add_argument('--before', type=int, default=9)
    args = ap.parse_args()
    rows = disasm(args.start, args.end)
    for i, (va, mn, ops) in enumerate(rows):
        if mn == 'call' and ('dword ptr [eax' in ops or 'dword ptr [ecx' in ops or 'dword ptr [edx' in ops):
            ctx = rows[max(0, i - args.before):i]
            pushed = []
            for _, m2, o2 in ctx:
                if m2 == 'push':
                    pushed.append(o2)
                elif m2 in ('mov', 'lea') and pushed:
                    continue
            print('%08x %-6s %s' % (va, mn, ops))
            for p in pushed:
                print('        push %s' % p)
            print('        --')


if __name__ == '__main__':
    sys.exit(main())
