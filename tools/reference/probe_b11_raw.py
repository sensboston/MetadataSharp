"""Pinned, read-only B11 container preflight; not a census batch or support test.

This runs before the B11 census observer so that a fixture the pinned build refuses outright is
visible as a preflight finding instead of being folded into the census as a wall of uniform
refusals. Both refusals and acceptances are recorded, and neither is a support claim: these
files are authored containers with owned CIFF components, not camera captures.

The preflight is also where the two questions this batch must not conflate are asked apart:
whether the *header* is accepted (recognition, which for TGA in this build is the file name)
and whether a *metadata component* is reached through ``crwMapping_[]``. So the seed-grid
formats are asked seven flags each and the container stubs five, in separate working
directories, with the `-et` byte-count semantics recorded per row:

* `-pS`/`-pC` reach the base `Image::printStructure`, which throws for every B11 format except
  the BMFF fall-through -- the unknown-brand movie is the only stub expected to print.
* `-pe`/`-px`/`-pc`/`-pa` are listings, so an empty listing with exit 0 is a recorded text,
  never an assertion that a family is unsupported.
* `-et` exits with the number of bytes it wrote: 0 with no artifact is *consistent* for a
  reader with no thumbnail, and that is what the row says.
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

import b11_container_fixtures as catalog
import b7_exiftool_probe as secondary
import exiv2_baseline as baseline
import observe_b0 as common
from observe_b11 import SEED_FORMATS, container_facts, environment, predicted_artifact_bytes
from observe_b9 import verify_source

SEED_FLAGS = ('-pS', '-pe', '-pc', '-pC', '-px', '-pa', '-et')
VIDEO_FLAGS = ('-pS', '-pa', '-pC', '-px', '-et')
CHUNK_LIMIT = 12   # with the census recorder's 30 this stays inside GitHub's 50 per check run


def flags_for_format(fmt):
    """Seven flags for the formats the pinned table has rows for, five for the stubs.

    The stubs skip `-pc` and `-pe`: comment `amNone` and a reader that writes no Exif tag are
    both already answered by `-pa`, and the budget buys the brand-dispatch pair instead.
    """
    return list(SEED_FLAGS if fmt in SEED_FORMATS else VIDEO_FLAGS)


def run_probe(exe, source, work, layout):
    original = source.read_bytes()
    fmt = catalog.FIXTURES[layout][0]
    commands, results = {'commands': []}, []
    for flag in flags_for_format(fmt):
        subdir = work / flag[1:]
        subdir.mkdir(parents=True, exist_ok=True)
        target = subdir / ('case' + source.suffix)
        target.write_bytes(original)
        rc, stdout, stderr = common.soft_run(commands, [exe, flag, target], cwd=subdir)
        output = target.read_bytes()
        artifacts = [{'file': p.name, 'length': p.stat().st_size,
                      'sha256': hashlib.sha256(p.read_bytes()).hexdigest()}
                     for p in sorted(subdir.iterdir()) if p.is_file() and p != target]
        text = stdout.decode('utf-8', 'replace')
        row = {'flag': flag, 'exitCode': rc, 'inputUnchanged': output == original,
               'stdout': text[:1200], 'stdoutLength': len(stdout),
               'printedTokens': sorted(t for t in catalog.TOKENS if t in text),
               'stderr': stderr.decode('utf-8', 'replace')[:600], 'artifacts': artifacts}
        if flag == '-et':
            # In this build the status is the byte count the CLI wrote, so the preflight
            # records the comparison instead of inferring success from a nonzero exit.
            written = sum(item['length'] for item in artifacts)
            expected = predicted_artifact_bytes(layout, 'extract')
            row['artifactBytes'] = written
            row['exitCodeEqualsArtifactBytes'] = rc == written
            if expected is not None:
                row['predictedArtifactBytes'] = expected
                row['predictionMatched'] = expected == written
        results.append(row)
    return {'fixture': source.name, 'format': fmt, 'layout': layout,
            'fixtureSha256': baseline.digest(original),
            'container': container_facts(original, source.name, layout),
            'flags': len(flags_for_format(fmt)), 'operations': results,
            'commands': commands['commands']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--upstream', type=Path, required=True)
    parser.add_argument('--build', type=Path, required=True)
    parser.add_argument('--exiftool-checkout', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=Path('artifacts/e01/b11-container-preflight'))
    args = parser.parse_args()
    upstream, build, out = args.upstream.resolve(), args.build.resolve(), args.output.resolve()
    out.mkdir(parents=True, exist_ok=True)
    report = {'schema': 1, 'status': 'running',
              'scope': 'B11 container preflight only; no census rows',
              'sourceCommit': os.environ.get('E01_SOURCE_COMMIT'), 'baseline': {},
              'environment': environment(),
              'denominatorFrozen': False, 'verification': 'not-assessed',
              'observations': [], 'secondary': []}

    def save():
        (out / 'observations.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')

    try:
        if not re.fullmatch('[0-9a-f]{40}', report['sourceCommit'] or ''):
            raise ValueError('missing source commit')
        exe = verify_source(upstream, build, report)
        report['fixtureManifest'] = catalog.verify()
        work = out / 'work'
        if work.exists():
            shutil.rmtree(work)
        for item in report['fixtureManifest']:
            if item.get('blocked'):
                continue
            src = catalog.DIRECTORY / item['file']
            probe = run_probe(exe, src, work / src.stem, item['layout'])
            probe['expectedDispatch'] = item['dispatch']
            report['observations'].append(probe)
            report['secondary'].append(secondary.observe(args.exiftool_checkout, src))
        expected = sum(len(flags_for_format(fmt)) for fmt, _ in catalog.FIXTURES.values())
        recorded = sum(len(probe['operations']) for probe in report['observations'])
        if recorded != expected or len(report['observations']) != len(catalog.FIXTURES):
            raise ValueError(f'B11 preflight recorded {recorded} rows, expected {expected}')
        report['recordCount'] = recorded
        report['status'] = 'passed'
        save()
        encoded = base64.b64encode(zlib.compress(
            json.dumps(report, separators=(',', ':'), sort_keys=True).encode(), 9)).decode()
        chunks = [encoded[i:i + 4000] for i in range(0, len(encoded), 4000)]
        if len(chunks) > CHUNK_LIMIT:
            raise ValueError('report too large for complete annotation recovery')
        for i, chunk in enumerate(chunks):
            print(f'::notice title=B11CONT observations.json {i}/{len(chunks)}::{chunk}', flush=True)
        # A separate count marker: the chunk titles carry the expected count, so recovery
        # can prove the payload arrived whole instead of assuming the last chunk was sent.
        print(f'::notice title=B11CONT observations.json count::{len(chunks)}', flush=True)
    except Exception:
        import traceback
        report['status'] = 'failed'
        report['failure'] = traceback.format_exc()[-3500:]
        save()
        raise


if __name__ == '__main__':
    main()
