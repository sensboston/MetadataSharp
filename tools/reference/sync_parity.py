"""What ExifToXmp and XmpToExif produce, held against the conversion of the pinned exiv2.

A separate check from key parity. The tool converts when it writes and when it
reads an XMP sidecar, and that is what is used here, on the JPEG files of the
pinned corpus:

  1. Each file is copied and "exiv2 -d xi" takes the XMP and the IPTC out of
     the copy, so that EXIF is the only source.
  2. "exiv2 -eX" writes NAME.xmp from the copy: the tool's EXIF to XMP.
  3. The fixture generator (sync-parity) writes NAME.ours.xmp, this library's
     ExifToXmp of the same copy, and NAME.back.jpg, a file whose EXIF block is
     this library's XmpToExif of the tool's NAME.xmp.
  4. EXIF to XMP: "exiv2 -PXkv" of NAME.xmp against the same of NAME.ours.xmp.
     XMP to EXIF: "exiv2 -PEkv" of NAME.xmp, which is the tool's own conversion
     of its sidecar back to EXIF, against the same of NAME.back.jpg.

Both sides of a comparison are printed by the tool, so what is compared is
which keys there are and the value the tool prints for each.

A key is "same" when both sides print the same value, and "form" when the
values are the same items written apart differently (one item per element here,
one text with spaces there). Every other outcome ("differs", "tool only",
"ours only") has to be one this file documents in DOCUMENTED, with the reason;
an outcome that is not documented fails the check. The counts of every outcome
are printed either way.

Usage: sync_parity.py CORPUS WORK --exiv2 PATH --fixture "COMMAND ..."
"""
import argparse
import re
import shlex
import shutil
import subprocess
import sys
from collections import Counter, OrderedDict
from pathlib import Path

MAX_BYTES = 32 * 1024 * 1024
TO_XMP, TO_EXIF = "EXIF to XMP", "XMP to EXIF"

# Keys a JPEG file has for its own structure, which a converted sidecar has no use for.
STRUCTURE = {"Exif.Image.ExifTag", "Exif.Image.GPSTag", "Exif.Photo.InteroperabilityTag"}

# (direction, key, outcome) -> why the difference is intended. A key may end in * for a prefix.
DOCUMENTED = OrderedDict()


def document(direction, key, outcomes, reason):
    for outcome in outcomes.split(","):
        DOCUMENTED[(direction, key, outcome.strip())] = reason


def documented(direction, key, outcome):
    """The reason an outcome is intended, or None."""
    reason = DOCUMENTED.get((direction, key, outcome))
    if reason is not None:
        return reason
    for (way, pattern, kind), text in DOCUMENTED.items():
        if way == direction and kind == outcome and pattern.endswith("*") and key.startswith(pattern[:-1]):
            return text
    return None


def properties(text, prefix):
    """key -> the values printed for it, in order, from a "-Pkv" print-out."""
    found = OrderedDict()
    for line in text.splitlines():
        parts = line.split(None, 1)
        if not parts or not parts[0].startswith(prefix):
            continue
        found.setdefault(parts[0], []).append(parts[1].strip() if len(parts) > 1 else "")
    return found


def items(values):
    """The items of printed values, however they were kept apart."""
    return [item for value in values for item in re.split(r"[,\s]+", value) if item]


def outcome(theirs, ours):
    if theirs is None:
        return "ours only"
    if ours is None:
        return "tool only"
    if theirs == ours:
        return "same"
    if items(theirs) == items(ours):
        return "form"
    return "differs"


def compare(theirs, ours, ignore=()):
    """(key, outcome, tool's values, our values) for every key of either side."""
    rows = []
    for key in list(theirs) + [key for key in ours if key not in theirs]:
        if key in ignore:
            continue
        rows.append((key, outcome(theirs.get(key), ours.get(key)), theirs.get(key), ours.get(key)))
    return rows


def run(command, timeout=120):
    try:
        done = subprocess.run(command, capture_output=True, timeout=timeout)
    except subprocess.TimeoutExpired:
        return 124, "", "not finished in time"
    return (done.returncode, done.stdout.decode("utf-8", "replace"), done.stderr.decode("utf-8", "replace"))


def prepare(corpus, work, exiv2, runner=run):
    """Copies the JPEG files, leaves EXIF as their only metadata, and has the tool write the sidecars."""
    work = Path(work)
    work.mkdir(parents=True, exist_ok=True)
    names = OrderedDict()
    skipped = Counter()
    sources = sorted(path for path in Path(corpus).rglob("*")
                     if path.is_file() and path.suffix.lower() in (".jpg", ".jpeg"))
    for path in sources:
        if path.stat().st_size > MAX_BYTES:
            skipped["too large"] += 1
            continue
        name = f"s{len(names) + sum(skipped.values()):05d}"
        copy = work / (name + ".jpg")
        shutil.copyfile(path, copy)
        code, _, _ = runner([exiv2, "-d", "xi", str(copy)])
        if code != 0:
            copy.unlink()
            skipped["not rewritten by the tool"] += 1
            continue
        code, _, _ = runner([exiv2, "-eX", str(copy)])
        sidecar = work / (name + ".xmp")
        if code != 0 and sidecar.exists():
            sidecar.unlink()
        names[name] = str(path)
    return names, skipped


