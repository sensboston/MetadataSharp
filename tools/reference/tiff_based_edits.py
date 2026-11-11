"""The pinned exiv2 must still read every file the TIFF-based writer edited.

A separate check from key parity: nothing here is compared with what this
library reads. The fixture generator (tiff-based-edits) writes authored
TIFF, DNG, NEF-like and CR2-like files, the same files after each kind of
edit, and manifest.tsv with one row per edited file:

    source <TAB> edited <TAB> keys the edit may change, comma-separated
           <TAB> text the tool has to print for the edited file, |-separated

For each row the tool is run on both files. The edited file passes when
the tool exits with 0 for it, prints every expected text, and prints every
line it printed for the source whose key the edit was not allowed to
change. A source the tool cannot read is a broken fixture, and a failure.

Usage: tiff_based_edits.py DIRECTORY --exiv2-command "exiv2 -pa"
"""
import argparse
import shlex
import subprocess
import sys
from pathlib import Path

MAX_ANNOTATIONS = 20


def rows(text):
    """The lines of a print-out by key: the first column, then the rest with its padding folded."""
    by_key = {}
    for line in text.splitlines():
        parts = line.split(None, 3)
        if len(parts) < 2:
            continue
        by_key.setdefault(parts[0], []).append(" ".join(parts[1:]))
    return by_key


def parse_manifest(text):
    entries = []
    for number, line in enumerate(text.splitlines(), 1):
        if not line:
            continue
        fields = line.split("\t")
        if len(fields) != 4 or not fields[0] or not fields[1]:
            raise ValueError(f"manifest line {number} does not have four columns")
        entries.append((fields[0], fields[1],
                        {key for key in fields[2].split(",") if key},
                        [item for item in fields[3].split("|") if item]))
    if not entries:
        raise ValueError("the manifest names no edited file")
    return entries


def compare(source_output, edited_output, changed, expected):
    """What the print-out of an edited file lacks; an empty list when it lacks nothing."""
    problems = []
    for item in expected:
        if item not in edited_output:
            problems.append(f"expected text is not printed: {item}")
    before, after = rows(source_output), rows(edited_output)
    for key, lines in before.items():
        if key in changed:
            continue
        if key not in after:
            problems.append(f"{key} is no longer printed")
        elif after[key] != lines:
            problems.append(f"{key} changed: {lines} -> {after[key]}")
    return problems


def run(command, path):
    done = subprocess.run(command + [str(path)], capture_output=True, timeout=60)
    return (done.returncode, done.stdout.decode("utf-8", "replace"),
            done.stderr.decode("utf-8", "replace"))


def check(directory, command, runner=run):
    """Every problem found, as (file, message), and the number of edited files checked."""
    directory = Path(directory)
    entries = parse_manifest((directory / "manifest.tsv").read_text(encoding="utf-8"))
    problems, sources = [], {}
    for source, edited, changed, expected in entries:
        if source not in sources:
            sources[source] = runner(command, directory / source)
        code, output, errors = sources[source]
        if code != 0 or not output.strip():
            problems.append((source, f"the source is not read (exit {code}): {errors.strip()[:300]}"))
            continue
        edited_code, edited_output, edited_errors = runner(command, directory / edited)
        if edited_code != 0:
            problems.append((edited, f"not read (exit {edited_code}): {edited_errors.strip()[:300]}"))
            continue
        for problem in compare(output, edited_output, changed, expected):
            problems.append((edited, problem))
    return problems, len(entries)


def main(argv=None, runner=run):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("directory")
    parser.add_argument("--exiv2-command", required=True)
    args = parser.parse_args(argv)
    problems, count = check(args.directory, shlex.split(args.exiv2_command), runner)
    for name, message in problems[:MAX_ANNOTATIONS]:
        text = message.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")
        print(f"::error title=TIFF-based edits {name}::{text}")
    for name, message in problems:
        print(f"FAIL {name}: {message}")
    if problems:
        print(f"{len(problems)} problem(s) in {count} edited file(s)")
        return 1
    print(f"::notice title=TIFF-based edits::{count} edited files are read by the pinned tool; "
          "every line of their sources that the edit did not name is still printed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
