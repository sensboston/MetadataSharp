"""Author or verify a minimal ASCII EPS fixture; never execute PostScript."""
import hashlib
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
DIRECTORY = ROOT / 'tests/fixtures/b8'
FILE = 'owned-vector.eps'
DATA = (b'%!PS-Adobe-3.0 EPSF-3.0\n'
        b'%%BoundingBox: 0 0 10 12\n'
        b'%%Creator: MetadataSharp B8\n'
        b'%%EndComments\n'
        b'newpath 0 0 moveto 10 0 lineto 10 12 lineto 0 12 lineto closepath fill\n'
        b'%%EOF\n')


def manifest(data):
    return {'file': FILE, 'format': 'EPS', 'length': len(data), 'sha256': hashlib.sha256(data).hexdigest(),
            'rights': 'owned-synthetic', 'boundingBox': [0, 0, 10, 12], 'metadata': 'none'}


def verify():
    data = (DIRECTORY / FILE).read_bytes()
    recorded = json.loads((DIRECTORY / 'eps-manifest.json').read_text(encoding='utf-8'))
    if data != DATA or recorded != manifest(data):
        raise ValueError('EPS fixture or manifest differs from independently authored bytes')
    return recorded


def main():
    if sys.argv[1:] == ['generate']:
        DIRECTORY.mkdir(parents=True, exist_ok=True)
        (DIRECTORY / FILE).write_bytes(DATA)
        (DIRECTORY / 'eps-manifest.json').write_text(json.dumps(manifest(DATA), indent=2) + '\n', encoding='utf-8')
    elif sys.argv[1:] != ['verify']:
        raise SystemExit('expected generate or verify')
    verify()


if __name__ == '__main__':
    main()
