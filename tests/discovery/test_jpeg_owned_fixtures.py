import importlib.util
import json
import tempfile
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
import sys
sys.path.insert(0, str(ROOT / 'tools/reference'))
spec = importlib.util.spec_from_file_location('owned_fixtures', ROOT / 'tools/reference/jpeg_owned_fixtures.py')
owned = importlib.util.module_from_spec(spec); spec.loader.exec_module(owned)
import observe_b0  # noqa: E402

MANIFEST = Path(ROOT / 'tests/fixtures/jpeg/manifest.json')


class OwnedJpegFixtureTests(unittest.TestCase):
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
                import hashlib
                self.assertEqual(entry['sha256'], hashlib.sha256(data).hexdigest())
                self.assertEqual(entry['length'], len(data))

    def test_segments_are_well_formed_and_scan_is_last(self):
        for name in self.pinned:
            data = owned.fixture_bytes(name)
            self.assertEqual(b'\xff\xd8', data[:2], name)
            self.assertEqual(b'\xff\xd9', data[-2:], name)
            pos, saw_scan = 2, False
            while pos < len(data) - 2:
                self.assertEqual(0xFF, data[pos], name)
                marker = data[pos + 1]
                if marker == 0xDA:
                    saw_scan = True
                    break
                size = int.from_bytes(data[pos + 2:pos + 4], 'big')
                self.assertGreaterEqual(size, 2, name)
                pos += 2 + size
            self.assertTrue(saw_scan, name)

    def test_exif_base_carries_orientation_and_artist(self):
        data = owned.fixture_bytes('exif-base.jpg')
        exif = [p for m, p in observe_b0.jpeg_segments(data) if m == 0xE1]
        blocks = owned.parse_tiff(exif[0][6:])
        self.assertEqual(1, len(blocks))
        tags = blocks[0]['tags']
        self.assertEqual(b'\x01\x00', tags[274][0][2])           # Orientation SHORT 1
        self.assertEqual(b'Alice\x00', tags[315][0][2])           # Artist ASCII

    def test_exif_dup_tag_has_two_artist_entries_in_authored_order(self):
        exif = [p for m, p in observe_b0.jpeg_segments(owned.fixture_bytes('exif-dup-tag.jpg')) if m == 0xE1]
        blocks = owned.parse_tiff(exif[0][6:])
        values = [v[2] for v in blocks[0]['tags'][315]]
        self.assertEqual([b'Alice\x00', b'Bob\x00'], values)

    def test_subifd_fixture_pointer_resolves_to_subifd_body(self):
        exif = [p for m, p in observe_b0.jpeg_segments(owned.fixture_bytes('exif-subifd.jpg')) if m == 0xE1]
        blocks = owned.parse_tiff(exif[0][6:])
        self.assertEqual(2, len(blocks))
        pointer = blocks[0]['tags'][0x8769][0][2]
        self.assertEqual(blocks[1]['body'][0], int.from_bytes(pointer, 'little'))
        self.assertEqual(200, int.from_bytes(blocks[1]['tags'][34855][0][2], 'little'))

    def test_multi_app1_blocks_carry_alice_then_bob(self):
        exif = [p for m, p in observe_b0.jpeg_segments(owned.fixture_bytes('exif-multi-app1.jpg')) if m == 0xE1]
        blocks = [owned.parse_tiff(p[6:]) for p in exif]
        self.assertEqual(2, len(blocks))
        self.assertEqual(b'Alice\x00', blocks[0][0]['tags'][315][0][2])
        self.assertEqual(b'Bob\x00', blocks[1][0]['tags'][315][0][2])

    def test_comment_payloads_match_recipes(self):
        payloads = {name: [p for m, p in observe_b0.jpeg_segments(owned.fixture_bytes(name)) if m == 0xFE]
                    for name in ('com-none.jpg', 'com-single.jpg', 'com-multiple.jpg', 'com-multiple-dup.jpg')}
        self.assertEqual([], payloads['com-none.jpg'])
        self.assertEqual([owned.COMMENT_A], payloads['com-single.jpg'])
        self.assertEqual([owned.COMMENT_A, owned.COMMENT_B], payloads['com-multiple.jpg'])
        self.assertEqual([owned.COMMENT_A, owned.COMMENT_A], payloads['com-multiple-dup.jpg'])

    def test_icc_profile_has_acsp_signature_and_reassembles(self):
        a, b = owned.PROFILE_A, owned.PROFILE_B
        self.assertEqual(b'acsp', a[36:40])
        self.assertEqual(b'acsp', b[36:40])
        self.assertEqual(len(a), int.from_bytes(a[0:4], 'big'))
        self.assertEqual(len(b), int.from_bytes(b[0:4], 'big'))
        chunks = {name: [p for m, p in observe_b0.jpeg_segments(owned.fixture_bytes(name)) if m == 0xE2]
                  for name in ('icc-segmented.jpg', 'icc-segmented-dup.jpg')}

        def reassemble(payloads):
            return b''.join(p[len(owned.ICC_SIGNATURE) + 2:] for p in payloads)
        self.assertEqual(a, reassemble(chunks['icc-segmented.jpg']))
        self.assertEqual(a + b, reassemble(chunks['icc-segmented-dup.jpg']))

    def test_provenance_matches_manifest(self):
        provenance = json.loads((ROOT / 'tests/fixtures/jpeg/provenance.json').read_text())
        self.assertEqual({e['file'] for e in self.pinned.values()},
                         {e['file'] for e in provenance['fixtures']})


if __name__ == '__main__':
    unittest.main()
