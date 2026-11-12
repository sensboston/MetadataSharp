#!/usr/bin/env python3
"""Edits of CRW files by this library, read back by the pinned exiv2.

    crw_parity.py CORPUS WORK --exiv2 PATH --fixture "COMMAND ..."

Copies the CRW files of CORPUS to WORK, has the fixture generator (crw-parity) set tags and the
comment of each, every change in a copy of its own, and prints the source and every copy with
"exiv2 -u -PEkv" (and the comment with "exiv2 -pc"). A copy passes when

  * the tool prints the key that was set with the value that was set (for the comment: the comment),
  * every other key is printed with the lines the source has for it, and no key came or went,
  * the tool exits as it does for the source and warns of nothing it does not warn of there.

The rules are those of makernote_parity.py, whose functions are used. A refusal of the library is
no failure: it is counted, by the operation and its code. The lines of "exiv2 --version --verbose"
that say which formats the pinned build was made with are printed as a notice.
"""
import argparse
import importlib.util
import shlex
import shutil
import sys
from collections import Counter, OrderedDict
from pathlib import Path

_spec = importlib.util.spec_from_file_location("makernote_parity", Path(__file__).with_name("makernote_parity.py"))
mk = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(mk)
run, annotate = mk.run, mk.annotate

MAX_BYTES = 24 * 1024 * 1024
BUILD = ("exiv2=", "version=", "enable_video=", "enable_bmff=", "enable_brotli=", "enable_inih=", "have_inttypes=")


def comment(exiv2, path, runner=run):
    code, output, _ = runner([exiv2, "-pc", str(path)])
    return output.strip("\r\n\0 ")


def comment_verdict(exiv2, source, source_print, copy, value, runner=run):
    """As makernote_parity.verdict, for the comment: it reads as set and no key changed."""
    if comment(exiv2, copy, runner) != value:
        return "comment the tool does not print", f"wanted {value!r}, printed {comment(exiv2, copy, runner)[:60]!r}"
    (before, code_before, warned_before), (after, code_after, warned_after) = source_print, mk.printed(exiv2, copy, runner)
    for key in before:
        if key not in after:
            return "another key gone", key
        if after[key] != before[key]:
            return "another key changed", f"{key}: {before[key][:1]!r} -> {after[key][:1]!r}"
    for key in after:
        if key not in before:
            return "a key appeared", key
    if code_after != code_before:
        return "exit code changed", f"{code_before} -> {code_after}"
    new = [line for line in warned_after if line not in warned_before]
    return ("new warning", new[0]) if new else ("ok", "")


def check(work, names, exiv2, runner=run):
    work = Path(work)
    counts, examples, prints = Counter(), {}, {}
    for name, number, operation, key, status, value in mk.rows_of(work):
        if operation == "none":
            counts[("none", status)] += 1
            continue
        if status != "written":
            outcome, detail = status, f"{key}: {value}"
        else:
            source = work / f"{name}.crw"
            if name not in prints:
                prints[name] = mk.printed(exiv2, source, runner)
            copy = work / f"{name}.{number}.ours.crw"
            if operation == "comment":
                outcome, detail = comment_verdict(exiv2, source, prints[name], copy, value, runner)
            else:
                outcome, detail = mk.verdict(prints[name], mk.printed(exiv2, copy, runner), key, value)
            copy.unlink(missing_ok=True)
        counts[(operation, outcome)] += 1
        examples.setdefault((operation, outcome), f"{Path(names[name]).name}: {detail}")
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

    _, verbose, _ = runner([args.exiv2, "--version", "--verbose"])
    annotate("notice", "CRW parity - the pinned build (exiv2 --version --verbose)",
             [line for line in verbose.splitlines() if line.startswith(BUILD)])

    names = OrderedDict()
    for path in sorted(p for p in Path(args.corpus).rglob("*.crw") if p.is_file() and p.stat().st_size <= MAX_BYTES):
        name = f"s{len(names):05d}"
        shutil.copyfile(path, work / f"{name}.crw")
        names[name] = str(path)
    if not names:
        print("::error title=CRW parity::no CRW file of the corpus was prepared")
        return 1
    code, output, errors = runner(shlex.split(args.fixture) + ["crw-parity", str(work)], timeout=900)
    sys.stdout.write(output)
    if code != 0:
        sys.stdout.write(errors)
        annotate("error", f"CRW parity - the fixture generator failed (exit {code})",
                 errors.strip().splitlines()[-12:] or output.strip().splitlines()[-12:] or ["no output"])
        return 1

    counts, examples = check(work, names, args.exiv2, runner)
    failures, summary = [], []
    for (operation, outcome), count in sorted(counts.items()):
        line = f"{operation}: {outcome} {count}"
        if operation == "none" or outcome == "ok":
            summary.append(line)
        elif outcome.startswith("refused "):
            summary.append(f"{line}; e.g. {examples[(operation, outcome)]}")
        else:
            failures.append(f"{line}; e.g. {examples[(operation, outcome)]}")
    passed = sum(count for (operation, outcome), count in counts.items() if outcome == "ok")
    annotate("error", "CRW parity - edits the tool does not read back as set", failures, size=2500, limit=8)
    annotate("notice", f"CRW parity - {len(names)} files; {passed} edits read back by the tool", summary)
    if passed == 0:
        print("::error title=CRW parity::no edit was written and read back")
        return 1
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
