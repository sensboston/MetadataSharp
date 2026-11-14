#!/usr/bin/env python3
"""Preview parity: what ``MetadataSharp.Dump --previews`` lists against what ``exiv2 -pp`` printed.

The pinned exiv2 tree keeps the output of its own preview test in
``test/data/test_reference_files/preview-test.out``: for every input a block

    -----> NAME <-----

    Command: exiv2 -pp NAME
    Preview 1: image/jpeg, 160x120 pixels, 4158 bytes
    Exit code: 0

followed by the block of the extraction command, which says nothing about the list. That file is
read as text; no external code is configured, built or run. The comparison is of a different kind
from the key-by-key one in ``the C# key-parity report`` and shares none of its rules: a preview is the triple
(MIME type, width x height, byte count), and a file's previews are compared as a multiset.

    match    a reference preview we list with the same type, dimensions and length
    missing  a reference preview we do not list
    extra    a preview we list that the reference does not

The order is judged separately: a file is ``ordered`` when the previews both sides list come in
the same order on both.

With ``--fixtures`` and ``--exiv2-command`` the expectation is not that text: the pinned tool lists
each file of the given directories in the same run, as ``the C# key-parity report`` does for key parity. This
is how a file is measured that the pinned tree holds but its preview test does not name.
"""
from __future__ import annotations

import argparse
import base64
import json
import re
import shlex
import struct
import subprocess
import sys
import tempfile
import zlib
from collections import Counter
from pathlib import Path
from typing import Any

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
if str(HERE) not in sys.path:
    sys.path.insert(0, str(HERE))

import parity_support as run_parity  # noqa: E402

KIND = "Preview"
TITLE = "PARITY preview"
REFERENCE = Path("test") / "data" / "test_reference_files" / "preview-test.out"

_BLOCK = re.compile(r"^-----> (?P<name>.+) <-----$")
_COMMAND = re.compile(r"^Command: exiv2 (?P<arguments>.+)$")
_PREVIEW = re.compile(r"^Preview \d+: (?P<mime>\S+), (?P<width>\d+)x(?P<height>\d+) pixels, (?P<bytes>\d+) bytes$")

Preview = tuple[str, int, int, int]


class PreviewParityError(Exception):
    """The run cannot be trusted; nothing should be concluded from its numbers."""


def parse_reference(text: str) -> dict[str, list[Preview]]:
    """The previews ``exiv2 -pp`` listed for every file named in the expectation text, in its order."""
    expected: dict[str, list[Preview]] = {}
    name: str | None = None
    listing = False
    for line in text.splitlines():
        block = _BLOCK.match(line)
        if block:
            name = block.group("name")
            if name in expected:
                raise PreviewParityError(f"the expectation names {name!r} twice")
            expected[name] = []
            listing = False
            continue
        command = _COMMAND.match(line)
        if command:
            # Only the listing command describes the previews; the extraction command repeats nothing.
            listing = command.group("arguments").split()[0] == "-pp"
            continue
        preview = _PREVIEW.match(line)
        if preview:
            if name is None:
                raise PreviewParityError("a preview line comes before any file block")
            if listing:
                expected[name].append((preview.group("mime"), int(preview.group("width")),
                                       int(preview.group("height")), int(preview.group("bytes"))))
        elif line.startswith("Preview "):
            raise PreviewParityError(f"a preview line does not parse: {line!r}")
    return expected


def oracle_reference(outputs: dict[str, str]) -> dict[str, list[Preview]]:
    """What the pinned tool printed for each file, read by the rules of the expectation text.

    A file the tool printed nothing for expects no preview: whatever we list for it is ``extra``.
    """
    blocks = [f"-----> {name} <-----\nCommand: exiv2 -pp {name}\n{text}\n" for name, text in outputs.items()]
    return parse_reference("".join(blocks))


def _find_all(haystack: bytes, needle: bytes) -> list[int]:
    if not needle:
        return []
    result: list[int] = []
    at = 0
    while (at := haystack.find(needle, at)) >= 0:
        result.append(at)
        at += 1
    return result


