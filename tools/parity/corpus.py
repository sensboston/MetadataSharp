"""Corpus discovery for the differential parity harness.

Two corpora are read, and neither one is copied into this repository:

``exiv2``
    The upstream test tree of the pinned commit. Its ``test/data`` directory holds the sample files
    and ``test/data/test_reference_files`` holds what the reference tool printed for them at that
    commit, so the expectation and the input travel together. A reference whose input cannot be
    found is reported rather than guessed at - upstream keeps a handful of expectations for
    synthetic inputs that no longer exist.

``generated``
    Fixtures this repository owns. The expectation for these is produced in the same CI job by the
    pinned reference build, so a disagreement is either a library gap or a change in the pinned
    tool's output, never a stale committed file.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path

#: Extensions tried when a reference's basename does not name an existing input file. Upstream names
#: some expectations after a converted file rather than the one on disk.
FALLBACK_EXTENSIONS = (
    ".jpg", ".jpeg", ".tiff", ".tif", ".png", ".webp", ".psd", ".jp2", ".jxl", ".avif", ".heic",
    ".crw", ".raf", ".dng", ".cr2", ".nef", ".orf", ".rw2", ".arw", ".sr2", ".srw", ".pef", ".mrw",
    ".mp4", ".mov", ".avi", ".asf", ".wav", ".xmp", ".exv", ".eps", ".pgf", ".dcp",
)
SKIP_SUFFIXES = (".json", ".md", ".txt", ".py", ".sh", ".out")


class CorpusError(Exception):
    """A corpus directory is missing or empty, which means the run is set up wrongly."""


@dataclass
class Candidate:
    """One file to read, with the expectation to compare it against."""

    label: str
    path: Path
    corpus: str
    reference: Path | None = None
    format: str = field(default="")
    notes: list[str] = field(default_factory=list)

    def __post_init__(self) -> None:
        if not self.format:
            self.format = self.path.suffix.lstrip(".").lower() or "none"

    @property
    def uid(self) -> str:
        """Unique within a run: two corpora can hold files with the same basename."""
        return f"{self.corpus}:{self.label}"


def discover_exiv2(tree: Path) -> tuple[list[Candidate], list[str]]:
    """Pair every expected-output file under ``tree/test/data`` with its input.

    Returns the candidates and the names of expectations whose input is not in the tree.
    """
    data = tree / "test" / "data"
    references = data / "test_reference_files"
    if not references.is_dir():
        raise CorpusError(f"no expected-output directory at {references}")
    candidates: list[Candidate] = []
    unmatched: list[str] = []
    for out in sorted(references.iterdir()):
        if out.suffix != ".out" or not out.is_file():
            continue
        stem = out.name[: -len(".out")]
        found = _resolve_input(stem, data)
        if found is None:
            unmatched.append(stem)
            continue
        candidates.append(Candidate(label=stem, path=found, corpus="exiv2", reference=out, format=_format_of(found)))
    if not candidates:
        raise CorpusError(f"{references} holds no usable expectation")
    return candidates, unmatched


def _resolve_input(stem: str, data: Path) -> Path | None:
    exact = data / stem
    if exact.is_file():
        return exact
    base = stem[: stem.rfind(".")] if "." in stem else stem
    for extension in FALLBACK_EXTENSIONS:
        candidate = data / (base + extension)
        if candidate.is_file():
            return candidate
    return None


def _format_of(path: Path) -> str:
    suffix = path.suffix.lstrip(".").lower()
    return {"tif": "tiff", "jpeg": "jpg", "cr2": "raw", "nef": "raw", "orf": "raw", "rw2": "raw",
            "arw": "raw", "sr2": "raw", "srw": "raw", "pef": "raw", "mrw": "raw", "raf": "raw",
            "crw": "raw", "dng": "dng"}.get(suffix, suffix or "none")


def discover_generated(*directories: Path) -> list[Candidate]:
    """Every non-bookkeeping file under the given fixture directories.

    Manifests and provenance ledgers describe the fixtures; they are not metadata samples, so they
    are skipped by suffix rather than by an allowlist of image extensions.
    """
    candidates: list[Candidate] = []
    seen: set[Path] = set()
    for directory in directories:
        if not directory.is_dir():
            raise CorpusError(f"missing fixture directory {directory}")
        for path in sorted(directory.rglob("*")):
            if not path.is_file() or path.suffix.lower() in SKIP_SUFFIXES:
                continue
            if path in seen:
                continue
            seen.add(path)
            candidates.append(Candidate(label=path.name, path=path, corpus="generated", format=_format_of(path)))
    if not candidates:
        raise CorpusError("no fixture files found in " + ", ".join(str(d) for d in directories))
    return candidates
