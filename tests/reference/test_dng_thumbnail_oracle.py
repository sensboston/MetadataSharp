"""Bounded thumbnail extractor tests."""
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/reference'))
import dng_census_fixtures as fixtures
import dng_thumbnail_oracle as oracle


class DngThumbnailOracleTests(unittest.TestCase):
    def test_ifd1_is_separate_from_preview(self):
        for order in ('II', 'MM'):
            for shape in ('raw', 'thumb'):
                with self.subTest(order=order, shape=shape):
                    self.assertIsNone(oracle.ifd1_jpeg(fixtures.build(order, shape, 'present')))
                    data = fixtures.build(order, shape, 'present', ifd1_jpeg=True)
                    self.assertEqual(fixtures.jpeg.thumb_jpeg_bytes(), oracle.ifd1_jpeg(data))
                    with self.assertRaises(oracle.ThumbnailOracleError):
                        oracle.ifd1_jpeg(data[:-1])

    def test_bad_header(self):
        for data in (b'', b'II\x2a\x00', b'XX' + b'\0' * 20):
            with self.assertRaises(oracle.ThumbnailOracleError):
                oracle.ifd1_jpeg(data)


if __name__ == '__main__':
    unittest.main()
