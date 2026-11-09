"""Independent bounded JP2 fixture checks; not a JPEG 2000 validator."""
import hashlib
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/reference'))
import b8_jp2_fixture as fixture


def boxes(data):
    result, at = [], 0
    while at < len(data):
        if len(data) - at < 8:
            raise ValueError('truncated box')
        size = int.from_bytes(data[at:at + 4], 'big')
        if size < 8 or size > len(data) - at:
            raise ValueError('invalid size')
        result.append((data[at + 4:at + 8], data[at + 8:at + size]))
        at += size
    return result


class Jp2FixtureTests(unittest.TestCase):
    def test_independent_framing(self):
        data = (fixture.DIRECTORY / fixture.FILE).read_bytes()
        self.assertEqual('5590ea8a66fe8729c6cf986f61a8cb52ee537d5dadc63b96ba8aa2ce0099ee6f', hashlib.sha256(data).hexdigest())
        parts = boxes(data)
        self.assertEqual([b'jP  ', b'ftyp', b'jp2h', b'jp2c'], [p[0] for p in parts])
        self.assertEqual(b'\r\n\x87\n', parts[0][1])
        self.assertEqual(b'jp2 \x00\x00\x00\x00jp2 ', parts[1][1])
        header = boxes(parts[2][1])
        self.assertEqual([b'ihdr', b'colr'], [p[0] for p in header])
        self.assertEqual((8, 8), (int.from_bytes(header[0][1][0:4], 'big'), int.from_bytes(header[0][1][4:8], 'big')))
        self.assertEqual(b'\xff\x4f', parts[3][1][:2])
        fixture.verify()

    def test_modified_fixture_is_rejected(self):
        with tempfile.TemporaryDirectory() as tmp, patch.object(fixture, 'DIRECTORY', Path(tmp)):
            original = Path(__file__).resolve().parents[1] / 'fixtures/b8'
            (Path(tmp) / 'jp2-manifest.json').write_bytes((original / 'jp2-manifest.json').read_bytes())
            (Path(tmp) / fixture.FILE).write_bytes((original / fixture.FILE).read_bytes()[:-1])
            with self.assertRaises(ValueError):
                fixture.verify()


if __name__ == '__main__':
    unittest.main()
