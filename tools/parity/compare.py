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


def compare_tags(reference: dict[str, dict], dumped: dict[str, tuple[str, int, str]]) -> list[dict[str, Any]]:
    """Rows for every key on either side, in reference order first."""
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
        rows.append(row(key, outcome, ours=ours, theirs=theirs, flags=tuple(flags)))
    for key in dumped:
        if key not in reference:
            rows.append(row(key, EXTRA_KEY, ours=dumped[key], theirs=None))
    return rows


def compare_file(label: str, corpus: str, format_name: str, status: str, detail: str,
                 reference: dict[str, dict], dumped: dict[str, tuple[str, int, str]],
                 notes: list[str]) -> dict[str, Any]:
    """One file's verdict. Only files we attempted contribute rows to parity.

    A refusal is a different kind of answer, not a wrong one: the library refusing a file the
    reference tool could not read either is the expected outcome, so those files are counted apart.
    """
    counted = status == "ok"
    rows = compare_tags(reference, dumped) if counted else [
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
    advisory: Counter[str] = Counter()
    notes: Counter[str] = Counter()
    by_format: dict[str, Counter[str]] = defaultdict(Counter)
    by_family: dict[str, Counter[str]] = defaultdict(Counter)
    missing_files: Counter[str] = Counter()
    missing_tags: Counter[str] = Counter()
    unread_files: Counter[str] = Counter()
    unread_tags: Counter[str] = Counter()
    extra_files: Counter[str] = Counter()
    extra_tags: Counter[str] = Counter()

    for entry in files:
        statuses[entry["status"]] += 1
        if entry["status"] != "ok" and entry["detail"]:
            refusals[f"{entry['status']}:{entry['detail']}"] += 1
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

    attempted = sum(1 for entry in files if entry["attempted"])
    expected = outcomes[MATCH_VALUE] + outcomes[MATCH_KEY] + outcomes[VALUE_DIFF]
    comparable = outcomes[MATCH_VALUE] + outcomes[VALUE_DIFF]

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
        "files": {"total": len(files), "attempted": attempted, "statuses": dict(statuses), "refusals": dict(refusals)},
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
        "gaps": {
            "missing_keys": gaps(missing_files, missing_tags, MISSING_KEY),
            "unread_keys": gaps(unread_files, unread_tags, NOT_READ),
            "extra_keys": gaps(extra_files, extra_tags, EXTRA_KEY),
        },
    }
