#!/usr/bin/env python3
"""Fail-closed differential gate for the Exiv2 -pt tag listing."""
from __future__ import annotations

import argparse
import json
import re
import shlex
import subprocess
import sys
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
if str(HERE) not in sys.path:
    sys.path.insert(0, str(HERE))
import corpus  # noqa: E402

TITLE = "PARITY tag print"

def _catalog_key_ids() -> dict[str, int]:
    data = json.loads((ROOT / "src/MetadataSharp.Core/tag-catalog-data.json").read_text(encoding="utf-8"))
    return {"Exif." + group["name"] + "." + name: tag["id"]
            for group in data["exifGroups"] for tag in group["tags"] for name in tag["names"]}

CATALOG_KEY_IDS = _catalog_key_ids()
CATALOG_KEYS = set(CATALOG_KEY_IDS)
_NUMERIC_KEY = re.compile(r"^(Exif\..+)\.0[xX]([0-9A-Fa-f]+)$")


def _canonical_key(key: str) -> tuple[str, int] | tuple[str]:
    if key in CATALOG_KEY_IDS:
        group = key.rsplit(".", 1)[0]
        return group, CATALOG_KEY_IDS[key]
    match = _NUMERIC_KEY.fullmatch(key)
    if match:
        return match.group(1), int(match.group(2), 16)
    return (key,)
_RAW_NUMERIC_TAG = re.compile(r"\.0[xX][0-9A-Fa-f]+$")


def _is_raw_numeric_tag(key: str) -> bool:
    return bool(_RAW_NUMERIC_TAG.search(key))


PINNED_EXIV2_COMMIT = "b0859fc61fea5dedfc898aece6b15893ddf4314e"

# Exception keys are upstream test/data basenames, never extensions or directories. Every retained
# entry must describe why this precise fixture cannot serve as a -pt comparator.
_TAG_PRINT_KEY_EXCEPTIONS: dict[str, str] = {
    "Exif.GPSInfo.GPSAltitudeRef": (
        "MetadataSharp follows the expanded Exif 3.0 ellipsoidal/sea-level definitions, while the pinned "
        "Exiv2 0.28.9 print table exposes only its earlier above/below sea-level labels. The raw field and "
        "numeric code remain covered by the other tag/value gates; this exact key's translated wording differs by revision."
    ),
}

# Exact display aliases, not descriptions copied from Exiv2. These pairs represent the same assigned
# values in the local Exif vocabulary and Exiv2 0.28.9's shorter/older print terminology. Any other
# value for these keys still fails comparison.
_TAG_PRINT_DISPLAY_ALIASES: dict[str, dict[str, str]] = {
    "Exif.Photo.MeteringMode": {"Multi-segment": "Pattern"},
    "Exif.Photo.ExposureProgram": {"Auto": "Normal program"},
    "Exif.Image.CalibrationIlluminant1": {"Standard light A": "17"},
    "Exif.Image.CalibrationIlluminant2": {"D65": "21"},
    "Exif.Photo.SensitivityType": {
        "0": "Unknown",
        "1": "Standard output sensitivity (SOS)",
        "2": "Recommended exposure index (REI)",
        "3": "ISO speed",
        "4": "Standard output sensitivity (SOS) and recommended exposure index (REI)",
        "5": "Standard output sensitivity (SOS) and ISO speed",
        "6": "Recommended exposure index (REI) and ISO speed",
        "7": "Standard output sensitivity (SOS) and recommended exposure index (REI) and ISO speed",
    },
}
_TAG_PRINT_DISPLAY_REASONS = {
    "Exif.Image.CalibrationIlluminant1": "The DNG field's local catalog entry preserves its numeric code; Exiv2 renders code 17 with the standard LightSource label. This exact code-to-label pair is accepted without extending the other-source interpretation table.",
    "Exif.Image.CalibrationIlluminant2": "The DNG field's local catalog entry preserves its numeric code; Exiv2 renders code 21 with the standard LightSource label. This exact code-to-label pair is accepted without extending the other-source interpretation table.",
    "Exif.Photo.MeteringMode": "The same EXIF metering code is called Pattern by the local vocabulary and Multi-segment by Exiv2; these terms describe the same assigned mode.",
    "Exif.Photo.ExposureProgram": "The same EXIF exposure-program code is called Normal program by the local vocabulary and Auto by Exiv2; this is a terminology-only difference.",
    "Exif.Photo.SensitivityType": "MetadataSharp renders the assigned EXIF meaning; Exiv2 0.28.9 prints the stored numeric code for this tag. Exact code-to-label pairs are listed and checked, not generalized to arbitrary values.",
}

