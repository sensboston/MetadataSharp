# Pinned `-pt` refusal catalog

The parity report records each reader refusal by the exact pinned fixture name and observed status. These are **named refusals, not successful tag comparisons**. A refusal is exempted only when the same file produces the same outcome again; a changed status or code returns the file to the failing comparison set. The per-file inventory is deliberately kept separate from value, name, format, and description differences.

## MetadataSharp outcomes

The descriptions below explain the observed error code; they do not claim the fixture's bytes were independently repaired or compared.

| Exact pinned input | Observed result | Meaning recorded by the gate |
| --- | --- | --- |
| `1-string-format.jpg` | `refused / InvalidJpeg` | JPEG structure validation rejected the input. |
| `2018-01-09-exiv2-crash-002.tiff` | `refused / InvalidExif` | EXIF structure validation rejected the input. |
| `2018-01-09-exiv2-crash-003.tiff` | `refused / InvalidExif` | EXIF structure validation rejected the input. |
| `FurnaceCreekInn.jpg` | `refused / InvalidExif` | EXIF structure validation rejected the input. |
| `IMGP0006-min.jpg` | `refused / InvalidJpeg` | JPEG structure validation rejected the input. |
| `coverage_xmp_doctype.exv` | `refused / InvalidXmp` | XMP packet validation rejected the input. |
| `coverage_xmpsidecar_isXmpType.xmp` | `refused / InvalidXmp` | XMP packet validation rejected the input. |
| `cve_2017_18005_reproducer.tiff` | `refused / InvalidExif` | EXIF structure validation rejected the input. |
| `exiv2-bug443.jpg` | `refused / LimitExceeded` | The configured metadata parsing limit was reached. |
| `exiv2-bug479.jpg` | `refused / InvalidExif` | EXIF structure validation rejected the input. |
| `exiv2-bug855.jpg` | `refused / InvalidExif` | EXIF structure validation rejected the input. |
| `exiv2-bug922.jpg` | `refused / InvalidExif` | EXIF structure validation rejected the input. |
| `exiv2_0-26_exiv2_uncontrolled-recursion_printIFDStructure.tif` | `refused / InvalidExif` | EXIF structure validation rejected the input. |
| `heap-oob-write.tiff` | `refused / InvalidExif` | EXIF structure validation rejected the input. |
| `issue_1706_poc.exv` | `refused / LimitExceeded` | The configured metadata parsing limit was reached. |
| `issue_1713_poc.xmp` | `refused / InvalidXmp` | XMP packet validation rejected the input. |
| `issue_1819_poc.exv` | `refused / InvalidXmp` | XMP packet validation rejected the input. |
| `issue_1821_poc.xmp` | `refused / InvalidXmp` | XMP packet validation rejected the input. |
| `issue_1912_poc.jpg` | `refused / InvalidJpeg` | JPEG structure validation rejected the input. |
| `issue_1920_poc.tiff` | `refused / InvalidExif` | EXIF structure validation rejected the input. |
| `issue_2006_poc.tiff` | `refused / InvalidExif` | EXIF structure validation rejected the input. |
| `issue_851_poc.xmp` | `refused / InvalidXmp` | XMP packet validation rejected the input. |
| `issue_867_poc.psd` | `refused / InvalidPsd` | PSD structure validation rejected the input. |
| `issue_94_poc3.pgf` | `refused / InvalidPng` | The image reader returned `InvalidPng` for this PGF input. |
| `issue_ghsa_5p8g_9xf3_gfrr_poc.exv` | `refused / UnsupportedFeature` | The encrypted Nikon MakerNote layout is unsupported. |
| `issue_ghsa_5p8g_9xf3_gfrr_poc.webp` | `refused / UnsupportedFeature` | The encrypted Nikon MakerNote layout is unsupported. |
| `issue_ghsa_g44w_q3vm_gwjq_poc.jpg` | `refused / InvalidJpeg` | JPEG structure validation rejected the input. |
| `issue_ghsa_h9x9_4f77_336w_poc.exv` | `refused / InvalidExif` | EXIF structure validation rejected the input. |
| `issue_ghsa_jgm9_5fw5_pw9p_poc.exv` | `refused / AmbiguousMetadata` | The reader refused to choose between ambiguous metadata. |
| `issue_ghsa_jgm9_5fw5_pw9p_poc.jp2` | `refused / InvalidJp2` | JP2 structure validation rejected the input. |
| `issue_ghsa_m479_7frc_gqqg_poc.crw` | `refused / InvalidExif` | EXIF structure validation rejected the input. |
| `issue_ghsa_v5g7_46xf_h728_poc.exv` | `refused / InvalidXmp` | XMP packet validation rejected the input. |
| `poc_2542.exv` | `refused / AmbiguousMetadata` | The reader refused to choose between ambiguous metadata. |
| `pr_2574.dcp` | `not-attempted / magic` | The reader did not recognize the DCP input signature. |

