import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch
from contextlib import redirect_stdout, redirect_stderr
from tempfile import TemporaryDirectory
import io
import json
import subprocess

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


class HarnessExecutionTests(unittest.TestCase):
    def test_optional_main_records_skip_not_pass(self):
        with TemporaryDirectory() as temp, patch.object(h, "available", return_value=False), redirect_stdout(io.StringIO()):
            self.assertEqual(0, h.main(["--output", temp]))
            self.assertEqual("skipped", json.loads((Path(temp) / "report.json").read_text())["status"])

    def test_required_main_records_failure_not_skip(self):
        with TemporaryDirectory() as temp, patch.object(h, "available", side_effect=RuntimeError("Required tools missing")), redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
            self.assertEqual(1, h.main(["--required", "--output", temp]))
            self.assertEqual("failed", json.loads((Path(temp) / "report.json").read_text())["status"])

    def test_nonzero_tool_exit_is_preserved_and_fails(self):
        result = subprocess.CompletedProcess(["exiv2"], 2, "diagnostic", "error")
        with TemporaryDirectory() as temp, patch.object(h, "available", return_value=True), patch.object(h.subprocess, "run", return_value=result), redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
            self.assertEqual(1, h.main(["--required", "--output", temp]))
            report = json.loads((Path(temp) / "report.json").read_text())
            self.assertEqual("failed", report["status"])
            self.assertEqual(2, report["commands"][0]["exitCode"])
