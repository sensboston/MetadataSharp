import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("harness", Path(__file__).parents[2] / "tools/reference/harness.py")
h = importlib.util.module_from_spec(spec)
spec.loader.exec_module(h)


class HarnessPolicyTests(unittest.TestCase):
    def test_required_missing_tools_fail(self):
        with self.assertRaises(RuntimeError): h.available(True, lambda _: None)

    def test_optional_missing_tools_skip_explicitly(self):
        self.assertFalse(h.available(False, lambda _: None))
        self.assertTrue(h.available(True, lambda _: "/tool"))

    def test_versions_are_exact_not_latest(self):
        h.check_version("exiv2", "exiv2 0.27.6\nCopyright", "0.27.6")
        h.check_version("exiftool", "12.76\n", "12.76")
        with self.assertRaises(RuntimeError): h.check_version("exiftool", "12.77", "12.76")
        with self.assertRaises(RuntimeError): h.check_version("exiv2", "garbage", "0.27.6")

    def test_plain_values_are_normalized_without_display_labels(self):
        result = h.exiv_values("Exif.Image.Orientation  Short  1  1\nExif.Image.Artist Ascii 6 Alice\n")
        self.assertEqual(result["Exif.Image.Orientation"], ("Short", 1, "1"))
        with self.assertRaises(RuntimeError): h.exiv_values("x Short 1 1\nx Short 1 1")
        with self.assertRaises(RuntimeError): h.exiv_values("not a row")

    def test_only_reviewed_warning_categories_are_accepted(self):
        self.assertTrue(h.classify_warning("Missing required JPEG IFD0 tag 0x011a XResolution"))
        self.assertFalse(h.classify_warning("Bad IFD offset"))
        self.assertFalse(h.classify_warning("Missing required JPEG ExifIFD tag 0x9000 ExifVersion"))

    def test_fixture_specific_preservation_check_detects_changes(self):
        self.assertEqual(h.scan_tail(b"header\xff\xdascan"), b"\xff\xdascan")
        with self.assertRaises(RuntimeError): h.scan_tail(b"missing marker")
