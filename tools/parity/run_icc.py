#!/usr/bin/env python3
"""ICC parity: the profile ``MetadataSharp.Dump --icc`` reports against what ``exiv2 -pC`` printed.

``exiv2 -pC`` writes the bytes of the profile, so the pinned tree does not keep that output as
text; its own tests keep what they compare it with. Three of them say something about a file that
no command has yet changed, and each is read here as text. No external code is configured, built or
run: the two Python files are parsed, never imported.

``tests/bash_tests/testcases.py`` and ``test/data/test_reference_files/icc-test.out``
    ``icc_test`` loops over a list of images and, for each, ends by printing six MD5 sums on six
    lines. The first of the six is of ``exiv2 -pC`` on the image as it is in the tree; the other
    five are of files the test has rewritten, and say nothing of a reader.

``tests/bugfixes/redmine/test_issue_1074.py``
    One ``exiv2 -pC`` per file name, and the MD5 of what each must print.

``tests/bugfixes/github/test_pr1475_*.py``
    One class per BMFF file; among its commands ``exiv2 -pC`` with or without ``--binary``, and at
    the same place in ``stdout`` what it must print: nothing, or ``data:`` and the profile in base64.

A file is judged against the one expectation the tests agree on:

    match    the same profile (the same MD5, and the same length where the test gives one), or
             no profile on either side
    differs  a profile on both sides, and not the same one
    missing  the reference prints a profile and we return none
    extra    we return a profile where the reference prints nothing

Unlike the key and preview comparisons, this one is a gate: every file must match.
"""
from __future__ import annotations

import argparse
import ast
import base64
import binascii
import hashlib
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

import run_parity  # noqa: E402

KIND = "Icc"
TITLE = "PARITY icc"
BASH_TEST = Path("tests") / "bash_tests" / "testcases.py"
BASH_OUTPUT = Path("test") / "data" / "test_reference_files" / "icc-test.out"
MD5_CASE = Path("tests") / "bugfixes" / "redmine" / "test_issue_1074.py"
BINARY_CASES = Path("tests") / "bugfixes" / "github"
BINARY_PATTERN = "test_pr1475_*.py"
SUMS_PER_IMAGE = 6

_MD5 = re.compile(r"[0-9a-f]{32}")

# What the reference prints for one file: None for nothing, otherwise the MD5 of the profile and,
# where the test holds the bytes themselves, their number.
Expectation = tuple[str, int | None] | None


class IccParityError(Exception):
    """The run cannot be trusted; nothing should be concluded from its numbers."""


def _tree(text: str, name: str) -> ast.Module:
    try:
        return ast.parse(text)
    except SyntaxError as error:
        raise IccParityError(f"{name} does not parse as Python: {error}") from error


def _strings(node: ast.AST | None) -> list[str] | None:
    """The elements of a list or tuple written out as string literals, or None when it is not one."""
    if not isinstance(node, (ast.List, ast.Tuple)):
        return None
    values = [element.value for element in node.elts if isinstance(element, ast.Constant) and isinstance(element.value, str)]
    return values if len(values) == len(node.elts) else None


def _assigned(scope: ast.AST, name: str) -> list[ast.AST]:
    """Every value assigned to ``name`` anywhere inside ``scope``, in source order."""
    return [node.value for node in ast.walk(scope)
            if isinstance(node, ast.Assign) and len(node.targets) == 1
            and isinstance(node.targets[0], ast.Name) and node.targets[0].id == name]


def bash_images(test_text: str) -> list[str]:
    """The images ``icc_test`` loops over, in its order."""
    for function in ast.walk(_tree(test_text, str(BASH_TEST))):
        if isinstance(function, ast.FunctionDef) and function.name == "icc_test":
            for loop in ast.walk(function):
                if isinstance(loop, ast.For) and isinstance(loop.target, ast.Name) and loop.target.id == "img":
                    images = _strings(loop.iter)
                    if images:
                        return images
            raise IccParityError("icc_test has no loop over a written-out list of images")
    raise IccParityError(f"{BASH_TEST} has no icc_test")


def bash_sums(output_text: str) -> list[list[str]]:
    """The runs of consecutive lines of ``icc-test.out`` that are an MD5 sum and nothing else."""
    runs: list[list[str]] = []
    inside = False
    for line in output_text.splitlines():
        if _MD5.fullmatch(line):
            if not inside:
                runs.append([])
            runs[-1].append(line)
            inside = True
        else:
            inside = False
    return runs


