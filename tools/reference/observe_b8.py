"""B8 read-only pinned CLI probes for the cleared owned PSD/JP2/EPS fixtures.

One census row is exactly one pinned-CLI command per owned fixture. Outcomes are
recorded, never gated: a refusal is a finding, not a support claim, and an empty
metadata probe proves only that this CLI build printed nothing for this fixture.
PGF, EXV and DCP have no owned fixture and no completed rights/oracle review, so
their rows are recorded as blocked rather than refused, excluded or not-applicable.

No PostScript is executed (the EPS probe only hands the file to the read-only
reference CLI), no pixels are decoded and nothing is written back to a fixture.
This is bounded census evidence, not E08 product verification.
"""
import argparse
import base64
from collections import Counter
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import zlib

import b7_exiftool_probe as secondary
import b8_census_fixtures as catalog
import exiv2_baseline as baseline
import observe_b0 as common

ROOT = Path(__file__).resolve().parents[2]

# Format -> why no row can be attempted yet. Kept next to the probe so the blocked
# reason travels with the evidence, and reviewed in
# docs/research/E01_B8_RIGHTS_AND_ORACLE_REVIEW.md.
BLOCK_REASONS = {
    'PGF': 'No independent normative specification or encoder rights review is complete, so no '
           'PGF fixture is cleared; a magic-byte stub would not be a valid image.',
    'EXV': 'No independent layout review and no rights-safe sample; EXV is an Exiv2-associated '
           'metadata file, not a reviewed raster container.',
    'DCP': 'DCP is a camera profile, not an ordinary TIFF image; no standalone profile layout, '
           'required-tag set or independent oracle has been reviewed and no fixture cleared.',
}
FLAGS = {'inventory': ['-pS'], 'comments': ['-pc'], 'icc': ['-pC']}


def flags_for(row):
    """One read-only flag per row; extraction is asked for explicitly."""
    if row['family'] == 'inventory':
        return FLAGS['inventory']
    if row['operation'] == 'extract':
        return ['-et']
    return FLAGS.get(row['family'], ['-pa'])


def fixture_for(row):
    return catalog.DIRECTORY / catalog.FIXTURES[row['layout']][1]


def structure_facts(fmt, data):
    """Container framing only, with per-box digests dropped to stay within the budget.

    The full structure stays available in b8_census_fixtures; the census report
    records the invariants that matter for interpreting the probe (section and box
    accounting plus the authored pixel/payload digests).
    """
    facts = catalog.structure(fmt, data)
    if 'boxes' in facts:
        facts = dict(facts, boxCount=len(facts['boxes']), boxTypes=[b['type'] for b in facts['boxes']])
        facts.pop('boxes', None)
    if 'resources' in facts:
        facts = dict(facts, resources=[{'identifier': r['identifier'], 'payloadLength': r['payloadLength']}
                                       for r in facts['resources']])
    facts.pop('dscComments', None)
    facts.pop('creator', None)
    return facts


def observe_row(row, exe, work):
    if catalog.FIXTURES[row['layout']][0] != row['format']:
        raise ValueError(f"census layout {row['layout']} and format {row['format']} disagree")
    source = fixture_for(row)
    original = source.read_bytes()
    target = work / ('case' + source.suffix)
    target.write_bytes(original)
    flags = flags_for(row)
    commands = {'commands': []}  # throwaway sink: soft_run's stdout excerpt stays out of the report
    rc, stdout, stderr = common.soft_run(commands, [exe, *flags, target], cwd=work)
    after = target.read_bytes()
    artifacts = [{'file': p.name, 'length': p.stat().st_size, 'sha256': baseline.digest(p.read_bytes())}
                 for p in sorted(work.iterdir()) if p.is_file() and p != target]
    text = stdout.decode('utf-8', 'replace')
    facts = {'inputUnchanged': original == after, 'stdoutSha256': baseline.digest(stdout),
             'stdoutLength': len(stdout),
             'stderr': re.sub(r'\S*/case\.[A-Za-z0-9]+', '<fixture>',
                              stderr.decode('utf-8', 'replace'))[:300],
             'artifacts': artifacts, 'container': structure_facts(row['format'], original),
             'printedTokens': sorted(token for token in catalog.AUTHORED_TOKENS if token in text)}
    # A read-only probe that mutated the input is a finding even if the CLI claimed success.
    outcome = ('scan-changed' if original != after else
               common.classify(rc, True, None, True, artifact_produced=bool(artifacts)))
    return {'id': row['rowId'], 'seedId': f"EX0289-{row['format']}-{row['family'].upper()}",
            'operation': row['operation'], 'family': row['family'], 'layout': row['layout'],
            'flag': flags[0], 'exitCode': rc,
            'duplicates': row['duplicates'], 'textEncoding': row['textEncoding'],
            'fixture': source.name, 'fixtureSha256': baseline.digest(original),
            'fixtureRights': 'owned-synthetic', 'inputSha256': baseline.digest(original),
            'outputSha256': baseline.digest(after), 'recordOnly': True, 'expectOk': None,
            'outcome': outcome, 'facts': facts}


