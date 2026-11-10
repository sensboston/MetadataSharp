"""B9 census fixtures: owned synthetic TIFF-derived RAW probes for wave 1.

Standard library only. Nothing here is a camera capture, a vendor sample or a
decodable RAW image: each fixture is a byte-authored TIFF/EP-rooted file carrying
the header contract the *pinned reference build* uses to dispatch a file to its
RAW reader, plus the metadata carriers this project already owns (authored EXIF
tags, XMP packet, raw IIM datasets, a minimal ICC profile, a chained IFD1 JPEG
thumbnail payload, XPComment and EXIF UserComment). No pixel data is decodable
and none is decoded.

Why these exact bytes (reviewed in docs/research/E01_B9_RIGHTS_AND_ORACLE_REVIEW.md):
the pinned Exiv2 0.28.9 source dispatches by header, and that is the part a
synthetic file can reproduce without a vendor specification.

* ``TiffHeaderBase::read`` (src/tiffimage_int.cpp at the pin) accepts 'II'/'MM',
  a uint16 at bytes 2-3 equal to 42 (or 444 JPEG-XR / 17234 DCP) and takes the
  root IFD offset from bytes 4-7. ARW, NEF, PEF and SRW share this reader
  (``ImageType::arw`` and friends all bind ``isTiffType`` in ``src/image.cpp``),
  and their vendor identity in the build comes from ``TiffImage::mimeType``'s
  compression mapping (32767 arw, 34713 nef, 32770 srw, 65535 pef), so those
  fixtures carry that Compression value.
* ``Cr2Header::read`` (src/cr2header_int.cpp) needs a 16-byte header spelling
  'CR\\2\\0' at bytes 8-11 and reads offset2 from bytes 12-15 (authored 0, as the
  reference writer's default does), with the root IFD offset still taken from
  bytes 4-7 and authored as 16.
* ``OrfHeader::read`` (src/orfimage_int.cpp) needs the uint16 at bytes 2-3 to be
  0x4f52 ('OR'; 0x5352 'SR' is also accepted) and the root IFD offset at 4-7, so
  an ORF header here is 'IIRO' + offset 8.

``structure`` re-derives that framing from the bytes, independently of the writer,
including ``recognizers``: a transcription of those three readers, so the offline
check proves a fixture says what it claims without running anything. That is a
container-framing oracle for our own authored bytes only - not a vendor RAW
layout specification, not pixel or MakerNote semantics, and not a support claim
about MetadataSharp or about Exiv2 beyond this recorded build.

Usage: b9_raw_fixtures.py verify     # offline; no Exiv2, no ExifTool, no PIL
       b9_raw_fixtures.py generate   # rewrite fixtures plus the byte-pinned manifest
"""
import hashlib
import json
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[2]
DIRECTORY = ROOT / 'tests/fixtures/b9'
MANIFEST = DIRECTORY / 'census-manifest.json'
sys.path.insert(0, str(Path(__file__).resolve().parent))
import jpeg_owned_fixtures as owned  # noqa: E402  (owned payloads: XMP, IIM, ICC, thumbnail)

T_BYTE, T_ASCII, T_SHORT, T_LONG, T_UNDEFINED = 1, 2, 3, 4, 7
SIZEOF = {T_BYTE: 1, T_ASCII: 1, T_SHORT: 2, T_LONG: 4, T_UNDEFINED: 1}
PIXEL = b'\x0a\x14\x1e'                        # shared owned single pixel, RGB
# The only tokens a probe may print. B9Comment rides in EXIF UserComment (ASCII, so it
# survives a stdout scan); the XPComment carrier is UTF-16LE and cannot be matched this
# way, which is the batch-B4 finding rather than a new one.
TOKENS = ('B9Artist', 'B9Tool', 'B9Comment')
COMMENT = 'MetadataSharp B9 comment \u00e9 \u2713'.encode('utf-16-le') + b'\x00\x00'
IIM = owned.IPTC_ALPHA + owned.IPTC_BETA
EXIF_VERSION, DATETIME = b'0231', b'2026:11:10 00:00:00\x00'
USER_COMMENT = b'ASCII\x00\x00\x00B9Comment'

