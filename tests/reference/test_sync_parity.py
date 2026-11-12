"""Offline checks for the comparison of this library's conversions with the pinned tool's.

Neither the tool nor the fixture generator is run here: a runner stands in for both.
"""
import contextlib
import importlib.util
import io
import tempfile
import unittest
from collections import OrderedDict
from pathlib import Path

spec = importlib.util.spec_from_file_location(
    "sync_parity", Path(__file__).parents[2] / "tools" / "reference" / "sync_parity.py")
x = importlib.util.module_from_spec(spec)
spec.loader.exec_module(x)

PRINTED = (
    "Xmp.tiff.Make                                Canon\n"
    "Xmp.exif.ISOSpeedRatings                     100 200\n"
    "Xmp.dc.description                           lang=\"x-default\" Two words\n"
    "Xmp.exif.Empty\n"
    "Exif.Image.Make                              Canon\n"
)


class PrintOutTests(unittest.TestCase):
    def test_properties_are_the_keys_of_one_kind_with_their_values(self):
        found = x.properties(PRINTED, "Xmp.")
        self.assertEqual(["Xmp.tiff.Make", "Xmp.exif.ISOSpeedRatings", "Xmp.dc.description", "Xmp.exif.Empty"], list(found))
        self.assertEqual(['lang="x-default" Two words'], found["Xmp.dc.description"])
        self.assertEqual([""], found["Xmp.exif.Empty"])
        self.assertEqual({"Exif.Image.Make": ["Canon"]}, dict(x.properties(PRINTED, "Exif.")))

    def test_a_key_printed_twice_keeps_both_values(self):
        self.assertEqual({"Xmp.a.b": ["1", "2"]}, dict(x.properties("Xmp.a.b 1\nXmp.a.b 2\n", "Xmp.")))


class OutcomeTests(unittest.TestCase):
    def test_outcomes(self):
        self.assertEqual("same", x.outcome(["1/250"], ["1/250"]))
        self.assertEqual("form", x.outcome(["100 200"], ["100, 200"]))
        self.assertEqual("differs", x.outcome(["100 200"], ["100, 201"]))
        self.assertEqual("tool only", x.outcome(["1"], None))
        self.assertEqual("tool only (empty)", x.outcome(['lang="x-default"'], None))
        self.assertEqual("tool only (empty)", x.outcome(["charset=Unicode"], None))
        self.assertEqual("tool only (empty)", x.outcome([""], None))
        self.assertEqual("tool only", x.outcome(['lang="x-default" text'], None))
        self.assertEqual("ours only", x.outcome(None, ["1"]))

    def test_compare_lists_every_key_of_either_side_once_and_leaves_out_the_ignored(self):
        theirs = OrderedDict([("Exif.Image.Make", ["Canon"]), ("Exif.Photo.FNumber", ["4/1"])])
        ours = OrderedDict([("Exif.Image.Make", ["Canon"]), ("Exif.Image.ExifTag", ["90"]), ("Exif.Image.Model", ["X"])])
        rows = x.compare(theirs, ours, x.STRUCTURE)
        self.assertEqual([("Exif.Image.Make", "same"), ("Exif.Photo.FNumber", "tool only"), ("Exif.Image.Model", "ours only")],
                         [(key, kind) for key, kind, _, _ in rows])

    def test_a_documented_difference_is_found_by_key_and_by_prefix(self):
        saved = OrderedDict(x.DOCUMENTED)
        try:
            x.DOCUMENTED.clear()
            x.document(x.TO_XMP, "Xmp.exif.A", "differs, tool only", "reason A")
            x.document(x.TO_EXIF, "Exif.GPSInfo.*", "differs", "reason B")
            self.assertEqual("reason A", x.documented(x.TO_XMP, "Xmp.exif.A", "tool only"))
            self.assertIsNone(x.documented(x.TO_XMP, "Xmp.exif.A", "ours only"))
            self.assertIsNone(x.documented(x.TO_EXIF, "Xmp.exif.A", "differs"))
            self.assertEqual("reason B", x.documented(x.TO_EXIF, "Exif.GPSInfo.GPSLatitude", "differs"))
        finally:
            x.DOCUMENTED.clear()
            x.DOCUMENTED.update(saved)


