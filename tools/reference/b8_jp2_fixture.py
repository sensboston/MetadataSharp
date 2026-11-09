"""Generate one owned boxed JP2 image with pinned external research encoder."""
import hashlib
import io
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
DIRECTORY = ROOT / 'tests/fixtures/b8'
FILE = 'owned-rgb.jp2'
PIXELS = bytes((i * 37 + 11) % 256 for i in range(8 * 8 * 3))


def encode():
    from PIL import Image, features
    if Image.__version__ != '11.3.0' or features.version('jpg_2000') != '2.5.3':
        raise ValueError('expected Pillow 11.3.0 and OpenJPEG 2.5.3')
    out = io.BytesIO()
    Image.frombytes('RGB', (8, 8), PIXELS).save(out, format='JPEG2000', irreversible=False,
                                                num_resolutions=3, no_jp2=False)
    return out.getvalue()


def verify():
    manifest = json.loads((DIRECTORY / 'jp2-manifest.json').read_text(encoding='utf-8'))
    data = (DIRECTORY / FILE).read_bytes()
    if manifest['file'] != FILE or manifest['length'] != len(data) or manifest['sha256'] != hashlib.sha256(data).hexdigest():
        raise ValueError('JP2 fixture hash mismatch')
    return manifest


def main():
    if sys.argv[1:] == ['generate']:
        data = encode()
        DIRECTORY.mkdir(parents=True, exist_ok=True)
        (DIRECTORY / FILE).write_bytes(data)
        meta = {'file': FILE, 'format': 'JP2', 'length': len(data), 'sha256': hashlib.sha256(data).hexdigest(),
                'rights': 'owned-synthetic', 'width': 8, 'height': 8, 'metadata': 'none'}
        (DIRECTORY / 'jp2-manifest.json').write_text(json.dumps(meta, indent=2) + '\n', encoding='utf-8')
    elif sys.argv[1:] != ['verify']:
        raise SystemExit('expected generate or verify')
    verify()


if __name__ == '__main__':
    main()
