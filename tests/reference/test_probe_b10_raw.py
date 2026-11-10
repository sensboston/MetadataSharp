"""Offline tests for the B10 wave-2 preflight recorder.

The preflight is the only place where the pinned CLI is asked for five flags on every fixture
at once, so its record must stay small enough to recover whole and must not borrow B9's
container vocabulary: `compression` and `directoryCount` are B9 structure keys and simply do
not exist at the top level of a B10 record.
"""
import base64
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import zlib

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/reference'))
import b10_raw_fixtures as catalog  # noqa: E402
import observe_b0 as common  # noqa: E402
import probe_b10_raw as probe  # noqa: E402

B9_ONLY_KEYS = ('compression', 'directoryCount')


class ProbeB10RawTests(unittest.TestCase):
    def fixture(self, fmt='MRW'):
        entry = next(item for item in catalog.entries() if item['format'] == fmt)
        return catalog.DIRECTORY / entry['file'], fmt

    def test_container_facts_use_the_b10_vocabulary(self):
        source, fmt = self.fixture()
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(common, 'soft_run', return_value=(0, b'', b'')):
                result = probe.run_probe(Path('/mock'), source, Path(tmp) / 'w', fmt)
        container = result['container']
        for key in B9_ONLY_KEYS:
            self.assertNotIn(key, container, f'{key} is a B9 structure key, not a B10 one')
        self.assertIn('dispatch', container)
        self.assertIn('acceptedBy', container)
        self.assertEqual(len(probe.FLAGS), len(result['operations']))
        self.assertEqual(result['fixture'], [item['file'] for item in catalog.entries()
                                             if item['format'] == fmt][0])
        self.assertEqual(result['fixtureSha256'], catalog.digest(source.read_bytes()))

    def test_every_flag_is_recorded_once_and_input_is_never_consumed(self):
        source, fmt = self.fixture('RAF')
        before = source.read_bytes()
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(common, 'soft_run', return_value=(0, b'', b'')) as run:
                result = probe.run_probe(Path('/mock'), source, Path(tmp) / 'w', fmt)
        self.assertEqual(list(probe.FLAGS), [op['flag'] for op in result['operations']])
        self.assertEqual(len(probe.FLAGS), run.call_count)
        self.assertTrue(all(op['inputUnchanged'] for op in result['operations']))
        self.assertEqual(before, source.read_bytes())
        # The preflight never writes a flag outside the read-only five.
        for call in run.call_args_list:
            command = call.args[1]
            self.assertTrue(set(command[1:-1]) <= set(probe.FLAGS), command)

    def test_extract_row_records_the_byte_count_instead_of_guessing(self):
        def produce(report, command, cwd):
            if '-et' in command:
                (Path(cwd) / 'case-thumb.jpg').write_bytes(b'0123456789')
                return 10, b'', b''
            return 0, b'', b''
        source, fmt = self.fixture('RW2')
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(common, 'soft_run', side_effect=produce):
                result = probe.run_probe(Path('/mock'), source, Path(tmp) / 'w', fmt)
        extract = next(op for op in result['operations'] if op['flag'] == '-et')
        self.assertEqual(10, extract['artifactBytes'])
        self.assertTrue(extract['exitCodeEqualsArtifactBytes'])
        read = next(op for op in result['operations'] if op['flag'] == '-pa')
        self.assertNotIn('artifactBytes', read)

    def test_report_stays_inside_the_annotation_recovery_budget(self):
        report = {'status': 'passed', 'observations': [], 'secondary': [], 'fixtureManifest': []}
        with tempfile.TemporaryDirectory() as tmp:
            for entry in catalog.entries():
                source = catalog.DIRECTORY / entry['file']
                with patch.object(common, 'soft_run',
                                  return_value=(0, b'Artist = B10Artist\n' * 40, b'')):
                    report['observations'].append(
                        probe.run_probe(Path('/mock'), source, Path(tmp) / source.stem,
                                        entry['format']))
                report['fixtureManifest'].append(entry)
        encoded = base64.b64encode(zlib.compress(
            json.dumps(report, separators=(',', ':'), sort_keys=True).encode(), 9)).decode()
        parts = [encoded[i:i + 4000] for i in range(0, len(encoded), 4000)]
        self.assertLessEqual(len(parts), 10, 'the preflight must stay recoverable from annotations')
        # And the recovery must be exact: decoding the concatenated parts reproduces the report.
        self.assertEqual(report, json.loads(zlib.decompress(base64.b64decode(''.join(parts)))))


if __name__ == '__main__':
    unittest.main()
