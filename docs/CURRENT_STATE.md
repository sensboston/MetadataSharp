# Current state

Updated 2026-11-12. Rules: `METADATASHARP_PROMPT.md`. History: `docs/archive/` (read-only).

## Done (read side unless noted)

- Format coverage and per-family read/write limits change as source-backed capability rows are
  added. The maintained matrix is the [release-readiness audit](RELEASE_READINESS_AUDIT.md); the
  executable operation contract is `MetadataCapabilities` and the format readers/writers.
- JPEG, PNG, WebP, TIFF-based files (including DNG and supported camera raw structures), CRW, MRW,
  RAF, CR3, BMFF still images, PSD, JP2, JXL, EXV, EPS, PGF, XMP sidecars and selected video
  containers have read paths, with different family coverage. GIF, BMP and TGA recognition is
  limited to dimensions. Editing is deliberately narrower and varies by container and operation;
  a missing or partial capability row is not an assertion of full Exiv2 parity.
- JPEG: all frame types; EXIF/IPTC/XMP read and restricted editing; stray bytes, bytes after EOI and
  IPTC split across several APP13 segments are read (edits copy unknown bytes verbatim).
- PNG, WebP: format-specific metadata reads and restricted editing. Standalone TIFF and TIFF-based
  raw files: layout accounting and restricted editing. XMP packets and sidecars use a bounded
  profile. PSD, JP2, EPS and other readers expose only the families listed in the audit.
- EXIF vocabulary: `ExifTags` (names, types, counts, value meanings from CIPA DC-008 / TIFF 6.0;
  names only, marked by `ExifTagDefinition.Source`, from TIFF/EP, DNG 1.7, DCF, Microsoft, PIM).
- MakerNotes (`ExifMetadata.MakerNote`; read, and edited tag by tag in place, see below): Canon (incl. arrays and AFInfo), Nikon 1/2/3
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
  a digest without a stream. PNG: EXIF in a "Raw profile type" text chunk, eXIf after IDAT
  (IPTC in its raw profile chunk is editable, see below).
- Previews: `Previews.List` / `Previews.Extract` in `MetadataSharp.Formats` report bounded file offsets
  and support JPEG/EXV/TIFF/PNG/WebP/PSD/BMFF plus CRW, MRW, RAF, JP2, EPS and XMP thumbnail loaders.
  Encoded BMFF HEVC/AV1 bytes can be copied without decoding; EPS/XMP thumbnail filters return the
  generated image bytes. Existing EXIF thumbnails and PSD's JPEG resource are writable only through
  their format-specific writers; there is no general preview replacement API. See
  [`PREVIEWS_USAGE_AND_LIMITS.md`](usage/PREVIEWS_USAGE_AND_LIMITS.md) and `PreviewTests`.
  `MetadataSharp.Dump --previews` is checked against pinned `exiv2 -pp` output and group-C fixtures.
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
  one pointer changed; no byte moves, old bytes stay. Image data is never written to, and the
  MakerNote only where a tag of it is named by key (inside the note, same length); an append is refused when image data runs to the end of the file. The output is read back and
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
- ICC profiles: `IccProfiles.Read` returns the profile bytes of JPEG and EXV (APP2 chunks), PNG
  (iCCP), TIFF (tag 0x8773, first directory), WebP (ICCP), HEIC/HEIF/AVIF (`colr` of the primary
  item), JPEG 2000 (`colr` of `jp2h`) and PSD (resource 0x040F); header checked (size, `acsp`), nothing
  interpreted; new code `InvalidIccProfile`. ICC is written by the JPEG, PNG, WebP, safe TIFF/BMFF
  writers, `ExvFile.Create`, `PsdMetadata.Write` and `Jp2Metadata.Write`; `MetadataSharp.Dump --icc`.
  The parity job requires the 17 profiles the pinned tests give for `exiv2 -pC` (`tools/parity/run_icc.py`).
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
- Canon CRW, Minolta MRW, Fujifilm RAF (the formats of the Exiv2 0.28.9 registry that are no TIFF
  file): `CrwMetadata.Read` gives the records of the CIFF heap the reference maps to tags (the Canon
  ones as a Canon MakerNote, `DateTimeOriginal` in UTC), the comment, and where preview and
  thumbnail lie; `MrwMetadata.Read` the TIFF structure of the TTW block; `RafMetadata.Read` the
  metadata of the embedded JPEG file and the IFD0 tags only the TIFF structure has. MRW and RAF are
  read-only, as in the reference. `CrwMetadataDocument` (`SaveExif`, `SaveComment`, synchronous)
  writes what the reference writes: make and model, orientation, pixel dimensions, date, color
  space, subject distance, thumbnail, the Canon tags by key, the comment. The heap is rewritten in
  the reference's layout, records not named keep bytes and order, the output is read back
  (`UnsafeRelocation`); other tags: `TagNotEditable`. Measured (run 43327984837): stored
  expectations of the six corpus files, 116 of 116 keys found, 114 equal (the two differ by the
  time zone the expectation was made in); pinned tool run on 13 CRW files and 24 MRW / RAF files
  authored from corpus TIFF / JPEG files (`tools/reference/raw_container_fixtures.py`): 1,504 of
  1,509 keys found, 1,498 equal; 39 CRW edits read back by the tool as set
  (`tools/reference/crw_parity.py`), 6 refused here.
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
  Same profile: copy. Verified before written. A DNG or raw layout with no valid existing primary
  tag 0x8773, and a first directory that is a reduced image without such a target, are refused
  (`UnsupportedFeature`). A readable existing primary profile permits only replace/remove, preserving
  image and MakerNote bytes; insertion remains refused.
