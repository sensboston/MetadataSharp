"""Offline rules of the preview comparison: how the expectation and the dump are read, and how a
file's previews are judged. No external tool runs here."""
import base64
import contextlib
import io
import struct
import sys
import tempfile
import unittest
import zlib
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools" / "parity"))

import run_previews  # noqa: E402

REFERENCE = """
-----> one.jpg <-----

Command: exiv2 -pp one.jpg
Preview 1: image/jpeg, 160x120 pixels, 4158 bytes
Preview 2: image/jpeg, 320x240 pixels, 16993 bytes
Exit code: 0

Command: exiv2 -f -ep one.jpg
Exit code: 0

-----> two.jpg <-----

Command: exiv2 -pp two.jpg
Warning: Directory Canon has an unexpected next pointer; ignored.
Preview 1: image/tiff, 96x72 pixels, 20916 bytes
Exit code: 0

Command: exiv2 -f -ep two.jpg
Warning: Directory Canon has an unexpected next pointer; ignored.
Exit code: 0

-----> none.jpg <-----

Command: exiv2 -pp none.jpg
Exit code: 0
"""

SMALL = ("image/jpeg", 160, 120, 4158)
LARGE = ("image/jpeg", 320, 240, 16993)


class ReferenceTests(unittest.TestCase):
    def test_the_listing_command_is_read_per_file_in_order(self):
        expected = run_previews.parse_reference(REFERENCE)
        self.assertEqual(["one.jpg", "two.jpg", "none.jpg"], list(expected))
        self.assertEqual([SMALL, LARGE], expected["one.jpg"])
        self.assertEqual([("image/tiff", 96, 72, 20916)], expected["two.jpg"])
        self.assertEqual([], expected["none.jpg"])

    def test_a_preview_line_of_another_command_is_not_counted(self):
        text = REFERENCE.replace("Command: exiv2 -f -ep one.jpg\n", "Command: exiv2 -f -ep one.jpg\nPreview 1: image/jpeg, 1x1 pixels, 9 bytes\n")
        self.assertEqual([SMALL, LARGE], run_previews.parse_reference(text)["one.jpg"])

    def test_damage_is_an_error_and_not_a_smaller_expectation(self):
        with self.assertRaises(run_previews.PreviewParityError):
            run_previews.parse_reference(REFERENCE.replace("4158 bytes", "many bytes"))
        with self.assertRaises(run_previews.PreviewParityError):
            run_previews.parse_reference(REFERENCE + "\n-----> one.jpg <-----\n")
        with self.assertRaises(run_previews.PreviewParityError):
            run_previews.parse_reference("Preview 1: image/jpeg, 1x1 pixels, 9 bytes\n")


class OracleTests(unittest.TestCase):
    def test_what_the_pinned_tool_printed_is_read_by_the_rules_of_the_expectation(self):
        expected = run_previews.oracle_reference({
            "raw.cr3": "Preview 1: image/jpeg, 160x120 pixels, 4158 bytes\nPreview 2: image/jpeg, 320x240 pixels, 16993 bytes\n",
            "warned.cr3": "Warning: Directory Canon has an unexpected next pointer; ignored.\n"
                          "Preview 1: image/tiff, 96x72 pixels, 20916 bytes\n",
        })
        self.assertEqual(["raw.cr3", "warned.cr3"], list(expected))
        self.assertEqual([SMALL, LARGE], expected["raw.cr3"])
        self.assertEqual([("image/tiff", 96, 72, 20916)], expected["warned.cr3"])

    def test_a_file_the_tool_printed_nothing_for_expects_no_preview(self):
        expected = run_previews.oracle_reference({"silent.cr3": "", "raw.cr3": "Preview 1: image/jpeg, 160x120 pixels, 4158 bytes"})
        self.assertEqual([], expected["silent.cr3"])
        self.assertEqual([SMALL], expected["raw.cr3"])
        outcome = run_previews.compare_file(expected["silent.cr3"], [SMALL])
        self.assertEqual((0, [], [SMALL]), (outcome["match"], outcome["missing"], outcome["extra"]))

    def test_a_line_of_the_tool_that_does_not_parse_is_an_error(self):
        with self.assertRaises(run_previews.PreviewParityError):
            run_previews.oracle_reference({"raw.cr3": "Preview 1: image/jpeg, 160 by 120"})

    def test_fixture_oracle_only_receives_image_formats_with_preview_loaders(self):
        self.assertIn(".crw", run_previews._PREVIEW_FIXTURE_SUFFIXES)
        self.assertIn(".pgf", run_previews._PREVIEW_FIXTURE_SUFFIXES)
        self.assertNotIn(".asf", run_previews._PREVIEW_FIXTURE_SUFFIXES)
        self.assertNotIn(".wav", run_previews._PREVIEW_FIXTURE_SUFFIXES)
        self.assertIn("b11eps", run_previews._METADATA_ONLY_EPS_FIXTURE_DIRS)
        self.assertIn("owned-codestream.jxl", run_previews._REFERENCE_UNSUPPORTED_PREVIEW_FIXTURES)

    def test_the_two_sources_of_the_expectation_exclude_each_other(self):
        for arguments in ([], ["--tree", "t", "--fixtures", "f"]):
            with self.assertRaises(SystemExit):
                with contextlib.redirect_stderr(io.StringIO()):
                    run_previews.main(["--dump-command", "dump"] + arguments)
        with self.assertRaises(run_previews.PreviewParityError):
            run_previews.main(["--dump-command", "dump", "--fixtures", "f"])