def _compact_hex_offsets(source: bytes, start: int, end: int, encoded: bytes) -> list[int]:
    digits = bytearray()
    offsets: list[int] = []
    for at in range(start, end):
        value = source[at]
        if chr(value).isspace():
            continue
        if value not in b"0123456789abcdefABCDEF":
            digits.clear()
            offsets.clear()
            continue
        digits.append(value)
        offsets.append(at)
    token = encoded.upper()
    found = _find_all(bytes(digits).upper(), token)
    return [offsets[at] for at in found]


def _compact_base64_offsets(source: bytes, encoded: bytes) -> list[int]:
    token = base64.b64encode(encoded)
    compact = bytearray()
    offsets: list[int] = []
    found: list[int] = []
    for at, value in enumerate(source):
        if chr(value).isspace():
            continue
        if value not in b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=":
            found.extend(offsets[index] for index in _find_all(bytes(compact), token))
            compact.clear()
            offsets.clear()
            continue
        compact.append(value)
        offsets.append(at)
    found.extend(offsets[index] for index in _find_all(bytes(compact), token))
    return found


def _tiff_first_strip(data: bytes) -> bytes | None:
    """The first image strip of a standalone TIFF generated for Exiv2's reduced-image preview."""
    if len(data) < 8 or data[:2] not in (b"II", b"MM"):
        return None
    little = data[:2] == b"II"
    order = "<" if little else ">"
    if struct.unpack_from(order + "H", data, 2)[0] != 42:
        return None
    ifd = struct.unpack_from(order + "I", data, 4)[0]
    if ifd > len(data) - 2:
        return None
    count = struct.unpack_from(order + "H", data, ifd)[0]
    if count > 4096 or ifd + 2 + count * 12 > len(data):
        return None
    fields: dict[int, tuple[int, int, int]] = {}
    widths = {1: 1, 2: 1, 3: 2, 4: 4, 5: 8, 6: 1, 7: 1, 8: 2, 9: 4, 10: 8, 11: 4, 12: 8, 13: 4}
    for index in range(count):
        at = ifd + 2 + index * 12
        tag, kind, number, value = struct.unpack_from(order + "HHII", data, at)
        if kind not in widths:
            continue
        length = widths[kind] * number
        fields[tag] = (kind, number, value if length <= 4 else value)
    offsets, counts = fields.get(273), fields.get(279)
    if offsets is None or counts is None or offsets[1] == 0 or offsets[1] != counts[1]:
        return None

    # Locate each field's inline value independently; external arrays use their TIFF offsets.
    def field_values(tag: int, field: tuple[int, int, int]) -> list[int]:
        for index in range(count):
            entry = ifd + 2 + index * 12
            if struct.unpack_from(order + "H", data, entry)[0] != tag:
                continue
            kind, number, pointer = field
            length = widths[kind] * number
            start = entry + 8 if length <= 4 else pointer
            fmt = {1: "B", 3: "H", 4: "I", 13: "I", 6: "B", 7: "B", 8: "h", 9: "i"}.get(kind)
            if fmt is None or start < 0 or start > len(data) - length:
                return []
            return list(struct.unpack_from(order + fmt * number, data, start))
        return []

    strip_offsets = field_values(273, offsets)
    strip_counts = field_values(279, counts)
    if not strip_offsets or not strip_counts or strip_offsets[0] > len(data) - strip_counts[0]:
        return None
    return data[strip_offsets[0]:strip_offsets[0] + strip_counts[0]]


def _png_raw_exif_profile_offsets(source: bytes, extracted: bytes) -> list[int]:
    """Map a PNG Raw profile thumbnail back to its compressed zTXt profile payload."""
    signature = b"\x89PNG\r\n\x1a\n"
    candidates: list[int] = []
    search = 0
    while (png := source.find(signature, search)) >= 0:
        search = png + len(signature)
        at = search
        while at + 12 <= len(source):
            length = struct.unpack_from(">I", source, at)[0]
            if length > len(source) - at - 12:
                break
            kind = source[at + 4:at + 8]
            payload = source[at + 8:at + 8 + length]
            if kind == b"zTXt":
                keyword, separator, rest = payload.partition(b"\0")
                if keyword == b"Raw profile type exif" and separator and rest[:1] == b"\0":
                    try:
                        text = zlib.decompress(rest[1:])
                        lines = text.lstrip(b"\r\n").split(b"\n", 2)
                        if len(lines) == 3 and lines[0].strip().lower() == b"exif":
                            expected = int(lines[1].strip())
                            digits = bytes(value for value in lines[2] if value not in b" \t\r\n")
                            decoded = bytes.fromhex(digits.decode("ascii"))
                            if len(decoded) == expected and extracted in decoded:
                                candidates.append(at + 8 + len(keyword) + 2)
                    except (ValueError, UnicodeDecodeError, zlib.error):
                        pass
            at += 12 + length
            if kind == b"IEND":
                break
    return candidates


