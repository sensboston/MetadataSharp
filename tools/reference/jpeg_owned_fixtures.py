"""Generate owned synthetic census JPEG fixtures (B0 observation batch).

Our own deterministic stdlib writer; not derived from Exiv2, ExifTool, libjpeg,
camera samples, or any imported corpus. The 8x8 single-scan baseline body is an
independent re-implementation of the authored recipe also used by the C# JPEG
fixtures (tools/MetadataSharp.ReferenceFixture). EXIF TIFFs are classic II,
authored here from the TIFF 6.0 prose; ICC profiles are minimal and carry no
decodable real-world data (container-level observations only). Deterministic;
CI regenerates and compares SHA-256 against tests/fixtures/jpeg/manifest.json.

Usage: jpeg_owned_fixtures.py generate OUTDIR   # write fixtures + manifest.json
       jpeg_owned_fixtures.py verify OUTDIR     # fail if manifest pins drift
       jpeg_owned_fixtures.py print-manifest    # emit pinned manifest JSON
       jpeg_owned_fixtures.py print-provenance  # emit provenance JSON
"""
import argparse
import hashlib
import json
import struct
import sys
from pathlib import Path

T_BYTE, T_ASCII, T_SHORT, T_LONG = 1, 2, 3, 4
SIZEOF = {T_BYTE: 1, T_ASCII: 1, T_SHORT: 2, T_LONG: 4}
ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / 'tests/fixtures/jpeg/manifest.json'
PROVENANCE = ROOT / 'tests/fixtures/jpeg/provenance.json'

# ---------------------------------------------------------------- TIFF core
def pack_values(type_id, values, fmt='<'):
    if type_id == T_BYTE:
        return bytes(values)
    if type_id == T_ASCII:
        return bytes(values) if isinstance(values, (bytes, list)) else str(values).encode('utf-8')
    if type_id == T_SHORT:
        return struct.pack(f'{fmt}{len(values)}H', *values)
    if type_id == T_LONG:
        return struct.pack(f'{fmt}{len(values)}I', *values)
    raise AssertionError(type_id)


def _entry(tag, type_id, values, subifd=None):
    kind = len(values) if isinstance(values, list) else len(values)
    packed = pack_values(type_id, values)
    return (tag, type_id, kind, packed, subifd)


def build_tiff(ifds, chain=False):
    """classic II TIFF; ifds = list of entry lists; entries (tag, type, count,
    packed, subifd_index) where subifd_index points at the IFD holding the
    EXIF SubIFD (tag 0x8769, LONG, 1, value = that IFD offset). With
    chain=True each IFD's trailing pointer links to the next IFD (IFD0->IFD1,
    used for EXIF thumbnail fixtures); otherwise pointers are 0."""
    fmt = '<'
    for entries in ifds:
        entries.sort(key=lambda e: e[0])  # stable: duplicate tags keep authored order
    bodies = [2 + 12 * len(entries) + 4 for entries in ifds]
    offsets, off = [], 8
    for size in bodies:
        offsets.append(off)
        off += size
    data_base = off
    placements, pool_order = {}, []
    for i, entries in enumerate(ifds):
        for j, (_, _, _, packed, subifd) in enumerate(entries):
            if subifd is not None or len(packed) <= 4:
                continue
            placements[(i, j)] = data_base
            pool_order.append((i, j))
            data_base += (len(packed) + 1) & ~1
    out = bytearray(b'II' + struct.pack('<H', 42) + struct.pack('<I', offsets[0]))
    for i, entries in enumerate(ifds):
        out += struct.pack('<H', len(entries))
        for j, (tag, type_id, count, packed, subifd) in enumerate(entries):
            out += struct.pack('<HHI', tag, type_id, count)
            if subifd is not None:
                out += struct.pack('<I', offsets[subifd])
            elif len(packed) <= 4:
                out += packed.ljust(4, b'\0')
            else:
                out += struct.pack('<I', placements[(i, j)])
        out += struct.pack('<I', offsets[i + 1] if chain and i + 1 < len(ifds) else 0)
    for key in pool_order:
        i, j = key
        packed = ifds[i][j][3]
        assert len(packed) > 4 and len(out) == placements[key]
        out += packed
        if len(out) & 1:
            out += b'\0'
    return bytes(out)


