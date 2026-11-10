"""B10 census fixtures: owned synthetic TIFF-derived RAW probes for wave 2.

Standard library only. Nothing here is a camera capture, a vendor sample or a
decodable RAW image. Each fixture is a byte-authored container carrying exactly the
header contract the *pinned reference build* uses to recognise and read this format,
plus the metadata carriers this project already owns (authored EXIF tags, XMP packet,
raw IIM datasets, a minimal ICC profile, an owned JPEG preview, a chained IFD1
thumbnail payload). No pixel data is decodable and none is decoded.

Why these exact bytes (reviewed in
docs/research/E01_B10_RIGHTS_AND_ORACLE_REVIEW.md): the pinned Exiv2 0.28.9 source
recognises and reads these formats through small, fully readable header routines, and
those routines are the only specification this batch reviewed. No vendor layout
specification was read, so nothing here claims to be a valid capture.

* MRW -- ``isMrwType`` (src/mrwimage.cpp:128) requires the file to start ``\\0MRM``;
  ``MrwImage::readMetadata`` (src/mrwimage.cpp:60-112) takes the block bound from the
  big-endian uint32 at bytes 4-7, walks 8-byte block headers while
  ``memcmp(tmp + 1, "TTW", 3)`` fails, and hands the ``TTW`` payload to
  ``TiffParser::decode``. The metadata of an MRW *is* an embedded TIFF, and every
  ``enforce`` on that walk is reproducible here.
* RW2 -- ``Rw2Header`` is ``TiffHeaderBase(0x0055, 24, littleEndian, 0x18)``
  (src/rw2image_int.cpp:6) and ``isRw2Type`` reads 24 bytes through it
  (src/rw2image.cpp:203). Because ``Image::isTiffType`` demands tag 42, a file with
  magic 85 is *not* claimed by the earlier ``tiff`` registry entry, so ``Rw2Image`` --
  and its ``RW2 IMAGE`` structure print -- is what the CLI reaches.
* RAF -- ``isRafType`` requires ``FUJIFILM`` at offset 0 (src/rafimage.cpp:339);
  ``RafImage::readMetadata`` (src/rafimage.cpp:224-320) reads big-endian uint32 pairs
  at 84/88 (preview JPEG, whose Exif/IPTC/XMP *and* comment are copied), 92/96 (a
  block the build only ever prints) and 100/104 (an embedded TIFF gated on ``II*\\0``
  or ``MM\\0*``), then injects ``Exif.Image2.JPEGInterchangeFormat{,Length}``;
  ``RafImage::printStructure`` (src/rafimage.cpp:65-222) slices 16 bytes at 0, 4 at
  16, 8 at 20, 32 at 28, 4 at 60 and 20 at 64, so those regions are observable text.
* SR2 -- registered as ``newTiffInstance``/``isTiffType`` (src/image.cpp:91) and absent
  from ``TiffImage::mimeType``'s ``mimeTypeList``, so this build has no SR2-specific
  observable. Its fixture is a plain generic-TIFF marker whose purpose is to record
  that absence, and it must carry no vendor compression value.

``recognizers`` transcribes those readers *and* the registry's first-match order, so the
offline check proves each fixture reaches the class it claims (and, for SR2, that it
reaches only the generic TIFF reader). It rejects the two wildcard magics 444 and 17234,
which every TIFF-family header in this build accepts: passing on those would prove
nothing about the format being claimed. This is a container-framing oracle for our own
authored bytes only -- not a vendor RAW layout specification, not pixel or MakerNote
semantics, and not a support claim about MetadataSharp or about Exiv2 beyond this
recorded build.

Usage: b10_raw_fixtures.py verify     # offline; no Exiv2, no ExifTool, no PIL
       b10_raw_fixtures.py generate   # rewrite fixtures plus the byte-pinned manifest
"""
import hashlib
import json
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[2]
DIRECTORY = ROOT / 'tests/fixtures/b10'
MANIFEST = DIRECTORY / 'census-manifest.json'
sys.path.insert(0, str(Path(__file__).resolve().parent))
import jpeg_owned_fixtures as owned  # noqa: E402  (owned payloads: XMP, IIM, ICC, JPEG builder)

