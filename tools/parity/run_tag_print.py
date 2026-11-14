#!/usr/bin/env python3
"""Fail-closed differential gate for the Exiv2 -pt tag listing."""
from __future__ import annotations

import argparse
import json
import re
import shlex
import subprocess
import sys
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
if str(HERE) not in sys.path:
    sys.path.insert(0, str(HERE))
import corpus  # noqa: E402

TITLE = "PARITY tag print"

def _catalog_key_ids() -> dict[str, int]:
    data = json.loads((ROOT / "src/MetadataSharp.Core/tag-catalog-data.json").read_text(encoding="utf-8"))
    return {"Exif." + group["name"] + "." + name: tag["id"]
            for group in data["exifGroups"] for tag in group["tags"] for name in tag["names"]}

CATALOG_KEY_IDS = _catalog_key_ids()
CATALOG_KEYS = set(CATALOG_KEY_IDS)
_NUMERIC_KEY = re.compile(r"^(Exif\..+)\.0[xX]([0-9A-Fa-f]+)$")


def _canonical_key(key: str) -> tuple[str, int] | tuple[str]:
    if key in CATALOG_KEY_IDS:
        group = key.rsplit(".", 1)[0]
        return group, CATALOG_KEY_IDS[key]
    match = _NUMERIC_KEY.fullmatch(key)
    if match:
        return match.group(1), int(match.group(2), 16)
    return (key,)


def _canonical_type(type_name: str) -> str:
    # TIFF IFD (type 13) is a 32-bit offset; the local value model stores that word as Long.
    return "Long" if type_name == "Ifd" else type_name


_RAW_NUMERIC_TAG = re.compile(r"\.0[xX][0-9A-Fa-f]+$")


def _is_raw_numeric_tag(key: str) -> bool:
    return bool(_RAW_NUMERIC_TAG.search(key))


PINNED_EXIV2_COMMIT = "b0859fc61fea5dedfc898aece6b15893ddf4314e"

# Exact EXIF keys omitted from the tag-table comparison for a reviewed, key-specific compatibility
# reason. Entries remain visible in the report and are not general value/description wildcards.
_TAG_PRINT_KEY_EXCEPTIONS: dict[str, str] = {
    "Exif.GPSInfo.GPSAltitudeRef": (
        "MetadataSharp follows the expanded Exif 3.0 ellipsoidal/sea-level definitions, while the pinned "
        "Exiv2 0.28.9 print table exposes only its earlier above/below sea-level labels. The raw field and "
        "numeric code remain covered by the other tag/value gates; this exact key's translated wording differs by revision."
    ),
    "Exif.Canon.PictureStylePC": (
        "The pinned Exiv2 0.28.9 Canon tag table leaves this PC-picture-style entry commented out and "
        "does not include it in -pt; MetadataSharp retains its local MakerNote decoder. This exact local-only "
        "tag is excluded from the Exiv2 tag-table comparison."
    ),
    "Exif.Canon.Categories": (
        "The pinned Exiv2 0.28.9 Canon tag table leaves this Categories entry commented out and does not "
        "include it in -pt; MetadataSharp retains its local MakerNote decoder. This exact local-only tag is "
        "excluded from the Exiv2 tag-table comparison."
    ),
    "Exif.Canon.FilterInfo": (
        "MetadataSharp retains the raw Canon FilterInfo MakerNote entry, while Exiv2 0.28.9 does not surface "
        "tag 0x4024 in these -pt records. Its exact raw key remains covered by the separate upstream parity gate."
    ),
}

# Exact display aliases, not descriptions copied from Exiv2. These pairs represent the same assigned
# values in the local Exif vocabulary and Exiv2 0.28.9's shorter/older print terminology. Any other
# value for these keys still fails comparison.
_TAG_PRINT_DISPLAY_ALIASES: dict[str, dict[str, str]] = {
    "Exif.Photo.MeteringMode": {"Multi-segment": "Pattern"},
    "Exif.Photo.ExposureProgram": {"Auto": "Normal program"},
    "Exif.Image.CalibrationIlluminant1": {"Standard light A": "17"},
    "Exif.Image.CalibrationIlluminant2": {"D65": "21"},
    "Exif.Photo.SensitivityType": {
        "0": "Unknown",
        "1": "Standard output sensitivity (SOS)",
        "2": "Recommended exposure index (REI)",
        "3": "ISO speed",
        "4": "Standard output sensitivity (SOS) and recommended exposure index (REI)",
        "5": "Standard output sensitivity (SOS) and ISO speed",
        "6": "Recommended exposure index (REI) and ISO speed",
        "7": "Standard output sensitivity (SOS) and recommended exposure index (REI) and ISO speed",
    },
}
_TAG_PRINT_DISPLAY_REASONS = {
    "Exif.Image.CalibrationIlluminant1": "The DNG field's local catalog entry preserves its numeric code; Exiv2 renders code 17 with the standard LightSource label. This exact code-to-label pair is accepted without extending the other-source interpretation table.",
    "Exif.Image.CalibrationIlluminant2": "The DNG field's local catalog entry preserves its numeric code; Exiv2 renders code 21 with the standard LightSource label. This exact code-to-label pair is accepted without extending the other-source interpretation table.",
    "Exif.Photo.MeteringMode": "The same EXIF metering code is called Pattern by the local vocabulary and Multi-segment by Exiv2; these terms describe the same assigned mode.",
    "Exif.Photo.ExposureProgram": "The same EXIF exposure-program code is called Normal program by the local vocabulary and Auto by Exiv2; this is a terminology-only difference.",
    "Exif.Photo.SensitivityType": "MetadataSharp renders the assigned EXIF meaning; Exiv2 0.28.9 prints the stored numeric code for this tag. Exact code-to-label pairs are listed and checked, not generalized to arbitrary values.",
}

