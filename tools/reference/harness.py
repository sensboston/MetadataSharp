"""Optional independent CLI comparison. No third-party test code or fixtures."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

EXPECTED_EXIV2 = "0.27.6"
EXPECTED_EXIFTOOL = "12.76"


def available(required, which=shutil.which):
    missing = [name for name in ("dotnet", "exiv2", "exiftool") if not which(name)]
    if missing and required:
        raise RuntimeError("Required tools missing: " + ", ".join(missing))
    return not missing


def check_version(name, text, expected):
    actual = re.search(r"\bexiv2\s+(\d+\.\d+\.\d+)\b", text) if name == "exiv2" else re.fullmatch(r"\s*(\d+\.\d+)\s*", text)
    if not actual or actual.group(1) != expected:
        raise RuntimeError(f"{name} version mismatch: expected {expected}, got {text.strip()!r}")


def exiv_values(text):
    values = {}
    for line in text.splitlines():
        if not line.strip():
            continue
        fields = line.split(None, 3)
        if len(fields) != 4 or fields[0] in values:
            raise RuntimeError("Unexpected or duplicate Exiv2 value row: " + line)
        values[fields[0]] = (fields[1], int(fields[2]), fields[3])
    return values


def classify_warning(text):
    # Missing mandatory default-resolution tags are an explicit minimal-EXIF limitation.
    return text.casefold() in {
        ("Missing required JPEG IFD0 tag " + tag + " " + name).casefold()
        for tag, name in (("0x011a", "XResolution"), ("0x011b", "YResolution"),
                          ("0x0128", "ResolutionUnit"), ("0x0213", "YCbCrPositioning"))
    }


def scan_tail(data):
    # This is fixture-specific, not a generic JPEG parser: generated metadata never contains FFDA.
    start = data.find(b"\xff\xda")
    if start < 0:
        raise RuntimeError("Authored fixture has no SOS")
    return data[start:]


def main(argv=None):
    parser = argparse.ArgumentParser()
    parser.add_argument("--required", action="store_true")
    parser.add_argument("--output", default="artifacts/reference")
    args = parser.parse_args(argv)
    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    report = {"status": "running", "locale": "C", "commands": [], "warnings": []}

    def save():
        (output / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")

    def run(command):
        result = subprocess.run(command, capture_output=True, text=True, timeout=60,
                                env={**os.environ, "LC_ALL": "C", "LANG": "C"}, check=False)
        report["commands"].append({"args": list(map(str, command)), "exitCode": result.returncode,
                                   "stdout": result.stdout, "stderr": result.stderr})
        save()
        if result.returncode:
            raise RuntimeError(f"Command failed ({result.returncode}): {command!r}: {result.stdout} {result.stderr}")
        return result.stdout

    def validate_image(image):
        validation = json.loads(run(["exiftool", "-validate", "-warning", "-a", "-j", str(image)]), object_pairs_hook=list)
        pairs = validation[0]
        for name, value in pairs:
            if name.lower().startswith("warning"):
                classification = "documented-minimal-exif-default-tag" if classify_warning(value) else "unclassified"
                report["warnings"].append({"file": image.name, "text": value, "classification": classification})
                if classification == "unclassified":
                    raise RuntimeError("Unclassified validation warning: " + value)
                print("::notice title=Reference discrepancy::" + value)
            elif name.lower().startswith("error"):
                raise RuntimeError("Validation error: " + str(value))

    try:
        if not __debug__:
            raise RuntimeError("Reference checks require Python without optimization (-O)")
        if not available(args.required):
            report["status"] = "skipped"; report["reason"] = "Optional tools missing"; save()
            print("SKIP: optional reference tools unavailable")
            return 0
        exiv_version = run(["exiv2", "-V"])
        tool_version = run(["exiftool", "-ver"])
        check_version("exiv2", exiv_version, EXPECTED_EXIV2)
        check_version("exiftool", tool_version, EXPECTED_EXIFTOOL)
        report["sourceCommit"] = run(["git", "rev-parse", "HEAD"]).strip()
        report["recipeHashes"] = {str(p): hashlib.sha256(p.read_bytes()).hexdigest()
                                  for p in (Path("tools/MetadataSharp.ReferenceFixture/Program.cs"),
                                            Path("tests/MetadataSharp.Tests/Jpeg/JpegFixture.cs"))}
        report["versions"] = {"exiv2": EXPECTED_EXIV2, "exiftool": EXPECTED_EXIFTOOL}
        fixture = Path("tools/MetadataSharp.ReferenceFixture/bin/Release/net8.0/MetadataSharp.ReferenceFixture.dll").resolve()
        def ours(*parameters):
            return run(["dotnet", str(fixture), *map(str, parameters)])
        ours("generate", output)
        image = output / "ours.jpg"
        baseline = (output / "base.jpg").read_bytes()
        authored = image.read_bytes()
        assert (output / "noop.jpg").read_bytes() == authored, "No-op differs"
        assert (output / "removed.jpg").read_bytes() == baseline, "Whole removal changed unrelated bytes"
        assert scan_tail(baseline) == scan_tail(authored), "Encoded-image range changed"
        values = json.loads(run(["exiftool", "-j", "-n", "-Artist", "-Orientation", str(image)]))[0]
        assert values["Artist"] == "Alice" and values["Orientation"] == 1, values
        values = exiv_values(run(["exiv2", "-P", "kycv", "-K", "Exif.Image.Artist", "-K", "Exif.Image.Orientation", str(image)]))
        assert values == {"Exif.Image.Artist": ("Ascii", 6, "Alice"), "Exif.Image.Orientation": ("Short", 1, "1")}, values
        validate_image(image)
        for tool, artist in (("exiftool", "External"), ("exiv2", "Exiv2")):
            changed = output / (tool + ".jpg"); shutil.copyfile(image, changed)
            if tool == "exiftool": run([tool, "-overwrite_original", "-Artist=" + artist, str(changed)])
            else: run([tool, "-M", "set Exif.Image.Artist Ascii " + artist, str(changed)])
            actual = json.loads(ours("inspect", changed))
            assert actual == {"artist": artist, "orientation": 1}, actual
            copied = output / (tool + "-noop.jpg"); ours("noop", changed, copied)
            assert changed.read_bytes() == copied.read_bytes(), tool + " reverse no-op mismatch"
            assert scan_tail(changed.read_bytes()) == scan_tail(baseline), tool + " changed encoded data"
        xmp = output / "xmp.jpg"
        validate_image(xmp)
        validate_image(output / "xmp-rich.xmp")
        assert (output / "xmp-noop.jpg").read_bytes() == xmp.read_bytes(), "XMP no-op differs"
        assert (output / "xmp-removed.jpg").read_bytes() == authored, "XMP removal changed EXIF or unrelated bytes"
        assert scan_tail(xmp.read_bytes()) == scan_tail(baseline), "XMP changed encoded bytes"
        values = json.loads(run(["exiftool", "-j", "-XMP-xmp:CreatorTool", str(xmp)]))[0]
        assert values["CreatorTool"] == "MetadataSharp", values
        values = exiv_values(run(["exiv2", "-P", "kycv", "-K", "Xmp.xmp.CreatorTool", str(xmp)]))
        assert values == {"Xmp.xmp.CreatorTool": ("XmpText", 13, "MetadataSharp")}, values
        rich = json.loads(run(["exiftool", "-j", "-XMP-dc:Subject", "-XMP-dc:Title", "-XMP-dc:Title-ru", str(output / "xmp-rich.xmp")]))[0]
        assert rich["Subject"] == ["alpha", "beta"] and rich["Title"] == "Hello" and rich["Title-ru"] == "Привет", rich
        for size in (65502, 65503, 65504):
            boundary = output / f"xmp-boundary-{size}.jpg"
            validate_image(boundary)
            actual = json.loads(ours("inspect-xmp", boundary))["creatorTool"]
            assert actual and set(actual) == {"a"}, "Boundary packet value differs"
            external = json.loads(run(["exiftool", "-j", "-XMP-xmp:CreatorTool", str(boundary)]))[0]
            assert external["CreatorTool"] == actual, "ExifTool boundary mismatch"
            rows = exiv_values(run(["exiv2", "-P", "kycv", "-K", "Xmp.xmp.CreatorTool", str(boundary)]))
            assert rows == {"Xmp.xmp.CreatorTool": ("XmpText", len(actual), actual)}, "Exiv2 boundary mismatch"
            assert scan_tail(boundary.read_bytes()) == scan_tail(baseline), "Boundary image changed"
        for tool, text in (("exiftool", "ExternalXmp"), ("exiv2", "Exiv2Xmp")):
            changed = output / (tool + "-xmp.jpg"); shutil.copyfile(xmp, changed)
            if tool == "exiftool": run([tool, "-overwrite_original", "-XMP-xmp:CreatorTool=" + text, str(changed)])
            else: run([tool, "-M", "set Xmp.xmp.CreatorTool XmpText " + text, str(changed)])
            assert json.loads(ours("inspect-xmp", changed)) == {"creatorTool": text}, "Reverse XMP value differs"
            copied = output / (tool + "-xmp-noop.jpg"); ours("noop-xmp", changed, copied)
            assert copied.read_bytes() == changed.read_bytes(), "Reverse XMP no-op differs"
            assert scan_tail(changed.read_bytes()) == scan_tail(baseline), "Reverse XMP encoded bytes differ"
        report["fixtures"] = [{"name": p.name, "sha256": hashlib.sha256(p.read_bytes()).hexdigest(),
                               "source": "MetadataSharp authored synthetic generator", "license": "MIT"}
                              for p in sorted(output.iterdir()) if p.suffix in (".jpg", ".xmp")]
        report["status"] = "passed"; save()
        print("PASS: external reads, reverse edits, byte preservation and validation classification")
        return 0
    except Exception as error:
        report["status"] = "failed"; report["error"] = str(error); save()
        message = str(error).replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")
        print("::error title=Reference validation::" + message)
        print(str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
