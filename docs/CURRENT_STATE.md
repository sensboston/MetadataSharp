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
- JPEG XL, read-only: `JxlMetadata.Read` gives the block of the `Exif` box, the packet of the `xml `
  box and `HasJumbf` (a `jumb` box is noticed, not read). A `brob` box is Brotli-decoded
  (`System.IO.Compression`) when it stands for either, never past the retained budget; a bare
  codestream gives an empty result. Tests author every layout; the corpus has three files, none with
  a `brob` box: 177 of their 179 stored keys equal (run 43126968305), the other two are the
  multi-line descriptions of `Reagan.jxl`, cut at the line end in the stored text as in `Reagan.jpg`.
- Write, JPEG ICC profile and comment: `JpegMetadataDocument` `SaveIccProfile` / `SaveComment`
  (null removes). The profile replaces every APP2 `ICC_PROFILE` segment before the first scan with
  numbered chunks of at most 65,519 bytes after the leading APP0/APP1 segments (at most 255, else
  `IccProfileTooLarge`; no chunk under 4 bytes, where exiv2 stops reading a file); the comment
  replaces every COM there with one (UTF-8, at most 65,533 bytes, else `CommentTooLarge`). All other
  bytes are copied; the planned output is scanned and compared before it is written. Run
  43131239557: 2,215 tests; `exiv2 -pC`, `-pc` and `-pa` agree on 16 edited fixtures and on the 266
  files written from the corpus's 123 JPEG files (no refusal). Six capability rows for JPEG.
- Write, PNG ICC profile and comment, WebP ICC profile: `PngMetadataDocument` `SaveIccProfile` /
  `SaveComment` and `WebPMetadataDocument` `SaveIccProfile` (null removes). PNG: one `iCCP` chunk
  (name `ICC Profile`, zlib of the platform) where the old one stood, else directly after `IHDR`;
  writing a profile takes an `sRGB` chunk out, removing one leaves it. The comment replaces every
  `tEXt` / `zTXt` / `iTXt` chunk with the keyword `Comment` by one where the first stood, else before
  the first `IDAT`: `tEXt` when the text is ISO-8859-1 and reads back as itself, else uncompressed
  `iTXt` in UTF-8. WebP: one `ICCP` chunk where the old one stood, else directly after `VP8X`, with
  the ICC flag; a simple-layout file is given a `VP8X` chunk, which stays when the profile goes. A
  file that holds exactly the profile is copied. The output is planned (`PlannedOutput`), scanned and
  compared before it is written. Run 43135792289: 2,283 tests; `exiv2 -pC`, `-pc`, `-pa` agree on 36
  edited fixtures and 25 files written from the corpus, and `exiv2 -pS` lists the expected chunks
  for 19 and 23 PNG files (the tool reads a PNG comment from `Description`, so the `Comment` chunk
  is checked by type, length and CRC-32). Nine capability rows.
- Write, ICC profile of TIFF-based and BMFF files: `SaveIccProfile` of `TiffBasedMetadataDocument`
  (tag 0x8773 of the first directory: over the stored value, else appended; removal leaves the bytes
  unaddressed) and `BmffMetadataDocument` (`colr` `prof` of the primary item: replaced in place,
  added last, removed as `free`; `iprp` in place, else `meta` grows or is appended; no item moves).
  Same profile: copy. Verified before written. A DNG, a raw file (ORF / RW2 header, CR2 signature,
  raw sensor data) and a first directory that is a reduced image are refused (`UnsupportedFeature`).
  Run 43146310534: 2,408 tests; `exiv2 -pC`, `-pc`, `-pa` agree on 56 + 117 edited fixtures and on
  10 + 15 files written from the corpus (15 of 22 TIFF-based, 2 of 15 BMFF refused). Twelve partial rows.
- One entry point for reading: `ImageMetadata.Read` (span, stream, async, path) tells the format by
  signature (`ImageMetadata.Detect`, `MetadataFormat`) and gives EXIF with MakerNote, IPTC, XMP, ICC
  profile, comments and previews; a kind its reader refuses is listed in `Errors`, the others are
  read. The readers per container are unchanged. The Dump tool reads eight formats through it: run
  43152164430, 2,515 tests, all ten parity reports equal to those of the old tool (run 43151606423).
- Release readiness, no behaviour change: every public member of both libraries has an XML comment
  and CS1591 is no longer suppressed (the documentation file is written in every build). Both are
  marked `IsAotCompatible` and built under the trim, AOT and single-file analyzers (no native publish
  is run). Packages carry description, tags, the repository (`MetadataSharpRepositoryUrl`) and commit;
  symbols are a `.snupkg` with Source Link, checked by `tools/audit/run.py`. README "Quick start":
  the regions of `tests/MetadataSharp.Examples/QuickStart.cs`, run by 12 tests that hold the README to them.

## Next (in order)

1. Publish 1.0 to NuGet: a version and a release workflow (packages are ready; nothing is published).
2. Write side: MakerNote-safe rules per vendor; removal of a whole BMFF item.
3. Interpreted values: logarithmic MakerNote codes, model-dependent fields, the other vendors
   (1,906 corpus texts), XMP.
4. Rewrite the Python parity harness and fixture generators in C# (one toolchain).

## Known limits

- Writes are restricted and refuse when safety is not established; MakerNotes are never moved.
- Interpreted values cover standard EXIF, IPTC and part of Canon/Nikon/Sony MakerNotes; not XMP.
- No ICC write for PSD, JPEG 2000 and EXV; comments are written in JPEG and PNG only; a PNG profile
  over the retained budget (1 MiB by default) is refused; `gAMA` / `cHRM` / `cICP` are not made to
  agree with a written profile. A BMFF item is edited or added, never
  removed. Previews are not decoded and not written. CR3 and JPEG XL are read, never written.
- EXIF blocks of PNG, WebP and PSD are still held to 65,527 bytes (`ExifTooLarge`), a JPEG rule.
