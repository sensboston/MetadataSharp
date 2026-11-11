"""Offline tests for the differential parity harness.

Nothing here runs a build, reads the exiv2 corpus, or executes the reference tool: the harness's
rules are string transformations, so they are checked with strings. A stand-in dump command stands in
for the .NET tool, which keeps the orchestration - batching, isolation of an incomplete batch,
missing blocks, report shape, exit status - verifiable on any machine.
"""

import contextlib
import hashlib
import shlex
import io
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/parity'))
import compare as compare_mod  # noqa: E402
import corpus as corpus_mod  # noqa: E402
import dump_format  # noqa: E402
import exiv2_print  # noqa: E402
import run_parity  # noqa: E402

#: Reads the same grammar the C# tool writes - escaping included - and answers from the records the
#: test supplied. The stand-in imports the harness's own writer helper instead of carrying a second
#: copy of the rules, because the value of the fake is that whatever it prints really parses.
FAKE_DUMP = '''"""A stand-in for MetadataSharp.Dump, honouring the dump grammar."""
import sys

sys.path.insert(0, @PARITY_DIR@)
from dump_format import record

DATA = @DATA@
SKIPPED = @SKIPPED@

for line in sys.stdin.read().splitlines():
    path = line.strip()
    if not path or path in SKIPPED:
        continue
    print(record("FILE", path))
    for fields in DATA.get(path, []):
        print(record(*[str(field) for field in fields]))
'''


def py_command(script: Path) -> str:
    """A command line for a generated helper script, shaped the way the harness splits one.

    ``run_parity`` lexes ``--dump-command`` POSIX-style, so every token is quoted: a Windows
    interpreter path contains backslashes an unquoted token would lose, and forward slashes are no
    substitute for the loader that has to find the executable.
    """
    return shlex.quote(sys.executable) + " " + shlex.quote(str(script))


def write_fake(directory: Path, blocks: dict, skip: list | None = None) -> str:
    script = directory / "fake_dump.py"
    source = (FAKE_DUMP
              .replace("@PARITY_DIR@", repr(str(ROOT / "tools/parity")))
              .replace("@DATA@", repr({key: [list(row) for row in value] for key, value in blocks.items()}))
              .replace("@SKIPPED@", repr(list(skip or []))))
    script.write_text(source, encoding="utf-8")
    return py_command(script)

def run_main(arguments: list[str]) -> tuple[int, str]:
    printed = io.StringIO()
    with contextlib.redirect_stdout(printed), contextlib.redirect_stderr(printed):
        code = run_parity.main(arguments)
    return code, printed.getvalue()


