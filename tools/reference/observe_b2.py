"""E01 census observation batch B2: pinned Exiv2 CLI against owned synthetic PNGs.

B2 covers PNG x {inventory, comments (noDocumentedClaim read), exif
(noDocumentedClaim read), icc, iptc, thumbnail, xmp} per docs/research/
EXIV2_OPERATION_SEEDS.json (claim '-' cells materialize as one noDocumentedClaim
read row each; the CLI may reveal undocumented support, which is recorded as
observed behavior, never asserted).

Observation, not pass/fail: outcomes are recorded honestly (accepted /
accepted-unexpected / refused / scan-changed / accepted-unparseable /
nonzero-exit-artifact-produced) and only infrastructure failures stop the run.
The PNG analog of the JPEG scan invariant is the IDAT payload stream: metadata
mutations must never change the coded image data. PNG chunks additionally carry
CRC-32; a post-mutation CRC failure is recorded as accepted-unparseable (a
finding), not a crash.

Usage: observe_b2.py --upstream artifacts/e01/source --build artifacts/e01/build [--output artifacts/e01]
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
import png_owned_fixtures as owned  # noqa: E402

PIN = baseline.PIN
VERSION = baseline.VERSION
CENSUS = ROOT / 'docs/research/EXIV2_CENSUS.json'
from build_census import STATIC_NOT_APPLICABLE  # noqa: E402

PNG_SIG = b'\x89PNG\r\n\x1a\n'

THUMB_TAG_IDS = frozenset({0x0100, 0x0101, 0x0103, 0x0111, 0x0112, 0x0115, 0x0116, 0x0117, 0x0201, 0x0202})


def png_chunks(data):
    """Strict structural parse + CRC-32 verification of every chunk."""
    if data[:8] != PNG_SIG:
        raise ValueError('Missing PNG signature')
    chunks, pos, crc_ok = [], 8, True
    end = None
    while pos + 8 <= len(data):
        length = int.from_bytes(data[pos:pos + 4], 'big')
        ctype = data[pos + 4:pos + 8]
        start = pos + 8
        stop = start + length
        if stop + 4 > len(data):
            raise ValueError(f'Chunk {ctype.decode("latin1")} overruns the file')
        payload = data[start:stop]
        stored_crc = int.from_bytes(data[stop:stop + 4], 'big')
        actual_crc = zlib.crc32(ctype + payload) & 0xFFFFFFFF
        if stored_crc != actual_crc:
            crc_ok = False
        chunks.append((ctype, payload))
        pos = stop + 4
        if ctype == b'IEND':
            end = pos
            break
    if end is None:
        raise ValueError('Missing IEND chunk')
    return chunks, crc_ok


def idat_payloads(data):
    chunks, _ = png_chunks(data)
    return b''.join(payload for ctype, payload in chunks if ctype == b'IDAT')


def text_chunks(data):
    chunks, _ = png_chunks(data)
    out = []
    for ctype, payload in chunks:
        if ctype == b'tEXt':
            keyword, _, text = payload.partition(b'\x00')
            out.append(('tEXt', keyword.decode('latin1', 'replace'), text))
        elif ctype == b'zTXt':
            keyword, _, rest = payload.partition(b'\x00')
            out.append(('zTXt', keyword.decode('latin1', 'replace'), zlib.decompress(rest[1:]) if rest else b''))
        elif ctype == b'iTXt':
            keyword, _, rest = payload.partition(b'\x00')
            flag = rest[0:1]
            body = rest[2:]
            language, _, rest2 = body.partition(b'\x00')
            translated, _, text = rest2.partition(b'\x00')
            if flag == b'\x01':
                try:
                    text = zlib.decompress(text)
                except zlib.error:
                    pass
            out.append(('iTXt', keyword.decode('latin1', 'replace'), text))
    return out


def xmp_packets(data):
    """XMP payloads carried by iTXt/zTXt 'XML:com.adobe.xmp' chunks."""
    packets = []
    for ctype, payload in png_chunks(data)[0]:
        if ctype == b'iTXt':
            keyword, _, rest = payload.partition(b'\x00')
            if keyword != b'XML:com.adobe.xmp':
                continue
            flag, method = rest[0], rest[1]
            body = rest[2:]
            language, _, translated = body.partition(b'\x00')
            _, _, text = translated.partition(b'\x00')
            packets.append(zlib.decompress(text) if flag == 1 else text)
        elif ctype == b'zTXt':
            keyword, _, rest = payload.partition(b'\x00')
            if keyword != b'XML:com.adobe.xmp':
                continue
            packets.append(zlib.decompress(rest[1:]))
    return packets


def exif_blocks(data):
    """Classic II TIFF blocks from eXIf chunks (PNG eXIf carries no APP1 prefix)."""
    blocks = []
    for ctype, payload in png_chunks(data)[0]:
        if ctype == b'eXIf':
            blocks.append(jpegs.parse_tiff(payload))
    return blocks


def icc_profiles(data):
    """Decompressed profiles from iCCP chunks (method byte 0 + zlib payload)."""
    profiles = []
    for ctype, payload in png_chunks(data)[0]:
        if ctype != b'iCCP':
            continue
        _, _, rest = payload.partition(b'\x00')
        profiles.append(zlib.decompress(rest[1:]))  # method byte skipped
    return profiles


def raw_profile_text(data, kind):
    """First 'Raw profile type <kind>' payload text (zTXt/iTXt), else None."""
    for ctype, payload in png_chunks(data)[0]:
        keyword_match = False
        if ctype == b'zTXt':
            keyword, _, rest = payload.partition(b'\x00')
            if keyword == f'Raw profile type {kind}'.encode():
                return zlib.decompress(rest[1:])
        elif ctype == b'iTXt':
            keyword, _, rest = payload.partition(b'\x00')
            if keyword == f'Raw profile type {kind}'.encode():
                body = rest[2:]
                _, _, translated = body.partition(b'\x00')
                _, _, text = translated.partition(b'\x00')
                return text if rest[0] == 0 else zlib.decompress(text)
    return None


def raw_profile_keywords(data, kind):
    """IIM Application2 keywords inside an Exiv2 'Raw profile type <kind>' text."""
    raw = raw_profile_text(data, kind)
    if raw is None:
        return None
    # raw-profile text: '\niptc\n     <byte-length>\n' followed by hex that may be
    # wrapped across lines (the CLI re-wraps on write); strip all non-hex bytes.
    match = re.match(rb'\s*\niptc\n\s*\d+\s*\n(.*)$', raw, re.S)
    if not match:
        return []
    hexstr = re.sub(rb'[^0-9a-fA-F]', b'', match.group(1))
    body = bytes.fromhex(hexstr.decode())
    keywords = []
    for resource_id, block in shared.bim_blocks(body):
        if resource_id != 0x0404:
            continue
        j = 0
        while j + 5 <= len(block):
            if block[j] != 0x1C:
                j += 1
                continue
            record, dataset = block[j + 1], block[j + 2]
            size = int.from_bytes(block[j + 3:j + 5], 'big'); j += 5
            if size & 0x8000:
                j += 2
                size &= 0x7FFF
            value = block[j:j + size]; j += size
            if record == 2 and dataset == 25:
                keywords.append(value.decode('ascii', 'backslashreplace'))
    return keywords


def thumbnail_tags(data):
    tags = {}
    for ifds in exif_blocks(data):
        for ifd in ifds[1:]:
            for tag, values in ifd['tags'].items():
                tags.setdefault(tag, []).extend(values)
    return tags


def case_fixture(family, layout, duplicates, operation=None, text_encoding=None):
    if family == 'inventory':
        return 'png-base.png'
    if family == 'comments':
        return 'png-text.png'
    if family == 'exif':
        return 'png-exif.png'
    if family == 'icc':
        return 'png-iccp-dup.png' if duplicates == 'first-wins' else 'png-iccp.png'
    if family == 'iptc':
        prefix = 'png-iptc-itxt' if layout == 'itxt-raw-profile-iptc' else 'png-iptc'
        return f'{prefix}-dup.png' if duplicates == 'first-wins' else f'{prefix}.png'
    if family == 'xmp':
        table = {
            ('itxt-xmp', 'none', 'utf-8'): 'png-xmp.png',
            ('itxt-xmp', 'none', 'unknown-raw'): 'png-xmp-nonutf8.png',
            ('itxt-xmp', 'first-wins', 'utf-8'): 'png-xmp-dup.png',
            ('itxt-xmp', 'first-wins', 'unknown-raw'): 'png-xmp-nonutf8.png',
            ('ztxt-xmp', 'none', 'utf-8'): 'png-xmp-ztxt.png',
            ('ztxt-xmp', 'none', 'unknown-raw'): 'png-xmp-ztxt-nonutf8.png',
            ('ztxt-xmp', 'first-wins', 'utf-8'): 'png-xmp-ztxt-dup.png',
            ('ztxt-xmp', 'first-wins', 'unknown-raw'): 'png-xmp-ztxt-nonutf8.png',
        }
        return table.get((layout, duplicates, text_encoding))
    if family == 'thumbnail':
        return 'png-exif-thumb-jpeg.png' if layout == 'exif-ifd1-jpeg' else 'png-exif-thumb-raw.png'
    return None


def static_not_applicable(row):
    for family, layout, duplicates, operation, reason in STATIC_NOT_APPLICABLE:
        if (row['family'] == family and row['layout'] == layout
                and (duplicates is None or row['duplicates'] == duplicates)
                and (operation is None or row['operation'] == operation)):
            return reason
    return None


TEXT_PAYLOADS = {
    'ascii': b'MetadataSharp B2 insert',
    'utf-8': 'MetadataSharp B2 insert é ✓'.encode('utf-8'),
    'unknown-raw': b'\x89RAW\xff\xfe',  # no NUL: execve forbids embedded NUL in argv
}

XMP_UTF8_VALUE = 'MetadataSharp B2 é ✓'
XMP_RAW_VALUE = b'\x89RAW\xff\xfe'


def run_inventory(report, row, exe, fixture_path, work, fixtures_dir):
    original = fixture_path.read_bytes()
    rc, _, _ = shared.soft_run(report, [exe, '-pS', fixture_path], cwd=work)
    return rc, original, original, True, None, False, {'idatPreserved': True}, b'', fixture_path.name


def run_claim_read(report, row, exe, fixture_path, work, fixtures_dir):
    """noDocumentedClaim read rows (PNG comments/EXIF, claim '-'): record what the
    pinned CLI does; the cell carries no documented claim, so nothing is pre-asserted."""
    original = fixture_path.read_bytes()
    family = row['family']
    facts = {}
    facts['textChunkKeywords'] = [kw for _, kw, _ in text_chunks(original)]
    if family == 'comments':
        rc, stdout, _ = shared.soft_run(report, [exe, '-pc', fixture_path], cwd=work)
    else:  # exif
        rc, stdout, _ = shared.soft_run(report, [exe, '-pa', fixture_path], cwd=work)
    facts['stdoutSha256'] = hashlib.sha256(stdout).hexdigest()
    parse_ok = True
    try:
        png_chunks(original)
    except Exception as error:
        parse_ok, facts['parseError'] = False, str(error)
    return rc, original, original, parse_ok, None, False, facts, stdout, fixture_path.name


def run_icc(report, row, exe, fixture_path, work, fixtures_dir):
    original = fixture_path.read_bytes()
    operation = row['operation']
    record_only = row['duplicates'] != 'none'
    facts = {}
    source_dir = work / 'icc-src'
    source_dir.mkdir(exist_ok=True)
    (source_dir / 'case.icc').write_bytes(jpegs.PROFILE_A)  # exiv2 -iC -l dir reads case.icc
    if operation == 'read':
        rc, stdout, _ = shared.soft_run(report, [exe, '-pC', fixture_path], cwd=work)
        after, expect_ok = original, None
        facts['stdoutEqualsProfileA'] = bytes(stdout) == jpegs.PROFILE_A
    elif operation in ('insert', 'replace'):
        target = work / 'case.png'
        shutil.copyfile(fixture_path, target)
        rc, stdout, _ = shared.soft_run(report, [exe, '-iC', '-l', source_dir, target], cwd=work)
        after = target.read_bytes()
        profiles = icc_profiles(after)
        facts['iccProfileCount'] = len(profiles)
        facts['iccProfileEqualsA'] = jpegs.PROFILE_A in profiles
        expect_ok = None if record_only else (jpegs.PROFILE_A in profiles)
    elif operation == 'remove':
        target = work / 'case.png'
        shutil.copyfile(fixture_path, target)
        rc, stdout, _ = shared.soft_run(report, [exe, '-d', 'C', target], cwd=work)
        after = target.read_bytes()
        profiles = icc_profiles(after)
        facts['iccProfileCountAfter'] = len(profiles)
        expect_ok = None if record_only else len(profiles) == 0
    else:
        raise AssertionError(operation)
    facts['idatPreserved'] = idat_payloads(after) == idat_payloads(original)
    parse_ok = True
    try:
        png_chunks(after)
    except Exception as error:
        parse_ok, facts['parseError'] = False, str(error)
    return rc, original, after, parse_ok, expect_ok, record_only, facts, b'', fixture_path.name


def run_iptc(report, row, exe, fixture_path, work, fixtures_dir):
    original = fixture_path.read_bytes()
    operation = row['operation']
    carrier = row['layout']  # ztxt-raw-profile-iptc | itxt-raw-profile-iptc
    record_only = row['duplicates'] != 'none' or carrier == 'itxt-raw-profile-iptc'
    facts = {}

    def keywords_after(target):
        return raw_profile_keywords(target.read_bytes(), 'iptc')

    operative = fixture_path.name
    if operation == 'read':
        rc, stdout, _ = shared.soft_run(report, [exe, '-pi', fixture_path], cwd=work)
        after = original
        expect_ok = None
        if not record_only:
            expect_ok = b'MetadataSharpAlpha' in stdout and b'MetadataSharpBeta' in stdout
    elif operation == 'insert':
        base_name = fixture_path.name
        if row['duplicates'] == 'none':
            base_name = 'png-base.png'  # from-nothing case
        operative = base_name
        base = fixtures_dir / base_name
        target = work / 'case.png'
        shutil.copyfile(base, target)
        rc, stdout, _ = shared.soft_run(report, [exe, '-M', 'add Iptc.Application2.Keywords String MetadataSharpAdded', target], cwd=work)
        after, original = target.read_bytes(), base.read_bytes()
        facts['keywordsAfter'] = keywords_after(target)
        expect_ok = None if record_only else ('MetadataSharpAdded' in (facts['keywordsAfter'] or []))
    elif operation == 'replace':
        target = work / 'case.png'
        shutil.copyfile(fixture_path, target)
        rc, stdout, _ = shared.soft_run(report, [exe, '-M', 'set Iptc.Application2.Keywords String MetadataSharpReplacement', target], cwd=work)
        after = target.read_bytes()
        facts['keywordsAfter'] = keywords_after(target)
        record_only = True  # repeated-dataset 'set' semantics are CLI-defined, never pre-asserted
        expect_ok = None
    elif operation == 'remove':
        target = work / 'case.png'
        shutil.copyfile(fixture_path, target)
        rc, stdout, _ = shared.soft_run(report, [exe, '-M', 'del Iptc.Application2.Keywords', target], cwd=work)
        after = target.read_bytes()
        facts['keywordsAfter'] = keywords_after(target)
        expect_ok = None if record_only else (not facts['keywordsAfter'])
    else:
        raise AssertionError(operation)
    # 'before' facts describe the actual input bytes (base may differ for from-nothing inserts)
    facts['keywordsBefore'] = raw_profile_keywords(original, 'iptc')
    facts['rawProfilePresent'] = raw_profile_text(original, 'iptc') is not None
    facts['idatPreserved'] = idat_payloads(after) == idat_payloads(original)
    parse_ok = True
    try:
        png_chunks(after)
    except Exception as error:
        parse_ok, facts['parseError'] = False, str(error)
    return rc, original, after, parse_ok, expect_ok, record_only, facts, b'', operative


def run_thumbnail(report, row, exe, fixture_path, work, fixtures_dir):
    original = fixture_path.read_bytes()
    operation = row['operation']
    facts = {}

    def ifd1_has_jpeg_thumbnail(data):
        tags = thumbnail_tags(data)
        return 0x0201 in tags and 0x0202 in tags

    def after_tags(target):
        return sorted(tag for tag in thumbnail_tags(target.read_bytes()) if tag in THUMB_TAG_IDS)

    record_only = True
    operative = fixture_path.name
    if operation == 'read':
        rc, stdout, _ = shared.soft_run(report, [exe, '-pa', fixture_path], cwd=work)
        after = original
        expect_ok = None
    elif operation == 'insert':
        base = fixtures_dir / 'png-exif.png'  # EXIF present, no IFD1 thumbnail; from-nothing case
        target = work / 'case.png'
        shutil.copyfile(base, target)
        (work / 'case-thumb.jpg').write_bytes(jpegs.thumb_jpeg_bytes())
        rc, stdout, _ = shared.soft_run(report, [exe, '-i', 't', target], cwd=work)
        after, original, operative = target.read_bytes(), base.read_bytes(), 'png-exif.png'
        facts['thumbnailTagsAfter'] = after_tags(target)
        record_only = False
        expect_ok = ifd1_has_jpeg_thumbnail(after)
    elif operation == 'replace':
        target = work / 'case.png'
        shutil.copyfile(fixture_path, target)
        (work / 'case-thumb.jpg').write_bytes(jpegs.thumb_jpeg_bytes())
        rc, stdout, _ = shared.soft_run(report, [exe, '-i', 't', target], cwd=work)
        after = target.read_bytes()
        facts['thumbnailTagsAfter'] = after_tags(target)
        expect_ok = None  # mixed-state result on uncompressed source is CLI-defined; record-only
    elif operation == 'remove':
        target = work / 'case.png'
        shutil.copyfile(fixture_path, target)
        rc, stdout, _ = shared.soft_run(report, [exe, '-d', 't', target], cwd=work)
        after = target.read_bytes()
        facts['thumbnailTagsAfter'] = after_tags(target)
        record_only = False
        expect_ok = facts['thumbnailTagsAfter'] == []
    else:
        raise AssertionError(operation)
    # 'before' facts describe the actual input bytes (base may differ for from-nothing inserts)
    facts['thumbnailTagsBefore'] = sorted(tag for tag in thumbnail_tags(original) if tag in THUMB_TAG_IDS)
    facts['idatPreserved'] = idat_payloads(after) == idat_payloads(original)
    parse_ok = True
    try:
        png_chunks(after)
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
            base_name, operative = 'png-base.png', 'png-base.png'  # from-nothing case
        base = fixtures_dir / base_name
        target = work / 'case.png'
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
            token = 'MetadataSharp B2' if operation == 'insert' else 'Bob'
            expect_ok = spec_derived_value_present(target, token)
    elif operation == 'remove':
        base = fixture_path
        target = work / 'case.png'
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
    facts['idatPreserved'] = idat_payloads(after) == idat_payloads(original)
    parse_ok = True
    try:
        png_chunks(after)
    except Exception as error:
        parse_ok, facts['parseError'] = False, str(error)
    return rc, original, after, parse_ok, expect_ok, record_only, facts, b'', operative


RUNNERS = {'inventory': run_inventory, 'comments': run_claim_read, 'exif': run_claim_read,
           'icc': run_icc, 'iptc': run_iptc, 'xmp': run_xmp, 'thumbnail': run_thumbnail}


def main():
    parser = argparse.ArgumentParser(description='B2 census observation batch (PNG)')
    parser.add_argument('--upstream', type=Path, required=True)
    parser.add_argument('--build', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/e01',
                        help='root directory; writes <output>/b2/observations.json')
    parser.add_argument('--census', type=Path, default=CENSUS)
    args = parser.parse_args()
    upstream, build, output_root = args.upstream.resolve(), args.build.resolve(), args.output.resolve()
    output = (output_root / 'b2').resolve()
    output.mkdir(parents=True, exist_ok=True)
    report = dict(schema=1, status='running', batch='B2',
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
        rows = [row for row in all_rows if row['batch'].lower() == 'b2']
        assert rows, 'No B2 rows enumerated in the census yet'
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
            if facts.get('idatPreserved') is False:
                outcome = 'scan-changed'
            else:
                outcome = shared.classify(rc, parse_ok, expect_ok, True,
                                          artifact_produced=bool(facts.get('extractedFiles')))
            report['observations'].append(dict(
                id=row_id,
                seedId=f"EX0289-{row['format']}-{'COMMENTS' if row['family'] == 'comments' else row['family'].upper()}",
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
            print(f'::notice title=B2Z observations.json {i}/{len(chunks)}::{chunk}', flush=True)
    except Exception:
        import traceback
        report['status'] = 'failed'
        report['failure'] = traceback.format_exc()[-3500:]
        save()
        diagnostic = json.dumps(dict(error=report['failure']), separators=(',', ':'))
        diagnostic = diagnostic.replace('%', '%25').replace('\r', '%0D').replace('\n', '%0A')
        print(f'::error title=B2 census observation failure::' + diagnostic, flush=True)
        raise


if __name__ == '__main__':
    main()
