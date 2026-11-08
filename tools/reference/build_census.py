"""Generate the E01 census enumeration artifact (schemaVersion 1).

Expands the documented 31x6 capability seeds into census rows for the batches
enumerated so far. B0 pilot enumerates JPEG x {exif, comments, icc} and the
JPEG inventory track. All rows are generated deterministically; the artifact is
immutable between reviews (`--check` fails on drift). This is enumeration, not
observation: every row starts as not-run / not-assessed and no denominator is
computed here.

Usage: build_census.py          # write docs/research/EXIV2_CENSUS.json
       build_census.py --check  # fail if the committed artifact differs
"""
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SEEDS = ROOT / 'docs/research/EXIV2_OPERATION_SEEDS.json'
OUTPUT = ROOT / 'docs/research/EXIV2_CENSUS.json'

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

    def emit(fmt, family, operation, **kwargs):
        key = (fmt, family, operation)
        counter = counters.get(key, 0) + 1
        counters[key] = counter
        rows.append(_row(f'EX0289-{fmt}-{family.upper()}-{operation.upper()}-{counter:04d}',
                         fmt, family, operation, batch='B0', **kwargs))

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
    return rows


def build(seeds):
    rows = build_rows(seeds)
    enumerated = {'JPEG/inventory'} | {f'JPEG/{family}' for family in ('exif', 'comments', 'icc')}
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
    return dict(
        schemaVersion=1,
        baseline=seeds['baseline'],
        commit=seeds['commit'],
        source=seeds['source'],
        documentationBlob=seeds['documentationBlob'],
        generatedBy='tools/reference/build_census.py',
        denominatorFrozen=False,
        scope=('B0 pilot enumeration for JPEG x {exif, comments, icc} and the JPEG inventory track. '
               'All rows not-run/not-assessed; no coverage percentage; no denominator computed. '
               'Other documented cells stay visible in EXIV2_OPERATION_SEEDS.json until their batch.'),
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
