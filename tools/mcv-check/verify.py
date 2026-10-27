#!/usr/bin/env python3
"""Compare every parsed frame to the source client's C++ parser, including bad inputs."""
import struct
import subprocess
import tempfile
from pathlib import Path


def main():
    here = Path(__file__).resolve().parent
    source = here.parents[3] / 'Beidou-Server'
    video_source = source / 'tool/client-video'
    subprocess.run(['rtk', 'proxy', 'dotnet', 'build', str(here / 'McvCheck.csproj'), '-c', 'Release'], check=True)
    managed = here / 'bin/Release/net8.0/McvCheck.dll'
    with tempfile.TemporaryDirectory(prefix='mapledash-mcv-check-') as tmp:
        oracle = Path(tmp) / 'oracle'
        subprocess.run(['rtk', 'proxy', 'c++', '-std=c++11', '-I', str(video_source),
                        str(here / 'oracle.cpp'), str(video_source / 'McvFormat.cpp'), '-o', str(oracle)], check=True)
        files = sorted((source / 'clien/Data/Video').glob('*.mcv'))
        if not files:
            raise RuntimeError('No real MCV fixtures')
        cases = list(files)
        # Exercise all flag combinations, relative payload offsets, zero-size
        # alpha entries, explicit/implicit timing and UInt64 overflow boundaries.
        for flags in range(8):
            table = struct.pack('<II', 0, 1)
            if flags & 1:
                table += struct.pack('<II', 0, 0)
            if flags & 2:
                table += struct.pack('<I', 3)
            if flags & 4:
                table += struct.pack('<Q', 7)
            header = bytearray(36)
            struct.pack_into('<4sHHIHHIB3xQI', header, 0, b'MCV0', 0, 36,
                             0x30385056 ^ 0xA5A5A5A5, 1, 1, 1, flags, 1000000, 2)
            file = Path(tmp) / f'flags-{flags}.mcv'
            file.write_bytes(header + table + b'X')
            cases.append(file)
        seed = files[0].read_bytes()
        corruptions = [b'', seed[:35], b'NOPE' + seed[4:], seed[:-1]]
        for offset, encoded in [(6, struct.pack('<H', 1)), (12, b'\0\0'),
                                (16, struct.pack('<I', 100001)), (20, b'\x80'),
                                (24, struct.pack('<Q', 0xFFFFFFFFFFFFFFFF))]:
            changed = bytearray(seed)
            changed[offset:offset + len(encoded)] = encoded
            corruptions.append(changed)
        for i, content in enumerate(corruptions):
            file = Path(tmp) / f'corrupt-{i}.mcv'
            file.write_bytes(content)
            cases.append(file)
        for file in cases:
            expected = subprocess.run([str(oracle), str(file)], capture_output=True)
            actual = subprocess.run(['dotnet', str(managed), str(file)], capture_output=True)
            if expected.returncode != actual.returncode or expected.stdout != actual.stdout:
                raise AssertionError(f'MCV mismatch: {file.name}\n{expected.stderr!r}\n{actual.stderr!r}')
        print(f'PASS: {len(files)} original MCVs + {len(cases) - len(files)} boundary cases; every frame matches original C++')


if __name__ == '__main__':
    main()
