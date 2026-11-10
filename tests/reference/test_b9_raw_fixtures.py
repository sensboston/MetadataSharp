"""Offline tests for the B9 census fixture catalog and its byte-structure oracle.

No external tool is executed here: the catalog must verify with the standard library
alone, so CI and a developer laptop reach the same conclusion about the fixtures.
These tests check our own authored framing; they say nothing about vendor RAW
layouts, and none of them is a support or conformance assertion.
"""
import json
import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/reference'))
import b9_raw_fixtures as catalog  # noqa: E402
import jpeg_owned_fixtures as owned  # noqa: E402


def data_for(fmt):
    return catalog.encode(fmt)


def facts(fmt):
    return catalog.structure(data_for(fmt))


class CatalogTests(unittest.TestCase):
    def test_manifest_matches_bytes_and_records_the_authored_contract(self):
        manifest = catalog.verify()
        self.assertEqual(sorted(catalog.FIXTURES), sorted(entry['layout'] for entry in manifest))
        self.assertEqual(6, len(manifest))
        for entry in manifest:
            raw = (catalog.DIRECTORY / entry['file']).read_bytes()
            self.assertEqual(len(raw), entry['length'])
            self.assertEqual(catalog.digest(raw), entry['sha256'])
            self.assertEqual('owned-synthetic', entry['rights'])
            self.assertIn('tools/reference/b9_raw_fixtures.py', entry['recipe'])
            self.assertEqual(catalog.FIXTURES[entry['layout']][0], entry['format'])
            self.assertEqual('authored-exif-xmp-iptc-icc-thumbnail-comments', entry['metadata'])
            # These fixtures carry no decodable image data, by design.
            self.assertFalse(entry['decodable'])
            if entry['dispatch'] == 'tiff':
                # A marker format's identity is its compression value, by contract.
                self.assertIn('Compression', entry['vendorIdentity'])
            else:
                self.assertIn('header', entry['vendorIdentity'])

    def test_vendor_private_layouts_are_absent_from_the_catalog(self):
        # A blocked cell must never be able to grow a fixture behind the review.
        for layout in catalog.VENDOR_LAYOUTS:
            self.assertNotIn(layout, catalog.FIXTURES)
        self.assertEqual(6, len(catalog.VENDOR_LAYOUTS))
        self.assertEqual({'ARW', 'CR2', 'NEF', 'ORF', 'PEF', 'SRW'},
                         {fmt for fmt, _ in catalog.FIXTURES.values()})
        for fmt in ('ARW', 'CR2', 'NEF', 'ORF', 'PEF', 'SRW'):
            reason = catalog.vendor_reason(fmt)
            self.assertIn(fmt, reason)
            self.assertIn('blocked', reason)
            self.assertIn('E01_B9_RIGHTS_AND_ORACLE_REVIEW.md', reason)
        self.assertEqual(6, len({catalog.vendor_reason(f) for f in
                                 ('ARW', 'CR2', 'NEF', 'ORF', 'PEF', 'SRW')}))


class HeaderContractTests(unittest.TestCase):
    def test_cr2_header_carries_the_signature_the_pinned_reader_requires(self):
        raw = data_for('CR2')
        self.assertEqual(b'II', raw[:2])
        self.assertEqual(42, struct.unpack_from('<H', raw, 2)[0])
        self.assertEqual(16, struct.unpack_from('<I', raw, 4)[0])
        self.assertEqual(b'CR\2\0', raw[8:12])
        self.assertEqual(0, struct.unpack_from('<I', raw, 12)[0])
        info = facts('CR2')
        self.assertEqual('cr2', info['dispatch'])
        self.assertEqual(16, info['rootOffset'])
        self.assertEqual(16, info['headerBytes'])
        self.assertTrue(info['recognizers']['cr2']['accepted'])
        # The generic TIFF reader would also accept tag 42, but the registry scans
        # cr2 first; that ordering is the reason dispatch is asserted, not assumed.
        self.assertTrue(info['recognizers']['tiff']['accepted'])

    def test_orf_header_uses_the_or_tag_the_pinned_reader_requires(self):
        raw = data_for('ORF')
        self.assertEqual(b'IIRO', raw[:4])
        self.assertEqual(8, struct.unpack_from('<I', raw, 4)[0])
        info = facts('ORF')
        self.assertEqual('orf', info['dispatch'])
        self.assertEqual('0x4f52', info['recognizers']['orf']['sig'])
        self.assertFalse(info['recognizers']['tiff']['accepted'],
                         'an ORF header must not look like plain TIFF to the pinned reader')

    def test_compression_marker_formats_dispatch_as_tiff(self):
        for fmt, compression in (('ARW', 32767), ('NEF', 34713), ('PEF', 65535), ('SRW', 32770)):
            info = facts(fmt)
            self.assertEqual('tiff', info['dispatch'], fmt)
            self.assertEqual(compression, info['compression'], fmt)
            self.assertEqual(8, info['rootOffset'], fmt)
            self.assertFalse(info['recognizers']['cr2']['accepted'], fmt)
            self.assertFalse(info['recognizers']['orf']['accepted'], fmt)
        # CR2 and ORF keep the plain value because their identity is the header.
        self.assertEqual(1, facts('CR2')['compression'])
        self.assertEqual(1, facts('ORF')['compression'])


