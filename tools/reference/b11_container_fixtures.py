"""B11 census fixtures: owned synthetic CIFF, BMP/GIF/TGA and container probes.

Standard library only. Nothing here is a camera capture, a vendor sample, a rendered
photograph or a decodable movie. Each fixture is a byte-authored container carrying exactly
the header contract the *pinned reference build* uses to recognise this format, plus the
metadata components this batch needs to observe, and nothing else. No pixel data and no
media stream is authored, because no B11 documented claim depends on one.

Why these exact bytes (reviewed in docs/research/E01_B11_RIGHTS_AND_ORACLE_REVIEW.md): the
pinned Exiv2 0.28.9 source recognises and reads these formats through small, fully readable
routines, and those routines are the only specification this batch reviewed.

* CRW -- ``isCrwType`` (src/crwimage.cpp:139) requires ``II``/``MM`` at bytes 0-1 and
  ``HEAPCCDR`` at bytes 6-13. ``CiffHeader::read`` (src/crwimage_int.cpp:155) takes the root
  offset from a ``u32`` at byte 2, demands ``14 <= offset <= size``, treats ``14 .. offset``
  as padding and parses the root directory from ``offset`` *to the end of the file*.
  ``CiffDirectory::readDirectory`` (:238) reads the offset of the entry count from the last
  four bytes of the region and walks ``count`` 10-byte entries, building a ``CiffDirectory``
  when ``CiffComponent::typeId`` (the ``tag & 0x3800`` switch at :450) says ``directory`` and
  a ``CiffEntry`` otherwise. ``CiffComponent::doRead`` (:186) reads ``size``/``offset`` from
  the entry's two ``u32`` fields whenever ``dataLocation`` (the ``tag & 0xc000`` switch at
  :469) says ``valueData``, and enforces that the payload never overlaps the entry itself.
  ``CrwMap::decode`` (:634) looks a component up by *(parent directory tag, tag & 0x3fff)*,
  so a mapped tag sitting in the wrong directory is not corrupt: it is silently unmapped.
* BMP -- ``isBmpType`` (src/bmpimage.cpp:119) compares two bytes, ``BM``, and ``readMetadata``
  (:42) reads 26 bytes and branches on ``getULong(buf + 14)``: 12 is the OS/2
  ``BITMAPCOREHEADER`` with 16-bit width/height, anything else is read as the 32-bit
  ``BITMAPINFOHEADER`` fields. This reader has no metadata path at all, so a BMP fixture
  carries no carriers by construction rather than by omission.
* GIF -- ``isGifType`` (src/gifimage.cpp:75) compares six bytes against ``GIF87a``/``GIF89a``
  and advances; ``readMetadata`` (:37) then reads four bytes for the logical screen
  descriptor and *silently* leaves dimensions alone if that read comes up short.
* TGA -- ``isTgaType`` (src/tgaimage.cpp:99) ignores its ``advance`` argument and returns true
  for any path ending in ``.tga``/``.TGA`` before reading a byte; otherwise it demands
  ``iIo.tell() >= 26`` before it looks at the 26-byte footer, which the factory never
  satisfies because it calls with a freshly opened io at position 0. ``readMetadata`` (:39)
  reads 18 bytes and takes width/height as little-endian ``u16`` at +12/+14. Those two facts
  together are why this module ships a ``.tga``-named footer file, a ``.tga``-named file with
  no footer at all, and the *same bytes* under a ``.targa`` name.
* MOV/MKV/AVI/WAV/ASF -- ``isQTimeType`` (src/quicktimevideo.cpp:1616) needs one of eleven
  known atom tags at offset 4 *and* a brand at offset 8 present in ``qTimeFileType``
  (:42-128); ``isBmffType`` (src/bmffimage.cpp:794) is the fall-through for ``ftyp``/``JXL ``;
  ``isMkvType`` compares ``1A 45 DF A3``; ``isRiffType`` compares only ``RIFF``;
  ``isAsfType`` compares the 16-byte ASF Header Object GUID (src/asfvideo.cpp:95). These stubs
  are built from those tests and nothing more: each one is a *dispatch* fixture, existing to
  record which reader the pinned registry reaches, which is the only thing this batch can
  observe for a format the pinned documentation never puts in its table.

``recognizers`` transcribes those readers *and* the registry's first-match order, so the
offline check proves each fixture reaches the class it claims -- and, for the ``.targa``
byte-identical twin and the unknown-brand movie, that it reaches what the review says it
must. This is a container-framing oracle for our own authored bytes only: not a media format
specification, not Canon CIFF semantics beyond the mapping table, and not a support claim
about MetadataSharp or about Exiv2 beyond this recorded build.

Usage: b11_container_fixtures.py verify     # offline; no Exiv2, no ExifTool, no PIL
       b11_container_fixtures.py generate   # rewrite fixtures plus the byte-pinned manifest
"""
import hashlib
import json
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[2]
DIRECTORY = ROOT / 'tests/fixtures/b11'
MANIFEST = DIRECTORY / 'census-manifest.json'
sys.path.insert(0, str(Path(__file__).resolve().parent))
import jpeg_owned_fixtures as owned  # noqa: E402  (owned payload: the thumbnail JPEG)

