"""Offline checks for the rule that the pinned tool lists the chunks a PNG edit was to leave.

The tool itself is not run here: its print-outs are authored text in the format of exiv2 -pS.
"""
import contextlib
import importlib.util
import io
import tempfile
import unittest
import zlib
from pathlib import Path

spec = importlib.util.spec_from_file_location(
    "png_chunk_edits", Path(__file__).parents[2] / "tools" / "reference" / "png_chunk_edits.py")
p = importlib.util.module_from_spec(spec)
spec.loader.exec_module(p)


def row(kind, data, address=0):
    """One row as the tool prints it: unprintable bytes as dots, a zero that ends the first 30 bytes left out."""
    shown = data[:30]
    text = "".join("." if byte < 32 or byte >= 127 else chr(byte)
                   for index, byte in enumerate(shown) if not (byte == 0 and index == len(shown) - 1))
    return f"{address:8d} | {kind:<5s} |{len(data):8d} | {text:<30s} | 0x{zlib.crc32(kind.encode() + data):08x}"


def printed(*rows):
    lines = ["STRUCTURE OF PNG FILE: a.png", " address | chunk |  length | data                           | checksum"]
    return ("\n".join(lines + [row(kind, data, 8 + 40 * index) for index, (kind, data) in enumerate(rows)]) + "\n").encode()


IHDR = ("IHDR", bytes([0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0]))
GAMA = ("gAMA", bytes([0, 1, 0x86, 0xA0]))
SRGB = ("sRGB", b"\0")
PLTE = ("PLTE", bytes(range(6)))
IDAT = ("IDAT", b"x\x9cc\xe0\x12\x91\x03\x00\x00\x81\x00\x3d")
TIME = ("tIME", bytes([7, 0xEA, 11, 11, 10, 9, 8]))
IEND = ("IEND", b"")
ICCP = ("iCCP", b"ICC Profile\0\0x\x9c" + bytes(range(40)))
OTHER_ICCP = ("iCCP", b"Adobe RGB\0\0x\x9c" + bytes(range(60)))


def text(comment):
    return "tEXt", b"Comment\0" + comment.encode("latin-1")


def wide(comment):
    return "iTXt", b"Comment\0\0\0\0\0" + comment.encode("utf-8")


def packed(payload=b"x\x9c\x01\x02"):
    return "zTXt", b"Comment\0\0" + payload


def table(*rows):
    return p.chunks(printed(*rows))


class ManifestTests(unittest.TestCase):
    def test_a_row_names_source_edited_kind_and_expectation(self):
        self.assertEqual(
            [("a.png", "b.png", "comment", ("tEXt", "Hi")), ("a.png", "c.png", "comment", ("iTXt", "\u00e9\u20ac")),
             ("a.png", "d.png", "comment", "-"), ("a.png", "e.png", "profile", "iCCP"), ("a.png", "f.png", "profile", "-")],
            p.parse_manifest("a.png\tb.png\tcomment\ttEXt:4869\n\na.png\tc.png\tcomment\tiTXt:c3a9e282ac\n"
                             "a.png\td.png\tcomment\t-\na.png\te.png\tprofile\tiCCP\na.png\tf.png\tprofile\t-\n"))

    def test_a_row_that_is_not_understood_is_an_error(self):
        for bad in ("a.png\tb.png\tcomment\n", "a.png\tb.png\t\t-\n", "a.png\tb.png\tcomment\tzTXt:4869\n",
                    "a.png\tb.png\tcomment\ttEXt:Hi\n", "a.png\tb.png\tcomment\tiTXt:ff\n", "a.png\tb.png\tcomment\ttEXt:\n",
                    "a.png\tb.png\tprofile\tsRGB\n", "a.png\tb.png\texif\t-\n", "\n"):
            with self.assertRaises(ValueError, msg=bad):
                p.parse_manifest(bad)


