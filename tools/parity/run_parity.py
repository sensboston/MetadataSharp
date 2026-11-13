"""Differential parity harness: our readers against exiv2's own expectations.

The harness never writes corpus material into this repository. It reads an exiv2 checkout that CI
fetched at the pinned commit, and it reads the fixture directories this project owns. Everything it
learns goes into a report under ``artifacts/``; the expectations it compares against stay where they
came from. That is a licensing posture as much as a hygiene one - upstream code is GPL and some of
its sample files have unclear provenance, so the only thing that crosses into the repo is this
harness and our own fixtures.

Exit status is 0 when the run was structurally sound, whatever the parity rates say, and 1 when it
was not: a truncated dump, a file whose read raised something other than a documented refusal, a
damaged expectation, or a missing dump block. Parity itself is a measurement, not a gate - a gap is
a unit of work for the next feature PR, and failing a build on it would only hide the measurement.
"""

from __future__ import annotations

import argparse
import base64
import json
import shlex
import subprocess
import sys
import zlib
from pathlib import Path
from typing import Any

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
if str(HERE) not in sys.path:
    sys.path.insert(0, str(HERE))

import compare as compare_mod  # noqa: E402
import corpus as corpus_mod  # noqa: E402
import dump_format  # noqa: E402
import exiv2_print  # noqa: E402

CHUNK_LIMIT = 10
CHUNK_SIZE = 4000
CHUNK_TITLE = "PARITY"
#: Notices one workflow step may leave. The summary is written first and must fit; the per-file
#: detail takes what is left and is trimmed to it.
STEP_NOTICE_LIMIT = 10
#: Characters of each value kept when the detail shows one differing pair per key.
SAMPLE_WIDTH = 48
#: Files per tool invocation. A batch is only a performance detail: an incomplete batch is retried
#: file by file, so one hostile input cannot take the measurement down.
DUMP_CHUNK_FILES = 40
#: An expectation file with this many key-looking lines that do not parse is damaged, not noisy.
UNPARSED_KEY_BUDGET = 4


class StructuralError(Exception):
    """The run cannot be trusted; nothing should be concluded from its numbers."""


def run_dump(dump_command: list[str], paths: list[Path]) -> dict[str, dict]:
    """Every file in ``paths``, dumped by as few invocations of our tool as will answer completely."""
    blocks: dict[str, dict] = {}
    for start in range(0, len(paths), DUMP_CHUNK_FILES):
        blocks.update(_run_batch(dump_command, list(paths[start : start + DUMP_CHUNK_FILES])))
    return blocks


def _run_batch(dump_command: list[str], batch: list[Path]) -> dict[str, dict]:
    listing = "".join(f"{path}\n" for path in batch)
    try:
        finished = subprocess.run(dump_command + ["--files-from", "-"], input=listing, capture_output=True,
                                 text=True, encoding="utf-8", errors="replace", timeout=1800, check=False)
    except FileNotFoundError as error:
        raise StructuralError(f"the dump command is not runnable: {error}") from error
    except subprocess.TimeoutExpired as error:
        raise StructuralError(f"the dump tool timed out on a batch of {len(batch)} file(s)") from error
    text = finished.stdout or ""
    if finished.returncode != 0 and not text.strip():
        raise StructuralError(f"the dump tool failed with {finished.returncode}: {(finished.stderr or '').strip()[:400]}")
    try:
        blocks = dump_format.parse_dump(text, source="dump")
    except dump_format.DumpFormatError as error:
        if len(batch) > 1:
            return _isolate(dump_command, batch)
        raise StructuralError(f"the dump output is malformed: {error}") from error
    if any(str(path) not in blocks for path in batch) and len(batch) > 1:
        return _isolate(dump_command, batch)
    return blocks


def _isolate(dump_command: list[str], batch: list[Path]) -> dict[str, dict]:
    recovered: dict[str, dict] = {}
    for path in batch:
        recovered.update(_run_batch(dump_command, [path]))
    return recovered


