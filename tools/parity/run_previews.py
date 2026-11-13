#!/usr/bin/env python3
"""Preview parity: what ``MetadataSharp.Dump --previews`` lists against what ``exiv2 -pp`` printed.

The pinned exiv2 tree keeps the output of its own preview test in
``test/data/test_reference_files/preview-test.out``: for every input a block

    -----> NAME <-----

    Command: exiv2 -pp NAME
    Preview 1: image/jpeg, 160x120 pixels, 4158 bytes
    Exit code: 0

followed by the block of the extraction command, which says nothing about the list. That file is
read as text; no external code is configured, built or run. The comparison is of a different kind
from the key-by-key one in ``the C# key-parity report`` and shares none of its rules: a preview is the triple
(MIME type, width x height, byte count), and a file's previews are compared as a multiset.

    match    a reference preview we list with the same type, dimensions and length
    missing  a reference preview we do not list
    extra    a preview we list that the reference does not

The order is judged separately: a file is ``ordered`` when the previews both sides list come in
the same order on both.

With ``--fixtures`` and ``--exiv2-command`` the expectation is not that text: the pinned tool lists
each file of the given directories in the same run, as ``the C# key-parity report`` does for key parity. This
is how a file is measured that the pinned tree holds but its preview test does not name.
"""
from __future__ import annotations

import argparse
import json
import re
import shlex
import subprocess
import sys
from collections import Counter
from pathlib import Path
from typing import Any

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
if str(HERE) not in sys.path:
    sys.path.insert(0, str(HERE))

import parity_support as run_parity  # noqa: E402

KIND = "Preview"
TITLE = "PARITY preview"
REFERENCE = Path("test") / "data" / "test_reference_files" / "preview-test.out"

_BLOCK = re.compile(r"^-----> (?P<name>.+) <-----$")
_COMMAND = re.compile(r"^Command: exiv2 (?P<arguments>.+)$")
_PREVIEW = re.compile(r"^Preview \d+: (?P<mime>\S+), (?P<width>\d+)x(?P<height>\d+) pixels, (?P<bytes>\d+) bytes$")

Preview = tuple[str, int, int, int]


class PreviewParityError(Exception):
    """The run cannot be trusted; nothing should be concluded from its numbers."""


def parse_reference(text: str) -> dict[str, list[Preview]]:
    """The previews ``exiv2 -pp`` listed for every file named in the expectation text, in its order."""
    expected: dict[str, list[Preview]] = {}
    name: str | None = None
    listing = False
    for line in text.splitlines():
        block = _BLOCK.match(line)
        if block:
            name = block.group("name")
            if name in expected:
                raise PreviewParityError(f"the expectation names {name!r} twice")
            expected[name] = []
            listing = False
            continue
        command = _COMMAND.match(line)
        if command:
            # Only the listing command describes the previews; the extraction command repeats nothing.
            listing = command.group("arguments").split()[0] == "-pp"
            continue
        preview = _PREVIEW.match(line)
        if preview:
            if name is None:
                raise PreviewParityError("a preview line comes before any file block")
            if listing:
                expected[name].append((preview.group("mime"), int(preview.group("width")),
                                       int(preview.group("height")), int(preview.group("bytes"))))
        elif line.startswith("Preview "):
            raise PreviewParityError(f"a preview line does not parse: {line!r}")
    return expected


def oracle_reference(outputs: dict[str, str]) -> dict[str, list[Preview]]:
    """What the pinned tool printed for each file, read by the rules of the expectation text.

    A file the tool printed nothing for expects no preview: whatever we list for it is ``extra``.
    """
    blocks = [f"-----> {name} <-----\nCommand: exiv2 -pp {name}\n{text}\n" for name, text in outputs.items()]
    return parse_reference("".join(blocks))


