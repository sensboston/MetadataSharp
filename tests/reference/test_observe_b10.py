"""Offline policy tests for the B10 observation recorder.

Exiv2 is not installed here, so these tests exercise the decision logic against the real
owned fixtures with a replaced command runner: exactly one read-only probe per census row,
refusals recorded as refusals, input mutation reported as a finding, no write flag ever
reachable, the pinned `-et` byte-count semantics carried in the record, and a report small
enough to recover from check-run annotations whole.
"""
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/reference'))
import b10_raw_fixtures as catalog  # noqa: E402
import observe_b10 as observer  # noqa: E402


def b10_rows():
    census = json.loads((ROOT / 'docs/research/EXIV2_CENSUS.json').read_text())
    return [r for r in census['rows'] if r['batch'] == 'B10']


def row(layout='raw-mrw-ttw-embedded-tiff', family='exif', operation='read'):
    # Format is derived from the layout so a test cannot pair RAF bytes with an MRW row.
    return {'rowId': 'test-row', 'format': catalog.FIXTURES[layout][0], 'layout': layout,
            'family': family, 'operation': operation, 'duplicates': 'none',
            'textEncoding': 'not-applicable'}


def workdir(tmp, name='w'):
    path = Path(tmp) / name
    path.mkdir(parents=True, exist_ok=True)
    return path