def run_reference(reference_command: list[str], path: Path) -> str:
    """The pinned reference tool's own print for one of our fixtures.

    Run on files this project wrote, and on a file of the pinned tree that the tree gives no
    expectation for. Its output is parsed in memory and never stored, so no expectation of ours -
    and none of theirs - ends up in the repository.
    """
    finished = subprocess.run(reference_command + [str(path)], capture_output=True, text=True,
                              encoding="utf-8", errors="replace", timeout=300, check=False)
    if finished.returncode != 0:
        detail = (finished.stderr or finished.stdout or "").strip().replace("\n", " ")[:400]
        raise StructuralError(f"the pinned reference failed on {path.name} with {finished.returncode}: {detail}")
    return finished.stdout or ""


def check_reference_shape(label: str, notes: list[str], problems: list[str]) -> None:
    """Flag expectation text that looks damaged rather than merely full of prose."""
    unparsed = [line for line in notes if compare_mod.kind(line) == "unparsed-key"]
    if len(unparsed) > UNPARSED_KEY_BUDGET:
        problems.append(f"{label}: {len(unparsed)} lines begin with a tag key but do not parse, "
                        f"first is {unparsed[0][:80]!r}")


def collect(candidates: list[corpus_mod.Candidate], reference_texts: dict[str, str],
            problems: list[str]) -> dict[str, tuple[dict[str, dict], list[str]]]:
    references: dict[str, tuple[dict[str, dict], list[str]]] = {}
    for candidate in candidates:
        if candidate.reference is not None:
            text = candidate.reference.read_text(encoding="utf-8", errors="replace")
        else:
            text = reference_texts.get(candidate.uid, "")
        tags, notes = exiv2_print.parse_reference(text)
        check_reference_shape(candidate.uid, notes, problems)
        references[candidate.uid] = (tags, notes)
    return references


def reference_formats(candidates: list[corpus_mod.Candidate]) -> dict[Path, str]:
    """The format a whole-file look at the bytes names, for the formats told by their end.

    The library tells a format by a signature, and a TARGA file has none at its start: it is told by
    the twenty-six bytes at its end. The corpus names such a file "targa" from its suffix, which is
    also what the reference does, so this only says the same of a file whose suffix it does not name.
    Only the format changes here; the reference output does not.
    """
    found: dict[Path, str] = {}
    for candidate in candidates:
        if candidate.path.suffix.lower() not in (".tga", ".targa"):
            continue
        try:
            tail = candidate.path.read_bytes()[-26:]
        except OSError:
            continue
        if len(tail) == 26 and tail[8:].startswith(b"TRUEVISION-XFILE."):
            found[candidate.path] = "targa"
    return found


#: The suffixes of the formats whose header states a size but which carry no metadata block. For
#: these the reference prints no key at all, and the size its summary prints is what is measured.
SIZE_ONLY_SUFFIXES = (".gif", ".bmp", ".tga", ".targa", ".pgf", ".eps")


def size_summaries(command: list[str] | None, candidates: list[corpus_mod.Candidate]) -> dict[str, tuple[str, str, str]]:
    """What the reference's own summary prints of the size of each file that has only a size.

    The summary option is a mode of its own (``exiv2 -ps``) and cannot be asked for in the same run as
    the key print, so the files of these formats are measured in a second pass. Its output is parsed in
    memory and never stored, like every other piece of the reference's output.
    """
    found: dict[str, tuple[str, str, str]] = {}
    if command is None:
        return found
    for candidate in candidates:
        if candidate.path.suffix.lower() not in SIZE_ONLY_SUFFIXES:
            continue
        parsed = exiv2_print.parse_summary(run_reference(command, candidate.path))
        if parsed is not None:
            found[candidate.uid] = parsed
    return found


