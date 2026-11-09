"""E01 census observation batch B5: pinned Exiv2 CLI against owned XMP packets.

B5 covers XMP x {inventory, exif (noDocumentedClaim), comments (noDocumentedClaim),
icc (noDocumentedClaim), iptc (noDocumentedClaim), thumbnail (noDocumentedClaim),
xmp} per docs/research/EXIV2_OPERATION_SEEDS.json, plus the B5
sidecar-presence read probes for JPEG/PNG/WEBP/TIFF (presence is in scope;
xmp-applied writes stay E15 per the approved denominators).

Observation, not pass/fail: outcomes are recorded honestly (accepted /
accepted-unexpected / refused / accepted-unparseable /
nonzero-exit-artifact-produced; scan-changed never applies to this batch) and
only infrastructure failures stop the run. A standalone .xmp file IS an XMP
packet document, so the bytes rewritten by metadata operations are the whole
file; there is no coded payload to preserve and the invariant is recorded as
byte-length/SHA-256 facts instead. The sidecar probes copy an image fixture
from the already-pinned JPEG/PNG/WebP/TIFF sets, place an authored
SIDECARPROBE <stem>.xmp next to it and invoke the pinned CLI on the IMAGE
only; whether the adjacent sidecar token appears in the image's printed
output is the presence/applied distinction, recorded and never asserted.

The wrapped layout carries xpacket PIs + x:xmpmeta (the shape the in-image
XMP fixtures already hold); the unwrapped layout omits the optional wrapper
per XMP Part 1 SS7.3-SS7.5 (editions/blobs pinned in
docs/design/XMP_NORMATIVE_REVIEW_AND_RESTRICTED_PROFILE.md). Leading raw BOMs,
double packets and Extended-XMP segmented layouts are deliberate non-goals of
this batch (recorded in README/limits).

Usage: observe_b5.py --upstream artifacts/e01/source --build artifacts/e01/build [--output artifacts/e01]
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
import observe_b0 as shared  # noqa: E402  (soft_run, classify)
import xmp_owned_fixtures as owned  # noqa: E402

PIN = baseline.PIN
VERSION = baseline.VERSION
CENSUS = ROOT / 'docs/research/EXIV2_CENSUS.json'
from build_census import STATIC_NOT_APPLICABLE  # noqa: E402

SIDECAR_PROBE_PACKET = (b'<?xpacket begin="\xef\xbb\xbf" id="W5M0MpCehiHzreSzkc9d"?>'
                        b'<x:xmpmeta xmlns:x="adobe:ns:meta/" x:xmptk="MetadataSharp">'
                        b'<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">'
                        b'<rdf:Description rdf:about="" xmlns:xmp="http://ns.adobe.com/xap/1.0/">'
                        b'<xmp:CreatorTool>SIDECARPROBE</xmp:CreatorTool>'
                        b'</rdf:Description></rdf:RDF></x:xmpmeta>'
                        b'<?xpacket end="w"?>')

SIDECAR_IMAGE_FIXTURES = {'JPEG': 'xmp-standard.jpg', 'PNG': 'png-xmp.png',
                          'WEBP': 'webp-xmp.webp', 'TIFF': 'tiff-xmp.tif'}

XMP_UTF8_VALUE = 'MetadataSharp B5 é ✓'
XMP_RAW_VALUE = b'\x89RAW\xff\xfe'  # no NUL: execve forbids embedded NUL in argv


def xmp_inventory_facts(data):
    return {'packetSha256': hashlib.sha256(data).hexdigest(), 'packetBytes': len(data)}


def parse_error_of(data):
    return owned.strict_parse_xmp(data)


def case_fixture(family, layout, duplicates, operation=None, text_encoding=None):
    if family == 'inventory':
        return 'xmp-base.xmp'
    if family in ('exif', 'iptc', 'comments', 'icc', 'thumbnail'):
        return 'xmp-base.xmp'  # noDocumentedClaim cells: record, never assert
    if family == 'xmp':
        table = {
            ('xmp-packet', 'utf-8', 'read'): 'xmp-base.xmp',
            ('xmp-packet', 'unknown-raw', 'read'): 'xmp-nonutf8.xmp',
            ('xmp-packet', 'utf-8', 'insert'): 'xmp-no-props.xmp',
            ('xmp-packet', 'unknown-raw', 'insert'): 'xmp-no-props.xmp',
            ('xmp-packet', 'utf-8', 'replace'): 'xmp-base.xmp',
            ('xmp-packet', 'unknown-raw', 'replace'): 'xmp-nonutf8.xmp',
            ('xmp-packet', 'utf-8', 'remove'): 'xmp-base.xmp',
            ('xmp-packet', 'unknown-raw', 'remove'): 'xmp-nonutf8.xmp',
            ('xmp-packet-unwrapped', 'utf-8', 'read'): 'xmp-unwrapped.xmp',
            ('xmp-packet-unwrapped', 'unknown-raw', 'read'): 'xmp-unwrapped-nonutf8.xmp',
            ('xmp-packet-unwrapped', 'utf-8', 'insert'): 'xmp-unwrapped-no-props.xmp',
            ('xmp-packet-unwrapped', 'unknown-raw', 'insert'): 'xmp-unwrapped-no-props.xmp',
            ('xmp-packet-unwrapped', 'utf-8', 'replace'): 'xmp-unwrapped.xmp',
            ('xmp-packet-unwrapped', 'unknown-raw', 'replace'): 'xmp-unwrapped-nonutf8.xmp',
            ('xmp-packet-unwrapped', 'utf-8', 'remove'): 'xmp-unwrapped.xmp',
            ('xmp-packet-unwrapped', 'unknown-raw', 'remove'): 'xmp-unwrapped-nonutf8.xmp',
        }
        return table.get((layout, text_encoding, operation))
    return None


def case_fixture_for_probe(fmt):
    return SIDECAR_IMAGE_FIXTURES.get(fmt)


def static_not_applicable(row):
    for family, layout, duplicates, operation, reason in STATIC_NOT_APPLICABLE:
        if (row['family'] == family and row['layout'] == layout
                and (duplicates is None or row['duplicates'] == duplicates)
                and (operation is None or row['operation'] == operation)):
            return reason
    return None


def run_inventory(report, row, exe, fixture_path, work, fixtures_dir):
    original = fixture_path.read_bytes()
    rc, stdout, _ = shared.soft_run(report, [exe, '-pS', fixture_path], cwd=work)
    parse_error = parse_error_of(original)
    facts = {'scanPreserved': True, **xmp_inventory_facts(original)}
    if parse_error:
        facts['parseError'] = parse_error
    return rc, original, original, parse_error is None, None, False, facts, stdout, fixture_path.name


def run_claim_read(report, row, exe, fixture_path, work, fixtures_dir):
    """noDocumentedClaim read rows (claim '-'): record what the pinned CLI does;
    the cell carries no documented claim, so nothing is pre-asserted."""
    original = fixture_path.read_bytes()
    facts = {}
    rc, stdout, _ = shared.soft_run(report, [exe, '-pa', fixture_path], cwd=work)
    facts['stdoutSha256'] = hashlib.sha256(stdout).hexdigest()
    facts['stdoutPreview'] = stdout[:400].decode('utf-8', 'replace')
    parse_error = parse_error_of(original)
    if parse_error:
        facts['parseError'] = parse_error
    facts['scanPreserved'] = True
    return rc, original, original, parse_error is None, None, False, facts, stdout, fixture_path.name


def run_xmp(report, row, exe, fixture_path, work, fixtures_dir):
    original = fixture_path.read_bytes()
    operation, encoding = row['operation'], row['textEncoding']
    record_only = encoding == 'unknown-raw'
    facts = {}

    def post_value_present(target, token):
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
        base = fixtures_dir / base_name
        target = work / 'case.xmp'
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
            token = 'MetadataSharp B5' if operation == 'insert' else 'Bob'
            expect_ok = post_value_present(target, token)
    elif operation == 'remove':
        base = fixture_path
        target = work / 'case.xmp'
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
    # 'before' facts describe the actual input bytes (base may differ for inserts)
    facts['xmpPacketSha256Before'] = hashlib.sha256(original).hexdigest()
    facts['xmpPacketSha256After'] = hashlib.sha256(after).hexdigest()
    facts['xmpPacketBytesBefore'] = len(original)
    facts['xmpPacketBytesAfter'] = len(after)
    # the pinned CLI's raw-XMP failure mode prepends a partial canonical packet and
    # appends a duplicate end PI (observed 2026-11-09); count the PIs so the artifact is visible
    facts['xmpBeginPICountBefore'] = original.count(b'<?xpacket begin')
    facts['xmpBeginPICountAfter'] = after.count(b'<?xpacket begin')
    facts['xmpEndPICountBefore'] = original.count(b'<?xpacket end')
    facts['xmpEndPICountAfter'] = after.count(b'<?xpacket end')
    facts['scanPreserved'] = True  # no coded payload in a standalone packet file
    parse_error = parse_error_of(after)
    if parse_error:
        facts['parseError'] = parse_error
    return rc, original, after, parse_error is None, expect_ok, record_only, facts, b'', operative


def run_sidecar_probe(report, row, exe, fixture_path, work, fixtures_dir):
    """Presence probe: <stem>.xmp SIDECARPROBE sidecar sits next to the copied
    image fixture; the pinned CLI opens the image only. The presence/applied
    distinction is the observation: sidecarTokenInStdout records whether the
    adjacent sidecar leaked into the image's printed output."""
    original = fixture_path.read_bytes()
    image_copy = work / fixture_path.name
    shutil.copyfile(fixture_path, image_copy)
    sidecar = image_copy.with_suffix('.xmp')
    sidecar.write_bytes(SIDECAR_PROBE_PACKET)
    facts = {
        'scanPreserved': True,
        'sidecarName': sidecar.name,
        'sidecarSha256': hashlib.sha256(SIDECAR_PROBE_PACKET).hexdigest(),
        'sidecarBytes': len(SIDECAR_PROBE_PACKET),
    }
    rc, stdout, _ = shared.soft_run(report, [exe, '-pa', image_copy], cwd=work)
    facts['stdoutSha256'] = hashlib.sha256(stdout).hexdigest()
    facts['stdoutPreview'] = stdout[:400].decode('utf-8', 'replace')
    facts['sidecarTokenInStdout'] = b'SIDECARPROBE' in stdout
    facts['imageTokenInStdout'] = b'MetadataSharp' in stdout
    return rc, original, original, True, None, True, facts, stdout, fixture_path.name