def parse_tiff(exif):
    """Parse classic II TIFF from EXIF payload; returns list of IFD dicts with
    tag -> list of (type, count, value_bytes) and raw spans for byte checks."""
    def u16(p):
        return struct.unpack_from('<H', exif, p)[0]
    def u32(p):
        return struct.unpack_from('<I', exif, p)[0]
    if exif[:2] != b'II' or u16(2) != 42:
        raise ValueError('Only authored classic little-endian TIFF is supported')
    seen, stack = [], [u32(4)]
    first = True
    while stack:
        base = stack.pop()
        if base == 0 or base in [s[0] for s in seen]:
            continue
        count = u16(base)
        tags, entries, spans = {}, [], []
        for p in range(base + 2, base + 2 + 12 * count, 12):
            tag, type_id, n = u16(p), u16(p + 2), u32(p + 4)
            size = n * SIZEOF[type_id]
            off = p + 8 if size <= 4 else u32(p + 8)
            entries.append((tag, type_id, n, off, size))
            spans.append((p, p + 12))
            tags.setdefault(tag, []).append((type_id, n, exif[off:off + size]))
            if tag == 0x8769:
                stack.append(u32(p + 8))
        next_base = u32(base + 2 + 12 * count)
        if next_base:
            stack.append(next_base)
        seen.append((base, dict(count=count, tags=tags, spans=spans,
                                body=(base, base + 2 + 12 * count + 4))))
        first = False
    seen.sort(key=lambda item: item[0])
    return [item[1] for item in seen]

# ---------------------------------------------------------------- JPEG body
def _segment(marker, payload):
    return b'\xff' + bytes([marker]) + struct.pack('>H', len(payload) + 2) + payload


def _dqt():
    return _segment(0xDB, b'\0' + b'\x01' * 64)  # table 0, all-ones (authored)


def _dht():
    counts = [1] + [0] * 15                      # 1 symbol of length 1 (DC)
    symbols = [0x00, 0x10, 0x01] + [0x00] * 16   # authored symbol set
    return _segment(0xC4, bytes([0x00] + counts + symbols))


def _sof0():
    return _segment(0xC0, bytes([8, 0, 8, 0, 8, 1, 1, 0x11, 0]))


def _sos():
    return _segment(0xDA, bytes([1, 1, 0, 0, 63, 0]))


def jpeg_build(prefix):
    """SOI + authored prefix segments + minimal baseline body + EOI."""
    return b'\xff\xd8' + prefix + _dqt() + _dht() + _sof0() + _sos() + b'\x3f' + b'\xff\xd9'


def exif_segment(tiff):
    return _segment(0xE1, b'Exif\0\0' + tiff)


def comment_segment(payload):
    return _segment(0xFE, payload)


ICC_SIGNATURE = b'ICC_PROFILE\0'


def icc_segment(seq, count, chunk):
    return _segment(0xE2, ICC_SIGNATURE + bytes([seq, count]) + chunk)