def _source_offsets(source: bytes, extracted: bytes, mime: str, width: int, height: int) -> list[int]:
    exact = _find_all(source, extracted)
    if exact:
        return exact
    if mime == "image/tiff":
        first_strip = _tiff_first_strip(extracted)
        if first_strip:
            return _find_all(source, first_strip)
    if mime == "image/jpeg":
        # Photoshop EPS stores JPEG data as hexadecimal IRB data; XMP thumbnails use base64.
        start, end = 0, len(source)
        begin = source.find(b"%BeginPhotoshop:")
        if begin >= 0:
            line = source.find(b"\n", begin)
            finish = source.find(b"%EndPhotoshop", line + 1) if line >= 0 else -1
            if finish >= 0:
                found = _compact_hex_offsets(source, line + 1, finish, extracted.hex().encode("ascii"))
                if found:
                    return [line + 1]
        found = _compact_base64_offsets(source, extracted)
        if found:
            return found
        found = _png_raw_exif_profile_offsets(source, extracted)
        if found:
            return found
    elif mime == "image/x-portable-anymap" and extracted.startswith(b"P6\n"):
        header = f"P6\n{width} {height}\n255\n".encode("ascii")
        if extracted.startswith(header):
            begin = source.find(b"%%BeginData:")
            line = source.find(b"\n", begin) if begin >= 0 else -1
            finish = source.find(b"%%EndData", line + 1) if line >= 0 else -1
            if finish >= 0:
                found = _compact_hex_offsets(source, line + 1, finish, extracted[len(header):].hex().encode("ascii"))
                if found:
                    return [line + 1]
    return []


def oracle_offsets(exiv2_command: list[str], path: Path, previews: list[Preview]) -> list[list[int]]:
    """Infer file-relative source offsets from Exiv2's own -ep byte outputs.

    The pinned -pp text has no offset column. Direct items are found byte-for-byte; XMP and EPS
    filters are reversed as base64/hex at their source payload. Ambiguous repeated bytes are kept as
    a set and the MetadataSharp offset must be one of those positions.
    """
    with tempfile.TemporaryDirectory(prefix="metadata-preview-") as temporary:
        try:
            finished = subprocess.run(exiv2_command + ["-f", "-ep", str(path.resolve())], cwd=temporary,
                                      capture_output=True, text=True, encoding="utf-8", errors="replace",
                                      timeout=300, check=False)
        except FileNotFoundError as error:
            raise PreviewParityError(f"the pinned extraction command is not runnable: {error}") from error
        except subprocess.TimeoutExpired as error:
            raise PreviewParityError(f"the pinned extraction command timed out on {path.name}") from error
        if finished.returncode != 0:
            detail = (finished.stderr or finished.stdout or "").strip().replace("\n", " ")[:400]
            raise PreviewParityError(f"pinned exiv2 -ep failed on {path.name}: {detail}")
        outputs: list[tuple[int, Path]] = []
        for output in Path(temporary).iterdir():
            match = re.search(r"-preview(\d+)(?:\.|$)", output.name, re.IGNORECASE)
            if match and output.is_file():
                outputs.append((int(match.group(1)), output))
        outputs.sort(key=lambda item: item[0])
        if [number for number, _ in outputs] != list(range(1, len(previews) + 1)):
            raise PreviewParityError(f"exiv2 -ep produced {len(outputs)} previews for {path.name}, but -pp listed {len(previews)}")
        source = path.read_bytes()
        offsets: list[list[int]] = []
        for ((mime, width, height, _), (_, output)) in zip(previews, outputs):
            candidates = _source_offsets(source, output.read_bytes(), mime, width, height)
            if not candidates:
                raise PreviewParityError(f"cannot locate exiv2 preview bytes in {path.name}; offset parity is unprovable")
            offsets.append(candidates)
        return offsets