# The tokens a B11 probe may print. B10's tokens are deliberately not reused: these are a
# separate batch, and a shared payload would make a cross-batch digest collision look like
# evidence.
TOKENS = ('B11CrwComment', 'CANON', 'B11 SYNTHETIC CRW', 'B11Owner', 'B11FIRMWARE')
COMMENT = b'B11CrwComment'
MAKE_MODEL = b'CANON\x00B11 SYNTHETIC CRW\x00'
OWNER = b'B11Owner\x00'
FIRMWARE = b'B11FIRMWARE\x00'
FILE_NUMBER = 0x1234
WIDTH, HEIGHT = 96, 64
# 2026-01-01T00:00:00Z. decode0x180e renders this through localtime_r, so the printed text
# depends on the runner's timezone: the CI job pins TZ=UTC0 and the census row says so.
EPOCH = 1767225600
SUBJECT_DISTANCE = (1000, 10)   # print0x9206 divides the pair -> "100.00 m"
COLOR_SPACE = 1                 # Exif.Photo.ColorSpace = sRGB

# CIFF constants: src/crwimage_int.hpp:468 and src/crwimage_int.cpp:450-477.
CIFF_SIGNATURE = b'HEAPCCDR'
CIFF_TYPE_MASK = 0x3800
CIFF_DIR_MASK = 0xc000
CIFF_ID_MASK = 0x3fff
CIFF_DIRECTORY_TYPES = (0x2800, 0x3000)
# (parent dir, tag id) -> what the pinned table does with it. This is the subset of
# crwMapping_ (src/crwimage_int.cpp:82-110) that B11 exercises, plus the two root-level
# components CrwParser::decode handles itself (src/crwimage.cpp:102-115).
CIFF_MAPPED = {(0x300a, 0x0805): 'comment', (0x2807, 0x080a): 'make-model',
               (0x300a, 0x1810): 'dimensions', (0x300a, 0x180e): 'datetime',
               (0x300b, 0x10b4): 'colorspace', (0x3002, 0x1807): 'distance',
               (0x3004, 0x080b): 'firmware', (0x2807, 0x0810): 'owner-name',
               (0x300a, 0x1817): 'file-number', (0x0000, 0x2008): 'thumbnail',
               (0x0000, 0x2007): 'preview-offset'}
# What each of those rows becomes on the printer, recorded so the census row and the
# fixture cannot drift apart: (kind) -> Exif key, from the mapping row's exifTag_ plus
# groupName(ifdId_) at src/tags_int.cpp:2531.
CIFF_KEYS = {'comment': 'Exif.Image2.Comment (setComment, no Exif key)',
             'make-model': 'Exif.Image.Make + Exif.Image.Model',
             'dimensions': 'Exif.Photo.PixelXDimension + Exif.Photo.PixelYDimension',
             'datetime': 'Exif.Photo.DateTimeOriginal',
             'colorspace': 'Exif.Photo.ColorSpace',
             'distance': 'Exif.Photo.SubjectDistance',
             'firmware': 'Exif.Canon.FirmwareVersion',
             'owner-name': 'Exif.Canon.OwnerName',
             'file-number': 'Exif.Canon.FileNumber',
             'thumbnail': 'Exif.Thumbnail.JPEGInterchangeFormat{,Length} in ifd1',
             'preview-offset': 'nothing: CrwMap has no row for (0x0000, 0x2007)'}
# CrwMap::crwSubDir_ (src/crwimage_int.cpp:128-132), arranged bottom-up so that every
# directory has exactly one path to the root.
CIFF_PARENT = {0x3004: 0x2807, 0x300b: 0x300a, 0x3003: 0x300a, 0x3002: 0x300a,
               0x2807: 0x300a, 0x2804: 0x300a, 0x300a: 0x0000}
CIFF_MAPPED_DIRS = (0x300a, 0x2807, 0x3004, 0x3002, 0x3003, 0x300b)
# src/quicktimevideo.cpp:1625, the atom tags isQTimeType accepts at offset 4.
QTIME_ATOMS = ('PICT', 'free', 'ftyp', 'junk', 'mdat', 'moov', 'pict', 'pnot', 'skip',
               'uuid', 'wide')
# src/quicktimevideo.cpp:42-129, transcribed in full (86 rows, in source order): isQTimeType
    # matches only when the
# brand at offset 8 is one of these, which is what makes the brand -- not the extension --
# decide QuickTime versus BMFF.
QTIME_BRANDS = (
                 '3g2a', '3g2b', '3g2c', '3ge6', '3ge7', '3gg6', '3gp1', '3gp2', '3gp3', '3gp4', '3gp5',
                 '3gp6', '3gs7', 'CAEP', 'CDes', 'F4A ', 'F4B ', 'F4P ', 'F4V ', 'JP2 ', 'JP20', 'KDDI',
                 'M4A ', 'M4B ', 'M4P ', 'M4V ', 'M4VH', 'M4VP', 'MPPI', 'MSNV', 'NDAS', 'NDSC', 'NDSH',
                 'NDSM', 'NDSP', 'NDSS', 'NDXC', 'NDXH', 'NDXM', 'NDXP', 'NDXS', 'NIKO', 'ROSS', 'avc1',
                 'caqv', 'da0a', 'da0b', 'da1a', 'da1b', 'da2a', 'da2b', 'da3a', 'da3b', 'dmb1', 'dmpf',
                 'drc1', 'dv1a', 'dv1b', 'dv2a', 'dv2b', 'dv3a', 'dv3b', 'dvr1', 'dvt1', 'isc2', 'iso2',
                 'isom', 'jpm ', 'jpx ', 'mj2s', 'mjp2', 'mmp4', 'mp21', 'mp41', 'mp42', 'mp71', 'mqt ',
                 'niko', 'odcf', 'opf2', 'opx2', 'pana', 'qt  ', 'sdv ', 'ssc1', 'ssc2')
ASF_HEADER_GUID = struct.pack('<IHH8s', 0x75B22630, 0x668E, 0x11CF,
                              bytes([0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C]))
