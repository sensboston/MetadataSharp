import copy
import importlib.util
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('seeds', ROOT / 'tools/reference/build_capability_seeds.py')
seeds = importlib.util.module_from_spec(spec); spec.loader.exec_module(seeds)

class CapabilitySeedPolicyTests(unittest.TestCase):
    def setUp(self):
        self.facts = json.loads(seeds.SOURCE.read_text())
        self.report = seeds.build(self.facts)

    def test_every_documented_cell_stays_visible(self):
        self.assertEqual(sum(len(f['documented']) for f in self.facts['formats']), len(self.report['seeds']))
        self.assertEqual(len(self.report['seeds']), len({s['seedId'] for s in self.report['seeds']}))

    def test_read_only_does_not_imply_write(self):
        for row in self.report['seeds']:
            if row['documentedClaim'] == 'Read':
                self.assertEqual(['read'], row['candidateOperations'])

    def test_write_suboperations_are_not_observed(self):
        rows = [r for r in self.report['seeds'] if r['documentedClaim'] == 'Read/Write']
        self.assertTrue(rows)
        for row in rows:
            self.assertIn('inferred', row['operationEvidence'])
            self.assertEqual('not-run', row['baselineObservation'])

    def test_no_claim_is_not_an_exclusion(self):
        for row in self.report['seeds']:
            if row['documentedClaim'] in ('', '-'):
                self.assertEqual([], row['candidateOperations'])
                self.assertEqual('needs-reconciliation', row['disposition'])

    def test_no_parity_or_fixture_rights_inferred(self):
        self.assertFalse(self.report['denominatorFrozen'])
        for row in self.report['seeds']:
            self.assertEqual('not-reviewed', row['fixtureRights'])
            self.assertEqual('not-assessed', row['metadataSharpVerification'])

    def test_unknown_claim_refuses_instead_of_assuming_support(self):
        facts = copy.deepcopy(self.facts)
        facts['formats'][0]['documented']['exif'] = 'Maybe'
        with self.assertRaises(ValueError):
            seeds.build(facts)
