"""Shared non-key-parity helpers for the preview, ICC, and soundness reports."""
from __future__ import annotations

import base64
import json
import subprocess
import zlib
from pathlib import Path
from typing import Any

import corpus as corpus_mod

CHUNK_LIMIT = 10
CHUNK_SIZE = 4000


class StructuralError(Exception):
    """The report run cannot be trusted because its input or tool failed structurally."""


def run_reference(reference_command: list[str], path: Path, *, require_success: bool = False) -> str:
    try:
        finished = subprocess.run(reference_command + [str(path)], capture_output=True, text=True,
                                 encoding="utf-8", errors="replace", timeout=300, check=False)
    except FileNotFoundError as error:
        raise StructuralError(f"the pinned reference tool is not runnable: {error}") from error
    except subprocess.TimeoutExpired as error:
        raise StructuralError(f"the pinned reference tool timed out on {path.name}") from error
    if require_success and finished.returncode != 0:
        detail = (finished.stderr or finished.stdout or "").strip().replace("\n", " ")[:400]
        raise StructuralError(f"pinned reference failed on {path.name} with {finished.returncode}: {detail}")
    return finished.stdout or ""


def emit_annotations(payload: dict[str, Any], title: str, word: str = "summary") -> int:
    raw = json.dumps(payload, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
    encoded = base64.b64encode(zlib.compress(raw, 9)).decode("ascii")
    parts = [encoded[i:i + CHUNK_SIZE] for i in range(0, len(encoded), CHUNK_SIZE)]
    if not parts or len(parts) > CHUNK_LIMIT:
        raise StructuralError(f"the {word} requires {len(parts)} annotations; allowed 1..{CHUNK_LIMIT}")
    for number, part in enumerate(parts, 1):
        print(f"::notice title={title} {word} {number}/{len(parts)}::{part}")
    return len(parts)
