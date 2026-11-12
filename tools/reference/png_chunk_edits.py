"""The pinned exiv2 must list the chunks of a PNG as this library's profile or comment edit left them.

A separate check from key parity, and the companion of embedded_edits.py for
what the tool cannot print as a value. The tool reads the comment of a PNG
from the keyword Description; the keyword Comment, which this library writes,
it only lists. So the tool is asked for the structure of the file (-pS): one
row per chunk with its type, its length, the first bytes of its data and the
checksum it ends with. The generator (png-webp-embedded-edits) writes
chunks.tsv with one row per edited file:

    source <TAB> edited <TAB> kind <TAB> expected

For the kind "comment", expected is "-" when the edited file is to have no
chunk with the keyword Comment, or TYPE:HEX, the type of the one chunk it is
to have (tEXt or iTXt) and its text as UTF-8 in hexadecimal. The data of that
chunk is built here from the text, in ISO-8859-1 for tEXt and in UTF-8 after
an empty language and translated keyword for iTXt, and its length and its
CRC-32 are held against the row the tool prints. The chunk has to stand where
the first such chunk of the source stood, or directly before the first IDAT.

For the kind "profile", expected is "-" when the edited file is to have no
iCCP chunk, or "iCCP" when it is to have one: named "ICC Profile", before
PLTE and IDAT, where that of the source stood or directly after IHDR, and
without an sRGB chunk beside it. What the chunk inflates to is the business
of embedded_edits.py (-pC).

In both kinds every other chunk of the source has to be listed for the edited
file in the same order with the same type, length and checksum.

With --skip-unreadable-sources a source whose structure the tool does not
print is counted and skipped, for corpus files that are damaged on purpose.

Usage: png_chunk_edits.py DIRECTORY --exiv2 PATH [--skip-unreadable-sources] [--title TEXT]
"""
import argparse
import re
import subprocess
import sys
import zlib
from pathlib import Path

MAX_ANNOTATIONS = 20
NONE = "-"
STRUCTURE = "-pS"
TEXT_TYPES = ("tEXt", "zTXt", "iTXt")
PROFILE_NAME = "ICC Profile"
# "%8d | %-5s |%8d | " + 30 characters + " | 0x%08x": the data column may hold any printable character.
ROW = re.compile(r"^ *(\d+) \| (\S{4}) +\| *(\d+) \| (.{30}) \| 0x([0-9a-f]{8})$")


def parse_manifest(text):
    entries = []
    for number, line in enumerate(text.splitlines(), 1):
        if not line:
            continue
        fields = line.split("\t")
        if len(fields) != 4 or not all(fields):
            raise ValueError(f"manifest line {number} does not have four columns")
        source, edited, kind, expected = fields
        if kind == "comment":
            if expected != NONE:
                chunk, _, digits = expected.partition(":")
                if chunk not in ("tEXt", "iTXt"):
                    raise ValueError(f"manifest line {number}: a comment is written as tEXt or iTXt")
                try:
                    expected = (chunk, bytes.fromhex(digits).decode("utf-8"))
                except ValueError:
                    raise ValueError(f"manifest line {number}: the comment is not UTF-8 in hexadecimal") from None
                if not expected[1]:
                    raise ValueError(f"manifest line {number}: the comment is empty")
        elif kind == "profile":
            if expected not in (NONE, "iCCP"):
                raise ValueError(f"manifest line {number}: a profile is '-' or 'iCCP'")
        else:
            raise ValueError(f"manifest line {number}: the kind is 'comment' or 'profile'")
        entries.append((source, edited, kind, expected))
    if not entries:
        raise ValueError("the manifest names no edited file")
    return entries


def chunks(output):
    """The rows of a -pS print-out as (type, length, data column, checksum); None when it is not a whole PNG."""
    rows = []
    for line in output.decode("latin-1").split("\n"):
        found = ROW.match(line)
        if found:
            rows.append((found.group(2), int(found.group(3)), found.group(4), found.group(5)))
    if len(rows) < 3 or rows[0][0] != "IHDR" or rows[-1][0] != "IEND":
        return None
    return rows


def is_comment(row):
    """A text chunk whose keyword is Comment: the tool prints the zero after it as a dot, and not at all when it ends the data."""
    kind, length, data, _ = row
    return kind in TEXT_TYPES and (data.startswith("Comment.") or (length == 8 and data.rstrip(" ") == "Comment"))


def comment_chunk(kind, text):
    """The type, the length and the checksum of the chunk a comment is written as."""
    if kind == "tEXt":
        data = b"Comment\0" + text.encode("latin-1")
    else:
        data = b"Comment\0\0\0\0\0" + text.encode("utf-8")
    return kind, len(data), f"{zlib.crc32(kind.encode('ascii') + data):08x}"


def identity(row):
    return row[0], row[1], row[3]


def first_difference(before, after):
    for index, (was, now) in enumerate(zip(before, after)):
        if was != now:
            return f"chunk {index + 1} of the others was {was[0]} of {was[1]} bytes (0x{was[2]}) and is {now[0]} of {now[1]} bytes (0x{now[2]})"
    longer, name = (before, "fewer") if len(before) > len(after) else (after, "more")
    extra = longer[min(len(before), len(after))]
    return f"{abs(len(before) - len(after))} chunk(s) {name} than the edit names, the first a {extra[0]} of {extra[1]} bytes"


