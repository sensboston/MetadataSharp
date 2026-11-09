"""Generate owned synthetic XMP packet fixtures (B5 observation batch).

Our own deterministic stdlib writer; not derived from Exiv2, Adobe's SDK or any
imported corpus. A `.xmp` file *is* an XMP packet document (the sidecar
artifact), so these fixtures are complete packets, Byte-for-byte the same shape
the in-image XMP carriers hold:

- xmp-base.xmp              canonical packet: xpacket begin/end PIs, <x:xmpmeta>
                            wrapper, CreatorTool=MetadataSharp (attribute/element
                            order fixed; the layout the JPEG/PNG/WebP/TIFF
                            in-image fixtures carry)
- xmp-no-props.xmp          same wrapper, empty rdf:Description (from-nothing
                            insert base)
- xmp-unwrapped.xmp         optional wrapper omitted per XMP Part 1 SS7.3-SS7.5
                            (edition pinned in XMP_NORMATIVE_REVIEW_AND_
                            RESTRICTED_PROFILE.md): xpacket PIs around a bare
                            rdf:RDF; CreatorTool=MetadataSharp
- xmp-unwrapped-no-props.xmp  unwrapped, empty rdf:Description (from-nothing
                            insert base for the unwrapped layout)
- xmp-nonutf8.xmp           wrapped, invalid UTF-8 bytes in CreatorTool
                            (record-only; never asserted)
- xmp-unwrapped-nonutf8.xmp unwrapped analog of xmp-nonutf8.xmp

The sidecar presence/applied distinction is exercised by the B5 observation
itself: image fixtures from the already-pinned JPEG/PNG/WebP/TIFF sets are
copied with an adjacent SIDECARPROBE .xmp file and the pinned CLI's behavior on
the *image* is recorded (presence is in scope; xmp-applied writes stay E15 per
the approved denominators). No sidecar fixture is authored for that purpose.

Encoding is UTF-8 for every packet (optional xpacket-carried BOM char, per Part
1); a leading raw UTF-8 BOM before `<?xpacket` is a deliberate non-goal of this
batch (recorded in README/limits, not silently dropped).

Pinned by tests/fixtures/xmp/manifest.json; CI regenerates and compares
SHA-256. Usage:
       xmp_owned_fixtures.py generate OUTDIR   # write fixtures + manifest.json
       xmp_owned_fixtures.py verify OUTDIR     # fail if manifest pins drift
       xmp_owned_fixtures.py print-manifest    # emit pinned manifest JSON
       xmp_owned_fixtures.py print-provenance  # emit provenance JSON
"""
import argparse
import hashlib
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / 'tests/fixtures/xmp/manifest.json'
PROVENANCE = ROOT / 'tests/fixtures/xmp/provenance.json'

XP_OPEN = b'<?xpacket begin="\xef\xbb\xbf" id="W5M0MpCehiHzreSzkc9d"?>'
XP_CLOSE = b'<?xpacket end="w"?>'
XMETA_OPEN = b'<x:xmpmeta xmlns:x="adobe:ns:meta/" x:xmptk="MetadataSharp">'
XMETA_CLOSE = b'</x:xmpmeta>'
RDF_OPEN = b'<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">'
RDF_CLOSE = b'</rdf:RDF>'
DESC_OPEN = b'<rdf:Description rdf:about="" xmlns:xmp="http://ns.adobe.com/xap/1.0/">'
DESC_CLOSE = b'</rdf:Description>'
CREATOR = b'<xmp:CreatorTool>MetadataSharp</xmp:CreatorTool>'

NONUTF8 = b'\x89\xfe\xffRAW not xml'


def _wrapped(body):
    return XP_OPEN + XMETA_OPEN + RDF_OPEN + body + RDF_CLOSE + XMETA_CLOSE + XP_CLOSE


def _unwrapped(body):
    return XP_OPEN + RDF_OPEN + body + RDF_CLOSE + XP_CLOSE


def packet_wrapped():
    return _wrapped(DESC_OPEN + CREATOR + DESC_CLOSE)


def packet_wrapped_empty():
    return _wrapped(b'<rdf:Description rdf:about="" xmlns:xmp="http://ns.adobe.com/xap/1.0/"/>')


def packet_unwrapped():
    return _unwrapped(DESC_OPEN + CREATOR + DESC_CLOSE)


def packet_unwrapped_empty():
    return _unwrapped(b'<rdf:Description rdf:about="" xmlns:xmp="http://ns.adobe.com/xap/1.0/"/>')


def packet_wrapped_nonutf8():
    return _wrapped(DESC_OPEN + b'<xmp:CreatorTool>' + NONUTF8 + b'</xmp:CreatorTool>' + DESC_CLOSE)