class PrintOutTests(unittest.TestCase):
    def test_the_rows_are_type_length_data_and_checksum(self):
        rows = table(IHDR, GAMA, IDAT, IEND)
        self.assertEqual(["IHDR", "gAMA", "IDAT", "IEND"], [found[0] for found in rows])
        self.assertEqual((4, f"{zlib.crc32(b'gAMA' + GAMA[1]):08x}"), (rows[1][1], rows[1][3]))
        self.assertEqual(30, len(rows[1][2]))

    def test_a_data_column_with_bars_and_spaces_is_still_one_column(self):
        rows = table(IHDR, text(" | 0x12345678 | tEXt |"), IDAT, IEND)
        self.assertEqual("tEXt", rows[1][0])
        self.assertTrue(p.is_comment(rows[1]))

    def test_a_print_out_that_is_no_whole_file_is_not_a_chunk_list(self):
        self.assertIsNone(p.chunks(b""))
        self.assertIsNone(p.chunks(b"Exiv2 exception in print action for file a.png:\n"))
        self.assertIsNone(p.chunks(printed(IHDR, IDAT)))
        self.assertIsNone(p.chunks(printed(GAMA, IDAT, IEND)))

    def test_a_comment_is_a_text_chunk_with_exactly_that_keyword(self):
        for chunk in (text("x"), wide("x"), packed(), ("tEXt", b"Comment\0")):
            self.assertTrue(p.is_comment(table(IHDR, chunk, IDAT, IEND)[1]), chunk)
        for chunk in (("tEXt", b"Description\0x"), ("tEXt", b"Commentary\0x"), ("tEXt", b"comment\0x"),
                      ("tEXt", b"Comment"), ("eXIf", b"Comment\0x")):
            self.assertFalse(p.is_comment(table(IHDR, chunk, IDAT, IEND)[1]), chunk)

    def test_the_expected_chunk_is_built_from_the_text(self):
        self.assertEqual(("tEXt", 12, f"{zlib.crc32(b'tEXtComment' + bytes([0]) + b'caf' + bytes([0xE9])):08x}"),
                         p.comment_chunk("tEXt", "caf\u00e9"))
        self.assertEqual(("iTXt", 17, f"{zlib.crc32(b'iTXtComment' + bytes(5) + b'caf' + bytes([0xC3, 0xA9])):08x}"),
                         p.comment_chunk("iTXt", "caf\u00e9"))


class CommentTests(unittest.TestCase):
    SOURCE = (IHDR, GAMA, IDAT, TIME, IEND)

    def compare(self, expected, source, edited):
        return p.compare_comment(expected, table(*source), table(*edited))

    def test_an_inserted_comment_before_the_image_data_passes(self):
        self.assertEqual([], self.compare(("tEXt", "Hi"), self.SOURCE, (IHDR, GAMA, text("Hi"), IDAT, TIME, IEND)))
        self.assertEqual([], self.compare(("iTXt", "\u20ac"), self.SOURCE, (IHDR, GAMA, wide("\u20ac"), IDAT, TIME, IEND)))

    def test_another_text_another_type_or_another_encoding_is_a_problem(self):
        for edited in (text("Ho"), wide("Hi"), packed()):
            problems = self.compare(("tEXt", "Hi"), self.SOURCE, (IHDR, GAMA, edited, IDAT, TIME, IEND))
            self.assertEqual(1, len(problems), edited)
            self.assertIn("expected a tEXt of 10 bytes", problems[0])

    def test_a_replaced_comment_stands_where_the_first_stood(self):
        source = (IHDR, packed(), GAMA, IDAT, wide("two"), TIME, IEND)
        self.assertEqual([], self.compare(("tEXt", "Hi"), source, (IHDR, text("Hi"), GAMA, IDAT, TIME, IEND)))
        problems = self.compare(("tEXt", "Hi"), source, (IHDR, GAMA, text("Hi"), IDAT, TIME, IEND))
        self.assertEqual(["the comment does not stand where the first comment of the source stood"], problems)

    def test_an_inserted_comment_elsewhere_is_a_problem(self):
        for edited in ((IHDR, text("Hi"), GAMA, IDAT, TIME, IEND), (IHDR, GAMA, IDAT, text("Hi"), TIME, IEND),
                       (IHDR, GAMA, IDAT, TIME, text("Hi"), IEND)):
            self.assertEqual(["the comment does not stand directly before the first IDAT chunk"],
                             self.compare(("tEXt", "Hi"), self.SOURCE, edited))

    def test_two_comments_or_none_where_one_was_to_be_is_a_problem(self):
        self.assertIn("0 chunks", self.compare(("tEXt", "Hi"), self.SOURCE, self.SOURCE)[0])
        self.assertIn("2 chunks", self.compare(("tEXt", "Hi"), self.SOURCE, (IHDR, GAMA, text("Hi"), text("Hi"), IDAT, TIME, IEND))[0])

    def test_a_removal_leaves_no_comment_and_every_other_chunk(self):
        source = (IHDR, packed(), GAMA, IDAT, wide("two"), TIME, IEND)
        self.assertEqual([], self.compare("-", source, self.SOURCE))
        self.assertEqual(["1 chunk(s) with the keyword Comment are left"], self.compare("-", source, (IHDR, GAMA, IDAT, wide("two"), TIME, IEND)))

    def test_another_chunk_that_changed_went_or_came_is_a_problem(self):
        changed = (IHDR, ("gAMA", bytes([0, 0, 0xB1, 0x8F])), text("Hi"), IDAT, TIME, IEND)
        self.assertIn("chunk 2 of the others was gAMA of 4 bytes", self.compare(("tEXt", "Hi"), self.SOURCE, changed)[0])
        self.assertIn("was tIME of 7 bytes", self.compare(("tEXt", "Hi"), self.SOURCE, (IHDR, GAMA, text("Hi"), IDAT, IEND))[0])
        self.assertIn("is sRGB of 1 bytes", self.compare("-", self.SOURCE, (IHDR, GAMA, SRGB, IDAT, TIME, IEND))[0])
        self.assertIn("the chunks that are no comment changed", self.compare("-", self.SOURCE, (IHDR, IDAT, GAMA, TIME, IEND))[0])

    def test_a_list_that_only_ends_sooner_or_later_is_told_as_fewer_or_more(self):
        one, two = ("IHDR", 13, "00000001"), ("gAMA", 4, "00000002")
        self.assertEqual("1 chunk(s) fewer than the edit names, the first a gAMA of 4 bytes", p.first_difference([one, two], [one]))
        self.assertEqual("1 chunk(s) more than the edit names, the first a gAMA of 4 bytes", p.first_difference([one], [one, two]))

    def test_a_text_chunk_of_a_text_outside_its_encoding_is_reported_not_raised(self):
        problems = self.compare(("tEXt", "\u20ac"), self.SOURCE, (IHDR, GAMA, wide("\u20ac"), IDAT, TIME, IEND))
        self.assertEqual(["the manifest asks for a tEXt chunk of a text that is not ISO-8859-1"], problems)


