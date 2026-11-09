"""Toy structural byte tests, not valid image fixtures or codec tests."""
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/reference'))
import bmff_structure as probe


def box(kind, payload=b''):
    return (8 + len(payload)).to_bytes(4, 'big') + kind + payload


class BmffStructureTests(unittest.TestCase):
    def test_flat_ranges_and_ftyp_brands(self):
        # Fabricated fourcc values are only structural test data.
        data = box(b'ftyp', b'abcd' + b'\x00\x00\x00\x02' + b'efghijkl') + box(b'free', b'X')
        one, two = probe.boxes(data)
        self.assertEqual((0, 24, 8), (one.start, one.size, one.header_size))
        self.assertEqual((24, 33), (two.start, two.end))
        self.assertEqual({'majorBrand': b'abcd', 'minorVersion': 2,
                          'compatibleBrands': [b'efgh', b'ijkl']}, probe.ftyp(data, one))
        self.assertEqual([two], probe.boxes(data, 24, len(data)))
        self.assertEqual([], probe.boxes(data, len(data), len(data)))

    def test_extended_zero_and_uuid_headers(self):
        ext = b'\0\0\0\x01wide' + (19).to_bytes(8, 'big') + b'abc'
        uuid = (26).to_bytes(4, 'big') + b'uuid' + bytes(range(16)) + b'XY'
        final = b'\0\0\0\0last' + b'z'
        parsed = probe.boxes(ext + uuid + final)
        self.assertEqual([16, 24, 8], [b.header_size for b in parsed])
        self.assertEqual(bytes(range(16)), parsed[1].user_type)
        self.assertEqual(9, parsed[2].size)
        self.assertEqual(len(ext + uuid + final), parsed[-1].end)

    def test_malformed_and_bounds_fail_closed(self):
        malformed = [b'\0' * 7, b'\0\0\0\x07free', b'\0\0\0\x09free',
                     b'\0\0\0\x01wide', b'\0\0\0\x01wide' + (8).to_bytes(8, 'big'),
                     b'\0\0\0\x08uuid', b'\0\0\0\x18uuid' + b'\0' * 15]
        for data in malformed:
            with self.subTest(data=data), self.assertRaises(probe.BoxError):
                probe.boxes(data)
        for start, end in ((-1, 0), (2, 1), (0, 10)):
            with self.assertRaises(probe.BoxError):
                probe.boxes(b'123', start, end)
        with self.assertRaises(probe.BoxError):
            probe.boxes(b'\0' * (probe.MAX_INPUT_BYTES + 1))
        with self.assertRaises(probe.BoxError):
            probe.boxes(box(b'free') * (probe.MAX_BOXES + 1))

    def test_ftyp_rejects_wrong_kind_and_bad_payload(self):
        data = box(b'free') + box(b'ftyp', b'bad') + box(b'ftyp', b'12345678x')
        items = probe.boxes(data)
        for item in items:
            with self.assertRaises(probe.BoxError):
                probe.ftyp(data, item)
        with self.assertRaises(probe.BoxError):
            probe.ftyp(data, probe.Box(b'ftyp', 0, len(data) + 1, 8))

    def test_jxl_signature_does_not_claim_format_validity(self):
        self.assertEqual('container-signature', probe.jxl_signature(b'\0\0\0\x0cJXL \r\n\x87\n'))
        self.assertEqual('codestream-signature', probe.jxl_signature(b'\xff\x0a'))
        for value in (b'', b'\xff', b'\0\0\0\x0cJXL ', b'no format'):
            self.assertEqual('unknown', probe.jxl_signature(value))


if __name__ == '__main__':
    unittest.main()