# ---------------------------------------------------------------- ICC bodies
def build_icc_profile(color_space, description):
    """Minimal synthetic ICC RGB/GRAY display profile: header + 'desc' only.
    Not a usable color profile; exists for container-level observations."""
    desc = description.encode('ascii') + b'\0'
    desc_data = b'desc' + struct.pack('>I', len(desc)) + desc + b'\0\0\0\0' + b'\0\0\0\0' + b'\0\0\0\0'
    tag_count, record_size = 1, 12
    tag_table_off = 128
    data_off = tag_table_off + 4 + record_size * tag_count
    size = data_off + len(desc_data)
    header = bytearray(128)
    struct.pack_into('>I', header, 0, size)                     # profile size
    header[4:8] = b'SOBS'                                       # preferred CMM (arbitrary)
    struct.pack_into('>I', header, 8, 0x04300000)               # ICC version 4.3
    header[12:16] = b'mntr'                                     # display device class
    header[16:20] = color_space                                 # 'RGB ' / 'GRAY'
    header[20:24] = b'XYZ '                                     # PCS
    struct.pack_into('>6H', header, 24, 2026, 11, 8, 0, 0, 0)   # authored date/time
    header[36:40] = b'acsp'                                     # profile signature
    header[40:44] = b'SOBR'                                     # platform (arbitrary)
    struct.pack_into('>I', header, 64, 0)                       # attributes hi
    struct.pack_into('>I', header, 68, 0)                       # attributes lo
    struct.pack_into('>I', header, 72, 0)                       # rendering intent
    struct.pack_into('>3i', header, 76, 0x0000F6D6, 0x00010000, 0x0000D32D)  # D50 illuminant
    header[88:124] = b'MetadataSharp synthetic ICC authored fixture, not for real color transforms\0\0\0\0'[:36]
    header[124:128] = b'\0\0\0\0'
    out = bytes(header)
    out += struct.pack('>I', tag_count)
    out += b'desc' + struct.pack('>2I', data_off, len(desc_data))
    out += desc_data
    assert len(out) == size
    return out


PROFILE_A = build_icc_profile(b'RGB ', 'MetadataSharp ICC profile A')
PROFILE_B = build_icc_profile(b'GRAY', 'MetadataSharp ICC profile B')

# ------------------------------------------------------------- fixture set
def _ifd0_common(entries):
    return [_entry(274, T_SHORT, [1]), *entries]  # Orientation first under sort


def _artist(text):
    return _entry(315, T_ASCII, text + b'\0')


def _subifd_ptr(target_ifd):
    return (0x8769, T_LONG, 1, b'\0\0\0\0', target_ifd)


def _iso(value):
    return _entry(34855, T_SHORT, [value])


def exif_ifd0_tiff():
    return build_tiff([_ifd0_common([_artist(b'Alice')])])


def exif_dup_tag_tiff():
    return build_tiff([_ifd0_common([_artist(b'Alice'), _artist(b'Bob')])])


def _block(artist, with_subifd):
    if with_subifd:
        sub = [_iso(200)]
        return [_ifd0_common([_artist(artist), _subifd_ptr(1)]), sub]
    return [[*_ifd0_common([_artist(artist)])]]


def exif_subifd_tiff():
    return build_tiff(_block(b'Alice', True))


def exif_subifd_dup_tag_tiff():
    # duplicate SHORT tag inside the SubIFD (first-wins scenario, SubIFD level)
    sub = [_iso(100), _iso(400)]
    return build_tiff([_ifd0_common([_artist(b'Alice'), _subifd_ptr(1)]), sub])


def exif_subifd_multi_blocks():
    block1 = build_tiff(_block(b'Alice', True))
    block2 = build_tiff([_ifd0_common([_artist(b'Bob')])])
    return block1, block2


COMMENT_A = b'MetadataSharp B0 comment A'
COMMENT_B = b'MetadataSharp B0 comment B'

FIXTURES = {}


def _register(name, data, layout, duplicates, recipe):
    FIXTURES[name] = dict(bytes=data, layout=layout, duplicates=duplicates, recipe=recipe)


