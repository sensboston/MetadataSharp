"""E01 census observation batch B3: pinned Exiv2 CLI against owned synthetic WebPs.

B3 covers WebP x {inventory, comments (noDocumentedClaim read), exif, icc,
iptc (noDocumentedClaim read), thumbnail, xmp} per docs/research/
EXIV2_OPERATION_SEEDS.json (claim '-' cells materialize as one noDocumentedClaim
read row each; the CLI may reveal undocumented support, which is recorded as
observed behavior, never asserted).

Observation, not pass/fail: outcomes are recorded honestly (accepted /
accepted-unexpected / refused / scan-changed / accepted-unparseable /
nonzero-exit-artifact-produced) and only infrastructure failures stop the run.
The WebP analog of the JPEG scan invariant is the VP8/VP8L bitstream chunk
payload stream: metadata rewrites must never change it. RIFF structure and
chunk sizes are strictly verified on the structural parse of mutated files;
the fixtures carry a hand-authored fake VP8 keyframe header (never decoded by
metadata operations).

Usage: observe_b3.py --upstream artifacts/e01/source --build artifacts/e01/build [--output artifacts/e01]
"""
import argparse
import base64
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import zlib
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/reference'))
import exiv2_baseline as baseline  # noqa: E402  (single source for pin/options/checks)
import observe_b0 as shared  # noqa: E402  (soft_run, classify, bim_blocks)
import jpeg_owned_fixtures as jpegs  # noqa: E402  (shared TIFF parser + profile constants)
import webp_owned_fixtures as owned  # noqa: E402

PIN = baseline.PIN
VERSION = baseline.VERSION
CENSUS = ROOT / 'docs/research/EXIV2_CENSUS.json'
from build_census import STATIC_NOT_APPLICABLE  # noqa: E402

THUMB_TAG_IDS = frozenset({0x0100, 0x0101, 0x0103, 0x0111, 0x0112, 0x0115, 0x0116, 0x0117, 0x0201, 0x0202})


def webp_chunks(data):
    """Strict structural parse of the RIFF/WebP container (sizes, padding, tree)."""
    if data[:4] != b'RIFF' or data[8:12] != b'WEBP':
        raise ValueError('Missing RIFF/WEBP signature')
    riff_size = int.from_bytes(data[4:8], 'little')
    if riff_size != len(data) - 8:
        raise ValueError(f'RIFF size field {riff_size} != file size {len(data)} - 8')
    chunks, pos = [], 12
    while pos < len(data):
        if pos + 8 > len(data):
            raise ValueError('Truncated chunk header')
        cid = data[pos:pos + 4]
        length = int.from_bytes(data[pos + 4:pos + 8], 'little')
        start = pos + 8
        stop = start + length
        if stop > len(data):
            raise ValueError(f'Chunk {cid.decode("latin1")} overruns the file')
        payload = data[start:stop]
        chunks.append((cid, payload))
        pos = stop + (length % 2)  # chunks are zero-padded to even sizes
    return chunks


def scan_payloads(data):
    """VP8/VP8L bitstream payloads; the metadata-rewrite invariant of this batch."""
    return b''.join(payload for cid, payload in webp_chunks(data)
                    if cid in (b'VP8 ', b'VP8L'))


def xmp_packets(data):
    """XMP payloads carried by raw 'XMP ' chunks (WebP extended file format)."""
    return [payload for cid, payload in webp_chunks(data) if cid == b'XMP ']


def exif_blocks(data):
    """Classic II TIFF blocks from EXIF chunks (WebP carries no JPEG APP1 prefix)."""
    blocks = []
    for cid, payload in webp_chunks(data):
        if cid == b'EXIF':
            blocks.append(jpegs.parse_tiff(payload[6:] if payload.startswith(b'Exif\x00\x00') else payload))
    return blocks


def icc_profiles(data):
    """Profiles from ICCP chunks (raw profile bytes, WebP does not compress them)."""
    return [payload for cid, payload in webp_chunks(data) if cid == b'ICCP']


