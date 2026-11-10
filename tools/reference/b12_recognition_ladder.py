"""B12 fixtures: a byte-length ladder over three recognizers, to locate a boundary, not to cover formats.

Standard library only. Every file here is authored by this module -- no camera capture, no vendor
sample, no decodable media -- and each rung of a ladder is the *same* fixed bytes at a different
total length. That is the whole design. B11 recorded seven small fixtures that the pinned build's
factory handed to no reader, and its review left the cause unresolved between candidates that make
different predictions about *length*. A ladder measures which length owns the flip, so the question
can be settled from pinned-build observations alone -- no debugger, and no support claim either way.

Why these three formats (reviewed in docs/research/E01_B12_RIGHTS_AND_ORACLE_REVIEW.md):

* BMP  -- ``isBmpType`` (src/bmpimage.cpp:119) reads 2 bytes, ``if (iIo.error() || iIo.eof())
  return false;``, and only afterwards seeks back. Registry index 23.
* GIF  -- ``isGifType`` (src/gifimage.cpp:75) is the same shape with 6 bytes. Registry index 20, one
  entry after the recognizer B11's leading candidate blames. If both flip at the same size the cause
  is upstream of both; if only BMP flips it is per-entry.
* TGA under a ``.targa`` name -- ``isTgaType`` (src/tgaimage.cpp:99) ignores ``advance``, tests the
  file *name* first, and otherwise returns false as soon as ``iIo.tell() < 26``. Registry index 22,
  and the name test is deliberately unreachable for a ``.targa`` twin.

The candidate windows the ladder is sized against, transcribed from the pinned source rather than
remembered: 15 of the 17 recognizers whose bodies this review could read are written "read N, return
false if eof or error, *then* seek back", so a file shorter than N strands the cursor for every later
entry and those entries return false without ever comparing magic. The one that owns the boundary for
these three formats is ``isXmpType`` (src/xmpsidecar.cpp:156-198): it reads ``strlen(xmlHeader) + 1`` =
55 bytes, then ``80 - 55`` more, and both of its ``return false`` paths precede the single restore.
Earlier entries own the smaller steps (``isCr2Type`` 16 at index 2, ``isRw2Type`` 24 at index 11), and
``isEpsType`` (src/epsimage.cpp:1102) is a third shape: it asks ``DataBuf FileIo::read(size_t)``
(src/basicio.cpp:530-540) for 26 bytes and that overload *throws* -- so under 26 bytes the scan does not
refuse, it aborts. Sizes 8 through 128 cross every boundary that model produces, and the ``bmp-xmltail``
control (``<?xml `` planted at offset 30, inside the window xmp reads before it decides and past the 26
bytes the BMP reader consumes) tests whether the mechanism depends on the bytes at all rather than on how
many of them there are.

Nothing here is a format-support statement, in either direction: a refusal on a 24-byte file says
what this build does with 24 bytes.
"""
import hashlib
import json
from pathlib import Path
import re
import struct
import sys

ROOT = Path(__file__).resolve().parents[2]
DIRECTORY = ROOT / 'tests/fixtures/b12'
MANIFEST = DIRECTORY / 'ladder-manifest.json'

# Sizes bracketing every candidate window named above. 8 bytes is the floor -- below the 6-byte GIF
# test, so the shortest rung has to refuse for a reason the ladder can still attribute -- and 128 is
# comfortably past every window, the top rung each format is predicted to accept.
SIZES = (8, 12, 16, 20, 24, 40, 54, 55, 56, 64, 79, 80, 81, 96, 128)

