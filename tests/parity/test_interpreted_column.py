"""Offline tests for the interpreted (human-readable) column of the parity harness.

The interpreted value is measured apart from the raw one. These cases pin both halves of that
sentence: the column is read from both sides and judged by its own rules, and no number the raw
comparison produces moves when it is present. Like the rest of ``tests/parity`` they run without a
corpus, a build, or the reference tool.
"""

from __future__ import annotations

import base64
import json
import subprocess
import sys
import tempfile
import unittest
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/parity"))
sys.path.insert(0, str(Path(__file__).resolve().parent))
import compare as compare_mod  # noqa: E402
import dump_format  # noqa: E402
import exiv2_print  # noqa: E402
import run_parity  # noqa: E402
from test_parity_harness import py_command, run_main, write_fake  # noqa: E402

#: What the reference prints for a small camera file: raw column, two spaces, interpreted column.
REFERENCE = (
    "Exif.Image.Make                              Ascii       6  NIKON  NIKON\n"
    "Exif.Image.XResolution                       Rational    1  72/1  72\n"
    "Exif.Image.ResolutionUnit                    Short       1  2  inch\n"
    "Exif.Photo.ExposureTime                      Rational    1  10/2500  1/250 s\n"
    "Exif.Photo.FNumber                           Rational    1  28/10  F2.8\n"
    "Exif.Photo.SensitivityType                   Short       1  2  2\n"
    "Exif.Photo.DateTimeOriginal                  Ascii      20  2011:06:27 21:35:33  2011:06:27 21:35:33\n"
    "Exif.Nikon3.Quality                          Ascii       8  FINE     FINE   \n"
    "Xmp.dc.format                                XmpText    10  image/jpeg  image/jpeg\n"
)

#: Our records for the same file. Standard EXIF records carry the sixth field; the rest do not.
RECORDS = [
    ["TAG", "Exif.Image.Make", "Ascii", 6, "NIKON", ""],
    ["TAG", "Exif.Image.XResolution", "Rational", 1, "72/1", "72"],
    ["TAG", "Exif.Image.ResolutionUnit", "Short", 1, "2", "inches"],
    ["TAG", "Exif.Photo.ExposureTime", "Rational", 1, "10/2500", "1/250 s"],
    ["TAG", "Exif.Photo.FNumber", "Rational", 1, "28/10", ""],
    ["TAG", "Exif.Photo.SensitivityType", "Short", 1, "2", "Recommended exposure index (REI)"],
    ["TAG", "Exif.Photo.DateTimeOriginal", "Ascii", 20, "2011:06:27 21:35:33", "2011:06:27 21:35:33"],
    ["TAG", "Exif.Nikon3.Quality", "Ascii", 8, "FINE"],
    ["TAG", "Xmp.dc.format", "XmpText", 10, "image/jpeg"],
    ["STATUS", "ok"],
]


def without_sixth_field(records: list[list]) -> list[list]:
    return [row[:5] if row[0] == "TAG" else row for row in records]


def parsed(reference: str = REFERENCE) -> dict[str, dict]:
    return exiv2_print.parse_reference(reference)[0]


def dumped(records: list[list] = RECORDS) -> tuple[dict, dict]:
    text = dump_format.record("FILE", "x") + "\n" + "".join(
        dump_format.record(*[str(field) for field in row]) + "\n" for row in records)
    block = dump_format.parse_dump(text)["x"]
    return block["tags"], block["interpreted"]


