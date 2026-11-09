# B8 PSD research preflight (not a census batch)

Two owned PSD v1 flat 2×2 RGB fixtures use uncompressed planar bytes: one has
no image resources; the other has individually authored EXIF Artist `B8Artist`,
XMP CreatorTool `B8Tool`, and IPTC keyword `B8Keyword` in image-resource blocks
1058, 1060 and 1028. Independent byte/length, TIFF, XML, IIM and planar checks
are in `tests/reference/test_b8_psd_fixture.py`; fixture generation uses only
the standard library. The per-format rights decision was committed before
authoring. ICC, comments and thumbnails are absent; PSB, layers and color
management remain out of scope.

Local ImageMagick 6.9.11-60 `identify` reported PSD 2×2 for both; `convert
-depth 8 rgb:-` produced the exact authored RGB pixels for each. This is a local
decoder sanity check, not a normative validation or a reproducible CI oracle.
The local pinned ExifTool 12.76 read-only JSON is preserved in
`exiftool-flat.json` and `exiftool-rich.json`. Both returned zero and reported
2×2; the rich file reported all three authored values. For **both**, ExifTool
reported `Photoshop:Compression` as `Unknown (12183)` although the independently
inspected authored compression field is zero and ImageMagick decoded the expected
pixels. This is a recorded discrepancy, not a diagnosed defect or a conformance
claim. ExifTool's paths, timestamps and stdout hashes are environment-specific.

The workflow runs `tools/reference/probe_b8_psd.py` using pinned Exiv2 0.28.9
and ExifTool 12.76. It verifies fixture bytes, source versions/build features,
records each CLI command, exit, outputs, extraction artifacts, unchanged input,
and emits compressed annotations plus an artifact. Outcomes are recorded, not
gated as support assertions. CI output and results are not claimed here until
recovered and inspected. Exiv2/ExifTool are external tools only; no upstream
fixtures/code are copied into the project or linked into production.

These probes have **not** been folded into the census, and do not complete B8.
JPEG 2000, PGF, EPS, EXV and DCP still require individual rights/oracle work;
B7 CR3 remains blocked. No percentage, denominator freeze, MetadataSharp
verification or production format support is implied.