def build(dump_command: list[str], candidates: list[corpus_mod.Candidate],
          references: dict[str, tuple[dict[str, dict], list[str]]],
          summaries: dict[str, tuple[str, str, str]], problems: list[str]) -> list[dict[str, Any]]:
    blocks = run_dump(dump_command, [candidate.path for candidate in candidates])
    named = reference_formats(candidates)
    files: list[dict[str, Any]] = []
    for candidate in candidates:
        block = blocks.get(str(candidate.path))
        if block is None:
            problems.append(f"{candidate.uid}: the dump tool printed no record for {candidate.path}")
            continue
        tags, notes = references[candidate.uid]
        if block["status"] == "error":
            problems.append(f"{candidate.uid}: our reader raised instead of refusing"
                            f" ({block['detail'] or 'the tool reported no exception type'})")
        files.append(compare_mod.compare_file(candidate.uid, candidate.corpus, named.get(candidate.path, candidate.format),
                                              block["status"], block["detail"], tags, block["tags"], notes,
                                              block.get("interpreted"), block.get("size"),
                                              summaries.get(candidate.uid)))
    return files


def _percent(numerator: int, denominator: int) -> str:
    return "n/a" if not denominator else f"{100.0 * numerator / denominator:.1f}%"


def format_table(report: dict[str, Any]) -> list[str]:
    header = f"{'format':<12} {'files':>6} {'read':>6} {'skipped':>8} {'keys':>7} {'found':>7} {'values':>8} {'keys ok':>8}"
    lines = [header, "-" * len(header)]
    for name, counts in report["by_format"].items():
        expected = sum(counts.get(outcome, 0) for outcome in
                       (compare_mod.MATCH_VALUE, compare_mod.MATCH_KEY, compare_mod.VALUE_DIFF, compare_mod.MISSING_KEY))
        found = counts.get(compare_mod.MATCH_VALUE, 0) + counts.get(compare_mod.MATCH_KEY, 0)
        agreed = counts.get(compare_mod.MATCH_VALUE, 0)
        comparable = agreed + counts.get(compare_mod.VALUE_DIFF, 0)
        read = counts.get("files", 0) - counts.get("excluded", 0)
        lines.append(f"{name:<12} {counts.get('files', 0):>6} {read:>6} {counts.get('excluded', 0):>8} "
                     f"{expected:>7} {found:>7} {_percent(agreed, comparable):>8} {_percent(found, expected):>8}")
    return lines


def interpreted_by_kind(report: dict[str, Any]) -> dict[str, dict[str, int]]:
    """The interpreted column's own per-kind counters; empty for a report made before it existed."""
    return report.get("interpreted", {}).get("by_kind", {})


def kind_table(report: dict[str, Any]) -> list[str]:
    """One row per kind of metadata: the raw comparison, then the interpreted one beside it.

    The two halves count different things and are never added. The raw half is the per-family table
    folded; the interpreted half is its own tally, over the keys both sides print for which our
    record offers an interpreted field. ``both`` are the values both sides interpret, the only ones
    whose texts are compared and the only ones in the rate. ``not int.`` are the values we return no
    interpretation for, and ``ref. has`` the part of them the reference translates; ``ref. none``
    are the values we interpret and the reference only repeats.
    """
    raw = by_kind(report["by_family"])
    interpreted = interpreted_by_kind(report)
    lines = [f"{'kind':<6} {'keys':>7} {'found':>7} {'values':>8} | {'both':>8} {'agree':>7} {'rate':>8}"
             f" {'not int.':>9} {'ref. has':>9} {'ref. none':>10} {'not offered':>12}"]
    for name in sorted(set(raw) | set(interpreted)):
        counts = raw.get(name, {})
        found = sum(counts.get(outcome, 0) for outcome in
                    (compare_mod.MATCH_VALUE, compare_mod.MATCH_KEY, compare_mod.VALUE_DIFF))
        expected = found + counts.get(compare_mod.MISSING_KEY, 0)
        agreed = counts.get(compare_mod.MATCH_VALUE, 0)
        comparable = agreed + counts.get(compare_mod.VALUE_DIFF, 0)
        ours = interpreted.get(name, {})
        same = ours.get(compare_mod.MATCH_INTERPRETED, 0)
        compared = same + ours.get(compare_mod.INTERPRETED_DIFF, 0)
        lines.append(f"{name:<6} {expected:>7} {found:>7} {_percent(agreed, comparable):>8} | "
                     f"{compared:>8} {same:>7} {_percent(same, compared):>8}"
                     f" {ours.get(compare_mod.NOT_INTERPRETED, 0):>9}"
                     f" {ours.get(compare_mod.NOT_INTERPRETED_TRANSLATED, 0):>9}"
                     f" {ours.get(compare_mod.REFERENCE_NOT_INTERPRETED, 0):>10}"
                     f" {ours.get(compare_mod.NOT_OFFERED, 0):>12}")
    return lines


