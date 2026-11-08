"""Bounded external v0.28.9 observations; no upstream implementation/test imports."""
import argparse
import base64
from collections import Counter
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import struct
import subprocess
import traceback
import zlib

ROOT = Path(__file__).resolve().parents[2]
PIN = 'b0859fc61fea5dedfc898aece6b15893ddf4314e'
VERSION = '0.28.9'
OPTIONS = {
    'CMAKE_BUILD_TYPE': 'Release', 'BUILD_SHARED_LIBS': 'OFF',
    'EXIV2_BUILD_SAMPLES': 'OFF', 'EXIV2_BUILD_UNIT_TESTS': 'OFF', 'EXIV2_BUILD_DOC': 'OFF',
    'EXIV2_ENABLE_XMP': 'ON', 'EXIV2_ENABLE_EXTERNAL_XMP': 'OFF',
    'EXIV2_ENABLE_PNG': 'ON', 'EXIV2_ENABLE_BMFF': 'ON', 'EXIV2_ENABLE_BROTLI': 'ON',
    'EXIV2_ENABLE_NLS': 'OFF', 'EXIV2_ENABLE_FILESYSTEM_ACCESS': 'ON',
    'EXIV2_BUILD_EXIV2_COMMAND': 'ON', 'EXIV2_BUILD_FUZZ_TESTS': 'OFF',
    'EXIV2_ENABLE_VIDEO': 'ON', 'EXIV2_ENABLE_INIH': 'ON', 'EXIV2_ENABLE_LENSDATA': 'ON',
    'EXIV2_ENABLE_WEBREADY': 'OFF', 'EXIV2_ENABLE_CURL': 'OFF', 'EXIV2_ENABLE_DYNAMIC_RUNTIME': 'ON',
}


# Measured v0.28.9 verbose keys. PNG's compile switch is separately mandatory;
# have_libz is a dependency observation, not by itself proof of PNG functionality.
RUNTIME_FEATURES = ('have_xmptoolkit', 'have_libz', 'have_brotli', 'enable_bmff')

def digest(data):
    return hashlib.sha256(data).hexdigest()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def check_version(text):
    found = re.search(r'\bexiv2\s+(\S+)', text)
    require(found is not None and found.group(1) == VERSION, 'Wrong or missing Exiv2 version')


def check_cache(text):
    values = {}
    types = {}
    for line in text.splitlines():
        match = re.fullmatch(r'([^/#][^:=]*):([^=]+)=(.*)', line)
        if match:
            require(match[1] not in values, 'Duplicate CMake cache key')
            types[match[1]], values[match[1]] = match[2], match[3]
    for key, expected in OPTIONS.items():
        require(values.get(key) == expected, f'CMake setting missing/wrong: {key}')
        require(types[key] == ('STRING' if key == 'CMAKE_BUILD_TYPE' else 'BOOL'), f'Unrecognized/untyped CMake option: {key}')
    # Retain all effective upstream feature BOOL values, not just enabled headline features.
    return {k: v for k, v in sorted(values.items()) if k.startswith('EXIV2_') or k in OPTIONS or k in ('CMAKE_CXX_COMPILER', 'CMAKE_CXX_COMPILER_VERSION')}


def check_runtime_features(text):
    facts = dict(s.split('=', 1) for s in text.splitlines() if '=' in s and s.startswith(('enable_', 'have_')))
    for key in RUNTIME_FEATURES:
        require(facts.get(key) == '1', f'Missing/disabled runtime feature: {key}')


