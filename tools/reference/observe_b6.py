"""E01 B6 pinned CLI observations on owned D1-derived DNG fixtures.

CFA strip bytes are checked, not raw decoding. The authored IFD1 JPEG variant
is a structural probe whose full DNG conformance is not independently proven.
Outcomes are observations, never MetadataSharp verification or release approval.
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

import dng_census_fixtures as fixtures
import dng_metadata_oracle as metadata
import dng_raw_oracle as raw
import dng_thumbnail_oracle as thumbnail
import exiv2_baseline as baseline
import jpeg_owned_fixtures as jpeg
import observe_b0 as common

ROOT = Path(__file__).resolve().parents[2]


def fixture_for(row):
    _, shape, order = row['layout'].split('-')
    shape = 'thumb' if shape == 'preview' else shape
    present = row['operation'] != 'insert'
    ifd1 = row['family'] == 'thumbnail' and present
    return f"b6-{shape}-{order}-{'present' if present else 'empty'}{'-ifd1' if ifd1 else ''}.dng"


def records(data, tag):
    return [v['bytes'] for v in metadata.root_values(data, {tag})]


def probe(report, row, exe, source, work):
    original = source.read_bytes()
    target = work / 'case.dng'
    target.write_bytes(original)
    family, op = row['family'], row['operation']
    args = []
    expected = None
    record_only = family in ('inventory', 'comments', 'xmp', 'iptc')
    facts = {}
    # Check the intended precondition independently, before asking the CLI.
    tags = {'exif': 315, 'icc': 34675, 'iptc': 33723, 'xmp': 700}
    if family in tags:
        before_values = records(original, tags[family])
        if bool(before_values) != (op != 'insert'):
            raise ValueError('fixture metadata precondition mismatch: ' + row['rowId'])
    if family == 'thumbnail':
        thumb_before = thumbnail.ifd1_jpeg(original)
        if (thumb_before is not None) != (op != 'insert'):
            raise ValueError('fixture thumbnail precondition mismatch: ' + row['rowId'])
    replacement_thumb = jpeg.thumb_jpeg_bytes().replace(b'\xff\xd8', b'\xff\xd8\xff\xfe\x00\x08B6new!', 1)
    if family == 'inventory':
        args = ['-pS']
    elif family == 'comments':
        args = ['-pc']
    elif family == 'exif':
        args = ['-pa'] if op == 'read' else ['-M', {
            'insert': 'set Exif.Image.Artist Ascii B6Inserted',
            'replace': 'set Exif.Image.Artist Ascii B6Replacement',
            'remove': 'del Exif.Image.Artist'}[op]]
    elif family == 'icc':
        if op == 'read':
            args = ['-pC']
        elif op in ('insert', 'replace'):
            (work / 'case.icc').write_bytes(jpeg.PROFILE_B)
            args = ['-iC', '-l', str(work)]
        else:
            args = ['-d', 'C']
    elif family in ('iptc', 'xmp'):
        key = 'Iptc.Application2.Keywords' if family == 'iptc' else 'Xmp.xmp.CreatorTool'
        args = ['-pa'] if op == 'read' else ['-M', ('del ' + key if op == 'remove' else 'set ' + key + ' B6Replacement')]
    elif family == 'thumbnail':
        if op == 'read':
            args = ['-et']
        elif op in ('insert', 'replace'):
            (work / 'case-thumb.jpg').write_bytes(replacement_thumb)
            args = ['-it']
        else:
            args = ['-dt']
    else:
        raise ValueError('unsupported B6 family')
    rc, stdout, stderr = common.soft_run(report, [exe, *args, target], work)
    after = target.read_bytes()
    output_artifact = work / 'case-thumb.jpg'
    # Only extraction produces an output artifact; the insert/replace input is
    # not evidence of output and must not alter nonzero-exit classification.
    extracted = output_artifact.read_bytes() if family == 'thumbnail' and op == 'read' and output_artifact.exists() else None
    if extracted is not None:
        facts['extractedSha256'] = baseline.digest(extracted)
        facts['extractedLength'] = len(extracted)
    parse_ok = True
    try:
        raw_before = raw.raw_strip_fingerprints(original)
        raw_after = raw.raw_strip_fingerprints(after)
        facts['rawBefore'], facts['rawAfter'] = raw_before, raw_after
        facts['scanPreserved'] = raw_before == raw_after
    except raw.DngOracleError as error:
        facts['rawOracleError'] = str(error)
        facts['scanPreserved'] = None
        parse_ok = False
    try:
        if family == 'exif':
            found = records(after, 315)
            facts['artistValuesHex'] = [v.hex() for v in found]
            expected = (found == [b'B6Alice\0'] and b'Exif.Image.Artist' in stdout and b'B6Alice' in stdout if op == 'read' else
                        not found if op == 'remove' else
                        found == [(b'B6Inserted\0' if op == 'insert' else b'B6Replacement\0')])
        elif family == 'icc':
            profiles = records(after, 34675)
            facts['iccSha256'] = [baseline.digest(v) for v in profiles]
            expected = (stdout == jpeg.PROFILE_A if op == 'read' else
                        not profiles if op == 'remove' else profiles == [jpeg.PROFILE_B])
        elif family == 'thumbnail':
            output_thumb = thumbnail.ifd1_jpeg(after)
            facts['thumbnailSha256'] = None if output_thumb is None else baseline.digest(output_thumb)
            expected = (extracted == jpeg.thumb_jpeg_bytes() if op == 'read' else
                        output_thumb is None if op == 'remove' else output_thumb == replacement_thumb)
        elif family in ('xmp', 'iptc'):
            rc2, out2, err2 = common.soft_run(report, [exe, '-pa', target], work)
            facts['readbackExitCode'] = rc2
            facts['readbackStdoutSha256'] = baseline.digest(out2)
            key = b'Iptc.Application2.Keywords' if family == 'iptc' else b'Xmp.xmp.CreatorTool'
            relevant = [line for line in out2.splitlines() if line.startswith(key)]
            facts['readbackMatchingLines'] = [line.decode('utf-8', 'replace') for line in relevant]
            facts['readbackContainsOriginal'] = any(b'MetadataSharp' in line for line in relevant)
            facts['readbackContainsReplacement'] = any(b'B6Replacement' in line for line in relevant)
            facts['readbackStderr'] = err2.decode('utf-8', 'replace')[:1000]
    except (metadata.MetadataOracleError, thumbnail.ThumbnailOracleError) as error:
        facts['metadataOracleError'] = str(error)
        parse_ok = False
    outcome = common.classify(rc, parse_ok, expected, facts['scanPreserved'] is not False,
                              artifact_produced=extracted is not None)
    return {'id': row['rowId'], 'seedId': 'EX0289-DNG-' + family.upper(),
            'operation': op, 'family': family, 'layout': row['layout'],
            'duplicates': row['duplicates'], 'textEncoding': row['textEncoding'],
            'fixture': source.name, 'fixtureSha256': baseline.digest(original),
            'fixtureRights': 'owned-synthetic', 'inputSha256': baseline.digest(original),
            'outputSha256': baseline.digest(after), 'recordOnly': record_only,
            'expectOk': expected, 'outcome': outcome, 'facts': facts}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--upstream', type=Path, required=True)
    parser.add_argument('--build', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/e01')
    parser.add_argument('--census', type=Path, default=ROOT / 'docs/research/EXIV2_CENSUS.json')
    args = parser.parse_args()
    upstream, build = args.upstream.resolve(), args.build.resolve()
    output = (args.output / 'b6').resolve()
    output.mkdir(parents=True, exist_ok=True)
    report = {'schema': 1, 'status': 'running', 'batch': 'B6',
              'sourceCommit': os.environ.get('E01_SOURCE_COMMIT'),
              'baseline': {'version': baseline.VERSION, 'sourceCommit': baseline.PIN},
              'commands': [], 'observations': [], 'staticNotApplicable': [],
              'outcomeCounts': {}, 'denominatorFrozen': False}
    def save():
        (output / 'observations.json').write_text(json.dumps(report, indent=2) + '\n')
    try:
        if not re.fullmatch('[0-9a-f]{40}', report['sourceCommit'] or ''):
            raise ValueError('missing E01_SOURCE_COMMIT')
        head = subprocess.run(['git', '-C', str(upstream), 'rev-parse', 'HEAD'], check=True, capture_output=True, text=True).stdout.strip()
        dirty = subprocess.run(['git', '-C', str(upstream), 'status', '--porcelain', '--untracked-files=no'], check=True, capture_output=True, text=True).stdout.strip()
        if head != baseline.PIN or dirty:
            raise ValueError('wrong or modified upstream')
        cache = (build / 'CMakeCache.txt').read_bytes()
        report['baseline']['effectiveCmake'] = baseline.check_cache(cache.decode())
        report['baseline']['cmakeCacheSha256'] = baseline.digest(cache)
        report['baseline']['licenseSha256'] = baseline.digest((upstream / 'COPYING').read_bytes())
        exe = build / 'bin/exiv2'
        report['baseline']['executableSha256'] = baseline.digest(exe.read_bytes())
        env = {**os.environ, 'LC_ALL': 'C'}
        version = subprocess.run([str(exe), '--version'], capture_output=True, text=True, check=True, env=env, timeout=30).stdout
        baseline.check_version(version)
        verbose = subprocess.run([str(exe), '-v', '-V'], capture_output=True, text=True, check=True, env=env, timeout=30).stdout
        baseline.check_runtime_features(verbose)
        report['baseline']['verboseVersion'] = verbose
        fixtures.verify()
        fixture_dir = output / 'fixtures'
        fixtures.generate(fixture_dir)
        report['fixtureManifest'] = fixtures.manifest()
        rows = [row for row in json.loads(args.census.read_text())['rows'] if row['batch'] == 'B6']
        if not rows:
            raise ValueError('no B6 census rows')
        for i, row in enumerate(rows):
            work = output / 'work' / f'{i:04d}'
            # Stale artifacts from an earlier run could otherwise masquerade as
            # a successful thumbnail extraction.
            if work.exists():
                shutil.rmtree(work)
            work.mkdir(parents=True)
            report['observations'].append(probe(report, row, exe, fixture_dir / fixture_for(row), work))
            save()
        counts = Counter(o['outcome'] for o in report['observations'])
        counts['static-not-applicable'] = 0
        report['outcomeCounts'] = dict(sorted(counts.items()))
        report['status'] = 'passed'
        save()
        encoded = base64.b64encode(zlib.compress(json.dumps(report, separators=(',', ':')).encode())).decode()
        # GitHub retains at most ten notice annotations per step. Use a larger
        # chunk for this report and fail explicitly rather than emit an incomplete
        # recoverable-looking sequence when the report grows beyond that cap.
        # Check-run annotation messages are truncated at 4096 characters.
        chunks = [encoded[i:i + 4000] for i in range(0, len(encoded), 4000)]
        if len(chunks) > 10:
            raise ValueError('B6 report exceeds the ten-annotation recovery limit')
        for i, chunk in enumerate(chunks):
            print(f'::notice title=B6Z observations.json {i}/{len(chunks)}::{chunk}', flush=True)
    except Exception:
        import traceback
        report['status'] = 'failed'
        report['failure'] = traceback.format_exc()[-3500:]
        save()
        raise


if __name__ == '__main__':
    main()
