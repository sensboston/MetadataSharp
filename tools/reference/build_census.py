"""Generate the E01 census enumeration artifact (schemaVersion 1).

Expands the documented 31x6 capability seeds into census rows for the batches
enumerated so far. B0 enumerates JPEG x {exif, comments, icc} and the JPEG
inventory track; B1 enumerates JPEG x {xmp, iptc, thumbnail}; B2 enumerates
PNG x {inventory, comments, exif, icc, iptc, thumbnail, xmp}; B3 enumerates
WebP x {inventory, comments, exif, icc, iptc, thumbnail, xmp}; B4 enumerates
TIFF x {inventory, comments, exif, icc, iptc, thumbnail, xmp}; B5 enumerates
XMP (the standalone packet document / sidecar artifact) x {inventory, exif
(noDocumentedClaim), comments (noDocumentedClaim), icc (noDocumentedClaim),
iptc (noDocumentedClaim), thumbnail (noDocumentedClaim), xmp} plus the B5
sidecar-presence read probes for JPEG/PNG/WebP/TIFF (presence is in scope;
xmp-applied writes stay E15 per the approved denominators). All rows are
generated deterministically; the artifact is immutable between reviews
(`--check` fails on drift). This is enumeration, not observation: every row
starts as not-run / not-assessed and no denominator is computed here.

Usage: build_census.py          # write docs/research/EXIV2_CENSUS.json
       build_census.py --check  # fail if the committed artifact differs
"""
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SEEDS = ROOT / 'docs/research/EXIV2_OPERATION_SEEDS.json'
OUTPUT = ROOT / 'docs/research/EXIV2_CENSUS.json'
# NOTE: tests/fixtures/webp/provenance.json is reserved for the .NET library's
# PNG/WebP-group audit (Pillow-encoded single-pixel fixtures); the E01 census
# batch fixtures pin their own manifest/provenance pair there. The TIFF batch
# fixtures follow the same pattern with tests/fixtures/tiff/{manifest,provenance}.json.
EVIDENCE_BY_BATCH = {
    'B0': ROOT / 'docs/research/evidence/e01-b0-census/observations.json',
    'B1': ROOT / 'docs/research/evidence/e01-b1-census/observations.json',
    'B2': ROOT / 'docs/research/evidence/e01-b2-census/observations.json',
    'B3': ROOT / 'docs/research/evidence/e01-b3-census/observations.json',
    'B4': ROOT / 'docs/research/evidence/e01-b4-census/observations.json',
    'B5': ROOT / 'docs/research/evidence/e01-b5-census/observations.json',
    'B6': ROOT / 'docs/research/evidence/e01-b6-census/observations.json',
    'B7': ROOT / 'docs/research/evidence/e01-b7-census/observations.json',
    'B8': ROOT / 'docs/research/evidence/e01-b8-census/observations.json',
}
FIXTURE_MANIFESTS = {
    'JPEG': ROOT / 'tests/fixtures/jpeg/manifest.json',
    'PNG': ROOT / 'tests/fixtures/png/manifest.json',
    'WEBP': ROOT / 'tests/fixtures/webp/census-manifest.json',
    'TIFF': ROOT / 'tests/fixtures/tiff/manifest.json',
    'XMP': ROOT / 'tests/fixtures/xmp/manifest.json',
    'DNG': ROOT / 'tests/fixtures/dng/census-manifest.json',
    **{fmt: ROOT / 'tests/fixtures/b7/manifest.json' for fmt in ('AVIF', 'HEIF', 'HEIC', 'JXL')},
    # B8 keeps one manifest for its three formats; each entry pins its own format.
    **{fmt: ROOT / 'tests/fixtures/b8/census-manifest.json' for fmt in ('PSD', 'JP2', 'EPS')},
}

# Axis collapses that are statically impossible, reviewed with the B0 pilot and B1.
# (family, layout, duplicates or None for all, operation or None for all) -> reason
STATIC_NOT_APPLICABLE = [
    ('exif', 'app1-exif-multiple', None, None,
     'app1-exif-multiple layout is itself the duplicate-block case; duplicates axis not independent '
     '(duplicate-block coverage lives in app1-exif-ifd0/app1-exif-ifd0-subifd x multiple-independent)'),
    ('comments', 'com-single', 'first-wins', None,
     'single COM segment cannot carry a duplication policy; duplicate coverage lives in com-multiple'),
    ('comments', 'com-single', 'multiple-independent', None,
     'single COM segment cannot carry a duplication policy; duplicate coverage lives in com-multiple'),
    ('thumbnail', 'exif-ifd1-uncompressed', None, 'insert',
     'pinned CLI exposes no uncompressed-thumbnail write path (-i t embeds JPEG thumbnails only); '
     'revisit if the CLI grows the capability; flagged for developer review at denominators'),
]


def static_not_applicable(row):
    for family, layout, duplicates, operation, reason in STATIC_NOT_APPLICABLE:
        if (row['family'] == family and row['layout'] == layout
                and (duplicates is None or row['duplicates'] == duplicates)
                and (operation is None or row['operation'] == operation)):
            return reason
    return None