class RunTests(unittest.TestCase):
    """A corpus of two files; the stand-in tool refuses to rewrite the second."""

    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        root = Path(self.directory.name)
        self.corpus, self.work = root / "corpus", root / "work"
        self.corpus.mkdir()
        (self.corpus / "a.jpg").write_bytes(b"a")
        (self.corpus / "b.JPG").write_bytes(b"b")
        (self.corpus / "c.png").write_bytes(b"c")
        self.ours_xmp = "Xmp.tiff.Make Canon\nXmp.exif.ISOSpeedRatings 100, 200\n"
        self.back = "Exif.Image.Make Canon\nExif.Image.ExifTag 90\n"

    def runner(self, command, timeout=120):
        if command[0] != "exiv2":
            self.assertEqual(["fixture", "--", "sync-parity", str(self.work)], command)
            for name in ("s00000",):
                (self.work / (name + ".ours.xmp")).write_text("ours")
                (self.work / (name + ".back.jpg")).write_text("back")
            return 0, "::notice title=Sync parity fixture::done\n", ""
        mode, path = command[1], Path(command[-1])
        if mode == "-d":
            self.assertEqual("xi", command[2])
            return (1, "", "refused") if path.read_bytes() == b"b" else (0, "", "")
        if mode == "-eX":
            path.with_suffix(".xmp").write_text("theirs")
            return 0, "", ""
        if mode == "-PXkv":
            return 0, self.ours_xmp if path.name.endswith(".ours.xmp") else "Xmp.tiff.Make Canon\nXmp.exif.ISOSpeedRatings 100 200\n", ""
        if mode == "-PEkv":
            return 0, self.back if path.name.endswith(".back.jpg") else "Exif.Image.Make Canon\n", ""
        raise AssertionError(command)

    def main(self):
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            code = x.main([str(self.corpus), str(self.work), "--exiv2", "exiv2", "--fixture", "fixture --"], self.runner)
        return code, out.getvalue()

    def test_the_same_keys_and_values_pass_and_a_form_is_counted(self):
        code, out = self.main()
        self.assertEqual(0, code, out)
        self.assertIn("1 files compared of 1 prepared (1 not rewritten by the tool); left out, not read by this library: none; "
                      "values: same 1, form 1, differs 0", out)
        self.assertIn("XMP to EXIF::1 files compared of 1 prepared", out)
        self.assertIn("EXIF to XMP Xmp.exif.ISOSpeedRatings: 1; s00000: tool ['100 200'] ours ['100, 200']", out)
        self.assertFalse((self.work / "s00001.jpg").exists())

    def test_a_difference_that_is_not_documented_fails(self):
        self.ours_xmp = "Xmp.tiff.Make Nikon\n"
        self.back = "Exif.Image.Model X\n"
        code, out = self.main()
        self.assertEqual(1, code, out)
        self.assertIn("FAIL EXIF to XMP Xmp.tiff.Make differs: 1; s00000: tool ['Canon'] ours ['Nikon']", out)
        self.assertIn("FAIL EXIF to XMP Xmp.exif.ISOSpeedRatings tool only: 1", out)
        self.assertIn("FAIL XMP to EXIF Exif.Image.Make tool only: 1", out)
        self.assertIn("FAIL XMP to EXIF Exif.Image.Model ours only: 1", out)
        self.assertIn("::error title=Sync parity - differences that are not documented::", out)

    def test_a_documented_difference_passes_and_is_reported_with_its_reason(self):
        self.ours_xmp = "Xmp.tiff.Make Nikon\nXmp.exif.ISOSpeedRatings 100 200\n"
        saved = OrderedDict(x.DOCUMENTED)
        try:
            x.document(x.TO_XMP, "Xmp.tiff.*", "differs", "an authored reason")
            code, out = self.main()
        finally:
            x.DOCUMENTED.clear()
            x.DOCUMENTED.update(saved)
        self.assertEqual(0, code, out)
        self.assertIn("EXIF to XMP Xmp.tiff.Make differs: 1; an authored reason; s00000", out)

    def test_a_file_this_library_refuses_is_left_out_and_counted(self):
        inner = self.runner

        def refusing(command, timeout=120):
            result = inner(command, timeout)
            if command[0] != "exiv2":
                (self.work / "s00000.ours.xmp").unlink()
                (self.work / "ours.tsv").write_text(
                    "s00000\tto-xmp\trefused InvalidJpeg\ta reason\ns00000\tto-exif\twritten\tExif.A -> Xmp.b: a note | Exif.C -> Xmp.d: another\n",
                    encoding="utf-8")
            return result
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            code = x.main([str(self.corpus), str(self.work), "--exiv2", "exiv2", "--fixture", "fixture --"], refusing)
        text = out.getvalue()
        self.assertEqual(0, code, text)
        self.assertIn("EXIF to XMP::0 files compared of 1 prepared (1 not rewritten by the tool); "
                      "left out, not read by this library: 1 refused InvalidJpeg; values: same 0", text)
        self.assertIn("1 file(s), first s00000: to-xmp refused InvalidJpeg: a reason", text)
        self.assertIn("1 file(s), first s00000: to-exif Exif.C -> Xmp.d: another", text)

    def test_a_failing_fixture_generator_fails(self):
        def failing(command, timeout=120):
            return (3, "", "boom") if command[0] != "exiv2" else self.runner(command, timeout)
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            code = x.main([str(self.corpus), str(self.work), "--exiv2", "exiv2", "--fixture", "fixture --"], failing)
        self.assertEqual(1, code)
        self.assertIn("the fixture generator failed (exit 3)", out.getvalue())


if __name__ == "__main__":
    unittest.main()