class DumpFormatTests(unittest.TestCase):
    def test_a_block_reads_back_the_records_the_tool_prints(self):
        text = (
            "FILE\tcorpus/a.jpg\n"
            "TAG\tExif.Image.0x0112\tShort\t1\t2\n"
            "TAG\tExif.Image.0x010e\tAscii\t22\tSome camera\n"
            "STATUS\tok\n"
        )
        blocks = dump_format.parse_dump(text)
        self.assertEqual(list(blocks), ["corpus/a.jpg"])
        block = blocks["corpus/a.jpg"]
        self.assertEqual(block["status"], "ok")
        self.assertEqual(block["tags"]["Exif.Image.0x0112"], ("Short", 1, "2"))
        self.assertEqual(block["tags"]["Exif.Image.0x010e"], ("Ascii", 22, "Some camera"))
        self.assertEqual(block["order"], ["Exif.Image.0x0112", "Exif.Image.0x010e"])

    def test_control_characters_survive_the_round_trip(self):
        value = "a\\b\tc\nd\x00"
        text = (dump_format.record("FILE", "x") + "\n"
                + dump_format.record("TAG", "Exif.Image.0x010e", "Ascii", "8", value) + "\n"
                + dump_format.record("STATUS", "ok") + "\n")
        block = dump_format.parse_dump(text)["x"]
        self.assertEqual(block["tags"]["Exif.Image.0x010e"][2], value)

    def test_an_unclosed_block_is_an_error_rather_than_a_short_run(self):
        with self.assertRaises(dump_format.DumpFormatError):
            dump_format.parse_dump("FILE\tx\nTAG\tExif.Image.0x0112\tShort\t1\t2\n")

    def test_a_record_with_the_wrong_shape_is_an_error(self):
        broken = [
            "TAG\tkey\tShort\t1\n",
            "STATUS\tsomething-else\n",
            "NOPE\t1\n",
            "FILE\tx\nFILE\ty\nSTATUS\tok\n",
            "TAG\tExif.Image.0x0112\tShort\tno\t2\n",
        ]
        for text in broken:
            with self.assertRaises(dump_format.DumpFormatError, msg=text):
                dump_format.parse_dump(text)

    def test_a_windows_path_round_trips_through_the_grammar(self):
        # A path is a value in this grammar, and a Windows one is full of the escape character: an
        # unescaped echo of it would be read back as a broken escape, or as a subtly wrong path.
        windows = "C:\\Users\\runner\\Temp\\a.jpg"
        text = dump_format.record("FILE", windows) + "\n" + dump_format.record("STATUS", "ok") + "\n"
        self.assertEqual(list(dump_format.parse_dump(text)), [windows])
        self.assertTrue(dump_format.escape(windows).startswith("C:\\\\Users"))

    def test_a_carriage_return_from_a_shell_redirect_does_not_reach_a_value(self):
        text = "FILE\tx\nTAG\tExif.Image.0x0112\tShort\t1\t2\r\nSTATUS\tok\r\n"
        block = dump_format.parse_dump(text)["x"]
        self.assertEqual(block["tags"]["Exif.Image.0x0112"], ("Short", 1, "2"))
        self.assertEqual(block["status"], "ok")

    def test_an_escape_that_cannot_come_from_the_tool_is_rejected(self):
        with self.assertRaises(dump_format.DumpFormatError):
            dump_format.unescape("dangling \\ at the end")
        self.assertEqual(dump_format.unescape("no escapes"), "no escapes")
        self.assertEqual(dump_format.unescape("a\\\\b"), "a\\b")

    def test_duplicate_keys_are_counted_and_the_first_one_wins(self):
        text = ("FILE\tx\nTAG\tExif.Thumbnail.0x0100\tShort\t1\t10\n"
                "TAG\tExif.Thumbnail.0x0100\tShort\t1\t20\nSTATUS\tok\n")
        block = dump_format.parse_dump(text)["x"]
        self.assertEqual(block["tags"]["Exif.Thumbnail.0x0100"], ("Short", 1, "10"))
        self.assertEqual(block["duplicates"], 1)