# --- Approved enumerations (design review 2026-11-08; extended 2026-11-09) ----
# Every axis value used by a row MUST come from this block (closed world).
# Adding a value = PR + review; it never happens inside a row.
# 2026-11-09 extension (B5, approved batch): 'xmp-packet' added as a legal carrier
# layout for the comments/exif/icc/iptc/thumbnail families — the XMP-format
# noDocumentedClaim probe cells of B5 ride inside the standalone packet document
# itself; no row of another format may use it (no such rows exist).
FAMILIES = ['exif', 'iptc', 'xmp', 'comments', 'icc', 'thumbnail']
OPERATIONS = ['inventory', 'read', 'insert', 'replace', 'remove', 'extract']
EXTRACT_FAMILIES = ['comments', 'icc', 'thumbnail']
TEXT_ENCODINGS = ['ascii', 'utf-8', 'unknown-raw', 'not-applicable']
VALUE_TYPES = ['any-documented', 'opaque', 'text-bytes']  # 'any-documented': B14 splits EXIF
LAYOUTS = {
    'exif': ['app1-exif-ifd0', 'app1-exif-ifd0-subifd', 'app1-exif-multiple', 'png-exif', 'webp-exif', 'tiff-exif', 'xmp-packet'],
    'comments': ['com-single', 'com-multiple', 'png-text', 'webp-comments', 'tiff-comments', 'xmp-packet'],
    'icc': ['app2-icc-single', 'app2-icc-segmented', 'iccp', 'webp-iccp', 'tiff-icc', 'xmp-packet'],
    'xmp': ['app1-xmp-standard', 'app1-xmp-extended', 'itxt-xmp', 'ztxt-xmp', 'webp-xmp', 'tiff-xmp',
            'xmp-packet', 'xmp-packet-unwrapped'],
    'iptc': ['app13-8bim-iptc', 'app13-8bim-iptc-multiple', 'ztxt-raw-profile-iptc', 'itxt-raw-profile-iptc', 'webp-iptc', 'tiff-iptc', 'xmp-packet'],
    'thumbnail': ['exif-ifd1-jpeg', 'exif-ifd1-uncompressed', 'xmp-packet'],
    'inventory': ['container'],
}
# B6: exact byte order and raw-root versus preview-root are distinct fixture layouts.
DNG_LAYOUTS = ['dng-raw-ii', 'dng-raw-mm', 'dng-preview-ii', 'dng-preview-mm']
for _family in FAMILIES + ['inventory']:
    LAYOUTS[_family].extend(DNG_LAYOUTS)

# B7 read-only probe layouts; CR3 stays present as a blocked rights cell.
B7_FORMAT_LAYOUTS = [('AVIF', 'bmff-avif'), ('HEIF', 'bmff-heif-hevc'),
                     ('HEIC', 'bmff-heic-hevc'), ('CR3', 'bmff-cr3'),
                     ('JXL', 'jxl-container'), ('JXL', 'jxl-codestream')]
for _family in FAMILIES + ['inventory']:
    LAYOUTS[_family].extend(layout for _, layout in B7_FORMAT_LAYOUTS)

# B8 probe layouts. The two PSD layouts carry the authored EXIF/XMP/IPTC resources;
# JP2 and EPS are deliberately metadata-free, so their rows are absence records.
# The unobserved layouts keep a cell for a format whose fixture and oracle review is
# still pending (docs/research/E01_B8_RIGHTS_AND_ORACLE_REVIEW.md): pending is not
# refused, not applicable or excluded.
B8_FORMAT_LAYOUTS = [('PSD', ('flat-rgb-v1-metadata', 'flat-rgb-v1-no-resources')),
                     ('JP2', ('jp2-boxed-rgb',)), ('EPS', ('eps-dsc-bounding-box',)),
                     ('PGF', ('pgf-unobserved',)), ('EXV', ('exv-unobserved',)),
                     ('DCP', ('dcp-unobserved',))]
for _family in FAMILIES + ['inventory']:
    LAYOUTS[_family].extend(layout for _, layouts in B8_FORMAT_LAYOUTS for layout in layouts)

# (batch, format) cells that may legitimately end up blocked rather than observed.
BLOCKED_CELLS = frozenset({('B7', 'CR3'), ('B8', 'PGF'), ('B8', 'EXV'), ('B8', 'DCP')})

DUPLICATES = ['none', 'first-wins', 'multiple-independent']
SIDECARS = ['none', 'xmp-present']  # B5 adds the presence-probe value (xmp-applied stays E15)
PRESENCE_VALUES = ['true', 'false', 'unknown']
RIGHTS_CLASSES = ['owned-synthetic', 'cc0-verified', 'public-domain-verified', 'licensed-embedded', 'blocked', 'not-applicable']
ORACLE_TYPES = ['specification', 'pinned-cli', 'exiftool', 'independent']


def _row(row_id, fmt, family, operation, *, value_type, text_encoding, layout, duplicates,
         batch, notes, no_claim=False, sidecar='none'):
    return dict(
        rowId=row_id,
        format=fmt,
        family=family,
        operation=operation,
        valueType=value_type,
        textEncoding=text_encoding,
        layout=layout,
        duplicates=duplicates,
        sidecar=sidecar,
        presence={'icc': 'unknown', 'comment': 'unknown', 'preview': 'unknown'},
        noDocumentedClaim=no_claim,
        fixture=None,
        oracle={'type': 'specification', 'detail': 'container/spec + documentation blob; pinned-CLI observation pending'},
        baselineObservation='not-run',
        verification='not-assessed',
        batch=batch,
        limits='finite by D0/E02 limit policy; per-batch caps recorded when observed',
        notes=notes,
    )