def thumbnail_tags(data):
    tags = {}
    for ifds in exif_blocks(data):
        for ifd in ifds[1:]:
            for tag, values in ifd['tags'].items():
                tags.setdefault(tag, []).extend(values)
    return tags


def case_fixture(family, layout, duplicates, operation=None, text_encoding=None):
    if family == 'inventory':
        return 'webp-base.webp'
    if family == 'comments':
        return 'webp-comments.webp'
    if family == 'exif':
        return 'webp-exif-dup.webp' if duplicates != 'none' else 'webp-exif.webp'
    if family == 'icc':
        return 'webp-iccp-dup.webp' if duplicates != 'none' else 'webp-iccp.webp'
    if family == 'iptc':
        return 'webp-iptc.webp'
    if family == 'xmp':
        table = {
            ('webp-xmp', 'none', 'utf-8'): 'webp-xmp.webp',
            ('webp-xmp', 'none', 'unknown-raw'): 'webp-xmp-nonutf8.webp',
            ('webp-xmp', 'first-wins', 'utf-8'): 'webp-xmp-dup.webp',
            ('webp-xmp', 'first-wins', 'unknown-raw'): 'webp-xmp-nonutf8.webp',
        }
        return table.get((layout, duplicates, text_encoding))
    if family == 'thumbnail':
        return 'webp-exif-thumb-jpeg.webp' if layout == 'exif-ifd1-jpeg' else 'webp-exif-thumb-raw.webp'
    return None


def static_not_applicable(row):
    for family, layout, duplicates, operation, reason in STATIC_NOT_APPLICABLE:
        if (row['family'] == family and row['layout'] == layout
                and (duplicates is None or row['duplicates'] == duplicates)
                and (operation is None or row['operation'] == operation)):
            return reason
    return None


TEXT_PAYLOADS = {}

XMP_UTF8_VALUE = 'MetadataSharp B3 é ✓'
XMP_RAW_VALUE = b'\x89RAW\xff\xfe'


def run_inventory(report, row, exe, fixture_path, work, fixtures_dir):
    original = fixture_path.read_bytes()
    rc, stdout, _ = shared.soft_run(report, [exe, '-pS', fixture_path], cwd=work)
    return rc, original, original, True, None, False, {'scanPreserved': True}, stdout, fixture_path.name


def run_claim_read(report, row, exe, fixture_path, work, fixtures_dir):
    """noDocumentedClaim read rows (WebP comments/IPTC, claim '-'): record what the
    pinned CLI does; the cell carries no documented claim, so nothing is pre-asserted."""
    original = fixture_path.read_bytes()
    family = row['family']
    facts = {}
    if family == 'comments':
        rc, stdout, _ = shared.soft_run(report, [exe, '-pc', fixture_path], cwd=work)
        facts['carrierFourccs'] = [cid.decode('latin1', 'replace') for cid, _ in webp_chunks(original)
                                   if cid not in (b'VP8 ', b'VP8L', b'VP8X')]
    else:  # iptc
        rc, stdout, _ = shared.soft_run(report, [exe, '-pi', fixture_path], cwd=work)
        facts['carrierFourccs'] = [cid.decode('latin1', 'replace') for cid, _ in webp_chunks(original)
                                   if cid not in (b'VP8 ', b'VP8L', b'VP8X')]
    facts['stdoutSha256'] = hashlib.sha256(stdout).hexdigest()
    facts['stdoutPreview'] = stdout[:200].decode('utf-8', 'replace')
    parse_ok = True
    try:
        webp_chunks(original)
    except Exception as error:
        parse_ok, facts['parseError'] = False, str(error)
    facts['scanPreserved'] = True
    return rc, original, original, parse_ok, None, False, facts, stdout, fixture_path.name


