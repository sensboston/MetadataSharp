"""Pinned, read-only B12 recognition-ladder probe; not a census batch or a support test.

B12 asks one question: at how many bytes does this build stop being able to *find* a reader whose magic is
already at offset 0. The ladder in `b12_recognition_ladder` varies only the total file length, so a flip is a
measurement and not an inference about a format. Nothing here edits a fixture, reads the census, or claims
coverage: the report is observations plus the recorded prediction, and the CI step's own verdict is the one the
census suite makes about *itself*.

Two flags per rung, chosen for what a black box can actually distinguish:

* `-pS` is `pmStructure` -> `ImageFactory::open(path)` then `image->printStructure(out, kpsBasic)`
  (`app/actions.cpp:1983-1991`). Nine classes override `printStructure`; BMP, GIF and TGA are not among them, so
  the base throw (`src/image.cpp:136-138`) is only reachable for a file the scan *dispatched*: "Image type <path>
  is not supported" means a reader was reached, and "<path>: The file contains data of an unknown image type"
  means it was not. For these three formats that is a clean binary, which is the whole point of the batch.
* `-pe` is the documented read path with no structure dependency: an image that opened and holds no Exif exits 0
  with an empty listing, so the pair separates "refused by the scan" from "reached a reader that reports
  nothing" -- which neither flag does alone.

The write-capable and extract flags B11 used are deliberately absent: a failure mode in which a write
half-happens must not disturb a length measurement. `inputUnchanged` is checked for every row instead.

The prediction each row is compared against was written before this recorder existed, in
docs/research/E01_B12_RIGHTS_AND_ORACLE_REVIEW.md, and is carried into the rows by the fixture manifest: the
recorder copies `entry['prediction']` rather than recomputing it, so a re-run cannot retrofit an expectation.
"""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import zlib

import b12_recognition_ladder as ladder
import exiv2_baseline as baseline
import observe_b0 as common
from observe_b11 import environment
from observe_b9 import verify_source

CHUNK_LIMIT = 8    # a 94-row report is small; this keeps recovery provable, not hopeful
FLAGS = ladder.FLAGS

# Message -> family, in the order the pinned sources produce them. `reader-reached` carries two distinct
# messages on purpose (see the -pS note above): for these three formats both mean a reader ran, which is the
# fact under test, and `signal` keeps the raw text so the distinction is never lost in the label.
FAMILIES = (
    ('The file contains data of an unknown image type', 'unknown-image-type'),
    ('is not supported', 'reader-reached'),
    ('Failed to read input data', 'throws-during-scan'),
    ('Failed to allocate memory', 'throws-during-scan'),
    ('Failed to read image data', 'reader-reached'),
    ('corrupted image metadata', 'corrupted-metadata'),
    ('not a', 'reader-reached'),          # kerNotAnImage: "This does not look like a %1 image"
)


def classify(row):
    """Which of the predicted families this row's own words fall into."""
    text = row['stderr']
    if 'Exiv2 exception' not in text and row['exitCode'] == 0:
        # -pe on an image that opened and carries no Exif: exit 0, empty stdout, no exception.
        return 'reader-reached', None
    for needle, family in FAMILIES:
        if needle in text:
            return family, needle
    return 'other', None


def message_of(stderr):
    """The error line itself, minus the CLI's `Exiv2 exception in print action for file <p>:` prefix."""
    lines = [line for line in stderr.splitlines() if line.strip()]
    return lines[-1] if lines else None


