#!/usr/bin/env python3
"""The pinned RAW corpus: downloaded from a free-licensed source, pinned by SHA-256, never committed.

The differential gates read camera raw files that the pinned upstream checkout happens to hold, which
is one to three files for most raw formats and none at all for some. This module fetches a wider
corpus from raw.pixls.us, whose upload form is a CC0 1.0 dedication, and keeps it out of the
repository:

  * ``tools/parity/raw-corpus.json`` records, for every file, the exact URL, the byte size, the
    SHA-256 and the camera it came from. Nothing is downloaded by the repository itself.
  * ``download`` fetches only what the manifest names, verifies the size and the hash, and refuses to
    keep a file that does not match its pin. An entry whose pin is still ``null`` is a file that has
    been chosen but not yet measured: it is downloaded and kept, and its measured pin is printed so
    that it can be written down. ``verify`` refuses an unpinned entry, so a run cannot pass on one.
  * ``pin`` prints (or, with ``--write``, records) the measured size and hash of every downloaded
    file. It is the only way the numbers in the manifest are produced.

Usage:
    raw_corpus.py download DEST [--report PATH]
    raw_corpus.py verify DEST
    raw_corpus.py pin DEST [--write]
    raw_corpus.py list
"""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = Path(__file__).with_name("raw-corpus.json")
CHUNK = 1024 * 1024


class CorpusError(Exception):
    """The manifest is malformed, or the corpus does not match it."""


@dataclass(frozen=True)
class Entry:
    """One pinned sample."""

    name: str
    url: str
    bytes: int | None
    sha256: str | None
    camera: str
    format: str
    source_file: str

    @property
    def slug(self) -> str:
        return self.name.rsplit(".", 1)[0]


def load(path: Path = MANIFEST) -> dict:
    """The manifest on disk, after the checks below."""
    return check(json.loads(path.read_text(encoding="utf-8")))


def load_from(document: dict) -> dict:
    """A manifest that is already in memory, checked the same way as one on disk."""
    return check(document)


def check(document: dict) -> dict:
    """The manifest, after checking the parts every entry needs."""
    if not document.get("license"):
        raise CorpusError("the manifest has no license")
    files = document.get("files")
    if not isinstance(files, dict) or not files:
        raise CorpusError("the manifest holds no files")
    for name, item in files.items():
        for field in ("url", "camera", "format", "source_file"):
            if not item.get(field):
                raise CorpusError(f"{name}: no {field}")
        if not item["url"].startswith("https://raw.pixls.us/"):
            raise CorpusError(f"{name}: the source is not raw.pixls.us: {item['url']}")
        if Path(name).name != name or Path(name).suffix == "":
            raise CorpusError(f"{name}: the local name has to be a plain file name with an extension")
        expected = item["source_file"].lower()
        if not name.lower().endswith(Path(expected).suffix):
            raise CorpusError(f"{name}: the local extension does not match {item['source_file']}")
    return document


def entries(document: dict | None = None) -> list[Entry]:
    """Every entry of the manifest, ordered by local name."""
    document = document if document is not None else load()
    return [Entry(name=name, url=item["url"], bytes=item.get("bytes"), sha256=item.get("sha256"),
                  camera=item["camera"], format=item["format"], source_file=item["source_file"])
            for name, item in sorted(document["files"].items())]


def digest(path: Path) -> str:
    """The SHA-256 of a file, read in blocks so a 30 MB sample is not held twice."""
    sha = hashlib.sha256()
    with path.open("rb") as stream:
        while block := stream.read(CHUNK):
            sha.update(block)
    return sha.hexdigest()


def _fetch(url: str, target: Path) -> None:
    """Download one URL. curl is used because the runners have it and it retries."""
    partial = target.with_suffix(target.suffix + ".part")
    if partial.exists():
        partial.unlink()
    curl = shutil.which("curl")
    if curl is None:
        raise CorpusError("curl is not available to download the corpus")
    command = [curl, "-fsSL", "--retry", "5", "--retry-delay", "5", "--max-time", "600",
               "-o", str(partial), url]
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        message = (result.stderr or result.stdout).strip().splitlines()
        raise CorpusError(f"download failed ({result.returncode}): {message[-1] if message else 'no output'}")
    partial.replace(target)


def _check(entry: Entry, path: Path) -> tuple[str | None, dict]:
    """Why a downloaded file is not the pinned one, with what was measured."""
    measured = {"bytes": path.stat().st_size, "sha256": digest(path)}
    if entry.bytes is not None and measured["bytes"] != entry.bytes:
        return f"expected {entry.bytes} bytes, got {measured['bytes']}", measured
    if entry.sha256 is not None and measured["sha256"] != entry.sha256:
        return f"expected sha256 {entry.sha256}, got {measured['sha256']}", measured
    return None, measured


