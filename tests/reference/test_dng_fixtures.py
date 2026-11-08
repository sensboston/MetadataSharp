"""Offline checks for the authored DNG fixture generator.

These tests do not decode or render DNG; they pin the byte layout the C# tests
also assert, and they keep the Python and C# writers byte-identical via CI.
"""
import hashlib
import importlib.util
import struct
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location(
    "dng_fixtures", Path(__file__).parents[2] / "tools" / "reference" / "dng_fixtures.py")
g = importlib.util.module_from_spec(spec)
spec.loader.exec_module(g)

PINNED = {
    ("II", "raw"): (598, "c4e2893a31565c4b53a1fa26686288bdce9421252bbd0c920b9ee5eeabc9701c", 582, None, 8, 8),
    ("II", "thumb"): (1040, "8117e8756c4d69c1519a5201259d952e5777d332ffa1e37150444e2e83f637f0", 1024, 426, 450, 8),
    ("MM", "raw"): (598, "5c1ade1005f83ed9ac1d4ff19c3c06972b1964989b85ba2523312620d1dc24b4", 582, None, 8, 8),
    ("MM", "thumb"): (1040, "7ba9311517d1c1751c3061b053b8ab464a4e1fde56d8765a27e00dc1ada4b799", 1024, 426, 450, 8),
}
RAW_PIXEL_SHA256 = {
    "II": "067436bafb67573443706a095729cfe4b4d0427dd1c25d165777df8ff41780b9",
    "MM": "6472f018a621000b17e1b6c90e5555b2f11ed33f71d113a55af0c1c85e74716c",
}


class DngFixtureTests(unittest.TestCase):
    def test_deterministic_and_byte_identical_across_calls(self):
        for (order, shape) in PINNED:
            first, first_info = g.build(order, shape)
            second, second_info = g.build(order, shape)
            self.assertEqual(first, second)
            self.assertEqual(first_info, second_info)

    def test_layout_and_sha256_pinned(self):
        for (order, shape), (length, sha, raw_strip, thumb_strip, raw_ifd, ifd0) in PINNED.items():
            data, info = g.build(order, shape)
            self.assertEqual(length, len(data))
            self.assertEqual(sha, hashlib.sha256(data).hexdigest())
            self.assertEqual(ifd0, info["ifd0"])
            self.assertEqual(raw_strip, info["raw_strip"])
            self.assertEqual(thumb_strip, info["thumb_strip"])
            self.assertEqual(raw_ifd, info["raw_ifd"])
            self.assertEqual(16, info["raw_strip_length"])

    def test_structure_invariants(self):
        for (order, shape) in PINNED:
            data, _ = g.build(order, shape)
            little = order == "II"
            fmt = "<" if little else ">"
            u16 = lambda at: int.from_bytes(data[at:at + 2], "little" if little else "big")
            u32 = lambda at: int.from_bytes(data[at:at + 4], "little" if little else "big")
            self.assertEqual((b"II", b"MM")[not little], data[:2])
            self.assertEqual(42, u16(2))
            self.assertEqual(8, u32(4))
            count = u16(8)
            self.assertGreaterEqual(count, 15)
            tags = [u16(10 + 12 * i) for i in range(count)]
            self.assertEqual(sorted(tags), tags)
            self.assertEqual(len(set(tags)), len(tags))

    def test_raw_strip_payload_hash(self):
        for (order, shape) in PINNED:
            data, info = g.build(order, shape)
            strip = data[info["raw_strip"]:info["raw_strip"] + info["raw_strip_length"]]
            self.assertEqual(RAW_PIXEL_SHA256[order], hashlib.sha256(strip).hexdigest())

    def test_dng_version_is_1_1_0_0_on_the_raw_leaf(self):
        for (order, shape) in PINNED:
            data, info = g.build(order, shape)
            little = order == "II"
            fmt = "<" if little else ">"
            def ifd(offset):
                fields = {}
                count = int.from_bytes(data[offset:offset + 2], "little" if little else "big")
                for i in range(count):
                    at = offset + 2 + 12 * i
                    tag, typ, n = struct.unpack_from(fmt + "HHI", data, at)
                    fields[tag] = (typ, n, at)
                return fields
            leaf = ifd(info["raw_ifd"])
            typ, n, at = leaf[50706]
            self.assertEqual((1, 4), (typ, n))
            loc = at + 8
            self.assertEqual(b"\x01\x01\x00\x00", data[loc:loc + 4])  # inline (count 4, 1 byte each)


if __name__ == "__main__":
    unittest.main()
