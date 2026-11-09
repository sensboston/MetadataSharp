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
