"""The operations of this library, held against the same operations of the pinned exiv2.

A separate check from key parity. On a copy of every JPEG file of the pinned
corpus the tool runs an operation of its command line, and this library runs
the operation it offers for the same purpose (the fixture generator,
operations-parity). Both results are printed by the tool with "exiv2 -pa" and
the print-outs are compared, key by key:

  adjust     exiv2 -a 1:30:15 -Y 1 -O 1 -D 2 ad    MetadataTimestamps.Adjust
  modify     exiv2 -m modify.txt                    ModifyScript.Parse, saved
  delete-a   exiv2 rm                               MetadataOperations.Delete (all)
  delete-t   exiv2 -d t rm                          ... Delete (thumbnail)
  delete-xc  exiv2 -d xc rm                         ... Delete (XMP, comment)
  extract    exiv2 ex         (the .exv is printed) MetadataOperations.ExtractExv
  insert     exiv2 in         (the tool's .exv into a file without metadata)
                                                    MetadataOperations.InsertExv
  thumbnail  exiv2 -i t in    (NAME-thumb.jpg)      ExifEdit.SetThumbnail
  fixiso     exiv2 fi                               MetadataOperations.FixIso
  fixcom     exiv2 -n ISO-8859-1 fc                 MetadataOperations.FixUserComment
  rename     exiv2 -F -r FORMAT mv                  MetadataFileNames.Format
             (the name the tool gives the file against the name returned)

A key is "same" when both print-outs hold the same line for it. Where they
differ, the print-out of the source says who changed the key: "differs (tool
changed)" when this library left the line of the source and the tool did not,
"differs (ours changed)" for the reverse, "differs" when both or neither did.
Every outcome but "same" (those, "tool only", "ours only") has to be one DOCUMENTED names
with its reason; an outcome that is not documented fails the check. Keys whose
value is an offset of the layout are named in LAYOUT and not compared.

A file the tool fails on has nothing to compare for that operation, and neither
has a file this library refuses; both are counted and printed, the refusals
with their texts.

Usage: operations_parity.py CORPUS WORK --exiv2 PATH --fixture "COMMAND ..."
"""
import argparse
import importlib.util
import shlex
import shutil
import sys
from collections import Counter, OrderedDict
from pathlib import Path

_spec = importlib.util.spec_from_file_location("sync_parity", Path(__file__).with_name("sync_parity.py"))
sync = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(sync)
run, annotate = sync.run, sync.annotate

MAX_BYTES = 8 * 1024 * 1024
RENAME_FORMAT = "%Y%m%d_%H%M%S_:basename:"
MODIFY_SCRIPT = """# The same file is given to exiv2 -m and to ModifyScript.Parse.
set Exif.Image.Artist Parity Artist
set Exif.Photo.UserComment charset=Ascii a parity comment
del Exif.Image.Software
set Iptc.Application2.Headline A parity headline
add Iptc.Application2.Keywords parity
set Xmp.dc.subject parity
set Xmp.photoshop.City Parity City
"""

# operation -> (the tool's arguments before the file, the extension of the result that is printed)
OPERATIONS = OrderedDict([
    ("adjust", (["-a", "1:30:15", "-Y", "1", "-O", "1", "-D", "2", "ad"], "jpg")),
    ("modify", (None, "jpg")),
    ("delete-a", (["rm"], "jpg")),
    ("delete-t", (["-d", "t", "rm"], "jpg")),
    ("delete-xc", (["-d", "xc", "rm"], "jpg")),
    ("extract", (["ex"], "exv")),
    ("insert", (["in"], "jpg")),
    ("thumbnail", (["-i", "t", "in"], "jpg")),
    ("fixiso", (["fi"], "jpg")),
    ("fixcom", (["-n", "ISO-8859-1", "fc"], "jpg")),
])

# Keys whose value is where something lies in the block: a matter of the layout, not of the operation.
LAYOUT = {
    "Exif.Image.ExifTag", "Exif.Image.GPSTag", "Exif.Photo.InteroperabilityTag",
    "Exif.Thumbnail.JPEGInterchangeFormat", "Exif.MakerNote.Offset",
}

# (operation, key, outcome) -> why the difference is intended. A key may end in * for a prefix.
DOCUMENTED = OrderedDict()


def document(operation, key, outcomes, reason):
    for outcome in outcomes.split(","):
        DOCUMENTED[(operation, key, outcome.strip())] = reason