def parse_dump(text: str) -> dict[str, dict[str, Any]]:
    """``FILE`` / ``PREVIEW`` / ``STATUS`` records of ``MetadataSharp.Dump --previews``, by path."""
    blocks: dict[str, dict[str, Any]] = {}
    current: dict[str, Any] | None = None
    for number, line in enumerate(text.splitlines(), 1):
        if not line:
            continue
        fields = line.split("\t")
        if fields[0] == "FILE" and len(fields) == 2:
            if current is not None and current["status"] is None:
                raise PreviewParityError(f"dump:{number}: FILE record before the previous one was closed")
            current = {"previews": [], "offsets": [], "sources": [], "status": None, "detail": ""}
            if fields[1] in blocks:
                raise PreviewParityError(f"dump:{number}: duplicate FILE record {fields[1]!r}")
            blocks[fields[1]] = current
        elif fields[0] == "PREVIEW" and len(fields) == 7 and current is not None and current["status"] is None:
            size = re.fullmatch(r"(\d+)x(\d+)", fields[3])
            if size is None or not fields[4].isdigit() or not fields[5].isdigit():
                raise PreviewParityError(f"dump:{number}: PREVIEW record does not parse: {line!r}")
            current["previews"].append((fields[2], int(size.group(1)), int(size.group(2)), int(fields[4])))
            current["offsets"].append(int(fields[5]))
            current["sources"].append(fields[6])
        elif fields[0] == "STATUS" and len(fields) >= 2 and current is not None and current["status"] is None:
            current["status"] = fields[1]
            current["detail"] = fields[2] if len(fields) > 2 else ""
        else:
            raise PreviewParityError(f"dump:{number}: unexpected record: {line!r}")
    if current is not None and current["status"] is None:
        raise PreviewParityError("the dump ends inside a FILE block")
    return blocks


def compare_file(expected: list[Preview], listed: list[Preview]) -> dict[str, Any]:
    """Multiset comparison of one file's previews, and whether the common ones come in the same order."""
    remaining = Counter(listed)
    matched: list[Preview] = []
    missing: list[Preview] = []
    for preview in expected:
        if remaining[preview] > 0:
            remaining[preview] -= 1
            matched.append(preview)
        else:
            missing.append(preview)
    extra = list(remaining.elements())
    common = Counter(matched)
    ours: list[Preview] = []
    for preview in listed:
        if common[preview] > 0:
            common[preview] -= 1
            ours.append(preview)
    return {"match": len(matched), "missing": missing, "extra": extra, "ordered": ours == matched}


