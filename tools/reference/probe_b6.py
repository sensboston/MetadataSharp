"""B6 exploratory D1-fixture probes; NOT the census observation batch.

Runs the pinned CLI against disposable copies of the four owned D1 fixtures.
Results are raw command facts, not census rows or MetadataSharp verification.
Usage: E01_SOURCE_COMMIT=<repo HEAD> python tools/reference/probe_b6.py \
    --upstream artifacts/e01/source --build artifacts/e01/build --output artifacts/e01/b6-probe.json
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile

import dng_fixtures
import dng_raw_oracle
import exiv2_baseline as baseline

ROOT = Path(__file__).resolve().parents[2]
# These are independent probes of fresh inputs, not a chained mutation sequence.
PROBES = (
    ('inventory', ['-pS']),
    ('metadata', ['-pa']),
    ('replace-artist', ['-M', 'set Exif.Image.Artist B6Artist']),
    ('remove-artist', ['-M', 'del Exif.Image.Artist']),
    ('insert-description', ['-M', 'set Exif.Image.ImageDescription B6Description']),
    ('insert-xmp', ['-M', 'set Xmp.xmp.CreatorTool B6Tool']),
    ('insert-iptc', ['-M', 'set Iptc.Application2.Keywords B6Keyword']),
)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def run(upstream, build, output):
    source_commit = os.environ.get('E01_SOURCE_COMMIT', '')
    if not re.fullmatch('[0-9a-f]{40}', source_commit):
        raise ValueError('E01_SOURCE_COMMIT must identify the repository commit')
    head = subprocess.run(['git', '-C', str(upstream), 'rev-parse', 'HEAD'],
                          check=True, capture_output=True, text=True).stdout.strip()
    if head != baseline.PIN:
        raise ValueError('wrong upstream commit')
    dirty = subprocess.run(['git', '-C', str(upstream), 'status', '--porcelain', '--untracked-files=no'],
                           check=True, capture_output=True, text=True).stdout.strip()
    if dirty:
        raise ValueError('modified upstream source')
    cache = (build / 'CMakeCache.txt').read_bytes()
    effective = baseline.check_cache(cache.decode())
    exe = (build / 'bin/exiv2').resolve()
    env = {**os.environ, 'LC_ALL': 'C'}
    version = subprocess.run([str(exe), '--version'], check=True, capture_output=True,
                             text=True, env=env, timeout=30).stdout
    baseline.check_version(version)
    verbose = subprocess.run([str(exe), '-v', '-V'], check=True, capture_output=True,
                             text=True, env=env, timeout=30).stdout
    baseline.check_runtime_features(verbose)
    manifest = json.loads((ROOT / 'tests/fixtures/dng/manifest.json').read_text())
    report = {'kind': 'exploratory-not-census', 'sourceCommit': source_commit,
              'baseline': {'sourceCommit': head, 'version': version.strip(),
                           'executableSha256': digest(exe.read_bytes()),
                           'cacheSha256': digest(cache), 'effectiveCmake': effective,
                           'verboseVersion': verbose}, 'probes': []}
    with tempfile.TemporaryDirectory(prefix='b6-probe-') as tmp:
        path = Path(tmp) / 'fixture.dng'
        for entry in manifest:
            original, _ = dng_fixtures.build(entry['byteOrder'], entry['shape'])
            if digest(original) != entry['sha256'] or len(original) != entry['length']:
                raise ValueError('D1 fixture mismatch: ' + entry['file'])
            before = dng_raw_oracle.raw_strip_fingerprints(original)
            if len(before) != 1 or before[0]['sha256'] != entry['rawStripSha256']:
                raise ValueError('D1 raw strip mismatch: ' + entry['file'])
            for name, args in PROBES:
                path.write_bytes(original)
                result = subprocess.run([str(exe), *args, str(path)], capture_output=True,
                                        env=env, timeout=30)
                after = path.read_bytes()
                readback = subprocess.run([str(exe), '-pa', str(path)], capture_output=True,
                                          env=env, timeout=30)
                try:
                    raw_after = dng_raw_oracle.raw_strip_fingerprints(after)
                    raw_error = None
                except dng_raw_oracle.DngOracleError as error:
                    raw_after, raw_error = None, str(error)
                report['probes'].append({'fixture': entry['file'], 'fixtureSha256': entry['sha256'],
                                         'probe': name, 'args': args, 'exitCode': result.returncode,
                                         'stdout': result.stdout.decode('utf-8', errors='replace'),
                                         'stderr': result.stderr.decode('utf-8', errors='replace'),
                                         'readbackExitCode': readback.returncode,
                                         'readbackStdout': readback.stdout.decode('utf-8', errors='replace'),
                                         'readbackStderr': readback.stderr.decode('utf-8', errors='replace'),
                                         'outputSha256': digest(after), 'rawBefore': before,
                                         'rawAfter': raw_after, 'rawOracleError': raw_error,
                                         'rawPreserved': None if raw_after is None else raw_after == before})
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--upstream', type=Path, required=True)
    parser.add_argument('--build', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    report = run(args.upstream, args.build, args.output)
    print(f"Exploratory probes: {len(report['probes'])}; report: {args.output}")


if __name__ == '__main__':
    main()
