#!/usr/bin/env python3
"""Record installed DLL identities and extract the verified core video table.

Requires pefile. Does not execute or modify any source-client binary.
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path

import pefile

CORE_SHA256 = '3882737456d7c95795b2afe63ad91703cd70ef299c6b83fefe5f6f70764b466f'


def main():
    project = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=project.parent.parent / 'Beidou-Server')
    args = parser.parse_args()
    client = args.source / 'clien'
    modules = []
    for path in sorted(client.glob('*.dll')):
        raw = path.read_bytes()
        pe = pefile.PE(data=raw)
        base = pe.OPTIONAL_HEADER.ImageBase
        modules.append(dict(file=path.name, sha256=hashlib.sha256(raw).hexdigest(),
                            bytes=len(raw), imageBase=f'0x{base:08X}',
                            exports=[dict(name=s.name.decode('ascii') if s.name else None,
                                          ordinal=s.ordinal, va=f'0x{base + s.address:08X}')
                                     for s in getattr(getattr(pe, 'DIRECTORY_ENTRY_EXPORT', None), 'symbols', [])]))
    core = client / 'BeiDouSkillCompatCore.dll'
    raw = core.read_bytes()
    if hashlib.sha256(raw).hexdigest() != CORE_SHA256:
        raise ValueError('Core changed: re-verify the lookup function before reusing its table addresses')
    pe = pefile.PE(data=raw)
    base = pe.OPTIONAL_HEADER.ImageBase
    videos = {}
    # Verified consumer 0x67286027: begin 0x6728A540, stride 12,
    # compare skill at +0, call player with filename at +4, end 0x6728A714.
    for address in range(0x6728A540, 0x6728A714, 12):
        skill, path_ptr, message_ptr = struct.unpack('<III', pe.get_data(address - base, 12))
        filename = pe.get_data(path_ptr - base, 256).split(b'\0')[0].decode('ascii')
        if not filename.startswith('Data\\Video\\') or not filename.endswith('.mcv'):
            raise ValueError(f'Invalid video entry at {address:#x}')
        relative = filename[len('Data\\'):].replace('\\', '/')
        if not (client / 'Data' / relative).is_file():
            raise FileNotFoundError(relative)
        videos[str(skill)] = relative
    scene = client / 'KaringSceneCompat.dll'
    scene_raw = scene.read_bytes()
    if hashlib.sha256(scene_raw).hexdigest() != '3d2c5373696f08e1124e0ce16cef74dbe7ba4f0890fbdd984136ac34c0783e41':
        raise ValueError('Scene DLL changed; re-verify marker lookup')
    scene_pe = pefile.PE(data=scene_raw)
    scene_base = scene_pe.OPTIONAL_HEADER.ImageBase
    scenes = {}
    # Consumer 0x6D8415D1: 36 entries, index*8, code+0 and path+4.
    for address in range(0x6D842620, 0x6D842740, 8):
        code, ptr = struct.unpack('<II', scene_pe.get_data(address - scene_base, 8))
        filename = scene_pe.get_data(ptr - scene_base, 256).split(b'\0')[0].decode('ascii')
        if not filename.startswith('Data\\Video\\') or not filename.endswith('.mcv'):
            raise ValueError(f'Invalid scene entry at {address:#x}')
        scenes[str(code)] = filename[len('Data\\'):].replace('\\', '/')
    (project / 'Assets/Resources/NativeBossVideos.json').write_text(json.dumps(scenes, indent=2) + '\n')
    report = project / 'docs/custom-dll-inventory.json'
    report.write_text(json.dumps(dict(modules=modules, coreVideoLookup='0x67286027',
                                     coreVideoTable='0x6728A540..0x6728A714', videos=videos,
                                     sceneVideoLookup='0x6D8415D1', scenes=scenes), indent=2) + '\n')
    target = project / 'Assets/Resources/NativeSkillVideos.json'
    target.write_text(json.dumps(videos, indent=2) + '\n')
    print(f'{len(modules)} DLL identities; {len(videos)} verified skill video mappings; all source MCVs present')


if __name__ == '__main__':
    main()
