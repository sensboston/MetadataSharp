"""Generate owned synthetic EPS and PGF fixtures (B11 group: formats with their own containers).

Our own deterministic stdlib writer; not derived from Exiv2 or any imported corpus. Each file is
authored byte by byte from the published container definitions and the DSC 3.0 convention for XMP in
PostScript, so the same bytes can be read by both sides of the parity job.

EPS: a plain EPS file is a first line (`%!PS-Adobe-3.0 EPSF-3.0` or one of the two the reference
also accepts), a comment block, `%%EndComments`, and a small program. The XMP variants carry the
packet as a comment block, which is where the pinned tool looks for it. The DOS variant frames the
same PostScript section in the thirty-byte header of the Windows EPS convention and adds a TIFF
section whose bytes are a classic little-endian TIFF.

PGF: the header is `PGF` and a version byte, the size of the header region as a 32-bit word, a
sixteen-byte structure (width, height, levels, quality, bits per pixel, channels, mode, ...), an
optional 256-entry colour table when the mode byte is 2, and then the header region itself, which
holds one whole image file. Our PGF files put a PNG in that region, which is what the pinned tool
expects to find and hands to its own reader.

Deterministic; CI regenerates and compares SHA-256 against tests/fixtures/b11eps/manifest.json.

Usage: eps_pgf_fixtures.py generate OUTDIR   # write fixtures + manifest.json
       eps_pgf_fixtures.py verify OUTDIR     # fail if manifest pins drift
       eps_pgf_fixtures.py print-manifest    # emit pinned manifest JSON
"""
import argparse
import hashlib
import json
import struct
import sys
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / 'tests/fixtures/b11eps/manifest.json'
sys.path.insert(0, str(Path(__file__).resolve().parent))
import jpeg_owned_fixtures as owned  # noqa: E402  (shared authored TIFF and XMP cores)
import png_owned_fixtures as png  # noqa: E402  (the authored PNG container)

EPS_FIRST = b'%!PS-Adobe-3.0 EPSF-3.0'
EPS_FIRST_SPACE = b'%!PS-Adobe-3.0 EPSF-3.0 '      # OpenOffice
EPS_FIRST_31 = b'%!PS-Adobe-3.1 EPSF-3.0'          # Illustrator
DOS_SIGNATURE = b'\xC5\xD0\xD3\xC6'
DOS_HEADER_BYTES = 30


def xmp_packet():
    """The authored packet of the JPEG/PNG fixtures, which both sides already know."""
    return owned.xmp_packet()


def eps_file(packet=None, first=EPS_FIRST, newline=b'\n', trailer=b'<?xpacket end="w"?>',
             announce=b'%%ADO_ContainsXMP: MainFirst', extra_comments=b'', body=b''):
    """A plain EPS file: first line, comment block, %%EndComments, then a program."""
    comment = first + newline + b'%%BoundingBox: 0 0 100 100' + newline
    if announce is not None:
        comment += announce + newline
    comment += extra_comments + b'%%EndComments' + newline
    program = b'%%BeginProlog' + newline + b'%%EndProlog' + newline
    program += b'%%Page: 1 1' + newline + b'%%BeginPageSetup' + newline + b'%%EndPageSetup' + newline
    program += body
    program += b'%%PageTrailer' + newline + b'%%Trailer' + newline + b'%%EOF' + newline
    if packet is None:
        return comment + program
    return comment + packet + newline + trailer + newline + program


def dos_eps_file(postscript, tiff=None):
    """The same PostScript section framed by the thirty-byte DOS header, with a TIFF section."""
    tiff = tiff or b''
    ps_offset = DOS_HEADER_BYTES
    tiff_offset = ps_offset + len(postscript) if tiff else 0
    header = (DOS_SIGNATURE
              + struct.pack('<II', ps_offset, len(postscript))
              + struct.pack('<II', 0, 0)                    # no WMF section
              + struct.pack('<II', tiff_offset, len(tiff))
              + struct.pack('<H', 0xFFFF))                  # the documented checksum
    assert len(header) == DOS_HEADER_BYTES, len(header)
    return header + postscript + tiff


def pgf_structure(width, height, levels=3, quality=24, bpp=8, channels=3, mode=0):
    """The sixteen-byte header structure: width and height first, all little endian."""
    return struct.pack('<II', width, height) + bytes([levels, quality, bpp, channels, mode, 0, 0, 0])


def pgf_file(width, height, image, version=ord('6'), colour_table=None, mode=0):
    """A PGF file whose header region holds ``image`` and nothing else."""
    structure = pgf_structure(width, height, mode=mode)
    if colour_table is not None:
        assert len(colour_table) == 768
        region_prefix = structure + colour_table
    else:
        region_prefix = structure
    header_size = len(structure) + (768 if colour_table is not None else 0) + len(image)
    return b'PGF' + bytes([version]) + struct.pack('<I', header_size) + region_prefix + image


FIXTURES = {}


def _register(name, data, layout, recipe, **extra):
    entry = dict(bytes=data, layout=layout, recipe=recipe)
    entry.update(extra)
    FIXTURES[name] = entry