def compare_comment(expected, source, edited):
    """What the chunk list of the edited file lacks for a comment edit; an empty list when nothing."""
    problems = []
    before = [identity(row) for row in source if not is_comment(row)]
    after = [identity(row) for row in edited if not is_comment(row)]
    if before != after:
        problems.append("the chunks that are no comment changed: " + first_difference(before, after))
    found = [index for index, row in enumerate(edited) if is_comment(row)]
    if expected == NONE:
        if found:
            problems.append(f"{len(found)} chunk(s) with the keyword Comment are left")
        return problems
    if len(found) != 1:
        problems.append(f"{len(found)} chunks with the keyword Comment, expected one")
        return problems
    at = found[0]
    try:
        wanted = comment_chunk(*expected)
    except UnicodeEncodeError:
        problems.append("the manifest asks for a tEXt chunk of a text that is not ISO-8859-1")
        return problems
    if identity(edited[at]) != wanted:
        row = edited[at]
        problems.append(f"the comment is a {row[0]} of {row[1]} bytes (0x{row[3]}), "
                        f"expected a {wanted[0]} of {wanted[1]} bytes (0x{wanted[2]})")
    old = [index for index, row in enumerate(source) if is_comment(row)]
    others_before = sum(1 for row in edited[:at] if not is_comment(row))
    if old:
        if others_before != sum(1 for row in source[:old[0]] if not is_comment(row)):
            problems.append("the comment does not stand where the first comment of the source stood")
    elif edited[at + 1][0] != "IDAT" or edited[at - 1][0] == "IDAT":
        problems.append("the comment does not stand directly before the first IDAT chunk")
    return problems


def compare_profile(expected, source, edited):
    """What the chunk list of the edited file lacks for a profile edit; an empty list when nothing."""
    problems = []
    gone = ("iCCP",) if expected == NONE else ("iCCP", "sRGB")
    before = [identity(row) for row in source if row[0] not in gone]
    after = [identity(row) for row in edited if row[0] != "iCCP"]
    if before != after:
        problems.append("the chunks that are no profile changed: " + first_difference(before, after))
    found = [index for index, row in enumerate(edited) if row[0] == "iCCP"]
    if expected == NONE:
        if found:
            problems.append("an iCCP chunk is left")
        return problems
    if len(found) != 1:
        problems.append(f"{len(found)} iCCP chunks, expected one")
        return problems
    at = found[0]
    # The name, its zero and the compression method 0, then the zlib stream.
    if not edited[at][2].startswith(PROFILE_NAME + ".."):
        problems.append(f"the iCCP chunk is not named {PROFILE_NAME!r} with the compression method 0: {edited[at][2].rstrip()!r}")
    if any(row[0] in ("PLTE", "IDAT") for row in edited[:at]):
        problems.append("the iCCP chunk follows PLTE or IDAT")
    old = [index for index, row in enumerate(source) if row[0] == "iCCP"]
    if old:
        if (sum(1 for row in edited[:at] if row[0] != "iCCP")
                != sum(1 for row in source[:old[0]] if row[0] not in gone)):
            problems.append("the iCCP chunk does not stand where that of the source stood")
    elif at != 1:
        problems.append("the iCCP chunk does not stand directly after IHDR")
    return problems


def run(command, path):
    try:
        done = subprocess.run(command + [str(path)], capture_output=True, timeout=120)
    except subprocess.TimeoutExpired:
        return 124, b"", "the tool did not finish in 120 seconds"
    return done.returncode, done.stdout, done.stderr.decode("utf-8", "replace")


def check(directory, exiv2, runner=run, skip_unreadable=False):
    """Every problem found, as (file, message); the edited files checked; those skipped for their source."""
    directory = Path(directory)
    entries = parse_manifest((directory / "chunks.tsv").read_text(encoding="utf-8"))
    problems, sources, checked, skipped = [], {}, 0, 0
    for source, edited, kind, expected in entries:
        if source not in sources:
            code, output, _ = runner([exiv2, STRUCTURE], directory / source)
            sources[source] = chunks(output) if code == 0 else None
        before = sources[source]
        if before is None:
            if skip_unreadable:
                skipped += 1
            else:
                problems.append((source, f"the structure of the source is not printed with {STRUCTURE}"))
            continue
        checked += 1
        code, output, error = runner([exiv2, STRUCTURE], directory / edited)
        after = chunks(output) if code == 0 else None
        if after is None:
            problems.append((edited, f"{STRUCTURE} does not print the structure of the file (exit {code}): {error.strip()[:300]}"))
            continue
        compare = compare_comment if kind == "comment" else compare_profile
        for problem in compare(expected, before, after):
            problems.append((edited, problem))
    return problems, checked, skipped


def main(argv=None, runner=run):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("directory")
    parser.add_argument("--exiv2", required=True)
    parser.add_argument("--skip-unreadable-sources", action="store_true")
    parser.add_argument("--title", default="PNG profile and comment chunks")
    args = parser.parse_args(argv)
    problems, count, skipped = check(args.directory, args.exiv2, runner, args.skip_unreadable_sources)
    for name, message in problems[:MAX_ANNOTATIONS]:
        text = message.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")
        print(f"::error title={args.title} {name}::{text}")
    for name, message in problems:
        print(f"FAIL {name}: {message}")
    if problems:
        print(f"{len(problems)} problem(s) in {count} edited file(s)")
        return 1
    left_out = f" ({skipped} left out: the tool does not print the structure of their source)" if skipped else ""
    print(f"::notice title={args.title}::{count} edited PNG files: the pinned tool lists ({STRUCTURE}) the comment or the "
          f"profile chunk each was to have, at its place, and every other chunk of the source unchanged{left_out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
