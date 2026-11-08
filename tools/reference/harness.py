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


def iptc_rows(text):
    rows = []
    for line in text.splitlines():
        if not line.strip():
            continue
        fields = line.split(None, 3)
        if len(fields) != 4:
            raise RuntimeError("Unexpected IPTC value row: " + line)
        rows.append((fields[0], fields[1], int(fields[2]), fields[3]))
    return rows


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
                    raise RuntimeError("Unclassified validation warning in " + image.name + ": " + value)
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
        iptc = output / "iptc.jpg"
        validate_image(iptc)
        assert (output / "iptc-noop.jpg").read_bytes() == iptc.read_bytes(), "IPTC no-op differs"
        assert (output / "iptc-removed.jpg").read_bytes() == (output / "iptc-expected-removed.jpg").read_bytes(), "IPTC removal lost unrelated resources"
        assert scan_tail(iptc.read_bytes()) == scan_tail(baseline), "IPTC changed encoded bytes"
        own = json.loads(ours("inspect-iptc", iptc))
        assert own["caption"] == "Привет" and own["keywords"] == ["alpha", "beta", "alpha"], own
        facts = json.loads(run(["exiftool", "-j", "-n", "-IPTC:Caption-Abstract", "-IPTC:Keywords", "-Photoshop:IPTCDigest", str(iptc)]))[0]
        assert facts["Caption-Abstract"] == own["caption"] and facts["Keywords"] == own["keywords"], facts
        assert facts["IPTCDigest"].lower() == own["digest"], "Stored IPTC digest is stale"
        rows = iptc_rows(run(["exiv2", "-P", "kycv", "-K", "Iptc.Application2.Keywords", "-K", "Iptc.Application2.Caption", str(iptc)]))
        assert [r[1:] for r in rows if r[0] == "Iptc.Application2.Keywords"] == [("String", 5, "alpha"), ("String", 4, "beta"), ("String", 5, "alpha")], rows
        assert [r[1:] for r in rows if r[0] == "Iptc.Application2.Caption"] == [("String", 12, "Привет")], rows
        full = output / "iptc-boundary.jpg"
        validate_image(full)
        full_own = json.loads(ours("inspect-iptc", full))["keywords"]
        full_external = json.loads(run(["exiftool", "-j", "-IPTC:Keywords", str(full)]))[0]["Keywords"]
        assert full_external == full_own and len(full_own) == 950, "IPTC full-capacity repetitions differ"
        full_rows = iptc_rows(run(["exiv2", "-P", "kycv", "-K", "Iptc.Application2.Keywords", str(full)]))
        assert [r[3] for r in full_rows] == full_own, "Exiv2 full-capacity repetitions differ"
        for tool, caption in (("exiftool", "ExternalCaption"), ("exiv2", "Exiv2Caption")):
            changed = output / (tool + "-iptc.jpg"); shutil.copyfile(iptc, changed)
            if tool == "exiftool": run([tool, "-overwrite_original", "-IPTC:Caption-Abstract=" + caption, str(changed)])
            else: run([tool, "-M", "set Iptc.Application2.Caption String " + caption, str(changed)])
            actual = json.loads(ours("inspect-iptc", changed))
            assert actual["caption"] == caption and actual["keywords"] == own["keywords"], actual
            copied = output / (tool + "-iptc-noop.jpg"); ours("noop-iptc", changed, copied)
            assert copied.read_bytes() == changed.read_bytes(), "Reverse IPTC no-op differs"
            assert scan_tail(changed.read_bytes()) == scan_tail(baseline), "Reverse IPTC encoded bytes differ"
            refreshed = output / (tool + "-iptc-refreshed.jpg"); ours("rewrite-iptc", changed, refreshed)
            current = json.loads(ours("inspect-iptc", refreshed))
            checksum = json.loads(run(["exiftool", "-j", "-n", "-Photoshop:IPTCDigest", str(refreshed)]))[0]
            assert checksum["IPTCDigest"].lower() == current["digest"], "Refreshed IPTC digest is stale"
        # PNG independent structural accounting: CRC and IDAT zlib are checked here,
        # without reusing the library scanner or its CRC implementation.
        import zlib
        def png_pixels(path):
            data = path.read_bytes(); assert data[:8] == b"\x89PNG\r\n\x1a\n"
            at = 8; image = bytearray()
            while at < len(data):
                length = int.from_bytes(data[at:at+4], "big")
                kind = data[at+4:at+8]; body = data[at+8:at+8+length]
                assert zlib.crc32(kind + body) == int.from_bytes(data[at+8+length:at+12+length], "big")
                if kind == b"IDAT": image.extend(body)
                at += length + 12
            assert at == len(data)
            return bytes(image), zlib.decompress(image)
        png = output / "ours.png"
        assert png_pixels(png) == png_pixels(output / "base.png")
        assert png_pixels(png)[1] == bytes([0, 10, 20, 30])
        assert (output / "noop.png").read_bytes() == png.read_bytes()
        assert (output / "removed.png").read_bytes() == (output / "exif.png").read_bytes()
        validate_image(png)
        values = json.loads(run(["exiftool", "-j", "-n", "-Artist", "-Orientation", "-XMP-xmp:CreatorTool", str(png)]))[0]
        assert values["Artist"] == "Alice" and values["Orientation"] == 1 and values["CreatorTool"] == "MetadataSharp", values
        rows = exiv_values(run(["exiv2", "-P", "kycv", "-K", "Exif.Image.Artist", "-K", "Xmp.xmp.CreatorTool", str(png)]))
        assert rows == {"Exif.Image.Artist": ("Ascii", 6, "Alice"), "Xmp.xmp.CreatorTool": ("XmpText", 13, "MetadataSharp")}, rows
        changed = output / "external.png"; shutil.copyfile(png, changed)
        run(["exiftool", "-overwrite_original", "-Artist=ExternalPng", "-XMP-xmp:CreatorTool=ExternalPng", str(changed)])
        assert json.loads(ours("inspect-png", changed)) == {"artist": "ExternalPng", "creatorTool": "ExternalPng"}
        copied = output / "external-noop.png"; ours("noop-png", changed, copied)
        assert copied.read_bytes() == changed.read_bytes()
        assert png_pixels(changed) == png_pixels(png)
        report["fixtures"] = [{"name": p.name, "sha256": hashlib.sha256(p.read_bytes()).hexdigest(),
                               "source": "MetadataSharp authored synthetic generator", "license": "MIT"}
                              for p in sorted(output.iterdir()) if p.suffix in (".jpg", ".xmp", ".png")]
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
