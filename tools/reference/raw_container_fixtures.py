#!/usr/bin/env python3
"""MRW and RAF files authored from files of the pinned corpus, and its CRW files, for key parity.

    raw_container_fixtures.py CORPUS OUT

The pinned corpus has no MRW file with an expectation and only two RAF files of 120 bytes. This
writes, into OUT:

  * NAME.mrw for TIFF files of CORPUS: the header block of an MRW file, a PRD block, and a TTW block
    whose content is the TIFF file, whole;
  * NAME.raf for JPEG files of CORPUS: the header of a RAF file, the JPEG file, whole, and after it
    an authored TIFF structure of one tag (DocumentName);
  * a copy of every CRW file of CORPUS.

Nothing here is derived from a camera file of these formats: the containers are authored, and what
they hold are files of the corpus as they are. The names are kept short; the output maps them.
"""
import struct
import sys
from pathlib import Path

LIMIT = 300 * 1024
COUNT = 12


def mrw(tiff):
    prd = b"\0PRD" + struct.pack(">I", 24) + b"27730001" + bytes(16)
    ttw = b"\0TTW" + struct.pack(">I", len(tiff)) + tiff
    return b"\0MRM" + struct.pack(">I", len(prd) + len(ttw)) + prd + ttw + bytes(64)


def raf(jpeg, tiff):
    header = bytearray(108)
    header[:16] = b"FUJIFILMCCD-RAW "
    header[16:20] = b"0201"
    header[20:28] = b"FF389501"
    header[28:60] = b"X-T3".ljust(32, b"\0")
    struct.pack_into(">II", header, 84, 108, len(jpeg))
    struct.pack_into(">II", header, 100, 108 + len(jpeg), len(tiff))
    return bytes(header) + jpeg + tiff


def tiff_structure():
    """A TIFF structure of one directory with one tag, DocumentName."""
    text = b"raf structure\0"
    return b"II*\0" + struct.pack("<IH", 8, 1) + struct.pack("<HHII", 0x010D, 2, len(text), 26) + struct.pack("<I", 0) + text


def is_tiff(data):
    return data[:4] in (b"II*\0", b"MM\0*")


def main(argv=None):
    corpus, out = Path((argv or sys.argv)[1]), Path((argv or sys.argv)[2])
    out.mkdir(parents=True, exist_ok=True)
    names = []
    small = sorted(p for p in corpus.iterdir() if p.is_file() and p.stat().st_size <= LIMIT)

    tiffs = [p for p in small if p.suffix.lower() in (".tif", ".tiff") and is_tiff(p.read_bytes())][:COUNT]
    for number, path in enumerate(tiffs):
        (out / f"m{number:02d}.mrw").write_bytes(mrw(path.read_bytes()))
        names.append(f"m{number:02d}.mrw\t{path.name}")

    structure = tiff_structure()
    jpegs = [p for p in small if p.suffix.lower() == ".jpg" and p.name.startswith("exiv2-") and p.read_bytes()[:2] == b"\xff\xd8"][:COUNT]
    for number, path in enumerate(jpegs):
        (out / f"r{number:02d}.raf").write_bytes(raf(path.read_bytes(), structure))
        names.append(f"r{number:02d}.raf\t{path.name}")

    for number, path in enumerate(sorted(corpus.glob("*.crw"))):
        (out / f"c{number:02d}.crw").write_bytes(path.read_bytes())
        names.append(f"c{number:02d}.crw\t{path.name}")

    print("\n".join(names))
    print(f"{len(tiffs)} MRW, {len(jpegs)} RAF and {len(names) - len(tiffs) - len(jpegs)} CRW files")
    return 0 if tiffs and jpegs else 1


if __name__ == "__main__":
    sys.exit(main())