# The registry in pinned order, with the length of the window each recognizer touches first (None
# where the test is not a fixed-size read). Index and window come from src/image.cpp:75-116 plus the
# sources cited in the module docstring.
REGISTRY = (
    ('jpeg', 0, 2), ('exv', 1, 7), ('cr2', 2, 16), ('crw', 3, 14), ('mrw', 4, 4),
    ('tiff', 5, 8), ('webp', 6, 12), ('dng', 7, 8), ('nef', 8, 8), ('pef', 9, 8),
    ('arw', 10, 8), ('rw2', 11, 24), ('sr2', 12, 8), ('srw', 13, 8), ('orf', 14, 8),
    ('png', 15, 8), ('pgf', 16, 3), ('raf', 17, 8), ('eps', 18, None), ('xmp', 19, 80),
    ('gif', 20, 6), ('psd', 21, 6), ('tga', 22, 26), ('bmp', 23, 2), ('jp2', 24, None),
    ('qtime', 25, None), ('asf', 26, 16), ('riff', 27, 12), ('mkv', 28, 4), ('bmff', 29, None),
)
INDEX = {name: index for name, index, _window in REGISTRY}
XMP_WINDOW = 80          # src/xmpsidecar.cpp:165 -- const int32_t len = 80
STRANDED_FORMATS = tuple(name for name, index, _ in REGISTRY if index > INDEX['xmp'])

# The flag pair this batch records. B12 observes recognition only, so it never asks the build to
# write: -pS prints the image type the factory reached, -pe is the documented read path, and both
# are read-only. Adding -pa/-px would let one failure mode (a write that half-happens) contaminate
# a measurement whose whole point is that nothing about the file changed.
FLAGS = ('-pS', '-pe')


def pad(length, prefix, filler=b'\x00'):
    """`prefix` followed by filler out to exactly `length` bytes, or `prefix` truncated to fit.

    Every rung of one ladder is therefore a byte-for-byte extension of the rung below it, so a
    difference in outcome between two adjacent sizes can only come from the bytes the longer file
    exposes to a recognizer -- never from a header that drifted.
    """
    if len(prefix) >= length:
        return prefix[:length]
    return prefix + filler * (length - len(prefix))


XML_PLANT = b'<?xml '
# The first byte ``BmpImage::readMetadata`` never consults: it reads 26, so a plant at 30 changes
# recognition only. Anything inside the header would also change whether the DIB is *valid*, which
# is a second axis and would make a refusal at that rung uninterpretable.
XML_PLANT_OFFSET = 30


def xml_plant(data, offset=XML_PLANT_OFFSET, text=XML_PLANT):
    """Overwrite `data[offset:offset + len(text)]` with `text`, keeping the length fixed."""
    return data[:offset] + text + data[offset + len(text):]