# Exact reference rows that no local row can answer, because the reference reads something the library
# deliberately does not. The key is (pinned file name, key pattern): a pattern ending with "." covers
# that group prefix, the pattern "Exif.*" covers every EXIF key of a file that has no EXIF block of its
# own, and any other pattern must equal the key exactly. Only reference-only rows can be covered: a
# documented row that the library starts printing fails the comparison until the entry is removed, and
# so does every row the patterns do not name.
_SIDECAR_CONVERSION_RULE = (
    "Exiv2's XMP sidecar reader converts the packet into an EXIF block and prints the converted tags; "
    "MetadataSharp reads a sidecar as a packet and offers that conversion explicitly through "
    "MetadataSync.XmpToExif instead. The file has no EXIF block of its own (the gate requires that the "
    "library prints no EXIF row for it), so these rows measure the reference's conversion, not its tag table."
)
_TAG_PRINT_ROW_EXCEPTIONS: dict[tuple[str, str], str] = {
    ("BlueSquare.xmp", "Exif.*"): _SIDECAR_CONVERSION_RULE,
    ("StaffPhotographer-Example.xmp", "Exif.*"): _SIDECAR_CONVERSION_RULE,
    ("exiv2-bug1112.xmp", "Exif.*"): _SIDECAR_CONVERSION_RULE,
    ("issue_1959_poc.xmp", "Exif.*"): _SIDECAR_CONVERSION_RULE,
    ("issue_1998.xmp", "Exif.*"): _SIDECAR_CONVERSION_RULE,
    ("IMG_1361.dng", "Exif.SubImage1."): (
        "The 0th directory of this file points at two image directories with tag 0x014A (SubIFDs). A whole "
        "TIFF or DNG is read for its metadata directories only: strip, tile and SubIFD tags are entries and "
        "are never followed, so the reference's SubImage group has no local counterpart. The rule covers "
        "this exact file and group prefix only."
    ),
    ("IMG_1361.dng", "Exif.SubImage2."): (
        "The 0th directory of this file points at two image directories with tag 0x014A (SubIFDs). A whole "
        "TIFF or DNG is read for its metadata directories only: strip, tile and SubIFD tags are entries and "
        "are never followed, so the reference's SubImage group has no local counterpart. The rule covers "
        "this exact file and group prefix only."
    ),
    ("exiv2-pr2323_coverage1.exv", "Exif.Sony1.ColorTemperature"): (
        "This fixture holds tag 0xB021 twice in one MakerNote directory. A MakerNote directory is read once "
        "per tag ID, so that a tag can be addressed by key for an edit; the reference prints every entry. "
        "Only the second occurrence is in question here, and the rule covers this exact file and key."
    ),
    ("exiv2-pr2323_coverage4.exv", "Exif.Sony2.WBShiftABGM"): (
        "This fixture holds tag 0x2014 twice in one MakerNote directory. A MakerNote directory is read once "
        "per tag ID, so that a tag can be addressed by key for an edit; the reference prints every entry. "
        "Only the second occurrence is in question here, and the rule covers this exact file and key."
    ),
    ("exiv2-bug447.jpg", "Exif.Photo.UserComment"): (
        "The directory entry of this tag names field type 14, which is not a TIFF 6.0 field type. The reader "
        "leaves an entry of unknown type out, as TIFF 6.0 tells a reader to do, and marks the block not "
        "editable; the reference prints the numeric type and keeps the entry. The rule covers this exact "
        "file and key."
    ),
}


def _row_exception(file_name: str, row: dict[str, Any], has_local_exif: bool) -> str | None:
    """The documented reason for a reference row with no local counterpart, or None."""
    key = str(row.get("key"))
    for (name, pattern), reason in _TAG_PRINT_ROW_EXCEPTIONS.items():
        if name != file_name:
            continue
        if pattern == "Exif.*":
            if not has_local_exif and key.startswith("Exif."):
                return reason
        elif pattern.endswith("."):
            if key.startswith(pattern):
                return reason
        elif key == pattern:
            return reason
    return None


_TAG_PRINT_EXCEPTIONS: dict[str, str] = {

    # These are upstream expectation basenames for command-line/unit tests, not metadata files.
    # corpus.discover_exiv2 deliberately does not guess an image for a *_test input.
    "addmoddel": "Synthetic command-line test expectation; no corresponding media input is present in the pinned test/data tree.",
    "conversions": "Synthetic conversion-test expectation; no corresponding media input is present in the pinned test/data tree.",
    "crw-test": "Synthetic CRW test expectation; no corresponding CRW input is present in the pinned test/data tree.",
    "exifdata-test": "Synthetic ExifData unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
    "exiv2-bug1145a.exv_test": "Synthetic regression-test expectation; no corresponding EXV input is present in the pinned test/data tree.",
    "exiv2-test": "Synthetic CLI test expectation; no corresponding media input is present in the pinned test/data tree.",
    "geotag-test": "Synthetic geotag unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
    "icc-test": "Synthetic ICC unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
    "iotest": "Synthetic I/O test expectation; no corresponding media input is present in the pinned test/data tree.",
    "iso65k-test": "Synthetic ISO-65K unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
    "modify-test": "Synthetic metadata-edit test expectation; no corresponding media input is present in the pinned test/data tree.",
    "nls-test": "Synthetic localization test expectation; no corresponding media input is present in the pinned test/data tree.",
    "png-test": "Synthetic PNG unit-test expectation; no corresponding PNG input is present in the pinned test/data tree.",
    "preview-test": "Synthetic preview unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
    "stdin-test": "Synthetic standard-input test expectation; no corresponding media input is present in the pinned test/data tree.",
    "stringto-test": "Synthetic string-formatting unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
    "test_pr_1905_poc1_ref": "Synthetic pull-request proof-of-concept expectation; its generated input is not checked into the pinned test/data tree.",
    "tiff-test": "Synthetic TIFF unit-test expectation; no corresponding TIFF input is present in the pinned test/data tree.",
    "webp-test": "Synthetic WebP unit-test expectation; no corresponding WebP input is present in the pinned test/data tree.",
    "write-test-debug": "Synthetic writer-debug test expectation; no corresponding media input is present in the pinned test/data tree.",
    "write-test": "Synthetic writer unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
    "write2-test": "Synthetic second writer unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
    "xmpparser-test": "Synthetic XMP parser unit-test expectation; no corresponding media input is present in the pinned test/data tree.",
}


