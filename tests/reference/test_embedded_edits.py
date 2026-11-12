"""Offline checks for the rule that the pinned tool prints the profile and the comment a file was given.

The tool itself is not run here: its print-outs are authored bytes.
"""
import contextlib
import importlib.util
import io
import tempfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location(
    "embedded_edits", Path(__file__).parents[2] / "tools" / "reference" / "embedded_edits.py")
e = importlib.util.module_from_spec(spec)
spec.loader.exec_module(e)

PROFILE = bytes(range(200))
OTHER = bytes(reversed(range(200)))
KEYS = b"Exif.Image.Artist  Ascii  6  Alice\nXmp.xmp.CreatorTool  XmpText  13  MetadataSharp\n"


def ok(output):
    return 0, output, ""


class ManifestTests(unittest.TestCase):
    def test_a_row_names_source_edited_profile_and_comment(self):
        self.assertEqual([("a.jpg", "b.jpg", "p.icc", b"Hi"), ("a.jpg", "c.jpg", "-", "="), ("a.jpg", "d.jpg", "=", "-")],
                         e.parse_manifest("a.jpg\tb.jpg\tp.icc\t4869\n\na.jpg\tc.jpg\t-\t=\na.jpg\td.jpg\t=\t-\n"))

    def test_a_short_row_an_empty_field_a_comment_that_is_no_hexadecimal_or_an_empty_manifest_is_an_error(self):
        for text in ("a.jpg\tb.jpg\tp.icc\n", "a.jpg\tb.jpg\t\t-\n", "a.jpg\tb.jpg\t-\tHi\n", "\n"):
            with self.assertRaises(ValueError):
                e.parse_manifest(text)


