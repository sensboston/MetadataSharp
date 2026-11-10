"""B9 read-only pinned CLI probes for the cleared owned TIFF-derived RAW fixtures.

One census row is exactly one pinned-CLI command per owned fixture, so a
recognition row and a metadata row never share a command. Outcomes are recorded,
never gated: a refusal is a finding, not a support claim, and an empty print only
proves that this CLI build printed nothing for this fixture. Each vendor-private
layout stays blocked instead of refused because no fixture is cleared for it
(docs/research/E01_B9_RIGHTS_AND_ORACLE_REVIEW.md).

These fixtures are authored markers, not camera captures: the file carries the
header contract the pinned build dispatches on and the shared owned metadata
payloads, so a refusal here says nothing about the vendor layout it stands in for
and a success says nothing about real RAW decoding. No pixels are decoded,
nothing is written back to a fixture, and no MakerNote is interpreted. This is
bounded census evidence, not E10 product verification.
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
import b9_raw_fixtures as catalog
import exiv2_baseline as baseline
import observe_b0 as common

ROOT = Path(__file__).resolve().parents[2]
FLAGS = {'inventory': ['-pS'], 'comments': ['-pc'], 'icc': ['-pC']}
# Only these facts travel to the report; the full structure stays in the catalog.
# Per-format reasons a vendor-private cell cannot be attempted, kept next to the probe
# so the blocked reason travels with the evidence, reviewed in
# docs/research/E01_B9_RIGHTS_AND_ORACLE_REVIEW.md.
BLOCK_REASONS = {fmt: catalog.vendor_reason(fmt)
                 for fmt in ('ARW', 'CR2', 'NEF', 'ORF', 'PEF', 'SRW')}
FACT_KEYS = ('dispatch', 'rootOffset', 'headerBytes', 'directoryCount', 'compression', 'make',
             'model', 'carrierTags', 'thumbnailPresent', 'dataPoolSpans', 'trailingPadBytes',
             'fileLength', 'fileSha256')


def flags_for(row):
    """One read-only flag per row; extraction is asked for explicitly."""
    if row['family'] == 'inventory':
        return FLAGS['inventory']
    if row['operation'] == 'extract':
        return ['-et']
    return FLAGS.get(row['family'], ['-pa'])


def fixture_for(row):
    return catalog.DIRECTORY / catalog.FIXTURES[row['layout']][1]


def structure_facts(data):
    facts = catalog.structure(data)
    return {key: facts[key] for key in FACT_KEYS if key in facts}


def observe_row(row, exe, work):
    if catalog.FIXTURES[row['layout']][0] != row['format']:
        raise ValueError(f"census layout {row['layout']} and format {row['format']} disagree")
    source = fixture_for(row)
    original = source.read_bytes()
    # Keep the owned extension: the pinned build dispatches on header bytes, and the
    # recorded exit codes are only interpretable for a file named as its format.
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
             'artifacts': artifacts, 'container': structure_facts(original),
             'printedTokens': sorted(token for token in catalog.TOKENS if token in text)}
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
        raise ValueError(f'B9 report needs {len(parts)} annotations; the recovery limit is {limit}')
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
    out = (args.output / 'b9').resolve()
    out.mkdir(parents=True, exist_ok=True)
    report = {'schema': 1, 'status': 'running', 'batch': 'B9',
              'scope': 'read-only pinned CLI probes of owned synthetic TIFF-derived RAW fixtures '
                       '(ARW/CR2/NEF/ORF/PEF/SRW header or compression markers plus owned metadata '
                       'carriers) and blocked vendor-private cells; no product verification, no '
                       'support claim, no vendor layout conformance claim',
              'limitations': 'fixtures are authored markers, not camera captures: they carry no '
                             'vendor private IFD, no MakerNote, no decodable raw data and no real '
                             'preview, so a refusal is not evidence that a vendor layout is unsupported '
                             'and a success is not evidence of RAW support; the pinned build dispatches '
                             'on header bytes, so the four compression-marker formats are read by the '
                             'generic TIFF reader and only their MIME mapping distinguishes them; no B9 '
                             'write suboperation is enumerated because no owned writable fixture is '
                             'cleared and the reference registry marks arw/rw2/sr2/raf read-only; '
                             'ExifTool 12.76 is a secondary reference, not an oracle.',
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
        rows = [r for r in json.loads(args.census.read_text())['rows'] if r['batch'] == 'B9']
        if len(rows) != 96:
            raise ValueError(f'unexpected B9 census row count: {len(rows)}')
        for i, row in enumerate(rows):
            if row['layout'] not in catalog.FIXTURES:
                report['blocked'].append({'id': row['rowId'],
                                          'reason': BLOCK_REASONS[row['format']]})
                save()
                continue
            work = out / 'work' / f'{i:04d}'
            if work.exists():
                shutil.rmtree(work)
            work.mkdir(parents=True)
            report['observations'].append(observe_row(row, exe, work))
            save()
        if len({o['id'] for o in report['observations']}) != len(report['observations']):
            raise ValueError('duplicate B9 observation id')
        if {o['id'] for o in report['observations']} & {b['id'] for b in report['blocked']}:
            raise ValueError('B9 row both observed and blocked')
        if {b['id'] for b in report['blocked']} != {r['rowId'] for r in rows
                                                    if r['layout'] not in catalog.FIXTURES}:
            raise ValueError('B9 blocked set does not match the unobserved layouts')
        used = sorted({o['fixture'] for o in report['observations']})
        if set(used) != {entry['file'] for entry in report['fixtureManifest']}:
            raise ValueError('a cleared B9 fixture was never probed')
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
            print(f'::notice title=B9Z observations.json {i}/{len(parts)}::{part}', flush=True)
    except Exception:
        import traceback
        report['status'] = 'failed'
        report['failure'] = traceback.format_exc()[-3500:]
        save()
        raise


if __name__ == '__main__':
    main()
