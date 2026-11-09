"""Create or verify tiny authored B7 image probes (not camera captures).

Generation needs the exact external encoder wheels in the provenance ledger;
verification uses only the standard library and never executes those encoders.
These files are bounded black-box probes, not a comprehensive format corpus.
"""
import argparse
import hashlib
import io
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FIXTURES = ROOT / 'tests/fixtures/b7'

XMP = (b'<x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF '
       b'xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">'
       b'<rdf:Description rdf:about="" xmlns:xmp="http://ns.adobe.com/xap/1.0/" '
       b'xmp:CreatorTool="B7Tool"/></rdf:RDF></x:xmpmeta>')


def pixel_data(seed):
    return bytes((i * 17 + seed) % 256 for i in range(8 * 8 * 3))


def encode_catalog():
    # External encoders: never imported by the product or used as an oracle for
    # their own format's conformance. Exact wheel versions make the recipe clear.
    from PIL import Image, features
    import imagecodecs
    import numpy
    import pillow_heif
    if (Image.__version__ != '11.3.0' or features.version('avif') != '1.3.0'
            or pillow_heif.__version__ != '1.1.0'
            or pillow_heif.libheif_info()['libheif'] != '1.20.1'
            or imagecodecs.__version__ != '2025.8.2'
            or imagecodecs.jpegxl_version() != 'libjxl 0.11.1'
            or numpy.__version__ != '2.2.6'):
        raise ValueError('wrong pinned fixture encoder versions')
    data = {}
    for name, seed, kind in [('owned.avif', 23, 'avif'), ('owned.heif', 29, 'heif'),
                             ('owned.heic', 31, 'heic')]:
        image = Image.frombytes('RGB', (8, 8), pixel_data(seed))
        exif = Image.Exif()
        exif[315] = 'B7Artist'
        out = io.BytesIO()
        if kind == 'avif':
            image.save(out, format='AVIF', quality=90, speed=8, max_threads=1, exif=exif, xmp=XMP)
        else:
            pillow_heif.from_pillow(image).save(out, quality=90, exif=exif, xmp=XMP)
        data[name] = out.getvalue()
    pixels = numpy.frombuffer(pixel_data(37), dtype=numpy.uint8).reshape(8, 8, 3)
    for name, container in [('owned-codestream.jxl', False), ('owned-container.jxl', True)]:
        encoded = imagecodecs.jpegxl_encode(pixels, lossless=True, effort=3,
                                            usecontainer=container, numthreads=1)
        if container:
            # Owned EXIF and XMP payloads; the four-byte EXIF TIFF-header offset
            # precedes our independently authored TIFF. No upstream fixture data.
            exif = Image.Exif()
            exif[315] = 'B7Artist'
            tiff = exif.tobytes()[6:]
            def box(kind, payload):
                return (8 + len(payload)).to_bytes(4, 'big') + kind + payload
            encoded += box(b'Exif', b'\x00\x00\x00\x00' + tiff) + box(b'xml ', XMP)
        data[name] = encoded
    return data


def verify(directory=FIXTURES):
    entries = json.loads((directory / 'manifest.json').read_text())
    if len(entries) != 5 or len({item['file'] for item in entries}) != 5:
        raise ValueError('unexpected fixture manifest')
    expected = {'owned.avif', 'owned.heif', 'owned.heic', 'owned-codestream.jxl', 'owned-container.jxl'}
    if {item['file'] for item in entries} != expected:
        raise ValueError('unexpected fixture names')
    for item in entries:
        data = (directory / item['file']).read_bytes()
        if len(data) != item['length'] or hashlib.sha256(data).hexdigest() != item['sha256']:
            raise ValueError('fixture hash or length mismatch: ' + item['file'])
    return entries


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('command', choices=['generate', 'verify'])
    p.add_argument('--output', type=Path, default=FIXTURES)
    args = p.parse_args()
    if args.command == 'verify':
        verify(args.output)
    else:
        args.output.mkdir(parents=True, exist_ok=True)
        for name, data in encode_catalog().items():
            (args.output / name).write_bytes(data)
            print(name, len(data), hashlib.sha256(data).hexdigest())


if __name__ == '__main__':
    main()