# layout -> (format, file). Census rows name these layouts, the manifest pins
# these bytes and the observer probes these files, so all three agree by name.
FIXTURES = {
    'raw-cr2-ii': ('CR2', 'owned-marker-cr2.cr2'),
    'raw-orf-ii': ('ORF', 'owned-marker-orf.orf'),
    'raw-tiff-ep-ii-arw': ('ARW', 'owned-marker-arw.arw'),
    'raw-tiff-ep-ii-nef': ('NEF', 'owned-marker-nef.nef'),
    'raw-tiff-ep-ii-pef': ('PEF', 'owned-marker-pef.pef'),
    'raw-tiff-ep-ii-srw': ('SRW', 'owned-marker-srw.srw'),
}
MARKER_LAYOUTS = tuple(sorted(FIXTURES))
VENDOR_LAYOUTS = tuple(f'raw-{fmt.lower()}-vendor-private-unobserved'
                       for fmt in ('ARW', 'CR2', 'NEF', 'ORF', 'PEF', 'SRW'))

FORMATS = {
    'ARW': dict(ext='arw', header='tiff', tag=42, root=8, compression=32767,
                make='SONY', model='B9 SYNTHETIC ARW',
                identity='Compression 32767 maps to image/x-sony-arw in TiffImage::mimeType'),
    'CR2': dict(ext='cr2', header='cr2', tag=42, root=16, compression=1,
                make='CANON', model='B9 SYNTHETIC CR2',
                identity="header signature 'CR\\2\\0' selects Cr2Image; Compression stays 1"),
    'NEF': dict(ext='nef', header='tiff', tag=42, root=8, compression=34713,
                make='NIKON CORPORATION', model='B9 SYNTHETIC NEF',
                identity='Compression 34713 maps to image/x-nikon-nef in TiffImage::mimeType'),
    'ORF': dict(ext='orf', header='orf', tag=0x4f52, root=8, compression=1,
                make='OLYMPUS IMAGING CORP.', model='B9 SYNTHETIC ORF',
                identity="header tag 0x4f52 ('OR') selects OrfImage; Compression stays 1"),
    'PEF': dict(ext='pef', header='tiff', tag=42, root=8, compression=65535,
                make='PENTAX', model='B9 SYNTHETIC PEF',
                identity='Compression 65535 maps to image/x-pentax-pef in TiffImage::mimeType'),
    'SRW': dict(ext='srw', header='tiff', tag=42, root=8, compression=32770,
                make='SAMSUNG', model='B9 SYNTHETIC SRW',
                identity='Compression 32770 maps to image/x-samsung-srw in TiffImage::mimeType'),
}
# The vendor private IFD is the part a synthetic file cannot stand in for, so
# those census cells stay blocked instead of being called refused or absent.
VENDOR_DETAIL = {'ARW': 'Sony raw-image and lens tables in the private IFDs',
                 'CR2': 'Canon IFD3 color-metadata block',
                 'NEF': 'Nikon maker note and NEF verification tags',
                 'ORF': 'Olympus private IFD, whose offsets are relative to the directory',
                 'PEF': 'Pentax maker note and PENTAX/AE sections',
                 'SRW': 'Samsung OP body-data offsets'}
VENDOR_REASON = ('{fmt} private layout ({detail}): proprietary, with no independent normative '
                 'specification reviewed here and no rights-cleared sample. Authoring a '
                 'plausible-looking private IFD would invent evidence rather than observe it, '
                 'so no fixture is cleared and the cell stays visible as blocked. See '
                 'docs/research/E01_B9_RIGHTS_AND_ORACLE_REVIEW.md.')
RECIPE = 'authored byte-level vendor-headed TIFF/EP writer; tools/reference/b9_raw_fixtures.py'
# Data pool layout is declared, never derived from entry order, so the bytes are a
# function of this list and nothing else.
POOL_ORDER = ('bitspersample', 'make', 'model', 'software', 'artist', 'copyright', 'comment', 'icc',
              'iptc', 'xmp', 'datetime', 'usercomment', 'pixel', 'thumb')


def digest(data):
    return hashlib.sha256(data).hexdigest()


def vendor_reason(fmt):
    return VENDOR_REASON.format(fmt=fmt, detail=VENDOR_DETAIL[fmt])