_TAG_PRINT_EXCEPTIONS: dict[str, str] = {
    # These are upstream expectation basenames for command-line/unit tests, not metadata files.
    # corpus.discover_exiv2 deliberately does not guess an image for a *_test input.
    "addmoddel": "Synthetic command-line test expectation; no corresponding media input is present in the pinned test/data tree.",
    "conversions": "Synthetic conversion-test expectation; no corresponding media input is present in the pinned test/data tree.",
    "crw-test": "Synthetic CRW test expectation; no corresponding CRW input is present in the pinned test/data tree.",
    "exifdata-test": "Synthetic ExifData unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
    "exiv2-bug1145a.exv_test": "Synthetic regression-test expectation; no corresponding EXV input is present in the pinned test/data tree.",
    "exiv2-test": "Synthetic CLI test expectation; no corresponding media input is present in the pinned test/data tree.",
    "geotag-test": "Synthetic geotag unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
    "icc-test": "Synthetic ICC unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
    "iotest": "Synthetic I/O test expectation; no corresponding media input is present in the pinned test/data tree.",
    "iso65k-test": "Synthetic ISO-65K unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
    "modify-test": "Synthetic metadata-edit test expectation; no corresponding media input is present in the pinned test/data tree.",
    "nls-test": "Synthetic localization test expectation; no corresponding media input is present in the pinned test/data tree.",
    "png-test": "Synthetic PNG unit-test expectation; no corresponding PNG input is present in the pinned test/data tree.",
    "preview-test": "Synthetic preview unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
    "stdin-test": "Synthetic standard-input test expectation; no corresponding media input is present in the pinned test/data tree.",
    "stringto-test": "Synthetic string-formatting unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
    "test_pr_1905_poc1_ref": "Synthetic pull-request proof-of-concept expectation; its generated input is not checked into the pinned test/data tree.",
    "tiff-test": "Synthetic TIFF unit-test expectation; no corresponding TIFF input is present in the pinned test/data tree.",
    "webp-test": "Synthetic WebP unit-test expectation; no corresponding WebP input is present in the pinned test/data tree.",
    "write-test-debug": "Synthetic writer-debug test expectation; no corresponding media input is present in the pinned test/data tree.",
    "write-test": "Synthetic writer unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
    "write2-test": "Synthetic second writer unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
    "xmpparser-test": "Synthetic XMP parser unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
}


class TagPrintError(Exception):
    """A structural failure in the tag-print oracle or dump protocol."""


def _decode(value: str) -> str:
    slash = chr(92)
    result: list[str] = []
    at = 0
    while at < len(value):
        if value[at] != slash or at + 1 == len(value):
            result.append(value[at]); at += 1; continue
        code = value[at + 1]
        if code == "t": result.append("\t"); at += 2
        elif code == "n": result.append("\n"); at += 2
        elif code == "r": result.append("\r"); at += 2
        elif code == slash: result.append(slash); at += 2
        elif code == "x" and at + 3 < len(value):
            try: result.append(chr(int(value[at + 2:at + 4], 16))); at += 4
            except ValueError: result.append(slash); at += 1
        else: result.append(slash); at += 1
    return "".join(result)


def parse_exiv2(text: str) -> list[dict[str, Any]]:
    """Parse ``-pt`` rows: key, type, count and the one value Exiv2 prints (translated when known)."""
    rows: list[dict[str, Any]] = []
    for line in text.splitlines():
        pieces = line.split(None, 3)
        if len(pieces) < 3 or not pieces[0].startswith("Exif."):
            continue
        key, type_name, count_text = pieces[:3]
        if not count_text.isdecimal():
            continue
        rows.append({"key": key, "type": type_name, "count": int(count_text),
                     "value": pieces[3] if len(pieces) == 4 else ""})
    return rows