def bmp_header(width=4, height=2, planes=1, depth=24, compression=0, header_size=40):
    """The 54-byte Windows ``BITMAPINFOHEADER``, which is all ``BmpImage::readMetadata`` reads.

    ``readMetadata`` (src/bmpimage.cpp:42) reads 26 bytes and branches on the ``u32`` at offset 14:
    12 means the OS/2 core header with 16-bit dimensions, anything else is read as these 32-bit
    fields. No pixel array is authored, so no B11 carrier tokens are present by construction.
    """
    # 2s + thirteen 4-byte and 2-byte fields = 54 bytes, the layout described at src/bmpimage.cpp:60.
    return struct.pack('<2sIIIiIiHHIIIIII', b'BM', 54 + width * height * (depth // 8), 0, 54,
                       header_size, width, height, planes, depth, compression,
                       width * height * (depth // 8), 0, 0, 0, 0)


def gif_header(version=b'GIF87a', width=2, height=1):
    """Six signature bytes plus the logical screen descriptor, 13 bytes in all."""
    return version + struct.pack('<HHBBB', width, height, 0, 0, 0)


# An 18-byte TGA image header (no colour map, image type 2) and the 26-byte version-2 footer:
# 8 bytes of extension/developer offsets, the 16-character signature ``isTgaType`` compares at
# ``buf + 8`` of the last 26 bytes, and the two terminator bytes (src/tgaimage.cpp:99-122). The
# footer is exactly 26 bytes because that is what the recognizer reads, and a footer one byte off
# centre would make every refusal over-determined by alignment alone.
TGA_HEAD = struct.pack('<BBBHHBHHHHBB', 0, 0, 2, 0, 0, 0, 0, 0, 4, 2, 24, 0x20)
TGA_FOOTER = b'\x00' * 8 + b'TRUEVISION-XFILE' + b'.\x00'
TGA_MINIMUM = len(TGA_HEAD) + len(TGA_FOOTER)


def tga_body(size):
    """Header, zero fill, footer at the last 26 bytes -- or a front-truncated head below that.

    Padding goes in the *middle* on purpose: appending after the footer would move the signature,
    and prepending would move the head, so either way a refusal would be over-determined by
    alignment rather than by length. Below ``TGA_MINIMUM`` the two fixed regions cannot both fit and
    keep their offsets, so those rungs are labelled non-attributable instead of quietly included.
    """
    if size >= TGA_MINIMUM:
        return TGA_HEAD + b'\x00' * (size - TGA_MINIMUM) + TGA_FOOTER
    return (TGA_HEAD + TGA_FOOTER)[-size:]


BUILDERS = {
    'bmp': lambda size: pad(size, bmp_header()),
    'gif': lambda size: pad(size, gif_header()),
    'targa': tga_body,
    # The content control: `<?xml ` substituted at offset 30, inside the 55-byte window isXmpType
    # reads before it decides and beyond the 26 bytes BmpImage::readMetadata consults, so the only
    # thing it varies is what a recognizer sees, not whether the DIB is valid. The strand model says
    # the plant buys nothing -- under 80 bytes the cursor strands on the *short read*, before any
    # content is compared -- so the prediction is "refused at 40, reached at 96", identical to the
    # plain BMP ladder. If the build reaches the 40-byte one, the mechanism reads content rather than
    # length, and the boundary story in the review is wrong.
    'bmp-xmltail': lambda size: xml_plant(pad(size, bmp_header())),
}

LADDER = tuple((kind, size) for kind in ('bmp', 'gif', 'targa') for size in SIZES) + (
    ('bmp-xmltail', 40), ('bmp-xmltail', 96))


def layout_for(kind, size):
    return f'{kind}-len{size:03d}-v1'


def name_for(kind, size):
    extension = 'targa' if kind == 'targa' else kind.split('-')[0]
    return f'owned-{kind}-len{size:03d}.{extension}'


def fixtures():
    out = {}
    for kind, size in LADDER:
        out[layout_for(kind, size)] = (kind.upper().split('-')[0], name_for(kind, size), kind, size)
    return out


FIXTURES = fixtures()


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def structure(kind, name, data):
    """Parse this module's own bytes back out, independently of any builder variable.

    Every field is optional *for this parser*: a rung can be shorter than the header it declares --
    that is the condition under test -- so the parse degrades instead of throwing.
    """
    kind = kind.upper()
    facts = {'length': len(data), 'sha256': sha256(data), 'signature': data[:6].hex()}
    if kind.startswith('BMP'):
        facts['signatureText'] = data[:2].decode('latin-1')
        if len(data) >= 18:
            file_size, _reserved, offset, header_size = struct.unpack('<IIIi', data[2:18])
            facts.update(headerSize=header_size, bitmapOffset=offset, declaredFileSize=file_size,
                         consistentLength=file_size == len(data))
        if len(data) >= 30:
            width, height, planes, depth = struct.unpack('<IiHH', data[18:30])
            facts.update(pixelWidth=width, pixelHeight=height, planes=planes, depth=depth)
    elif kind == 'GIF':
        facts['signatureText'] = data[:6].decode('latin-1')
        facts['hasLogicalScreenDescriptor'] = len(data) >= 13
    elif kind == 'TARGA':
        tail = data[-26:] if len(data) >= 26 else b''
        facts['footerSignature'] = tail[8:24].decode('latin-1') or None
        # Reported separately on purpose: a 26..43-byte rung does carry the signature at the end, but
        # its image header is truncated, so a refusal there cannot be attributed to anything but the
        # file being too short to be a TGA at all.
        facts['footerAtEof'] = len(data) >= 26 and tail[8:24] == b'TRUEVISION-XFILE'
        facts['headAtOffset0'] = data[:len(TGA_HEAD)] == TGA_HEAD
        facts['bothRegionsCanonical'] = facts['footerAtEof'] and facts['headAtOffset0']
        facts['nameMatchesRecognizer'] = bool(re.search(r'\.(tga|TGA)', name))
        # The short-circuit that makes this ladder a negative control: isTgaType returns false for a
        # non-``.tga`` name as soon as it sees ``tell() < 26``, and the factory calls every
        # recognizer on a freshly opened stream at position 0. The footer is therefore unreachable at
        # *any* length, which is a different mechanism from the strand the other two ladders measure.
        facts['positionTestPassesDuringScan'] = False
    return facts


EPS_READ_BYTES = 26     # max(dosEpsSignature.size(), longest EpsImage::epsFirstLine entry)


def first_strand(target_index, size):
    """The first registry entry before `target_index` whose opening read is longer than `size`.

    A recognizer written as "read N, bail if eof, *then* seek back" strands the stream at N (or at
    `size`, for a short file) with the eof indicator set, so every later entry reads zero bytes and
    a later entry with the same shape returns false without ever comparing magic. This is our model
    of the pinned source, transcribed entry by entry in the review document, and the ladder exists to
    test it: the shape holds for 15 of the 17 recognizers whose bodies this review could read
    (bmp, cr2, crw, gif, jpeg, exv, mrw, orf, pgf, png, psd, raf, rw2, tiff, xmp), and fails for
    ``isEpsType`` (it seeks back when the read is short) and ``isJp2Type`` (no guard at all).
    """
    for name, index, window in REGISTRY:
        if index >= target_index:
            break
        if window and size < window:
            return name, index, window
    return None, None, None


def strand_prediction(kind, size):
    target = 'tga' if kind == 'targa' else ('bmp' if kind == 'bmp-xmltail' else kind)
    name, index, window = first_strand(INDEX[target], size)
    return {'target': target, 'strandedBy': name, 'strandedIndex': index, 'ownerWindow': window}


def predicted_family(kind, size):
    """The outcome *family* the strand model predicts for this rung -- a class of message, not a string.

    Families are the only honest unit of comparison. `Image::printStructure` is overridden by just
    nine registry classes (cr2, jp2, jpg, orf, png, raf, rw2, tiff, webp; src/image.cpp:136-138), so
    for BMP, GIF and TGA the two -pS results are a clean binary: "Image type <path> is not supported"
    proves a reader was reached, "<path>: The file contains data of an unknown image type" proves the
    scan matched nothing (src/image.cpp:825-830) -- or matched and got no image back from
    ``newInstance_``, which the factory cannot tell apart (:843-848).
    """
    strand = strand_prediction(kind, size)
    target = strand['target']
    if size < EPS_READ_BYTES and INDEX[target] > INDEX['eps']:
        # eps at index 18 reads through ``DataBuf FileIo::read(size_t)`` (src/basicio.cpp:530-540),
        # which throws when the request exceeds the file size or yields nothing. So a file this short
        # produces an exception *inside* the scan for every format in this batch -- a refusal by
        # neither magic nor length as such, and a reason the small rungs must not be read as
        # format statements about anything.
        return {'family': 'throws-during-scan', 'entry': 'eps',
                'note': f'read({EPS_READ_BYTES}) on a {size}-byte file throws, so no later entry runs'}
    if strand['strandedBy']:
        return {'family': 'unknown-image-type', 'entry': strand['strandedBy'],
                'note': f"cursor stranded by index {strand['strandedIndex']} (window "
                        f"{strand['ownerWindow']}) before {target} at {INDEX[target]}"}
    if target == 'tga':
        # No strand before it, and still no match: the name test cannot fire on a `.targa` twin and
        # the position test fails because the factory calls every recognizer on a freshly opened
        # stream at 0. If a rung here is ever recognised, the position was not 0 -- evidence that
        # some earlier entry left the cursor advanced, i.e. that stranding is real and wider than xmp.
        return {'family': 'unknown-image-type', 'entry': None,
                'note': 'name test cannot fire, tell() < 26 at scan position 0'}
    return {'family': 'reader-reached', 'entry': target,
            'note': 'nothing before it strands, so its own magic test runs at position 0'}


def predicted_outcome(kind, size):
    """Recorded prediction: what the strand model says, as data the probe can disagree with.

    Kept apart from `structure()` deliberately -- one is a claim about the build, the other a
    description of bytes this module wrote. The probe compares the two, and that comparison is the
    result; nothing here is evidence of what the build did.
    """
    family = predicted_family(kind, size)
    return {'recognised': family['family'] == 'reader-reached',
            'cause': 'registry-strand' if family['family'] != 'reader-reached' else 'no-strand',
            'attributable': (size >= TGA_MINIMUM) if kind == 'targa' else True,
            'predictedFamily': family['family'], 'predictedEntry': family['entry'],
            'predictedNote': family['note'],
            'basis': 'static reading of the pinned 0.28.9 source; no offline rehearsal'}


def model_recognizers(data, kind):
    """This repository's own model of the magic tests, for the record and for the review's prose.

    Deliberately a *model* and never evidence: B11's model predicted a match for both BMPs and the
    build handed them to no reader, which is the whole reason B12 exists.
    """
    matched = []
    if data[:2] == b'BM':
        matched.append('bmp')
    if data[:6] in (b'GIF87a', b'GIF89a'):
        matched.append('gif')
    if len(data) >= 26 and data[-26:][8:24] == b'TRUEVISION-XFILE':
        matched.append('tga-footer')
    return {'model': sorted(matched), 'registryIndex': INDEX.get(kind.split('-')[0].lower()),
            'xmpWindow': XMP_WINDOW}


def entries():
    out = []
    for layout, (fmt, name, kind, size) in sorted(FIXTURES.items()):
        data = BUILDERS[kind](size)
        out.append({'layout': layout, 'format': fmt, 'file': name, 'kind': kind, 'size': size,
                    'sha256': sha256(data), 'structure': structure(kind, name, data),
                    'prediction': predicted_outcome(kind, size),
                    'recognizers': model_recognizers(data, fmt)})
    return out


def expected_dispatch(data, name, kind):
    """Which reader our *own* model says the factory reaches -- never compared against the build here.

    Like B11's `recognizers`, this is a container-framing check over bytes this module wrote; it
    cannot tell us what the pinned build did, and no B12 claim reads through it.
    """
    if kind == 'bmp' or kind == 'bmp-xmltail':
        return 'bmp' if data[:2] == b'BM' else 'none'
    if kind == 'gif':
        return 'gif' if data[:6] in (b'GIF87a', b'GIF89a') else 'none'
    if kind == 'targa':
        return 'none'          # the name test cannot fire, the position test never passes
    raise ValueError(f'no dispatch model for kind {kind!r}')


def verify():
    """Re-derive every rung from this module and check bytes, framing and the ladder's invariants."""
    manifest = entries()
    for entry in manifest:
        data = (DIRECTORY / entry['file']).read_bytes()
        if data != BUILDERS[entry['kind']](entry['size']):
            raise ValueError(f"{entry['file']} differs from its authored bytes")
        if len(data) != entry['size']:
            raise ValueError(f"{entry['file']} is {len(data)} bytes, manifest says {entry['size']}")
        if sha256(data) != entry['sha256']:
            raise ValueError(f"{entry['file']} digest differs from the manifest")
        if entry['structure'] != structure(entry['kind'], entry['file'], data):
            raise ValueError(f"{entry['file']} structure fields are stale")
        # The magic tests for BMP and GIF are position-independent, so our two models of them have
        # to agree byte for byte; the TGA model is position- and name-dependent and therefore exempt.
        if entry['kind'] in ('bmp', 'gif', 'bmp-xmltail'):
            model = entry['recognizers']['model']
            expected = expected_dispatch(data, entry['file'], entry['kind'])
            if expected != (model[0] if model and model[0] in ('bmp', 'gif') else 'none'):
                raise ValueError(f"{entry['file']} dispatch model and magic model disagree")
    # Each pad-based ladder must be a chain of prefixes: rung n+1 extends rung n.
    for kind in ('bmp', 'gif'):
        rungs = sorted((e['size'], BUILDERS[e['kind']](e['size'])) for e in manifest if e['kind'] == kind)
        for (small, a), (big, b) in zip(rungs, rungs[1:]):
            if b[:len(a)] != a:
                raise ValueError(f'{kind}: the {big}-byte rung does not extend the {small}-byte one')
    # The `.targa` ladder is exempt and for a stated reason: its fixed bytes are the footer, which
    # has to stay at the end, so its rungs grow in the middle and share a suffix instead of a prefix.
    for entry in manifest:
        if entry['kind'] != 'targa' or entry['size'] < TGA_MINIMUM:
            continue
        data = (DIRECTORY / entry['file']).read_bytes()
        if data[:len(TGA_HEAD)] != TGA_HEAD or data[-len(TGA_FOOTER):] != TGA_FOOTER:
            raise ValueError(f"{entry['file']}: the fixed TGA regions must keep their offsets")
        if data[len(TGA_HEAD):-len(TGA_FOOTER)] != b'\x00' * (entry['size'] - TGA_MINIMUM):
            raise ValueError(f"{entry['file']}: only the middle padding may vary between rungs")
    # The content control must differ from its plain twin at exactly the planted window, so a
    # difference in outcome cannot be attributed to any other byte.
    plain = {e['size']: BUILDERS['bmp'](e['size']) for e in manifest if e['kind'] == 'bmp'}
    for size in (40, 96):
        control, base = BUILDERS['bmp-xmltail'](size), plain[size]
        if len(control) != size:
            raise ValueError(f'bmp-xmltail len{size}: the control must match its twin\'s length')
        tail = XML_PLANT_OFFSET + len(XML_PLANT)
        if control[:XML_PLANT_OFFSET] != base[:XML_PLANT_OFFSET]:
            raise ValueError(f'bmp-xmltail len{size}: its header stopped matching the plain rung')
        if control[XML_PLANT_OFFSET:tail] != XML_PLANT:
            raise ValueError(f'bmp-xmltail len{size}: the plant is not at offset {XML_PLANT_OFFSET}')
        if control[tail:] != base[tail:]:
            raise ValueError(f'bmp-xmltail len{size}: only the planted window may differ')
        if control[:26] != base[:26]:
            raise ValueError(f'bmp-xmltail len{size}: the 26 bytes the BMP reader reads must survive the plant')
    recorded = json.loads(MANIFEST.read_text(encoding='utf-8')) if MANIFEST.exists() else None
    if recorded != manifest:
        raise ValueError('B12 ladder manifest differs from the fixture bytes')
    return recorded


def main():
    if sys.argv[1:] == ['generate']:
        DIRECTORY.mkdir(parents=True, exist_ok=True)
        for entry in entries():
            (DIRECTORY / entry['file']).write_bytes(BUILDERS[entry['kind']](entry['size']))
        MANIFEST.write_text(json.dumps(entries(), indent=2) + '\n', encoding='utf-8')
        print(f'wrote {len(FIXTURES)} rungs and {MANIFEST.name}')
    elif sys.argv[1:] != ['verify']:
        raise SystemExit('expected verify or generate')
    manifest = verify()
    print(f'B12 recognition ladder verified: {len(manifest)} entries over sizes '
          f'{min(e["size"] for e in manifest)}-{max(e["size"] for e in manifest)} bytes, '
          f'{TGA_MINIMUM}-byte targa floor')


if __name__ == '__main__':
    main()
