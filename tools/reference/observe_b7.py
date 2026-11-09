"""B7 read-only pinned CLI probes and secondary ExifTool observations.

AVIF/HEIF/HEIC and container JXL use owned 8x8 synthetic images; HEIF and HEIC
are both HEVC with HEIC brand. Naked JXL is separately probed. CR3 remains
rights-blocked, not refused or excluded. Empty IPTC/thumbnail/ICC probes do
not establish support. No rendered-pixel equivalence or full conformance claimed.
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
import b7_owned_fixtures as owned
import bmff_structure as bmff
import exiv2_baseline as baseline
import observe_b0 as common

ROOT = Path(__file__).resolve().parents[2]
FILES = {'bmff-avif': 'owned.avif', 'bmff-heif-hevc': 'owned.heif',
         'bmff-heic-hevc': 'owned.heic', 'jxl-container': 'owned-container.jxl',
         'jxl-codestream': 'owned-codestream.jxl'}
BLOCK_REASON = ('No independently reviewed CR3 layout and owned or verified CC0/public-domain '
                'CR3 image; no pinned CLI attempt possible without a rights-safe fixture.')


def structure(data, layout):
    if layout == 'jxl-codestream':
        return {'signature': bmff.jxl_signature(data), 'codestreamSha256': baseline.digest(data)}
    boxes = bmff.boxes(data)
    return {'boxes': [{'type': b.kind.decode('latin-1'), 'offset': b.start, 'length': b.size,
                       'payloadSha256': baseline.digest(data[b.payload_start:b.end])} for b in boxes],
            'signature': bmff.jxl_signature(data) if layout == 'jxl-container' else 'not-applicable'}


def observe_row(report, row, exe, source, work):
    original = source.read_bytes()
    target = work / ('case' + source.suffix)
    target.write_bytes(original)
    family, operation = row['family'], row['operation']
    flags = (['-pS'] if family == 'inventory' else ['-et'] if operation == 'extract'
             else ['-pc'] if family == 'comments' else ['-pC'] if family == 'icc' else ['-pa'])
    rc, stdout, stderr = common.soft_run(report, [exe, *flags, target], cwd=work)
    after = target.read_bytes()
    extra = [p for p in work.iterdir() if p.is_file() and p != target]
    artifacts = [{'file': p.name, 'length': p.stat().st_size, 'sha256': baseline.digest(p.read_bytes())}
                 for p in sorted(extra)]
    facts = {'inputUnchanged': original == after, 'stdoutSha256': baseline.digest(stdout),
             'stdoutLength': len(stdout), 'stderr': stderr.decode('utf-8', 'replace')[:1000],
             'artifacts': artifacts, 'container': structure(original, row['layout']),
             'authoredTargetPresent': (family in ('exif', 'xmp') and row['layout'] != 'jxl-codestream')
                                      if family != 'inventory' else None,
             'printedArtist': b'B7Artist' in stdout, 'printedCreatorTool': b'B7Tool' in stdout}
    # Read-only changes are a finding, even if the CLI reported a refusal.
    outcome = ('scan-changed' if original != after else
               common.classify(rc, True, None, True, artifact_produced=bool(artifacts)))
    return {'id': row['rowId'], 'seedId': f"EX0289-{row['format']}-{family.upper()}",
            'operation': operation, 'family': family, 'layout': row['layout'],
            'duplicates': row['duplicates'], 'textEncoding': row['textEncoding'],
            'fixture': source.name, 'fixtureSha256': baseline.digest(original),
            'fixtureRights': 'owned-synthetic', 'inputSha256': baseline.digest(original),
            'outputSha256': baseline.digest(after), 'recordOnly': True, 'expectOk': None,
            'outcome': outcome, 'facts': facts}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--upstream', type=Path, required=True)
    p.add_argument('--build', type=Path, required=True)
    p.add_argument('--exiftool-checkout', type=Path, required=True)
    p.add_argument('--census', type=Path, default=ROOT / 'docs/research/EXIV2_CENSUS.json')
    p.add_argument('--output', type=Path, default=ROOT / 'artifacts/e01')
    args = p.parse_args()
    upstream, build = args.upstream.resolve(), args.build.resolve()
    out = (args.output / 'b7').resolve()
    out.mkdir(parents=True, exist_ok=True)
    report = {'schema': 1, 'status': 'running', 'batch': 'B7',
              'sourceCommit': os.environ.get('E01_SOURCE_COMMIT'),
              'baseline': {'version': baseline.VERSION, 'sourceCommit': baseline.PIN},
              'commands': [], 'observations': [], 'blocked': [], 'secondaryObservations': {},
              'outcomeCounts': {}, 'denominatorFrozen': False}
    def save():
        (out / 'observations.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    try:
        if not re.fullmatch('[0-9a-f]{40}', report['sourceCommit'] or ''):
            raise ValueError('missing E01_SOURCE_COMMIT')
        head = subprocess.run(['git', '-C', str(upstream), 'rev-parse', 'HEAD'], check=True, capture_output=True, text=True).stdout.strip()
        dirty = subprocess.run(['git', '-C', str(upstream), 'status', '--porcelain', '--untracked-files=no'], check=True, capture_output=True, text=True).stdout.strip()
        if head != baseline.PIN or dirty:
            raise ValueError('wrong or modified Exiv2 source')
        cache = (build / 'CMakeCache.txt').read_bytes()
        report['baseline']['effectiveCmake'] = baseline.check_cache(cache.decode())
        report['baseline']['cmakeCacheSha256'] = baseline.digest(cache)
        report['baseline']['licenseSha256'] = baseline.digest((upstream / 'COPYING').read_bytes())
        exe = build / 'bin/exiv2'
        report['baseline']['executableSha256'] = baseline.digest(exe.read_bytes())
        env = {**os.environ, 'LC_ALL': 'C'}
        version = subprocess.run([str(exe), '--version'], check=True, capture_output=True, text=True, env=env, timeout=30).stdout
        baseline.check_version(version)
        verbose = subprocess.run([str(exe), '-v', '-V'], check=True, capture_output=True, text=True, env=env, timeout=30).stdout
        baseline.check_runtime_features(verbose)
        report['baseline']['verboseVersion'] = verbose
        report['fixtureManifest'] = owned.verify()
        for name in sorted(FILES.values()):
            report['secondaryObservations'][name] = secondary.observe(args.exiftool_checkout, owned.FIXTURES / name)
        rows = [r for r in json.loads(args.census.read_text())['rows'] if r['batch'] == 'B7']
        if len(rows) != 48:
            raise ValueError('unexpected B7 census row count')
        for i, row in enumerate(rows):
            if row['format'] == 'CR3':
                report['blocked'].append({'id': row['rowId'], 'reason': BLOCK_REASON})
                continue
            work = out / 'work' / f'{i:04d}'
            if work.exists():
                shutil.rmtree(work)
            work.mkdir(parents=True)
            report['observations'].append(observe_row(report, row, exe, owned.FIXTURES / FILES[row['layout']], work))
            save()
        counts = Counter(o['outcome'] for o in report['observations'])
        counts['blocked-rights'] = len(report['blocked'])
        report['outcomeCounts'] = dict(sorted(counts.items()))
        report['status'] = 'passed'
        save()
        encoded = base64.b64encode(zlib.compress(json.dumps(report, separators=(',', ':')).encode())).decode()
        chunks = [encoded[i:i + 4000] for i in range(0, len(encoded), 4000)]
        if len(chunks) > 10:
            raise ValueError('B7 report exceeds ten annotation limit')
        for i, chunk in enumerate(chunks):
            print(f'::notice title=B7Z observations.json {i}/{len(chunks)}::{chunk}', flush=True)
    except Exception:
        import traceback
        report['status'] = 'failed'
        report['failure'] = traceback.format_exc()[-3500:]
        save()
        raise


if __name__ == '__main__':
    main()
