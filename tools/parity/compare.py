"""Comparison of the reference print format against our dump grammar.

Everything here is pure string work on already-parsed inputs, which is what makes the parity rules
testable without a build or a corpus. The outcome vocabulary is deliberately small:

``match-value``
    The key is present on both sides and the value columns agree after normalization.
``match-key``
    The key is present on both sides but the reference printed a decoded label where we print raw
    bytes, so only the key can be compared. This is a gap in our naming, not a disagreement.
``value-diff``
    The key is present on both sides, the value columns are comparable, and they disagree.
``missing-key``
    The reference printed a tag we did not.
``extra-key``
    We printed a tag the reference did not. Reported, never counted as a failure: a file can carry
    metadata the reference tool's print options do not select.

The interpreted (human-readable) value is a second question about a row that already has one of the
outcomes above, and it is answered apart: the row gains one of four verdicts under its own field,
they are tallied in their own table, and nothing they say reaches the raw outcomes, the key rate or
the value rate. A row is asked the question only when both sides print the key, our record offers an
interpreted field, and the reference's two columns can be separated.

A side interprets a value when it has something to say beyond the raw value: we, when our field is
not empty; the reference, when its interpreted column differs from its raw one (it repeats the raw
value where it has no translation). That gives four cases:

``match-interpreted`` / ``interpreted-diff``
    Both sides interpret the value, and the two texts are the same or differ. Only these two are
    compared, and the interpreted rate is taken over them alone.
``not-interpreted``
    We return no interpretation. This is a gap, not a disagreement; ``translated`` on the row says
    whether the reference has a translation we lack.
``reference-not-interpreted``
    We return an interpretation and the reference only repeats its raw value, so there is nothing to
    compare ours with.
"""

from __future__ import annotations

import re
from collections import Counter, defaultdict
from typing import Any

MATCH_VALUE = "match-value"
MATCH_KEY = "match-key"
VALUE_DIFF = "value-diff"
MISSING_KEY = "missing-key"
EXTRA_KEY = "extra-key"
#: The reference printed tags for a file we did not read, so the keys were never even compared.
NOT_READ = "not-read"
OUTCOMES = (MATCH_VALUE, MATCH_KEY, VALUE_DIFF, MISSING_KEY, EXTRA_KEY, NOT_READ)

#: Both sides interpret the value and say the same.
MATCH_INTERPRETED = "match-interpreted"
#: Both sides interpret the value and say different things.
INTERPRETED_DIFF = "interpreted-diff"
#: We return no interpretation for the value, whatever the reference says.
NOT_INTERPRETED = "not-interpreted"
#: We return an interpretation and the reference only repeats its raw value.
REFERENCE_NOT_INTERPRETED = "reference-not-interpreted"
#: Deliberately not part of OUTCOMES: every sum over that tuple is a statement about raw values.
INTERPRETED_OUTCOMES = (MATCH_INTERPRETED, INTERPRETED_DIFF, NOT_INTERPRETED, REFERENCE_NOT_INTERPRETED)
#: The two verdicts that compare one text with another; the interpreted rate is over these alone.
INTERPRETED_COMPARED = (MATCH_INTERPRETED, INTERPRETED_DIFF)
#: A counter beside ``not-interpreted``: the rows of it for which the reference has a translation.
NOT_INTERPRETED_TRANSLATED = "not-interpreted-translated"
#: Why a key both sides print was not asked about its interpretation.
NOT_OFFERED = "not-offered"
NO_REFERENCE_COLUMN = "no-reference-column"
INTERPRETED_SAMPLE_WIDTH = 60

#: A value column may be followed by exiv2's decoded label, which starts after a wider gap.
COLUMN_GAP = re.compile(r"\s{2,}")
QUOTED = re.compile(r'"([^"]*)"')
VALUE_SAMPLE_WIDTH = 160


def normalize(value: str) -> str:
    return re.sub(r"\s+", " ", value).strip()


def columns(value: str) -> list[str]:
    return [part.strip() for part in COLUMN_GAP.split(value) if part.strip() != ""]


def split_list(value: str) -> list[str] | None:
    """The items of a comma-separated list of quoted strings, or ``None`` if it is not one.

    An XMP array prints as ``"a", "b"``, sometimes followed by the label column, so the quoted items
    have to be recovered before the value can be compared with our joined rendering.
    """
    if value.count('"') < 2:
        return None
    items = QUOTED.findall(value)
    if not items:
        return None
    return items