def _build_catalog():
    if FIXTURES:
        return FIXTURES
    _register('exif-base.jpg', jpeg_build(exif_segment(exif_ifd0_tiff())),
              'app1-exif-ifd0', 'none', 'jpeg-exif-ifd0-orientation-artist-v1')
    _register('exif-dup-tag.jpg', jpeg_build(exif_segment(exif_dup_tag_tiff())),
              'app1-exif-ifd0', 'first-wins', 'jpeg-exif-ifd0-duplicate-artist-v1')
    _register('exif-multi-app1.jpg', jpeg_build(exif_segment(build_tiff([_ifd0_common([_artist(b'Alice')])])) +
                                                exif_segment(build_tiff([_ifd0_common([_artist(b'Bob')])]))),
              'app1-exif-ifd0', 'multiple-independent', 'jpeg-exif-two-app1-independent-v1')
    _register('exif-subifd.jpg', jpeg_build(exif_segment(exif_subifd_tiff())),
              'app1-exif-ifd0-subifd', 'none', 'jpeg-exif-subifd-iso-v1')
    _register('exif-subifd-dup-tag.jpg', jpeg_build(exif_segment(exif_subifd_dup_tag_tiff())),
              'app1-exif-ifd0-subifd', 'first-wins', 'jpeg-exif-subifd-duplicate-iso-v1')
    block1, block2 = exif_subifd_multi_blocks()
    _register('exif-subifd-multi.jpg', jpeg_build(exif_segment(block1) + exif_segment(block2)),
              'app1-exif-ifd0-subifd', 'multiple-independent', 'jpeg-exif-subifd-two-app1-v1')
    _register('com-none.jpg', jpeg_build(b''),
              'com-single', 'none', 'jpeg-com-none-v1')
    _register('com-single.jpg', jpeg_build(comment_segment(COMMENT_A)),
              'com-single', 'none', 'jpeg-com-single-a-v1')
    _register('com-multiple.jpg', jpeg_build(comment_segment(COMMENT_A) + comment_segment(COMMENT_B)),
              'com-multiple', 'none', 'jpeg-com-multiple-distinct-v1')
    _register('com-multiple-dup.jpg', jpeg_build(comment_segment(COMMENT_A) + comment_segment(COMMENT_A)),
              'com-multiple', 'first-wins', 'jpeg-com-multiple-identical-v1')
    half_a, half_b = len(PROFILE_A) // 2, len(PROFILE_B) // 2
    _register('icc-single.jpg', jpeg_build(icc_segment(1, 1, PROFILE_A)),
              'app2-icc-single', 'none', 'jpeg-icc-single-segment-a-v1')
    _register('icc-single-dup.jpg', jpeg_build(icc_segment(1, 1, PROFILE_A) + icc_segment(1, 1, PROFILE_B)),
              'app2-icc-single', 'first-wins', 'jpeg-icc-two-single-segment-profiles-v1')
    _register('icc-segmented.jpg', jpeg_build(icc_segment(1, 2, PROFILE_A[:half_a]) +
                                              icc_segment(2, 2, PROFILE_A[half_a:])),
              'app2-icc-segmented', 'none', 'jpeg-icc-two-segment-a-v1')
    _register('icc-segmented-dup.jpg', jpeg_build(icc_segment(1, 2, PROFILE_A[:half_a]) +
                                                  icc_segment(2, 2, PROFILE_A[half_a:]) +
                                                  icc_segment(1, 2, PROFILE_B[:half_b]) +
                                                  icc_segment(2, 2, PROFILE_B[half_b:])),
              'app2-icc-segmented', 'first-wins', 'jpeg-icc-two-two-segment-profiles-v1')
    _register('xmp-none.jpg', jpeg_build(b''),
              'app1-xmp-standard', 'none', 'jpeg-xmp-none-v1')
    _register('xmp-standard.jpg', jpeg_build(xmp_segment(xmp_packet())),
              'app1-xmp-standard', 'none', 'jpeg-xmp-standard-packet-v1')
    ext_doc = xmp_extended_doc()
    half = len(ext_doc) // 2
    _register('xmp-extended.jpg', jpeg_build(xmp_segment(xmp_base_with_extended()) +
                                             xmp_extension_segment(0, ext_doc[:half], len(ext_doc)) +
                                             xmp_extension_segment(half, ext_doc[half:], len(ext_doc))),
              'app1-xmp-extended', 'none', 'jpeg-xmp-extended-base-plus-2-chunks-v1')
    _register('xmp-duplicate.jpg', jpeg_build(xmp_segment(xmp_packet().replace(b'MetadataSharp</xmp:CreatorTool>',
                                                                               b'MetadataSharpA</xmp:CreatorTool>')) +
                                              xmp_segment(xmp_packet().replace(b'MetadataSharp</xmp:CreatorTool>',
                                                                               b'MetadataSharpB</xmp:CreatorTool>'))),
              'app1-xmp-standard', 'first-wins', 'jpeg-xmp-two-app1-different-creators-v1')
    _register('xmp-extended-duplicate.jpg', jpeg_build(xmp_segment(xmp_base_with_extended()) +
                                                      xmp_segment(xmp_base_with_extended()) +
                                                      xmp_extension_segment(0, ext_doc[:half], len(ext_doc)) +
                                                      xmp_extension_segment(half, ext_doc[half:], len(ext_doc))),
              'app1-xmp-extended', 'first-wins', 'jpeg-xmp-extended-two-base-one-chunk-set-v1')
    _register('xmp-extended-nonutf8.jpg', jpeg_build(xmp_segment(b'\x89RAW\xff\xfe not parseable xmp') +
                                                    xmp_extension_segment(0, b'\x89EXT-chunk-1\xff\xfe', 16) +
                                                    xmp_extension_segment(8, b'\x89EXT-chunk-2\xff\xfe', 16)),
              'app1-xmp-extended', 'none', 'jpeg-xmp-extended-non-utf8-base-and-chunks-v1')
    _register('xmp-nonutf8.jpg', jpeg_build(xmp_segment(b'\x89\xfe\xffRAW not xml')),
              'app1-xmp-standard', 'none', 'jpeg-xmp-non-utf8-payload-v1')
    _register('iptc-none.jpg', jpeg_build(b''),
              'app13-8bim-iptc', 'none', 'jpeg-iptc-none-v1')
    _register('iptc-single.jpg', jpeg_build(app13_iptc([IPTC_ALPHA + IPTC_BETA])),
              'app13-8bim-iptc', 'none', 'jpeg-iptc-single-8bim-unique-v1')
    _register('iptc-single-dup-dataset.jpg', jpeg_build(app13_iptc([IPTC_DUP + IPTC_DUP])),
              'app13-8bim-iptc', 'first-wins', 'jpeg-iptc-single-8bim-repeated-dataset-v1')
    _register('iptc-multiple.jpg', jpeg_build(app13_iptc([IPTC_ALPHA]) + app13_iptc([IPTC_BETA])),
              'app13-8bim-iptc-multiple', 'none', 'jpeg-iptc-two-app13-unique-v1')
    _register('iptc-multiple-dup.jpg', jpeg_build(app13_iptc([IPTC_DUP]) + app13_iptc([IPTC_DUP])),
              'app13-8bim-iptc-multiple', 'first-wins', 'jpeg-iptc-two-app13-shared-dataset-v1')
    raw_strip = bytes(i % 256 for i in range(192))
    _register('exif-thumb-jpeg.jpg', jpeg_build(exif_segment(thumb_tiff(thumb_jpeg_bytes(), True))),
              'exif-ifd1-jpeg', 'none', 'jpeg-exif-ifd1-jpeg-thumbnail-v1')
    _register('exif-thumb-raw.jpg', jpeg_build(exif_segment(thumb_tiff(raw_strip, False))),
              'exif-ifd1-uncompressed', 'none', 'jpeg-exif-ifd1-uncompressed-thumbnail-v1')
    _register('com-raw.jpg', jpeg_build(comment_segment(RAW_COM_A)),
              'com-single', 'none', 'jpeg-com-raw-high-bytes-v1')
    _register('com-raw-multiple.jpg', jpeg_build(comment_segment(RAW_COM_A) + comment_segment(RAW_COM_B)),
              'com-multiple', 'none', 'jpeg-com-multiple-raw-distinct-v1')
    _register('com-raw-multiple-dup.jpg', jpeg_build(comment_segment(RAW_COM_A) + comment_segment(RAW_COM_A)),
              'com-multiple', 'first-wins', 'jpeg-com-multiple-raw-identical-v1')
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
        encoder='None (authored byte-level writer; tools/reference/jpeg_owned_fixtures.py)',
        recipe=('Deterministic classic-II-TIFF/EXIF/COM/APP1-XMP/APP13-IPTC/ICC-in-JPEG writer: '
                'SOI + owned segments + 8x8 single-scan baseline body (DQT/DHT/SOF0/SOS, one entropy byte) + EOI; '
                'EXIF IFD0 carries Orientation and ASCII Artist; SubIFD fixtures carry tag 0x8769; '
                'EXIF IFD1 carries the embedded thumbnail (IFD0->IFD1 next-IFD chain, inline patched '
                'strip/JPEG blob offsets); COM payloads are opaque bytes as stored (incl. non-UTF-8 high-byte '
                'variants); XMP fixtures carry standard APP1 packets, base+2 extension chunks (Extended XMP) '
                'and non-UTF-8/duplicate/undecodable variants; IPTC fixtures carry Photoshop 8BIM 0x0404 '
                'resources with repeatable 2:25 keywords (single and two-APP13-segment variants); '
                'ICC profiles are minimal synthetic RGB/GRAY display profiles (header + desc tag only), '
                'never decodable real profiles. '
                'Pinned by tests/fixtures/jpeg/manifest.json; CI regenerates and compares SHA-256.'),
        inputRights=('MetadataSharp authored synthetic JPEG fixtures, MIT; no imported images, '
                     'no imported codec code/tests, no upstream fixture corpus; the 8x8 baseline body '
                     'independently re-implements the authored recipe used by the C# JPEG fixtures.'),
        fixtures=manifest())