def printed(exiv2, mode, path, prefix, runner=run):
    """The properties the tool prints for a file; None when there is no such file or the tool fails on it."""
    if not Path(path).exists():
        return None
    code, output, _ = runner([exiv2, mode, str(path)])
    found = properties(output, prefix)
    # The tool exits with 253 for a file without that kind of metadata.
    if code != 0 and not found:
        return OrderedDict() if code == 253 else None
    return found


def check(work, names, exiv2, runner=run):
    """Every row of every file as (direction, file, key, outcome, theirs, ours), and the files compared per direction."""
    work = Path(work)
    rows = []
    files = Counter()
    for name in names:
        theirs = printed(exiv2, "-PXkv", work / (name + ".xmp"), "Xmp.", runner)
        ours = printed(exiv2, "-PXkv", work / (name + ".ours.xmp"), "Xmp.", runner)
        if theirs or ours:
            files[TO_XMP] += 1
            rows.extend((TO_XMP, name, *row) for row in compare(theirs or {}, ours or {}))
        theirs = printed(exiv2, "-PEkv", work / (name + ".xmp"), "Exif.", runner)
        ours = printed(exiv2, "-PEkv", work / (name + ".back.jpg"), "Exif.", runner)
        if theirs or ours:
            files[TO_EXIF] += 1
            rows.extend((TO_EXIF, name, *row) for row in compare(theirs or {}, ours or {}, STRUCTURE))
    return rows, files


def summarise(rows):
    """(direction, key, outcome) -> (count, first example) for every outcome that is not "same"."""
    table = OrderedDict()
    for direction, name, key, kind, theirs, ours in rows:
        if kind == "same":
            continue
        count, example = table.get((direction, key, kind), (0, None))
        if example is None:
            example = f"{name}: tool {theirs!r} ours {ours!r}"
        table[(direction, key, kind)] = (count + 1, example)
    return table


def annotate(level, title, lines, size=5500, limit=9):
    """Prints lines as annotations of at most `size` characters each, `limit` of them."""
    chunk, length, sent = [], 0, 0
    for line in list(lines) + [None]:
        if line is None or length + len(line) + 3 > size:
            if chunk and sent < limit:
                text = "\n".join(chunk).replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")
                print(f"::{level} title={title}::{text}")
                sent += 1
            chunk, length = [], 0
        if line is not None:
            chunk.append(line[:600])
            length += len(chunk[-1]) + 3


def main(argv=None, runner=run):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("corpus")
    parser.add_argument("work")
    parser.add_argument("--exiv2", required=True)
    parser.add_argument("--fixture", required=True, help="the fixture generator, to which 'sync-parity WORK' is added")
    args = parser.parse_args(argv)

    names, skipped = prepare(args.corpus, args.work, args.exiv2, runner)
    if not names:
        print("::error title=Sync parity::no JPEG file of the corpus was prepared")
        return 1
    code, output, errors = runner(shlex.split(args.fixture) + ["sync-parity", str(args.work)], timeout=1500)
    sys.stdout.write(output)
    if code != 0:
        sys.stdout.write(errors)
        print(f"::error title=Sync parity::the fixture generator failed (exit {code})")
        return 1

    rows, files = check(args.work, names, args.exiv2, runner)
    totals = Counter((direction, kind) for direction, _, _, kind, _, _ in rows)
    table = summarise(rows)
    unexplained = [(key, value) for key, value in table.items() if key[2] != "form" and documented(*key) is None]
    explained = [(key, value) for key, value in table.items() if key[2] != "form" and documented(*key) is not None]
    forms = [(key, value) for key, value in table.items() if key[2] == "form"]

    for direction in (TO_XMP, TO_EXIF):
        counts = ", ".join(f"{kind} {totals[(direction, kind)]}"
                           for kind in ("same", "form", "differs", "tool only", "ours only"))
        print(f"::notice title=Sync parity, {direction}::{files[direction]} files of {len(names)} prepared "
              f"({', '.join(f'{count} {why}' for why, count in skipped.items()) or 'none skipped'}); values: {counts}")
    annotate("notice", "Sync parity, same items in another form",
             (f"{direction} {key}: {count}; {example}" for (direction, key, _), (count, example) in forms))
    annotate("notice", "Sync parity, documented differences",
             (f"{direction} {key} {kind}: {count}; {documented(direction, key, kind)}; {example}"
              for (direction, key, kind), (count, example) in explained))
    annotate("error", "Sync parity, differences that are not documented",
             (f"{direction} {key} {kind}: {count}; {example}" for (direction, key, kind), (count, example) in unexplained))
    for (direction, key, kind), (count, example) in unexplained:
        print(f"FAIL {direction} {key} {kind}: {count}; {example}")
    return 1 if unexplained else 0


if __name__ == "__main__":
    sys.exit(main())
