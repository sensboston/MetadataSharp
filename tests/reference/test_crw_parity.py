"""Offline checks for the comparer of CRW edits and the authored MRW / RAF files."""
import importlib.util
import struct
import tempfile
import unittest
from pathlib import Path


def load(name):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).parents[2] / "tools" / "reference" / f"{name}.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


x = load("crw_parity")
f = load("raw_container_fixtures")


class Tool:
    """Stands in for exiv2: prints what the test says a file holds."""

    def __init__(self, keys, comments):
        self.keys, self.comments = keys, comments

    def __call__(self, command, timeout=120):
        name = Path(command[-1]).name
        if command[1] == "-pc":
            return 0, self.comments.get(name, "") + "\n", ""
        return 0, "".join(f"{key}  {value}\n" for key, value in self.keys.get(name, [])), ""


class CheckTests(unittest.TestCase):
    def work(self, rows):
        directory = Path(tempfile.mkdtemp())
        (directory / "ours.tsv").write_text("\n".join("\t".join(row) for row in rows) + "\n", encoding="utf-8")
        return directory

    def test_a_tag_and_a_comment_read_back_are_ok_and_a_refusal_is_counted(self):
        work = self.work([("s00000", "1", "exif", "Exif.Image.Model", "written", "New"),
                          ("s00000", "2", "comment", "Comment", "written", "hello"),
                          ("s00000", "3", "canon", "Exif.Canon.OwnerName", "refused TagNotEditable", "no")])
        tool = Tool({"s00000.crw": [("Exif.Image.Model", "Old")], "s00000.1.ours.crw": [("Exif.Image.Model", "New")],
                     "s00000.2.ours.crw": [("Exif.Image.Model", "Old")]}, {"s00000.2.ours.crw": "hello"})
        counts, _ = x.check(work, {"s00000": "a.crw"}, "exiv2", tool)
        self.assertEqual({("exif", "ok"): 1, ("comment", "ok"): 1, ("canon", "refused TagNotEditable"): 1}, dict(counts))

    def test_a_comment_the_tool_does_not_print_and_a_key_it_lost_are_named(self):
        work = self.work([("s00000", "1", "comment", "Comment", "written", "hello"),
                          ("s00000", "2", "comment", "Comment", "written", "hello")])
        tool = Tool({"s00000.crw": [("Exif.Image.Model", "Old")], "s00000.1.ours.crw": [("Exif.Image.Model", "Old")]},
                    {"s00000.1.ours.crw": "other", "s00000.2.ours.crw": "hello"})
        counts, _ = x.check(work, {"s00000": "a.crw"}, "exiv2", tool)
        self.assertEqual({("comment", "comment the tool does not print"): 1, ("comment", "another key gone"): 1}, dict(counts))


class FixtureTests(unittest.TestCase):
    def test_the_mrw_file_holds_the_tiff_file_in_its_ttw_block(self):
        tiff = b"II*\0" + bytes(10)
        data = f.mrw(tiff)
        self.assertEqual(b"\0MRM", data[:4])
        at = 8 + 8 + struct.unpack_from(">I", data, 12)[0]
        self.assertEqual(b"\0TTW", data[at:at + 4])
        self.assertEqual(tiff, data[at + 8:at + 8 + struct.unpack_from(">I", data, at + 4)[0]])
        self.assertEqual(at + 8 + len(tiff), 8 + struct.unpack_from(">I", data, 4)[0])

    def test_the_raf_header_places_the_jpeg_file_and_the_tiff_structure(self):
        jpeg, tiff = b"\xff\xd8" + bytes(20) + b"\xff\xd9", f.tiff_structure()
        data = f.raf(jpeg, tiff)
        self.assertEqual(b"FUJIFILM", data[:8])
        start, length = struct.unpack_from(">II", data, 84)
        self.assertEqual(jpeg, data[start:start + length])
        start, length = struct.unpack_from(">II", data, 100)
        self.assertEqual(tiff, data[start:start + length])
        self.assertEqual(b"raf structure\0", tiff[struct.unpack_from("<I", tiff, 18)[0]:])


if __name__ == "__main__":
    unittest.main()
