"""Pinned, read-only B9 RAW-marker preflight; not a census batch or support test.

This runs before the B9 census observer so a fixture the pinned build refuses
outright is visible as a preflight finding instead of being silently folded into
the census as 48 uniform refusals. Both the refusals and the acceptances are
recorded, and neither is a support claim: these files are authored header
markers with owned metadata payloads, not camera captures.
"""
import argparse
import base64
import hashlib
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

FLAGS = ('-pS', '-pa', '-pc', '-pC', '-et')


def run_probe(exe, source, work, fmt):
    original = source.read_bytes()
    commands, results = {'commands': []}, []
    for flag in FLAGS:
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
        results.append({'flag': flag, 'exitCode': rc, 'inputUnchanged': output == original,
                        'stdout': text[:1200], 'stdoutLength': len(stdout),
                        'printedTokens': sorted(t for t in catalog.TOKENS if t in text),
                        'stderr': stderr.decode('utf-8', 'replace')[:600], 'artifacts': artifacts})
    return {'fixture': source.name, 'format': fmt, 'fixtureSha256': baseline.digest(original),
            'container': {k: v for k, v in catalog.structure(original).items()
                          if k in ('dispatch', 'compression', 'rootOffset', 'directoryCount')},
            'operations': results, 'commands': commands['commands']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--upstream', type=Path, required=True)
    parser.add_argument('--build', type=Path, required=True)
    parser.add_argument('--exiftool-checkout', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=Path('artifacts/e01/b9-raw-preflight'))
    args = parser.parse_args()
    upstream, build, out = args.upstream.resolve(), args.build.resolve(), args.output.resolve()
    out.mkdir(parents=True, exist_ok=True)
    report = {'schema': 1, 'status': 'running', 'scope': 'B9 RAW-marker preflight only; no census rows',
              'sourceCommit': os.environ.get('E01_SOURCE_COMMIT'), 'baseline': {},
              'denominatorFrozen': False, 'verification': 'not-assessed',
              'observations': [], 'secondary': []}

    def save():
        (out / 'observations.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')

    try:
        if not re.fullmatch('[0-9a-f]{40}', report['sourceCommit'] or ''):
            raise ValueError('missing source commit')
        head = subprocess.run(['git', '-C', str(upstream), 'rev-parse', 'HEAD'],
                              capture_output=True, text=True, check=True).stdout.strip()
        dirty = subprocess.run(['git', '-C', str(upstream), 'status', '--porcelain', '--untracked-files=no'],
                               capture_output=True, text=True, check=True).stdout.strip()
        if head != baseline.PIN or dirty:
            raise ValueError('wrong or modified Exiv2 source')
        cache = (build / 'CMakeCache.txt').read_bytes()
        exe = build / 'bin/exiv2'
        env = {**os.environ, 'LC_ALL': 'C', 'LANG': 'C'}
        version = subprocess.run([str(exe), '--version'], capture_output=True, text=True,
                                 check=True, timeout=30, env=env).stdout
        verbose = subprocess.run([str(exe), '-v', '-V'], capture_output=True, text=True,
                                 check=True, timeout=30, env=env).stdout
        baseline.check_version(version)
        baseline.check_runtime_features(verbose)
        report['baseline'] = {'sourceCommit': head, 'version': baseline.VERSION,
                              'effectiveCmake': baseline.check_cache(cache.decode()),
                              'cmakeCacheSha256': baseline.digest(cache),
                              'licenseSha256': baseline.digest((upstream / 'COPYING').read_bytes()),
                              'executableSha256': baseline.digest(exe.read_bytes()),
                              'verboseVersion': verbose}
        report['fixtureManifest'] = catalog.verify()
        work = out / 'work'
        if work.exists():
            shutil.rmtree(work)
        for item in report['fixtureManifest']:
            src = catalog.DIRECTORY / item['file']
            probe = run_probe(exe, src, work / src.stem, item['format'])
            probe['expectedDispatch'] = item['dispatch']
            report['observations'].append(probe)
            report['secondary'].append(secondary.observe(args.exiftool_checkout, src))
        report['status'] = 'passed'
        save()
        encoded = base64.b64encode(zlib.compress(
            json.dumps(report, separators=(',', ':'), sort_keys=True).encode(), 9)).decode()
        chunks = [encoded[i:i + 4000] for i in range(0, len(encoded), 4000)]
        if len(chunks) > 10:
            raise ValueError('report too large for complete annotation recovery')
        for i, chunk in enumerate(chunks):
            print(f'::notice title=B9RAW observations.json {i}/{len(chunks)}::{chunk}', flush=True)
    except Exception:
        import traceback
        report['status'] = 'failed'
        report['failure'] = traceback.format_exc()[-3500:]
        save()
        raise


if __name__ == '__main__':
    main()
