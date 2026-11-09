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

    def test_verification_stays_not_assessed(self):
        for row in self.report['rows']:
            self.assertEqual('not-assessed', row['verification'])

    def test_observed_rows_carry_pinned_owned_synthetic_fixtures(self):
        manifests = {fmt: {e['file']: e for e in json.loads((ROOT / f'tests/fixtures/{fmt.lower()}/manifest.json').read_text())}
                     for fmt in ('JPEG', 'PNG')}
        for row in self.report['rows']:
            if row['baselineObservation'] == 'not-applicable':
                self.assertIsNone(row['fixture'])
                self.assertIn('static not-applicable', row['notes'])
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
        self.assertEqual(2, sum(1 for r in self.report['rows'] if r['family'] == 'inventory'))
        self.assertEqual(135, sum(1 for r in self.report['rows'] if r['batch'] == 'B0'))
        self.assertEqual(58, sum(1 for r in self.report['rows'] if r['batch'] == 'B1'))
        self.assertEqual(67, sum(1 for r in self.report['rows'] if r['batch'] == 'B2'))
        self.assertEqual(260, len(self.report['rows']))

    def test_pending_cells_partition_the_seed_grid(self):
        enumerated = set(self.report['coverage']['enumerated'])
        pending = set(self.report['coverage']['pendingSeedIds'])
        all_seeds = {s['seedId'] for s in self.seeds['seeds']}
        enumerated_seeds = {s['seedId'] for s in self.seeds['seeds']
                             if f"{s['format']}/{s['family']}" in enumerated}
        self.assertEqual(all_seeds, enumerated_seeds | pending)
        self.assertEqual(12, len(enumerated_seeds))

    def test_no_claim_rows_are_only_the_undocumented_png_cells(self):
        no_claim = {r['rowId'] for r in self.report['rows'] if r['noDocumentedClaim']}
        self.assertEqual({'EX0289-PNG-COMMENTS-READ-0001', 'EX0289-PNG-EXIF-READ-0001'}, no_claim)
        for row in self.report['rows']:
            if row['noDocumentedClaim']:
                self.assertEqual('read', row['operation'])
                self.assertEqual('B2', row['batch'])

    def test_rows_never_use_metadataSharp_as_oracle(self):
        for row in self.report['rows']:
            self.assertIn(row['oracle']['type'], self.report['enumerations']['oracleTypes'])
            self.assertNotEqual('metadataSharp', row['oracle']['type'])

if __name__ == '__main__':
    unittest.main()
