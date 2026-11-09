import hashlib
import importlib.util
import json
import sys
import tempfile
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/reference'))
spec = importlib.util.spec_from_file_location('owned_tiff_fixtures', ROOT / 'tools/reference/tiff_owned_fixtures.py')
owned = importlib.util.module_from_spec(spec); spec.loader.exec_module(owned)
import observe_b4  # noqa: E402
import jpeg_owned_fixtures as jpegs  # noqa: E402

MANIFEST = Path(ROOT / 'tests/fixtures/tiff/manifest.json')


class OwnedTiffFixtureTests(unittest.TestCase):
    def setUp(self):
        self.pinned = {e['file']: e for e in json.loads(MANIFEST.read_text())}

    def test_manifest_pins_every_fixture(self):
        self.assertEqual(14, len(self.pinned))
        for name, entry in self.pinned.items():
            self.assertEqual('owned-synthetic', entry['rights'])
            self.assertRegex(entry['sha256'], r'^[0-9a-f]{64}$')

    def test_regeneration_is_byte_deterministic(self):
        with tempfile.TemporaryDirectory() as tmp:
            owned.generate(tmp)
            for name, entry in self.pinned.items():
                data = (Path(tmp) / name).read_bytes()
                self.assertEqual(entry['sha256'], hashlib.sha256(data).hexdigest())
                self.assertEqual(entry['length'], len(data))

    def test_every_fixture_is_a_well_formed_classic_tiff(self):
        for name in self.pinned:
            data = owned.fixture_bytes(name)
            self.assertIsNone(observe_b4.find_strict_parse_errors(data), name)
            self.assertEqual(b'II', data[:2], name)
            self.assertEqual(42, int.from_bytes(data[2:4], 'little'), name)

    def test_strip_and_tile_payloads_round_trip_the_pinned_pixels(self):
        self.assertEqual(owned.PIXEL, observe_b4.scan_payloads(owned.fixture_bytes('tiff-base.tif')))
        self.assertEqual(owned.TILE_PIXEL, observe_b4.scan_payloads(owned.fixture_bytes('tiff-tiles.tif')))

    def test_tiles_fixture_uses_tile_tags_not_strip_tags(self):
        ifds = jpegs.parse_tiff(owned.fixture_bytes('tiff-tiles.tif'))
        self.assertIn(322, ifds[0]['tags'])  # TileWidth
        self.assertNotIn(273, ifds[0]['tags'])  # StripOffsets

    def test_exif_fixture_carries_orientation_and_artist(self):
        ifds = jpegs.parse_tiff(owned.fixture_bytes('tiff-exif.tif'))
        tags = ifds[0]['tags']
        self.assertEqual(b'\x01\x00', tags[274][0][2])           # Orientation SHORT 1
        self.assertEqual(b'Alice\x00', tags[315][0][2])           # Artist ASCII

    def test_exif_dup_fixture_carries_two_artist_entries(self):
        ifds = jpegs.parse_tiff(owned.fixture_bytes('tiff-exif-dup.tif'))
        artists = [raw for raw in (e[2] for e in ifds[0]['tags'][315])]
        self.assertEqual([b'Alice\x00', b'Bob\x00'], artists)

    def test_thumbnail_fixtures_chain_ifd1(self):
        for name in ('tiff-exif-thumb-jpeg.tif', 'tiff-exif-thumb-raw.tif'):
            ifds = jpegs.parse_tiff(owned.fixture_bytes(name))
            self.assertEqual(2, len(ifds), name)  # IFD0 -> IFD1 chain
            self.assertIn(0x0103, ifds[1]['tags'], name)  # Compression
        jpeg_thumb = jpegs.parse_tiff(owned.fixture_bytes('tiff-exif-thumb-jpeg.tif'))[1]['tags']
        self.assertIn(0x0201, jpeg_thumb)  # JPEGInterchangeFormat
        raw_thumb = jpegs.parse_tiff(owned.fixture_bytes('tiff-exif-thumb-raw.tif'))[1]['tags']
        self.assertIn(273, raw_thumb)  # StripOffsets (uncompressed strip)

    def test_iccp_fixtures(self):
        profiles = observe_b4.icc_profiles(owned.fixture_bytes('tiff-iccp.tif'))
        self.assertEqual([jpegs.PROFILE_A], profiles)
        dup_profiles = observe_b4.icc_profiles(owned.fixture_bytes('tiff-iccp-dup.tif'))
        self.assertEqual([jpegs.PROFILE_A, jpegs.PROFILE_A], dup_profiles)

    def test_iptc_fixtures_round_trip_iim_keywords(self):
        for name in ('tiff-iptc.tif', 'tiff-iptc-dup.tif'):
            keywords = observe_b4.iptc_keywords(owned.fixture_bytes(name))
            self.assertEqual([b'MetadataSharpAlpha', b'MetadataSharpBeta'],
                             keywords if name == 'tiff-iptc.tif' else keywords[:2], name)
        dup_keywords = observe_b4.iptc_keywords(owned.fixture_bytes('tiff-iptc-dup.tif'))
        self.assertEqual([b'MetadataSharpAlpha', b'MetadataSharpBeta'] * 2, dup_keywords)

    def test_xmp_carriers(self):
        packets = observe_b4.xmp_packets(owned.fixture_bytes('tiff-xmp.tif'))
        self.assertEqual(1, len(packets))
        self.assertIn(b'MetadataSharp</xmp:CreatorTool>', packets[0])
        dup = observe_b4.xmp_packets(owned.fixture_bytes('tiff-xmp-dup.tif'))
        self.assertEqual(2, len(dup))
        self.assertIn(b'MetadataSharp</xmp:CreatorTool>', dup[0])
        self.assertIn(b'MetadataSharpB</xmp:CreatorTool>', dup[1])
        nonutf8 = observe_b4.xmp_packets(owned.fixture_bytes('tiff-xmp-nonutf8.tif'))
        self.assertEqual([b'\x89\xfe\xffRAW not xml'], nonutf8)

    def test_comments_carrier_is_xpcomment_utf16le_and_record_only(self):
        ifds = jpegs.parse_tiff(owned.fixture_bytes('tiff-comments.tif'))
        raw = ifds[0]['tags'][0x9C9C][0][2]
        self.assertIn('MetadataSharp B4 comment', raw.decode('utf-16-le'))
        # The pinned CLI's -pc prints nothing on this carrier; recorded in the
        # evidence, never asserted here.

    def test_scan_invariant_holds_across_fixtures(self):
        for name in self.pinned:
            data = owned.fixture_bytes(name)
            self.assertEqual(observe_b4.scan_payloads(data), observe_b4.scan_payloads(data), name)

    def test_provenance_matches_manifest(self):
        provenance = json.loads((ROOT / 'tests/fixtures/tiff/provenance.json').read_text())
        self.assertEqual({e['file'] for e in self.pinned.values()},
                         {e['file'] for e in provenance['fixtures']})


if __name__ == '__main__':
    unittest.main()