class ProfileTests(unittest.TestCase):
    SOURCE = (IHDR, GAMA, PLTE, IDAT, TIME, IEND)

    def compare(self, expected, source, edited):
        return p.compare_profile(expected, table(*source), table(*edited))

    def test_an_inserted_profile_directly_after_the_header_passes(self):
        self.assertEqual([], self.compare("iCCP", self.SOURCE, (IHDR, ICCP, GAMA, PLTE, IDAT, TIME, IEND)))

    def test_an_inserted_profile_elsewhere_is_a_problem(self):
        self.assertEqual(["the iCCP chunk does not stand directly after IHDR"],
                         self.compare("iCCP", self.SOURCE, (IHDR, GAMA, ICCP, PLTE, IDAT, TIME, IEND)))
        problems = self.compare("iCCP", self.SOURCE, (IHDR, GAMA, PLTE, ICCP, IDAT, TIME, IEND))
        self.assertEqual(["the iCCP chunk follows PLTE or IDAT", "the iCCP chunk does not stand directly after IHDR"], problems)

    def test_the_srgb_chunk_goes_with_a_written_profile_and_stays_with_a_removed_one(self):
        source = (IHDR, SRGB, GAMA, IDAT, IEND)
        self.assertEqual([], self.compare("iCCP", source, (IHDR, ICCP, GAMA, IDAT, IEND)))
        self.assertIn("is sRGB of 1 bytes", self.compare("iCCP", source, (IHDR, ICCP, SRGB, GAMA, IDAT, IEND))[0])
        both = (IHDR, SRGB, OTHER_ICCP, GAMA, IDAT, IEND)
        self.assertEqual([], self.compare("-", both, source))
        self.assertIn("was sRGB of 1 bytes", self.compare("-", both, (IHDR, GAMA, IDAT, IEND))[0])

    def test_a_replaced_profile_stands_where_the_old_one_stood(self):
        source = (IHDR, GAMA, OTHER_ICCP, PLTE, IDAT, IEND)
        self.assertEqual([], self.compare("iCCP", source, (IHDR, GAMA, ICCP, PLTE, IDAT, IEND)))
        self.assertEqual(["the iCCP chunk does not stand where that of the source stood"],
                         self.compare("iCCP", source, (IHDR, ICCP, GAMA, PLTE, IDAT, IEND)))
        # The sRGB chunk that went does not count for the place.
        self.assertEqual([], self.compare("iCCP", (IHDR, SRGB, GAMA, OTHER_ICCP, IDAT, IEND), (IHDR, GAMA, ICCP, IDAT, IEND)))

    def test_the_chunk_carries_the_name_this_library_writes(self):
        problems = self.compare("iCCP", self.SOURCE, (IHDR, OTHER_ICCP, GAMA, PLTE, IDAT, TIME, IEND))
        self.assertEqual(1, len(problems))
        self.assertIn("is not named 'ICC Profile'", problems[0])

    def test_two_profiles_or_none_where_one_was_to_be_is_a_problem(self):
        self.assertEqual(["0 iCCP chunks, expected one"], self.compare("iCCP", self.SOURCE, self.SOURCE))
        self.assertEqual(["2 iCCP chunks, expected one"], self.compare("iCCP", self.SOURCE, (IHDR, ICCP, ICCP, GAMA, PLTE, IDAT, TIME, IEND)))

    def test_a_removal_leaves_no_profile_and_every_other_chunk(self):
        source = (IHDR, GAMA, OTHER_ICCP, PLTE, IDAT, TIME, IEND)
        self.assertEqual([], self.compare("-", source, self.SOURCE))
        self.assertEqual(["an iCCP chunk is left"], self.compare("-", source, source))
        self.assertIn("the chunks that are no profile changed", self.compare("-", source, (IHDR, GAMA, IDAT, TIME, IEND))[0])


class CheckTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.calls = []

    def check(self, manifest, files, skip=False):
        (Path(self.directory.name) / "chunks.tsv").write_text(manifest, encoding="utf-8")

        def runner(command, path):
            self.calls.append((command[1], Path(path).name))
            return files[Path(path).name]

        return p.check(self.directory.name, "exiv2", runner, skip)

    SOURCE = (0, printed(IHDR, GAMA, IDAT, IEND), "")

    def test_the_tool_is_asked_for_the_structure_and_the_source_once(self):
        files = {"a.png": self.SOURCE, "b.png": (0, printed(IHDR, GAMA, text("Hi"), IDAT, IEND), ""),
                 "c.png": (0, printed(IHDR, ICCP, GAMA, IDAT, IEND), "")}
        problems, checked, skipped = self.check("a.png\tb.png\tcomment\ttEXt:4869\na.png\tc.png\tprofile\tiCCP\n", files)
        self.assertEqual(([], 2, 0), (problems, checked, skipped))
        self.assertEqual([("-pS", "a.png"), ("-pS", "b.png"), ("-pS", "c.png")], self.calls)

    def test_a_problem_names_the_edited_file(self):
        files = {"a.png": self.SOURCE, "b.png": (0, printed(IHDR, GAMA, text("Ho"), IDAT, IEND), "")}
        problems, checked, _ = self.check("a.png\tb.png\tcomment\ttEXt:4869\n", files)
        self.assertEqual((1, "b.png", 1), (len(problems), problems[0][0], checked))

    def test_an_edited_file_the_tool_does_not_list_is_a_problem(self):
        for broken in ((1, b"", "Exiv2 exception"), (0, b"nothing\n", "")):
            problems, _, _ = self.check("a.png\tb.png\tcomment\t-\n", {"a.png": self.SOURCE, "b.png": broken})
            self.assertEqual(1, len(problems))
            self.assertIn("-pS does not print the structure", problems[0][1])

    def test_a_source_the_tool_does_not_list_is_a_failure_or_left_out_and_counted(self):
        files = {"a.png": (1, b"", "damaged"), "b.png": self.SOURCE}
        problems, checked, skipped = self.check("a.png\tb.png\tcomment\t-\n", files)
        self.assertEqual((1, "a.png", 0, 0), (len(problems), problems[0][0], checked, skipped))
        self.assertEqual(([], 0, 1), self.check("a.png\tb.png\tcomment\t-\n", files, skip=True))

    def test_main_reports_the_outcome_through_its_exit_code(self):
        (Path(self.directory.name) / "chunks.tsv").write_text("a.png\tb.png\tcomment\ttEXt:4869\n", encoding="utf-8")
        good = {"a.png": self.SOURCE, "b.png": (0, printed(IHDR, GAMA, text("Hi"), IDAT, IEND), "")}
        bad = dict(good, **{"b.png": self.SOURCE})
        for files, code, word in ((good, 0, "::notice title=PNG profile and comment chunks::1 edited PNG files"),
                                  (bad, 1, "::error title=PNG profile and comment chunks b.png::")):
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                result = p.main([self.directory.name, "--exiv2", "exiv2"], lambda command, path, files=files: files[Path(path).name])
            self.assertEqual(code, result)
            self.assertIn(word, out.getvalue())


if __name__ == "__main__":
    unittest.main()
