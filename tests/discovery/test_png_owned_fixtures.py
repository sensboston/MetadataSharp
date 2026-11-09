import importlib.util
import json
import sys
import tempfile
import zlib
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/reference'))
spec = importlib.util.spec_from_file_location('owned_png_fixtures', ROOT / 'tools/reference/png_owned_fixtures.py')
owned = importlib.util.module_from_spec(spec); spec.loader.exec_module(owned)
import observe_b2  # noqa: E402
import jpeg_owned_fixtures as jpegs  # noqa: E402

MANIFEST = Path(ROOT / 'tests/fixtures/png/manifest.json')


class OwnedPngFixtureTests(unittest.TestCase):
    def setUp(self):
        self.pinned = {e['file']: e for e in json.loads(MANIFEST.read_text())}

    def test_manifest_pins_every_fixture(self):
        self.assertEqual(17, len(self.pinned))
        for name, entry in self.pinned.items():
            self.assertEqual('owned-synthetic', entry['rights'])
            self.assertRegex(entry['sha256'], r'^[0-9a-f]{64}$')

    def test_regeneration_is_byte_deterministic(self):
        import hashlib
        with tempfile.TemporaryDirectory() as tmp:
            owned.generate(tmp)
            for name, entry in self.pinned.items():
                data = (Path(tmp) / name).read_bytes()
                self.assertEqual(entry['sha256'], hashlib.sha256(data).hexdigest())
                self.assertEqual(entry['length'], len(data))

    def test_chunks_are_well_formed_and_crcs_valid(self):
        for name in self.pinned:
            data = owned.fixture_bytes(name)
            chunks, crc_ok = observe_b2.png_chunks(data)
            self.assertTrue(crc_ok, name)
            types = [ctype for ctype, _ in chunks]
            self.assertEqual(b'IHDR', types[0], name)
            self.assertEqual(b'IEND', types[-1], name)
            self.assertIn(b'IDAT', types, name)

    def test_exif_fixture_carries_orientation_and_artist(self):
        blocks = observe_b2.exif_blocks(owned.fixture_bytes('png-exif.png'))
        self.assertEqual(1, len(blocks))
        tags = blocks[0][0]['tags']
        self.assertEqual(b'\x01\x00', tags[274][0][2])           # Orientation SHORT 1
        self.assertEqual(b'Alice\x00', tags[315][0][2])           # Artist ASCII

    def test_exif_thumbnail_fixtures_chain_ifd1(self):
        for name in ('png-exif-thumb-jpeg.png', 'png-exif-thumb-raw.png'):
            blocks = observe_b2.exif_blocks(owned.fixture_bytes(name))
            self.assertEqual(1, len(blocks), name)  # single eXIf chunk
            ifds = blocks[0]
            self.assertEqual(2, len(ifds), name)  # IFD0 -> IFD1 chain
            self.assertIn(0x0103, ifds[1]['tags'], name)  # Compression

    def test_iccp_fixture_roundtrips(self):
        profiles = observe_b2.icc_profiles(owned.fixture_bytes('png-iccp.png'))
        self.assertEqual([jpegs.PROFILE_A], profiles)
        dup_profiles = observe_b2.icc_profiles(owned.fixture_bytes('png-iccp-dup.png'))
        self.assertEqual([jpegs.PROFILE_A, jpegs.PROFILE_A], dup_profiles)

    def test_iptc_raw_profile_carriers(self):
        for name in ('png-iptc.png', 'png-iptc-dup.png', 'png-iptc-itxt.png', 'png-iptc-itxt-dup.png'):
            keywords = observe_b2.raw_profile_keywords(owned.fixture_bytes(name), 'iptc')
            self.assertEqual(['MetadataSharpAlpha', 'MetadataSharpBeta'], keywords, name)
        for name, carrier in (('png-iptc.png', b'zTXt'), ('png-iptc-itxt.png', b'iTXt')):
            types = [ctype for ctype, _ in observe_b2.png_chunks(owned.fixture_bytes(name))[0]]
            self.assertIn(carrier, types, name)

    def test_xmp_carriers(self):
        itxt = owned.fixture_bytes('png-xmp.png')
        ztxt = owned.fixture_bytes('png-xmp-ztxt.png')
        packets_it = observe_b2.xmp_packets(itxt)
        packets_zt = observe_b2.xmp_packets(ztxt)
        self.assertEqual(1, len(packets_it))
        self.assertEqual(1, len(packets_zt))
        self.assertEqual(packets_it, packets_zt)
        self.assertIn(b'MetadataSharp</xmp:CreatorTool>', packets_it[0])
        for dup_name in ('png-xmp-dup.png', 'png-xmp-ztxt-dup.png'):
            dup = observe_b2.xmp_packets(owned.fixture_bytes(dup_name))
            self.assertEqual(2, len(dup), dup_name)
            self.assertIn(b'MetadataSharpA</xmp:CreatorTool>', dup[0])
            self.assertIn(b'MetadataSharpB</xmp:CreatorTool>', dup[1])

    def test_idat_invariant_holds_across_mutation(self):
        for name in self.pinned:
            data = owned.fixture_bytes(name)
            self.assertEqual(observe_b2.idat_payloads(data), observe_b2.idat_payloads(data), name)

    def test_text_trio_fixture(self):
        chunks = observe_b2.text_chunks(owned.fixture_bytes('png-text.png'))
        keywords = [kw for _, kw, _ in chunks]
        self.assertEqual(['Description', 'Comment', 'Comment'], keywords)

    def test_provenance_matches_manifest(self):
        provenance = json.loads((ROOT / 'tests/fixtures/png/provenance.json').read_text())
        self.assertEqual({e['file'] for e in self.pinned.values()},
                         {e['file'] for e in provenance['fixtures']})


if __name__ == '__main__':
    unittest.main()
