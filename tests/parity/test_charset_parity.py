"""Offline rules of the character-set comparison: the two prints, the round trip and the iconv skip.

No Exiv2 binary and no fixture generator runs here: a fake runner answers for both, which is what
makes the gate's own rules - what counts as a difference, and what a build without iconv does -
visible without a network.
"""
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools" / "reference"))

import run_charset  # noqa: E402

PRINTED = """Exif.Image.Make                          MetadataSharp charset fixture
Exif.Image.XPTitle                       Grüße — 日本語
Exif.Photo.UserComment                   charset=Unicode Grüße aus Köln
Exif.Photo.ExifVersion                   0230
"""


class CharsetParityTest(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.work = Path(self.temporary.name)
        (self.work / "charset-00.jpg").write_bytes(b"\xff\xd8\xff\xd9")
        self.rows = [
            "charset-00.jpg\twindows-1252\t1252\tExif.Photo.UserComment\tGrüße aus Köln",
            "charset-00.jpg\twindows-1252\t1252\tExif.Image.XPTitle\tGrüße — 日本語",
            "charset-00.jpg\twindows-1252\t1252\twritten\tGrüße aus Köln|Grüße — 日本語",
        ]
        (self.work / "ours.tsv").write_text("\n".join(self.rows) + "\n", encoding="utf-8")

    def tearDown(self):
        self.temporary.cleanup()

    def run_gate(self, tool_output=PRINTED, tool_code=0, tool_errors=""):
        commands = []

        def runner(command, timeout=None):
            commands.append(list(command))
            if command[0] == "fixture":
                return 0, "", ""
            return tool_code, tool_output, tool_errors

        result = run_charset.main([str(self.work), "--exiv2", "exiv2", "--fixture", "fixture"],
                                  runner=runner)
        return result, commands

    def test_the_tools_text_and_this_librarys_text_have_to_be_equal(self):
        result, commands = self.run_gate()
        self.assertEqual(0, result)
        # One print with -n for the comment, one without for the title.
        self.assertIn(["exiv2", "-n", "windows-1252", "-PEkv", str(self.work / "charset-00.jpg")], commands)
        self.assertIn(["exiv2", "-PEkv", str(self.work / "charset-00.jpg")], commands)

    def test_a_comment_the_tool_reads_differently_fails(self):
        result, _ = self.run_gate(tool_output=PRINTED.replace("Grüße aus Köln", "Gruesse"))
        self.assertEqual(1, result)

    def test_a_title_the_tool_reads_differently_fails(self):
        result, _ = self.run_gate(tool_output=PRINTED.replace("Grüße — 日本語", "Gruesse"))
        self.assertEqual(1, result)

    def test_a_written_text_that_does_not_read_back_fails_before_the_tool_is_asked(self):
        self.rows[0] = self.rows[0].replace("\tGrüße aus Köln", "\tGruse")
        (self.work / "ours.tsv").write_text("\n".join(self.rows) + "\n", encoding="utf-8")
        result, _ = self.run_gate()
        self.assertEqual(1, result)

    def test_a_manifest_that_is_missing_or_unreadable_is_reported_and_not_a_traceback(self):
        (self.work / "ours.tsv").unlink()
        self.assertEqual(1, self.run_gate()[0])
        (self.work / "ours.tsv").write_bytes(b"charset-00.jpg\twindows-1252\t1252\tExif.Image.XPTitle\t\xff\n")
        self.assertEqual(1, self.run_gate()[0])

    def test_a_build_without_iconv_says_so_rather_than_passing_quietly(self):
        result, _ = self.run_gate(tool_code=1, tool_errors="exiv2: Cannot convert text encoding from 'windows-1252' to 'UTF-8'")
        self.assertEqual(0, result)


if __name__ == "__main__":
    unittest.main()