MKV_MAGIC = b'\x1a\x45\xdf\xa3'
TGA_FOOTER_SIGNATURE = b'TRUEVISION-XFILE'
UNKNOWN_BRAND = 'b10u'   # absent from QTIME_BRANDS on purpose, so BMFF gets the file
# Every TIFF-family magic this build accepts, so no B11 fixture may land on one by
# accident and be dispatched to tiff instead of the format it names.
TIFF_MAGICS = (42, 444, 17234, 0x0055, 0x5500)

FIXTURES = {
    'ciff-mapped-v1': ('CRW', 'owned-crw-mapped.crw'),
    'ciff-thumb-only-v1': ('CRW', 'owned-crw-thumb.crw'),
    'ciff-preview-plus-thumb-v1': ('CRW', 'owned-crw-preview-thumb.crw'),
    'ciff-unmapped-only-v1': ('CRW', 'owned-crw-unmapped.crw'),
    'bmp-dib-v3': ('BMP', 'owned-bmp-dib.bmp'),
    'bmp-os2-coreheader-v1': ('BMP', 'owned-bmp-os2.bmp'),
    'gif-lsd-v1': ('GIF', 'owned-gif-lsd.gif'),
    'gif-truncated-v1': ('GIF', 'owned-gif-no-lsd.gif'),
    'tga-full-v1': ('TGA', 'owned-tga-full.tga'),
    'tga-name-only-v1': ('TGA', 'owned-tga-name-only.tga'),
    'tga-signature-wrong-name-v1': ('TGA', 'owned-tga-signature-only.targa'),
    'qtime-known-brand-v1': ('MOV', 'owned-mov-brand.mov'),
    'qtime-unknown-brand-v1': ('MOV', 'owned-mov-unknownbrand.mov'),
    'mkv-ebml-header-v1': ('MKV', 'owned-mkv-header.mkv'),
    'riff-avi-avih-v1': ('AVI', 'owned-avi-avih.avi'),
    'riff-wav-fmt-v1': ('WAV', 'owned-wav-fmt.wav'),
    'asf-header-guid-v1': ('ASF', 'owned-asf-header.asf'),
}
SEED_LAYOUTS = tuple(sorted(FIXTURES))
BLOCKED_LAYOUTS = ('ciff-vendor-private-unobserved',)

FORMATS = {
    'CRW': dict(ext='crw', carriers='ciff-components',
                identity='II/MM byte-order mark, HEAPCCDR at 6, root directory at the u32 in '
                        'bytes 2-5; CrwImage::mimeType is image/x-canon-crw and dimensions '
                        'come from Exif.Photo.PixelXDimension, not from pixels'),
    'BMP': dict(ext='bmp', carriers='none',
                identity='BM plus a DIB header; BmpImage::mimeType is image/x-ms-bmp and the '
                        'reader has no Exif, IPTC, XMP, comment, ICC or thumbnail path'),
    'GIF': dict(ext='gif', carriers='none',
                identity='GIF87a/GIF89a plus the logical screen descriptor; GifImage::mimeType '
                        'is image/gif'),
    'TGA': dict(ext='tga', carriers='none',
                identity='18-byte header with width/height at +12/+14 and an optional 26-byte '
                        'TRUEVISION-XFILE footer; TgaImage::mimeType is image/targa and '
                        'isTgaType consults the file name before any byte'),
    'MOV': dict(ext='mov', carriers='xmp-only',
                identity='8-byte atom header plus a brand; isQTimeType needs a known atom tag '
                        '*and* a brand in qTimeFileType, and isBmffType is the fall-through, '
                        'so the brand decides the reader'),
    'MKV': dict(ext='mkv', carriers='xmp-only',
                identity='EBML magic 1A 45 DF A3; MatroskaVideo::mimeType is video/matroska'),
    'AVI': dict(ext='avi', carriers='xmp-only',
                identity='the RIFF fourcc only -- isRiffType checks nothing else -- with an '
                        'AVI LIST/hdrl/avih body; RiffVideo::mimeType is video/riff'),
    'WAV': dict(ext='wav', carriers='xmp-only',
                identity='RIFF fourcc with a WAVE/fmt body: the same video/riff reader handles '
                        'it, which is what the pinned bullet means by "riff based"'),
    'ASF': dict(ext='asf', carriers='xmp-only',
                identity='the 16-byte ASF Header Object GUID; AsfVideo::mimeType is video/asf, '
                        'and nothing about this container is RIFF'),
}
BLOCKED_REASON = ('Canon CIFF content beyond the 17 canonId rows of crwMapping_ is not authored: '
                  'the private tag semantics are vendor-defined, no independent normative '
                  'specification was reviewed here and no rights-cleared sample is admitted, so '
                  'the cell is blocked rather than refused -- a plausible-looking private '
                  'component would invent evidence instead of observing it. See '
                  'docs/research/E01_B11_RIGHTS_AND_ORACLE_REVIEW.md.')
RECIPE = ('authored byte-level small-format and container writer (CIFF directory tree with '
          'payload-before-count regions, BMP BITMAPINFOHEADER/BITMAPCOREHEADER, GIF logical '
          'screen descriptor, TGA header plus optional footer, and per-reader container stubs); '
          'tools/reference/b11_container_fixtures.py')


def digest(data):
    return hashlib.sha256(data).hexdigest()


def blocked_reason():
    return BLOCKED_REASON


# ------------------------------------------------------------------------------- CIFF
def ciff_type_id(tag):
    """CiffComponent::typeId: the ``(tag & 0x3800)`` switch at src/crwimage_int.cpp:450."""
    family = tag & CIFF_TYPE_MASK
    if family in CIFF_DIRECTORY_TYPES:
        return 'directory'
    return {0x0000: 'unsignedByte', 0x0800: 'asciiString', 0x1000: 'unsignedShort',
            0x1800: 'unsignedLong', 0x2000: 'undefined'}[family]