def verify_source(upstream, build, report):
    head = subprocess.run(['git', '-C', str(upstream), 'rev-parse', 'HEAD'],
                          check=True, capture_output=True, text=True).stdout.strip()
    dirty = subprocess.run(['git', '-C', str(upstream), 'status', '--porcelain', '--untracked-files=no'],
                           check=True, capture_output=True, text=True).stdout.strip()
    if head != baseline.PIN or dirty:
        raise ValueError('wrong or modified Exiv2 source')
    cache = (build / 'CMakeCache.txt').read_bytes()
    exe = build / 'bin/exiv2'
    env = {**os.environ, 'LC_ALL': 'C', 'LANG': 'C'}
    version = subprocess.run([str(exe), '--version'], check=True, capture_output=True, text=True,
                             env=env, timeout=30).stdout
    verbose = subprocess.run([str(exe), '-v', '-V'], check=True, capture_output=True, text=True,
                             env=env, timeout=30).stdout
    baseline.check_version(version)
    baseline.check_runtime_features(verbose)
    report['baseline'] = {'sourceCommit': head, 'version': baseline.VERSION,
                          'effectiveCmake': baseline.check_cache(cache.decode()),
                          'cmakeCacheSha256': baseline.digest(cache),
                          'licenseSha256': baseline.digest((upstream / 'COPYING').read_bytes()),
                          'executableSha256': baseline.digest(exe.read_bytes()), 'verboseVersion': verbose}
    return exe


def chunks(report, size=4000, limit=10):
    """Compress the report into check-run annotations, the only retrievable channel.

    Bulk Actions logs are not reachable from this environment, so a report that
    would need more annotations than the limit is a hard error instead of silent
    truncation: the evidence must be recoverable whole or the batch is not recorded.
    """
    payload = json.dumps(report, separators=(',', ':'), sort_keys=True)
    encoded = base64.b64encode(zlib.compress(payload.encode(), 9)).decode()
    parts = [encoded[i:i + size] for i in range(0, len(encoded), size)]
    if len(parts) > limit:
        raise ValueError(f'B8 report needs {len(parts)} annotations; the recovery limit is {limit}')
    return parts


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--upstream', type=Path, required=True)
    parser.add_argument('--build', type=Path, required=True)
    parser.add_argument('--exiftool-checkout', type=Path, required=True)
    parser.add_argument('--census', type=Path, default=ROOT / 'docs/research/EXIV2_CENSUS.json')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/e01')
    args = parser.parse_args()
    upstream, build = args.upstream.resolve(), args.build.resolve()
    out = (args.output / 'b8').resolve()
    out.mkdir(parents=True, exist_ok=True)
    report = {'schema': 1, 'status': 'running', 'batch': 'B8',
              'scope': 'read-only pinned CLI probes of owned PSD/JP2/EPS fixtures plus blocked '
                       'PGF/EXV/DCP cells; no product verification, no support claim',
              'limitations': 'Metadata-free JP2 and EPS fixtures cannot show metadata-present reading; '
                             'no B8 write suboperation is observed because no owned writable fixture is '
                             'cleared; PSD ICC and thumbnail resources, PSB, layers and codec conformance '
                             'are not established; ExifTool 12.76 is a secondary reference, not an oracle.',
              'sourceCommit': os.environ.get('E01_SOURCE_COMMIT'),
              'baseline': {'version': baseline.VERSION, 'sourceCommit': baseline.PIN},
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
        rows = [r for r in json.loads(args.census.read_text())['rows'] if r['batch'] == 'B8']
        if len(rows) != 53:
            raise ValueError(f'unexpected B8 census row count: {len(rows)}')
        for i, row in enumerate(rows):
            if row['layout'] not in catalog.FIXTURES:
                report['blocked'].append({'id': row['rowId'], 'reason': BLOCK_REASONS[row['format']]})
                save()
                continue
            work = out / 'work' / f'{i:04d}'
            if work.exists():
                shutil.rmtree(work)
            work.mkdir(parents=True)
            report['observations'].append(observe_row(row, exe, work))
            save()
        if len({o['id'] for o in report['observations']}) != len(report['observations']):
            raise ValueError('duplicate B8 observation id')
        if {o['id'] for o in report['observations']} & {b['id'] for b in report['blocked']}:
            raise ValueError('B8 row both observed and blocked')
        if {b['id'] for b in report['blocked']} != {r['rowId'] for r in rows
                                                    if r['layout'] not in catalog.FIXTURES}:
            raise ValueError('B8 blocked set does not match the unobserved layouts')
        used = sorted({o['fixture'] for o in report['observations']})
        if set(used) != {entry['file'] for entry in report['fixtureManifest']}:
            raise ValueError('a cleared B8 fixture was never probed')
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
            print(f'::notice title=B8Z observations.json {i}/{len(parts)}::{part}', flush=True)
    except Exception:
        import traceback
        report['status'] = 'failed'
        report['failure'] = traceback.format_exc()[-3500:]
        save()
        raise


if __name__ == '__main__':
    main()
