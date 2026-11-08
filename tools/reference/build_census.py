"""Generate the E01 census enumeration artifact (schemaVersion 1).

Expands the documented 31x6 capability seeds into census rows for the batches
enumerated so far. B0 enumerates JPEG x {exif, comments, icc} and the JPEG
inventory track; B1 enumerates JPEG x {xmp, iptc, thumbnail}. All rows are
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
EVIDENCE_BY_BATCH = {
    'B0': ROOT / 'docs/research/evidence/e01-b0-census/observations.json',
    'B1': ROOT / 'docs/research/evidence/e01-b1-census/observations.json',
}
FIXTURE_MANIFEST = ROOT / 'tests/fixtures/jpeg/manifest.json'

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
    'exif': ['app1-exif-ifd0', 'app1-exif-ifd0-subifd', 'app1-exif-multiple'],
    'comments': ['com-single', 'com-multiple'],
    'icc': ['app2-icc-single', 'app2-icc-segmented'],
    'xmp': ['app1-xmp-standard', 'app1-xmp-extended'],
    'iptc': ['app13-8bim-iptc', 'app13-8bim-iptc-multiple'],
    'thumbnail': ['exif-ifd1-jpeg', 'exif-ifd1-uncompressed'],
    'inventory': ['container'],
}
DUPLICATES = ['none', 'first-wins', 'multiple-independent']
SIDECARS = ['none']  # JPEG pilot rows; xmp rows enumerated in a later batch
PRESENCE_VALUES = ['true', 'false', 'unknown']
RIGHTS_CLASSES = ['owned-synthetic', 'cc0-verified', 'public-domain-verified', 'licensed-embedded', 'blocked', 'not-applicable']
ORACLE_TYPES = ['specification', 'pinned-cli', 'exiftool', 'independent']


def _row(row_id, fmt, family, operation, *, value_type, text_encoding, layout, duplicates,
         batch, notes):
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
        noDocumentedClaim=False,
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
        for layout in LAYOUTS['exif']:
            for duplicates in DUPLICATES:
                emit('JPEG', 'exif', operation, value_type='any-documented',
                     text_encoding='not-applicable', layout=layout, duplicates=duplicates,
                     notes='generic EXIF row; valueType split deferred to B14 vocabulary census')

    # JPEG comments: payload encodings are distinct atomic behaviors.
    for operation in ('read', 'insert', 'replace', 'remove'):
        for layout in LAYOUTS['comments']:
            for duplicates in DUPLICATES:
                for encoding in ('ascii', 'utf-8', 'unknown-raw'):
                    emit('JPEG', 'comments', operation, value_type='text-bytes',
                         text_encoding=encoding, layout=layout, duplicates=duplicates,
                         notes='JPEG COM payload as stored; interpretation left to the caller')
    for layout in LAYOUTS['comments']:
        for duplicates in DUPLICATES:
            emit('JPEG', 'comments', 'extract', value_type='opaque', text_encoding='not-applicable',
                 layout=layout, duplicates=duplicates,
                 notes='comment payload copied out as raw bytes')

    # JPEG ICC: single vs multi-segment APP2 profile; opaque bytes, no decode.
    for operation in ('read', 'insert', 'replace', 'remove', 'extract'):
        for layout in LAYOUTS['icc']:
            for duplicates in ('none', 'first-wins'):
                emit('JPEG', 'icc', operation, value_type='opaque', text_encoding='not-applicable',
                     layout=layout, duplicates=duplicates,
                     notes='ICC profile container operation; no profile decoding/validation claimed')

    # JPEG XMP (B1): standard APP1 packet vs base+extension chunks (Extended XMP).
    # The ascii cell is omitted: a pure-ASCII XMP packet is byte-identical to utf-8 behavior.
    for operation in ('read', 'insert', 'replace', 'remove'):
        for layout in LAYOUTS['xmp']:
            for duplicates in ('none', 'first-wins'):
                for encoding in ('utf-8', 'unknown-raw'):
                    emit('JPEG', 'xmp', operation, value_type='text-bytes',
                         text_encoding=encoding, layout=layout, duplicates=duplicates, batch='B1',
                         notes='XMP packet as stored; interpretation left to the caller')
    # JPEG IPTC (B1): Photoshop 8BIM 0x0404 resource; dataset-level duplication axis.
    for operation in ('read', 'insert', 'replace', 'remove'):
        for layout in LAYOUTS['iptc']:
            for duplicates in ('none', 'first-wins'):
                emit('JPEG', 'iptc', operation, value_type='opaque', text_encoding='not-applicable',
                     layout=layout, duplicates=duplicates, batch='B1',
                     notes='IPTC-NAA datasets as raw bytes; no dataset interpretation claimed')
    # JPEG thumbnail (B1): EXIF IFD1 embedded thumbnail (JPEG-compressed vs uncompressed strip).
    for operation in ('read', 'insert', 'replace', 'remove', 'extract'):
        for layout in LAYOUTS['thumbnail']:
            emit('JPEG', 'thumbnail', operation, value_type='opaque', text_encoding='not-applicable',
                 layout=layout, duplicates='none', batch='B1',
                 notes='embedded EXIF IFD1 thumbnail container operation; bytes opaque')
    return rows


def apply_observations(rows):
    """Fold the committed pinned-CLI observation evidence into the rows.

    Without the evidence file every row stays not-run/not-assessed (pre-B0), so
    the artifact is stable either way; with it, each row is flipped to
    observed-cli (or statically not-applicable) and pinned to its fixture.
    """
    manifest = {entry['file']: entry for entry in json.loads(FIXTURE_MANIFEST.read_text())}
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
        fixture = manifest[item['fixture']]
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
    enumerated = {'JPEG/inventory'} | {f'JPEG/{family}' for family in ('exif', 'comments', 'icc', 'xmp', 'iptc', 'thumbnail')}
    pending = sorted(s['seedId'] for s in seeds['seeds']
                     if f"{s['format']}/{s['family']}" not in {e for e in enumerated if e != 'JPEG/inventory'})
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
               'B1 enumeration for JPEG x {xmp, iptc, thumbnail}. '
               + ((f'Pinned-CLI observation complete for batch(es) {", ".join(applied_batches)}: '
                    'rows flipped to observed-cli/not-applicable; no coverage percentage; '
                    'no denominator computed. ') if applied_batches else
                  'All rows not-run/not-assessed; no coverage percentage; no denominator computed. ')
               + 'Other documented cells stay visible in EXIV2_OPERATION_SEEDS.json until their batch.'),
        coverage={
            'enumerated': sorted(enumerated - {'JPEG/inventory'}),
            'inventoryTrack': ['JPEG'],
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