class ReferenceFormatTests(unittest.TestCase):
    def test_a_padded_tag_line_is_split_into_its_columns(self):
        parsed = exiv2_print.split_tag_line("Exif.Image.XResolution  Rational     1  72/1  72")
        self.assertEqual(parsed, ("Exif.Image.XResolution", "Rational", 1, "72/1  72", False))

    def test_a_narrow_type_column_does_not_split_type_from_count(self):
        # "Undefined" fills the padded column, so its count follows after a single space.
        parsed = exiv2_print.split_tag_line("Exif.Image.PrintImageMatching                Undefined 106  80 114 105")
        self.assertEqual(parsed, ("Exif.Image.PrintImageMatching", "Undefined", 106, "80 114 105", False))

    def test_a_filename_column_marks_the_value_as_a_decoded_label(self):
        parsed = exiv2_print.split_tag_line("mtf_camera.jpg   Exif.Photo.SensingMethod  Short 1  One-chip color area")
        self.assertEqual(parsed, ("Exif.Photo.SensingMethod", "Short", 1, "One-chip color area", True))

    def test_array_indices_and_qualified_paths_are_part_of_a_key(self):
        parsed = exiv2_print.split_tag_line("Xmp.xmpMM.History[1]/stEvt:action                XmpText    40  xmp.iid:1")
        self.assertEqual(parsed[0], "Xmp.xmpMM.History[1]/stEvt:action")
        self.assertEqual(parsed[2], 40)

    def test_an_empty_value_is_not_confused_with_a_missing_column(self):
        parsed = exiv2_print.split_tag_line("Xmp.cm2e.Family                              XmpBag      0  ")
        self.assertEqual(parsed, ("Xmp.cm2e.Family", "XmpBag", 0, "", False))

    def test_prose_and_packets_are_not_tag_lines(self):
        for line in ("Warning: JPEG format error, rc = 5",
                     "Error: Directory Image: IFD entry 3 lies outside of the data",
                     "<x:xmpmeta xmlns:x='adobe:ns:meta/'>",
                     "Set Xmp.dc.subject \"Sky\" (XmpBag)",
                     "0x0000 41 42 43 44",
                     ""):
            self.assertIsNone(exiv2_print.split_tag_line(line), line)

    def test_parse_reference_normalizes_newlines_and_counts_repeated_rows(self):
        text = "Exif.Image.Make  Ascii  6  NIKON\r\nExif.Image.Make  Ascii  6  OTHER\r\nWarning: seen\r\n"
        tags, notes = exiv2_print.parse_reference(text)
        self.assertEqual(tags["Exif.Image.Make"]["value"], "NIKON")
        self.assertEqual(tags["Exif.Image.Make"]["repeats"], 1)
        self.assertEqual(notes, ["Warning: seen"])
        self.assertEqual(exiv2_print.summarize_notes(notes), {"diagnostic": 1})

    def test_an_entry_with_an_invalid_type_identifier_carries_no_value(self):
        # exiv2 prints the raw type number of a corrupt entry and no value column at all.
        parsed = exiv2_print.split_tag_line("Exif.Pentax.PreviewOffset                    0x0000      0")
        self.assertEqual(parsed, ("Exif.Pentax.PreviewOffset", "0x0000", 0, "", True))

    def test_a_row_without_a_raw_value_matches_on_the_key(self):
        reference = {"Exif.Pentax.PreviewOffset": {"type": "0x0000", "count": 0, "value": "", "labelled": True}}
        rows = compare_mod.compare_tags(reference, {"Exif.Pentax.PreviewOffset": ("Undefined", 4, "0 0 0 0")})
        self.assertEqual(rows[0]["outcome"], compare_mod.MATCH_KEY)


