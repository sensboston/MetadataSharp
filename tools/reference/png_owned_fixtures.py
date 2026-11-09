"""Generate owned synthetic census PNG fixtures (B2 observation batch).

Our own deterministic stdlib writer; not derived from Exiv2, libpng, camera
samples, or any imported corpus. PNG chunks are framed exactly (length, type,
data, CRC-32) with an 8x8 8-bit truecolor baseline body. eXIf chunks carry
classic II TIFF payloads (same authored TIFF core as the JPEG fixtures, minus
the JPEG APP1 signature prefix, per the PNG eXIf convention). ICC profiles are
zlib-compressed into iCCP exactly as PNG requires (minimal synthetic RGB
profile, never decodable real-world data). IPTC rides the "Raw profile type
ipc" zTXt carrier the pinned CLI itself writes (hex-encoded IIM/8BIM payload);
XMP rides an uncompressed iTXt with keyword "XML:com.adobe.xmp". Deterministic;
CI regenerates and compares SHA-256 against tests/fixtures/png/manifest.json.

Usage: png_owned_fixtures.py generate OUTDIR   # write fixtures + manifest.json
       png_owned_fixtures.py verify OUTDIR     # fail if manifest pins drift
       png_owned_fixtures.py print-manifest    # emit pinned manifest JSON
       png_owned_fixtures.py print-provenance  # emit provenance JSON
"""
import argparse
import hashlib
import json
import struct
import sys
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / 'tests/fixtures/png/manifest.json'
PROVENANCE = ROOT / 'tests/fixtures/png/provenance.json'
sys.path.insert(0, str(Path(__file__).resolve().parent))
import jpeg_owned_fixtures as owned  # noqa: E402  (shared authored TIFF/XMP/ICC/IPTC cores)

SIG = b'\x89PNG\r\n\x1a\n'


def chunk(ctype, data):
    ctype = ctype if isinstance(ctype, bytes) else ctype.encode('ascii')
    return (struct.pack('>I', len(data)) + ctype + data
            + struct.pack('>I', zlib.crc32(ctype + data) & 0xFFFFFFFF))


def png(chunks):
    """chunks: fully-framed chunk byte strings (see chunk())."""
    width = height = 8
    ihdr = struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0)
    raw = b''.join(b'\x00' + b'\x80\x80\x80' * width for _ in range(height))
    body = SIG + chunk(b'IHDR', ihdr) + b''.join(chunks)
    body += chunk(b'IDAT', zlib.compress(raw)) + chunk(b'IEND', b'')
    return body


def text_chunk(keyword, text_bytes):
    return chunk(b'tEXt', keyword.encode('latin1') + b'\x00' + text_bytes)


def ztxt_chunk(keyword, text_bytes):
    return chunk(b'zTXt', keyword.encode('latin1') + b'\x00\x00' + zlib.compress(text_bytes))


def itxt(keyword, text_bytes):
    return chunk(b'iTXt', keyword.encode('latin1') + b'\x00\x00\x00\x00\x00' + text_bytes)


def iccp_chunk(profile, name):
    return chunk(b'iCCP', name + b'\x00\x00' + zlib.compress(profile))


def _iptc_raw_profile_raw(payloads):
    bim = b''.join(owned.bim_block(0x0404, b'', p) for p in payloads)
    return (b'\niptc\n     ' + str(len(bim)).encode('ascii') + b'\n'
            + bim.hex().encode('ascii') + b'\n')


def iptc_raw_profile_chunk(payloads):
    """Exiv2's raw-profile carrier: zTXt 'Raw profile type iptc' with a text
    blob '\\niptc\\n     <byte-length>\\n<hex of IIM/8BIM blocks>\\n'."""
    return chunk(b'zTXt', b'Raw profile type iptc\x00\x00' + zlib.compress(_iptc_raw_profile_raw(payloads)))


def iptc_raw_profile_itxt_chunk(payloads):
    """The same raw-profile text in an uncompressed iTXt carrier (spec-legal
    PNG; the pinned CLI rejects it with 'corrupted image metadata' - recorded)."""
    return itxt('Raw profile type iptc', _iptc_raw_profile_raw(payloads))


def xmp_itxt_chunk(packet):
    return itxt('XML:com.adobe.xmp', packet)


def xmp_ztxt_chunk(packet):
    return chunk(b'zTXt', b'XML:com.adobe.xmp\x00\x00' + zlib.compress(packet))


