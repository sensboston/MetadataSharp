"""Offline checks for the rule that the pinned tool prints every IPTC dataset an edit wrote.

The tool itself is not run here: its print-outs are authored strings.
"""
import contextlib
import importlib.util
import io
import tempfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location(
    "iptc_edits", Path(__file__).parents[2] / "tools" / "reference" / "iptc_edits.py")
x = importlib.util.module_from_spec(spec)
spec.loader.exec_module(x)

PRINTED = (
    "Exif.Image.Make                              Ascii      12  TestCompany\n"
    "Iptc.Envelope.CharacterSet                   String      3  \x1b%G\n"
    "Iptc.Application2.RecordVersion              Short       1  4\n"
    "Iptc.Application2.Keywords                   String      7  harbour\n"
    "Iptc.Application2.Keywords                   String      4  dawn\n"
    "Iptc.Application2.DateCreated                Date        8  2026-11-12\n"
    "Iptc.Application2.Caption                    String      9  two words\n"
    "Xmp.xmp.Rating                               XmpText     1  4\n"
)
EXPECTED = [
    "Iptc.Envelope.CharacterSet String 3 *",
    "Iptc.Application2.RecordVersion Short 1 4",
    "Iptc.Application2.Keywords String 7 harbour",
    "Iptc.Application2.Keywords String 4 dawn",
    "Iptc.Application2.DateCreated Date 8 2026-11-12",
    "Iptc.Application2.Caption String 9 two words",
]


class CompareTests(unittest.TestCase):
    def test_a_print_out_of_exactly_the_written_datasets_passes(self):
        self.assertEqual([], x.compare(PRINTED, EXPECTED))

    def test_the_order_of_the_lines_is_not_compared(self):
        self.assertEqual([], x.compare(PRINTED, list(reversed(EXPECTED))))

    def test_a_value_that_differs_is_a_problem_on_both_sides(self):
        problems = x.compare(PRINTED.replace("dawn", "dusk"), EXPECTED)
        self.assertEqual(["not printed: Iptc.Application2.Keywords String 4 dawn",
                          "printed, not written: Iptc.Application2.Keywords String 4 dusk"], problems)

    def test_an_occurrence_that_is_missing_is_a_problem(self):
        lines = [line for line in PRINTED.splitlines() if "harbour" not in line]
        self.assertEqual(["not printed: Iptc.Application2.Keywords String 7 harbour"],
                         x.compare("\n".join(lines), EXPECTED))

    def test_an_occurrence_printed_twice_is_a_problem(self):
        problems = x.compare(PRINTED + "Iptc.Application2.Keywords  String  4  dawn\n", EXPECTED)
        self.assertEqual(["printed, not written: Iptc.Application2.Keywords String 4 dawn"], problems)

    def test_a_dataset_nobody_wrote_is_a_problem(self):
        problems = x.compare(PRINTED + "Iptc.Application2.City  String  4  Riga\n", EXPECTED)
        self.assertEqual(["printed, not written: Iptc.Application2.City String 4 Riga"], problems)

    def test_any_value_still_needs_the_key_the_type_and_the_count(self):
        lines = [line for line in PRINTED.splitlines() if "CharacterSet" not in line]
        self.assertEqual(["not printed: Iptc.Envelope.CharacterSet String 3 *"],
                         x.compare("\n".join(lines), EXPECTED))

    def test_other_families_are_not_looked_at(self):
        self.assertEqual([], x.compare(PRINTED + "Xmp.dc.format  XmpText  10  image/jpeg\n", EXPECTED))


class ManifestTests(unittest.TestCase):
    def test_rows_are_a_file_and_its_lines(self):
        entries = x.parse_manifest("a.jpg\tIptc.Application2.City String 4 Riga|Iptc.Application2.Urgency String 1 5\n")
        self.assertEqual([("a.jpg", ["Iptc.Application2.City String 4 Riga", "Iptc.Application2.Urgency String 1 5"])], entries)

    def test_a_manifest_without_files_or_columns_is_refused(self):
        with self.assertRaises(ValueError):
            x.parse_manifest("")
        with self.assertRaises(ValueError):
            x.parse_manifest("a.jpg\n")


class CheckTests(unittest.TestCase):
    def directory(self):
        folder = tempfile.TemporaryDirectory()
        self.addCleanup(folder.cleanup)
        Path(folder.name, "manifest.tsv").write_text("a.jpg\t" + "|".join(EXPECTED) + "\n", encoding="utf-8")
        return folder.name

    def test_both_print_modes_are_run_for_every_file(self):
        calls = []

        def runner(command, path):
            calls.append((" ".join(command[1:]), path.name))
            return 0, PRINTED, ""

        problems, count = x.check(self.directory(), "exiv2", runner)
        self.assertEqual(([], 1), (problems, count))
        self.assertEqual([("-u -pi", "a.jpg"), ("-u -pa", "a.jpg")], calls)

    def test_a_file_the_tool_refuses_is_a_problem(self):
        problems, _ = x.check(self.directory(), "exiv2", lambda command, path: (1, "", "corrupted"))
        self.assertEqual([("a.jpg", "-pi: not read (exit 1): corrupted"), ("a.jpg", "-pa: not read (exit 1): corrupted")], problems)

    def test_the_first_problem_of_a_file_carries_the_print_out(self):
        problems, _ = x.check(self.directory(), "exiv2", lambda command, path: (0, "Iptc.Application2.City  String  4  Riga\n", ""))
        self.assertIn("printed:\nIptc.Application2.City String 4 Riga", problems[0][1])

    def test_main_reports_and_fails_or_notes_and_passes(self):
        folder = self.directory()
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            self.assertEqual(0, x.main([folder, "--exiv2", "exiv2"], lambda command, path: (0, PRINTED, "")))
        self.assertIn("::notice title=IPTC edits::1 files", out.getvalue())
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            self.assertEqual(1, x.main([folder, "--exiv2", "exiv2"], lambda command, path: (1, "", "no")))
        self.assertIn("::error title=IPTC edits a.jpg::", out.getvalue())


if __name__ == "__main__":
    unittest.main()
