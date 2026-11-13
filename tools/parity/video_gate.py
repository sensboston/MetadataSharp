"""Fail closed on video-container key/value parity outside recorded reader divergences.

The pinned Exiv2 0.28.9 RIFF reader reads BITMAPINFOHEADER pixel densities as QWORDs, so the
color-count fields for the upstream ``flame.avi`` sample are shifted. We intentionally decode the
bounded, declared DWORD fields instead. It also rejects RIFF/WAVE as an unknown image type, so the
authored WAV is tested by MetadataSharp but excluded from Exiv2 parity. No key-set difference is
allowlisted for formats the oracle reads.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path
from typing import Any

VIDEO_FORMATS = {"asf", "avi", "mov", "mp4", "mkv", "wav", "webm"}
ALLOWED_VALUE_DIFFS = {
    ("exiv2:flame.avi", "Xmp.video.NumOfColours"),
    ("exiv2:flame.avi", "Xmp.video.NumIfImpColours"),
}
REQUIRED_GENERATED = {
    "generated:authored-asf.asf",
    "generated:authored-avi.avi",
    "generated:authored-matroska.mkv",
    "generated:authored-quicktime.mp4",
    "generated:authored-webm.webm",
}


def evaluate(report: dict[str, Any]) -> tuple[bool, str]:
    files = report.get("per_file")
    if not isinstance(files, list):
        return False, "parity report has no per_file rows"
    selected = [entry for entry in files if entry.get("format") in VIDEO_FORMATS]
    # run_parity stores Candidate.uid (already ``corpus:label``) in the label field.
    ids = {str(entry.get("label")) for entry in selected}
    absent = sorted(REQUIRED_GENERATED - ids)
    if absent:
        generated = sorted(
            f"{entry.get('label')} [format={entry.get('format')}]"
            for entry in files if entry.get("corpus") == "generated"
        )
        return False, (
            "required authored video fixtures missing from video selection: " + ", ".join(absent)
            + "; generated report entries: " + (", ".join(generated) or "none")
        )

    ours_only: set[str] = set()
    reference_only: set[str] = set()
    unexpected_values: list[tuple[str, str, str, str]] = []
    failures: list[str] = []
    allowed_seen: set[tuple[str, str]] = set()
    for entry in selected:
        label = str(entry.get("label"))
        if entry.get("status") != "ok":
            failures.append(f"{label}: reader status {entry.get('status')} ({entry.get('detail', '')})")
        if entry.get("rows_dropped", 0):
            failures.append(f"{label}: parity report dropped {entry['rows_dropped']} rows; key/value gate requires complete rows")
        for row in entry.get("rows", []):
            outcome, key = row.get("outcome"), row.get("key", "")
            if outcome == "extra-key":
                ours_only.add(key)
            elif outcome == "missing-key":
                reference_only.add(key)
            elif outcome == "value-diff":
                pair = (label, key)
                if pair in ALLOWED_VALUE_DIFFS:
                    allowed_seen.add(pair)
                else:
                    unexpected_values.append((label, key, row.get("our_value", ""), row.get("their_value", "")))
            elif outcome == "match-key":
                unexpected_values.append((label, key, row.get("our_value", ""), row.get("their_value", "")))

    problems: list[str] = []
    if ours_only:
        problems.append("ours-only keys (must be removed): " + ", ".join(sorted(ours_only)))
    if reference_only:
        problems.append("reference-only keys (must be implemented): " + ", ".join(sorted(reference_only)))
    if unexpected_values:
        problems.append("unapproved value differences: " + "; ".join(
            f"{label} {key}: ours={ours!r}, Exiv2={theirs!r}" for label, key, ours, theirs in unexpected_values))
    if failures:
        problems.extend(failures)

    lines = ["Video parity gate (Exiv2 0.28.9):", f"  measured video files: {len(selected)}",
             "  documented oracle limitation: generated:authored-wave.wav (RIFF/WAVE is rejected as unknown image type; covered by reader tests, not key parity)",
             "  ours-only keys: " + (", ".join(sorted(ours_only)) or "none"),
             "  reference-only keys: " + (", ".join(sorted(reference_only)) or "none"),
             "  allowed documented AVI value differences: " + (", ".join(
                 f"{label} {key}" for label, key in sorted(allowed_seen)) or "none")]
    if problems:
        lines.append("FAIL")
        lines.extend("  " + problem for problem in problems)
        return False, "\n".join(lines) + "\n"
    lines.append("PASS")
    return True, "\n".join(lines) + "\n"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report", type=Path, help="parity-report.json written by run_parity.py")
    args = parser.parse_args(argv)
    try:
        report = json.loads(args.report.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        print(f"video parity gate could not read report: {error}")
        print("::error::Video gate could not load parity-report.json: " + str(error)[:1200])
        return 1
    print("::notice title=VideoGate::started")
    try:
        passed, message = evaluate(report)
    except Exception as error:
        print("::error::Video gate evaluation crashed: " + repr(error))
        raise
    print(message, end="", flush=True)
    summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary_path:
        with open(summary_path, "a", encoding="utf-8") as summary:
            summary.write("## Video parity gate (Exiv2 0.28.9)\n\n```text\n")
            summary.write(message)
            summary.write("```\n")
    if not passed:
        # Write to stderr as well as stdout: this remains visible in the failed step log even when
        # the runner or check-annotation collector suppresses stdout annotations from child tools.
        sys.stderr.write(message)
        sys.stderr.flush()
        for line in message.splitlines():
            if line.strip() not in {"FAIL", ""}:
                safe_line = line.strip()[:1500]
                print("::notice title=VideoGate result::" + safe_line, flush=True)
                print("::error title=VideoGate::" + safe_line, flush=True)
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