def inspect_jpeg(data):
    """Only our tiny single-scan, single-IFD JPEG variant; NOT a general image reader."""
    require(data[:2] == b'\xff\xd8', 'Missing SOI')
    pos = 2
    non_exif = []
    exif = None
    while pos < len(data):
        start = pos
        require(pos + 4 <= len(data) and data[pos] == 255, 'Truncated/invalid JPEG marker')
        marker = data[pos + 1]
        require(marker not in (0, 1, 0xd8, 0xd9, 0xff) and not 0xd0 <= marker <= 0xd7, 'Unsupported standalone/fill marker')
        size = int.from_bytes(data[pos+2:pos+4], 'big')
        require(size >= 2 and pos + 2 + size <= len(data), 'Invalid segment bounds')
        payload = data[pos+4:pos+2+size]
        pos += 2 + size
        if marker == 0xda:
            require(data[-2:] == b'\xff\xd9', 'Missing EOI')
            scan = data[start:]
            break
        if marker == 0xe1 and payload.startswith(b'Exif\0\0'):
            require(exif is None, 'Duplicate EXIF APP1')
            exif = payload[6:]
        else:
            non_exif.append(data[start:pos])
    else:
        raise ValueError('Missing SOS')
    tags = {}
    if exif is not None:
        require(len(exif) >= 8 and exif[:2] == b'II', 'Only authored classic little-endian TIFF')
        def u16(p):
            require(0 <= p <= len(exif)-2, 'SHORT outside TIFF')
            return struct.unpack_from('<H', exif, p)[0]
        def u32(p):
            require(0 <= p <= len(exif)-4, 'LONG outside TIFF')
            return struct.unpack_from('<I', exif, p)[0]
        require(u16(2) == 42, 'Not classic TIFF')
        first = u32(4)
        require(first >= 8 and first % 2 == 0, 'Invalid first IFD offset')
        count = u16(first)
        end = first + 2 + 12 * count
        require(count <= 32 and end + 4 <= len(exif) and u32(end) == 0, 'Not authored one-IFD variant')
        for p in range(first + 2, end, 12):
            tag, kind, n = u16(p), u16(p+2), u32(p+4)
            require(tag not in tags and kind in (2, 3) and 0 < n <= 1024, 'Duplicate/unsupported fixture field')
            size = n * (2 if kind == 3 else 1)
            offset = p + 8 if size <= 4 else u32(p+8)
            require(offset >= (end + 4 if size > 4 else p+8) and offset + size <= len(exif), 'Field outside data extent')
            tags[tag] = (kind, n, exif[offset:offset+size])
    return dict(tags=tags, scan=scan, nonExif=b''.join(non_exif), nonExifParts=non_exif)


def verify(before, after, expected, allow_segment_reorder=False):
    target = None if expected is None else (2, len(expected), expected)
    require(after['tags'].get(315) == target, 'Artist raw type/count/value mismatch')
    require({k: v for k, v in before['tags'].items() if k != 315} == {k: v for k, v in after['tags'].items() if k != 315}, 'Non-target TIFF fields changed')
    require(before['scan'] == after['scan'], 'JPEG coded scan bytes changed')
    if before['nonExif'] == after['nonExif']:
        return 'exact-bytes-and-order'
    old, new = before['nonExifParts'], after['nonExifParts']
    if allow_segment_reorder and Counter(old) == Counter(new):
        return 'byte-identical-segment-permutation-NOT-exact-order'
    removed = list((Counter(old) - Counter(new)).elements())
    added = list((Counter(new) - Counter(old)).elements())
    raise ValueError('Non-EXIF segments changed: ' + json.dumps(dict(oldCount=len(old), newCount=len(new), removedHex=[s.hex()[:500] for s in removed], addedHex=[s.hex()[:500] for s in added])))