def ciff_data_location(tag):
    """CiffComponent::dataLocation: the ``(tag & 0xc000)`` switch at :469."""
    location = tag & CIFF_DIR_MASK
    if not location:
        return 'valueData'
    if location == 0x4000:
        return 'directoryData'
    raise ValueError(f'CIFF tag 0x{tag:04x} carries a reserved data-location encoding')


def render_ciff_directory(children):
    """Build one CIFF directory region from ``[(tag, payload)]``.

    The region is ``[payloads][u16 count][10 bytes per entry][u32 offset of the count]``,
    which is the exact shape ``CiffDirectory::readDirectory`` walks. Putting the payloads
    *before* the count is what satisfies the overlap rule (``size <= start - offset`` for
    ``offset < start``) for every entry at once, and the trailing u32 is what the root
    region's own ``getULong(pData + size - 4)`` reads, so the file's last four bytes are
    the only self-referential number a CRW has to get right.
    """
    pool, placed = b'', []
    for tag, payload in children:
        placed.append((tag, len(pool), len(payload)))
        pool += payload
    count_at = len(pool)
    entries = b''
    for i, (tag, offset, size) in enumerate(placed):
        start = count_at + 2 + 10 * i
        if offset >= start or size > start - offset:
            raise ValueError(f'CIFF entry 0x{tag:04x} would overlap its own ten bytes')
        entries += struct.pack('<HII', tag, size, offset)
    return pool + struct.pack('<H', len(placed)) + entries + struct.pack('<I', count_at)


def ciff_directory(tag, children):
    """A sub-directory payload: the child region, with its own count pointer."""
    if ciff_type_id(tag) != 'directory':
        raise ValueError(f'0x{tag:04x} is not a CIFF directory tag')
    if CIFF_PARENT.get(tag) is None:
        raise ValueError(f'0x{tag:04x} is not a directory in crwSubDir_')
    return render_ciff_directory(children)


def ciff_value(tag, payload):
    if ciff_type_id(tag) == 'directory':
        raise ValueError(f'0x{tag:04x} is a directory tag, not a value tag')
    return payload


def crw_tree(kind):
    """The root children of one CRW fixture."""
    if kind == 'mapped':
        return [(0x300a, ciff_directory(0x300a, [
            (0x0805, ciff_value(0x0805, COMMENT)),
            (0x1810, ciff_value(0x1810, struct.pack('<7I', WIDTH, HEIGHT, 0, 0, 0, 0, 0))),
            (0x180e, ciff_value(0x180e, struct.pack('<7I', EPOCH, 0, 0, 0, 0, 0, 0))),
            (0x2807, ciff_directory(0x2807, [
                (0x080a, ciff_value(0x080a, MAKE_MODEL)),
                (0x0810, ciff_value(0x0810, OWNER)),
                (0x3004, ciff_directory(0x3004, [(0x080b, ciff_value(0x080b, FIRMWARE))]))])),
            (0x3002, ciff_directory(0x3002, [(0x1807, ciff_value(0x1807,
                                             struct.pack('<2I', *SUBJECT_DISTANCE)))])),
            (0x300b, ciff_directory(0x300b, [(0x10b4, ciff_value(0x10b4,
                                            struct.pack('<H', COLOR_SPACE)))])),
            # The mapping row carries size_ = 4, which decodeBasic lets override whatever the
            # entry claims (src/crwimage_int.cpp:808), so this payload is deliberately 8.
            (0x1817, ciff_value(0x1817, struct.pack('<2I', 0x1234, 0xDEAD)))]))]
    if kind == 'thumb':
        return [(0x2008, ciff_value(0x2008, owned.thumb_jpeg_bytes()))]
    if kind == 'preview-thumb':
        thumb = owned.thumb_jpeg_bytes()
        return [(0x2007, ciff_value(0x2007, thumb)), (0x2008, ciff_value(0x2008, thumb))]
    if kind == 'unmapped':
        # 0x08ff is not in crwMapping_ at all and 0x2005 is a plain root-level component
        # with no mapping row; 0x1810 (mapped in 0x300a) is filed under 0x2807 and 0x080a
        # (mapped in 0x2807) under 0x300b, which the (dir, tag) lookup treats as unmapped.
        return [(0x300a, ciff_directory(0x300a, [
            (0x08ff, ciff_value(0x08ff, b'B11Unmapped')),
            (0x2807, ciff_directory(0x2807, [
                (0x1810, ciff_value(0x1810, struct.pack('<7I', WIDTH, HEIGHT, 0, 0, 0, 0, 0)))])),
            (0x300b, ciff_directory(0x300b, [
                (0x080a, ciff_value(0x080a, MAKE_MODEL))]))])),
            (0x2005, ciff_value(0x2005, struct.pack('<2I', 1, 1)))]
    raise ValueError(f'unknown CRW tree kind {kind!r}')


def encode_crw(kind):
    region = render_ciff_directory(crw_tree(kind))
    root_offset = 26
    head = struct.pack('<2sI', b'II', root_offset) + CIFF_SIGNATURE
    if root_offset < 14 or root_offset > root_offset + len(region):
        raise ValueError('CIFF root offset outside the file')
    return head + bytes([0x20] * (root_offset - len(head))) + region


