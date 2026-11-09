"""Pinned, read-only B8 EPS preflight; not a census batch or support test."""
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
import b8_eps_fixture as fixture
import exiv2_baseline as baseline
import observe_b0 as common


def run_probe(exe, source, work):
    original = source.read_bytes()
    commands, results = {'commands': []}, []
    for flag in ('-pS', '-pa', '-pc', '-pC', '-et'):
        subdir = work / flag[1:]
        subdir.mkdir(parents=True, exist_ok=True)
        target = subdir / ('case' + source.suffix)
        target.write_bytes(original)
        rc, stdout, stderr = common.soft_run(commands, [exe, flag, target], cwd=subdir)
        output = target.read_bytes()
        artifacts = [{'file': p.name, 'length': p.stat().st_size,
                      'sha256': hashlib.sha256(p.read_bytes()).hexdigest()}
                     for p in sorted(subdir.iterdir()) if p.is_file() and p != target]
        results.append({'flag': flag, 'exitCode': rc, 'inputUnchanged': output == original,
                        'stdout': stdout.decode('utf-8', 'replace')[:4000],
                        'stderr': stderr.decode('utf-8', 'replace')[:1000], 'artifacts': artifacts})
    return {'fixture': source.name, 'fixtureSha256': baseline.digest(original),
            'operations': results, 'commands': commands['commands']}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--upstream', type=Path, required=True)
    p.add_argument('--build', type=Path, required=True)
    p.add_argument('--exiftool-checkout', type=Path, required=True)
    p.add_argument('--output', type=Path, default=Path('artifacts/e01/b8-eps-preflight'))
    a = p.parse_args()
    upstream, build, out = a.upstream.resolve(), a.build.resolve(), a.output.resolve()
    out.mkdir(parents=True, exist_ok=True)
    report = {'schema': 1, 'status': 'running', 'scope': 'B8 EPS preflight only; no census rows',
              'sourceCommit': os.environ.get('E01_SOURCE_COMMIT'), 'baseline': {},
              'denominatorFrozen': False, 'verification': 'not-assessed', 'observations': [], 'secondary': []}
    def save():
        (out / 'observations.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    try:
        if not re.fullmatch('[0-9a-f]{40}', report['sourceCommit'] or ''):
            raise ValueError('missing source commit')
        head = subprocess.run(['git', '-C', str(upstream), 'rev-parse', 'HEAD'], capture_output=True, text=True, check=True).stdout.strip()
        dirty = subprocess.run(['git', '-C', str(upstream), 'status', '--porcelain', '--untracked-files=no'], capture_output=True, text=True, check=True).stdout.strip()
        if head != baseline.PIN or dirty:
            raise ValueError('wrong or modified Exiv2 source')
        cache = (build / 'CMakeCache.txt').read_bytes()
        exe = build / 'bin/exiv2'
        version = subprocess.run([str(exe), '--version'], capture_output=True, text=True, check=True, timeout=30).stdout
        verbose = subprocess.run([str(exe), '-v', '-V'], capture_output=True, text=True, check=True, timeout=30).stdout
        baseline.check_version(version)
        baseline.check_runtime_features(verbose)
        report['baseline'] = {'sourceCommit': head, 'version': baseline.VERSION,
                              'effectiveCmake': baseline.check_cache(cache.decode()),
                              'cmakeCacheSha256': baseline.digest(cache),
                              'licenseSha256': baseline.digest((upstream / 'COPYING').read_bytes()),
                              'executableSha256': baseline.digest(exe.read_bytes()), 'verboseVersion': verbose}
        report['fixtureManifest'] = [fixture.verify()]
        work = out / 'work'
        if work.exists():
            shutil.rmtree(work)
        for item in report['fixtureManifest']:
            src = fixture.DIRECTORY / item['file']
            report['observations'].append(run_probe(exe, src, work / src.stem))
            report['secondary'].append(secondary.observe(a.exiftool_checkout, src))
        report['status'] = 'passed'
        save()
        encoded = base64.b64encode(zlib.compress(json.dumps(report, separators=(',', ':')).encode())).decode()
        chunks = [encoded[i:i+4000] for i in range(0, len(encoded), 4000)]
        if len(chunks) > 10:
            raise ValueError('report too large for complete annotation recovery')
        for i, chunk in enumerate(chunks):
            print(f'::notice title=B8EPS observations.json {i}/{len(chunks)}::{chunk}', flush=True)
    except Exception:
        import traceback
        report['status'] = 'failed'
        report['failure'] = traceback.format_exc()[-3500:]
        save()
        raise


if __name__ == '__main__':
    main()
