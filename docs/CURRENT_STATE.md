# Current state

Updated 2026-11-11. Rules: `METADATASHARP_PROMPT.md`. History: `docs/archive/` (read-only).

## Done (read side unless noted)

- JPEG: all frame types; EXIF/IPTC/XMP read and restricted editing; stray bytes, bytes after EOI and
  IPTC split across several APP13 segments are read (edits copy unknown bytes verbatim).
- PNG, WebP: EXIF/XMP read and restricted editing. Standalone TIFF: layout accounting and
  restricted descriptive editing. XMP packets and sidecars (bounded profile). EXV: read.
  DNG: narrow read-only inventory. PSD/JP2/EPS: narrow inspection.
- EXIF vocabulary: `ExifTags` (names, types, counts, value meanings from CIPA DC-008 / TIFF 6.0).
- MakerNotes (`ExifMetadata.MakerNote`, read-only): Canon (incl. arrays and AFInfo), Nikon 1/2/3
  (incl. versioned and encrypted arrays), Fujifilm, Pentax (AOC, DNG, DNGPrivateData),
  Sony (incl. enciphered arrays), Panasonic, Olympus/OM System (incl. sub-directories).
- Reading tolerates unaligned TIFF offsets.
- XMP: `XmpPacket.Entries` lists every property of any packet (structures, arrays of structures,
  qualifiers) as read-only path rows; editing stays on the bounded profile. NUL padding is ignored.
- TIFF-based files: IPTC read (`TiffIptc.Read`, tag 0x83BB or the resource in 0x8649).
- Parity harness: `tools/MetadataSharp.Dump` vs the pinned exiv2 0.28.9 corpus (CI job
  `differential-parity`). Run 42962213342, values agreeing of reference keys: EXIF 29,768 of
  34,821; XMP 1,895 of 2,641; IPTC 68 of 319 (no IPTC API for PNG or sidecars; typed values raw).
- Interpreted values: `ExifTags.Interpret` for standard EXIF tags (the standard's wording, rationals
  with units, GPS, Flash, versions, UserComment), the dump's sixth column, measured apart from raw
  values. Run 42967101642: 8,371 of 11,382 EXIF texts equal the reference's; 2,782 differ in wording.

## Next (in order)

1. BMFF read (HEIC/HEIF/AVIF), 4.2c text fixes, IPTC/XMP dump names — PR #75.
2. TIFF-based RAW read (CR2, NEF, ARW, ORF, RW2, PEF) with MakerNotes, measured on the corpus.
3. Interpreted values for MakerNote, IPTC and XMP entries (standard EXIF is done).
4. Remaining MakerNote vendors (Minolta, Casio, Samsung, Sigma).
5. Rewrite the Python parity harness and fixture generators in C# (one toolchain).
6. Write side: TIFF/RAW, then BMFF; MakerNote-safe rules per vendor.

## Known limits

- Writes are restricted and refuse when safety is not established; MakerNotes are never moved.
- Interpreted (human-readable) values are not produced yet; the dump prints raw values.
- No ICC, preview or comment APIs yet; no BMFF/RAW write.