def compare(expected: dict[str, list[Preview]], dumped: dict[str, dict[str, Any]], paths: dict[str, Path | None],
            expected_offsets: dict[str, list[list[int]]] | None = None) -> dict[str, Any]:
    totals = Counter()
    statuses = Counter()
    files: list[dict[str, Any]] = []
    for name in sorted(expected):
        path = paths.get(name)
        block = dumped.get(str(path)) if path is not None else None
        if path is None:
            status, listed = "no-input", []
        elif block is None:
            raise PreviewParityError(f"the dump has no block for {name}")
        else:
            status, listed = block["status"], block["previews"]
            if status == "ok":
                file_size = path.stat().st_size
                offsets, sources = block.get("offsets", []), block.get("sources", [])
                if len(offsets) != len(listed) or len(sources) != len(listed):
                    raise PreviewParityError(f"{name}: the dump has no offset/source for every listed preview")
                for index, (preview, offset, source) in enumerate(zip(listed, offsets, sources), 1):
                    generated = source in {"EPS.AI7.Hex", "EPS.Photoshop.Hex"} or source.startswith("Xmp.Thumbnail")
                    if offset < 0 or offset >= file_size or preview[3] <= 0 or (not generated and offset + preview[3] > file_size):
                        raise PreviewParityError(f"{name}: preview {index} has an invalid file range: "
                                                 f"offset={offset}, length={preview[3]}, file={file_size}, source={source}")
        outcome = compare_file(expected[name], listed if status == "ok" else [])
        offset_mismatch: list[dict[str, Any]] = []
        reference_offsets = (expected_offsets or {}).get(name)
        if status == "ok" and reference_offsets is not None:
            if len(reference_offsets) != len(expected[name]) or len(block.get("offsets", [])) != len(expected[name]):
                raise PreviewParityError(f"{name}: offset oracle/dump count differs from the -pp list")
            available: dict[Preview, list[int]] = {}
            for index, preview in enumerate(listed):
                available.setdefault(preview, []).append(index)
            for index, preview in enumerate(expected[name]):
                candidates = available.get(preview, [])
                if not candidates:
                    continue
                actual_index = candidates.pop(0)
                actual_offset = block["offsets"][actual_index]
                allowed = reference_offsets[index]
                if actual_offset not in allowed:
                    offset_mismatch.append({"preview": list(preview), "actual": actual_offset, "expected_any": allowed})
            totals["offset_mismatch"] += len(offset_mismatch)
        statuses[status] += 1
        totals["expected"] += len(expected[name])
        totals["match"] += outcome["match"]
        totals["missing"] += len(outcome["missing"])
        totals["extra"] += len(outcome["extra"])
        totals["files"] += 1
        totals["files_equal"] += 1 if not outcome["missing"] and not outcome["extra"] else 0
        totals["files_ordered"] += 1 if outcome["ordered"] else 0
        entry = {"file": name, "status": status, "match": outcome["match"], "ordered": outcome["ordered"],
                 "listed_offsets": block.get("offsets", []) if block is not None else []}
        if outcome["missing"]:
            entry["missing"] = [list(preview) for preview in outcome["missing"]]
        if outcome["extra"]:
            entry["extra"] = [list(preview) for preview in outcome["extra"]]
        if offset_mismatch:
            entry["offset_mismatch"] = offset_mismatch
        if status == "error":
            entry["detail"] = block["detail"] if block is not None else ""
        files.append(entry)
    rate = round(totals["match"] / totals["expected"], 6) if totals["expected"] else None
    return {"kind": KIND, "totals": dict(totals), "rate": rate, "statuses": dict(statuses), "files": files}


def gate_exit_code(report: dict[str, Any]) -> int:
    """Fail closed for list mismatches, missing corpus input, or reader refusals."""
    return 1 if (report["statuses"].get("error") or report["totals"].get("missing") or
                 report["totals"].get("extra") or report["totals"].get("offset_mismatch") or
                 report["statuses"].get("refused") or report["statuses"].get("no-input")) else 0


def text_report(report: dict[str, Any]) -> str:
    totals = report["totals"]
    lines = [
        f"{KIND} parity against exiv2 -pp",
        f"  files: {totals.get('files', 0)} ({totals.get('files_equal', 0)} with the same previews, "
        f"{totals.get('files_ordered', 0)} in the same order); statuses: {report['statuses']}",
        f"  previews: {totals.get('expected', 0)} expected, {totals.get('match', 0)} match, "
        f"{totals.get('missing', 0)} missing, {totals.get('extra', 0)} extra, "
        f"{totals.get('offset_mismatch', 0)} offset mismatches; rate {report['rate']}",
    ]
    for entry in report["files"]:
        if "missing" in entry or "extra" in entry or "offset_mismatch" in entry or not entry["ordered"] or entry["status"] != "ok":
            lines.append(f"  {entry['file']} [{entry['status']}] missing={entry.get('missing', [])} "
                         f"extra={entry.get('extra', [])} offset_mismatch={entry.get('offset_mismatch', [])} "
                         f"ordered={entry['ordered']}")
    return "\n".join(lines) + "\n"


def summary(report: dict[str, Any]) -> dict[str, Any]:
    """The part of the report that travels as annotations: the totals and every file that differs."""
    differing = [entry for entry in report["files"]
                 if "missing" in entry or "extra" in entry or "offset_mismatch" in entry or not entry["ordered"] or entry["status"] != "ok"]
    return {"kind": KIND, "totals": report["totals"], "rate": report["rate"], "statuses": report["statuses"],
            "differing": differing}