class DumpTests(unittest.TestCase):
    def test_records_are_grouped_by_file(self):
        text = ("FILE\t/c/one.jpg\nPREVIEW\t1\timage/jpeg\t160x120\t4158\t64\tExif.Thumbnail.JPEGInterchangeFormat\n"
                "PREVIEW\t2\tvideo/H265\t0x0\t77\t128\tBmff.thmb\nSTATUS\tok\n"
                "FILE\t/c/two.jpg\nSTATUS\trefused\tLimitExceeded\n")
        blocks = run_previews.parse_dump(text)
        self.assertEqual([SMALL, ("video/H265", 0, 0, 77)], blocks["/c/one.jpg"]["previews"])
        self.assertEqual([64, 128], blocks["/c/one.jpg"]["offsets"])
        self.assertEqual(["Exif.Thumbnail.JPEGInterchangeFormat", "Bmff.thmb"], blocks["/c/one.jpg"]["sources"])
        self.assertEqual("ok", blocks["/c/one.jpg"]["status"])
        self.assertEqual(("refused", "LimitExceeded", []),
                         (blocks["/c/two.jpg"]["status"], blocks["/c/two.jpg"]["detail"], blocks["/c/two.jpg"]["previews"]))

    def test_a_malformed_dump_is_refused(self):
        for text in ("PREVIEW\t1\timage/jpeg\t1x1\t9\t8\tx\n", "FILE\t/a\n", "FILE\t/a\nPREVIEW\t1\timage/jpeg\t1by1\t9\t8\tx\nSTATUS\tok\n",
                     "FILE\t/a\nSTATUS\tok\nPREVIEW\t1\timage/jpeg\t1x1\t9\t8\tx\n", "FILE\t/a\nFILE\t/b\nSTATUS\tok\n"):
            with self.assertRaises(run_previews.PreviewParityError, msg=text):
                run_previews.parse_dump(text)