def verify_dir(outdir):
    outdir = Path(outdir)
    pinned = {entry['file']: entry for entry in json.loads(MANIFEST.read_text())}
    missing = sorted(set(pinned) - {p.name for p in outdir.glob('*.jpg')})
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
    return len(FIXTURES) if FIXTURES else len(_build_catalog())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('command', choices=['generate', 'verify', 'print-manifest', 'print-provenance'])
    ap.add_argument('outdir', nargs='?')
    args = ap.parse_args()
    if args.command == 'generate':
        outdir = args.outdir or (ROOT / 'artifacts/owned-jpeg')
        print(f'wrote {generate(outdir)} fixtures to {outdir}')
    elif args.command == 'verify':
        verify_dir(args.outdir or (ROOT / 'artifacts/owned-jpeg'))
    elif args.command == 'print-manifest':
        sys.stdout.write(json.dumps(manifest(), indent=2) + '\n')
    elif args.command == 'print-provenance':
        sys.stdout.write(json.dumps(provenance(), indent=2) + '\n')


# ------------------------------------------------- XMP / IPTC / thumbnail / raw COM
XMP_SIGNATURE = b'http://ns.adobe.com/xap/1.0/\x00'
XMP_EXT_SIGNATURE = b'http://ns.adobe.com/xmp/extension/\x00'
XMP_EXT_GUID = b'B0B0B0B0000000000000000000000001'


