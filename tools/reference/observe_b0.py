"""E01 census observation batches: pinned Exiv2 CLI against owned synthetic JPEGs.

B0 covers JPEG x {exif, comments, icc} + inventory; B1 adds JPEG x {xmp, iptc, thumbnail}.

Records what the pinned 0.28.9 black-box CLI actually does with the B0
fixture axes (layout x duplicates x operation x encoding) for every row in
docs/research/EXIV2_CENSUS.json. Observation, not pass/fail: outcomes are
recorded honestly (accepted / accepted-unexpected / refused / scan-changed /
accepted-unparseable) and only infrastructure failures stop the run. The
E01-validated exif-base.jpg Artist cases remain CI-hard regression anchors via
tools/reference/exiv2_baseline.py; this batch deliberately has no pre-asserted
behavior beyond the spec-derived expectations below and the scan-bytes invariant.

Usage: observe_b0.py --upstream artifacts/e01/source --build artifacts/e01/build [--output artifacts/e01/b0]
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
import jpeg_owned_fixtures as owned  # noqa: E402

PIN = baseline.PIN
VERSION = baseline.VERSION
CENSUS = ROOT / 'docs/research/EXIV2_CENSUS.json'
from build_census import STATIC_NOT_APPLICABLE  # noqa: E402



def jpeg_segments(data):
    pos, segments = 2, []
    if data[:2] != b'\xff\xd8':
        raise ValueError('Missing SOI')
    while pos < len(data):
        start = pos
        marker = data[pos + 1]
        if marker in (0xd8, 0xd9, 0xda):
            size = None if marker == 0xda else 2
            if marker == 0xda:
                segments.append((marker, data[start:]))
                return segments
            pos += 2
            continue
        size = int.from_bytes(data[pos + 2:pos + 4], 'big')
        segments.append((marker, data[start + 4:pos + 2 + size]))
        pos += 2 + size
    return segments


def scan_bytes(data):
    segments = jpeg_segments(data)
    scan = [payload for marker, payload in segments if marker == 0xda]
    return scan[0] if scan else None


def comment_payloads(data):
    return [payload for marker, payload in jpeg_segments(data) if marker == 0xFE]


def icc_chunks(data):
    profiles = []
    current = None
    for marker, payload in jpeg_segments(data):
        if marker != 0xE2 or not payload.startswith(owned.ICC_SIGNATURE):
            continue
        seq, count = payload[len(owned.ICC_SIGNATURE)], payload[len(owned.ICC_SIGNATURE) + 1]
        chunk = payload[len(owned.ICC_SIGNATURE) + 2:]
        if current is None or seq == 1:
            if current is not None:
                profiles.append(current)
            current = {'seq_count': count, 'chunks': {}}
        current['chunks'][seq] = chunk
    if current is not None:
        profiles.append(current)
    complete = []
    for prof in profiles:
        if sorted(prof['chunks']) == list(range(1, prof['seq_count'] + 1)):
            complete.append(b''.join(prof['chunks'][i] for i in range(1, prof['seq_count'] + 1)))
    return complete


def exif_blocks(data):
    blocks = []
    for marker, payload in jpeg_segments(data):
        if marker == 0xE1 and payload.startswith(b'Exif\0\0'):
            blocks.append(owned.parse_tiff(payload[6:]))
    return blocks


def case_fixture(family, layout, duplicates, operation=None, text_encoding=None):
    if family == 'inventory':
        return 'exif-base.jpg'
    if family == 'comments' and operation == 'read' and text_encoding == 'unknown-raw':
        # B0 refresh: unknown-raw READ rows observe genuinely non-UTF-8 COM payloads
        table = {
            ('com-single', 'none'): 'com-raw.jpg',
            ('com-multiple', 'none'): 'com-raw-multiple.jpg',
            ('com-multiple', 'first-wins'): 'com-raw-multiple-dup.jpg',
            ('com-multiple', 'multiple-independent'): 'com-raw-multiple.jpg',
        }
        return table.get((layout, duplicates))
    if family in ('xmp',) and operation == 'read' and text_encoding == 'unknown-raw':
        table = {
            ('app1-xmp-standard', 'none'): 'xmp-nonutf8.jpg',
            ('app1-xmp-standard', 'first-wins'): 'xmp-nonutf8.jpg',
            ('app1-xmp-extended', 'none'): 'xmp-extended-nonutf8.jpg',
            ('app1-xmp-extended', 'first-wins'): 'xmp-extended-nonutf8.jpg',
        }
        return table.get((layout, duplicates))
    key = (family, layout, duplicates)
    table = {
        ('exif', 'app1-exif-ifd0', 'none'): 'exif-base.jpg',
        ('exif', 'app1-exif-ifd0', 'first-wins'): 'exif-dup-tag.jpg',
        ('exif', 'app1-exif-ifd0', 'multiple-independent'): 'exif-multi-app1.jpg',
        ('exif', 'app1-exif-ifd0-subifd', 'none'): 'exif-subifd.jpg',
        ('exif', 'app1-exif-ifd0-subifd', 'first-wins'): 'exif-subifd-dup-tag.jpg',
        ('exif', 'app1-exif-ifd0-subifd', 'multiple-independent'): 'exif-subifd-multi.jpg',
        ('comments', 'com-single', 'none'): 'com-single.jpg',
        ('comments', 'com-multiple', 'none'): 'com-multiple.jpg',
        ('comments', 'com-multiple', 'first-wins'): 'com-multiple-dup.jpg',
        ('comments', 'com-multiple', 'multiple-independent'): 'com-multiple.jpg',
        ('icc', 'app2-icc-single', 'none'): 'icc-single.jpg',
        ('icc', 'app2-icc-single', 'first-wins'): 'icc-single-dup.jpg',
        ('icc', 'app2-icc-segmented', 'none'): 'icc-segmented.jpg',
        ('icc', 'app2-icc-segmented', 'first-wins'): 'icc-segmented-dup.jpg',
        ('xmp', 'app1-xmp-standard', 'none'): 'xmp-standard.jpg',
        ('xmp', 'app1-xmp-standard', 'first-wins'): 'xmp-duplicate.jpg',
        ('xmp', 'app1-xmp-extended', 'none'): 'xmp-extended.jpg',
        ('xmp', 'app1-xmp-extended', 'first-wins'): 'xmp-extended-duplicate.jpg',
        ('iptc', 'app13-8bim-iptc', 'none'): 'iptc-single.jpg',
        ('iptc', 'app13-8bim-iptc', 'first-wins'): 'iptc-single-dup-dataset.jpg',
        ('iptc', 'app13-8bim-iptc-multiple', 'none'): 'iptc-multiple.jpg',
        ('iptc', 'app13-8bim-iptc-multiple', 'first-wins'): 'iptc-multiple-dup.jpg',
        ('thumbnail', 'exif-ifd1-jpeg', 'none'): 'exif-thumb-jpeg.jpg',
        ('thumbnail', 'exif-ifd1-uncompressed', 'none'): 'exif-thumb-raw.jpg',
    }
    return table.get(key)


def static_not_applicable(row):
    family, layout, duplicates, operation = row['family'], row['layout'], row['duplicates'], row['operation']
    for fam, lay, dups, op, reason in STATIC_NOT_APPLICABLE:
        if (fam == family and lay == layout and (dups is None or duplicates == dups)
                and (op is None or operation == op)):
            return reason
    return None


TEXT_PAYLOADS = {
    'ascii': b'MetadataSharp B0 insert',
    'utf-8': 'MetadataSharp B0 insert é ✓'.encode('utf-8'),
    'unknown-raw': b'\x89RAW\xff\xfe',  # no NUL: execve forbids embedded NUL in argv
}


def soft_run(report, command, cwd):
    command = [c if isinstance(c, bytes) else str(c) for c in command]
    result = subprocess.run(command, cwd=cwd, capture_output=True, timeout=180,
                            env={**os.environ, 'LC_ALL': 'C', 'LANG': 'C'})
    stdout, stderr = result.stdout, result.stderr
    json_args = [a.decode('utf-8', 'backslashreplace') if isinstance(a, bytes) else a for a in command]
    report['commands'].append(dict(args=json_args, exitCode=result.returncode,
                                   stdoutSha256=hashlib.sha256(stdout).hexdigest(),
                                   stdout=stdout[:4000].decode('utf-8', 'replace'),
                                   stderr=stderr[:1000].decode('utf-8', 'replace')))
    return result.returncode, stdout, stderr


def run_exif(report, row, exe, fixture_path, work, fixtures_dir):
    original = fixture_path.read_bytes()
    scan_before = scan_bytes(original)
    operation = row['operation']
    record_only = row['duplicates'] != 'none'
    facts = {}
    if operation == 'read':
        rc, stdout, _ = soft_run(report, [exe, '-P', 'Ekycv', '-K', 'Exif.Image.Artist', fixture_path], cwd=work)
        after = original
    else:
        target = work / 'case.jpg'
        shutil.copyfile(fixture_path, target)
        modification = {'insert': 'set Exif.Image.ImageDescription Ascii MetadataSharp',
                        'replace': 'set Exif.Image.Artist Ascii Bob',
                        'remove': 'del Exif.Image.Artist'}[operation]
        rc, stdout, _ = soft_run(report, [exe, '-M', modification, target], cwd=work)
        after = target.read_bytes()
    facts['scanPreserved'] = scan_bytes(after) == scan_bytes(original)
    parse_ok, blocks = True, None
    try:
        blocks = exif_blocks(after)
    except Exception as error:  # record-only; parse failure is a finding, never a crash
        parse_ok, facts['parseError'] = False, str(error)
    expect_ok = None
    if rc == 0 and blocks:
        ifd0 = blocks[0][0]['tags']
        if operation == 'read':
            if not record_only:
                vals = ifd0.get(315) or []
                expect_ok = len(vals) == 1 and vals[0][2].rstrip(b'\0') == b'Alice'
        elif operation == 'insert':
            if not record_only:
                vals = ifd0.get(270) or []
                expect_ok = len(vals) == 1 and vals[0][2] == b'MetadataSharp\0'
        elif operation == 'replace':
            if not record_only:
                vals = ifd0.get(315) or []
                expect_ok = len(vals) == 1 and vals[0][2] == b'Bob\0'
        elif operation == 'remove':
            if not record_only:
                expect_ok = 315 not in ifd0
    elif rc == 0 and not blocks:
        expect_ok = False
    return rc, original, after, parse_ok, expect_ok, record_only, facts, stdout, fixture_path.name


def run_comments(report, row, exe, fixture_path, work, fixtures_dir):
    original = fixture_path.read_bytes()
    operative = fixture_path.name
    operation = row['operation']
    record_only = row['layout'] == 'com-multiple'  # which COM an op touches is CLI-defined
    facts = {}
    if operation == 'read':
        rc, stdout, _ = soft_run(report, [exe, '-pC', fixture_path], cwd=work)
        after, expect_ok = original, None
        facts['commentPayloadHex'] = [p.hex() for p in comment_payloads(original)]
    elif operation in ('insert', 'replace'):
        base = 'com-none.jpg' if operation == 'insert' else fixture_path.name
        operative = base
        target = work / 'case.jpg'
        shutil.copyfile(fixtures_dir / base, target)
        payload = TEXT_PAYLOADS[row['textEncoding']]
        rc, stdout, _ = soft_run(report, [exe, b'-c', payload, target], cwd=work)
        after = target.read_bytes()
        original = (fixtures_dir / base).read_bytes()
        coms = comment_payloads(after)
        facts['commentCount'] = len(coms)
        facts['commentEqualsPayload'] = payload in coms
        facts['commentEqualsPayloadMinusTrailingNul'] = [c.rstrip(b'\0') == payload for c in coms]
        facts['commentPayloadHex'] = [c.hex() for c in coms]
        # exiv2 rewrites the comment with a charset prefix/trailing NUL (known D001); round-trip
        # bytes are recorded, never pre-asserted: encoding behavior is an observation, not a gate.
        expect_ok = None
    elif operation == 'remove':
        target = work / 'case.jpg'
        shutil.copyfile(fixture_path, target)
        rc, stdout, _ = soft_run(report, [exe, '-d', 'c', target], cwd=work)
        after = target.read_bytes()
        coms = comment_payloads(after)
        facts['commentCountAfter'] = len(coms)
        expect_ok = None if record_only else len(coms) == 0
    elif operation == 'extract':
        target = work / 'case.jpg'
        shutil.copyfile(fixture_path, target)
        outdir = work / 'extract-out'
        outdir.mkdir()
        rc, stdout, _ = soft_run(report, [exe, '-e', 'c', '-l', outdir, target], cwd=work)
        after = target.read_bytes()
        produced = sorted(outdir.iterdir()) if outdir.is_dir() else []
        facts['extractedFiles'] = [p.name for p in produced]
        facts['extractedSha256'] = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in produced}
        expect_ok = None
    else:
        raise AssertionError(operation)
    facts['scanPreserved'] = scan_bytes(after) == scan_bytes(original)
    parse_ok = True
    try:
        jpeg_segments(after)
    except Exception as error:
        parse_ok, facts['parseError'] = False, str(error)
    return rc, original, after, parse_ok, expect_ok, record_only, facts, stdout, operative


def run_icc(report, row, exe, fixture_path, work, fixtures_dir):
    original = fixture_path.read_bytes()
    scan_before = scan_bytes(original)
    operation = row['operation']
    record_only = row['duplicates'] != 'none'
    facts = {}
    source_dir = work / 'icc-src'
    source_dir.mkdir(exist_ok=True)
    (source_dir / 'case.icc').write_bytes(owned.PROFILE_A)  # exiv2 -iC -l dir case.jpg reads case.icc
    if operation == 'read':
        rc, stdout, _ = soft_run(report, [exe, '-pC', fixture_path], cwd=work)
        after, expect_ok = original, None
    elif operation in ('insert', 'replace'):
        target = work / 'case.jpg'
        shutil.copyfile(fixture_path, target)
        rc, stdout, _ = soft_run(report, [exe, '-iC', '-l', source_dir, target], cwd=work)
        after = target.read_bytes()
        profiles = icc_chunks(after)
        facts['iccProfileCount'] = len(profiles)
        facts['iccProfileEqualsA'] = owned.PROFILE_A in profiles
        expect_ok = None if record_only else owned.PROFILE_A in profiles
    elif operation == 'remove':
        target = work / 'case.jpg'
        shutil.copyfile(fixture_path, target)
        rc, stdout, _ = soft_run(report, [exe, '-d', 'C', target], cwd=work)
        after = target.read_bytes()
        profiles = icc_chunks(after)
        facts['iccProfileCountAfter'] = len(profiles)
        expect_ok = None if record_only else len(profiles) == 0
    elif operation == 'extract':
        target = work / 'case.jpg'
        shutil.copyfile(fixture_path, target)
        outdir = work / 'icc-out'
        outdir.mkdir()
        rc, stdout, _ = soft_run(report, [exe, '-eC', '-l', outdir, target], cwd=work)
        after = target.read_bytes()
        produced = sorted(outdir.iterdir()) if outdir.is_dir() else []
        facts['extractedFiles'] = [p.name for p in produced]
        expect_ok = None
        if not record_only and produced:
            data = produced[0].read_bytes()
            facts['extractedSha256'] = hashlib.sha256(data).hexdigest()
            facts['extractedEqualsA'] = data == owned.PROFILE_A
            expect_ok = data == owned.PROFILE_A
    else:
        raise AssertionError(operation)
    facts['scanPreserved'] = scan_bytes(after) == scan_bytes(original)
    parse_ok = True
    try:
        jpeg_segments(after)
    except Exception as error:
        parse_ok, facts['parseError'] = False, str(error)
    return rc, original, after, parse_ok, expect_ok, record_only, facts, stdout, fixture_path.name


def run_inventory(report, row, exe, fixture_path, work, fixtures_dir):
    original = fixture_path.read_bytes()
    rc, stdout, _ = soft_run(report, [exe, '-pS', fixture_path], cwd=work)
    return rc, original, original, True, None, False, {'scanPreserved': True}, stdout, fixture_path.name

XMP_UTF8_VALUE = 'MetadataSharp B1 é ✓'
XMP_RAW_VALUE = b'\x89RAW\xff\xfe'
IPTC_ADDED = b'MetadataSharpAdded'

THUMB_TAG_IDS = frozenset({0x0100, 0x0101, 0x0103, 0x0111, 0x0112, 0x0115, 0x0116, 0x0117, 0x0201, 0x0202})
APP13_SIGNATURE = b'Photoshop 3.0\x00'


def bim_blocks(body):
    """Walk 8BIM blocks inside one APP13 payload (after the Photoshop signature)."""
    blocks, i = [], 0
    while i + 4 <= len(body):
        if body[i:i + 4] != b'8BIM':
            i += 1
            continue
        start = i
        i += 4
        resource_id = int.from_bytes(body[i:i + 2], 'big'); i += 2
        pascal_len = body[i] + 1
        if pascal_len % 2:
            pascal_len += 1
        i += pascal_len
        size = int.from_bytes(body[i:i + 4], 'big'); i += 4
        data = body[i:i + size]; i += size
        if (i - start) % 2:
            i += 1
        blocks.append((resource_id, data))
    return blocks


def iptc_keywords(data):
    """All IIM 2:25 keyword datasets, across APP13 segments (in file order)."""
    keywords = []
    for marker, payload in jpeg_segments(data):
        if marker != 0xED:
            continue
        body = payload[len(APP13_SIGNATURE):] if payload.startswith(APP13_SIGNATURE) else payload
        for resource_id, block in bim_blocks(body):
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
                    j += 2  # extended-length size fields are not produced by the authored fixtures
                    size &= 0x7FFF
                value = block[j:j + size]; j += size
                if record == 2 and dataset == 25:
                    keywords.append(value)
    return keywords


def thumbnail_tags(data):
    """EXIF tags living in IFDs after the first (IFD1 thumbnail territory)."""
    tags = {}
    for ifds in exif_blocks(data):
        for ifd in ifds[1:]:
            for tag, values in ifd['tags'].items():
                tags.setdefault(tag, []).extend(values)
    return tags


def run_xmp(report, row, exe, fixture_path, work, fixtures_dir):
    original = fixture_path.read_bytes()
    operation, encoding = row['operation'], row['textEncoding']
    record_only = row['duplicates'] != 'none' or encoding == 'unknown-raw'
    facts = {}
    xmp_segments = [p for m, p in jpeg_segments(original) if m == 0xE1 and p.startswith(owned.XMP_SIGNATURE)]
    facts['xmpSegmentCount'] = len(xmp_segments)
    facts['xmpPacketSha256'] = [hashlib.sha256(p).hexdigest() for p in xmp_segments]
    facts['declaresHasExtendedXMP'] = [owned.XMP_EXT_GUID in p for p in xmp_segments]

    def spec_derived_value_present(target, token):
        rc2, out2, _ = soft_run(report, [exe, '-pa', target], cwd=work)
        facts['stdoutCreatorToolAfter'] = out2.decode('utf-8', 'replace')[:400]
        return token in out2.decode('utf-8', 'replace')

    if operation == 'read':
        rc, stdout, _ = soft_run(report, [exe, '-pa', fixture_path], cwd=work)
        after, operative = original, fixture_path.name
        expect_ok = None
        if not record_only:
            if row['layout'] == 'app1-xmp-standard':
                expect_ok = b'MetadataSharp' in stdout
            elif row['layout'] == 'app1-xmp-extended':
                expect_ok = owned.XMP_EXT_GUID in stdout
    elif operation in ('insert', 'replace'):
        base_name = fixture_path.name
        operative = base_name
        if operation == 'insert' and row['layout'] == 'app1-xmp-standard' and row['duplicates'] == 'none':
            base_name, operative = 'xmp-none.jpg', 'xmp-none.jpg'  # from-nothing case, mirrors COM insert
        base = fixtures_dir / base_name
        target = work / 'case.jpg'
        shutil.copyfile(base, target)
        value = XMP_UTF8_VALUE if encoding == 'utf-8' else None
        if operation == 'replace' and encoding == 'utf-8':
            value = 'Bob'
        if value is not None:
            mod = f'set Xmp.xmp.CreatorTool XmpText {value}'
        else:
            mod = b'set Xmp.xmp.CreatorTool XmpText ' + XMP_RAW_VALUE
        rc, stdout, _ = soft_run(report, [exe, '-M', mod, target], cwd=work)
        after, original = target.read_bytes(), base.read_bytes()
        expect_ok = None
        if not record_only and encoding == 'utf-8':
            token = 'MetadataSharp B1' if operation == 'insert' else 'Bob'
            expect_ok = spec_derived_value_present(target, token)
    elif operation == 'remove':
        base = fixture_path
        target = work / 'case.jpg'
        shutil.copyfile(base, target)
        rc, stdout, _ = soft_run(report, [exe, '-M', 'del Xmp.xmp.CreatorTool', target], cwd=work)
        after, original, operative = target.read_bytes(), fixture_path.read_bytes(), fixture_path.name
        expect_ok = None
        if not record_only and encoding == 'utf-8' and row['layout'] == 'app1-xmp-standard':
            rc2, out2, _ = soft_run(report, [exe, '-pa', target], cwd=work)
            facts['stdoutCreatorToolAfter'] = out2.decode('utf-8', 'replace')[:400]
            expect_ok = b'MetadataSharp' not in out2
    else:
        raise AssertionError(operation)
    facts['scanPreserved'] = scan_bytes(after) == scan_bytes(original)
    parse_ok = True
    try:
        jpeg_segments(after)
    except Exception as error:
        parse_ok, facts['parseError'] = False, str(error)
    return rc, original, after, parse_ok, expect_ok, record_only, facts, stdout, operative


def run_iptc(report, row, exe, fixture_path, work, fixtures_dir):
    original = fixture_path.read_bytes()
    operation = row['operation']
    record_only = row['duplicates'] != 'none' or row['layout'] == 'app13-8bim-iptc-multiple'
    facts = {}
    app13 = [p for m, p in jpeg_segments(original) if m == 0xED]
    facts['app13SegmentCount'] = len(app13)
    facts['keywordsBefore'] = [k.decode('ascii', 'backslashreplace') for k in iptc_keywords(original)]
    facts['bimResourceCount'] = sum(len(bim_blocks(p[len(APP13_SIGNATURE):])) for p in app13
                                    if p.startswith(APP13_SIGNATURE))

    def keywords_after(target):
        return [k.decode('ascii', 'backslashreplace') for k in iptc_keywords(target.read_bytes())]

    if operation == 'read':
        rc, stdout, _ = soft_run(report, [exe, '-pi', fixture_path], cwd=work)
        after, operative = original, fixture_path.name
        expect_ok = None
        if not record_only:
            expect_ok = b'MetadataSharpAlpha' in stdout and b'MetadataSharpBeta' in stdout
    elif operation == 'insert':
        base_name, operative = fixture_path.name, fixture_path.name
        if row['layout'] == 'app13-8bim-iptc' and row['duplicates'] == 'none':
            base_name, operative = 'iptc-none.jpg', 'iptc-none.jpg'
        base = fixtures_dir / base_name
        target = work / 'case.jpg'
        shutil.copyfile(base, target)
        rc, stdout, _ = soft_run(report, [exe, '-M', f'add Iptc.Application2.Keywords String MetadataSharpAdded', target], cwd=work)
        after, original = target.read_bytes(), base.read_bytes()
        facts['keywordsAfter'] = keywords_after(target)
        expect_ok = 'MetadataSharpAdded' in facts['keywordsAfter']
    elif operation == 'replace':
        target = work / 'case.jpg'
        shutil.copyfile(fixture_path, target)
        rc, stdout, _ = soft_run(report, [exe, '-M', 'set Iptc.Application2.Keywords String MetadataSharpReplacement', target], cwd=work)
        after, operative = target.read_bytes(), fixture_path.name
        facts['keywordsAfter'] = keywords_after(target)
        record_only = True  # repeated-dataset 'set' semantics are CLI-defined, never pre-asserted
        expect_ok = None
    elif operation == 'remove':
        target = work / 'case.jpg'
        shutil.copyfile(fixture_path, target)
        rc, stdout, _ = soft_run(report, [exe, '-M', 'del Iptc.Application2.Keywords', target], cwd=work)
        after, operative = target.read_bytes(), fixture_path.name
        facts['keywordsAfter'] = keywords_after(target)
        if not record_only:
            expect_ok = facts['keywordsAfter'] == []
        else:
            expect_ok = None
    else:
        raise AssertionError(operation)
    facts['scanPreserved'] = scan_bytes(after) == scan_bytes(original)
    parse_ok = True
    try:
        jpeg_segments(after)
    except Exception as error:
        parse_ok, facts['parseError'] = False, str(error)
    return rc, original, after, parse_ok, expect_ok, record_only, facts, stdout, operative


def run_thumbnail(report, row, exe, fixture_path, work, fixtures_dir):
    original = fixture_path.read_bytes()
    operation = row['operation']
    facts = {}
    facts['thumbnailTagsBefore'] = sorted(tag for tag in thumbnail_tags(original) if tag in THUMB_TAG_IDS)

    def ifd1_has_jpeg_thumbnail(data):
        tags = thumbnail_tags(data)
        return 0x0201 in tags and 0x0202 in tags

    def after_tags(target):
        return sorted(tag for tag in thumbnail_tags(target.read_bytes()) if tag in THUMB_TAG_IDS)

    record_only = True
    if operation == 'read':
        rc, stdout, _ = soft_run(report, [exe, '-pa', fixture_path], cwd=work)
        after, operative = original, fixture_path.name
        expect_ok = None
    elif operation == 'insert':
        base = fixtures_dir / 'exif-base.jpg'  # no-thumbnail EXIF base; from-nothing case
        target = work / 'case.jpg'
        shutil.copyfile(base, target)
        (work / 'case-thumb.jpg').write_bytes(owned.thumb_jpeg_bytes())
        rc, stdout, _ = soft_run(report, [exe, '-i', 't', target], cwd=work)
        after, original, operative = target.read_bytes(), base.read_bytes(), 'exif-base.jpg'
        facts['thumbnailTagsAfter'] = after_tags(target)
        record_only = False
        expect_ok = ifd1_has_jpeg_thumbnail(after)
    elif operation == 'replace':
        target = work / 'case.jpg'
        shutil.copyfile(fixture_path, target)
        (work / 'case-thumb.jpg').write_bytes(owned.thumb_jpeg_bytes())
        rc, stdout, _ = soft_run(report, [exe, '-i', 't', target], cwd=work)
        after, operative = target.read_bytes(), fixture_path.name
        facts['thumbnailTagsAfter'] = after_tags(target)
        expect_ok = None  # mixed-state result on uncompressed source is CLI-defined; record-only
    elif operation == 'remove':
        target = work / 'case.jpg'
        shutil.copyfile(fixture_path, target)
        rc, stdout, _ = soft_run(report, [exe, '-d', 't', target], cwd=work)
        after, operative = target.read_bytes(), fixture_path.name
        facts['thumbnailTagsAfter'] = after_tags(target)
        record_only = False
        expect_ok = facts['thumbnailTagsAfter'] == []
    elif operation == 'extract':
        target = work / 'case.jpg'
        shutil.copyfile(fixture_path, target)
        outdir = work / 'extract-out'
        outdir.mkdir()
        rc, stdout, _ = soft_run(report, [exe, '-e', 't', '-l', outdir, target], cwd=work)
        after, operative = target.read_bytes(), fixture_path.name
        produced = sorted(outdir.iterdir()) if outdir.is_dir() else []
        facts['extractedFiles'] = [p.name for p in produced]
        facts['extractedSha256'] = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in produced}
        if row['layout'] == 'exif-ifd1-jpeg':
            expect_ok = (len(produced) == 1 and produced[0].read_bytes() == owned.thumb_jpeg_bytes())
            record_only = False
        else:
            expect_ok = None  # uncompressed thumb is converted to TIFF by the CLI; bytes are CLI-defined
    else:
        raise AssertionError(operation)
    facts['scanPreserved'] = scan_bytes(after) == scan_bytes(original)
    parse_ok = True
    try:
        jpeg_segments(after)
    except Exception as error:
        parse_ok, facts['parseError'] = False, str(error)
    return rc, original, after, parse_ok, expect_ok, record_only, facts, stdout, operative


RUNNERS = {'exif': run_exif, 'comments': run_comments, 'icc': run_icc, 'inventory': run_inventory,
           'xmp': run_xmp, 'iptc': run_iptc, 'thumbnail': run_thumbnail}


def classify(rc, parse_ok, expect_ok, scan_ok, artifact_produced=False):
    if not scan_ok:
        return 'scan-changed'
    if rc != 0:
        # the pinned CLI exits 137/46 on successful thumbnail extraction; the exit code is
        # recorded verbatim and the produced artifacts decide honesty: an artifact produced
        # by a nonzero-exit command is neither 'accepted' nor 'refused'
        return 'nonzero-exit-artifact-produced' if artifact_produced else 'refused'
    if not parse_ok:
        return 'accepted-unparseable'
    if expect_ok is False:
        return 'accepted-unexpected'
    return 'accepted'


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--upstream', type=Path, required=True)
    parser.add_argument('--build', type=Path, required=True)
    parser.add_argument('--batch', choices=['b0', 'b1', 'all'], default='all')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/e01',
                        help='root directory; each batch writes <output>/<batch>/observations.json')
    parser.add_argument('--census', type=Path, default=CENSUS)
    args = parser.parse_args()
    upstream, build, output_root = args.upstream.resolve(), args.build.resolve(), args.output.resolve()
    batches = ['b0', 'b1'] if args.batch == 'all' else [args.batch]
    all_rows = json.loads(args.census.read_text())['rows']
    output_root.mkdir(parents=True, exist_ok=True)
    try:
        for batch in batches:
            run_batch(args, upstream, build, output_root, batch, all_rows)
    except Exception:
        # failing mid-batch leaves <output>/<batch>/observations.json with status=failed (written by run_batch)
        raise


def run_batch(args, upstream, build, output_root, batch, all_rows):
    output = (output_root / batch).resolve()
    output.mkdir(parents=True, exist_ok=True)
    report = dict(schema=1, status='running', batch=batch.upper(),
                  sourceCommit=os.environ.get('E01_SOURCE_COMMIT'),
                  baseline=dict(version=VERSION, sourceCommit=PIN),
                  commands=[], observations=[], staticNotApplicable=[],
                  outcomeCounts={}, denominatorFrozen=False)

    def save():
        (output / 'observations.json').write_text(json.dumps(report, indent=2) + '\n')

    def require_env(condition, message):
        if not condition:
            raise ValueError(message)

    try:
        require_env(bool(report['sourceCommit'] and re.fullmatch('[0-9a-f]{40}', report['sourceCommit'])),
                    'Missing source identity (E01_SOURCE_COMMIT)')
        head = subprocess.run(['git', '-C', upstream, 'rev-parse', 'HEAD'],
                                       capture_output=True, text=True, check=True).stdout.strip()
        require_env(head == PIN, 'Wrong upstream commit')
        dirty = subprocess.run(['git', '-C', upstream, 'status', '--porcelain', '--untracked-files=no'],
                                        capture_output=True, text=True, check=True).stdout.strip()
        require_env(not dirty, 'Modified upstream source')
        cache = (build / 'CMakeCache.txt').read_bytes()
        report['baseline']['effectiveCmake'] = baseline.check_cache(cache.decode())
        report['baseline']['cmakeCacheSha256'] = baseline.digest(cache)
        report['baseline']['licenseSha256'] = baseline.digest((upstream / 'COPYING').read_bytes())
        exe = build / 'bin/exiv2'
        report['baseline']['executableSha256'] = baseline.digest(exe.read_bytes())
        version = subprocess.run([exe, '--version'], capture_output=True, text=True, check=True,
                                          env={**os.environ, 'LC_ALL': 'C'})
        baseline.check_version(version.stdout)
        report['commands'].append(dict(args=[str(exe), '--version'], exitCode=version.returncode,
                                       stdoutSha256=baseline.digest(version.stdout.encode()),
                                       stdout=version.stdout))
        fixtures_dir = output / 'fixtures'
        owned.generate(fixtures_dir)
        owned.verify_dir(fixtures_dir)
        report['fixtureManifest'] = owned.manifest()
        rows = [row for row in all_rows if row['batch'].lower() == batch]
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
            require_env(fixture_name is not None and (fixtures_dir / fixture_name).exists(),
                        f'No fixture for row {row_id}')
            fixture_path = fixtures_dir / fixture_name
            rc, before, after, parse_ok, expect_ok, record_only, facts, stdout, operative_fixture = \
                RUNNERS[row['family']](report, row, exe, fixture_path, work, fixtures_dir)
            # hard invariant: a successful run must never change the coded scan
            if not facts.get('scanPreserved', True):
                outcome = 'scan-changed'
            else:
                outcome = classify(rc, parse_ok, expect_ok, True,
                                   artifact_produced=bool(facts.get('extractedFiles')))
            report['observations'].append(dict(
                id=row_id,
                seedId=f"EX0289-JPEG-{'COMMENTS' if row['family'] == 'comments' else row['family'].upper()}",
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
            print(f'::notice title={batch.upper()}Z observations.json {i}/{len(chunks)}::{chunk}', flush=True)
    except Exception:
        import traceback
        report['status'] = 'failed'
        report['failure'] = traceback.format_exc()[-3500:]
        save()
        diagnostic = json.dumps(dict(error=report['failure']), separators=(',', ':'))
        diagnostic = diagnostic.replace('%', '%25').replace('\r', '%0D').replace('\n', '%0A')
        print(f'::error title={batch.upper()} census observation failure::' + diagnostic, flush=True)
        raise


if __name__ == '__main__':
    main()
