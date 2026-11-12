"""The pinned exiv2 must print every XMP property an edit by path wrote, as it was written.

A separate check from key parity: nothing here is compared with what this
library reads. The fixture generator (xmp-edits) writes files after XMP edits
and manifest.tsv with one row per file:

    file <TAB> the lines the tool has to print for the XMP of the file, |-separated

A line is "key type count value" with single spaces, which is what a line of
"exiv2 -pa" is once its padding is folded. A value of * stands for any value.

A file passes when
  - "exiv2 -pa" exits with 0 for it, prints every line of the row, and prints
    no Xmp key the row does not name: nothing was lost and nothing appeared;
  - "exiv2 -pX" exits with 0 for it and prints one well-formed XML document
    that holds an rdf:RDF element: the packet as the tool takes it out.
    The tool does not take the packet out of a sidecar (.xmp): there the file
    itself has to be that document.

Usage: xmp_edits.py DIRECTORY --exiv2 PATH
"""
import argparse
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

MAX_ANNOTATIONS = 20
RDF = "{http://www.w3.org/1999/02/22-rdf-syntax-ns#}RDF"
ANY = "*"
SIDECAR = ".xmp"


def xmp_lines(text):
    """The Xmp lines of a print-out with their padding folded: key, type, count, value."""
    lines = []
    for line in text.splitlines():
        parts = line.split(None, 3)
        if len(parts) >= 3 and parts[0].startswith("Xmp."):
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


def compare(output, expected):
    """What the print-out lacks or has too much of; an empty list when it is the expected one."""
    printed = xmp_lines(output)
    problems = []
    keys = set()
    for item in expected:
        key = item.split(" ", 1)[0]
        keys.add(key)
        if item.endswith(" " + ANY):
            stem = item[:-len(ANY)]
            if not any(line.startswith(stem) for line in printed):
                problems.append(f"not printed: {item}")
        elif item not in printed:
            problems.append(f"not printed: {item}")
    for line in printed:
        if line.split(" ", 1)[0] not in keys:
            problems.append(f"printed, not written: {line}")
    return problems


def packet_problem(output):
    """Why the output of -pX is not an XMP packet, or None."""
    try:
        root = ET.fromstring(output.strip())
    except ET.ParseError as error:
        return f"the packet is not well-formed XML: {error}"
    if root.tag != RDF and root.find(".//" + RDF) is None:
        return "the packet holds no rdf:RDF element"
    return None


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
        code, output, errors = runner([exiv2, "-pa"], directory / name)
        if code != 0:
            problems.append((name, f"-pa: not read (exit {code}): {errors.strip()[:300]}"))
        else:
            found = compare(output, expected)
            if found:
                # The whole print-out goes with the first problem: one run shows what the tool prints.
                found[0] += "\nprinted:\n" + "\n".join(xmp_lines(output))
            problems.extend((name, problem) for problem in found)
        if name.lower().endswith(SIDECAR):
            # The tool has no -pX for a sidecar: the file is the packet, and is held to the same rule.
            problem = packet_problem((directory / name).read_text(encoding="utf-8"))
            if problem:
                problems.append((name, "the sidecar: " + problem))
            continue
        code, output, errors = runner([exiv2, "-pX"], directory / name)
        if code != 0:
            problems.append((name, f"-pX: not read (exit {code}): {errors.strip()[:300]}"))
        else:
            problem = packet_problem(output)
            if problem:
                problems.append((name, "-pX: " + problem))
    return problems, len(entries)


def main(argv=None, runner=run):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("directory")
    parser.add_argument("--exiv2", required=True)
    parser.add_argument("--title", default="XMP edits")
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
    print(f"::notice title={args.title}::{count} files after an XMP edit are read by the pinned tool; "
          f"every property is printed as it was written and no other, and the packet is taken out whole")
    return 0


if __name__ == "__main__":
    sys.exit(main())