def run_dump(dump_command: list[str], paths: list[Path]) -> dict[str, dict[str, Any]]:
    listing = "".join(f"{path}\n" for path in paths)
    try:
        finished = subprocess.run(dump_command + ["--previews", "--files-from", "-"], input=listing, capture_output=True,
                                 text=True, encoding="utf-8", errors="replace", timeout=1800, check=False)
    except FileNotFoundError as error:
        raise PreviewParityError(f"the dump command is not runnable: {error}") from error
    except subprocess.TimeoutExpired as error:
        raise PreviewParityError("the dump tool timed out") from error
    if finished.returncode != 0:
        raise PreviewParityError(f"the dump tool failed with {finished.returncode}: {(finished.stderr or '').strip()[:400]}")
    return parse_dump(finished.stdout or "")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Compare our preview list with exiv2's own expectations.")
    parser.add_argument("--tree", type=Path, help="root of a pinned exiv2 checkout, for its preview expectation")
    parser.add_argument("--fixtures", type=Path, action="append", default=[],
                        help="a directory of files the pinned tool lists in this run instead; repeatable")
    parser.add_argument("--exiv2-command", help="pinned exiv2 command (normally 'exiv2 -pp'); used as the list oracle "
                                                "for --fixtures and as -ep source-offset oracle when supplied")
    parser.add_argument("--dump-command", required=True, help="how to run MetadataSharp.Dump, split POSIX-style on spaces")
    parser.add_argument("--out", type=Path, default=ROOT / "artifacts/parity/preview", help="report directory")
    parser.add_argument("--annotate", action="store_true", help="emit the summary as chunked workflow annotations")
    parser.add_argument("--title", default=TITLE, help="annotation title")
    args = parser.parse_args(argv)

    if (args.tree is None) == (not args.fixtures):
        parser.error("pass either --tree or --fixtures")
    paths: dict[str, Path | None]
    if args.fixtures:
        if not args.exiv2_command:
            raise PreviewParityError("--fixtures needs --exiv2-command: the files have no stored expectation")
        oracle = shlex.split(args.exiv2_command)
        paths = {}
        for candidate in run_parity.corpus_mod.discover_generated(*args.fixtures):
            if candidate.label in paths:
                raise PreviewParityError(f"two files are named {candidate.label}")
            paths[candidate.label] = candidate.path
        expected = oracle_reference({name: run_parity.run_reference(oracle, path, require_success=True)
                                     for name, path in paths.items() if path})
    else:
        reference = args.tree / REFERENCE
        if not reference.is_file():
            raise PreviewParityError(f"no preview expectation at {reference}")
        expected = parse_reference(reference.read_text(encoding="utf-8", errors="replace"))
        if not expected:
            raise PreviewParityError(f"{reference} names no file")
        data = args.tree / "test" / "data"
        paths = {name: (data / name if (data / name).is_file() else None) for name in expected}
    present = [path for path in paths.values() if path is not None]
    if not present:
        raise PreviewParityError("none of the files the expectation names is in the tree")
    expected_offsets: dict[str, list[list[int]]] = {}
    if args.exiv2_command:
        extraction_command = [part for part in shlex.split(args.exiv2_command) if part != "-pp"]
        if not extraction_command:
            raise PreviewParityError("--exiv2-command must name the pinned exiv2 executable")
        executable = Path(extraction_command[0])
        if not executable.is_absolute() and executable.exists():
            extraction_command[0] = str(executable.resolve())
        for name, path in paths.items():
            if path is not None:
                expected_offsets[name] = oracle_offsets(extraction_command, path, expected[name])
    report = compare(expected, run_dump(shlex.split(args.dump_command), present), paths,
                     expected_offsets if args.exiv2_command else None)

    args.out.mkdir(parents=True, exist_ok=True)
    (args.out / "preview-report.json").write_text(json.dumps(report, indent=1, sort_keys=True) + "\n", encoding="utf-8")
    text = text_report(report)
    (args.out / "preview-report.txt").write_text(text, encoding="utf-8")
    print(text, end="")
    if args.annotate:
        run_parity.emit_annotations(summary(report), args.title)
    return gate_exit_code(report)


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (PreviewParityError, run_parity.StructuralError, run_parity.corpus_mod.CorpusError) as failure:
        print(f"::error title={TITLE} harness::{failure}", file=sys.stderr)
        print(f"structural failure: {failure}", file=sys.stderr)
        sys.exit(1)