def packet_unwrapped_nonutf8():
    return _unwrapped(DESC_OPEN + b'<xmp:CreatorTool>' + NONUTF8 + b'</xmp:CreatorTool>' + DESC_CLOSE)


def strict_parse_xmp(data):
    """Structural requirements of every authored/written packet of this batch.

    Returns None when `data` is an authored-profile packet, else a short reason.
    Deliberately narrow: DOCTYPE rejected outright (never produced by our
    fixtures, never emitted by the pinned CLI's XMP writer), XML must be
    well-formed and root must be the xpacket-carried xmpmeta or a bare rdf:RDF.
    """
    if data.startswith(b'\xef\xbb\xbf'):
        return 'leading raw BOM outside the xpacket PI (non-goal of this batch)'
    head = data[:512]
    if b'<!DOCTYPE' in head or b'<!ENTITY' in head:
        return 'DOCTYPE/ENTITY not permitted in authored packets'
    try:
        data.decode('utf-8')
    except UnicodeDecodeError:
        return 'not utf-8'
    try:
        root = ET.fromstring(data)
    except ET.ParseError as error:
        return f'XML parse failed: {error}'
    local = root.tag.rsplit('}', 1)[-1] if isinstance(root.tag, str) else ''
    if local not in ('xmpmeta', 'RDF'):
        return f'unexpected root {root.tag!r}'
    return None


FIXTURES = {}


def _register(name, data, layout, duplicates, recipe):
    FIXTURES[name] = dict(bytes=data, layout=layout, duplicates=duplicates, recipe=recipe)


def _build_catalog():
    if FIXTURES:
        return FIXTURES
    _register('xmp-base.xmp', packet_wrapped(), 'xmp-packet', 'none', 'xmp-sidecar-standard-v1')
    _register('xmp-no-props.xmp', packet_wrapped_empty(), 'xmp-packet', 'none',
              'xmp-sidecar-standard-empty-desc-v1')
    _register('xmp-unwrapped.xmp', packet_unwrapped(), 'xmp-packet-unwrapped', 'none',
              'xmp-sidecar-unwrapped-v1')
    _register('xmp-unwrapped-no-props.xmp', packet_unwrapped_empty(), 'xmp-packet-unwrapped', 'none',
              'xmp-sidecar-unwrapped-empty-desc-v1')
    _register('xmp-nonutf8.xmp', packet_wrapped_nonutf8(), 'xmp-packet', 'none',
              'xmp-sidecar-standard-nonutf8-v1')
    _register('xmp-unwrapped-nonutf8.xmp', packet_unwrapped_nonutf8(), 'xmp-packet-unwrapped', 'none',
              'xmp-sidecar-unwrapped-nonutf8-v1')
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
        encoder='None (authored byte-level XMP packet writer; tools/reference/xmp_owned_fixtures.py)',
        recipe=('Deterministic authored XMP packets: xpacket begin/end PIs (the BOM char rides '
                'the begin PI per XMP Part 1); the wrapped layout carries x:xmpmeta (the shape the '
                'in-image XMP fixtures of the JPEG/PNG/WebP/TIFF batches already hold); the unwrapped '
                'layout omits the optional wrapper per Part 1 SS7.3-SS7.5 (editions/blobs pinned in '
                'docs/design/XMP_NORMATIVE_REVIEW_AND_RESTRICTED_PROFILE.md). CreatorTool is the '
                'single simple property; non-UTF-8 variants swap the CreatorTool value for raw '
                'invalid-UTF-8 bytes (record-only; never asserted). Standalone .xmp files ARE XMP '
                'packet documents (the sidecar artifact); no image bytes are present. Pinned by '
                'tests/fixtures/xmp/manifest.json; CI regenerates and compares SHA-256.'),
        inputRights=('MetadataSharp authored synthetic XMP fixtures, MIT; no imported packets, '
                     'no Adobe SDK material, no upstream fixture corpus.'),
        fixtures=manifest())


def verify_dir(outdir):
    outdir = Path(outdir)
    pinned = {entry['file']: entry for entry in json.loads(MANIFEST.read_text())}
    missing = sorted(set(pinned) - {p.name for p in outdir.glob('*.xmp')})
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
        outdir = args.outdir or (ROOT / 'artifacts/owned-xmp')
        print(f'wrote {generate(outdir)} fixtures to {outdir}')
    elif args.command == 'verify':
        verify_dir(args.outdir or (ROOT / 'artifacts/owned-xmp'))
    elif args.command == 'print-manifest':
        sys.stdout.write(json.dumps(manifest(), indent=2) + '\n')
    elif args.command == 'print-provenance':
        sys.stdout.write(json.dumps(provenance(), indent=2) + '\n')


if __name__ == '__main__':
    main()