T_BYTE, T_ASCII, T_SHORT, T_LONG, T_UNDEFINED = 1, 2, 3, 4, 7
SIZEOF = {T_BYTE: 1, T_ASCII: 1, T_SHORT: 2, T_LONG: 4, T_UNDEFINED: 1}
PIXEL = b'\x0a\x14\x1e'                        # shared owned single pixel, RGB
# The only tokens a B10 probe may print. RAF's comment rides in the embedded JPEG's COM
# segment, not in an EXIF tag, because that is the field RafImage::readMetadata copies;
# the TIFF-side XPComment carrier that batch B4 established cannot be matched on stdout.
TOKENS = ('B10Artist', 'B10Tool')
RAF_COMMENT = b'B10RafComment'
COMMENT = 'MetadataSharp B10 comment \u00e9 \u2713'.encode('utf-16-le') + b'\x00\x00'
IIM = owned.IPTC_ALPHA + owned.IPTC_BETA
EXIF_VERSION, DATETIME = b'0231', b'2026:11:10 00:00:00\x00'
# TiffImage::mimeType's compression map at the pin (src/tiffimage.cpp:54). A B10
# fixture may use none of these: they would borrow a wave-1 identity.
MIME_COMPRESSIONS = (32767, 32769, 32770, 34713, 65000, 65535)
# The tag values TiffHeaderBase::read accepts from *every* TIFF-family header, so a
# fixture built on one of them would not be evidence about the format it names.
WILDCARD_MAGICS = (444, 17234)
# carrier name -> (tag, type, payload). The tag numbers are the ones the generic TIFF
# mapping uses, which is what the MRW and RAF embedded blobs are parsed by.
CARRIERS = {'icc': (0x8773, T_UNDEFINED, owned.PROFILE_A),
            'iptc': (0x83BB, T_BYTE, IIM),
            'xmp': (0x02BC, T_BYTE, owned.xmp_packet()),
            'comment': (0x9C9C, T_BYTE, COMMENT)}
ALL_CARRIERS = ('icc', 'iptc', 'xmp', 'comment')

FIXTURES = {
    'raw-mrw-ttw-embedded-tiff': ('MRW', 'owned-mrw-ttw.mrw'),
    'raw-raf-fujifilm-be-offsets': ('RAF', 'owned-raf-fujifilm.raf'),
    'raw-rw2-ii-55-root-24': ('RW2', 'owned-rw2-root24.rw2'),
    'raw-tiff-ep-ii-sr2': ('SR2', 'owned-marker-sr2.sr2'),
}
MARKER_LAYOUTS = tuple(sorted(FIXTURES))
VENDOR_LAYOUTS = tuple(f'raw-{fmt.lower()}-vendor-private-unobserved'
                       for fmt in ('MRW', 'RAF', 'RW2', 'SR2'))

FORMATS = {
    'MRW': dict(ext='mrw', container='mrw', make='MINOLTA', model='B10 SYNTHETIC MRW',
                carriers=('icc', 'iptc', 'xmp', 'comment'), thumbnail=True,
                identity='\\0MRM header plus a \\0TTW block holding a whole TIFF; '
                        'MrwImage::mimeType is the constant image/x-minolta-mrw'),
    'RAF': dict(ext='raf', container='raf', make='FUJIFILM', model='B10 SYNTHETIC RAF',
                carriers=('icc',), thumbnail=False,
                identity='FUJIFILM magic with big-endian block pairs at 84/88, 92/96 and '
                        '100/104; RafImage::mimeType is image/x-fuji-raf'),
    'RW2': dict(ext='rw2', container='rw2', make='PANASONIC', model='B10 SYNTHETIC RW2',
                carriers=('icc', 'iptc', 'xmp', 'comment'), thumbnail=True,
                identity='magic 0x0055, 24-byte header, root IFD at 24 '
                        '(TiffHeaderBase(0x0055, 24, littleEndian, 0x18)); '
                        'Rw2Image::mimeType is image/x-panasonic-rw2'),
    # No vendor marker is authored for SR2 on purpose: this build has no SR2 entry in
    # mimeTypeList and no SR2 reader, so any such byte would be an invention.
    'SR2': dict(ext='sr2', container='tiff', make='SONY', model='B10 SYNTHETIC SR2 MARKER',
                carriers=('icc', 'iptc', 'xmp', 'comment'), thumbnail=True,
                identity='generic TIFF dispatch only; no compression value in this build '
                        'maps to an SR2 MIME type, so mimeType stays image/tiff'),
}
VENDOR_DETAIL = {'MRW': 'the \\0DAT raw picture block and the camera-private data it holds',
                 'RAF': 'the offset-92 structure the reader itself labels "proprietary" (its '
                        'comment cites third-party documentation that was not reviewed)',
                 'RW2': 'the raw image data and the Panasonic-private tag semantics behind the '
                        'Tag::pana mapping',
                 'SR2': 'the CT/SATD/FMT directory structures, which nothing in this build '
                        'reads, and the Sony maker-note tables batch B12 owns'}