## Pinned Exiv2 oracle outcomes

These files have no usable Exiv2 `-pt` row set at the pinned version; they are likewise named separately and never counted as matched rows.

| Exact pinned input | Observed result | Meaning recorded by the gate |
| --- | --- | --- |
| `Free_Test_Data_500KB_WAV.wav` | `exiv2-refused / exit 1` | Exiv2 0.28.9 reports an unknown image type. |
| `flame.avi` | `exiv2-refused / exit 1` | Exiv2 0.28.9 reports an unknown image type. |
| `issue_ghsa_mxw9_qx4c_6m8v_poc.jp2` | `exiv2-refused / exit 1` | Exiv2 0.28.9 reports corrupted image metadata. |
| `sample_960x540.asf` | `exiv2-refused / exit 1` | Exiv2 0.28.9 reports an unknown image type. |

The executable exception table is in `tools/parity/run_tag_print.py`; tests require every entry to name a reason and require its observed status to remain unchanged. The report continues to emit these entries with all remaining differences so the exception set cannot silently stand in for parity.

## Documented row rules

A row rule names one reference row that no local row answers, because the reference reads something
this library deliberately does not: the conversion its sidecar reader performs. The gate keeps such a
row visible in its report, counts it in no matched-row total, and requires the exact pinned file and
key listed below: a different file, a different key, or a local row that starts answering the same key
fails the comparison again. There is no rule for a structure the local reader lacked: image
directories named by tag 0x014A, a MakerNote directory that states one tag ID twice and an entry whose
field type TIFF does not define are read and compared row by row.

| Exact pinned input | Exact key or group prefix | Why the reference row has no local counterpart |
| --- | --- | --- |
| `BlueSquare.xmp` | `Exif.*` | The reference's XMP sidecar reader converts the packet into an EXIF block and prints the converted tags. A sidecar is read as a packet here, and `MetadataSync.XmpToExif` is the explicit conversion. The rule applies only while the file prints no EXIF row at all. |
| `StaffPhotographer-Example.xmp` | `Exif.*` | Same sidecar conversion rule. |
| `exiv2-bug1112.xmp` | `Exif.*` | Same sidecar conversion rule. |
| `issue_1959_poc.xmp` | `Exif.*` | Same sidecar conversion rule. |
| `issue_1998.xmp` | `Exif.*` | Same sidecar conversion rule. |

## What the comparison covers without a rule

| Structure | How it is read |
| --- | --- |
| Image directories named by tag 0x014A (SubIFDs) | A whole TIFF-based file lists them as `Exif.SubImage1.*`, `Exif.SubImage2.*` in the order the tag lists them, next to the metadata. `ExifMetadata.SubImages` holds them, `ExifMetadata.Entries` does not: they describe pictures, so no edit and no layout plan reaches them. Nothing inside them is followed. |
| A MakerNote directory that states one tag ID twice | Every occurrence is listed, as the reference prints it; the lookup, a key name and an edit address the first one. |
| An entry whose field type is not one of the twelve TIFF types | The strict read refuses the block and the read-only rules read it: the value is one byte per element, as the reference reads it, the entry keeps the type code the file states, and the block is not editable. |

## How a reference row is read

The reference prints one record per line with fixed column widths: the key is left-aligned in 44
characters, the field type in 9, the count is right-aligned in 3, and the printed value follows two
spaces of its own. The gate reads those columns rather than splitting on whitespace, because a stored
value may begin with spaces and one MakerNote group names a tag with a space in its name. The value
column keeps its exact text, including leading and internal whitespace; only trailing space and NUL
padding of an ASCII field is accepted as a print difference, since the reference stops an ASCII value
at its first NUL.