def interpreted_headline(report: dict[str, Any]) -> str:
    """The interpreted rate, over the values both sides interpret, with what we do not interpret beside it."""
    section = report.get("interpreted", {})
    same, compared = section.get("agreed", 0), section.get("compared", 0)
    return (f"interpreted:  {same}/{compared} = {_percent(same, compared)} of the values both sides interpret;"
            f" not interpreted: {section.get('not_interpreted', 0)}"
            f" (the reference translates {section.get('not_interpreted_translated', 0)} of them)")


def interpreted_diff_table(entries: list[dict[str, Any]], limit: int) -> list[str]:
    if not entries:
        return ["  (none)"]
    lines = [f"  {'key':<44} {'tags':>6}  most frequent pair: ours | reference"]
    for entry in entries[:limit]:
        lines.append(f"  {entry['key']:<44} {entry['tags']:>6}  {entry['ours'] or '(none)'} | {entry['theirs']}")
    if len(entries) > limit:
        lines.append(f"  ... {len(entries) - limit} further keys are listed in parity-report.json")
    return lines


def not_interpreted_table(entries: list[dict[str, Any]], limit: int) -> list[str]:
    if not entries:
        return ["  (none)"]
    lines = [f"  {'key':<44} {'tags':>6}  most frequent reference text"]
    for entry in entries[:limit]:
        lines.append(f"  {entry['key']:<44} {entry['tags']:>6}  {entry['theirs']}")
    if len(entries) > limit:
        lines.append(f"  ... {len(entries) - limit} further keys are listed in parity-report.json")
    return lines


def gap_table(entries: list[dict[str, Any]], limit: int) -> list[str]:
    if not entries:
        return ["  (none)"]
    lines = [f"  {'key':<44} {'files':>6} {'tags':>6}  class"]
    for entry in entries[:limit]:
        note = f"  {entry['note']}" if entry.get("note") else ""
        lines.append(f"  {entry['key']:<44} {entry['files']:>6} {entry['tags']:>6}  {entry['class']}{note}")
    if len(entries) > limit:
        lines.append(f"  ... {len(entries) - limit} further keys are listed in parity-report.json")
    return lines


def size_headline(report: dict[str, Any]) -> str:
    """The size line of the formats the reference prints no key of, apart from the key rates."""
    sizes = report.get("sizes", {})
    outcomes = sizes.get("outcomes", {})
    if not outcomes:
        return "size parity:  none measured (no file of a format without a metadata block)"
    total = sum(outcomes.get(name, 0) for name in compare_mod.SIZE_OUTCOMES)
    same = outcomes.get(compare_mod.SIZE_MATCH_VALUE, 0)
    return (f"size parity:  {same}/{total} = {_percent(same, total)} of the sizes the reference prints"
            + "".join(f", {name}={outcomes.get(name, 0)}" for name in compare_mod.SIZE_OUTCOMES[1:]
                      if outcomes.get(name, 0)))