def project_dump(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """Select the text a ``-pt`` style print would show from each dump record."""
    return [{"key": row["key"], "type": row["type"], "count": row["count"],
             "value": row["translated"] or row["raw"], "_raw_value": row["raw"],
             "_has_translation": bool(row["translated"])}
            for row in rows if row["key"].startswith("Exif.")]


def parse_dump(text: str) -> dict[str, dict[str, Any]]:
    files: dict[str, dict[str, Any]] = {}
    current: dict[str, Any] | None = None
    for number, line in enumerate(text.splitlines(), 1):
        if not line:
            continue
        fields = line.split("\t")
        if fields[0] == "FILE" and len(fields) == 2:
            if current is not None and current["status"] is None:
                raise TagPrintError(f"dump:{number}: FILE record before the previous block ended")
            path = _decode(fields[1])
            if path in files:
                raise TagPrintError(f"dump:{number}: repeated FILE record {path}")
            current = {"rows": [], "status": None, "detail": ""}
            files[path] = current
        elif fields[0] == "TAG" and len(fields) in (5, 6) and current is not None and current["status"] is None:
            try:
                count = int(fields[3])
            except ValueError as error:
                raise TagPrintError(f"dump:{number}: invalid TAG count {fields[3]!r}") from error
            current["rows"].append({"key": _decode(fields[1]), "type": fields[2], "count": count,
                                    "raw": _decode(fields[4]), "translated": _decode(fields[5]) if len(fields) == 6 else ""})
        elif fields[0] == "STATUS" and len(fields) >= 2 and current is not None and current["status"] is None:
            current["status"] = fields[1]
            current["detail"] = _decode("\t".join(fields[2:]))
        else:
            raise TagPrintError(f"dump:{number}: unexpected record: {line!r}")
    for path, result in files.items():
        if result["status"] is None:
            raise TagPrintError(f"dump omitted the end status for {path}")
    return files


def compare_rows(reference: list[dict[str, Any]], actual: list[dict[str, Any]],
                 description_aliases: dict[str, dict[str, str]] | None = None,
                 accepted_aliases: Counter[tuple[str, str, str]] | None = None,
                 accepted_padding: Counter[str] | None = None,
                 accepted_names: Counter[tuple[str, str]] | None = None) -> list[dict[str, Any]]:
    """Compare rows as a multiset, allowing only exact, documented display aliases."""
    def identity(row: dict[str, Any]) -> tuple[Any, ...]:
        return _canonical_key(str(row.get("key"))), row.get("type"), row.get("count")

    expected_by_key: dict[tuple[Any, ...], list[dict[str, Any]]] = defaultdict(list)
    actual_by_key: dict[tuple[Any, ...], list[dict[str, Any]]] = defaultdict(list)
    for row in reference: expected_by_key[identity(row)].append(row)
    for row in actual: actual_by_key[identity(row)].append(row)
    differences: list[dict[str, Any]] = []
    for key in sorted(expected_by_key.keys() | actual_by_key.keys(), key=repr):
        expected = expected_by_key[key].copy()
        observed = actual_by_key[key].copy()
        for row in expected.copy():
            def is_padding_equivalent(candidate: dict[str, Any]) -> bool:
                key_name = str(row.get("key"))
                if row.get("type") != "Ascii" and key_name != "Exif.Photo.UserComment":
                    return False
                expected_value, actual_value = row.get("value"), candidate.get("value")
                return (isinstance(expected_value, str) and isinstance(actual_value, str)
                        and expected_value != actual_value
                        and expected_value.rstrip(" \0") == actual_value.rstrip(" \0"))

            match = next((candidate for candidate in observed
                          if candidate == row or (
                              _canonical_key(str(candidate.get("key"))) == _canonical_key(str(row.get("key")))
                              and candidate.get("value") == row.get("value")
                          ) or is_padding_equivalent(candidate) or (
                              description_aliases is not None
                              and description_aliases.get(str(row.get("key")), {}).get(str(row.get("value")))
                                  == candidate.get("value")
                          )), None)
            if match is not None:
                if (match.get("key") != row.get("key") and match.get("value") == row.get("value")
                        and accepted_names is not None):
                    accepted_names[(str(row.get("key")), str(match.get("key")))] += 1
                elif match != row and is_padding_equivalent(match) and accepted_padding is not None:
                    accepted_padding[str(row.get("type"))] += 1
                elif (match.get("value") != row.get("value") and accepted_aliases is not None):
                    accepted_aliases[(str(row.get("key")), str(row.get("value")), str(match.get("value")))] += 1
                expected.remove(row); observed.remove(match)
        while expected and observed:
            differences.append({"kind": "value-diff", "exiv2": expected.pop(), "metadatasharp": observed.pop()})
        differences.extend({"kind": "missing", "exiv2": row, "metadatasharp": None} for row in expected)
        differences.extend({"kind": "extra", "exiv2": None, "metadatasharp": row} for row in observed)
    return differences


def verify_raw_value_differences(differences: list[dict[str, Any]], raw_reference: list[dict[str, Any]],
                                 source_rows: list[dict[str, Any]]) -> tuple[list[dict[str, Any]], list[dict[str, str]]]:
    """Resolve -pt text differences only when Exiv2 -pv confirms identical raw value and structure."""
    raw_by_identity: dict[tuple[Any, ...], list[dict[str, Any]]] = defaultdict(list)
    source_by_identity: dict[tuple[Any, ...], list[dict[str, Any]]] = defaultdict(list)
    for row in raw_reference:
        raw_by_identity[( _canonical_key(str(row.get("key"))), row.get("type"), row.get("count"))].append(row)
    for row in source_rows:
        source_by_identity[(row.get("key"), row.get("type"), row.get("count"))].append(row)
    remaining: list[dict[str, Any]] = []
    verified: list[dict[str, str]] = []
    for difference in differences:
        if difference.get("kind") != "value-diff":
            remaining.append(difference)
            continue
        expected, observed = difference["exiv2"], difference["metadatasharp"]
        identity = (_canonical_key(str(expected.get("key"))), expected.get("type"), expected.get("count"))
        local_key = (observed.get("key"), observed.get("type"), observed.get("count"))
        local_source = next((row for row in source_by_identity.get(local_key, [])
                             if (row.get("translated") or row.get("raw")) == observed.get("value")), None)
        if local_source is None:
            remaining.append(difference)
            continue
        raw_match = next((row for row in raw_by_identity.get(identity, [])
                          if row.get("value") == local_source.get("raw")), None)
        if raw_match is None:
            remaining.append(difference)
            continue
        raw_by_identity[identity].remove(raw_match)
        verified.append({"key": str(expected.get("key")), "exiv2_pt": str(expected.get("value")),
                         "metadatasharp_pt": str(observed.get("value")),
                         "raw_value": str(raw_match.get("value"))})
    return remaining, verified


def _summary(report: dict[str, Any]) -> dict[str, Any]:
    # Keep the one-line workflow annotation below GitHub's annotation payload limit while making
    # the first discrepancies actionable; the full report is written to the JSON artifact.
    selected: list[dict[str, Any]] = []
    refusal_counts: Counter[str] = Counter()
    sampled_extensions: set[str] = set()
    for item in report["differing"]:
        kind = item["kind"]
        extension = Path(item["file"]).suffix.lower()
        if kind in ("metadatasharp-refused", "exiv2-refused"):
            if refusal_counts[kind] >= 2: continue
            refusal_counts[kind] += 1
        elif kind == "rows-differ":
            if extension in sampled_extensions: continue
            sampled_extensions.add(extension)
        if len(selected) >= 8: break
        selected.append(item)
    samples = []
    for item in selected:
        sample = {key: value for key, value in item.items() if key != "rows"}
        if "rows" in item:
            sample["rows"] = []
            for row in item["rows"][:5]:
                compact = dict(row)
                for side in ("exiv2", "metadatasharp"):
                    record = compact.get(side)
                    if isinstance(record, dict):
                        compact[side] = {key: value[:160] + "…" if isinstance(value, str) and len(value) > 160 else value
                                         for key, value in record.items()}
                sample["rows"].append(compact)
        samples.append(sample)
    return {"totals": report["totals"], "failure_kinds": report["failure_kinds"],
            "difference_types": report["difference_types"],
            "padding_format_rule": report["padding_format_rule"],
            "mismatch_groups": report["mismatch_groups"][:150],
            "display_rule_hits": report["display_rule_hits"],
            "name_rule_hits": report["name_rule_hits"],
            "raw_value_display_hits": report["raw_value_display_hits"],
            "value_diff_by_key": report["value_diff_by_key"][:40],
            "refusals": report["refusals"],
            "formats": report["formats"],
            "exceptions": report["exceptions"], "tag_exceptions": report["tag_exceptions"],
            "raw_only_tag_exception_total": len(report["raw_only_tag_exceptions"]),
            "raw_only_tag_exceptions": report["raw_only_tag_exceptions"][:30],
            "differing_total": len(report["differing"]),
            "differing_sample": samples}


def _emit_annotation(payload: dict[str, Any]) -> None:
    import base64, zlib

    def emit(title: str, part: dict[str, Any]) -> None:
        raw = json.dumps(part, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
        encoded = base64.b64encode(zlib.compress(raw)).decode("ascii")
        if len(encoded) > 3500:
            raise TagPrintError(f"annotation payload too large for {title}: {len(encoded)} characters")
        print(f"::notice title={title}::{encoded}")

    grouped = payload.pop("mismatch_groups", [])
    refusals = payload.pop("refusals", [])
    value_differences = payload.pop("value_diff_by_key", [])
    alias_hits = payload.pop("display_rule_hits", [])
    name_hits = payload.pop("name_rule_hits", [])
    raw_value_hits = payload.pop("raw_value_display_hits", [])
    emit(f"{TITLE} summary", payload)
    for label, items, size in (("groups", grouped, 75), ("refusals", refusals, 17),
                               ("values", value_differences, 40), ("display rules", alias_hits, 20),
                               ("key-name rules", name_hits, 20), ("raw-value display rules", raw_value_hits, 20)):
        chunks = [items[i:i + size] for i in range(0, len(items), size)] or [[]]
        for index, chunk in enumerate(chunks, 1):
            emit(f"{TITLE} {label} {index}/{len(chunks)}", {label: chunk})


def run_dump(command: list[str], paths: list[Path]) -> dict[str, dict[str, Any]]:
    listing = "".join(f"{path}\n" for path in paths)
    try:
        result = subprocess.run(command + ["--files-from", "-"], input=listing, capture_output=True,
                                text=True, encoding="utf-8", errors="replace", timeout=1800, check=False)
    except (FileNotFoundError, subprocess.TimeoutExpired) as error:
        raise TagPrintError(f"MetadataSharp.Dump failed to run: {error}") from error
    if result.returncode:
        raise TagPrintError(f"MetadataSharp.Dump exited {result.returncode}: {(result.stderr or '').strip()[:400]}")
    return parse_dump(result.stdout or "")


def _run_reference(command: list[str], path: Path, mode: str = "-pt") -> tuple[int, str, str]:
    try:
        result = subprocess.run(command + [mode, str(path)], capture_output=True, text=True,
                                encoding="utf-8", errors="replace", timeout=120, check=False)
    except (FileNotFoundError, subprocess.TimeoutExpired) as error:
        raise TagPrintError(f"Exiv2 -pt failed for {path.name}: {error}") from error
    return result.returncode, result.stdout or "", result.stderr or ""


def build_report(candidates: list[corpus.Candidate], reference_command: list[str],
                 dump: dict[str, dict[str, Any]], unmatched: list[str] | None = None) -> dict[str, Any]:
    unmatched_names = set(unmatched or [])
    unexplained = unmatched_names - _TAG_PRINT_EXCEPTIONS.keys()
    if unexplained:
        raise TagPrintError("unmatched upstream expectations need named, reviewed exceptions: " + ", ".join(sorted(unexplained)))
    exceptions: list[dict[str, str]] = [
        {"file": name, "reason": _TAG_PRINT_EXCEPTIONS[name]}
        for name in sorted(unmatched_names)
    ]
    tag_exceptions = [{"key": key, "reason": reason} for key, reason in sorted(_TAG_PRINT_KEY_EXCEPTIONS.items())]
    raw_only_tags: Counter[str] = Counter()
    differing: list[dict[str, Any]] = []
    totals: Counter[str] = Counter()
    failure_kinds: Counter[str] = Counter()
    difference_types: Counter[str] = Counter()
    value_pairs: Counter[tuple[str, str, str]] = Counter()
    accepted_aliases: Counter[tuple[str, str, str]] = Counter()
    accepted_padding: Counter[str] = Counter()
    accepted_names: Counter[tuple[str, str]] = Counter()
    verified_raw_displays: Counter[tuple[str, str, str, str]] = Counter()
    mismatch_groups: Counter[tuple[str, str, str, str]] = Counter()
    formats: dict[str, Counter[str]] = defaultdict(Counter)
    for candidate in candidates:
        name = candidate.label
        if name in _TAG_PRINT_EXCEPTIONS:
            exceptions.append({"file": name, "reason": _TAG_PRINT_EXCEPTIONS[name]})
            continue
        totals["files"] += 1
        format_counts = formats[candidate.format.upper()]
        format_counts["files"] += 1
        status, output, error = _run_reference(reference_command, candidate.path)
        ours = dump.get(candidate.path.as_posix())
        problem: dict[str, Any] | None = None
        if status != 0:
            problem = {"file": name, "kind": "exiv2-refused", "status": status,
                       "detail": (error.strip() or output.strip())[:300]}
        elif ours is None:
            problem = {"file": name, "kind": "missing-dump-file"}
        elif ours["status"] != "ok":
            problem = {"file": name, "kind": "metadatasharp-refused", "status": ours["status"], "detail": ours["detail"]}
        else:
            expected = [row for row in parse_exiv2(output) if row["key"] not in _TAG_PRINT_KEY_EXCEPTIONS]
            source_by_identity: dict[tuple[str, str, int], list[dict[str, Any]]] = defaultdict(list)
            for source_row in ours["rows"]:
                source_by_identity[(source_row["key"], source_row["type"], source_row["count"])].append(source_row)
            all_observed = project_dump(ours["rows"])
            expected_keys = {_canonical_key(row["key"]) for row in expected}
            raw_only_tags.update(row["key"] for row in all_observed
                                 if _is_raw_numeric_tag(row["key"])
                                 and _canonical_key(row["key"]) not in expected_keys)
            observed = [row for row in all_observed
                        if (_canonical_key(row["key"]) in expected_keys or not _is_raw_numeric_tag(row["key"]))
                        and row["key"] not in _TAG_PRINT_KEY_EXCEPTIONS]
            rows = compare_rows(expected, observed, _TAG_PRINT_DISPLAY_ALIASES, accepted_aliases, accepted_padding, accepted_names)
            if any(row["kind"] == "value-diff" for row in rows):
                raw_status, raw_output, _ = _run_reference(reference_command, candidate.path, "-pv")
                if raw_status == 0:
                    rows, raw_verified = verify_raw_value_differences(rows, parse_exiv2(raw_output), ours["rows"])
                    for item in raw_verified:
                        verified_raw_displays[(item["key"], item["exiv2_pt"], item["metadatasharp_pt"], item["raw_value"])] += 1
            if rows:
                for row in rows:
                    difference_types[row["kind"]] += 1
                    if row["kind"] == "value-diff":
                        expected_row, actual_row = row["exiv2"], row["metadatasharp"]
                        key = expected_row["key"]
                        value_pairs[(key, expected_row["value"], actual_row["value"])] += 1
                        local_source = next((candidate for candidate in source_by_identity[
                            (actual_row["key"], actual_row["type"], actual_row["count"])]
                            if project_dump([candidate])[0]["value"] == actual_row["value"]), {})
                        local_is_interpreted = bool(local_source.get("translated"))
                        category = "description" if local_is_interpreted else "value"
                        if (expected_row["type"] == "Undefined" and not local_is_interpreted
                                and re.fullmatch(r"\d+(?:\s+\d+)+", actual_row["value"].strip())):
                            category = "format"
                        mismatch_groups[(category, key, expected_row["type"],
                                         expected_row["value"] + " => " + actual_row["value"])] += 1
                    else:
                        side = row["exiv2"] or row["metadatasharp"]
                        mismatch_groups[(row["kind"], side["key"], side["type"], str(side["count"]))] += 1
                problem = {"file": name, "kind": "rows-differ", "exiv2_rows": len(expected),
                           "metadatasharp_rows": len(observed), "rows": rows[:8]}
            else:
                totals["matched_rows"] += len(expected)
                format_counts["matched_rows"] += len(expected)
        if problem:
            totals["failed_files"] += 1
            failure_kinds[problem["kind"]] += 1
            format_counts["failed_files"] += 1
            differing.append(problem)
    raw_only_exceptions = [
        {"key": key, "occurrences": count,
         "reason": "This exact numeric/vendor key is absent from the pinned Exiv2 0.28.9 tag registry; the raw-only MetadataSharp value is outside the -pt named-tag table comparison."}
        for key, count in sorted(raw_only_tags.items())
    ]
    value_diff_by_key = [
        {"key": key, "exiv2": expected[:100], "metadatasharp": actual[:100], "occurrences": count}
        for (key, expected, actual), count in value_pairs.most_common(100)
    ]
    mismatch_by_key: Counter[tuple[str, str, str]] = Counter()
    mismatch_examples: dict[tuple[str, str, str], Counter[str]] = defaultdict(Counter)
    for (category, key, type_name, detail), count in mismatch_groups.items():
        group = (category, key, type_name)
        mismatch_by_key[group] += count
        mismatch_examples[group][detail] += count
    grouped_differences = [
        {"category": category, "key": key, "type": type_name, "occurrences": count,
         "examples": [{"difference": detail[:180], "occurrences": occurrences}
                      for detail, occurrences in mismatch_examples[(category, key, type_name)].most_common(3)]}
        for (category, key, type_name), count in sorted(mismatch_by_key.items(), key=lambda item: (-item[1], item[0]))[:200]
    ]
    refusals = [item for item in differing if item["kind"] in
                ("metadatasharp-refused", "exiv2-refused", "missing-dump-file")]
    display_rule_hits = [
        {"key": key, "exiv2": reference, "metadatasharp": local, "occurrences": count,
         "rule": _TAG_PRINT_DISPLAY_REASONS[key]}
        for (key, reference, local), count in sorted(accepted_aliases.items())
    ]
    name_rule_hits = [
        {"exiv2_key": reference, "metadatasharp_key": local, "occurrences": count,
         "rule": "The keys identify the same tag number in the same EXIF directory; the local catalog supplies the name where Exiv2 prints a numeric fallback."}
        for (reference, local), count in sorted(accepted_names.items())
    ]
    raw_value_display_hits = [
        {"key": key, "exiv2_pt": reference, "metadatasharp_pt": local, "raw_value": raw,
         "occurrences": count,
         "rule": "The pinned Exiv2 -pv and MetadataSharp dump report the same raw value with the same type and count; only -pt presentation text differs."}
        for (key, reference, local, raw), count in sorted(verified_raw_displays.items())
    ]
    padding_format_rule = {
        "occurrences": sum(accepted_padding.values()),
        "types": dict(sorted(accepted_padding.items())),
        "rule": "Trailing space/NUL padding is ignored only for ASCII fields and EXIF UserComment; internal whitespace and all non-text values remain exact.",
    }
    return {"kind": "TagPrint", "totals": dict(totals), "failure_kinds": dict(sorted(failure_kinds.items())),
            "difference_types": dict(sorted(difference_types.items())),
            "mismatch_groups": grouped_differences,
            "display_rule_hits": display_rule_hits,
            "name_rule_hits": name_rule_hits,
            "raw_value_display_hits": raw_value_display_hits,
            "padding_format_rule": padding_format_rule,
            "value_diff_by_key": value_diff_by_key,
            "refusals": refusals,
            "formats": {key: dict(value) for key, value in sorted(formats.items())},
            "exceptions": exceptions, "tag_exceptions": tag_exceptions,
            "raw_only_tag_exceptions": raw_only_exceptions, "differing": differing}


def text_report(report: dict[str, Any]) -> str:
    totals = report["totals"]
    lines = [f"{TITLE} against pinned Exiv2 -pt",
             f"  files: {totals.get('files', 0)}; matched rows: {totals.get('matched_rows', 0)}; "
             f"failed files: {totals.get('failed_files', 0)}"]
    for format_name, counts in report["formats"].items():
        lines.append(f"  {format_name}: {counts.get('matched_rows', 0)} rows matched / "
                     f"{counts['files']} files ({counts.get('failed_files', 0)} failed)")
    for entry in report["exceptions"]:
        lines.append(f"  named missing-input exception: {entry['file']}: {entry['reason']}")
    for entry in report["tag_exceptions"]:
        lines.append(f"  named tag exception: {entry['key']}: {entry['reason']}")
    padding_rule = report["padding_format_rule"]
    lines.append(f"  padding-format rule: {padding_rule['occurrences']} rows across {padding_rule['types']}: {padding_rule['rule']}")
    for entry in report["display_rule_hits"]:
        lines.append(f"  display rule: {entry['key']} {entry['exiv2']!r} => {entry['metadatasharp']!r} "
                     f"({entry['occurrences']} rows): {entry['rule']}")
    for entry in report["raw_only_tag_exceptions"]:
        lines.append(f"  named raw-only tag exception: {entry['key']} ({entry['occurrences']} rows): {entry['reason']}")
    for entry in report["mismatch_groups"]:
        examples = "; ".join(f"{item['difference']} ({item['occurrences']})" for item in entry["examples"])
        lines.append(f"  {entry['category']} {entry['key']} [{entry['type']}]: {entry['occurrences']} rows; {examples}")
    for entry in report["refusals"]:
        lines.append(f"  refusal {entry['file']}: {entry['kind']} {entry.get('status', '')} {entry.get('detail', '')}")
    return "\n".join(lines) + "\n"


def verify_oracle(tree: Path, executable: str) -> None:
    try:
        revision = subprocess.run(["git", "-C", str(tree), "rev-parse", "HEAD"], capture_output=True,
                                  text=True, timeout=30, check=True).stdout.strip()
        version = subprocess.run([executable, "--version"], capture_output=True, text=True,
                                 timeout=30, check=True).stdout.splitlines()[0]
    except (OSError, subprocess.SubprocessError, IndexError) as error:
        raise TagPrintError(f"cannot verify pinned Exiv2 oracle: {error}") from error
    if revision != PINNED_EXIV2_COMMIT:
        raise TagPrintError(f"Exiv2 source checkout is {revision}, expected {PINNED_EXIV2_COMMIT}")
    if "0.28.9" not in version:
        raise TagPrintError(f"Exiv2 binary is not version 0.28.9: {version}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Compare MetadataSharp's tag output with pinned exiv2 -pt.")
    parser.add_argument("--tree", type=Path, required=True, help="pinned Exiv2 checkout")
    parser.add_argument("--exiv2", required=True, help="pinned Exiv2 executable")
    parser.add_argument("--dump-command", required=True, help="MetadataSharp.Dump command")
    parser.add_argument("--out", type=Path, default=ROOT / "artifacts/parity/tag-print")
    parser.add_argument("--annotate", action="store_true")
    args = parser.parse_args(argv)
    verify_oracle(args.tree, args.exiv2)
    candidates, unmatched = corpus.discover_exiv2(args.tree)
    paths = [candidate.path for candidate in candidates]
    dump = run_dump(shlex.split(args.dump_command), paths)
    expected_paths = {path.as_posix() for path in paths}
    unexpected_paths = set(dump) - expected_paths
    if unexpected_paths:
        raise TagPrintError("dump returned unrequested files: " + ", ".join(sorted(unexpected_paths)[:5]))
    report = build_report(candidates, [args.exiv2], dump, unmatched)
    args.out.mkdir(parents=True, exist_ok=True)
    (args.out / "tag-print-report.json").write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    text = text_report(report)
    (args.out / "tag-print-report.txt").write_text(text, encoding="utf-8")
    print(text, end="")
    if args.annotate:
        _emit_annotation(_summary(report))
    return 1 if report["totals"].get("failed_files") else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (TagPrintError, corpus.CorpusError) as failure:
        print(f"::error title={TITLE} harness::{failure}", file=sys.stderr)
        sys.exit(2)
