"""Generate owned synthetic census TIFF fixtures (B4 observation batch).

Our own deterministic stdlib writer, built on the shared authored TIFF
primitives of tools/reference/jpeg_owned_fixtures.py (build_tiff/_entry); not
derived from Exiv2, libtiff, camera samples, or any imported corpus. Fixtures
are classic little-endian II/42 TIFFs:

- tiff-base.tif      strips, bare IFD0 (recognition + from-nothing insert base)
- tiff-tiles.tif     16x16 uncompressed tiles (tile-path recognition)
- tiff-comments.tif  closest plausible comment carrier: XPComment tag 0x9C9D
                     (UTF-16LE), which the pinned CLI does not surface —
                     observed and recorded, never asserted
- tiff-exif.tif      IFD0 Orientation + Artist
- tiff-exif-dup.tif  IFD0 Artist=Alice + nested EXIF SubIFD (0x8769) Artist=Bob
- tiff-exif-thumb-jpeg.tif / tiff-exif-thumb-raw.tif
                     IFD0->IFD1 chain: JPEG-compressed and uncompressed-strip
                     embedded thumbnails (shared authored thumbnail payloads)
- tiff-iccp.tif      tag 0x8773 with a minimal synthetic RGB profile
- tiff-iccp-dup.tif  same tag in IFD0 and SubIFD (byte-identical)
- tiff-iptc.tif      tag 0x83BB with raw IIM/8BIM (bim_block 0x0404) bytes
- tiff-iptc-dup.tif  same tag in IFD0 and SubIFD (byte-identical)
- tiff-xmp.tif       tag 0x02BC with the owned XMP packet
- tiff-xmp-dup.tif   tag 0x02BC in IFD0 (A) and SubIFD (B), different creators
- tiff-xmp-nonutf8.tif
                     tag 0x02BC with a non-UTF-8 payload

Full BigTIFF, strip/tile external-to-file offsets, MM (big-endian) byte order
and multi-page chains beyond IFD1 are out of scope for this batch (recorded in
README/limits). Pinned by tests/fixtures/tiff/manifest.json; CI regenerates
and compares SHA-256.

Usage: tiff_owned_fixtures.py generate OUTDIR   # write fixtures + manifest.json
       tiff_owned_fixtures.py verify OUTDIR     # fail if manifest pins drift
       tiff_owned_fixtures.py print-manifest    # emit pinned manifest JSON
       tiff_owned_fixtures.py print-provenance  # emit provenance JSON
"""
import argparse
import hashlib
import json
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / 'tests/fixtures/tiff/manifest.json'
PROVENANCE = ROOT / 'tests/fixtures/tiff/provenance.json'
sys.path.insert(0, str(Path(__file__).resolve().parent))
import jpeg_owned_fixtures as owned  # noqa: E402  (shared TIFF primitives + owned payloads)

T_BYTE, T_ASCII, T_SHORT, T_LONG = owned.T_BYTE, owned.T_ASCII, owned.T_SHORT, owned.T_LONG
_entry = owned._entry
build_tiff = owned.build_tiff

PIXEL = b'\x0a\x14\x1e'               # owned single pixel, RGB
TILE_PIXEL = PIXEL * 256              # 16x16 RGB tile = 768 bytes


def _strip_entries(pix_off, pix_len):
    return [
        _entry(256, T_SHORT, [1]),        # ImageWidth
        _entry(257, T_SHORT, [1]),        # ImageLength
        _entry(258, T_SHORT, [8, 8, 8]),  # BitsPerSample
        _entry(259, T_SHORT, [1]),        # Compression (none)
        _entry(262, T_SHORT, [2]),        # PhotometricInterpretation (RGB)
        _entry(274, T_SHORT, [1]),        # Orientation
        (273, T_LONG, 1, struct.pack('<I', pix_off), None),   # StripOffsets
        _entry(277, T_SHORT, [3]),        # SamplesPerPixel
        _entry(278, T_SHORT, [1]),        # RowsPerStrip
        (279, T_LONG, 1, struct.pack('<I', pix_len), None),   # StripByteCounts
        _entry(284, T_SHORT, [1]),        # PlanarConfiguration
    ]


