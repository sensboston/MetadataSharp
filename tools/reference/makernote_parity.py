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

The same job also tries the public MakerNote=Copy path for every corpus file with a decoded note,
counts copied/skipped/refused by layout and reason, and compares every copied note with Exiv2
(-u -PEkv). For skips it tests Exiv2's own -ea/-ia round trip into the same neutral JPEG; a note
Exiv2 carries but this library skips fails the gate. The full per-file ledger is copy-report.json.

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


def maker_note_printed(exiv2, path, runner=run):
    """The decoded MakerNote keys and values, excluding standard EXIF directories."""
    values, code, warnings = printed(exiv2, path, runner)
    standard = {"Image", "Photo", "GPSInfo", "Iop", "Thumbnail", "MakerNote"}
    return OrderedDict((key, value) for key, value in values.items()
                       if len(key.split(".")) > 2 and key.split(".")[1] not in standard), code, warnings


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
            outcome, detail = status, f"{key}: {value}"
        else:
            if name not in prints:
                prints[name] = printed(exiv2, work / f"{name}.{extension}", runner)
            copy = work / f"{name}.{number}.ours.{extension}"
            outcome, detail = verdict(prints[name], printed(exiv2, copy, runner), key, value)
            copy.unlink(missing_ok=True)
        entry = (group_of(key), operation, outcome)
        if outcome == "ok":
            counts[("-", "files", extension)] += 1
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

    # The edit corpus above is intentionally JPEG/raw-only. The copy scan is broader: give the
    # fixture generator every corpus file and let the public reader identify decoded notes.
    copy_input_dir = work / "copy-inputs"
    copy_input_dir.mkdir(parents=True, exist_ok=True)
    copy_input_names = {}
    for path in sorted(Path(args.corpus).rglob("*")):
        if not path.is_file():
            continue
        ident = f"c{len(copy_input_names):06d}"
        suffix = path.suffix.lower()
        if len(suffix) > 12:
            suffix = ".bin"
        target = copy_input_dir / (ident + suffix)
        shutil.copyfile(path, target)
        copy_input_names[ident] = target
    code, output, errors = runner(shlex.split(args.fixture) + ["makernote-parity", str(work)], timeout=900)
    sys.stdout.write(output)
    if code != 0:
        sys.stdout.write(errors)
        annotate("error", f"MakerNote parity - the fixture generator failed (exit {code})",
                 errors.strip().splitlines()[-12:] or output.strip().splitlines()[-12:] or ["no output"])
        return 1

    counts, examples = check(work, names, args.exiv2, runner)
    failures, accepted, summary = [], [], []
    copy_rows = []
    copy_tsv = work / "copy.tsv"
    if copy_tsv.exists():
        for line in copy_tsv.read_text(encoding="utf-8").splitlines():
            parts = line.split("\t", 4)
            if len(parts) == 5:
                copy_rows.append(parts)
    copy_counts = Counter()
    copy_examples = {}
    copy_details = []
    oracle_root = work / "copy-oracle"
    neutral = work / "copy-neutral.jpg"
    if not copy_rows:
        failures.append("the corpus scan found no decoded MakerNotes")

    def oracle_copy(source_path, ident):
        """Try Exiv2's own -ea/-ia path into the same neutral JPEG; return status and detail."""
        directory = oracle_root / ident
        directory.mkdir(parents=True, exist_ok=True)
        local_source = directory / ("source" + source_path.suffix.lower())
        shutil.copyfile(source_path, local_source)
        exv = local_source.with_suffix(".exv")
        code, output, errors = runner([args.exiv2, "-ea", "ex", str(local_source)])
        if code != 0 or not exv.is_file():
            return "refused", f"extract exit={code}: {(errors or output).strip()[:400]}"
        dest = directory / "destination.jpg"
        shutil.copyfile(neutral, dest)
        shutil.copyfile(exv, dest.with_suffix(".exv"))
        code, output, errors = runner([args.exiv2, "-ia", "in", str(dest)])
        if code != 0:
            return "refused", f"insert exit={code}: {(errors or output).strip()[:400]}"
        before, before_code, _ = maker_note_printed(args.exiv2, local_source, runner)
        after, after_code, _ = maker_note_printed(args.exiv2, dest, runner)
        if before_code != after_code:
            return "differs", f"-PEkv exit code {before_code} -> {after_code}"
        if not before:
            return "not_printable", "Exiv2 -u -PEkv printed no decoded MakerNote keys from the source"
        if before == after:
            return "carried", f"Exiv2 preserved {len(before)} MakerNote keys/values through -ea/-ia"
        missing = [key for key in before if after.get(key) != before[key]]
        extra = [key for key in after if key not in before]
        return "differs", f"Exiv2's own copy changed/dropped values: missing/changed={missing[:4]}, extra={extra[:4]}"

    for ident, layout, status, reason_codes, message in copy_rows:
        reason = reason_codes or "none"
        oracle_status, oracle_detail = "not_applicable", ""
        if status == "copied":
            source_path = copy_input_names.get(ident)
            copied_path = work / "copy-output" / (ident + ".jpg")
            if source_path is None or not copied_path.is_file():
                failures.append(f"{layout} {ident}: copied status has no source or destination file")
                oracle_status, oracle_detail = "missing", "copy output/source missing"
            else:
                before, before_code, before_errors = maker_note_printed(args.exiv2, source_path, runner)
                after, after_code, after_errors = maker_note_printed(args.exiv2, copied_path, runner)
                if not before:
                    failures.append(f"{layout} {ident}: Exiv2 printed no MakerNote keys on the corpus source "
                                    f"(exit {before_code}; {(before_errors or '')[:240]})")
                    oracle_status, oracle_detail = "source_not_printable", ""
                elif before_code != after_code or before != after:
                    missing = [key for key in before if after.get(key) != before[key]]
                    extra = [key for key in after if key not in before]
                    failures.append(f"{layout} {ident}: copied MakerNote differs under Exiv2; "
                                    f"exit {before_code}->{after_code}, missing/changed={missing[:5]}, extra={extra[:5]}")
                    oracle_status, oracle_detail = "ours_differs", ""
                else:
                    oracle_status, oracle_detail = "matched", f"{len(before)} MakerNote keys/values"
        elif status == "skipped":
            source_path = copy_input_names.get(ident)
            if source_path is None:
                oracle_status, oracle_detail = "missing", "source path missing"
                failures.append(f"{layout} {ident}: skipped source path is missing")
            else:
                oracle_status, oracle_detail = oracle_copy(source_path, ident)
                if oracle_status == "carried":
                    failures.append(f"{layout} {ident}: MetadataSharp skipped ({reason}) but Exiv2 carries the MakerNote; "
                                    f"{message}; {oracle_detail}")
        copy_counts[(layout, status, reason, oracle_status)] += 1
        copy_examples.setdefault((layout, status, reason, oracle_status), f"{ident}: {message or oracle_detail}")
        copy_details.append({"id": ident, "layout": layout, "status": status, "reason_codes": reason_codes,
                             "message": message, "exiv2_copy": oracle_status, "detail": oracle_detail})

    for key, count in sorted(copy_counts.items()):
        layout, status, reason, oracle_status = key
        line = f"{layout} copy {status} reason={reason} exiv2={oracle_status}: {count}"
        summary.append(line + (f"; e.g. {copy_examples[key]}" if status != "copied" else ""))
    copy_report = {"decoded_makernote_files": len(copy_rows),
                   "counts": [{"layout": k[0], "status": k[1], "reason_codes": k[2],
                               "exiv2_copy": k[3], "count": v} for k, v in sorted(copy_counts.items())],
                   "files": copy_details}
    (work / "copy-report.json").write_text(__import__("json").dumps(copy_report, indent=2) + "\n", encoding="utf-8")
    copy_totals = Counter(row[2] for row in copy_rows)
    summary.insert(0, f"Corpus MakerNote copies: {len(copy_rows)} decoded; " +
                   ", ".join(f"{name}={copy_totals.get(name, 0)}" for name in ("copied", "skipped", "refused")))
    for (group, operation, outcome), count in sorted(counts.items()):
        line = f"{group} {operation}: {outcome} {count}"
        if operation == "files":
            summary.append(f"edits read back in .{outcome} files: {count}")
        elif operation == "none" or outcome == "ok":
            summary.append(line)
        elif outcome.startswith("refused "):
            summary.append(f"{line}; e.g. {examples[(group, operation, outcome)]}")
        elif expected(group, operation, outcome):
            accepted.append(f"{line}; {expected(group, operation, outcome)}; e.g. {examples[(group, operation, outcome)]}")
        else:
            failures.append(f"{line}; e.g. {examples[(group, operation, outcome)]}")
    passed = sum(count for (_, operation, outcome), count in counts.items() if outcome == "ok" and operation != "files")
    written = {operation: sum(count for (_, op, outcome), count in counts.items() if op == operation and outcome == "ok")
               for operation in ("same", "grow", "short")}
    annotate("error", "MakerNote parity - edits the tool does not read back as set", failures, size=2500, limit=8)
    annotate("notice", "MakerNote parity - accepted differences", accepted)
    annotate("notice", f"MakerNote parity - {len(names)} files; {passed} edits read back by the tool "
             f"(same {written['same']}; grow {written['grow']}; short {written['short']})", summary)
    if passed == 0:
        print("::error title=MakerNote parity::no edit was written and read back")
        return 1
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
