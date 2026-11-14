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
Exif.Photo.ExifVersion                   0230
"""

COMMENT = "Grüße aus Köln"
CONVERTED = 'Setting Exif UNICODE user comment to "' + COMMENT + '"\n' 


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

    def run_gate(self, tool_output=PRINTED, tool_code=0, tool_errors="", converted_text=CONVERTED,
                 convert_code=0, convert_errors=""):
        commands = []

        def runner(command, timeout=None):
            commands.append(list(command))
            if command[0] == "fixture":
                return 0, "", ""
            if "fc" in command:
                return convert_code, converted_text, convert_errors
            return tool_code, tool_output, tool_errors

        result = run_charset.main([str(self.work), "--exiv2", "exiv2", "--fixture", "fixture"],
                                  runner=runner)
        return result, commands

    def test_the_tool_is_asked_what_n_means_for_the_comment_and_prints_the_title_translated(self):
        result, commands = self.run_gate()
        self.assertEqual(0, result)
        # -n belongs to the fc action: the tool converts a copy and says what it read in its verbose
        # line, which is what has to equal this library's text. The title is read translated.
        self.assertIn(["exiv2", "-v", "-n", "windows-1252", "fc", str(self.work / "charset-00.jpg.fc.jpg")], commands)
        # Only the title is printed translated: the comment's bytes are a code page in these files.
        self.assertIn(["exiv2", "-PEkt", "-K", "Exif.Image.XPTitle", str(self.work / "charset-00.jpg")], commands)
        self.assertFalse((self.work / "charset-00.jpg.fc.jpg").exists())

    def test_the_one_code_page_the_tool_does_not_flush_is_compared_without_its_last_character(self):
        # glibc's windows-1258 converter holds the final character until a flush the tool never does.
        short = 'Setting Exif UNICODE user comment to "Grüße aus Köl"\n'
        self.use_charset("windows-1258")
        self.assertEqual(0, self.run_gate(converted_text=short)[0])
        # The same text one character short in any other code page is still a difference.
        self.use_charset("windows-1252")
        self.assertEqual(1, self.run_gate(converted_text=short)[0])

    def use_charset(self, name):
        code_page = "1258" if name == "windows-1258" else "1252"
        rows = [row.replace("windows-1252", name).replace("windows-1258", name)
                   .replace("\t1252\t", "\t" + code_page + "\t").replace("\t1258\t", "\t" + code_page + "\t")
                for row in self.rows]
        (self.work / "ours.tsv").write_text("\n".join(rows) + "\n", encoding="utf-8")

    def test_a_comment_the_tool_reads_differently_fails(self):
        result, _ = self.run_gate(converted_text='Setting Exif UNICODE user comment to "Gruesse"\n')
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
        result, _ = self.run_gate(converted_text="", convert_code=1,
                                  convert_errors="exiv2: Cannot convert text encoding from 'windows-1252' to 'UTF-8'")
        self.assertEqual(0, result)

    def test_a_conversion_that_says_nothing_fails_the_gate(self):
        result, _ = self.run_gate(converted_text="", convert_code=1, convert_errors="exiv2: Failed to open the file")
        self.assertEqual(1, result)


if __name__ == "__main__":
    unittest.main()