def values_agree(ours: str, theirs: str) -> bool:
    if ours == theirs:
        return True
    if normalize(ours) == normalize(theirs):
        return True
    for column in columns(theirs):
        if normalize(ours) == normalize(column):
            return True
    items = split_list(theirs)
    if items is not None and normalize(ours) == ", ".join(normalize(item) for item in items):
        return True
    return False


def row(key: str, outcome: str, *, ours: tuple[str, int, str] | None, theirs: dict | None,
        flags: tuple[str, ...] = ()) -> dict[str, Any]:
    entry: dict[str, Any] = {"key": key, "outcome": outcome}
    if theirs is not None:
        entry["their_type"] = theirs["type"]
        entry["their_count"] = theirs["count"]
        entry["their_value"] = theirs["value"][:VALUE_SAMPLE_WIDTH]
    if ours is not None:
        entry["our_type"], entry["our_count"], entry["our_value"] = ours[0], ours[1], ours[2][:VALUE_SAMPLE_WIDTH]
    if flags:
        entry["flags"] = list(flags)
    return entry


def judge_interpreted(entry: dict[str, Any], theirs: dict, ours: str | None) -> None:
    """Add the interpreted verdict to a row for a key both sides print. The row's outcome is not read.

    The reference prints its raw value again where it has no translation; ``translated`` records
    whether it has one. An empty field of ours is "no interpretation" and is never compared: it is
    counted as such, with or without a translation on the other side. Text is compared only where
    both sides have some, as printed, less the surrounding blanks a line format cannot keep.
    """
    if ours is None:
        entry["interpreted_skipped"] = NOT_OFFERED
        return
    their_text = theirs.get("interpreted")
    their_raw = theirs.get("raw")
    if their_text is None or their_raw is None:
        entry["interpreted_skipped"] = NO_REFERENCE_COLUMN
        return
    translated = their_raw.strip() != their_text.strip()
    if ours.strip() == "":
        entry["interpreted"] = NOT_INTERPRETED
    elif not translated:
        entry["interpreted"] = REFERENCE_NOT_INTERPRETED
    else:
        entry["interpreted"] = MATCH_INTERPRETED if ours.strip() == their_text.strip() else INTERPRETED_DIFF
    entry["translated"] = translated
    entry["our_interpreted"] = ours[:INTERPRETED_SAMPLE_WIDTH]
    entry["their_interpreted"] = their_text[:INTERPRETED_SAMPLE_WIDTH]


def compare_tags(reference: dict[str, dict], dumped: dict[str, tuple[str, int, str]],
                 interpreted: dict[str, str] | None = None) -> list[dict[str, Any]]:
    """Rows for every key on either side, in reference order first.

    ``interpreted`` maps a key to the interpreted value our record offered for it. It only ever adds
    fields to a row whose raw outcome has already been decided.
    """
    interpreted = interpreted or {}
    rows: list[dict[str, Any]] = []
    for key, theirs in reference.items():
        ours = dumped.get(key)
        if ours is None:
            rows.append(row(key, MISSING_KEY, ours=None, theirs=theirs))
            continue
        flags: list[str] = []
        if normalize(ours[0]) != theirs["type"]:
            flags.append("type")
        if ours[1] != theirs["count"]:
            flags.append("count")
        if theirs["labelled"]:
            outcome = MATCH_VALUE if values_agree(ours[2], theirs["value"]) else MATCH_KEY
        elif values_agree(ours[2], theirs["value"]):
            outcome = MATCH_VALUE
        elif theirs["count"] == 0 and ours[2] == "":
            outcome = MATCH_VALUE
        else:
            outcome = VALUE_DIFF
        entry = row(key, outcome, ours=ours, theirs=theirs, flags=tuple(flags))
        judge_interpreted(entry, theirs, interpreted.get(key))
        rows.append(entry)
    for key in dumped:
        if key not in reference:
            rows.append(row(key, EXTRA_KEY, ours=dumped[key], theirs=None))
    return rows


def compare_file(label: str, corpus: str, format_name: str, status: str, detail: str,
                 reference: dict[str, dict], dumped: dict[str, tuple[str, int, str]],
                 notes: list[str], interpreted: dict[str, str] | None = None) -> dict[str, Any]:
    """One file's verdict. Only files we attempted contribute rows to parity.

    A refusal is a different kind of answer, not a wrong one: the library refusing a file the
    reference tool could not read either is the expected outcome, so those files are counted apart.
    """
    counted = status == "ok"
    rows = compare_tags(reference, dumped, interpreted) if counted else [
        row(key, NOT_READ, ours=None, theirs=theirs) for key, theirs in reference.items()
    ]
    return {
        "label": label,
        "corpus": corpus,
        "format": format_name,
        "status": status,
        "detail": detail,
        "attempted": counted,
        "reference_tags": len(reference),
        "our_tags": len(dumped),
        "note_kinds": dict(Counter(kind(line) for line in notes)),
        "reference_repeats": sum(entry.get("repeats", 0) for entry in reference.values()),
        "rows": rows,
    }


