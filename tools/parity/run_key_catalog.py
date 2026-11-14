#!/usr/bin/env python3
"""Fail-closed gap check of the tag catalog against the pinned Exiv2 source tables.

    run_key_catalog.py SOURCE [--catalog PATH]

The catalog (``src/MetadataSharp.Core/tag-catalog-data.json``) is the set of keys this library can
name: ``Exif.<group>.<name>`` for the standard directories and for every vendor group of a
MakerNote. The reference is the pinned Exiv2 checkout, read as text and never configured, built or
run: its ``groupInfo[]`` table maps each ``IfdId`` to the group name it prints and to the function
that returns the tag table of that group, and every entry of that table is one key.

A key the reference has and the catalog does not is a failure: the comparison exists so that a tag
the pinned corpus never exercises cannot stay unnamed here. A key only the catalog has is reported,
not failed: this library keeps a few exact keys the reference's own table comments out
(``Exif.Canon.Categories``, ``Exif.Canon.PictureStylePC``, ``Exif.Canon.FilterInfo``), and a local
name for a tag the reference leaves as ``0x``nnnn is a name, not a loss.

Names in parentheses are the end-of-table markers of the reference (``(UnknownCanonCsTag)``), not
keys: they are skipped on both sides. Commented-out entries are not part of a table: comments are
removed before the tables are read, which is why the five Canon tags Exiv2 0.28.9 leaves commented
out are not expected here.

The run fails as well when the reference itself cannot be read as expected: an unresolved
``tagList`` function, no table for a group, or a key count far below what the pinned version has
would make a green run meaningless.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from collections import Counter, OrderedDict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_CATALOG = ROOT / "src" / "MetadataSharp.Core" / "tag-catalog-data.json"
MINIMUM_KEYS = 6000
PLACEHOLDER = re.compile(r"\(\w*\)")


def strip_comments(text: str) -> str:
    """The C++ source without its comments: a table entry that is commented out is not a key."""
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def group_info(tags: str) -> list[tuple[str, str, str, str]]:
    """(ifd id, ifd name, group name, tag-list function) per row of ``groupInfo[]``."""
    at = tags.index("constexpr GroupInfo groupInfo[] = {")
    body = tags[at:]
    body = body[: body.index("\n};")]
    return re.findall(r'\{(IfdId::\w+),\s*"([^"]*)",\s*"([^"]*)",\s*([\w:]+)\}', body)


def resolve(text: dict[str, str], function: str) -> tuple[str, str] | None:
    """The file and the table a ``tagList`` function returns."""
    pattern = re.compile(r"\b" + re.escape(function) + r"\s*\(\s*\)\s*(?:const\s*)?\{\s*return\s+([\w:]+)\s*;")
    for name, source in text.items():
        match = pattern.search(source)
        if match:
            return name, match.group(1)
    return None


def table_entries(source: str, table: str, scope: str | None) -> list[tuple[str, str]] | None:
    """The (tag, name) rows of a table, or None when the table is not in the file."""
    name = table.split("::")[-1]
    qualified = f"{scope}::{name}" if scope else (table if "::" in table else name)
    pattern = re.compile(r"TagInfo\s+" + re.escape(qualified) + r"\[\]\s*=\s*\{")
    match = pattern.search(source)
    if not match:
        pattern = re.compile(r"TagInfo\s+(?:[\w:]+::)?" + re.escape(name) + r"\[\]\s*=\s*\{")
        match = pattern.search(source)
    if not match:
        return None
    rest = source[match.end():]
    ends = [index for index in (rest.find("\n};"), rest.find("}};")) if index >= 0]
    if not ends:
        return None
    return re.findall(r'\{\s*(0x[0-9a-fA-F]+|\d+)\s*,\s*"([^"]*)"', rest[: min(ends)])


def reference_keys(source: Path) -> tuple[dict[str, tuple[str, str]], list[str]]:
    """``Exif.group.name`` -> (tag, name) of every table entry, and what could not be read."""
    directory = source / "src"
    if not (directory / "tags_int.cpp").is_file():
        raise SystemExit(f"{source} is not an Exiv2 checkout: src/tags_int.cpp is missing")
    text = {path.name: strip_comments(path.read_text(encoding="utf-8", errors="replace"))
            for path in list(directory.glob("*.cpp")) + list(directory.glob("*.hpp"))}
    keys: dict[str, tuple[str, str]] = OrderedDict()
    unresolved: list[str] = []
    for ifd, _ifd_name, group, function in group_info(text["tags_int.cpp"]):
        if function == "nullptr":
            continue
        found = resolve(text, function)
        if found is None:
            unresolved.append(f"{group}: {function} returns no table this script could find")
            continue
        file, table = found
        scope = function.rsplit("::", 1)[0] if "::" in function else None
        entries = table_entries(text[file], table, scope)
        if entries is None:
            unresolved.append(f"{group}: table {table} of {file} is not a tag table")
            continue
        for tag, name in entries:
            if tag.lower() in ("0xffff", "65535") or PLACEHOLDER.fullmatch(name):
                continue
            keys[f"Exif.{group}.{name}"] = (tag, name)
    return keys, unresolved


def catalog_keys(path: Path) -> dict[str, tuple[str, str]]:
    data = json.loads(path.read_text(encoding="utf-8"))
    keys: dict[str, tuple[str, str]] = OrderedDict()
    for group in data["exifGroups"]:
        for tag in group["tags"]:
            for name in tag["names"]:
                if PLACEHOLDER.fullmatch(name):
                    continue  # the end-of-table marker the catalog copied from the reference
                keys[f"Exif.{group['name']}.{name}"] = (str(tag["id"]), name)
    return keys


def annotate(level: str, title: str, lines: list[str], size: int = 6000) -> None:
    """One GitHub annotation of up to `size` characters, the rest counted."""
    if not lines:
        return
    body, kept = [], 0
    for line in lines:
        if kept + len(line) + 1 > size and body:
            body.append(f"... and {len(lines) - len(body)} more")
            break
        body.append(line)
        kept += len(line) + 1
    text = "\n".join(body).replace("%", "%25").replace("\r", " ").replace("\n", "%0A")
    print(f"::{'error' if level == 'error' else 'notice'} title={title}::{text}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("source", help="the pinned Exiv2 checkout")
    parser.add_argument("--catalog", default=str(DEFAULT_CATALOG))
    args = parser.parse_args(argv)

    reference, unresolved = reference_keys(Path(args.source))
    catalog = catalog_keys(Path(args.catalog))
    if unresolved:
        annotate("error", "PARITY tag keys - the reference could not be read", unresolved)
        return 1
    if len(reference) < MINIMUM_KEYS:
        annotate("error", "PARITY tag keys - the reference holds too few keys to judge",
                 [f"{len(reference)} keys read, at least {MINIMUM_KEYS} expected"])
        return 1

    missing = sorted(set(reference) - set(catalog))
    extra = sorted(set(catalog) - set(reference))
    annotate("error", "PARITY tag keys - keys the reference has and this library cannot name", missing)
    annotate("notice", f"PARITY tag keys - {len(extra)} local-only keys (a name for a tag the reference "
                       "leaves as 0x, or a key its own table comments out)", extra[:40])

    shared = sorted(set(reference) & set(catalog))
    differs = [f"{key}: reference {reference[key][0]}, catalog {catalog[key][0]}"
               for key in shared if _number(reference[key][0]) != _number(catalog[key][0])]
    annotate("notice", "PARITY tag keys - numbers that differ (split tables and reused groups make "
                       "these dependent on the entry)", differs[:30])

    groups = Counter(key.split(".")[1] for key in reference)
    annotate("notice", f"PARITY tag keys - {len(reference)} reference keys in {len(groups)} groups; "
                       f"{len(catalog)} catalog keys; {len(missing)} missing; {len(extra)} local-only; "
                       f"{len(differs)} numbers differ",
             [f"{group}: {count}" for group, count in sorted(groups.items())])
    return 1 if missing else 0


def _number(tag: str) -> int | str:
    try:
        return int(tag, 16) if tag.lower().startswith("0x") else int(tag)
    except ValueError:
        return tag


if __name__ == "__main__":
    sys.exit(main())