def exif_chunk(tiff):
    return chunk(b'eXIf', tiff)


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
    _register('png-base.png', png([]), 'container', 'none', 'png-base-v1')
    _register('png-text.png', png([text_chunk('Description', b'MetadataSharp PNG text'),
                                   ztxt_chunk('Comment', b'MetadataSharp PNG zTXt'),
                                   itxt('Comment', 'MetadataSharp iTXt é ✓'.encode('utf-8'))]),
              'png-text', 'none', 'png-text-ztxt-itxt-trio-v1')
    _register('png-exif.png', png([exif_chunk(owned.exif_ifd0_tiff())]),
              'png-exif', 'none', 'png-exif-orientation-artist-v1')
    _register('png-exif-thumb-jpeg.png', png([exif_chunk(owned.thumb_tiff(owned.thumb_jpeg_bytes(), True))]),
              'exif-ifd1-jpeg', 'none', 'png-exif-ifd1-jpeg-thumbnail-v1')
    _register('png-exif-thumb-raw.png', png([exif_chunk(owned.thumb_tiff(RAW_STRIP, False))]),
              'exif-ifd1-uncompressed', 'none', 'png-exif-ifd1-uncompressed-thumbnail-v1')
    _register('png-iccp.png', png([iccp_chunk(owned.PROFILE_A, b'MetadataSharp')]),
              'iccp', 'none', 'png-iccp-named-v1')
    _register('png-iccp-dup.png', png([iccp_chunk(owned.PROFILE_A, b'MetadataSharp'),
                                       iccp_chunk(owned.PROFILE_A, b'MetadataSharp')]),
              'iccp', 'first-wins', 'png-iccp-two-byte-identical-chunks-v1')
    _register('png-iptc.png', png([iptc_raw_profile_chunk([owned.IPTC_ALPHA + owned.IPTC_BETA])]),
              'ztxt-raw-profile-iptc', 'none', 'png-iptc-ztxt-raw-profile-alpha-beta-v1')
    _register('png-iptc-dup.png', png([iptc_raw_profile_chunk([owned.IPTC_ALPHA + owned.IPTC_BETA]),
                                       iptc_raw_profile_chunk([owned.IPTC_ALPHA + owned.IPTC_BETA])]),
              'ztxt-raw-profile-iptc', 'first-wins', 'png-iptc-two-byte-identical-ztxt-v1')
    _register('png-iptc-itxt.png', png([iptc_raw_profile_itxt_chunk([owned.IPTC_ALPHA + owned.IPTC_BETA])]),
              'itxt-raw-profile-iptc', 'none', 'png-iptc-itxt-raw-profile-alpha-beta-v1')
    _register('png-iptc-itxt-dup.png', png([iptc_raw_profile_itxt_chunk([owned.IPTC_ALPHA + owned.IPTC_BETA]),
                                            iptc_raw_profile_itxt_chunk([owned.IPTC_ALPHA + owned.IPTC_BETA])]),
              'itxt-raw-profile-iptc', 'first-wins', 'png-iptc-two-byte-identical-itxt-v1')
    _register('png-xmp.png', png([xmp_itxt_chunk(XMP_PKT_A)]),
              'itxt-xmp', 'none', 'png-xmp-itxt-standard-v1')
    _register('png-xmp-dup.png', png([xmp_itxt_chunk(XMP_PKT_A2), xmp_itxt_chunk(XMP_PKT_B)]),
              'itxt-xmp', 'first-wins', 'png-xmp-two-itxt-different-creators-v1')
    _register('png-xmp-nonutf8.png', png([xmp_itxt_chunk(XMP_PKT_NONUTF8)]),
              'itxt-xmp', 'none', 'png-xmp-itxt-non-utf8-payload-v1')
    _register('png-xmp-ztxt.png', png([xmp_ztxt_chunk(XMP_PKT_A)]),
              'ztxt-xmp', 'none', 'png-xmp-ztxt-standard-v1')
    _register('png-xmp-ztxt-dup.png', png([xmp_ztxt_chunk(XMP_PKT_A2), xmp_ztxt_chunk(XMP_PKT_B)]),
              'ztxt-xmp', 'first-wins', 'png-xmp-two-ztxt-different-creators-v1')
    _register('png-xmp-ztxt-nonutf8.png', png([xmp_ztxt_chunk(XMP_PKT_NONUTF8)]),
              'ztxt-xmp', 'none', 'png-xmp-ztxt-non-utf8-payload-v1')
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
        encoder='None (authored byte-level writer; tools/reference/png_owned_fixtures.py)',
        recipe=('Deterministic PNG writer: signature + IHDR (8x8 8-bit truecolor RGB) + '
                'owned chunks + IDAT (zlib of filter-0 scanlines) + IEND; every chunk carries '
                'a correct CRC-32. eXIf chunks carry classic II TIFF payloads without the '
                'JPEG APP1 prefix (PNG eXIf convention); iCCP carries a minimal synthetic RGB '
                'profile, zlib-compressed (never decodable real-world data); IPTC rides the '
                'zTXt "Raw profile type iptc" carrier the pinned CLI itself writes '
                '(hex-encoded IIM/8BIM payload in the raw-profile text format); XMP rides an '
                'uncompressed iTXt with keyword "XML:com.adobe.xmp". Pinned by '
                'tests/fixtures/png/manifest.json; CI regenerates and compares SHA-256.'),
        inputRights=('MetadataSharp authored synthetic PNG fixtures, MIT; no imported images, '
                     'no imported codec code/tests, no upstream fixture corpus.'),
        fixtures=manifest())


def verify_dir(outdir):
    outdir = Path(outdir)
    pinned = {entry['file']: entry for entry in json.loads(MANIFEST.read_text())}
    missing = sorted(set(pinned) - {p.name for p in outdir.glob('*.png')})
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
        outdir = args.outdir or (ROOT / 'artifacts/owned-png')
        print(f'wrote {generate(outdir)} fixtures to {outdir}')
    elif args.command == 'verify':
        verify_dir(args.outdir or (ROOT / 'artifacts/owned-png'))
    elif args.command == 'print-manifest':
        sys.stdout.write(json.dumps(manifest(), indent=2) + '\n')
    elif args.command == 'print-provenance':
        sys.stdout.write(json.dumps(provenance(), indent=2) + '\n')


if __name__ == '__main__':
    main()