class StructureOracleTests(unittest.TestCase):
    def test_directory_walk_finds_ifd0_the_chained_thumbnail_and_the_exif_subifd(self):
        for fmt in catalog.FORMATS:
            info = facts(fmt)
            self.assertEqual(3, info['directoryCount'], fmt)
            self.assertEqual(['IFD0', 'ExifIFD', 'IFD1'], [d['name'] for d in info['directories']], fmt)
            self.assertTrue(info['thumbnailPresent'], fmt)
            self.assertEqual([0x02BC, 0x83BB, 0x8773, 0x9C9C], info['carrierTags'], fmt)
            self.assertIn(0x8769, info['ifd0Tags'], fmt)
            self.assertIn(0x9286, info['exifTags'], fmt)

    def test_carrier_payloads_are_the_owned_bytes_not_a_reconstruction(self):
        info = facts('ARW')
        ifd0 = catalog.read_ifd(data_for('ARW'), info['rootOffset'], 'IFD0', set())
        expected = {0x02BC: owned.xmp_packet(), 0x83BB: catalog.IIM, 0x8773: owned.PROFILE_A,
                    0x9C9C: catalog.COMMENT}
        for tag, payload in expected.items():
            self.assertEqual(catalog.digest(payload), ifd0['byTag'][tag]['payloadSha256'], hex(tag))

    def test_data_pool_is_fully_accounted_for(self):
        for fmt in catalog.FORMATS:
            info = facts(fmt)
            self.assertEqual(info['fileLength'], info['dataPoolEnd'] + info['trailingPadBytes'], fmt)
            self.assertIn(info['trailingPadBytes'], (0, 1), fmt)
            self.assertGreaterEqual(info['dataPoolSpans'], 12, fmt)

    def test_tampering_is_rejected_rather_than_silently_parsed(self):
        raw = bytearray(data_for('ARW'))
        info = facts('ARW')
        # A directory entry out of ascending order is not a file this batch authors.
        entries = [e['tag'] for e in catalog.read_ifd(bytes(raw), info['rootOffset'],
                                                       'IFD0', set())['entries']]
        self.assertEqual(sorted(entries), entries)
        broken = bytearray(raw)
        struct.pack_into('<H', broken, info['rootOffset'] + 2, 0xFFFF)   # first entry tag
        struct.pack_into('<H', broken, info['rootOffset'] + 4, 0x0001)   # second entry tag
        with self.assertRaises(ValueError):
            catalog.structure(bytes(broken))
        # A payload offset pointing outside the file must fail closed: rewrite the
        # StripOffsets value field (entry index of tag 273 in IFD0) with a wild offset.
        tags = [e['tag'] for e in catalog.read_ifd(bytes(raw), info['rootOffset'],
                                                   'IFD0', set())['entries']]
        slot = info['rootOffset'] + 2 + 12 * tags.index(273)
        bad = bytearray(raw)
        struct.pack_into('<I', bad, slot + 8, len(bad) + 64)
        with self.assertRaises(ValueError):
            catalog.structure(bytes(bad))

    def test_encoding_is_deterministic_and_matches_the_committed_files(self):
        for fmt in catalog.FORMATS:
            self.assertEqual(catalog.encode(fmt), catalog.encode(fmt), fmt)
            name = catalog.FIXTURES[{'ARW': 'raw-tiff-ep-ii-arw', 'CR2': 'raw-cr2-ii',
                                     'NEF': 'raw-tiff-ep-ii-nef', 'ORF': 'raw-orf-ii',
                                     'PEF': 'raw-tiff-ep-ii-pef', 'SRW': 'raw-tiff-ep-ii-srw'}[fmt]][1]
            self.assertEqual((catalog.DIRECTORY / name).read_bytes(), catalog.encode(fmt), fmt)


class AuthoredTokenTests(unittest.TestCase):
    def test_tokens_and_identity_strings_are_present(self):
        for fmt, spec in catalog.FORMATS.items():
            raw = data_for(fmt)
            self.assertIn(b'B9Artist', raw, fmt)
            self.assertIn(b'B9Tool', raw, fmt)
            self.assertIn(b'B9Comment', raw, fmt)
            self.assertIn(spec['make'].encode('ascii'), raw, fmt)
            self.assertIn(spec['model'].encode('ascii'), raw, fmt)
            # No IPTC keyword token exists in these fixtures; the owned IIM records are
            # reused verbatim, so claiming a B9 IPTC token would be a false oracle.
            self.assertNotIn(b'B9Keyword', raw, fmt)

    def test_utf16_comment_carrier_cannot_be_token_matched(self):
        # Recorded so a reviewer does not read a missing -pc token as a missing comment.
        self.assertIn(b'\x00', catalog.COMMENT)
        self.assertNotIn(b'B9Comment', catalog.COMMENT)

    def test_manifest_json_is_committed_and_matches_the_live_catalog(self):
        recorded = json.loads(catalog.MANIFEST.read_text(encoding='utf-8'))
        self.assertEqual(recorded, catalog.entries())
        self.assertTrue((catalog.DIRECTORY / 'census-manifest.json').exists())


if __name__ == '__main__':
    unittest.main()
