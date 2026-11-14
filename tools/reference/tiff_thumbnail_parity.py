"""The thumbnail of a TIFF-based file, held against the same operation of the pinned exiv2.

A separate check from key parity. On a copy of every TIFF-based file of the pinned
corpus the tool sets a thumbnail with "exiv2 -i t in" and removes it with
"exiv2 -d t rm"; this library sets it with ExifEdit.SetThumbnail and removes it
with ExifEdit.RemoveThumbnail, the fixture generator (tiff-thumbnails) writing
what each makes of the file. Both results are printed by the tool with
"exiv2 -pa" and the print-outs are compared key by key, as
tools/reference/operations_parity.py compares them; the thumbnail stream of
each result is then extracted by the tool with "exiv2 -et" and the two streams
are compared.

A file this library refuses is no failure: it is one where the directory after
the primary one is a picture of the file, or one the reader refuses. Nothing is
written for it, which is checked (the file is absent), and every refusal is
counted and printed with its message; a refusal of a code not listed in
ALLOWED is a failure, as is one for a different reason in a file the tool
handles. A file the tool fails on has nothing to compare and is counted.

Usage: tiff_thumbnail_parity.py CORPUS WORK --exiv2 PATH --fixture "COMMAND ..."
"""
import argparse
import importlib.util
import shlex
import shutil
import sys
from collections import Counter, OrderedDict
from pathlib import Path

_spec = importlib.util.spec_from_file_location("operations_parity", Path(__file__).with_name("operations_parity.py"))
ops = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(ops)
run, annotate = ops.run, ops.annotate
printed, compare, documented = ops.printed, ops.compare, ops.documented

# The signatures the library reads as TIFF-based: TIFF itself, DNG, the raw formats built as one.
EXTENSIONS = (".tif", ".tiff", ".dng", ".nef", ".nrw", ".cr2", ".arw", ".sr2", ".pef", ".orf", ".rw2")

# operation -> the tool's arguments before the file
OPERATIONS = OrderedDict([("set", ["-i", "t", "in"]), ("remove", ["-d", "t", "rm"])])

# The codes this library refuses a thumbnail change with, each for a reason of its own: a directory
# that is a picture of the file (UnsupportedFeature), one nothing may be appended to
# (UnsafeRelocation), one over a budget (LimitExceeded), and a file the reader refuses.
ALLOWED = {"UnsupportedFeature", "UnsafeRelocation", "LimitExceeded", "InvalidTiff", "InvalidExif"}

# A raw file of the corpus is larger than the buffer a print of a JPEG file needs: a camera raw of
# 60 MB is a file this library and the tool both hold whole.
MAX_BYTES = 128 * 1024 * 1024


def sources(corpus):
    return sorted(path for path in Path(corpus).rglob("*")
                  if path.is_file() and path.suffix.lower() in EXTENSIONS and path.stat().st_size <= MAX_BYTES)


def stream(exiv2, path, runner=run):
    """The thumbnail stream the tool extracts from a file, or None when it extracts none."""
    extracted = Path(str(path) + "-thumb.jpg")
    if extracted.exists():
        extracted.unlink()
    runner([exiv2, "-et", str(path)])
    found = extracted.read_bytes() if extracted.exists() else None
    if extracted.exists():
        extracted.unlink()
    return found


def tool_side(work, names, exiv2, runner=run):
    """Runs the tool's operations on copies; (name, operation) -> None when it worked, else its complaint."""
    work = Path(work)
    failed = {}
    for name in names:
        for operation, arguments in OPERATIONS.items():
            copy = work / f"{name}.{operation}.tool"
            shutil.copyfile(work / (name + ".src"), copy)
            shutil.copyfile(work / "thumb.jpg", work / f"{name}.{operation}.tool-thumb.jpg")
            code, _, errors = runner([exiv2] + arguments + [str(copy)])
            if code != 0:
                failed[(name, operation)] = (errors.strip().splitlines() or [f"exit {code}"])[-1]
    return failed


def statuses(work):
    """(name, operation) -> (status, text) from the ours-tiff.tsv the fixture generator wrote."""
    found = {}
    path = Path(work) / "ours-tiff.tsv"
    if path.exists():
        for line in path.read_text(encoding="utf-8").splitlines():
            fields = line.split("\t")
            if len(fields) >= 4:
                found[(fields[0], fields[1])] = (fields[2], fields[3])
    return found


def check(work, names, exiv2, failed, runner=run):
    """
    Rows (operation, file, key, outcome, theirs, ours), the files compared per operation, the files
    left out per (operation, who, why) and the refusals per (operation, code, message).
    """
    work = Path(work)
    rows, files, left_out, refusals = [], Counter(), Counter(), Counter()
    state = statuses(work)
    for name in names:
        before = printed(exiv2, work / (name + ".src"), runner)
        for operation in OPERATIONS:
            if (name, operation) in failed:
                left_out[(operation, "tool", failed[(name, operation)])] += 1
                continue
            status, text = state.get((name, operation), ("missing", ""))
            if status.startswith("refused"):
                refusals[(operation, status.split(" ", 1)[1], text)] += 1
                # A refusal writes nothing: the file the library was given is what it returns.
                if (work / f"{name}.{operation}.ours").exists():
                    rows.append((operation, name, "the file", "written and refused", ["a file"], ["the source"]))
                continue
            if status in ("missing", "unchanged"):
                left_out[(operation, "ours", status)] += 1
                continue
            files[operation] += 1
            their_file, our_file = work / f"{name}.{operation}.tool", work / f"{name}.{operation}.ours"
            theirs, ours = printed(exiv2, their_file, runner), printed(exiv2, our_file, runner)
            if theirs is None or ours is None:
                rows.append((operation, name, "the file", "differs",
                             ["not printed" if theirs is None else "printed"], ["not printed" if ours is None else "printed"]))
                continue
            rows.extend((operation, name, *row) for row in compare(theirs, ours, before))
            # The stream itself: the tool extracts the same bytes from both results, or none from both.
            theirs_stream, ours_stream = stream(exiv2, their_file, runner), stream(exiv2, our_file, runner)
            same = theirs_stream == ours_stream
            rows.append((operation, name, "the thumbnail stream", "same" if same else "differs",
                         [f"{len(theirs_stream)} bytes" if theirs_stream is not None else "none"],
                         [f"{len(ours_stream)} bytes" if ours_stream is not None else "none"]))
    return rows, files, left_out, refusals


