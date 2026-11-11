"""Offline rules of the ICC comparison: how the three expectations and the dump are read, and how
a file's profile is judged. No external tool runs here."""
import base64
import hashlib
import sys
import unittest
from pathlib import Path, PurePosixPath, PureWindowsPath

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools" / "parity"))

import run_icc  # noqa: E402

A, B, C = "a" * 32, "b" * 32, "c" * 32

BASH_TEST = '''
class TestCases(unittest.TestCase):
    def geotag_test(self):
        for img in ['other.jpg']:
            pass

    def icc_test(self):
        def test1120(img):
            return img
        out = BT.Output()
        for img in ['one.jpg'
                    ,'two.webp'
                    ]:
            stub = img.split('.')[0]
            for i in ['large.icc', 'small.icc', img]:
                BT.copyTestFile(i)
'''

BASH_OUTPUT = "\n".join(["STRUCTURE OF JPEG FILE: one.jpg", A, B, B, C, C, C, "abcdefg", "Exif.Image.Make",
                         B, C, C, A, A, A, ""])

MD5_CASE = '''
import system_tests

class IccProfileInApp2Segment(metaclass=system_tests.CaseMeta):
    num = 1074
    filenames = [
        system_tests.path("$data_path/" + fname)
        for fname in ("bug$num.png", "three.tiff", "one.jpg")
    ]
    commands = ["$exiv2 -pC " + fname for fname in filenames]
    stderr = [bytes()] * len(filenames)
    stdout = ["%s", "%s", "%s"]
    retval = [0] * len(filenames)
''' % (C, B, A)

PROFILE = bytes(range(200))

BINARY_CASE = '''
import system_tests
bSkip = False

class with_a_profile(metaclass=system_tests.CaseMeta):
    filename = "$data_path/four.heic"
    if bSkip:
        commands=[]
        stdout=[]
    else:
        commands = ["$exiv2 -pS          $filename"
                   ,"$exiv2 -pC --binary $filename"
                   ]
        retval = [ 0  ] * len(commands)
        stdout = ["""a structure
""","""data:%s
"""]

class without_one(metaclass=system_tests.CaseMeta):
    filename = "$data_path/five.avif"
    if bSkip:
        commands=[]
        stdout=[]
    else:
        commands = ["$exiv2 -pC $filename", "$exiv2 -pX $filename"]
        stdout = ["", "a packet"]
''' % "\n".join(base64.b64encode(PROFILE).decode()[i:i + 59] for i in range(0, 268, 59))

PROFILE_SUM = hashlib.md5(PROFILE).hexdigest()


class BashTestTests(unittest.TestCase):
    def test_the_first_sum_of_each_six_is_the_profile_of_the_untouched_image(self):
        self.assertEqual(["one.jpg", "two.webp"], run_icc.bash_images(BASH_TEST))
        self.assertEqual([[A, B, B, C, C, C], [B, C, C, A, A, A]], run_icc.bash_sums(BASH_OUTPUT))
        self.assertEqual({"one.jpg": (A, None), "two.webp": (B, None)}, run_icc.parse_bash(BASH_TEST, BASH_OUTPUT))

    def test_damage_is_an_error_and_not_a_smaller_expectation(self):
        for test, output in ((BASH_TEST.replace("icc_test", "other_test"), BASH_OUTPUT),
                             (BASH_TEST.replace("for img in ['one.jpg'", "for img in images + ['one.jpg'"), BASH_OUTPUT),
                             (BASH_TEST.replace(",'two.webp'", ""), BASH_OUTPUT),
                             (BASH_TEST.replace("'two.webp'", "'one.jpg'"), BASH_OUTPUT),
                             (BASH_TEST, BASH_OUTPUT.replace(C + "\nabcdefg", "abcdefg")),
                             (BASH_TEST + "    def broken(:\n", BASH_OUTPUT)):
            with self.assertRaises(run_icc.IccParityError):
                run_icc.parse_bash(test, output)


