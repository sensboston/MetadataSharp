"""IFD0 byte extraction and malformed input checks for B6."""
import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/reference'))
import dng_census_fixtures as fixtures
import dng_metadata_oracle as oracle


class DngMetadataOracleTests(unittest.TestCase):
    def test_expected_carriers_both_byte_orders_and_shapes(self):
        for order in ('II', 'MM'):
            for shape in ('raw', 'thumb'):
                with self.subTest(order=order, shape=shape):
                    empty = fixtures.build(order, shape, 'empty')
                    present = fixtures.build(order, shape, 'present')
                    tags = {315, 700, 33723, 34675, 40092}
                    self.assertEqual([], oracle.root_values(empty, tags))
                    records = oracle.root_values(present, tags)
                    self.assertEqual(sorted(tags), [v['tag'] for v in records])
                    self.assertEqual(b'B6Alice\0', records[0]['bytes'])
                    self.assertEqual(fixtures.jpeg.PROFILE_A,
                                     next(v['bytes'] for v in records if v['tag'] == 34675))

    def test_invalid_header_and_payload_fail_closed(self):
        data = fixtures.build('II', 'raw', 'present')
        for value in (b'', b'II*\x00', b'MM' + data[2:]):
            with self.assertRaises(oracle.MetadataOracleError):
                oracle.root_values(value, {315})
        mutable = bytearray(data)
        count = struct.unpack_from('<H', mutable, 8)[0]
        artist_entry = next(10 + i * 12 for i in range(count)
                            if struct.unpack_from('<H', mutable, 10 + i * 12)[0] == 315)
        struct.pack_into('<I', mutable, artist_entry + 8, len(data))
        with self.assertRaises(oracle.MetadataOracleError):
            oracle.root_values(mutable, {315})
        struct.pack_into('<H', mutable, artist_entry + 2, 99)
        with self.assertRaises(oracle.MetadataOracleError):
            oracle.root_values(mutable, {315})

    def test_duplicate_tag_is_preserved(self):
        # Minimal classic TIFF with two inline BYTE tag 700 records.
        data = (b'II\x2a\x00\x08\x00\x00\x00' + struct.pack('<H', 2) +
                struct.pack('<HHI4s', 700, 1, 1, b'A\0\0\0') +
                struct.pack('<HHI4s', 700, 1, 1, b'B\0\0\0') + b'\0' * 4)
        self.assertEqual([b'A', b'B'], [v['bytes'] for v in oracle.root_values(data, {700})])


if __name__ == '__main__':
    unittest.main()