class ValueComparisonTests(unittest.TestCase):
    def test_a_raw_value_followed_by_a_label_agrees_with_the_raw_value(self):
        self.assertTrue(compare_mod.values_agree("2", "2  2"))
        self.assertTrue(compare_mod.values_agree("72/1", "72/1  72"))

    def test_a_quoted_array_agrees_with_our_joined_items(self):
        self.assertTrue(compare_mod.values_agree("Sky, Sun", '"Sky", "Sun"'))

    def test_differences_are_not_absorbed_by_normalization(self):
        self.assertFalse(compare_mod.values_agree("72/1", "72/2  72"))
        self.assertFalse(compare_mod.values_agree("NIKON", "Canon"))

    def test_the_outcomes_separate_a_missing_key_from_a_wrong_value(self):
        reference = {
            "Exif.Image.Make": {"type": "Ascii", "count": 6, "value": "NIKON", "labelled": False},
            "Exif.Image.Model": {"type": "Ascii", "count": 6, "value": "E995", "labelled": False},
            "Exif.Photo.DateTimeDigitized": {"type": "Ascii", "count": 19, "value": "2011:06:27 21:35:33", "labelled": False},
        }
        dumped = {
            "Exif.Image.Make": ("Ascii", 6, "NIKON"),
            "Exif.Image.Model": ("Ascii", 5, "E99"),
            "Exif.Image.Software": ("Ascii", 4, "tool"),
        }
        rows = {entry["key"]: entry for entry in compare_mod.compare_tags(reference, dumped)}
        self.assertEqual(rows["Exif.Image.Make"]["outcome"], compare_mod.MATCH_VALUE)
        self.assertEqual(rows["Exif.Image.Model"]["outcome"], compare_mod.VALUE_DIFF)
        self.assertEqual(rows["Exif.Photo.DateTimeDigitized"]["outcome"], compare_mod.MISSING_KEY)
        self.assertEqual(rows["Exif.Image.Software"]["outcome"], compare_mod.EXTRA_KEY)
        self.assertEqual(rows["Exif.Image.Model"]["flags"], ["count"])

    def test_a_labelled_row_matches_on_the_key_alone(self):
        reference = {"Exif.Photo.SensingMethod": {"type": "Short", "count": 1, "value": "One-chip color area", "labelled": True}}
        rows = compare_mod.compare_tags(reference, {"Exif.Photo.SensingMethod": ("Short", 1, "2")})
        self.assertEqual(rows[0]["outcome"], compare_mod.MATCH_KEY)

    def test_a_numeric_label_that_equals_the_value_still_counts_as_a_value_match(self):
        reference = {"Iptc.Application2.RecordVersion": {"type": "Short", "count": 1, "value": "0  0", "labelled": True}}
        rows = compare_mod.compare_tags(reference, {"Iptc.Application2.RecordVersion": ("String", 2, "0 0")})
        self.assertEqual(rows[0]["outcome"], compare_mod.MATCH_VALUE)

    def test_an_empty_reference_value_with_a_zero_count_is_not_a_difference(self):
        reference = {"Xmp.cm2e.Family": {"type": "XmpBag", "count": 0, "value": "", "labelled": False}}
        rows = compare_mod.compare_tags(reference, {"Xmp.cm2e.Family": ("XmpBag", 0, "")})
        self.assertEqual(rows[0]["outcome"], compare_mod.MATCH_VALUE)

    def test_a_file_we_did_not_read_does_not_become_a_missing_key(self):
        entry = compare_mod.compare_file("x", "exiv2", "jpg", "not-attempted", "magic",
                                         {"Exif.Image.Make": {"type": "Ascii", "count": 6, "value": "NIKON", "labelled": False}},
                                         {}, [])
        self.assertEqual(entry["rows"][0]["outcome"], compare_mod.NOT_READ)
        report = compare_mod.aggregate([entry])
        self.assertEqual(report["parity"]["expected_keys"], 0)
        self.assertIsNone(report["parity"]["key_rate"])
        self.assertEqual(report["gaps"]["unread_keys"][0]["key"], "Exif.Image.Make")
        self.assertEqual(report["gaps"]["missing_keys"], [])

    def test_advisory_flags_and_classifications_reach_the_report(self):
        entries = [
            compare_mod.compare_file(f"exiv2:n{index}", "exiv2", "jpg", "ok", "",
                                     {"Exif.MakerNote.Offset": {"type": "Long", "count": 1, "value": "0", "labelled": False}}, {}, [])
            for index in range(2)
        ]
        report = compare_mod.aggregate(entries, {"Exif.MakerNote.Offset": {"class": "known-gap", "note": "no maker notes yet"}})
        gap = report["gaps"]["missing_keys"][0]
        self.assertEqual(gap["files"], 2)
        self.assertEqual(gap["tags"], 2)
        self.assertEqual(gap["class"], "known-gap")
        self.assertEqual(gap["note"], "no maker notes yet")
        self.assertEqual(report["parity"]["key_rate"], 0.0)
        self.assertEqual(report["by_family"]["Exif.MakerNote"]["missing-key"], 2)


class CandidateTests(unittest.TestCase):
    def test_a_candidate_names_its_format_and_corpus(self):
        candidate = corpus_mod.Candidate(label="a.jpg", path=Path("/x/a.jpg"), corpus="exiv2")
        self.assertEqual(candidate.format, "jpg")
        self.assertEqual(candidate.uid, "exiv2:a.jpg")

    def test_a_raw_sample_shares_one_format_bucket(self):
        for suffix in ("cr2", "nef", "raf"):
            self.assertEqual(corpus_mod._format_of(Path(f"/x/a.{suffix}")), "raw")
        self.assertEqual(corpus_mod._format_of(Path("/x/a.tif")), "tiff")
        self.assertEqual(corpus_mod._format_of(Path("/x/a")), "none")


