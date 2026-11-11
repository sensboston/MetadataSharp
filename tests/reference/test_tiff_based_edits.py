"""Offline checks for the rule that an edited TIFF-based file must still read in the pinned tool.

The tool itself is not run here: its print-outs are authored strings.
"""
import contextlib
import importlib.util
import io
import tempfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location(
    "tiff_based_edits", Path(__file__).parents[2] / "tools" / "reference" / "tiff_based_edits.py")
t = importlib.util.module_from_spec(spec)
spec.loader.exec_module(t)

SOURCE = (
    "Exif.Image.Make                              Ascii      12  TestCompany\n"
    "Exif.Image.StripOffsets                      Long        1  300\n"
    "Exif.Image.ExifTag                           Long        1  170\n"
    "Exif.Photo.DateTimeOriginal                  Ascii      20  2020:01:02 03:04:05\n"
)
EDITED = (
    "Exif.Image.Make                              Ascii      12  TestCompany\n"
    "Exif.Image.StripOffsets                      Long        1  300\n"
    "Exif.Image.ExifTag                           Long        1  364\n"
    "Exif.Photo.DateTimeOriginal                  Ascii      20  2021:06:07 08:09:10\n"
    "Exif.Photo.DateTimeDigitized                 Ascii      20  2021:06:07 08:09:10\n"
)
CHANGED = {"Exif.Image.ExifTag", "Exif.Photo.DateTimeOriginal"}


class CompareTests(unittest.TestCase):
    def test_an_edit_that_changes_only_what_it_names_passes(self):
        self.assertEqual([], t.compare(SOURCE, EDITED, CHANGED, ["2021:06:07 08:09:10", "Exif.Photo.DateTimeDigitized"]))

    def test_a_key_the_edit_did_not_name_may_not_change(self):
        problems = t.compare(SOURCE, EDITED, {"Exif.Photo.DateTimeOriginal"}, [])
        self.assertEqual(1, len(problems))
        self.assertIn("Exif.Image.ExifTag changed", problems[0])

    def test_a_moved_strip_is_a_problem(self):
        moved = EDITED.replace("1  300", "1  420")
        self.assertIn("Exif.Image.StripOffsets changed", t.compare(SOURCE, moved, CHANGED, [])[0])

    def test_a_key_that_is_gone_is_a_problem(self):
        gone = "".join(line + "\n" for line in EDITED.splitlines() if "Make" not in line)
        self.assertEqual(["Exif.Image.Make is no longer printed"], t.compare(SOURCE, gone, CHANGED, []))

    def test_expected_text_has_to_be_printed(self):
        self.assertEqual(["expected text is not printed: Xmp.xmp.CreatorTool"],
                         t.compare(SOURCE, EDITED, CHANGED, ["Xmp.xmp.CreatorTool"]))

    def test_column_padding_is_not_a_difference_and_repeated_keys_keep_their_order(self):
        self.assertEqual([], t.compare("K.a.b  Ascii   2  x\nK.a.b  Ascii   2  y\n", "K.a.b Ascii 2 x\nK.a.b Ascii 2 y\n", set(), []))
        self.assertEqual(1, len(t.compare("K.a.b Ascii 2 x\nK.a.b Ascii 2 y\n", "K.a.b Ascii 2 y\nK.a.b Ascii 2 x\n", set(), [])))


class ManifestTests(unittest.TestCase):
    def test_a_row_names_source_edited_changed_keys_and_expected_text(self):
        self.assertEqual([("a.tif", "a-gps.tif", {"K.one", "K.two"}, ["text one", "two"])],
                         t.parse_manifest("a.tif\ta-gps.tif\tK.one,K.two\ttext one|two\n"))
        self.assertEqual([("a.tif", "b.tif", set(), [])], t.parse_manifest("a.tif\tb.tif\t\t\n"))

    def test_a_short_row_or_an_empty_manifest_is_an_error(self):
        with self.assertRaises(ValueError):
            t.parse_manifest("a.tif\tb.tif\tK.one\n")
        with self.assertRaises(ValueError):
            t.parse_manifest("\n")


class CheckTests(unittest.TestCase):
    def run_check(self, outputs):
        with tempfile.TemporaryDirectory() as directory:
            (Path(directory) / "manifest.tsv").write_text(
                "a.tif\ta-edit.tif\tExif.Image.ExifTag,Exif.Photo.DateTimeOriginal\t2021:06:07 08:09:10\n", encoding="utf-8")
            return t.check(directory, ["tool"], lambda command, path: outputs[path.name])

    def test_a_file_that_still_reads_passes(self):
        self.assertEqual(([], 1), self.run_check({"a.tif": (0, SOURCE, ""), "a-edit.tif": (0, EDITED, "")}))

    def test_an_edited_file_the_tool_refuses_fails(self):
        problems, _ = self.run_check({"a.tif": (0, SOURCE, ""), "a-edit.tif": (1, "", "corrupted image metadata")})
        self.assertEqual("a-edit.tif", problems[0][0])
        self.assertIn("not read (exit 1): corrupted image metadata", problems[0][1])

    def test_a_source_the_tool_cannot_read_is_a_failure_not_a_pass(self):
        problems, _ = self.run_check({"a.tif": (1, "", "not an image"), "a-edit.tif": (0, EDITED, "")})
        self.assertEqual("a.tif", problems[0][0])
        problems, _ = self.run_check({"a.tif": (0, "", ""), "a-edit.tif": (0, EDITED, "")})
        self.assertIn("the source is not read", problems[0][1])

    def test_main_reports_the_outcome_through_its_exit_code(self):
        with tempfile.TemporaryDirectory() as directory:
            (Path(directory) / "manifest.tsv").write_text("a.tif\tb.tif\t\t\n", encoding="utf-8")
            printed = io.StringIO()
            with contextlib.redirect_stdout(printed):
                failed = t.main([directory, "--exiv2-command", "tool -pa"], lambda command, path: (3, "", "refused"))
                passed = t.main([directory, "--exiv2-command", "tool -pa"], lambda command, path: (0, SOURCE, ""))
        self.assertEqual((1, 0), (failed, passed))
        self.assertIn("::error title=TIFF-based edits a.tif::the source is not read (exit 3): refused", printed.getvalue())
        self.assertIn("::notice title=TIFF-based edits::1 edited files", printed.getvalue())


if __name__ == "__main__":
    unittest.main()
