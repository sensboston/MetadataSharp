"""Offline tests for the B10 census fixture catalog and its byte-structure oracle.

No external tool is executed here: the catalog must verify with the standard library
alone, so CI and a developer laptop reach the same conclusion about the fixtures.
These tests check our own authored framing; they say nothing about vendor RAW layouts,
and none of them is a support or conformance assertion.
"""
import json
import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/reference'))
import b10_raw_fixtures as catalog  # noqa: E402
import jpeg_owned_fixtures as owned  # noqa: E402


def data_for(fmt):
    return catalog.encode(fmt)


def facts(fmt):
    return catalog.structure(fmt, data_for(fmt))


class CatalogTests(unittest.TestCase):
    def test_manifest_matches_bytes_and_records_the_authored_contract(self):
        manifest = catalog.verify()
        self.assertEqual(sorted(catalog.FIXTURES), sorted(entry['layout'] for entry in manifest))
        self.assertEqual(4, len(manifest))
        for entry in manifest:
            raw = catalog.DIRECTORY / entry['file']
            self.assertEqual(raw.read_bytes(), data_for(entry['format']))
            self.assertEqual(len(raw.read_bytes()), entry['length'])
            self.assertEqual(catalog.digest(raw.read_bytes()), entry['sha256'])
            self.assertEqual('owned-synthetic', entry['rights'])
            self.assertIn('tools/reference/b10_raw_fixtures.py', entry['recipe'])
            self.assertEqual(catalog.FIXTURES[entry['layout']][0], entry['format'])
            self.assertEqual('authored-exif-xmp-iptc-icc-thumbnail-comments',
                             entry['metadata'])
            self.assertFalse(entry['decodable'])
            self.assertEqual(list(catalog.FORMATS[entry['format']]['carriers']),
                             entry['carrierTags'])
            # The manifest records which reader the bytes reach, not which one we hope for.
            self.assertEqual(catalog.FORMATS[entry['format']]['container'], entry['dispatch'])
            self.assertEqual(catalog.FORMATS[entry['format']]['thumbnail'],
                             entry['thumbnailPresent'])

    def test_vendor_private_layouts_are_absent_from_the_catalog(self):
        # A blocked cell must never be able to grow a fixture behind the review.
        for layout in catalog.VENDOR_LAYOUTS:
            self.assertNotIn(layout, catalog.FIXTURES)
        self.assertEqual(4, len(catalog.VENDOR_LAYOUTS))
        self.assertEqual({'MRW', 'RAF', 'RW2', 'SR2'}, {fmt for fmt, _ in catalog.FIXTURES.values()})
        for fmt in ('MRW', 'RAF', 'RW2', 'SR2'):
            reason = catalog.vendor_reason(fmt)
            self.assertIn(fmt, reason)
            self.assertIn('blocked', reason)
            self.assertIn('E01_B10_RIGHTS_AND_ORACLE_REVIEW.md', reason)
        self.assertEqual(4, len({catalog.vendor_reason(f) for f in ('MRW', 'RAF', 'RW2', 'SR2')}))
        # The RAF reason must name what the pinned reader itself refuses to parse.
        self.assertIn('proprietary', catalog.vendor_reason('RAF'))
        self.assertIn('B12', catalog.vendor_reason('SR2'))

    def test_mrw_walk_lands_on_the_ttw_block_the_reader_searches_for(self):
        report = facts('MRW')
        mrw = report['mrw']
        self.assertEqual(['\x00PRD', '\x00WBG', '\x00RIF', '\x00TTW'], mrw['blockTags'])
        self.assertEqual(80, mrw['ttwAt'])
        self.assertTrue(mrw['coversFile'])
        # The bound the C++ enforces against is also what ExifTool computes as its loop
        # bound (MinoltaRaw.pm: Get32u(data,4) + 8), so both readers see the same span.
        self.assertEqual(report['fileLength'], mrw['exiftoolLoopBound'])
        self.assertEqual(mrw['blockBound'] + 8, report['fileLength'])
        embedded = report['embedded']
        self.assertEqual(['IFD0', 'IFD1', 'ExifIFD'], embedded['directoryOrder'])
        self.assertEqual(8, embedded['rootOffset'])
        self.assertTrue(embedded['thumbnailPresent'])
        self.assertEqual('mrw', report['dispatch'])

    def test_mrw_block_bound_is_enforced_and_not_merely_advisory(self):
        data = bytearray(data_for('MRW'))
        struct.pack_into('>I', data, 4, 40)          # claim a smaller bound than the blocks
        with self.assertRaises(ValueError) as caught:
            catalog.structure('MRW', bytes(data))
        self.assertIn('no room left for the next block header', str(caught.exception))

    def test_rw2_header_matches_the_pinned_magic_root_and_length(self):
        report = facts('RW2')
        rw2 = report['rw2']
        self.assertEqual('0x0055', report['recognizers']['rw2']['sig'])
        self.assertEqual(24, rw2['rootOffset'])
        self.assertEqual(24, rw2['headerBytes'])
        self.assertEqual('B10-RW2-IDENTITY', rw2['identityField'])
        self.assertEqual('rw2', report['dispatch'])
        # The whole point of magic 85: the generic TIFF checker must reject this file,
        # otherwise the registry's earlier `tiff` entry would swallow it and Rw2Image
        # (and its `RW2 IMAGE` structure print) would never be reached.
        self.assertFalse(report['recognizers']['tiff']['accepted'])

    def test_rw2_accept_test_needs_the_full_24_byte_header(self):
        with self.assertRaises(ValueError) as caught:
            catalog.recognizers(data_for('RW2')[:23])
        self.assertIn('24 bytes', str(caught.exception))

    def test_raf_offset_table_uses_the_fields_the_reader_reads(self):
        report = facts('RAF')
        raf = report['raf']
        self.assertEqual('FUJIFILM', raf['printedText']['magic'][:8])
        self.assertEqual('0232', raf['printedText']['magic'][8:12])
        self.assertEqual('    ', raf['printedText']['data1'])
        self.assertEqual('B10DATA2', raf['printedText']['data2'])
        self.assertTrue(raf['printedText']['camera'].startswith('B10 SYNTHETIC RAF CAMERA'))
        self.assertEqual('0100', raf['printedText']['version'])
        self.assertEqual(108, raf['jpegOffset'])
        self.assertGreaterEqual(raf['jpegLength'], 12)     # the pinned enforce()
        for off, length in ((raf['jpegOffset'], raf['jpegLength']),
                            (raf['tiffOffset'], raf['tiffLength']),
                            (raf['cfaOffset'], raf['cfaLength'])):
            self.assertLessEqual(off + length, report['fileLength'])
        data = data_for('RAF')
        self.assertEqual(b'II\x2a\x00', data[raf['tiffOffset']:raf['tiffOffset'] + 4])
        self.assertEqual(b'\xff\xd8\xff\xe1', data[raf['jpegOffset']:raf['jpegOffset'] + 4])
        self.assertEqual('raf', report['dispatch'])

    def test_raf_families_come_from_the_carrier_the_code_copies_them_from(self):
        report = facts('RAF')
        # ICC belongs to the camera-settings TIFF (the only block TiffParser::decode
        # receives); the comment belongs to the preview JPEG's COM segment, which is the
        # field RafImage::readMetadata actually copies.
        self.assertEqual(['icc'], report['embedded']['carriers'])
        self.assertFalse(report['embedded']['thumbnailPresent'])
        preview = report['preview']
        self.assertEqual(catalog.digest(catalog.RAF_COMMENT), preview['commentSha256'])
        # The APP1 carries a whole TIFF of its own, and the lengths must add up: the
        # JPEG segment field counts itself, which is where an off-by-two would hide.
        self.assertEqual(42, preview['tiff']['magicTag'])
        self.assertGreater(preview['app1Length'], 8)
        self.assertEqual(preview['app1Length'], preview['exifTiffLength'])
        self.assertTrue(preview['jpegSha256'])
        tags = preview['tiff']['ifd0Tags']
        for tag in (271, 272, 305, 315, 0x83BB, 0x02BC):
            self.assertIn(tag, tags)
        # The comment never travels as a TIFF tag here, and the profile never travels
        # inside the JPEG: swapping them would silently credit a family to the wrong block.
        self.assertNotIn(0x9C9C, tags)
        self.assertNotIn(0x8773, tags)
        self.assertNotIn(0x9C9C, report['embedded']['ifd0Tags'])
        self.assertNotIn(0x02BC, report['embedded']['ifd0Tags'])

    def test_sr2_marker_reaches_only_the_generic_tiff_reader(self):
        report = facts('SR2')
        self.assertEqual('tiff', report['dispatch'])
        self.assertTrue(report['recognizers']['tiff']['accepted'])
        self.assertNotIn(report['embedded']['compression'], catalog.MIME_COMPRESSIONS)
        self.assertEqual(1, report['embedded']['compression'])
        self.assertEqual('SONY', report['embedded']['make'])

    def test_sr2_may_not_borrow_a_wave_one_identity(self):
        data = bytearray(data_for('SR2'))
        # Locate the Compression entry (tag 259) in IFD0 and rewrite its value.
        root = struct.unpack_from('<I', data, 4)[0]
        count = struct.unpack_from('<H', data, root)[0]
        for index in range(count):
            at = root + 2 + 12 * index
            if struct.unpack_from('<H', data, at)[0] == 259:
                struct.pack_into('<H', data, at + 8, 32767)   # Sony ARW
                break
        else:
            self.fail('the SR2 marker carries no Compression entry')
        with self.assertRaises(ValueError) as caught:
            catalog.structure('SR2', bytes(data))
        self.assertIn('wave-1 vendor compression value', str(caught.exception))

    def test_wildcard_magics_are_refused_rather_than_flattered(self):
        # 444 (JPEG-XR) and 17234 (DCP) pass every TIFF-family header in this build, so a
        # fixture that relied on one would prove nothing about the format it names.
        for magic in catalog.WILDCARD_MAGICS:
            data = bytearray(data_for('SR2'))
            struct.pack_into('<H', data, 2, magic)
            with self.assertRaises(ValueError) as caught:
                catalog.structure('SR2', bytes(data))
            self.assertIn('wildcard magic', str(caught.exception))

    def test_embedded_blobs_obey_the_same_padding_and_span_rules(self):
        for fmt in ('MRW', 'RAF', 'RW2', 'SR2'):
            report = facts(fmt)
            embedded = report['embedded']
            self.assertGreaterEqual(embedded['dataPoolSpans'], 4, fmt)
            self.assertIn(embedded['trailingPadBytes'], (0, 1), fmt)
            self.assertIn(report['trailingPadBytes'], (0, 1), fmt)
            if fmt in ('RW2', 'SR2'):
                # The file *is* the TIFF, so blob facts are file facts.
                self.assertEqual(report['fileLength'], embedded['blobLength'], fmt)
            else:
                self.assertLess(embedded['blobLength'], report['fileLength'], fmt)

    def test_tampering_is_rejected_rather_than_silently_parsed(self):
        # Every carrier payload is compared with its owned source, so a single flipped byte
        # inside the XMP or ICC span has to raise rather than print something plausible.
        cases = {'MRW': 0x02BC, 'RW2': 0x8773, 'SR2': 0x83BB}
        for fmt, tag in cases.items():
            report = facts(fmt)
            # An embedded blob addresses its own start, so the file offset is the block
            # position plus the blob-relative offset the reparse reported.
            base = report['mrw']['ttwAt'] if fmt == 'MRW' else 0
            data = bytearray(data_for(fmt))
            ifd_at = base + report['embedded']['rootOffset']
            count = struct.unpack_from('<H', data, ifd_at)[0]
            for index in range(count):
                at = ifd_at + 2 + 12 * index
                if struct.unpack_from('<H', data, at)[0] == tag:
                    offset = base + struct.unpack_from('<I', data, at + 8)[0]
                    data[offset] ^= 0x01
                    break
            else:
                self.fail(f'{fmt}: carrier tag {tag} was not authored')
            with self.assertRaises(ValueError) as caught:
                catalog.structure(fmt, bytes(data))
            self.assertIn('differs from the authored', str(caught.exception))
        # RAF's ICC lives in the camera-settings block, not the file root.
        report = facts('RAF')
        data = bytearray(data_for('RAF'))
        at = report['raf']['tiffOffset'] + report['embedded']['rootOffset']
        count = struct.unpack_from('<H', data, at)[0]
        for index in range(count):
            entry = at + 2 + 12 * index
            if struct.unpack_from('<H', data, entry)[0] == 0x8773:
                offset = report['raf']['tiffOffset'] + struct.unpack_from('<I', data, entry + 8)[0]
                data[offset] ^= 0x01
                break
        with self.assertRaises(ValueError) as caught:
            catalog.structure('RAF', bytes(data))
        self.assertIn('differs from the authored icc', str(caught.exception))

    def test_raf_cfa_block_is_owned_filler_and_is_never_claimed_as_a_structure(self):
        report = facts('RAF')
        data = data_for('RAF')
        block = data[report['raf']['cfaOffset']:report['raf']['cfaOffset'] +
                     report['raf']['cfaLength']]
        self.assertTrue(block.startswith(b'B10 OWNED OFFSET-92 FILLER'))
        self.assertIn(b'NOT A VENDOR CFA STRUCTURE', block)
        for entry in catalog.entries():
            self.assertNotIn('CFA', entry['metadata'])
            self.assertNotIn('cfa', entry['metadata'])

    def test_encoding_is_deterministic_and_matches_the_committed_files(self):
        for fmt, name in sorted(catalog.FIXTURES.values()):
            self.assertEqual(data_for(fmt), (catalog.DIRECTORY / name).read_bytes(), fmt)
            self.assertEqual(data_for(fmt), data_for(fmt), f'{fmt} is not a pure function')

    def test_tokens_and_identity_strings_are_present(self):
        self.assertEqual(('B10Artist', 'B10Tool'), catalog.TOKENS)
        blob = data_for('SR2')
        self.assertIn(b'B10Artist\x00', blob)
        self.assertIn(b'B10Tool\x00', blob)
        self.assertIn(b'B10RafComment', data_for('RAF'))
        for fmt in catalog.FORMATS:
            self.assertIn('B10', catalog.FORMATS[fmt]['model'])
            self.assertTrue(catalog.FORMATS[fmt]['identity'])

    def test_owned_payloads_are_reused_not_reinvented(self):
        # The B10 carriers are the same owned bytes the earlier batches pinned, so the
        # rights record written for them keeps covering this batch too.
        self.assertEqual(owned.PROFILE_A, catalog.CARRIERS['icc'][2])
        self.assertEqual(owned.xmp_packet(), catalog.CARRIERS['xmp'][2])
        self.assertEqual(owned.IPTC_ALPHA + owned.IPTC_BETA, catalog.CARRIERS['iptc'][2])
        self.assertEqual(owned.thumb_jpeg_bytes(), catalog.payload_for('SR2', 'thumb'))

    def test_manifest_json_is_committed_and_matches_the_live_catalog(self):
        recorded = json.loads(catalog.MANIFEST.read_text(encoding='utf-8'))
        self.assertEqual(recorded, catalog.entries())
        self.assertEqual(4, len(recorded))


if __name__ == '__main__':
    unittest.main()