def _build_catalog():
    if FIXTURES:
        return FIXTURES
    packet = xmp_packet()
    carrier = png.png([png.exif_chunk(owned.exif_ifd0_tiff()), png.xmp_itxt_chunk(owned.xmp_packet())])

    _register('eps-plain.eps', eps_file(), 'eps-plain', 'eps-plain-empty-comment-block-v1',
              dispatch='eps', carriers='none', xmp=False)
    _register('eps-xmp.eps', eps_file(packet), 'eps-xmp', 'eps-xmp-packet-in-comment-block-v1',
              dispatch='eps', carriers='xmp', xmp=True)
    _register('eps-xmp-space.eps', eps_file(packet, first=EPS_FIRST_SPACE), 'eps-xmp-space',
              'eps-xmp-openoffice-first-line-v1', dispatch='eps', carriers='xmp', xmp=True)
    _register('eps-xmp-31.eps', eps_file(packet, first=EPS_FIRST_31), 'eps-xmp-31',
              'eps-xmp-illustrator-first-line-v1', dispatch='eps', carriers='xmp', xmp=True)
    _register('eps-xmp-crlf.eps', eps_file(packet, newline=b'\r\n'), 'eps-xmp-crlf',
              'eps-xmp-dos-line-endings-v1', dispatch='eps', carriers='xmp', xmp=True)
    _register('eps-xmp-nomain.eps', eps_file(packet, announce=b'%%ADO_ContainsXMP: NoMain'),
              'eps-xmp-nomain', 'eps-xmp-packet-but-header-says-none-v1',
              dispatch='eps', carriers='none', xmp=False)
    _register('eps-xmp-readonly.eps', eps_file(packet, trailer=b'<?xpacket end="r"?>'),
              'eps-xmp-readonly', 'eps-xmp-read-only-trailer-v1',
              dispatch='eps', carriers='xmp-refused', xmp=False)
    _register('dos-eps-xmp.eps', dos_eps_file(eps_file(packet), owned.exif_ifd0_tiff()),
              'dos-eps-xmp', 'dos-eps-postscript-and-tiff-sections-v1',
              dispatch='eps', carriers='xmp', xmp=True)
    _register('pgf-png.pgf', pgf_file(320, 211, carrier), 'pgf-png', 'pgf-header-region-holding-a-png-v1',
              dispatch='pgf', carriers='exif,xmp', xmp=True)
    _register('pgf-colour-table.pgf', pgf_file(64, 48, carrier, mode=2, colour_table=bytes(768)),
              'pgf-colour-table', 'pgf-indexed-mode-colour-table-v1',
              dispatch='pgf', carriers='exif,xmp', xmp=True)
    _register('pgf-empty-region.pgf', pgf_file(8, 8, b''), 'pgf-empty-region',
              'pgf-header-region-of-no-length-v1', dispatch='pgf', carriers='none', xmp=False)
    return FIXTURES


def fixture_bytes(name):
    return _build_catalog()[name]['bytes']


def manifest():
    catalog = _build_catalog()
    entries = []
    for name in sorted(catalog):
        data = catalog[name]['bytes']
        entry = {'file': name, 'length': len(data), 'sha256': hashlib.sha256(data).hexdigest(),
                 'layout': catalog[name]['layout'], 'recipe': catalog[name]['recipe'],
                 'rights': 'owned-synthetic'}
        for key, value in catalog[name].items():
            if key not in ('bytes', 'layout', 'recipe'):
                entry[key] = value
        entries.append(entry)
    return entries


def generate(outdir):
    outdir = Path(outdir)
    outdir.mkdir(parents=True, exist_ok=True)
    for name, entry in _build_catalog().items():
        (outdir / name).write_bytes(entry['bytes'])
    payload = json.dumps(manifest(), indent=2) + '\n'
    (outdir / 'manifest.json').write_text(payload, encoding='utf-8')
    return payload


def verify(outdir):
    """Fail when the bytes the writer produces now are not the bytes the manifest pins."""
    expected = json.loads(MANIFEST.read_text(encoding='utf-8'))
    actual = manifest()
    if expected != actual:
        for pinned, fresh in zip(expected, actual):
            if pinned != fresh:
                print(f'drift: {pinned.get("file")} pinned {pinned.get("sha256")} now {fresh.get("sha256")}',
                      file=sys.stderr)
        print('the fixture bytes have drifted from the pinned manifest', file=sys.stderr)
        return 1
    outdir = Path(outdir)
    for entry in expected:
        path = outdir / entry['file']
        if path.is_file() and hashlib.sha256(path.read_bytes()).hexdigest() != entry['sha256']:
            print(f'drift: {path} is not the pinned bytes', file=sys.stderr)
            return 1
    print(f'{len(expected)} fixtures verified')
    return 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('command', choices=['generate', 'verify', 'print-manifest'])
    parser.add_argument('outdir', nargs='?', default=str(ROOT / 'tests/fixtures/b11eps'))
    args = parser.parse_args(argv)
    if args.command == 'generate':
        generate(args.outdir)
        return 0
    if args.command == 'verify':
        return verify(args.outdir)
    print(json.dumps(manifest(), indent=2))
    return 0


if __name__ == '__main__':
    sys.exit(main())