for _key in ("Xmp.xmp.ModifyDate", "Xmp.xmp.CreateDate", "Xmp.photoshop.DateCreated", "Xmp.exif.DateTimeOriginal",
             "Xmp.exif.DateTimeDigitized", "Xmp.tiff.DateTime", "Iptc.Application2.DateCreated",
             "Iptc.Application2.TimeCreated", "Iptc.Application2.DigitizationDate", "Iptc.Application2.DigitizationTime"):
    document("adjust", _key, "differs (ours changed)",
             "the XMP and IPTC counterparts of the EXIF timestamps move with them; the tool moves the EXIF tags only")
for _operation in ("adjust", "modify", "delete-t", "delete-xc", "extract", "thumbnail", "fixiso", "fixcom"):
    document(_operation, "Exif.Photo.MakerNote", "differs (tool changed)",
             "the tool writes the MakerNote anew when it writes the block; here its bytes stay as the source has them")
    document(_operation, "Exif.(MakerNote).*", "differs (tool changed)",
             "the tool writes the MakerNote anew, so its byte order and the offsets it holds change; here its bytes stay")
for _key in ("Iptc.Envelope.CharacterSet", "Iptc.Application2.RecordVersion"):
    document("modify", _key, "ours only",
             "an IPTC stream that is created declares UTF-8 and its record version; the tool writes the datasets it was given only")
    document("insert", _key, "ours only, differs",
             "the IPTC stream is written anew and declares UTF-8 and the record version of the writer, not those of the EXV file")
for _key in ("Exif.Thumbnail.XResolution", "Exif.Thumbnail.YResolution", "Exif.Thumbnail.ResolutionUnit"):
    document("thumbnail", _key, "ours only",
             "a thumbnail directory is created with the resolution Exif requires of a JPEG thumbnail (72 dpi); "
             "the tool creates it with the compression and the stream only")
document("insert", "Exif.(MakerNote).*", "tool only",
         "the MakerNote is not carried by an insert: its offsets belong to the file it was read from "
         "(the tool carries it); InsertExv says so in its notes")
for _key in ("Exif.Thumbnail.XResolution", "Exif.Thumbnail.YResolution", "Exif.Thumbnail.ResolutionUnit"):
    document("insert", _key, "ours only",
             "a thumbnail directory is created with the resolution Exif requires of a JPEG thumbnail (72 dpi)")
document("insert", "Exif.Thumbnail.*", "tool only",
         "a thumbnail this library does not read as a JPEG stream (see ExtractThumbnail) is not carried, nor the tags of its directory")
document("insert", "Iptc.*", "tool only",
         "a dataset whose text is not UTF-8, from a stream that declares no character set, is not carried into a stream "
         "that declares UTF-8; InsertExv says so in its notes")
document("insert", "Exif.Photo.MakerNote", "tool only", "the MakerNote is not carried by an insert (see Exif.(MakerNote).*)")

STANDARD_GROUPS = ("Image", "Photo", "GPSInfo", "Iop", "Thumbnail")


def grouped_key(operation, key):
    """The key a row is counted under: the tags of the MakerNote groups are one key."""
    parts = key.split(".")
    if parts[0] == "Exif" and len(parts) > 2 and parts[1] not in STANDARD_GROUPS:
        return "Exif.(MakerNote).*"
    return key


def documented(operation, key, outcome):
    """The reason an outcome is intended, or None."""
    reason = DOCUMENTED.get((operation, key, outcome))
    if reason is not None:
        return reason
    for (op, pattern, kind), text in DOCUMENTED.items():
        if op == operation and kind == outcome and pattern.endswith("*") and key.startswith(pattern[:-1]):
            return text
    return None


def printed(exiv2, path, runner=run):
    """key -> the lines "exiv2 -pa" prints for it, without the key; None when the tool cannot print the file."""
    if not Path(path).exists():
        return None
    code, output, _ = runner([exiv2, "-pa", str(path)])
    found = OrderedDict()
    for line in output.splitlines():
        parts = line.split(None, 1)
        if parts and parts[0].startswith(("Exif.", "Iptc.", "Xmp.")):
            found.setdefault(parts[0], []).append(" ".join(parts[1].split()) if len(parts) > 1 else "")
    # The tool exits with 253 for a file without metadata.
    if code != 0 and not found:
        return OrderedDict() if code == 253 else None
    return found