# --------------------------------------------------------------------- writing
def _payloads(spec):
    return {'make': (spec['make'] + '\x00').encode('ascii'),
            'model': (spec['model'] + '\x00').encode('ascii'),
            'software': b'B9Tool\x00', 'artist': b'B9Artist\x00',
            'copyright': b'(c) 2026 MetadataSharp owned synthetic\x00',
            'comment': COMMENT, 'icc': owned.PROFILE_A, 'iptc': IIM,
            'xmp': owned.xmp_packet(), 'datetime': DATETIME,
            'usercomment': USER_COMMENT, 'pixel': PIXEL,
            'thumb': owned.thumb_jpeg_bytes(),
            # BitsPerSample is three SHORTs, so TIFF puts it in the data pool.
            'bitspersample': b'\x08\x00\x08\x00\x08\x00'}


def _ifds(spec):
    """(tag, type, kind, arg) entries in ascending tag order.

    kind 'lit' carries literal value bytes; 'name' a pool payload (inlined when it
    fits in four bytes, otherwise stored in the pool and referenced by offset);
    'off'/'len' the offset/length of a pool blob; 'ifd' the offset of a directory.
    """
    ifd0 = [(256, T_SHORT, 'lit', b'\x08\x00'), (257, T_SHORT, 'lit', b'\x08\x00'),
            (258, T_SHORT, 'name', 'bitspersample'),
            # The vendor marker the pinned build maps to this format's MIME type.
            (259, T_SHORT, 'lit', struct.pack('<H', spec['compression'])),
            (262, T_SHORT, 'lit', b'\x02\x00'),
            (271, T_ASCII, 'name', 'make'), (272, T_ASCII, 'name', 'model'),
            (273, T_LONG, 'off', 'pixel'), (274, T_SHORT, 'lit', b'\x01\x00'),
            (277, T_SHORT, 'lit', b'\x03\x00'), (278, T_SHORT, 'lit', b'\x08\x00'),
            (279, T_LONG, 'len', 'pixel'), (284, T_SHORT, 'lit', b'\x01\x00'),
            (305, T_ASCII, 'name', 'software'), (315, T_ASCII, 'name', 'artist'),
            (33432, T_ASCII, 'name', 'copyright'), (0x8769, T_LONG, 'ifd', 'ExifIFD'),
            (0x9C9C, T_BYTE, 'name', 'comment'), (0x8773, T_UNDEFINED, 'name', 'icc'),
            (0x83BB, T_BYTE, 'name', 'iptc'), (0x02BC, T_BYTE, 'name', 'xmp')]
    ifd1 = [(259, T_SHORT, 'lit', b'\x06\x00'), (262, T_SHORT, 'lit', b'\x02\x00'),
            (513, T_LONG, 'off', 'thumb'), (514, T_LONG, 'len', 'thumb')]
    exif = [(0x9000, T_BYTE, 'lit', EXIF_VERSION), (0x9003, T_ASCII, 'name', 'datetime'),
            (0x9286, T_BYTE, 'name', 'usercomment'), (0xA002, T_LONG, 'lit', struct.pack('<I', 640))]
    return [{'name': 'IFD0', 'entries': sorted(ifd0)}, {'name': 'IFD1', 'entries': sorted(ifd1)},
            {'name': 'ExifIFD', 'entries': sorted(exif)}]


def _header(spec):
    head = b'II' + struct.pack('<H', spec['tag']) + struct.pack('<I', spec['root'])
    if spec['header'] == 'cr2':
        head += b'CR\2\0' + struct.pack('<I', 0)   # signature, then offset2
    return head