VENDOR_REASON = ('{fmt} private layout ({detail}): proprietary, with no independent normative '
                 'specification reviewed here and no rights-cleared sample. Authoring a '
                 'plausible-looking private block would invent evidence rather than observe it, '
                 'so no fixture is cleared for it and the cell stays visible as blocked. See '
                 'docs/research/E01_B10_RIGHTS_AND_ORACLE_REVIEW.md.')
RECIPE = ('authored byte-level wave-2 container writer (MRW block chain, RAF big-endian offset '
          'table, RW2 0x0055 header, generic TIFF marker); tools/reference/b10_raw_fixtures.py')
# Data pool layout is declared, never derived from entry order, so the bytes are a
# function of this list and nothing else.
POOL_ORDER = ('make', 'model', 'software', 'artist', 'copyright', 'comment', 'icc',
              'iptc', 'xmp', 'datetime', 'pixel', 'thumb')


def digest(data):
    return hashlib.sha256(data).hexdigest()


def vendor_reason(fmt):
    return VENDOR_REASON.format(fmt=fmt, detail=VENDOR_DETAIL[fmt])


# --------------------------------------------------------------------- writing
def payload_for(fmt, name):
    """One owned payload by name; the pool is built from what a directory references.

    Deriving the pool from the entries rather than from a fixed list means a payload that
    nothing points at cannot ride along (batch B9 caught exactly that as a stray
    BitsPerSample span), and a reference to something unauthored is a construction error
    here instead of a gap the reviewer has to explain later.
    """
    spec = FORMATS[fmt]
    table = {'make': (spec['make'] + '\x00').encode('ascii'),
             'model': (spec['model'] + '\x00').encode('ascii'),
             'software': b'B10Tool\x00', 'artist': b'B10Artist\x00',
             'copyright': b'(c) 2026 MetadataSharp owned synthetic\x00',
             'datetime': DATETIME, 'pixel': PIXEL, 'thumb': owned.thumb_jpeg_bytes(),
             'comment': COMMENT, 'icc': owned.PROFILE_A, 'iptc': IIM,
             'xmp': owned.xmp_packet()}
    if name not in table:
        raise ValueError(f'{fmt}: {name} is not an owned payload')
    return table[name]


def _ifds(fmt, *, carriers=ALL_CARRIERS, thumbnail=True, exif_child=True, body=()):
    """(tag, type, kind, arg) entries in ascending tag order, for one TIFF directory set.

    kind 'lit' carries literal value bytes; 'name' a pool payload (inlined when it fits
    in four bytes, otherwise stored in the pool and referenced by offset); 'off'/'len'
    the offset/length of a pool blob; 'ifd' the offset of a directory.
    """
    ifd0 = [(256, T_SHORT, 'lit', b'\x08\x00'), (257, T_SHORT, 'lit', b'\x08\x00'),
            # Compression 1 everywhere: any wave-1 vendor value would borrow an identity
            # this build derives from that tag, and none of them means SR2.
            (259, T_SHORT, 'lit', b'\x01\x00'), (262, T_SHORT, 'lit', b'\x02\x00'),
            (271, T_ASCII, 'name', 'make'), (272, T_ASCII, 'name', 'model'),
            (273, T_LONG, 'off', 'pixel'), (274, T_SHORT, 'lit', b'\x01\x00'),
            (279, T_LONG, 'len', 'pixel'), (305, T_ASCII, 'name', 'software'),
            (315, T_ASCII, 'name', 'artist'), (33432, T_ASCII, 'name', 'copyright')]
    ifd0 += list(body)
    ifd0 += [(CARRIERS[name][0], CARRIERS[name][1], 'name', name) for name in carriers]
    if exif_child:
        ifd0.append((0x8769, T_LONG, 'ifd', 'ExifIFD'))
    ifds = [{'name': 'IFD0', 'entries': sorted(set(ifd0))}]
    if thumbnail:
        ifds.append({'name': 'IFD1', 'entries': sorted(
            [(259, T_SHORT, 'lit', b'\x06\x00'), (262, T_SHORT, 'lit', b'\x02\x00'),
             (513, T_LONG, 'off', 'thumb'), (514, T_LONG, 'len', 'thumb')])})
    if exif_child:
        ifds.append({'name': 'ExifIFD', 'entries': sorted(
            [(0x9000, T_BYTE, 'lit', EXIF_VERSION), (0x9003, T_ASCII, 'name', 'datetime')])})
    return ifds