# Only the observed result for each exact pinned input is allowed. A changed outcome returns to parity.
_TAG_PRINT_REFUSAL_EXCEPTIONS: dict[str, tuple[str, str | int, str | None, str]] = {
    '1-string-format.jpg': ('metadatasharp-refused', 'refused', 'InvalidJpeg', 'MetadataSharp rejects the JPEG structure with InvalidJpeg. The exact pinned file 1-string-format.jpg is listed separately; the refusal is not counted as a successful comparison.'),
    '2018-01-09-exiv2-crash-002.tiff': ('metadatasharp-refused', 'refused', 'InvalidExif', 'MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file 2018-01-09-exiv2-crash-002.tiff is listed separately; the refusal is not counted as a successful comparison.'),
    '2018-01-09-exiv2-crash-003.tiff': ('metadatasharp-refused', 'refused', 'InvalidExif', 'MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file 2018-01-09-exiv2-crash-003.tiff is listed separately; the refusal is not counted as a successful comparison.'),
    'Free_Test_Data_500KB_WAV.wav': ('exiv2-refused', 1, None, 'Pinned Exiv2 0.28.9 exits with status 1 for this exact input; its -pt oracle row set is unavailable. The refusal is named and not counted as a successful comparison.'),
    'FurnaceCreekInn.jpg': ('metadatasharp-refused', 'refused', 'InvalidExif', 'MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file FurnaceCreekInn.jpg is listed separately; the refusal is not counted as a successful comparison.'),
    'IMGP0006-min.jpg': ('metadatasharp-refused', 'refused', 'InvalidJpeg', 'MetadataSharp rejects the JPEG structure with InvalidJpeg. The exact pinned file IMGP0006-min.jpg is listed separately; the refusal is not counted as a successful comparison.'),
    'coverage_xmp_doctype.exv': ('metadatasharp-refused', 'refused', 'InvalidXmp', 'MetadataSharp rejects the XMP packet with InvalidXmp. The exact pinned file coverage_xmp_doctype.exv is listed separately; the refusal is not counted as a successful comparison.'),
    'coverage_xmpsidecar_isXmpType.xmp': ('metadatasharp-refused', 'refused', 'InvalidXmp', 'MetadataSharp rejects the XMP packet with InvalidXmp. The exact pinned file coverage_xmpsidecar_isXmpType.xmp is listed separately; the refusal is not counted as a successful comparison.'),
    'cve_2017_18005_reproducer.tiff': ('metadatasharp-refused', 'refused', 'InvalidExif', 'MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file cve_2017_18005_reproducer.tiff is listed separately; the refusal is not counted as a successful comparison.'),
    'exiv2-bug443.jpg': ('metadatasharp-refused', 'refused', 'LimitExceeded', 'MetadataSharp stops at its configured parsing limit. The exact pinned file exiv2-bug443.jpg is listed separately; the refusal is not counted as a successful comparison.'),
    'exiv2-bug479.jpg': ('metadatasharp-refused', 'refused', 'InvalidExif', 'MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file exiv2-bug479.jpg is listed separately; the refusal is not counted as a successful comparison.'),
    'exiv2-bug855.jpg': ('metadatasharp-refused', 'refused', 'InvalidExif', 'MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file exiv2-bug855.jpg is listed separately; the refusal is not counted as a successful comparison.'),
    'exiv2-bug922.jpg': ('metadatasharp-refused', 'refused', 'InvalidExif', 'MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file exiv2-bug922.jpg is listed separately; the refusal is not counted as a successful comparison.'),
    'exiv2_0-26_exiv2_uncontrolled-recursion_printIFDStructure.tif': ('metadatasharp-refused', 'refused', 'InvalidExif', 'MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file exiv2_0-26_exiv2_uncontrolled-recursion_printIFDStructure.tif is listed separately; the refusal is not counted as a successful comparison.'),
    'flame.avi': ('exiv2-refused', 1, None, 'Pinned Exiv2 0.28.9 exits with status 1 for this exact input; its -pt oracle row set is unavailable. The refusal is named and not counted as a successful comparison.'),
    'heap-oob-write.tiff': ('metadatasharp-refused', 'refused', 'InvalidExif', 'MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file heap-oob-write.tiff is listed separately; the refusal is not counted as a successful comparison.'),
    'issue_1706_poc.exv': ('metadatasharp-refused', 'refused', 'LimitExceeded', 'MetadataSharp stops at its configured parsing limit. The exact pinned file issue_1706_poc.exv is listed separately; the refusal is not counted as a successful comparison.'),
    'issue_1713_poc.xmp': ('metadatasharp-refused', 'refused', 'InvalidXmp', 'MetadataSharp rejects the XMP packet with InvalidXmp. The exact pinned file issue_1713_poc.xmp is listed separately; the refusal is not counted as a successful comparison.'),
    'issue_1819_poc.exv': ('metadatasharp-refused', 'refused', 'InvalidXmp', 'MetadataSharp rejects the XMP packet with InvalidXmp. The exact pinned file issue_1819_poc.exv is listed separately; the refusal is not counted as a successful comparison.'),
    'issue_1821_poc.xmp': ('metadatasharp-refused', 'refused', 'InvalidXmp', 'MetadataSharp rejects the XMP packet with InvalidXmp. The exact pinned file issue_1821_poc.xmp is listed separately; the refusal is not counted as a successful comparison.'),
    'issue_1912_poc.jpg': ('metadatasharp-refused', 'refused', 'InvalidJpeg', 'MetadataSharp rejects the JPEG structure with InvalidJpeg. The exact pinned file issue_1912_poc.jpg is listed separately; the refusal is not counted as a successful comparison.'),
    'issue_1920_poc.tiff': ('metadatasharp-refused', 'refused', 'InvalidExif', 'MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file issue_1920_poc.tiff is listed separately; the refusal is not counted as a successful comparison.'),
    'issue_2006_poc.tiff': ('metadatasharp-refused', 'refused', 'InvalidExif', 'MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file issue_2006_poc.tiff is listed separately; the refusal is not counted as a successful comparison.'),
    'issue_851_poc.xmp': ('metadatasharp-refused', 'refused', 'InvalidXmp', 'MetadataSharp rejects the XMP packet with InvalidXmp. The exact pinned file issue_851_poc.xmp is listed separately; the refusal is not counted as a successful comparison.'),
    'issue_867_poc.psd': ('metadatasharp-refused', 'refused', 'InvalidPsd', 'MetadataSharp rejects the PSD structure with InvalidPsd. The exact pinned file issue_867_poc.psd is listed separately; the refusal is not counted as a successful comparison.'),
    'issue_94_poc3.pgf': ('metadatasharp-refused', 'refused', 'InvalidPng', 'The image reader reports InvalidPng for this PGF fixture. The exact pinned file issue_94_poc3.pgf is listed separately; the refusal is not counted as a successful comparison.'),
    'issue_ghsa_5p8g_9xf3_gfrr_poc.exv': ('metadatasharp-refused', 'refused', 'UnsupportedFeature', 'MetadataSharp reports an unsupported feature for this encrypted Nikon MakerNote fixture. The exact pinned file issue_ghsa_5p8g_9xf3_gfrr_poc.exv is listed separately; the refusal is not counted as a successful comparison.'),
    'issue_ghsa_5p8g_9xf3_gfrr_poc.webp': ('metadatasharp-refused', 'refused', 'UnsupportedFeature', 'MetadataSharp reports an unsupported feature for this encrypted Nikon MakerNote fixture. The exact pinned file issue_ghsa_5p8g_9xf3_gfrr_poc.webp is listed separately; the refusal is not counted as a successful comparison.'),
    'issue_ghsa_g44w_q3vm_gwjq_poc.jpg': ('metadatasharp-refused', 'refused', 'InvalidJpeg', 'MetadataSharp rejects the JPEG structure with InvalidJpeg. The exact pinned file issue_ghsa_g44w_q3vm_gwjq_poc.jpg is listed separately; the refusal is not counted as a successful comparison.'),
    'issue_ghsa_h9x9_4f77_336w_poc.exv': ('metadatasharp-refused', 'refused', 'InvalidExif', 'MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file issue_ghsa_h9x9_4f77_336w_poc.exv is listed separately; the refusal is not counted as a successful comparison.'),
    'issue_ghsa_jgm9_5fw5_pw9p_poc.exv': ('metadatasharp-refused', 'refused', 'AmbiguousMetadata', 'MetadataSharp refuses to resolve ambiguous metadata. The exact pinned file issue_ghsa_jgm9_5fw5_pw9p_poc.exv is listed separately; the refusal is not counted as a successful comparison.'),
    'issue_ghsa_jgm9_5fw5_pw9p_poc.jp2': ('metadatasharp-refused', 'refused', 'InvalidJp2', 'MetadataSharp rejects the JP2 structure with InvalidJp2. The exact pinned file issue_ghsa_jgm9_5fw5_pw9p_poc.jp2 is listed separately; the refusal is not counted as a successful comparison.'),
    'issue_ghsa_m479_7frc_gqqg_poc.crw': ('metadatasharp-refused', 'refused', 'InvalidExif', 'MetadataSharp reports InvalidExif while validating EXIF structure. The exact pinned file issue_ghsa_m479_7frc_gqqg_poc.crw is listed separately; the refusal is not counted as a successful comparison.'),
    'issue_ghsa_mxw9_qx4c_6m8v_poc.jp2': ('exiv2-refused', 1, None, 'Pinned Exiv2 0.28.9 exits with status 1 for this exact input; its -pt oracle row set is unavailable. The refusal is named and not counted as a successful comparison.'),
    'issue_ghsa_v5g7_46xf_h728_poc.exv': ('metadatasharp-refused', 'refused', 'InvalidXmp', 'MetadataSharp rejects the XMP packet with InvalidXmp. The exact pinned file issue_ghsa_v5g7_46xf_h728_poc.exv is listed separately; the refusal is not counted as a successful comparison.'),
    'poc_2542.exv': ('metadatasharp-refused', 'refused', 'AmbiguousMetadata', 'MetadataSharp refuses to resolve ambiguous metadata. The exact pinned file poc_2542.exv is listed separately; the refusal is not counted as a successful comparison.'),
    'pr_2574.dcp': ('metadatasharp-refused', 'not-attempted', 'magic', 'MetadataSharp does not recognize this DCP input signature and marks the file not attempted. The exact pinned file pr_2574.dcp is listed separately; the refusal is not counted as a successful comparison.'),
    'sample_960x540.asf': ('exiv2-refused', 1, None, 'Pinned Exiv2 0.28.9 exits with status 1 for this exact input; its -pt oracle row set is unavailable. The refusal is named and not counted as a successful comparison.'),
}


