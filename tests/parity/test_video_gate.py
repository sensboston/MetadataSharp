import contextlib
import io
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).parents[2]
sys.path.insert(0, str(ROOT / "tools" / "parity"))
import video_gate  # noqa: E402


class VideoGateTests(unittest.TestCase):
    def report(self, *, rows=None):
        return {"per_file": [
            {"corpus": "generated", "label": name.removeprefix("generated:"), "format": fmt,
             "status": "ok", "rows": []}
            for name, fmt in (
                ("generated:authored-asf.asf", "asf"),
                ("generated:authored-avi.avi", "avi"),
                ("generated:authored-matroska.mkv", "mkv"),
                ("generated:authored-quicktime.mp4", "mp4"),
                ("generated:authored-webm.webm", "webm"),
            )
        ] + [
            {"corpus": "exiv2", "label": "flame.avi", "format": "avi", "status": "ok",
             "rows": rows or []}
        ]}

    def test_all_keys_must_match_and_only_the_two_documented_avi_values_are_allowed(self):
        rows = [
            {"key": "Xmp.video.NumOfColours", "outcome": "value-diff", "our_value": "Unspecified",
             "their_value": "1263424842  1263424842"},
            {"key": "Xmp.video.NumIfImpColours", "outcome": "value-diff", "our_value": "All",
             "their_value": "1816  1816"},
        ]
        passed, message = video_gate.evaluate(self.report(rows=rows))
        self.assertTrue(passed, message)
        self.assertIn("PASS", message)
        self.assertIn("generated:authored-wave.wav", message)
        self.assertIn("unknown image type", message)

    def test_any_extra_or_missing_key_fails_and_names_which_side_has_it(self):
        report = self.report(rows=[
            {"key": "Xmp.video.Extra", "outcome": "extra-key", "our_value": "1"},
            {"key": "Xmp.video.Missing", "outcome": "missing-key", "their_value": "2"},
        ])
        passed, message = video_gate.evaluate(report)
        self.assertFalse(passed)
        self.assertIn("ours-only keys (must be removed): Xmp.video.Extra", message)
        self.assertIn("reference-only keys (must be implemented): Xmp.video.Missing", message)

    def test_permitted_avi_divergences_do_not_have_to_occur(self):
        passed, message = video_gate.evaluate(self.report())
        self.assertTrue(passed, message)

    def test_truncated_rows_fail_closed_instead_of_claiming_key_set_parity(self):
        report = self.report()
        report["per_file"][0]["rows_dropped"] = 1
        passed, message = video_gate.evaluate(report)
        self.assertFalse(passed)
        self.assertIn("key/value gate requires complete rows", message)

    def test_missing_authored_mkv_fails_the_gate(self):
        report = self.report()
        report["per_file"] = [entry for entry in report["per_file"] if entry["label"] != "authored-matroska.mkv"]
        passed, message = video_gate.evaluate(report)
        self.assertFalse(passed)
        self.assertIn("generated:authored-matroska.mkv", message)
        self.assertIn("generated report entries:", message)
        self.assertIn("generated:authored-webm.webm [format=webm]", message)

    def test_main_writes_failure_reason_to_github_step_summary(self):
        report = self.report(rows=[{"key": "Xmp.video.Extra", "outcome": "extra-key"}])
        with tempfile.TemporaryDirectory() as directory:
            report_path = Path(directory) / "parity-report.json"
            summary_path = Path(directory) / "summary.md"
            report_path.write_text(json.dumps(report), encoding="utf-8")
            stdout = io.StringIO()
            stderr = io.StringIO()
            with patch.dict(os.environ, {"GITHUB_STEP_SUMMARY": str(summary_path)}):
                with contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
                    result = video_gate.main([str(report_path)])
            self.assertEqual(result, 1)
            contents = summary_path.read_text(encoding="utf-8")
            self.assertIn("ours-only keys (must be removed): Xmp.video.Extra", contents)
            self.assertTrue(contents.endswith("```\n"))


if __name__ == "__main__":
    unittest.main()
