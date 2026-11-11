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
change; a key that ends in * names every key that begins with what is before
it. A source the tool cannot read is a broken fixture, and a failure.

The generator's tiff-based-corpus command writes the same manifest for
files this repository did not author. Many of those are damaged on purpose,
so with --skip-unreadable-sources a source the tool cannot read is counted
and skipped: the rule is about files the tool could read before the edit.

Usage: tiff_based_edits.py DIRECTORY --exiv2-command "exiv2 -pa" [--skip-unreadable-sources]
"""
import argparse
import shlex
import subprocess
import sys
from pathlib import Path

MAX_ANNOTATIONS = 20


FAMILIES = ("Exif.", "Iptc.", "Xmp.")


def rows(text):
    """The lines of a print-out by key: the first column, then the rest with its padding folded.

    A value that holds a line break continues on the next line, which then begins with no key:
    it belongs to the row before it.
    """
    by_key, last = {}, None
    for line in text.splitlines():
        parts = line.split(None, 3)
        if len(parts) >= 2 and parts[0].startswith(FAMILIES):
            last = by_key.setdefault(parts[0], [])
            last.append(" ".join(parts[1:]))
        elif last is not None and line.strip():
            last[-1] += "\n" + " ".join(line.split())
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
    prefixes = tuple(key[:-1] for key in changed if key.endswith("*"))
    for key, lines in before.items():
        if key in changed or key.startswith(prefixes):
            continue
        if key not in after:
            problems.append(f"{key} is no longer printed")
        elif after[key] != lines:
            problems.append(f"{key} changed: {lines} -> {after[key]}")
    return problems


def run(command, path):
    try:
        done = subprocess.run(command + [str(path)], capture_output=True, timeout=60)
    except subprocess.TimeoutExpired:
        return 124, "", "the tool did not finish in 60 seconds"
    return (done.returncode, done.stdout.decode("utf-8", "replace"),
            done.stderr.decode("utf-8", "replace"))


def check(directory, command, runner=run, skip_unreadable=False):
    """Every problem found, as (file, message); the edited files checked; those skipped for their source."""
    directory = Path(directory)
    entries = parse_manifest((directory / "manifest.tsv").read_text(encoding="utf-8"))
    problems, sources, checked, skipped = [], {}, 0, 0
    for source, edited, changed, expected in entries:
        if source not in sources:
            sources[source] = runner(command, directory / source)
        code, output, errors = sources[source]
        if code != 0 or not output.strip():
            if skip_unreadable:
                skipped += 1
            else:
                problems.append((source, f"the source is not read (exit {code}): {errors.strip()[:300]}"))
            continue
        checked += 1
        edited_code, edited_output, edited_errors = runner(command, directory / edited)
        if edited_code != 0:
            problems.append((edited, f"not read (exit {edited_code}): {edited_errors.strip()[:300]}"))
            continue
        for problem in compare(output, edited_output, changed, expected):
            problems.append((edited, problem))
    return problems, checked, skipped


def main(argv=None, runner=run):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("directory")
    parser.add_argument("--exiv2-command", required=True)
    parser.add_argument("--skip-unreadable-sources", action="store_true")
    parser.add_argument("--title", default="TIFF-based edits")
    args = parser.parse_args(argv)
    problems, count, skipped = check(args.directory, shlex.split(args.exiv2_command), runner,
                                     args.skip_unreadable_sources)
    for name, message in problems[:MAX_ANNOTATIONS]:
        text = message.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")
        print(f"::error title={args.title} {name}::{text}")
    for name, message in problems:
        print(f"FAIL {name}: {message}")
    if problems:
        print(f"{len(problems)} problem(s) in {count} edited file(s)")
        return 1
    left_out = f" ({skipped} left out: the tool does not read their source)" if skipped else ""
    print(f"::notice title={args.title}::{count} edited files are read by the pinned tool; "
          f"every line of their sources that the edit did not name is still printed{left_out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
