"""Report interpreted MakerNote coverage and gate groups completed against the pinned output.

The report compares the interpreted text independently of raw key/value parity.  This gate is for a
finished ``Exif.<Group>`` only: every value the reference translates must have the same rendering,
and raw key/value differences in that group are rejected unless named as a documented exception.
"""
from __future__ import annotations

import argparse
import base64
import json
import re
import zlib
from collections import Counter
from pathlib import Path
from typing import Any

MATCH_VALUE = "match-value"
MATCH_KEY = "match-key"
VALUE_DIFF = "value-diff"
MISSING_KEY = "missing-key"
EXTRA_KEY = "extra-key"
NOT_READ = "not-read"
MATCH_INTERPRETED = "match-interpreted"
INTERPRETED_DIFF = "interpreted-diff"
NOT_INTERPRETED_TRANSLATED = "not-interpreted-translated"

# Interpretation exception: Exiv2 obtains this name from its separately maintained lens-ID database.
DOCUMENTED_EXCEPTIONS: dict[str, str] = {
    "Exif.Pentax.LensType": (
        "Exiv2 supplies a lens name from its external lens-ID database; this project intentionally "
        "does not add that database and keeps the raw encoded value."
    ),
    "Exif.PentaxDng.LensType": (
        "Exiv2 supplies a lens name from its external lens-ID database; this project intentionally "
        "does not add that database and keeps the raw encoded value."
    ),
}

# The parser deliberately refuses these two damaged/over-budget inputs. These are exact pinned corpus
# files and exact refusal codes, not an exemption for the MakerNote family or for similar future files.
DOCUMENTED_FILE_REFUSALS: dict[str, tuple[str, str]] = {
    "exiv2:issue_1706_poc.exv": ("LimitExceeded", "the EXIF directory exceeds configured reader limits"),
    "exiv2:issue_1920_poc.tiff": ("InvalidExif", "the EXIF structure is malformed"),
    "exiv2:IMGP0006-min.jpg": ("InvalidJpeg", "the JPEG structure is malformed"),
}

# The reader exposes unnamed raw tags as 0xNNNN keys. Exiv2 has no corresponding named key, so these
# are informative extras, not discrepancies in a key that both tools print.
OPAQUE_NUMERIC_KEY = re.compile(r"^Exif\.[A-Za-z0-9]+\.0x[0-9a-fA-F]{4,8}$")


def group_metrics(report: dict[str, Any], group: str) -> dict[str, int]:
    raw = report.get("by_family", {}).get(group, {})
    interpreted = report.get("interpreted", {}).get("by_family", {}).get(group, {})
    equal = interpreted.get(MATCH_INTERPRETED, 0)
    different = interpreted.get(INTERPRETED_DIFF, 0)
    untranslated = interpreted.get(NOT_INTERPRETED_TRANSLATED, 0)
    return {
        "keys_found": raw.get(MATCH_VALUE, 0) + raw.get(MATCH_KEY, 0) + raw.get(VALUE_DIFF, 0),
        "raw_values_equal": raw.get(MATCH_VALUE, 0),
        "interpreted_keys_found": equal + different + untranslated,
        "interpreted_values_equal": equal,
        "unrendered": untranslated,
        "interpreted_different": different,
        "missing_keys": raw.get(MISSING_KEY, 0),
        "extra_keys": raw.get(EXTRA_KEY, 0),
        "not_read": raw.get(NOT_READ, 0),
        "raw_value_different": raw.get(VALUE_DIFF, 0) + raw.get(MATCH_KEY, 0),
    }


def inventory(report: dict[str, Any]) -> list[tuple[str, dict[str, int]]]:
    groups = set(report.get("by_family", {})) | set(report.get("interpreted", {}).get("by_family", {}))
    rows = [(group, group_metrics(report, group)) for group in groups if group.startswith("Exif.")]
    return sorted((row for row in rows if row[1]["interpreted_keys_found"] or row[1]["missing_keys"]),
                  key=lambda row: (-row[1]["unrendered"] - row[1]["interpreted_different"], row[0]))


