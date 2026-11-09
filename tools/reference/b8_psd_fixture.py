"""Author and verify two deliberately narrow, owned, uncompressed PSD v1 files.

Only flat 2x2 RGB pixels, empty layers and selected owned metadata resources are
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
RICH_NAME = 'owned-metadata-rgb.psd'
# Four independently invented RGB pixels in row-major order.
PIXELS = ((13, 47, 91), (101, 151, 203), (241, 37, 79), (19, 223, 127))


def encode():
    header = struct.pack('>4sH6sHIIHH', b'8BPS', 1, bytes(6), 3, 2, 2, 8, 3)
    empty_sections = struct.pack('>III', 0, 0, 0)
    planar = bytes(pixel[channel] for channel in range(3) for pixel in PIXELS)
    return header + empty_sections + struct.pack('>H', 0) + planar



def resource(identifier, data):
    return b'8BIM' + struct.pack('>H', identifier) + b'\x00\x00' + struct.pack('>I', len(data)) + data + bytes(len(data) % 2)


def encode_rich():
    # Own TIFF/IFD0 Artist tag, offset relative to TIFF header, ASCII + NUL.
    artist = b'B8Artist\x00'
    exif = (b'II' + struct.pack('<HIH', 42, 8, 1) +
            struct.pack('<HHII', 315, 2, len(artist), 26) + struct.pack('<I', 0) + artist)
    xmp = (b'<x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF '
           b'xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">'
           b'<rdf:Description rdf:about="" xmlns:xmp="http://ns.adobe.com/xap/1.0/" '
           b'xmp:CreatorTool="B8Tool"/></rdf:RDF></x:xmpmeta>')
    keyword = b'B8Keyword'
    iptc = b'\x1c\x02\x19' + struct.pack('>H', len(keyword)) + keyword
    resources = resource(1058, exif) + resource(1060, xmp) + resource(1028, iptc)
    base = encode()
    return base[:30] + struct.pack('>I', len(resources)) + resources + base[34:]

def entry(data, rich=False):
    return {'file': RICH_NAME if rich else NAME, 'format': 'PSD',
            'layout': 'flat-rgb-v1-metadata' if rich else 'flat-rgb-v1-no-resources',
            'length': len(data), 'sha256': hashlib.sha256(data).hexdigest(),
            'rights': 'owned-synthetic', 'width': 2, 'height': 2,
            'metadata': 'authored-exif-xmp-iptc' if rich else 'none', 'risk': 'ordinary'}


def verify():
    data = (DIRECTORY / NAME).read_bytes()
    recorded = json.loads((DIRECTORY / 'manifest.json').read_text(encoding='utf-8'))
    rich = (DIRECTORY / RICH_NAME).read_bytes()
    if data != encode() or rich != encode_rich() or recorded != [entry(data), entry(rich, rich=True)]:
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
        rich = encode_rich()
        (DIRECTORY / RICH_NAME).write_bytes(rich)
        (DIRECTORY / 'manifest.json').write_text(json.dumps([entry(data), entry(rich, rich=True)], indent=2) + '\n', encoding='utf-8')
    verify()
    print('B8 PSD fixture verified')


if __name__ == '__main__':
    main()