- Write, complete-set PSD and JP2 metadata: `PsdMetadata.Write` replaces EXIF/IPTC/XMP/ICC resources,
  and adds or removes a JPEG thumbnail resource; unrelated resources and image bytes are preserved.
  `Jp2Metadata.Write` replaces EXIF/IPTC/XMP UUID boxes and ICC `colr`; unknown boxes and the
  codestream are preserved. These complete-set byte-buffer APIs are tested in `PsdMetadataTests` and
  `Jp2MetadataTests`; they are not in-place/stream document APIs. `ExvFile.Create` and selected
  `MetadataOperations.ExtractExv` output ICC as numbered APP2 segments.
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
- Typed reading (nothing throws; absent or unreadable is `false` / null): `ExifValue.TryGet…` for every
  field type (arrays, single values, strings); `ExifMetadata` `Make`, `Model`, `LensModel`,
  `DateTimeOriginal`, `DateTaken` (0x9003, else 0x9004, else 0x0132; both `…WithOffset`, the
  offset tag of the same date only), `Orientation`, `ExposureTime`, `FNumber`, `IsoSpeed`,
  `FocalLength`, `PixelWidth` / `PixelHeight`, `GpsPosition` (each coordinate the nearest double to
  its rationals); `IptcPacket` `ObjectName`, `Keywords`, `Bylines`, `Headline`, `Copyright`, `Caption`;
  `XmpPacket` `Title`, `Description`, `Creators`, `Rights`, `Subjects`, `Rating`. Run 43164879862:
  2,706 tests. Against the stored rows of the 330 corpus files (one-off local run, not a CI job):
  2,641 values equal, none different.
- README: the quick start reads through these properties (16 tests run the snippets and hold the
  README to them); its links are absolute under `MetadataSharpRepositoryUrl`, checked by a test.
- Any EXIF tag is written (`ExifEdit.Set` / `Remove`, typed overloads): every tag of IFD0, EXIF, GPS,
  Interop and an existing thumbnail directory, known to `ExifTags` or not; `Set` holds a listed tag
  to its definition, `SetUnchecked` writes any field type as given.
  Pointers, image-data addresses and the MakerNote are not values to set (`TagNotEditable`,
  `ExifEdit.IsEditable`). JPEG, PNG and WebP now edit a block where it lies, as the TIFF-based and
  BMFF writers do: in place when a value fits, else appended, the MakerNote at its offset; a
  directory appears with its first tag and goes with its last; a thumbnail directory is not created.
  Run 43182485287 / 43182493868: 2,817 tests; `exiv2 -u -pa` reads 54 authored edits and 387 files
  written from the corpus's 132 JPEG / PNG / WebP files (114 of them gained their block); every
  line the edit did not name is printed as before. The ExifTool harness (scheduled job) was not run.