def summarise(rows):
    """(operation, key, outcome) -> (count, first example) for every outcome that is not "same"."""
    table = OrderedDict()
    for operation, name, key, kind, theirs, ours in rows:
        if kind == "same":
            continue
        counted = ops.grouped_key(operation, key)
        count, example = table.get((operation, counted, kind), (0, None))
        if example is None:
            example = f"{name} {key}: tool {str(theirs)[:70]} ours {str(ours)[:70]}"
        table[(operation, counted, kind)] = (count + 1, example)
    return table


def main(argv=None, runner=run):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("corpus")
    parser.add_argument("work")
    parser.add_argument("--exiv2", required=True)
    parser.add_argument("--fixture", required=True, help="the fixture generator, to which the command and WORK are added")
    args = parser.parse_args(argv)
    work = Path(args.work)
    work.mkdir(parents=True, exist_ok=True)
    fixture = shlex.split(args.fixture)

    names = OrderedDict()
    for path in sources(args.corpus):
        name = f"t{len(names):05d}"
        shutil.copyfile(path, work / (name + ".src"))
        names[name] = str(path)
        ops.NAMES[name] = path.name
    if not names:
        print("::error title=TIFF thumbnail parity::no TIFF-based file of the corpus was prepared")
        return 1

    code, output, errors = runner(fixture + ["operations-parity-blank", str(work / "thumb.jpg")], timeout=600)
    if code != 0:
        sys.stdout.write(output + errors)
        print(f"::error title=TIFF thumbnail parity::the fixture generator failed (exit {code})")
        return 1
    failed = tool_side(work, names, args.exiv2, runner)
    code, output, errors = runner(fixture + ["tiff-thumbnails", str(work)], timeout=1500)
    sys.stdout.write(output)
    if code != 0:
        sys.stdout.write(errors)
        annotate("error", f"TIFF thumbnail parity - the fixture generator failed (exit {code})",
                 errors.strip().splitlines()[-12:] or ["no output"])
        return 1

    rows, files, left_out, refusals = check(work, names, args.exiv2, failed, runner)
    totals = Counter((operation, kind) for operation, _, _, kind, _, _ in rows)
    table = summarise(rows)
    unexplained = [(key, value) for key, value in table.items() if documented(*key) is None]
    explained = [(key, value) for key, value in table.items() if documented(*key) is not None]
    unexplained.sort(key=lambda item: -item[1][0])
    # A refusal the library has no reason of its own for is a defect, not a limit.
    unknown = [(key, count) for key, count in refusals.items() if key[1] not in ALLOWED]

    annotate("error", "TIFF thumbnail parity - differences that are not documented",
             (f"{operation} {key} {kind}: {count}; {example}" for (operation, key, kind), (count, example) in unexplained),
             size=2500, limit=8)
    annotate("error", "TIFF thumbnail parity - refusals of a code that is not a reason of its own",
             (f"{operation} refused {code}: {count}; {text}" for (operation, code, text), count in unknown),
             size=2500, limit=4)
    summary = []
    for operation in OPERATIONS:
        kinds = sorted({kind for (op, kind) in totals if op == operation and kind != "same"})
        counts = ", ".join(f"{kind} {totals[(operation, kind)]}" for kind in ["same"] + kinds)
        tool = sum(count for (op, who, _), count in left_out.items() if op == operation and who == "tool")
        refused = sum(count for (op, code, _), count in refusals.items() if op == operation)
        summary.append(f"{operation}: {files[operation]} files compared (tool failed on {tool}, refused here {refused}); {counts}")
    annotate("notice", f"TIFF thumbnail parity - {len(names)} files", summary, limit=2)
    annotate("notice", "TIFF thumbnail parity - refusals and files left out",
             [f"{operation} refused {code}: {count}; {text}" for (operation, code, text), count in refusals.most_common()]
             + [f"{operation}, {who}: {count} file(s): {why}" for (operation, who, why), count in left_out.most_common()],
             size=3000, limit=4)
    annotate("notice", "TIFF thumbnail parity - documented differences",
             (f"{operation} {key} {kind}: {count}; {documented(operation, key, kind)}; {example}"
              for (operation, key, kind), (count, example) in explained), limit=3)
    for (operation, key, kind), (count, example) in unexplained:
        print(f"FAIL {operation} {key} {kind}: {count}; {example}")
    for (operation, code, text), count in unknown:
        print(f"FAIL {operation} refused {code}: {count}; {text}")
    return 1 if unexplained or unknown else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # the log of a job is not always at hand: the failure is said where the report is
        import traceback
        annotate("error", "TIFF thumbnail parity - the comparer failed", traceback.format_exc().splitlines()[-12:])
        sys.exit(1)
