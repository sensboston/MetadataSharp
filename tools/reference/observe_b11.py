"""B11 read-only pinned CLI probes for owned CIFF, BMP, GIF, TGA and container stubs.

One census row is exactly one pinned-CLI flag per owned fixture, so a recognition row and a
metadata row never share a command. Outcomes are recorded, never gated: a refusal is a
finding about this build, not a support claim, and an empty print only proves this CLI build
printed nothing for this fixture. The CRW vendor-private layout stays blocked rather than
refused because no fixture is cleared for it
(docs/research/E01_B11_RIGHTS_AND_ORACLE_REVIEW.md).

These fixtures are authored containers, not camera captures. Each carries the header contract
the pinned build dispatches on, plus the CIFF components or absence of components this batch
reads, and no pixel data; so a refusal says nothing about a real CRW or a real movie, and a
success says nothing about MetadataSharp. No MakerNote is interpreted beyond what the pinned
`crwMapping_` table itself does, and nothing is written back to a fixture.

Four things are recorded here that earlier batches had no reason to look at:

* `-pS` reaches `Image::printStructure` (src/image.cpp:136), which throws
  ``kerUnsupportedImageType`` for CRW, BMP, GIF, TGA and for all five video stubs -- none of
  them overrides it -- while the unknown-brand movie lands on BmffVideo, which *does*
  (src/bmffimage.cpp:743). The pair of MOV stubs is therefore the one place in this batch
  where a structure print is expected to produce text, and that difference is an observation
  about two readers, not a judgement about containers.
* `-pC` is the ICC-profile print, not a "copy" or "comment" print; with no ICC carrier in any
  B11 fixture every `-pC` row reaches the same throw or the same empty profile, which is why
  the three minimal formats are expected to agree with each other here.
* `-et`'s exit code in this build is **the number of bytes written**, not a status
  (``app/actions.cpp:858`` -> ``src/exiv2.cpp`` thumbnail action -> ``ExifThumbC::copy``); a
  reader with no thumbnail returns 0 and writes nothing, which is recorded as
  `zero-exit-no-artifact` and never as a defect. Where a fixture does carry a CIFF `0x2008`
  component the recorder predicts the artifact length from the fixture bytes -- that is a
  consistency check on this recorder, not an expectation about CRW support.
* `-pa` asks for all three tables, so on a format whose reader fills nothing it prints an
  empty listing with exit 0. That is recorded as the text it printed, never as "Exif is
  unsupported".
"""
import argparse
import base64
from collections import Counter
import json
import os
from pathlib import Path
import re
import shutil
import time
import zlib

import b11_container_fixtures as catalog
import b7_exiftool_probe as secondary
import exiv2_baseline as baseline
import observe_b0 as common
from observe_b9 import verify_source

ROOT = Path(__file__).resolve().parents[2]
# One flag per census row, chosen so a row's flag is the narrowest print that reaches the
# family it names. `-pi` (IPTC table) is deliberately absent: no B11 fixture carries IPTC, so
# the listing rows use `-pa`, which is what the review authorized (7 flags for the seed-grid
# formats, 5 for the container stubs).
FLAGS = {'inventory': ['-pS'], 'exif': ['-pe'], 'comments': ['-pc'], 'icc': ['-pC'],
         'xmp': ['-px'], 'iptc': ['-pa'], 'thumbnail': ['-pa']}
SEED_FORMATS = ('CRW', 'BMP', 'GIF', 'TGA')
VIDEO_FORMATS = ('MOV', 'MKV', 'AVI', 'WAV', 'ASF')
FORMATS = SEED_FORMATS + VIDEO_FORMATS
# The blocked cell carries the review's reason verbatim so the census and the evidence cannot
# disagree about why nothing was attempted.
BLOCK_REASONS = {'CRW': catalog.blocked_reason()}
FACT_KEYS = ('dispatch', 'fileLength', 'fileSha256')
CIFF_KEYS = ('rootOffset', 'padding', 'regionLength')
FORMAT_KEYS = {'CRW': CIFF_KEYS, 'BMP': ('headerSize', 'width', 'height', 'bitsPerPixel',
                                         'dataOffset'),
               'GIF': ('signature', 'lsdBytes', 'width', 'height'),
               'TGA': ('footerSigned', 'width', 'height', 'recognisedByName',
                       'footerRouteReachableByFactory'),
               'MOV': ('atom', 'brand', 'brandKnownToQuickTime', 'bmffAlsoAccepts'),
               'MKV': ('magic', 'headerId'), 'AVI': ('fourcc',), 'WAV': ('fourcc',),
               'ASF': ('headerGuid', 'objectSize', 'subobjects')}