def text_report(report: dict[str, Any], problems: list[str]) -> str:
    parity = report["parity"]
    outcomes = report["outcomes"]
    lines = [
        "exiv2 differential parity",
        "=========================",
        f"files: {report['files']['total']} attempted, {report['files']['attempted']} read"
        f" ({', '.join(f'{name}={count}' for name, count in sorted(report['files']['statuses'].items()))})",
        f"key parity:   {parity['found_keys']}/{parity['expected_keys']} = "
        f"{_percent(parity['found_keys'], parity['expected_keys'])} of the reference's keys",
        f"value parity: {parity['agreed_values']}/{parity['comparable_values']} = "
        f"{_percent(parity['agreed_values'], parity['comparable_values'])} of comparable values",
        interpreted_headline(report),
        "",
        "outcomes: " + ", ".join(f"{name}={outcomes.get(name, 0)}" for name in compare_mod.OUTCOMES),
        size_headline(report),
        "advisory: " + (", ".join(f"{name}={count}" for name, count in sorted(report["advisory"].items())) or "none"),
        "reference non-tag lines: "
        + (", ".join(f"{name}={count}" for name, count in sorted(report["reference_notes"].items())) or "none")
        + (f", repeated reference rows: {report['reference_repeats']}" if report.get("reference_repeats") else ""),
        "",
        "by format:",
        *format_table(report),
        "",
        "by kind (raw values | interpreted values, compared apart and never added to the raw side):",
        *kind_table(report),
        "interpreted outcomes: "
        + ", ".join(f"{name}={report.get('interpreted', {}).get('outcomes', {}).get(name, 0)}"
                    for name in compare_mod.INTERPRETED_OUTCOMES),
        "",
        "most frequent interpreted differences (both sides interpret the value and word it differently):",
        *interpreted_diff_table(report.get("interpreted", {}).get("diffs", []), 25),
        "",
        "most frequent values we do not interpret and the reference translates:",
        *not_interpreted_table(report.get("interpreted", {}).get("not_interpreted_keys", []), 25),
        "",
        "most frequent missing keys (the reference printed one we did not):",
        *gap_table(report["gaps"]["missing_keys"], 15),
        "",
        "keys behind files we did not read:",
        *gap_table(report["gaps"]["unread_keys"], 8),
        "",
        "most frequent extra keys (we printed one the reference did not):",
        *gap_table(report["gaps"]["extra_keys"], 8),
        "",
    ]
    if report["files"]["partial"]:
        lines.append("files read with one kind of metadata refused:")
        lines.extend(f"  {name}: {count}" for name, count in sorted(report["files"]["partial"].items(), key=lambda pair: -pair[1]))
    if report["files"]["refusals"]:
        lines.append("our refusals, by status and code:")
        lines.extend(f"  {name}: {count}" for name, count in sorted(report["files"]["refusals"].items(), key=lambda pair: -pair[1]))
        lines.append("")
    corpora = report["corpora"]
    if corpora["exiv2"]["files"]:
        lines.append(f"corpus exiv2: {corpora['exiv2']['files']} file(s) from {corpora['exiv2']['tree']}")
        if corpora["exiv2"]["unmatched_names"]:
            lines.append(f"  {corpora['exiv2']['unmatched_expectations']} expectation(s) name no input in the tree"
                         f" (e.g. {', '.join(corpora['exiv2']['unmatched_names'][:6])})")
    if corpora["generated"]["files"]:
        lines.append(f"corpus generated: {corpora['generated']['files']} file(s) from "
                     f"{', '.join(corpora['generated']['directories'])}")
    lines.append("")
    if problems:
        lines.append(f"structural problems ({len(problems)}):")
        lines.extend(f"  {problem}" for problem in problems[:20])
        if len(problems) > 20:
            lines.append(f"  ... {len(problems) - 20} more")
    else:
        lines.append("structural problems: none")
    return "\n".join(lines) + "\n"


def by_kind(by_family: dict[str, dict[str, int]]) -> dict[str, dict[str, int]]:
    """The per-family outcome table folded on the first segment of the key (Exif, Iptc, Xmp).

    The full table has a row per group and stays in the report; three rows are what the annotation
    channel can carry, and they are what a change to one kind of metadata is measured by."""
    folded: dict[str, dict[str, int]] = {}
    for name, counts in by_family.items():
        bucket = folded.setdefault(name.split(".")[0], {})
        for outcome, count in counts.items():
            bucket[outcome] = bucket.get(outcome, 0) + count
    return {name: folded[name] for name in sorted(folded)}