def parse_dump(text: str) -> dict[str, dict[str, Any]]:
    """``FILE`` / ``PREVIEW`` / ``STATUS`` records of ``MetadataSharp.Dump --previews``, by path."""
    blocks: dict[str, dict[str, Any]] = {}
    current: dict[str, Any] | None = None
    for number, line in enumerate(text.splitlines(), 1):
        if not line:
            continue
        fields = line.split("\t")
        if fields[0] == "FILE" and len(fields) == 2:
            if current is not None and current["status"] is None:
                raise PreviewParityError(f"dump:{number}: FILE record before the previous one was closed")
            current = {"previews": [], "sources": [], "status": None, "detail": ""}
            blocks[fields[1]] = current
        elif fields[0] == "PREVIEW" and len(fields) == 6 and current is not None and current["status"] is None:
            size = re.fullmatch(r"(\d+)x(\d+)", fields[3])
            if size is None or not fields[4].isdigit():
                raise PreviewParityError(f"dump:{number}: PREVIEW record does not parse: {line!r}")
            current["previews"].append((fields[2], int(size.group(1)), int(size.group(2)), int(fields[4])))
            current["sources"].append(fields[5])
        elif fields[0] == "STATUS" and len(fields) >= 2 and current is not None and current["status"] is None:
            current["status"] = fields[1]
            current["detail"] = fields[2] if len(fields) > 2 else ""
        else:
            raise PreviewParityError(f"dump:{number}: unexpected record: {line!r}")
    if current is not None and current["status"] is None:
        raise PreviewParityError("the dump ends inside a FILE block")
    return blocks


def compare_file(expected: list[Preview], listed: list[Preview]) -> dict[str, Any]:
    """Multiset comparison of one file's previews, and whether the common ones come in the same order."""
    remaining = Counter(listed)
    matched: list[Preview] = []
    missing: list[Preview] = []
    for preview in expected:
        if remaining[preview] > 0:
            remaining[preview] -= 1
            matched.append(preview)
        else:
            missing.append(preview)
    extra = list(remaining.elements())
    common = Counter(matched)
    ours: list[Preview] = []
    for preview in listed:
        if common[preview] > 0:
            common[preview] -= 1
            ours.append(preview)
    return {"match": len(matched), "missing": missing, "extra": extra, "ordered": ours == matched}


def compare(expected: dict[str, list[Preview]], dumped: dict[str, dict[str, Any]], paths: dict[str, Path | None]) -> dict[str, Any]:
    totals = Counter()
    statuses = Counter()
    files: list[dict[str, Any]] = []
    for name in sorted(expected):
        path = paths.get(name)
        block = dumped.get(str(path)) if path is not None else None
        if path is None:
            status, listed = "no-input", []
        elif block is None:
            raise PreviewParityError(f"the dump has no block for {name}")
        else:
            status, listed = block["status"], block["previews"]
        outcome = compare_file(expected[name], listed if status == "ok" else [])
        statuses[status] += 1
        totals["expected"] += len(expected[name])
        totals["match"] += outcome["match"]
        totals["missing"] += len(outcome["missing"])
        totals["extra"] += len(outcome["extra"])
        totals["files"] += 1
        totals["files_equal"] += 1 if not outcome["missing"] and not outcome["extra"] else 0
        totals["files_ordered"] += 1 if outcome["ordered"] else 0
        entry = {"file": name, "status": status, "match": outcome["match"], "ordered": outcome["ordered"]}
        if outcome["missing"]:
            entry["missing"] = [list(preview) for preview in outcome["missing"]]
        if outcome["extra"]:
            entry["extra"] = [list(preview) for preview in outcome["extra"]]
        if status == "error":
            entry["detail"] = block["detail"] if block is not None else ""
        files.append(entry)
    rate = round(totals["match"] / totals["expected"], 6) if totals["expected"] else None
    return {"kind": KIND, "totals": dict(totals), "rate": rate, "statuses": dict(statuses), "files": files}


def text_report(report: dict[str, Any]) -> str:
    totals = report["totals"]
    lines = [
        f"{KIND} parity against exiv2 -pp",
        f"  files: {totals.get('files', 0)} ({totals.get('files_equal', 0)} with the same previews, "
        f"{totals.get('files_ordered', 0)} in the same order); statuses: {report['statuses']}",
        f"  previews: {totals.get('expected', 0)} expected, {totals.get('match', 0)} match, "
        f"{totals.get('missing', 0)} missing, {totals.get('extra', 0)} extra; rate {report['rate']}",
    ]
    for entry in report["files"]:
        if "missing" in entry or "extra" in entry or not entry["ordered"] or entry["status"] != "ok":
            lines.append(f"  {entry['file']} [{entry['status']}] missing={entry.get('missing', [])} "
                         f"extra={entry.get('extra', [])} ordered={entry['ordered']}")
    return "\n".join(lines) + "\n"


