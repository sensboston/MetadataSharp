"""Offline checks for the authored EPS and PGF fixture generator.

These tests do not decode or render PostScript or PGF; they pin the byte layout the C# tests also
assert, and they keep the Python writer and the manifest byte-identical via CI.
"""
import hashlib
import importlib.util
import json
import struct
import unittest
from pathlib import Path

SPEC = importlib.util.spec_from_file_location(
    "eps_pgf_fixtures", Path(__file__).parents[2] / "tools" / "reference" / "eps_pgf_fixtures.py")
g = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(g)


class EpsPgfFixtureTests(unittest.TestCase):
    def test_deterministic_across_calls(self):
        first = {name: data["bytes"] for name, data in g._build_catalog().items()}
        second = {name: data["bytes"] for name, data in g._build_catalog().items()}
        self.assertEqual(first, second)

    def test_manifest_is_pinned_to_the_bytes_written_today(self):
        pinned = json.loads(g.MANIFEST.read_text(encoding="utf-8"))
        self.assertEqual(pinned, g.manifest(),
                         "the fixture bytes and tests/fixtures/b11eps/manifest.json have drifted")

    def test_every_file_hashes_to_its_manifest_entry(self):
        for entry in g.manifest():
            path = Path(g.MANIFEST).parent / entry["file"]
            self.assertTrue(path.is_file(), entry["file"])
            data = path.read_bytes()
            self.assertEqual(len(data), entry["length"], entry["file"])
            self.assertEqual(hashlib.sha256(data).hexdigest(), entry["sha256"], entry["file"])

    def test_eps_files_begin_with_an_accepted_first_line(self):
        accepted = (g.EPS_FIRST, g.EPS_FIRST_SPACE, g.EPS_FIRST_31)
        for name, entry in g._build_catalog().items():
            if not name.endswith(".eps"):
                continue
            data = entry["bytes"]
            body = data[g.DOS_HEADER_BYTES:] if data.startswith(g.DOS_SIGNATURE) else data
            self.assertTrue(any(body.startswith(line) for line in accepted), name)

    def test_dos_header_places_both_sections_inside_the_file(self):
        data = g.fixture_bytes("dos-eps-xmp.eps")
        self.assertTrue(data.startswith(g.DOS_SIGNATURE))
        self.assertEqual(len(data[:g.DOS_HEADER_BYTES]), 30)
        ps_offset, ps_length, wmf_offset, wmf_length, tiff_offset, tiff_length = struct.unpack("<IIIIII", data[4:28])
        self.assertEqual(ps_offset, 30)
        self.assertEqual(wmf_offset + wmf_length, 0)
        self.assertEqual(tiff_offset, ps_offset + ps_length)
        self.assertEqual(tiff_offset + tiff_length, len(data))

    def test_pgf_header_size_counts_the_structure_and_the_region(self):
        data = g.fixture_bytes("pgf-png.pgf")
        self.assertEqual(data[:3], b"PGF")
        header_size = struct.unpack("<I", data[4:8])[0]
        width, height = struct.unpack("<II", data[8:16])
        # The region begins after the sixteen-byte structure, which this fixture does not enlarge.
        self.assertEqual(header_size, 16 + len(data) - 24)
        self.assertEqual((width, height), (320, 211))
        self.assertEqual(data[24:32], b"\x89PNG\r\n\x1a\n")

    def test_indexed_pgf_puts_its_colour_table_between_structure_and_region(self):
        data = g.fixture_bytes("pgf-colour-table.pgf")
        # The colour mode is the thirteenth byte of the sixteen-byte structure.
        self.assertEqual(data[20], 2)
        header_size = struct.unpack("<I", data[4:8])[0]
        self.assertEqual(header_size, 16 + 768 + len(data) - (24 + 768))
        self.assertEqual(data[24 + 768:24 + 768 + 8], b"\x89PNG\r\n\x1a\n")

    def test_empty_region_pgf_is_the_header_alone(self):
        data = g.fixture_bytes("pgf-empty-region.pgf")
        self.assertEqual(len(data), 24)
        self.assertEqual(struct.unpack("<I", data[4:8])[0], 16)

    def test_xmp_variants_carry_the_authored_packet(self):
        # The file states the trailer once, after the packet, so the packet it holds is the authored one
        # without the trailer that packet itself ends with.
        packet = g.xmp_packet()[:-len(g.PACKET_TRAILER)]
        for name in ("eps-xmp.eps", "eps-xmp-space.eps", "eps-xmp-31.eps", "eps-xmp-crlf.eps", "dos-eps-xmp.eps"):
            self.assertIn(packet, g.fixture_bytes(name), name)
        self.assertNotIn(packet, g.fixture_bytes("eps-plain.eps"))


if __name__ == "__main__":
    unittest.main()