class CorpusDiscoveryTests(unittest.TestCase):
    def setUp(self):
        self._temporary = tempfile.TemporaryDirectory()
        self.root = Path(self._temporary.name)
        self.data = self.root / "test" / "data"
        self.references = self.data / "test_reference_files"
        self.data.mkdir(parents=True)
        self.references.mkdir()

    def tearDown(self):
        self._temporary.cleanup()

    def write_reference(self, name: str) -> None:
        (self.references / f"{name}.out").write_text("Exif.Image.Make  Ascii  6  NIKON\n", encoding="utf-8")

    def test_an_expectation_is_paired_with_the_file_it_names(self):
        (self.data / "a.jpg").write_bytes(b"\xff\xd8\xff")
        self.write_reference("a.jpg")
        self.write_reference("gone.png")
        found, unmatched = corpus_mod.discover_exiv2(self.root)
        self.assertEqual([candidate.label for candidate in found], ["a.jpg"])
        self.assertEqual(found[0].reference.name, "a.jpg.out")
        self.assertEqual(unmatched, ["gone.png"])

    def test_an_expectation_named_after_a_converted_file_finds_the_original(self):
        (self.data / "shot.jpg").write_bytes(b"\xff\xd8")
        self.write_reference("shot.png")
        found, unmatched = corpus_mod.discover_exiv2(self.root)
        self.assertEqual([(c.label, c.path.name, c.format) for c in found], [("shot.png", "shot.jpg", "jpg")])
        self.assertEqual(unmatched, [])

    def test_a_tree_without_expectations_is_a_setup_error(self):
        with self.assertRaises(corpus_mod.CorpusError):
            corpus_mod.discover_exiv2(self.root / "nowhere")
        with self.assertRaises(corpus_mod.CorpusError):
            corpus_mod.discover_exiv2(self.root)

    def test_generated_fixtures_skip_the_bookkeeping_files(self):
        fixtures = self.root / "fixtures"
        (fixtures / "sub").mkdir(parents=True)
        (fixtures / "a.png").write_bytes(b"\x89PNG")
        (fixtures / "manifest.json").write_text("{}", encoding="utf-8")
        (fixtures / "sub" / "b.xmp").write_text("<x/>", encoding="utf-8")
        found = corpus_mod.discover_generated(fixtures)
        self.assertEqual([candidate.path.name for candidate in found], ["a.png", "b.xmp"])
        with self.assertRaises(corpus_mod.CorpusError):
            corpus_mod.discover_generated(self.root / "missing")


