"""Owned D1-derived classic DNG fixtures for the E01 B6 census.

Four raw/preview-root, II/MM shapes each have an empty-descriptive and a
metadata-present IFD0 variant, plus a metadata-present variant with an
EXIF IFD1 JPEG thumbnail. The preview root is distinct from the IFD1 thumbnail.
No vendor samples are used; no comprehensive DNG conformance is asserted.
The CFA pixels and DNG structural tags come from our D1 writer; metadata
payloads are independently authored B4 fixtures. This is a bounded fixture
writer, not a general TIFF serializer or DNG validator.
"""
import argparse
import hashlib
import json
from pathlib import Path

import dng_fixtures as d1
import jpeg_owned_fixtures as jpeg
import tiff_owned_fixtures as tiff

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / 'tests/fixtures/dng/census-manifest.json'


def _replace(entries, tag, typ, count, values):
    for entry in entries:
        if entry[0] == tag:
            entry[:] = [tag, typ, count, values]
            return
    raise ValueError('missing structural tag ' + str(tag))


def _metadata():
    artist = b'B6Alice\0'
    # XPComment convention: BYTE array of UTF-16LE code units.
    comment = 'B6 comment'.encode('utf-16-le') + b'\0\0'
    return [(315, d1.T_ASCII, len(artist), artist),
            (700, d1.T_BYTE, len(tiff.XMP_PKT_A), tiff.XMP_PKT_A),
            (33723, d1.T_BYTE, len(tiff.IIM_PAYLOAD), tiff.IIM_PAYLOAD),
            (34675, d1.T_BYTE, len(jpeg.PROFILE_A), jpeg.PROFILE_A),
            (40092, d1.T_BYTE, len(comment), comment)]


def build(order, shape, metadata, ifd1_jpeg=False):
    if order not in ('II', 'MM') or shape not in ('raw', 'thumb') or metadata not in ('empty', 'present'):
        raise ValueError('unsupported fixture variant')
    if ifd1_jpeg and metadata != 'present':
        raise ValueError('IFD1 fixture requires metadata-present variant')
    fmt = '<' if order == 'II' else '>'
    raw = [list(e) for e in d1.raw_entries()]
    root = raw if shape == 'raw' else [list(e) for e in d1.thumbnail_entries()]
    # Both variants have a known Artist precondition in IFD0; the raw SubIFD's
    # D1 Artist is left untouched on thumbnail-root fixtures.
    root[:] = [e for e in root if e[0] != 315]
    if metadata == 'present':
        root.extend([list(e) for e in _metadata()])
    root_items, end = d1.plan_ifd(fmt, root, 8)
    blocks = {0: order.encode('ascii') + (42).to_bytes(2, 'little' if order == 'II' else 'big')
                 + (8).to_bytes(4, 'little' if order == 'II' else 'big')}
    if shape == 'raw':
        raw_strip = end
        _replace(root, 273, d1.T_LONG, 1, [raw_strip])
        root_items, _ = d1.plan_ifd(fmt, root, 8)
        blocks[8] = d1.render_ifd(fmt, root_items, 8, 0)
    else:
        thumb_strip = end
        raw_ifd = thumb_strip + len(d1.thumb_pixels_bytes())
        _replace(root, 273, d1.T_LONG, 1, [thumb_strip])
        _replace(root, 330, d1.T_LONG, 1, [raw_ifd])
        root_items, _ = d1.plan_ifd(fmt, root, 8)
        _, raw_strip = d1.plan_ifd(fmt, raw, raw_ifd)
        _replace(raw, 273, d1.T_LONG, 1, [raw_strip])
        raw_items, _ = d1.plan_ifd(fmt, raw, raw_ifd)
        blocks[8] = d1.render_ifd(fmt, root_items, 8, 0)
        blocks[thumb_strip] = d1.thumb_pixels_bytes()
        blocks[raw_ifd] = d1.render_ifd(fmt, raw_items, raw_ifd, 0)
    blocks[raw_strip] = d1.raw_pixels_bytes(fmt)
    if ifd1_jpeg:
        # An independently authored JPEG thumbnail in a chained IFD1, distinct
        # from the RGB preview root and CFA SubIFD in the 'thumb' shape.
        ifd1_offset = raw_strip + len(d1.raw_pixels_bytes(fmt))
        thumb = jpeg.thumb_jpeg_bytes()
        entries = [[259, d1.T_SHORT, 1, [6]], [262, d1.T_SHORT, 1, [2]],
                   [513, d1.T_LONG, 1, [0]], [514, d1.T_LONG, 1, [len(thumb)]]]
        _, jpeg_offset = d1.plan_ifd(fmt, entries, ifd1_offset)
        _replace(entries, 513, d1.T_LONG, 1, [jpeg_offset])
        items, _ = d1.plan_ifd(fmt, entries, ifd1_offset)
        blocks[8] = d1.render_ifd(fmt, root_items, 8, ifd1_offset)
        blocks[ifd1_offset] = d1.render_ifd(fmt, items, ifd1_offset, 0)
        blocks[jpeg_offset] = thumb
    data = bytearray()
    for off, chunk in sorted(blocks.items()):
        if off < len(data):
            raise ValueError('fixture block overlap')
        data.extend(b'\0' * (off - len(data)))
        data.extend(chunk)
    return bytes(data)


def catalog():
    for order in ('II', 'MM'):
        for shape in ('raw', 'thumb'):
            for metadata, ifd1 in (('empty', False), ('present', False), ('present', True)):
                suffix = '-ifd1' if ifd1 else ''
                name = f'b6-{shape}-{order.lower()}-{metadata}{suffix}.dng'
                data = build(order, shape, metadata, ifd1_jpeg=ifd1)
                yield name, data, {'file': name, 'length': len(data),
                    'sha256': hashlib.sha256(data).hexdigest(),
                    'byteOrder': order, 'shape': shape, 'metadata': metadata, 'ifd1Jpeg': ifd1,
                    'recipe': 'tools/reference/dng_census_fixtures.py build: D1-derived, owned synthetic'}


def manifest():
    return [entry for _, _, entry in catalog()]


def generate(out):
    out.mkdir(parents=True, exist_ok=True)
    for name, data, _ in catalog():
        (out / name).write_bytes(data)
    (out / 'census-manifest.json').write_text(json.dumps(manifest(), indent=2) + '\n', encoding='utf-8')


def verify():
    expected = json.loads(MANIFEST.read_text(encoding='utf-8'))
    if expected != manifest():
        raise ValueError('DNG census fixture manifest differs from generator')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('generate', 'verify', 'print-manifest'))
    parser.add_argument('outdir', nargs='?', type=Path)
    args = parser.parse_args()
    if args.command == 'generate':
        if args.outdir is None:
            parser.error('generate requires outdir')
        generate(args.outdir)
    elif args.command == 'verify':
        verify()
    else:
        print(json.dumps(manifest(), indent=2))


if __name__ == '__main__':
    main()