class DumpFormatInterpretedTests(unittest.TestCase):
    def test_the_sixth_field_is_kept_beside_the_raw_tuple_not_in_it(self):
        text = ("FILE\tx\n"
                "TAG\tExif.Photo.FNumber\tRational\t1\t28/10\tF2.8\n"
                "TAG\tExif.Image.Make\tAscii\t6\tNIKON\t\n"
                "TAG\tExif.Nikon3.Quality\tAscii\t8\tFINE\n"
                "STATUS\tok\n")
        block = dump_format.parse_dump(text)["x"]
        self.assertEqual(block["tags"]["Exif.Photo.FNumber"], ("Rational", 1, "28/10"))
        self.assertEqual(block["tags"]["Exif.Image.Make"], ("Ascii", 6, "NIKON"))
        self.assertEqual(block["order"], ["Exif.Photo.FNumber", "Exif.Image.Make", "Exif.Nikon3.Quality"])
        # An empty field is an answer ("no interpretation"); an absent one is no answer at all.
        self.assertEqual(block["interpreted"], {"Exif.Photo.FNumber": "F2.8", "Exif.Image.Make": ""})

    def test_a_five_field_dump_parses_exactly_as_it_always_did(self):
        text = "FILE\tx\nTAG\tExif.Image.0x0112\tShort\t1\t2\nSTATUS\tok\n"
        block = dump_format.parse_dump(text)["x"]
        self.assertEqual(block["tags"], {"Exif.Image.0x0112": ("Short", 1, "2")})
        self.assertEqual(block["interpreted"], {})

    def test_the_sixth_field_is_escaped_like_every_other(self):
        comment = "line one\nline\ttwo \\ three\x00"
        text = (dump_format.record("FILE", "x") + "\n"
                + dump_format.record("TAG", "Exif.Photo.UserComment", "Undefined", "30", "65 83", comment) + "\n"
                + dump_format.record("STATUS", "ok") + "\n")
        block = dump_format.parse_dump(text)["x"]
        self.assertEqual(block["interpreted"]["Exif.Photo.UserComment"], comment)
        self.assertEqual(block["tags"]["Exif.Photo.UserComment"], ("Undefined", 30, "65 83"))

    def test_a_record_with_a_seventh_field_is_still_an_error(self):
        for fields in ("TAG\tk\tShort\t1\t2\tinch\textra\n", "TAG\tk\tShort\t1\n"):
            with self.assertRaises(dump_format.DumpFormatError, msg=fields) as caught:
                dump_format.parse_dump("FILE\tx\n" + fields + "STATUS\tok\n")
            self.assertIn("5 fields", str(caught.exception))
            self.assertIn("6 with an interpreted value", str(caught.exception))

    def test_a_repeated_key_keeps_the_interpretation_of_its_first_record(self):
        text = ("FILE\tx\nTAG\tExif.Thumbnail.Compression\tShort\t1\t6\tfirst\n"
                "TAG\tExif.Thumbnail.Compression\tShort\t1\t1\tsecond\nSTATUS\tok\n")
        block = dump_format.parse_dump(text)["x"]
        self.assertEqual(block["tags"]["Exif.Thumbnail.Compression"], ("Short", 1, "6"))
        self.assertEqual(block["interpreted"]["Exif.Thumbnail.Compression"], "first")
        self.assertEqual(block["duplicates"], 1)


class ReferenceColumnTests(unittest.TestCase):
    def test_one_gap_separates_the_raw_value_from_the_interpreted_one(self):
        self.assertEqual(exiv2_print.split_columns("72/1  72"), ("72/1", "72"))
        self.assertEqual(exiv2_print.split_columns("2  inch"), ("2", "inch"))
        self.assertEqual(exiv2_print.split_columns("10/2500  1/250 s"), ("10/2500", "1/250 s"))
        self.assertEqual(exiv2_print.split_columns("51/1 106969/10000 0/1  51 deg 10' 41.81\""),
                         ("51/1 106969/10000 0/1", "51 deg 10' 41.81\""))
        # A raw value that ends in a blank widens the gap; it is still one gap.
        self.assertEqual(exiv2_print.split_columns("FINE     FINE"), ("FINE", "FINE"))

    def test_a_value_with_one_column_cannot_be_split(self):
        self.assertIsNone(exiv2_print.split_columns(""))
        self.assertIsNone(exiv2_print.split_columns("NIKON"))
        self.assertIsNone(exiv2_print.split_columns("48 50 51 48"))

    def test_text_with_runs_of_spaces_splits_only_where_it_is_printed_twice(self):
        self.assertEqual(exiv2_print.split_columns("a  b  a  b"), ("a  b", "a  b"))
        self.assertEqual(exiv2_print.split_columns("Canon EOS  5D  Canon EOS  5D"), ("Canon EOS  5D", "Canon EOS  5D"))
        # Three columns that are not a repetition could be cut in two places; neither is guessed.
        self.assertIsNone(exiv2_print.split_columns("1 2  3 4  5 6"))

    def test_parse_reference_adds_the_columns_and_leaves_the_printed_value_alone(self):
        tags = parsed()
        unit = tags["Exif.Image.ResolutionUnit"]
        self.assertEqual(unit["value"], "2  inch")
        self.assertEqual((unit["raw"], unit["interpreted"]), ("2", "inch"))
        date = tags["Exif.Photo.DateTimeOriginal"]
        self.assertEqual((date["raw"], date["interpreted"]), ("2011:06:27 21:35:33", "2011:06:27 21:35:33"))
        self.assertEqual(tags["Xmp.dc.format"]["interpreted"], "image/jpeg")

    def test_a_row_without_a_raw_value_has_no_columns_to_offer(self):
        tags, _ = exiv2_print.parse_reference(
            "mtf_camera.jpg   Exif.Photo.SensingMethod  Short 1  One-chip color area\n"
            "Exif.Pentax.PreviewOffset                    0x0000      0\n"
            "Xmp.cm2e.Family                              XmpBag      0  \n")
        for key in ("Exif.Photo.SensingMethod", "Exif.Pentax.PreviewOffset", "Xmp.cm2e.Family"):
            self.assertIsNone(tags[key]["raw"], key)
            self.assertIsNone(tags[key]["interpreted"], key)