def render_tiff(fmt, ifds, *, header=None, root=None, label=None):
    """Assemble one TIFF: header, directories in the given order, then the data pool.

    One sizing pass suffices because entry counts and types never depend on the resolved
    offsets: the directory sizes fix the pool base, the pool follows the declared
    POOL_ORDER, and every value longer than four bytes lives there. Offsets written into
    entries are relative to the *blob* start, which is exactly how the pinned reader sees
    an embedded MRW or RAF payload: it hands ``TiffParser::decode`` the block alone.
    """
    if header is None:
        # The generic header carries its own root offset at bytes 4-7; the caller may
        # pass a longer header (RW2) but then must pass the root it declares.
        header = b'II' + struct.pack('<HI', 42, 8 if root is None else root)
    root = len(header) if root is None else root
    if root < len(header):
        raise ValueError(f'{label}: root offset lies inside the header')
    cursor = root
    ifd_at = {}
    for ifd in ifds:
        ifd_at[ifd['name']] = cursor
        cursor += 2 + 12 * len(ifd['entries']) + 4
    # The pool holds exactly what the directories reference: no dead payload can ride
    # along, and a reference to something unauthored is a construction error here rather
    # than a gap the reviewer would have to explain later.
    referenced = {arg for ifd in ifds for (_, _, kind, arg) in ifd['entries']
                  if kind in ('name', 'off', 'len')}
    payloads = {name: payload_for(fmt, name) for name in referenced}
    names = [name for name in POOL_ORDER if name in referenced]
    data_at = {}
    for name in names:
        data_at[name] = cursor
        cursor += (len(payloads[name]) + 1) & ~1
    total = cursor

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
                raise ValueError(f'{label}: tag {tag} carries a literal longer than four '
                                 'bytes; values that long must be named pool payloads')
            out += struct.pack('<HHI', tag, type_id, count) + field
        chain = ifd_at['IFD1'] if ifd['name'] == 'IFD0' and 'IFD1' in ifd_at else 0
        out += struct.pack('<I', chain)
    for name in names:
        assert len(out) == data_at[name], f'{label}: pool offset drift at {name}'
        out += payloads[name]
        if len(out) & 1:
            out += b'\0'
    assert len(out) == total, f'{label}: rendered length differs from the sizing pass'
    return bytes(out)


def encode(fmt):
    """Assemble one B10 fixture, as a pure function of this module's tables."""
    spec = FORMATS[fmt]
    container = spec['container']
    if container == 'tiff':                                     # SR2: generic marker only
        return render_tiff(fmt, _ifds(fmt, carriers=spec['carriers']))
    if container == 'rw2':
        root = 24
        header = b'II' + struct.pack('<HI', 0x0055, root) + b'B10-RW2-IDENTITY'
        assert len(header) == root, 'the RW2 header contract and the root offset must agree'
        return render_tiff(fmt, _ifds(fmt, carriers=spec['carriers']),
                           header=header, root=root)
    if container == 'mrw':
        blob = render_tiff(fmt, _ifds(fmt, carriers=spec['carriers']), label='MRW/TTW')
        segments = [(b'\x00PRD', b'B10 OWNED PRD DIMENSIONS'.ljust(24, b'\0')),
                    (b'\x00WBG', b'B10 WB'.ljust(8, b'\0')),
                    (b'\x00RIF', b'B10 RF'.ljust(8, b'\0')),
                    (b'\x00TTW', blob)]
        body = b''.join(tag + struct.pack('>I', len(data)) + data for tag, data in segments)
        padded = len(body) + (len(body) & 1)
        # The MRM length field is the block bound the walk enforces against, and it is
        # fileLength - 8, which is also what ExifTool computes as its own loop bound.
        return b'\x00MRM' + struct.pack('>I', padded) + body + b'\0' * (padded - len(body))
    if container == 'raf':
        # The camera-settings blob carries the profile; the preview JPEG carries the
        # comment, EXIF, IIM and XMP that RafImage::readMetadata copies. Keeping them in
        # different blocks means a family can never be credited to the wrong carrier.
        camera = render_tiff('RAF', _ifds('RAF', carriers=spec['carriers'],
                                          thumbnail=False, exif_child=False), label='RAF/tiff')
        preview_tiff = render_tiff(
            'RAF',
            [{'name': 'IFD0', 'entries': sorted(
                [(271, T_ASCII, 'name', 'make'), (272, T_ASCII, 'name', 'model'),
                 (305, T_ASCII, 'name', 'software'), (315, T_ASCII, 'name', 'artist'),
                 (0x83BB, T_BYTE, 'name', 'iptc'), (0x02BC, T_BYTE, 'name', 'xmp')])}],
            label='RAF/preview')
        preview = owned.jpeg_build(owned.exif_segment(preview_tiff)
                                   + owned.comment_segment(RAF_COMMENT))
        cfa = b'B10 OWNED OFFSET-92 FILLER, NOT A VENDOR CFA STRUCTURE'.ljust(64, b'\0')
        base = 108                       # 84 bytes of printed header + 6 BE uint32 fields
        jpeg_off, tiff_off = base, base + len(preview)
        cfa_off = tiff_off + len(camera)
        end = cfa_off + len(cfa)
        head = bytearray(b'FUJIFILM' + b'0232' + b' ' * 8)
        head += b'B10DATA2' + b'B10 SYNTHETIC RAF CAMERA'.ljust(32, b'\0')
        head += b'0100' + b'B10-UNKNOWN-FILLER'.ljust(20, b'\0')
        head += bytes(base - len(head))
        assert len(head) == base, 'the RAF printed header must end where the fields begin'
        for at, value in ((84, jpeg_off), (88, len(preview)), (92, cfa_off), (96, len(cfa)),
                          (100, tiff_off), (104, len(camera))):
            head[at:at + 4] = struct.pack('>I', value)
        tail = bytes(head) + preview + camera + cfa
        assert len(tail) == end, 'RAF: the blocks do not end where the offset table says'
        return tail
    raise AssertionError(container)


