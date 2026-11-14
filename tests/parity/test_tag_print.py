from __future__ import annotations

import io
import sys
from collections import Counter
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from unittest.mock import patch

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
            {"key": "Exif.Image.Orientation", "type": "Short", "count": 1, "value": "top, left", "_raw_value": "1", "_has_translation": True},
            {"key": "Exif.Photo.FocalLength", "type": "Rational", "count": 1, "value": "50/1", "_raw_value": "50/1", "_has_translation": False},
            {"key": "Exif.Image.ImageDescription", "type": "Ascii", "count": 3, "value": "   ", "_raw_value": "   ", "_has_translation": False},
        ], gate.project_dump([
            {"key": "Exif.Image.Orientation", "type": "Short", "count": 1, "raw": "1", "translated": "top, left"},
            {"key": "Exif.Photo.FocalLength", "type": "Rational", "count": 1, "raw": "50/1", "translated": ""},
            {"key": "Exif.Image.ImageDescription", "type": "Ascii", "count": 3, "raw": "   ", "translated": ""},
            {"key": "Iptc.Application2.Caption", "type": "String", "count": 1, "raw": "x", "translated": "x"},
        ]))

    def test_comparison_ignores_record_iteration_order_but_not_values(self) -> None:
        expected = [
            {"key": "Exif.Image.Make", "type": "Ascii", "count": 1, "value": "Camera"},
            {"key": "Exif.Image.Model", "type": "Ascii", "count": 1, "value": "Body"},
        ]
        self.assertEqual([], gate.compare_rows(expected, list(reversed(expected))))
        changed = [expected[0], {**expected[1], "value": "Other body"}]
        differences = gate.compare_rows(expected, changed)
        self.assertEqual(1, len(differences))
        self.assertEqual(["value-diff"], [row["kind"] for row in differences])

    def test_numeric_fallback_matches_only_the_same_catalogued_tag(self) -> None:
        accepted = Counter()
        expected = [{"key": "Exif.Image.0x0100", "type": "Long", "count": 1, "value": "640"}]
        actual = [{"key": "Exif.Image.ImageWidth", "type": "Long", "count": 1, "value": "640"}]
        self.assertEqual([], gate.compare_rows(expected, actual, accepted_names=accepted))
        self.assertEqual({("Exif.Image.0x0100", "Exif.Image.ImageWidth"): 1}, dict(accepted))
        other_directory = [{"key": "Exif.Photo.0x0100", "type": "Long", "count": 1, "value": "640"}]
        wrong_directory = gate.compare_rows(other_directory, actual)
        self.assertEqual(["extra", "missing"], sorted(row["kind"] for row in wrong_directory))
        wrong_value = [{"key": "Exif.Image.ImageWidth", "type": "Long", "count": 1, "value": "641"}]
        self.assertEqual(1, len(gate.compare_rows(expected, wrong_value)))

    def test_pt_text_difference_requires_exact_independent_pv_raw_value(self) -> None:
        difference = {"kind": "value-diff",
                      "exiv2": {"key": "Exif.Sony1MltCsA100.AELButton", "type": "Short", "count": 1, "value": "Hold"},
                      "metadatasharp": {"key": "Exif.Sony1MltCsA100.AELButton", "type": "Short", "count": 1, "value": "0"}}
        raw_reference = [{"key": "Exif.Sony1MltCsA100.AELButton", "type": "Short", "count": 1, "value": "0"}]
        source_rows = [{"key": "Exif.Sony1MltCsA100.AELButton", "type": "Short", "count": 1,
                        "raw": "0", "translated": ""}]
        remaining, verified = gate.verify_raw_value_differences([difference], raw_reference, source_rows)
        self.assertEqual([], remaining)
        self.assertEqual("0", verified[0]["raw_value"])
        changed_raw = [{**raw_reference[0], "value": "1"}]
        remaining, verified = gate.verify_raw_value_differences([difference], changed_raw, source_rows)
        self.assertEqual([difference], remaining)
        self.assertEqual([], verified)
        wrong_type = [{**raw_reference[0], "type": "SShort"}]
        remaining, verified = gate.verify_raw_value_differences([difference], wrong_type, source_rows)
        self.assertEqual([difference], remaining)
        self.assertEqual([], verified)

    def test_only_trailing_ascii_and_user_comment_padding_is_ignored_as_format(self) -> None:
        accepted = Counter()
        expected = [{"key": "Exif.Image.Make", "type": "Ascii", "count": 8, "value": "Camera "}]
        actual = [{"key": "Exif.Image.Make", "type": "Ascii", "count": 8, "value": "Camera"}]
        self.assertEqual([], gate.compare_rows(expected, actual, accepted_padding=accepted))
        self.assertEqual({"Ascii": 1}, dict(accepted))
        expected = [{"key": "Exif.Photo.FocalLength", "type": "Rational", "count": 1, "value": "50/1 "}]
        actual = [{"key": "Exif.Photo.FocalLength", "type": "Rational", "count": 1, "value": "50/1"}]
        self.assertEqual(1, len(gate.compare_rows(expected, actual)))

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
        self.assertEqual({"Exif.GPSInfo.GPSAltitudeRef"}, set(gate._TAG_PRINT_KEY_EXCEPTIONS))
        self.assertTrue(all(reason.strip() for reason in gate._TAG_PRINT_KEY_EXCEPTIONS.values()))
        self.assertIn("Exif.Image.ImageWidth", gate.CATALOG_KEYS)
        self.assertNotIn("Exif.Canon.0x0019", gate.CATALOG_KEYS)

    def test_only_numeric_raw_tags_without_a_pinned_named_row_are_reported_as_raw_only(self) -> None:
        self.assertTrue(gate._is_raw_numeric_tag("Exif.Canon.0x0019"))
        self.assertFalse(gate._is_raw_numeric_tag("Exif.Photo.ImageWidth"))
        self.assertTrue(gate._is_raw_numeric_tag("Exif.Canon.0X0019"))

    def test_unregistered_numeric_fallback_is_reported_but_unknown_named_key_still_fails(self) -> None:
        candidate = gate.corpus.Candidate("sample.jpg", Path("/tmp/sample.jpg"), "test")
        dump = {"/tmp/sample.jpg": {"status": "ok", "rows": [
            {"key": "Exif.Image.ImageWidth", "type": "Long", "count": 1, "raw": "160", "translated": ""},
            {"key": "Exif.Canon.0x0019", "type": "Short", "count": 1, "raw": "1", "translated": "1"},
        ]}}
        with patch.object(gate, "_run_reference", return_value=(0, "Exif.Image.ImageWidth Long 1 160\n", "")):
            report = gate.build_report([candidate], [], dump)
        self.assertEqual(0, report["totals"].get("failed_files", 0))
        self.assertEqual("Exif.Canon.0x0019", report["raw_only_tag_exceptions"][0]["key"])

        dump["/tmp/sample.jpg"]["rows"].append(
            {"key": "Exif.Canon.CameraSerialNumber", "type": "Ascii", "count": 1, "raw": "x", "translated": "x"})
        with patch.object(gate, "_run_reference", return_value=(0, "Exif.Image.ImageWidth Long 1 160\n", "")):
            report = gate.build_report([candidate], [], dump)
        self.assertEqual(1, report["totals"]["failed_files"])
        self.assertEqual("extra", report["differing"][0]["rows"][0]["kind"])

    def test_annotation_chunks_keep_all_failure_groups_and_refusals_under_github_limit(self) -> None:
        payload = {
            "totals": {"files": 40},
            "mismatch_groups": [{"category": "description", "key": f"Exif.Photo.Tag{i}",
                                 "type": "Short", "occurrences": i,
                                 "examples": [{"difference": "text differs", "occurrences": i}]}
                                for i in range(40)],
            "refusals": [{"file": f"failed-{i}.jpg", "kind": "metadatasharp-refused",
                          "detail": "a" * 160} for i in range(34)],
            "value_diff_by_key": [{"key": f"Exif.Photo.Tag{i}", "exiv2": "x", "metadatasharp": "y",
                                   "occurrences": i} for i in range(30)],
            "display_rule_hits": [],
        }
        output = io.StringIO()
        with redirect_stdout(output):
            gate._emit_annotation(payload)
        lines = output.getvalue().splitlines()
        self.assertGreater(len(lines), 1)
        self.assertTrue(all(len(line) < 4000 for line in lines))
        self.assertEqual(2, sum("refusals " in line for line in lines))

    def test_report_groups_text_only_differences_by_key_and_category(self) -> None:
        candidate = gate.corpus.Candidate("metering.jpg", Path("/tmp/metering.jpg"), "test")
        dump = {"/tmp/metering.jpg": {"status": "ok", "rows": [
            {"key": "Exif.Photo.MeteringMode", "type": "Short", "count": 1, "raw": "5", "translated": "Pattern"},
        ]}}
        with patch.object(gate, "_run_reference", return_value=(0, "Exif.Photo.MeteringMode Short 1 Multi-segment\n", "")):
            report = gate.build_report([candidate], [], dump)
        self.assertEqual([{"key": "Exif.Photo.MeteringMode", "exiv2": "Multi-segment", "metadatasharp": "Pattern",
                           "occurrences": 1, "rule": gate._TAG_PRINT_DISPLAY_REASONS["Exif.Photo.MeteringMode"]}],
                         report["display_rule_hits"])
        self.assertEqual([], report["mismatch_groups"])


if __name__ == "__main__":
    unittest.main()
