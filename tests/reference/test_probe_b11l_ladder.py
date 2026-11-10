"""Offline tests for the B11L ladder recorder.

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
import b11l_recognition_ladder as ladder  # noqa: E402
import observe_b0 as common  # noqa: E402
import probe_b11l_ladder as probe  # noqa: E402

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


class ProbeB11LTests(unittest.TestCase):
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
        self.assertEqual([[13, '-pS', 'corrupted-metadata', 'throws-during-scan'],
                          [13, '-pe', 'corrupted-metadata', 'throws-during-scan']], summary['disagreements'])
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


class B11LEvidenceTests(unittest.TestCase):
    """The committed ladder payload must tie out -- to the bytes it probed, to the model it was
    compared against, and to the numbers its own README quotes.

    This is the B11 discipline applied to a diagnostic batch: the report is not evidence because CI
    printed it, it is evidence because every derived field in it can be recomputed from the committed
    fixtures and the recorded CLI text. Each `*-observations.json` in the evidence directory is checked
    the same way, so a second run's payload cannot be committed as a decoration: when more than one is
    present they must agree on the `observations` section and differ nowhere else without being named.
    """

    EVIDENCE = ROOT / 'docs/research/evidence/e01-b11l-ladder'

    def samples(self):
        # `observations.json` sorts first, so docs[0] is always the run the review authorised.
        return sorted(path.name for path in self.EVIDENCE.glob('*observations.json'))

    def test_every_rung_and_flag_is_present(self):
        entries = ladder.entries()
        for name in self.samples():
            report = json.loads((self.EVIDENCE / name).read_text(encoding='utf-8'))
            self.assertEqual('passed', report['status'], name)
            self.assertEqual(len(entries), len(report['observations']), name)
            self.assertEqual(len(entries) * len(probe.FLAGS), report['recordCount'], name)
            self.assertEqual({e['layout'] for e in entries}, {row['layout'] for row in report['observations']}, name)
            for row in report['observations']:
                self.assertEqual(list(probe.FLAGS), row['flags'], name)
                self.assertEqual(len(probe.FLAGS), len({op['flag'] for op in row['operations']}), name)

    def test_families_summaries_and_predictions_are_recomputable(self):
        entries = {e['layout']: e for e in ladder.entries()}
        for name in self.samples():
            report = json.loads((self.EVIDENCE / name).read_text(encoding='utf-8'))
            for row in report['observations']:
                entry = entries[row['layout']]
                self.assertEqual(entry['size'], row['size'], name)
                self.assertEqual(entry['sha256'], row['fixtureSha256'], f'{name}: the rung probed is not the rung committed')
                for op in row['operations']:
                    family, _ = probe.classify({'exitCode': op['exitCode'], 'stdout': op['stdout'],
                                                'stderr': op['stderr']})
                    self.assertEqual(family, op['family'], f"{name}: {row['layout']} {op['flag']}")
                    self.assertEqual(probe.message_of(op['stderr']), op['message'], name)
                    predicted = ladder.predicted_outcome(entry['kind'], entry['size'])['predictedFamily']
                    self.assertEqual(predicted, op['predictedFamily'],
                                     f"{name}: {row['layout']} was judged against a different model than this one")
                    self.assertEqual(predicted == op['family'], op['matchesPrediction'], name)
            self.assertEqual(report['summary'], probe.ladder_summary(report), f'{name}: summary is stale')
            # Recomputed from the rows rather than trusted: `noArtifactsEverywhere` is re-derived from
            # the artifact lists themselves, not from the flag the recorder wrote beside them.
            self.assertEqual(all(op['inputUnchanged'] for r in report['observations'] for op in r['operations']),
                             report['inputUnchangedEverywhere'], name)
            self.assertEqual(all(not op['artifacts'] for r in report['observations'] for op in r['operations']),
                             report['noArtifactsEverywhere'], name)
            self.assertTrue(all(op['inputUnchanged'] for r in report['observations'] for op in r['operations']),
                            f'{name}: a read-only probe must report every input unchanged')
            self.assertTrue(all(not op['artifacts'] for r in report['observations'] for op in r['operations']),
                            f'{name}: a read-only probe must produce no artifact at any rung')
            self.assertEqual('not-assessed', report['verification'], name)
            self.assertFalse(report['denominatorFrozen'], name)

    def test_the_boundary_and_the_short_band_are_what_the_readme_says(self):
        report = json.loads((self.EVIDENCE / 'observations.json').read_text(encoding='utf-8'))
        summary, rows = report['summary'], report['observations']
        self.assertEqual(80, summary['bmp']['flipSize'])
        self.assertEqual(80, summary['gif']['flipSize'])
        self.assertEqual(79, summary['bmp']['lastRefusal'])
        self.assertEqual(79, summary['gif']['lastRefusal'])
        self.assertTrue(summary['bmp']['monotonic'] and summary['gif']['monotonic'])
        self.assertIsNone(summary['targa']['flipSize'])
        self.assertEqual(0, summary['bmp-xmltail']['rowsDifferingFromPrediction'],
                         'the content control must track its plain twin')
        by = {(r['kind'], r['size']): r for r in rows}
        for size in ladder.SIZES:
            families = {tuple(op['family'] for op in by[(k, size)]['operations']) for k in ('bmp', 'gif', 'targa')}
            if size < ladder.XMP_WINDOW:
                self.assertEqual(1, len(families), f'size {size}: the three ladders parted ways')
        for size in (8, 12, 16, 20):
            kinds = {by[(k, size)]['operations'][0]['family'] for k in ('bmp', 'gif', 'targa')}
            self.assertEqual(1, len(kinds), f'size {size} must be format-independent')
        self.assertEqual('corrupted-metadata', by[('bmp', 12)]['operations'][0]['family'])
        self.assertFalse(any(op['family'] == 'reader-reached' for r in rows if r['kind'] == 'targa'
                             for op in r['operations']), 'the .targa control must not have fired')

    def test_readme_numbers_are_derived_not_typed(self):
        """Every count the README quotes has to be recomputable from the payload, in both directions."""
        report = json.loads((self.EVIDENCE / 'observations.json').read_text(encoding='utf-8'))
        readme = (self.EVIDENCE / 'README.md').read_text(encoding='utf-8')
        rows = report['observations']
        sizes = {r['size'] for r in rows}
        needles = [str(len(rows)), str(report['recordCount']), str(len(sizes)),
                   f"`flipSize` {report['summary']['bmp']['flipSize']}",
                   f"`lastRefusal` {report['summary']['bmp']['lastRefusal']}",
                   f"**zero**", str(report['summary']['bmp-xmltail']['rowsDifferingFromPrediction'])]
        for needle in needles:
            self.assertIn(needle, readme, f"the README must carry the derived value {needle!r}")
        # And the claims must not be stale in the other direction: no number in the prose may name a
        # boundary the payload does not contain.
        import re as _re
        for claimed in {int(m) for m in _re.findall(r'(?:flip|flips) (?:at|between .* and )?(\d+) bytes', readme)}:
            self.assertIn(claimed, sizes | {report['summary'][k]['flipSize'] for k in report['summary']},
                          f'{claimed}: a boundary quoted but not measured')

    def test_two_runs_two_heads_and_one_set_of_observations(self):
        """The reproducibility claim B11 earned, applied here: a second payload from a different head.

        Both reports must agree byte-for-byte on everything the measurement is made of
        (`observations`, `summary`, `fixtureManifest`, `prediction`, `environment`) and be allowed to
        disagree only on the build identity and the commit that triggered the run. That is the
        distinction that makes a ladder credible: the *same* pinned source, a *different*
        compilation of it, and one answer.
        """
        samples = self.samples()
        self.assertEqual(['observations.json', 'prior-run-observations.json'], samples,
                         'B11L evidence is a pair: the run the review authorised, and a second run from a new head')
        docs = [json.loads((self.EVIDENCE / name).read_text(encoding='utf-8')) for name in samples]
        canon = lambda doc: json.dumps(doc, sort_keys=True, separators=(',', ':')).encode()
        first, second = docs
        for section in ('observations', 'summary', 'fixtureManifest', 'prediction', 'environment',
                        'recordCount', 'familyCounts', 'inputUnchangedEverywhere', 'noArtifactsEverywhere'):
            self.assertEqual(canon(first[section]), canon(second[section]),
                             f'{section}: the two runs parted ways, so the boundary is not reproducible')
        differing = {key for key in set(first) | set(second) if canon(first.get(key)) != canon(second.get(key))}
        self.assertEqual({'baseline', 'sourceCommit'}, differing)
        moved = {key for key in set(first['baseline']) | set(second['baseline'])
                 if canon(first['baseline'].get(key)) != canon(second['baseline'].get(key))}
        self.assertEqual({'executableSha256', 'verboseVersion'}, moved,
                         'a rebuild may move the binary and its version banner; the CMake policy must not move')
        self.assertEqual(first['baseline']['cmakeCacheSha256'], second['baseline']['cmakeCacheSha256'])
        self.assertEqual(first['baseline']['effectiveCmake'], second['baseline']['effectiveCmake'])
        for doc in docs:
            self.assertRegex(doc['sourceCommit'], '^[0-9a-f]{40}$')
        self.assertNotEqual(first['sourceCommit'], second['sourceCommit'],
                            'the pair is only evidence if the two runs were not the same commit')
        # Ancestry is asserted when this checkout can resolve both heads, and skipped when not:
        # a shallow clone must not turn a real claim into a false failure, and the assertion must
        # not be relaxed into "if it is convenient".
        import subprocess
        resolved = {sha: subprocess.run(['git', '-C', str(ROOT), 'cat-file', '-e', f'{sha}^{{commit}}'],
                                         capture_output=True).returncode == 0
                    for sha in (first['sourceCommit'], second['sourceCommit'])}
        if all(resolved.values()):
            self.assertEqual(0, subprocess.run(['git', '-C', str(ROOT), 'merge-base', '--is-ancestor',
                                                first['sourceCommit'], second['sourceCommit']],
                                               capture_output=True).returncode,
                             'the second run must be taken from a descendant head, not an unrelated tree')

    def test_readme_records_the_pair_it_claims(self):
        readme = (self.EVIDENCE / 'README.md').read_text(encoding='utf-8')
        docs = [json.loads((self.EVIDENCE / name).read_text(encoding='utf-8')) for name in self.samples()]
        differing = sorted({key for key in set(docs[0]) | set(docs[1])
                            if json.dumps(docs[0].get(key), sort_keys=True)
                            != json.dumps(docs[1].get(key), sort_keys=True)})
        self.assertTrue(docs, 'no samples to describe')
        for needle in (str(len(docs)),) + tuple(f"`{key}`" for key in differing):
            self.assertIn(needle, readme, f'the README must state the derived pair fact {needle!r}')

    def test_evidence_directory_holds_only_the_recovered_payloads(self):
        present = {path.name for path in self.EVIDENCE.iterdir()}
        self.assertTrue({'README.md', 'observations.json'} <= present, present)
        self.assertEqual({'README.md', 'observations.json', 'prior-run-observations.json'}, present,
                         'the B11L evidence directory is exactly the pair plus its README')


if __name__ == '__main__':
    unittest.main()