class InterpretedComparisonTests(unittest.TestCase):
    def rows(self, records: list[list] = RECORDS) -> dict[str, dict]:
        tags, interpreted = dumped(records)
        return {entry["key"]: entry for entry in compare_mod.compare_tags(parsed(), tags, interpreted)}

    def test_the_same_text_on_both_sides_is_a_match(self):
        rows = self.rows()
        for key in ("Exif.Image.XResolution", "Exif.Photo.ExposureTime"):
            self.assertEqual(rows[key]["interpreted"], compare_mod.MATCH_INTERPRETED, key)
            self.assertTrue(rows[key]["translated"], key)

    def test_different_wording_is_a_difference_and_the_raw_outcome_does_not_notice(self):
        row = self.rows()["Exif.Image.ResolutionUnit"]
        self.assertEqual(row["interpreted"], compare_mod.INTERPRETED_DIFF)
        self.assertEqual(row["outcome"], compare_mod.MATCH_VALUE)
        self.assertEqual((row["our_interpreted"], row["their_interpreted"]), ("inches", "inch"))

    def test_no_interpretation_of_ours_is_its_own_outcome_and_never_a_difference(self):
        rows = self.rows()
        # The reference says F2.8 and we say nothing: a gap, recorded as one.
        self.assertEqual(rows["Exif.Photo.FNumber"]["interpreted"], compare_mod.NOT_INTERPRETED)
        self.assertTrue(rows["Exif.Photo.FNumber"]["translated"])
        self.assertEqual(rows["Exif.Photo.FNumber"]["their_interpreted"], "F2.8")
        # The reference repeats the raw value for Make: we say nothing either, and that is not a match.
        self.assertEqual(rows["Exif.Image.Make"]["interpreted"], compare_mod.NOT_INTERPRETED)
        self.assertFalse(rows["Exif.Image.Make"]["translated"])

    def test_a_field_of_blanks_is_no_interpretation(self):
        reference = parsed("Exif.Photo.FNumber                           Rational    1  28/10  F2.8\n")
        row = compare_mod.compare_tags(reference, {"Exif.Photo.FNumber": ("Rational", 1, "28/10")},
                                       {"Exif.Photo.FNumber": "  "})[0]
        self.assertEqual(row["interpreted"], compare_mod.NOT_INTERPRETED)

    def test_an_interpretation_the_reference_does_not_make_is_not_compared(self):
        rows = self.rows()
        # We name the sensitivity type and the reference prints the number it stored.
        self.assertEqual(rows["Exif.Photo.SensitivityType"]["interpreted"], compare_mod.REFERENCE_NOT_INTERPRETED)
        self.assertFalse(rows["Exif.Photo.SensitivityType"]["translated"])
        # Our text is the raw value and so is the reference's: neither says anything to compare.
        self.assertEqual(rows["Exif.Photo.DateTimeOriginal"]["interpreted"], compare_mod.REFERENCE_NOT_INTERPRETED)

    def test_a_record_without_the_field_is_not_asked(self):
        rows = self.rows()
        for key in ("Exif.Nikon3.Quality", "Xmp.dc.format"):
            self.assertNotIn("interpreted", rows[key], key)
            self.assertEqual(rows[key]["interpreted_skipped"], compare_mod.NOT_OFFERED, key)
            self.assertEqual(rows[key]["outcome"], compare_mod.MATCH_VALUE, key)

    def test_a_reference_row_whose_columns_cannot_be_separated_is_not_asked(self):
        reference = parsed("Exif.Photo.UserComment                       Undefined 264    \n"
                           "Exif.Image.Software                          Ascii      10  1 2  3 4  5 6\n")
        dumped_tags = {"Exif.Photo.UserComment": ("Undefined", 264, ""), "Exif.Image.Software": ("Ascii", 10, "1 2  3 4  5 6")}
        rows = compare_mod.compare_tags(reference, dumped_tags, {"Exif.Photo.UserComment": "", "Exif.Image.Software": ""})
        for row in rows:
            self.assertNotIn("interpreted", row, row["key"])
            self.assertEqual(row["interpreted_skipped"], compare_mod.NO_REFERENCE_COLUMN, row["key"])

    def test_surrounding_blanks_are_not_a_difference_but_inner_ones_are(self):
        reference = parsed("Exif.Image.Artist                            Ascii       6  A  B   A  B\n")
        self.assertEqual(reference["Exif.Image.Artist"]["interpreted"], "A  B")
        tags = {"Exif.Image.Artist": ("Ascii", 6, "A  B")}
        # The reference repeats its raw value here, so our text has nothing to be compared with.
        same = compare_mod.compare_tags(reference, tags, {"Exif.Image.Artist": "A  B "})[0]
        self.assertEqual(same["interpreted"], compare_mod.REFERENCE_NOT_INTERPRETED)
        translated = parsed("Exif.Photo.ExposureTime                      Rational    1  10/2500  1/250 s\n")
        time = {"Exif.Photo.ExposureTime": ("Rational", 1, "10/2500")}
        same = compare_mod.compare_tags(translated, time, {"Exif.Photo.ExposureTime": " 1/250 s "})[0]
        self.assertEqual(same["interpreted"], compare_mod.MATCH_INTERPRETED)
        other = compare_mod.compare_tags(translated, time, {"Exif.Photo.ExposureTime": "1/250  s"})[0]
        self.assertEqual(other["interpreted"], compare_mod.INTERPRETED_DIFF)

    def test_a_key_only_one_side_prints_gets_no_verdict(self):
        reference = parsed("Exif.Photo.FNumber                           Rational    1  28/10  F2.8\n")
        rows = {entry["key"]: entry for entry in compare_mod.compare_tags(
            reference, {"Exif.Photo.ISOSpeed": ("Long", 1, "100")}, {"Exif.Photo.ISOSpeed": "", "Exif.Photo.FNumber": "F2.8"})}
        self.assertEqual(rows["Exif.Photo.FNumber"]["outcome"], compare_mod.MISSING_KEY)
        self.assertEqual(rows["Exif.Photo.ISOSpeed"]["outcome"], compare_mod.EXTRA_KEY)
        for row in rows.values():
            self.assertNotIn("interpreted", row)
            self.assertNotIn("interpreted_skipped", row)

    def test_a_file_that_was_not_read_gets_no_verdicts(self):
        entry = compare_mod.compare_file("x", "exiv2", "jpg", "refused", "code", parsed(), {}, [], {"Exif.Image.Make": ""})
        self.assertTrue(all(row["outcome"] == compare_mod.NOT_READ for row in entry["rows"]))
        self.assertTrue(all("interpreted" not in row for row in entry["rows"]))
        self.assertEqual(compare_mod.aggregate([entry])["interpreted"]["compared"], 0)

    def test_the_raw_comparison_is_the_same_with_and_without_the_column(self):
        tags, interpreted = dumped()
        with_column = compare_mod.compare_tags(parsed(), tags, interpreted)
        without = compare_mod.compare_tags(parsed(), tags)
        raw_fields = ("key", "outcome", "their_type", "their_count", "their_value", "our_type", "our_count", "our_value", "flags")
        self.assertEqual([{name: row.get(name) for name in raw_fields} for row in with_column],
                         [{name: row.get(name) for name in raw_fields} for row in without])
        # Even a raw disagreement is judged on its own: the interpretation can still be the same.
        reference = parsed("Exif.Image.XResolution                       Rational    1  72/1  72\n")
        row = compare_mod.compare_tags(reference, {"Exif.Image.XResolution": ("Rational", 1, "144/2")},
                                       {"Exif.Image.XResolution": "72"})[0]
        self.assertEqual((row["outcome"], row["interpreted"]), (compare_mod.VALUE_DIFF, compare_mod.MATCH_INTERPRETED))

    def test_the_new_outcomes_are_not_raw_outcomes(self):
        self.assertEqual(compare_mod.INTERPRETED_OUTCOMES,
                         ("match-interpreted", "interpreted-diff", "not-interpreted", "reference-not-interpreted"))
        self.assertEqual(compare_mod.INTERPRETED_COMPARED, ("match-interpreted", "interpreted-diff"))
        for name in compare_mod.INTERPRETED_OUTCOMES:
            self.assertNotIn(name, compare_mod.OUTCOMES)