class Md5CaseTests(unittest.TestCase):
    def test_each_file_name_takes_the_sum_at_its_place(self):
        self.assertEqual({"bug1074.png": (C, None), "three.tiff": (B, None), "one.jpg": (A, None)},
                         run_icc.parse_md5_case(MD5_CASE))

    def test_damage_is_an_error(self):
        for text in (MD5_CASE.replace('"$exiv2 -pC "', '"$exiv2 -pX "'), MD5_CASE.replace(', "one.jpg"', ""),
                     MD5_CASE.replace(C, "not a sum"), MD5_CASE.replace("    num = 1074\n", ""),
                     MD5_CASE.replace('"three.tiff"', '"one.jpg"'), "import system_tests\n"):
            with self.assertRaises(run_icc.IccParityError, msg=text):
                run_icc.parse_md5_case(text)


class BinaryCaseTests(unittest.TestCase):
    def test_the_output_at_the_place_of_the_command_is_a_profile_or_nothing(self):
        self.assertEqual({"four.heic": (PROFILE_SUM, 200), "five.avif": None},
                         run_icc.parse_binary_case(BINARY_CASE, "case.py"))

    def test_damage_is_an_error(self):
        for text in (BINARY_CASE.replace('"data:', '"date:'), BINARY_CASE.replace("data:AAEC", "data:A*EC"),
                     BINARY_CASE.replace(',"$exiv2 -pC --binary $filename"\n', ""),
                     BINARY_CASE.replace("-pC --binary", "-pX --binary"),
                     BINARY_CASE.replace("five.avif", "four.heic"), BINARY_CASE.replace('["", "a packet"]', '[""]'),
                     "import system_tests\n"):
            with self.assertRaises(run_icc.IccParityError, msg=text):
                run_icc.parse_binary_case(text, "case.py")


class MergeTests(unittest.TestCase):
    def test_tests_that_agree_give_one_expectation_and_the_length_where_one_knows_it(self):
        expected, origin = run_icc.merge({"b": {"one.jpg": (A, None), "none.avif": None},
                                          "a": {"one.jpg": (A, 560), "two.png": (B, None)},
                                          "c": {"none.avif": None, "one.jpg": (A, None)}})
        self.assertEqual({"one.jpg": (A, 560), "two.png": (B, None), "none.avif": None}, expected)
        self.assertEqual({"one.jpg": ["a", "b", "c"], "two.png": ["a"], "none.avif": ["b", "c"]}, origin)

    def test_tests_that_disagree_are_damage(self):
        with self.assertRaises(run_icc.IccParityError):
            run_icc.merge({"a": {"one.jpg": (A, None)}, "b": {"one.jpg": (B, None)}})
        with self.assertRaises(run_icc.IccParityError):
            run_icc.merge({"a": {"one.jpg": (A, None)}, "b": {"one.jpg": None}})


class DumpTests(unittest.TestCase):
    def test_records_are_grouped_by_file(self):
        text = f"FILE\t/c/one.jpg\nICC\t560\t{A}\nSTATUS\tok\nFILE\t/c/two.png\nSTATUS\tok\nFILE\t/c/x\nSTATUS\trefused\tInvalidIccProfile\n"
        blocks = run_icc.parse_dump(text)
        self.assertEqual(((A, 560), "ok"), (blocks["/c/one.jpg"]["profile"], blocks["/c/one.jpg"]["status"]))
        self.assertEqual((None, "ok"), (blocks["/c/two.png"]["profile"], blocks["/c/two.png"]["status"]))
        self.assertEqual(("refused", "InvalidIccProfile"), (blocks["/c/x"]["status"], blocks["/c/x"]["detail"]))

    def test_a_malformed_dump_is_refused(self):
        for text in (f"ICC\t560\t{A}\n", "FILE\t/a\n", f"FILE\t/a\nICC\tmany\t{A}\nSTATUS\tok\n", "FILE\t/a\nICC\t560\tA\nSTATUS\tok\n",
                     f"FILE\t/a\nICC\t560\t{A}\nICC\t560\t{A}\nSTATUS\tok\n", f"FILE\t/a\nSTATUS\tok\nICC\t560\t{A}\n",
                     "FILE\t/a\nFILE\t/b\nSTATUS\tok\n", "FILE\t/a\nPREVIEW\t1\timage/jpeg\t1x1\t9\tx\nSTATUS\tok\n"):
            with self.assertRaises(run_icc.IccParityError, msg=text):
                run_icc.parse_dump(text)


