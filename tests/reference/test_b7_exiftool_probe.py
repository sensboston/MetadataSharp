"""Offline tests for the B7 secondary-oracle boundary."""
import hashlib
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/reference'))
import b7_exiftool_probe as probe


def completed(stdout=b'', stderr=b'', rc=0):
    return subprocess.CompletedProcess([], rc, stdout, stderr)


class ExiftoolProbeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / 'exiftool').write_bytes(b'placeholder')
        self.fixture = self.root / 'input.dat'
        self.fixture.write_bytes(b'owned toy bytes')

    def mock_responses(self, observation):
        return [completed(probe.EXIFTOOL_COMMIT.encode() + b'\n'), completed(),
                completed(b'12.76\n'), observation]

    def test_pinned_read_only_observation(self):
        with patch.object(probe, '_run', side_effect=self.mock_responses(completed(b'[{"FileType":"demo"}]'))) as run:
            report = probe.observe(self.root, self.fixture)
        self.assertEqual(0, report['exitCode'])
        self.assertEqual('demo', report['json'][0]['FileType'])
        self.assertIsNone(report['jsonParseError'])
        self.assertEqual(hashlib.sha256(b'owned toy bytes').hexdigest(), report['fixtureSha256'])
        self.assertEqual(['-config', '', '-json', '-G1', '-s'], run.call_args_list[-1].args[0][2:7])

    def test_wrong_commit_version_and_dirty_checkout_fail(self):
        cases = [[completed(b'wrong'), completed()],
                 [completed(probe.EXIFTOOL_COMMIT.encode()), completed(b' M src')],
                 [completed(probe.EXIFTOOL_COMMIT.encode()), completed(), completed(b'12.77')]]
        for responses in cases:
            with self.subTest(responses=responses), patch.object(probe, '_run', side_effect=responses):
                with self.assertRaises(probe.SecondaryProbeError):
                    probe.observe(self.root, self.fixture)

    def test_nonzero_and_invalid_json_are_recorded_not_passed(self):
        with patch.object(probe, '_run', side_effect=self.mock_responses(completed(b'not json', b'failure', 2))):
            report = probe.observe(self.root, self.fixture)
        self.assertEqual(2, report['exitCode'])
        self.assertIsNone(report['json'])
        self.assertTrue(report['jsonParseError'])
        self.assertEqual('failure', report['stderr'])

    def test_read_only_mutation_is_an_error(self):
        outputs = iter(self.mock_responses(completed(b'[{}]')))
        def fake(command):
            result = next(outputs)
            if '-json' in command:
                self.fixture.write_bytes(b'changed')
            return result
        with patch.object(probe, '_run', side_effect=fake):
            with self.assertRaises(probe.SecondaryProbeError):
                probe.observe(self.root, self.fixture)

    def test_subprocess_output_is_spooled_and_capped(self):
        with patch.object(probe, 'MAX_OUTPUT_BYTES', 4):
            self.assertEqual(b'abc', probe._run([sys.executable, '-c', "print('abc', end='')"]).stdout)
            with self.assertRaises(probe.SecondaryProbeError):
                probe._run([sys.executable, '-c', "print('abcdef', end='')"])

    def test_bad_shape_and_output_cap(self):
        with patch.object(probe, '_run', side_effect=self.mock_responses(completed(b'[]'))):
            self.assertIsNotNone(probe.observe(self.root, self.fixture)['jsonParseError'])
        with patch.object(probe, 'MAX_OUTPUT_BYTES', 1), patch.object(
                probe, '_run', side_effect=self.mock_responses(completed(b'[{}]'))):
            with self.assertRaises(probe.SecondaryProbeError):
                probe.observe(self.root, self.fixture)


if __name__ == '__main__':
    unittest.main()