class InterpretedAggregateTests(unittest.TestCase):
    def entry(self, records: list[list] = RECORDS, label: str = "exiv2:a.jpg") -> dict:
        tags, interpreted = dumped(records)
        return compare_mod.compare_file(label, "exiv2", "jpg", "ok", "", parsed(), tags, [], interpreted)

    def test_the_rate_is_over_what_both_sides_interpret_and_the_rest_is_counted_beside_it(self):
        report = compare_mod.aggregate([self.entry()])
        section = report["interpreted"]
        self.assertEqual(section["outcomes"], {
            # XResolution and ExposureTime; ResolutionUnit.
            compare_mod.MATCH_INTERPRETED: 2, compare_mod.INTERPRETED_DIFF: 1,
            # Make and FNumber; SensitivityType and DateTimeOriginal.
            compare_mod.NOT_INTERPRETED: 2, compare_mod.REFERENCE_NOT_INTERPRETED: 2,
        })
        self.assertEqual((section["agreed"], section["compared"]), (2, 3))
        self.assertEqual(section["rate"], round(2 / 3, 6))
        # Of the two values we do not interpret, the reference translates one: FNumber.
        self.assertEqual((section["not_interpreted"], section["not_interpreted_translated"]), (2, 1))
        self.assertEqual(section["by_kind"]["Exif"], {
            compare_mod.MATCH_INTERPRETED: 2, compare_mod.INTERPRETED_DIFF: 1,
            compare_mod.NOT_INTERPRETED: 2, compare_mod.NOT_INTERPRETED_TRANSLATED: 1,
            compare_mod.REFERENCE_NOT_INTERPRETED: 2,
            compare_mod.NOT_OFFERED: 1,
        })
        self.assertEqual(section["by_kind"]["Xmp"], {compare_mod.NOT_OFFERED: 1})
        self.assertEqual(section["by_family"]["Exif.Photo"], {
            compare_mod.MATCH_INTERPRETED: 1, compare_mod.NOT_INTERPRETED: 1,
            compare_mod.NOT_INTERPRETED_TRANSLATED: 1, compare_mod.REFERENCE_NOT_INTERPRETED: 2,
        })
        self.assertNotIn("Exif.Nikon3", section["by_family"])

    def test_a_dump_that_interprets_nothing_has_no_rate_and_no_differences(self):
        nothing = [row[:5] + [""] if row[0] == "TAG" and len(row) == 6 else row for row in RECORDS]
        section = compare_mod.aggregate([self.entry(nothing)])["interpreted"]
        self.assertEqual(section["outcomes"], {compare_mod.NOT_INTERPRETED: 7})
        self.assertEqual((section["agreed"], section["compared"], section["rate"]), (0, 0, None))
        self.assertEqual((section["not_interpreted"], section["not_interpreted_translated"]), (7, 4))
        self.assertEqual(section["diffs"], [])

    def test_the_raw_numbers_do_not_move_when_the_column_appears(self):
        with_column = compare_mod.aggregate([self.entry()])
        without = compare_mod.aggregate([self.entry(without_sixth_field(RECORDS))])
        for name in ("files", "outcomes", "parity", "advisory", "by_format", "by_family", "by_kind_format", "gaps"):
            self.assertEqual(with_column[name], without[name], name)
        self.assertEqual(set(with_column["outcomes"]), {compare_mod.MATCH_VALUE})
        self.assertEqual(without["interpreted"]["compared"], 0)
        self.assertEqual(without["interpreted"]["by_kind"]["Exif"], {compare_mod.NOT_OFFERED: 8})

    def test_differences_are_listed_by_key_with_their_most_frequent_pair(self):
        changed = [list(row) for row in RECORDS]
        changed[2][5] = "centimeters"
        report = compare_mod.aggregate([self.entry(), self.entry(label="exiv2:b.jpg"), self.entry(changed, "exiv2:c.jpg")])
        diffs = {entry["key"]: entry for entry in report["interpreted"]["diffs"]}
        self.assertEqual(list(diffs)[:1], ["Exif.Image.ResolutionUnit"])
        unit = diffs["Exif.Image.ResolutionUnit"]
        self.assertEqual((unit["tags"], unit["ours"], unit["theirs"], unit["pair_tags"]), (3, "inches", "inch", 2))
        # Only a pair of texts is a difference: no value we leave uninterpreted is listed here.
        self.assertEqual(list(diffs), ["Exif.Image.ResolutionUnit"])

    def test_what_we_do_not_interpret_is_listed_by_key_with_the_reference_text(self):
        report = compare_mod.aggregate([self.entry(), self.entry(label="exiv2:b.jpg")])
        listed = report["interpreted"]["not_interpreted_keys"]
        # Make is not listed: the reference has no translation for it either.
        self.assertEqual(listed, [{"key": "Exif.Photo.FNumber", "tags": 2, "theirs": "F2.8", "text_tags": 2}])


