"""Emit concise Roslyn inventory findings as GitHub check annotations."""

import base64
import json
import sys
import zlib
from pathlib import Path


def emit(project: str, category: str, rows: list[str]) -> None:
    chunks: list[str] = []
    current: list[str] = []
    size = 0
    for row in rows:
        if current and size + len(row) + 1 > 5600:
            chunks.append("\n".join(current))
            current = []
            size = 0
        current.append(row)
        size += len(row) + 1
    if current:
        chunks.append("\n".join(current))

    for number, text in enumerate(chunks, start=1):
        encoded = base64.b64encode(zlib.compress(text.encode("utf-8"), 9)).decode("ascii")
        title = f"Roslyn baseline {project} {category} {number}/{len(chunks)}"
        print(f"::notice title={title}::{encoded}")


def main() -> int:
    if len(sys.argv) != 3:
        print("Usage: report.py PROJECT INVENTORY.json", file=sys.stderr)
        return 2
    project, report_path = sys.argv[1:]
    report = json.loads(Path(report_path).read_text(encoding="utf-8"))
    print(f"::notice title=Roslyn baseline {project} summary::{json.dumps(report['Summary'], separators=(',', ':'))}")

    public = []
    internal = []
    for entry in report["Declarations"]:
        location = f"{entry['Path']}:{entry['Line']}"
        if entry["PublicGaps"]:
            public.append(f"{location} {entry['Name']} missing {', '.join(entry['PublicGaps'])}")
        elif not entry["Public"] and entry["NonTrivial"] and not entry["PurposeComment"]:
            internal.append(f"{location} {entry['Kind']} {entry['Name']}")
    emit(project, "public", public)
    emit(project, "internal", internal)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