- Any XMP property is written (`XmpEdit` by path, the keys `XmpPacket.Entries` lists, `XmpEntry.Key`):
  simple values, Bag/Seq/Alt items, language alternatives, structures, arrays of structures and
  qualifiers in any namespace (`RegisterNamespace`); what an edit empties goes with its empty parents.
  A packet is editable when it can be rewritten without loss, else refused (`UnsupportedFeature`); a
  path that does not apply is `InvalidXmpPath`. One canonical RDF form, read back before it is kept.
  JPEG reads and writes Extended XMP (over 65,504 bytes); TIFF and BMFF pad a packet for in-place edits.
  CI job: `exiv2 -pa` prints every written property and no other in JPEG, PNG, WebP, TIFF, HEIC and a
  sidecar, `-pX` gives the packet; the tool reads only the standard packet of Extended XMP (PR #107).
- Any IPTC dataset is written (`IptcEdit`, ordered operations; `IptcDatasets` is the table of records
  1 to 9, written from IIM 4.2 as remembered): text, numbers, dates, times, repeated values by index,
  raw bytes for anything else. Kept occurrences keep their bytes. `ESC ( B` reads as no declaration;
  the first non-ASCII text makes a packet UTF-8, or is refused when undeclared 8-bit text stays.
  JPEG (APP13), TIFF-based files (0x83BB, or the 0x0404 resource inside 0x8649 when IPTC lives
  there) and PNG (`Raw profile type iptc`, kept in its form; new: zTXt before IDAT). The digest
  0x0425 is left stale unless `IptcWriteOptions.UpdateDigest`; never added. CI job: `exiv2 -u -pi`
  and `-pa` print every written dataset, as often as written, and no other (PR #108).
- Sync (PR #109): `MetadataSync` in Core converts EXIF, IPTC and XMP into each other
  (`ExifToXmp`, `XmpToExif`, `IptcToXmp`, `XmpToIptc`; modes overwrite / fill missing, optional
  erasing of the source) and `Reconcile` brings the shared fields into agreement by the MWG
  precedence, with the IPTC digest deciding between IPTC and XMP. Results are `SyncEdits`
  (edits, notes, choices); `SaveAll` / `SaveAllAs` of the JPEG, PNG, WebP, BMFF and TIFF-based
  documents write them in one pass and refresh an existing digest. The MWG rules were taken from
  how ExifTool implements them (`MWG.pm`), not from the MWG document, which was not at hand.
  CI step in `differential-parity` (`tools/reference/sync_parity.py`, run 43270057746) against the
  conversion of exiv2 0.28.9 on the corpus's JPEGs: EXIF to XMP, 71 files, 2,474 values the same,
  4 the same in another form, 44 documented differences (29 empty texts the tool writes and we
  do not); XMP to EXIF, 77 files, 2,391 the same, 8 in another form, 69 documented differences
  (SubSec digits 39, comment character code 14). An undocumented difference fails the job.
  14 files this library refuses to read are left out of the first direction.
- Operations of the exiv2 command line (PR #110): `MetadataSharp.Core.Operations`
  (`MetadataTimestamps.Adjust`, `MetadataFileNames.Format`, `ModifyScript`, `MetadataKeys`) and
  `MetadataSharp.Formats.Operations` (`MetadataOperations.Delete`, `ExtractExv`,
  `ExtractXmpSidecar`, `ExtractThumbnail`, `InsertExv`, `InsertXmpSidecar`, `FixIso`,
  `FixUserComment`; `ExvFile.Create`). `ExifEdit.SetThumbnail` / `RemoveThumbnail` create, replace
  and remove the thumbnail directory (IFD1) in JPEG, PNG and WebP. `XmpEdit.ReplaceWith` puts a
  whole packet in place of the file's. CI job "operations parity"
  (`tools/reference/operations_parity.py`, run 43290037609) against exiv2 0.28.9 on 152 files of
  the corpus, eleven operations, 53 to 136 files compared each, 49,028 keys the same; every
  difference is documented in the script and an undocumented one fails the job. Table and
  differences: `docs/usage/OPERATIONS_USAGE_AND_LIMITS.md`.

- MakerNote tags are edited in place (`ExifEdit.SetByKey` / `SetTextByKey` / `RemoveByKey`
  with the keys the dump prints, `Exif.Canon.OwnerName`, `Exif.CanonCs.Quality`; the same keys in
  `ModifyScript`; `MakerNote.GetKey` gives the key of an entry): JPEG, PNG, WebP and TIFF-based
  files (CR2, NEF, ARW, PEF, ORF, DNG; there every changed byte lies inside the note and the file
  keeps its length). Every layout the reader decodes; tags, fields of arrays (Nikon re-encrypted
  with the file's key, Sony re-enciphered), tags of sub-directories (Olympus), removal by
  compacting the directory. Nothing moves. A value that grows is appended to the EXIF block of a
  JPEG, PNG or WebP file for Canon, Nikon1, Nikon2 and Sigma notes (`MakerNoteKeys.Grows`: the
  layouts the parity job confirmed); elsewhere growth, a tag that is not there and BMFF files are
  refused (`UnsupportedFeature`). Addresses and the Nikon cipher keys are `TagNotEditable`, and so
  is a tag without a name of type LONG, SLONG or UNDEFINED unless `allowUnnamed` is passed. The
  result is re-read and every unnamed tag compared byte for byte (`MakerNoteWriter`), and the
  layout around the note is compared (`TiffBasedWriteVerifier.MakerNote`). Parity job
  "MakerNote edit parity" (run 43316787075): `exiv2 -u -PEkv` reads 178 edits of corpus JPEGs back
  as set with every other key unchanged (text of the same length 85, grown text 42, SHORT 51).
  Not proven by the tool: TIFF-based files (the corpus has no whole raw file with a decodable
  note; synthetic tests only) and growth in notes that count offsets from themselves (code
  present, switched off).

## Next (in order)

1. Publish 1.0 to NuGet: a version and a release workflow (packages are ready; nothing is published).
2. Write side: the thumbnail in TIFF-based and BMFF files; MakerNote-safe rules per vendor (so an
   insert can carry the MakerNote); removal of a whole BMFF item.
3. Interpreted values: logarithmic MakerNote codes, model-dependent fields, the other vendors
   (1,906 corpus texts), XMP.
4. Rewrite the Python parity harness and fixture generators in C# (one toolchain).

## Known limits

- Writes refuse when safety is not established; MakerNotes are never moved; their tags are edited in place, and a grown value is appended for four layouts only. EXIF takes any
  tag; the thumbnail (IFD1) is edited in JPEG, PNG and WebP only. The PSD complete-set writer omits EXIF MakerNote/image-address/thumbnail-directory tags and drops IPTC digest resource 0x0425; sidecars have no IPTC write. XMP and
  IPTC agree only when asked (`MetadataSync`); an independent `SaveIptc` leaves the digest stale.
- Sync: `SaveAll` has no async twin; PSD and sidecars have no `SaveAll`. Not in the mapping table:
  CFAPattern, OECF, SpatialFrequencyResponse, DeviceSettingDescription. A value of a type or count
  the tag is not defined with is not converted and is listed in `SyncEdits.Notes`.
- Interpreted values cover standard EXIF, IPTC and part of Canon/Nikon/Sony MakerNotes; not XMP.
- PSD and JP2 writes are complete-set byte-buffer APIs, not the general per-family stream/path document APIs; JP2 XML boxes not parsed as XMP remain opaque. EXIF blocks written for PSD keep the 65,527-byte ceiling. ICC insertion for TIFF-based RAW remains refused unless a layout proves that tag 0x8773 targets the image rather than a thumbnail or raw-sensor IFD. The bounded DNG case is an un-reduced, uncompressed RGB primary with one accounted strip and a separate CFA SubIFD; other DNG and camera-RAW layouts without a validated existing primary ICC target remain refused (valid existing targets permit replacement/removal). Comments are written in JPEG, PNG and CRW only; a PNG profile over the retained budget (1 MiB by default) is refused; `gAMA` / `cHRM` / `cICP` are not made to agree with a written profile. A BMFF item is edited or added, never removed. Previews are not pixel-decoded; no generic preview replacement API is provided (existing EXIF thumbnail and PSD resource writes are format-specific). CR3, JPEG XL, MRW and RAF are read, never written. CRW: no async members; a record outside the directory the reference writes it to is not changed; the Fujifilm directory (0xF000) of a RAF TIFF structure is not read.
- EXIF blocks of PNG, WebP and PSD are still held to 65,527 bytes (`ExifTooLarge`), a JPEG rule.