BLOCK_KEY = {'CRW': 'ciff', 'BMP': 'bmp', 'GIF': 'gif', 'TGA': 'tga', 'MOV': 'mov',
             'MKV': 'matroska', 'AVI': 'riff', 'WAV': 'riff', 'ASF': 'asf'}


def flags_for(row):
    """One read-only flag per row; extraction is asked for explicitly."""
    if row['family'] == 'inventory':
        return FLAGS['inventory']
    if row['operation'] == 'extract':
        return ['-et']
    return FLAGS[row['family']]


def fixture_for(row):
    return catalog.DIRECTORY / catalog.FIXTURES[row['layout']][1]


def container_facts(data, name, layout):
    """The framing this build was shown, in the vocabulary of the review's §3 facts."""
    facts = catalog.structure(layout, name, data)
    out = {key: facts[key] for key in FACT_KEYS if key in facts}
    out['matched'] = facts['recognizers']['matched']
    fmt = catalog.FIXTURES[layout][0]
    # The framing block is named for the *container walked*, not the format label: CRW's is a
    # CIFF directory tree, and AVI and WAV are both walked as RIFF. Without this map a WAV or
    # MKV record silently loses its block, because the lookup key would not exist.
    key = BLOCK_KEY[fmt]
    block = facts.get(key)
    if block is None:
        raise ValueError(f'{layout}: structure() produced no {key!r} block for {fmt}')
    out[key] = {name: block[name] for name in FORMAT_KEYS.get(fmt, ()) if name in block}
    if set(out[key]) != set(FORMAT_KEYS.get(fmt, ())):
        raise ValueError(f'{layout}: {key} block lost keys {set(FORMAT_KEYS.get(fmt, ())) - set(out[key])}')
    for key in ('byteOrder', 'mappedKeys', 'thumbnailLength', 'previewLength'):
        if key in facts:
            out[key] = facts[key]
    return out


def extract_semantics(flag, rc, artifacts, expected=None):
    """What `-et` means in this build: the status is a byte count, not a success flag."""
    if flag != '-et':
        return None
    written = sum(item['length'] for item in artifacts)
    semantics = {'exitCodeIsByteCountInPinnedBuild': True,
                 'artifactBytes': written,
                 'exitCodeEqualsArtifactBytes': rc == written,
                 'reportedNoThumbnail': False}
    if expected is not None:
        # A recorder consistency check only: the prediction is computed from the fixture
        # bytes this repository wrote, so a mismatch means the recorder or the fixture
        # changed, not that CRW thumbnails do or do not work.
        semantics['predictedArtifactBytes'] = expected
        semantics['predictionMatched'] = expected == written
    return semantics


def predicted_artifact_bytes(layout, operation):
    entry = next(item for item in catalog.entries() if item['layout'] == layout)
    if operation != 'extract':
        return None
    return entry.get('thumbnailLength')