def run_exif(report, row, exe, fixture_path, work, fixtures_dir):
    """WebP EXIF (documented Read/Write claim): classic II TIFF in an 'EXIF' chunk."""
    original = fixture_path.read_bytes()
    operation = row['operation']
    record_only = row['duplicates'] != 'none'
    facts = {}

    def artist_values(data):
        values = []
        for tiff in exif_blocks(data):
            for ifd in tiff:
                for tag, vals in ifd['tags'].items():
                    if tag == 0x013B:
                        for type_, count, raw in vals:
                            if type_ == 2:  # ASCII
                                values.append(raw.rstrip(b'\x00').decode('ascii', 'backslashreplace'))
        return values

    operative = fixture_path.name
    if operation == 'read':
        rc, stdout, _ = shared.soft_run(report, [exe, '-pa', fixture_path], cwd=work)
        after = original
        expect_ok = None
        if not record_only:
            expect_ok = b'Alice' in stdout
        facts['stdoutPreview'] = stdout[:400].decode('utf-8', 'replace')
    elif operation in ('insert', 'replace'):
        base_name = fixture_path.name
        operative = base_name
        if operation == 'insert' and row['duplicates'] == 'none':
            base_name, operative = 'webp-base.webp', 'webp-base.webp'  # from-nothing case
        base = fixtures_dir / base_name
        target = work / 'case.webp'
        shutil.copyfile(base, target)
        value = 'MetadataSharpAdded' if operation == 'insert' else 'MetadataSharpReplacement'
        rc, stdout, _ = shared.soft_run(report, [exe, '-M', f'set Exif.Image.Artist Ascii {value}', target], cwd=work)
        after, original = target.read_bytes(), base.read_bytes()
        facts['artistValues'] = artist_values(after)
        expect_ok = None if record_only else (value in (facts['artistValues'] or []))
    elif operation == 'remove':
        target = work / 'case.webp'
        shutil.copyfile(fixture_path, target)
        rc, stdout, _ = shared.soft_run(report, [exe, '-M', 'del Exif.Image.Artist', target], cwd=work)
        after = target.read_bytes()
        facts['artistValues'] = artist_values(after)
        expect_ok = None if record_only else ('Alice' not in (facts['artistValues'] or []))
    else:
        raise AssertionError(operation)
    # 'before' facts describe the actual input bytes (base may differ for from-nothing inserts)
    facts['exifChunkCount'] = sum(1 for cid, _ in webp_chunks(original) if cid == b'EXIF')
    facts['scanPreserved'] = scan_payloads(after) == scan_payloads(original)
    parse_ok = True
    try:
        webp_chunks(after)
    except Exception as error:
        parse_ok, facts['parseError'] = False, str(error)
    return rc, original, after, parse_ok, expect_ok, record_only, facts, b'', operative


def run_icc(report, row, exe, fixture_path, work, fixtures_dir):
    original = fixture_path.read_bytes()
    operation = row['operation']
    record_only = row['duplicates'] != 'none'
    facts = {}
    source_dir = work / 'icc-src'
    source_dir.mkdir(exist_ok=True)
    (source_dir / 'case.icc').write_bytes(jpegs.PROFILE_A)  # exiv2 -iC -l dir reads <target-stem>.icc
    if operation == 'read':
        rc, stdout, _ = shared.soft_run(report, [exe, '-pC', fixture_path], cwd=work)
        after, expect_ok = original, None
        facts['stdoutEqualsProfileA'] = bytes(stdout) == jpegs.PROFILE_A
    elif operation in ('insert', 'replace'):
        target = work / 'case.webp'
        shutil.copyfile(fixture_path, target)
        rc, stdout, _ = shared.soft_run(report, [exe, '-iC', '-l', source_dir, target], cwd=work)
        after = target.read_bytes()
        profiles = icc_profiles(after)
        facts['iccProfileCount'] = len(profiles)
        facts['iccProfileEqualsA'] = jpegs.PROFILE_A in profiles
        expect_ok = None if record_only else (jpegs.PROFILE_A in profiles)
    elif operation == 'remove':
        target = work / 'case.webp'
        shutil.copyfile(fixture_path, target)
        rc, stdout, _ = shared.soft_run(report, [exe, '-d', 'C', target], cwd=work)
        after = target.read_bytes()
        profiles = icc_profiles(after)
        facts['iccProfileCountAfter'] = len(profiles)
        expect_ok = None if record_only else len(profiles) == 0
    else:
        raise AssertionError(operation)
    facts['scanPreserved'] = scan_payloads(after) == scan_payloads(original)
    parse_ok = True
    try:
        webp_chunks(after)
    except Exception as error:
        parse_ok, facts['parseError'] = False, str(error)
    return rc, original, after, parse_ok, expect_ok, record_only, facts, b'', fixture_path.name


