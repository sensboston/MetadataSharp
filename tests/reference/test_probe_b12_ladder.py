"""Offline tests for the B12 ladder recorder.

These exercise the recorder's own bookkeeping: that a row's family is derived from what the CLI printed, that
the prediction a row is compared against is the one committed in the manifest (so the comparison cannot be
retrofitted after the run), that the fixture name reaches the build unchanged (for `.targa` the name *is* the
input under test), and that the report stays recoverable from annotations. No test here asserts anything about
what the pinned build does -- an offline mock cannot show that, and pretending otherwise is exactly the mistake
B11's review warns about.
"""
import base64
from pathlib import Path
import json
import sys
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch
import zlib

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/reference'))
import b12_recognition_ladder as ladder  # noqa: E402
import observe_b0 as common  # noqa: E402
import probe_b12_ladder as probe  # noqa: E402

# The exact CLI framing, so classify() is tested against the shape the build really produces:
# `app/actions.cpp:225-227` prints "Exiv2 exception in print action for file <path>:" and then the
# error, and each family's body is the pinned message text this review cited.
PREFIX = 'Exiv2 exception in print action for file case{s}:\n'
MESSAGES = {'unknown-image-type': 'case{s}: The file contains data of an unknown image type\n',
            'reader-reached': 'Image type case{s} is not supported\n',
            'throws-during-scan': 'Failed to read input data\n',
            'corrupted-metadata': 'corrupted image metadata\n'}


def cli_error(suffix, family):
    return (PREFIX + MESSAGES[family]).format(s=suffix)


def fake_cli(entry, suffix):
    """A `soft_run` stand-in that answers with the message family the rung predicted."""
    family = entry['prediction']['predictedFamily']

    def run(commands, argv, cwd=None):
        commands['commands'].append([str(part) for part in argv])
        if family == 'reader-reached' and argv[1] == '-pe':
            return 0, b'', b''
        if family == 'reader-reached' and argv[1] == '-pS':
            # For these three formats "reached a reader" *is* the base printStructure throw.
            return 1, b'', cli_error(suffix, 'reader-reached').encode()
        if family == 'reader-reached':
            return 0, b'', b''
        return 1, b'', cli_error(suffix, family).encode()
    return run