def run_probe(exe, source, work, entry):
    original = source.read_bytes()
    operations = []
    commands = {'commands': []}
    for flag in FLAGS:
        subdir = work / flag[1:]
        subdir.mkdir(parents=True, exist_ok=True)
        # The name, not just the bytes, is a measured input: isTgaType tests the path. `case.targa` keeps
        # the fixture's extension so the rung means what the rung on disk means.
        target = subdir / ('case' + source.suffix)
        target.write_bytes(original)
        rc, stdout, stderr = common.soft_run(commands, [exe, flag, target], cwd=subdir)
        row = {'flag': flag, 'exitCode': rc,
               'inputUnchanged': target.read_bytes() == original,
               'stdout': stdout.decode('utf-8', 'replace')[:1200], 'stdoutLength': len(stdout),
               'stderr': stderr.decode('utf-8', 'replace')[:600],
               'artifacts': [{'file': path.name, 'length': path.stat().st_size,
                              'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
                             for path in sorted(subdir.iterdir())
                             if path.is_file() and path != target]}
        row['message'] = message_of(row['stderr'])
        row['family'], row['matchedNeedle'] = classify(row)
        row['predictedFamily'] = entry['prediction']['predictedFamily']
        row['matchesPrediction'] = row['family'] == row['predictedFamily']
        # A write-capable flag would let a half-written file masquerade as a recognition result; these two
        # must never produce one, and the row records the check rather than assuming it.
        row['noArtifactProduced'] = not row['artifacts']
        operations.append(row)
    return {'layout': entry['layout'], 'kind': entry['kind'], 'size': entry['size'],
            'fixture': source.name, 'fixtureSha256': baseline.digest(original),
            'flags': list(FLAGS), 'operations': operations, 'commands': commands['commands']}


def ladder_summary(report):
    """Where each ladder flips, computed from the rows -- never typed in.

    `flipSize` is the smallest size at which the ladder reports `reader-reached`; `lastRefusal` the largest
    size at which it still reports the unknown-type family. A ladder whose two do not bracket one size has no
    single boundary, and `monotonic` says so. `entriesBeforeFlipDisagree` counts the rows where the recorded
    prediction and the build parted ways, which is the number the finding is about.
    """
    out = {}
    for kind in sorted({row['kind'] for row in report['observations']}):
        rows = sorted((r for r in report['observations'] if r['kind'] == kind), key=lambda r: r['size'])
        reached = [r['size'] for r in rows
                   if any(o['family'] == 'reader-reached' for o in r['operations'])]
        refused = [r['size'] for r in rows
                   if all(o['family'] in ('unknown-image-type', 'throws-during-scan') for o in r['operations'])]
        # Plain lists, not tuples: the summary is recomputed from a JSON round trip in the evidence
        # test, and a tuple/list drift would make the committed report unrecomputable.
        disagree = [[r['size'], o['flag'], o['family'], o['predictedFamily']] for r in rows
                    for o in r['operations'] if not o['matchesPrediction']]
        out[kind] = {'rungs': len(rows), 'sizes': [r['size'] for r in rows],
                     'flipSize': min(reached) if reached else None,
                     'lastRefusal': max(refused) if refused else None,
                     'monotonic': bool(reached) and bool(refused) and max(refused) < min(reached),
                     'attributableRungs': [r['size'] for r in rows if any(
                         o['matchesPrediction'] and o['family'] == 'reader-reached' for o in r['operations'])],
                     'rowsDifferingFromPrediction': len(disagree),
                     'disagreements': disagree[:20]}
    return out


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--upstream', type=Path, required=True)
    parser.add_argument('--build', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=Path('artifacts/e01/b12-ladder'))
    args = parser.parse_args()
    upstream, build, out = args.upstream.resolve(), args.build.resolve(), args.output.resolve()
    out.mkdir(parents=True, exist_ok=True)
    report = {'schema': 1, 'status': 'running',
              'scope': 'B12 recognition ladder only; no census rows, no support claims',
              'sourceCommit': os.environ.get('E01_SOURCE_COMMIT'), 'baseline': {},
              'environment': environment(),
              'denominatorFrozen': False, 'verification': 'not-assessed',
              'observations': [], 'summary': {}, 'prediction': {
                  'name': 'registry-strand staircase',
                  'basis': 'static reading of the pinned source, recorded before the run; '
                           'docs/research/E01_B12_RIGHTS_AND_ORACLE_REVIEW.md',
                  'epsThrowBandBelow': ladder.EPS_READ_BYTES,
                  'strandedWindow': ladder.XMP_WINDOW,
                  'strandedFormats': list(ladder.STRANDED_FORMATS),
                  'registryIndex': {name: index for name, index in ladder.INDEX.items()}}}

    def save():
        (out / 'observations.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')

    try:
        if not re.fullmatch('[0-9a-f]{40}', report['sourceCommit'] or ''):
            raise ValueError('missing source commit')
        exe = verify_source(upstream, build, report)
        report['fixtureManifest'] = ladder.verify()
        work = out / 'work'
        if work.exists():
            shutil.rmtree(work)
        for item in report['fixtureManifest']:
            report['observations'].append(
                run_probe(exe, ladder.DIRECTORY / item['file'], work / Path(item['file']).stem, item))
        expected = len(report['fixtureManifest']) * len(FLAGS)
        recorded = sum(len(row['operations']) for row in report['observations'])
        if recorded != expected:
            raise ValueError(f'B12 ladder recorded {recorded} rows, expected {expected}')
        report['recordCount'] = recorded
        report['summary'] = ladder_summary(report)
        report['inputUnchangedEverywhere'] = all(op['inputUnchanged']
                                                 for row in report['observations'] for op in row['operations'])
        report['noArtifactsEverywhere'] = all(op['noArtifactProduced']
                                              for row in report['observations'] for op in row['operations'])
        report['familyCounts'] = dict(sorted(_count(report).items()))
        if not (report['inputUnchangedEverywhere'] and report['noArtifactsEverywhere']):
            raise ValueError('a read-only ladder produced an artifact or changed its input')
        report['status'] = 'passed'
        save()
        encoded = base64.b64encode(zlib.compress(
            json.dumps(report, separators=(',', ':'), sort_keys=True).encode(), 9)).decode()
        chunks = [encoded[i:i + 4000] for i in range(0, len(encoded), 4000)]
        if len(chunks) > CHUNK_LIMIT:
            raise ValueError('report too large for complete annotation recovery')
        for i, chunk in enumerate(chunks):
            print(f'::notice title=B12Z observations.json {i}/{len(chunks)}::{chunk}', flush=True)
        print(f'::notice title=B12Z observations.json count::{len(chunks)}', flush=True)
    except Exception:
        import traceback
        report['status'] = 'failed'
        report['failure'] = traceback.format_exc()[-3500:]
        save()
        raise


def _count(report):
    out = {}
    for row in report['observations']:
        for op in row['operations']:
            key = f'{row["kind"]}:{op["family"]}'
            out[key] = out.get(key, 0) + 1
    return out


if __name__ == '__main__':
    main()
