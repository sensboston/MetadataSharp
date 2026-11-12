# Current state

Updated 2026-11-11. Rules: `METADATASHARP_PROMPT.md`. History: `docs/archive/` (read-only).

## Done (read side unless noted)

- JPEG: all frame types; EXIF/IPTC/XMP read and restricted editing; stray bytes, bytes after EOI and
  IPTC split across several APP13 segments are read (edits copy unknown bytes verbatim).
- PNG, WebP: EXIF/XMP read and restricted editing. Standalone TIFF: layout accounting and
  restricted descriptive editing. XMP packets and sidecars (bounded profile). EXV: read.
  DNG: narrow read-only inventory. PSD: header and resources of any v1 file, with EXIF, IPTC and
  XMP read from them (`PsdMetadata.ReadExif/ReadIptc/ReadXmp`). JP2/EPS: narrow inspection.
- EXIF vocabulary: `ExifTags` (names, types, counts, value meanings from CIPA DC-008 / TIFF 6.0;
  names only, marked by `ExifTagDefinition.Source`, from TIFF/EP, DNG 1.7, DCF, Microsoft, PIM).
- MakerNotes (`ExifMetadata.MakerNote`, read-only): Canon (incl. arrays and AFInfo), Nikon 1/2/3
  (incl. versioned and encrypted arrays, preview directory), Fujifilm, Pentax (AOC, DNG, DNGPrivateData),
  Sony (incl. enciphered arrays, camera settings, the A100's Minolta directory), Panasonic, Olympus/OM System (incl. sub-directories),
  Minolta/Konica Minolta (incl. the camera-settings arrays), Casio (both layouts), Samsung
  (plain IFD and PictureWizard; "AOC" notes read as Pentax), Sigma/Foveon. Run 42974953667: +113
  EXIF values (Minolta 90, Sigma 23); the corpus has no Casio or Samsung file, so unit tests only.
- Reading tolerates unaligned TIFF offsets. EXIF that the editable profile refuses for an unknown
  field type, strip/tile/SubIFD tags or values sharing bytes is read with
  `ExifMetadata.IsEditable` false (the entry of unknown type is left out) and is still refused for
  editing; malformed EXIF is still refused for reading too.
- XMP: `XmpPacket.Entries` lists every property of any packet (structures, arrays of structures,
  qualifiers) as read-only path rows; editing stays on the bounded profile. NUL padding is ignored.
- TIFF-based files: IPTC read (`TiffIptc.Read`, tag 0x83BB or the resource in 0x8649).
- IPTC read-only forms (`IptcPacket.IsEditable` false): padding, repeated equal controls or resources,
  a digest without a stream. PNG: EXIF and IPTC in "Raw profile type" text chunks, eXIf after IDAT.
- Previews, read-only: `Previews.List` / `Previews.Extract` in `MetadataSharp.Formats` (EXIF thumbnail,
  MakerNote pointers, JPEG sub-images of TIFF-based raw files, BMFF `thmb` items, PSD resource 0x040C).
  `MetadataSharp.Dump --previews`; compared with `exiv2 -pp` as kind "Preview" in the parity job.
- Parity harness: `tools/MetadataSharp.Dump` vs the pinned exiv2 0.28.9 corpus (CI job
  `differential-parity`). Run 42962213342, values agreeing of reference keys: EXIF 29,768 of
  34,821; XMP 1,895 of 2,641; IPTC 68 of 319 (no IPTC API for PNG or sidecars; typed values raw).
- Interpreted values: `ExifTags.Interpret` for standard EXIF tags (short labels for enumerated
  values, rationals with units, GPS, Flash, versions, UserComment, `(raw)` for an unreadable value),
  the dump's sixth column, measured apart from raw values. Run 42971951262: 10,975 of 11,382 EXIF
  texts equal the reference's (96.4%); the rest are terms where the reference leaves the standard.
- Interpreted values beyond standard EXIF: `IptcDatasetNames.Interpret` (numbers, dates, times) and
  `MakerNote.Interpret` for Canon, Nikon3 and Sony (hand-written labels and units for the tags and
  numbers the corpus has; an unlisted number reads `(n)`; other vendors answer null). Left out on
  purpose: logarithmic codes (apertures, shutter speeds, sensitivities), lens identifiers, fields
  whose meaning depends on the camera model.
- Interpreted report: the rate is over the values both sides interpret; a value we return nothing
  for is `not-interpreted`, counted beside it. Run 43071738560: 15,951 of 16,319 texts equal (97.7%);
  not interpreted and translated by the reference: 4,400 (Canon 807, Nikon 948, Sony 534, other
  vendors 1,906, standard EXIF 205). Equal of both-interpreted: Canon 3,288 of 3,334, Nikon 2,518
  of 2,538, Sony 2,300 of 2,330, standard EXIF 7,845 of 8,117; the reference translates no IPTC value.
- Write, TIFF-based files (TIFF, DNG, CR2, NEF, ARW, PEF, ORF, RW2): `TiffBasedMetadataDocument`
  `SaveExif/SaveXmp/SaveIptc`. A value that fits is written in place; what grows is appended and
  one pointer changed; no byte moves, old bytes stay. Image data and the MakerNote are never written
  to; an append is refused when image data runs to the end of the file. The output is read back and
  refused unless only the named tags differ. Parity job (run 43082543288): `exiv2 -pa` reads the 84
  edited fixtures and the 27 files written from the corpus's TIFFs; the corpus has no whole RAW file.
  Capability rows for TIFF, DNG, ARW, CR2, NEF, ORF, PEF (authored layouts) and RW2 (in place only).
- Write, BMFF (HEIC, HEIF, AVIF): `BmffMetadataDocument` `SaveExif/SaveXmp` edit the Exif and XMP
  items of the `meta` box. An item that keeps its length is written in place; one that grows or is
  new goes to a new `mdat` at the end and its `iloc` entry is repointed; no item data moves, old
  bytes stay. `meta` is rewritten in place, grows when it is the last box, or is appended (the old
  one becomes `free`). The output is read back and refused unless only the edit differs. Parity job
  (run 43093871306): `exiv2 -pa` reads the 36 edited fixtures and 30 of the 33 files written from
  the corpus's 15 BMFF files (the tool reads no source of the other 3). Rows for AVIF, HEIC, HEIF.
- BMFF Exif items are bounded by the budgets, not by the 65,527 bytes of a JPEG APP1 segment:
  read, edit and insert. `BmffReadOptions` and `BmffMetadata.Read` default to the whole-file
  budgets below. Run 43104853783: 1,845 tests; `exiv2 -pa` reads 46 edited fixtures, 10 of them
  with an Exif item of 70,116 bytes or more. JPEG, PNG, WebP and PSD blocks keep the cap.
- Whole TIFF-based files: default `TiffReadOptions` (and `TiffExif.Read` without limits) allow a
  value of 16 MiB and 64 MiB retained. The `MetadataLimits` defaults (65,527 bytes, 1 MiB) are the
  sizes of an EXIF block in a JPEG segment and refused `ReaganLargeTiff.tiff` (ICC profile, tag
  0x8773, 1,613,600 bytes); with the new defaults no corpus TIFF is refused for a budget.
- ICC profiles, read-only: `IccProfiles.Read` returns the profile bytes of JPEG and EXV (APP2 chunks),
  PNG (iCCP), TIFF (tag 0x8773, first directory), WebP (ICCP), HEIC/HEIF/AVIF (`colr` of the primary
  item), JPEG 2000 (`colr` of `jp2h`) and PSD (resource 0x040F); header checked (size, `acsp`), nothing
  interpreted; new code `InvalidIccProfile`. `MetadataSharp.Dump --icc`; the parity job requires the
  17 profiles the pinned tests give for `exiv2 -pC` (`tools/parity/run_icc.py`).
- Comments, read-only: `Comments.Read` lists the COM segments of a JPEG or EXV and the PNG text
  chunks with the keyword `Comment` (`exiv2 -pc` prints `Description` instead, so no parity for PNG).
  `MetadataSharp.Dump --comments`. Capability rows: ICC under Extract for ten kinds, comments under Read.
  The parity job also runs both readers over every corpus file (`tools/parity/run_sweep.py`).
- Canon CR3, read-only: `Cr3Metadata.Read` gives the CMT1, CMT2 and CMT4 boxes of the Canon box as
  the IFD0, Exif and GPS directories of one block (not editable), CMT3 as the Canon MakerNote, and
  the packet of the XMP box. `Previews` lists THMB (version 0) and PRVW; a picture has to end in
  its box. Tests author every layout. The corpus has one CR3, `Canon-R6-pruned.CR3`, with no stored
  expectation: the parity job runs the pinned tool on it (run 43121692654): 225 of the 226 keys of
  `exiv2 -Pkycvt` with equal values (missing `Exif.CanonFi.RFLensType`), both previews of `exiv2 -pp`.

## Next (in order)

1. Write side: MakerNote-safe rules per vendor; removal of a whole BMFF item.
2. Interpreted values: logarithmic MakerNote codes, model-dependent fields, the other vendors
   (1,906 corpus texts), XMP.
3. Rewrite the Python parity harness and fixture generators in C# (one toolchain).

## Known limits

- Writes are restricted and refuse when safety is not established; MakerNotes are never moved.
- Interpreted values cover standard EXIF, IPTC and part of Canon/Nikon/Sony MakerNotes; not XMP.
- ICC profiles and comments are read, never written; a BMFF item is edited or added, never
  removed. Previews are not decoded and not written. CR3 is read, never written.
- EXIF blocks of PNG, WebP and PSD are still held to 65,527 bytes (`ExifTooLarge`), a JPEG rule.
