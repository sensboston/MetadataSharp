"""Generate owned synthetic census WebP fixtures (B3 observation batch).

Our own deterministic stdlib writer; not derived from Exiv2, libwebp, camera
samples, or any imported corpus. WebP is a RIFF container: 'RIFF' + uint32le
size + 'WEBP' + chunks (fourcc, uint32le size, payload, zero pad to even). The
fixtures carry a hand-authored fake VP8 lossy keyframe header as the coded
image data (the pinned CLI does not decode VP8 for metadata operations; the
VP8 payload is preserved byte-for-byte across metadata rewrites, which is the
B3 analog of the byte-preserved coded-scan invariant). EXIF chunk carries a
classic II TIFF payload (same authored TIFF core as the JPEG/PNG fixtures; no
Exif\\0\\0 signature prefix - the CLI accepts either); ICCP carries a minimal
synthetic RGB profile; XMP rides the raw 'XMP ' chunk; the IPTC/comments
fixtures carry the closest plausible carriers ('IPTC' chunk with IIM/8BIM
payload, 'COM ' chunk with bytes) which the pinned CLI does NOT support -
recorded, not asserted. Deterministic; CI regenerates and compares SHA-256
against tests/fixtures/webp/census-manifest.json.

Usage: webp_owned_fixtures.py generate OUTDIR   # write fixtures + manifest.json
       webp_owned_fixtures.py verify OUTDIR     # fail if manifest pins drift
       webp_owned_fixtures.py print-manifest    # emit pinned manifest JSON
       webp_owned_fixtures.py print-provenance  # emit provenance JSON
"""
import argparse
import hashlib
import json
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / 'tests/fixtures/webp/census-manifest.json'
PROVENANCE = ROOT / 'tests/fixtures/webp/census-provenance.json'
sys.path.insert(0, str(Path(__file__).resolve().parent))
import jpeg_owned_fixtures as owned  # noqa: E402  (shared authored TIFF/XMP/ICC/IPTC cores)

# Hand-authored fake VP8 lossy keyframe header (10 bytes) + filler. NOT a real
# decodable VP8 frame; metadata operations never decode it, and the pinned CLI
# rewrites it byte-for-byte. The PROVENANCE/README record this honestly.
VP8_PAYLOAD = b'\x10\x00\x00\x9d\x01\x2a\x08\x00\x08\x00' + b'\x00' * 8


def chunk(cid, payload):
    assert len(cid) == 4
    pad = b'\x00' if len(payload) % 2 else b''
    return cid + struct.pack('<I', len(payload)) + payload + pad


def riff(chunks):
    body = b'WEBP' + b''.join(chunks)
    return b'RIFF' + struct.pack('<I', len(body)) + body


def vp8x_chunk(flags, width=8, height=8):
    return chunk(b'VP8X', bytes([flags]) + b'\x00\x00\x00'
                 + (width - 1).to_bytes(3, 'little') + (height - 1).to_bytes(3, 'little'))


def vp8_chunk():
    return chunk(b'VP8 ', VP8_PAYLOAD)


def base(flags=0x00):
    return riff([vp8x_chunk(flags), vp8_chunk()])


def exif_chunk(tiff):
    return chunk(b'EXIF', tiff)


def exif_webp(tiff, flags=0x08):
    return riff([vp8x_chunk(flags), exif_chunk(tiff), vp8_chunk()])


def exif_dup_webp(tiff_a, tiff_b):
    return riff([vp8x_chunk(0x08), exif_chunk(tiff_a), exif_chunk(tiff_b), vp8_chunk()])


def iccp_chunk(profile):
    return chunk(b'ICCP', profile)


def iccp_webp(flags=0x02):
    return riff([vp8x_chunk(flags), iccp_chunk(owned.PROFILE_A), vp8_chunk()])


def iccp_dup_webp():
    return riff([vp8x_chunk(0x02), iccp_chunk(owned.PROFILE_A), iccp_chunk(owned.PROFILE_A), vp8_chunk()])


def iptc_webp():
    """Closest plausible IPTC carrier: an 'IPTC' chunk holding IIM/8BIM bytes.
    The pinned CLI does not surface it; observed and recorded, never asserted."""
    iim = owned.bim_block(0x0404, b'', owned.IPTC_ALPHA + owned.IPTC_BETA)
    return riff([vp8x_chunk(0x00), chunk(b'IPTC', iim), vp8_chunk()])


def comments_webp():
    """Closest plausible comment carrier: a classic 'COM ' chunk. The pinned
    CLI does not surface it; observed and recorded, never asserted."""
    return riff([vp8x_chunk(0x00), chunk(b'COM ', b'MetadataSharp B3 comment'), vp8_chunk()])


def xmp_chunk(packet):
    return chunk(b'XMP ', packet)


def xmp_webp(packet, flags=0x04):
    return riff([vp8x_chunk(flags), xmp_chunk(packet), vp8_chunk()])


def xmp_dup_webp(a, b):
    return riff([vp8x_chunk(0x04), xmp_chunk(a), xmp_chunk(b), vp8_chunk()])