class CompareTests(unittest.TestCase):
    def compare(self, profile=ok(PROFILE), comment=ok(b"Hi\n"), keys=ok(KEYS), source_keys=ok(KEYS)):
        return e.compare(PROFILE, b"Hi", source_keys, profile, comment, keys)

    def test_the_profile_the_comment_and_the_unchanged_keys_pass(self):
        self.assertEqual([], self.compare())

    def test_the_profile_has_to_be_printed_byte_for_byte(self):
        self.assertEqual(["-pC prints 199 bytes 000102030405060708090a0b0c0d0e0f1011121314151617..., "
                          "expected 200 bytes 000102030405060708090a0b0c0d0e0f1011121314151617..."],
                         self.compare(profile=ok(PROFILE[:-1])))
        self.assertEqual(1, len(self.compare(profile=ok(b""))))
        self.assertEqual(1, len(self.compare(profile=ok(OTHER))))

    def test_the_comment_is_what_stands_before_the_one_line_break_the_tool_adds(self):
        self.assertEqual([], self.compare(comment=ok(b"Hi\n")))
        self.assertEqual(["-pc prints 3 bytes 48690a, expected 2 bytes 4869"], self.compare(comment=ok(b"Hi\n\n")))
        self.assertEqual(["-pc prints 0 bytes , expected 2 bytes 4869"], self.compare(comment=ok(b"\n")))
        self.assertEqual(["-pc prints no line: 2 bytes 4869"], self.compare(comment=ok(b"Hi")))

    def test_no_profile_and_no_comment_are_an_empty_print_out_and_an_empty_line(self):
        self.assertEqual([], e.compare(b"", b"", ok(KEYS), ok(b""), ok(b"\n"), ok(KEYS)))
        self.assertEqual(2, len(e.compare(b"", b"", ok(KEYS), ok(PROFILE), ok(b"Hi\n"), ok(KEYS))))

    def test_a_run_that_does_not_exit_with_zero_is_a_problem_whatever_it_printed(self):
        self.assertEqual(["-pC does not read the file (exit 1): Exiv2 exception"],
                         self.compare(profile=(1, PROFILE, "Exiv2 exception\n")))
        self.assertEqual(["-pc does not read the file (exit 1): Exiv2 exception"],
                         self.compare(comment=(1, b"Hi\n", "Exiv2 exception\n")))

    def test_the_keys_have_to_be_printed_as_for_the_source_with_the_same_exit_code(self):
        fewer = KEYS.splitlines(keepends=True)[1]
        self.assertEqual(["-pa prints 1 line(s) fewer and 0 more than for the source: Exif.Image.Artist  Ascii  6  Alice"],
                         self.compare(keys=ok(fewer)))
        self.assertEqual(["-pa prints 0 line(s) fewer and 1 more than for the source: Iptc.Application2.Caption  String  1  x"],
                         self.compare(keys=ok(KEYS + b"Iptc.Application2.Caption  String  1  x\n")))
        self.assertEqual(["-pa exits with 253, with 0 for the source"], self.compare(keys=(253, KEYS, "")))
        # A file without Exif, IPTC or XMP data: the tool prints nothing and does not exit with 0, before and after.
        none = (253, b"", "No Exif data found in the file")
        self.assertEqual([], self.compare(keys=none, source_keys=none))

    def test_a_key_that_holds_the_profile_is_left_out_of_the_comparison_and_counted(self):
        key = b"Exif.Image.InterColorProfile  Undefined  200  0 1 2 3\n"
        named = ("Exif.Image.InterColorProfile",)
        # Without the option the line is one more than the source has.
        self.assertEqual(1, len(self.compare(keys=ok(KEYS + key))))
        self.assertEqual([], e.compare(PROFILE, b"Hi", ok(KEYS), ok(PROFILE), ok(b"Hi\n"), ok(key + KEYS), named))
        self.assertEqual([], e.compare(PROFILE, b"Hi", ok(key + KEYS), ok(PROFILE), ok(b"Hi\n"), ok(KEYS + key), named))
        self.assertEqual([], e.compare(b"", b"Hi", ok(KEYS + key), ok(b""), ok(b"Hi\n"), ok(KEYS), named))
        self.assertEqual(["-pa prints 0 line(s) of Exif.Image.InterColorProfile, expected 1"],
                         e.compare(PROFILE, b"Hi", ok(KEYS), ok(PROFILE), ok(b"Hi\n"), ok(KEYS), named))
        self.assertEqual(["-pa prints 1 line(s) of Exif.Image.InterColorProfile, expected 0"],
                         e.compare(b"", b"Hi", ok(KEYS), ok(b""), ok(b"Hi\n"), ok(KEYS + key), named))
        self.assertEqual(["-pa prints 2 line(s) of Exif.Image.InterColorProfile, expected 1"],
                         e.compare(PROFILE, b"Hi", ok(KEYS), ok(PROFILE), ok(b"Hi\n"), ok(key + KEYS + key), named))
        # Another line is still a difference, and a key that only begins like the named one is another line.
        other = b"Exif.Image.InterColorProfileX  Undefined  1  0\n"
        self.assertEqual(1, len(e.compare(PROFILE, b"Hi", ok(KEYS), ok(PROFILE), ok(b"Hi\n"), ok(KEYS + key + other), named)))
        # Where the tool does not exit with 0, before and after, the lines are not counted.
        self.assertEqual([], e.compare(PROFILE, b"Hi", (253, KEYS, ""), ok(PROFILE), ok(b"Hi\n"), (253, KEYS, ""), named))

    def test_one_of_several_keys_counts(self):
        named = ("Exif.Image.InterColorProfile", "Exif.PanasonicRaw.0x8773")
        key = b"Exif.PanasonicRaw.0x8773  Undefined  200  0 1 2 3\n"
        self.assertEqual([], e.compare(PROFILE, b"Hi", ok(KEYS), ok(PROFILE), ok(b"Hi\n"), ok(KEYS + key), named))
        self.assertEqual(["-pa prints 0 line(s) of Exif.Image.InterColorProfile or Exif.PanasonicRaw.0x8773, expected 1"],
                         e.compare(PROFILE, b"Hi", ok(KEYS), ok(PROFILE), ok(b"Hi\n"), ok(KEYS), named))

    def test_the_line_before_the_profile_of_an_orf_or_rw2_file_is_no_part_of_it(self):
        self.assertEqual(PROFILE, e.printed_profile("a.orf", b"ORF IMAGE\n" + PROFILE))
        self.assertEqual(b"", e.printed_profile("a.ORF", b"ORF IMAGE\n"))
        self.assertEqual(PROFILE, e.printed_profile("a.rw2", b"RW2 IMAGE\n" + PROFILE))
        # Only of such a file, and only its own line.
        self.assertEqual(b"ORF IMAGE\n" + PROFILE, e.printed_profile("a.tif", b"ORF IMAGE\n" + PROFILE))
        self.assertEqual(b"RW2 IMAGE\n" + PROFILE, e.printed_profile("a.orf", b"RW2 IMAGE\n" + PROFILE))
        self.assertEqual(PROFILE, e.printed_profile("a.orf", PROFILE))

    def test_the_same_lines_in_another_order_are_a_difference(self):
        swapped = b"".join(reversed(KEYS.splitlines(keepends=True)))
        self.assertEqual(["-pa prints 0 line(s) fewer and 0 more than for the source: the same lines in another order"],
                         self.compare(keys=ok(swapped)))


