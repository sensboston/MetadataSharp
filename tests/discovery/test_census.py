import importlib.util
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('census', ROOT / 'tools/reference/build_census.py')
census = importlib.util.module_from_spec(spec); spec.loader.exec_module(census)

ROW_ID = __import__('re').compile(r'^EX0289-[A-Za-z]+-[A-Z]+-[A-Z]+-\d{4}$')


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

    def test_verification_stays_not_assessed(self):
        for row in self.report['rows']:
            self.assertEqual('not-assessed', row['verification'])

    def test_observed_rows_carry_pinned_owned_synthetic_fixtures(self):
        manifests = {}
        for fmt in ('JPEG', 'PNG', 'TIFF', 'XMP'):
            manifests[fmt] = {e['file']: e for e in json.loads((ROOT / f'tests/fixtures/{fmt.lower()}/manifest.json').read_text())}
        manifests['DNG'] = {e['file']: e for e in json.loads((ROOT / 'tests/fixtures/dng/census-manifest.json').read_text())}
        manifests['WEBP'] = {e['file']: e for e in json.loads((ROOT / 'tests/fixtures/webp/census-manifest.json').read_text())}
        for row in self.report['rows']:
            if row['baselineObservation'] == 'not-applicable':
                self.assertIsNone(row['fixture'])
                self.assertIn('static not-applicable', row['notes'])
                continue
            if row['baselineObservation'] == 'not-run':
                self.assertIsNone(row['fixture'])
                continue
            fixture = row['fixture']
            pin = manifests[row['format']][fixture['file']]
            self.assertEqual(pin['sha256'], fixture['sha256'])
            self.assertEqual(pin['length'], fixture['length'])
            self.assertEqual(pin['recipe'], fixture['recipe'])
            self.assertEqual('owned-synthetic', fixture['rights'])
            self.assertIn(f"{row['batch']} pinned-CLI observation", row['notes'])

    def test_scope_is_observed_batches_plus_inventory(self):
        families = {r['family'] for r in self.report['rows']}
        self.assertEqual({'exif', 'comments', 'icc', 'xmp', 'iptc', 'thumbnail', 'inventory'}, families)
        self.assertEqual(9, sum(1 for r in self.report['rows'] if r['family'] == 'inventory'))
        self.assertEqual(135, sum(1 for r in self.report['rows'] if r['batch'] == 'B0'))
        self.assertEqual(58, sum(1 for r in self.report['rows'] if r['batch'] == 'B1'))
        self.assertEqual(67, sum(1 for r in self.report['rows'] if r['batch'] == 'B2'))
        self.assertEqual(43, sum(1 for r in self.report['rows'] if r['batch'] == 'B3'))
        self.assertEqual(50, sum(1 for r in self.report['rows'] if r['batch'] == 'B4'))
        self.assertEqual(26, sum(1 for r in self.report['rows'] if r['batch'] == 'B5'))
        self.assertEqual(88, sum(1 for r in self.report['rows'] if r['batch'] == 'B6'))
        self.assertEqual(467, len(self.report['rows']))

    def test_pending_cells_partition_the_seed_grid(self):
        enumerated = set(self.report['coverage']['enumerated'])
        pending = set(self.report['coverage']['pendingSeedIds'])
        all_seeds = {s['seedId'] for s in self.seeds['seeds']}
        enumerated_seeds = {s['seedId'] for s in self.seeds['seeds']
                             if f"{s['format']}/{s['family']}" in enumerated}
        self.assertEqual(all_seeds, enumerated_seeds | pending)
        self.assertEqual(36, len(enumerated_seeds))

    def test_no_claim_rows_are_only_the_undocumented_cells(self):
        no_claim = {r['rowId'] for r in self.report['rows'] if r['noDocumentedClaim']}
        self.assertEqual({'EX0289-PNG-COMMENTS-READ-0001', 'EX0289-PNG-EXIF-READ-0001',
                          'EX0289-WEBP-COMMENTS-READ-0001', 'EX0289-WEBP-IPTC-READ-0001',
                          'EX0289-TIFF-COMMENTS-READ-0001',
                          'EX0289-XMP-EXIF-READ-0001', 'EX0289-XMP-IPTC-READ-0001',
                          'EX0289-XMP-COMMENTS-READ-0001', 'EX0289-XMP-ICC-READ-0001',
                          'EX0289-XMP-THUMBNAIL-READ-0001',
                          'EX0289-DNG-COMMENTS-READ-0001', 'EX0289-DNG-COMMENTS-READ-0002',
                          'EX0289-DNG-COMMENTS-READ-0003', 'EX0289-DNG-COMMENTS-READ-0004'}, no_claim)
        for row in self.report['rows']:
            if row['noDocumentedClaim']:
                self.assertEqual('read', row['operation'])
                self.assertIn(row['batch'], ('B2', 'B3', 'B4', 'B5', 'B6'))

    def test_rows_never_use_metadataSharp_as_oracle(self):
        for row in self.report['rows']:
            self.assertIn(row['oracle']['type'], self.report['enumerations']['oracleTypes'])
            self.assertNotEqual('metadataSharp', row['oracle']['type'])

if __name__ == '__main__':
    unittest.main()