class InterpretedHarnessTests(unittest.TestCase):
    """End to end, against a stand-in dump command that prints the sixth field."""

    def setUp(self):
        self._temporary = tempfile.TemporaryDirectory()
        self.root = Path(self._temporary.name)
        self.tree = self.root / "exiv2"
        self.data = self.tree / "test" / "data"
        self.references = self.data / "test_reference_files"
        self.data.mkdir(parents=True)
        self.references.mkdir()
        self.out = self.root / "artifacts"
        self.path = self.data / "a.jpg"
        self.path.write_bytes(b"\xff\xd8\xff\xe0\x00\x10Exif\x00\x00II*\x00\x08\x00\x00\x00")
        (self.references / "a.jpg.out").write_text(REFERENCE, encoding="utf-8")

    def tearDown(self):
        self._temporary.cleanup()

    def arguments(self, records: list[list]) -> list[str]:
        return ["--tree", str(self.tree), "--dump-command", write_fake(self.root, {str(self.path): records}),
                "--out", str(self.out)]

    def report(self) -> dict:
        return json.loads((self.out / "parity-report.json").read_text(encoding="utf-8"))

    def test_the_report_states_the_interpreted_agreement_beside_the_raw_one(self):
        code, printed = run_main(self.arguments(RECORDS))
        self.assertEqual(code, 0, printed)
        report = self.report()
        self.assertEqual(report["outcomes"], {compare_mod.MATCH_VALUE: 9})
        self.assertEqual(report["parity"]["value_rate"], 1.0)
        self.assertEqual(report["interpreted"]["by_kind"]["Exif"][compare_mod.MATCH_INTERPRETED], 2)
        self.assertEqual(report["interpreted"]["by_kind"]["Exif"][compare_mod.INTERPRETED_DIFF], 1)
        self.assertEqual(report["interpreted"]["by_kind"]["Exif"][compare_mod.NOT_INTERPRETED], 2)
        text = (self.out / "parity-report.txt").read_text(encoding="utf-8")
        # The headline is over the values both sides interpret, with the uninterpreted ones beside it.
        self.assertIn("interpreted:  2/3 = 66.7% of the values both sides interpret; "
                      "not interpreted: 2 (the reference translates 1 of them)", text)
        self.assertIn("by kind (raw values | interpreted values", text)
        self.assertIn("interpreted outcomes: match-interpreted=2, interpreted-diff=1, not-interpreted=2, "
                      "reference-not-interpreted=2", text)
        # The raw outcome line keeps its six names and gains none.
        self.assertIn("outcomes: match-value=9, match-key=0, value-diff=0, missing-key=0, extra-key=0, not-read=0", text)
        exif = next(line for line in text.splitlines() if line.startswith("Exif "))
        # keys, found, raw values | both interpret, agreed, rate, not interpreted, of which the
        # reference translates, only we interpret, not offered
        self.assertEqual(exif.split(), ["Exif", "8", "8", "100.0%", "|", "3", "2", "66.7%", "2", "1", "2", "1"])
        self.assertIn("Exif.Image.ResolutionUnit", text)
        self.assertIn("inches | inch", text)
        self.assertNotIn("(none) | F2.8", text)
        gap = text.split("most frequent values we do not interpret and the reference translates:")[1].split("\n\n")[0]
        self.assertEqual(gap.split("\n")[2].split(), ["Exif.Photo.FNumber", "1", "F2.8"])
        row = next(row for row in report["per_file"][0]["rows"] if row["key"] == "Exif.Photo.ExposureTime")
        self.assertEqual((row["outcome"], row["interpreted"]), (compare_mod.MATCH_VALUE, compare_mod.MATCH_INTERPRETED))

    def test_a_dump_without_the_column_reports_the_same_raw_numbers(self):
        code, _ = run_main(self.arguments(RECORDS))
        self.assertEqual(code, 0)
        with_column = self.report()
        code, _ = run_main(self.arguments(without_sixth_field(RECORDS)))
        self.assertEqual(code, 0)
        without = self.report()
        for name in ("files", "outcomes", "parity", "advisory", "by_format", "by_family", "by_kind_format", "gaps"):
            self.assertEqual(with_column[name], without[name], name)
        self.assertEqual(without["interpreted"]["outcomes"], {})
        self.assertIsNone(without["interpreted"]["rate"])
        text = (self.out / "parity-report.txt").read_text(encoding="utf-8")
        self.assertIn("interpreted:  0/0 = n/a of the values both sides interpret; "
                      "not interpreted: 0 (the reference translates 0 of them)", text)
        self.assertIn("interpreted outcomes: match-interpreted=0, interpreted-diff=0, not-interpreted=0, "
                      "reference-not-interpreted=0", text)

    def test_a_record_with_too_many_fields_fails_the_run(self):
        broken = [["TAG", "Exif.Image.Make", "Ascii", 6, "NIKON", "", "extra"], ["STATUS", "ok"]]
        with self.assertRaises(run_parity.StructuralError) as caught:
            run_parity.main(self.arguments(broken))
        self.assertIn("6 with an interpreted value", str(caught.exception))

    def test_the_annotation_summary_carries_the_column_apart_from_the_raw_kinds(self):
        completed = subprocess.run(
            [sys.executable, str(ROOT / "tools/parity/run_parity.py")] + self.arguments(RECORDS) + ["--annotate"],
            capture_output=True, text=True, check=False)
        self.assertEqual(completed.returncode, 0, completed.stderr)
        marker = "::notice title=PARITY summary 1/1::"
        self.assertIn(marker, completed.stdout)
        decoded = json.loads(zlib.decompress(base64.b64decode(completed.stdout.split(marker)[1].split("\n")[0])))
        self.assertEqual(decoded["by_kind"], {"Exif": {"match-value": 8}, "Xmp": {"match-value": 1}})
        section = decoded["interpreted"]
        self.assertEqual(section["outcomes"], {"match-interpreted": 2, "interpreted-diff": 1,
                                               "not-interpreted": 2, "reference-not-interpreted": 2})
        self.assertEqual((section["agreed"], section["compared"], section["rate"]), (2, 3, round(2 / 3, 6)))
        self.assertEqual((section["not_interpreted"], section["not_interpreted_translated"]), (2, 1))
        self.assertEqual(section["by_kind"]["Exif"]["not-interpreted-translated"], 1)
        self.assertEqual(section["by_family"]["Exif.Image"], {"match-interpreted": 1, "interpreted-diff": 1,
                                                              "not-interpreted": 1})
        self.assertEqual([entry["key"] for entry in section["diff_top"]], ["Exif.Image.ResolutionUnit"])
        self.assertEqual([entry["key"] for entry in section["not_interpreted_top"]], ["Exif.Photo.FNumber"])

    def test_a_summary_of_a_report_without_the_section_still_builds(self):
        entries = [compare_mod.compare_file("exiv2:n", "exiv2", "jpg", "ok", "", {}, {}, [])]
        report = compare_mod.aggregate(entries)
        del report["interpreted"]
        report["corpora"] = {"exiv2": {"tree": None, "files": 0, "unmatched_expectations": 0, "unmatched_names": []},
                             "generated": {"directories": [], "files": 0, "oracle": None}}
        summary = run_parity.summary(report, [])
        self.assertEqual(summary["interpreted"]["by_kind"], {})
        text = run_parity.text_report(report, [])
        self.assertIn("interpreted:  0/0 = n/a of the values both sides interpret; not interpreted: 0", text)
        self.assertIn("interpreted outcomes: match-interpreted=0, interpreted-diff=0, not-interpreted=0, "
                      "reference-not-interpreted=0", text)


if __name__ == "__main__":
    unittest.main()
