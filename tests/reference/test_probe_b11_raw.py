"""Offline tests for the B11 container preflight recorder.

The preflight is the only place where the pinned CLI is asked for seven flags on every
seed-grid fixture and five on every container stub at once, so its record must stay small
enough to recover whole, must not borrow the census recorder's vocabulary, and must prove
the two halves really are asked different questions.
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
import b11_container_fixtures as catalog  # noqa: E402
import observe_b11 as observer  # noqa: E402
import observe_b0 as common  # noqa: E402
import probe_b11_raw as probe  # noqa: E402


class ProbeB11RawTests(unittest.TestCase):
    def fixture(self, layout='ciff-mapped-v1'):
        name = catalog.FIXTURES[layout][1]
        return catalog.DIRECTORY / name, layout

    def test_flag_budget_differs_between_the_two_halves_of_the_batch(self):
        for fmt in observer.SEED_FORMATS:
            self.assertEqual(list(probe.SEED_FLAGS), probe.flags_for_format(fmt), fmt)
        for fmt in observer.VIDEO_FORMATS:
            self.assertEqual(list(probe.VIDEO_FLAGS), probe.flags_for_format(fmt), fmt)
        # The stubs drop -pc and -pe deliberately: comment amNone and a reader that writes no
        # Exif tag are both answered by -pa, and the budget buys the brand pair instead.
        self.assertEqual({'-pc', '-pe'}, set(probe.SEED_FLAGS) - set(probe.VIDEO_FLAGS))
        self.assertEqual(107, sum(len(probe.flags_for_format(fmt))
                                  for fmt, _ in catalog.FIXTURES.values()))

    def test_record_shape_is_the_layouts_own_vocabulary(self):
        source, layout = self.fixture('bmp-dib-v3')
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(common, 'soft_run', return_value=(0, b'', b'')):
                result = probe.run_probe(Path('/mock'), source, Path(tmp) / 'w', layout)
        self.assertEqual({'fixture', 'format', 'layout', 'fixtureSha256', 'container', 'flags',
                          'operations', 'commands'}, set(result))
        self.assertEqual('BMP', result['format'])
        self.assertEqual(len(probe.flags_for_format('BMP')), len(result['operations']))
        self.assertEqual({'dispatch', 'fileLength', 'fileSha256', 'matched', 'bmp'},
                         set(result['container']))
        # A BMP record carries no CIFF block and no MOV block: formats do not share keys.
        self.assertNotIn('ciff', result['container'])
        self.assertNotIn('mov', result['container'])
        self.assertEqual(result['fixtureSha256'], catalog.digest(source.read_bytes()))

    def test_every_flag_is_recorded_once_and_input_is_never_consumed(self):
        source, layout = self.fixture('ciff-thumb-only-v1')
        before = source.read_bytes()
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(common, 'soft_run', return_value=(0, b'', b'')) as run:
                result = probe.run_probe(Path('/mock'), source, Path(tmp) / 'w', layout)
        self.assertEqual(list(probe.flags_for_format('CRW')), [op['flag'] for op in
                                                                result['operations']])
        self.assertEqual(len(probe.SEED_FLAGS), run.call_count)
        self.assertTrue(all(op['inputUnchanged'] for op in result['operations']))
        self.assertEqual(before, source.read_bytes())
        for call in run.call_args_list:
            command = call.args[1]
            self.assertTrue(set(command[1:-1]) <= set(probe.SEED_FLAGS), command)

    def test_extract_row_records_the_byte_count_and_the_prediction(self):
        # The prediction is the length of the CIFF 0x2008 component, computed from the
        # fixture; this fake CLI writes exactly that many bytes, so the recorder must report
        # agreement. Writing one byte more or less is how a recorder bug would show up.
        source, layout = self.fixture('ciff-preview-plus-thumb-v1')
        expected = next(entry for entry in catalog.entries()
                        if entry['layout'] == layout)['thumbnailLength']

        def produce(report, command, cwd):
            if '-et' in command:
                payload = b'\xff\xd8' + bytes(expected - 4) + b'\xff\xd9'
                (Path(cwd) / 'case-thumb.jpg').write_bytes(payload)
                return len(payload), b'', b''
            return 0, b'', b''

        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(common, 'soft_run', side_effect=produce):
                result = probe.run_probe(Path('/mock'), source, Path(tmp) / 'w', layout)
        extract = next(op for op in result['operations'] if op['flag'] == '-et')
        self.assertEqual(expected, extract['artifactBytes'])
        self.assertTrue(extract['exitCodeEqualsArtifactBytes'])
        self.assertTrue(extract['predictionMatched'])
        self.assertEqual(expected, extract['predictedArtifactBytes'])
        read = next(op for op in result['operations'] if op['flag'] == '-pa')
        self.assertNotIn('artifactBytes', read)

    def test_a_one_byte_short_artifact_is_a_prediction_mismatch_not_a_failure(self):
        source, layout = self.fixture('ciff-thumb-only-v1')
        expected = next(entry for entry in catalog.entries()
                        if entry['layout'] == layout)['thumbnailLength']

        def produce(report, command, cwd):
            if '-et' in command:
                payload = bytes(expected - 1)
                (Path(cwd) / 'case-thumb.jpg').write_bytes(payload)
                return len(payload), b'', b''
            return 0, b'', b''

        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(common, 'soft_run', side_effect=produce):
                result = probe.run_probe(Path('/mock'), source, Path(tmp) / 'w', layout)
        extract = next(op for op in result['operations'] if op['flag'] == '-et')
        self.assertFalse(extract['predictionMatched'])
        # The exit code still equals the bytes written, so nothing about the *build* is
        # implicated: only the recorder's expectation and the artifact disagree.
        self.assertTrue(extract['exitCodeEqualsArtifactBytes'])

    def test_no_prediction_without_a_thumbnail_component(self):
        source, layout = self.fixture('bmp-os2-coreheader-v1')
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(common, 'soft_run', return_value=(0, b'', b'')):
                result = probe.run_probe(Path('/mock'), source, Path(tmp) / 'w', layout)
        extract = next(op for op in result['operations'] if op['flag'] == '-et')
        self.assertEqual(0, extract['artifactBytes'])
        self.assertNotIn('predictedArtifactBytes', extract,
                         'a fixture with no 0x2008 component has nothing to predict from')

    def test_report_stays_inside_the_annotation_recovery_budget(self):
        report = {'status': 'passed', 'observations': [], 'secondary': [], 'fixtureManifest': []}
        with tempfile.TemporaryDirectory() as tmp:
            for entry in catalog.entries():
                if entry.get('blocked'):
                    continue
                source = catalog.DIRECTORY / entry['file']
                with patch.object(common, 'soft_run',
                                  return_value=(0, b'Comment = B11CrwComment\n' * 40, b'')):
                    report['observations'].append(
                        probe.run_probe(Path('/mock'), source, Path(tmp) / source.stem,
                                        entry['layout']))
                report['fixtureManifest'].append(entry)
        self.assertEqual(17, len(report['observations']))
        self.assertEqual(17, len(catalog.FIXTURES))
        encoded = base64.b64encode(zlib.compress(
            json.dumps(report, separators=(',', ':'), sort_keys=True).encode(), 9)).decode()
        parts = [encoded[i:i + 4000] for i in range(0, len(encoded), 4000)]
        self.assertLessEqual(len(parts), probe.CHUNK_LIMIT,
                             'the preflight must stay recoverable from annotations')
        # And the recovery must be exact: decoding the concatenated parts reproduces the report.
        self.assertEqual(report, json.loads(zlib.decompress(base64.b64decode(''.join(parts)))))


if __name__ == '__main__':
    unittest.main()