def encode(fmt):
    """Assemble one fixture: header, IFD0, chained IFD1, EXIF sub-IFD, data pool.

    One sizing pass suffices because entry counts and types never depend on the
    resolved offsets: the directory sizes fix the pool base, the pool follows the
    declared POOL_ORDER, and every value longer than four bytes lives there.
    """
    spec = FORMATS[fmt]
    header, payloads, ifds = _header(spec), _payloads(spec), _ifds(spec)
    root = spec['root']
    if root < len(header):
        raise ValueError(f'{fmt}: root offset lies inside the header')
    cursor = root
    ifd_at = {}
    for ifd in ifds:
        ifd_at[ifd['name']] = cursor
        cursor += 2 + 12 * len(ifd['entries']) + 4
    data_at = {}
    for name in POOL_ORDER:
        data_at[name] = cursor
        cursor += (len(payloads[name]) + 1) & ~1
    file_length = cursor

    def value_for(kind, arg):
        if kind == 'lit':
            return arg
        if kind == 'name':
            return payloads[arg]
        if kind == 'off':
            return struct.pack('<I', data_at[arg])
        if kind == 'len':
            return struct.pack('<I', len(payloads[arg]))
        return struct.pack('<I', ifd_at[arg])

    out = bytearray(header) + bytes(root - len(header))
    for index, ifd in enumerate(ifds):
        out += struct.pack('<H', len(ifd['entries']))
        for tag, type_id, kind, arg in ifd['entries']:
            payload = value_for(kind, arg)
            count = len(payload) // SIZEOF[type_id]
            if len(payload) <= 4:
                field = payload.ljust(4, b'\0')
            elif kind == 'name':
                field = struct.pack('<I', data_at[arg])
            else:
                raise ValueError(f'{fmt}: tag {tag} carries a literal longer than four bytes; '
                                 'values that long must be named pool payloads')
            out += struct.pack('<HHI', tag, type_id, count) + field
        out += struct.pack('<I', ifd_at['IFD1'] if index == 0 else 0)
    for name in POOL_ORDER:
        assert len(out) == data_at[name], f'{fmt}: pool offset drift at {name}'
        out += payloads[name]
        if len(out) & 1:
            out += b'\0'
    assert len(out) == file_length, f'{fmt}: rendered length differs from the sizing pass'
    return bytes(out)


# ---------------------------------------------------------------------- oracle
def recognizers(data):
    """Transcription of the three pinned header readers, as an offline check.

    Mirrors ``TiffHeaderBase::read`` (src/tiffimage_int.cpp), ``Cr2Header::read``
    (src/cr2header_int.cpp) and ``OrfHeader::read`` (src/orfimage_int.cpp) at the
    pinned commit, including the accepted extra tags 444 (JPEG-XR) and 17234 (DCP)
    and the fact that ``ImageFactory::open`` returns the *first* registry match, so
    a plain-TIFF-header file is dispatched to the TIFF reader even when its name and
    Compression say ARW/NEF/PEF/SRW. Not a vendor specification.
    """
    if len(data) < 16:
        raise ValueError('fixture is shorter than the CR2 header contract')
    if not (data[0] == data[1] == 0x49):
        raise ValueError('authored fixtures are little-endian II only')
    tag = struct.unpack_from('<H', data, 2)[0]
    root = struct.unpack_from('<I', data, 4)[0]
    tiff_ok = tag in (42, 444, 17234)
    cr2_ok = tag == 42 and data[8:12] == b'CR\2\0'
    orf_ok = tag in (0x4f52, 0x5352)
    return {'byteOrder': 'littleEndian',
            'tiff': {'accepted': tiff_ok, 'tag': tag, 'rootOffset': root},
            'cr2': {'accepted': cr2_ok, 'tag': tag, 'rootOffset': root,
                    'signature': data[8:12].decode('latin-1'),
                    'offset2': struct.unpack_from('<I', data, 12)[0]},
            'orf': {'accepted': orf_ok, 'sig': f'0x{tag:04x}', 'rootOffset': root},
            'dispatch': 'cr2' if cr2_ok else 'orf' if orf_ok else 'tiff' if tiff_ok else 'none'}


