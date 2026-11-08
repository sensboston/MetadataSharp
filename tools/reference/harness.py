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
EXPECTED_DWEBP = "1.3.2"


def available(required, which=shutil.which):
    missing = [name for name in ("dotnet", "exiv2", "exiftool", "dwebp") if not which(name)]
    if missing and required:
        raise RuntimeError("Required tools missing: " + ", ".join(missing))
    return not missing


def check_version(name, text, expected):
    actual = re.search(r"\bexiv2\s+(\d+\.\d+\.\d+)\b", text) if name == "exiv2" else re.fullmatch(r"\s*(\d+(?:\.\d+)+)\s*", text)
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
        check_version("dwebp", run(["dwebp", "-version"]), EXPECTED_DWEBP)
        report["sourceCommit"] = run(["git", "rev-parse", "HEAD"]).strip()
        report["recipeHashes"] = {str(p): hashlib.sha256(p.read_bytes()).hexdigest()
                                  for p in (Path("tools/MetadataSharp.ReferenceFixture/Program.cs"),
                                            Path("tests/MetadataSharp.Tests/Jpeg/JpegFixture.cs"),
                                            Path("tests/MetadataSharp.Tests/Png/PngFixture.cs"),
                                            Path("tests/MetadataSharp.Tests/WebP/WebPFixture.cs"),
                                            Path("tests/MetadataSharp.Tests/Tiff/StandaloneTiffFixture.cs"),
                                            Path("tests/MetadataSharp.Tests/Dng/DngFixture.cs"),
                                            Path("tools/reference/dng_fixtures.py"))}
        report["versions"] = {"exiv2": EXPECTED_EXIV2, "exiftool": EXPECTED_EXIFTOOL, "dwebp": EXPECTED_DWEBP}
        target = os.environ.get("METADATASHARP_REFERENCE_TFM", "net8.0")
        if target not in ("net8.0", "net10.0"):
            raise ValueError("Unsupported reference target framework")
        fixture = Path(f"tools/MetadataSharp.ReferenceFixture/bin/Release/{target}/MetadataSharp.ReferenceFixture.dll").resolve()
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
        run(["exiftool", "-overwrite_original", "-EXIF:Artist=ExternalPng", "-XMP-xmp:CreatorTool=ExternalPng", str(changed)])
        actual = json.loads(ours("inspect-png", changed))
        assert actual["artist"] == "ExternalPng" and actual["xmpEditable"] is False, actual
        # ExifTool emits a read-only xpacket in PNG. This is intentionally opaque
        # under the already approved XMP profile, not an editable reverse-read pass.
        import xml.etree.ElementTree as ET
        assert "end='r'" in actual["rawXmp"]
        assert ET.fromstring(actual["rawXmp"]).find(".//{http://ns.adobe.com/xap/1.0/}CreatorTool").text == "ExternalPng"
        ours("probe-png-opaque", changed)
        report.setdefault("limitations", []).append("ExifTool 12.76 PNG XMP uses read-only xpacket: owned raw packet preserved, property editing refused before output, whole removal tested; semantic reverse property read unsupported")
        copied = output / "external-noop.png"; ours("noop-png", changed, copied)
        assert copied.read_bytes() == changed.read_bytes()
        assert png_pixels(changed) == png_pixels(png)
        def webp_coded(path):
            data = path.read_bytes(); assert data[:4] == b"RIFF" and data[8:12] == b"WEBP"
            assert int.from_bytes(data[4:8], "little") == len(data) - 8
            at = 12; coded = bytearray()
            while at < len(data):
                size = int.from_bytes(data[at+4:at+8], "little"); end = at + 8 + size + size % 2
                assert end <= len(data)
                if size % 2: assert data[end-1] == 0
                if data[at:at+4] in (b"VP8 ", b"VP8L", b"ALPH", b"ICCP"): coded.extend(data[at:end])
                at = end
            assert at == len(data)
            return bytes(coded)
        for kind in range(4):
            webp = output / f"ours-{kind}.webp"; base_webp = output / f"base-{kind}.webp"
            assert webp_coded(webp) == webp_coded(base_webp)
            assert (output / f"noop-{kind}.webp").read_bytes() == webp.read_bytes()
            assert (output / f"removed-{kind}.webp").read_bytes() == (output / f"exif-{kind}.webp").read_bytes()
            validate_image(webp)
            values = json.loads(run(["exiftool", "-j", "-n", "-Artist", "-Orientation", "-XMP-xmp:CreatorTool", str(webp)]))[0]
            assert values["Artist"] == "Alice" and values["Orientation"] == 1 and values["CreatorTool"] == "MetadataSharp", values
            rows = exiv_values(run(["exiv2", "-P", "kycv", "-K", "Exif.Image.Artist", "-K", "Xmp.xmp.CreatorTool", str(webp)]))
            assert rows == {"Exif.Image.Artist": ("Ascii", 6, "Alice"), "Xmp.xmp.CreatorTool": ("XmpText", 13, "MetadataSharp")}, rows
            decoded = output / f"base-{kind}.pam"; edited_pixels = output / f"ours-{kind}.pam"
            run(["dwebp", str(base_webp), "-pam", "-o", str(decoded)])
            run(["dwebp", str(webp), "-pam", "-o", str(edited_pixels)])
            assert decoded.read_bytes() == edited_pixels.read_bytes(), "Independent WebP decoded pixels differ"
            external = output / f"external-{kind}.webp"; shutil.copyfile(webp, external)
            run(["exiftool", "-overwrite_original", "-EXIF:Artist=ExternalWebP", "-XMP-xmp:CreatorTool=ExternalWebP", str(external)])
            actual = json.loads(ours("inspect-webp", external)); assert actual["artist"] == "ExternalWebP", actual
            if actual["xmpEditable"]:
                assert actual["creatorTool"] == "ExternalWebP", actual
            else:
                assert "end='r'" in actual["rawXmp"]
                assert ET.fromstring(actual["rawXmp"]).find(".//{http://ns.adobe.com/xap/1.0/}CreatorTool").text == "ExternalWebP"
                ours("probe-webp-opaque", external)
                report.setdefault("limitations", []).append(f"ExifTool 12.76 WebP kind {kind}: read-only xpacket opaque; property refusal/whole removal tested; semantic reverse property read unsupported")
            copied = output / f"external-noop-{kind}.webp"; ours("noop-webp", external, copied)
            assert copied.read_bytes() == external.read_bytes() and webp_coded(external) == webp_coded(base_webp)
        for name in ("legacy.webp", "legacy-rebuilt.webp"):
            facts = json.loads(run(["exiftool", "-j", "-n", "-Orientation", str(output / name)]))[0]
            assert facts["Orientation"] == 1, facts
        assert webp_coded(output / "legacy.webp") == webp_coded(output / "legacy-rebuilt.webp")
        assert b"Exif\0\0" in (output / "legacy.webp").read_bytes()
        assert b"Exif\0\0" not in (output / "legacy-rebuilt.webp").read_bytes()
        for big in (False, True):
            for tiles in (False, True):
                tiff = output / f"owned-{big}-{tiles}.tif"
                own = json.loads(ours("inspect-tiff", tiff))
                assert own == {"width": 16, "height": 16, "supported": True, "segments": 1, "storage": "Tiles" if tiles else "Strips"}, own
                facts = json.loads(run(["exiftool", "-j", "-n", "-ImageWidth", "-ImageHeight", "-BitsPerSample", "-Compression", str(tiff)]))[0]
                assert facts["ImageWidth"] == 16 and facts["ImageHeight"] == 16 and facts["BitsPerSample"] == 8 and facts["Compression"] == 1, facts
                rows = exiv_values(run(["exiv2", "-P", "kycv", "-K", "Exif.Image.ImageWidth", "-K", "Exif.Image.ImageLength", str(tiff)]))
                assert rows == {"Exif.Image.ImageWidth": ("Long", 1, "16"), "Exif.Image.ImageLength": ("Long", 1, "16")}, rows
                changed = output / f"external-{big}-{tiles}.tif"; shutil.copyfile(tiff, changed)
                run(["exiftool", "-overwrite_original", "-IFD0:Artist=ExternalTiff", str(changed)])
                assert json.loads(ours("inspect-tiff", changed)) == own
                edited_tiff = output / f"edited-{big}-{tiles}.tif"
                base_bytes = tiff.read_bytes(); edited_bytes = edited_tiff.read_bytes()
                assert edited_bytes[:4] == base_bytes[:4] and edited_bytes[8:len(base_bytes)] == base_bytes[8:]
                assert (output / f"edit-noop-{big}-{tiles}.tif").read_bytes() == edited_bytes
                # Independent bounded pointer/value accounting for the owned one-IFD profile.
                endian = "big" if big else "little"
                def number(data, at, size):
                    assert at >= 0 and at + size <= len(data)
                    return int.from_bytes(data[at:at+size], endian)
                root = number(edited_bytes, 4, 4); assert root >= len(base_bytes) and root % 2 == 0
                count = number(edited_bytes, root, 2); assert 1 <= count <= 512
                assert number(edited_bytes, root + 2 + 12*count, 4) == 0
                fields = {}; extents = [(0, 8), (root, root + 6 + 12*count)]
                last = -1
                for i in range(count):
                    at = root + 2 + i*12; tag = number(edited_bytes, at, 2); typ = number(edited_bytes, at+2, 2); n = number(edited_bytes, at+4, 4)
                    assert tag > last; last = tag
                    width = {2: 1, 3: 2, 4: 4}[typ]; size = width*n
                    pos = at+8 if size <= 4 else number(edited_bytes, at+8, 4)
                    assert pos + size <= len(edited_bytes)
                    if size > 4: assert pos % 2 == 0; extents.append((pos, pos+size))
                    fields[tag] = (typ, n, edited_bytes[pos:pos+size])
                offset_tag, count_tag = (324, 325) if tiles else (273, 279)
                start = int.from_bytes(fields[offset_tag][2], endian); size = int.from_bytes(fields[count_tag][2], endian)
                assert start == 2048 and size == 256 and edited_bytes[start:start+size] == base_bytes[start:start+size]
                extents.append((start, start+size)); extents.sort()
                assert all(a[1] <= b[0] for a, b in zip(extents, extents[1:]))
                assert fields[305][2] == b"MetadataSharp\0" and fields[315][2] == b"Alice\0"
                facts = json.loads(run(["exiftool", "-j", "-IFD0:Software", "-IFD0:Artist", str(edited_tiff)]))[0]
                assert facts["Software"] == "MetadataSharp" and facts["Artist"] == "Alice", facts
                rows = exiv_values(run(["exiv2", "-P", "kycv", "-K", "Exif.Image.Software", "-K", "Exif.Image.Artist", str(edited_tiff)]))
                assert rows == {"Exif.Image.Software": ("Ascii", 14, "MetadataSharp"), "Exif.Image.Artist": ("Ascii", 6, "Alice")}, rows
                removed_tiff = output / f"edit-removed-{big}-{tiles}.tif"
                assert json.loads(ours("inspect-tiff-values", removed_tiff)) == {"artist": None, "software": "MetadataSharp"}
                assert b"Alice\0" in removed_tiff.read_bytes()  # Deliberate physical retention, not privacy scrubbing.
                assert json.loads(ours("inspect-tiff-values", changed))["artist"] == "ExternalTiff"
                refreshed = output / f"refreshed-{big}-{tiles}.tif"; ours("rewrite-tiff", changed, refreshed)
                facts = json.loads(run(["exiftool", "-j", "-IFD0:Software", "-IFD0:Artist", str(refreshed)]))[0]
                assert facts["Software"] == "Refreshed" and facts["Artist"] == "ExternalTiff", facts
        # DNG read-only profile: the two independent writers of the owned fixtures must agree byte-for-byte.
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        import dng_fixtures
        for order in ("II", "MM"):
            for shape in ("raw", "thumb"):
                data, _info = dng_fixtures.build(order, shape)
                name = f"dng-{shape}-{'ii' if order == 'II' else 'mm'}.dng"
                if (output / name).read_bytes() != data:
                    raise RuntimeError(f"DNG fixture byte mismatch between independent writers: {name}")
        report["dngFixtureAgreement"] = "byte-identical"

        # Structural oracle, independent of both writers and of the library: classic II/MM magic 42,
        # sorted unique tags, two-byte payload alignment, exact strip extents, DNGVersion on the raw leaf.
        import struct

        type_width = {1: 1, 2: 1, 3: 2, 4: 4, 5: 8, 10: 8}

        def dng_file(path):
            data = path.read_bytes()
            le = data[:2] == b"II"
            fmt = "<" if le else ">"
            order = "little" if le else "big"
            assert struct.unpack_from(fmt + "HI", data, 2) == (42, 8), path.name
            def ifd(offset):
                (n,) = struct.unpack_from(fmt + "H", data, offset)
                assert 1 <= n <= 64, (path.name, offset)
                fields = []
                last = -1
                for i in range(n):
                    at = offset + 2 + 12 * i
                    tag, typ, count = struct.unpack_from(fmt + "HHI", data, at)
                    assert tag > last and count >= 1, (path.name, offset)
                    last = tag
                    width = type_width[typ]
                    loc = at + 8
                    if width * count <= 4:
                        payload = bytes(data[loc:loc + width * count])
                    else:
                        (ptr,) = struct.unpack_from(fmt + "I", data, loc)
                        assert ptr % 2 == 0 and ptr + width * count <= len(data), (path.name, offset)
                        payload = bytes(data[ptr:ptr + width * count])
                    fields.append((tag, typ, count, payload))
                (nxt,) = struct.unpack_from(fmt + "I", data, offset + 2 + 12 * n)
                assert nxt == 0, (path.name, offset)
                return fields
            def field(fields, tag):
                return next(f for f in fields if f[0] == tag)
            def unsigned(fields, tag):
                f = field(fields, tag)
                return int.from_bytes(f[3][:4], order) if f[1] == 4 else int.from_bytes(f[3][:2], order)
            def check_image(fields):
                width = unsigned(fields, 256); height = unsigned(fields, 257)
                samples = unsigned(fields, 277); photometric = unsigned(fields, 262)
                bits = field(fields, 258)
                bps = list(struct.unpack(fmt + "H" * bits[2], bits[3]))
                expected = [16] if samples == 1 else [8, 8, 8]
                assert bps == expected, path.name
                strip = unsigned(fields, 273); strip_bytes = unsigned(fields, 279)
                assert strip_bytes == width * height * samples * (2 if samples == 1 else 1), path.name
                assert strip + strip_bytes <= len(data), path.name
                return width, height, samples, photometric
            root = ifd(8)
            sub = unsigned(root, 330) if any(f[0] == 330 for f in root) else None
            return root, sub, ifd, field, unsigned, check_image, data

        def dng_check(path, expect_leaf):
            root, sub, ifd, field, unsigned, check_image, data = dng_file(path)
            dims = check_image(root)
            if expect_leaf:
                assert field(root, 50706)[3] == b"\x01\x01\x00\x00", path.name  # DNGVersion 1.1.0.0
            else:
                assert sub is not None, path.name
                raw = ifd(sub)
                check_image(raw)
                assert field(raw, 50706)[3] == b"\x01\x01\x00\x00", path.name
                assert unsigned(raw, 262) == 32803, path.name  # CFA photometric on raw leaf
            return dims
        raw_dims = dng_check(output / "dng-raw-ii.dng", True)
        assert raw_dims[3] == 32803
        dng_check(output / "dng-raw-mm.dng", True)
        thumb_dims = dng_check(output / "dng-thumb-ii.dng", False)
        assert thumb_dims[3] == 2
        dng_check(output / "dng-thumb-mm.dng", False)
        dng_version_digits = lambda value: "".join(ch for ch in str(value) if ch.isdigit())
        facts = json.loads(run(["exiftool", "-j", "-n", "-DNGVersion", "-ImageWidth", "-ImageLength", "-Compression", "-SamplesPerPixel", "-PhotometricInterpretation", str(output / "dng-raw-ii.dng")]))[0]
        assert dng_version_digits(facts["DNGVersion"]) == "1100" and facts["ImageWidth"] == 4 and facts["ImageLength"] == 2 and facts["Compression"] == 1 and facts["SamplesPerPixel"] == 1 and facts["PhotometricInterpretation"] == 32803, facts
        facts = json.loads(run(["exiftool", "-j", "-n", "-DNGVersion", "-ImageWidth", "-ImageLength", "-Compression", "-SamplesPerPixel", "-PhotometricInterpretation", str(output / "dng-thumb-ii.dng")]))[0]
        assert dng_version_digits(facts["DNGVersion"]) == "1100" and facts["ImageWidth"] == 4 and facts["ImageLength"] == 2 and facts["Compression"] == 1 and facts["SamplesPerPixel"] == 3 and facts["PhotometricInterpretation"] == 2, facts
        own_dng = json.loads(ours("inspect-dng", output / "dng-raw-ii.dng"))
        assert own_dng == {"width": 4, "height": 2, "bitsPerSample": 16, "samplesPerPixel": 1, "supported": True, "role": "CfaRaw", "strips": 1}, own_dng
        own_dng = json.loads(ours("inspect-dng", output / "dng-thumb-ii.dng"))
        assert own_dng == {"width": 4, "height": 2, "bitsPerSample": 8, "samplesPerPixel": 3, "supported": True, "role": "RgbThumbnail", "strips": 1}, own_dng

        report["fixtures"] = [{"name": p.name, "sha256": hashlib.sha256(p.read_bytes()).hexdigest(),
                               "source": "MetadataSharp authored synthetic generator", "license": "MIT"}
                              for p in sorted(output.iterdir()) if p.suffix in (".jpg", ".xmp", ".png", ".webp", ".tif", ".dng")]
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
