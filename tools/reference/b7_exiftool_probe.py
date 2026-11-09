"""Version-pinned, read-only ExifTool secondary observation for B7 research.

ExifTool is an external executable, never a production dependency or copied
source. Results are recorded, not interpreted as proof of format conformance.
The caller is responsible for fixture rights and a separate independent oracle.
"""
import argparse
import hashlib
import json
import os
import subprocess
import tempfile
from pathlib import Path

EXIFTOOL_COMMIT = '212f02cf22b9484674752181cad3b73089ce9694'
EXIFTOOL_VERSION = '12.76'
MAX_FIXTURE_BYTES = 16 * 1024 * 1024
MAX_OUTPUT_BYTES = 1024 * 1024


class SecondaryProbeError(ValueError):
    pass


def _run(command):
    # No shell, no network; enforce a finite execution time. The selected
    # ExifTool options print to stdout and do not perform any writes.
    env = {**os.environ, 'LC_ALL': 'C', 'LANG': 'C'}
    for key in ('PERL5LIB', 'PERLLIB', 'PERL5OPT'):
        env.pop(key, None)
    # Spool rather than buffering an untrusted tool's entire output in memory.
    # The timeout bounds runtime; the output cap below bounds retained bytes.
    with tempfile.TemporaryFile() as out, tempfile.TemporaryFile() as err:
        result = subprocess.run(command, stdout=out, stderr=err, timeout=60, check=False, env=env)
        out.seek(0)
        err.seek(0)
        stdout, stderr = out.read(MAX_OUTPUT_BYTES + 1), err.read(MAX_OUTPUT_BYTES + 1)
    if len(stdout) > MAX_OUTPUT_BYTES or len(stderr) > MAX_OUTPUT_BYTES:
        raise SecondaryProbeError('tool output exceeds limit')
    return subprocess.CompletedProcess(command, result.returncode, stdout, stderr)


def verify_checkout(upstream, perl='perl'):
    upstream = Path(upstream).resolve()
    script = upstream / 'exiftool'
    if not script.is_file():
        raise SecondaryProbeError('ExifTool executable missing')
    head = _run(['git', '-C', str(upstream), 'rev-parse', 'HEAD'])
    dirty = _run(['git', '-C', str(upstream), 'status', '--porcelain', '--untracked-files=all'])
    if head.returncode or head.stdout.strip() != EXIFTOOL_COMMIT.encode():
        raise SecondaryProbeError('wrong ExifTool source commit')
    if dirty.returncode or dirty.stdout.strip():
        raise SecondaryProbeError('modified ExifTool tracked source')
    version = _run([perl, str(script), '-config', '', '-ver'])
    if version.returncode or version.stdout.strip() != EXIFTOOL_VERSION.encode():
        raise SecondaryProbeError('wrong ExifTool runtime version')
    return script


def observe(upstream, fixture, perl='perl'):
    script = verify_checkout(upstream, perl=perl)
    fixture = Path(fixture).resolve()
    if not fixture.is_file():
        raise SecondaryProbeError('fixture missing')
    if fixture.stat().st_size > MAX_FIXTURE_BYTES:
        raise SecondaryProbeError('fixture exceeds size limit')
    original = fixture.read_bytes()
    if len(original) > MAX_FIXTURE_BYTES:
        raise SecondaryProbeError('fixture exceeds size limit')
    cmd = [perl, str(script), '-config', '', '-json', '-G1', '-s', str(fixture)]
    result = _run(cmd)
    if len(result.stdout) > MAX_OUTPUT_BYTES or len(result.stderr) > MAX_OUTPUT_BYTES:
        raise SecondaryProbeError('ExifTool output exceeds limit')
    after = fixture.read_bytes()
    if original != after:
        raise SecondaryProbeError('read-only command modified fixture')
    output = None
    parse_error = None
    try:
        output = json.loads(result.stdout)
        if not isinstance(output, list) or len(output) != 1 or not isinstance(output[0], dict):
            raise ValueError('expected exactly one JSON object')
    except (ValueError, UnicodeDecodeError) as error:
        output = None
        parse_error = str(error)
    return {'tool': 'ExifTool', 'version': EXIFTOOL_VERSION, 'sourceCommit': EXIFTOOL_COMMIT,
            'fixtureSha256': hashlib.sha256(original).hexdigest(), 'fixtureLength': len(original),
            'arguments': ['-config', '', '-json', '-G1', '-s', '<fixture>'],
            'exitCode': result.returncode, 'stdoutSha256': hashlib.sha256(result.stdout).hexdigest(),
            'stderr': result.stderr.decode('utf-8', 'replace'), 'json': output,
            'jsonParseError': parse_error, 'interpretation': 'secondary-observation-not-conformance'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--exiftool-checkout', required=True, type=Path)
    parser.add_argument('--fixture', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--perl', default='perl')
    args = parser.parse_args()
    result = observe(args.exiftool_checkout, args.fixture, args.perl)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