def compare(theirs, ours, source=None):
    """(key, outcome, the tool's lines, ours) for every key of either side that is no matter of the layout."""
    rows = []
    source = source or {}
    for key in list(theirs) + [key for key in ours if key not in theirs]:
        if key in LAYOUT:
            continue
        left, right, before = theirs.get(key), ours.get(key), source.get(key)
        if left is None:
            kind = "ours only"
        elif right is None:
            kind = "tool only"
        elif left == right:
            kind = "same"
        elif before is not None and right == before:
            kind = "differs (tool changed)"
        elif before is not None and left == before:
            kind = "differs (ours changed)"
        else:
            kind = "differs"
        rows.append((key, kind, left, right))
    return rows


def sources(corpus):
    return sorted(path for path in Path(corpus).rglob("*")
                  if path.is_file() and path.suffix.lower() in (".jpg", ".jpeg") and path.stat().st_size <= MAX_BYTES)


def tool_side(work, names, exiv2, runner=run):
    """Runs the tool's operations on copies; (name, operation) -> None when it worked, else its first complaint."""
    work = Path(work)
    failed = {}
    renamed = {}
    for name in names:
        source = work / (name + ".jpg")
        for operation, (arguments, _) in OPERATIONS.items():
            copy = work / f"{name}.{operation}.tool.jpg"
            if operation == "insert":
                # The tool reads NAME.exv next to the file it inserts into.
                extracted = work / f"{name}.extract.tool.exv"
                if not extracted.exists():
                    failed[(name, operation)] = "the tool extracted nothing"
                    continue
                shutil.copyfile(work / "blank.jpg", copy)
                shutil.copyfile(extracted, work / f"{name}.insert.tool.exv")
            else:
                shutil.copyfile(source, copy)
            if operation == "thumbnail":
                shutil.copyfile(work / "blank.jpg", work / f"{name}.thumbnail.tool-thumb.jpg")
            if arguments is None:
                arguments = ["-m", str(work / "modify.txt"), "mo"]
            code, _, errors = runner([exiv2] + arguments + [str(copy)])
            if code != 0:
                failed[(name, operation)] = (errors.strip().splitlines() or [f"exit {code}"])[-1]
        folder = work / ("mv-" + name)
        folder.mkdir(exist_ok=True)
        shutil.copyfile(source, folder / (name + ".jpg"))
        code, _, errors = runner([exiv2, "-F", "-r", RENAME_FORMAT, "mv", str(folder / (name + ".jpg"))])
        left = sorted(path.name for path in folder.iterdir())
        if code != 0 or len(left) != 1:
            failed[(name, "rename")] = (errors.strip().splitlines() or [f"exit {code}"])[-1]
        else:
            renamed[name] = left[0]
    return failed, renamed


def statuses(work):
    """(file, operation) -> (status, text) from the ours.tsv the fixture generator wrote."""
    found = {}
    path = Path(work) / "ours.tsv"
    if path.exists():
        for line in path.read_text(encoding="utf-8").splitlines():
            fields = line.split("\t")
            if len(fields) >= 3:
                found[(fields[0], fields[1])] = (fields[2], fields[3] if len(fields) > 3 else "")
    return found


def check(work, names, exiv2, failed, renamed, runner=run):
    """
    Rows (operation, file, key, outcome, theirs, ours), the files compared per operation, and the
    files left out per (operation, who, why).
    """
    work = Path(work)
    rows, files, left_out = [], Counter(), Counter()
    state = statuses(work)
    for name in names:
        before = printed(exiv2, work / (name + ".jpg"), runner)
        for operation in list(OPERATIONS) + ["rename"]:
            status, text = state.get((name, operation), ("missing", ""))
            if (name, operation) in failed:
                left_out[(operation, "tool", failed[(name, operation)].replace(name, "NAME"))] += 1
                continue
            if status.startswith(("refused", "missing")) or (operation == "rename" and not text):
                left_out[(operation, "ours", f"{status}: {text}" if text else status if operation != "rename" else "no date taken")] += 1
                continue
            files[operation] += 1
            if operation == "rename":
                theirs = renamed[name]
                rows.append((operation, name, "file name", "same" if theirs == text else "differs", [theirs], [text]))
                continue
            extension = OPERATIONS[operation][1]
            theirs = printed(exiv2, work / f"{name}.{operation}.tool.{extension}", runner)
            ours = printed(exiv2, work / f"{name}.{operation}.ours.{extension}", runner)
            if theirs is None or ours is None:
                rows.append((operation, name, "the file", "differs",
                             ["not printed" if theirs is None else "printed"], ["not printed" if ours is None else "printed"]))
                continue
            # What an insert starts from is the EXV file the tool wrote.
            origin = printed(exiv2, work / f"{name}.insert.tool.exv", runner) if operation == "insert" else before
            rows.extend((operation, name, *row) for row in compare(theirs, ours, origin))
    return rows, files, left_out


