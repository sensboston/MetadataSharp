import importlib.util
import json
import sys
import tempfile
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/reference'))
spec = importlib.util.spec_from_file_location('owned_webp_fixtures', ROOT / 'tools/reference/webp_owned_fixtures.py')
owned = importlib.util.module_from_spec(spec); spec.loader.exec_module(owned)
import observe_b3  # noqa: E402
import jpeg_owned_fixtures as jpegs  # noqa: E402

MANIFEST = Path(ROOT / 'tests/fixtures/webp/census-manifest.json')


class OwnedWebpFixtureTests(unittest.TestCase):
    def setUp(self):
        self.pinned = {e['file']: e for e in json.loads(MANIFEST.read_text())}

    def test_manifest_pins_every_fixture(self):
        self.assertEqual(12, len(self.pinned))
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

    def test_every_fixture_is_a_well_formed_webp(self):
        for name in self.pinned:
            data = owned.fixture_bytes(name)
            chunks = observe_b3.webp_chunks(data)  # raises on structural problems
            fourccs = [cid for cid, _ in chunks]
            self.assertIn(b'VP8 ', fourccs, name)
            self.assertIn(b'VP8X', fourccs, name)

    def test_vp8_payload_is_the_documented_fake_keyframe(self):
        for name in self.pinned:
            data = owned.fixture_bytes(name)
            streams = observe_b3.scan_payloads(data)
            self.assertEqual(owned.VP8_PAYLOAD, streams, name)

    def test_exif_fixture_carries_orientation_and_artist(self):
        blocks = observe_b3.exif_blocks(owned.fixture_bytes('webp-exif.webp'))
        self.assertEqual(1, len(blocks))
        tags = blocks[0][0]['tags']
        self.assertEqual(b'\x01\x00', tags[274][0][2])           # Orientation SHORT 1
        self.assertEqual(b'Alice\x00', tags[315][0][2])           # Artist ASCII

    def test_exif_dup_fixture_carries_two_independent_chunks(self):
        blocks = observe_b3.exif_blocks(owned.fixture_bytes('webp-exif-dup.webp'))
        self.assertEqual(2, len(blocks))

    def test_exif_thumbnail_fixtures_chain_ifd1(self):
        for name in ('webp-exif-thumb-jpeg.webp', 'webp-exif-thumb-raw.webp'):
            blocks = observe_b3.exif_blocks(owned.fixture_bytes(name))
            self.assertEqual(1, len(blocks), name)  # single EXIF chunk
            ifds = blocks[0]
            self.assertEqual(2, len(ifds), name)  # IFD0 -> IFD1 chain
            self.assertIn(0x0103, ifds[1]['tags'], name)  # Compression

    def test_iccp_fixtures(self):
        profiles = observe_b3.icc_profiles(owned.fixture_bytes('webp-iccp.webp'))
        self.assertEqual([jpegs.PROFILE_A], profiles)
        dup_profiles = observe_b3.icc_profiles(owned.fixture_bytes('webp-iccp-dup.webp'))
        self.assertEqual([jpegs.PROFILE_A, jpegs.PROFILE_A], dup_profiles)

    def test_iptc_and_comments_carriers_are_recorded_never_asserted(self):
        iptc = owned.fixture_bytes('webp-iptc.webp')
        com = owned.fixture_bytes('webp-comments.webp')
        self.assertIn(b'IPTC', [cid for cid, _ in observe_b3.webp_chunks(iptc)])
        self.assertIn(b'COM ', [cid for cid, _ in observe_b3.webp_chunks(com)])

    def test_xmp_carriers(self):
        packets = observe_b3.xmp_packets(owned.fixture_bytes('webp-xmp.webp'))
        self.assertEqual(1, len(packets))
        self.assertIn(b'MetadataSharp</xmp:CreatorTool>', packets[0])
        dup = observe_b3.xmp_packets(owned.fixture_bytes('webp-xmp-dup.webp'))
        self.assertEqual(2, len(dup))
        self.assertIn(b'MetadataSharpA</xmp:CreatorTool>', dup[0])
        self.assertIn(b'MetadataSharpB</xmp:CreatorTool>', dup[1])

    def test_scan_invariant_holds_across_fixtures(self):
        for name in self.pinned:
            data = owned.fixture_bytes(name)
            self.assertEqual(observe_b3.scan_payloads(data), observe_b3.scan_payloads(data), name)

    def test_provenance_matches_census_manifest(self):
        provenance = json.loads((ROOT / 'tests/fixtures/webp/census-provenance.json').read_text())
        self.assertEqual({e['file'] for e in self.pinned.values()},
                         {e['file'] for e in provenance['fixtures']})

    def test_audit_provenance_is_untouched(self):
        # The .NET library's PNG/WebP-group audit owns tests/fixtures/webp/provenance.json;
        # the E01 census batch must never shadow it.
        audit = json.loads((ROOT / 'tests/fixtures/webp/provenance.json').read_text())
        self.assertIn('Pillow', audit['encoder'])
        self.assertNotIn('webp-base.webp', json.dumps(audit))


if __name__ == '__main__':
    unittest.main()
