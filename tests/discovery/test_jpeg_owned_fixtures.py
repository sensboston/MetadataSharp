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
        self.assertEqual(31, len(self.pinned))
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

    def test_xmp_fixtures(self):
        std = owned.fixture_bytes('xmp-standard.jpg')
        xmp = [p for m, p in observe_b0.jpeg_segments(std) if m == 0xE1]
        self.assertEqual(1, len(xmp))
        self.assertTrue(xmp[0].startswith(owned.XMP_SIGNATURE))
        self.assertIn(b'MetadataSharp</xmp:CreatorTool>', xmp[0])

        none = owned.fixture_bytes('xmp-none.jpg')
        self.assertEqual([], [p for m, p in observe_b0.jpeg_segments(none) if m == 0xE1])

        dup = owned.fixture_bytes('xmp-duplicate.jpg')
        segs = [p for m, p in observe_b0.jpeg_segments(dup) if m == 0xE1]
        self.assertEqual(2, len(segs))
        self.assertIn(b'MetadataSharpA</xmp:CreatorTool>', segs[0])
        self.assertIn(b'MetadataSharpB</xmp:CreatorTool>', segs[1])

        nonutf8 = owned.fixture_bytes('xmp-nonutf8.jpg')
        seg = [p for m, p in observe_b0.jpeg_segments(nonutf8) if m == 0xE1][0]
        self.assertTrue(seg.startswith(owned.XMP_SIGNATURE))
        self.assertFalse(seg[len(owned.XMP_SIGNATURE):].startswith(b'<?xpacket'))

    def test_xmp_extended_fixtures_reassemble(self):
        data = owned.fixture_bytes('xmp-extended.jpg')
        segs = [p for m, p in observe_b0.jpeg_segments(data) if m == 0xE1]
        base = [p for p in segs if p.startswith(owned.XMP_SIGNATURE)]
        chunks = [p for p in segs if p.startswith(owned.XMP_EXT_SIGNATURE)]
        self.assertEqual(1, len(base))
        self.assertIn(owned.XMP_EXT_GUID, base[0])
        self.assertEqual(2, len(chunks))
        full = int.from_bytes(chunks[0][len(owned.XMP_EXT_SIGNATURE) + 32:len(owned.XMP_EXT_SIGNATURE) + 36], 'big')
        pieces = {}
        for chunk in chunks:
            rest = chunk[len(owned.XMP_EXT_SIGNATURE):]
            self.assertEqual(owned.XMP_EXT_GUID, rest[:32])
            size, offset = int.from_bytes(rest[32:36], 'big'), int.from_bytes(rest[36:40], 'big')
            self.assertEqual(full, size)
            pieces[offset] = rest[40:]
        self.assertEqual(owned.xmp_extended_doc(), b''.join(pieces[off] for off in sorted(pieces)))

        dup = owned.fixture_bytes('xmp-extended-duplicate.jpg')
        dup_segs = [p for m, p in observe_b0.jpeg_segments(dup) if m == 0xE1]
        self.assertEqual(2, sum(p.startswith(owned.XMP_SIGNATURE) for p in dup_segs))
        self.assertEqual(2, sum(p.startswith(owned.XMP_EXT_SIGNATURE) for p in dup_segs))

        nonutf8 = owned.fixture_bytes('xmp-extended-nonutf8.jpg')
        ext_segs = [p for m, p in observe_b0.jpeg_segments(nonutf8) if m == 0xE1]
        self.assertEqual(1, sum(p.startswith(owned.XMP_SIGNATURE) for p in ext_segs))
        self.assertEqual(2, sum(p.startswith(owned.XMP_EXT_SIGNATURE) for p in ext_segs))

    def test_iptc_fixtures(self):
        def keywords(name):
            return observe_b0.iptc_keywords(owned.fixture_bytes(name))

        self.assertEqual([], keywords('iptc-none.jpg'))
        self.assertEqual([b'MetadataSharpAlpha', b'MetadataSharpBeta'], keywords('iptc-single.jpg'))
        self.assertEqual([b'MetadataSharpDup', b'MetadataSharpDup'], keywords('iptc-single-dup-dataset.jpg'))
        self.assertEqual([b'MetadataSharpAlpha', b'MetadataSharpBeta'], keywords('iptc-multiple.jpg'))
        self.assertEqual([b'MetadataSharpDup', b'MetadataSharpDup'], keywords('iptc-multiple-dup.jpg'))

        single = owned.fixture_bytes('iptc-single.jpg')
        app13 = [p for m, p in observe_b0.jpeg_segments(single) if m == 0xED]
        self.assertEqual(1, len(app13))
        blocks = observe_b0.bim_blocks(app13[0][len(observe_b0.APP13_SIGNATURE):])
        self.assertEqual(1, len(blocks))
        self.assertEqual(0x0404, blocks[0][0])

        multiple = owned.fixture_bytes('iptc-multiple.jpg')
        app13_multi = [p for m, p in observe_b0.jpeg_segments(multiple) if m == 0xED]
        self.assertEqual(2, len(app13_multi))

    def test_thumbnail_fixtures(self):
        for name, expect_width in (('exif-thumb-jpeg.jpg', None), ('exif-thumb-raw.jpg', 8)):
            data = owned.fixture_bytes(name)
            exif = [p for m, p in observe_b0.jpeg_segments(data) if m == 0xE1][0]
            blocks = owned.parse_tiff(exif[6:])
            self.assertEqual(2, len(blocks), name)  # IFD0 -> IFD1 chain
            thumb_tags = blocks[1]['tags']
            self.assertIn(0x0103, thumb_tags, name)  # Compression
        jpeg_tiff = [p for m, p in observe_b0.jpeg_segments(
            owned.fixture_bytes('exif-thumb-jpeg.jpg')) if m == 0xE1][0][6:]
        jpeg_tags = owned.parse_tiff(jpeg_tiff)[1]['tags']
        strip_off = int.from_bytes(jpeg_tags[0x0201][0][2], 'little')
        strip_len = int.from_bytes(jpeg_tags[0x0202][0][2], 'little')
        self.assertEqual(owned.thumb_jpeg_bytes(), jpeg_tiff[strip_off:strip_off + strip_len])
        raw_tiff = [p for m, p in observe_b0.jpeg_segments(
            owned.fixture_bytes('exif-thumb-raw.jpg')) if m == 0xE1][0][6:]
        raw_tags = owned.parse_tiff(raw_tiff)[1]['tags']
        raw_off = int.from_bytes(raw_tags[0x0111][0][2], 'little')
        raw_len = int.from_bytes(raw_tags[0x0117][0][2], 'little')
        self.assertEqual(192, raw_len)
        self.assertEqual(192, len(raw_tiff[raw_off:raw_off + raw_len]))

    def test_com_raw_fixtures(self):
        payloads = {name: [p for m, p in observe_b0.jpeg_segments(owned.fixture_bytes(name)) if m == 0xFE]
                    for name in ('com-raw.jpg', 'com-raw-multiple.jpg', 'com-raw-multiple-dup.jpg')}
        self.assertEqual([owned.RAW_COM_A], payloads['com-raw.jpg'])
        self.assertEqual([owned.RAW_COM_A, owned.RAW_COM_B], payloads['com-raw-multiple.jpg'])
        self.assertEqual([owned.RAW_COM_A, owned.RAW_COM_A], payloads['com-raw-multiple-dup.jpg'])
        with self.assertRaises(UnicodeDecodeError):
            owned.RAW_COM_A.decode('utf-8')

    def test_provenance_matches_manifest(self):
        provenance = json.loads((ROOT / 'tests/fixtures/jpeg/provenance.json').read_text())
        self.assertEqual({e['file'] for e in self.pinned.values()},
                         {e['file'] for e in provenance['fixtures']})


if __name__ == '__main__':
    unittest.main()
