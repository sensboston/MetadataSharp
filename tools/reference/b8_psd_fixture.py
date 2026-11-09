"""Author and verify one deliberately narrow, owned, uncompressed PSD v1.

Only flat 2x2 RGB pixels and empty color, image-resource and layer sections are
constructed. This is a census research input, not a general PSD implementation.
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DIRECTORY = ROOT / 'tests/fixtures/b8'
NAME = 'owned-flat-rgb.psd'
# Four independently invented RGB pixels in row-major order.
PIXELS = ((13, 47, 91), (101, 151, 203), (241, 37, 79), (19, 223, 127))


def encode():
    header = struct.pack('>4sH6sHIIHH', b'8BPS', 1, bytes(6), 3, 2, 2, 8, 3)
    empty_sections = struct.pack('>III', 0, 0, 0)
    planar = bytes(pixel[channel] for channel in range(3) for pixel in PIXELS)
    return header + empty_sections + struct.pack('>H', 0) + planar


def entry(data):
    return {'file': NAME, 'format': 'PSD', 'layout': 'flat-rgb-v1-no-resources',
            'length': len(data), 'sha256': hashlib.sha256(data).hexdigest(),
            'rights': 'owned-synthetic', 'width': 2, 'height': 2,
            'metadata': 'none', 'risk': 'ordinary'}


def verify():
    data = (DIRECTORY / NAME).read_bytes()
    recorded = json.loads((DIRECTORY / 'manifest.json').read_text(encoding='utf-8'))
    if data != encode() or recorded != [entry(data)]:
        raise ValueError('B8 PSD fixture or manifest differs from authored bytes')
    return recorded


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=('generate', 'verify'))
    args = parser.parse_args()
    if args.action == 'generate':
        DIRECTORY.mkdir(parents=True, exist_ok=True)
        data = encode()
        (DIRECTORY / NAME).write_bytes(data)
        (DIRECTORY / 'manifest.json').write_text(json.dumps([entry(data)], indent=2) + '\n', encoding='utf-8')
    verify()
    print('B8 PSD fixture verified')


if __name__ == '__main__':
    main()
