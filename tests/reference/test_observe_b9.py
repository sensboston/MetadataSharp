"""Offline policy tests for the B9 observation recorder.

Exiv2 is not installed here, so these tests exercise the decision logic against the
real owned fixtures with a replaced command runner: exactly one read-only probe per
census row, refusals recorded as refusals, input mutation reported as a finding, no
write flag ever reachable, and a report small enough to recover from annotations.
"""
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/reference'))
import b9_raw_fixtures as catalog  # noqa: E402
import observe_b9 as observer  # noqa: E402


def b9_rows():
    census = json.loads((ROOT / 'docs/research/EXIV2_CENSUS.json').read_text())
    return [r for r in census['rows'] if r['batch'] == 'B9']


def row(layout='raw-tiff-ep-ii-arw', family='exif', operation='read'):
    # Format is derived from the layout so a test cannot pair ORF bytes with an ARW row.
    return {'rowId': 'test-row', 'format': catalog.FIXTURES[layout][0], 'layout': layout,
            'family': family, 'operation': operation, 'duplicates': 'none',
            'textEncoding': 'not-applicable'}


class ObserveB9Tests(unittest.TestCase):
    def test_census_partitions_into_observed_and_blocked_cells(self):
        rows = b9_rows()
        self.assertEqual(96, len(rows))
        blocked = [r for r in rows if r['layout'] not in catalog.FIXTURES]
        self.assertEqual(48, len(blocked))
        self.assertEqual({'ARW', 'CR2', 'NEF', 'ORF', 'PEF', 'SRW'}, {r['format'] for r in blocked})
        self.assertEqual(set(catalog.VENDOR_LAYOUTS), {r['layout'] for r in blocked})
        self.assertEqual(len(rows) - 48, len([r for r in rows if r['layout'] in catalog.FIXTURES]))
        # Every blocked cell carries its own per-format reason.
        for fmt in sorted(observer.BLOCK_REASONS):
            self.assertIn(fmt, observer.BLOCK_REASONS[fmt])
            self.assertIn('blocked', observer.BLOCK_REASONS[fmt])

    def test_exactly_one_read_only_probe_per_row(self):
        flags = {'-pS', '-pa', '-pc', '-pC', '-et'}
        for r in b9_rows():
            chosen = observer.flags_for(r)
            self.assertEqual(1, len(chosen))
            self.assertIn(chosen[0], flags, 'only read-only flags may be reachable')
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
                result = observer.observe_row(row('raw-orf-ii'), Path('/mock'), work)
        self.assertEqual('refused', result['outcome'])
        self.assertEqual(1, result['exitCode'])
        self.assertTrue(result['recordOnly'])
        self.assertIsNone(result['expectOk'])
        self.assertTrue(result['facts']['inputUnchanged'])
        self.assertEqual([], result['facts']['printedTokens'])
        self.assertEqual('orf', result['facts']['container']['dispatch'])
        self.assertEqual('owned-marker-orf.orf', result['fixture'])
        self.assertEqual('owned-synthetic', result['fixtureRights'])

    def test_layout_and_format_disagreement_is_refused_before_any_command(self):
        broken = row()
        broken['format'] = 'NEF'
        with tempfile.TemporaryDirectory() as tmp:
            work = Path(tmp) / 'w'
            work.mkdir()
            with patch.object(observer.common, 'soft_run', return_value=(0, b'', b'')) as run:
                with self.assertRaises(ValueError):
                    observer.observe_row(broken, Path('/mock'), work)
        self.assertEqual(0, run.call_count)

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
        with tempfile.TemporaryDirectory() as tmp:
            work = Path(tmp) / 'w'
            work.mkdir()
            with patch.object(observer.common, 'soft_run',
                              return_value=(0, b'Artist  = B9Artist\nSoftware = B9Tool\n'
                                            b'xmp: MetadataSharp\n', b'')):
                result = observer.observe_row(row(), Path('/mock'), work)
        self.assertEqual(['B9Artist', 'B9Tool'], result['facts']['printedTokens'])
        # An unrelated 'B9Comment' string in stderr must not appear as a printed token.
        self.assertNotIn('B9Comment', result['facts']['printedTokens'])

    def test_container_facts_stay_inside_the_whitelist(self):
        with tempfile.TemporaryDirectory() as tmp:
            work = Path(tmp) / 'w'
            work.mkdir()
            with patch.object(observer.common, 'soft_run', return_value=(0, b'', b'')):
                result = observer.observe_row(row('raw-cr2-ii'), Path('/mock'), work)
        self.assertEqual(set(observer.FACT_KEYS), set(result['facts']['container']))
        self.assertNotIn('recognizers', result['facts']['container'])
        self.assertNotIn('ifd0Tags', result['facts']['container'])
        self.assertEqual(16, result['facts']['container']['rootOffset'])

    def test_runner_specific_paths_are_normalized_out_of_stderr(self):
        with tempfile.TemporaryDirectory() as tmp:
            work = Path(tmp) / 'w'
            work.mkdir()
            noise = f'{work}/case.arw: The image file contains corrupt data'
            with patch.object(observer.common, 'soft_run', return_value=(1, b'', noise.encode())):
                result = observer.observe_row(row(), Path('/mock'), work)
        self.assertIn('<fixture>', result['facts']['stderr'])
        self.assertNotIn(str(work), result['facts']['stderr'])

    def test_full_report_fits_the_annotation_recovery_budget(self):
        rows = [r for r in b9_rows() if r['layout'] in catalog.FIXTURES]
        observations = []
        with tempfile.TemporaryDirectory() as tmp:
            for index, r in enumerate(rows):
                work = Path(tmp) / f'w{index:02d}'
                work.mkdir()
                with patch.object(observer.common, 'soft_run',
                                  return_value=(0, b'Artist = B9Artist', b'')):
                    observations.append(observer.observe_row(r, Path('/mock'), work))
        self.assertEqual(48, len(observations))
        report = {'observations': observations,
                  'blocked': [{'id': r['rowId'], 'reason': observer.BLOCK_REASONS[r['format']]}
                              for r in b9_rows() if r['layout'] not in catalog.FIXTURES]}
        parts = observer.chunks(report)
        self.assertLessEqual(len(parts), 10)
        self.assertGreaterEqual(len(parts), 1)
        # An empty report must fail loudly instead of annotating nothing.
        with self.assertRaises(ValueError):
            observer.chunks({'observations': [], 'blocked': []})
        self.assertTrue(all(len(p) <= 4000 for p in parts))
        # The guard must actually guard: an incompressible oversized report is an error,
        # not a silently truncated annotation stream.
        import os
        padded = [{'blob': os.urandom(1500).hex()} for _ in range(200)]
        with self.assertRaises(ValueError):
            observer.chunks({'observations': padded})


if __name__ == '__main__':
    unittest.main()