def build_rows(seeds):
    rows = []
    counters = {}

    def emit(fmt, family, operation, batch='B0', sidecar='none', **kwargs):
        key = (fmt, family, operation)
        counter = counters.get(key, 0) + 1
        counters[key] = counter
        rows.append(_row(f'EX0289-{fmt}-{family.upper()}-{operation.upper()}-{counter:04d}',
                         fmt, family, operation, batch=batch, sidecar=sidecar, **kwargs))

    # JPEG inventory track (recognition is never metadata support).
    emit('JPEG', 'inventory', 'inventory', value_type='opaque', text_encoding='not-applicable',
         layout='container', duplicates='none',
         notes='recognize/dimensions/MIME without decoding; count tracked separately, never support')

    jpeg = {s['family']: s for s in seeds['seeds'] if s['format'] == 'JPEG'}
    assert set(jpeg) == set(FAMILIES), 'JPEG seeds missing a family'

    # JPEG EXIF: generic value types; vocabulary split is B14, recorded as any-documented.
    for operation in ('read', 'insert', 'replace', 'remove'):
        for layout in ('app1-exif-ifd0', 'app1-exif-ifd0-subifd', 'app1-exif-multiple'):
            for duplicates in DUPLICATES:
                emit('JPEG', 'exif', operation, value_type='any-documented',
                     text_encoding='not-applicable', layout=layout, duplicates=duplicates,
                     notes='generic EXIF row; valueType split deferred to B14 vocabulary census')

    # JPEG comments: payload encodings are distinct atomic behaviors.
    for operation in ('read', 'insert', 'replace', 'remove'):
        for layout in ('com-single', 'com-multiple'):
            for duplicates in DUPLICATES:
                for encoding in ('ascii', 'utf-8', 'unknown-raw'):
                    emit('JPEG', 'comments', operation, value_type='text-bytes',
                         text_encoding=encoding, layout=layout, duplicates=duplicates,
                         notes='JPEG COM payload as stored; interpretation left to the caller')
    for layout in ('com-single', 'com-multiple'):
        for duplicates in DUPLICATES:
            emit('JPEG', 'comments', 'extract', value_type='opaque', text_encoding='not-applicable',
                 layout=layout, duplicates=duplicates,
                 notes='comment payload copied out as raw bytes')

    # JPEG ICC: single vs multi-segment APP2 profile; opaque bytes, no decode.
    for operation in ('read', 'insert', 'replace', 'remove', 'extract'):
        for layout in ('app2-icc-single', 'app2-icc-segmented'):
            for duplicates in ('none', 'first-wins'):
                emit('JPEG', 'icc', operation, value_type='opaque', text_encoding='not-applicable',
                     layout=layout, duplicates=duplicates,
                     notes='ICC profile container operation; no profile decoding/validation claimed')

    # JPEG XMP (B1): standard APP1 packet vs base+extension chunks (Extended XMP).
    # The ascii cell is omitted: a pure-ASCII XMP packet is byte-identical to utf-8 behavior.
    for operation in ('read', 'insert', 'replace', 'remove'):
        for layout in ('app1-xmp-standard', 'app1-xmp-extended'):
            for duplicates in ('none', 'first-wins'):
                for encoding in ('utf-8', 'unknown-raw'):
                    emit('JPEG', 'xmp', operation, value_type='text-bytes',
                         text_encoding=encoding, layout=layout, duplicates=duplicates, batch='B1',
                         notes='XMP packet as stored; interpretation left to the caller')
    # JPEG IPTC (B1): Photoshop 8BIM 0x0404 resource; dataset-level duplication axis.
    for operation in ('read', 'insert', 'replace', 'remove'):
        for layout in ('app13-8bim-iptc', 'app13-8bim-iptc-multiple'):
            for duplicates in ('none', 'first-wins'):
                emit('JPEG', 'iptc', operation, value_type='opaque', text_encoding='not-applicable',
                     layout=layout, duplicates=duplicates, batch='B1',
                     notes='IPTC-NAA datasets as raw bytes; no dataset interpretation claimed')
    # JPEG thumbnail (B1): EXIF IFD1 embedded thumbnail (JPEG-compressed vs uncompressed strip).
    for operation in ('read', 'insert', 'replace', 'remove', 'extract'):
        for layout in ('exif-ifd1-jpeg', 'exif-ifd1-uncompressed'):
            emit('JPEG', 'thumbnail', operation, value_type='opaque', text_encoding='not-applicable',
                 layout=layout, duplicates='none', batch='B1',
                 notes='embedded EXIF IFD1 thumbnail container operation; bytes opaque')

    # PNG inventory track (recognition is never metadata support).
    emit('PNG', 'inventory', 'inventory', value_type='opaque', text_encoding='not-applicable',
         layout='container', duplicates='none', batch='B2',
         notes='recognize/dimensions/MIME without decoding; count tracked separately, never support')

    png = {s['family']: s for s in seeds['seeds'] if s['format'] == 'PNG'}
    assert set(png) == set(FAMILIES), 'PNG seeds missing a family'
    # PNG noDocumentedClaim cells (claim '-'): one read row each, observed via pinned CLI.
    emit('PNG', 'comments', 'read', value_type='text-bytes', text_encoding='not-applicable',
         layout='png-text', duplicates='none', batch='B2', no_claim=True,
         notes='no documented claim (seed EX0289-PNG-COMMENTS); pinned-CLI observation recorded, nothing asserted')
    emit('PNG', 'exif', 'read', value_type='any-documented', text_encoding='not-applicable',
         layout='png-exif', duplicates='none', batch='B2', no_claim=True,
         notes='no documented claim (seed EX0289-PNG-EXIF); pinned-CLI observation recorded, nothing asserted')
    # PNG ICC (B2): iCCP chunk carries a zlib-compressed profile (container ops, no profile decode).
    for operation in ('read', 'insert', 'replace', 'remove'):
        for duplicates in ('none', 'first-wins'):  # PNG allows at most one iCCP chunk
            emit('PNG', 'icc', operation, value_type='opaque', text_encoding='not-applicable',
                 layout='iccp', duplicates=duplicates, batch='B2',
                 notes='PNG iCCP chunk (zlib-compressed profile); opaque bytes, no profile decoding/validation claimed')
    # PNG XMP (B2): uncompressed iTXt vs zTXt-compressed carrier; no Extended-XMP analog in this batch.
    for operation in ('read', 'insert', 'replace', 'remove'):
        for layout in ('itxt-xmp', 'ztxt-xmp'):
            for duplicates in ('none', 'first-wins'):
                for encoding in ('utf-8', 'unknown-raw'):
                    emit('PNG', 'xmp', operation, value_type='text-bytes',
                         text_encoding=encoding, layout=layout, duplicates=duplicates, batch='B2',
                         notes='XMP packet as stored in iTXt/zTXt; interpretation left to the caller')
    # PNG IPTC (B2): the CLI reads the hex-encoded IIM/8BIM "raw profile type iptc" carrier;
    # the same raw text in an uncompressed iTXt is spec-legal but the pinned CLI rejects it.
    for operation in ('read', 'insert', 'replace', 'remove'):
        for layout in ('ztxt-raw-profile-iptc', 'itxt-raw-profile-iptc'):
            for duplicates in ('none', 'first-wins'):
                emit('PNG', 'iptc', operation, value_type='opaque', text_encoding='not-applicable',
                     layout=layout, duplicates=duplicates, batch='B2',
                     notes='IPTC-NAA datasets as raw bytes in a raw-profile carrier; no dataset interpretation claimed')
    # PNG thumbnail (B2): eXIf chunk with an IFD1 thumbnail (JPEG-compressed vs uncompressed strip).
    for operation in ('read', 'insert', 'replace', 'remove'):
        for layout in ('exif-ifd1-jpeg', 'exif-ifd1-uncompressed'):
            emit('PNG', 'thumbnail', operation, value_type='opaque', text_encoding='not-applicable',
                 layout=layout, duplicates='none', batch='B2',
                 notes='embedded EXIF IFD1 thumbnail container operation; bytes opaque')

    # WebP inventory track (recognition is never metadata support).
    emit('WEBP', 'inventory', 'inventory', value_type='opaque', text_encoding='not-applicable',
         layout='container', duplicates='none', batch='B3',
         notes='recognize/dimensions/MIME without decoding; count tracked separately, never support')

    webp = {s['family']: s for s in seeds['seeds'] if s['format'] == 'WEBP'}
    assert set(webp) == set(FAMILIES), 'WEBP seeds missing a family'
    # WebP noDocumentedClaim cells (claim '-'): one read row each, observed via pinned CLI.
    emit('WEBP', 'comments', 'read', value_type='text-bytes', text_encoding='not-applicable',
         layout='webp-comments', duplicates='none', batch='B3', no_claim=True,
         notes='no documented claim (seed EX0289-WEBP-COMMENTS); pinned-CLI observation recorded, nothing asserted')
    emit('WEBP', 'iptc', 'read', value_type='opaque', text_encoding='not-applicable',
         layout='webp-iptc', duplicates='none', batch='B3', no_claim=True,
         notes='no documented claim (seed EX0289-WEBP-IPTC); pinned-CLI observation recorded, nothing asserted')
    # WebP EXIF (B3): classic II TIFF in an 'EXIF' chunk; at most one EXIF chunk per spec.
    for operation in ('read', 'insert', 'replace', 'remove'):
        for duplicates in ('none', 'first-wins'):
            emit('WEBP', 'exif', operation, value_type='any-documented', text_encoding='not-applicable',
                 layout='webp-exif', duplicates=duplicates, batch='B3',
                 notes='classic II TIFF in a WebP EXIF chunk; duplicate-EXIF behavior recorded, never pre-asserted')
    # WebP ICC (B3): raw profile bytes in an 'ICCP' chunk; no profile decode.
    for operation in ('read', 'insert', 'replace', 'remove'):
        for duplicates in ('none', 'first-wins'):  # at most one ICCP chunk per spec
            emit('WEBP', 'icc', operation, value_type='opaque', text_encoding='not-applicable',
                 layout='webp-iccp', duplicates=duplicates, batch='B3',
                 notes='WebP ICCP chunk (raw profile bytes); opaque bytes, no profile decoding/validation claimed')
    # WebP XMP (B3): raw 'XMP ' chunk carrier (note the trailing space); no Extended-XMP analog in this batch.
    for operation in ('read', 'insert', 'replace', 'remove'):
        for duplicates in ('none', 'first-wins'):
            for encoding in ('utf-8', 'unknown-raw'):
                emit('WEBP', 'xmp', operation, value_type='text-bytes',
                     text_encoding=encoding, layout='webp-xmp', duplicates=duplicates, batch='B3',
                     notes='XMP packet as stored in a raw XMP chunk; interpretation left to the caller')
    # WebP thumbnail (B3): EXIF IFD1 embedded thumbnail (JPEG-compressed vs uncompressed strip).
    for operation in ('read', 'insert', 'replace', 'remove'):
        for layout in ('exif-ifd1-jpeg', 'exif-ifd1-uncompressed'):
            emit('WEBP', 'thumbnail', operation, value_type='opaque', text_encoding='not-applicable',
                 layout=layout, duplicates='none', batch='B3',
                 notes='embedded EXIF IFD1 thumbnail container operation; bytes opaque')

    # TIFF inventory track (recognition is never metadata support).
    emit('TIFF', 'inventory', 'inventory', value_type='opaque', text_encoding='not-applicable',
         layout='container', duplicates='none', batch='B4',
         notes='recognize/dimensions/MIME without decoding; count tracked separately, never support')

    tiff = {s['family']: s for s in seeds['seeds'] if s['format'] == 'TIFF'}
    assert set(tiff) == set(FAMILIES), 'TIFF seeds missing a family'
    # TIFF noDocumentedClaim cells (claim '-'): one read row each, observed via pinned CLI.
    emit('TIFF', 'comments', 'read', value_type='text-bytes', text_encoding='not-applicable',
         layout='tiff-comments', duplicates='none', batch='B4', no_claim=True,
         notes='no documented claim (seed EX0289-TIFF-COMMENTS); pinned-CLI observation recorded, nothing asserted; '
               'XPComment carrier tag 0x9C9C (UTF-16LE) not surfaced by the pinned CLI')
    # TIFF EXIF (B4): classic II TIFF, Artist in IFD0 (0x013B); duplicate carriers share IFD0.
    for operation in ('read', 'insert', 'replace', 'remove'):
        for duplicates in ('none', 'first-wins'):
            emit('TIFF', 'exif', operation, value_type='any-documented', text_encoding='not-applicable',
                 layout='tiff-exif', duplicates=duplicates, batch='B4',
                 notes='classic II TIFF IFD0; Artist entry authored twice in the duplicates fixture; '
                       'duplicate-carrier behavior recorded, never pre-asserted')
    # TIFF ICC (B4): raw profile bytes in tag 0x8773; no profile decode.
    for operation in ('read', 'insert', 'replace', 'remove'):
        for duplicates in ('none', 'first-wins'):
            emit('TIFF', 'icc', operation, value_type='opaque', text_encoding='not-applicable',
                 layout='tiff-icc', duplicates=duplicates, batch='B4',
                 notes='ICC profile in tag 0x8773 (raw profile bytes); opaque bytes, no profile decoding/validation claimed; '
                       'duplicate-carrier behavior recorded, never pre-asserted')
    # TIFF IPTC (B4): raw IIM/8BIM (IPTC resource 0x0404) bytes in tag 0x83BB; documented Read/Write.
    for operation in ('read', 'insert', 'replace', 'remove'):
        for duplicates in ('none', 'first-wins'):
            emit('TIFF', 'iptc', operation, value_type='opaque', text_encoding='not-applicable',
                 layout='tiff-iptc', duplicates=duplicates, batch='B4',
                 notes='IPTC-NAA datasets as raw IIM/8BIM bytes in tag 0x83BB; no dataset interpretation claimed; '
                       'duplicate-carrier behavior recorded, never pre-asserted')
    # TIFF XMP (B4): raw XMP packet bytes in tag 0x02BC; no Extended-XMP analog in this batch.
    for operation in ('read', 'insert', 'replace', 'remove'):
        for duplicates in ('none', 'first-wins'):
            for encoding in ('utf-8', 'unknown-raw'):
                emit('TIFF', 'xmp', operation, value_type='text-bytes',
                     text_encoding=encoding, layout='tiff-xmp', duplicates=duplicates, batch='B4',
                     notes='XMP packet as stored in tag 0x02BC; interpretation left to the caller')
    # TIFF thumbnail (B4): IFD1 embedded thumbnail (JPEG-compressed vs uncompressed strip), chained IFD0->IFD1.
    for operation in ('read', 'insert', 'replace', 'remove'):
        for layout in ('exif-ifd1-jpeg', 'exif-ifd1-uncompressed'):
            emit('TIFF', 'thumbnail', operation, value_type='opaque', text_encoding='not-applicable',
                 layout=layout, duplicates='none', batch='B4',
                 notes='IFD1 embedded thumbnail container operation; bytes opaque; uncompressed-strip insert '
                       'has no pinned write path and is statically not-applicable')

    # XMP packet inventory track (a standalone .xmp file IS an XMP packet document;
    # recognition is never metadata support).
    emit('XMP', 'inventory', 'inventory', value_type='opaque', text_encoding='not-applicable',
         layout='container', duplicates='none', batch='B5',
         notes='recognize an XMP packet document without decoding the payload; count tracked separately, never support')

    xmpf = {s['family']: s for s in seeds['seeds'] if s['format'] == 'XMP'}
    assert set(xmpf) == set(FAMILIES), 'XMP seeds missing a family'
    # XMP noDocumentedClaim cells (claim '-'): one read row each, observed via pinned CLI.
    emit('XMP', 'exif', 'read', value_type='any-documented', text_encoding='not-applicable',
         layout='xmp-packet', duplicates='none', batch='B5', no_claim=True,
         notes='no documented claim (seed EX0289-XMP-EXIF); pinned-CLI observation recorded, nothing asserted')
    emit('XMP', 'iptc', 'read', value_type='opaque', text_encoding='not-applicable',
         layout='xmp-packet', duplicates='none', batch='B5', no_claim=True,
         notes='no documented claim (seed EX0289-XMP-IPTC); pinned-CLI observation recorded, nothing asserted')
    emit('XMP', 'comments', 'read', value_type='text-bytes', text_encoding='not-applicable',
         layout='xmp-packet', duplicates='none', batch='B5', no_claim=True,
         notes='no documented claim (seed EX0289-XMP-COMMENTS); pinned-CLI observation recorded, nothing asserted')
    emit('XMP', 'icc', 'read', value_type='opaque', text_encoding='not-applicable',
         layout='xmp-packet', duplicates='none', batch='B5', no_claim=True,
         notes='no documented claim (seed EX0289-XMP-ICC); pinned-CLI observation recorded, nothing asserted')
    emit('XMP', 'thumbnail', 'read', value_type='opaque', text_encoding='not-applicable',
         layout='xmp-packet', duplicates='none', batch='B5', no_claim=True,
         notes='no documented claim (seed EX0289-XMP-THUMBNAIL); pinned-CLI observation recorded, nothing asserted')
    # XMP x XMP (documented Read/Write): the standalone packet document. The wrapped layout is the
    # canonical packet the in-image fixtures already carry; the unwrapped layout omits the optional
    # wrapper per XMP Part 1 SS7.3-SS7.5 (editions/blobs pinned in
    # docs/design/XMP_NORMATIVE_REVIEW_AND_RESTRICTED_PROFILE.md). Leading raw BOMs, double packets and
    # Extended-XMP-style segmentation are deliberate non-goals of this batch (recorded in README/limits).
    for operation in ('read', 'insert', 'replace', 'remove'):
        for layout in ('xmp-packet', 'xmp-packet-unwrapped'):
            for encoding in ('utf-8', 'unknown-raw'):
                emit('XMP', 'xmp', operation, value_type='text-bytes',
                     text_encoding=encoding, layout=layout, duplicates='none', batch='B5',
                     notes='XMP packet document as stored; interpretation left to the caller; '
                           'optional-wrapper layout per XMP Part 1 SS7.3-SS7.5 (pinned normative review)')
    # B5 sidecar-presence read probes (presence only; xmp-applied writes stay E15 per the
    # approved denominators, section 6(v)/section 7 of the census design and its 2026-11-08
    # answers). A <stem>.xmp sidecar is placed adjacent to each observed image fixture; the
    # pinned CLI opens the image only, so the sidecar never influences the image's behavior.
    # One probe per observed image format; presence is deliberately NOT re-enumerated across
    # operations (recorded pruning): the image operation is byte-identical with or without the
    # adjacent sidecar, and the full image XMP operation grid is already covered by B1-B4.
    for fmt, layout in (('JPEG', 'app1-xmp-standard'), ('PNG', 'itxt-xmp'),
                        ('WEBP', 'webp-xmp'), ('TIFF', 'tiff-xmp')):
        emit(fmt, 'xmp', 'read', value_type='text-bytes', text_encoding='utf-8',
             layout=layout, duplicates='none', batch='B5', sidecar='xmp-present',
             notes=f'presence probe: adjacent <stem>.xmp sidecar next to the {fmt} fixture; the pinned CLI '
                   'opens the image only; presence never implies application; xmp-applied writes stay E15')
    # B6: D1-derived DNGs, separating byte order and raw/preview roots.
    # EXIF IFD1 JPEG is used for thumbnail operations, and is distinct from
    # the reduced RGB root preview; conformance of that authored IFD1 variant
    # remains unverified. Duplicate and raw-encoding grids remain future work.
    for layout in DNG_LAYOUTS:
        emit('DNG', 'inventory', 'inventory', value_type='opaque', text_encoding='not-applicable',
             layout=layout, duplicates='none', batch='B6',
             notes='owned D1-derived DNG recognition probe; raw CFA bytes checked, no raw decode')
        emit('DNG', 'comments', 'read', value_type='text-bytes', text_encoding='not-applicable',
             layout=layout, duplicates='none', batch='B6', no_claim=True,
             notes='no documented comments claim; XPComment is a structural probe, not support')
        for family in ('exif', 'icc', 'iptc', 'xmp', 'thumbnail'):
            for operation in ('read', 'insert', 'replace', 'remove'):
                value_type = 'any-documented' if family == 'exif' else 'text-bytes' if family == 'xmp' else 'opaque'
                encoding = 'utf-8' if family == 'xmp' else 'not-applicable'
                emit('DNG', family, operation, value_type=value_type, text_encoding=encoding,
                     layout=layout, duplicates='none', batch='B6',
                     notes='D1-derived DNG probe; CFA strip-byte invariant is not a raw-decode oracle')
    # B7: observation and honest blocked rows are both retained. The HEIF and
    # HEIC inputs are distinct HEVC encodes with the same HEIC brand, not two
    # independent codec claims; JXL codestream and container remain distinct.
    for fmt, layout in B7_FORMAT_LAYOUTS:
        emit(fmt, 'inventory', 'inventory', value_type='opaque', text_encoding='not-applicable',
             layout=layout, duplicates='none', batch='B7',
             notes='read-only container probe; recognition does not establish valid decode')
        for family in FAMILIES:
            claim = next(s for s in seeds['seeds'] if s['format'] == fmt and s['family'] == family)
            no_claim = claim['documentedClaim'] in ('-', '')
            value_type = 'any-documented' if family == 'exif' else 'text-bytes' if family in ('xmp', 'comments') else 'opaque'
            encoding = 'utf-8' if family == 'xmp' else 'not-applicable'
            emit(fmt, family, 'read', value_type=value_type, text_encoding=encoding, layout=layout,
                 duplicates='none', batch='B7', no_claim=no_claim,
                 notes='read-only probe; absent metadata and recognition are not support claims')
            if family == 'thumbnail' and not no_claim:
                emit(fmt, family, 'extract', value_type='opaque', text_encoding='not-applicable', layout=layout,
                     duplicates='none', batch='B7',
                     notes='thumbnail extract attempted; absence of an authored thumbnail is recorded')
    # B8: read-only probes of the three owned fixtures cleared by the format-by-format
    # rights review (two PSD layouts, boxed JP2, ASCII EPS), plus honest blocked rows for
    # PGF/EXV/DCP, which stay visible pending their own review. Layout order fixes the
    # rowId counters, so EX0289-PSD-*-READ-0001 is the metadata-bearing layout and -0002
    # is the flat one. Only inventory/read/extract are enumerated: no B8 format has an
    # owned writable fixture yet, so the write suboperations stay unobserved rather than
    # being called not-applicable from a single refusal probe.
    for fmt, layouts in B8_FORMAT_LAYOUTS:
        claim = {s['family']: s for s in seeds['seeds'] if s['format'] == fmt}
        assert set(claim) == set(FAMILIES), f'{fmt} seeds missing a family'
        for layout in sorted(layouts):
            emit(fmt, 'inventory', 'inventory', value_type='opaque', text_encoding='not-applicable',
                 layout=layout, duplicates='none', batch='B8',
                 notes='read-only container probe; recognition and dimensions are not metadata support')
            for family in FAMILIES:
                no_claim = claim[family]['documentedClaim'] in ('-', '')
                value_type = ('any-documented' if family == 'exif'
                              else 'text-bytes' if family in ('xmp', 'comments') else 'opaque')
                encoding = 'utf-8' if family == 'xmp' else 'not-applicable'
                emit(fmt, family, 'read', value_type=value_type, text_encoding=encoding, layout=layout,
                     duplicates='none', batch='B8', no_claim=no_claim,
                     notes='read-only bounded probe; absent or unreadable metadata is recorded, nothing asserted')
                if family == 'thumbnail' and not no_claim:
                    emit(fmt, family, 'extract', value_type='opaque', text_encoding='not-applicable',
                         layout=layout, duplicates='none', batch='B8',
                         notes='thumbnail extract attempted; no authored thumbnail exists, so absence is recorded')
    return rows


