#!/usr/bin/env python3
"""Index the client's Data.wz overlay IMG; it is not a PKG1 WZ archive."""
import argparse
import hashlib
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT.parent.parent/'Beidou-Server/tool/wz-python'))
from wzpy import WzKey
from wzpy.reader import WzBinaryReader
from wzpy.properties import _decode_basic_tag


def index(path):
    with path.open('rb') as stream:
        reader = WzBinaryReader(stream, WzKey.for_region('BMS'))
        if reader.read_byte() != 0x73 or reader.read_string() != 'Property' or reader.read_u16() != 0:
            raise ValueError('Not the expected overlay IMG')
        count = reader.read_compressed_int()
        rows = []
        for _ in range(count):
            name = reader.read_string_block(0)
            tag = reader.read_byte()
            row = {'path': name, 'tag': tag}
            if tag == 9:
                size = reader.read_u32()
                row.update(offset=reader.position, size=size)
                reader.seek(reader.position+size)
            else:
                prop = _decode_basic_tag(reader, 0, name, tag, None, None)
                row['value'] = prop.value
            rows.append(row)
        if reader.position != path.stat().st_size:
            raise ValueError('Overlay has unindexed trailing bytes')
        return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=Path.home()/'Documents/mxd/MapleStory/Data.wz')
    parser.add_argument('--contains', default='cube')
    parser.add_argument('--out', type=Path)
    args = parser.parse_args()
    rows = index(args.source)
    if args.out:
        with args.source.open('rb') as stream:
            digest = hashlib.file_digest(stream, 'sha256').hexdigest()
        args.out.write_text(json.dumps({'sha256': digest, 'entries': rows}, ensure_ascii=False, indent=1)+'\n')
    selected = [row for row in rows if args.contains.lower() in row['path'].lower()]
    print(json.dumps({'total': len(rows), 'matches': selected}, ensure_ascii=False, indent=1))


if __name__ == '__main__':
    main()