# ------------------------------------------------------------------------ small formats
def encode_bmp(version):
    """A BMP with a 40-byte BITMAPINFOHEADER or a 12-byte OS/2 BITMAPCOREHEADER."""
    if version == 'dib':
        header = struct.pack('<IiiHHIIiiII', 40, 64, 48, 1, 24, 0, 0, 0, 0, 0, 0)
    elif version == 'os2':
        header = struct.pack('<IHHHH', 12, 48, 32, 1, 24)
    else:
        raise ValueError(f'unknown BMP version {version!r}')
    data_at = 14 + len(header)
    size = data_at + 1                      # one byte of pixel data; nothing decodes it
    return struct.pack('<2sIHHI', b'BM', size, 0, 0, data_at) + header + b'\x00'


def encode_gif(version):
    if version == 'lsd':
        return b'GIF89a' + struct.pack('<HH', 64, 48) + b'\x00\x00\x00'
    if version == 'truncated':
        return b'GIF87a'                     # exactly six bytes: the four-byte read fails
    raise ValueError(f'unknown GIF version {version!r}')


TGA_NAME_ONLY_BODY = b'B11 name-only TGA probe'


def encode_tga(version):
    """A full TGA (header + footer) or a name-only body with neither."""
    footer = bytes(8) + TGA_FOOTER_SIGNATURE + b'.\x00'
    if version in ('full', 'wrong-name'):
        # 18 bytes: idLength, colour-map type, image type, colour-map entry, origin x/y,
        # width, height, bits per pixel, image descriptor.
        header = struct.pack('<BBBHHBHHHHBB', 0, 0, 2, 0, 0, 0, 0, 0, 80, 60, 24, 0x20)
        if len(header) != 18:
            raise ValueError('a TGA header is 18 bytes')
        return header + footer
    if version == 'name-only':
        return TGA_NAME_ONLY_BODY
    raise ValueError(f'unknown TGA version {version!r}')


def tga_name_only_dimensions():
    """What the pinned reader will report for the name-only bytes, computed not asserted."""
    return struct.unpack_from('<HH', TGA_NAME_ONLY_BODY, 12)


# ---------------------------------------------------------------------------- containers
def _atom(tag, payload=b''):
    return struct.pack('>I', 8 + len(payload)) + tag.encode('latin-1') + payload


def encode_mov(brand):
    """``ftyp`` + ``wide`` + an empty ``moov``, carrying one brand at offset 8.

    ``isQTimeType`` reads 12 bytes and matches on (atom tag, brand); everything past that is
    the reader's problem, and the reader's problem is what this batch observes.
    """
    if len(brand) != 4:
        raise ValueError('a QuickTime brand is four bytes')
    ftyp = _atom('ftyp', brand.encode('latin-1') + struct.pack('>I', 0))
    return ftyp + _atom('wide', struct.pack('>I', 0)) + _atom('moov')


def encode_mkv():
    """EBML magic plus a header element carrying a DocType of ``matroska``."""
    doctype = b'\x42\x82\x88matroska'
    header_body = b'\x42\x81\x81\x04' + doctype
    return MKV_MAGIC + b'\x1f\x4d\x9b\x74' + bytes([0x80 + len(header_body)]) + header_body


def encode_riff(kind):
    """``RIFF`` + fourcc + chunks; isRiffType stops at the fourcc."""
    if kind == 'avi':
        avih = struct.pack('<13I', 33333, 0, 0, 0x10, 64, 48, 0, 0, 1, 24, 0, 0, 0)
        hdrl = b'LIST' + struct.pack('<I', 4 + len(b'avih') + len(avih)) + b'avih' \
            + struct.pack('<I', len(avih)) + avih
        body = b'AVI ' + b'LIST' + struct.pack('<I', len(hdrl)) + hdrl
    elif kind == 'wav':
        fmt = struct.pack('<HHIIHH', 1, 1, 8000, 8000, 1, 8)
        body = b'WAVE' + b'fmt ' + struct.pack('<I', len(fmt)) + fmt
    else:
        raise ValueError(f'unknown RIFF kind {kind!r}')
    return b'RIFF' + struct.pack('<I', 4 + len(body)) + body


def encode_asf():
    """The 100-byte ASF Header Object with no sub-objects."""
    fixed = 16 + 8 + 4 + 2 + 2
    return ASF_HEADER_GUID + struct.pack('<QIHH', 100, 0, 2, 1) + bytes(100 - fixed)


BUILDERS = {
    'ciff-mapped-v1': lambda: encode_crw('mapped'),
    'ciff-thumb-only-v1': lambda: encode_crw('thumb'),
    'ciff-preview-plus-thumb-v1': lambda: encode_crw('preview-thumb'),
    'ciff-unmapped-only-v1': lambda: encode_crw('unmapped'),
    'bmp-dib-v3': lambda: encode_bmp('dib'),
    'bmp-os2-coreheader-v1': lambda: encode_bmp('os2'),
    'gif-lsd-v1': lambda: encode_gif('lsd'),
    'gif-truncated-v1': lambda: encode_gif('truncated'),
    'tga-full-v1': lambda: encode_tga('full'),
    'tga-name-only-v1': lambda: encode_tga('name-only'),
    'tga-signature-wrong-name-v1': lambda: encode_tga('wrong-name'),
    'qtime-known-brand-v1': lambda: encode_mov('qt  '),
    'qtime-unknown-brand-v1': lambda: encode_mov(UNKNOWN_BRAND),
    'mkv-ebml-header-v1': encode_mkv,
    'riff-avi-avih-v1': lambda: encode_riff('avi'),
    'riff-wav-fmt-v1': lambda: encode_riff('wav'),
    'asf-header-guid-v1': encode_asf,
}