def _tile_entries(pix_off, pix_len):
    return [
        _entry(256, T_SHORT, [16]),
        _entry(257, T_SHORT, [16]),
        _entry(258, T_SHORT, [8, 8, 8]),
        _entry(259, T_SHORT, [1]),
        _entry(262, T_SHORT, [2]),
        _entry(274, T_SHORT, [1]),
        _entry(277, T_SHORT, [3]),
        _entry(322, T_SHORT, [16]),       # TileWidth
        _entry(323, T_SHORT, [16]),       # TileLength
        (324, T_LONG, 1, struct.pack('<I', pix_off), None),   # TileOffsets
        (325, T_LONG, 1, struct.pack('<I', pix_len), None),   # TileByteCounts
        _entry(284, T_SHORT, [1]),
    ]


def _strip_ifds(extra_ifd0=()):
    def make(pix_off, pix_len):
        ifd0 = _strip_entries(pix_off, pix_len)
        ifd0 += [_entry(tag, t, vals) for tag, t, vals in extra_ifd0]
        return [ifd0]
    return make


def base_tiff(extra_ifd0=()):
    make = _strip_ifds(extra_ifd0)
    phase1 = build_tiff(make(0, len(PIXEL)))
    pix_off = len(phase1)
    body = build_tiff(make(pix_off, len(PIXEL)))
    assert len(body) == pix_off
    return body + PIXEL


def tiles_tiff():
    def make(off, ln):
        return [_tile_entries(off, ln)]
    phase1 = build_tiff(make(0, len(TILE_PIXEL)))
    off = len(phase1)
    body = build_tiff(make(off, len(TILE_PIXEL)))
    assert len(body) == off
    return body + TILE_PIXEL


COMMENT = 'MetadataSharp B4 comment é ✓'.encode('utf-16-le') + b'\x00\x00'


def comments_tiff():
    # XPComment (0x9C9C per the Microsoft tag table), UTF-16LE, NUL-terminated.
    return base_tiff(extra_ifd0=[(0x9C9C, T_BYTE, COMMENT)])


def exif_tiff():
    return base_tiff(extra_ifd0=[(315, T_ASCII, b'Alice\x00')])


def exif_dup_tiff():
    # same IFD0 carrying Artist twice (invalid-but-authored repeated carrier;
    # entries keep authored order — build_tiff's sort is stable)
    return base_tiff(extra_ifd0=[(315, T_ASCII, b'Alice\x00'), (315, T_ASCII, b'Bob\x00')])


def exif_thumb_tiff(compressed):
    blob = owned.thumb_jpeg_bytes() if compressed else bytes(i % 256 for i in range(192))

    def make(pix_off, blob_off):
        ifd0 = _strip_entries(pix_off, len(PIXEL))
        if compressed:
            ifd1 = [_entry(259, T_SHORT, [6]), _entry(262, T_SHORT, [2]),
                    (513, T_LONG, 1, struct.pack('<I', blob_off), None),
                    _entry(514, T_LONG, [len(blob)])]
        else:
            ifd1 = [_entry(256, T_SHORT, [8]), _entry(257, T_SHORT, [8]),
                    _entry(259, T_SHORT, [1]), _entry(262, T_SHORT, [2]),
                    (273, T_LONG, 1, struct.pack('<I', blob_off), None),
                    _entry(277, T_SHORT, [3]), _entry(278, T_SHORT, [8]),
                    _entry(279, T_LONG, [len(blob)])]
        return [ifd0, ifd1]

    phase1 = build_tiff(make(0, 0), chain=True)
    pix_off = len(phase1)
    blob_off = pix_off + len(PIXEL)
    body = build_tiff(make(pix_off, blob_off), chain=True)
    assert len(body) == pix_off
    return body + PIXEL + blob


