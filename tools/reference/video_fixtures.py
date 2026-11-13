#!/usr/bin/env python3
"""Deterministic, project-authored tiny video-container inputs; no media samples or copied metadata code."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import sys


def chunk(tag: bytes, payload: bytes) -> bytes:
    assert len(tag) == 4
    return tag + len(payload).to_bytes(4, "little") + payload + (b"\0" if len(payload) & 1 else b"")


def riff(form: bytes, body: bytes) -> bytes:
    payload = form + body
    return b"RIFF" + len(payload).to_bytes(4, "little") + payload


def atom(tag: bytes, payload: bytes, *, extended: bool = False, zero: bool = False) -> bytes:
    assert len(tag) == 4
    if zero:
        return b"\0\0\0\0" + tag + payload
    if extended:
        return (1).to_bytes(4, "big") + tag + (16 + len(payload)).to_bytes(8, "big") + payload
    return (8 + len(payload)).to_bytes(4, "big") + tag + payload


def vint_size(value: int) -> bytes:
    for width in range(1, 9):
        if value < (1 << (7 * width)) - 1:
            return ((1 << (7 * width)) | value).to_bytes(width, "big")
    raise ValueError(value)


def ebml_element(identifier: bytes, payload: bytes, *, unknown: bool = False) -> bytes:
    return identifier + (b"\xff" if unknown else vint_size(len(payload))) + payload


def asf_object(guid: bytes, payload: bytes) -> bytes:
    assert len(guid) == 16
    return guid + (24 + len(payload)).to_bytes(8, "little") + payload


def fixtures() -> dict[str, bytes]:
    file_props_guid = bytes.fromhex("a1dcab8c47a9cf118ee400c00c205365")
    content_guid = bytes.fromhex("3326b2758e66cf11a6d900aa0062ce6c")
    file_id = bytes.fromhex("00112233445566778899aabbccddeeff")
    file_props = (file_id + (123456).to_bytes(8, "little") + (0).to_bytes(8, "little") +
                  (12).to_bytes(8, "little") + (50000000).to_bytes(8, "little") +
                  (0).to_bytes(8, "little") + (3000).to_bytes(8, "little") +
                  (2).to_bytes(4, "little") + (1024).to_bytes(4, "little") +
                  (2048).to_bytes(4, "little") + (800000).to_bytes(4, "little"))
    title = "Video fixture".encode("utf-16le")
    content = (len(title).to_bytes(2, "little") + b"\0\0\0\0\0\0\0\0" + title)
    children = asf_object(file_props_guid, file_props) + asf_object(content_guid, content)
    asf_header_guid = bytes.fromhex("3026b2758e66cf11a6d900aa0062ce6c")
    asf_payload = (2).to_bytes(4, "little") + b"\x01\x02" + children
    asf = asf_object(asf_header_guid, asf_payload)

    ebml_payload = (ebml_element(bytes.fromhex("4286"), b"\x01") +
                    ebml_element(bytes.fromhex("42f7"), b"\x01") +
                    ebml_element(bytes.fromhex("42f2"), b"\x04") +
                    ebml_element(bytes.fromhex("42f3"), b"\x08") +
                    ebml_element(bytes.fromhex("4282"), b"webm") +
                    ebml_element(bytes.fromhex("4287"), b"\x04") +
                    ebml_element(bytes.fromhex("4285"), b"\x02"))
    ebml_header = ebml_element(bytes.fromhex("1a45dfa3"), ebml_payload)
    info = (ebml_element(bytes.fromhex("2ad7b1"), (1_000_000).to_bytes(3, "big")) +
            ebml_element(bytes.fromhex("4489"), __import__("struct").pack(">d", 1200.0)) +
            ebml_element(bytes.fromhex("7ba9"), b"Fixture WebM"))
    segment = ebml_element(bytes.fromhex("18538067"), ebml_element(bytes.fromhex("1549a966"), info))
    webm = ebml_header + segment

    ftyp = atom(b"ftyp", b"isom" + (512).to_bytes(4, "big") + b"isommp42")
    mvhd = bytearray(100)
    mvhd[0] = 0
    mvhd[12:16] = (1000).to_bytes(4, "big")
    mvhd[16:20] = (12000).to_bytes(4, "big")
    mvhd[20:24] = (1 << 16).to_bytes(4, "big")
    mvhd[24:26] = (1 << 8).to_bytes(2, "big")
    mvhd_atom = atom(b"mvhd", bytes(mvhd))
    qt = ftyp + atom(b"moov", mvhd_atom + atom(b"free", b"authored"))

    fmt = ((1).to_bytes(2, "little") + (2).to_bytes(2, "little") + (44100).to_bytes(4, "little") +
           (176400).to_bytes(4, "little") + (4).to_bytes(2, "little") + (16).to_bytes(2, "little"))
    wav = riff(b"WAVE", chunk(b"fmt ", fmt) + chunk(b"data", b"\0\0" * 16))

    avih = (33333).to_bytes(4, "little") + (100000).to_bytes(4, "little") + (0).to_bytes(4, "little") + (0).to_bytes(4, "little")
    avih += (30).to_bytes(4, "little") + (0).to_bytes(4, "little") + (1).to_bytes(4, "little") + (0).to_bytes(4, "little")
    avih += (320).to_bytes(4, "little") + (240).to_bytes(4, "little") + bytes(16)
    strh = b"vids" + b"DIB " + bytes(8) + (1).to_bytes(4, "little") + (30).to_bytes(4, "little")
    strh += bytes(4) + (30).to_bytes(4, "little") + bytes(4) + (30).to_bytes(4, "little") + bytes(4) + (0xFFFFFFFF).to_bytes(4, "little")
    strh += bytes(16)
    strf = (40).to_bytes(4, "little") + (320).to_bytes(4, "little", signed=True) + (240).to_bytes(4, "little", signed=True)
    strf += (1).to_bytes(2, "little") + (24).to_bytes(2, "little") + b"DIB " + (320 * 240 * 3).to_bytes(4, "little") + bytes(16)
    hdrl = chunk(b"avih", avih) + chunk(b"LIST", b"strl" + chunk(b"strh", strh) + chunk(b"strf", strf))
    avi = riff(b"AVI ", chunk(b"LIST", b"hdrl" + hdrl) + chunk(b"LIST", b"movi"))

    return {
        "authored-asf.asf": asf,
        "authored-webm.webm": webm,
        "authored-quicktime.mp4": qt,
        "authored-wave.wav": wav,
        "authored-avi.avi": avi,
        "malformed-asf.asf": asf[:16] + (0xFFFFFFFFFFFFFFFF).to_bytes(8, "little") + asf[24:],
        "malformed-ebml.webm": b"\x1a\x45\xdf\xa3\x01\x01\x00\x00\x00\x00\x00\x00",
        "malformed-quicktime.mov": atom(b"ftyp", b"isom" + bytes(4)) + b"\0\0\0\1moov" + (0xFFFFFFFFFFFFFFFF).to_bytes(8, "big"),
        "malformed-riff.wav": b"RIFF\xff\xff\xff\xffWAVEfmt \xff\xff\xff\xff",
    }


def main() -> None:
    if len(sys.argv) != 3 or sys.argv[1] not in {"generate", "verify"}:
        raise SystemExit("usage: video_fixtures.py generate|verify OUTPUT_DIRECTORY")
    mode, root = sys.argv[1], Path(sys.argv[2])
    valid = root / "valid"
    malformed = root / "malformed"
    expected = fixtures()
    contents: dict[str, bytes] = {}
    for name, data in expected.items():
        target_dir = malformed if name.startswith("malformed-") else valid
        target = target_dir / name.removeprefix("malformed-") if name.startswith("malformed-") else target_dir / name
        contents[str(target.relative_to(root)).replace("\\", "/")] = data
    if mode == "generate":
        for name, data in contents.items():
            path = root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
    for name, data in contents.items():
        path = root / name
        if not path.is_file() or path.read_bytes() != data:
            raise SystemExit(f"fixture differs: {name}")
    manifest = {"provenance": "Authored by tools/reference/video_fixtures.py; no media, copied files, or copied parser code.",
                "files": {name: {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()} for name, data in sorted(contents.items())}}
    manifest_path = root / "manifest.json"
    if mode == "generate":
        root.mkdir(parents=True, exist_ok=True)
        manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    elif not manifest_path.is_file() or json.loads(manifest_path.read_text(encoding="utf-8")) != manifest:
        raise SystemExit("video fixture manifest differs")
    print(f"video fixtures verified: {len(contents)} files")


if __name__ == "__main__":
    main()