# --------------------------------------------------------------------------- recognizers
def parse_ciff(data):
    """An independent transcription of CiffHeader::read, raising where the reference raises."""
    if len(data) < 14:
        raise ValueError('CIFF: file shorter than the 14-byte header')
    if data[0] != data[1] or data[0:1] not in (b'I', b'M'):
        raise ValueError('CIFF: no II/MM byte-order mark')
    byte_order = '<' if data[0:1] == b'I' else '>'
    root_offset = struct.unpack_from(byte_order + 'I', data, 2)[0]
    if root_offset < 14 or root_offset > len(data):
        raise ValueError('CIFF: root offset outside the file')
    if data[6:14] != CIFF_SIGNATURE:
        raise ValueError('CIFF: signature is not HEAPCCDR')
    region = data[root_offset:]
    return dict(byteOrder='littleEndian' if byte_order == '<' else 'bigEndian',
                rootOffset=root_offset, padding=root_offset - 14, regionLength=len(region),
                tree=_read_ciff_directory(region, byte_order, 0x0000, root_offset))


def _read_ciff_directory(region, byte_order, dir_tag, base):
    """``base`` is where ``region`` starts in the file: entry offsets are region-relative."""
    """Mirrors CiffDirectory::readDirectory, including its three enforce()s."""
    if len(region) < 4:
        raise ValueError(f'CIFF: directory 0x{dir_tag:04x} region shorter than four bytes')
    count_at = struct.unpack_from(byte_order + 'I', region, len(region) - 4)[0]
    if count_at > len(region) - 2:
        raise ValueError(f'CIFF: directory 0x{dir_tag:04x} count offset outside the region')
    count = struct.unpack_from(byte_order + 'H', region, count_at)[0]
    cursor = count_at + 2
    if count * 10 > len(region) - cursor:
        raise ValueError(f'CIFF: directory 0x{dir_tag:04x} entries run past the region')
    directory = {'tag': dir_tag, 'entries': []}
    for _ in range(count):
        tag = struct.unpack_from(byte_order + 'H', region, cursor)[0]
        kind = ciff_type_id(tag)
        location = ciff_data_location(tag)
        if location == 'valueData':
            size = struct.unpack_from(byte_order + 'I', region, cursor + 2)[0]
            offset = struct.unpack_from(byte_order + 'I', region, cursor + 6)[0]
            if offset < cursor:
                if size > cursor - offset:
                    raise ValueError(f'CIFF: 0x{tag:04x} payload overlaps its entry')
            elif offset < cursor + 10 or offset > len(region) or size > len(region) - offset:
                raise ValueError(f'CIFF: 0x{tag:04x} offset out of range')
        else:
            size, offset = 8, cursor + 2
        entry = {'tag': tag, 'dir': dir_tag, 'type': kind, 'location': location,
                 'size': size, 'offset': offset, 'at': base + offset, 'entryAt': base + cursor}
        if kind == 'directory':
            entry['children'] = _read_ciff_directory(region[offset:offset + size], byte_order,
                                                      tag, base + offset)
        directory['entries'].append(entry)
        cursor += 10
    return directory


def walk_ciff(directory):
    for entry in directory['entries']:
        yield entry
        if entry.get('children'):
            yield from walk_ciff(entry['children'])


def mapped_components(parsed):
    """The components the pinned crwMapping_ table reaches, by (parent dir, tag id)."""
    out = []
    for entry in walk_ciff(parsed['tree']):
        kind = CIFF_MAPPED.get((entry['dir'], entry['tag'] & CIFF_ID_MASK))
        if kind:
            out.append((kind, entry))
    return out


def is_crw(data, _name):
    try:
        parse_ciff(data)
    except ValueError:
        return False
    return True


def _tiff_magic(data, _name=None):
    if data[:2] not in (b'II', b'MM') or len(data) < 4:
        return False
    return struct.unpack_from('<H' if data[:2] == b'II' else '>H', data, 2)[0] in TIFF_MAGICS


REGISTRY = (
    ('jpeg', lambda data, name: data[:2] == b'\xff\xd8'),
    # isExvType (src/jpgimage.cpp:1055) wants FF 01 plus the five-byte exv id.
    ('exv', lambda data, name: data[:2] == b'\xff\x01'),
    # cr2, and every TIFF-alias entry (dng, nef, pef, arw, sr2, srw), are covered by the
    # combined TIFF check below: none of them can match bytes that are not II/MM plus one of
    # the accepted magics, and that is the only thing this module has to rule out.
    ('crw', is_crw),
    ('mrw', lambda data, name: data[:4] == b'\x00MRM'),
    ('tiff', _tiff_magic),
    ('orf', lambda data, name: data[:4] in (b'IIRO', b'MMOR')),
    ('webp', lambda data, name: data[:4] == b'RIFF' and data[8:12] == b'WEBP'),
    ('png', lambda data, name: data[:8] == b'\x89PNG\r\n\x1a\n'),
    ('pgf', lambda data, name: data[:3] == b'PGF'),
    ('raf', lambda data, name: data[:8] == b'FUJIFILM'),
    ('eps', lambda data, name: data[:4] in (b'%!PS', b'\xc5\xd0\xd3\xc6')),
    ('xmp', lambda data, name: data[:8] in (b'<?xpack', b'<x:xmpm', b'<?xml ')),
    ('gif', lambda data, name: data[:6] in (b'GIF87a', b'GIF89a')),
    ('psd', lambda data, name: data[:6] == b'8BPS\x00\x01'),
    # isTgaType (src/tgaimage.cpp:99): the name first, and the footer route gated behind a
    # stream position the factory never has. advance is ignored, so the footer branch is
    # unreachable through ImageFactory::open, and a fixture proves it by dispatching to none.
    ('tga', lambda data, name: name.endswith('.tga') or name.endswith('.TGA')),
    ('bmp', lambda data, name: data[:2] == b'BM'),
    ('jp2', lambda data, name: data[:12] == b'\x00\x00\x00\x0cjP2 \x0d\x0a\x87\x0a'),
    ('qtime', lambda data, name: len(data) >= 12
     and data[4:8].decode('latin-1') in QTIME_ATOMS
     and data[8:12].decode('latin-1') in QTIME_BRANDS),
    ('asf', lambda data, name: data[:16] == ASF_HEADER_GUID),
    ('riff', lambda data, name: data[:4] == b'RIFF'),
    ('mkv', lambda data, name: data[:4] == MKV_MAGIC),
    ('bmff', lambda data, name: len(data) >= 8 and data[4:8] in (b'ftyp', b'JXL ')),
)


