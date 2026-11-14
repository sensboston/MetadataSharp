from __future__ import annotations

import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools" / "parity"))
import run_tag_print as gate


class TagPrintProtocolTests(unittest.TestCase):
    def test_exiv2_pt_rows_have_key_type_count_and_one_printed_value(self) -> None:
        text = (
            "Exif.Image.NewSubfileType                    Long        1  Thumbnail/Preview image\n"
            "Exif.Image.ImageWidth                        Long        1  160\n"
            "Exif.Image.Make                              Ascii      18  NIKON CORPORATION\n"
            "Iptc.Application2.Caption                     String      1  not part of -pt\n"
        )
        self.assertEqual([
            {"key": "Exif.Image.NewSubfileType", "type": "Long", "count": 1, "value": "Thumbnail/Preview image"},
            {"key": "Exif.Image.ImageWidth", "type": "Long", "count": 1, "value": "160"},
            {"key": "Exif.Image.Make", "type": "Ascii", "count": 18, "value": "NIKON CORPORATION"},
        ], gate.parse_exiv2(text))

    def test_dump_projection_uses_local_interpretation_or_raw_value_for_pt(self) -> None:
        self.assertEqual([
            {"key": "Exif.Image.Orientation", "type": "Short", "count": 1, "value": "top, left"},
            {"key": "Exif.Photo.FocalLength", "type": "Rational", "count": 1, "value": "50/1"},
        ], gate.project_dump([
            {"key": "Exif.Image.Orientation", "type": "Short", "count": 1, "raw": "1", "translated": "top, left"},
            {"key": "Exif.Photo.FocalLength", "type": "Rational", "count": 1, "raw": "50/1", "translated": ""},
            {"key": "Iptc.Application2.Caption", "type": "String", "count": 1, "raw": "x", "translated": "x"},
        ]))

    def test_dump_protocol_unescapes_records_and_rejects_open_or_malformed_blocks(self) -> None:
        parsed = gate.parse_dump("FILE\t/tmp/a\\tb.jpg\nTAG\tExif.Image.Make\tAscii\t2\tA\\\\B\t\nSTATUS\tok\n")
        self.assertEqual("A\\B", parsed["/tmp/a\tb.jpg"]["rows"][0]["raw"])
        with self.assertRaises(gate.TagPrintError):
            gate.parse_dump("FILE\ta\nFILE\tb\nSTATUS\tok\n")
        with self.assertRaises(gate.TagPrintError):
            gate.parse_dump("FILE\ta\nTAG\tExif.Image.Width\tLong\tbad\t1\nSTATUS\tok\n")

    def test_each_unmatched_reference_requires_a_named_reason(self) -> None:
        self.assertEqual(set(gate._TAG_PRINT_EXCEPTIONS), {
            "addmoddel", "conversions", "crw-test", "exifdata-test", "exiv2-bug1145a.exv_test",
            "exiv2-test", "geotag-test", "icc-test", "iotest", "iso65k-test", "modify-test",
            "nls-test", "png-test", "preview-test", "stdin-test", "stringto-test",
            "test_pr_1905_poc1_ref", "tiff-test", "webp-test", "write-test-debug", "write-test",
            "write2-test", "xmpparser-test",
        })
        self.assertTrue(all(reason.strip() for reason in gate._TAG_PRINT_EXCEPTIONS.values()))


if __name__ == "__main__":
    unittest.main()