def summary(report: dict[str, Any], problems: list[str]) -> dict[str, Any]:
    """The part of the report small enough for the annotation channel, which is the only one that can
    be read back from this environment."""
    return {
        "files": report["files"],
        "parity": report["parity"],
        "outcomes": report["outcomes"],
        "advisory": report["advisory"],
        "by_format": report["by_format"],
        "by_kind": by_kind(report["by_family"]),
        # Per format for the two small kinds; EXIF per format is by_format less these.
        "by_kind_format": {name: formats for name, formats in report["by_kind_format"].items() if name != "Exif"},
        # The interpreted column, apart from everything above: its outcomes, the rate over the values
        # both sides interpret with the count of those we do not interpret beside it, the per-kind
        # counters the text report prints beside the raw ones, the groups one by one, and the keys
        # that differ or go uninterpreted most often with a sample.
        "sizes": report.get("sizes", {}),
        "interpreted": {
            "outcomes": report.get("interpreted", {}).get("outcomes", {}),
            "compared": report.get("interpreted", {}).get("compared", 0),
            "agreed": report.get("interpreted", {}).get("agreed", 0),
            "rate": report.get("interpreted", {}).get("rate"),
            "not_interpreted": report.get("interpreted", {}).get("not_interpreted", 0),
            "not_interpreted_translated": report.get("interpreted", {}).get("not_interpreted_translated", 0),
            "by_kind": interpreted_by_kind(report),
            "by_family": report.get("interpreted", {}).get("by_family", {}),
            "diff_top": report.get("interpreted", {}).get("diffs", [])[:40],
            "not_interpreted_top": report.get("interpreted", {}).get("not_interpreted_keys", [])[:40],
        },
        "missing_top": report["gaps"]["missing_keys"][:30],
        "unread_top": report["gaps"]["unread_keys"][:12],
        "extra_top": report["gaps"]["extra_keys"][:12],
        "corpora": report["corpora"],
        "problems": problems[:10],
        "problem_count": len(problems),
    }


def detail(files: list[dict[str, Any]]) -> dict[str, Any]:
    """What the summary folds away, for the same channel: which file, and which key.

    One entry per file that is not a complete agreement - its status, the reader's reason, and how
    many of its keys per group are missing, different or unread - and every missing or differing key
    with its count; a differing key also carries the two values of its first occurrence, ours then
    theirs, cut to SAMPLE_WIDTH characters. Nothing here is a new measurement: it is the per-file rows, counted. It has to be
    taken before the rows are bounded for the JSON report.
    """
    listed: dict[str, dict[str, Any]] = {}
    missing: dict[str, int] = {}
    differing: dict[str, int] = {}
    samples: dict[str, list[str]] = {}
    for entry in files:
        groups: dict[str, dict[str, int]] = {}
        extra = 0
        for r in entry["rows"]:
            outcome = r["outcome"]
            if outcome == compare_mod.EXTRA_KEY:
                extra += 1
            elif outcome in (compare_mod.MISSING_KEY, compare_mod.VALUE_DIFF, compare_mod.NOT_READ):
                bucket = groups.setdefault(outcome, {})
                group = compare_mod.family(r["key"])
                bucket[group] = bucket.get(group, 0) + 1
                if outcome == compare_mod.MISSING_KEY:
                    missing[r["key"]] = missing.get(r["key"], 0) + 1
                elif outcome == compare_mod.VALUE_DIFF:
                    differing[r["key"]] = differing.get(r["key"], 0) + 1
                    samples.setdefault(r["key"], [r.get("our_value", "")[:SAMPLE_WIDTH], r.get("their_value", "")[:SAMPLE_WIDTH]])
        # A size that differs is a disagreement about a fact, and it is named with the keys. The two
        # sides agreeing that a file states no size is not a disagreement and is not named.
        if "size" in entry and entry["size"]["outcome"] not in (compare_mod.SIZE_MATCH_VALUE,
                                                                 compare_mod.SIZE_NONE_BOTH):
            groups.setdefault(entry["size"]["outcome"], {})["Size"] = 1
        if entry["status"] == "ok" and not entry["detail"] and not groups:
            continue
        row: dict[str, Any] = {"format": entry["format"], "status": entry["status"]}
        if entry["detail"]:
            row["detail"] = entry["detail"]
        if extra:
            row["extra"] = extra
        row.update(groups)
        listed[entry["label"]] = row
    return {"files": listed, "missing": missing, "differing": differing, "samples": samples, "trimmed": 0}