class CheckTests(unittest.TestCase):
    def run_check(self, row, outputs, skip_unreadable=False):
        """outputs: by file name, the three results of -pC, -pc and -pa."""
        asked = []

        def runner(command, path):
            asked.append((command[0], command[1], path.name))
            return outputs[path.name][("-pC", "-pc", "-pa").index(command[1])]

        with tempfile.TemporaryDirectory() as directory:
            (Path(directory) / "manifest.tsv").write_text(row, encoding="utf-8")
            (Path(directory) / "p.icc").write_bytes(PROFILE)
            result = e.check(directory, "tool", runner, skip_unreadable)
        self.assertTrue(all(tool == "tool" for tool, _, _ in asked))
        return result

    SOURCE = (ok(OTHER), ok(b"Old\n"), ok(KEYS))

    def test_a_given_profile_and_comment_pass(self):
        self.assertEqual(([], 1, 0), self.run_check("a.jpg\tb.jpg\tp.icc\t4869\n",
                                                    {"a.jpg": self.SOURCE, "b.jpg": (ok(PROFILE), ok(b"Hi\n"), ok(KEYS))}))

    def test_the_equals_sign_is_what_the_tool_prints_for_the_source(self):
        row = "a.jpg\tb.jpg\t=\t=\n"
        self.assertEqual(([], 1, 0), self.run_check(row, {"a.jpg": self.SOURCE, "b.jpg": self.SOURCE}))
        problems, _, _ = self.run_check(row, {"a.jpg": self.SOURCE, "b.jpg": (ok(PROFILE), ok(b"Hi\n"), ok(KEYS))})
        self.assertEqual(["b.jpg", "b.jpg"], [name for name, _ in problems])
        self.assertIn("-pC prints", problems[0][1])
        self.assertIn("-pc prints 2 bytes 4869, expected 3 bytes 4f6c64", problems[1][1])

    def test_the_minus_sign_is_none(self):
        row = "a.jpg\tb.jpg\t-\t-\n"
        self.assertEqual(([], 1, 0), self.run_check(row, {"a.jpg": self.SOURCE, "b.jpg": (ok(b""), ok(b"\n"), ok(KEYS))}))
        problems, _, _ = self.run_check(row, {"a.jpg": self.SOURCE, "b.jpg": self.SOURCE})
        self.assertEqual(2, len(problems))

    def test_a_source_the_tool_cannot_read_is_a_failure_not_a_pass(self):
        unread = ((1, b"", "not an image"), ok(b"\n"), ok(KEYS))
        problems, checked, skipped = self.run_check("a.jpg\tb.jpg\tp.icc\t4869\n",
                                                    {"a.jpg": unread, "b.jpg": (ok(PROFILE), ok(b"Hi\n"), ok(KEYS))})
        self.assertEqual(([("a.jpg", "the source is not read with -pC")], 0, 0), (problems, checked, skipped))

    def test_a_file_that_was_never_readable_can_be_left_out_and_is_counted(self):
        row = "a.jpg\tb.jpg\tp.icc\t4869\n"
        unread = (ok(b""), (1, b"", "not an image"), ok(KEYS))
        self.assertEqual(([], 0, 1), self.run_check(row, {"a.jpg": unread, "b.jpg": unread}, True))
        # Leaving unreadable sources out does not excuse an edited file whose source did read.
        problems, checked, skipped = self.run_check(row, {"a.jpg": self.SOURCE, "b.jpg": unread}, True)
        self.assertEqual((2, 1, 0), (len(problems), checked, skipped))

    def test_the_profile_keys_and_the_format_line_reach_every_row(self):
        key = b"Exif.Image.InterColorProfile  Undefined  200  0 1 2 3\n"
        outputs = {"a.orf": (ok(b"ORF IMAGE\n"), ok(b"\n"), ok(KEYS)),
                   "b.orf": (ok(b"ORF IMAGE\n" + PROFILE), ok(b"\n"), ok(KEYS + key)),
                   "c.orf": (ok(b"ORF IMAGE\n"), ok(b"\n"), ok(KEYS))}
        asked = []

        def runner(command, path):
            asked.append(command[1])
            return outputs[path.name][("-pC", "-pc", "-pa").index(command[1])]

        with tempfile.TemporaryDirectory() as directory:
            (Path(directory) / "manifest.tsv").write_text("a.orf\tb.orf\tp.icc\t=\nb.orf\tc.orf\t-\t=\n", encoding="utf-8")
            (Path(directory) / "p.icc").write_bytes(PROFILE)
            self.assertEqual(([], 2, 0), e.check(directory, "tool", runner, profile_keys=("Exif.Image.InterColorProfile",)))
            problems, _, _ = e.check(directory, "tool", runner)
        self.assertEqual(["-pC", "-pc", "-pa"], asked[:3])
        self.assertEqual(["b.orf", "c.orf"], [name for name, _ in problems])

    def test_the_source_is_read_once_for_all_its_rows(self):
        calls = []

        def runner(command, path):
            calls.append((command[1], path.name))
            return ok(b"\n" if command[1] == "-pc" else b"")

        with tempfile.TemporaryDirectory() as directory:
            (Path(directory) / "manifest.tsv").write_text("a.jpg\tb.jpg\t-\t-\na.jpg\tc.jpg\t=\t=\n", encoding="utf-8")
            self.assertEqual(([], 2, 0), e.check(directory, "tool", runner))
        self.assertEqual(3, sum(1 for _, name in calls if name == "a.jpg"))
        self.assertEqual(9, len(calls))

    def test_main_reports_the_outcome_through_its_exit_code(self):
        with tempfile.TemporaryDirectory() as directory:
            (Path(directory) / "manifest.tsv").write_text("a.jpg\tb.jpg\t-\t-\n", encoding="utf-8")
            printed = io.StringIO()
            with contextlib.redirect_stdout(printed):
                passed = e.main([directory, "--exiv2", "tool"], lambda command, path: ok(b"\n" if command[1] == "-pc" else b""))
                failed = e.main([directory, "--exiv2", "tool", "--title", "Corpus"],
                                lambda command, path: ok(b"\n" if command[1] == "-pc" else b"") if path.name == "a.jpg" else (1, b"", "damaged"))
        self.assertEqual((0, 1), (passed, failed))
        self.assertIn("::notice title=JPEG profile and comment edits::1 edited files: the pinned tool prints the profile (-pC)",
                      printed.getvalue())
        self.assertIn("::error title=Corpus b.jpg::-pC does not read the file (exit 1): damaged", printed.getvalue())
        self.assertIn("3 problem(s) in 1 edited file(s)", printed.getvalue())

    def test_main_names_the_profile_keys_it_was_given(self):
        with tempfile.TemporaryDirectory() as directory:
            (Path(directory) / "manifest.tsv").write_text("a.tif\tb.tif\t-\t=\n", encoding="utf-8")
            printed = io.StringIO()
            with contextlib.redirect_stdout(printed):
                passed = e.main([directory, "--exiv2", "tool", "--profile-key", "Exif.Image.InterColorProfile", "--profile-key", "Exif.X.Y"],
                                lambda command, path: ok(b"\n" if command[1] == "-pc" else b""))
        self.assertEqual(0, passed)
        self.assertIn("for the source, but for Exif.Image.InterColorProfile and Exif.X.Y, which it prints where there is a profile "
                      "and nowhere else", printed.getvalue())


if __name__ == "__main__":
    unittest.main()
