"""Offline behavior and failure-class tests for the B6 observer."""
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/reference'))
import dng_census_fixtures as fixtures
import observe_b6 as observer


def row(family, operation):
    return {'rowId': 'test-row', 'layout': 'dng-raw-ii', 'family': family,
            'operation': operation, 'duplicates': 'none', 'textEncoding': 'not-applicable'}


class ObserveB6Tests(unittest.TestCase):
    def test_fixture_choice_distinguishes_ifd1_from_preview(self):
        r = row('thumbnail', 'read')
        r['layout'] = 'dng-preview-mm'
        self.assertEqual('b6-thumb-mm-present-ifd1.dng', observer.fixture_for(r))
        r['operation'] = 'insert'
        self.assertEqual('b6-thumb-mm-empty.dng', observer.fixture_for(r))

    def test_thumbnail_nonzero_exit_with_extracted_artifact_is_not_refusal(self):
        with tempfile.TemporaryDirectory() as tmp:
            work = Path(tmp)
            source = work / 'input.dng'
            source.write_bytes(fixtures.build('II', 'raw', 'present', ifd1_jpeg=True))
            def fake(report, command, cwd):
                (cwd / 'case-thumb.jpg').write_bytes(fixtures.jpeg.thumb_jpeg_bytes())
                return 47, b'', b''
            with patch.object(observer.common, 'soft_run', side_effect=fake):
                result = observer.probe({'commands': []}, row('thumbnail', 'read'), Path('/mock'), source, work)
            self.assertEqual('nonzero-exit-artifact-produced', result['outcome'])
            self.assertTrue(result['expectOk'])
            self.assertTrue(result['facts']['scanPreserved'])

    def test_unparseable_success_is_not_preservation(self):
        with tempfile.TemporaryDirectory() as tmp:
            work = Path(tmp)
            source = work / 'input.dng'
            source.write_bytes(fixtures.build('II', 'raw', 'empty'))
            def fake(report, command, cwd):
                (cwd / 'case.dng').write_bytes(b'bad')
                return 0, b'', b''
            with patch.object(observer.common, 'soft_run', side_effect=fake):
                result = observer.probe({'commands': []}, row('exif', 'insert'), Path('/mock'), source, work)
            self.assertEqual('accepted-unparseable', result['outcome'])
            self.assertIsNone(result['facts']['scanPreserved'])

    def test_wrong_input_precondition_is_an_error(self):
        with tempfile.TemporaryDirectory() as tmp:
            work = Path(tmp)
            source = work / 'input.dng'
            source.write_bytes(fixtures.build('II', 'raw', 'empty'))
            with self.assertRaises(ValueError):
                observer.probe({'commands': []}, row('exif', 'replace'), Path('/mock'), source, work)


if __name__ == '__main__':
    unittest.main()
