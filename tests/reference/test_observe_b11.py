"""Offline policy tests for the B11 observation recorder.

Exiv2 is not installed here, so these tests exercise the decision logic against the real owned
fixtures with a replaced command runner: one read-only probe per census row, the narrowest flag
that reaches the family the row names, refusals recorded as refusals, input mutation reported as
a finding, no write flag reachable at all, the pinned `-et` byte-count semantics carried in the
record, and a report small enough to recover from check-run annotations whole.

Nothing here predicts what the real build will print. The one exception is the `-et` artifact
length, which is predicted from bytes this repository wrote: it checks the recorder, and a
mismatch is a finding about the recorder rather than a claim about CRW thumbnails.
"""
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/reference'))
import b11_container_fixtures as catalog  # noqa: E402
import observe_b11 as observer  # noqa: E402

SEED_FLAG_SET = {'-pS', '-pe', '-pc', '-pC', '-px', '-pa', '-et'}
VIDEO_FLAG_SET = {'-pS', '-pa', '-pC', '-px', '-et'}


def b11_rows():
    census = json.loads((ROOT / 'docs/research/EXIV2_CENSUS.json').read_text())
    return [r for r in census['rows'] if r['batch'] == 'B11']


def row(layout='ciff-mapped-v1', family='exif', operation='read'):
    # Format comes from the layout, so a test cannot pair BMP bytes with a CRW row.
    return {'rowId': 'test-row', 'format': catalog.FIXTURES[layout][0], 'layout': layout,
            'family': family, 'operation': operation, 'duplicates': 'none',
            'textEncoding': 'not-applicable'}


def workdir(tmp, name='w'):
    path = Path(tmp) / name
    path.mkdir(parents=True, exist_ok=True)
    return path