def observe_row(row, exe, work):
    if catalog.FIXTURES[row['layout']][0] != row['format']:
        raise ValueError(f"census layout {row['layout']} and format {row['format']} disagree")
    source = fixture_for(row)
    original = source.read_bytes()
    # Keep the owned extension: TGA recognition in this build *is* the file name, so a
    # renamed copy would probe a different question than the census row asks.
    target = work / ('case' + source.suffix)
    target.write_bytes(original)
    flags = flags_for(row)
    commands = {'commands': []}  # throwaway sink: soft_run's stdout excerpt stays out of the report
    rc, stdout, stderr = common.soft_run(commands, [exe, *flags, target], cwd=work)
    after = target.read_bytes()
    artifacts = [{'file': p.name, 'length': p.stat().st_size,
                  'sha256': baseline.digest(p.read_bytes())}
                 for p in sorted(work.iterdir()) if p.is_file() and p != target]
    text = stdout.decode('utf-8', 'replace')
    err = re.sub(r'\S*/case\.[A-Za-z0-9]+', '<fixture>', stderr.decode('utf-8', 'replace'))[:300]
    expected = predicted_artifact_bytes(row['layout'], row['operation'])
    semantics = extract_semantics(flags[0], rc, artifacts, expected)
    if semantics is not None and 'thumbnail' in err.lower():
        semantics['reportedNoThumbnail'] = True
    # A read-only probe that mutated the input is a finding even if the CLI claimed success.
    if original != after:
        outcome = 'scan-changed'
    elif rc == 0:
        outcome = ('zero-exit-artifact-produced' if semantics is not None and artifacts else
                   'zero-exit-no-artifact' if semantics is not None else
                   common.classify(rc, True, None, True, artifact_produced=bool(artifacts)))
    else:
        outcome = ('nonzero-exit-artifact-produced' if artifacts else 'refused') if semantics \
            else common.classify(rc, True, None, True, artifact_produced=bool(artifacts))
    if semantics is not None and not semantics['exitCodeEqualsArtifactBytes']:
        # The one shape this build must not produce: a status that is not the byte count.
        finding = (f'-et exit code {rc} does not equal the {semantics["artifactBytes"]} '
                    'bytes the command wrote')
    else:
        finding = None
    return {'id': row['rowId'], 'seedId': f"EX0289-{row['format']}-{row['family'].upper()}",
            'operation': row['operation'], 'family': row['family'], 'layout': row['layout'],
            'flag': flags[0], 'exitCode': rc,
            'duplicates': row['duplicates'], 'textEncoding': row['textEncoding'],
            'fixture': source.name, 'fixtureSha256': baseline.digest(original),
            'fixtureRights': 'owned-synthetic', 'inputSha256': baseline.digest(original),
            'outputSha256': baseline.digest(after), 'recordOnly': True, 'expectOk': None,
            'outcome': outcome,
            'facts': {'finding': finding,
                      'inputUnchanged': original == after,
                      'stdoutSha256': baseline.digest(stdout), 'stdoutLength': len(stdout),
                      'stderr': err, 'artifacts': artifacts,
                      'container': container_facts(original, source.name, row['layout']),
                      'printedTokens': sorted(token for token in catalog.TOKENS if token in text),
                      'extractSemantics': semantics}}


def chunks(report, size=4000, limit=30):
    """Compress the report into check-run annotations, the only retrievable channel.

    Bulk Actions logs are not reachable from this environment, so a report that would need
    more annotations than the limit is a hard error instead of silent truncation. The limit is
    30 here rather than B10's 10 because B11 records 87 observations and 8 blocked cells
    instead of 32 and 32; the job's *other* recorder caps itself at 12, and GitHub's limit is
    50 annotations per check run for the whole job.
    """
    if not report.get('observations') and not report.get('blocked'):
        raise ValueError('B11 report carries neither observations nor blocked cells')
    payload = json.dumps(report, separators=(',', ':'), sort_keys=True)
    encoded = base64.b64encode(zlib.compress(payload.encode(), 9)).decode()
    parts = [encoded[i:i + size] for i in range(0, len(encoded), size)]
    if not parts:
        raise ValueError('B11 report is empty; there would be nothing to recover')
    if len(parts) > limit:
        raise ValueError(f'B11 report needs {len(parts)} annotations; the recovery limit is {limit}')
    return parts