def run_thumbnail(report, row, exe, fixture_path, work, fixtures_dir):
    original = fixture_path.read_bytes()
    operation = row['operation']
    facts = {}

    def ifd1_has_jpeg_thumbnail(data):
        tags = thumbnail_tags(data)
        return 0x0201 in tags and 0x0202 in tags

    def after_tags(data):
        return sorted(tag for tag in thumbnail_tags(data) if tag in THUMB_TAG_IDS)

    record_only = True
    operative = fixture_path.name
    if operation == 'read':
        rc, stdout, _ = shared.soft_run(report, [exe, '-pa', fixture_path], cwd=work)
        after = original
        expect_ok = None
    elif operation == 'insert':
        base = fixtures_dir / 'webp-exif.webp'  # EXIF present, no IFD1 thumbnail; from-nothing case
        target = work / 'case.webp'
        shutil.copyfile(base, target)
        (work / 'case-thumb.jpg').write_bytes(jpegs.thumb_jpeg_bytes())
        rc, stdout, _ = shared.soft_run(report, [exe, '-i', 't', target], cwd=work)
        after, original, operative = target.read_bytes(), base.read_bytes(), 'webp-exif.webp'
        facts['thumbnailTagsAfter'] = after_tags(after)
        record_only = False
        expect_ok = ifd1_has_jpeg_thumbnail(after)
    elif operation == 'replace':
        target = work / 'case.webp'
        shutil.copyfile(fixture_path, target)
        (work / 'case-thumb.jpg').write_bytes(jpegs.thumb_jpeg_bytes())
        rc, stdout, _ = shared.soft_run(report, [exe, '-i', 't', target], cwd=work)
        after = target.read_bytes()
        facts['thumbnailTagsAfter'] = after_tags(after)
        expect_ok = None  # mixed-state result on uncompressed source is CLI-defined; record-only
    elif operation == 'remove':
        target = work / 'case.webp'
        shutil.copyfile(fixture_path, target)
        rc, stdout, _ = shared.soft_run(report, [exe, '-d', 't', target], cwd=work)
        after = target.read_bytes()
        facts['thumbnailTagsAfter'] = after_tags(after)
        record_only = False
        expect_ok = facts['thumbnailTagsAfter'] == []
    else:
        raise AssertionError(operation)
    # 'before' facts describe the actual input bytes (base may differ for from-nothing inserts)
    facts['thumbnailTagsBefore'] = sorted(tag for tag in thumbnail_tags(original) if tag in THUMB_TAG_IDS)
    facts['scanPreserved'] = scan_payloads(after) == scan_payloads(original)
    parse_ok = True
    try:
        webp_chunks(after)
    except Exception as error:
        parse_ok, facts['parseError'] = False, str(error)
    return rc, original, after, parse_ok, expect_ok, record_only, facts, b'', operative


