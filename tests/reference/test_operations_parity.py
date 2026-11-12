"""Offline checks for the comparison of this library's file operations with the pinned tool's.

Neither the tool nor the fixture generator is run here: a runner stands in for the tool.
"""
import importlib.util
import tempfile
import unittest
from collections import OrderedDict
from pathlib import Path

spec = importlib.util.spec_from_file_location(
    "operations_parity", Path(__file__).parents[2] / "tools" / "reference" / "operations_parity.py")
x = importlib.util.module_from_spec(spec)
spec.loader.exec_module(x)


def lines(**keys):
    return OrderedDict((key.replace("_", "."), value) for key, value in keys.items())


class PrintOutTests(unittest.TestCase):
    def printed(self, code, output):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "a.jpg"
            path.write_bytes(b"x")
            return x.printed("exiv2", path, lambda command, **_: (code, output, ""))

    def test_a_key_holds_its_lines_without_the_key_and_with_single_spaces(self):
        found = self.printed(0, "Exif.Image.Make   Ascii   6  Canon\nIptc.Application2.Keywords String 1 a\n"
                                "Iptc.Application2.Keywords String 1 b\nWarning: something\n")
        self.assertEqual({"Exif.Image.Make": ["Ascii 6 Canon"], "Iptc.Application2.Keywords": ["String 1 a", "String 1 b"]}, dict(found))

    def test_a_file_without_metadata_is_an_empty_print_out_and_a_failure_is_none(self):
        self.assertEqual(OrderedDict(), self.printed(253, ""))
        self.assertIsNone(self.printed(1, ""))

    def test_a_file_that_is_not_there_is_none(self):
        self.assertIsNone(x.printed("exiv2", "/nowhere/a.jpg", lambda command, **_: (0, "", "")))


class CompareTests(unittest.TestCase):
    def kind(self, theirs, ours, source=None):
        rows = x.compare(lines(K=theirs) if theirs else lines(), lines(K=ours) if ours else lines(),
                         lines(K=source) if source else lines())
        return rows[0][1]

    def test_the_source_says_who_changed_a_key(self):
        self.assertEqual("same", self.kind(["a"], ["a"], ["b"]))
        self.assertEqual("differs (tool changed)", self.kind(["a"], ["b"], ["b"]))
        self.assertEqual("differs (ours changed)", self.kind(["a"], ["b"], ["a"]))
        self.assertEqual("differs", self.kind(["a"], ["b"], ["c"]))
        self.assertEqual("differs", self.kind(["a"], ["b"]))

    def test_the_source_says_who_dropped_or_added_a_key(self):
        self.assertEqual("ours only (tool removed)", self.kind(None, ["a"], ["a"]))
        self.assertEqual("ours only", self.kind(None, ["a"]))
        self.assertEqual("ours only", self.kind(None, ["a"], ["b"]))
        self.assertEqual("tool only (ours removed)", self.kind(["a"], None, ["a"]))
        self.assertEqual("tool only (tool added)", self.kind(["a"], None))
        self.assertEqual("tool only", self.kind(["a"], None, ["b"]))

    def test_values_no_edit_writes_are_named(self):
        self.assertEqual("tool only (no elements)", self.kind(["Ascii 0"], None, ["Ascii 0"]))
        self.assertEqual("tool only (undefined type)", self.kind(["0x000e 12 0 0"], None, ["0x000e 12 0 0"]))

    def test_layout_keys_are_no_rows(self):
        key = next(iter(x.LAYOUT))
        self.assertEqual([], x.compare(OrderedDict({key: ["1"]}), OrderedDict({key: ["2"]})))


class DocumentedTests(unittest.TestCase):
    def test_a_key_the_operation_addresses_is_never_excused_by_the_source(self):
        self.assertTrue(x.addressed("fixiso", "Exif.Photo.ISOSpeedRatings"))
        self.assertIsNone(x.documented("fixiso", "Exif.Photo.ISOSpeedRatings", "differs (tool changed)"))
        self.assertTrue(x.addressed("delete-xc", "Xmp.dc.title"))
        self.assertIsNone(x.documented("delete-xc", "Xmp.dc.title", "ours only (tool removed)"))
        self.assertIsNone(x.documented("thumbnail", "Exif.Thumbnail.Orientation", "ours only (tool removed)"))

    def test_what_the_tool_did_to_a_key_the_operation_does_not_address_is_intended(self):
        self.assertFalse(x.addressed("fixiso", "Exif.Image.Make"))
        for outcome in ("differs (tool changed)", "ours only (tool removed)", "tool only (tool added)"):
            self.assertIsNotNone(x.documented("fixiso", "Exif.Image.Make", outcome))

    def test_what_this_library_did_to_a_key_it_does_not_address_is_not_intended(self):
        for outcome in ("differs (ours changed)", "tool only (ours removed)", "differs", "ours only", "tool only"):
            self.assertIsNone(x.documented("fixiso", "Exif.Image.Make", outcome))
        # An extract addresses nothing: whatever is lost on the way is a difference.
        self.assertIsNone(x.documented("extract", "Exif.Image.Make", "tool only (ours removed)"))

    def test_named_differences_carry_their_reason(self):
        self.assertIn("MakerNote", x.documented("modify", "Exif.Photo.MakerNote", "differs (tool changed)"))
        self.assertIsNotNone(x.documented("insert", "Exif.(MakerNote).*", "tool only (ours removed)"))
        self.assertIsNotNone(x.documented("insert", "Iptc.Application2.Byline", "tool only (ours removed)"))
        self.assertIsNone(x.documented("insert", "Exif.Image.Make", "tool only (ours removed)"))

    def test_every_reason_is_a_sentence_and_every_operation_is_known(self):
        for (operation, key, outcome), reason in x.DOCUMENTED.items():
            self.assertIn(operation, list(x.OPERATIONS) + ["rename"])
            self.assertGreater(len(reason), 20, (operation, key, outcome))


class SummaryTests(unittest.TestCase):
    def test_the_tags_of_a_makernote_group_are_one_key(self):
        self.assertEqual("Exif.(MakerNote).*", x.grouped_key("extract", "Exif.Nikon3.Preview"))
        self.assertEqual("Exif.Image.Make", x.grouped_key("extract", "Exif.Image.Make"))
        self.assertEqual("Xmp.dc.title", x.grouped_key("extract", "Xmp.dc.title"))

    def test_outcomes_are_counted_per_key_with_the_first_example_and_same_is_left_out(self):
        rows = [("modify", "s1", "Exif.Image.Make", "differs", ["a"], ["b"]),
                ("modify", "s2", "Exif.Image.Make", "differs", ["c"], ["d"]),
                ("modify", "s2", "Exif.Image.Model", "same", ["c"], ["c"])]
        table = x.summarise(rows)
        self.assertEqual([("modify", "Exif.Image.Make", "differs")], list(table))
        count, example = table[("modify", "Exif.Image.Make", "differs")]
        self.assertEqual(2, count)
        self.assertIn("s1 Exif.Image.Make", example)


if __name__ == "__main__":
    unittest.main()
