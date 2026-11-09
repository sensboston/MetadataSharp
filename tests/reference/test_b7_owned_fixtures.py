"""Pinned byte/structure checks; not an independent image decoder."""
import hashlib
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/reference'))
import b7_owned_fixtures as owned
import bmff_structure as bmff


class B7OwnedFixtureTests(unittest.TestCase):
    def test_manifest_hashes_and_declared_scope(self):
        manifest = owned.verify()
        self.assertEqual({'AVIF', 'HEIF', 'HEIC', 'JXL'}, {v['format'] for v in manifest})
        self.assertTrue(all(v['rights'] == 'owned-synthetic' for v in manifest))
        self.assertEqual(192, len(owned.pixel_data(23)))
        self.assertNotEqual(hashlib.sha256(owned.pixel_data(23)).digest(),
                            hashlib.sha256(owned.pixel_data(29)).digest())

    def test_structural_signatures_and_metadata_bytes(self):
        root = owned.FIXTURES
        for name in ('owned.avif', 'owned.heif', 'owned.heic'):
            with self.subTest(name=name):
                raw = (root / name).read_bytes()
                boxes = bmff.boxes(raw)
                self.assertEqual(b'ftyp', boxes[0].kind)
                self.assertEqual({b'ftyp', b'meta', b'mdat'}, {b.kind for b in boxes})
                self.assertIn(b'B7Artist', raw)
                self.assertIn(b'B7Tool', raw)
        self.assertEqual('codestream-signature', bmff.jxl_signature((root / 'owned-codestream.jxl').read_bytes()))
        container = (root / 'owned-container.jxl').read_bytes()
        self.assertEqual('container-signature', bmff.jxl_signature(container))
        self.assertTrue({b'jxlc', b'Exif', b'xml '} <= {b.kind for b in bmff.boxes(container)})
        self.assertIn(b'B7Artist', container)
        self.assertIn(b'B7Tool', container)

    def test_verify_fails_on_tampered_file(self):
        import tempfile
        import shutil
        with tempfile.TemporaryDirectory() as temp:
            shutil.copytree(owned.FIXTURES, Path(temp) / 'fixtures')
            path = Path(temp) / 'fixtures' / 'owned.avif'
            path.write_bytes(path.read_bytes() + b'!')
            with self.assertRaises(ValueError):
                owned.verify(path.parent)


if __name__ == '__main__':
    unittest.main()