def summarise(rows):
    """(operation, key, outcome) -> (count, first example) for every outcome that is not "same"."""
    table = OrderedDict()
    for operation, name, key, kind, theirs, ours in rows:
        if kind == "same":
            continue
        counted = grouped_key(operation, key)
        count, example = table.get((operation, counted, kind), (0, None))
        if example is None:
            example = f"{name} {key}: tool {str(theirs)[:70]} ours {str(ours)[:70]}"
        table[(operation, counted, kind)] = (count + 1, example)
    return table


def main(argv=None, runner=run):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("corpus")
    parser.add_argument("work")
    parser.add_argument("--exiv2", required=True)
    parser.add_argument("--fixture", required=True, help="the fixture generator, to which the command and WORK are added")
    args = parser.parse_args(argv)
    work = Path(args.work)
    work.mkdir(parents=True, exist_ok=True)
    fixture = shlex.split(args.fixture)

    names = OrderedDict()
    for path in sources(args.corpus):
        name = f"s{len(names):05d}"
        shutil.copyfile(path, work / (name + ".jpg"))
        names[name] = str(path)
    if not names:
        print("::error title=Operations parity::no JPEG file of the corpus was prepared")
        return 1
    (work / "modify.txt").write_text(MODIFY_SCRIPT, encoding="utf-8")
    (work / "rename.txt").write_text(RENAME_FORMAT, encoding="utf-8")
    code, output, errors = runner(fixture + ["operations-parity-blank", str(work / "blank.jpg")], timeout=600)
    if code != 0:
        sys.stdout.write(output + errors)
        print(f"::error title=Operations parity::the fixture generator failed (exit {code})")
        return 1
    failed, renamed = tool_side(work, names, args.exiv2, runner)
    code, output, errors = runner(fixture + ["operations-parity", str(work)], timeout=1500)
    sys.stdout.write(output)
    if code != 0:
        sys.stdout.write(errors)
        annotate("error", f"Operations parity - the fixture generator failed (exit {code})", errors.strip().splitlines()[-12:] or ["no output"])
        return 1

    rows, files, left_out = check(work, names, args.exiv2, failed, renamed, runner)
    totals = Counter((operation, kind) for operation, _, _, kind, _, _ in rows)
    table = summarise(rows)
    unexplained = [(key, value) for key, value in table.items() if documented(*key) is None]
    explained = [(key, value) for key, value in table.items() if documented(*key) is not None]
    unexplained.sort(key=lambda item: -item[1][0])

    annotate("error", "Operations parity - differences that are not documented",
             (f"{operation} {key} {kind}: {count}; {example}" for (operation, key, kind), (count, example) in unexplained),
             size=2500, limit=8)
    summary = []
    for operation in list(OPERATIONS) + ["rename"]:
        counts = ", ".join(f"{kind} {totals[(operation, kind)]}" for kind in
                           ("same", "differs", "differs (tool changed)", "differs (ours changed)", "tool only", "ours only"))
        tool = sum(count for (op, who, _), count in left_out.items() if op == operation and who == "tool")
        ours = sum(count for (op, who, _), count in left_out.items() if op == operation and who == "ours")
        summary.append(f"{operation}: {files[operation]} files compared (tool failed on {tool}, refused here {ours}); {counts}")
    annotate("notice", f"Operations parity - {len(names)} files", summary, limit=2)
    annotate("notice", "Operations parity - files left out",
             (f"{operation}, {who}: {count} file(s): {why}" for (operation, who, why), count in left_out.most_common()), limit=3)
    annotate("notice", "Operations parity - documented differences",
             (f"{operation} {key} {kind}: {count}; {documented(operation, key, kind)}; {example}"
              for (operation, key, kind), (count, example) in explained), limit=3)
    for (operation, key, kind), (count, example) in unexplained:
        print(f"FAIL {operation} {key} {kind}: {count}; {example}")
    return 1 if unexplained else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception:  # the log of a job is not always at hand: the failure is said where the report is
        import traceback
        annotate("error", "Operations parity - the comparer failed", traceback.format_exc().splitlines()[-12:])
        sys.exit(1)
