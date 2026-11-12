"""The pinned exiv2 must print every IPTC dataset an edit wrote, as it was written.

A separate check from key parity: nothing here is compared with what this
library reads. The fixture generator (iptc-edits) writes files after IPTC edits
and manifest.tsv with one row per file:

    file <TAB> the lines the tool has to print for the IPTC of the file, |-separated

A line is "key type count value" with single spaces, which is what a line of
"exiv2 -pi" is once its padding is folded. A value of * stands for any value.
A dataset that occurs several times is several lines with the same key.

A file passes when both "exiv2 -pi" and "exiv2 -pa" exit with 0 for it, print
every line of the row as often as the row has it, and print no other Iptc
line: nothing was lost, nothing appeared, and no occurrence was doubled.
The order of the lines is not compared.

Usage: iptc_edits.py DIRECTORY --exiv2 PATH
"""
import argparse
import subprocess
import sys
from pathlib import Path

MAX_ANNOTATIONS = 20
ANY = "*"
MODES = ("-pi", "-pa")


def iptc_lines(text):
    """The Iptc lines of a print-out with their padding folded: key, type, count, value."""
    lines = []
    for line in text.splitlines():
        parts = line.split(None, 3)
        if len(parts) >= 3 and parts[0].startswith("Iptc."):
            lines.append(" ".join(parts))
    return lines


def parse_manifest(text):
    entries = []
    for number, line in enumerate(text.splitlines(), 1):
        if not line:
            continue
        fields = line.split("\t")
        if len(fields) != 2 or not fields[0] or not fields[1]:
            raise ValueError(f"manifest line {number} does not have two columns")
        entries.append((fields[0], [item for item in fields[1].split("|") if item]))
    if not entries:
        raise ValueError("the manifest names no file")
    return entries


def matches(item, line):
    if item.endswith(" " + ANY):
        return line.startswith(item[:-len(ANY)])
    return item == line


def compare(output, expected):
    """What the print-out lacks or has too much of; an empty list when it is the expected one."""
    left = iptc_lines(output)
    problems = []
    for item in expected:
        for index, line in enumerate(left):
            if matches(item, line):
                del left[index]
                break
        else:
            problems.append(f"not printed: {item}")
    problems.extend(f"printed, not written: {line}" for line in left)
    return problems


def run(command, path):
    try:
        done = subprocess.run(command + [str(path)], capture_output=True, timeout=60)
    except subprocess.TimeoutExpired:
        return 124, "", "the tool did not finish in 60 seconds"
    return (done.returncode, done.stdout.decode("utf-8", "replace"),
            done.stderr.decode("utf-8", "replace"))


def check(directory, exiv2, runner=run):
    """Every problem found, as (file, message), and the number of files checked."""
    directory = Path(directory)
    entries = parse_manifest((directory / "manifest.tsv").read_text(encoding="utf-8"))
    problems = []
    for name, expected in entries:
        for mode in MODES:
            code, output, errors = runner([exiv2, mode], directory / name)
            if code != 0:
                problems.append((name, f"{mode}: not read (exit {code}): {errors.strip()[:300]}"))
                continue
            found = compare(output, expected)
            if found:
                # The whole print-out goes with the first problem: one run shows what the tool prints.
                found[0] += "\nprinted:\n" + "\n".join(iptc_lines(output))
            problems.extend((name, f"{mode}: {problem}") for problem in found)
    return problems, len(entries)


def main(argv=None, runner=run):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("directory")
    parser.add_argument("--exiv2", required=True)
    parser.add_argument("--title", default="IPTC edits")
    args = parser.parse_args(argv)
    problems, count = check(args.directory, args.exiv2, runner)
    for name, message in problems[:MAX_ANNOTATIONS]:
        text = message[:6000].replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")
        print(f"::error title={args.title} {name}::{text}")
    for name, message in problems:
        print(f"FAIL {name}: {message}")
    if problems:
        print(f"{len(problems)} problem(s) in {count} file(s)")
        return 1
    print(f"::notice title={args.title}::{count} files after an IPTC edit are read by the pinned tool; "
          f"every dataset is printed as it was written, as often as it was written, and no other")
    return 0


if __name__ == "__main__":
    sys.exit(main())