def apply_observations(rows):
    """Fold the committed pinned-CLI observation evidence into the rows.

    Without the evidence file every row stays not-run/not-assessed (pre-B0), so
    the artifact is stable either way; with it, each row is flipped to
    observed-cli (or statically not-applicable) and pinned to its fixture.
    """
    manifests = {fmt: {entry['file']: entry for entry in json.loads(path.read_text())}
                 for fmt, path in FIXTURE_MANIFESTS.items()}
    evidence_by_batch = {}
    for batch, path in EVIDENCE_BY_BATCH.items():
        if path.exists():
            evidence_by_batch[batch] = json.loads(path.read_text())
            assert evidence_by_batch[batch].get('status') == 'passed', \
                f'{path.name} evidence must be a passed run'
            print('observations:', path.relative_to(ROOT))
        else:
            print(f'note: no committed evidence for batch {batch}; its rows stay not-run')

    def flip(row):
        batch = row['batch']
        reason = static_not_applicable(row)
        if reason:
            row = dict(row)
            row['baselineObservation'] = 'not-applicable'
            row['oracle'] = {'type': 'specification', 'detail': 'axis collapse; not applicable: ' + reason}
            row['notes'] = f'JPEG pilot static not-applicable ({batch}): ' + reason
            return row
        evidence = evidence_by_batch.get(batch)
        if evidence is None:
            return row  # batch pending observation; stays not-run/not-assessed
        row = dict(row)
        observed = {item['id']: item for item in evidence['observations']}
        blocked = {item['id']: item for item in evidence.get('blocked', [])}
        assert not (observed.keys() & blocked.keys()), 'row both observed and blocked'
        if row['rowId'] in blocked:
            assert (batch, row['format']) in BLOCKED_CELLS, 'unexpected blocked row'
            reason = blocked[row['rowId']]['reason']
            assert reason, 'blocked reason required'
            row['baselineObservation'] = 'blocked'
            row['oracle'] = {'type': 'specification', 'detail': 'pending rights and independent fixture/oracle review'}
            row['notes'] = f'{batch} blocked-rights: ' + reason
            return row
        item = observed.get(row['rowId'])
        assert item is not None, f'observation missing for applicable row {row["rowId"]}'
        fixture = manifests[row['format']][item['fixture']]
        if batch in ('B7', 'B8'):
            assert fixture['format'] == row['format'], f'fixture format mismatch for {row["rowId"]}'
        assert fixture['sha256'] == item['fixtureSha256'], \
            f'fixture/evidence hash mismatch for {row["rowId"]}'
        row['fixture'] = {'file': fixture['file'], 'sha256': fixture['sha256'], 'length': fixture['length'],
                          'rights': 'owned-synthetic', 'recipe': fixture['recipe']}
        row['baselineObservation'] = 'observed-cli'
        evidence_dir = EVIDENCE_BY_BATCH[batch].parent.name
        if item['recordOnly'] or item['expectOk'] is None:
            detail = ('record-only bounded probe; no independent expectation or support assertion'
                      if batch in ('B7', 'B8') else
                      'spec silent on this scenario; pinned Exiv2 0.28.9 observation recorded (no pre-asserted behavior)')
            row['oracle'] = {'type': 'pinned-cli', 'detail': detail + '; see evidence/' + evidence_dir}
        else:
            row['oracle'] = {'type': 'specification',
                             'detail': 'spec-derived independent expectation checked at observation time; pinned Exiv2 0.28.9 observation recorded; see evidence/' + evidence_dir}
        facts = f"outcome={item['outcome']}; exit-observed; fixture={item['fixture']}"
        # Batches name the invariant differently (scanPreserved/inputUnchanged); either
        # being false is a finding and must appear in the row, never be dropped.
        preserved = item['facts'].get('scanPreserved', item['facts'].get('inputUnchanged', True))
        if preserved is False:
            facts += '; FINDING scan-changed'
        row['notes'] = f'{batch} pinned-CLI observation: {facts}'
        return row

    applied = tuple(sorted(evidence_by_batch))
    return [flip(row) for row in rows], applied