def encode(payload: dict[str, Any]) -> str:
    return base64.b64encode(zlib.compress(json.dumps(payload, separators=(",", ":"), sort_keys=True).encode(), 9)).decode()


def fit_detail(payload: dict[str, Any], notices: int) -> dict[str, Any] | None:
    """The detail, cut until it fits ``notices`` annotations: the samples go first, all of them, then
    the rarest missing keys.

    ``trimmed`` says how many keys went, so a reader of the annotations knows the table is not the
    whole of it; the per-file counts are never cut. ``None`` when not even those fit.
    """
    budget = notices * CHUNK_SIZE
    if budget <= 0:
        return None
    if len(encode(payload)) <= budget:
        return payload
    payload = dict(payload, samples={})
    if len(encode(payload)) <= budget:
        return payload
    ranked = sorted(payload["missing"].items(), key=lambda pair: (-pair[1], pair[0]))
    low, high = 0, len(ranked)
    # The encoded size grows with the number of keys kept, so the largest table that fits is found
    # by bisection rather than by encoding once per key.
    while low < high:
        middle = (low + high + 1) // 2
        trial = dict(payload, missing=dict(ranked[:middle]), trimmed=len(ranked) - middle)
        if len(encode(trial)) <= budget:
            low = middle
        else:
            high = middle - 1
    fitted = dict(payload, missing=dict(ranked[:low]), trimmed=len(ranked) - low)
    return fitted if len(encode(fitted)) <= budget else None


