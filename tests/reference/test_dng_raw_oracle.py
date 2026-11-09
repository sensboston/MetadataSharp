"""Offline checks for the B6 raw-byte oracle; no raw rendering is implied."""
import hashlib
import importlib.util
import struct
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def load(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'tools/reference' / (name + '.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


g = load('dng_fixtures')
oracle = load('dng_raw_oracle')


class DngRawOracleTests(unittest.TestCase):
    def test_owned_raw_and_subifd_both_byte_orders(self):
        for order in ('II', 'MM'):
            for shape in ('raw', 'thumb'):
                with self.subTest(order=order, shape=shape):
                    data, info = g.build(order, shape)
                    expected = data[info['raw_strip']:info['raw_strip'] + info['raw_strip_length']]
                    self.assertEqual([expected], oracle.raw_strips(data))
                    self.assertEqual([{'length': 16, 'sha256': hashlib.sha256(expected).hexdigest()}],
                                     oracle.raw_strip_fingerprints(data))

    def test_raw_mutation_is_detected_thumbnail_mutation_is_not_raw(self):
        data, info = g.build('II', 'thumb')
        original = oracle.raw_strip_fingerprints(data)
        altered = bytearray(data)
        altered[info['thumb_strip']] ^= 1
        self.assertEqual(original, oracle.raw_strip_fingerprints(altered))
        altered[info['raw_strip']] ^= 1
        self.assertNotEqual(original, oracle.raw_strip_fingerprints(altered))

    def test_malformed_inputs_fail_closed(self):
        data, info = g.build('II', 'raw')
        for bad in (b'', b'II*\x00\x08', data[:-1], b'MM' + data[2:]):
            with self.subTest(length=len(bad)), self.assertRaises(oracle.DngOracleError):
                oracle.raw_strips(bad)
        cycle = bytearray(data)
        count = struct.unpack_from('<H', data, info['ifd0'])[0]
        struct.pack_into('<I', cycle, info['ifd0'] + 2 + count * 12, info['ifd0'])
        with self.assertRaises(oracle.DngOracleError):
            oracle.raw_strips(cycle)

    def test_missing_cfa_and_out_of_bounds_strip_fail_closed(self):
        data, info = g.build('MM', 'raw')
        count = struct.unpack_from('>H', data, info['ifd0'])[0]
        entries = {struct.unpack_from('>H', data, info['ifd0'] + 2 + 12 * i)[0]:
                   info['ifd0'] + 2 + 12 * i for i in range(count)}
        no_cfa = bytearray(data)
        struct.pack_into('>H', no_cfa, entries[262] + 8, 2)
        with self.assertRaises(oracle.DngOracleError):
            oracle.raw_strips(no_cfa)
        bad_range = bytearray(data)
        struct.pack_into('>I', bad_range, entries[273] + 8, len(data))
        with self.assertRaises(oracle.DngOracleError):
            oracle.raw_strips(bad_range)


if __name__ == '__main__':
    unittest.main()
