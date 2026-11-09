"""Offline invariants for B6 owned DNG fixture catalog."""
import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/reference'))
import dng_census_fixtures as fixtures
import dng_fixtures as d1
import dng_raw_oracle as oracle


def root_fields(data):
    byteorder = 'little' if data[:2] == b'II' else 'big'
    def n(pos, size):
        return int.from_bytes(data[pos:pos + size], byteorder)
    pos = n(4, 4)
    result = {}
    for i in range(n(pos, 2)):
        at = pos + 2 + i * 12
        tag, typ, count = n(at, 2), n(at + 2, 2), n(at + 4, 4)
        if tag in (315, 700, 33723, 34675, 40092):
            assert typ in (1, 2)
            start = at + 8 if count <= 4 else n(at + 8, 4)
            assert start + count <= len(data)
            result[tag] = data[start:start + count]
    return result


class DngCensusFixtureTests(unittest.TestCase):
    def test_manifest_and_deterministic_output(self):
        fixtures.verify()
        self.assertEqual(12, len(fixtures.manifest()))
        with tempfile.TemporaryDirectory() as tmp:
            fixtures.generate(Path(tmp))
            self.assertEqual(fixtures.manifest(), json.loads((Path(tmp) / 'census-manifest.json').read_text()))
            for name, data, entry in fixtures.catalog():
                self.assertEqual(data, (Path(tmp) / name).read_bytes())
                self.assertEqual(entry['sha256'], hashlib.sha256(data).hexdigest())

    def test_root_metadata_preconditions_and_d1_raw_bytes(self):
        expected = {315: b'B6Alice\0', 700: fixtures.tiff.XMP_PKT_A,
                    33723: fixtures.tiff.IIM_PAYLOAD, 34675: fixtures.jpeg.PROFILE_A,
                    40092: 'B6 comment'.encode('utf-16-le') + b'\0\0'}
        for order in ('II', 'MM'):
            for shape in ('raw', 'thumb'):
                original, _ = d1.build(order, shape)
                for state in ('empty', 'present'):
                    with self.subTest(order=order, shape=shape, state=state):
                        data = fixtures.build(order, shape, state)
                        self.assertEqual([] if state == 'empty' else sorted(expected), sorted(root_fields(data)))
                        if state == 'present':
                            self.assertEqual(expected, root_fields(data))
                        self.assertEqual(oracle.raw_strip_fingerprints(original), oracle.raw_strip_fingerprints(data))

    def test_ifd1_jpeg_bytes_and_raw_preserved(self):
        for order in ('II', 'MM'):
            endian = 'little' if order == 'II' else 'big'
            for shape in ('raw', 'thumb'):
                data = fixtures.build(order, shape, 'present', ifd1_jpeg=True)
                baseline = fixtures.build(order, shape, 'present')
                self.assertEqual(oracle.raw_strip_fingerprints(baseline), oracle.raw_strip_fingerprints(data))
                count = int.from_bytes(data[8:10], endian)
                ifd1 = int.from_bytes(data[10 + 12 * count:14 + 12 * count], endian)
                n = int.from_bytes(data[ifd1:ifd1 + 2], endian)
                tags = {}
                for i in range(n):
                    at = ifd1 + 2 + 12 * i
                    tag = int.from_bytes(data[at:at + 2], endian)
                    tags[tag] = int.from_bytes(data[at + 8:at + 12], endian)
                self.assertEqual(fixtures.jpeg.thumb_jpeg_bytes(), data[tags[513]:tags[513] + tags[514]])

    def test_rejects_bad_variants(self):
        for values in (('xx', 'raw', 'empty'), ('II', 'other', 'empty'), ('II', 'raw', 'other')):
            with self.assertRaises(ValueError):
                fixtures.build(*values)


if __name__ == '__main__':
    unittest.main()
