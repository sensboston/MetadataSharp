"""Parsing of the dump grammar emitted by ``tools/MetadataSharp.Dump``.

The grammar is one record per line, tab-separated fields, LF endings:

    FILE\t<path>
    TAG\t<key>\t<type>\t<count>\t<value>
    STATUS\tok|refused|not-attempted|error\t<detail>

This module is the harness's contract with the C# tool: it is deliberately strict, because a
silently truncated or mis-split block would otherwise be reported as a parity gap in the library
rather than as a broken run.
"""

from __future__ import annotations

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


def parse_dump(text: str, *, source: str = "dump") -> dict[str, dict]:
    """Return ``{path: {"status": str, "detail": str, "tags": {key: (type, count, value)}}}``.

    Every ``FILE`` record must be followed by exactly one ``STATUS`` record and ``TAG`` records in
    between; anything else raises :class:`DumpFormatError`.
    """
    files: dict[str, dict] = {}
    current: dict | None = None
    for lineno, line in enumerate(text.split("\n"), 1):
        if not line:
            continue
        fields = line.split("\t")
        kind = fields[0]
        if kind == "FILE":
            if len(fields) < 2:
                raise DumpFormatError(f"{source}:{lineno}: FILE record without a path")
            if current is not None:
                raise DumpFormatError(f"{source}:{lineno}: FILE record before the previous one was closed")
            current = {"status": None, "detail": "", "tags": {}, "order": [], "duplicates": 0}
            files[unescape(fields[1])] = current
            continue
        if current is None:
            raise DumpFormatError(f"{source}:{lineno}: {kind} record outside any FILE block")
        if kind == "TAG":
            if len(fields) != 5:
                raise DumpFormatError(f"{source}:{lineno}: TAG record must have 5 fields, got {len(fields)}")
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