# ---------------------------------------------------------------------- oracle
def recognizers(data):
    """Transcription of the four pinned readers plus the registry's first-match order.

    ``ImageFactory::open`` returns the *first* registry entry whose type check passes
    (src/image.cpp:770,844); the wave-2 entries sit at :83 (mrw), :84 (tiff), :90 (rw2),
    :91 (sr2, bound to the TIFF checker) and :98 (raf). The generic rule is
    ``TiffHeaderBase::read``: 'II'/'MM', then the uint16 at bytes 2-3 must equal the
    subclass magic or one of the wildcards 444 (JPEG-XR) / 17234 (DCP), and the root IFD
    offset comes from bytes 4-7 unchecked. Not a vendor specification.
    """
    if len(data) < 24:
        raise ValueError('fixture is shorter than the RW2 header contract (24 bytes)')
    bo = ('littleEndian' if data[0] == data[1] == 0x49 else
          'bigEndian' if data[0] == data[1] == 0x4d else 'invalidByteOrder')
    order = '<' if bo == 'littleEndian' else '>'
    tag = struct.unpack_from(f'{order}H', data, 2)[0] if bo != 'invalidByteOrder' else None
    root = struct.unpack_from(f'{order}I', data, 4)[0] if bo != 'invalidByteOrder' else None
    tiff_ok = bo != 'invalidByteOrder' and tag in (42,) + WILDCARD_MAGICS
    rw2_ok = bo != 'invalidByteOrder' and tag in (0x0055,) + WILDCARD_MAGICS
    mrw_ok = data[0:4] == b'\x00MRM'
    raf_ok = data[0:8] == b'FUJIFILM'
    dispatch = next((name for name, ok in (('mrw', mrw_ok), ('tiff', tiff_ok),
                                           ('rw2', rw2_ok), ('raf', raf_ok)) if ok), 'none')
    return {'byteOrder': bo,
            'mrw': {'accepted': mrw_ok, 'magic': data[0:4].hex()},
            'tiff': {'accepted': tiff_ok, 'tag': tag, 'rootOffset': root},
            'rw2': {'accepted': rw2_ok, 'sig': None if tag is None else f'0x{tag:04x}',
                    'rootOffset': root, 'headerBytes': 24},
            'raf': {'accepted': raf_ok, 'magic': data[0:8].decode('latin-1')},
            'wildcardMagic': tag in WILDCARD_MAGICS,
            'dispatch': dispatch}


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


def _spans_ok(spans, data, directory_end, where):
    spans = sorted(spans)
    for (_, previous_end), (nxt, _) in zip(spans, spans[1:]):
        if previous_end > nxt:
            raise ValueError(f'{where}: data pool payloads overlap')
        if nxt - previous_end > 1 or (nxt > previous_end and data[previous_end] not in (0,)):
            raise ValueError(f'{where}: data pool carries an unexplained gap')
    if spans and spans[0][0] < directory_end:
        raise ValueError(f'{where}: a payload offset points inside the directory area')
    return spans


