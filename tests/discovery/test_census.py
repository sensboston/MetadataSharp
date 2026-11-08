import importlib.util
import json
import re
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('census', ROOT / 'tools/reference/build_census.py')
census = importlib.util.module_from_spec(spec); spec.loader.exec_module(census)

ROW_ID = re.compile(r'^EX0289-[A-Za-z]+-[A-Z]+-[A-Z]+-\d{4}$')

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

    def test_b0_pilot_is_all_not_run_and_not_assessed(self):
        self.assertFalse(self.report['denominatorFrozen'])
        for row in self.report['rows']:
            self.assertEqual('not-run', row['baselineObservation'])
            self.assertEqual('not-assessed', row['verification'])
            self.assertIsNone(row['fixture'])
            self.assertEqual('B0', row['batch'])

    def test_fixture_rights_never_inferred(self):
        for row in self.report['rows']:
            self.assertIsNone(row['fixture'])

    def test_pilot_scope_is_exactly_jpeg_exif_comments_icc_plus_inventory(self):
        families = {r['family'] for r in self.report['rows']}
        self.assertEqual({'exif', 'comments', 'icc', 'inventory'}, families)
        self.assertEqual(1, sum(1 for r in self.report['rows'] if r['family'] == 'inventory'))
        self.assertEqual(135, len(self.report['rows']))

    def test_pending_cells_partition_the_seed_grid(self):
        enumerated = set(self.report['coverage']['enumerated'])
        pending = set(self.report['coverage']['pendingSeedIds'])
        all_seeds = {s['seedId'] for s in self.seeds['seeds']}
        enumerated_seeds = {s['seedId'] for s in self.seeds['seeds']
                             if f"{s['format']}/{s['family']}" in enumerated}
        self.assertEqual(all_seeds, enumerated_seeds | pending)
        self.assertEqual(3, len(enumerated_seeds))

    def test_no_claim_never_enters_the_pilot(self):
        for row in self.report['rows']:
            self.assertFalse(row['noDocumentedClaim'])

    def test_rows_never_use_metadataSharp_as_oracle(self):
        for row in self.report['rows']:
            self.assertIn(row['oracle']['type'], self.report['enumerations']['oracleTypes'])
            self.assertNotEqual('metadataSharp', row['oracle']['type'])

if __name__ == '__main__':
    unittest.main()