def read_ifd(data, offset, name, seen):
    """Walk one directory by its own count field, resolving every value span."""
    if offset in seen:
        raise ValueError(f'{name}: IFD offset {offset} revisited')
    if offset + 2 > len(data):
        raise ValueError(f'{name}: IFD offset {offset} is out of range')
    seen.add(offset)
    count = struct.unpack_from('<H', data, offset)[0]
    entries, cursor, previous = [], offset + 2, 0
    for _ in range(count):
        if cursor + 12 > len(data):
            raise ValueError(f'{name}: truncated IFD entry')
        tag, type_id, values = struct.unpack_from('<HHI', data, cursor)
        if tag <= previous:
            raise ValueError(f'{name}: entries must be in ascending unique tag order')
        previous = tag
        size = SIZEOF.get(type_id)
        if size is None:
            raise ValueError(f'{name}: unauthored TIFF type {type_id} at tag {tag}')
        total = size * values
        field = data[cursor + 8:cursor + 12]
        if total <= 4:
            entry = dict(tag=tag, type=type_id, count=values, length=total, inline=True,
                         payload=field[:total])
        else:
            at = struct.unpack('<I', field)[0]
            if at + total > len(data):
                raise ValueError(f'{name}: tag {tag} payload runs past the file')
            entry = dict(tag=tag, type=type_id, count=values, length=total, inline=False,
                         payloadOffset=at, payload=data[at:at + total])
        entry['payloadSha256'] = digest(entry['payload'])
        entries.append(entry)
        cursor += 12
    if cursor + 4 > len(data):
        raise ValueError(f'{name}: missing next-IFD pointer')
    return {'name': name, 'offset': offset, 'end': cursor + 4, 'entryCount': count,
            'entries': entries, 'byTag': {e['tag']: e for e in entries},
            'nextOffset': struct.unpack_from('<I', data, cursor)[0]}


def name_of_tag(tag):
    return {273: 'strip', 513: 'thumbnail'}.get(tag, str(tag))


def structure(data):
    """Independent framing facts for one B9 fixture; raises if it is not self-consistent."""
    rec = recognizers(data)
    root = rec[rec['dispatch']]['rootOffset']
    seen = {root}
    ifd0 = read_ifd(data, root, 'IFD0', set())
    exif_at = struct.unpack('<I', ifd0['byTag'][0x8769]['payload'])[0]
    sub = read_ifd(data, exif_at, 'ExifIFD', seen)
    second = read_ifd(data, ifd0['nextOffset'], 'IFD1', seen)
    del seen
    dirs = [ifd0, sub, second]
    tags, blobs = ifd0['byTag'], {e['tag']: e['payload'] for e in sub['entries']}
    for tag, name in ((0x02BC, 'xmp'), (0x83BB, 'iptc'), (0x8773, 'icc'), (0x9C9C, 'comment')):
        expected = {'xmp': owned.xmp_packet(), 'iptc': IIM, 'icc': owned.PROFILE_A,
                    'comment': COMMENT}[name]
        if tags[tag]['payload'] != expected:
            raise ValueError(f'tag {tag} payload differs from the authored {name}')
    if blobs.get(0x9286) != USER_COMMENT:
        raise ValueError('EXIF UserComment differs from the authored payload')
    # Strip and thumbnail blobs are referenced by a 4-byte offset, so they are not
    # entries with a payload span of their own; their ranges are added explicitly.
    blob_refs = []
    for directory, off_tag, len_tag, expected in ((ifd0, 273, 279, PIXEL),
                                                   (second, 513, 514, owned.thumb_jpeg_bytes())):
        at = struct.unpack('<I', directory['byTag'][off_tag]['payload'])[0]
        length = struct.unpack('<I', directory['byTag'][len_tag]['payload'])[0]
        if data[at:at + length] != expected:
            raise ValueError(f'{name_of_tag(off_tag)} bytes differ from the owned payload')
        blob_refs.append((at, at + length))
    spans = sorted([(e['payloadOffset'], e['payloadOffset'] + e['length'])
                    for d in dirs for e in d['entries'] if not e['inline']] + blob_refs)
    for (_, end), (nxt, _) in zip(spans, spans[1:]):
        if end > nxt:
            raise ValueError('data pool payloads overlap')
        if nxt - end > 1 or (nxt > end and data[end] not in (0,)):
            raise ValueError('data pool carries an unexplained gap')
    directory_end = max(d['end'] for d in dirs)
    if spans and spans[0][0] < directory_end:
        raise ValueError('a payload offset points inside the directory area')
    # Even-length padding is the only byte a TIFF may append after the last value.
    last = max((end for _, end in spans), default=directory_end)
    if len(data) - last not in (0, 1) or (len(data) > last and data[last:] != b'\x00'):
        raise ValueError('trailing bytes are unaccounted for')
    return {'byteOrder': rec['byteOrder'], 'dispatch': rec['dispatch'], 'recognizers': rec,
            'headerBytes': 16 if rec['dispatch'] == 'cr2' else 8, 'rootOffset': root,
            'directoryCount': len(dirs),
            'directories': [{'name': d['name'], 'offset': d['offset'], 'entryCount': d['entryCount'],
                             'tags': [e['tag'] for e in d['entries']]} for d in dirs],
            'ifd0Tags': sorted(tags), 'exifTags': sorted(blobs),
            'compression': struct.unpack('<H', tags[259]['payload'])[0],
            'make': tags[271]['payload'].rstrip(b'\x00').decode('ascii'),
            'model': tags[272]['payload'].rstrip(b'\x00').decode('ascii'),
            'carrierTags': sorted(t for t in tags if t in (0x02BC, 0x83BB, 0x8773, 0x9C9C)),
            'thumbnailPresent': 513 in second['byTag'],
            'dataPoolSpans': len(spans), 'dataPoolEnd': last,
            'trailingPadBytes': len(data) - last,
            'fileLength': len(data), 'fileSha256': digest(data)}


