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
  Sony (incl. enciphered arrays), Panasonic, Olympus/OM System (incl. sub-directories),
  Minolta/Konica Minolta (incl. the camera-settings arrays), Casio (both layouts), Samsung
  (plain IFD and PictureWizard; "AOC" notes read as Pentax), Sigma/Foveon. Run 42974953667: +113
  EXIF values (Minolta 90, Sigma 23); the corpus has no Casio or Samsung file, so unit tests only.
- Reading tolerates unaligned TIFF offsets.
- XMP: `XmpPacket.Entries` lists every property of any packet (structures, arrays of structures,
  qualifiers) as read-only path rows; editing stays on the bounded profile. NUL padding is ignored.
- TIFF-based files: IPTC read (`TiffIptc.Read`, tag 0x83BB or the resource in 0x8649).
- Parity harness: `tools/MetadataSharp.Dump` vs the pinned exiv2 0.28.9 corpus (CI job
  `differential-parity`). Run 42962213342, values agreeing of reference keys: EXIF 29,768 of
  34,821; XMP 1,895 of 2,641; IPTC 68 of 319 (no IPTC API for PNG or sidecars; typed values raw).
- Interpreted values: `ExifTags.Interpret` for standard EXIF tags (short labels for enumerated
  values, rationals with units, GPS, Flash, versions, UserComment, `(raw)` for an unreadable value),
  the dump's sixth column, measured apart from raw values. Run 42971951262: 10,975 of 11,382 EXIF
  texts equal the reference's (96.4%); the rest are terms where the reference leaves the standard.

## Next (in order)

1. BMFF read (HEIC/HEIF/AVIF), 4.2c text fixes, IPTC/XMP dump names — PR #75.
2. TIFF-based RAW read (CR2, NEF, ARW, ORF, RW2, PEF) with MakerNotes, measured on the corpus.
3. Interpreted values for MakerNote, IPTC and XMP entries (standard EXIF is done); name
   InteroperabilityVersion in the vocabulary (it changes raw key counts, so it is its own step).
4. Rewrite the Python parity harness and fixture generators in C# (one toolchain).
5. Write side: TIFF/RAW, then BMFF; MakerNote-safe rules per vendor.

## Known limits

- Writes are restricted and refuse when safety is not established; MakerNotes are never moved.
- Interpreted (human-readable) values are not produced yet; the dump prints raw values.
- No ICC, preview or comment APIs yet; no BMFF/RAW write.
