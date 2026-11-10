"""Offline tests for the B11L recognition-ladder fixture catalog.

The ladder is the only place in this repository where a *byte length* is the independent variable, so these
tests are mostly about what would silently destroy that: a rung whose header drifted, a rung that gained a
second difference besides length, a fixture that grew a payload, a prediction typed by hand instead of
derived, or a file in the directory that no rung claims. None of them asks what the pinned build did — that
question is answered only by `probe_b11l_ladder.py` in CI, and nothing here may pretend otherwise.
"""
from pathlib import Path
import json
import struct
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/reference'))
import b11l_recognition_ladder as ladder  # noqa: E402


class B11LLadderTests(unittest.TestCase):
    def setUp(self):
        self.entries = ladder.entries()

    def entry(self, layout):
        matches = [e for e in self.entries if e['layout'] == layout]
        self.assertEqual(1, len(matches), layout)
        return matches[0]

    def test_catalog_verifies_and_covers_the_whole_ladder(self):
        recorded = ladder.verify()
        self.assertEqual(self.entries, recorded, 'the committed manifest must be the derived one')
        self.assertEqual(47, len(recorded))
        for kind in ('bmp', 'gif', 'targa'):
            sizes = sorted(e['size'] for e in recorded if e['kind'] == kind)
            self.assertEqual(list(ladder.SIZES), sizes, kind)
        # Only the two content-control rungs are outside the three ladders.
        self.assertEqual({'bmp-xmltail'}, {e['kind'] for e in recorded} - {'bmp', 'gif', 'targa'})
        self.assertEqual([40, 96], sorted(e['size'] for e in recorded if e['kind'] == 'bmp-xmltail'))

    def test_rungs_are_bytes_only_and_bounded(self):
        for entry in self.entries:
            data = (ladder.DIRECTORY / entry['file']).read_bytes()
            self.assertEqual(entry['size'], len(data))
            self.assertGreaterEqual(len(data), 8, entry['file'])
            self.assertLessEqual(len(data), 128, f"{entry['file']}: a ladder rung that grows stops being a length test")
            self.assertEqual(ladder.sha256(data), entry['sha256'])

    def test_each_directory_holds_only_claimed_rungs(self):
        claimed = {e['file'] for e in self.entries} | {ladder.MANIFEST.name}
        present = {p.name for p in ladder.DIRECTORY.iterdir()}
        self.assertEqual(set(), present - claimed, 'an unclaimed sample in the fixture directory is a rights problem')
        self.assertTrue(all(name.startswith('owned-') for name in present - {ladder.MANIFEST.name}),
                        'every B11L fixture is authored here and must be named that way')

    def test_bmp_and_gif_ladders_are_prefix_chains(self):
        """A flip between adjacent rungs must be attributable to length alone, so the prefix must not drift."""
        for kind in ('bmp', 'gif'):
            rungs = sorted((e['size'], ladder.BUILDERS[kind](e['size'])) for e in self.entries if e['kind'] == kind)
            for (small, a), (big, b) in zip(rungs, rungs[1:]):
                self.assertEqual(big - small, len(b) - len(a))
                self.assertEqual(a, b[:len(a)], f'{kind}: {big}-byte rung does not extend the {small}-byte one')

    def test_targa_rungs_move_only_the_middle_padding(self):
        """The footer has to stay at the end and the head at the start, so growth happens between them."""
        for entry in self.entries:
            if entry['kind'] != 'targa':
                continue
            data = ladder.BUILDERS['targa'](entry['size'])
            if entry['size'] < ladder.TGA_MINIMUM:
                self.assertFalse(entry['prediction']['attributable'], entry['file'])
                continue
            self.assertEqual(ladder.TGA_HEAD, data[:len(ladder.TGA_HEAD)], entry['file'])
            self.assertEqual(ladder.TGA_FOOTER, data[-len(ladder.TGA_FOOTER):], entry['file'])
            self.assertEqual(b'\x00' * (entry['size'] - ladder.TGA_MINIMUM),
                             data[len(ladder.TGA_HEAD):-len(ladder.TGA_FOOTER)], entry['file'])
            self.assertTrue(entry['structure']['bothRegionsCanonical'], entry['file'])

    def test_content_control_differs_from_its_twin_at_exactly_the_planted_window(self):
        for size in (40, 96):
            control = ladder.BUILDERS['bmp-xmltail'](size)
            plain = ladder.BUILDERS['bmp'](size)
            self.assertEqual(size, len(control))
            self.assertEqual(plain[:ladder.XML_PLANT_OFFSET], control[:ladder.XML_PLANT_OFFSET],
                             f'len{size}: the bytes before the plant must be identical')
            tail = ladder.XML_PLANT_OFFSET + len(ladder.XML_PLANT)
            self.assertEqual(ladder.XML_PLANT, control[ladder.XML_PLANT_OFFSET:tail],
                             'the plant must sit inside the window xmp reads before it decides')
            self.assertEqual(plain[tail:], control[tail:], f'len{size}: only the planted window may differ')
            self.assertEqual(plain[:26], control[:26], 'the 26 bytes the BMP reader reads must survive the plant')
            self.assertEqual(b'BM', control[:2], 'both rungs must still start with BM')

    def test_prediction_is_derived_from_the_ladder_and_not_typed(self):
        """Every rung's prediction must follow from the model functions, at the rung's own size."""
        for entry in self.entries:
            expected = ladder.predicted_outcome(entry['kind'], entry['size'])
            self.assertEqual(expected, entry['prediction'], entry['file'])
        # The three regimes the review names, checked as boundaries rather than as a table of 47 rows.
        self.assertEqual('throws-during-scan', ladder.predicted_outcome('bmp', ladder.EPS_READ_BYTES - 1)['predictedFamily'])
        for size in (ladder.EPS_READ_BYTES, ladder.XMP_WINDOW - 1):
            self.assertEqual('unknown-image-type', ladder.predicted_outcome('bmp', size)['predictedFamily'], size)
            self.assertEqual('unknown-image-type', ladder.predicted_outcome('gif', size)['predictedFamily'], size)
        for size in (ladder.XMP_WINDOW, 128):
            self.assertEqual('reader-reached', ladder.predicted_outcome('bmp', size)['predictedFamily'], size)
            self.assertEqual('reader-reached', ladder.predicted_outcome('gif', size)['predictedFamily'], size)
        # The control is flat by construction: that is why it is in the batch.
        self.assertEqual({'unknown-image-type'}, {e['prediction']['predictedFamily'] for e in self.entries
                                                  if e['kind'] == 'targa' and e['size'] >= ladder.EPS_READ_BYTES})

    def test_the_strand_model_names_the_entry_that_owns_each_rung(self):
        """Which registry entry strands the cursor is itself a prediction the build can refute."""
        self.assertEqual(('xmp', 19), (ladder.strand_prediction('bmp', 60)['strandedBy'],
                                       ladder.strand_prediction('bmp', 60)['strandedIndex']))
        self.assertEqual(('cr2', 2), (ladder.strand_prediction('gif', 12)['strandedBy'],
                                      ladder.strand_prediction('gif', 12)['strandedIndex']))
        self.assertEqual((None, None), (ladder.strand_prediction('gif', 128)['strandedBy'],
                                        ladder.strand_prediction('gif', 128)['strandedIndex']))
        # eps is clean (it restores), so a file that only eps would trip on is not a strand case.
        self.assertNotEqual('eps', ladder.strand_prediction('bmp', 30)['strandedBy'])

    def test_registry_positions_and_windows_are_the_pinned_ones(self):
        self.assertEqual(30, len(ladder.REGISTRY))
        self.assertEqual(0, ladder.INDEX['jpeg'])
        self.assertEqual(18, ladder.INDEX['eps'])
        self.assertEqual(19, ladder.INDEX['xmp'])
        self.assertEqual(20, ladder.INDEX['gif'])
        self.assertEqual(22, ladder.INDEX['tga'])
        self.assertEqual(23, ladder.INDEX['bmp'])
        self.assertEqual(29, ladder.INDEX['bmff'])
        self.assertEqual(('gif', 'psd', 'tga', 'bmp', 'jp2', 'qtime', 'asf', 'riff', 'mkv', 'bmff'),
                         ladder.STRANDED_FORMATS)
        self.assertEqual(80, ladder.XMP_WINDOW)
        self.assertEqual(26, ladder.EPS_READ_BYTES)
        self.assertEqual(44, ladder.TGA_MINIMUM)

    def test_magic_model_agrees_with_the_bytes_for_every_position_independent_rung(self):
        for entry in self.entries:
            data = ladder.BUILDERS[entry['kind']](entry['size'])
            self.assertEqual(entry['recognizers'], ladder.model_recognizers(data, entry['format']), entry['file'])
            if entry['kind'] in ('bmp', 'bmp-xmltail'):
                self.assertEqual(['bmp'], entry['recognizers']['model'], entry['file'])
                self.assertEqual(b'BM', data[:2])
            elif entry['kind'] == 'gif':
                self.assertEqual(['gif'], entry['recognizers']['model'], entry['file'])
                self.assertIn(data[:6], (b'GIF87a', b'GIF89a'))

    def test_fixtures_carry_no_metadata_carrier_of_any_kind(self):
        """B11L authors no carriers, so no row can be read as an extraction result -- or as a rights question."""
        needles = (b'Exif', b'http://ns.adobe.com', b'Photoshop 3.0', b'8BIM', b'<x:xmpmeta', b'RIFF', b'JFIF')
        for entry in self.entries:
            data = ladder.BUILDERS[entry['kind']](entry['size'])
            for needle in needles:
                self.assertNotIn(needle, data, f"{entry['file']} grew a {needle!r} carrier")
        # The one XML-ish text in the batch is the planted control, and it is inside a BMP body.
        self.assertIn(b'<?xml ', ladder.BUILDERS['bmp-xmltail'](96))
        self.assertNotIn(b'<?xml', ladder.BUILDERS['bmp'](96))

    def test_bmp_rungs_declare_a_file_size_no_rung_has(self):
        """The declared size is 54 + pixel bytes for every rung, which only the largest rung can match."""
        declared = struct.unpack('<I', ladder.bmp_header()[2:6])[0]
        self.assertEqual(78, declared, '4x2 24bpp pixel bytes on top of a 54-byte header')
        consistent = {e['size'] for e in self.entries if e['kind'] == 'bmp' and e['structure'].get('consistentLength')}
        self.assertEqual(set(), consistent, 'a consistent length would make the size axis a validity test too')

    def test_structure_fields_describe_the_bytes_and_not_a_model(self):
        for entry in self.entries:
            data = ladder.BUILDERS[entry['kind']](entry['size'])
            self.assertEqual(entry['structure'], ladder.structure(entry['kind'], entry['file'], data), entry['file'])
            self.assertEqual(len(data), entry['structure']['length'])
            self.assertEqual(ladder.sha256(data), entry['structure']['sha256'])
            if entry['kind'] == 'bmp' and entry['size'] >= 30:
                self.assertEqual((4, 2, 1, 24), (entry['structure']['pixelWidth'], entry['structure']['pixelHeight'],
                                                 entry['structure']['planes'], entry['structure']['depth']))

    def test_the_gate_document_exists_and_agrees_with_the_code_it_gates(self):
        """A fixture catalog and a recorder that cite a review must keep agreeing with it, in both directions.

        This is the cheap half of the guarantee: it checks that the sentences the batch depends on are
        present and that the numbers in them are the numbers in the code. It cannot check that the
        review's reading of the pinned source is right -- that is what the ladder run is for.
        """
        doc = ROOT / 'docs/research/E01_B11L_RIGHTS_AND_ORACLE_REVIEW.md'
        self.assertTrue(doc.is_file(), 'B11L is gated on its rights and oracle review')
        text = ' '.join(doc.read_text(encoding='utf-8').split())   # wrapping is a typesetting choice
        for citation in (ladder.__doc__ or '', __import__('probe_b11l_ladder').__doc__ or ''):
            self.assertIn('E01_B11L_RIGHTS_AND_ORACLE_REVIEW.md', citation)
        for heading in ('## Rights', '## Oracle review', '## Method and evidence contract',
                        'One B11 row the model does not explain'):
            self.assertIn(heading, doc.read_text(encoding='utf-8'), heading)
        # The constants the prose quotes, quoted from the code so neither side can drift alone.
        self.assertIn(f'{ladder.XMP_WINDOW}-byte window', text)
        self.assertIn('at offset 30', text)
        self.assertIn('8, 12, 16, 20, 24, 40, 54, 55, 56, 64, 79, 80, 81, 96 and 128', text)
        self.assertEqual(', '.join(str(s) for s in ladder.SIZES[:-1]) + f' and {ladder.SIZES[-1]}',
                         '8, 12, 16, 20, 24, 40, 54, 55, 56, 64, 79, 80, 81, 96 and 128')
        for name in ('cr2', 'jp2', 'jpg', 'orf', 'png', 'raf', 'rw2', 'tiff', 'webp'):
            self.assertIn(name, text, f'{name}: the nine printStructure overriders are the -pS binary')
        for cite in ('src/image.cpp:75-116', 'src/xmpsidecar.cpp:156-198', 'src/basicio.cpp:530-540',
                     'src/image.cpp:136-138', 'src/image.cpp:825-830', 'src/tgaimage.cpp:99-122',
                     'app/actions.cpp:1983-1991'):
            self.assertIn(cite, text, cite)
        # The two admissions that make this a test rather than a confirmation exercise.
        self.assertIn('owned-gif-lsd-v1.gif', text)
        self.assertIn('the Sony maker-note tables batch B12 owns', text)
        self.assertIn('not-establishable-by-this-method', text)

    def test_census_is_untouched_by_this_batch(self):
        """B11L is diagnostic: the published payload must not have gained a row, a batch or a claim."""
        payload = json.loads((ROOT / 'docs/research/EXIV2_CENSUS.json').read_text(encoding='utf-8'))
        self.assertEqual(823, len(payload['rows']), 'B11L must not add census rows')
        self.assertNotIn('B11L', payload['scope'], 'the scope sentence is written by build_census, not here')
        self.assertNotIn('ladder', payload['scope'], 'no ladder vocabulary may enter the published scope')
        self.assertEqual(set(), {row.get('batch') for row in payload['rows']} & {'B11L', 'B12'},
                         'a ladder row would turn a diagnostic side-batch into a coverage claim')
        layouts = {layout for kinds in payload['enumerations']['layout'].values() for layout in kinds}
        self.assertEqual(set(), layouts & set(ladder.FIXTURES), 'ladder rungs are not census layouts')
        # The census still says "the Sony maker-note tables batch B12 owns" in B10's blocked-rights
        # notes. That sentence is *correct*: B12 is the MakerNotes applicability overlay in the master
        # plan and it has not run -- the ladder was moved out of the number rather than the plan being
        # edited, because those notes are bytes inside a payload whose cross-run identity is asserted
        # elsewhere. Pinned here so neither half can drift: the string must stay in the census, it must
        # appear only on SR2 rows, and no B11L batch may ever appear in the census at all.
        mentions = [row['rowId'] for row in payload['rows'] if 'batch B12 owns' in row.get('notes', '')]
        self.assertTrue(mentions, 'the plan claim in B10 notes must stay as recorded')
        self.assertTrue(all(row.startswith('EX0289-SR2-') for row in mentions),
                        'the B12 mention is the MakerNotes overlay, not this batch')


if __name__ == '__main__':
    unittest.main()
