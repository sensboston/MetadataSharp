#!/usr/bin/env python3
"""The character-set matrix against the pinned Exiv2, with and without ``-n``.

    run_charset.py WORK --exiv2 PATH --fixture "COMMAND"

The fixture generator (``charset-parity``) writes one JPEG per character set of
``MetadataSharp.MetadataCharsets``: a ``UNICODE`` UserComment whose body is the eight-bit code page
the set names - which is exactly what Exiv2's ``-n`` reinterprets - and a UCS-2 little-endian
``XPTitle``. It reads every file back through the public API and writes ``ours.tsv``: the file, the
character set, the key and the text this library shows, plus the text that was written.

The comparison prints each file with the pinned tool twice, once with ``-n <name>`` for the comment
and once without it for the title, and requires the tool's text to be the text this library shows,
character for character. A round-trip row (``written``) must read back as it was written.

``-n`` is behind Exiv2's own optional iconv support. When the pinned build cannot convert, the run
says so in a notice and compares nothing - the matrix's own round trips are proved by the unit
tests, and a gate that silently passed would be worse than one that says what it could not check.
"""
from __future__ import annotations

import argparse
import importlib.util
import shlex
import sys
import traceback
from collections import Counter
from pathlib import Path

_spec = importlib.util.spec_from_file_location("sync_parity", Path(__file__).with_name("sync_parity.py"))
sync = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(sync)
run, annotate = sync.run, sync.annotate

COMMENT = "Exif.Photo.UserComment"
TITLE = "Exif.Image.XPTitle"
NO_ICONV = "Cannot convert text encoding"


def printed(exiv2, path, charset=None, runner=run):
    """(key -> the value the tool prints, exit code, stderr) for one file."""
    command = [exiv2, "-PEkv"]
    if charset is not None:
        command = [exiv2, "-n", charset, "-PEkv"]
    code, output, errors = runner(command + [str(path)])
    values = {}
    for line in output.splitlines():
        parts = line.split(None, 1)
        if parts and parts[0].startswith("Exif."):
            values[parts[0]] = parts[1].strip() if len(parts) > 1 else ""
    return values, code, errors


def comment_text(printed_value: str) -> str:
    """The text of a comment line: the tool puts the character set it read in front of it."""
    return printed_value.split(" ", 1)[1] if printed_value.startswith("charset=") else printed_value


def compare(argv=None, runner=run) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("work")
    parser.add_argument("--exiv2", required=True)
    parser.add_argument("--fixture", required=True, help="the fixture generator, to which the command and WORK are added")
    args = parser.parse_args(argv)
    work = Path(args.work)
    work.mkdir(parents=True, exist_ok=True)

    code, output, errors = runner(shlex.split(args.fixture) + ["charset-parity", str(work)], timeout=900)
    sys.stdout.write(output)
    if code != 0:
        sys.stdout.write(errors)
        annotate("error", f"PARITY charset - the fixture generator failed (exit {code})",
                 (errors or output).strip().splitlines()[-12:] or ["no output"])
        return 1

    manifest = work / "ours.tsv"
    if not manifest.is_file():
        annotate("error", "PARITY charset - the fixture wrote no manifest", [f"{manifest} does not exist"])
        return 1
    rows = [line.split("\t") for line in manifest.read_text(encoding="utf-8").splitlines() if line.count("\t") == 4]
    if not rows:
        annotate("error", "PARITY charset - the fixture wrote no row", ["ours.tsv is empty"])
        return 1

    by_file: dict[str, dict[str, str]] = {}
    for name, charset, code_page, key, text in rows:
        by_file.setdefault(name, {})[key] = text
        by_file[name]["_charset"] = charset
        by_file[name]["_code_page"] = code_page

    failures: list[str] = []
    counts: Counter[str] = Counter()
    iconv_missing = False
    for name in sorted(by_file):
        values = by_file[name]
        charset = values["_charset"]
        written = values.get("written", "|").split("|")
        if len(written) != 2:
            failures.append(f"{name}: the fixture wrote no text to compare with")
            continue
        if values.get(COMMENT, "") != written[0]:
            failures.append(f"{name} ({charset}): the comment reads back as {values.get(COMMENT)!r}, "
                            f"written {written[0]!r}")
            counts["round trip differs"] += 1
        else:
            counts["round trip"] += 1
        if values.get(TITLE, "") != written[1]:
            failures.append(f"{name} ({charset}): the title reads back as {values.get(TITLE)!r}, "
                            f"written {written[1]!r}")
        path = work / name
        tool, tool_code, tool_errors = printed(args.exiv2, path, charset, runner)
        if tool_code != 0 and NO_ICONV in (tool_errors or ""):
            iconv_missing = True
            break
        if tool_code != 0:
            failures.append(f"{name}: the tool failed (exit {tool_code}): {(tool_errors or '').strip()[:300]}")
            continue
        if COMMENT not in tool:
            failures.append(f"{name}: the tool does not print {COMMENT}")
        elif comment_text(tool[COMMENT]) != values.get(COMMENT, ""):
            failures.append(f"{name} ({charset}): the tool prints {comment_text(tool[COMMENT])!r}, "
                            f"this library {values.get(COMMENT)!r}")
            counts["comment differs"] += 1
        else:
            counts["comment matched"] += 1
        title, title_code, title_errors = printed(args.exiv2, path, None, runner)
        if title_code != 0:
            failures.append(f"{name}: the tool failed without -n (exit {title_code}): "
                            f"{(title_errors or '').strip()[:300]}")
        elif title.get(TITLE, "") != values.get(TITLE, ""):
            failures.append(f"{name}: the tool prints the title as {title.get(TITLE)!r}, this library "
                            f"{values.get(TITLE)!r}")
            counts["title differs"] += 1
        elif TITLE in title:
            counts["title matched"] += 1

    if iconv_missing:
        # Nothing can be compared with the tool here, but what was compared without it still counts:
        # a text the fixture wrote and this library does not read back is a failure on its own.
        if failures:
            annotate("error", "PARITY charset - texts the fixture wrote that do not read back", failures)
        annotate("notice", "PARITY charset - the pinned build has no iconv, so -n is not compared",
                 [f"The fixture wrote {len(by_file)} files and this library read {counts['round trip']} of "
                  "their comments and titles back as written; the matrix, its encodings and its round "
                  "trips are covered by the unit tests (MetadataTextTests). The pinned build cannot "
                  "convert text encodings, which is what -n is behind, so the tool's reading of them is "
                  "not compared here."])
        return 0 if not failures else 1

    annotate("error", "PARITY charset - texts the tool and this library read differently", failures)
    annotate("notice", f"PARITY charset - {len(by_file)} character sets; "
                       f"comments matched {counts['comment matched']}, titles matched {counts['title matched']}",
             [f"{key}: {count}" for key, count in sorted(counts.items())])
    return 1 if failures else 0


def main(argv=None, runner=run) -> int:
    """The comparison, with its own failures reported as annotations: a traceback in the log is not."""
    try:
        return compare(argv, runner)
    except SystemExit:
        raise
    except Exception:  # noqa: BLE001 - a gate that crashes has to say so where the run can be read
        annotate("error", "PARITY charset - the comparison itself failed",
                 [line for line in traceback.format_exc().splitlines() if line.strip()][-12:])
        return 1


if __name__ == "__main__":
    sys.exit(main())