class ComparisonTests(unittest.TestCase):
    def test_previews_are_compared_as_a_multiset(self):
        outcome = run_previews.compare_file([SMALL, LARGE], [SMALL, LARGE])
        self.assertEqual((2, [], [], True), (outcome["match"], outcome["missing"], outcome["extra"], outcome["ordered"]))
        outcome = run_previews.compare_file([SMALL, LARGE], [LARGE, ("video/H265", 0, 0, 77)])
        self.assertEqual((1, [SMALL], [("video/H265", 0, 0, 77)], True),
                         (outcome["match"], outcome["missing"], outcome["extra"], outcome["ordered"]))
        outcome = run_previews.compare_file([SMALL, SMALL], [SMALL])
        self.assertEqual((1, [SMALL], []), (outcome["match"], outcome["missing"], outcome["extra"]))

    def test_one_differing_number_is_a_missing_and_an_extra_preview(self):
        outcome = run_previews.compare_file([SMALL], [("image/jpeg", 160, 120, 4159)])
        self.assertEqual((0, [SMALL], [("image/jpeg", 160, 120, 4159)]), (outcome["match"], outcome["missing"], outcome["extra"]))

    def test_the_order_of_the_common_previews_is_judged_on_its_own(self):
        self.assertFalse(run_previews.compare_file([SMALL, LARGE], [LARGE, SMALL])["ordered"])
        self.assertTrue(run_previews.compare_file([SMALL, LARGE], [SMALL, ("image/x-raw-rgb", 2, 2, 12), LARGE])["ordered"])

    def test_totals_count_every_expected_preview_and_only_ok_files_contribute(self):
        expected = run_previews.parse_reference(REFERENCE)
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "one.jpg").write_bytes(bytes(30000))
            dumped = {
                str(root / "one.jpg"): {"previews": [SMALL, LARGE], "offsets": [100, 200],
                                         "sources": ["a", "b"], "status": "ok", "detail": ""},
                str(root / "two.jpg"): {"previews": [("image/tiff", 96, 72, 20916)], "sources": ["a"],
                                         "status": "refused", "detail": "InvalidJpeg"},
            }
            paths = {"one.jpg": root / "one.jpg", "two.jpg": root / "two.jpg", "none.jpg": None}
            report = run_previews.compare(expected, dumped, paths)
        self.assertEqual("Preview", report["kind"])
        self.assertEqual({"expected": 3, "match": 2, "missing": 1, "extra": 0, "files": 3, "files_equal": 2, "files_ordered": 3},
                         report["totals"])
        self.assertEqual({"ok": 1, "refused": 1, "no-input": 1}, report["statuses"])
        self.assertEqual(round(2 / 3, 6), report["rate"])
        differing = run_previews.summary(report)["differing"]
        self.assertEqual(["none.jpg", "two.jpg"], [entry["file"] for entry in differing])
        self.assertIn("two.jpg [refused]", run_previews.text_report(report))

    def test_a_file_the_dump_did_not_answer_for_is_a_structural_failure(self):
        expected = run_previews.parse_reference(REFERENCE)
        with self.assertRaises(run_previews.PreviewParityError):
            run_previews.compare(expected, {}, {"one.jpg": Path("/c/one.jpg"), "two.jpg": None, "none.jpg": None})

    def test_a_differing_source_offset_fails_even_when_the_preview_list_matches(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "file.jpg"
            path.write_bytes(bytes(30000))
            expected = {"file.jpg": [SMALL, LARGE]}
            dumped = {str(path): {"previews": [SMALL, LARGE], "offsets": [100, 200], "sources": ["Exif", "Exif"],
                                  "status": "ok", "detail": ""}}
            report = run_previews.compare(expected, dumped, {"file.jpg": path}, {"file.jpg": [[99], [200]]})
        self.assertEqual(1, report["totals"]["offset_mismatch"])
        self.assertEqual([{"preview": list(SMALL), "actual": 100, "expected_any": [99]}],
                         report["files"][0]["offset_mismatch"])
        self.assertEqual(1, run_previews.gate_exit_code(report))

    def test_source_offsets_are_recovered_from_direct_hex_and_base64_preview_bytes(self):
        jpeg = b"\xff\xd8sample\xff\xd9"
        self.assertEqual([3], run_previews._source_offsets(b"xx:" + jpeg + b":yy", jpeg, "image/jpeg", 1, 1))
        hex_jpeg = jpeg.hex().encode()
        photoshop = b"%BeginPhotoshop: 1\n" + hex_jpeg + b"\n%EndPhotoshop"
        self.assertEqual([len(b"%BeginPhotoshop: 1\n")],
                         run_previews._source_offsets(photoshop, jpeg, "image/jpeg", 1, 1))
        xmp = b"<image>" + base64.b64encode(jpeg) + b"</image>"
        self.assertEqual([len(b"<image>")], run_previews._source_offsets(xmp, jpeg, "image/jpeg", 1, 1))
        rgb = bytes((1, 2, 3))
        pnm = b"P6\n1 1\n255\n" + rgb
        eps = b"%%BeginData: 6 Hex Bytes\n 010203\n%%EndData"
        self.assertEqual([len(b"%%BeginData: 6 Hex Bytes\n")],
                         run_previews._source_offsets(eps, pnm, "image/x-portable-anymap", 1, 1))
        profile = b"Exif\0\0" + jpeg
        text = b"\nexif\n" + str(len(profile)).encode() + b"\n" + profile.hex().encode()
        keyword = b"Raw profile type exif"
        payload = keyword + b"\0\0" + zlib.compress(text)
        chunk = struct.pack(">I", len(payload)) + b"zTXt" + payload + bytes(4)
        png = b"\x89PNG\r\n\x1a\n" + chunk + struct.pack(">I", 0) + b"IEND" + bytes(4)
        expected_offset = 8 + 8 + len(keyword) + 2
        self.assertEqual([expected_offset], run_previews._source_offsets(png, jpeg, "image/jpeg", 1, 1))

    def test_generated_tiff_offset_is_recovered_from_its_source_strip(self):
        pixels = b"RGB!"
        # Two TIFF fields: StripOffsets at 38 and StripByteCounts at four bytes.
        tiff = (b"II" + struct.pack("<HI", 42, 8) + struct.pack("<H", 2) +
                struct.pack("<HHII", 273, 4, 1, 38) + struct.pack("<HHII", 279, 4, 1, len(pixels)) +
                struct.pack("<I", 0) + pixels)
        source = b"prefix" + pixels + b"suffix"
        self.assertEqual([6], run_previews._source_offsets(source, tiff, "image/tiff", 1, 1))

    def test_a_reported_offset_outside_its_source_file_is_a_structural_failure(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "file.jpg"
            path.write_bytes(bytes(10))
            expected = {"file.jpg": [SMALL]}
            dumped = {str(path): {"previews": [SMALL], "offsets": [11], "sources": ["EXIF"],
                                  "status": "ok", "detail": ""}}
            with self.assertRaises(run_previews.PreviewParityError):
                run_previews.compare(expected, dumped, {"file.jpg": path})

    def test_any_unexplained_preview_mismatch_or_refusal_fails_the_gate(self):
        matching = {"statuses": {"ok": 1}, "totals": {"missing": 0, "extra": 0}}
        self.assertEqual(0, run_previews.gate_exit_code(matching))
        for report in (
            {"statuses": {"ok": 1}, "totals": {"missing": 1, "extra": 0}},
            {"statuses": {"ok": 1}, "totals": {"missing": 0, "extra": 1}},
            {"statuses": {"refused": 1}, "totals": {"missing": 0, "extra": 0}},
            {"statuses": {"no-input": 1}, "totals": {"missing": 0, "extra": 0}},
        ):
            self.assertEqual(1, run_previews.gate_exit_code(report), report)


if __name__ == "__main__":
    unittest.main()
