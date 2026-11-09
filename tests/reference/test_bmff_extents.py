"""Generic extent arithmetic tests; no image format fixture is asserted."""
import hashlib
import sys
import unittest
from unittest.mock import patch
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/reference'))
import bmff_extents as extent


class BmffExtentTests(unittest.TestCase):
    def test_split_ranges_order_and_base_offsets(self):
        result = extent.inspect_extents(b'abcdefghij', [(2, 1, 3), (0, 7, 2)])
        self.assertEqual([3, 7], [f['start'] for f in result['extents']])
        self.assertEqual([hashlib.sha256(b'def').hexdigest(), hashlib.sha256(b'hi').hexdigest()],
                         [f['sha256'] for f in result['extents']])
        self.assertEqual([], result['overlaps'])

    def test_overlap_duplicate_and_empty_ranges_are_not_hidden(self):
        result = extent.inspect_extents(b'abcdef', [(0, 1, 3), (0, 3, 2), (0, 1, 3), (6, 0, 0)])
        self.assertEqual([[0, 1], [0, 2], [1, 2]], result['overlaps'])
        self.assertEqual(hashlib.sha256(b'').hexdigest(), result['extents'][3]['sha256'])

    def test_invalid_values_and_limits_fail(self):
        for ranges in ([(-1, 0, 0)], [(0, 0, -1)], [(0, 9, 0)], [(0, 5, 4)],
                       [(True, 0, 1)], [(0, 1.0, 1)], [(0, 1)], [(2**64, 0, 1)]):
            with self.subTest(ranges=ranges), self.assertRaises(extent.ExtentError):
                extent.inspect_extents(b'12345678', ranges)
        with patch.object(extent, 'MAX_TOTAL_EXTENT_BYTES', 2):
            with self.assertRaises(extent.ExtentError):
                extent.inspect_extents(b'abc', [(0, 0, 2), (0, 2, 1)])
        with self.assertRaises(extent.ExtentError):
            extent.inspect_extents(b'a', [(0, 0, 0)] * (extent.MAX_EXTENTS + 1))
        with self.assertRaises(extent.ExtentError):
            extent.inspect_extents(b'a', [(0, 0, 1)] * 100)
        with self.assertRaises(extent.ExtentError):
            extent.inspect_extents(b'a' * (extent.MAX_INPUT_BYTES + 1), [])


if __name__ == '__main__':
    unittest.main()