def summary(report: dict[str, Any]) -> dict[str, Any]:
    """The part of the report that travels as annotations: the totals and every file that differs."""
    differing = [entry for entry in report["files"]
                 if "missing" in entry or "extra" in entry or not entry["ordered"] or entry["status"] != "ok"]
    return {"kind": KIND, "totals": report["totals"], "rate": report["rate"], "statuses": report["statuses"],
            "differing": differing}


def run_dump(dump_command: list[str], paths: list[Path]) -> dict[str, dict[str, Any]]:
    listing = "".join(f"{path}\n" for path in paths)
    try:
        finished = subprocess.run(dump_command + ["--previews", "--files-from", "-"], input=listing, capture_output=True,
                                 text=True, encoding="utf-8", errors="replace", timeout=1800, check=False)
    except FileNotFoundError as error:
        raise PreviewParityError(f"the dump command is not runnable: {error}") from error
    except subprocess.TimeoutExpired as error:
        raise PreviewParityError("the dump tool timed out") from error
    if finished.returncode != 0:
        raise PreviewParityError(f"the dump tool failed with {finished.returncode}: {(finished.stderr or '').strip()[:400]}")
    return parse_dump(finished.stdout or "")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Compare our preview list with exiv2's own expectations.")
    parser.add_argument("--tree", type=Path, help="root of a pinned exiv2 checkout, for its preview expectation")
    parser.add_argument("--fixtures", type=Path, action="append", default=[],
                        help="a directory of files the pinned tool lists in this run instead; repeatable")
    parser.add_argument("--exiv2-command", help="the pinned tool with its listing option (exiv2 -pp), split "
                                                "POSIX-style; the oracle for --fixtures, its output is never stored")
    parser.add_argument("--dump-command", required=True, help="how to run MetadataSharp.Dump, split POSIX-style on spaces")
    parser.add_argument("--out", type=Path, default=ROOT / "artifacts/parity/preview", help="report directory")
    parser.add_argument("--annotate", action="store_true", help="emit the summary as chunked workflow annotations")
    parser.add_argument("--title", default=TITLE, help="annotation title")
    args = parser.parse_args(argv)

    if (args.tree is None) == (not args.fixtures):
        parser.error("pass either --tree or --fixtures")
    paths: dict[str, Path | None]
    if args.fixtures:
        if not args.exiv2_command:
            raise PreviewParityError("--fixtures needs --exiv2-command: the files have no stored expectation")
        oracle = shlex.split(args.exiv2_command)
        paths = {}
        for candidate in run_parity.corpus_mod.discover_generated(*args.fixtures):
            if candidate.label in paths:
                raise PreviewParityError(f"two files are named {candidate.label}")
            paths[candidate.label] = candidate.path
        expected = oracle_reference({name: run_parity.run_reference(oracle, path) for name, path in paths.items() if path})
    else:
        reference = args.tree / REFERENCE
        if not reference.is_file():
            raise PreviewParityError(f"no preview expectation at {reference}")
        expected = parse_reference(reference.read_text(encoding="utf-8", errors="replace"))
        if not expected:
            raise PreviewParityError(f"{reference} names no file")
        data = args.tree / "test" / "data"
        paths = {name: (data / name if (data / name).is_file() else None) for name in expected}
    present = [path for path in paths.values() if path is not None]
    if not present:
        raise PreviewParityError("none of the files the expectation names is in the tree")
    report = compare(expected, run_dump(shlex.split(args.dump_command), present), paths)

    args.out.mkdir(parents=True, exist_ok=True)
    (args.out / "preview-report.json").write_text(json.dumps(report, indent=1, sort_keys=True) + "\n", encoding="utf-8")
    text = text_report(report)
    (args.out / "preview-report.txt").write_text(text, encoding="utf-8")
    print(text, end="")
    if args.annotate:
        run_parity.emit_annotations(summary(report), args.title)
    # A reader that failed on a file is a defect; a preview we do not list is a measurement.
    return 1 if report["statuses"].get("error") else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (PreviewParityError, run_parity.StructuralError, run_parity.corpus_mod.CorpusError) as failure:
        print(f"::error title={TITLE} harness::{failure}", file=sys.stderr)
        print(f"structural failure: {failure}", file=sys.stderr)
        sys.exit(1)