def parse_bash(test_text: str, output_text: str) -> dict[str, Expectation]:
    """What ``icc_test`` expects ``exiv2 -pC`` to print for each image before it changes it."""
    images = bash_images(test_text)
    runs = bash_sums(output_text)
    if len(runs) != len(images):
        raise IccParityError(f"icc_test names {len(images)} images and its output has {len(runs)} runs of sums")
    expected: dict[str, Expectation] = {}
    for image, run in zip(images, runs):
        if len(run) != SUMS_PER_IMAGE:
            raise IccParityError(f"the sums of {image} are {len(run)}, not {SUMS_PER_IMAGE}")
        if image in expected:
            raise IccParityError(f"icc_test names {image!r} twice")
        expected[image] = (run[0], None)
    return expected


def parse_md5_case(text: str) -> dict[str, Expectation]:
    """``test_issue_1074.py``: a tuple of file names, one ``-pC`` each, and the MD5 of each output."""
    expected: dict[str, Expectation] = {}
    for case in ast.walk(_tree(text, str(MD5_CASE))):
        if not isinstance(case, ast.ClassDef):
            continue
        names: list[str] | None = None
        for value in _assigned(case, "filenames"):
            if isinstance(value, ast.ListComp) and len(value.generators) == 1:
                names = _strings(value.generators[0].iter)
        commands = [ast.unparse(value) for value in _assigned(case, "commands")]
        sums = [found for found in (_strings(value) for value in _assigned(case, "stdout")) if found]
        number = [value.value for value in _assigned(case, "num") if isinstance(value, ast.Constant)]
        if not names or len(sums) != 1 or len(commands) != 1 or "-pC" not in commands[0]:
            raise IccParityError(f"{MD5_CASE}: {case.name} is not a list of files, one -pC each, and their sums")
        if len(sums[0]) != len(names) or not all(_MD5.fullmatch(value) for value in sums[0]):
            raise IccParityError(f"{MD5_CASE}: {case.name} does not give one MD5 per file")
        for name, digest in zip(names, sums[0]):
            if "$num" in name:
                if len(number) != 1:
                    raise IccParityError(f"{MD5_CASE}: {name!r} needs a number the class does not give")
                name = name.replace("$num", str(number[0]))
            if "$" in name or name in expected:
                raise IccParityError(f"{MD5_CASE}: the file name {name!r} is not usable")
            expected[name] = (digest, None)
    if not expected:
        raise IccParityError(f"{MD5_CASE} has no test class")
    return expected


def parse_binary_case(text: str, source: str) -> dict[str, Expectation]:
    """A ``test_pr1475_*.py`` file: per class one file, its commands, and the output of each."""
    expected: dict[str, Expectation] = {}
    for case in ast.walk(_tree(text, source)):
        if not isinstance(case, ast.ClassDef):
            continue
        names = [value.value for value in _assigned(case, "filename")
                 if isinstance(value, ast.Constant) and isinstance(value.value, str)]
        # A class assigns both lists twice: empty where the build has no BMFF support, then in full.
        commands = [found for found in (_strings(value) for value in _assigned(case, "commands")) if found]
        outputs = [found for found in (_strings(value) for value in _assigned(case, "stdout")) if found]
        if len(names) != 1 or len(commands) != 1 or len(outputs) != 1 or len(commands[0]) != len(outputs[0]):
            raise IccParityError(f"{source}: {case.name} is not one file with as many outputs as commands")
        name = names[0].rsplit("/", 1)[-1]
        printed = [output for command, output in zip(commands[0], outputs[0]) if "-pC" in command.split()]
        if len(printed) != 1:
            raise IccParityError(f"{source}: {case.name} prints the profile {len(printed)} times")
        if "$" in name or name in expected:
            raise IccParityError(f"{source}: the file name {name!r} is not usable")
        if printed[0] == "":
            expected[name] = None
        elif printed[0].startswith("data:"):
            try:
                profile = base64.b64decode("".join(printed[0][5:].split()), validate=True)
            except (binascii.Error, ValueError) as error:
                raise IccParityError(f"{source}: the profile of {name} is not base64: {error}") from error
            if not profile:
                raise IccParityError(f"{source}: the profile of {name} is empty")
            expected[name] = (hashlib.md5(profile).hexdigest(), len(profile))
        else:
            raise IccParityError(f"{source}: the output of -pC for {name} is neither empty nor a data: line")
    if not expected:
        raise IccParityError(f"{source} has no test class")
    return expected