def build(seeds):
    rows = build_rows(seeds)
    rows, applied_batches = apply_observations(rows)
    enumerated = ({'JPEG/inventory', 'PNG/inventory', 'WEBP/inventory', 'TIFF/inventory', 'XMP/inventory', 'DNG/inventory'}
                  | {f'JPEG/{family}' for family in ('exif', 'comments', 'icc', 'xmp', 'iptc', 'thumbnail')}
                  | {f'PNG/{family}' for family in ('exif', 'comments', 'icc', 'xmp', 'iptc', 'thumbnail')}
                  | {f'WEBP/{family}' for family in ('exif', 'comments', 'icc', 'xmp', 'iptc', 'thumbnail')}
                  | {f'TIFF/{family}' for family in ('exif', 'comments', 'icc', 'xmp', 'iptc', 'thumbnail')}
                  | {f'XMP/{family}' for family in ('exif', 'comments', 'icc', 'xmp', 'iptc', 'thumbnail')}
                  | {f'DNG/{family}' for family in ('exif', 'comments', 'icc', 'xmp', 'iptc', 'thumbnail')}
                  | {f'{fmt}/{family}' for fmt in ('AVIF', 'HEIF', 'HEIC', 'CR3', 'JXL') for family in FAMILIES}
                  | {f'{fmt}/inventory' for fmt in ('AVIF', 'HEIF', 'HEIC', 'CR3', 'JXL')}
                  | {f'{fmt}/{family}' for fmt, _ in B8_FORMAT_LAYOUTS for family in FAMILIES}
                  | {f'{fmt}/inventory' for fmt, _ in B8_FORMAT_LAYOUTS})
    pending = sorted(s['seedId'] for s in seeds['seeds']
                     if f"{s['format']}/{s['family']}" not in {e for e in enumerated if 'inventory' not in e})
    assert len({r['rowId'] for r in rows}) == len(rows)
    enumerations = dict(
        reviewStatus='reviewed',
        reviewedAt='2026-11-09',
        reviewedBy='developer design review, recorded in EXIV2_E01_CENSUS_AND_DENOMINATOR_PROPOSAL.md; B5 adds xmp-packet carriers; B6 adds DNG raw/preview and byte-order layouts; B7 adds BMFF and JXL probe layouts; B8 adds owned PSD/JP2/EPS probe layouts and the pending-format unobserved layouts',
        family=[*FAMILIES, 'inventory'],
        operation=OPERATIONS,
        extractFamilies=EXTRACT_FAMILIES,
        textEncoding=TEXT_ENCODINGS,
        valueType=VALUE_TYPES,
        layout=LAYOUTS,
        duplicates=DUPLICATES,
        sidecar=SIDECARS,
        presenceValues=PRESENCE_VALUES,
        rightsClasses=RIGHTS_CLASSES,
        oracleTypes=ORACLE_TYPES,
    )
    scope_batches = ', '.join(applied_batches) if applied_batches else 'none'
    return dict(
        schemaVersion=1,
        baseline=seeds['baseline'],
        commit=seeds['commit'],
        source=seeds['source'],
        documentationBlob=seeds['documentationBlob'],
        generatedBy='tools/reference/build_census.py',
        denominatorFrozen=False,
        scope=('B0 pilot enumeration for JPEG x {exif, comments, icc} and the JPEG inventory track; '
               'B1 enumeration for JPEG x {xmp, iptc, thumbnail}; '
               'B2 enumeration for PNG x {inventory, comments, exif (noDocumentedClaim), icc, iptc, '
               'thumbnail, xmp}. '
               'B3 enumeration for WebP x {inventory, comments (noDocumentedClaim), exif, icc, '
               'iptc (noDocumentedClaim), thumbnail, xmp}. '
               'B4 enumeration for TIFF x {inventory, comments (noDocumentedClaim), exif, icc, '
               'iptc, thumbnail, xmp}. '
               'B5 enumeration for XMP x {inventory, exif (noDocumentedClaim), comments (noDocumentedClaim), '
               'icc (noDocumentedClaim), iptc (noDocumentedClaim), thumbnail (noDocumentedClaim), xmp} and '
               'the B5 sidecar-presence read probes for JPEG/PNG/WEBP/TIFF. '
               'B6 enumeration for DNG inventory and six metadata families, four D1-derived layouts. '
               'B7 read-only BMFF/JXL probes; CR3 rights-blocked, JXL container and codestream separate. '
               'B8 read-only PSD (flat and EXIF/XMP/IPTC resource), boxed JP2 and ASCII EPS probes; '
               'PGF/EXV/DCP cells stay visible as blocked rows because their fixture and oracle '
               'review is pending, and no B8 write suboperation is enumerated without an owned '
               'writable fixture. '
               + ((f'Pinned-CLI evidence recorded for batch(es) {", ".join(applied_batches)}: '
                    'rows recorded as observed-cli/not-applicable/blocked as appropriate; no coverage percentage; '
                    'no denominator computed. ') if applied_batches else
                  'All rows not-run/not-assessed; no coverage percentage; no denominator computed. ')
               + 'Other documented cells stay visible in EXIV2_OPERATION_SEEDS.json until their batch.'),
        coverage={
            'enumerated': sorted(enumerated - {'JPEG/inventory'}),
            'inventoryTrack': ['JPEG', 'PNG', 'WEBP', 'TIFF', 'XMP', 'DNG', 'AVIF', 'HEIF', 'HEIC', 'CR3', 'JXL'],
            'pendingSeedIds': pending,
        },
        enumerations=enumerations,
        rows=rows,
    )


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    text = json.dumps(build(json.loads(SEEDS.read_text())), indent=2) + '\n'
    if args.check:
        if not OUTPUT.exists() or OUTPUT.read_text() != text:
            raise SystemExit('Census artifact differs from generator output; regenerate and review the delta')
    else:
        OUTPUT.write_text(text)
        print(f'wrote {OUTPUT}')


if __name__ == '__main__':
    main()
