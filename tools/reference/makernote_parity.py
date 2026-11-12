#!/usr/bin/env python3
"""Edits of MakerNote tags by this library, read back by the pinned exiv2.

    makernote_parity.py CORPUS WORK --exiv2 PATH --fixture "COMMAND ..."

Copies the JPEG and TIFF-based raw files of CORPUS to WORK, has the fixture generator
(makernote-parity) set tags of their MakerNotes, each in a copy of its own, and prints the source
and every copy with "exiv2 -u -PEkv" (the key and the value as it is stored, tags without a name
too; the tag that holds the note is left out, since its value is the bytes of the note). A copy
passes when

  * the tool prints the key that was set with the value that was set,
  * every other key is printed with the lines the source has for it, and no key came or went,
  * the tool exits as it does for the source and warns of nothing it does not warn of there.

The operations are "same" (a text of the length the tag has), "grow" (a longer text, which the
library appends) and "short" (another number in a SHORT tag). A refusal of the library is no
failure: it is counted, by the layout of the note and its code. A copy that does not pass fails the
job unless EXPECTED names the layout, the operation and the verdict with a reason.

What this decides: MakerNoteKeys.Grows lists the layouts in which a value may grow. A layout is
listed there only when its "grow" rows pass here.
"""
import argparse
import importlib.util
import re
import shlex
import shutil
import sys
from collections import Counter, OrderedDict
from pathlib import Path

_spec = importlib.util.spec_from_file_location("sync_parity", Path(__file__).with_name("sync_parity.py"))
sync = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(sync)
run, annotate = sync.run, sync.annotate

MAX_BYTES = 24 * 1024 * 1024
EXTENSIONS = {".jpg": "jpg", ".jpeg": "jpg", ".cr2": "cr2", ".nef": "nef", ".arw": "arw", ".pef": "pef",
              ".orf": "orf", ".dng": "dng", ".srw": "srw", ".sr2": "sr2", ".nrw": "nrw"}

# The tags whose value is the note itself: its bytes, which an edit changes.
HOLDERS = ("Exif.Photo.MakerNote", "Exif.Image.DNGPrivateData")

# (group of the key, operation, verdict) -> why the difference is accepted. A group is the second
# part of the key ("Canon" of Exif.Canon.OwnerName); "*" stands for every group.
EXPECTED = {}


def expected(group, operation, verdict):
    return EXPECTED.get((group, operation, verdict)) or EXPECTED.get(("*", operation, verdict))


def printed(exiv2, path, runner=run):
    """(key -> the values "exiv2 -PEkv" prints for it, the exit code, the warnings without the path)."""
    code, output, errors = runner([exiv2, "-u", "-PEkv", str(path)])
    found = OrderedDict()
    for line in output.splitlines():
        parts = line.split(None, 1)
        if parts and parts[0].startswith("Exif.") and parts[0] not in HOLDERS:
            found.setdefault(parts[0], []).append(parts[1].strip() if len(parts) > 1 else "")
    name = Path(path).name
    warnings = sorted({re.sub(r"\s+", " ", line.replace(str(path), "").replace(name, "")).strip()
                       for line in errors.splitlines() if line.strip()})
    return found, code, warnings


def verdict(source, result, key, value):
    """"ok", or what is wrong with the print-out of an edited copy, and the detail."""
    (before, code_before, warned_before), (after, code_after, warned_after) = source, result
    if key not in before:
        return "key the tool does not print", key
    if key not in after:
        return "key gone for the tool", key
    if value not in after[key]:
        return "value the tool does not print", f"{key}: wanted {value!r}, printed {after[key][:2]!r}"
    for other in before:
        if other == key:
            continue
        if other not in after:
            return "another key gone", other
        if after[other] != before[other]:
            return "another key changed", f"{other}: {before[other][:1]!r} -> {after[other][:1]!r}"
    for other in after:
        if other not in before:
            return "a key appeared", other
    if code_after != code_before:
        return "exit code changed", f"{code_before} -> {code_after}"
    new = [line for line in warned_after if line not in warned_before]
    if new:
        return "new warning", new[0]
    return "ok", ""


