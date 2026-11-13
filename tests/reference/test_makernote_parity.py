"""Offline checks for the comparer of MakerNote edits; a runner stands in for the tool and the fixture."""
import importlib.util
import tempfile
import unittest
from unittest.mock import patch
from collections import OrderedDict
from pathlib import Path

spec = importlib.util.spec_from_file_location(
    "makernote_parity", Path(__file__).parents[2] / "tools" / "reference" / "makernote_parity.py")
x = importlib.util.module_from_spec(spec)
spec.loader.exec_module(x)


def side(keys, code=0, warnings=()):
    return OrderedDict(keys), code, list(warnings)


class VerdictTests(unittest.TestCase):
    before = side({"Exif.Canon.OwnerName": ["Old"], "Exif.Canon.ModelID": ["1"]})

    def test_the_value_set_and_the_rest_kept_is_ok(self):
        after = side({"Exif.Canon.OwnerName": ["New"], "Exif.Canon.ModelID": ["1"]})
        self.assertEqual(("ok", ""), x.verdict(self.before, after, "Exif.Canon.OwnerName", "New"))

    def test_a_value_the_tool_does_not_print_is_named(self):
        after = side({"Exif.Canon.OwnerName": ["Old"], "Exif.Canon.ModelID": ["1"]})
        self.assertEqual("value the tool does not print", x.verdict(self.before, after, "Exif.Canon.OwnerName", "New")[0])

    def test_another_key_that_changed_went_or_came_is_named(self):
        changed = side({"Exif.Canon.OwnerName": ["New"], "Exif.Canon.ModelID": ["2"]})
        gone = side({"Exif.Canon.OwnerName": ["New"]})
        came = side({"Exif.Canon.OwnerName": ["New"], "Exif.Canon.ModelID": ["1"], "Exif.Canon.0x0001": ["3"]})
        self.assertEqual("another key changed", x.verdict(self.before, changed, "Exif.Canon.OwnerName", "New")[0])
        self.assertEqual("another key gone", x.verdict(self.before, gone, "Exif.Canon.OwnerName", "New")[0])
        self.assertEqual("a key appeared", x.verdict(self.before, came, "Exif.Canon.OwnerName", "New")[0])

    def test_a_key_the_tool_never_printed_an_exit_code_and_a_warning_are_named(self):
        after = side({"Exif.Canon.OwnerName": ["New"], "Exif.Canon.ModelID": ["1"]}, 1)
        warned = side({"Exif.Canon.OwnerName": ["New"], "Exif.Canon.ModelID": ["1"]}, 0, ["Warning: offset"])
        self.assertEqual("key the tool does not print", x.verdict(self.before, after, "Exif.Canon.Nothing", "1")[0])
        self.assertEqual("exit code changed", x.verdict(self.before, after, "Exif.Canon.OwnerName", "New")[0])
        self.assertEqual(("new warning", "Warning: offset"), x.verdict(self.before, warned, "Exif.Canon.OwnerName", "New"))


class RunTests(unittest.TestCase):
    def test_a_run_counts_what_passes_and_fails_on_what_does_not(self):
        with tempfile.TemporaryDirectory() as folder:
            corpus, work = Path(folder) / "corpus", Path(folder) / "work"
            corpus.mkdir()
            (corpus / "a.jpg").write_bytes(b"x")

            def runner(command, **_):
                if "makernote-parity" in command:
                    (work / "s00000.1.ours.jpg").write_bytes(b"y")
                    (work / "s00000.2.ours.jpg").write_bytes(b"z")
                    (work / "ours.tsv").write_text(
                        "s00000\t1\tsame\tExif.Canon.OwnerName\twritten\tNew\n"
                        "s00000\t2\tgrow\tExif.Canon.OwnerName\twritten\tLonger\n"
                        "s00000\t3\tgrow\tExif.Canon.ImageType\trefused UnsupportedFeature\tG\n", encoding="utf-8")
                    return 0, "", ""
                path = command[-1]
                value = "New" if path.endswith(".1.ours.jpg") else "Old"
                return 0, f"Exif.Canon.OwnerName   {value}\n", f"Warning: {path} odd\n"

            # This test deliberately constructs a failing parity result. Keep its simulated GitHub
            # annotations from becoming real errors on the workflow run that executes the test suite.
            with patch.object(x, "annotate"):
                self.assertEqual(1, x.main([str(corpus), str(work), "--exiv2", "exiv2", "--fixture", "fixture"], runner))
            self.assertFalse((work / "s00000.1.ours.jpg").exists())

    def test_the_print_out_keeps_values_with_spaces_and_drops_the_path_from_warnings(self):
        found, code, warnings = x.printed("exiv2", "/w/a.jpg", lambda command, **_: (
            0, "Exif.Canon.OwnerName   A  B\nExif.Canon.ModelID\n", "Warning: /w/a.jpg: bad\n"))
        self.assertEqual({"Exif.Canon.OwnerName": ["A  B"], "Exif.Canon.ModelID": [""]}, dict(found))
        self.assertEqual((0, ["Warning: : bad"]), (code, warnings))


if __name__ == "__main__":
    unittest.main()