class ObserveB11Tests(unittest.TestCase):
    def test_census_partitions_into_observed_and_blocked_cells(self):
        rows = b11_rows()
        self.assertEqual(95, len(rows))
        blocked = [r for r in rows if r['layout'] not in catalog.FIXTURES]
        self.assertEqual(8, len(blocked))
        self.assertEqual({'CRW'}, {r['format'] for r in blocked})
        self.assertEqual(set(catalog.BLOCKED_LAYOUTS), {r['layout'] for r in blocked})
        self.assertEqual(87, len([r for r in rows if r['layout'] in catalog.FIXTURES]))
        self.assertIn('blocked', observer.BLOCK_REASONS['CRW'])
        self.assertIn('E01_B11_RIGHTS_AND_ORACLE_REVIEW.md', observer.BLOCK_REASONS['CRW'])
        self.assertEqual({'CRW'}, set(observer.BLOCK_REASONS))
        self.assertEqual(observer.SEED_FORMATS + observer.VIDEO_FORMATS, observer.FORMATS)

    def test_exactly_one_read_only_probe_per_row_and_the_narrowest_flag_for_it(self):
        for r in b11_rows():
            if r['layout'] not in catalog.FIXTURES:
                continue
            chosen = observer.flags_for(r)
            self.assertEqual(1, len(chosen), r['rowId'])
            flag = chosen[0]
            if r['format'] in observer.VIDEO_FORMATS:
                self.assertIn(flag, VIDEO_FLAG_SET, r['rowId'])
                self.assertEqual('-pS', flag, 'a container stub row is inventory-only')
            else:
                self.assertIn(flag, SEED_FLAG_SET, r['rowId'])
            if r['family'] == 'inventory':
                self.assertEqual('-pS', flag, r['rowId'])
            elif r['operation'] == 'extract':
                self.assertEqual('-et', flag, r['rowId'])
            else:
                self.assertEqual({'exif': '-pe', 'comments': '-pc', 'icc': '-pC',
                                  'xmp': '-px', 'iptc': '-pa', 'thumbnail': '-pa'}[r['family']],
                                 flag, r['rowId'])

    def test_no_write_flag_is_reachable_from_a_census_row(self):
        # -ei/-ex/-c/-d/-g/-r/-u are the mutating half of this CLI; a B11 row must never be
        # able to ask for one, because the batch observes readers, not writers.
        forbidden = {'-ei', '-ex', '-ep', '-ec', '-el', '-en', '-et!'}
        for r in b11_rows():
            self.assertNotIn(observer.flags_for(r)[0], forbidden, r['rowId'])
        for value in vars(observer).values():
            if isinstance(value, list) and value and all(isinstance(v, str) for v in value):
                self.assertFalse(forbidden & set(value))

    def test_dispatch_is_recorded_per_format_from_the_fixture_bytes(self):
        expected = {'ciff-mapped-v1': 'crw', 'bmp-dib-v3': 'bmp', 'gif-lsd-v1': 'gif',
                    'tga-full-v1': 'tga', 'tga-signature-wrong-name-v1': 'none',
                    'qtime-known-brand-v1': 'qtime', 'qtime-unknown-brand-v1': 'bmff',
                    'mkv-ebml-header-v1': 'mkv', 'riff-avi-avih-v1': 'riff',
                    'riff-wav-fmt-v1': 'riff', 'asf-header-guid-v1': 'asf'}
        for layout, dispatch in sorted(expected.items()):
            with tempfile.TemporaryDirectory() as tmp:
                with patch.object(observer.common, 'soft_run', return_value=(0, b'', b'')):
                    result = observer.observe_row(row(layout), Path('/mock'), workdir(tmp))
            self.assertEqual(dispatch, result['facts']['container']['dispatch'], layout)

    def test_container_facts_stay_inside_the_per_format_whitelist(self):
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(observer.common, 'soft_run', return_value=(0, b'', b'')):
                crw = observer.observe_row(row('ciff-mapped-v1'), Path('/mock'), workdir(tmp, 'a'))
                bmp = observer.observe_row(row('bmp-dib-v3', family='comments'), Path('/mock'),
                                           workdir(tmp, 'b'))
        container = crw['facts']['container']
        self.assertEqual({'dispatch', 'fileLength', 'fileSha256', 'matched', 'ciff', 'byteOrder',
                          'mappedKeys'}, set(container))
        self.assertEqual(set(observer.CIFF_KEYS), set(container['ciff']))
        self.assertEqual(9, len(container['mappedKeys']))
        self.assertEqual({'headerSize', 'width', 'height', 'bitsPerPixel', 'dataOffset'},
                         set(bmp['facts']['container']['bmp']))
        # A TGA record must carry the reachability fact, or the .targa twin looks like a bug
        # in the format rather than an unreachable code path in this build.
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(observer.common, 'soft_run', return_value=(0, b'', b'')):
                targa = observer.observe_row(row('tga-signature-wrong-name-v1'), Path('/mock'),
                                             workdir(tmp))
        self.assertFalse(targa['facts']['container']['tga']['footerRouteReachableByFactory'])
        self.assertTrue(targa['facts']['container']['tga']['footerSigned'])

    def test_every_layout_records_its_own_framing_block(self):
        # A record whose per-format block silently vanished would still be a clean-looking
        # observation of nothing: every layout must carry the block its format is walked as,
        # with the full key set. AVI and WAV share 'riff'; CRW's block is 'ciff', not 'crw'.
        for layout, (_fmt, _name) in sorted(catalog.FIXTURES.items()):
            with tempfile.TemporaryDirectory() as tmp:
                with patch.object(observer.common, 'soft_run', return_value=(0, b'', b'')):
                    result = observer.observe_row(row(layout), Path('/mock'), workdir(tmp))
            container = result['facts']['container']
            key = observer.BLOCK_KEY[catalog.FIXTURES[layout][0]]
            self.assertIn(key, container, layout)
            self.assertEqual(set(observer.FORMAT_KEYS[catalog.FIXTURES[layout][0]]),
                             set(container[key]), layout)
        self.assertEqual('riff', observer.BLOCK_KEY['WAV'])
        self.assertEqual('riff', observer.BLOCK_KEY['AVI'])
        self.assertEqual('ciff', observer.BLOCK_KEY['CRW'])

    def test_extract_row_records_the_byte_count_semantics(self):
        def extract(report, command, cwd):
            (Path(cwd) / 'case-thumb.jpg').write_bytes(b'abcdef')
            return 6, b'', b''
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(observer.common, 'soft_run', side_effect=extract):
                result = observer.observe_row(row('ciff-thumb-only-v1', family='thumbnail',
                                                  operation='extract'),
                                              Path('/mock'), workdir(tmp))
        semantics = result['facts']['extractSemantics']
        self.assertTrue(semantics['exitCodeIsByteCountInPinnedBuild'])
        self.assertEqual(6, semantics['artifactBytes'])
        self.assertTrue(semantics['exitCodeEqualsArtifactBytes'])
        self.assertEqual(len(owned_thumb_bytes()), semantics['predictedArtifactBytes'])
        self.assertFalse(semantics['predictionMatched'])
        self.assertEqual('nonzero-exit-artifact-produced', result['outcome'])

    def test_predicted_artifact_length_is_only_offered_for_a_fixture_with_a_thumbnail(self):
        self.assertEqual(len(owned_thumb_bytes()),
                         observer.predicted_artifact_bytes('ciff-thumb-only-v1', 'extract'))
        self.assertEqual(len(owned_thumb_bytes()),
                         observer.predicted_artifact_bytes('ciff-preview-plus-thumb-v1', 'extract'))
        self.assertIsNone(observer.predicted_artifact_bytes('ciff-mapped-v1', 'extract'))
        self.assertIsNone(observer.predicted_artifact_bytes('ciff-thumb-only-v1', 'read'))
        self.assertIsNone(observer.predicted_artifact_bytes('bmp-dib-v3', 'read'))

    def test_zero_exit_without_artifact_is_not_reported_as_success(self):
        def nothing(report, command, cwd):
            return 0, b'', b"case.crw: The image does not contain a thumbnail\n"
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(observer.common, 'soft_run', side_effect=nothing):
                result = observer.observe_row(row('ciff-mapped-v1', family='thumbnail',
                                                  operation='extract'),
                                              Path('/mock'), workdir(tmp))
        self.assertEqual('zero-exit-no-artifact', result['outcome'])
        self.assertTrue(result['facts']['extractSemantics']['reportedNoThumbnail'])
        self.assertTrue(result['facts']['extractSemantics']['exitCodeEqualsArtifactBytes'])
        self.assertIsNone(result['facts']['finding'])

    def test_artifact_written_with_zero_exit_is_a_byte_count_finding(self):
        def produced(report, command, cwd):
            (Path(cwd) / 'case-thumb.jpg').write_bytes(b'abcdef')
            return 0, b'', b''
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(observer.common, 'soft_run', side_effect=produced):
                result = observer.observe_row(row('ciff-thumb-only-v1', family='thumbnail',
                                                  operation='extract'),
                                              Path('/mock'), workdir(tmp))
        self.assertEqual('zero-exit-artifact-produced', result['outcome'])
        self.assertIn('does not equal the 6 bytes', result['facts']['finding'])

    def test_empty_listing_is_a_record_and_never_a_refusal(self):
        # -pa on a reader that fills nothing prints nothing and exits 0. That is a listing that
        # came back empty, which is not the same statement as "this family is unsupported".
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(observer.common, 'soft_run', return_value=(0, b'', b'')):
                result = observer.observe_row(row('bmp-dib-v3', family='iptc'), Path('/mock'),
                                              workdir(tmp))
        self.assertEqual('accepted', result['outcome'])
        self.assertEqual(0, result['facts']['stdoutLength'])
        self.assertIsNone(result['facts']['extractSemantics'])

    def test_structure_refusal_is_recorded_as_refused_with_its_stderr(self):
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(observer.common, 'soft_run',
                              return_value=(1, b'', b'Unsupported: The file format does not '
                                               b'support printStructure')):
                result = observer.observe_row(row('asf-header-guid-v1'), Path('/mock'),
                                              workdir(tmp))
        self.assertEqual('refused', result['outcome'])
        self.assertTrue(result['facts']['stderr'])
        self.assertTrue(result['recordOnly'])
        self.assertIsNone(result['expectOk'])

    def test_layout_and_format_disagreement_is_refused_before_any_command(self):
        broken = row()
        broken['format'] = 'BMP'
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
                result = observer.observe_row(row('gif-lsd-v1'), Path('/mock'), workdir(tmp))
        self.assertEqual('scan-changed', result['outcome'])
        self.assertFalse(result['facts']['inputUnchanged'])

    def test_the_fixture_extension_is_kept_because_tga_recognition_is_the_name(self):
        # The recorder copies the fixture to `case<suffix>` rather than to a fixed name: in
        # this build TGA recognition *is* the extension, so renaming the copy would ask a
        # different question than the census row does.
        seen = []

        def capture(report, command, cwd):
            seen.append(Path(command[-1]))
            return 0, b'', b''

        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(observer.common, 'soft_run', side_effect=capture):
                for layout in ('tga-signature-wrong-name-v1', 'tga-name-only-v1',
                               'ciff-mapped-v1'):
                    observer.observe_row(row(layout), Path('/mock'), workdir(tmp, layout))
        layouts = ('tga-signature-wrong-name-v1', 'tga-name-only-v1', 'ciff-mapped-v1')
        self.assertEqual(['case.targa', 'case.tga', 'case.crw'], [p.name for p in seen])
        for probe, layout in zip(seen, layouts):
            self.assertEqual(Path(catalog.FIXTURES[layout][1]).suffix, probe.suffix, layout)

    def test_only_the_authored_token_vocabulary_is_recorded(self):
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(observer.common, 'soft_run',
                              return_value=(0, b'Comment = B11CrwComment\nMake = CANON\n'
                                            b'OwnerName = B11Owner\nModel = B10 SYNTHETIC\n',
                                            b'')):
                result = observer.observe_row(row(), Path('/mock'), workdir(tmp))
        self.assertEqual(['B11CrwComment', 'B11Owner', 'CANON'], result['facts']['printedTokens'])

    def test_runner_specific_paths_are_normalized_out_of_stderr(self):
        with tempfile.TemporaryDirectory() as tmp:
            work = workdir(tmp)
            noise = f'{work}/case.crw: The image file contains corrupt data'
            with patch.object(observer.common, 'soft_run', return_value=(1, b'', noise.encode())):
                result = observer.observe_row(row(), Path('/mock'), work)
        self.assertIn('<fixture>', result['facts']['stderr'])
        self.assertNotIn(str(work), result['facts']['stderr'])

    def test_full_report_fits_the_annotation_recovery_budget(self):
        rows = [r for r in b11_rows() if r['layout'] in catalog.FIXTURES]
        observations = []
        with tempfile.TemporaryDirectory() as tmp:
            for index, r in enumerate(rows):
                work = workdir(tmp, f'w{index:03d}')
                with patch.object(observer.common, 'soft_run',
                                  return_value=(0, b'Comment = B11CrwComment', b'')):
                    observations.append(observer.observe_row(r, Path('/mock'), work))
        self.assertEqual(87, len(observations))
        report = {'observations': observations,
                  'blocked': [{'id': r['rowId'], 'reason': observer.BLOCK_REASONS['CRW']}
                              for r in b11_rows() if r['layout'] not in catalog.FIXTURES]}
        parts = observer.chunks(report)
        self.assertLessEqual(len(parts), 30)
        self.assertGreaterEqual(len(parts), 1)
        self.assertTrue(all(len(part) <= 4000 for part in parts))
        # A blocked-only report is still evidence: it must chunk, not raise.
        self.assertEqual(1, len(observer.chunks({'observations': [], 'blocked': [{'id': 'x'}]})))
        with self.assertRaises(ValueError):
            observer.chunks({'observations': [], 'blocked': []})
        import os
        padded = [{'blob': os.urandom(2000).hex()} for _ in range(300)]
        with self.assertRaises(ValueError):
            observer.chunks({'observations': padded})

    def test_timezone_is_part_of_the_record_because_0x180e_uses_localtime(self):
        env = observer.environment()
        self.assertIn('utcOffsetSeconds', env)
        self.assertIn('CrwMap::decode0x180e', env['note'])
        self.assertIn('TZ', env)

    def test_observer_shares_the_fixture_catalog_it_reports(self):
        # One manifest, one oracle: the recorder must not carry its own idea of the fixtures.
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(observer.common, 'soft_run', return_value=(0, b'', b'')):
                result = observer.observe_row(row('ciff-thumb-only-v1', family='thumbnail',
                                                  operation='extract'), Path('/mock'), workdir(tmp))
        manifest = {entry['file']: entry for entry in catalog.entries()}
        entry = manifest[result['fixture']]
        self.assertEqual(entry['sha256'], result['fixtureSha256'])
        self.assertEqual(entry['sha256'], result['facts']['container']['fileSha256'])
        self.assertEqual(entry['length'], result['facts']['container']['fileLength'])
        self.assertEqual(entry['thumbnailLength'],
                         observer.predicted_artifact_bytes(entry['layout'], 'extract'))


def owned_thumb_bytes():
    sys.path.insert(0, str(ROOT / 'tools/reference'))
    import jpeg_owned_fixtures as owned
    return owned.thumb_jpeg_bytes()


if __name__ == '__main__':
    unittest.main()