def merge(sources: dict[str, dict[str, Expectation]]) -> tuple[dict[str, Expectation], dict[str, list[str]]]:
    """One expectation per file, and the tests that give it. Two tests that disagree are damage."""
    expected: dict[str, Expectation] = {}
    origin: dict[str, list[str]] = {}
    for source in sorted(sources):
        for name, value in sources[source].items():
            if name in expected:
                known = expected[name]
                same = (known is None and value is None) or (known is not None and value is not None and known[0] == value[0])
                if not same:
                    raise IccParityError(f"the tests disagree about the profile of {name}")
                # The test that holds the bytes also knows their number.
                if known is not None and value is not None and known[1] is None:
                    expected[name] = value
            else:
                expected[name] = value
            origin.setdefault(name, []).append(source)
    return expected, origin


def key(path: Path) -> str:
    """The name a file travels under, to the dump tool and back: its path with forward slashes.

    The dump tool echoes the path it was given and escapes a backslash, so the path of a Windows
    file written the native way would come back as another string. Forward slashes are read by
    Python and by .NET on every system and come back as they went.
    """
    return path.as_posix()


def parse_dump(text: str) -> dict[str, dict[str, Any]]:
    """``FILE`` / ``ICC`` / ``STATUS`` records of ``MetadataSharp.Dump --icc``, by the path the tool echoes."""
    blocks: dict[str, dict[str, Any]] = {}
    current: dict[str, Any] | None = None
    for number, line in enumerate(text.splitlines(), 1):
        if not line:
            continue
        fields = line.split("\t")
        if fields[0] == "FILE" and len(fields) == 2:
            if current is not None and current["status"] is None:
                raise IccParityError(f"dump:{number}: FILE record before the previous one was closed")
            current = {"profile": None, "status": None, "detail": ""}
            blocks[fields[1]] = current
        elif fields[0] == "ICC" and len(fields) == 3 and current is not None and current["status"] is None:
            if current["profile"] is not None or not fields[1].isdigit() or not _MD5.fullmatch(fields[2]):
                raise IccParityError(f"dump:{number}: ICC record does not parse or is the second of its file: {line!r}")
            current["profile"] = (fields[2], int(fields[1]))
        elif fields[0] == "STATUS" and len(fields) >= 2 and current is not None and current["status"] is None:
            current["status"] = fields[1]
            current["detail"] = fields[2] if len(fields) > 2 else ""
        else:
            raise IccParityError(f"dump:{number}: unexpected record: {line!r}")
    if current is not None and current["status"] is None:
        raise IccParityError("the dump ends inside a FILE block")
    return blocks


def judge(expected: Expectation, found: tuple[str, int] | None) -> str:
    """``match``, ``differs``, ``missing`` or ``extra`` for one file that was read."""
    if expected is None:
        return "match" if found is None else "extra"
    if found is None:
        return "missing"
    same = expected[0] == found[0] and (expected[1] is None or expected[1] == found[1])
    return "match" if same else "differs"


def compare(expected: dict[str, Expectation], origin: dict[str, list[str]], dumped: dict[str, dict[str, Any]],
            paths: dict[str, Path | None]) -> dict[str, Any]:
    totals: Counter = Counter()
    statuses: Counter = Counter()
    files: list[dict[str, Any]] = []
    for name in sorted(expected):
        path = paths.get(name)
        block = dumped.get(key(path)) if path is not None else None
        if path is None:
            status, found = "no-input", None
        elif block is None:
            raise IccParityError(f"the dump has no block for {name}")
        else:
            status, found = block["status"], block["profile"]
        # A file that was not read has not been compared: it is its status, not a missing profile.
        outcome = judge(expected[name], found) if status == "ok" else status
        statuses[status] += 1
        totals["files"] += 1
        totals["profiles"] += 1 if expected[name] is not None else 0
        totals[outcome] += 1
        entry: dict[str, Any] = {"file": name, "status": status, "outcome": outcome, "tests": origin.get(name, [])}
        if expected[name] is not None:
            entry["expected"] = {"md5": expected[name][0], "bytes": expected[name][1]}
        if found is not None:
            entry["found"] = {"md5": found[0], "bytes": found[1]}
        if status != "ok" and block is not None:
            entry["detail"] = block["detail"]
        files.append(entry)
    rate = round(totals["match"] / totals["files"], 6) if totals["files"] else None
    return {"kind": KIND, "totals": dict(totals), "rate": rate, "statuses": dict(statuses), "files": files}