IIM_PAYLOAD = owned.IPTC_ALPHA + owned.IPTC_BETA  # raw IIM datasets (TIFF tag 0x83BB convention)
XMP_PKT_A = owned.xmp_packet()
XMP_PKT_B = XMP_PKT_A.replace(b'MetadataSharp</xmp:CreatorTool>', b'MetadataSharpB</xmp:CreatorTool>')
XMP_PKT_NONUTF8 = b'\x89\xfe\xffRAW not xml'


def iccp_tiff():
    return base_tiff(extra_ifd0=[(0x8773, T_BYTE, owned.PROFILE_A)])


def iccp_dup_tiff():
    return base_tiff(extra_ifd0=[(0x8773, T_BYTE, owned.PROFILE_A), (0x8773, T_BYTE, owned.PROFILE_A)])


def iptc_tiff():
    return base_tiff(extra_ifd0=[(0x83BB, T_BYTE, IIM_PAYLOAD)])


def iptc_dup_tiff():
    return base_tiff(extra_ifd0=[(0x83BB, T_BYTE, IIM_PAYLOAD), (0x83BB, T_BYTE, IIM_PAYLOAD)])


def xmp_tiff():
    return base_tiff(extra_ifd0=[(0x02BC, T_BYTE, XMP_PKT_A)])


def xmp_dup_tiff():
    return base_tiff(extra_ifd0=[(0x02BC, T_BYTE, XMP_PKT_A), (0x02BC, T_BYTE, XMP_PKT_B)])


def xmp_nonutf8_tiff():
    return base_tiff(extra_ifd0=[(0x02BC, T_BYTE, XMP_PKT_NONUTF8)])


def _register(name, data, layout, duplicates, recipe):
    FIXTURES[name] = dict(bytes=data, layout=layout, duplicates=duplicates, recipe=recipe)


def _build_catalog():
    if FIXTURES:
        return FIXTURES
    _register('tiff-base.tif', base_tiff(), 'container', 'none', 'tiff-strip-bare-ifd0-v1')
    _register('tiff-tiles.tif', tiles_tiff(), 'container', 'none', 'tiff-tile-16x16-v1')
    _register('tiff-comments.tif', comments_tiff(), 'tiff-comments', 'none',
              'tiff-xpcomment-tag-v1')
    _register('tiff-exif.tif', exif_tiff(), 'tiff-exif', 'none', 'tiff-exif-orientation-artist-v1')
    _register('tiff-exif-dup.tif', exif_dup_tiff(), 'tiff-exif', 'first-wins',
              'tiff-exif-ifd0-artist-twice-alice-bob-v1')
    _register('tiff-exif-thumb-jpeg.tif', exif_thumb_tiff(True), 'exif-ifd1-jpeg', 'none',
              'tiff-ifd0-ifd1-jpeg-thumbnail-v1')
    _register('tiff-exif-thumb-raw.tif', exif_thumb_tiff(False), 'exif-ifd1-uncompressed', 'none',
              'tiff-ifd0-ifd1-uncompressed-thumbnail-v1')
    _register('tiff-iccp.tif', iccp_tiff(), 'tiff-icc', 'none', 'tiff-iccp-single-v1')
    _register('tiff-iccp-dup.tif', iccp_dup_tiff(), 'tiff-icc', 'first-wins',
              'tiff-iccp-ifd0-tag-twice-byte-identical-v1')
    _register('tiff-iptc.tif', iptc_tiff(), 'tiff-iptc', 'none', 'tiff-iptc-raw-iim-v1')
    _register('tiff-iptc-dup.tif', iptc_dup_tiff(), 'tiff-iptc', 'first-wins',
              'tiff-iptc-ifd0-tag-twice-byte-identical-v1')
    _register('tiff-xmp.tif', xmp_tiff(), 'tiff-xmp', 'none', 'tiff-xmp-tag-700-standard-v1')
    _register('tiff-xmp-dup.tif', xmp_dup_tiff(), 'tiff-xmp', 'first-wins',
              'tiff-xmp-ifd0-tag-twice-a-b-v1')
    _register('tiff-xmp-nonutf8.tif', xmp_nonutf8_tiff(), 'tiff-xmp', 'none',
              'tiff-xmp-non-utf8-payload-v1')
    return FIXTURES


