"""Reading of exiv2's ``-Pkycvt`` print format.

The reference files in the upstream corpus are whole-stdout captures of ``exiv2 -Pkycvt <file>``, so a
line is either a tag line or noise: a warning, an error, part of an embedded packet, or the tool
banner. All the quirks of that print format live here, and nothing in this module reads the corpus, so
the behaviour is testable against strings alone.

A tag line is ``key  type count  value`` where the key is padded to a column, the type and the count
are separated by a single space (the type column is padded, the count is not), and the value - which
may itself contain runs of spaces - is set off by two. Two shapes carry no raw value to compare: a
line printed with the filename column, where the column after the count holds a decoded label, and a
line for an entry whose type identifier is invalid, where the type itself is printed as a hex number
and nothing follows the count.

The value is in fact two columns, which is what the ``v`` and the ``t`` of ``-Pkycvt`` ask for: the
raw value, two spaces, and the interpreted ("translated") value - ``72/1  72``, ``2  inch``,
``1/250  1/250 s``. The raw comparison has always treated the pair as one string and keeps doing so;
:func:`split_columns` separates them for the comparison of interpreted values, which is made apart.
"""

from __future__ import annotations

import re

#: A tag key: a family, a group, and a name. Array indices (``History[1]``), qualified path segments
#: (``/stEvt:action``) and numeric keys for unknown tags (``0x0201``) all appear, and a handful of
#: maker-note groups spell their names with a space ("Color Temperature"), which the column padding
#: keeps unambiguous.
NAME = r"[A-Za-z0-9_\-.:/\[\]]+"
KEY = r"(?:Exif|Iptc|Xmp|ICC)\.[A-Za-z0-9_\-.:/\[\]]+(?:\.[A-Za-z0-9_\-.:/\[\]]+)+(?: " + NAME + r")*"
TAG_LINE = re.compile(
    r"^(?:(?P<name>\S+) +)?"
    r"(?P<key>" + KEY + r")"
    r" +(?P<type>[A-Za-z][A-Za-z0-9]*|0x[0-9a-fA-F]{4}) +(?P<count>\d+)"
    r"(?: {2,}(?P<value>.*))?$"
)
#: A line that starts with a key, which is what distinguishes a tag line from prose that mentions one.
KEY_PREFIX = re.compile(r"^" + KEY + r"(?: {2,}|$)")


#: What separates the raw column from the interpreted one, and what a value may also contain.
COLUMN_GAP = re.compile(r" {2,}")


def split_columns(value: str) -> tuple[str, str] | None:
    """``(raw, interpreted)`` of a printed value, or ``None`` when the two cannot be told apart.

    One gap is the ordinary case. No gap means a column is empty - a count of zero, a blank string -
    and which one is not knowable from the line. Several gaps mean the value itself contains runs of
    spaces; that is still unambiguous when the text is simply printed twice, which is what the tool
    does for a string it does not translate, and it is given up on otherwise rather than guessed.
    """
    gaps = [(match.start(), match.end()) for match in COLUMN_GAP.finditer(value)]
    if not gaps:
        return None
    if len(gaps) == 1:
        start, end = gaps[0]
        return value[:start], value[end:]
    for start, end in gaps:
        if value[:start].strip() == value[end:].strip():
            return value[:start], value[end:]
    return None


def is_key(token: str) -> bool:
    return KEY_PREFIX.match(token) is not None


def split_tag_line(line: str) -> tuple[str, str, int, str, bool] | None:
    """Return ``(key, type, count, value, labelled)`` for a tag line, or ``None``.

    ``labelled`` is true when the printed value column is not a raw value at all - a decoded label in
    the filename-column shape, or nothing at all behind an invalid type - so only the key can be
    compared with a byte-level dump.
    """
    match = TAG_LINE.match(line.rstrip())
    if match is None:
        return None
    type_name = match.group("type")
    return (
        match.group("key"),
        type_name,
        int(match.group("count")),
        match.group("value") or "",
        match.group("name") is not None or type_name.startswith("0x"),
    )


def parse_reference(text: str) -> tuple[dict[str, dict], list[str]]:
    """Split a reference file into ``{key: {"type", "count", "value", "labelled"}}`` and its notes.

    Each entry also carries ``raw`` and ``interpreted``, the two columns of ``value`` where they can
    be separated and ``None`` where they cannot or where the row has no raw value at all. ``value``
    itself is left exactly as printed, because that is what the raw comparison reads.

    Notes are kept rather than dropped: they are how exiv2 reported a refusal or a warning, and the
    report has to distinguish "exiv2 refused the file" from "we disagree about a value". A key the
    reference prints twice - a second frame, a second IFD - keeps its first row and counts the rest
    in ``repeats``, because the first row is what a single-snapshot reader has to agree with.
    """
    lines = text.replace("\r\n", "\n").replace("\r", "\n").split("\n")
    tags: dict[str, dict] = {}
    notes: list[str] = []
    for raw in lines:
        line = raw.rstrip()
        if not line:
            continue
        parsed = split_tag_line(line)
        if parsed is None:
            notes.append(line)
            continue
        key, type_name, count, value, labelled = parsed
        if key in tags:
            tags[key]["repeats"] += 1
            continue
        columns = None if labelled else split_columns(value)
        tags[key] = {"type": type_name, "count": count, "value": value, "labelled": labelled, "repeats": 0,
                     "raw": columns[0] if columns else None, "interpreted": columns[1] if columns else None}
    return tags, notes


def summarize_notes(notes: list[str]) -> dict[str, int]:
    """Bucket non-tag lines so the report can say what the noise was."""
    kinds: dict[str, int] = {}
    for line in notes:
        if line.startswith(("Warning:", "Error:", "Permission:", "Notice:")):
            kind = "diagnostic"
        elif line.startswith("<") or line.startswith("?xml") or line.startswith("<!--"):
            kind = "packet"
        elif re.match(r"^0x[0-9a-f]{4} ", line):
            kind = "hexdump"
        elif KEY_PREFIX.match(line):
            kind = "unparsed-key"
        else:
            kind = "other"
        kinds[kind] = kinds.get(kind, 0) + 1
    return kinds
