"""The pinned exiv2 must print the profile and the comment a file was given by this library.

A separate check from key parity: nothing here is compared with what this
library reads. The fixture generator (jpeg-embedded-edits) writes authored
JPEG files, the same files after their ICC profile or their comment was
inserted, replaced or removed, and manifest.tsv with one row per edited file:

    source <TAB> edited <TAB> profile <TAB> comment

The profile is the name of a file of the directory that holds the bytes the
tool has to print for the edited file with -pC, "-" when it has to print
none, or "=" when it has to print what it prints for the source. The comment
is the bytes the tool has to print with -pc, in hexadecimal, or "-" or "="
in the same sense.

For each row the tool is run three ways on both files. The edited file
passes when the tool exits with 0 for -pC and -pc, prints exactly the
expected profile and the expected comment, and prints with -pa the lines,
and exits with the code, it did for the source: an edit of the profile or
the comment changes nothing of the Exif, IPTC and XMP data.

The png-webp-embedded-edits command writes the same manifest for PNG and WebP
files. Their comment column is always "=": a WebP has no comment, and the tool
reads that of a PNG from the keyword Description, not from the keyword Comment
this library writes, so an edit may not change what -pc prints. The comment
chunk itself is the business of png_chunk_edits.py.

The generator's jpeg-embedded-corpus and png-webp-embedded-corpus commands write the same manifest for
files this repository did not author. Many of those are damaged on purpose,
so with --skip-unreadable-sources a source for which -pC or -pc does not
exit with 0 is counted and skipped: the rule is about files the tool could
read before the edit. Without the option such a source is a broken fixture,
and a failure.

Usage: embedded_edits.py DIRECTORY --exiv2 PATH [--skip-unreadable-sources] [--title TEXT]
"""
import argparse
import subprocess
import sys
from pathlib import Path

MAX_ANNOTATIONS = 20
NONE, SAME = "-", "="
PROFILE, COMMENT, KEYS = "-pC", "-pc", "-pa"


def parse_manifest(text):
    entries = []
    for number, line in enumerate(text.splitlines(), 1):
        if not line:
            continue
        fields = line.split("\t")
        if len(fields) != 4 or not all(fields):
            raise ValueError(f"manifest line {number} does not have four columns")
        comment = fields[3]
        if comment not in (NONE, SAME):
            try:
                comment = bytes.fromhex(comment)
            except ValueError:
                raise ValueError(f"manifest line {number}: the comment is not hexadecimal") from None
        entries.append((fields[0], fields[1], fields[2], comment))
    if not entries:
        raise ValueError("the manifest names no edited file")
    return entries


def printed_comment(output):
    """The comment of a -pc print-out: the tool ends it with one line break; None when it does not."""
    return output[:-1] if output.endswith(b"\n") else None


def short(data):
    """Bytes for a message: their length, and the first of them."""
    return f"{len(data)} bytes {data[:24].hex()}" + ("..." if len(data) > 24 else "")


def compare(expected_profile, expected_comment, source_keys, profile, comment, keys):
    """What the print-outs of an edited file lack; an empty list when they lack nothing.

    Each of profile, comment and keys is (exit code, standard output, standard error) of one
    run on the edited file; source_keys is the same for -pa on the source.
    """
    problems = []
    if profile[0] != 0:
        problems.append(f"{PROFILE} does not read the file (exit {profile[0]}): {profile[2].strip()[:300]}")
    elif profile[1] != expected_profile:
        problems.append(f"{PROFILE} prints {short(profile[1])}, expected {short(expected_profile)}")
    if comment[0] != 0:
        problems.append(f"{COMMENT} does not read the file (exit {comment[0]}): {comment[2].strip()[:300]}")
    else:
        found = printed_comment(comment[1])
        if found is None:
            problems.append(f"{COMMENT} prints no line: {short(comment[1])}")
        elif found != expected_comment:
            problems.append(f"{COMMENT} prints {short(found)}, expected {short(expected_comment)}")
    if keys[0] != source_keys[0]:
        problems.append(f"{KEYS} exits with {keys[0]}, with {source_keys[0]} for the source")
    if keys[1] != source_keys[1]:
        before, after = source_keys[1].splitlines(), keys[1].splitlines()
        gone = [line for line in before if line not in after]
        new = [line for line in after if line not in before]
        detail = gone[0] if gone else new[0] if new else b"the same lines in another order"
        problems.append(f"{KEYS} prints {len(gone)} line(s) fewer and {len(new)} more than for the source: "
                        + detail.decode("utf-8", "replace")[:200])
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
    entries = parse_manifest((directory / "manifest.tsv").read_text(encoding="utf-8"))
    problems, sources, profiles, checked, skipped = [], {}, {}, 0, 0
    for source, edited, profile, comment in entries:
        if source not in sources:
            sources[source] = tuple(runner([exiv2, option], directory / source) for option in (PROFILE, COMMENT, KEYS))
        source_profile, source_comment, source_keys = sources[source]
        unread = [option for option, result in ((PROFILE, source_profile), (COMMENT, source_comment)) if result[0] != 0]
        if not unread and printed_comment(source_comment[1]) is None:
            unread = [COMMENT]
        if unread:
            if skip_unreadable:
                skipped += 1
            else:
                problems.append((source, f"the source is not read with {' and '.join(unread)}"))
            continue
        if profile == SAME:
            expected_profile = source_profile[1]
        elif profile == NONE:
            expected_profile = b""
        else:
            if profile not in profiles:
                profiles[profile] = (directory / profile).read_bytes()
            expected_profile = profiles[profile]
        if comment == SAME:
            expected_comment = printed_comment(source_comment[1])
        else:
            expected_comment = b"" if comment == NONE else comment
        checked += 1
        results = [runner([exiv2, option], directory / edited) for option in (PROFILE, COMMENT, KEYS)]
        for problem in compare(expected_profile, expected_comment, source_keys, *results):
            problems.append((edited, problem))
    return problems, checked, skipped


def main(argv=None, runner=run):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("directory")
    parser.add_argument("--exiv2", required=True)
    parser.add_argument("--skip-unreadable-sources", action="store_true")
    parser.add_argument("--title", default="JPEG profile and comment edits")
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
    left_out = f" ({skipped} left out: the tool does not read their source)" if skipped else ""
    print(f"::notice title={args.title}::{count} edited files: the pinned tool prints the profile ({PROFILE}) and the "
          f"comment ({COMMENT}) each was to have, and with {KEYS} what it prints for the source{left_out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
