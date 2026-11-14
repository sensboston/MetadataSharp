"""Offline checks of the pinned RAW corpus manifest and its downloader.

The corpus itself is never in the repository, so what can be checked here is the manifest and the
rules the downloader holds it to: every entry names a free-licensed source, a local name, a camera
and a format, and a file whose bytes are not the pinned ones is refused and deleted rather than kept
for a gate to read.
"""
from __future__ import annotations

import importlib.util
import json
import sys
import tempfile
import unittest
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("raw_corpus", ROOT / "tools/parity/raw_corpus.py")
raw_corpus = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = raw_corpus  # dataclasses look their module up while the class is built
SPEC.loader.exec_module(raw_corpus)

FORMATS = {"CRW", "CR2", "NEF", "ARW", "ORF", "PEF", "RW2", "SR2", "SRW", "DNG", "MRW", "RAF"}
# raw.pixls.us publishes exactly one .SR2 file (the Sony DSC-R1 is the camera that wrote that
# format), so that format cannot reach two cameras from the only free-licensed source there is.
KNOWN_SINGLE = {"SR2": 1}


class ManifestTests(unittest.TestCase):
    def setUp(self) -> None:
        self.document = raw_corpus.load()

    def test_every_entry_names_a_cc0_source_and_never_a_local_file(self) -> None:
        self.assertEqual("CC0-1.0", self.document["license"])
        for entry in raw_corpus.entries(self.document):
            self.assertTrue(entry.url.startswith("https://raw.pixls.us/data/"), entry.url)
            self.assertTrue(entry.camera, entry.name)
            self.assertEqual(entry.name, Path(entry.name).name)
            # The corpus is downloaded, never committed: nothing of it may live in the tree.
            self.assertFalse((ROOT / "tests/fixtures" / entry.name).exists(), entry.name)

    def test_each_named_format_holds_more_than_one_camera(self) -> None:
        cameras = Counter(entry.format for entry in raw_corpus.entries(self.document))
        self.assertEqual(FORMATS, set(cameras))
        for name, count in cameras.items():
            self.assertGreaterEqual(count, KNOWN_SINGLE.get(name, 2), f"{name} holds {count} file(s)")
        self.assertEqual(FORMATS, set(cameras) & FORMATS)
        # A camera may supply two formats (a Pentax K10D writes PEF and DNG); it still counts once.
        per_format = {}
        for entry in raw_corpus.entries(self.document):
            per_format.setdefault(entry.format, set()).add(entry.camera)
        for name, models in per_format.items():
            self.assertGreaterEqual(len(models), KNOWN_SINGLE.get(name, 2), f"{name} covers {sorted(models)}")

    def test_a_manifest_entry_without_a_source_is_refused(self) -> None:
        document = json.loads(json.dumps(self.document))
        name = next(iter(document["files"]))
        document["files"][name]["url"] = "https://example.invalid/sample.cr2"
        with self.assertRaises(raw_corpus.CorpusError):
            raw_corpus.load_from(document)

    def test_a_local_name_that_is_a_path_is_refused(self) -> None:
        document = json.loads(json.dumps(self.document))
        name = next(iter(document["files"]))
        document["files"]["nested/" + name] = document["files"].pop(name)
        with self.assertRaises(raw_corpus.CorpusError):
            raw_corpus.load_from(document)


class DownloadTests(unittest.TestCase):
    def setUp(self) -> None:
        self.document = json.loads(json.dumps(raw_corpus.load()))
        for item in self.document["files"].values():
            item["bytes"] = 4
            item["sha256"] = raw_corpus.hashlib.sha256(b"good").hexdigest()
        self.entry = raw_corpus.entries(self.document)[0]

    def test_a_file_that_does_not_match_its_pin_is_deleted(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            destination = Path(directory)
            original = raw_corpus._fetch
            raw_corpus._fetch = lambda url, target: target.write_bytes(b"evil")
            try:
                outcomes = raw_corpus.download(destination, self._one())
            finally:
                raw_corpus._fetch = original
            self.assertEqual("mismatch", outcomes["files"][0]["status"])
            self.assertFalse((destination / self.entry.name).exists())
            self.assertTrue(raw_corpus.verify(destination, self._one()))

    def test_a_file_that_matches_its_pin_is_kept_and_verified(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            destination = Path(directory)
            original = raw_corpus._fetch
            raw_corpus._fetch = lambda url, target: target.write_bytes(b"good")
            try:
                outcomes = raw_corpus.download(destination, self._one())
            finally:
                raw_corpus._fetch = original
            self.assertEqual("downloaded", outcomes["files"][0]["status"])
            self.assertEqual([], raw_corpus.verify(destination, self._one()))

    def test_an_unpinned_entry_can_be_downloaded_but_never_verified(self) -> None:
        document = self._one()
        name = next(iter(document["files"]))
        document["files"][name]["sha256"] = None
        with tempfile.TemporaryDirectory() as directory:
            destination = Path(directory)
            original = raw_corpus._fetch
            raw_corpus._fetch = lambda url, target: target.write_bytes(b"good")
            try:
                outcomes = raw_corpus.download(destination, document)
            finally:
                raw_corpus._fetch = original
            self.assertEqual("unpinned", outcomes["files"][0]["status"])
            self.assertTrue((destination / self.entry.name).exists())
            self.assertTrue(raw_corpus.verify(destination, document))

    def test_pin_records_what_the_download_measured(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            destination = Path(directory)
            document = self._one()
            (destination / self.entry.name).write_bytes(b"good")
            updated = raw_corpus.pin(destination, write=False, document=document, path=Path(directory) / "unused.json")
            name = next(iter(updated["files"]))
            self.assertEqual(4, updated["files"][name]["bytes"])
            self.assertEqual(raw_corpus.hashlib.sha256(b"good").hexdigest(), updated["files"][name]["sha256"])

    def test_the_pin_report_fits_in_one_workflow_command(self) -> None:
        import contextlib, io
        outcomes = {"files": [{"name": f"s{i}.rw2", "bytes": i, "sha256": f"{i:064x}", "status": "downloaded"}
                              for i in range(23)]}
        buffer = io.StringIO()
        with contextlib.redirect_stdout(buffer):
            raw_corpus.annotate(outcomes)
        lines = [line for line in buffer.getvalue().splitlines() if line.strip()]
        self.assertEqual(1, len(lines), "a workflow command is one line")
        self.assertIn("%0A", lines[0])
        self.assertNotIn("\n", lines[0])

    def _one(self) -> dict:
        return {"license": self.document["license"], "provenance": self.document["provenance"],
                "files": {self.entry.name: self.document["files"][self.entry.name]}}


if __name__ == "__main__":
    unittest.main()
