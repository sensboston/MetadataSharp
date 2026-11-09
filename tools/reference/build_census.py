"""Generate the E01 census enumeration artifact (schemaVersion 1).

Expands the documented 31x6 capability seeds into census rows for the batches
enumerated so far. B0 enumerates JPEG x {exif, comments, icc} and the JPEG
inventory track; B1 enumerates JPEG x {xmp, iptc, thumbnail}; B2 enumerates
PNG x {inventory, comments, exif, icc, iptc, thumbnail, xmp}; B3 enumerates
WebP x {inventory, comments, exif, icc, iptc, thumbnail, xmp}; B4 enumerates
TIFF x {inventory, comments, exif, icc, iptc, thumbnail, xmp}. All rows are
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
}
FIXTURE_MANIFESTS = {
    'JPEG': ROOT / 'tests/fixtures/jpeg/manifest.json',
    'PNG': ROOT / 'tests/fixtures/png/manifest.json',
    'WEBP': ROOT / 'tests/fixtures/webp/census-manifest.json',
    'TIFF': ROOT / 'tests/fixtures/tiff/manifest.json',
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

# --- Approved enumerations (design review 2026-11-08) -----------------------
# Every axis value used by a row MUST come from this block (closed world).
# Adding a value = PR + review; it never happens inside a row.
FAMILIES = ['exif', 'iptc', 'xmp', 'comments', 'icc', 'thumbnail']
OPERATIONS = ['inventory', 'read', 'insert', 'replace', 'remove', 'extract']
EXTRACT_FAMILIES = ['comments', 'icc', 'thumbnail']
TEXT_ENCODINGS = ['ascii', 'utf-8', 'unknown-raw', 'not-applicable']
VALUE_TYPES = ['any-documented', 'opaque', 'text-bytes']  # 'any-documented': B14 splits EXIF
LAYOUTS = {
    'exif': ['app1-exif-ifd0', 'app1-exif-ifd0-subifd', 'app1-exif-multiple', 'png-exif', 'webp-exif', 'tiff-exif'],
    'comments': ['com-single', 'com-multiple', 'png-text', 'webp-comments', 'tiff-comments'],
    'icc': ['app2-icc-single', 'app2-icc-segmented', 'iccp', 'webp-iccp', 'tiff-icc'],
    'xmp': ['app1-xmp-standard', 'app1-xmp-extended', 'itxt-xmp', 'ztxt-xmp', 'webp-xmp', 'tiff-xmp'],
    'iptc': ['app13-8bim-iptc', 'app13-8bim-iptc-multiple', 'ztxt-raw-profile-iptc', 'itxt-raw-profile-iptc', 'webp-iptc', 'tiff-iptc'],
    'thumbnail': ['exif-ifd1-jpeg', 'exif-ifd1-uncompressed'],
    'inventory': ['container'],
}
DUPLICATES = ['none', 'first-wins', 'multiple-independent']
SIDECARS = ['none']  # JPEG pilot rows; xmp rows enumerated in a later batch
PRESENCE_VALUES = ['true', 'false', 'unknown']
RIGHTS_CLASSES = ['owned-synthetic', 'cc0-verified', 'public-domain-verified', 'licensed-embedded', 'blocked', 'not-applicable']
ORACLE_TYPES = ['specification', 'pinned-cli', 'exiftool', 'independent']


def _row(row_id, fmt, family, operation, *, value_type, text_encoding, layout, duplicates,
         batch, notes, no_claim=False):
    return dict(
        rowId=row_id,
        format=fmt,
        family=family,
        operation=operation,
        valueType=value_type,
        textEncoding=text_encoding,
        layout=layout,
        duplicates=duplicates,
        sidecar='none',
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

    def emit(fmt, family, operation, batch='B0', **kwargs):
        key = (fmt, family, operation)
        counter = counters.get(key, 0) + 1
        counters[key] = counter
        rows.append(_row(f'EX0289-{fmt}-{family.upper()}-{operation.upper()}-{counter:04d}',
                         fmt, family, operation, batch=batch, **kwargs))

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
        item = observed.get(row['rowId'])
        assert item is not None, f'observation missing for applicable row {row["rowId"]}'
        fixture = manifests[row['format']][item['fixture']]
        assert fixture['sha256'] == item['fixtureSha256'], \
            f'fixture/evidence hash mismatch for {row["rowId"]}'
        row['fixture'] = {'file': fixture['file'], 'sha256': fixture['sha256'], 'length': fixture['length'],
                          'rights': 'owned-synthetic', 'recipe': fixture['recipe']}
        row['baselineObservation'] = 'observed-cli'
        evidence_dir = EVIDENCE_BY_BATCH[batch].parent.name
        if item['recordOnly'] or item['expectOk'] is None:
            row['oracle'] = {'type': 'pinned-cli',
                             'detail': 'spec silent on this scenario; pinned Exiv2 0.28.9 observation recorded (no pre-asserted behavior); see evidence/' + evidence_dir}
        else:
            row['oracle'] = {'type': 'specification',
                             'detail': 'spec-derived independent expectation checked at observation time; pinned Exiv2 0.28.9 observation recorded; see evidence/' + evidence_dir}
        facts = f"outcome={item['outcome']}; exit-observed; fixture={item['fixture']}"
        if item['facts'].get('scanPreserved') is False:
            facts += '; FINDING scan-changed'
        row['notes'] = f'{batch} pinned-CLI observation: {facts}'
        return row

    applied = tuple(sorted(evidence_by_batch))
    return [flip(row) for row in rows], applied


def build(seeds):
    rows = build_rows(seeds)
    rows, applied_batches = apply_observations(rows)
    enumerated = ({'JPEG/inventory', 'PNG/inventory', 'WEBP/inventory', 'TIFF/inventory'}
                  | {f'JPEG/{family}' for family in ('exif', 'comments', 'icc', 'xmp', 'iptc', 'thumbnail')}
                  | {f'PNG/{family}' for family in ('exif', 'comments', 'icc', 'xmp', 'iptc', 'thumbnail')}
                  | {f'WEBP/{family}' for family in ('exif', 'comments', 'icc', 'xmp', 'iptc', 'thumbnail')}
                  | {f'TIFF/{family}' for family in ('exif', 'comments', 'icc', 'xmp', 'iptc', 'thumbnail')})
    pending = sorted(s['seedId'] for s in seeds['seeds']
                     if f"{s['format']}/{s['family']}" not in {e for e in enumerated if 'inventory' not in e})
    assert len({r['rowId'] for r in rows}) == len(rows)
    enumerations = dict(
        reviewStatus='reviewed',
        reviewedAt='2026-11-08',
        reviewedBy='developer design review, recorded in EXIV2_E01_CENSUS_AND_DENOMINATOR_PROPOSAL.md',
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
               + ((f'Pinned-CLI observation complete for batch(es) {", ".join(applied_batches)}: '
                    'rows flipped to observed-cli/not-applicable; no coverage percentage; '
                    'no denominator computed. ') if applied_batches else
                  'All rows not-run/not-assessed; no coverage percentage; no denominator computed. ')
               + 'Other documented cells stay visible in EXIV2_OPERATION_SEEDS.json until their batch.'),
        coverage={
            'enumerated': sorted(enumerated - {'JPEG/inventory'}),
            'inventoryTrack': ['JPEG', 'PNG', 'WEBP', 'TIFF'],
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