def tiff_facts(blob, where):
    """Re-parse a whole TIFF (file or embedded blob) and check it against its carriers."""
    rec = {'rootOffset': struct.unpack_from('<I', blob, 4)[0],
           'tag': struct.unpack_from('<H', blob, 2)[0]}
    if rec['tag'] in WILDCARD_MAGICS:
        raise ValueError(f'{where}: the TIFF uses a wildcard magic')
    root = rec['rootOffset']
    ifd0 = read_ifd(blob, root, f'{where}/IFD0', set())
    names, dirs = ['IFD0'], [ifd0]
    if ifd0['nextOffset']:
        dirs.append(read_ifd(blob, ifd0['nextOffset'], f'{where}/IFD1', {root}, ))
        names.append('IFD1')
    if 0x8769 in ifd0['byTag']:
        at = struct.unpack('<I', ifd0['byTag'][0x8769]['payload'])[0]
        dirs.append(read_ifd(blob, at, f'{where}/ExifIFD', {root}, ))
        names.append('ExifIFD')
    refs = [(e['payloadOffset'], e['payloadOffset'] + e['length'])
            for d in dirs for e in d['entries'] if not e['inline']]
    for directory, off_tag, len_tag, expected, label in (
            (ifd0, 273, 279, PIXEL, 'strip'),
            (dirs[1] if 'IFD1' in names else None, 513, 514, owned.thumb_jpeg_bytes(),
             'thumbnail')):
        if directory is None or off_tag not in directory['byTag']:
            continue
        at = struct.unpack('<I', directory['byTag'][off_tag]['payload'])[0]
        length = struct.unpack('<I', directory['byTag'][len_tag]['payload'])[0]
        if blob[at:at + length] != expected:
            raise ValueError(f'{where}: {label} bytes differ from the owned payload')
        refs.append((at, at + length))
    spans = _spans_ok(refs, blob, max(d['end'] for d in dirs), where)
    carriers = []
    for name in ALL_CARRIERS:
        tag = CARRIERS[name][0]
        if tag not in ifd0['byTag']:
            continue
        if ifd0['byTag'][tag]['payload'] != CARRIERS[name][2]:
            raise ValueError(f'{where}: tag {tag} payload differs from the authored {name}')
        carriers.append(name)
    last = max([end for _, end in spans] + [d['end'] for d in dirs])
    # Even-length padding is the only byte a TIFF may append after its last value.
    if len(blob) - last not in (0, 1) or (len(blob) > last and blob[last:] != b'\x00'):
        raise ValueError(f'{where}: trailing bytes after the data pool are unaccounted for')
    def value(tag, kind='H'):
        if tag not in ifd0['byTag']:
            return None
        payload = ifd0['byTag'][tag]['payload']
        if kind == 'text':
            return payload.rstrip(b'\x00').decode('ascii')
        return struct.unpack('<' + kind, payload)[0]

    return {'rootOffset': root, 'magicTag': rec['tag'], 'directoryOrder': names,
            'ifd0Tags': sorted(e['tag'] for e in ifd0['entries']),
            'compression': value(259), 'make': value(271, 'text'), 'model': value(272, 'text'),
            'carriers': carriers, 'exifTags': sorted(e['tag'] for d in dirs if d['name'] == 'ExifIFD'
                                                      for e in d['entries']),
            'thumbnailPresent': 'IFD1' in names and 513 in dirs[1]['byTag'],
            'dataPoolSpans': len(spans), 'dataPoolEnd': last, 'blobLength': len(blob),
            'trailingPadBytes': len(blob) - last, 'blobSha256': digest(blob)}


def mrw_walk(data):
    """Replay MrwImage::readMetadata's block walk, enforcement for enforcement."""
    if len(data) < 16:
        raise ValueError('MRW fixture is shorter than two block headers')
    end = struct.unpack_from('>I', data, 4)[0]
    pos, cursor, blocks = 16, 8, []
    if pos > end:
        raise ValueError('MRW: enforce(pos <= end) fails on the first block header')
    while True:
        tag, size = data[cursor:cursor + 4], struct.unpack_from('>I', data, cursor + 4)[0]
        if tag == b'\x00TTW':
            if size > len(data) - (cursor + 8):
                raise ValueError('MRW: the TTW payload runs past the file')
            blocks.append(dict(tag=tag.decode('latin-1'), header=cursor, size=size,
                               payload=cursor + 8))
            break
        if size > end - pos:
            raise ValueError(f'MRW: block {tag!r} exceeds the declared bound')
        blocks.append(dict(tag=tag.decode('latin-1'), header=cursor, size=size,
                           payload=cursor + 8))
        pos += size
        if 8 > end - pos:
            raise ValueError('MRW: no room left for the next block header')
        pos += 8
        cursor += 8 + size
    ttw = blocks[-1]
    return {'blockBound': end, 'blocks': blocks, 'ttwAt': ttw['payload'],
            'ttwLength': ttw['size'], 'exiftoolLoopBound': end + 8,
            'coversFile': end + 8 == len(data)}


