"""Offline policy checks for B7 observation classification."""
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/reference'))
import b7_owned_fixtures as owned
import observe_b7 as observer


def row(layout, family='exif', operation='read'):
    return {'rowId': 'test-row', 'format': 'JXL' if layout.startswith('jxl') else 'AVIF',
            'layout': layout, 'family': family, 'operation': operation,
            'duplicates': 'none', 'textEncoding': 'not-applicable'}


class ObserveB7Tests(unittest.TestCase):
    def test_refusal_preserved_and_no_fixture_mutation(self):
        with tempfile.TemporaryDirectory() as tmp:
            work = Path(tmp)
            source = owned.FIXTURES / 'owned-codestream.jxl'
            with patch.object(observer.common, 'soft_run', return_value=(1, b'', b'unknown type')):
                result = observer.observe_row({'commands': []}, row('jxl-codestream'), Path('/mock'), source, work)
            self.assertEqual('refused', result['outcome'])
            self.assertTrue(result['facts']['inputUnchanged'])
            self.assertFalse(result['facts']['authoredTargetPresent'])
            self.assertEqual('codestream-signature', result['facts']['container']['signature'])

    def test_nonzero_extract_artifact_is_not_refusal(self):
        with tempfile.TemporaryDirectory() as tmp:
            work = Path(tmp)
            def fake(report, cmd, cwd):
                (cwd / 'case-thumb.jpg').write_bytes(b'output')
                return 7, b'', b''
            with patch.object(observer.common, 'soft_run', side_effect=fake):
                result = observer.observe_row({'commands': []}, row('bmff-avif', 'thumbnail', 'extract'),
                                              Path('/mock'), owned.FIXTURES / 'owned.avif', work)
            self.assertEqual('nonzero-exit-artifact-produced', result['outcome'])
            self.assertEqual(1, len(result['facts']['artifacts']))
            self.assertFalse(result['facts']['authoredTargetPresent'])

    def test_read_command_mutation_is_a_finding(self):
        with tempfile.TemporaryDirectory() as tmp:
            work = Path(tmp)
            def fake(report, cmd, cwd):
                cmd[-1].write_bytes(b'changed')
                return 0, b'', b''
            with patch.object(observer.common, 'soft_run', side_effect=fake):
                result = observer.observe_row({'commands': []}, row('bmff-avif'), Path('/mock'),
                                              owned.FIXTURES / 'owned.avif', work)
            self.assertEqual('scan-changed', result['outcome'])
            self.assertFalse(result['facts']['inputUnchanged'])


if __name__ == '__main__':
    unittest.main()
