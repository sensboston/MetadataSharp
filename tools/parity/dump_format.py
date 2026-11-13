"""Parsing of the dump grammar emitted by ``tools/MetadataSharp.Dump``.

The grammar is one record per line, tab-separated fields, LF endings:

    FILE\t<path>
    TAG\t<key>\t<type>\t<count>\t<value>[\t<interpreted>]
    SIZE\t<width>x<height>\t<mime type>
    STATUS\tok|refused|not-attempted|error\t<detail>

The sixth ``TAG`` field is optional. A record that carries it offers an interpreted (human-readable)
value for the tag, empty when the library has none; a record without it offers no interpretation at
all. The field never takes part in the comparison of raw values: it is kept beside the
``(type, count, value)`` tuple, not in it.

This module is the harness's contract with the C# tool: it is deliberately strict, because a
silently truncated or mis-split block would otherwise be reported as a parity gap in the library
rather than as a broken run.
"""

from __future__ import annotations

import re

STATUS_KINDS = ("ok", "refused", "not-attempted", "error")
_UNESCAPE = {"\\t": "\t", "\\n": "\n", "\\r": "\r", "\\\\": "\\"}


class DumpFormatError(Exception):
    """The dump output is not a complete, well-formed block."""


def unescape(value: str) -> str:
    """Inverse of the tool's escaping: only structure characters are encoded."""
    if "\\" not in value:
        return value
    out: list[str] = []
    i = 0
    while i < len(value):
        two = value[i : i + 2]
        if two in _UNESCAPE:
            out.append(_UNESCAPE[two])
            i += 2
            continue
        if value.startswith("\\x", i) and len(value) >= i + 4:
            hexa = value[i + 2 : i + 4]
            if len(hexa) == 2 and all(c in "0123456789abcdef" for c in hexa.lower()):
                out.append(chr(int(hexa, 16)))
                i += 4
                continue
        if value[i] == "\\":
            raise DumpFormatError(f"dangling backslash at offset {i} of {value!r}")
        out.append(value[i])
        i += 1
    return "".join(out)


def escape(value: str) -> str:
    """The writer's half of the grammar: the inverse of :func:`unescape`.

    ``tools/MetadataSharp.Dump`` implements this in C# and the tests build stand-in output with this
    function, so the two sides are checked against each other rather than against a description.
    Only the characters that would break one-record-per-line can appear escaped; everything else is
    written verbatim, which keeps a report readable.
    """
    out: list[str] = []
    for char in value:
        if char == "\\":
            out.append("\\\\")
        elif char == "\t":
            out.append("\\t")
        elif char == "\n":
            out.append("\\n")
        elif char == "\r":
            out.append("\\r")
        elif ord(char) < 0x20 or char == "\x7f":
            out.append("\\x%02x" % ord(char))
        else:
            out.append(char)
    return "".join(out)


def record(*fields: str) -> str:
    """One dump line: every field escaped, tabs between them. The shape the tool prints."""
    return "\t".join(escape(field) for field in fields)


def parse_dump(text: str, *, source: str = "dump") -> dict[str, dict]:
    """Return ``{path: {"status": str, "detail": str, "tags": {key: (type, count, value)}}}``.

    Every ``FILE`` record must be followed by exactly one ``STATUS`` record and ``TAG`` records in
    between; anything else raises :class:`DumpFormatError`. A block also has ``interpreted``, which
    maps a key to its sixth field for exactly the records that carried one.
    """
    files: dict[str, dict] = {}
    current: dict | None = None
    for lineno, line in enumerate(text.split("\n"), 1):
        if line.endswith("\r"):
            # The grammar says LF and the tool writes LF, but a stream that came from a shell redirect
            # on Windows may carry the partner byte; eating it here keeps it out of the last field.
            line = line[:-1]
        if not line:
            continue
        fields = line.split("\t")
        kind = fields[0]
        if kind == "FILE":
            if len(fields) < 2:
                raise DumpFormatError(f"{source}:{lineno}: FILE record without a path")
            if current is not None:
                raise DumpFormatError(f"{source}:{lineno}: FILE record before the previous one was closed")
            current = {"status": None, "detail": "", "tags": {}, "order": [], "duplicates": 0, "interpreted": {},
                   "size": None}
            files[unescape(fields[1])] = current
            continue
        if current is None:
            raise DumpFormatError(f"{source}:{lineno}: {kind} record outside any FILE block")
        if kind == "TAG":
            if len(fields) not in (5, 6):
                raise DumpFormatError(f"{source}:{lineno}: TAG record must have 5 fields, or 6 with an "
                                      f"interpreted value, got {len(fields)}")
            key = unescape(fields[1])
            try:
                count = int(fields[3])
            except ValueError as error:
                raise DumpFormatError(f"{source}:{lineno}: TAG count is not an integer: {fields[3]!r}") from error
            if key in current["tags"]:
                # A repeated key means two directories of the same file printed the same group/tag
                # (multi-page TIFF). The first occurrence is what a single-frame reader reports, so
                # keep it and count the rest instead of failing the run.
                current["duplicates"] += 1
                continue
            current["tags"][key] = (unescape(fields[2]), count, unescape(fields[4]))
            current["order"].append(key)
            if len(fields) == 6:
                current["interpreted"][key] = unescape(fields[5])
            continue
        if kind == "SIZE":
            # The record a format without a metadata block gives: the size its header states, which is
            # what the reference's own summary (`exiv2 -ps`) prints of such a file.
            if len(fields) != 3:
                raise DumpFormatError(f"{source}:{lineno}: SIZE record must have 3 fields, got {len(fields)}")
            if current["size"] is not None:
                raise DumpFormatError(f"{source}:{lineno}: second SIZE record for one file")
            match = re.fullmatch(r"(\d+)x(\d+)", fields[1])
            if match is None:
                raise DumpFormatError(f"{source}:{lineno}: SIZE record does not give two dimensions: {fields[1]!r}")
            current["size"] = (int(match.group(1)), int(match.group(2)), unescape(fields[2]))
            continue
        if kind == "STATUS":
            if current["status"] is not None:
                raise DumpFormatError(f"{source}:{lineno}: second STATUS record for one file")
            status = fields[1] if len(fields) > 1 else ""
            if status not in STATUS_KINDS:
                raise DumpFormatError(f"{source}:{lineno}: unknown status {status!r}")
            current["status"] = status
            current["detail"] = unescape(fields[2]) if len(fields) > 2 else ""
            current = None
            continue
        raise DumpFormatError(f"{source}:{lineno}: unknown record {kind!r}")
    if current is not None:
        raise DumpFormatError(f"{source}: dump ends inside the FILE block (a STATUS record is missing)")
    return files


def tag_count(block: dict) -> int:
    return len(block["tags"])