def sources(corpus):
    return sorted(path for path in Path(corpus).rglob("*")
                  if path.is_file() and path.suffix.lower() in EXTENSIONS and path.stat().st_size <= MAX_BYTES)


def rows_of(work):
    """(name, number, operation, key, status, value) for every line of ours.tsv."""
    rows = []
    for line in (Path(work) / "ours.tsv").read_text(encoding="utf-8").splitlines():
        parts = line.split("\t")
        if len(parts) == 6:
            rows.append(tuple(parts))
    return rows


def group_of(key):
    parts = key.split(".")
    return parts[1] if len(parts) > 2 else "?"


def check(work, names, exiv2, runner=run):
    """Counter of (group, operation, verdict or refusal), and an example for each."""
    work = Path(work)
    counts, examples, prints = Counter(), {}, {}
    for name, number, operation, key, status, value in rows_of(work):
        if operation == "none":
            counts[("-", "none", status)] += 1
            continue
        extension = names[name][1]
        if status != "written":
            outcome, detail = status, key
        else:
            if name not in prints:
                prints[name] = printed(exiv2, work / f"{name}.{extension}", runner)
            copy = work / f"{name}.{number}.ours.{extension}"
            outcome, detail = verdict(prints[name], printed(exiv2, copy, runner), key, value)
            copy.unlink(missing_ok=True)
        entry = (group_of(key), operation, outcome)
        counts[entry] += 1
        examples.setdefault(entry, f"{Path(names[name][0]).name}: {detail}")
    return counts, examples


def main(argv=None, runner=run):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("corpus")
    parser.add_argument("work")
    parser.add_argument("--exiv2", required=True)
    parser.add_argument("--fixture", required=True, help="the fixture generator, to which the command and WORK are added")
    args = parser.parse_args(argv)
    work = Path(args.work)
    work.mkdir(parents=True, exist_ok=True)

    names = OrderedDict()
    for path in sources(args.corpus):
        name, extension = f"s{len(names):05d}", EXTENSIONS[path.suffix.lower()]
        shutil.copyfile(path, work / f"{name}.{extension}")
        names[name] = (str(path), extension)
    if not names:
        print("::error title=MakerNote parity::no file of the corpus was prepared")
        return 1
    code, output, errors = runner(shlex.split(args.fixture) + ["makernote-parity", str(work)], timeout=900)
    sys.stdout.write(output)
    if code != 0:
        sys.stdout.write(errors)
        annotate("error", f"MakerNote parity - the fixture generator failed (exit {code})",
                 errors.strip().splitlines()[-12:] or output.strip().splitlines()[-12:] or ["no output"])
        return 1

    counts, examples = check(work, names, args.exiv2, runner)
    failures, accepted, summary = [], [], []
    for (group, operation, outcome), count in sorted(counts.items()):
        line = f"{group} {operation}: {outcome} {count}"
        if operation == "none" or outcome == "ok" or outcome.startswith("refused "):
            summary.append(line)
        elif expected(group, operation, outcome):
            accepted.append(f"{line}; {expected(group, operation, outcome)}; e.g. {examples[(group, operation, outcome)]}")
        else:
            failures.append(f"{line}; e.g. {examples[(group, operation, outcome)]}")
    passed = sum(count for (_, operation, outcome), count in counts.items() if outcome == "ok")
    written = {operation: sum(count for (_, op, outcome), count in counts.items() if op == operation and outcome == "ok")
               for operation in ("same", "grow", "short")}
    annotate("error", "MakerNote parity - edits the tool does not read back as set", failures, size=2500, limit=8)
    annotate("notice", "MakerNote parity - accepted differences", accepted)
    annotate("notice", f"MakerNote parity - {len(names)} files, {passed} edits read back by the tool "
             f"(same {written['same']}, grow {written['grow']}, short {written['short']})", summary)
    if passed == 0:
        print("::error title=MakerNote parity::no edit was written and read back")
        return 1
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