class HarnessTests(unittest.TestCase):
    """End to end, against a stand-in dump command."""

    def setUp(self):
        self._temporary = tempfile.TemporaryDirectory()
        self.root = Path(self._temporary.name)
        self.tree = self.root / "exiv2"
        self.data = self.tree / "test" / "data"
        self.references = self.data / "test_reference_files"
        self.data.mkdir(parents=True)
        self.references.mkdir()
        self.out = self.root / "artifacts"

    def tearDown(self):
        self._temporary.cleanup()

    def sample(self, name: str, reference: str) -> Path:
        path = self.data / name
        path.write_bytes(b"\xff\xd8\xff\xe0\x00\x10Exif\x00\x00II*\x00\x08\x00\x00\x00")
        (self.references / f"{name}.out").write_text(reference, encoding="utf-8")
        return path

    def arguments(self, dump_command: str) -> list[str]:
        return ["--tree", str(self.tree), "--dump-command", dump_command, "--out", str(self.out)]

    def report(self) -> dict:
        return json.loads((self.out / "parity-report.json").read_text(encoding="utf-8"))

    def test_a_matching_file_is_reported_and_the_run_is_sound(self):
        path = self.sample("a.jpg", "Exif.Image.Make  Ascii  6  NIKON  NIKON\nExif.Image.Model  Ascii  6  E995\n")
        blocks = {str(path): [["TAG", "Exif.Image.Make", "Ascii", 6, "NIKON"],
                               ["TAG", "Exif.Image.Model", "Ascii", 6, "E995"],
                               ["STATUS", "ok"]]}
        code, printed = run_main(self.arguments(write_fake(self.root, blocks)))
        self.assertEqual(code, 0, printed)
        report = self.report()
        self.assertEqual(report["outcomes"][compare_mod.MATCH_VALUE], 2)
        self.assertEqual(report["parity"]["key_rate"], 1.0)
        self.assertEqual(report["parity"]["value_rate"], 1.0)
        self.assertEqual(report["corpora"]["exiv2"]["files"], 1)
        self.assertEqual(report["corpora"]["generated"]["files"], 0)
        self.assertIn("structural problems: none", printed)
        self.assertIn("jpg", printed)

    def test_values_and_keys_are_scored_apart(self):
        path = self.sample("b.jpg", "Exif.Image.Make  Ascii  6  NIKON  NIKON\nExif.Image.Model  Ascii  6  E995\n")
        blocks = {str(path): [["TAG", "Exif.Image.Make", "Ascii", 6, "NIKON"],
                               ["TAG", "Exif.Image.Model", "Ascii", 6, "E99"],
                               ["STATUS", "ok"]]}
        code, _ = run_main(self.arguments(write_fake(self.root, blocks)))
        self.assertEqual(code, 0)
        report = self.report()
        self.assertEqual(report["outcomes"][compare_mod.MATCH_VALUE], 1)
        self.assertEqual(report["outcomes"][compare_mod.VALUE_DIFF], 1)
        self.assertEqual(report["parity"]["key_rate"], 1.0)
        self.assertEqual(report["parity"]["value_rate"], 0.5)

    def test_a_refused_file_is_counted_as_refused_not_as_a_wrong_value(self):
        self.sample("c.jpg", "Exif.Image.Make  Ascii  6  NIKON\n")
        blocks = {str(self.data / "c.jpg"): [["STATUS", "refused", "InvalidExif"]]}
        code, _ = run_main(self.arguments(write_fake(self.root, blocks)))
        self.assertEqual(code, 0)
        report = self.report()
        self.assertEqual(report["files"]["refusals"], {"refused:InvalidExif": 1})
        self.assertEqual(report["outcomes"][compare_mod.NOT_READ], 1)
        self.assertEqual(report["parity"]["expected_keys"], 0)

    def test_a_file_read_with_one_kind_refused_is_counted_and_reported_apart(self):
        self.sample("d.tif", "Exif.Image.Make  Ascii  6  NIKON\nIptc.Application2.Caption  String  5  hello\n"
                    "Xmp.dc.format  XmpText  10  image/tiff\n")
        blocks = {str(self.data / "d.tif"): [["TAG", "Exif.Image.Make", "Ascii", 6, "NIKON"],
                                              ["TAG", "Xmp.dc.format", "XmpText", 10, "image/tiff"],
                                              ["STATUS", "ok", "iptc-refused:InvalidIptc"]]}
        code, _ = run_main(self.arguments(write_fake(self.root, blocks)))
        self.assertEqual(code, 0)
        report = self.report()
        self.assertEqual(report["files"]["partial"], {"iptc-refused:InvalidIptc": 1})
        self.assertEqual(report["files"]["refusals"], {})
        self.assertEqual(report["files"]["attempted"], 1)
        self.assertEqual(report["outcomes"], {compare_mod.MATCH_VALUE: 2, compare_mod.MISSING_KEY: 1})
        self.assertEqual(report["by_kind_format"], {
            "Exif": {"tiff": {compare_mod.MATCH_VALUE: 1}},
            "Iptc": {"tiff": {compare_mod.MISSING_KEY: 1}},
            "Xmp": {"tiff": {compare_mod.MATCH_VALUE: 1}},
        })
        summary = run_parity.summary(report, [])
        self.assertEqual(sorted(summary["by_kind_format"]), ["Iptc", "Xmp"])

    def test_a_truncated_dump_fails_the_run_instead_of_reporting_a_gap(self):
        self.sample("d.jpg", "Exif.Image.Make  Ascii  6  NIKON\n")
        script = self.root / "truncating_dump.py"
        # The FILE record is written the way the grammar requires - the point of the case is the
        # truncated TAG line, and a path that happens to contain the escape character must not be
        # what trips the parser first, as it was on a Windows runner.
        script.write_text("import sys\n"
                          f"sys.path.insert(0, {str(ROOT / 'tools/parity')!r})\n"
                          "from dump_format import record\n"
                          "path = [line for line in sys.stdin.read().splitlines() if line][0]\n"
                          "sys.stdout.write(record('FILE', path) + '\\nTAG\\tExif.Image.Make\\n')\n",
                          encoding="utf-8")
        with self.assertRaises(run_parity.StructuralError) as caught:
            run_parity.main(self.arguments(py_command(script)))
        self.assertIn("5 fields", str(caught.exception))
        self.assertFalse((self.out / "parity-report.json").exists())

    def test_a_reserved_character_in_a_value_survives_the_whole_run(self):
        # The key a block is filed under is the path the tool echoed back, and a value may hold the
        # tab the grammar reserves for field separation; both have to round-trip through one run.
        path = self.sample("d.jpg", "Exif.Image.Model  Ascii  4  a b\n")
        blocks = {str(path): [["TAG", "Exif.Image.Model", "Ascii", 4, "a\tb"], ["STATUS", "ok"]]}
        code, printed = run_main(self.arguments(write_fake(self.root, blocks)))
        self.assertEqual(code, 0, printed)
        self.assertEqual(self.report()["outcomes"][compare_mod.MATCH_VALUE], 1)

    def test_a_file_the_tool_never_printed_is_a_structural_problem(self):
        path = self.sample("e.jpg", "Exif.Image.Make  Ascii  6  NIKON\n")
        code, printed = run_main(self.arguments(write_fake(self.root, {}, skip=[str(path)])))
        self.assertEqual(code, 1)
        self.assertIn("printed no record", printed)

    def test_an_unexpected_exception_on_our_side_is_a_failure_not_a_difference(self):
        path = self.sample("f.jpg", "Exif.Image.Make  Ascii  6  NIKON\n")
        blocks = {str(path): [["STATUS", "error", "IndexOutOfRangeException: at offset 41"]]
        }
        code, printed = run_main(self.arguments(write_fake(self.root, blocks)))
        self.assertEqual(code, 1)
        # The exception type is the whole point of the line: it is what turns "something is wrong"
        # into a place to start reading, and CI annotations are the only way to see it from here.
        self.assertIn("IndexOutOfRangeException", printed)
        self.assertIn("f.jpg", printed)

    def test_a_damaged_expectation_is_reported_once_per_file(self):
        damaged = [f"Exif.Image.Tag{index}  Ascii  6" for index in range(6)]
        problems: list[str] = []
        run_parity.check_reference_shape("exiv2:g.jpg", damaged, problems)
        self.assertEqual(len(problems), 1)
        self.assertIn("do not parse", problems[0])
        run_parity.check_reference_shape("exiv2:g.jpg", damaged[:2], problems)
        self.assertEqual(len(problems), 1)

    def test_the_generated_corpus_needs_an_oracle(self):
        fixtures = self.root / "fixtures"
        fixtures.mkdir()
        (fixtures / "own.png").write_bytes(b"\x89PNG\r\n\x1a\n")
        with self.assertRaises(run_parity.StructuralError) as caught:
            run_parity.main(["--fixtures", str(fixtures), "--dump-command", write_fake(self.root, {}),
                             "--out", str(self.out)])
        self.assertIn("--exiv2-command", str(caught.exception))

    def test_the_owned_corpus_uses_the_pinned_tool_as_its_oracle(self):
        fixtures = self.root / "owned"
        fixtures.mkdir()
        own = fixtures / "own.jpg"
        own.write_bytes(b"\xff\xd8\xff\xe0")
        oracle = fixtures / "fake_exiv2.py"
        oracle.write_text("import sys\n"
                          "sys.stdin.read()\n"
                          "print('Exif.Image.Make  Ascii  6  NIKON  NIKON')\n", encoding="utf-8")
        blocks = {str(own): [["TAG", "Exif.Image.Make", "Ascii", 6, "NIKON"], ["STATUS", "ok"]]}
        code, printed = run_main(["--fixtures", str(fixtures), "--exiv2-command", py_command(oracle),
                                  "--dump-command", write_fake(self.root, blocks), "--out", str(self.out)])
        self.assertEqual(code, 0, printed)
        report = json.loads((self.out / "parity-report.json").read_text(encoding="utf-8"))
        self.assertEqual(report["corpora"]["generated"]["files"], 1)
        self.assertEqual(report["outcomes"][compare_mod.MATCH_VALUE], 1)
        self.assertEqual(report["parity"]["key_rate"], 1.0)
        # The oracle is read in memory: nothing may be written next to the fixtures themselves.
        self.assertEqual(sorted(path.name for path in fixtures.iterdir()), ["fake_exiv2.py", "own.jpg"])

    def test_a_small_row_budget_bounds_the_detail_but_not_the_totals(self):
        reference = "".join(f"Exif.Image.Tag{index:02d}  Ascii  6  NIKON  NIKON\n" for index in range(60))
        path = self.sample("wide.jpg", reference)
        blocks = {str(path): [["STATUS", "ok"]]}
        code, _ = run_main(self.arguments(write_fake(self.root, blocks)) + ["--rows-per-file", "3"])
        self.assertEqual(code, 0)
        report = self.report()
        self.assertEqual(report["outcomes"][compare_mod.MISSING_KEY], 60)
        self.assertEqual(report["parity"]["expected_keys"], 60)
        only = report["per_file"][0]
        self.assertEqual(len(only["rows"]), 3)
        self.assertEqual(only["rows_dropped"], 57)

    def test_annotations_carry_the_summary_and_refuse_to_truncate(self):
        path = self.sample("h.jpg", "Exif.Image.Make  Ascii  6  NIKON\n")
        blocks = {str(path): [["TAG", "Exif.Image.Make", "Ascii", 6, "NIKON"], ["STATUS", "ok"]]}
        completed = subprocess.run(
            [sys.executable, str(ROOT / "tools/parity/run_parity.py")] + self.arguments(write_fake(self.root, blocks)) + ["--annotate"],
            capture_output=True, text=True, check=False)
        self.assertEqual(completed.returncode, 0, completed.stderr)
        marker = "::notice title=PARITY summary 1/1::"
        self.assertIn(marker, completed.stdout)
        import base64
        import zlib

        payload = completed.stdout.split(marker)[1].split("\n")[0]
        decoded = json.loads(zlib.decompress(base64.b64decode(payload)))
        self.assertEqual(decoded["parity"]["value_rate"], 1.0)
        self.assertEqual(decoded["problem_count"], 0)
        self.assertEqual(decoded["by_kind"], {"Exif": {"match-value": 1}})

    def test_the_summary_folds_families_into_metadata_kinds(self):
        folded = run_parity.by_kind({
            "Xmp.dc": {"match-value": 2, "missing-key": 1},
            "Xmp.xmpMM": {"match-value": 3, "value-diff": 1},
            "Iptc.Application2": {"missing-key": 4},
            "Exif.Image": {"match-value": 5},
        })
        self.assertEqual(list(folded), ["Exif", "Iptc", "Xmp"])
        self.assertEqual(folded["Xmp"], {"match-value": 5, "missing-key": 1, "value-diff": 1})
        self.assertEqual(folded["Iptc"], {"missing-key": 4})

    def test_a_summary_too_large_for_the_channel_is_an_error(self):
        entries = [compare_mod.compare_file(f"exiv2:n{index}", "exiv2", "jpg", "ok", "", {}, {}, []) for index in range(20)]
        report = compare_mod.aggregate(entries)
        report["corpora"] = {"exiv2": {"tree": None, "files": 0, "unmatched_expectations": 0, "unmatched_names": []},
                             "generated": {"directories": [], "files": 0, "oracle": None}}
        payload = run_parity.summary(report, [])
        original = (run_parity.CHUNK_LIMIT, run_parity.CHUNK_SIZE)
        self.addCleanup(lambda: setattr(run_parity, "CHUNK_LIMIT", original[0]))
        self.addCleanup(lambda: setattr(run_parity, "CHUNK_SIZE", original[1]))
        run_parity.CHUNK_LIMIT, run_parity.CHUNK_SIZE = 1, 16
        printed = io.StringIO()
        with contextlib.redirect_stdout(printed), self.assertRaises(run_parity.StructuralError) as caught:
            run_parity.emit_annotations(payload)
        self.assertIn("recovery limit", str(caught.exception))
        import base64
        import zlib

        run_parity.CHUNK_LIMIT, run_parity.CHUNK_SIZE = original
        printed.truncate(0)
        with contextlib.redirect_stdout(printed):
            run_parity.emit_annotations(payload)
        value = printed.getvalue()
        self.assertIn("::notice title=PARITY summary 1/1::", value)
        recovered = json.loads(zlib.decompress(base64.b64decode(value.split("::")[-1])))
        self.assertEqual(recovered["corpora"]["exiv2"]["files"], 0)
        self.assertEqual(recovered["problem_count"], 0)


if __name__ == "__main__":
    unittest.main()