def run_xmp(report, row, exe, fixture_path, work, fixtures_dir):
    original = fixture_path.read_bytes()
    operation, encoding = row['operation'], row['textEncoding']
    record_only = row['duplicates'] != 'none' or encoding == 'unknown-raw'
    facts = {}

    def spec_derived_value_present(target, token):
        rc2, out2, _ = shared.soft_run(report, [exe, '-pa', target], cwd=work)
        facts['stdoutCreatorToolAfter'] = out2.decode('utf-8', 'replace')[:400]
        return token in out2.decode('utf-8', 'replace')

    if operation == 'read':
        rc, stdout, _ = shared.soft_run(report, [exe, '-pa', fixture_path], cwd=work)
        after, operative = original, fixture_path.name
        expect_ok = None
        if not record_only:
            expect_ok = b'MetadataSharp' in stdout
    elif operation in ('insert', 'replace'):
        base_name = fixture_path.name
        operative = base_name
        if operation == 'insert' and row['duplicates'] == 'none':
            base_name, operative = 'webp-base.webp', 'webp-base.webp'  # from-nothing case
        base = fixtures_dir / base_name
        target = work / 'case.webp'
        shutil.copyfile(base, target)
        value = XMP_UTF8_VALUE if encoding == 'utf-8' else None
        if operation == 'replace' and encoding == 'utf-8':
            value = 'Bob'
        if value is not None:
            mod = f'set Xmp.xmp.CreatorTool XmpText {value}'
        else:
            mod = b'set Xmp.xmp.CreatorTool XmpText ' + XMP_RAW_VALUE
        rc, stdout, _ = shared.soft_run(report, [exe, '-M', mod, target], cwd=work)
        after, original = target.read_bytes(), base.read_bytes()
        expect_ok = None
        if not record_only and encoding == 'utf-8':
            token = 'MetadataSharp B3' if operation == 'insert' else 'Bob'
            expect_ok = spec_derived_value_present(target, token)
    elif operation == 'remove':
        base = fixture_path
        target = work / 'case.webp'
        shutil.copyfile(base, target)
        rc, stdout, _ = shared.soft_run(report, [exe, '-M', 'del Xmp.xmp.CreatorTool', target], cwd=work)
        after, original, operative = target.read_bytes(), fixture_path.read_bytes(), fixture_path.name
        expect_ok = None
        if not record_only and encoding == 'utf-8':
            rc2, out2, _ = shared.soft_run(report, [exe, '-pa', target], cwd=work)
            facts['stdoutCreatorToolAfter'] = out2.decode('utf-8', 'replace')[:400]
            expect_ok = b'MetadataSharp' not in out2
    else:
        raise AssertionError(operation)
    # 'before' facts describe the actual input bytes (base may differ for from-nothing inserts)
    packets = xmp_packets(original)
    facts['xmpPacketCount'] = len(packets)
    facts['xmpPacketSha256'] = [hashlib.sha256(p).hexdigest() for p in packets]
    facts['scanPreserved'] = scan_payloads(after) == scan_payloads(original)
    parse_ok = True
    try:
        webp_chunks(after)
    except Exception as error:
        parse_ok, facts['parseError'] = False, str(error)
    return rc, original, after, parse_ok, expect_ok, record_only, facts, b'', operative


RUNNERS = {'inventory': run_inventory, 'comments': run_claim_read, 'exif': run_exif,
           'icc': run_icc, 'iptc': run_claim_read, 'xmp': run_xmp, 'thumbnail': run_thumbnail}