def xmp_packet():
    return (b'<?xpacket begin="\xef\xbb\xbf" id="W5M0MpCehiHzreSzNTczkc9d"?>'
            b'<x:xmpmeta xmlns:x="adobe:ns:meta/" x:xmptk="MetadataSharp">'
            b'<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">'
            b'<rdf:Description rdf:about="" xmlns:xmp="http://ns.adobe.com/xap/1.0/">'
            b'<xmp:CreatorTool>MetadataSharp</xmp:CreatorTool>'
            b'</rdf:Description></rdf:RDF></x:xmpmeta>'
            b'<?xpacket end="w"?>')


def xmp_extended_doc():
    return (b'<x:xmpmeta xmlns:x="adobe:ns:meta/">'
            b'<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">'
            b'<rdf:Description rdf:about="" xmlns:xmp="http://ns.adobe.com/xap/1.0/" '
            b'xmlns:dc="http://purl.org/dc/elements/1.1/">'
            b'<xmp:ModifyDate>2026-11-08T00:00:00</xmp:ModifyDate>'
            b'<dc:creator><rdf:Seq><rdf:li>MetadataSharp</rdf:li></rdf:Seq></dc:creator>'
            b'</rdf:Description></rdf:RDF></x:xmpmeta>')


def xmp_base_with_extended():
    return (b'<?xpacket begin="\xef\xbb\xbf" id="W5M0MpCehiHzreSzNTczkc9d"?>'
            b'<x:xmpmeta xmlns:x="adobe:ns:meta/">'
            b'<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">'
            b'<rdf:Description rdf:about="" xmlns:xmpNote="http://ns.adobe.com/xmp/extension/">'
            b'<xmpNote:HasExtendedXMP>' + XMP_EXT_GUID + b'</xmpNote:HasExtendedXMP>'
            b'</rdf:Description></rdf:RDF></x:xmpmeta>'
            b'<?xpacket end="w"?>')