class ProbeB12Tests(unittest.TestCase):
    def entry(self, layout):
        matches = [e for e in ladder.entries() if e['layout'] == layout]
        self.assertEqual(1, len(matches), layout)
        return matches[0]

    def test_flags_come_from_the_catalog_and_are_read_only(self):
        self.assertEqual(('-pS', '-pe'), tuple(probe.FLAGS))
        self.assertEqual(tuple(ladder.FLAGS), tuple(probe.FLAGS),
                         'the recorder and the fixtures must ask the same two questions')
        for banned in ('-ex', '-eo', '-et', '-ee', '-d', '-E', '-r', '-fi', '-mo', '-mv', '-o'):
            self.assertNotIn(banned, probe.FLAGS, f'{banned} writes or extracts: not a recognition probe')

    def test_family_is_read_from_the_message_not_from_the_exit_code(self):
        cases = [
            (1, '', cli_error('.bmp', 'unknown-image-type'), 'unknown-image-type'),
            (1, '', cli_error('.bmp', 'reader-reached'), 'reader-reached'),
            (1, '', cli_error('.gif', 'throws-during-scan'), 'throws-during-scan'),
            (1, '', cli_error('.gif', 'corrupted-metadata'), 'corrupted-metadata'),
            (0, '', '', 'reader-reached'),
            (0, 'MIME type = image/gif\n', '', 'reader-reached'),
            (1, '', 'some other complaint\n', 'other'),
            (-9, '', '', 'other'),
        ]
        for code, out, err, expected in cases:
            row = {'exitCode': code, 'stdout': out, 'stderr': err}
            family, needle = probe.classify(row)
            self.assertEqual(expected, family, err or f'exit {code}')
            # The needle is the *reason*: it must be a substring of what the build printed, or the
            # label was invented rather than read.
            if needle is not None:
                self.assertIn(needle, err)

    def test_message_of_keeps_the_builds_own_words(self):
        text = cli_error('.bmp', 'unknown-image-type')
        self.assertEqual('case.bmp: The file contains data of an unknown image type',
                         probe.message_of(text))
        self.assertIsNone(probe.message_of(''))

    def test_record_shape_carries_the_prediction_it_will_be_judged_by(self):
        entry = self.entry('bmp-len080-v1')
        source = ladder.DIRECTORY / entry['file']
        with TemporaryDirectory() as tmp:
            with patch.object(common, 'soft_run', side_effect=fake_cli(entry, '.bmp')):
                result = probe.run_probe(Path('/mock'), source, Path(tmp) / 'w', entry)
        self.assertEqual(list(probe.FLAGS), result['flags'])
        self.assertEqual(2, len(result['operations']))
        self.assertEqual(2, len(result['commands']), 'one command line per flag, recorded for reproduction')
        self.assertEqual(entry['size'], result['size'])
        self.assertEqual(ladder.sha256(source.read_bytes()), result['fixtureSha256'])
        for op in result['operations']:
            self.assertEqual(entry['prediction']['predictedFamily'], op['predictedFamily'])
            self.assertTrue(op['matchesPrediction'], op)
            self.assertTrue(op['inputUnchanged'])
            self.assertTrue(op['noArtifactProduced'])
            self.assertEqual(op['stdoutLength'], len(op['stdout'].encode()))

    def test_the_fixture_name_is_passed_through_unchanged(self):
        """`isTgaType` tests the path, so the recorder may not rename the extension it asks about."""
        entry = self.entry('targa-len096-v1')
        source = ladder.DIRECTORY / entry['file']
        with TemporaryDirectory() as tmp:
            with patch.object(common, 'soft_run', side_effect=fake_cli(entry, '.targa')) as run:
                probe.run_probe(Path('/mock'), source, Path(tmp) / 'w', entry)
            called = [call[0] for call in run.call_args_list]
        self.assertEqual(2, len(called))
        for commands, argv in called:
            self.assertIn(argv[0], ('/mock', Path('/mock')), argv)
            self.assertIn(argv[1], probe.FLAGS)
            self.assertTrue(str(argv[2]).endswith('case.targa'),
                            f'the rung must be probed under the extension that decides its fate: {argv}')
            self.assertEqual(str(Path(source).suffix), Path(argv[2]).suffix)

    def test_input_mutation_and_stray_artifacts_are_recorded_not_assumed(self):
        entry = self.entry('bmp-len008-v1')
        source = ladder.DIRECTORY / entry['file']
        with TemporaryDirectory() as tmp:
            def mutator(commands, argv, cwd=None):
                (Path(cwd) / 'case.bmp').write_bytes(b'x')
                (Path(cwd) / 'case.bmp~').write_bytes(b'stale')
                commands['commands'].append(argv)
                return 1, b'', b''
            with patch.object(common, 'soft_run', side_effect=mutator):
                result = probe.run_probe(Path('/mock'), source, Path(tmp) / 'w', entry)
        for op in result['operations']:
            self.assertFalse(op['inputUnchanged'], 'a mock that rewrote its input must be reported')
            self.assertFalse(op['noArtifactProduced'])
            self.assertEqual(['case.bmp~'], [a['file'] for a in op['artifacts']])
            self.assertEqual('other', op['family'])

    def summary(self, families_by_size, kind='bmp'):
        rows = []
        for size, family in sorted(families_by_size.items()):
            rows.append({'kind': kind, 'size': size, 'operations': [
                {'flag': flag, 'family': family, 'predictedFamily': family,
                 'matchesPrediction': family == 'x'} for flag in probe.FLAGS]})
        return probe.ladder_summary({'observations': rows})[kind]

    def test_summary_finds_the_flip_and_only_the_flip(self):
        summary = self.summary({8: 'throws-during-scan', 40: 'unknown-image-type',
                                79: 'unknown-image-type', 80: 'reader-reached', 96: 'reader-reached'})
        self.assertEqual(80, summary['flipSize'])
        self.assertEqual(79, summary['lastRefusal'])
        self.assertTrue(summary['monotonic'], 'a single boundary is what the ladder is for')
        self.assertEqual(5, summary['rungs'])
        # A rung that refuses again after a reached one is a finding, not a rounding error.
        broken = self.summary({8: 'unknown-image-type', 80: 'reader-reached', 96: 'unknown-image-type'})
        self.assertEqual(80, broken['flipSize'])
        self.assertEqual(96, broken['lastRefusal'])
        # The report says so out loud instead of quietly reporting a boundary at 80: a ladder that
        # refuses again after it reached a reader has no single flip, and neither does the model.
        self.assertFalse(broken['monotonic'])
        self.assertLess(broken['flipSize'], broken['lastRefusal'])

    def test_summary_counts_disagreements_from_the_rows(self):
        rows = [{'kind': 'gif', 'size': 13, 'operations': [
            {'flag': flag, 'family': 'corrupted-metadata', 'predictedFamily': 'throws-during-scan',
             'matchesPrediction': False} for flag in probe.FLAGS]}]
        summary = probe.ladder_summary({'observations': rows})['gif']
        self.assertEqual(2, summary['rowsDifferingFromPrediction'])
        self.assertEqual([(13, '-pS', 'corrupted-metadata', 'throws-during-scan'),
                          (13, '-pe', 'corrupted-metadata', 'throws-during-scan')], summary['disagreements'])
        self.assertIsNone(summary['flipSize'])
        self.assertEqual([], summary['attributableRungs'])

    def test_report_is_recoverable_from_annotations_in_full(self):
        report = {'status': 'passed', 'observations': [], 'fixtureManifest': []}
        entries = ladder.entries()
        self.assertEqual(47, len(entries))
        self.assertEqual(94, len(entries) * len(probe.FLAGS))
        with TemporaryDirectory() as tmp:
            for entry in entries:
                source = ladder.DIRECTORY / entry['file']
                suffix = Path(entry['file']).suffix
                with patch.object(common, 'soft_run', side_effect=fake_cli(entry, suffix)):
                    report['observations'].append(
                        probe.run_probe(Path('/mock'), source, Path(tmp) / Path(entry['file']).stem, entry))
                report['fixtureManifest'].append(entry)
        encoded = base64.b64encode(zlib.compress(
            json.dumps(report, separators=(',', ':'), sort_keys=True).encode(), 9)).decode()
        parts = [encoded[i:i + 4000] for i in range(0, len(encoded), 4000)]
        self.assertLessEqual(len(parts), probe.CHUNK_LIMIT,
                             'the ladder report must stay recoverable from annotations')
        self.assertEqual(report, json.loads(zlib.decompress(base64.b64decode(''.join(parts)))))
        # Every rung agrees with its own prediction under the mock, which is the point of the shape:
        # if classify() and predicted_family() ever drift apart, this is where it shows.
        for row in report['observations']:
            self.assertTrue(all(op['matchesPrediction'] for op in row['operations']), row['layout'])


if __name__ == '__main__':
    unittest.main()