def evaluate(report: dict[str, Any], groups: list[str],
             exceptions: dict[str, str] | None = None) -> tuple[bool, str]:
    if not isinstance(report.get("per_file"), list):
        return False, "parity report has no per_file rows"
    exceptions = exceptions or {}
    failures: list[str] = []
    output = ["MakerNote interpreted parity gate (Exiv2 0.28.9):"]
    for group in groups:
        metrics = group_metrics(report, group)
        output.append(
            f"  {group}: interpreted keys found/equal="
            f"{metrics['interpreted_keys_found']}/{metrics['interpreted_values_equal']}; "
            f"raw keys found/equal={metrics['keys_found']}/{metrics['raw_values_equal']}"
        )
        if metrics["interpreted_keys_found"] == 0:
            failures.append(f"{group}: no reference-translated corpus values were measured")
        if metrics["missing_keys"] or metrics["raw_value_different"]:
            failures.append(
                f"{group}: raw parity has missing={metrics['missing_keys']}, "
                f"value-diff={metrics['raw_value_different']}"
            )

        rows_seen = 0
        opaque_extras = 0
        refused_rows: Counter[str] = Counter()
        key_exception_rows: Counter[str] = Counter()
        for file in report["per_file"]:
            if file.get("rows_dropped", 0):
                # The caller requests a high row limit for the corpus; fail closed if any rows were cut.
                failures.append(f"{group}: {file.get('label')}: report dropped rows")
                continue
            label = file.get("label", "")
            for row in file.get("rows", []):
                key = row.get("key", "")
                if not key.startswith(group + "."):
                    continue
                rows_seen += 1
                outcome = row.get("outcome")
                if outcome == EXTRA_KEY:
                    if OPAQUE_NUMERIC_KEY.fullmatch(key):
                        opaque_extras += 1
                    else:
                        failures.append(f"{group}: {label} {key}: raw outcome {outcome}")
                    continue
                if outcome == NOT_READ:
                    refusal = DOCUMENTED_FILE_REFUSALS.get(label)
                    if refusal is not None and file.get("detail") == refusal[0] and file.get("status") == "refused":
                        refused_rows[label] += 1
                        continue
                    failures.append(f"{group}: {label} {key}: raw outcome {outcome} ({file.get('detail', '')})")
                    continue
                if key in exceptions:
                    if row.get("translated") is True:
                        key_exception_rows[key] += 1
                    continue
                if outcome in (MISSING_KEY, VALUE_DIFF, MATCH_KEY):
                    failures.append(f"{group}: {label} {key}: raw outcome {outcome}")
                if row.get("translated") is True and row.get("interpreted") != MATCH_INTERPRETED:
                    failures.append(
                        f"{group}: {label} {key}: interpreted={row.get('interpreted')!r}, "
                        f"ours={row.get('our_interpreted', row.get('our_value', ''))!r}, "
                        f"Exiv2={row.get('their_interpreted', '')!r}"
                    )
        if rows_seen == 0:
            failures.append(f"{group}: no report rows were available (check row limit and group spelling)")
        if opaque_extras:
            output.append(f"    retained numeric unknown-tag rows: {opaque_extras} (no Exiv2 named key exists)")
        for label, count in sorted(refused_rows.items()):
            output.append(f"    documented reader refusal: {label}: {count} MakerNote rows; {DOCUMENTED_FILE_REFUSALS[label][1]}")
        for key, reason in exceptions.items():
            if key.startswith(group + "."):
                output.append(
                    f"    documented interpretation exception: {key}: "
                    f"{key_exception_rows[key]} translated rows; {reason}"
                )

    if failures:
        output.append("FAIL")
        output.extend("  " + failure for failure in failures)
        return False, "\n".join(output) + "\n"
    output.append("PASS")
    return True, "\n".join(output) + "\n"


def encode_inventory(rows: list[tuple[str, dict[str, int]]]) -> str:
    payload = [{"group": group, **metrics} for group, metrics in rows]
    return base64.b64encode(zlib.compress(json.dumps(payload, separators=(",", ":"), sort_keys=True).encode(), 9)).decode()


def gap_details(report: dict[str, Any], groups: list[str]) -> list[dict[str, Any]]:
    """Compact actionable samples for translated values not yet rendered exactly."""
    gaps: Counter[tuple[str, str, str, str]] = Counter()
    examples: dict[tuple[str, str, str, str], list[str]] = {}
    for file in report.get("per_file", []):
        label = file.get("label", "")
        for row in file.get("rows", []):
            key = row.get("key", "")
            group = next((candidate for candidate in groups if key.startswith(candidate + ".")), None)
            if group is None or row.get("translated") is not True or row.get("interpreted") == MATCH_INTERPRETED:
                continue
            signature = (group, key, str(row.get("our_interpreted", "")), str(row.get("their_interpreted", "")))
            gaps[signature] += 1
            examples.setdefault(signature, [])
            if label and len(examples[signature]) < 3 and label not in examples[signature]:
                examples[signature].append(label)
    return [
        {"group": group, "key": key, "count": count, "ours": ours, "exiv2": theirs,
         "files": examples[(group, key, ours, theirs)]}
        for (group, key, ours, theirs), count in sorted(gaps.items())
    ]


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report", type=Path)
    parser.add_argument("--require", action="append", default=[], metavar="EXIF.GROUP",
                        help="require interpreted and raw parity for a completed group; repeatable")
    parser.add_argument("--inventory", action="store_true", help="emit the corpus group inventory as one notice")
    args = parser.parse_args(argv)
    try:
        report = json.loads(args.report.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        print(f"MakerNote parity gate could not read report: {error}")
        return 1
    if args.inventory:
        rows = inventory(report)
        print("MakerNote interpreter inventory (largest full interpretation gap first):")
        for group, metrics in rows:
            gap = metrics["unrendered"] + metrics["interpreted_different"]
            print(f"  {group}: translated keys found/equal="
                  f"{metrics['interpreted_keys_found']}/{metrics['interpreted_values_equal']}; "
                  f"gap={gap}; raw keys found/equal={metrics['keys_found']}/{metrics['raw_values_equal']}")
        print(f"::notice title=MakerNote interpreter inventory::{encode_inventory(rows)}")
    if not args.require:
        return 0
    passed, message = evaluate(report, args.require, DOCUMENTED_EXCEPTIONS)
    if not passed:
        failure_lines = message.split("FAIL\n", 1)[-1].splitlines()
        payload = base64.b64encode(zlib.compress(json.dumps(failure_lines, separators=(",", ":")).encode(), 9)).decode()
        print(f"::error title=MakerNote parity failures::{payload}", flush=True)
    print(message, end="", flush=True)
    gaps = gap_details(report, args.require)
    if gaps:
        payload = base64.b64encode(zlib.compress(json.dumps(gaps, separators=(",", ":"), sort_keys=True).encode(), 9)).decode()
        print(f"::notice title=MakerNote parity gaps::{payload}", flush=True)
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
