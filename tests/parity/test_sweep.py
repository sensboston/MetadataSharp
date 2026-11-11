"""Offline rules of the soundness sweep: how a dump is read and what is counted. No external tool runs here."""
import sys
import unittest
from pathlib import Path, PureWindowsPath

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools" / "parity"))

import run_sweep  # noqa: E402

DUMP = ("FILE\t/c/one.jpg\nCOMMENT\t1\tfirst\nCOMMENT\t2\tsecond\nSTATUS\tok\n"
        "FILE\t/c/two.png\nSTATUS\tok\n"
        "FILE\t/c/bad.png\nSTATUS\trefused\tInvalidPng\n"
        "FILE\t/c/worse.jpg\nSTATUS\terror\tIndexOutOfRangeException: Index was outside the bounds\n")
PATHS = [Path("/c") / name for name in ("one.jpg", "two.png", "bad.png", "worse.jpg")]


class SweepTests(unittest.TestCase):
    def test_records_are_counted_per_file(self):
        blocks = run_sweep.parse_dump(DUMP, "COMMENT")
        self.assertEqual((2, "ok"), (blocks["/c/one.jpg"]["records"], blocks["/c/one.jpg"]["status"]))
        self.assertEqual((0, "refused", "InvalidPng"),
                         (blocks["/c/bad.png"]["records"], blocks["/c/bad.png"]["status"], blocks["/c/bad.png"]["detail"]))

    def test_a_record_of_another_mode_and_a_malformed_dump_are_refused(self):
        for text in (DUMP.replace("COMMENT\t1", "ICC\t1"), "FILE\t/a\n", "STATUS\tok\n", "FILE\t/a\nFILE\t/b\nSTATUS\tok\n",
                     "FILE\t/a\nSTATUS\tok\nCOMMENT\t1\ttext\n", "FILE\t/a\nCOMMENT\t1\nSTATUS\tok\n"):
            with self.assertRaises(run_sweep.SweepError, msg=text):
                run_sweep.parse_dump(text, "COMMENT")

    def test_the_tally_names_every_status_every_refusal_code_and_every_failure(self):
        part = run_sweep.tally(run_sweep.parse_dump(DUMP, "COMMENT"), PATHS)
        self.assertEqual({"error": 1, "ok": 2, "refused": 1}, part["statuses"])
        self.assertEqual({"InvalidPng": 1}, part["refused"])
        self.assertEqual((4, 1, 2), (part["files"], part["files_with_records"], part["records"]))
        self.assertEqual(["worse.jpg"], [entry["file"] for entry in part["errors"]])
        text = run_sweep.text_report({"comments": part})
        self.assertIn("comments: 4 files", text)
        self.assertIn("ERROR worse.jpg: IndexOutOfRangeException", text)

    def test_a_file_is_looked_up_under_the_forward_slash_path_the_dump_tool_was_given(self):
        # A native Windows path would come back from the tool with its backslashes escaped.
        self.assertEqual("C:/corpus/one.jpg", run_sweep.key(PureWindowsPath(r"C:\corpus\one.jpg")))
        self.assertEqual(["/c/one.jpg", "/c/two.png", "/c/bad.png", "/c/worse.jpg"], [run_sweep.key(path) for path in PATHS])

    def test_a_file_the_dump_did_not_answer_for_is_a_structural_failure(self):
        with self.assertRaises(run_sweep.SweepError):
            run_sweep.tally(run_sweep.parse_dump(DUMP, "COMMENT"), PATHS + [Path("/c/absent.jpg")])


if __name__ == "__main__":
    unittest.main()
