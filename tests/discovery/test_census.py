import importlib.util
import json
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('census', ROOT / 'tools/reference/build_census.py')
census = importlib.util.module_from_spec(spec); spec.loader.exec_module(census)

ROW_ID = __import__('re').compile(r'^EX0289-[A-Z][A-Z0-9]*-[A-Z]+-[A-Z]+-\d{4}$')


SIX_FAMILIES = ('EXIF', 'IPTC', 'XMP', 'COMMENTS', 'ICC', 'THUMBNAIL')


def load_evidence(batch='b0'):
    path = ROOT / f'docs/research/evidence/e01-{batch}-census/observations.json'
    return json.loads(path.read_text()) if path.exists() else None


class CensusPolicyTests(unittest.TestCase):
    def setUp(self):
        self.seeds = json.loads((ROOT / 'docs/research/EXIV2_OPERATION_SEEDS.json').read_text())
        self.report = census.build(self.seeds)

    def test_artifact_matches_generator(self):
        on_disk = json.loads((ROOT / 'docs/research/EXIV2_CENSUS.json').read_text())
        self.assertEqual(self.report, on_disk)

    def test_row_ids_unique_and_pinned_format(self):
        ids = [r['rowId'] for r in self.report['rows']]
        self.assertEqual(len(ids), len(set(ids)))
        for row_id in ids:
            self.assertRegex(row_id, ROW_ID)

    def test_enumerations_are_closed_and_rows_conform(self):
        enums = self.report['enumerations']
        for row in self.report['rows']:
            self.assertIn(row['family'], enums['family'])
            self.assertIn(row['operation'], enums['operation'])
            self.assertIn(row['valueType'], enums['valueType'])
            self.assertIn(row['textEncoding'], enums['textEncoding'])
            self.assertIn(row['layout'], enums['layout'][row['family']])
            self.assertIn(row['duplicates'], enums['duplicates'])
            self.assertIn(row['sidecar'], enums['sidecar'])
            self.assertEqual({'icc', 'comment', 'preview'}, set(row['presence']))
            for value in row['presence'].values():
                self.assertIn(value, enums['presenceValues'])

    def test_extract_only_on_opaque_payload_families(self):
        enums = self.report['enumerations']
        for row in self.report['rows']:
            if row['operation'] == 'extract':
                self.assertIn(row['family'], enums['extractFamilies'])
                self.assertEqual('not-applicable', row['textEncoding'])

    def test_b0_batch_is_fully_observed_or_static_not_applicable(self):
        self.assertFalse(self.report['denominatorFrozen'])
        evidence = load_evidence('b0')
        self.assertIsNotNone(evidence, 'B0 pinned-CLI observation evidence is required')
        self.assertEqual('passed', evidence['status'])
        b0 = [r for r in self.report['rows'] if r['batch'] == 'B0']
        static = {row['rowId'] for row in b0 if census.static_not_applicable(row)}
        observed = {r['rowId'] for r in b0 if r['baselineObservation'] == 'observed-cli'}
        not_applicable = {r['rowId'] for r in b0 if r['baselineObservation'] == 'not-applicable'}
        self.assertEqual(len(b0), len(observed) + len(not_applicable))
        self.assertEqual(static, not_applicable)
        self.assertEqual({o['id'] for o in evidence['observations']}, observed)
        self.assertEqual(len(static), 38)
        self.assertEqual(len(observed), 97)

    def test_b2_batch_is_fully_observed_or_static_not_applicable(self):
        evidence = load_evidence('b2')
        self.assertIsNotNone(evidence, 'B2 pinned-CLI observation evidence is required')
        self.assertEqual('passed', evidence['status'])
        self.assertEqual('B2', evidence['batch'])
        b2 = [r for r in self.report['rows'] if r['batch'] == 'B2']
        static = {row['rowId'] for row in b2 if census.static_not_applicable(row)}
        observed = {r['rowId'] for r in b2 if r['baselineObservation'] == 'observed-cli'}
        not_applicable = {r['rowId'] for r in b2 if r['baselineObservation'] == 'not-applicable'}
        self.assertEqual(len(b2), len(observed) + len(not_applicable))
        self.assertEqual(static, not_applicable)
        self.assertEqual({'EX0289-PNG-THUMBNAIL-INSERT-0002'}, static)
        self.assertEqual({o['id'] for o in evidence['observations']}, observed)
        self.assertEqual(1, evidence['outcomeCounts'].get('static-not-applicable'))
        self.assertEqual(0, evidence['outcomeCounts'].get('scan-changed', 0))
        self.assertEqual(66, len(observed))
        for row in b2:
            self.assertEqual('not-assessed', row['verification'])

    def test_b3_batch_is_fully_observed_or_static_not_applicable(self):
        evidence = load_evidence('b3')
        self.assertIsNotNone(evidence, 'B3 pinned-CLI observation evidence is required')
        self.assertEqual('passed', evidence['status'])
        self.assertEqual('B3', evidence['batch'])
        b3 = [r for r in self.report['rows'] if r['batch'] == 'B3']
        static = {row['rowId'] for row in b3 if census.static_not_applicable(row)}
        observed = {r['rowId'] for r in b3 if r['baselineObservation'] == 'observed-cli'}
        not_applicable = {r['rowId'] for r in b3 if r['baselineObservation'] == 'not-applicable'}
        self.assertEqual(len(b3), len(observed) + len(not_applicable))
        self.assertEqual(static, not_applicable)
        self.assertEqual({'EX0289-WEBP-THUMBNAIL-INSERT-0002'}, static)
        self.assertEqual({o['id'] for o in evidence['observations']}, observed)
        self.assertEqual(1, evidence['outcomeCounts'].get('static-not-applicable'))
        self.assertEqual(0, evidence['outcomeCounts'].get('scan-changed', 0))
        self.assertEqual(42, len(observed))
        # The one honest pinned-CLI anomaly of this batch: `del Xmp.xmp.CreatorTool`
        # exits 0 on a raw XMP chunk but never rewrites the packet (B2 PNG analog: accepted).
        unexpected = [o for o in evidence['observations'] if o['outcome'] == 'accepted-unexpected']
        self.assertEqual(['EX0289-WEBP-XMP-REMOVE-0001'], [o['id'] for o in unexpected])
        for row in b3:
            self.assertEqual('not-assessed', row['verification'])

    def test_b4_batch_is_fully_observed_or_static_not_applicable(self):
        evidence = load_evidence('b4')
        self.assertIsNotNone(evidence, 'B4 pinned-CLI observation evidence is required')
        self.assertEqual('passed', evidence['status'])
        self.assertEqual('B4', evidence['batch'])
        b4 = [r for r in self.report['rows'] if r['batch'] == 'B4']
        static = {row['rowId'] for row in b4 if census.static_not_applicable(row)}
        observed = {r['rowId'] for r in b4 if r['baselineObservation'] == 'observed-cli'}
        not_applicable = {r['rowId'] for r in b4 if r['baselineObservation'] == 'not-applicable'}
        self.assertEqual(len(b4), len(observed) + len(not_applicable))
        self.assertEqual(static, not_applicable)
        self.assertEqual({'EX0289-TIFF-THUMBNAIL-INSERT-0002'}, static)
        self.assertEqual({o['id'] for o in evidence['observations']}, observed)
        self.assertEqual(1, evidence['outcomeCounts'].get('static-not-applicable'))
        self.assertEqual(0, evidence['outcomeCounts'].get('scan-changed', 0))
        self.assertEqual(49, len(observed))
        unexpected = [o for o in evidence['observations'] if o['outcome'] == 'accepted-unexpected']
        self.assertEqual([], [o['id'] for o in unexpected])
        for row in b4:
            self.assertEqual('not-assessed', row['verification'])

    def test_b1_batch_is_fully_observed_or_static_not_applicable(self):
        evidence = load_evidence('b1')
        self.assertIsNotNone(evidence, 'B1 pinned-CLI observation evidence is required')
        self.assertEqual('passed', evidence['status'])
        self.assertEqual('B1', evidence['batch'])
        b1 = [r for r in self.report['rows'] if r['batch'] == 'B1']
        static = {row['rowId'] for row in b1 if census.static_not_applicable(row)}
        observed = {r['rowId'] for r in b1 if r['baselineObservation'] == 'observed-cli'}
        not_applicable = {r['rowId'] for r in b1 if r['baselineObservation'] == 'not-applicable'}
        self.assertEqual(len(b1), len(observed) + len(not_applicable))
        self.assertEqual(static, not_applicable)
        self.assertEqual({o['id'] for o in evidence['observations']}, observed)
        self.assertEqual(len(static), 1)
        self.assertEqual(len(observed), 57)
        self.assertEqual({'observed-cli', 'not-applicable'}, {r['baselineObservation'] for r in b1})
        for row in b1:
            self.assertEqual('not-assessed', row['verification'])
        self.assertEqual(1, evidence['outcomeCounts'].get('static-not-applicable'))

    def test_b5_batch_is_fully_observed(self):
        evidence = load_evidence('b5')
        self.assertIsNotNone(evidence, 'B5 pinned-CLI observation evidence is required')
        self.assertEqual('passed', evidence['status'])
        self.assertEqual('B5', evidence['batch'])
        self.assertEqual('b0859fc61fea5dedfc898aece6b15893ddf4314e', evidence['sourceCommit'])
        b5 = [r for r in self.report['rows'] if r['batch'] == 'B5']
        self.assertEqual(26, len(b5))
        observed = {r['rowId'] for r in b5 if r['baselineObservation'] == 'observed-cli'}
        self.assertEqual({o['id'] for o in evidence['observations']}, observed)
        self.assertEqual(26, len(observed))
        self.assertEqual({'observed-cli'}, {r['baselineObservation'] for r in b5})
        self.assertEqual({'accepted': 17, 'accepted-unexpected': 2, 'accepted-unparseable': 6,
                          'refused': 1, 'static-not-applicable': 0}, evidence['outcomeCounts'])
        self.assertEqual(0, evidence['outcomeCounts'].get('scan-changed', 0))
        # Pinned-CLI reality of this batch, recorded not gated: `del Xmp.xmp.CreatorTool`
        # exits 0 on a standalone packet but leaves the bytes untouched (B3 WEBP analog).
        unexpected = [o for o in evidence['observations'] if o['outcome'] == 'accepted-unexpected']
        self.assertEqual(['EX0289-XMP-XMP-REMOVE-0001', 'EX0289-XMP-XMP-REMOVE-0003'],
                         sorted(o['id'] for o in unexpected))
        # `-pS` on a bare XMP packet: the pinned CLI prints
        # "Image type ... is not supported" (rc=1); recorded as refused, never a pass.
        refused = [o for o in evidence['observations'] if o['outcome'] == 'refused']
        self.assertEqual(['EX0289-XMP-INVENTORY-INVENTORY-0001'], [o['id'] for o in refused])
        # Sidecar presence probes: adjacent SIDECARPROBE <stem>.xmp files are never
        # consulted when the pinned CLI operates on the image.
        probes = [o for o in evidence['observations'] if o['sidecar'] == 'xmp-present']
        self.assertEqual({'EX0289-JPEG-XMP-READ-0009', 'EX0289-PNG-XMP-READ-0009',
                          'EX0289-WEBP-XMP-READ-0005', 'EX0289-TIFF-XMP-READ-0005'},
                         {o['id'] for o in probes})
        for probe in probes:
            self.assertFalse(probe['facts']['sidecarTokenInStdout'], probe['id'])
            self.assertTrue(probe['facts']['imageTokenInStdout'], probe['id'])
        for row in b5:
            self.assertEqual('not-assessed', row['verification'])

    def test_b6_batch_pending_or_fully_observed(self):
        rows = [r for r in self.report['rows'] if r['batch'] == 'B6']
        self.assertEqual(88, len(rows))
        self.assertEqual(4, sum(r['family'] == 'inventory' for r in rows))
        evidence = load_evidence('b6')
        if evidence is None:
            self.assertEqual({'not-run'}, {r['baselineObservation'] for r in rows})
            return
        self.assertEqual('passed', evidence['status'])
        self.assertEqual('B6', evidence['batch'])
        self.assertEqual({r['rowId'] for r in rows}, {o['id'] for o in evidence['observations']})
        self.assertEqual({'observed-cli'}, {r['baselineObservation'] for r in rows})
        self.assertEqual({'accepted': 84, 'nonzero-exit-artifact-produced': 4,
                          'static-not-applicable': 0}, evidence['outcomeCounts'])
        self.assertEqual(88, sum(o['facts'].get('scanPreserved') is True for o in evidence['observations']))
        nonzero = [o for o in evidence['observations'] if o['outcome'] == 'nonzero-exit-artifact-produced']
        self.assertEqual(4, len(nonzero))
        self.assertTrue(all(o['family'] == 'thumbnail' and o['operation'] == 'read' for o in nonzero))
        self.assertFalse(evidence['denominatorFrozen'])
        receipt_dir = ROOT / 'docs/research/evidence/e01-b6-census'
        receipt_path = receipt_dir / 'receipt.json'
        if receipt_path.exists():
            import hashlib
            receipt = json.loads(receipt_path.read_text())
            ci_path = receipt_dir / 'ci-observations.json'
            ci = json.loads(ci_path.read_text())
            self.assertEqual(receipt['annotationRecovery']['ciObservationsSha256'],
                             hashlib.sha256(ci_path.read_bytes()).hexdigest())
            self.assertEqual(receipt['annotationRecovery']['localObservationsSha256'],
                             hashlib.sha256((receipt_dir / 'observations.json').read_bytes()).hexdigest())
            self.assertEqual(evidence['observations'], ci['observations'])
            self.assertEqual(evidence['fixtureManifest'], ci['fixtureManifest'])
        for row in rows:
            self.assertEqual('not-assessed', row['verification'])

    def test_b7_rows_preserve_rights_blocks(self):
        rows = [r for r in self.report['rows'] if r['batch'] == 'B7']
        self.assertEqual(48, len(rows))
        evidence = load_evidence('b7')
        if evidence is None:
            self.assertEqual({'not-run'}, {r['baselineObservation'] for r in rows})
            return
        self.assertEqual('passed', evidence['status'])
        observed = {o['id'] for o in evidence['observations']}
        blocked = {o['id'] for o in evidence['blocked']}
        self.assertEqual(40, len(observed))
        self.assertEqual(8, len(blocked))
        self.assertFalse(observed & blocked)
        self.assertEqual({r['rowId'] for r in rows}, observed | blocked)
        self.assertEqual({r['rowId'] for r in rows if r['format'] == 'CR3'}, blocked)
        self.assertEqual(blocked, {r['rowId'] for r in rows if r['baselineObservation'] == 'blocked'})
        self.assertEqual(observed, {r['rowId'] for r in rows if r['baselineObservation'] == 'observed-cli'})
        self.assertEqual({'accepted': 32, 'blocked-rights': 8, 'refused': 8}, evidence['outcomeCounts'])
        self.assertTrue(all(o['facts']['inputUnchanged'] for o in evidence['observations']))
        self.assertTrue(all(o['layout'] == 'jxl-codestream' for o in evidence['observations'] if o['outcome'] == 'refused'))
        self.assertEqual(5, len(evidence['secondaryObservations']))
        self.assertTrue(all(o['exitCode'] == 0 and o['jsonParseError'] is None for o in evidence['secondaryObservations'].values()))
        self.assertFalse(evidence['denominatorFrozen'])
        receipt_dir = ROOT / 'docs/research/evidence/e01-b7-census'
        if (receipt_dir / 'receipt.json').exists():
            import copy, hashlib, re
            receipt = json.loads((receipt_dir / 'receipt.json').read_text())
            ci_path = receipt_dir / 'ci-observations.json'
            ci = json.loads(ci_path.read_text())
            self.assertEqual(receipt['annotationRecovery']['ciObservationsSha256'], hashlib.sha256(ci_path.read_bytes()).hexdigest())
            self.assertEqual(receipt['annotationRecovery']['localObservationsSha256'], hashlib.sha256((receipt_dir / 'observations.json').read_bytes()).hexdigest())
            def normalize(rows):
                result = copy.deepcopy(rows)
                for item in result:
                    item['facts']['stderr'] = re.sub(r'/[^\s:]*?/case\.(?:jxl|avif|heic|heif)', '<fixture>', item['facts']['stderr'])
                return result
            def stable_secondary(item):
                return {key: value for key, value in item['json'][0].items()
                        if not key.startswith('System:') and key != 'SourceFile'}
            self.assertEqual(normalize(evidence['observations']), normalize(ci['observations']))
            self.assertEqual(evidence['blocked'], ci['blocked'])
            self.assertEqual(evidence['fixtureManifest'], ci['fixtureManifest'])
            self.assertEqual(set(evidence['secondaryObservations']), set(ci['secondaryObservations']))
            for name in evidence['secondaryObservations']:
                self.assertEqual(stable_secondary(evidence['secondaryObservations'][name]),
                                 stable_secondary(ci['secondaryObservations'][name]))

    def test_verification_stays_not_assessed(self):
        for row in self.report['rows']:
            self.assertEqual('not-assessed', row['verification'])

    def test_observed_rows_carry_pinned_owned_synthetic_fixtures(self):
        manifests = {}
        for fmt in ('JPEG', 'PNG', 'TIFF', 'XMP'):
            manifests[fmt] = {e['file']: e for e in json.loads((ROOT / f'tests/fixtures/{fmt.lower()}/manifest.json').read_text())}
        for fmt in ('AVIF', 'HEIF', 'HEIC', 'JXL'):
            manifests[fmt] = {e['file']: e for e in json.loads((ROOT / 'tests/fixtures/b7/manifest.json').read_text())}
        manifests['DNG'] = {e['file']: e for e in json.loads((ROOT / 'tests/fixtures/dng/census-manifest.json').read_text())}
        manifests['WEBP'] = {e['file']: e for e in json.loads((ROOT / 'tests/fixtures/webp/census-manifest.json').read_text())}
        b8 = {e['file']: e for e in json.loads((ROOT / 'tests/fixtures/b8/census-manifest.json').read_text())}
        for fmt in ('PSD', 'JP2', 'EPS'):
            manifests[fmt] = b8
        b9 = {e['file']: e for e in json.loads((ROOT / 'tests/fixtures/b9/census-manifest.json').read_text())}
        for fmt in ('ARW', 'CR2', 'NEF', 'ORF', 'PEF', 'SRW'):
            manifests[fmt] = b9
        b10 = {e['file']: e for e in json.loads((ROOT / 'tests/fixtures/b10/census-manifest.json').read_text())}
        for fmt in ('MRW', 'RAF', 'RW2', 'SR2'):
            manifests[fmt] = b10
        # B11 keeps one catalog for nine formats, and one entry of it (the blocked vendor-private
        # CIFF cell) has no file at all: it can never be looked up by name here, which is the
        # point of it, so the dict is built as-is rather than filtered.
        b11 = {e['file']: e for e in json.loads((ROOT / 'tests/fixtures/b11/census-manifest.json').read_text())}
        for fmt in ('CRW', 'BMP', 'GIF', 'TGA', 'MOV', 'MKV', 'AVI', 'WAV', 'ASF'):
            manifests[fmt] = b11
        for row in self.report['rows']:
            if row['baselineObservation'] == 'not-applicable':
                self.assertIsNone(row['fixture'])
                self.assertIn('static not-applicable', row['notes'])
                continue
            if row['baselineObservation'] in ('not-run', 'blocked'):
                self.assertIsNone(row['fixture'])
                continue
            fixture = row['fixture']
            pin = manifests[row['format']][fixture['file']]
            self.assertEqual(pin['sha256'], fixture['sha256'])
            self.assertEqual(pin['length'], fixture['length'])
            self.assertEqual(pin['recipe'], fixture['recipe'])
            self.assertEqual('owned-synthetic', fixture['rights'])
            self.assertIn(f"{row['batch']} pinned-CLI observation", row['notes'])

    def test_b8_rows_partition_observed_and_blocked(self):
        rows = [r for r in self.report['rows'] if r['batch'] == 'B8']
        self.assertEqual(53, len(rows))
        self.assertEqual({'PSD', 'JP2', 'EPS', 'PGF', 'EXV', 'DCP'}, {r['format'] for r in rows})
        # Two PSD layouts, one JP2 and one EPS fixture are cleared; the pending formats
        # keep exactly one unobserved layout each.
        self.assertEqual({'flat-rgb-v1-metadata', 'flat-rgb-v1-no-resources'},
                         {r['layout'] for r in rows if r['format'] == 'PSD'})
        self.assertEqual({'jp2-boxed-rgb'}, {r['layout'] for r in rows if r['format'] == 'JP2'})
        self.assertEqual({'eps-dsc-bounding-box'}, {r['layout'] for r in rows if r['format'] == 'EPS'})
        # No B8 write suboperation is enumerated yet: no owned writable fixture is cleared.
        for row in rows:
            self.assertEqual('not-assessed', row['verification'])
            self.assertIn(row['operation'], ('inventory', 'read', 'extract'))
        evidence = load_evidence('b8')
        blocked_layouts = {'pgf-unobserved', 'exv-unobserved', 'dcp-unobserved'}
        if evidence is None:
            self.assertEqual({'not-run'}, {r['baselineObservation'] for r in rows})
            self.assertTrue(all(r['fixture'] is None for r in rows),
                            'B8 evidence arrives with its own receipt PR')
            return
        self.assertEqual('passed', evidence['status'])
        self.assertEqual('B8', evidence['batch'])
        self.assertFalse(evidence['denominatorFrozen'])
        self.assertEqual('not-assessed', evidence['verification'])
        observed = {o['id'] for o in evidence['observations']}
        blocked = {o['id'] for o in evidence['blocked']}
        self.assertEqual(31, len(observed))
        self.assertEqual(22, len(blocked))
        self.assertFalse(observed & blocked)
        self.assertEqual({r['rowId'] for r in rows}, observed | blocked)
        self.assertEqual(blocked, {r['rowId'] for r in rows if r['layout'] in blocked_layouts})
        self.assertEqual(blocked, {r['rowId'] for r in rows if r['baselineObservation'] == 'blocked'})
        self.assertEqual(observed, {r['rowId'] for r in rows if r['baselineObservation'] == 'observed-cli'})
        # Read-only probes must never mutate an owned fixture, and the pinned CLI is the
        # only oracle here, so every row is recorded, not judged.
        self.assertTrue(all(o['facts']['inputUnchanged'] for o in evidence['observations']))
        self.assertTrue(all(o['recordOnly'] and o['expectOk'] is None for o in evidence['observations']))
        self.assertEqual(0, evidence['outcomeCounts'].get('scan-changed', 0))
        # The shared `-pa` output is proved by the evidence itself, not only by prose:
        # every metadata-family read row on the resource-bearing PSD printed the same
        # bytes, so no row may be read as an independent per-family claim.
        rich_pa = [o for o in evidence['observations']
                   if o['flag'] == '-pa' and o['layout'] == 'flat-rgb-v1-metadata']
        self.assertEqual(4, len(rich_pa))
        self.assertEqual(1, len({o['facts']['stdoutSha256'] for o in rich_pa}))
        self.assertTrue(all(o['facts']['printedTokens'] == ['B8Artist', 'B8Keyword', 'B8Tool']
                             for o in rich_pa))
        self.assertTrue(all(not o['facts']['printedTokens'] for o in evidence['observations']
                            if o['layout'] != 'flat-rgb-v1-metadata'))
        # Receipt integrity: the committed report must be exactly the recovered payload.
        import hashlib
        receipt_dir = ROOT / 'docs/research/evidence/e01-b8-census'
        receipt_path = receipt_dir / 'receipt.json'
        if not receipt_path.exists():
            self.skipTest('B8 receipt not committed')
        receipt = json.loads(receipt_path.read_text())
        evidence_path = receipt_dir / 'observations.json'
        recovery = receipt['annotationRecovery']
        self.assertEqual(recovery['observationsFileSha256'],
                         hashlib.sha256(evidence_path.read_bytes()).hexdigest())
        self.assertEqual(recovery['rawPayloadSha256'],
                         hashlib.sha256(json.dumps(evidence, separators=(',', ':'),
                                                   sort_keys=True).encode()).hexdigest())
        self.assertEqual(3, recovery['chunks'])
        self.assertEqual(3, recovery['ciChunks'])
        self.assertEqual(evidence['sourceCommit'], receipt['run']['headCommit'])
        # Cross-run reproduction. There is no local B8 run, so the second sample is
        # another CI run of the same recorder on a later head: the observation payload
        # must match byte-for-byte, while the compiled reference binary legitimately
        # differs because the build embeds its own timestamp.
        ci_path = receipt_dir / 'ci-observations.json'
        ci = json.loads(ci_path.read_text())
        self.assertEqual(recovery['ciObservationsSha256'],
                         hashlib.sha256(ci_path.read_bytes()).hexdigest())
        for section in ('observations', 'blocked', 'fixtureManifest', 'outcomeCounts'):
            self.assertEqual(evidence[section], ci[section], f'{section} drifted across runs')
        self.assertNotEqual(evidence['sourceCommit'], ci['sourceCommit'])
        self.assertNotEqual(evidence['baseline']['executableSha256'], ci['baseline']['executableSha256'])
        for key in ('cmakeCacheSha256', 'licenseSha256', 'sourceCommit', 'version'):
            self.assertEqual(evidence['baseline'][key], ci['baseline'][key])
        # The ExifTool report prints file timestamps, so its raw stdout digest cannot
        # match across runs by construction: compare the stable facts, not the envelope.
        def stable(item):
            return {'exitCode': item['exitCode'], 'interpretation': item['interpretation'],
                    'fixtureSha256': item['fixtureSha256'], 'fixtureLength': item['fixtureLength'],
                    'json': [{k: v for k, v in entry.items()
                              if not k.startswith('System:') and k != 'SourceFile'}
                             for entry in item['json']]}
        self.assertEqual({name: stable(item) for name, item in evidence['secondaryObservations'].items()},
                         {name: stable(item) for name, item in ci['secondaryObservations'].items()})
        self.assertNotEqual(evidence['secondaryObservations']['owned-flat-rgb.psd']['stdoutSha256'],
                            ci['secondaryObservations']['owned-flat-rgb.psd']['stdoutSha256'])
        self.assertEqual(len(set(evidence['baseline']['verboseVersion'].splitlines())
                           ^ set(ci['baseline']['verboseVersion'].splitlines())), 2)
        self.assertEqual(len(evidence['observations']), receipt['tally']['observed'])
        self.assertEqual(len(evidence['blocked']), receipt['tally']['blocked'])
        self.assertTrue(receipt['tally']['inputsUnchanged'])
        self.assertEqual(0, receipt['tally']['artifactsProduced'])
        self.assertFalse(receipt['denominatorFrozen'])
        self.assertTrue(any('verification' in claim for claim in receipt['notClaimed']))

    def test_scope_is_observed_batches_plus_inventory(self):
        families = {r['family'] for r in self.report['rows']}
        self.assertEqual({'exif', 'comments', 'icc', 'xmp', 'iptc', 'thumbnail', 'inventory'}, families)
        # B11 adds 12 inventory rows on the seed-grid layouts (five CIFF, two BMP, two GIF,
        # three TGA) and one per container stub, so the inventory track grows 42 -> 60.
        self.assertEqual(60, sum(1 for r in self.report['rows'] if r['family'] == 'inventory'))
        self.assertEqual(135, sum(1 for r in self.report['rows'] if r['batch'] == 'B0'))
        self.assertEqual(58, sum(1 for r in self.report['rows'] if r['batch'] == 'B1'))
        self.assertEqual(67, sum(1 for r in self.report['rows'] if r['batch'] == 'B2'))
        self.assertEqual(43, sum(1 for r in self.report['rows'] if r['batch'] == 'B3'))
        self.assertEqual(50, sum(1 for r in self.report['rows'] if r['batch'] == 'B4'))
        self.assertEqual(26, sum(1 for r in self.report['rows'] if r['batch'] == 'B5'))
        self.assertEqual(88, sum(1 for r in self.report['rows'] if r['batch'] == 'B6'))
        self.assertEqual(48, sum(1 for r in self.report['rows'] if r['batch'] == 'B7'))
        self.assertEqual(53, sum(1 for r in self.report['rows'] if r['batch'] == 'B8'))
        self.assertEqual(96, sum(1 for r in self.report['rows'] if r['batch'] == 'B9'))
        self.assertEqual(64, sum(1 for r in self.report['rows'] if r['batch'] == 'B10'))
        self.assertEqual(95, sum(1 for r in self.report['rows'] if r['batch'] == 'B11'))
        self.assertEqual(823, len(self.report['rows']))

    def test_b10_rows_partition_observed_and_blocked(self):
        rows = [r for r in self.report['rows'] if r['batch'] == 'B10']
        self.assertEqual(64, len(rows))
        self.assertEqual({'MRW', 'RAF', 'RW2', 'SR2'}, {r['format'] for r in rows})
        cleared = {'MRW': 'raw-mrw-ttw-embedded-tiff', 'RAF': 'raw-raf-fujifilm-be-offsets',
                   'RW2': 'raw-rw2-ii-55-root-24', 'SR2': 'raw-tiff-ep-ii-sr2'}
        for fmt, layout in sorted(cleared.items()):
            of_fmt = [r for r in rows if r['format'] == fmt]
            self.assertEqual(16, len(of_fmt), fmt)
            # one authored layout and one blocked vendor-private cell, eight rows each
            self.assertEqual(8, len([r for r in of_fmt if r['layout'] == layout]), fmt)
            self.assertEqual(8, len([r for r in of_fmt if r['layout'] ==
                                     f'raw-{fmt.lower()}-vendor-private-unobserved']), fmt)
        for row in rows:
            self.assertEqual('not-assessed', row['verification'])
            self.assertIn(row['operation'], ('inventory', 'read', 'extract'))
        # The denominator is not moved by this batch: no percentage, no freeze.
        self.assertFalse(self.report['denominatorFrozen'])
        self.assertNotIn('percentage', str(self.report['coverage']))
        evidence = load_evidence('b10')
        if evidence is None:
            # Pre-observation state: the batch is enumerated but nothing has been claimed
            # from it, and no row may point at a fixture the CI has not read.
            self.assertEqual({'not-run'}, {r['baselineObservation'] for r in rows})
            self.assertTrue(all(r['fixture'] is None for r in rows),
                            'B10 evidence arrives with its own receipt commit')
            return
        self.assertEqual('passed', evidence['status'])
        self.assertEqual('B10', evidence['batch'])
        self.assertFalse(evidence['denominatorFrozen'])
        self.assertEqual('not-assessed', evidence['verification'])
        observed = {o['id'] for o in evidence['observations']}
        blocked = {o['id'] for o in evidence['blocked']}
        self.assertEqual(32, len(observed))
        self.assertEqual(32, len(blocked))
        self.assertFalse(observed & blocked)
        self.assertEqual({r['rowId'] for r in rows}, observed | blocked)
        self.assertEqual({r['rowId'] for r in rows if 'vendor-private' in r['layout']}, blocked)
        self.assertEqual(blocked, {r['rowId'] for r in rows if r['baselineObservation'] == 'blocked'})
        self.assertEqual(observed, {r['rowId'] for r in rows
                                    if r['baselineObservation'] == 'observed-cli'})
        self.assertTrue(all(o['facts']['inputUnchanged'] for o in evidence['observations']))
        self.assertTrue(all(o['recordOnly'] for o in evidence['observations']))
        self.assertEqual(0, evidence['outcomeCounts'].get('scan-changed', 0))
        # Recognition is recorded from the fixture bytes, so the row must name the reader
        # the pinned registry actually dispatches to -- and for SR2 that is the *generic*
        # TIFF reader, because the registry has no SR2 entry of its own.
        # The observation records carry `layout`, not `format`: the format is a property of
        # the census row, so recovery reads it from the row table rather than trusting the
        # recorder to have repeated it.
        fmt_of = {r['rowId']: r['format'] for r in rows}
        dispatch = {fmt: {o['facts']['container']['dispatch'] for o in evidence['observations']
                          if fmt_of[o['id']] == fmt} for fmt in ('MRW', 'RAF', 'RW2', 'SR2')}
        self.assertEqual({'mrw'}, dispatch['MRW'])
        self.assertEqual({'raf'}, dispatch['RAF'])
        self.assertEqual({'rw2'}, dispatch['RW2'])
        self.assertEqual({'tiff'}, dispatch['SR2'])
        # Every extract row must carry the pinned build's byte-count semantics; a status
        # that is not the byte count is a finding, and it stays in the record.
        extracts = [o for o in evidence['observations'] if o['operation'] == 'extract']
        self.assertEqual(4, len(extracts))
        for item in extracts:
            self.assertTrue(item['facts']['extractSemantics']['exitCodeIsByteCountInPinnedBuild'])
            self.assertEqual(item['exitCode'], item['facts']['extractSemantics']['artifactBytes'],
                             item['id'])
        # An MRW -pS refusal is a property of the pinned build (no printStructure override),
        # not a fixture fault: it must be recorded as a refusal and never relabelled.
        mrw_scan = [o for o in evidence['observations']
                    if fmt_of[o['id']] == 'MRW' and o['flag'] == '-pS']
        self.assertEqual(1, len(mrw_scan))
        self.assertEqual('refused', mrw_scan[0]['outcome'],
                         'MRW has no printStructure override, so -pS must stay a refusal')
        # 'observed-cli' means the probe ran and was recorded whole; it is not a claim that
        # the build accepted the file. So a row whose outcome is a refusal must still carry
        # that refusal in its own record instead of disappearing from the evidence.
        for item in evidence['observations']:
            if item['outcome'] == 'refused':
                self.assertTrue(item['facts']['stderr'], item['id'])
        # Blocked rows carry a per-format reason, never a bare 'unsupported'.
        reasons = {b['reason'] for b in evidence['blocked']}
        self.assertEqual(4, len(reasons))
        self.assertTrue(all('blocked' in r for r in reasons))

    def test_b11_rows_are_the_seed_grid_plus_inventory_only_containers(self):
        rows = [r for r in self.report['rows'] if r['batch'] == 'B11']
        self.assertEqual(95, len(rows))
        self.assertEqual({'CRW', 'BMP', 'GIF', 'TGA', 'MOV', 'MKV', 'AVI', 'WAV', 'ASF'},
                         {r['format'] for r in rows})
        per_format = {fmt: len([r for r in rows if r['format'] == fmt])
                      for fmt in ('CRW', 'BMP', 'GIF', 'TGA', 'MOV')}
        # CRW keeps five layouts of eight rows (four fixtures plus the blocked vendor-private
        # cell); BMP and GIF two of seven each; TGA three of seven; MOV two inventory rows, one
        # per brand. Seven rather than eight because a '-' for the thumbnail family means no
        # extract row is invented for a reader that has no thumbnail to extract.
        self.assertEqual({'CRW': 40, 'BMP': 14, 'GIF': 14, 'TGA': 21, 'MOV': 2}, per_format)
        # The trap this batch was reviewed for: a container stub must never grow a metadata
        # family cell, because there is no claim to observe and none to negate.
        for fmt in ('MKV', 'AVI', 'WAV', 'ASF'):
            self.assertEqual(1, len([r for r in rows if r['format'] == fmt]), fmt)
            self.assertEqual({'inventory'}, {r['family'] for r in rows if r['format'] == fmt})
        for row in rows:
            self.assertIn(row['operation'], ('inventory', 'read', 'extract'))
            self.assertEqual('not-assessed', row['verification'])
        blocked = [r for r in rows if r['layout'] == 'ciff-vendor-private-unobserved']
        self.assertEqual(8, len(blocked))
        self.assertEqual({'CRW'}, {r['format'] for r in blocked})
        # Only CRW has a documented Read/Write claim in the pinned table, so only CRW carries
        # a claim-bearing exif, comments or thumbnail row.
        claimed = {(r['format'], r['family']) for r in rows
                   if r['operation'] == 'read' and not r['noDocumentedClaim']}
        self.assertEqual({('CRW', 'exif'), ('CRW', 'comments'), ('CRW', 'thumbnail')}, claimed)
        evidence = load_evidence('b11')
        if evidence is None:
            # Pre-observation state: enumerated, nothing claimed, and no row may point at a
            # fixture the CI has not read.
            self.assertEqual({'not-run'}, {r['baselineObservation'] for r in rows})
            self.assertTrue(all(r['fixture'] is None for r in rows),
                            'B11 evidence arrives with its own receipt commit')
            return
        self.assertEqual('passed', evidence['status'])
        self.assertEqual('B11', evidence['batch'])
        self.assertFalse(evidence['denominatorFrozen'])
        self.assertEqual('not-assessed', evidence['verification'])
        observed = {o['id'] for o in evidence['observations']}
        blocked_ids = {o['id'] for o in evidence['blocked']}
        self.assertEqual(87, len(observed))
        self.assertEqual(8, len(blocked_ids))
        self.assertFalse(observed & blocked_ids)
        self.assertEqual({r['rowId'] for r in rows}, observed | blocked_ids)
        self.assertEqual(blocked_ids, {r['rowId'] for r in rows
                                       if 'vendor-private' in r['layout']})
        self.assertTrue(all(o['facts']['inputUnchanged'] for o in evidence['observations']))
        self.assertTrue(all(o['recordOnly'] for o in evidence['observations']))
        self.assertEqual(0, evidence['outcomeCounts'].get('scan-changed', 0))
        fmt_of = {r['rowId']: r['format'] for r in rows}
        layout_of = {r['rowId']: r['layout'] for r in rows}
        # Which reader the pinned registry reaches is the whole point of the container half:
        # the brand decides QuickTime versus BMFF, WAV shares the RIFF reader with AVI, and a
        # footer-signed .targa under a non-TGA name reaches no reader at all.
        dispatch, by_layout = {}, {}
        for item in evidence['observations']:
            dispatch.setdefault(fmt_of[item['id']], set()).add(
                item['facts']['container']['dispatch'])
            by_layout.setdefault(layout_of[item['id']], set()).add(
                item['facts']['container']['dispatch'])
        self.assertEqual({'crw'}, dispatch['CRW'])
        self.assertEqual({'bmp'}, dispatch['BMP'])
        self.assertEqual({'gif'}, dispatch['GIF'])
        self.assertEqual({'tga', 'none'}, dispatch['TGA'])
        self.assertEqual({'qtime', 'bmff'}, dispatch['MOV'])
        self.assertEqual({'mkv'}, dispatch['MKV'])
        self.assertEqual({'riff'}, dispatch['AVI'])
        self.assertEqual({'riff'}, dispatch['WAV'])
        self.assertEqual({'asf'}, dispatch['ASF'])
        self.assertEqual({'qtime'}, by_layout['qtime-known-brand-v1'])
        self.assertEqual({'bmff'}, by_layout['qtime-unknown-brand-v1'])
        self.assertEqual({'tga'}, by_layout['tga-full-v1'])
        self.assertEqual({'none'}, by_layout['tga-signature-wrong-name-v1'])
        # `-et` is `Extract::writeThumbnail`'s return (app/actions.cpp:823-864), which is the
        # byte count only on the path that writes a thumbnail: a fixture carrying a CIFF 0x2008
        # component exits with the length it wrote, one with Exif data but no thumbnail exits 0,
        # and one whose reader filled nothing exits -3, narrowed to 253 because main() ends with
        # `return static_cast<unsigned int>(returnCode) % 256` (app/exiv2.cpp:195). The blanket
        # rule this batch was written to confirm -- exitCode == artifactBytes for every extract
        # row -- held on three rows out of four, so what is asserted below is the four shapes,
        # derived from what the CLI did. It deliberately does not read the recorder's own
        # `statusShape` label, so the evidence stays checkable whatever the recorder calls it.
        extracts = {o['layout']: o for o in evidence['observations']
                    if o['operation'] == 'extract'}
        self.assertEqual(4, len(extracts))
        self.assertEqual(
            {'ciff-mapped-v1': (0, 0, 'zero-exit-no-artifact', 'thumbnail'),
             'ciff-preview-plus-thumb-v1': (137, 137, 'nonzero-exit-artifact-produced', ''),
             'ciff-thumb-only-v1': (137, 137, 'nonzero-exit-artifact-produced', ''),
             'ciff-unmapped-only-v1': (253, 0, 'refused', 'No Exif data')},
            {layout: (item['exitCode'],
                      item['facts']['extractSemantics']['artifactBytes'],
                      item['outcome'],
                      'thumbnail' if 'thumbnail' in item['facts']['stderr']
                      else 'No Exif data' if 'No Exif data' in item['facts']['stderr']
                      else '')
             for layout, item in sorted(extracts.items())})
        for layout, item in sorted(extracts.items()):
            semantics = item['facts']['extractSemantics']
            self.assertEqual(item['exitCode'] == semantics['artifactBytes'],
                             semantics['exitCodeEqualsArtifactBytes'], layout)
            # Two recorder invariants, both easy to lose silently: the no-thumbnail flag is set
            # from the CLI's own wording, and a byte count that is not backed by a file on disk
            # would mean the artifacts list and the semantics disagree.
            self.assertEqual('thumbnail' in item['facts']['stderr'],
                             semantics['reportedNoThumbnail'], layout)
            self.assertEqual(bool(semantics['artifactBytes']), bool(item['facts']['artifacts']), layout)
            self.assertEqual(sum(a['length'] for a in item['facts']['artifacts']),
                             semantics['artifactBytes'], layout)
        self.assertEqual({'ciff-thumb-only-v1', 'ciff-preview-plus-thumb-v1'},
                         {layout for layout, item in extracts.items()
                          if item['facts']['extractSemantics']['artifactBytes']})
        # The two rows that wrote a thumbnail wrote exactly what the fixture says is in there,
        # and only those two are offered a prediction: predictedArtifactBytes exists solely where
        # a 0x2008 component exists, so it is a check on the recorder, never on CRW support. A
        # mismatch stays a finding about the recorder rather than a gate, so the field must exist
        # and is never asserted to be true.
        self.assertEqual({'ciff-thumb-only-v1': 137, 'ciff-preview-plus-thumb-v1': 137},
                         {layout: item['facts']['extractSemantics']['predictedArtifactBytes']
                          for layout, item in extracts.items()
                          if item['facts']['extractSemantics'].get('predictedArtifactBytes') is not None})
        for item in extracts.values():
            if 'predictionMatched' in item['facts']['extractSemantics']:
                self.assertIn(item['facts']['extractSemantics']['predictionMatched'], (True, False),
                              item['id'])
        # Seven layouts never reach a reader at all, and each keeps the build's own wording so no
        # row can be read downstream as "this format is unsupported". Six are the surprise this
        # batch exists to record: the magic test in the pinned source matches their bytes, yet
        # ImageFactory::open produced no instance (the review document predicted BMP, QuickTime,
        # Matroska and WAV would be read). The seventh is the prediction coming true: TGA is
        # claimed by file *name* in this build, so a footer-signed .targa reaches no reader and is
        # recorded as dispatch 'none' rather than as a TGA failure.
        refused = {item['layout'] for item in evidence['observations']
                   if 'unknown image type' in item['facts']['stderr']}
        self.assertEqual({'bmp-dib-v3', 'bmp-os2-coreheader-v1', 'mkv-ebml-header-v1',
                          'qtime-known-brand-v1', 'qtime-unknown-brand-v1', 'riff-wav-fmt-v1',
                          'tga-signature-wrong-name-v1'}, refused)
        self.assertEqual({'none'}, {item['facts']['container']['dispatch'] for item in
                                    evidence['observations']
                                    if item['layout'] == 'tga-signature-wrong-name-v1'})

    def test_evidence_files_hashed_by_a_receipt_survive_checkout_conversion(self):
        """A receipt that hashes committed bytes is only meaningful under one checkout mode.

        Windows runners check text out with ``core.autocrlf=true``, which rewrites an unpinned LF
        file to CRLF; every digest in the receipt then stops matching, and no Linux or macOS job
        can see it. B10 lost three pushes to that (``net8.0``/``net10.0 / windows-latest``, runs
        42744015517 and 42746390202). The first version of this test asked ``git check-attr`` for
        the attributes and turned out to be itself platform-fragile -- the Windows output did not
        parse into anything, so every file looked unpinned -- which is why it now compares the
        checked-out bytes with the blob git stores: the consequence, not the configuration that is
        supposed to prevent it. A file that differs from ``HEAD`` here is either missing a
        ``text eol=lf`` rule or was edited without regenerating the receipt that hashes it.
        """
        import hashlib
        import subprocess
        git = subprocess.run(['git', '-C', str(ROOT), 'rev-parse', '--git-dir'],
                             capture_output=True, text=True)
        if git.returncode != 0:
            self.skipTest('git metadata unavailable; no checkout conversion can occur')
        evidence = ROOT / 'docs/research/evidence'
        hashed = []
        for directory in sorted(evidence.iterdir()):
            receipt = directory / 'receipt.json'
            if not receipt.exists():
                continue
            text = receipt.read_text()
            for path in sorted(directory.glob('*.json')):
                if path.name == 'receipt.json':
                    continue
                if hashlib.sha256(path.read_bytes()).hexdigest() in text:
                    hashed.append(path.relative_to(ROOT).as_posix())
        self.assertTrue(hashed, 'expected receipts to hash their evidence files')
        # Two halves, because they fail in different places. On any host that would rewrite the
        # bytes, the comparison below catches it for real; the attribute statement is checked
        # everywhere so a batch that forgets the rule fails on the first Linux run too.
        attributes = (ROOT / '.gitattributes').read_text()
        self.assertIn('docs/research/evidence/**/*.json text eol=lf', attributes,
                      'evidence digests need a line-ending pin for CRLF-checkout hosts')
        rewritten = []
        for rel in hashed:
            blob = subprocess.run(['git', '-C', str(ROOT), 'show', f'HEAD:{rel}'],
                                  capture_output=True)
            if blob.returncode != 0:
                # Untracked on purpose in a working copy: nothing to compare against yet.
                continue
            if (ROOT / rel).read_bytes() != blob.stdout:
                rewritten.append(rel)
        self.assertEqual([], rewritten,
                         'checked-out bytes differ from the blobs the receipts hashed: pin these '
                         'files with `text eol=lf` in .gitattributes (or regenerate the receipt)')

    def test_b10_receipt_and_print_forms_are_self_consistent(self):
        """The committed evidence must be exactly the recovered payload, and the findings this
        batch exists to record must stay stated rather than paraphrased away."""
        import hashlib
        evidence_dir = ROOT / 'docs/research/evidence/e01-b10-census'
        receipt_path = evidence_dir / 'receipt.json'
        if not receipt_path.exists():
            self.skipTest('B10 receipt not committed')
        receipt = json.loads(receipt_path.read_text())
        evidence = load_evidence('b10')
        ci_path = evidence_dir / 'ci-observations.json'
        if not ci_path.exists():
            self.skipTest('B10 cross-run sample not committed')
        ci = json.loads(ci_path.read_text())
        recovery = receipt['annotationRecovery']
        self.assertEqual(recovery['observationsFileSha256'],
                         hashlib.sha256((evidence_dir / 'observations.json').read_bytes()).hexdigest())
        self.assertEqual(recovery['rawPayloadSha256'],
                         hashlib.sha256(json.dumps(evidence, sort_keys=True,
                                                    separators=(',', ':')).encode()).hexdigest())
        self.assertEqual(recovery['chunks'], recovery['expectedChunks'])
        self.assertTrue(recovery['markerVerified'], 'the count marker must have been matched')
        # The preflight is the only record that keeps stdout text, so its own digest is pinned.
        pre_dir = ROOT / 'docs/research/evidence/e01-b10-preflight'
        pre = json.loads((pre_dir / 'observations.json').read_text())
        pre_recovery = receipt['preflightRecovery']
        self.assertEqual(pre_recovery['observationsFileSha256'],
                         hashlib.sha256((pre_dir / 'observations.json').read_bytes()).hexdigest())
        self.assertEqual(pre_recovery['chunks'], pre_recovery['expectedChunks'])
        self.assertEqual('passed', pre['status'])
        self.assertEqual(evidence['sourceCommit'], pre['sourceCommit'])
        # Tally against the rows: 64 rows, half observed, half blocked, nothing mutated.
        self.assertEqual(64, receipt['tally']['censusRows'])
        self.assertEqual(32, receipt['tally']['observed'])
        self.assertEqual(32, receipt['tally']['blocked'])
        self.assertTrue(receipt['tally']['inputsUnchanged'])
        self.assertEqual(2, receipt['tally']['artifactsProduced'])
        self.assertEqual(evidence['outcomeCounts'], receipt['tally']['outcomeCounts'])
        self.assertEqual(receipt['tally']['artifactsProduced'],
                         sum(1 for o in evidence['observations'] if o['facts']['artifacts']))
        forms = receipt['printForms']
        self.assertEqual({'MRW', 'RAF', 'RW2', 'SR2'}, set(forms))
        # MRW: the TTW block is read for Exif yet the inventory and ICC prints throw, because
        # every -p mode funnels through Image::printStructure. Refused rows stay refused.
        self.assertEqual('refused', forms['MRW']['inventory']['outcome'])
        self.assertEqual('refused', forms['MRW']['icc']['outcome'])
        self.assertEqual(3785, forms['MRW']['pa']['stdoutLength'])
        self.assertEqual(['B10Artist', 'B10Tool'], forms['MRW']['pa']['tokens'])
        # RAF: the preview comment reaches -pc verbatim, and the ICC print succeeds empty.
        self.assertEqual(14, forms['RAF']['comment']['stdoutLength'])
        self.assertEqual(hashlib.sha256(b'B10RafComment\n').hexdigest(),
                         forms['RAF']['comment']['stdoutSha256'],
                         'the RAF comment print must be exactly the authored string plus a newline')
        self.assertEqual(0, forms['RAF']['icc']['stdoutLength'])
        self.assertEqual('accepted', forms['RAF']['icc']['outcome'])
        # RW2: the pana mapping names the root directory, so the authored IFD0 ASCII pair is
        # not surfaced. Recorded as an observation about the print, not about the file.
        self.assertEqual([], forms['RW2']['pa']['tokens'])
        self.assertTrue(forms['RW2']['inventory']['stdoutLength'] > 2000)
        # The 202-byte RW2 ICC print is the "RW2 IMAGE" banner plus the 192-byte profile, the
        # same mechanism that explains B9's ORF 202: stated in the receipt, so a future change
        # in print form shows up as a failing assertion instead of a quiet drift.
        self.assertEqual(202, forms['RW2']['icc']['stdoutLength'])
        self.assertEqual(192, forms['SR2']['icc']['stdoutLength'])
        self.assertEqual(192, receipt['iccPrintForm']['authoredProfileLength'])
        self.assertIn('src/rw2image.cpp:63', receipt['iccPrintForm']['note'])
        self.assertIn('ORF', receipt['iccPrintForm']['b9AnomalyResolved'])
        # -et: the byte-count rule holds on every extract row, including the two that produced
        # nothing, which is why 0-with-no-artifact is its own outcome label.
        trip = receipt['thumbnailRoundTrip']
        self.assertTrue(trip['byteExactCopyProducedByPinnedCli'])
        self.assertEqual(['MRW', 'SR2'], trip['extractionSucceededFor'])
        self.assertEqual(['RAF', 'RW2'], trip['extractionReturnedNothingFor'])
        extracts = [o for o in evidence['observations'] if o['operation'] == 'extract']
        for item in extracts:
            self.assertEqual(item['exitCode'], item['facts']['extractSemantics']['artifactBytes'],
                             item['id'])
        for item in [o for o in extracts if o['facts']['artifacts']]:
            self.assertEqual([trip['authoredThumbnailSha256']],
                             [a['sha256'] for a in item['facts']['artifacts']])
        self.assertEqual(50, receipt['annotationBudget']['perCheckRunLimit'])
        self.assertLessEqual(receipt['annotationBudget']['thisRunAnnotationsUsed'], 50)
        self.assertIn('42729776775', receipt['annotationBudget']['finding'])
        self.assertFalse(receipt['denominatorFrozen'])
        self.assertEqual('not-assessed', receipt['verification'])
        self.assertEqual({'MRW': ['mrw'], 'RAF': ['raf'], 'RW2': ['rw2'], 'SR2': ['tiff']},
                         receipt['recognition']['dispatchByFormat'])
        # Reproduction is a bounded claim: the second sample is another CI run of the same
        # recorder on a later head, so the payload must match section-for-section while the
        # reference binary legitimately differs (the build embeds its own timestamp).
        reproduction = receipt['reproduction']
        self.assertEqual('observations are reproducible; the compiled reference binary is not',
                         reproduction['claim'])
        for run in reproduction['runs']:
            self.assertRegex(run['head'], '^[0-9a-f]{40}$')
        self.assertEqual({evidence['sourceCommit'], ci['sourceCommit']},
                         {r['head'] for r in reproduction['runs'][:2]},
                         'the first two runs must be the two PR heads')
        self.assertNotEqual(evidence['sourceCommit'], ci['sourceCommit'])
        for section in ('observations', 'blocked', 'fixtureManifest', 'outcomeCounts'):
            self.assertEqual(evidence[section], ci[section], f'{section} drifted across runs')
        self.assertIn('observations', reproduction['identicalSections'])
        self.assertEqual(['baseline', 'secondaryObservations', 'sourceCommit'],
                         reproduction['differingSections'])
        self.assertEqual([], reproduction['secondaryDriftBeyondFileDates'],
                         'only ExifTool file dates may move between runs')
        self.assertTrue(reproduction['baselineDifference']['executableSha256Differs'])
        self.assertTrue(reproduction['baselineDifference']['cmakeCacheSha256Equal'])
        self.assertTrue(reproduction['baselineDifference']['effectiveCmakeEqual'])
        self.assertEqual(recovery['ciChunks'], recovery['expectedChunks'])
        self.assertEqual(recovery['ciObservationsSha256'],
                         hashlib.sha256(ci_path.read_bytes()).hexdigest())
        self.assertEqual(recovery['ciRawPayloadSha256'],
                         hashlib.sha256(json.dumps(ci, sort_keys=True,
                                                    separators=(',', ':')).encode()).hexdigest())
        self.assertEqual(recovery['ciRunId'], 42744015517)
        # The third sample is the post-merge master run: the merged tree re-observing the
        # committed evidence, which is the strongest form of "this record is stable" available
        # here. It is kept in its own block so a third run cannot overwrite the second one's
        # record, and its digests are pinned the same way.
        master_path = evidence_dir / 'master-observations.json'
        if not master_path.exists():
            self.skipTest('B10 post-merge master sample not committed')
        master = json.loads(master_path.read_text())
        block = reproduction['masterComparison']
        self.assertEqual(recovery['masterObservationsSha256'],
                         hashlib.sha256(master_path.read_bytes()).hexdigest())
        self.assertEqual(recovery['masterRawPayloadSha256'],
                         hashlib.sha256(json.dumps(master, sort_keys=True,
                                                    separators=(',', ':')).encode()).hexdigest())
        self.assertEqual(recovery['masterRunId'], 42747632654)
        self.assertEqual(block['head'], master['sourceCommit'])
        for section in ('observations', 'blocked', 'fixtureManifest', 'outcomeCounts'):
            self.assertEqual(evidence[section], master[section], f'{section} drifted on master')
        self.assertEqual(['baseline', 'secondaryObservations', 'sourceCommit'],
                         block['differingSections'])
        self.assertEqual([], block['secondaryDriftBeyondFileDates'])
        self.assertEqual(3, len(reproduction['runs']))
        self.assertIn('3 independent CI runs', reproduction['status'])
        self.assertEqual(sorted({evidence['sourceCommit'], ci['sourceCommit'], master['sourceCommit']}),
                         sorted(run['head'] for run in reproduction['runs']))
        self.assertEqual({'observations.json', 'ci-observations.json', 'master-observations.json'},
                         {run['payload'] for run in reproduction['runs']})

    def test_b11_receipt_and_cross_run_samples_are_self_consistent(self):
        """B11's census must rest on a payload that ties out to its own receipt, and the second
        run's payload may differ only in the fields this batch renamed."""
        import hashlib
        evidence_dir = ROOT / 'docs/research/evidence/e01-b11-census'
        receipt_path = evidence_dir / 'receipt.json'
        if not receipt_path.exists():
            self.skipTest('B11 receipt not committed')
        canonical = lambda doc: json.dumps(doc, sort_keys=True, separators=(',', ':')).encode()
        receipt = json.loads(receipt_path.read_text())
        census_raw = (evidence_dir / 'observations.json').read_bytes()
        evidence = json.loads(census_raw)
        prior_path = evidence_dir / 'prior-run-observations.json'
        self.assertTrue(prior_path.exists(), 'B11 cross-run sample must be committed beside the census')
        prior_raw = prior_path.read_bytes()
        prior = json.loads(prior_raw)

        recovery = receipt['annotationRecovery']
        self.assertEqual(recovery['observationsFileSha256'], hashlib.sha256(census_raw).hexdigest())
        self.assertEqual(recovery['rawPayloadSha256'], hashlib.sha256(canonical(evidence)).hexdigest())
        self.assertEqual(recovery['chunks'], recovery['expectedChunks'])
        self.assertEqual(recovery['chunks'], recovery['priorChunks'],
                         'the two runs must have paid the same recovery cost')
        self.assertTrue(recovery['markerVerified'], 'the count marker must have been matched')
        self.assertEqual(evidence['sourceCommit'], receipt['run']['headCommit'])
        self.assertEqual('passed', evidence['status'])

        # The four payloads are two same-head pairs, so each cross-run comparison is between a
        # build and a build, never between an observation and a different question.
        cross = receipt['censusCrossRun']
        self.assertEqual(cross['priorRun']['head'], prior['sourceCommit'])
        self.assertEqual(receipt['run']['secondSampleRun'], cross['priorRun']['runId'],
                         'the run object must name the same second run the comparison uses')
        self.assertNotEqual(receipt['run']['id'], receipt['run']['secondSampleRun'])
        self.assertEqual(cross['priorRun']['observationsFileSha256'], hashlib.sha256(prior_raw).hexdigest())
        self.assertEqual(cross['priorRun']['rawPayloadSha256'], hashlib.sha256(canonical(prior)).hexdigest())
        self.assertEqual(['baseline', 'observations', 'secondaryObservations', 'sourceCommit'],
                         cross['differingSections'])
        for section in ('outcomeCounts', 'fixtureManifest', 'blocked', 'environment', 'status',
                        'verification', 'denominatorFrozen', 'limitations'):
            self.assertIn(section, cross['identicalSections'], section)
        self.assertEqual([], cross['rowsDifferingBeyondShapeLabel'])
        self.assertEqual(['ciff-mapped-v1 -et', 'ciff-preview-plus-thumb-v1 -et',
                          'ciff-thumb-only-v1 -et', 'ciff-unmapped-only-v1 -et'],
                         cross['rowsDifferingOnlyByShapeLabel'])
        self.assertTrue(cross['observationCountEqual'] and cross['outcomeCountsEqual'])
        self.assertTrue(cross['blockedIdsEqual'] and cross['fixtureManifestEqual'])
        stated = re.match(r'(\d+) of (\d+) observations differ', cross['reading'])
        self.assertEqual([len(cross['rowsDifferingOnlyByShapeLabel']), len(evidence['observations'])],
                         [int(stated.group(1)), int(stated.group(2))],
                         'the prose tally must be the data tally, not a remembered number')
        pre_dir = ROOT / 'docs/research/evidence/e01-b11-preflight'
        pre = json.loads((pre_dir / 'observations.json').read_text())
        pre_prior = json.loads((pre_dir / 'prior-run-observations.json').read_text())
        self.assertEqual(evidence['sourceCommit'], pre['sourceCommit'])
        self.assertEqual(prior['sourceCommit'], pre_prior['sourceCommit'])
        self.assertTrue(receipt['preflightRecovery']['markerVerified'])
        comparison = receipt['reproduction']['priorRunComparison']
        self.assertTrue(comparison['observationsByteIdentical'])
        self.assertEqual([], comparison['secondaryDriftBeyondFileDates'])
        self.assertEqual(['baseline', 'secondary', 'sourceCommit'], comparison['differingSections'])
        master_census_path = evidence_dir / 'master-observations.json'
        master_pre_path = pre_dir / 'master-observations.json'
        self.assertTrue(master_census_path.exists(),
                        'the post-merge master sample must be committed once it has been recovered')
        self.assertTrue(master_pre_path.exists(),
                        'the master sample is only a same-head pair if both halves landed')
        master = json.loads(master_census_path.read_text())
        self.assertEqual({'e01-b11-census/observations.json',
                          'e01-b11-census/prior-run-observations.json',
                          'e01-b11-census/master-observations.json',
                          'e01-b11-preflight/observations.json',
                          'e01-b11-preflight/prior-run-observations.json',
                          'e01-b11-preflight/master-observations.json'},
                         {run['payload'] for run in receipt['reproduction']['runs']})
        self.assertEqual(sorted({evidence['sourceCommit'], prior['sourceCommit'],
                                master['sourceCommit']}),
                         sorted({run['head'] for run in receipt['reproduction']['runs']}))

        # The post-merge pair is the one that needs no subtraction to mean anything: same
        # recorder generation, same fixtures, a tree that already contains the batch.
        sample = receipt['masterSample']
        self.assertEqual(receipt['run']['thirdSampleRun'], sample['census']['runId'])
        for kind, path, doc in (('census', master_census_path, master),
                                ('preflight', master_pre_path, json.loads(master_pre_path.read_text()))):
            block = sample[kind]
            self.assertEqual(block['observationsFileSha256'],
                             hashlib.sha256(path.read_bytes()).hexdigest(), kind)
            self.assertEqual(block['rawPayloadSha256'],
                             hashlib.sha256(canonical(doc)).hexdigest(), kind)
            self.assertTrue(block['fileIsRecorderShaped'],
                            f'{kind}: master sample must be committed in the recorder\'s own shape')
            self.assertTrue(block['observationsByteIdentical'], kind)
            self.assertIn('observations', block['identicalSections'], kind)
            self.assertEqual([], block['secondaryDriftBeyondFileDates'], kind)
            self.assertTrue(block['baselineDifference']['executableSha256Differs'], kind)
            self.assertTrue(block['baselineDifference']['cmakeCacheSha256Equal'], kind)
            self.assertEqual({'baseline', 'secondaryObservations', 'sourceCommit'}
                             if kind == 'census' else {'baseline', 'secondary', 'sourceCommit'},
                             set(block['differingSections']), kind)
            self.assertEqual('passed', doc['status'], kind)
            self.assertEqual(master['sourceCommit'], doc['sourceCommit'], kind)
        self.assertTrue(sample['census']['canonicalHeadIsAncestorOfMaster'],
                        'the master sample must descend from the head the census rests on')
        self.assertEqual([len(sample['census']['identicalSections']), len(sample['preflight']['identicalSections'])],
                         [int(n) for n in re.findall(r'(\d+) of \d+ \w+ sections', sample['reading'])],
                         'the reading must quote the computed section counts, not remembered ones')

        # The two payload generations differ in exactly one thing: how the -et rows describe
        # themselves. Asserting the difference is what stops a swapped pair from passing.
        old_et = {o['layout']: o['facts']['extractSemantics']
                  for o in prior['observations'] if o['flag'] == '-et'}
        new_et = {o['layout']: o['facts']['extractSemantics']
                  for o in evidence['observations'] if o['flag'] == '-et'}
        self.assertEqual(set(old_et), set(new_et))
        self.assertTrue(all('exitCodeIsByteCountInPinnedBuild' in sem for sem in old_et.values()))
        self.assertTrue(all('statusShape' not in sem for sem in old_et.values()))
        self.assertTrue(all(sem['statusShape'] and 'statusIsByteCountWritten' in sem
                            for sem in new_et.values()))
        self.assertEqual({'ciff-unmapped-only-v1'},
                         {o['layout'] for o in prior['observations'] if o['facts']['finding']})
        self.assertEqual({}, receipt['extractForm']['findingsInPayload'])
        self.assertTrue(receipt['extractForm']['shapeLabelsInPayload'],
                        'the committed payload must be the labeled generation')

        # Each label must follow from the CLI's own wording, read here without the recorder's
        # classification logic, and from the numbers the row keeps.
        for layout, sem in sorted(new_et.items()):
            row = next(o for o in evidence['observations'] if o['layout'] == layout and o['flag'] == '-et')
            stderr = row['facts']['stderr']
            derived = ('thumbnail-written' if sem['artifactBytes'] else
                       'no-thumbnail-extension' if 'does not contain an Exif thumbnail' in stderr else
                       'no-exif-data' if 'No Exif data found in the file' in stderr else
                       'reader-threw' if 'Exiv2 exception in extract action' in stderr else
                       'unclassified')
            self.assertEqual(derived, sem['statusShape'], layout)
            expected_rc = {'thumbnail-written': sem['artifactBytes'], 'no-thumbnail-extension': 0,
                           'no-exif-data': 253, 'reader-threw': 1}[sem['statusShape']]
            self.assertEqual(expected_rc, row['exitCode'], layout)
            self.assertEqual(sem['statusShape'] == 'thumbnail-written',
                             sem['statusIsByteCountWritten'], layout)
            self.assertEqual(row['exitCode'] == sem['artifactBytes'], sem['exitCodeEqualsArtifactBytes'],
                             layout)
        self.assertEqual(['ciff-preview-plus-thumb-v1', 'ciff-thumb-only-v1'],
                         receipt['extractForm']['byteCountShapeIsTheSuccessPath'])
        self.assertEqual(['ciff-unmapped-only-v1'], receipt['extractForm']['exitCodeIsNotAByteCountFor'])
        for needle in ('app/actions.cpp:823-864', 'app/exiv2.cpp:195', '% 256'):
            self.assertIn(needle, receipt['extractForm']['rule'], needle)

        # Generated prose must quote the tables beside it. An earlier draft of the receipt said
        # "these five stubs" while the same block reported seven refusals, and nothing failed:
        # every count in that sentence is now computed, so a fixture change moves the prose or the
        # test complains.
        refusals = receipt['factoryRefusals']
        self.assertEqual(len(refusals['allLayoutsWithNoReader']), refusals['count'])
        self.assertEqual(refusals['count'],
                         len(refusals['layoutsRefusedByBuildButClaimedByModel'])
                         + len(refusals['modelAgreesThereIsNoReader']))
        for needle in (f"behind the {refusals['count']} refusals",
                       f"{len(refusals['layoutsRefusedByBuildButClaimedByModel'])} of them contradict"):
            self.assertIn(needle, refusals['notEstablished'], needle)
        self.assertTrue(any(f"{refusals['count']} refusals without a reader" in claim
                            for claim in receipt['notClaimed']),
                        'notClaimed must carry the same count as the table it points at')

        # Regenerating the census from the *other* payload must produce the same artifact: the
        # shape labels are description, and no row may depend on which generation was committed.
        on_disk = json.loads((ROOT / 'docs/research/EXIV2_CENSUS.json').read_text())
        saved = census.EVIDENCE_BY_BATCH['B11']
        import contextlib
        import io
        try:
            for label, path in (('prior run', prior_path), ('master sample', master_census_path)):
                census.EVIDENCE_BY_BATCH['B11'] = path
                with contextlib.redirect_stdout(io.StringIO()):
                    rebuilt = census.build(self.seeds)
                self.assertEqual([r for r in on_disk['rows'] if r['batch'] == 'B11'],
                                 [r for r in rebuilt['rows'] if r['batch'] == 'B11'],
                                 f'{label} payload would change the B11 rows')
                self.assertEqual(on_disk['rows'], rebuilt['rows'],
                                 f'{label} payload would change a row outside B11')
        finally:
            census.EVIDENCE_BY_BATCH['B11'] = saved

    def test_b9_rows_partition_observed_and_blocked(self):
        rows = [r for r in self.report['rows'] if r['batch'] == 'B9']
        self.assertEqual(96, len(rows))
        self.assertEqual({'ARW', 'CR2', 'NEF', 'ORF', 'PEF', 'SRW'}, {r['format'] for r in rows})
        # Each format has exactly two layouts: the authored marker and the blocked
        # vendor-private cell. Neither may substitute for the other.
        for fmt in ('ARW', 'CR2', 'NEF', 'ORF', 'PEF', 'SRW'):
            layouts = {r['layout'] for r in rows if r['format'] == fmt}
            self.assertEqual({f'raw-{fmt.lower()}-vendor-private-unobserved'},
                             {layout for layout in layouts if 'vendor-private' in layout}, fmt)
            self.assertEqual(1, len([layout for layout in layouts if 'vendor-private' not in layout]), fmt)
            self.assertEqual(16, len([r for r in rows if r['format'] == fmt]), fmt)
        for row in rows:
            self.assertEqual('not-assessed', row['verification'])
            self.assertIn(row['operation'], ('inventory', 'read', 'extract'))
        evidence = load_evidence('b9')
        if evidence is None:
            self.assertEqual({'not-run'}, {r['baselineObservation'] for r in rows})
            self.assertTrue(all(r['fixture'] is None for r in rows),
                            'B9 evidence arrives with its own receipt commit')
            return
        self.assertEqual('passed', evidence['status'])
        self.assertEqual('B9', evidence['batch'])
        self.assertFalse(evidence['denominatorFrozen'])
        self.assertEqual('not-assessed', evidence['verification'])
        observed = {o['id'] for o in evidence['observations']}
        blocked = {o['id'] for o in evidence['blocked']}
        self.assertEqual(48, len(observed))
        self.assertEqual(48, len(blocked))
        self.assertFalse(observed & blocked)
        self.assertEqual({r['rowId'] for r in rows}, observed | blocked)
        vendor = {r['rowId'] for r in rows if 'vendor-private' in r['layout']}
        self.assertEqual(vendor, blocked)
        self.assertEqual(blocked, {r['rowId'] for r in rows if r['baselineObservation'] == 'blocked'})
        self.assertEqual(observed, {r['rowId'] for r in rows
                                    if r['baselineObservation'] == 'observed-cli'})
        self.assertTrue(all(o['facts']['inputUnchanged'] for o in evidence['observations']))
        self.assertTrue(all(o['recordOnly'] and o['expectOk'] is None for o in evidence['observations']))
        self.assertEqual(0, evidence['outcomeCounts'].get('scan-changed', 0))
        # The four metadata-family reads on one marker fixture all ask for `-pa`, so they
        # print identical bytes: no row may be read as an independent per-family claim.
        for layout in sorted({o['layout'] for o in evidence['observations']}):
            pa = [o for o in evidence['observations'] if o['flag'] == '-pa' and o['layout'] == layout]
            self.assertEqual(4, len(pa), layout)
            self.assertEqual(1, len({o['facts']['stdoutSha256'] for o in pa}), layout)
            self.assertEqual(1, len({tuple(o['facts']['printedTokens']) for o in pa}), layout)
        # A refusal must never be recorded as an absence of vendor metadata: the marker
        # fixtures carry the carriers, so an empty -pa print is about the CLI, not the file.
        self.assertTrue(all(o['fixture'].startswith('owned-marker-') for o in evidence['observations']))
        self.assertEqual(0, sum(1 for o in evidence['observations'] if o['facts']['artifacts']
                                and o['operation'] != 'extract'))
        # Receipt integrity: the committed report is exactly the recovered payload.
        import hashlib
        receipt_dir = ROOT / 'docs/research/evidence/e01-b9-census'
        receipt_path = receipt_dir / 'receipt.json'
        if not receipt_path.exists():
            self.skipTest('B9 receipt not committed')
        receipt = json.loads(receipt_path.read_text())
        recovery = receipt['annotationRecovery']
        evidence_path = receipt_dir / 'observations.json'
        self.assertEqual(recovery['observationsFileSha256'],
                         hashlib.sha256(evidence_path.read_bytes()).hexdigest())
        self.assertEqual(recovery['rawPayloadSha256'],
                         hashlib.sha256(json.dumps(
                             {k: v for k, v in evidence.items()}, sort_keys=True,
                             separators=(',', ':')).encode()).hexdigest())
        self.assertEqual(evidence['sourceCommit'], receipt['run']['headCommit'])
        self.assertEqual(recovery['chunks'], recovery['expectedChunks'])
        self.assertTrue(recovery['markerVerified'], 'the count marker must have been matched')
        self.assertEqual(receipt['tally']['observed'] + receipt['tally']['blocked'], len(rows))
        self.assertEqual(0, receipt['tally']['artifactsProduced'] -
                         sum(1 for o in evidence['observations'] if o['facts']['artifacts']))
        # The -et round trip is asserted from the receipt, so a future run that stops
        # producing the byte-exact thumbnail copy shows up as a failing test rather
        # than as a quiet change in the evidence.
        trip = receipt['thumbnailRoundTrip']
        self.assertTrue(trip['byteExactCopyProducedByPinnedCli'])
        self.assertEqual(6, trip['rows'])
        self.assertEqual(137, int(trip['authoredThumbnailSha256'] and 137))
        for item in evidence['observations']:
            if item['operation'] == 'extract':
                self.assertEqual([trip['authoredThumbnailSha256']],
                                 [a['sha256'] for a in item['facts']['artifacts']])
                self.assertEqual(137, item['facts']['artifacts'][0]['length'])
        # Every observed row that printed anything printed exactly the authored EXIF
        # tokens; no token outside the reviewed vocabulary may appear.
        self.assertEqual({'B9Artist', 'B9Tool'},
                         {tok for o in evidence['observations'] for tok in o['facts']['printedTokens']})
        # Blocked rows carry a per-format reason naming what is missing, never a bare
        # 'unsupported', and no blocked row may hold a fixture reference.
        reasons = {b['reason'] for b in evidence['blocked']}
        self.assertEqual(6, len(reasons))
        self.assertTrue(all('blocked' in r for r in reasons))
        # The annotation-budget finding must stay recorded: the 50-annotation cap is the
        # reason this batch has its own job, and it must not be quietly forgotten.
        self.assertEqual(50, receipt['annotationBudget']['perCheckRunLimit'])
        self.assertIn('42729776775', receipt['annotationBudget']['finding'])
        # Cross-run reproduction. There is no local B9 run, so the second sample is
        # another CI run of the same recorder on a later head: the observation payload
        # must match byte-for-byte, while the reference binary legitimately differs
        # because the build embeds its own timestamp.
        ci_path = receipt_dir / 'ci-observations.json'
        ci = json.loads(ci_path.read_text())
        self.assertEqual(recovery['ciObservationsSha256'],
                         hashlib.sha256(ci_path.read_bytes()).hexdigest())
        self.assertEqual(4, recovery['ciChunks'])
        for section in ('observations', 'blocked', 'fixtureManifest', 'outcomeCounts'):
            self.assertEqual(evidence[section], ci[section], f'{section} drifted across runs')
        self.assertNotEqual(evidence['sourceCommit'], ci['sourceCommit'])
        self.assertNotEqual(evidence['baseline']['executableSha256'], ci['baseline']['executableSha256'])
        for key in ('cmakeCacheSha256', 'licenseSha256', 'sourceCommit', 'version'):
            self.assertEqual(evidence['baseline'][key], ci['baseline'][key])
        self.assertEqual(2, len(set(evidence['baseline']['verboseVersion'].splitlines())
                                ^ set(ci['baseline']['verboseVersion'].splitlines())))
        # ExifTool prints file timestamps, so its raw stdout digest cannot match across
        # runs by construction: compare the stable facts, not the envelope.
        def stable(item):
            return {'exitCode': item['exitCode'], 'interpretation': item['interpretation'],
                    'fixtureSha256': item['fixtureSha256'], 'fixtureLength': item['fixtureLength'],
                    'json': [{k: v for k, v in entry.items()
                              if not k.startswith('System:') and k != 'SourceFile'}
                             for entry in item['json']]}
        self.assertEqual({name: stable(item) for name, item in evidence['secondaryObservations'].items()},
                         {name: stable(item) for name, item in ci['secondaryObservations'].items()})
        self.assertNotEqual(evidence['secondaryObservations']['owned-marker-arw.arw']['stdoutSha256'],
                            ci['secondaryObservations']['owned-marker-arw.arw']['stdoutSha256'])
        # Third sample: the post-merge master run re-observes the merged tree. The receipt
        # describes it, and the identical/differing split is re-derived here rather than
        # taken on trust.
        master_path = receipt_dir / 'master-observations.json'
        master = json.loads(master_path.read_text())
        rep = receipt['masterRunReproduction']
        self.assertEqual(rep['observationsFileSha256'],
                         hashlib.sha256(master_path.read_bytes()).hexdigest())
        self.assertEqual(rep['rawPayloadSha256'],
                         hashlib.sha256(json.dumps(master, sort_keys=True,
                                                    separators=(',', ':')).encode()).hexdigest())
        self.assertEqual(4, rep['chunks'])
        self.assertEqual(42735514666, rep['runId'])
        self.assertEqual(sorted(master), sorted(evidence))
        self.assertEqual(
            {section for section in master
             if json.dumps(master[section], sort_keys=True)
             == json.dumps(evidence.get(section), sort_keys=True)},
            set(rep['identicalSections']))
        self.assertEqual(['baseline', 'secondaryObservations', 'sourceCommit'],
                         rep['differingSections'])
        for section in rep['identicalSections']:
            self.assertEqual(evidence[section], master[section], f'{section} drifted on master')
        self.assertNotEqual(evidence['sourceCommit'], master['sourceCommit'])
        # The pinned CLI's -et returns the number of bytes it wrote (app/actions.cpp:858 ->
        # src/exif.cpp:389 -> src/basicio.cpp:1757), so its exit code is an artifact size
        # and not a status. The recorder classifies extract rows by that rule, so pin it
        # in every sample instead of trusting the prose.
        for report in (evidence, ci, master):
            for row in report['observations']:
                if row['flag'] != '-et':
                    continue
                artifacts = row['facts']['artifacts']
                self.assertEqual(1, len(artifacts), 'one artifact per extract row')
                self.assertEqual(row['exitCode'], artifacts[0]['length'],
                                 '-et exit code must equal the bytes written')

    def test_pending_cells_partition_the_seed_grid(self):
        enumerated = set(self.report['coverage']['enumerated'])
        pending = set(self.report['coverage']['pendingSeedIds'])
        all_seeds = {s['seedId'] for s in self.seeds['seeds']}
        enumerated_seeds = {s['seedId'] for s in self.seeds['seeds']
                             if f"{s['format']}/{s['family']}" in enumerated}
        self.assertEqual(all_seeds, enumerated_seeds | pending)
        # B11 enumerates CRW/BMP/GIF/TGA x six families, so the last 24 pending seed cells
        # move to enumerated and the list empties. Closing it is bookkeeping about enumeration:
        # no row says a cell was verified, and no seed left the grid.
        self.assertEqual(186, len(enumerated_seeds))
        self.assertEqual(set(), pending)
        self.assertEqual(186, len(self.seeds['seeds']))

    def test_no_claim_rows_are_only_the_undocumented_cells(self):
        no_claim = {r['rowId'] for r in self.report['rows'] if r['noDocumentedClaim']}
        self.assertEqual({'EX0289-PNG-COMMENTS-READ-0001', 'EX0289-PNG-EXIF-READ-0001',
                          'EX0289-WEBP-COMMENTS-READ-0001', 'EX0289-WEBP-IPTC-READ-0001',
                          'EX0289-TIFF-COMMENTS-READ-0001',
                          'EX0289-XMP-EXIF-READ-0001', 'EX0289-XMP-IPTC-READ-0001',
                          'EX0289-XMP-COMMENTS-READ-0001', 'EX0289-XMP-ICC-READ-0001',
                          'EX0289-XMP-THUMBNAIL-READ-0001',
                          'EX0289-DNG-COMMENTS-READ-0001', 'EX0289-DNG-COMMENTS-READ-0002',
                          'EX0289-DNG-COMMENTS-READ-0003', 'EX0289-DNG-COMMENTS-READ-0004'} |
                         {f'EX0289-{fmt}-{family}-READ-{num:04d}'
                          for fmt in ('AVIF', 'HEIF', 'HEIC', 'CR3', 'JXL')
                          for family in ('COMMENTS', 'ICC')
                          for num in (range(1, 3) if fmt == 'JXL' else range(1, 2))} |
                         # B8 undocumented cells, derived from the pinned seed claims.
                         {'EX0289-PSD-COMMENTS-READ-0001', 'EX0289-PSD-COMMENTS-READ-0002',
                          'EX0289-PSD-ICC-READ-0001', 'EX0289-PSD-ICC-READ-0002',
                          'EX0289-JP2-COMMENTS-READ-0001',
                          'EX0289-EPS-COMMENTS-READ-0001', 'EX0289-EPS-EXIF-READ-0001',
                          'EX0289-EPS-ICC-READ-0001', 'EX0289-EPS-IPTC-READ-0001',
                          'EX0289-EPS-THUMBNAIL-READ-0001', 'EX0289-PGF-THUMBNAIL-READ-0001',
                          'EX0289-DCP-COMMENTS-READ-0001', 'EX0289-DCP-ICC-READ-0001',
                          'EX0289-DCP-IPTC-READ-0001', 'EX0289-DCP-THUMBNAIL-READ-0001',
                          'EX0289-DCP-XMP-READ-0001'} |
                         # B9 undocumented cells: comments has no comment mode for any of
                          # these formats in the pinned registry, and ICC carries no
                          # documented claim for ORF/SRW. Two layouts each (marker plus the
                          # blocked vendor-private cell).
                          {f'EX0289-{fmt}-COMMENTS-READ-{num:04d}'
                           for fmt in ('ARW', 'CR2', 'NEF', 'ORF', 'PEF', 'SRW') for num in (1, 2)} |
                          {f'EX0289-{fmt}-ICC-READ-{num:04d}'
                           for fmt in ('ORF', 'SRW') for num in (1, 2)} |
                         # B11 undocumented cells. BMP, GIF and TGA are '-' for all six
                         # families -- the pinned bullet says the format is recognised, a MIME
                         # type assigned and the dimensions read, and nothing more -- and CRW is
                         # '-' for iptc, xmp and icc. One row per layout, in sorted layout
                         # order: five CIFF, two BMP, two GIF, three TGA.
                          {f'EX0289-CRW-{family}-READ-{num:04d}'
                           for family in ('IPTC', 'XMP', 'ICC') for num in range(1, 6)} |
                          {f'EX0289-{fmt}-{family}-READ-{num:04d}'
                           for fmt in ('BMP', 'GIF') for family in SIX_FAMILIES
                           for num in (1, 2)} |
                          {f'EX0289-TGA-{family}-READ-{num:04d}'
                           for family in SIX_FAMILIES for num in (1, 2, 3)} |
                         # B10 undocumented cells. The pinned registry claims no comment
                         # mode for any wave-2 format and no ICC cell for any of them, so
                         # both layouts (marker and blocked vendor-private) are unclaimed.
                         {f'EX0289-{fmt}-{family}-READ-{num:04d}'
                          for fmt in ('MRW', 'RAF', 'RW2', 'SR2')
                          for family in ('COMMENTS', 'ICC') for num in (1, 2)}, no_claim)
        for row in self.report['rows']:
            if row['noDocumentedClaim']:
                self.assertEqual('read', row['operation'])
                self.assertIn(row['batch'], ('B2', 'B3', 'B4', 'B5', 'B6', 'B7', 'B8', 'B9',
                                             'B10', 'B11'))

    def test_rows_never_use_metadataSharp_as_oracle(self):
        for row in self.report['rows']:
            self.assertIn(row['oracle']['type'], self.report['enumerations']['oracleTypes'])
            self.assertNotEqual('metadataSharp', row['oracle']['type'])

if __name__ == '__main__':
    unittest.main()