RUNNERS = {'inventory': run_inventory, 'comments': run_claim_read, 'exif': run_claim_read,
           'icc': run_claim_read, 'iptc': run_claim_read, 'thumbnail': run_claim_read,
           'xmp': run_xmp}


def main():
    parser = argparse.ArgumentParser(description='B5 census observation batch (XMP packets and sidecars)')
    parser.add_argument('--upstream', type=Path, required=True)
    parser.add_argument('--build', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/e01',
                        help='root directory; writes <output>/b5/observations.json')
    parser.add_argument('--census', type=Path, default=CENSUS)
    args = parser.parse_args()
    upstream, build, output_root = args.upstream.resolve(), args.build.resolve(), args.output.resolve()
    output = (output_root / 'b5').resolve()
    output.mkdir(parents=True, exist_ok=True)
    report = dict(schema=1, status='running', batch='B5',
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
        # The sidecar probes reuse the image fixtures of the earlier batches;
        # their bytes are pinned by those batches' manifests (NOT re-emitted here).
        image_manifests = {
            'JPEG': json.loads((ROOT / 'tests/fixtures/jpeg/manifest.json').read_text()),
            'PNG': json.loads((ROOT / 'tests/fixtures/png/manifest.json').read_text()),
            'WEBP': json.loads((ROOT / 'tests/fixtures/webp/census-manifest.json').read_text()),
            'TIFF': json.loads((ROOT / 'tests/fixtures/tiff/manifest.json').read_text()),
        }
        # The probes need the image fixture bytes: regenerate the earlier batches'
        # fixtures into this batch's work tree (never committed) and verify them
        # against their own pins before use.
        import jpeg_owned_fixtures as jpegs  # noqa: PLC0415
        import png_owned_fixtures as pngs  # noqa: PLC0415
        import webp_owned_fixtures as webps  # noqa: PLC0415
        import tiff_owned_fixtures as tiffs  # noqa: PLC0415
        image_fixtures = {}
        for fmt, generator in (('JPEG', jpegs), ('PNG', pngs), ('WEBP', webps), ('TIFF', tiffs)):
            directory = output / 'probe-fixtures' / fmt.lower()
            generator.generate(directory)
            if hasattr(generator, 'verify_dir'):
                generator.verify_dir(directory)
            for entry in image_manifests[fmt]:
                image_fixtures[entry['file']] = (directory, entry)
        report['probeImageManifests'] = {fmt: [e['file'] for e in entries]
                                         for fmt, entries in image_manifests.items()}
        all_rows = json.loads(args.census.read_text())['rows']
        rows = [row for row in all_rows if row['batch'].lower() == 'b5']
        assert rows, 'No B5 rows enumerated in the census yet'
        report['denominatorFrozen'] = False
        for index, row in enumerate(rows):
            row_id = row['rowId']
            work = output / 'work' / f'{index:04d}'
            work.mkdir(parents=True, exist_ok=True)
            reason = static_not_applicable(row)
            if reason:
                report['staticNotApplicable'].append(dict(id=row_id, reason=reason))
                continue
            if row['sidecar'] == 'xmp-present':
                fixture_name = case_fixture_for_probe(row['format'])
                assert fixture_name is not None, f'No probe image fixture for row {row_id}'
                directory, entry = image_fixtures[fixture_name]
                fixture_path = directory / fixture_name
                data = fixture_path.read_bytes()
                assert hashlib.sha256(data).hexdigest() == entry['sha256'], \
                    f'probe image fixture drifted: {fixture_name}'
                rc, before, after, parse_ok, expect_ok, record_only, facts, stdout, operative_fixture = \
                    run_sidecar_probe(report, row, exe, fixture_path, work, fixtures_dir)
                facts['imageFixtureSha256'] = entry['sha256']
            else:
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
                sidecar=row['sidecar'],
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
            print(f'::notice title=B5Z observations.json {i}/{len(chunks)}::{chunk}', flush=True)
    except Exception:
        import traceback
        report['status'] = 'failed'
        report['failure'] = traceback.format_exc()[-3500:]
        save()
        diagnostic = json.dumps(dict(error=report['failure']), separators=(',', ':'))
        diagnostic = diagnostic.replace('%', '%25').replace('\r', '%0D').replace('\n', '%0A')
        print('::error title=B5 census observation failure::' + diagnostic, flush=True)
        raise


if __name__ == '__main__':
    main()