class ObserveB10Tests(unittest.TestCase):
    def test_census_partitions_into_observed_and_blocked_cells(self):
        rows = b10_rows()
        self.assertEqual(64, len(rows))
        blocked = [r for r in rows if r['layout'] not in catalog.FIXTURES]
        self.assertEqual(32, len(blocked))
        self.assertEqual(set(observer.FORMATS), {r['format'] for r in blocked})
        self.assertEqual(set(catalog.VENDOR_LAYOUTS), {r['layout'] for r in blocked})
        self.assertEqual(32, len([r for r in rows if r['layout'] in catalog.FIXTURES]))
        for fmt in sorted(observer.BLOCK_REASONS):
            self.assertIn(fmt, observer.BLOCK_REASONS[fmt])
            self.assertIn('blocked', observer.BLOCK_REASONS[fmt])
        self.assertEqual(4, len(observer.BLOCK_REASONS))

    def test_exactly_one_read_only_probe_per_row(self):
        flags = {'-pS', '-pa', '-pc', '-pC', '-et'}
        for r in b10_rows():
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

    def test_recognition_is_recorded_per_format_from_the_fixture_bytes(self):
        # SR2 has no reader of its own, so its dispatch is the *generic* TIFF one; that is
        # the finding, and the test states it rather than deriving it.
        for fmt, dispatch, layout in (
                ('MRW', 'mrw', 'raw-mrw-ttw-embedded-tiff'),
                ('RAF', 'raf', 'raw-raf-fujifilm-be-offsets'),
                ('RW2', 'rw2', 'raw-rw2-ii-55-root-24'),
                ('SR2', 'tiff', 'raw-tiff-ep-ii-sr2')):
            with tempfile.TemporaryDirectory() as tmp:
                with patch.object(observer.common, 'soft_run', return_value=(0, b'', b'')):
                    result = observer.observe_row(row(layout), catalog.DIRECTORY,
                                                  workdir(tmp))
            container = result['facts']['container']
            self.assertEqual(dispatch, container['dispatch'])
            # The dispatch evidence is the accepted-by map, and it must say that only the
            # intended reader takes the file (SR2 by the generic TIFF reader, nothing else).
            self.assertEqual({'mrw': fmt == 'MRW', 'tiff': fmt == 'SR2', 'rw2': fmt == 'RW2',
                              'raf': fmt == 'RAF'}, container['acceptedBy'], fmt)

    def test_embedded_blocks_travel_as_facts_not_as_a_second_parser(self):
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(observer.common, 'soft_run', return_value=(0, b'', b'')):
                mrw = observer.observe_row(row('raw-mrw-ttw-embedded-tiff'), Path('/mock'),
                                           workdir(tmp, 'a'))
                raf = observer.observe_row(row('raw-raf-fujifilm-be-offsets'), Path('/mock'),
                                           workdir(tmp, 'b'))
        self.assertEqual(['\x00PRD', '\x00WBG', '\x00RIF', '\x00TTW'],
                         mrw['facts']['container']['mrw']['blockTags'])
        self.assertTrue(mrw['facts']['container']['mrw']['coversFile'])
        self.assertEqual(['icc'], raf['facts']['container']['embedded']['carriers'])
        # The preview travels as its own digests; the nested reparse stays in the catalog.
        self.assertEqual({'jpegSha256', 'app1Length', 'exifTiffLength', 'commentSha256'},
                         set(raf['facts']['container']['raf']['preview']))

    def test_extract_row_records_the_byte_count_semantics(self):
        def extract(report, command, cwd):
            (Path(cwd) / 'case-thumb.jpg').write_bytes(b'abcdef')
            return 6, b'', b''
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(observer.common, 'soft_run', side_effect=extract):
                result = observer.observe_row(row(family='thumbnail', operation='extract'),
                                               Path('/mock'), workdir(tmp))
        semantics = result['facts']['extractSemantics']
        self.assertTrue(semantics['exitCodeIsByteCountInPinnedBuild'])
        self.assertEqual(6, semantics['artifactBytes'])
        self.assertTrue(semantics['exitCodeEqualsArtifactBytes'])
        # an artifact produced by a nonzero-exit command is neither accepted nor refused
        self.assertEqual('nonzero-exit-artifact-produced', result['outcome'])

    def test_zero_exit_without_artifact_is_not_reported_as_success(self):
        def nothing(report, command, cwd):
            return 0, b'', b'case.mrw: Image does not contain an Exif thumbnail'
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(observer.common, 'soft_run', side_effect=nothing):
                result = observer.observe_row(row(family='thumbnail', operation='extract'),
                                               Path('/mock'), workdir(tmp))
        self.assertEqual('zero-exit-no-artifact', result['outcome'])
        self.assertTrue(result['facts']['extractSemantics']['reportedNoThumbnail'])
        # Nothing was written and the status is 0: the byte-count rule holds, so this is a
        # no-thumbnail record, not a finding and not a refusal.
        self.assertTrue(result['facts']['extractSemantics']['exitCodeEqualsArtifactBytes'])
        self.assertIsNone(result['facts']['finding'])

    def test_artifact_written_with_zero_exit_is_a_byte_count_finding(self):
        def produced(report, command, cwd):
            (Path(cwd) / 'case-thumb.jpg').write_bytes(b'abcdef')
            return 0, b'', b''
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(observer.common, 'soft_run', side_effect=produced):
                result = observer.observe_row(row(family='thumbnail', operation='extract'),
                                               Path('/mock'), workdir(tmp))
        self.assertEqual('zero-exit-artifact-produced', result['outcome'])
        self.assertIn('does not equal the 6 bytes', result['facts']['finding'])

    def test_read_rows_have_no_extract_semantics_field(self):
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(observer.common, 'soft_run', return_value=(0, b'', b'')):
                result = observer.observe_row(row(), Path('/mock'), workdir(tmp))
        self.assertIsNone(result['facts']['extractSemantics'])
        self.assertIsNone(result['facts']['finding'])

    def test_refusal_is_recorded_and_input_stays_byte_identical(self):
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(observer.common, 'soft_run',
                              return_value=(1, b'', b"Unsupported: printStructure")):
                result = observer.observe_row(row('raw-tiff-ep-ii-sr2'), Path('/mock'),
                                              workdir(tmp))
        self.assertEqual('refused', result['outcome'])
        self.assertEqual(1, result['exitCode'])
        self.assertTrue(result['recordOnly'])
        self.assertIsNone(result['expectOk'])
        self.assertTrue(result['facts']['inputUnchanged'])
        self.assertEqual([], result['facts']['printedTokens'])
        self.assertEqual('owned-marker-sr2.sr2', result['fixture'])
        self.assertEqual('owned-synthetic', result['fixtureRights'])

    def test_layout_and_format_disagreement_is_refused_before_any_command(self):
        broken = row()
        broken['format'] = 'RAF'
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(observer.common, 'soft_run', return_value=(0, b'', b'')) as run:
                with self.assertRaises(ValueError):
                    observer.observe_row(broken, Path('/mock'), workdir(tmp))
        self.assertEqual(0, run.call_count)

    def test_read_probe_mutation_is_reported_as_a_finding(self):
        def mutate(report, command, cwd):
            Path(command[-1]).write_bytes(b'changed')
            return 0, b'', b''
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(observer.common, 'soft_run', side_effect=mutate):
                result = observer.observe_row(row('raw-rw2-ii-55-root-24'), Path('/mock'),
                                              workdir(tmp))
        self.assertEqual('scan-changed', result['outcome'])
        self.assertFalse(result['facts']['inputUnchanged'])

    def test_only_the_authored_token_vocabulary_is_recorded(self):
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(observer.common, 'soft_run',
                              return_value=(0, b'Artist = B10Artist\nSoftware = B10Tool\n'
                                            b'Comment = B10RafComment\n', b'')):
                result = observer.observe_row(row(), Path('/mock'), workdir(tmp))
        self.assertEqual(['B10Artist', 'B10Tool'], result['facts']['printedTokens'])

    def test_container_facts_stay_inside_the_whitelist(self):
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(observer.common, 'soft_run', return_value=(0, b'', b'')):
                result = observer.observe_row(row('raw-rw2-ii-55-root-24'), Path('/mock'),
                                              workdir(tmp))
        container = result['facts']['container']
        self.assertEqual(set(observer.FACT_KEYS) | {'acceptedBy', 'embedded', 'rw2'},
                         set(container))
        self.assertEqual(set(observer.EMBED_KEYS), set(container['embedded']))
        self.assertNotIn('ifd0Tags', container['embedded'])
        self.assertEqual(24, container['rw2']['rootOffset'])

    def test_runner_specific_paths_are_normalized_out_of_stderr(self):
        with tempfile.TemporaryDirectory() as tmp:
            work = workdir(tmp)
            noise = f'{work}/case.raf: The image file contains corrupt data'
            with patch.object(observer.common, 'soft_run', return_value=(1, b'', noise.encode())):
                result = observer.observe_row(row('raw-raf-fujifilm-be-offsets'), Path('/mock'), work)
        self.assertIn('<fixture>', result['facts']['stderr'])
        self.assertNotIn(str(work), result['facts']['stderr'])

    def test_full_report_fits_the_annotation_recovery_budget(self):
        rows = [r for r in b10_rows() if r['layout'] in catalog.FIXTURES]
        observations = []
        with tempfile.TemporaryDirectory() as tmp:
            for index, r in enumerate(rows):
                work = workdir(tmp, f'w{index:02d}')
                with patch.object(observer.common, 'soft_run',
                                  return_value=(0, b'Artist = B10Artist', b'')):
                    observations.append(observer.observe_row(r, Path('/mock'), work))
        self.assertEqual(32, len(observations))
        report = {'observations': observations,
                  'blocked': [{'id': r['rowId'], 'reason': observer.BLOCK_REASONS[r['format']]}
                              for r in b10_rows() if r['layout'] not in catalog.FIXTURES]}
        parts = observer.chunks(report)
        self.assertLessEqual(len(parts), 10)
        self.assertGreaterEqual(len(parts), 1)
        self.assertTrue(all(len(part) <= 4000 for part in parts))
        with self.assertRaises(ValueError):
            observer.chunks({'observations': [], 'blocked': []})
        import os
        padded = [{'blob': os.urandom(1500).hex()} for _ in range(200)]
        with self.assertRaises(ValueError):
            observer.chunks({'observations': padded})

    def test_observer_shares_the_fixture_catalog_it_reports(self):
        # The recorder must not carry its own idea of the fixtures: one manifest, one
        # oracle, so a fixture change cannot slip past the census.
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(observer.common, 'soft_run', return_value=(0, b'', b'')):
                result = observer.observe_row(row('raw-raf-fujifilm-be-offsets'), Path('/mock'),
                                              workdir(tmp))
        manifest = {entry['file']: entry for entry in catalog.entries()}
        entry = manifest[result['fixture']]
        self.assertEqual(entry['sha256'], result['fixtureSha256'])
        self.assertEqual(entry['sha256'], result['facts']['container']['fileSha256'])
        self.assertEqual(entry['length'], result['facts']['container']['fileLength'])


if __name__ == '__main__':
    unittest.main()
