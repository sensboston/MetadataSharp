"""Offline tests: preflight records outcomes without turning them into support."""
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/reference'))
import b8_psd_fixture as fixture
import probe_b8_psd as probe


class PsdProbeTests(unittest.TestCase):
    def test_recovered_ci_receipt_and_recorded_local_results(self):
        import hashlib
        import json
        evidence = Path(__file__).resolve().parents[2] / 'docs/research/evidence/e01-b8-psd-preflight'
        receipt_path = evidence / 'receipt.json'
        if not receipt_path.exists():
            self.skipTest('CI receipt not yet committed')
        receipt = json.loads(receipt_path.read_text())
        path = evidence / 'ci-observations.json'
        self.assertEqual(receipt['ciObservationsSha256'], hashlib.sha256(path.read_bytes()).hexdigest())
        report = json.loads(path.read_text())
        self.assertEqual(receipt['headCommit'], report['sourceCommit'])
        self.assertEqual('passed', report['status'])
        self.assertFalse(report['denominatorFrozen'])
        self.assertEqual('not-assessed', report['verification'])
        self.assertEqual(fixture.verify(), report['fixtureManifest'])
        self.assertEqual(2, len(report['observations']))
        self.assertTrue(all(op['inputUnchanged'] for item in report['observations'] for op in item['operations']))
        def stable(item):
            return {k: v for k, v in item['json'][0].items() if not k.startswith('System:') and k != 'SourceFile'}
        for index, name in enumerate(('flat', 'rich')):
            local = json.loads((evidence / f'exiftool-{name}.json').read_text())
            self.assertEqual(stable(local), stable(report['secondary'][index]))
        rich = report['observations'][1]
        read = next(op for op in rich['operations'] if op['flag'] == '-pa')
        self.assertEqual(0, read['exitCode'])
        for value in ('B8Artist', 'B8Tool', 'B8Keyword'):
            self.assertIn(value, read['stdout'])

    def test_records_returns_mutations_and_extraction_artifacts(self):
        def fake(report, cmd, cwd):
            if cmd[1] == '-pa':
                cmd[-1].write_bytes(b'modified')
                return 0, b'output', b''
            if cmd[1] == '-et':
                (cwd / 'case-thumb.jpg').write_bytes(b'artifact')
                return 1, b'', b'error'
            return 1, b'', b'no result'
        with tempfile.TemporaryDirectory() as tmp, patch.object(probe.common, 'soft_run', side_effect=fake):
            result = probe.run_probe(Path('/unused'), fixture.DIRECTORY / fixture.NAME, Path(tmp))
        ops = {r['flag']: r for r in result['operations']}
        self.assertEqual({'-pS', '-pa', '-pc', '-pC', '-et'}, set(ops))
        self.assertFalse(ops['-pa']['inputUnchanged'])
        self.assertEqual(1, ops['-et']['exitCode'])
        self.assertEqual('case-thumb.jpg', ops['-et']['artifacts'][0]['file'])
        self.assertEqual(1, ops['-pS']['exitCode'])


if __name__ == '__main__':
    unittest.main()
