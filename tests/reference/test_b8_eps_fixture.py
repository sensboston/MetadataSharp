"""Offline authored EPS check; never execute the PostScript body."""
import hashlib
import sys
from pathlib import Path
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/reference'))
import b8_eps_fixture as fixture


class EpsFixtureTests(unittest.TestCase):
    def test_authored_hash_and_dsc_header(self):
        fixture.verify()
        data = (fixture.DIRECTORY / fixture.FILE).read_bytes()
        self.assertEqual('17a5075bac1eaa58e7d37225db451e23e8ef5c13672eb8f24e7fef3d66a270ba', hashlib.sha256(data).hexdigest())
        lines = data.splitlines()
        self.assertEqual(b'%!PS-Adobe-3.0 EPSF-3.0', lines[0])
        self.assertEqual(b'%%BoundingBox: 0 0 10 12', lines[1])
        self.assertIn(b'%%EndComments', lines)
        self.assertEqual(b'%%EOF', lines[-1])

    def test_recovered_ci_preflight(self):
        import json
        evidence = Path(__file__).resolve().parents[2] / 'docs/research/evidence/e01-b8-eps-preflight'
        report_bytes = (evidence / 'ci-observations.json').read_bytes()
        receipt = json.loads((evidence / 'receipt.json').read_text())
        self.assertEqual(receipt['ciObservationsSha256'], hashlib.sha256(report_bytes).hexdigest())
        report = json.loads(report_bytes)
        self.assertEqual(receipt['headCommit'], report['sourceCommit'])
        self.assertEqual('passed', report['status'])
        self.assertFalse(report['denominatorFrozen'])
        self.assertEqual([fixture.verify()], report['fixtureManifest'])
        ops = report['observations'][0]['operations']
        self.assertEqual([1, 0, 0, 1, 253], [o['exitCode'] for o in ops])
        self.assertTrue(all(o['inputUnchanged'] and not o['artifacts'] for o in ops))
        self.assertEqual('0 0 10 12', report['secondary'][0]['json'][0]['PostScript:BoundingBox'])


if __name__ == '__main__':
    unittest.main()