def raf_fields(data):
    """The six big-endian fields plus the exact slices printStructure echoes."""
    def pair(at):
        return struct.unpack_from('>I', data, at)[0], struct.unpack_from('>I', data, at + 4)[0]
    jpeg, cfa, tiff = pair(84), pair(92), pair(100)
    slices = {'magic': (0, 16), 'data1': (16, 4), 'data2': (20, 8), 'camera': (28, 32),
              'version': (60, 4)}
    return {'jpegOffset': jpeg[0], 'jpegLength': jpeg[1], 'cfaOffset': cfa[0],
            'cfaLength': cfa[1], 'tiffOffset': tiff[0], 'tiffLength': tiff[1],
            'printedText': {name: data[at:at + size].decode('latin-1')
                           for name, (at, size) in slices.items()},
            'unknownSha256': digest(data[64:84])}


def raf_preview_facts(data, fields):
    """Peel the preview JPEG apart: SOI, APP1 EXIF, COM, then the authored body."""
    start = fields['jpegOffset']
    blob = data[start:start + fields['jpegLength']]
    if blob[0:2] != b'\xff\xd8' or blob[2:4] != b'\xff\xe1':
        raise ValueError('RAF: the preview is not SOI followed by the APP1 Exif segment')
    segment = struct.unpack('>H', blob[4:6])[0]
    if blob[6:12] != b'Exif\x00\x00':
        raise ValueError('RAF: the APP1 segment is not an Exif segment')
    # The JPEG length field counts itself, so the APP1 payload is `segment - 2` bytes
    # starting at 6, and the TIFF inside it starts 6 bytes further in. Off by that
    # self-count and the reparse silently inherits two bytes of the next marker, which is
    # the sort of thing a fixture oracle exists to catch.
    tiff = blob[12:segment + 4]
    found = {'jpegSha256': digest(blob), 'app1Length': segment - 8, 'exifTiffLength': len(tiff)}
    facts = tiff_facts(tiff, 'RAF/preview')
    marker = blob.find(b'\xff\xfe', 6)
    if marker < 0:
        raise ValueError('RAF: the preview carries no COM segment for the copied comment')
    length = struct.unpack('>H', blob[marker + 2:marker + 4])[0]
    if blob[marker + 4:marker + 4 + length - 2] != RAF_COMMENT:
        raise ValueError('RAF: the COM payload differs from the owned comment')
    found.update({'commentSha256': digest(RAF_COMMENT), 'tiff': facts})
    return found


def structure(fmt, data):
    """Independent framing facts for one B10 fixture; raises if it is not self-consistent."""
    rec = recognizers(data)
    container = FORMATS[fmt]['container']
    if rec['dispatch'] != container:
        raise ValueError(f'{fmt}: dispatch reached {rec["dispatch"]}, not {container}')
    if rec['wildcardMagic']:
        raise ValueError(f'{fmt}: the header uses a wildcard magic every TIFF reader accepts')
    out = {'byteOrder': rec['byteOrder'], 'dispatch': rec['dispatch'], 'recognizers': rec,
           'fileLength': len(data), 'fileSha256': digest(data)}
    if fmt == 'MRW':
        walk = mrw_walk(data)
        blob = data[walk['ttwAt']:walk['ttwAt'] + walk['ttwLength']]
        out['mrw'] = {'blockBound': walk['blockBound'], 'blockTags': [b['tag'] for b in walk['blocks']],
                      'ttwAt': walk['ttwAt'], 'ttwLength': walk['ttwLength'],
                      'coversFile': walk['coversFile'], 'exiftoolLoopBound': walk['exiftoolLoopBound']}
        out['embedded'] = tiff_facts(blob, 'MRW/TTW')
        last = walk['ttwAt'] + walk['ttwLength']
    elif fmt == 'RAF':
        fields = raf_fields(data)
        for off, length in (('jpegOffset', 'jpegLength'), ('tiffOffset', 'tiffLength'),
                            ('cfaOffset', 'cfaLength')):
            if fields[off] + fields[length] > len(data):
                raise ValueError(f'RAF: {off} + {length} runs past the file')
        if fields['jpegLength'] < 12:
            raise ValueError('RAF: the pinned reader enforces a preview length of at least 12')
        if data[fields['tiffOffset']:fields['tiffOffset'] + 4] not in (b'II\x2a\x00', b'MM\x00\x2a'):
            raise ValueError('RAF: the embedded TIFF fails the pinned II*/MM* gate, so the build '
                             'would skip parsing it silently rather than read it')
        if fields['printedText']['magic'][:8] != 'FUJIFILM':
            raise ValueError('RAF: the printed magic is not what isRafType requires')
        out['raf'] = fields
        out['embedded'] = tiff_facts(data[fields['tiffOffset']:
                                         fields['tiffOffset'] + fields['tiffLength']], 'RAF/tiff')
        out['preview'] = raf_preview_facts(data, fields)
        last = max(fields['tiffOffset'] + fields['tiffLength'],
                   fields['cfaOffset'] + fields['cfaLength'])
    elif fmt == 'RW2':
        root = rec['rw2']['rootOffset']
        out['rw2'] = {'magic': f'0x{struct.unpack_from("<H", data, 2)[0]:04x}', 'rootOffset': root,
                      'identityField': data[8:24].decode('latin-1'), 'headerBytes': 24}
        if root != 24:
            raise ValueError('RW2: the root offset must be the 24 the pinned header declares')
        # The file *is* the TIFF here, so the blob-relative rule is the file rule.
        out['embedded'] = tiff_facts(data, 'RW2')
        last = out['embedded']['dataPoolEnd']
    else:
        out['embedded'] = tiff_facts(data, 'SR2')
        if out['embedded']['compression'] in MIME_COMPRESSIONS:
            raise ValueError('SR2: the marker carries a wave-1 vendor compression value')
        last = out['embedded']['dataPoolEnd']
    out['lastAccountedByte'] = last
    if len(data) - last not in (0, 1) or (len(data) > last and data[last:] != b'\x00'):
        raise ValueError(f'{fmt}: trailing bytes are unaccounted for')
    out['trailingPadBytes'] = len(data) - last
    return out


