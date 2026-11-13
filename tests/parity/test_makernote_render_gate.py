import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).parents[2]
sys.path.insert(0, str(ROOT / "tools" / "parity"))
import makernote_render_gate as gate  # noqa: E402


class MakerNoteRenderGateTests(unittest.TestCase):
    GROUP = "Exif.PentaxDng"

    def report(self, *, interpreted="match-interpreted", raw="match-value", rows_dropped=0):
        return {
            "by_family": {self.GROUP: {"match-value": 1}},
            "interpreted": {
                "by_family": {self.GROUP: {"match-interpreted": 1}},
            },
            "per_file": [{
                "corpus": "exiv2", "label": "exiv2:sample.dng", "rows_dropped": rows_dropped,
                "rows": [{
                    "key": self.GROUP + ".Quality", "outcome": raw,
                    "translated": True, "interpreted": interpreted,
                    "our_interpreted": "Best", "their_interpreted": "Best",
                }],
            }],
        }

    def test_reports_raw_and_interpreted_counts_separately(self):
        metrics = gate.group_metrics(self.report(), self.GROUP)
        self.assertEqual(metrics["keys_found"], 1)
        self.assertEqual(metrics["raw_values_equal"], 1)
        self.assertEqual(metrics["interpreted_keys_found"], 1)
        self.assertEqual(metrics["interpreted_values_equal"], 1)
        self.assertTrue(gate.evaluate(self.report(), [self.GROUP])[0])

    def test_untranslated_or_different_rendering_fails(self):
        for verdict in ("not-interpreted", "interpreted-diff"):
            with self.subTest(verdict=verdict):
                report = self.report(interpreted=verdict)
                passed, message = gate.evaluate(report, [self.GROUP])
                self.assertFalse(passed)
                self.assertIn("interpreted=" + repr(verdict), message)

    def test_raw_key_or_value_difference_fails(self):
        for outcome in ("value-diff", "match-key"):
            with self.subTest(outcome=outcome):
                report = self.report(raw=outcome)
                report["by_family"][self.GROUP] = {outcome: 1}
                passed, message = gate.evaluate(report, [self.GROUP])
                self.assertFalse(passed)
                self.assertIn("value-diff=1", message)

    def test_unknown_numeric_tag_fallback_is_counted_but_does_not_fail_raw_parity(self):
        report = self.report()
        report["by_family"][self.GROUP]["extra-key"] = 1
        report["per_file"][0]["rows"].append({
            "key": self.GROUP + ".0x006b", "outcome": "extra-key",
        })
        passed, message = gate.evaluate(report, [self.GROUP])
        self.assertTrue(passed, message)
        self.assertIn("retained numeric unknown-tag rows: 1", message)

    def test_named_extra_key_still_fails(self):
        report = self.report()
        report["by_family"][self.GROUP]["extra-key"] = 1
        report["per_file"][0]["rows"].append({
            "key": self.GROUP + ".UnexpectedNamedTag", "outcome": "extra-key",
        })
        passed, message = gate.evaluate(report, [self.GROUP])
        self.assertFalse(passed)
        self.assertIn("raw outcome extra-key", message)

    def test_exact_documented_file_refusal_is_scoped_by_name_and_reason(self):
        report = self.report()
        report["by_family"][self.GROUP] = {"not-read": 1}
        report["per_file"] = [{
            "label": "exiv2:issue_1706_poc.exv", "detail": "LimitExceeded", "status": "refused",
            "rows_dropped": 0, "rows": [{"key": self.GROUP + ".Quality", "outcome": "not-read"}],
        }]
        passed, message = gate.evaluate(report, [self.GROUP])
        self.assertTrue(passed, message)
        self.assertIn("documented reader refusal", message)
        report["per_file"][0]["detail"] = "InvalidExif"
        passed, message = gate.evaluate(report, [self.GROUP])
        self.assertFalse(passed)
        self.assertIn("raw outcome not-read", message)

    def test_truncated_rows_fail_closed(self):
        passed, message = gate.evaluate(self.report(rows_dropped=1), [self.GROUP])
        self.assertFalse(passed)
        self.assertIn("report dropped rows", message)

    def test_exact_documented_key_exception_is_scoped_to_that_key(self):
        report = self.report(interpreted="not-interpreted")
        passed, _ = gate.evaluate(report, [self.GROUP], {self.GROUP + ".LensType": "lens database excluded"})
        self.assertFalse(passed)
        passed, message = gate.evaluate(report, [self.GROUP], {self.GROUP + ".Quality": "explicit corpus exception"})
        self.assertTrue(passed, message)

    def test_inventory_orders_largest_unrendered_gap_first(self):
        report = {
            "by_family": {},
            "interpreted": {"by_family": {
                "Exif.Other": {"not-interpreted-translated": 2},
                "Exif.PentaxDng": {"not-interpreted-translated": 7},
            }},
        }
        rows = gate.inventory(report)
        self.assertEqual([group for group, _ in rows], ["Exif.PentaxDng", "Exif.Other"])


if __name__ == "__main__":
    unittest.main()
