"""Offline policy tests for the B8 observation recorder.

Exiv2 is not installed here, so these tests exercise the decision logic against
the real owned fixtures with a replaced command runner: exactly one read-only
probe per census row, refusals recorded as refusals, input mutation reported as a
finding, and the whole report small enough to recover from check-run annotations.
"""
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/reference'))
import b8_census_fixtures as catalog
import observe_b8 as observer


def b8_rows():
    census = json.loads((ROOT / 'docs/research/EXIV2_CENSUS.json').read_text())
    return [r for r in census['rows'] if r['batch'] == 'B8']


def row(layout='flat-rgb-v1-metadata', family='exif', operation='read'):
    # Format is derived from the layout so a test cannot pair a JP2 layout with PSD bytes.
    return {'rowId': 'test-row', 'format': catalog.FIXTURES[layout][0], 'layout': layout,
            'family': family, 'operation': operation, 'duplicates': 'none',
            'textEncoding': 'not-applicable'}


class ObserveB8Tests(unittest.TestCase):
    def test_census_partitions_into_observed_and_blocked_cells(self):
        rows = b8_rows()
        self.assertEqual(53, len(rows))
        blocked = [r for r in rows if r['layout'] not in catalog.FIXTURES]
        self.assertEqual(22, len(blocked))
        self.assertEqual({'PGF', 'EXV', 'DCP'}, {r['format'] for r in blocked})
        self.assertEqual({'pgf-unobserved', 'exv-unobserved', 'dcp-unobserved'},
                         {r['layout'] for r in blocked})
        self.assertEqual(len(rows) - 22, len([r for r in rows if r['layout'] in catalog.FIXTURES]))

    def test_exactly_one_read_only_probe_per_row(self):
        flags = {'-pS', '-pa', '-pc', '-pC', '-et'}
        for r in b8_rows():
            chosen = observer.flags_for(r)
            self.assertEqual(1, len(chosen))
            self.assertIn(chosen[0], flags)
            if r['family'] == 'inventory':
                self.assertEqual('-pS', chosen[0])
            elif r['operation'] == 'extract':
                self.assertEqual('-et', chosen[0])
            elif r['family'] == 'comments':
                self.assertEqual('-pc', chosen[0])
            elif r['family'] == 'icc':
                self.assertEqual('-pC', chosen[0])
            else:
                self.assertEqual('-pa', chosen[0])

    def test_refusal_is_recorded_and_input_stays_byte_identical(self):
        with tempfile.TemporaryDirectory() as tmp:
            work = Path(tmp) / 'work'
            work.mkdir()
            with patch.object(observer.common, 'soft_run', return_value=(1, b'', b'unsupported')):
                result = observer.observe_row(row('jp2-boxed-rgb'), Path('/mock'), work)
        self.assertEqual('refused', result['outcome'])
        self.assertEqual('-pa', result['flag'])
        self.assertEqual(1, result['exitCode'])
        self.assertTrue(result['recordOnly'])
        self.assertIsNone(result['expectOk'])
        self.assertTrue(result['facts']['inputUnchanged'])
        self.assertEqual([], result['facts']['printedTokens'])
        self.assertFalse(result['facts']['container']['hasXmlBox'])
        self.assertEqual('owned-rgb.jp2', result['fixture'])

    def test_read_probe_mutation_is_reported_as_a_finding(self):
        def mutate(report, command, cwd):
            Path(command[-1]).write_bytes(b'changed')
            return 0, b'', b''
        with tempfile.TemporaryDirectory() as tmp:
            work = Path(tmp) / 'w'
            work.mkdir()
            with patch.object(observer.common, 'soft_run', side_effect=mutate):
                result = observer.observe_row(row(), Path('/mock'), work)
        self.assertEqual('scan-changed', result['outcome'])
        self.assertFalse(result['facts']['inputUnchanged'])

    def test_nonzero_exit_with_artifact_is_not_called_a_refusal(self):
        def extract(report, command, cwd):
            (Path(cwd) / 'case-thumb.jpg').write_bytes(b'artifact')
            return 1, b'', b'no thumbnail'
        with tempfile.TemporaryDirectory() as tmp:
            work = Path(tmp) / 'w'
            work.mkdir()
            with patch.object(observer.common, 'soft_run', side_effect=extract):
                result = observer.observe_row(row(family='thumbnail', operation='extract'),
                                              Path('/mock'), work)
        self.assertEqual('nonzero-exit-artifact-produced', result['outcome'])
        self.assertEqual(1, len(result['facts']['artifacts']))

    def test_only_the_authored_token_vocabulary_is_recorded(self):
        out = b'Artist = B8Artist, xmp:CreatorTool="B8Tool", some-other-token B9Nope'
        with tempfile.TemporaryDirectory() as tmp:
            work = Path(tmp) / 'w'
            work.mkdir()
            with patch.object(observer.common, 'soft_run', return_value=(0, out, b'')):
                result = observer.observe_row(row(), Path('/mock'), work)
        self.assertEqual(['B8Artist', 'B8Tool'], result['facts']['printedTokens'])
        self.assertEqual('accepted', result['outcome'])

    def test_runner_specific_paths_are_normalized_out_of_stderr(self):
        with tempfile.TemporaryDirectory() as tmp:
            work = Path(tmp) / 'w'
            work.mkdir()
            def failing(report, command, cwd):
                # soft_run yields bytes; a real refusal names the temp path it was given.
                return 1, b'', f'Error: unsupported file format: {cwd}/case.psd (FileType 0)\n'.encode()
            with patch.object(observer.common, 'soft_run', side_effect=failing):
                result = observer.observe_row(row(), Path('/mock'), work)
        self.assertNotIn(str(work), result['facts']['stderr'])
        self.assertIn('<fixture>', result['facts']['stderr'])
        self.assertLessEqual(len(result['facts']['stderr']), 300)

    def test_full_report_fits_the_annotation_recovery_budget(self):
        rows = [r for r in b8_rows() if r['layout'] in catalog.FIXTURES]
        observations = []
        with tempfile.TemporaryDirectory() as tmp:
            for index, r in enumerate(rows):
                work = Path(tmp) / f'w{index:02d}'
                work.mkdir()
                with patch.object(observer.common, 'soft_run',
                                  return_value=(0, b'Artist = B8Artist', b'')):
                    observations.append(observer.observe_row(r, Path('/mock'), work))
        self.assertEqual(31, len(observations))
        report = {'observations': observations,
                  'blocked': [{'id': r['rowId'], 'reason': observer.BLOCK_REASONS[r['format']]}
                              for r in b8_rows() if r['layout'] not in catalog.FIXTURES]}
        parts = observer.chunks(report)
        self.assertLessEqual(len(parts), 10)
        self.assertTrue(all(len(p) <= 4000 for p in parts))
        # The guard must actually guard: an incompressible oversized report is an error,
        # not a silently truncated annotation stream.
        import os
        padded = [{'blob': os.urandom(1500).hex()} for _ in range(200)]
        with self.assertRaises(ValueError):
            observer.chunks({'observations': padded})


if __name__ == '__main__':
    unittest.main()