def entries():
    result = []
    for layout in MARKER_LAYOUTS:
        fmt, name = FIXTURES[layout]
        facts = structure(fmt, (DIRECTORY / name).read_bytes())
        embedded = facts['embedded']
        result.append(dict(file=name, format=fmt, layout=layout, length=facts['fileLength'],
                           sha256=facts['fileSha256'], rights='owned-synthetic', recipe=RECIPE,
                           dispatch=facts['dispatch'], compression=embedded['compression'],
                           carrierTags=embedded['carriers'], thumbnailPresent=embedded['thumbnailPresent'],
                           embeddedBlobSha256=embedded['blobSha256'],
                           vendorIdentity=FORMATS[fmt]['identity'], decodable=False,
                           metadata='authored-exif-xmp-iptc-icc-thumbnail-comments'))
    return result


def verify():
    """Re-derive every fixture from this module and check bytes plus framing."""
    for fmt, name in FIXTURES.values():
        path = DIRECTORY / name
        data = path.read_bytes()
        if data != encode(fmt):
            raise ValueError(f'{name} differs from its authored bytes')
        facts = structure(fmt, data)
        spec, embedded = FORMATS[fmt], facts['embedded']
        if facts['dispatch'] != spec['container']:
            raise ValueError(f'{name}: dispatch does not match the container it claims')
        for tag in (305, 315):
            if tag not in embedded['ifd0Tags']:
                raise ValueError(f'{name}: the authored token tag {tag} is missing')
        if embedded['thumbnailPresent'] != spec['thumbnail']:
            raise ValueError(f'{name}: the chained IFD1 thumbnail differs from the recipe')
        for carrier in spec['carriers']:
            if carrier not in embedded['carriers']:
                raise ValueError(f'{name}: the {carrier} carrier tag is missing')
        for carrier in set(ALL_CARRIERS) - set(spec['carriers']):
            if carrier in embedded['carriers']:
                raise ValueError(f'{name}: an {carrier} carrier appeared where the recipe '
                                 'puts none')
    recorded = json.loads(MANIFEST.read_text(encoding='utf-8')) if MANIFEST.exists() else None
    if recorded != entries():
        raise ValueError('B10 census fixture manifest differs from the fixture bytes')
    return recorded


def main():
    if sys.argv[1:] == ['generate']:
        DIRECTORY.mkdir(parents=True, exist_ok=True)
        for fmt, name in sorted(FIXTURES.values()):
            (DIRECTORY / name).write_bytes(encode(fmt))
        MANIFEST.write_text(json.dumps(entries(), indent=2) + '\n', encoding='utf-8')
        print(f'wrote {len(FIXTURES)} fixtures and {MANIFEST.name}')
    elif sys.argv[1:] != ['verify']:
        raise SystemExit('expected verify or generate')
    manifest = verify()
    print(f'B10 census fixture catalog verified: {len(manifest)} fixtures')


if __name__ == '__main__':
    main()