def _document_refusal(name: str, problem: dict[str, Any]) -> dict[str, Any] | None:
    expected = _TAG_PRINT_REFUSAL_EXCEPTIONS.get(name)
    if expected is None:
        return None
    kind, status, detail, reason = expected
    if problem.get("kind") != kind or problem.get("status") != status:
        return None
    if detail is not None and problem.get("detail") != detail:
        return None
    return {"file": name, "kind": kind, "status": status, "detail": problem.get("detail"), "reason": reason}


class TagPrintError(Exception):
    """A structural failure in the tag-print oracle or dump protocol."""


def _decode(value: str) -> str:
    slash = chr(92)
    result: list[str] = []
    at = 0
    while at < len(value):
        if value[at] != slash or at + 1 == len(value):
            result.append(value[at]); at += 1; continue
        code = value[at + 1]
        if code == "t": result.append("\t"); at += 2
        elif code == "n": result.append("\n"); at += 2
        elif code == "r": result.append("\r"); at += 2
        elif code == slash: result.append(slash); at += 2
        elif code == "x" and at + 3 < len(value):
            try: result.append(chr(int(value[at + 2:at + 4], 16))); at += 4
            except ValueError: result.append(slash); at += 1
        else: result.append(slash); at += 1
    return "".join(result)


# The reference prints one record per line and gives each column a width: the key is left-aligned in
# 44 characters, the field type in 9, the count is right-aligned in 3, and the printed value follows
# two spaces of its own (app/actions.cpp, printMetadatum). A column is a minimum width, so a longer key
# or count pushes the rest of the line right. Both details matter here: collapsing runs of spaces would
# drop the leading spaces of a stored value, and a key of the Minolta camera-settings groups contains a
# space of its own, which a whitespace split reads as the start of the type column.
_PRINT_KEY_WIDTH = 44
_PRINT_TYPE_WIDTH = 9
_PRINT_COUNT_WIDTH = 3
_TYPE_NAMES = ("Byte", "Ascii", "Short", "Long", "Rational", "SByte", "Undefined", "SShort", "SLong",
               "SRational", "Float", "Double", "Ifd")
# An unknown field type is printed as its four-digit hexadecimal code.
_TYPE_ALTERNATION = "(?:" + "|".join(_TYPE_NAMES) + r"|0[xX][0-9a-fA-F]{4})"
# The count is right-aligned in three characters and the value follows two spaces; a recorded row
# without a value (a value of no bytes) ends after the count.
_COUNT_AND_VALUE = re.compile(r" *(?P<count>\d+)(?:  (?P<value>.*))?\Z")
_WIDE_ROW = re.compile(r"(?P<key>.*?) +(?P<type>" + _TYPE_ALTERNATION + r") *(?P<tail>.*)\Z")


def parse_pt_row(line: str) -> dict[str, Any] | None:
    """Split one printed row into its key, type, count and the exact text of the value column."""
    type_start = _PRINT_KEY_WIDTH + 1
    type_end = type_start + _PRINT_TYPE_WIDTH
    if len(line) > type_end and line[_PRINT_KEY_WIDTH] == " " and line[type_end] == " ":
        key = line[:_PRINT_KEY_WIDTH].rstrip(" ")
        type_name = line[type_start:type_end].rstrip(" ")
        count = _COUNT_AND_VALUE.match(line[type_end + 1:])
        if type_name and count is not None:
            return {"key": key, "type": type_name, "count": int(count.group("count")),
                    "value": count.group("value") or ""}
    # A key longer than its column, or a count wider than its own: read the columns by their content.
    wide = _WIDE_ROW.match(line)
    if wide is None:
        return None
    count = _COUNT_AND_VALUE.match(wide.group("tail"))
    if count is None:
        return None
    return {"key": wide.group("key"), "type": wide.group("type"), "count": int(count.group("count")),
            "value": count.group("value") or ""}


def parse_exiv2(text: str) -> list[dict[str, Any]]:
    """Parse ``-pt`` rows: key, type, count and the one value Exiv2 prints (translated when known)."""
    rows: list[dict[str, Any]] = []
    for line in text.splitlines():
        if not line.startswith("Exif."):
            continue
        row = parse_pt_row(line)
        if row is not None:
            rows.append(row)
    return rows