def recognizers(data, name):
    """Which recognisers accept these bytes, and which one the registry reaches first."""
    matched = [label for label, check in REGISTRY if check(data, name)]
    return dict(matched=matched, dispatch=matched[0] if matched else 'none')


# ----------------------------------------------------------------------------- structure
def expected_dispatch(layout, name):
    """The reader the pinned registry must reach for one fixture, per the review."""
    if layout == 'qtime-unknown-brand-v1':
        return 'bmff'                       # the brand, not the extension, decides
    if layout == 'tga-signature-wrong-name-v1':
        return 'none'                       # name test fails, footer route is unreachable
    fmt = FIXTURES[layout][0]
    return {'CRW': 'crw', 'BMP': 'bmp', 'GIF': 'gif', 'TGA': 'tga', 'MOV': 'qtime',
            'MKV': 'mkv', 'AVI': 'riff', 'WAV': 'riff', 'ASF': 'asf'}[fmt]


def structure(layout, name, data):
    """Independent framing facts for one B11 fixture; raises if it is not self-consistent."""
    fmt = FIXTURES[layout][0]
    rec = recognizers(data, name)
    want = expected_dispatch(layout, name)
    if rec['dispatch'] != want:
        raise ValueError(f'{name}: dispatch reached {rec["dispatch"]}, not {want}')
    out = {'dispatch': rec['dispatch'], 'recognizers': rec, 'fileLength': len(data),
           'fileSha256': digest(data)}
    if fmt == 'CRW':
        parsed = parse_ciff(data)
        out['byteOrder'] = parsed['byteOrder']
        out['ciff'] = {key: parsed[key] for key in ('rootOffset', 'padding', 'regionLength')}
        reached = mapped_components(parsed)
        keys = sorted({kind for kind, _ in reached})
        out['mappedKeys'] = keys
        expected = {'ciff-mapped-v1': ['colorspace', 'comment', 'datetime', 'dimensions',
                                       'distance', 'file-number', 'firmware', 'make-model',
                                       'owner-name'],
                    'ciff-thumb-only-v1': ['thumbnail'],
                    'ciff-preview-plus-thumb-v1': ['preview-offset', 'thumbnail'],
                    'ciff-unmapped-only-v1': []}[layout]
        if keys != expected:
            raise ValueError(f'{name}: mapped components are {keys}, not {expected}')
        for kind, entry in reached:
            payload = data[entry['at']:entry['at'] + entry['size']]
            if kind == 'thumbnail':
                if payload != owned.thumb_jpeg_bytes():
                    raise ValueError(f'{name}: the 0x2008 payload is not the owned thumbnail')
                out['thumbnailLength'] = len(payload)
                out['thumbnailSha256'] = digest(payload)
            if kind == 'preview-offset':
                out['previewLength'] = len(payload)
            if kind == 'file-number':
                if struct.unpack_from('<I', payload, 0)[0] != FILE_NUMBER:
                    raise ValueError(f'{name}: 0x1817 does not lead with the file number')
                if entry['size'] != 8:
                    raise ValueError(f'{name}: 0x1817 must claim eight bytes for the size '
                                     'override to be observable')
            if kind == 'dimensions':
                if struct.unpack_from('<2I', payload, 0) != (WIDTH, HEIGHT):
                    raise ValueError(f'{name}: 0x1810 does not carry the authored dimensions')
        if out.get('previewLength') != out.get('thumbnailLength') and 'previewLength' in out:
            raise ValueError(f'{name}: preview and thumbnail payloads were meant to match')
    elif fmt == 'BMP':
        if len(data) < 26:
            raise ValueError(f'{name}: BmpImage::readMetadata would read short')
        header_size = struct.unpack_from('<I', data, 14)[0]
        if header_size == 12:
            width, height = struct.unpack_from('<HH', data, 18)
        else:
            width, height = struct.unpack_from('<ii', data, 18)
        # The reader only ever looks at bytes 0..25, so the depth field's offset is the
        # thing that differs between the two header variants it branches on.
        depth_at = 24 if header_size == 12 else 28
        if len(data) < depth_at + 2:
            raise ValueError(f'{name}: the depth field is past the end of the file')
        out['bmp'] = {'headerSize': header_size, 'width': width, 'height': height,
                      'bitsPerPixel': struct.unpack_from('<H', data, depth_at)[0],
                      'dataOffset': struct.unpack_from('<I', data, 10)[0]}
        if out['bmp']['dataOffset'] + 1 > len(data):
            raise ValueError(f'{name}: the pixel data offset runs past the file')
    elif fmt == 'GIF':
        lsd = min(4, max(0, len(data) - 6))
        out['gif'] = {'signature': data[:6].decode('latin-1'), 'lsdBytes': lsd}
        if lsd == 4:
            out['gif']['width'], out['gif']['height'] = struct.unpack_from('<HH', data, 6)
            if len(data) != 13:
                raise ValueError(f'{name}: a GIF with a logical screen descriptor is 13 bytes '
                                 'here and nothing else')
        else:
            out['gif']['width'] = out['gif']['height'] = None
            if len(data) != 6:
                raise ValueError(f'{name}: a truncated GIF is exactly six bytes here')
    elif fmt == 'TGA':
        width = height = None
        if len(data) >= 18:
            width, height = struct.unpack_from('<HH', data, 12)
        footer_at = len(data) - 26
        signed = footer_at >= 0 and data[footer_at + 8:footer_at + 24] == TGA_FOOTER_SIGNATURE
        out['tga'] = {'footerSigned': signed, 'width': width, 'height': height,
                      'recognisedByName': name.endswith(('.tga', '.TGA')),
                      'footerRouteReachableByFactory': False}
        if layout in ('tga-full-v1', 'tga-signature-wrong-name-v1'):
            if not signed or (width, height) != (80, 60):
                raise ValueError(f'{name}: the full TGA must carry its footer and header')
        if layout == 'tga-name-only-v1':
            if signed or (width, height) != tga_name_only_dimensions():
                raise ValueError(f'{name}: the name-only dimensions are not what the bytes say')
    elif fmt == 'MOV':
        brand = data[8:12].decode('latin-1')
        out['mov'] = {'atom': data[4:8].decode('latin-1'), 'brand': brand,
                      'brandKnownToQuickTime': brand in QTIME_BRANDS,
                      # isBmffType (:794) accepts any ftyp at offset 4, so *both* movies match
                      # it; the registry lists quicktime (:109) before bmff (:115), which is the
                      # only reason the known brand is read as QuickTime. Fall-through, not
                      # exclusion, is what happens to the unknown one.
                      'bmffAlsoAccepts': 'bmff' in recognizers(data, name)['matched']}
        if out['mov']['brandKnownToQuickTime'] != (layout == 'qtime-known-brand-v1'):
            raise ValueError(f'{name}: the brand vocabulary disagrees with the layout name')
    elif fmt == 'MKV':
        out['matroska'] = {'magic': data[:4].hex(), 'headerId': data[4:8].hex()}
    elif fmt in ('AVI', 'WAV'):
        out['riff'] = {'fourcc': data[8:12].decode('latin-1')}
    elif fmt == 'ASF':
        out['asf'] = {'headerGuid': data[:16].hex(),
                      'objectSize': struct.unpack_from('<Q', data, 16)[0],
                      'subobjects': struct.unpack_from('<I', data, 24)[0]}
        if out['asf']['objectSize'] != len(data):
            raise ValueError(f'{name}: the ASF header object size is not the file size')
    return out