def fixture_bytes(name):
    return _build_catalog()[name]['bytes']


def manifest():
    entries = []
    for name, spec in sorted(_build_catalog().items()):
        data = spec['bytes']
        entries.append(dict(file=name, sha256=hashlib.sha256(data).hexdigest(), length=len(data),
                            layout=spec['layout'], duplicates=spec['duplicates'],
                            recipe=spec['recipe'], rights='owned-synthetic'))
    return entries


def provenance():
    return dict(
        encoder='None (authored byte-level TIFF writer; tools/reference/tiff_owned_fixtures.py on shared jpeg_owned_fixtures TIFF primitives)',
        recipe=('Deterministic classic II/42 TIFF writer: IFD0 (stdlib; strip or tile '
                'image data, uncompressed, owned 0x0A141E pixel values) + authored metadata '
                'authored on purpose); thumbnails '
                'chain IFD1 with JPEG (shared authored baseline body) or uncompressed strip '
                'blobs; IPTC rides raw IIM datasets in tag 0x83BB; '
                'XMP rides tag 0x02BC; ICC rides tag 0x8773; comments ride XPComment 0x9C9C '
                '(UTF-16LE), an undocumented carrier the pinned CLI does not surface. '
                'Pinned by tests/fixtures/tiff/manifest.json; CI regenerates and compares SHA-256.'),
        inputRights=('MetadataSharp authored synthetic TIFF fixtures, MIT; no imported images, '
                     'no imported codec code/tests, no upstream fixture corpus.'),
        fixtures=manifest())


def verify_dir(outdir):
    outdir = Path(outdir)
    pinned = {entry['file']: entry for entry in json.loads(MANIFEST.read_text())}
    missing = sorted(set(pinned) - {p.name for p in outdir.glob('*.tif')})
    if missing:
        raise ValueError(f'missing fixtures: {missing}')
    for name, entry in sorted(pinned.items()):
        data = (outdir / name).read_bytes()
        digest = hashlib.sha256(data).hexdigest()
        if digest != entry['sha256'] or len(data) != entry['length']:
            raise ValueError(f'fixture drifted: {name}')
    print(f'verified {len(pinned)} fixtures against {MANIFEST}')


def generate(outdir):
    outdir = Path(outdir)
    outdir.mkdir(parents=True, exist_ok=True)
    for name, spec in sorted(_build_catalog().items()):
        (outdir / name).write_bytes(spec['bytes'])
    (outdir / 'manifest.json').write_text(json.dumps(manifest(), indent=2) + '\n')
    return len(_build_catalog())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('command', choices=['generate', 'verify', 'print-manifest', 'print-provenance'])
    ap.add_argument('outdir', nargs='?')
    args = ap.parse_args()
    if args.command == 'generate':
        outdir = args.outdir or (ROOT / 'artifacts/owned-tiff')
        print(f'wrote {generate(outdir)} fixtures to {outdir}')
    elif args.command == 'verify':
        verify_dir(args.outdir or (ROOT / 'artifacts/owned-tiff'))
    elif args.command == 'print-manifest':
        sys.stdout.write(json.dumps(manifest(), indent=2) + '\n')
    elif args.command == 'print-provenance':
        sys.stdout.write(json.dumps(provenance(), indent=2) + '\n')


FIXTURES = {}

if __name__ == '__main__':
    main()