def parse_artist(text, expected):
    if expected is None:
        require(not text.strip(), 'Deleted Artist still printed')
    else:
        fields = text.split()
        require(fields == ['Exif.Image.Artist', 'Ascii', str(len(expected)), expected[:-1].decode('ascii')], 'Unexpected printed Artist fact')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--upstream', type=Path, required=True)
    parser.add_argument('--build', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/e01/observations')
    args = parser.parse_args()
    upstream, build, output = args.upstream.resolve(), args.build.resolve(), args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    report = dict(schema=1, status='running', sourceCommit=os.environ.get('E01_SOURCE_COMMIT'),
                  baseline=dict(version=VERSION, sourceCommit=PIN), commands=[], observations=[], denominatorFrozen=False)
    def save():
        (output / 'observations.json').write_text(json.dumps(report, indent=2) + '\n')
    def run(command, raw_only=False):
        command = list(map(str, command))
        result = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=180,
                                env={**os.environ, 'LC_ALL': 'C', 'LANG': 'C'})
        report['commands'].append(dict(args=command, exitCode=result.returncode,
                                      stdoutSha256=digest(result.stdout.encode()), stderr=result.stderr,
                                      **({} if raw_only else {'stdout': result.stdout})))
        save()
        require(result.returncode == 0, f'Command failed: {command!r}: {result.stderr}')
        return result.stdout
    try:
        require(bool(re.fullmatch('[0-9a-f]{40}', report['sourceCommit'] or '')), 'Missing source identity')
        require(run(['git', '-C', upstream, 'rev-parse', 'HEAD']).strip() == PIN, 'Wrong upstream commit')
        require(not run(['git', '-C', upstream, 'status', '--porcelain', '--untracked-files=no']).strip(), 'Modified upstream source')
        cache = (build / 'CMakeCache.txt').read_bytes()
        report['baseline']['effectiveCmake'] = check_cache(cache.decode())
        report['baseline']['cmakeCacheSha256'] = digest(cache)
        report['baseline']['licenseSha256'] = digest((upstream / 'COPYING').read_bytes())
        exe = build / 'bin/exiv2'
        report['baseline']['executableSha256'] = digest(exe.read_bytes())
        check_version(run([exe, '--version']))
        verbose = run([exe, '--verbose', '--version'], raw_only=True)
        (output / 'verbose-version.txt').write_text(verbose)
        # Do not persist namespace catalogs/tag dictionaries in the repository receipt.
        prefixes = ('exiv2=', 'version=', 'compiler=', 'bits=', 'platform=', 'build_type=', 'debug=', 'dll=', 'enable_', 'have_', 'library=')
        report['baseline']['verboseBuildFacts'] = [s for s in verbose.splitlines() if s.startswith(prefixes)]
        check_runtime_features(verbose)
        report['baseline']['runtimeFeatureChecks'] = dict.fromkeys(RUNTIME_FEATURES, '1')
        for command in (['cmake', '--version'], ['c++', '--version'], ['ldd', exe],
                        ['dpkg-query', '-W', 'cmake', 'ninja-build', 'g++', 'libexpat1-dev', 'zlib1g-dev', 'libbrotli-dev', 'libinih-dev', 'libfmt-dev']):
            run(command)
        fixtures = output / 'fixtures'
        run(['dotnet', 'run', '--project', ROOT / 'tools/MetadataSharp.ReferenceFixture', '-c', 'Release', '-f', 'net8.0', '--', 'generate', fixtures])
        cases = [('read', 'ours.jpg', None, b'Alice\0'),
                 ('insert', 'base.jpg', 'set Exif.Image.Artist Ascii Alice', b'Alice\0'),
                 ('replace', 'xmp.jpg', 'set Exif.Image.Artist Ascii Bob', b'Bob\0'),
                 ('remove', 'xmp.jpg', 'del Exif.Image.Artist', None)]
        for operation, name, modification, expected in cases:
            source = fixtures / name
            original = source.read_bytes()
            before = inspect_jpeg(original)
            initial = None if operation == 'insert' else b'Alice\0'
            verify(before, before, initial)
            target = output / (operation + '.jpg')
            shutil.copyfile(source, target)
            if modification:
                run([exe, '-M', modification, target])
            result = target.read_bytes()
            after = inspect_jpeg(result)
            report['currentCase'] = dict(operation=operation, fixture=name)
            segment_preservation = verify(before, after, expected, allow_segment_reorder=modification is not None)
            parse_artist(run([exe, '-P', 'kycv', '-K', 'Exif.Image.Artist', target]), expected)
            require(source.read_bytes() == original, 'Source fixture changed')
            if operation == 'read':
                require(result == original, 'Read operation mutated file')
            report['observations'].append(dict(id=f'EX0289-JPEG-EXIF-{operation.upper()}-IFD0-ARTIST-ASCII-II-01',
                seedId='EX0289-JPEG-EXIF', operation=operation, variant='single-scan 8x8 grayscale; classic II; IFD0 ASCII Artist; no MakerNote/thumbnail',
                inputSha256=digest(original), outputSha256=digest(result), fixture=name,
                fixtureRights='repository-authored generator; MIT; no upstream fixture',
                independentExpected=None if expected is None else dict(type=2, count=len(expected), rawHex=expected.hex()),
                expectedSource='authored literal; TIFF6 ASCII type/count/raw bytes; not reference-derived',
                preserved=dict(scanSha256=digest(after['scan']), nonExifSegmentsBeforeSha256=digest(before['nonExif']), nonExifSegmentsAfterSha256=digest(after['nonExif']), nonExifSegmentPreservation=segment_preservation, nonTargetIfd0=True),
                baselineObservation='passed-independent-byte-and-print-check',
                metadataSharpVerification='not-scored; generated input is not bidirectional operation parity', limits='Only this fixture variant, not full JPEG/EXIF support'))
            save()
        report['status'] = 'passed'
        save()
        raw = json.dumps(report, separators=(',', ':')).encode()
        encoded = base64.b64encode(zlib.compress(raw)).decode()
        chunks = [encoded[i:i+2500] for i in range(0, len(encoded), 2500)]
        for i, chunk in enumerate(chunks):
            print(f'::notice title=E01Z observations.json {i}/{len(chunks)}::{chunk}', flush=True)
    except Exception:
        report['status'] = 'failed'
        report['failure'] = traceback.format_exc()[-3500:]
        save()
        diagnostic = json.dumps(dict(error=report['failure'], currentCase=report.get('currentCase')), separators=(',', ':'))
        diagnostic = diagnostic.replace('%', '%25').replace('\r', '%0D').replace('\n', '%0A')
        print('::error title=E01 black-box observation failure::' + diagnostic, flush=True)
        raise


if __name__ == '__main__':
    main()