def download(destination: Path, document: dict | None = None) -> dict:
    """Fetch every entry into ``destination`` and report what happened to each one.

    A file that is already there and matches its pin is left alone, so a repeated run costs nothing.
    A file that does not match a pinned size or hash is deleted rather than kept: a gate must never
    read bytes nobody pinned. An entry whose pin is ``null`` is kept and its measured pin reported.
    """
    document = document if document is not None else load()
    destination.mkdir(parents=True, exist_ok=True)
    outcomes = []
    for entry in entries(document):
        path = destination / entry.name
        outcome = {"name": entry.name, "camera": entry.camera, "format": entry.format,
                   "url": entry.url, "pinned": entry.sha256 is not None}
        if path.is_file():
            problem, measured = _check(entry, path)
            if problem is None:
                outcome |= {"status": "cached"} | measured
                outcomes.append(outcome)
                continue
            if entry.sha256 is not None:
                path.unlink()
        try:
            _fetch(entry.url, path)
        except CorpusError as failure:
            outcome |= {"status": "failed", "detail": str(failure)}
            outcomes.append(outcome)
            continue
        problem, measured = _check(entry, path)
        outcome |= measured
        if entry.sha256 is None:
            # No hash to compare against: keep the bytes, report the pin so it can be written down.
            outcome |= {"status": "unpinned", "detail": problem or f"bytes={measured['bytes']}"}
        elif problem is not None:
            path.unlink()
            outcome |= {"status": "mismatch", "detail": problem}
        else:
            outcome |= {"status": "downloaded"}
        outcomes.append(outcome)
    return {"provenance": document.get("provenance"), "license": document.get("license"),
            "files": outcomes}


def verify(destination: Path, document: dict | None = None) -> list[str]:
    """Every way the directory fails to match the manifest. An empty list is a pass."""
    document = document if document is not None else load()
    problems: list[str] = []
    for entry in entries(document):
        path = destination / entry.name
        if entry.sha256 is None or entry.bytes is None:
            problems.append(f"{entry.name}: the manifest still has no pinned size or hash")
            continue
        if not path.is_file():
            problems.append(f"{entry.name}: not downloaded")
            continue
        problem, _ = _check(entry, path)
        if problem is not None:
            problems.append(f"{entry.name}: {problem}")
    return problems


def pin(destination: Path, write: bool = False, document: dict | None = None,
        path: Path = MANIFEST) -> dict:
    """Write the measured size and hash of every downloaded file into the manifest."""
    document = document if document is not None else load()
    updated = json.loads(json.dumps(document))
    missing: list[str] = []
    for entry in entries(document):
        source = destination / entry.name
        if not source.is_file():
            missing.append(entry.name)
            continue
        updated["files"][entry.name]["bytes"] = source.stat().st_size
        updated["files"][entry.name]["sha256"] = digest(source)
    if missing:
        raise CorpusError("not downloaded: " + ", ".join(missing))
    if write:
        path.write_text(json.dumps(updated, indent=2) + "\n", encoding="utf-8")
    return updated


def annotate(outcomes: dict) -> None:
    """Print what a workflow run needs: the pin of each file, and every failure as an error."""
    for item in outcomes["files"]:
        if item.get("sha256"):
            print(f"::notice title=raw corpus {item['status']}::{item['name']} bytes={item['bytes']} "
                  f"sha256={item['sha256']} camera={item['camera']}")
        if item["status"] in ("failed", "mismatch"):
            print(f"::error title=raw corpus {item['status']}::{item['name']} {item.get('detail', '')}")


def describe(outcomes: dict) -> str:
    """A short text summary: what was fetched, what came from the cache, what failed."""
    counts: dict[str, int] = {}
    formats: dict[str, int] = {}
    for item in outcomes["files"]:
        counts[item["status"]] = counts.get(item["status"], 0) + 1
        formats[item["format"]] = formats.get(item["format"], 0) + 1
    total = sum(item.get("bytes", 0) for item in outcomes["files"] if item.get("bytes"))
    parts = ", ".join(f"{status} {count}" for status, count in sorted(counts.items()))
    layout = ", ".join(f"{name} {count}" for name, count in sorted(formats.items()))
    return (f"raw corpus: {len(outcomes['files'])} files, {total / (1024 * 1024):.1f} MiB ({parts}); "
            f"formats: {layout}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Download and verify the pinned RAW corpus.")
    parser.add_argument("command", choices=("download", "verify", "pin", "list"))
    parser.add_argument("destination", nargs="?", type=Path, help="where the corpus is kept")
    parser.add_argument("--report", type=Path, help="write the run report here")
    parser.add_argument("--write", action="store_true", help="pin: record the measured values in the manifest")
    args = parser.parse_args(argv)
    document = load()
    if args.command == "list":
        for entry in entries(document):
            state = "pinned" if entry.sha256 else "unpinned"
            print(f"{entry.format:4} {entry.camera:28} {entry.name:36} {state} {entry.url}")
        return 0
    if args.destination is None:
        parser.error(f"{args.command} needs a destination directory")
    if args.command == "download":
        outcomes = download(args.destination, document)
        annotate(outcomes)
        text = describe(outcomes)
        print(text)
        if args.report:
            args.report.parent.mkdir(parents=True, exist_ok=True)
            args.report.write_text(json.dumps(outcomes, indent=2) + "\n", encoding="utf-8")
        failed = [item["name"] for item in outcomes["files"] if item["status"] in ("failed", "mismatch")]
        if failed:
            print("::error title=raw corpus::could not fetch " + ", ".join(failed))
            return 1
        return 0
    if args.command == "verify":
        problems = verify(args.destination, document)
        for problem in problems:
            print(f"::error title=raw corpus::{problem}")
        print(f"raw corpus verified: {len(entries(document))} files, {len(problems)} problems")
        return 1 if problems else 0
    updated = pin(args.destination, write=args.write, document=document)
    print(json.dumps(updated["files"], indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except CorpusError as failure:
        print(f"::error title=raw corpus harness::{failure}", file=sys.stderr)
        sys.exit(2)