def main():
    parser = argparse.ArgumentParser(description='B3 census observation batch (WebP)')
    parser.add_argument('--upstream', type=Path, required=True)
    parser.add_argument('--build', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/e01',
                        help='root directory; writes <output>/b3/observations.json')
    parser.add_argument('--census', type=Path, default=CENSUS)
    args = parser.parse_args()
    upstream, build, output_root = args.upstream.resolve(), args.build.resolve(), args.output.resolve()
    output = (output_root / 'b3').resolve()
    output.mkdir(parents=True, exist_ok=True)
    report = dict(schema=1, status='running', batch='B3',
                  sourceCommit=os.environ.get('E01_SOURCE_COMMIT'),
                  baseline=dict(version=VERSION, sourceCommit=PIN),
                  commands=[], observations=[], staticNotApplicable=[],
                  outcomeCounts={}, denominatorFrozen=False)

    def save():
        (output / 'observations.json').write_text(json.dumps(report, indent=2) + '\n')

    try:
        assert report['sourceCommit'] and re.fullmatch('[0-9a-f]{40}', report['sourceCommit']), \
            'Missing source identity (E01_SOURCE_COMMIT)'
        head = subprocess.run(['git', '-C', upstream, 'rev-parse', 'HEAD'],
                              capture_output=True, text=True, check=True).stdout.strip()
        assert head == PIN, 'Wrong upstream commit'
        dirty = subprocess.run(['git', '-C', upstream, 'status', '--porcelain', '--untracked-files=no'],
                               capture_output=True, text=True, check=True).stdout.strip()
        assert not dirty, 'Modified upstream source'
        cache = (build / 'CMakeCache.txt').read_bytes()
        report['baseline']['effectiveCmake'] = baseline.check_cache(cache.decode())
        report['baseline']['cmakeCacheSha256'] = baseline.digest(cache)
        report['baseline']['licenseSha256'] = baseline.digest((upstream / 'COPYING').read_bytes())
        exe = build / 'bin/exiv2'
        report['baseline']['executableSha256'] = baseline.digest(exe.read_bytes())
        version = subprocess.run([exe, '--version'], capture_output=True, text=True,
                                 check=True, env={**os.environ, 'LC_ALL': 'C'})
        baseline.check_version(version.stdout)
        report['commands'].append(dict(args=[str(exe), '--version'], exitCode=version.returncode,
                                       stdoutSha256=baseline.digest(version.stdout.encode()),
                                       stdout=version.stdout))
        fixtures_dir = output / 'fixtures'
        owned.generate(fixtures_dir)
        owned.verify_dir(fixtures_dir)
        report['fixtureManifest'] = owned.manifest()
        all_rows = json.loads(args.census.read_text())['rows']
        rows = [row for row in all_rows if row['batch'].lower() == 'b3']
        assert rows, 'No B3 rows enumerated in the census yet'
        report['denominatorFrozen'] = False
        for index, row in enumerate(rows):
            row_id = row['rowId']
            work = output / 'work' / f'{index:04d}'
            work.mkdir(parents=True, exist_ok=True)
            reason = static_not_applicable(row)
            if reason:
                report['staticNotApplicable'].append(dict(id=row_id, reason=reason))
                continue
            fixture_name = case_fixture(row['family'], row['layout'], row['duplicates'],
                                        row['operation'], row['textEncoding'])
            assert fixture_name is not None and (fixtures_dir / fixture_name).exists(), \
                f'No fixture for row {row_id}'
            fixture_path = fixtures_dir / fixture_name
            rc, before, after, parse_ok, expect_ok, record_only, facts, stdout, operative_fixture = \
                RUNNERS[row['family']](report, row, exe, fixture_path, work, fixtures_dir)
            if facts.get('scanPreserved') is False:
                outcome = 'scan-changed'
            else:
                outcome = shared.classify(rc, parse_ok, expect_ok, True,
                                          artifact_produced=bool(facts.get('extractedFiles')))
            report['observations'].append(dict(
                id=row_id,
                seedId=f"EX0289-{row['format']}-{row['family'].upper()}",
                operation=row['operation'],
                family=row['family'],
                layout=row['layout'],
                duplicates=row['duplicates'],
                textEncoding=row['textEncoding'],
                fixture=operative_fixture,
                fixtureSha256=baseline.digest(before),
                fixtureRights='owned-synthetic',
                inputSha256=baseline.digest(before),
                outputSha256=baseline.digest(after),
                recordOnly=record_only,
                expectOk=expect_ok,
                outcome=outcome,
                facts=facts,
            ))
            save()
        counts = Counter(item['outcome'] for item in report['observations'])
        counts['static-not-applicable'] = len(report['staticNotApplicable'])
        report['outcomeCounts'] = dict(sorted(counts.items()))
        report['status'] = 'passed'
        save()
        raw = json.dumps(report, separators=(',', ':')).encode()
        encoded = base64.b64encode(zlib.compress(raw)).decode()
        chunks = [encoded[i:i + 2500] for i in range(0, len(encoded), 2500)]
        for i, chunk in enumerate(chunks):
            print(f'::notice title=B3Z observations.json {i}/{len(chunks)}::{chunk}', flush=True)
    except Exception:
        import traceback
        report['status'] = 'failed'
        report['failure'] = traceback.format_exc()[-3500:]
        save()
        diagnostic = json.dumps(dict(error=report['failure']), separators=(',', ':'))
        diagnostic = diagnostic.replace('%', '%25').replace('\r', '%0D').replace('\n', '%0A')
        print(f'::error title=B3 census observation failure::' + diagnostic, flush=True)
        raise


if __name__ == '__main__':
    main()
