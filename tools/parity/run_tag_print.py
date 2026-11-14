#!/usr/bin/env python3
"""Fail-closed differential gate for the Exiv2 -pt tag listing."""
from __future__ import annotations

import argparse
import json
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
PINNED_EXIV2_COMMIT = "b0859fc61fea5dedfc898aece6b15893ddf4314e"

# Exception keys are upstream test/data basenames, never extensions or directories. Every retained
# entry must describe why this precise fixture cannot serve as a -pt comparator.
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
             "value": row["translated"] or row["raw"]} for row in rows if row["key"].startswith("Exif.")]


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


def compare_rows(reference: list[dict[str, Any]], actual: list[dict[str, Any]]) -> list[dict[str, Any]]:
    differences: list[dict[str, Any]] = []
    width = max(len(reference), len(actual))
    for index in range(width):
        expected = reference[index] if index < len(reference) else None
        observed = actual[index] if index < len(actual) else None
        if expected != observed:
            differences.append({"row": index + 1, "exiv2": expected, "metadatasharp": observed})
    return differences


def _summary(report: dict[str, Any]) -> dict[str, Any]:
    # Keep the one-line workflow annotation below GitHub's annotation payload limit while making
    # the first discrepancies actionable; the full report is written to the JSON artifact.
    samples = []
    for item in report["differing"][:4]:
        sample = {key: value for key, value in item.items() if key != "rows"}
        if "rows" in item:
            sample["rows"] = []
            for row in item["rows"][:3]:
                compact = dict(row)
                for side in ("exiv2", "metadatasharp"):
                    record = compact.get(side)
                    if isinstance(record, dict):
                        compact[side] = {key: value[:160] + "…" if isinstance(value, str) and len(value) > 160 else value
                                         for key, value in record.items()}
                sample["rows"].append(compact)
        samples.append(sample)
    return {"totals": report["totals"], "formats": report["formats"],
            "exceptions": report["exceptions"], "differing_total": len(report["differing"]),
            "differing_sample": samples}


def _emit_annotation(payload: dict[str, Any]) -> None:
    import base64, zlib
    raw = json.dumps(payload, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    encoded = base64.b64encode(zlib.compress(raw)).decode("ascii")
    print(f"::notice title={TITLE} summary 1/1::{encoded}")


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


def _run_reference(command: list[str], path: Path) -> tuple[int, str, str]:
    try:
        result = subprocess.run(command + ["-pt", str(path)], capture_output=True, text=True,
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
    differing: list[dict[str, Any]] = []
    totals: Counter[str] = Counter()
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
            expected = parse_exiv2(output)
            observed = project_dump(ours["rows"])
            rows = compare_rows(expected, observed)
            if rows:
                problem = {"file": name, "kind": "rows-differ", "exiv2_rows": len(expected),
                           "metadatasharp_rows": len(observed), "rows": rows[:8]}
            else:
                totals["matched_rows"] += len(expected)
                format_counts["matched_rows"] += len(expected)
        if problem:
            totals["failed_files"] += 1
            format_counts["failed_files"] += 1
            differing.append(problem)
    return {"kind": "TagPrint", "totals": dict(totals),
            "formats": {key: dict(value) for key, value in sorted(formats.items())},
            "exceptions": exceptions, "differing": differing}


def text_report(report: dict[str, Any]) -> str:
    totals = report["totals"]
    lines = [f"{TITLE} against pinned Exiv2 -pt",
             f"  files: {totals.get('files', 0)}; matched rows: {totals.get('matched_rows', 0)}; "
             f"failed files: {totals.get('failed_files', 0)}"]
    for format_name, counts in report["formats"].items():
        lines.append(f"  {format_name}: {counts.get('matched_rows', 0)} rows matched / "
                     f"{counts['files']} files ({counts.get('failed_files', 0)} failed)")
    for entry in report["exceptions"]:
        lines.append(f"  named exception: {entry['file']}: {entry['reason']}")
    for entry in report["differing"]:
        lines.append(f"  {entry['file']}: {entry['kind']} {json.dumps(entry, ensure_ascii=False)[:900]}")
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
