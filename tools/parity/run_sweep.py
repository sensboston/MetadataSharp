#!/usr/bin/env python3
"""Soundness sweep: ``MetadataSharp.Dump --icc`` and ``--comments`` over every file of the pinned corpus.

Nothing is compared with a reference here. The corpus holds files damaged on purpose, and the two
static readers take any bytes: each has to end every file with a profile, a list of comments,
nothing, or a refusal that carries a code. A reader that fails any other way (status ``error``) is
a defect and fails the run. The counts travel as annotations so that a change in what is read or
refused shows from one run to the next.
"""
from __future__ import annotations

import argparse
import json
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

import run_parity  # noqa: E402

TITLE = "SWEEP icc and comments"
# The option of the dump tool, and the one kind of record it writes between FILE and STATUS.
MODES = {"icc": ("--icc", "ICC"), "comments": ("--comments", "COMMENT")}


class SweepError(Exception):
    """The run cannot be trusted; nothing should be concluded from its numbers."""


def key(path: Path) -> str:
    """The name a file travels under, to the dump tool and back: its path with forward slashes.

    The dump tool echoes the path it was given and escapes a backslash, so the path of a Windows
    file written the native way would come back as another string. Forward slashes are read by
    Python and by .NET on every system and come back as they went.
    """
    return path.as_posix()


def parse_dump(text: str, record: str) -> dict[str, dict[str, Any]]:
    """``FILE`` / ``record`` / ``STATUS`` blocks by the path the tool echoes: how many records, the status and its detail."""
    blocks: dict[str, dict[str, Any]] = {}
    current: dict[str, Any] | None = None
    for number, line in enumerate(text.splitlines(), 1):
        if not line:
            continue
        fields = line.split("\t")
        if fields[0] == "FILE" and len(fields) == 2:
            if current is not None and current["status"] is None:
                raise SweepError(f"dump:{number}: FILE record before the previous one was closed")
            current = {"records": 0, "status": None, "detail": ""}
            blocks[fields[1]] = current
        elif fields[0] == record and len(fields) == 3 and current is not None and current["status"] is None:
            current["records"] += 1
        elif fields[0] == "STATUS" and len(fields) >= 2 and current is not None and current["status"] is None:
            current["status"] = fields[1]
            current["detail"] = fields[2] if len(fields) > 2 else ""
        else:
            raise SweepError(f"dump:{number}: unexpected record: {line!r}")
    if current is not None and current["status"] is None:
        raise SweepError("the dump ends inside a FILE block")
    return blocks


def tally(blocks: dict[str, dict[str, Any]], paths: list[Path]) -> dict[str, Any]:
    """Statuses, refusal codes, files with a record and records, over ``paths``; the files that failed."""
    statuses: Counter = Counter()
    refusals: Counter = Counter()
    with_records = records = 0
    errors: list[dict[str, str]] = []
    for path in paths:
        block = blocks.get(key(path))
        if block is None:
            raise SweepError(f"the dump has no block for {path.name}")
        statuses[block["status"]] += 1
        if block["status"] == "refused":
            refusals[block["detail"]] += 1
        elif block["status"] == "error":
            errors.append({"file": path.name, "detail": block["detail"]})
        with_records += 1 if block["records"] else 0
        records += block["records"]
    return {"files": len(paths), "statuses": dict(sorted(statuses.items())), "refused": dict(sorted(refusals.items())),
            "files_with_records": with_records, "records": records, "errors": errors}


def text_report(report: dict[str, Any]) -> str:
    lines = ["Soundness sweep over the corpus"]
    for mode in sorted(report):
        part = report[mode]
        lines.append(f"  {mode}: {part['files']} files; statuses {part['statuses']}; refused {part['refused']}; "
                     f"{part['records']} records in {part['files_with_records']} files")
        lines.extend(f"    ERROR {entry['file']}: {entry['detail']}" for entry in part["errors"])
    return "\n".join(lines) + "\n"


def run_dump(dump_command: list[str], option: str, record: str, paths: list[Path]) -> dict[str, dict[str, Any]]:
    listing = "".join(f"{key(path)}\n" for path in paths)
    try:
        finished = subprocess.run(dump_command + [option, "--files-from", "-"], input=listing, capture_output=True,
                                 text=True, encoding="utf-8", errors="replace", timeout=1800, check=False)
    except FileNotFoundError as error:
        raise SweepError(f"the dump command is not runnable: {error}") from error
    except subprocess.TimeoutExpired as error:
        raise SweepError("the dump tool timed out") from error
    if finished.returncode != 0:
        raise SweepError(f"the dump tool failed with {finished.returncode}: {(finished.stderr or '').strip()[:400]}")
    return parse_dump(finished.stdout or "", record)


def corpus(tree: Path) -> list[Path]:
    """Every regular file directly in ``test/data``: no name is trusted to say what a file is."""
    data = tree / "test" / "data"
    if not data.is_dir():
        raise SweepError(f"no corpus at {data}")
    # A tab or a line end in a name could not travel through the list the dump tool reads.
    paths = sorted(path for path in data.iterdir() if path.is_file() and not any(c in path.name for c in "\t\r\n\\"))
    if not paths:
        raise SweepError(f"{data} holds no file")
    return paths


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Run the ICC and comment readers over every corpus file.")
    parser.add_argument("--tree", type=Path, required=True, help="root of a pinned exiv2 checkout")
    parser.add_argument("--dump-command", required=True, help="how to run MetadataSharp.Dump, split POSIX-style on spaces")
    parser.add_argument("--out", type=Path, default=ROOT / "artifacts/parity/sweep", help="report directory")
    parser.add_argument("--annotate", action="store_true", help="emit the counts as chunked workflow annotations")
    parser.add_argument("--title", default=TITLE, help="annotation title")
    args = parser.parse_args(argv)

    paths = corpus(args.tree)
    command = shlex.split(args.dump_command)
    report = {mode: tally(run_dump(command, option, record, paths), paths) for mode, (option, record) in MODES.items()}

    args.out.mkdir(parents=True, exist_ok=True)
    (args.out / "sweep-report.json").write_text(json.dumps(report, indent=1, sort_keys=True) + "\n", encoding="utf-8")
    text = text_report(report)
    (args.out / "sweep-report.txt").write_text(text, encoding="utf-8")
    print(text, end="")
    if args.annotate:
        run_parity.emit_annotations(report, args.title)
    return 1 if any(part["errors"] for part in report.values()) else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (SweepError, run_parity.StructuralError) as failure:
        print(f"::error title={TITLE} harness::{failure}", file=sys.stderr)
        print(f"structural failure: {failure}", file=sys.stderr)
        sys.exit(1)