def entries():
    result = []
    for layout in SEED_LAYOUTS:
        fmt, name = FIXTURES[layout]
        facts = structure(layout, name, (DIRECTORY / name).read_bytes())
        entry = dict(file=name, format=fmt, layout=layout, length=facts['fileLength'],
                     sha256=facts['fileSha256'], rights='owned-synthetic', recipe=RECIPE,
                     dispatch=facts['dispatch'], carriers=FORMATS[fmt]['carriers'],
                     vendorIdentity=FORMATS[fmt]['identity'], decodable=False,
                     metadata='ciff-components' if fmt == 'CRW' else 'none')
        for key in ('mappedKeys', 'thumbnailLength', 'thumbnailSha256', 'previewLength',
                    'byteOrder', 'ciff', 'bmp', 'gif', 'tga', 'mov', 'matroska', 'riff', 'asf'):
            if key in facts:
                entry[key] = facts[key]
        result.append(entry)
    for layout in BLOCKED_LAYOUTS:
        result.append(dict(file=None, format='CRW', layout=layout, rights='not-admitted',
                           recipe=BLOCKED_REASON, dispatch='crw', carriers='none',
                           decodable=False, metadata='none', blocked=True))
    return result


def verify():
    """Re-derive every fixture from this module and check bytes plus framing."""
    for layout, (_fmt, name) in FIXTURES.items():
        data = (DIRECTORY / name).read_bytes()
        if data != BUILDERS[layout]():
            raise ValueError(f'{name} differs from its authored bytes')
        facts = structure(layout, name, data)
        if facts['dispatch'] != expected_dispatch(layout, name):
            raise ValueError(f'{name}: dispatch does not match the container it claims')
        if layout == 'tga-signature-wrong-name-v1':
            if data != BUILDERS['tga-full-v1']():
                raise ValueError(f'{name}: the wrong-name twin must be byte-identical to '
                                 'owned-tga-full.tga, or it proves nothing about the name')
            if facts['dispatch'] != 'none':
                raise ValueError(f'{name}: a footer-signed .targa must reach no reader at all')
    recorded = json.loads(MANIFEST.read_text(encoding='utf-8')) if MANIFEST.exists() else None
    if recorded != entries():
        raise ValueError('B11 census fixture manifest differs from the fixture bytes')
    return recorded


def main():
    if sys.argv[1:] == ['generate']:
        DIRECTORY.mkdir(parents=True, exist_ok=True)
        for _layout, (_fmt, name) in sorted(FIXTURES.items()):
            (DIRECTORY / name).write_bytes(BUILDERS[_layout]())
        MANIFEST.write_text(json.dumps(entries(), indent=2) + '\n', encoding='utf-8')
        print(f'wrote {len(FIXTURES)} fixtures and {MANIFEST.name}')
    elif sys.argv[1:] != ['verify']:
        raise SystemExit('expected verify or generate')
    manifest = verify()
    print(f'B11 census fixture catalog verified: {len(manifest)} entries')


if __name__ == '__main__':
    main()
