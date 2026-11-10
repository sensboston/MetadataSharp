"""Offline tests for the B11 census fixture catalog and its container-framing oracle.

No external tool is executed here: the catalog must verify with the standard library alone, so
CI and a developer laptop reach the same conclusion about the fixtures. These tests check our
own authored framing -- which reader the pinned registry would reach and which CIFF components
its mapping table would take -- and say nothing about vendor CRW semantics, about whether any
format is "supported", or about MetadataSharp.
"""
import json
import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/reference'))
import b11_container_fixtures as catalog  # noqa: E402
import jpeg_owned_fixtures as owned  # noqa: E402

SIX = ('EXIF', 'IPTC', 'XMP', 'COMMENTS', 'ICC', 'THUMBNAIL')


def data_for(layout):
    return catalog.BUILDERS[layout]()


def facts(layout, data=None, name=None):
    name = name or catalog.FIXTURES[layout][1]
    return catalog.structure(layout, name, data if data is not None else data_for(layout))


class CatalogTests(unittest.TestCase):
    def test_manifest_matches_bytes_and_records_the_authored_contract(self):
        manifest = catalog.verify()
        self.assertEqual(18, len(manifest))
        self.assertEqual(sorted(set(catalog.FIXTURES) | set(catalog.BLOCKED_LAYOUTS)),
                         sorted(entry['layout'] for entry in manifest))
        for entry in manifest:
            self.assertEqual('owned-synthetic' if entry['file'] else 'not-admitted', entry['rights'])
            self.assertFalse(entry['decodable'])
            if not entry['file']:
                continue
            self.assertEqual(catalog.FIXTURES[entry['layout']][0], entry['format'])
            self.assertEqual(catalog.FORMATS[entry['format']]['carriers'], entry['carriers'])
            self.assertTrue(entry['vendorIdentity'])
            raw = catalog.DIRECTORY / entry['file']
            self.assertEqual(raw.read_bytes(), data_for(entry['layout']))
            self.assertEqual(len(raw.read_bytes()), entry['length'])
            self.assertEqual(catalog.digest(raw.read_bytes()), entry['sha256'])
            self.assertIn('tools/reference/b11_container_fixtures.py', entry['recipe'])
            self.assertEqual(catalog.expected_dispatch(entry['layout'], entry['file']),
                             entry['dispatch'])
            self.assertEqual('ciff-components' if entry['format'] == 'CRW' else 'none',
                             entry['metadata'])

    def test_blocked_layout_is_absent_from_the_fixture_table_and_names_its_review(self):
        for layout in catalog.BLOCKED_LAYOUTS:
            self.assertNotIn(layout, catalog.FIXTURES)
        self.assertIn('crwMapping_', catalog.BLOCKED_REASON)
        self.assertIn('E01_B11_RIGHTS_AND_ORACLE_REVIEW.md', catalog.BLOCKED_REASON)
        blocked = next(entry for entry in catalog.entries() if entry.get('blocked'))
        self.assertIsNone(blocked['file'])
        self.assertEqual('CRW', blocked['format'])
        self.assertIn('blocked', blocked['recipe'] + catalog.BLOCKED_REASON)
        # No other format in this batch is allowed a blocked cell: BMP, GIF and TGA have no
        # vendor-private surface, and the container stubs have no family cells to block.
        self.assertEqual({'CRW'}, {entry['format'] for entry in catalog.entries()
                                   if entry.get('blocked')})

    def test_ciff_type_and_location_switches_are_the_pinned_ones(self):
        # typeId is the (tag & 0x3800) switch and dataLocation the (tag & 0xc000) switch:
        # both are transcribed here rather than assumed, because the whole directory-versus-
        # entry decision hinges on the first and the overlap rule on the second.
        self.assertEqual('asciiString', catalog.ciff_type_id(0x0805))
        self.assertEqual('unsignedLong', catalog.ciff_type_id(0x1810))
        self.assertEqual('unsignedShort', catalog.ciff_type_id(0x10b4))
        self.assertEqual('asciiString', catalog.ciff_type_id(0x08ff))
        self.assertEqual('unsignedByte', catalog.ciff_type_id(0x0005))
        self.assertEqual('undefined', catalog.ciff_type_id(0x2005))
        self.assertEqual('undefined', catalog.ciff_type_id(0x2007))
        self.assertEqual('directory', catalog.ciff_type_id(0x300a))
        self.assertEqual('directory', catalog.ciff_type_id(0x2807))
        self.assertEqual('valueData', catalog.ciff_data_location(0x300a))
        self.assertEqual('directoryData', catalog.ciff_data_location(0x4000 | 0x300a))
        with self.assertRaises(ValueError):
            catalog.ciff_data_location(0x8000 | 0x300a)

    def test_ciff_directories_cannot_be_authored_as_values_or_the_other_way_round(self):
        with self.assertRaises(ValueError):
            catalog.ciff_value(0x300a, b'x')
        with self.assertRaises(ValueError):
            catalog.ciff_directory(0x0805, [])
        with self.assertRaises(ValueError):
            catalog.ciff_directory(0x30ff, [])   # not a directory in crwSubDir_

    def test_ciff_regions_are_payload_count_entries_and_a_trailing_self_reference(self):
        parsed = catalog.parse_ciff(data_for('ciff-mapped-v1'))
        self.assertEqual('littleEndian', parsed['byteOrder'])
        self.assertEqual(26, parsed['rootOffset'])
        self.assertEqual(12, parsed['padding'])
        # The root region runs to the end of the file, so its length is file minus the offset.
        self.assertEqual(len(data_for('ciff-mapped-v1')) - 26, parsed['regionLength'])
        root = parsed['tree']
        self.assertEqual(0x0000, root['tag'])
        self.assertEqual([0x300a], [entry['tag'] for entry in root['entries']])
        scene = root['entries'][0]
        self.assertEqual('directory', scene['type'])
        self.assertEqual('valueData', scene['location'])
        self.assertEqual(sorted([0x0805, 0x1810, 0x180e, 0x2807, 0x3002, 0x300b, 0x1817]),
                         sorted(entry['tag'] for entry in scene['children']['entries']))
        # A sub-directory is addressed from the parent's payload pool, so it is the child's
        # own last four bytes that point at its count, not the file's.
        owner = next(entry for entry in scene['children']['entries'] if entry['tag'] == 0x2807)
        self.assertEqual(sorted([0x080a, 0x0810, 0x3004]),
                         sorted(entry['tag'] for entry in owner['children']['entries']))

    def test_mapped_components_are_found_by_directory_and_tag_together(self):
        keys = {layout: facts(layout)['mappedKeys'] for layout in sorted(catalog.FIXTURES)
                if layout.startswith('ciff')}
        self.assertEqual(['colorspace', 'comment', 'datetime', 'dimensions', 'distance',
                          'file-number', 'firmware', 'make-model', 'owner-name'],
                         keys['ciff-mapped-v1'])
        self.assertEqual(['thumbnail'], keys['ciff-thumb-only-v1'])
        self.assertEqual(['preview-offset', 'thumbnail'], keys['ciff-preview-plus-thumb-v1'])
        self.assertEqual([], keys['ciff-unmapped-only-v1'])
        # The keys the review says each row produces, recorded so the fixture and the census
        # cannot drift apart in vocabulary.
        self.assertEqual('Exif.Photo.DateTimeOriginal', catalog.CIFF_KEYS['datetime'])
        self.assertEqual('Exif.Photo.SubjectDistance', catalog.CIFF_KEYS['distance'])
        self.assertEqual('Exif.Canon.OwnerName', catalog.CIFF_KEYS['owner-name'])
        self.assertIn('no row for (0x0000, 0x2007)', catalog.CIFF_KEYS['preview-offset'])

    def test_a_mapped_tag_in_the_wrong_directory_is_dropped_not_rejected(self):
        # The unmapped fixture files 0x1810 (mapped in 0x300a) under 0x2807 and 0x080a
        # (mapped in 0x2807) under 0x300b. The lookup is (dir, tag), so the file parses and
        # the components are simply not decoded -- the exact behaviour the review recorded.
        parsed = catalog.parse_ciff(data_for('ciff-unmapped-only-v1'))
        tags = {entry['tag'] for entry in catalog.walk_ciff(parsed['tree'])}
        self.assertEqual({0x300a, 0x08ff, 0x2807, 0x1810, 0x300b, 0x080a, 0x2005}, tags)
        self.assertEqual([], catalog.mapped_components(parsed))

    def test_ciff_payload_overlap_is_rejected_as_the_reference_rejects_it(self):
        data = bytearray(data_for('ciff-thumb-only-v1'))
        root = catalog.parse_ciff(bytes(data))
        entry = root['tree']['entries'][0]
        # Re-point the thumbnail payload so it starts four bytes inside its own 10-byte
        # entry: the reference calls that kerOffsetOutOfRange, and so does this.
        struct.pack_into('<I', data, entry['entryAt'] + 6, entry['offset'] + 4)
        with self.assertRaises(ValueError) as caught:
            catalog.parse_ciff(bytes(data))
        self.assertIn('overlaps its entry', str(caught.exception))

    def test_ciff_header_rules_are_enforced(self):
        # Three checks the reference makes before it reads a single component: the II/MM
        # mark, the 14 <= offset <= size window, and the HEAPCCDR signature.
        bad_signature = bytearray(data_for('ciff-mapped-v1'))
        bad_signature[6:14] = b'NOTCIFFF'
        with self.assertRaises(ValueError) as caught:
            catalog.parse_ciff(bytes(bad_signature))
        self.assertIn('HEAPCCDR', str(caught.exception))
        low_offset = bytearray(data_for('ciff-mapped-v1'))
        struct.pack_into('<I', low_offset, 2, 8)          # below the 14-byte minimum
        with self.assertRaises(ValueError) as caught:
            catalog.parse_ciff(bytes(low_offset))
        self.assertIn('root offset', str(caught.exception))
        with self.assertRaises(ValueError):
            catalog.parse_ciff(b'II' + bytes(6))          # shorter than the header
        with self.assertRaises(ValueError):
            catalog.parse_ciff(b'MM' + data_for('ciff-mapped-v1')[2:])   # mark disagrees

    def test_thumbnail_payload_is_the_owned_jpeg_and_the_preview_shares_it(self):
        thumb = facts('ciff-thumb-only-v1')
        self.assertEqual(len(owned.thumb_jpeg_bytes()), thumb['thumbnailLength'])
        self.assertEqual(catalog.digest(owned.thumb_jpeg_bytes()), thumb['thumbnailSha256'])
        pair = facts('ciff-preview-plus-thumb-v1')
        self.assertEqual(pair['thumbnailLength'], pair['previewLength'])
        self.assertEqual(thumb['thumbnailSha256'], pair['thumbnailSha256'])

    def test_bmp_header_variants_are_read_the_way_the_reader_reads_them(self):
        dib, os2 = facts('bmp-dib-v3')['bmp'], facts('bmp-os2-coreheader-v1')['bmp']
        self.assertEqual((40, 64, 48, 24), (dib['headerSize'], dib['width'], dib['height'],
                                            dib['bitsPerPixel']))
        self.assertEqual((12, 48, 32, 24), (os2['headerSize'], os2['width'], os2['height'],
                                             os2['bitsPerPixel']))
        # A short read is not a zero-size read: the reader leaves the dimensions alone, so a
        # fixture under 26 bytes would prove nothing and must be refused here instead.
        with self.assertRaises(ValueError) as caught:
            catalog.structure('bmp-dib-v3', 'owned-bmp-dib.bmp', data_for('bmp-dib-v3')[:25])
        self.assertIn('read short', str(caught.exception))

    def test_gif_logical_screen_descriptor_is_optional_exactly_like_the_reader(self):
        self.assertEqual((64, 48), (facts('gif-lsd-v1')['gif']['width'],
                                    facts('gif-lsd-v1')['gif']['height']))
        self.assertEqual(13, facts('gif-lsd-v1')['fileLength'])
        truncated = facts('gif-truncated-v1')['gif']
        self.assertEqual(0, truncated['lsdBytes'])
        self.assertIsNone(truncated['width'])
        self.assertIsNone(truncated['height'])
        with self.assertRaises(ValueError):
            catalog.structure('gif-truncated-v1', 'owned-gif-no-lsd.gif', b'GIF87a' + bytes(4))

    def test_tga_recognition_is_the_file_name_and_the_footer_route_is_unreachable(self):
        full = facts('tga-full-v1')['tga']
        self.assertTrue(full['footerSigned'])
        self.assertTrue(full['recognisedByName'])
        self.assertFalse(full['footerRouteReachableByFactory'])
        self.assertEqual((80, 60), (full['width'], full['height']))
        name_only = facts('tga-name-only-v1')['tga']
        self.assertFalse(name_only['footerSigned'])
        self.assertEqual(list(catalog.tga_name_only_dimensions()),
                         [name_only['width'], name_only['height']])
        # The same bytes under a .targa name reach no reader at all, which is the whole point
        # of shipping the twin: it separates "this build recognised a TGA" from "this file is
        # a TGA".
        twin = facts('tga-signature-wrong-name-v1')
        self.assertEqual('none', twin['dispatch'])
        self.assertEqual(data_for('tga-full-v1'), data_for('tga-signature-wrong-name-v1'))
        self.assertEqual([], twin['recognizers']['matched'])

    def test_registry_order_is_first_match_and_the_gif_before_tga_case_is_recorded(self):
        # A .tga-named file whose bytes are a GIF header is dispatched to gif, because the
        # registry lists gif (image.cpp:101) before tga (:103). The earlier draft of this
        # batch claimed the opposite; the transcription is what corrected it.
        name = 'owned-tga-full.tga'
        rec = catalog.recognizers(b'GIF89a' + bytes(7), name)
        self.assertEqual('gif', rec['dispatch'])
        self.assertIn('tga', rec['matched'])
        self.assertEqual({'gif', 'tga'}, set(rec['matched']))

    def test_quicktime_brand_vocabulary_is_transcribed_not_sampled(self):
        self.assertEqual(86, len(catalog.QTIME_BRANDS))
        self.assertEqual(86, len(set(catalog.QTIME_BRANDS)))
        self.assertIn('qt  ', catalog.QTIME_BRANDS)
        self.assertIn('isom', catalog.QTIME_BRANDS)
        self.assertNotIn(catalog.UNKNOWN_BRAND, catalog.QTIME_BRANDS)
        known = facts('qtime-known-brand-v1')['mov']
        unknown = facts('qtime-unknown-brand-v1')['mov']
        self.assertTrue(known['brandKnownToQuickTime'])
        self.assertFalse(unknown['brandKnownToQuickTime'])
        # Both files satisfy isBmffType; only the brand decides which of the two readers the
        # registry reaches first. Recording the fall-through as *also accepted* rather than as
        # "fell through" is what keeps that distinction honest.
        self.assertTrue(known['bmffAlsoAccepts'])
        self.assertTrue(unknown['bmffAlsoAccepts'])
        self.assertEqual('qtime', facts('qtime-known-brand-v1')['dispatch'])
        self.assertEqual('bmff', facts('qtime-unknown-brand-v1')['dispatch'])
        # The pair differs in exactly one field: same atom, same length, different brand.
        known, unknown = data_for('qtime-known-brand-v1'), data_for('qtime-unknown-brand-v1')
        self.assertEqual(len(known), len(unknown))
        self.assertEqual(known[:8], unknown[:8])
        self.assertEqual(known[12:], unknown[12:])
        self.assertNotEqual(known[8:12], unknown[8:12])
        with self.assertRaises(ValueError):
            catalog.encode_mov('toolongbrand')

    def test_container_stubs_are_minimal_and_recognised_by_their_own_test(self):
        self.assertEqual({'magic', 'headerId'}, set(facts('mkv-ebml-header-v1')['matroska']))
        self.assertEqual('1a45dfa3', facts('mkv-ebml-header-v1')['matroska']['magic'])
        self.assertEqual('RIFF', data_for('riff-avi-avih-v1')[:4].decode('latin-1'))
        self.assertEqual('AVI ', facts('riff-avi-avih-v1')['riff']['fourcc'])
        self.assertEqual('WAVE', facts('riff-wav-fmt-v1')['riff']['fourcc'])
        # isRiffType compares only 'RIFF', so a WAV stub must be recognised as RIFF and
        # dispatched to the same reader an AVI is: that is the fact, not a naming accident.
        self.assertEqual('riff', facts('riff-wav-fmt-v1')['dispatch'])
        self.assertEqual('riff', facts('riff-avi-avih-v1')['dispatch'])
        asf = facts('asf-header-guid-v1')['asf']
        self.assertEqual(catalog.ASF_HEADER_GUID.hex(), asf['headerGuid'])
        self.assertEqual(100, asf['objectSize'])
        self.assertEqual(0, asf['subobjects'])
        self.assertEqual(100, len(data_for('asf-header-guid-v1')))
        # The ASF GUID is not RIFF: it must not be recognised by the RIFF test.
        self.assertNotIn('riff', facts('asf-header-guid-v1')['recognizers']['matched'])

    def test_asf_object_size_must_equal_the_file_size_it_claims(self):
        data = bytearray(data_for('asf-header-guid-v1'))
        struct.pack_into('<Q', data, 16, 4096)
        with self.assertRaises(ValueError) as caught:
            catalog.structure('asf-header-guid-v1', 'owned-asf-header.asf', bytes(data))
        self.assertIn('not the file size', str(caught.exception))

    def test_no_fixture_may_land_on_a_tiff_family_reader_by_accident(self):
        # Every CIFF file starts with II plus a u32, which is one misapplied constant away
        # from a TIFF magic; if that happened the registry would hand the file to tiff (its
        # entry is at image.cpp:79) and CRW would never be reached.
        for layout in sorted(catalog.FIXTURES):
            matched = facts(layout)['recognizers']['matched']
            self.assertNotIn('tiff', matched, layout)
            self.assertNotIn('orf', matched, layout)
            self.assertNotIn('jpeg', matched, layout)

    def test_dispatch_is_validated_against_the_layout_name(self):
        # structure() is not a reporter: it refuses a fixture whose bytes reach a different
        # reader than the layout claims, so a registry change cannot be recorded silently.
        with self.assertRaises(ValueError) as caught:
            catalog.structure('bmp-dib-v3', 'owned-bmp-dib.bmp', b'GIF89a' + bytes(7))
        self.assertIn('dispatch reached', str(caught.exception))

    def test_encoding_is_deterministic_and_matches_the_committed_files(self):
        for layout, (_fmt, name) in catalog.FIXTURES.items():
            self.assertEqual(data_for(layout), (catalog.DIRECTORY / name).read_bytes(), layout)
            self.assertEqual(data_for(layout), data_for(layout), f'{layout} is not pure')

    def test_tokens_appear_in_the_bytes_the_cli_will_be_asked_to_print(self):
        blob = data_for('ciff-mapped-v1')
        for token in catalog.TOKENS:
            self.assertIn(token.encode('latin-1'), blob, token)
        self.assertEqual(('B11CrwComment', 'CANON', 'B11 SYNTHETIC CRW', 'B11Owner',
                          'B11FIRMWARE'), catalog.TOKENS)
        for layout in catalog.FIXTURES:
            if layout.startswith('ciff') and layout != 'ciff-mapped-v1':
                self.assertNotIn(b'B11CrwComment', data_for(layout), layout)

    def test_manifest_json_is_committed_and_matches_the_live_catalog(self):
        recorded = json.loads(catalog.MANIFEST.read_text(encoding='utf-8'))
        self.assertEqual(recorded, catalog.entries())
        self.assertEqual(18, len(recorded))


if __name__ == '__main__':
    unittest.main()