def environment():
    """The timezone the runner printed datetimes in, because 0x180e uses localtime_r."""
    offset = -time.altzone if time.daylight else -time.timezone
    return {'TZ': os.environ.get('TZ'), 'utcOffsetSeconds': offset,
            'note': 'CrwMap::decode0x180e renders 0x180e through localtime_r, so the DateTime '
                    'text this batch records is only reproducible with the offset on the record'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--upstream', type=Path, required=True)
    parser.add_argument('--build', type=Path, required=True)
    parser.add_argument('--exiftool-checkout', type=Path, required=True)
    parser.add_argument('--census', type=Path, default=ROOT / 'docs/research/EXIV2_CENSUS.json')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/e01')
    args = parser.parse_args()
    upstream, build = args.upstream.resolve(), args.build.resolve()
    out = (args.output / 'b11').resolve()
    out.mkdir(parents=True, exist_ok=True)
    report = {'schema': 1, 'status': 'running', 'batch': 'B11',
              'scope': 'read-only pinned CLI probes of owned synthetic CIFF, BMP, GIF and TGA '
                       'containers and of five container stubs (QuickTime with a known and an '
                       'unknown brand, Matroska, RIFF/AVI, RIFF/WAV and ASF), plus the blocked '
                       'Canon vendor-private CIFF cell; no product verification, no support '
                       'claim, no vendor layout conformance claim',
              'limitations': 'fixtures are authored containers, not camera captures: a CRW here '
                             'carries CIFF components and no raw image data, BMP/GIF/TGA carry '
                             'their header and no pixel payload, and the five container stubs '
                             'carry only the bytes their reader tests, so a refusal is not '
                             'evidence that a format is unsupported and a success is not '
                             'evidence of metadata support; no MetadataSharp code is exercised; '
                             'no B11 write suboperation is attempted, so the symmetric CRW '
                             'encoder is neither claimed nor refuted; no video metadata family '
                             'is enumerated because the pinned table claims none; ExifTool is a '
                             'secondary reference, not an oracle.',
              'sourceCommit': os.environ.get('E01_SOURCE_COMMIT'),
              'baseline': {'version': baseline.VERSION, 'sourceCommit': baseline.PIN},
              'environment': environment(),
              'denominatorFrozen': False, 'verification': 'not-assessed',
              'observations': [], 'blocked': [], 'secondaryObservations': {},
              'outcomeCounts': {}}

    def save():
        (out / 'observations.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')

    try:
        if not re.fullmatch('[0-9a-f]{40}', report['sourceCommit'] or ''):
            raise ValueError('missing E01_SOURCE_COMMIT')
        exe = verify_source(upstream, build, report)
        report['fixtureManifest'] = catalog.verify()
        rows = [r for r in json.loads(args.census.read_text())['rows'] if r['batch'] == 'B11']
        if len(rows) != 95:
            raise ValueError(f'unexpected B11 census row count: {len(rows)}')
        for i, row in enumerate(rows):
            if row['layout'] not in catalog.FIXTURES:
                if row['format'] != 'CRW':
                    raise ValueError(f'{row["rowId"]}: a non-CRW row has no fixture in B11')
                report['blocked'].append({'id': row['rowId'],
                                          'reason': BLOCK_REASONS[row['format']]})
                save()
                continue
            if row['format'] in VIDEO_FORMATS and row['family'] != 'inventory':
                raise ValueError(f'{row["rowId"]}: a container stub row must be inventory-only')
            work = out / 'work' / f'{i:04d}'
            if work.exists():
                shutil.rmtree(work)
            work.mkdir(parents=True)
            report['observations'].append(observe_row(row, exe, work))
            save()
        if len({o['id'] for o in report['observations']}) != len(report['observations']):
            raise ValueError('duplicate B11 observation id')
        if {o['id'] for o in report['observations']} & {b['id'] for b in report['blocked']}:
            raise ValueError('B11 row both observed and blocked')
        if {b['id'] for b in report['blocked']} != {r['rowId'] for r in rows
                                                    if r['layout'] not in catalog.FIXTURES}:
            raise ValueError('B11 blocked set does not match the unobserved layouts')
        used = sorted({o['fixture'] for o in report['observations']})
        # The manifest carries the blocked cell too, with `file: null` -- it is enumerated in
        # the catalog precisely so the blocked layout cannot quietly gain a fixture later, so
        # the "was everything cleared probed" comparison has to skip it rather than demand a
        # probe of bytes that do not exist.
        cleared = {entry['file'] for entry in report['fixtureManifest'] if entry['file']}
        if set(used) != cleared:
            raise ValueError(f'a cleared B11 fixture was never probed: {sorted(cleared - set(used))}')
        for item in report['observations']:
            semantics = item['facts']['extractSemantics']
            if semantics is not None and not semantics['exitCodeEqualsArtifactBytes']:
                semantics['finding'] = ('exit code differs from the bytes written; see '
                                        'app/actions.cpp:858')
        for name in used:
            report['secondaryObservations'][name] = secondary.observe(
                args.exiftool_checkout, catalog.DIRECTORY / name)
        counts = Counter(o['outcome'] for o in report['observations'])
        counts['blocked-rights'] = len(report['blocked'])
        counts['static-not-applicable'] = 0
        report['outcomeCounts'] = dict(sorted(counts.items()))
        report['status'] = 'passed'
        save()
        parts = chunks(report)
        for i, part in enumerate(parts):
            print(f'::notice title=B11Z observations.json {i}/{len(parts)}::{part}', flush=True)
        # Recovery marker, emitted last: a check run keeps only 50 annotations, so the
        # recovery side must confirm it saw exactly `len(parts)` chunks (and this line)
        # before treating the evidence as whole. A silent drop becomes a mismatch.
        print(f'::notice title=B11Z observations.json count::{len(parts)}', flush=True)
    except Exception:
        import traceback
        report['status'] = 'failed'
        report['failure'] = traceback.format_exc()[-3500:]
        save()
        raise


if __name__ == '__main__':
    main()