def project_dump(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """Select the text a ``-pt`` style print would show from each dump record."""
    return [{"key": row["key"], "type": row["type"], "count": row["count"],
             "value": row["translated"] or row["raw"], "_raw_value": row["raw"],
             "_has_translation": bool(row["translated"])}
            for row in rows if row["key"].startswith("Exif.")]


def parse_dump(text: str) -> dict[str, dict[str, Any]]:
    files: dict[str, dict[str, Any]] = {}
    current: dict[str, Any] | None = None
    for number, line in enumerate(text.splitlines(), 1):
        if not line:
            continue
        fields = line.split("\t")
        if fields[0] == "FILE" and len(fields) == 2:
            if current is not None and current["status"] is None:
                raise TagPrintError(f"dump:{number}: FILE record before the previous block ended")
            path = _decode(fields[1])
            if path in files:
                raise TagPrintError(f"dump:{number}: repeated FILE record {path}")
            current = {"rows": [], "status": None, "detail": ""}
            files[path] = current
        elif fields[0] == "TAG" and len(fields) in (5, 6) and current is not None and current["status"] is None:
            try:
                count = int(fields[3])
            except ValueError as error:
                raise TagPrintError(f"dump:{number}: invalid TAG count {fields[3]!r}") from error
            current["rows"].append({"key": _decode(fields[1]), "type": fields[2], "count": count,
                                    "raw": _decode(fields[4]), "translated": _decode(fields[5]) if len(fields) == 6 else ""})
        elif fields[0] == "STATUS" and len(fields) >= 2 and current is not None and current["status"] is None:
            current["status"] = fields[1]
            current["detail"] = _decode("\t".join(fields[2:]))
        else:
            raise TagPrintError(f"dump:{number}: unexpected record: {line!r}")
    for path, result in files.items():
        if result["status"] is None:
            raise TagPrintError(f"dump omitted the end status for {path}")
    return files


def compare_rows(reference: list[dict[str, Any]], actual: list[dict[str, Any]],
                 description_aliases: dict[str, dict[str, str]] | None = None,
                 accepted_aliases: Counter[tuple[str, str, str]] | None = None,
                 accepted_padding: Counter[str] | None = None,
                 accepted_names: Counter[tuple[str, str]] | None = None,
                 accepted_line_truncations: Counter[str] | None = None,
                 accepted_type_aliases: Counter[tuple[str, str]] | None = None) -> list[dict[str, Any]]:
    """Compare rows as a multiset, allowing only exact, documented display aliases."""
    def identity(row: dict[str, Any]) -> tuple[Any, ...]:
        return _canonical_key(str(row.get("key"))), _canonical_type(str(row.get("type"))), row.get("count")

    expected_by_key: dict[tuple[Any, ...], list[dict[str, Any]]] = defaultdict(list)
    actual_by_key: dict[tuple[Any, ...], list[dict[str, Any]]] = defaultdict(list)
    for row in reference: expected_by_key[identity(row)].append(row)
    for row in actual: actual_by_key[identity(row)].append(row)
    differences: list[dict[str, Any]] = []
    for key in sorted(expected_by_key.keys() | actual_by_key.keys(), key=repr):
        expected = expected_by_key[key].copy()
        observed = actual_by_key[key].copy()
        for row in expected.copy():
            def is_padding_equivalent(candidate: dict[str, Any]) -> bool:
                key_name = str(row.get("key"))
                if row.get("type") != "Ascii" and key_name != "Exif.Photo.UserComment":
                    return False
                expected_value, actual_value = row.get("value"), candidate.get("value")
                return (isinstance(expected_value, str) and isinstance(actual_value, str)
                        and expected_value != actual_value
                        and expected_value.rstrip(" \0") == actual_value.rstrip(" \0"))

            def is_line_boundary_equivalent(candidate: dict[str, Any]) -> bool:
                key_name = str(row.get("key"))
                if row.get("type") != "Ascii" and key_name != "Exif.Photo.UserComment":
                    return False
                expected_value, actual_value = row.get("value"), candidate.get("value")
                if not isinstance(expected_value, str) or not isinstance(actual_value, str):
                    return False
                lines = actual_value.splitlines()
                return len(lines) > 1 and lines[0] == expected_value

            match = next((candidate for candidate in observed
                          if candidate == row or (
                              _canonical_key(str(candidate.get("key"))) == _canonical_key(str(row.get("key")))
                              and candidate.get("value") == row.get("value")
                          ) or is_padding_equivalent(candidate) or is_line_boundary_equivalent(candidate) or (
                              description_aliases is not None
                              and description_aliases.get(str(row.get("key")), {}).get(str(row.get("value")))
                                  == candidate.get("value")
                          )), None)
            if match is not None:
                if (match.get("key") != row.get("key") and match.get("value") == row.get("value")
                        and accepted_names is not None):
                    accepted_names[(str(row.get("key")), str(match.get("key")))] += 1
                elif (match.get("type") != row.get("type") and accepted_type_aliases is not None):
                    accepted_type_aliases[(str(row.get("type")), str(match.get("type")))] += 1
                elif match != row and is_padding_equivalent(match) and accepted_padding is not None:
                    accepted_padding[str(row.get("type"))] += 1
                elif match != row and is_line_boundary_equivalent(match) and accepted_line_truncations is not None:
                    accepted_line_truncations[str(row.get("type"))] += 1
                elif (match.get("value") != row.get("value") and accepted_aliases is not None):
                    accepted_aliases[(str(row.get("key")), str(row.get("value")), str(match.get("value")))] += 1
                expected.remove(row); observed.remove(match)
        while expected and observed:
            differences.append({"kind": "value-diff", "exiv2": expected.pop(), "metadatasharp": observed.pop()})
        differences.extend({"kind": "missing", "exiv2": row, "metadatasharp": None} for row in expected)
        differences.extend({"kind": "extra", "exiv2": None, "metadatasharp": row} for row in observed)
    return differences


def verify_raw_value_differences(differences: list[dict[str, Any]], raw_reference: list[dict[str, Any]],
                                 source_rows: list[dict[str, Any]],
                                 probes: list[dict[str, Any]] | None = None) -> tuple[list[dict[str, Any]], list[dict[str, str]]]:
    """Resolve -pt text differences only when Exiv2 -pv confirms identical raw value and structure."""
    raw_by_identity: dict[tuple[Any, ...], list[dict[str, Any]]] = defaultdict(list)
    source_by_identity: dict[tuple[Any, ...], list[dict[str, Any]]] = defaultdict(list)
    for row in raw_reference:
        raw_by_identity[( _canonical_key(str(row.get("key"))), row.get("type"), row.get("count"))].append(row)
    for row in source_rows:
        source_by_identity[(row.get("key"), row.get("type"), row.get("count"))].append(row)
    remaining: list[dict[str, Any]] = []
    verified: list[dict[str, str]] = []
    for difference in differences:
        if difference.get("kind") != "value-diff":
            remaining.append(difference)
            continue
        expected, observed = difference["exiv2"], difference["metadatasharp"]
        identity = (_canonical_key(str(expected.get("key"))), expected.get("type"), expected.get("count"))
        local_key = (observed.get("key"), observed.get("type"), observed.get("count"))
        local_source = next((row for row in source_by_identity.get(local_key, [])
                             if (row.get("translated") or row.get("raw")) == observed.get("value")), None)
        if local_source is None:
            if probes is not None and len(probes) < 20:
                probes.append({"key": expected.get("key"), "reason": "no matching local raw source row",
                               "pt_reference": expected.get("value"), "pt_local": observed.get("value")})
            remaining.append(difference)
            continue
        raw_candidates = raw_by_identity.get(identity, [])
        raw_match = next((row for row in raw_candidates if row.get("value") == local_source.get("raw")), None)
        if raw_match is None:
            if probes is not None and len(probes) < 20:
                probes.append({"key": expected.get("key"), "reason": "raw values differ",
                               "pt_reference": expected.get("value"), "pt_local": observed.get("value"),
                               "local_raw": local_source.get("raw"),
                               "exiv2_pv_raw": [row.get("value") for row in raw_candidates]})
            remaining.append(difference)
            continue
        raw_by_identity[identity].remove(raw_match)
        verified.append({"key": str(expected.get("key")), "exiv2_pt": str(expected.get("value")),
                         "metadatasharp_pt": str(observed.get("value")),
                         "raw_value": str(raw_match.get("value"))})
    return remaining, verified


def _summary(report: dict[str, Any]) -> dict[str, Any]:
    # Keep the one-line workflow annotation below GitHub's annotation payload limit while making
    # the first discrepancies actionable; the full report is written to the JSON artifact.
    selected: list[dict[str, Any]] = []
    refusal_counts: Counter[str] = Counter()
    sampled_extensions: set[str] = set()
    for item in report["differing"]:
        kind = item["kind"]
        extension = Path(item["file"]).suffix.lower()
        if kind in ("metadatasharp-refused", "exiv2-refused"):
            if refusal_counts[kind] >= 2: continue
            refusal_counts[kind] += 1
        elif kind == "rows-differ":
            if extension in sampled_extensions: continue
            sampled_extensions.add(extension)
        if len(selected) >= 8: break
        selected.append(item)
    samples = []
    for item in selected:
        sample = {key: value for key, value in item.items() if key != "rows"}
        if "rows" in item:
            sample["rows"] = []
            for row in item["rows"][:5]:
                compact = dict(row)
                for side in ("exiv2", "metadatasharp"):
                    record = compact.get(side)
                    if isinstance(record, dict):
                        compact[side] = {key: value[:160] + "…" if isinstance(value, str) and len(value) > 160 else value
                                         for key, value in record.items()}
                sample["rows"].append(compact)
        samples.append(sample)
    return {"totals": report["totals"], "failure_kinds": report["failure_kinds"],
            "difference_types": report["difference_types"],
            "padding_format_rule": report["padding_format_rule"],
            "line_boundary_rule": report["line_boundary_rule"],
            "mismatch_groups": report["mismatch_groups"][:150],
            "display_rule_hits": report["display_rule_hits"],
            "name_rule_hits": report["name_rule_hits"],
            "type_rule_hits": report["type_rule_hits"],
            "raw_value_display_hits": report["raw_value_display_hits"],
            "row_rule_hits": report["row_rule_hits"],
            "raw_display_probes": [
                {key: (value[:3] if isinstance(value, list) else value[:120] if isinstance(value, str) else value)
                 for key, value in probe.items()}
                for probe in report["raw_display_probes"][:3]
            ],
            "value_diff_by_key": report["value_diff_by_key"][:40],
            "refusals": report["refusals"],
            "formats": report["formats"],
            "exceptions": report["exceptions"], "tag_exceptions": report["tag_exceptions"],
            "raw_only_tag_exception_total": len(report["raw_only_tag_exceptions"]),
            "raw_only_tag_exceptions": report["raw_only_tag_exceptions"][:30],
            "differing_total": len(report["differing"]),
            "differing_sample": samples}


def _emit_annotation(payload: dict[str, Any]) -> None:
    import base64, zlib

    def emit(title: str, part: dict[str, Any]) -> None:
        raw = json.dumps(part, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
        encoded = base64.b64encode(zlib.compress(raw)).decode("ascii")
        if len(encoded) > 3500:
            raise TagPrintError(f"annotation payload too large for {title}: {len(encoded)} characters")
        print(f"::notice title={title}::{encoded}")

    grouped = payload.pop("mismatch_groups", [])
    refusals = payload.pop("refusals", [])
    value_differences = payload.pop("value_diff_by_key", [])
    alias_hits = payload.pop("display_rule_hits", [])
    name_hits = payload.pop("name_rule_hits", [])
    type_hits = payload.pop("type_rule_hits", [])
    raw_value_hits = payload.pop("raw_value_display_hits", [])
    row_rules = payload.pop("row_rule_hits", [])
    emit(f"{TITLE} summary", payload)
    for label, items, size in (("groups", grouped, 75), ("refusals", refusals, 17),
                               ("values", value_differences, 40), ("display rules", alias_hits, 20),
                               ("key-name rules", name_hits, 20), ("type rules", type_hits, 20),
                               ("raw-value display rules", raw_value_hits, 20),
                               ("row rules", row_rules, 25)):
        chunks = [items[i:i + size] for i in range(0, len(items), size)] or [[]]
        for index, chunk in enumerate(chunks, 1):
            emit(f"{TITLE} {label} {index}/{len(chunks)}", {label: chunk})


def run_dump(command: list[str], paths: list[Path]) -> dict[str, dict[str, Any]]:
    listing = "".join(f"{path}\n" for path in paths)
    try:
        result = subprocess.run(command + ["--files-from", "-"], input=listing, capture_output=True,
                                text=True, encoding="utf-8", errors="replace", timeout=1800, check=False)
    except (FileNotFoundError, subprocess.TimeoutExpired) as error:
        raise TagPrintError(f"MetadataSharp.Dump failed to run: {error}") from error
    if result.returncode:
        raise TagPrintError(f"MetadataSharp.Dump exited {result.returncode}: {(result.stderr or '').strip()[:400]}")
    return parse_dump(result.stdout or "")


def _run_reference(command: list[str], path: Path, mode: str = "-pt") -> tuple[int, str, str]:
    try:
        result = subprocess.run(command + [mode, str(path)], capture_output=True, text=True,
                                encoding="utf-8", errors="replace", timeout=120, check=False)
    except (FileNotFoundError, subprocess.TimeoutExpired) as error:
        raise TagPrintError(f"Exiv2 -pt failed for {path.name}: {error}") from error
    return result.returncode, result.stdout or "", result.stderr or ""


def build_report(candidates: list[corpus.Candidate], reference_command: list[str],
                 dump: dict[str, dict[str, Any]], unmatched: list[str] | None = None) -> dict[str, Any]:
    unmatched_names = set(unmatched or [])
    unexplained = unmatched_names - _TAG_PRINT_EXCEPTIONS.keys()
    if unexplained:
        raise TagPrintError("unmatched upstream expectations need named, reviewed exceptions: " + ", ".join(sorted(unexplained)))
    exceptions: list[dict[str, str]] = [
        {"file": name, "reason": _TAG_PRINT_EXCEPTIONS[name]}
        for name in sorted(unmatched_names)
    ]
    tag_exceptions = [{"key": key, "reason": reason} for key, reason in sorted(_TAG_PRINT_KEY_EXCEPTIONS.items())]
    raw_only_tags: Counter[str] = Counter()
    differing: list[dict[str, Any]] = []
    named_refusals: list[dict[str, Any]] = []
    totals: Counter[str] = Counter()
    failure_kinds: Counter[str] = Counter()
    difference_types: Counter[str] = Counter()
    value_pairs: Counter[tuple[str, str, str]] = Counter()
    accepted_aliases: Counter[tuple[str, str, str]] = Counter()
    accepted_padding: Counter[str] = Counter()
    accepted_line_truncations: Counter[str] = Counter()
    accepted_type_aliases: Counter[tuple[str, str]] = Counter()
    accepted_names: Counter[tuple[str, str]] = Counter()
    verified_raw_displays: Counter[tuple[str, str, str, str]] = Counter()
    raw_display_probes: list[dict[str, Any]] = []
    mismatch_groups: Counter[tuple[str, str, str, str]] = Counter()
    row_rule_hits: Counter[tuple[str, str, str, int, str]] = Counter()
    mismatch_locations: Counter[tuple[str, str, str, str]] = Counter()
    formats: dict[str, Counter[str]] = defaultdict(Counter)
    for candidate in candidates:
        name = candidate.label
        if name in _TAG_PRINT_EXCEPTIONS:
            exceptions.append({"file": name, "reason": _TAG_PRINT_EXCEPTIONS[name]})
            continue
        totals["files"] += 1
        format_counts = formats[candidate.format.upper()]
        format_counts["files"] += 1
        status, output, error = _run_reference(reference_command, candidate.path)
        ours = dump.get(candidate.path.as_posix())
        problem: dict[str, Any] | None = None
        if status != 0:
            problem = {"file": name, "kind": "exiv2-refused", "status": status,
                       "detail": (error.strip() or output.strip())[:300]}
        elif ours is None:
            problem = {"file": name, "kind": "missing-dump-file"}
        elif ours["status"] != "ok":
            problem = {"file": name, "kind": "metadatasharp-refused", "status": ours["status"], "detail": ours["detail"]}
        else:
            reference_rows = [row for row in parse_exiv2(output) if row["key"] not in _TAG_PRINT_KEY_EXCEPTIONS]
            has_local_exif = any(str(source_row["key"]).startswith("Exif.") for source_row in ours["rows"])
            expected = []
            for reference_row in reference_rows:
                reason = _row_exception(name, reference_row, has_local_exif)
                if reason is None:
                    expected.append(reference_row)
                else:
                    row_rule_hits[(name, str(reference_row["key"]), str(reference_row["type"]),
                                   int(reference_row["count"]), reason)] += 1
            source_by_identity: dict[tuple[str, str, int], list[dict[str, Any]]] = defaultdict(list)
            for source_row in ours["rows"]:
                source_by_identity[(source_row["key"], source_row["type"], source_row["count"])].append(source_row)
            all_observed = project_dump(ours["rows"])
            expected_keys = {_canonical_key(row["key"]) for row in expected}
            raw_only_tags.update(row["key"] for row in all_observed
                                 if _is_raw_numeric_tag(row["key"])
                                 and _canonical_key(row["key"]) not in expected_keys)
            observed = [row for row in all_observed
                        if (_canonical_key(row["key"]) in expected_keys or not _is_raw_numeric_tag(row["key"]))
                        and row["key"] not in _TAG_PRINT_KEY_EXCEPTIONS]
            rows = compare_rows(expected, observed, _TAG_PRINT_DISPLAY_ALIASES, accepted_aliases, accepted_padding,
                                accepted_names, accepted_line_truncations, accepted_type_aliases)
            if any(row["kind"] == "value-diff" for row in rows):
                raw_status, raw_output, _ = _run_reference(reference_command, candidate.path, "-Pkycv")
                if raw_status == 0:
                    rows, raw_verified = verify_raw_value_differences(rows, parse_exiv2(raw_output), ours["rows"], raw_display_probes)
                    for item in raw_verified:
                        verified_raw_displays[(item["key"], item["exiv2_pt"], item["metadatasharp_pt"], item["raw_value"])] += 1
            if rows:
                for row in rows:
                    difference_types[row["kind"]] += 1
                    if row["kind"] == "value-diff":
                        expected_row, actual_row = row["exiv2"], row["metadatasharp"]
                        key = expected_row["key"]
                        value_pairs[(key, expected_row["value"], actual_row["value"])] += 1
                        local_source = next((candidate for candidate in source_by_identity[
                            (actual_row["key"], actual_row["type"], actual_row["count"])]
                            if project_dump([candidate])[0]["value"] == actual_row["value"]), {})
                        local_is_interpreted = bool(local_source.get("translated"))
                        category = "description" if local_is_interpreted else "value"
                        if (expected_row["type"] == "Undefined" and not local_is_interpreted
                                and re.fullmatch(r"\d+(?:\s+\d+)+", actual_row["value"].strip())):
                            category = "format"
                        mismatch_groups[(category, key, expected_row["type"],
                                         expected_row["value"] + " => " + actual_row["value"])] += 1
                        mismatch_locations[(category, key, expected_row["type"], name)] += 1
                    else:
                        side = row["exiv2"] or row["metadatasharp"]
                        mismatch_groups[(row["kind"], side["key"], side["type"], str(side["count"]))] += 1
                        mismatch_locations[(row["kind"], side["key"], side["type"], name)] += 1
                problem = {"file": name, "kind": "rows-differ", "exiv2_rows": len(expected),
                           "metadatasharp_rows": len(observed), "rows": rows[:8]}
            else:
                totals["matched_rows"] += len(expected)
                format_counts["matched_rows"] += len(expected)
        if problem:
            documented = _document_refusal(name, problem)
            if documented is not None:
                named_refusals.append(documented)
                problem = None
        if problem:
            totals["failed_files"] += 1
            failure_kinds[problem["kind"]] += 1
            format_counts["failed_files"] += 1
            differing.append(problem)
    raw_only_exceptions = [
        {"key": key, "occurrences": count,
         "reason": "This exact numeric/vendor key is absent from the pinned Exiv2 0.28.9 tag registry; the raw-only MetadataSharp value is outside the -pt named-tag table comparison."}
        for key, count in sorted(raw_only_tags.items())
    ]
    value_diff_by_key = [
        {"key": key, "exiv2": expected[:100], "metadatasharp": actual[:100], "occurrences": count}
        for (key, expected, actual), count in value_pairs.most_common(100)
    ]
    mismatch_by_key: Counter[tuple[str, str, str]] = Counter()
    mismatch_examples: dict[tuple[str, str, str], Counter[str]] = defaultdict(Counter)
    for (category, key, type_name, detail), count in mismatch_groups.items():
        group = (category, key, type_name)
        mismatch_by_key[group] += count
        mismatch_examples[group][detail] += count
    grouped_differences = [
        {"category": category, "key": key, "type": type_name, "occurrences": count,
         "examples": [{"difference": detail[:180], "occurrences": occurrences}
                      for detail, occurrences in mismatch_examples[(category, key, type_name)].most_common(3)],
         "files": [{"file": name, "occurrences": occurrences}
                   for (file_category, file_key, file_type, name), occurrences in
                   sorted(mismatch_locations.items(), key=lambda item: (-item[1], item[0][3]))
                   if (file_category, file_key, file_type) == (category, key, type_name)][:10]}
        for (category, key, type_name), count in sorted(mismatch_by_key.items(), key=lambda item: (-item[1], item[0]))[:200]
    ]
    refusals = [item for item in differing if item["kind"] in
                ("metadatasharp-refused", "exiv2-refused", "missing-dump-file")]
    display_rule_hits = [
        {"key": key, "exiv2": reference, "metadatasharp": local, "occurrences": count,
         "rule": _TAG_PRINT_DISPLAY_REASONS[key]}
        for (key, reference, local), count in sorted(accepted_aliases.items())
    ]
    name_rule_hits = [
        {"exiv2_key": reference, "metadatasharp_key": local, "occurrences": count,
         "rule": "The keys identify the same tag number in the same EXIF directory; the local catalog supplies the name where Exiv2 prints a numeric fallback."}
        for (reference, local), count in sorted(accepted_names.items())
    ]
    type_rule_hits = [
        {"exiv2_type": reference, "metadatasharp_type": local, "occurrences": count,
         "rule": "TIFF IFD (type 13) is a 32-bit offset; MetadataSharp's public value model stores the same word as Long. Values and counts still compare exactly."}
        for (reference, local), count in sorted(accepted_type_aliases.items())
    ]
    raw_value_display_hits = [
        {"key": key, "exiv2_pt": reference, "metadatasharp_pt": local, "raw_value": raw,
         "occurrences": count,
         "rule": "The pinned Exiv2 -Pkycv and MetadataSharp dump report the same raw value with the same type and count; only -pt presentation text differs."}
        for (key, reference, local, raw), count in sorted(verified_raw_displays.items())
    ]
    row_rules = [
        {"file": file_name, "key": key, "type": type_name, "count": count, "occurrences": occurrences,
         "rule": reason}
        for (file_name, key, type_name, count, reason), occurrences in sorted(row_rule_hits.items())
    ]
    padding_format_rule = {
        "occurrences": sum(accepted_padding.values()),
        "types": dict(sorted(accepted_padding.items())),
        "rule": "Trailing space/NUL padding is ignored only for ASCII fields and EXIF UserComment; internal whitespace and all non-text values remain exact.",
    }
    line_boundary_rule = {
        "occurrences": sum(accepted_line_truncations.values()),
        "types": dict(sorted(accepted_line_truncations.items())),
        "rule": "For ASCII and EXIF UserComment text only, a local value may continue after a newline when the pinned -pt line equals the exact first line; the complete local text is retained. No other prefix or value difference is accepted.",
    }
    return {"kind": "TagPrint", "totals": dict(totals), "failure_kinds": dict(sorted(failure_kinds.items())),
            "difference_types": dict(sorted(difference_types.items())),
            "mismatch_groups": grouped_differences,
            "display_rule_hits": display_rule_hits,
            "name_rule_hits": name_rule_hits,
            "type_rule_hits": type_rule_hits,
            "raw_value_display_hits": raw_value_display_hits,
            "row_rule_hits": row_rules,
            "raw_display_probes": raw_display_probes,
            "padding_format_rule": padding_format_rule,
            "line_boundary_rule": line_boundary_rule,
            "value_diff_by_key": value_diff_by_key,
            "refusals": refusals + named_refusals,
            "named_refusals": named_refusals,
            "formats": {key: dict(value) for key, value in sorted(formats.items())},
            "exceptions": exceptions, "tag_exceptions": tag_exceptions,
            "raw_only_tag_exceptions": raw_only_exceptions, "differing": differing}


def text_report(report: dict[str, Any]) -> str:
    totals = report["totals"]
    lines = [f"{TITLE} against pinned Exiv2 -pt",
             f"  files: {totals.get('files', 0)}; matched rows: {totals.get('matched_rows', 0)}; "
             f"failed files: {totals.get('failed_files', 0)}"]
    for format_name, counts in report["formats"].items():
        lines.append(f"  {format_name}: {counts.get('matched_rows', 0)} rows matched / "
                     f"{counts['files']} files ({counts.get('failed_files', 0)} failed)")
    for entry in report["exceptions"]:
        lines.append(f"  named missing-input exception: {entry['file']}: {entry['reason']}")
    for entry in report["tag_exceptions"]:
        lines.append(f"  named tag exception: {entry['key']}: {entry['reason']}")
    padding_rule = report["padding_format_rule"]
    lines.append(f"  padding-format rule: {padding_rule['occurrences']} rows across {padding_rule['types']}: {padding_rule['rule']}")
    line_boundary_rule = report["line_boundary_rule"]
    lines.append(f"  line-boundary text rule: {line_boundary_rule['occurrences']} rows across {line_boundary_rule['types']}: {line_boundary_rule['rule']}")
    for entry in report["display_rule_hits"]:
        lines.append(f"  display rule: {entry['key']} {entry['exiv2']!r} => {entry['metadatasharp']!r} "
                     f"({entry['occurrences']} rows): {entry['rule']}")
    for entry in report["row_rule_hits"]:
        lines.append(f"  row rule: {entry['file']} {entry['key']} [{entry['type']} {entry['count']}] "
                     f"({entry['occurrences']} rows): {entry['rule']}")
    for entry in report["raw_only_tag_exceptions"]:
        lines.append(f"  named raw-only tag exception: {entry['key']} ({entry['occurrences']} rows): {entry['reason']}")
    for entry in report["mismatch_groups"]:
        examples = "; ".join(f"{item['difference']} ({item['occurrences']})" for item in entry["examples"])
        lines.append(f"  {entry['category']} {entry['key']} [{entry['type']}]: {entry['occurrences']} rows; {examples}")
    for entry in report["refusals"]:
        lines.append(f"  refusal {entry['file']}: {entry['kind']} {entry.get('status', '')} "
                     f"{entry.get('detail', '')} {entry.get('reason', '')}")
    return "\n".join(lines) + "\n"


def verify_oracle(tree: Path, executable: str) -> None:
    try:
        revision = subprocess.run(["git", "-C", str(tree), "rev-parse", "HEAD"], capture_output=True,
                                  text=True, timeout=30, check=True).stdout.strip()
        version = subprocess.run([executable, "--version"], capture_output=True, text=True,
                                 timeout=30, check=True).stdout.splitlines()[0]
    except (OSError, subprocess.SubprocessError, IndexError) as error:
        raise TagPrintError(f"cannot verify pinned Exiv2 oracle: {error}") from error
    if revision != PINNED_EXIV2_COMMIT:
        raise TagPrintError(f"Exiv2 source checkout is {revision}, expected {PINNED_EXIV2_COMMIT}")
    if "0.28.9" not in version:
        raise TagPrintError(f"Exiv2 binary is not version 0.28.9: {version}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Compare MetadataSharp's tag output with pinned exiv2 -pt.")
    parser.add_argument("--tree", type=Path, required=True, help="pinned Exiv2 checkout")
    parser.add_argument("--exiv2", required=True, help="pinned Exiv2 executable")
    parser.add_argument("--dump-command", required=True, help="MetadataSharp.Dump command")
    parser.add_argument("--out", type=Path, default=ROOT / "artifacts/parity/tag-print")
    parser.add_argument("--annotate", action="store_true")
    args = parser.parse_args(argv)
    verify_oracle(args.tree, args.exiv2)
    candidates, unmatched = corpus.discover_exiv2(args.tree)
    paths = [candidate.path for candidate in candidates]
    dump = run_dump(shlex.split(args.dump_command), paths)
    expected_paths = {path.as_posix() for path in paths}
    unexpected_paths = set(dump) - expected_paths
    if unexpected_paths:
        raise TagPrintError("dump returned unrequested files: " + ", ".join(sorted(unexpected_paths)[:5]))
    report = build_report(candidates, [args.exiv2], dump, unmatched)
    args.out.mkdir(parents=True, exist_ok=True)
    (args.out / "tag-print-report.json").write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    text = text_report(report)
    (args.out / "tag-print-report.txt").write_text(text, encoding="utf-8")
    print(text, end="")
    if args.annotate:
        _emit_annotation(_summary(report))
    return 1 if report["totals"].get("failed_files") else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (TagPrintError, corpus.CorpusError) as failure:
        print(f"::error title={TITLE} harness::{failure}", file=sys.stderr)
        sys.exit(2)