RAW_STRIP = bytes(i % 256 for i in range(192))
XMP_PKT_A = owned.xmp_packet()
XMP_PKT_B = XMP_PKT_A.replace(b'MetadataSharp</xmp:CreatorTool>', b'MetadataSharpB</xmp:CreatorTool>')
XMP_PKT_A2 = XMP_PKT_A.replace(b'MetadataSharp</xmp:CreatorTool>', b'MetadataSharpA</xmp:CreatorTool>')
XMP_PKT_NONUTF8 = b'\x89\xfe\xffRAW not xml'

FIXTURES = {}


def _register(name, data, layout, duplicates, recipe):
    FIXTURES[name] = dict(bytes=data, layout=layout, duplicates=duplicates, recipe=recipe)


def _build_catalog():
    if FIXTURES:
        return FIXTURES
    _register('webp-base.webp', base(0x00), 'container', 'none', 'webp-vp8x-base-v1')
    _register('webp-comments.webp', comments_webp(), 'webp-comments', 'none',
              'webp-com-chunk-carrier-v1')
    _register('webp-exif.webp', exif_webp(owned.exif_ifd0_tiff()), 'webp-exif', 'none',
              'webp-exif-orientation-artist-v1')
    tiff_a = owned.exif_ifd0_tiff()
    tiff_b = owned.exif_ifd0_tiff().replace(b'Alice', b'Bob  ')
    _register('webp-exif-dup.webp', exif_dup_webp(tiff_a, tiff_b), 'webp-exif',
              'multiple-independent', 'webp-two-exif-chunks-distinct-artists-v1')
    _register('webp-exif-thumb-jpeg.webp', exif_webp(owned.thumb_tiff(owned.thumb_jpeg_bytes(), True)),
              'exif-ifd1-jpeg', 'none', 'webp-exif-ifd1-jpeg-thumbnail-v1')
    _register('webp-exif-thumb-raw.webp', exif_webp(owned.thumb_tiff(RAW_STRIP, False)),
              'exif-ifd1-uncompressed', 'none', 'webp-exif-ifd1-uncompressed-thumbnail-v1')
    _register('webp-iccp.webp', iccp_webp(), 'webp-iccp', 'none', 'webp-iccp-single-v1')
    _register('webp-iccp-dup.webp', iccp_dup_webp(), 'webp-iccp', 'first-wins',
              'webp-iccp-two-byte-identical-v1')
    _register('webp-iptc.webp', iptc_webp(), 'webp-iptc', 'none', 'webp-iptc-chunk-carrier-v1')
    _register('webp-xmp.webp', xmp_webp(XMP_PKT_A), 'webp-xmp', 'none', 'webp-xmp-chunk-standard-v1')
    _register('webp-xmp-dup.webp', xmp_dup_webp(XMP_PKT_A2, XMP_PKT_B), 'webp-xmp',
              'last-wins', 'webp-xmp-two-chunks-different-creators-v1')
    _register('webp-xmp-nonutf8.webp', xmp_webp(XMP_PKT_NONUTF8), 'webp-xmp', 'none',
              'webp-xmp-non-utf8-payload-v1')
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
        encoder='None (authored byte-level writer; tools/reference/webp_owned_fixtures.py)',
        recipe=('Deterministic WebP (RIFF) writer: RIFF header + VP8X (flags/8x8 canvas) + '
                'authored metadata chunks + VP8 fake keyframe header (18 bytes). The VP8 '
                'payload is not a decodable frame: metadata operations never decode it and '
                'the pinned CLI rewrites it byte-for-byte (the B3 analog of the JPEG '
                'coded-scan invariant, verified at observation time). EXIF chunks carry '
                'classic II TIFF payloads (no Exif\\0\\0 prefix); ICCP carries a minimal '
                'synthetic RGB profile (never decodable real-world data); XMP rides the raw '
                'XMP chunk; the IPTC and COM chunks are undocumented carriers the pinned CLI '
                'does not surface (observed, never asserted). Pinned by '
                'tests/fixtures/webp/census-manifest.json; CI regenerates and compares SHA-256.'),
        inputRights=('MetadataSharp authored synthetic WebP fixtures, MIT; no imported images, '
                     'no imported codec code/tests, no upstream fixture corpus.'),
        fixtures=manifest())


def verify_dir(outdir):
    outdir = Path(outdir)
    pinned = {entry['file']: entry for entry in json.loads(MANIFEST.read_text())}
    missing = sorted(set(pinned) - {p.name for p in outdir.glob('*.webp')})
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
        outdir = args.outdir or (ROOT / 'artifacts/owned-webp')
        print(f'wrote {generate(outdir)} fixtures to {outdir}')
    elif args.command == 'verify':
        verify_dir(args.outdir or (ROOT / 'artifacts/owned-webp'))
    elif args.command == 'print-manifest':
        sys.stdout.write(json.dumps(manifest(), indent=2) + '\n')
    elif args.command == 'print-provenance':
        sys.stdout.write(json.dumps(provenance(), indent=2) + '\n')


if __name__ == '__main__':
    main()