def kind(line: str) -> str:
    if line.startswith(("Warning:", "Error:", "Permission:", "Notice:")):
        return "diagnostic"
    if line.startswith("<") or line.startswith("<?x"):
        return "packet"
    if re.match(r"^0x[0-9a-f]{4} ", line):
        return "hexdump"
    if line.startswith(("Exif.", "Iptc.", "Xmp.", "ICC.")):
        return "unparsed-key"
    return "other"


def family(key: str) -> str:
    parts = key.split(".")
    return parts[0] + "." + parts[1] if len(parts) > 1 else parts[0]


def aggregate(files: list[dict[str, Any]], classifications: dict[str, dict] | None = None) -> dict[str, Any]:
    """Totals, per-format and per-family roll-ups, and the recurring gaps.

    ``classifications`` is an optional sidecar of keys this project has investigated by hand. A gap
    stays ``unclassified`` until someone has read the bytes, which keeps the report from implying a
    diagnosis it has not earned.
    """
    classifications = classifications or {}
    outcomes: Counter[str] = Counter()
    statuses: Counter[str] = Counter()
    refusals: Counter[str] = Counter()
    partial: Counter[str] = Counter()
    advisory: Counter[str] = Counter()
    notes: Counter[str] = Counter()
    by_format: dict[str, Counter[str]] = defaultdict(Counter)
    by_family: dict[str, Counter[str]] = defaultdict(Counter)
    by_kind_format: dict[str, dict[str, Counter[str]]] = defaultdict(lambda: defaultdict(Counter))
    missing_files: Counter[str] = Counter()
    missing_tags: Counter[str] = Counter()
    unread_files: Counter[str] = Counter()
    unread_tags: Counter[str] = Counter()
    extra_files: Counter[str] = Counter()
    extra_tags: Counter[str] = Counter()
    # The interpreted column has its own counters from here to the end of the report.
    interpreted_outcomes: Counter[str] = Counter()
    interpreted_by_kind: dict[str, Counter[str]] = defaultdict(Counter)
    interpreted_by_family: dict[str, Counter[str]] = defaultdict(Counter)
    interpreted_diffs: Counter[str] = Counter()
    interpreted_pairs: dict[str, Counter[tuple[str, str]]] = defaultdict(Counter)
    gaps_translated = 0
    untranslated: Counter[str] = Counter()
    untranslated_texts: dict[str, Counter[str]] = defaultdict(Counter)

    for entry in files:
        statuses[entry["status"]] += 1
        if entry["status"] != "ok" and entry["detail"]:
            refusals[f"{entry['status']}:{entry['detail']}"] += 1
        # A file that was read, with one kind of its metadata refused: its rows count, and the
        # refusal is the reason some of them are missing.
        if entry["status"] == "ok" and entry["detail"]:
            partial[entry["detail"]] += 1
        for name, count in entry["note_kinds"].items():
            notes[name] += count
        bucket = by_format[entry["format"]]
        bucket["files"] += 1
        if not entry["attempted"]:
            bucket["excluded"] += 1
            bucket["excluded_tags"] += entry["reference_tags"]
        for r in entry["rows"]:
            outcomes[r["outcome"]] += 1
            bucket[r["outcome"]] += 1
            by_family[family(r["key"])][r["outcome"]] += 1
            by_kind_format[r["key"].split(".")[0]][entry["format"]][r["outcome"]] += 1
            for flag in r.get("flags", ()):
                advisory[flag] += 1
            if r["outcome"] == MISSING_KEY:
                missing_files[r["key"]] += 1
                missing_tags[r["key"]] += 1
            elif r["outcome"] == NOT_READ:
                unread_files[r["key"]] += 1
                unread_tags[r["key"]] += 1
            elif r["outcome"] == EXTRA_KEY:
                extra_files[r["key"]] += 1
                extra_tags[r["key"]] += 1
            kind_name = r["key"].split(".")[0]
            if "interpreted_skipped" in r:
                interpreted_by_kind[kind_name][r["interpreted_skipped"]] += 1
            verdict = r.get("interpreted")
            if verdict is None:
                continue
            interpreted_outcomes[verdict] += 1
            gap = verdict == NOT_INTERPRETED and r["translated"]
            if gap:
                gaps_translated += 1
            for bucket_name, table in ((kind_name, interpreted_by_kind), (family(r["key"]), interpreted_by_family)):
                table[bucket_name][verdict] += 1
                if gap:
                    table[bucket_name][NOT_INTERPRETED_TRANSLATED] += 1
            if verdict == INTERPRETED_DIFF:
                interpreted_diffs[r["key"]] += 1
                interpreted_pairs[r["key"]][(r["our_interpreted"], r["their_interpreted"])] += 1
            elif gap:
                untranslated[r["key"]] += 1
                untranslated_texts[r["key"]][r["their_interpreted"]] += 1

    attempted = sum(1 for entry in files if entry["attempted"])
    expected = outcomes[MATCH_VALUE] + outcomes[MATCH_KEY] + outcomes[VALUE_DIFF]
    comparable = outcomes[MATCH_VALUE] + outcomes[VALUE_DIFF]
    compared_texts = sum(interpreted_outcomes[name] for name in INTERPRETED_COMPARED)

    def rate(numerator: int, denominator: int) -> float | None:
        return round(numerator / denominator, 6) if denominator else None

    def gaps(counter_files: Counter[str], counter_tags: Counter[str], only: str) -> list[dict[str, Any]]:
        listed = []
        for key, files_with in counter_files.most_common():
            verdict = classifications.get(key, {})
            listed.append({
                "key": key,
                "files": files_with,
                "tags": counter_tags[key],
                "class": verdict.get("class", "unclassified"),
                "note": verdict.get("note", ""),
                "sample": verdict.get("sample", ""),
                "outcome": only,
            })
        return listed

    return {
        "schema": "metadatasharp-parity/1",
        "files": {"total": len(files), "attempted": attempted, "statuses": dict(statuses), "refusals": dict(refusals),
                  "partial": dict(partial)},
        "outcomes": dict(outcomes),
        "parity": {
            "expected_keys": expected + outcomes[MISSING_KEY],
            "found_keys": expected,
            "key_rate": rate(expected, expected + outcomes[MISSING_KEY]),
            "comparable_values": comparable,
            "agreed_values": outcomes[MATCH_VALUE],
            "value_rate": rate(outcomes[MATCH_VALUE], comparable),
        },
        "advisory": dict(advisory),
        "reference_repeats": sum(entry["reference_repeats"] for entry in files),
        "reference_notes": dict(notes),
        "by_format": {name: dict(counts) for name, counts in sorted(by_format.items())},
        "by_family": {name: dict(counts) for name, counts in sorted(by_family.items())},
        "by_kind_format": {name: {fmt: dict(counts) for fmt, counts in sorted(formats.items())}
                           for name, formats in sorted(by_kind_format.items())},
        "interpreted": {
            "outcomes": dict(interpreted_outcomes),
            # The rate is over the values both sides interpret; what we do not interpret is beside it.
            "compared": compared_texts,
            "agreed": interpreted_outcomes[MATCH_INTERPRETED],
            "rate": rate(interpreted_outcomes[MATCH_INTERPRETED], compared_texts),
            "not_interpreted": interpreted_outcomes[NOT_INTERPRETED],
            "not_interpreted_translated": gaps_translated,
            "by_kind": {name: dict(counts) for name, counts in sorted(interpreted_by_kind.items())},
            "by_family": {name: dict(counts) for name, counts in sorted(interpreted_by_family.items())},
            # One line per key, with the pair of texts that accounts for most of its differences.
            "diffs": [{"key": key, "tags": count, "ours": pair[0], "theirs": pair[1], "pair_tags": pair_count}
                      for key, count in interpreted_diffs.most_common()
                      for pair, pair_count in interpreted_pairs[key].most_common(1)],
            # One line per key the reference translates and we do not, with its most frequent text.
            "not_interpreted_keys": [{"key": key, "tags": count, "theirs": text, "text_tags": text_count}
                                     for key, count in untranslated.most_common()
                                     for text, text_count in untranslated_texts[key].most_common(1)],
        },
        "gaps": {
            "missing_keys": gaps(missing_files, missing_tags, MISSING_KEY),
            "unread_keys": gaps(unread_files, unread_tags, NOT_READ),
            "extra_keys": gaps(extra_files, extra_tags, EXTRA_KEY),
        },
    }