def entries():
    result = []
    for layout in MARKER_LAYOUTS:
        fmt, name = FIXTURES[layout]
        facts = structure((DIRECTORY / name).read_bytes())
        result.append(dict(file=name, format=fmt, layout=layout, length=facts['fileLength'],
                           sha256=facts['fileSha256'], rights='owned-synthetic', recipe=RECIPE,
                           dispatch=facts['dispatch'], compression=facts['compression'],
                           vendorIdentity=FORMATS[fmt]['identity'], decodable=False,
                           metadata='authored-exif-xmp-iptc-icc-thumbnail-comments'))
    return result


def verify():
    """Re-derive every fixture from this module and check bytes plus framing."""
    for layout in MARKER_LAYOUTS:
        fmt, name = FIXTURES[layout]
        data = (DIRECTORY / name).read_bytes()
        if data != encode(fmt):
            raise ValueError(f'{name} differs from its authored bytes')
        facts, spec = structure(data), FORMATS[fmt]
        want = {'cr2': 'cr2', 'orf': 'orf'}.get(spec['header'], 'tiff')
        if facts['dispatch'] != want:
            raise ValueError(f'{name}: header does not carry the {fmt} recognition contract')
        if facts['compression'] != spec['compression']:
            raise ValueError(f'{name}: Compression differs from the authored vendor marker')
        if facts['rootOffset'] != spec['root']:
            raise ValueError(f'{name}: root IFD offset differs from the header contract')
        if facts['make'] != spec['make'] or facts['model'] != spec['model']:
            raise ValueError(f'{name}: Make/Model differ from the authored identity')
        if not facts['thumbnailPresent']:
            raise ValueError(f'{name}: the chained IFD1 thumbnail was lost')
        if facts['carrierTags'] != [0x02BC, 0x83BB, 0x8773, 0x9C9C]:
            raise ValueError(f'{name}: an authored IFD0 carrier tag is missing')
        if [d['name'] for d in facts['directories']] != ['IFD0', 'ExifIFD', 'IFD1']:
            raise ValueError(f'{name}: directory walk did not find IFD0, ExifIFD and IFD1')
    recorded = json.loads(MANIFEST.read_text(encoding='utf-8')) if MANIFEST.exists() else None
    if recorded != entries():
        raise ValueError('B9 census fixture manifest differs from the fixture bytes')
    return recorded


def main():
    if sys.argv[1:] == ['generate']:
        DIRECTORY.mkdir(parents=True, exist_ok=True)
        for fmt, spec in sorted(FORMATS.items()):
            (DIRECTORY / f'owned-marker-{fmt.lower()}.{spec["ext"]}').write_bytes(encode(fmt))
        MANIFEST.write_text(json.dumps(entries(), indent=2) + '\n', encoding='utf-8')
        print(f'wrote {len(FORMATS)} fixtures and {MANIFEST.name}')
    elif sys.argv[1:] != ['verify']:
        raise SystemExit('expected verify or generate')
    manifest = verify()
    print(f'B9 census fixture catalog verified: {len(manifest)} fixtures')


if __name__ == '__main__':
    main()