class ComparisonTests(unittest.TestCase):
    def test_a_profile_is_judged_by_its_sum_and_by_its_length_where_the_test_gives_one(self):
        self.assertEqual("match", run_icc.judge((A, None), (A, 560)))
        self.assertEqual("match", run_icc.judge((A, 560), (A, 560)))
        self.assertEqual("match", run_icc.judge(None, None))
        self.assertEqual("differs", run_icc.judge((A, None), (B, 560)))
        self.assertEqual("differs", run_icc.judge((A, 560), (A, 561)))
        self.assertEqual("missing", run_icc.judge((A, None), None))
        self.assertEqual("extra", run_icc.judge(None, (A, 560)))

    def test_totals_count_every_file_and_a_file_that_was_not_read_is_its_status(self):
        expected = {"one.jpg": (A, None), "two.png": (B, 9), "none.avif": None, "gone.jpg": (C, None), "bad.jpg": (C, None)}
        origin = {name: ["a"] for name in expected}
        root = Path("/c")
        paths = {name: (None if name == "gone.jpg" else root / name) for name in expected}
        dumped = {
            run_icc.key(root / "one.jpg"): {"profile": (A, 560), "status": "ok", "detail": ""},
            run_icc.key(root / "two.png"): {"profile": None, "status": "ok", "detail": ""},
            run_icc.key(root / "none.avif"): {"profile": None, "status": "ok", "detail": ""},
            run_icc.key(root / "bad.jpg"): {"profile": None, "status": "refused", "detail": "InvalidIccProfile"},
        }
        report = run_icc.compare(expected, origin, dumped, paths)
        self.assertEqual("Icc", report["kind"])
        self.assertEqual({"files": 5, "profiles": 4, "match": 2, "missing": 1, "no-input": 1, "refused": 1}, report["totals"])
        self.assertEqual({"ok": 3, "no-input": 1, "refused": 1}, report["statuses"])
        self.assertEqual(0.4, report["rate"])
        differing = run_icc.summary(report)["differing"]
        self.assertEqual(["bad.jpg", "gone.jpg", "two.png"], [entry["file"] for entry in differing])
        text = run_icc.text_report(report)
        self.assertIn("bad.jpg [refused] refused", text)
        self.assertIn("InvalidIccProfile", text)
        self.assertIn("two.png [ok] missing", text)

    def test_a_file_is_looked_up_under_the_forward_slash_path_the_dump_tool_was_given(self):
        # A native Windows path would come back from the tool with its backslashes escaped.
        self.assertEqual("C:/corpus/one.jpg", run_icc.key(PureWindowsPath(r"C:\corpus\one.jpg")))
        self.assertEqual("/c/one.jpg", run_icc.key(PurePosixPath("/c/one.jpg")))
        dumped = run_icc.parse_dump(f"FILE\t/c/one.jpg\nICC\t560\t{A}\nSTATUS\tok\n")
        report = run_icc.compare({"one.jpg": (A, None)}, {}, dumped, {"one.jpg": Path("/c") / "one.jpg"})
        self.assertEqual({"files": 1, "profiles": 1, "match": 1}, report["totals"])

    def test_a_file_the_dump_did_not_answer_for_is_a_structural_failure(self):
        with self.assertRaises(run_icc.IccParityError):
            run_icc.compare({"one.jpg": (A, None)}, {}, {}, {"one.jpg": Path("/c/one.jpg")})


if __name__ == "__main__":
    unittest.main()