def text_report(report: dict[str, Any]) -> str:
    totals = report["totals"]
    lines = [
        f"{KIND} parity against exiv2 -pC",
        f"  files: {totals.get('files', 0)}, of which {totals.get('profiles', 0)} with a profile in the reference; "
        f"statuses: {report['statuses']}",
        f"  {totals.get('match', 0)} match, {totals.get('differs', 0)} differ, {totals.get('missing', 0)} missing, "
        f"{totals.get('extra', 0)} extra; rate {report['rate']}",
    ]
    for entry in report["files"]:
        if entry["outcome"] != "match":
            lines.append(f"  {entry['file']} [{entry['status']}] {entry['outcome']} expected={entry.get('expected')} "
                         f"found={entry.get('found')} {entry.get('detail', '')}".rstrip())
    return "\n".join(lines) + "\n"


def summary(report: dict[str, Any]) -> dict[str, Any]:
    """The part of the report that travels as annotations: the totals and every file that does not match."""
    return {"kind": KIND, "totals": report["totals"], "rate": report["rate"], "statuses": report["statuses"],
            "differing": [entry for entry in report["files"] if entry["outcome"] != "match"]}


def run_dump(dump_command: list[str], paths: list[Path]) -> dict[str, dict[str, Any]]:
    listing = "".join(f"{key(path)}\n" for path in paths)
    try:
        finished = subprocess.run(dump_command + ["--icc", "--files-from", "-"], input=listing, capture_output=True,
                                 text=True, encoding="utf-8", errors="replace", timeout=1800, check=False)
    except FileNotFoundError as error:
        raise IccParityError(f"the dump command is not runnable: {error}") from error
    except subprocess.TimeoutExpired as error:
        raise IccParityError("the dump tool timed out") from error
    if finished.returncode != 0:
        raise IccParityError(f"the dump tool failed with {finished.returncode}: {(finished.stderr or '').strip()[:400]}")
    return parse_dump(finished.stdout or "")


def read_expectations(tree: Path) -> tuple[dict[str, Expectation], dict[str, list[str]]]:
    def text(relative: Path) -> str:
        path = tree / relative
        if not path.is_file():
            raise IccParityError(f"no expectation at {path}")
        return path.read_text(encoding="utf-8", errors="replace")

    sources = {"icc_test": parse_bash(text(BASH_TEST), text(BASH_OUTPUT)), MD5_CASE.stem: parse_md5_case(text(MD5_CASE))}
    cases = sorted((tree / BINARY_CASES).glob(BINARY_PATTERN))
    if not cases:
        raise IccParityError(f"no {BINARY_PATTERN} under {tree / BINARY_CASES}")
    for case in cases:
        sources[case.stem] = parse_binary_case(case.read_text(encoding="utf-8", errors="replace"), case.name)
    return merge(sources)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Compare the ICC profile we read with exiv2's own expectations.")
    parser.add_argument("--tree", type=Path, required=True, help="root of a pinned exiv2 checkout")
    parser.add_argument("--dump-command", required=True, help="how to run MetadataSharp.Dump, split POSIX-style on spaces")
    parser.add_argument("--out", type=Path, default=ROOT / "artifacts/parity/icc", help="report directory")
    parser.add_argument("--annotate", action="store_true", help="emit the summary as chunked workflow annotations")
    parser.add_argument("--title", default=TITLE, help="annotation title")
    args = parser.parse_args(argv)

    expected, origin = read_expectations(args.tree)
    data = args.tree / "test" / "data"
    paths: dict[str, Path | None] = {name: (data / name if (data / name).is_file() else None) for name in expected}
    present = [path for path in paths.values() if path is not None]
    if not present:
        raise IccParityError("none of the files the expectations name is in the tree")
    report = compare(expected, origin, run_dump(shlex.split(args.dump_command), present), paths)

    args.out.mkdir(parents=True, exist_ok=True)
    (args.out / "icc-report.json").write_text(json.dumps(report, indent=1, sort_keys=True) + "\n", encoding="utf-8")
    text = text_report(report)
    (args.out / "icc-report.txt").write_text(text, encoding="utf-8")
    print(text, end="")
    if args.annotate:
        run_parity.emit_annotations(summary(report), args.title)
    # The profile is a run of bytes the file holds: there is no reading of it to disagree about.
    return 0 if report["totals"].get("match", 0) == report["totals"].get("files", 0) else 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (IccParityError, run_parity.StructuralError) as failure:
        print(f"::error title={TITLE} harness::{failure}", file=sys.stderr)
        print(f"structural failure: {failure}", file=sys.stderr)
        sys.exit(1)