def xmp_segment(packet):
    return _segment(0xE1, XMP_SIGNATURE + packet)


def xmp_extension_segment(offset, chunk, full_size):
    return _segment(0xE1, XMP_EXT_SIGNATURE + XMP_EXT_GUID +
                    struct.pack('>II', full_size, offset) + chunk)


IPTC_RECORD, IPTC_KEYWORDS = 2, 25  # 2:25 keywords, repeatable dataset (authored)


def iptc_dataset(record, dataset_id, data):
    size = len(data)
    assert size < 0x8000
    return bytes([0x1C, record, dataset_id]) + struct.pack('>H', size) + data


def iptc_keyword(text):
    return iptc_dataset(IPTC_RECORD, IPTC_KEYWORDS, text.encode('ascii'))


def bim_block(resource_id, name, data):
    pascal = bytes([len(name)]) + name
    if len(pascal) % 2:
        pascal += b'\0'
    out = b'8BIM' + struct.pack('>H', resource_id) + pascal + struct.pack('>I', len(data)) + data
    if len(out) % 2:
        out += b'\0'
    return out


IPTC_RESOURCE_ID = 0x0404  # IPTC/NAA resource id in Photoshop 8BIM blocks


def app13_iptc(payloads):
    body = b''.join(bim_block(IPTC_RESOURCE_ID, b'', p) for p in payloads)
    return _segment(0xED, b'Photoshop 3.0\x00' + body)


IPTC_ALPHA = iptc_keyword('MetadataSharpAlpha')
IPTC_BETA = iptc_keyword('MetadataSharpBeta')
IPTC_DUP = iptc_keyword('MetadataSharpDup')


def thumb_tiff(blob, compressed):
    """IFD0 (Orientation) chained to IFD1 (EXIF thumbnail); pool after bodies;
    the thumbnail blob is the only external value (offsets computed exactly)."""
    ifd0 = [[_entry(274, T_SHORT, [1])]]
    orientation = ifd0[0]
    if compressed:
        ifd1 = [_entry(259, T_SHORT, [6]), _entry(262, T_SHORT, [2]),
                (513, T_LONG, 1, b'\0\0\0\0', None),
                _entry(514, T_LONG, [len(blob)])]
    else:
        ifd1 = [_entry(256, T_SHORT, [8]), _entry(257, T_SHORT, [8]),
                _entry(259, T_SHORT, [1]), _entry(262, T_SHORT, [2]),
                (273, T_LONG, 1, b'\0\0\0\0', None),
                _entry(277, T_SHORT, [3]), _entry(278, T_SHORT, [8]),
                _entry(279, T_LONG, [len(blob)])]
    bodies = [2 + 12 * len(orientation) + 4, 2 + 12 * len(ifd1) + 4]
    ifd0_off, ifd1_off = 8, 8 + bodies[0]
    pool = ifd1_off + bodies[1]
    blob_off = pool
    patch_tag = 513 if compressed else 273
    for j, (tag, type_id, count, packed, subifd) in enumerate(ifd1):
        if tag == patch_tag:
            ifd1[j] = (tag, type_id, count, struct.pack('<I', blob_off), None)
    return build_tiff([orientation, ifd1], chain=True) + blob


THUMB_JPEG = None  # filled lazily to reuse the authored baseline body


def thumb_jpeg_bytes():
    global THUMB_JPEG
    if THUMB_JPEG is None:
        THUMB_JPEG = jpeg_build(b'')
    return THUMB_JPEG


RAW_COM_A = b'\x89\xfe\xffRAW high-byte comment A'
RAW_COM_B = b'\x89\xfe\xffRAW high-byte comment B'

if __name__ == '__main__':
    main()
