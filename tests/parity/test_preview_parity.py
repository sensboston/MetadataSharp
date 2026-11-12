"""Offline rules of the preview comparison: how the expectation and the dump are read, and how a
file's previews are judged. No external tool runs here."""
import contextlib
import io
import sys
import unittest
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

    def test_the_two_sources_of_the_expectation_exclude_each_other(self):
        for arguments in ([], ["--tree", "t", "--fixtures", "f"]):
            with self.assertRaises(SystemExit):
                with contextlib.redirect_stderr(io.StringIO()):
                    run_previews.main(["--dump-command", "dump"] + arguments)
        with self.assertRaises(run_previews.PreviewParityError):
            run_previews.main(["--dump-command", "dump", "--fixtures", "f"])


class DumpTests(unittest.TestCase):
    def test_records_are_grouped_by_file(self):
        text = ("FILE\t/c/one.jpg\nPREVIEW\t1\timage/jpeg\t160x120\t4158\tExif.Thumbnail.JPEGInterchangeFormat\n"
                "PREVIEW\t2\tvideo/H265\t0x0\t77\tBmff.thmb\nSTATUS\tok\n"
                "FILE\t/c/two.jpg\nSTATUS\trefused\tLimitExceeded\n")
        blocks = run_previews.parse_dump(text)
        self.assertEqual([SMALL, ("video/H265", 0, 0, 77)], blocks["/c/one.jpg"]["previews"])
        self.assertEqual(["Exif.Thumbnail.JPEGInterchangeFormat", "Bmff.thmb"], blocks["/c/one.jpg"]["sources"])
        self.assertEqual("ok", blocks["/c/one.jpg"]["status"])
        self.assertEqual(("refused", "LimitExceeded", []),
                         (blocks["/c/two.jpg"]["status"], blocks["/c/two.jpg"]["detail"], blocks["/c/two.jpg"]["previews"]))

    def test_a_malformed_dump_is_refused(self):
        for text in ("PREVIEW\t1\timage/jpeg\t1x1\t9\tx\n", "FILE\t/a\n", "FILE\t/a\nPREVIEW\t1\timage/jpeg\t1by1\t9\tx\nSTATUS\tok\n",
                     "FILE\t/a\nSTATUS\tok\nPREVIEW\t1\timage/jpeg\t1x1\t9\tx\n", "FILE\t/a\nFILE\t/b\nSTATUS\tok\n"):
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
        root = Path("/c")
        dumped = {
            str(root / "one.jpg"): {"previews": [SMALL, LARGE], "sources": ["a", "b"], "status": "ok", "detail": ""},
            str(root / "two.jpg"): {"previews": [("image/tiff", 96, 72, 20916)], "sources": ["a"], "status": "refused", "detail": "InvalidJpeg"},
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


if __name__ == "__main__":
    unittest.main()