def emit_annotations(payload: dict[str, Any], title: str = CHUNK_TITLE, word: str = "summary") -> int:
    """Write ``payload`` as numbered notices and return how many it took."""
    encoded = encode(payload)
    parts = [encoded[i : i + CHUNK_SIZE] for i in range(0, len(encoded), CHUNK_SIZE)]
    if not parts:
        raise StructuralError(f"the {word} is empty; there would be nothing to recover")
    if len(parts) > CHUNK_LIMIT:
        raise StructuralError(f"the {word} needs {len(parts)} annotations; the recovery limit is {CHUNK_LIMIT}")
    for number, part in enumerate(parts, 1):
        print(f"::notice title={title} {word} {number}/{len(parts)}::{part}")
    return len(parts)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Compare our metadata readers with exiv2's own expectations.")
    parser.add_argument("--tree", type=Path, help="root of a pinned exiv2 checkout, for the corpus it carries")
    parser.add_argument("--fixtures", type=Path, action="append", default=[],
                        help="a directory of fixtures this project owns; repeatable")
    parser.add_argument("--dump-command", required=True,
                        help="how to run MetadataSharp.Dump, split POSIX-style on spaces; e.g. "
                             "\"dotnet run --project tools/MetadataSharp.Dump -c Release -f net10.0 --\" (on Windows "
                             "write paths with forward slashes, which Python and dotnet both accept)")
    parser.add_argument("--exiv2-command", help="the pinned reference tool, split the same way; used as the "
                                                "oracle for --fixtures files, and its output is never stored")
    parser.add_argument("--summary-command", help="the same tool with its summary option (exiv2 -ps), split the same "
                                                  "way; the size of a file that carries no metadata block is compared "
                                                  "against what it prints of it")
    parser.add_argument("--classifications", type=Path,
                        help="JSON object mapping a key to {\"class\", \"note\"} for gaps this project has investigated")
    parser.add_argument("--out", type=Path, default=ROOT / "artifacts/parity", help="report directory")
    parser.add_argument("--rows-per-file", type=int, default=40, help="per-file rows kept in the JSON report")
    parser.add_argument("--annotate", action="store_true", help="emit the summary as chunked workflow annotations")
    parser.add_argument("--title", default=CHUNK_TITLE, help="annotation title, so two corpora in one job stay distinguishable")
    args = parser.parse_args(argv)

    if args.tree is None and not args.fixtures:
        parser.error("nothing to compare: pass --tree and/or --fixtures")

    candidates: list[corpus_mod.Candidate] = []
    unmatched: list[str] = []
    if args.tree is not None:
        candidates, unmatched = corpus_mod.discover_exiv2(args.tree)
    reference_texts: dict[str, str] = {}
    generated: list[corpus_mod.Candidate] = []
    if args.fixtures:
        generated = corpus_mod.discover_generated(*args.fixtures)
        if args.exiv2_command:
            oracle = shlex.split(args.exiv2_command)
            for candidate in generated:
                reference_texts[candidate.uid] = run_reference(oracle, candidate.path)
        elif generated:
            raise StructuralError("the generated corpus has no oracle: --fixtures needs --exiv2-command")
        candidates.extend(generated if args.exiv2_command else [])
    if not candidates:
        raise StructuralError("no candidate file was found in the corpora")

    problems: list[str] = []
    references = collect(candidates, reference_texts, problems)
    summaries = size_summaries(shlex.split(args.summary_command) if args.summary_command else None, candidates)
    files = build(shlex.split(args.dump_command), candidates, references, summaries, problems)

    classifications: dict[str, Any] = {}
    if args.classifications is not None:
        loaded = json.loads(args.classifications.read_text(encoding="utf-8"))
        if not isinstance(loaded, dict):
            raise StructuralError("--classifications must hold a JSON object keyed by tag key")
        classifications = loaded
    # The totals are counted over every row; only the per-file detail is bounded, so a small
    # ``--rows-per-file`` keeps the artifact readable without moving a single number.
    report = compare_mod.aggregate(files, classifications)
    per_file_detail = detail(files)
    for entry in files:
        keep = entry["rows"][: max(0, args.rows_per_file)]
        entry["rows_dropped"] = max(0, len(entry["rows"]) - len(keep))
        entry["rows"] = keep
    report["per_file"] = files
    report["corpora"] = {
        "exiv2": {
            "tree": str(args.tree) if args.tree else None,
            "files": sum(1 for entry in files if entry["corpus"] == "exiv2"),
            "unmatched_expectations": len(unmatched),
            "unmatched_names": unmatched[:40],
        },
        "generated": {
            "directories": [str(path) for path in args.fixtures],
            "files": sum(1 for entry in files if entry["corpus"] == "generated"),
            "oracle": args.exiv2_command or None,
        },
    }

    args.out.mkdir(parents=True, exist_ok=True)
    (args.out / "parity-report.json").write_text(json.dumps(report, indent=1, sort_keys=True) + "\n", encoding="utf-8")
    text = text_report(report, problems)
    (args.out / "parity-report.txt").write_text(text, encoding="utf-8")
    print(text, end="")
    if args.annotate:
        used = emit_annotations(summary(report, problems), args.title)
        fitted = fit_detail(per_file_detail, min(CHUNK_LIMIT, STEP_NOTICE_LIMIT - used))
        if fitted is not None:
            emit_annotations(fitted, args.title, "detail")
    return 1 if problems else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (StructuralError, corpus_mod.CorpusError) as failure:
        print(f"::error title={CHUNK_TITLE} harness::{failure}", file=sys.stderr)
        print(f"structural failure: {failure}", file=sys.stderr)
        sys.exit(1)
