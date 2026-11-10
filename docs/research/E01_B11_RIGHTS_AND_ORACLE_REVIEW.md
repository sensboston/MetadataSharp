# E01 B11 — small-format and container-recognition rights and oracle review

Status: authorized, reviewed, and closed for this batch.
Baseline reviewed: Exiv2 `b0859fc61fea5dedfc898aece6b15893ddf4314e` (`v0.28.9`), build configuration
`tools/reference/exiv2_baseline.OPTIONS` (`EXIV2_ENABLE_VIDEO=ON`, `EXIV2_ENABLE_BMFF=ON`,
`EXTERNAL_XMP=OFF`, `EXV_HAVE_XMP_TOOLKIT` therefore unset).
Reviewed on 2026-11-10. This document authorizes the fixtures, the observation set and the census
enumeration for batch **B11**; it authorizes nothing for any later batch.

## What B11 is, in one paragraph

B11 closes the **last 24 pending seed cells** — `EX0289-{BMP,CRW,GIF,TGA}-{EXIF,IPTC,XMP,COMMENTS,ICC,THUMBNAIL}`
— and it adds the first census rows for the container families that the pinned documentation never puts in
its support table at all: QuickTime, ASF, RIFF and Matroska. The seed-grid half is observation against a
documented claim; the container half has no claim to observe against, so it is recorded as an inventory /
recognition enumeration with the reference's own position written into the row. After B11
`coverage.pendingSeedIds` is empty. That is the whole batch: no format gets a `verified` cell, no
denominator is computed, and no MetadataSharp behaviour is asserted anywhere in it.

## Headline findings from the source reading

Five things came out of reading the pin that were not known going in, and each one changes how a B11 row is
written. They are stated here as source facts with citations; nothing here is an assertion about
MetadataSharp, and nothing here is an assertion about what the pinned binary *prints* — that is what the CI
run is for.

1. **The registry advertises Exif for the four video readers and no video reader writes any Exif.**
   `image.cpp:109-112` gives `amRead` for Exif while each constructor declares `mdNone` supported metadata
   and each reader assigns only into its XMP container. Three sources, three positions; B11 records the
   disagreement instead of picking a side.
2. **TGA recognition is name-based, and the byte-based fallback is unreachable from the normal entry
   points** (`tgaimage.cpp:99-123` gates the footer comparison behind `const size_t curPos = iIo.tell();
   if (curPos < 26) return false;`, and `tell()` is 0 on a freshly opened `FileIo` — `basicio.cpp:504-512`
   does not seek after `fopen`). This is the one place where "the image format is recognized" (`exiv2.md:123-125`) needs a
   third fixture to be recorded honestly.
3. **CRW dimensions are metadata, not pixels** (`crwimage.cpp:29-43`): a readable CRW with no `0x1810`
   component reports 0×0.
4. **CRW has two preview surfaces in two different groups, and only one of them holds bytes.**
   `decode0x2008` routes the `0x2008 @ 0x0000` component through `ExifThumb::setJpegThumbnail`, which stores
   the **actual JPEG data in the data area** of `Exif.Thumbnail.JPEGInterchangeFormat`
   (`exif.cpp:431-437`) — `ifd1Id`, group name `Thumbnail` (`tags_int.cpp:46`). `CrwParser::decode` then
   *also* injects `Exif.Image2.JPEGInterchangeFormat{,Length}` for a `0x2007 @ 0x0000` component
   (`crwimage.cpp:108-113`), and `Image2` is a different IFD (`ifd2Id`, `tags_int.cpp:47`), so the two do
   not overwrite each other: the `Thumbnail` pair carries the payload that `-et` writes out, while the
   `Image2` pair carries a bare file offset that is meaningless outside the file. B11 ships fixtures with
   and without `0x2007` and reports what each produces; the asymmetry is the row content.
5. **`-pR` is compiled out of Release builds** (`app/exiv2.cpp:654-663`) and **`-pC` is the ICC-profile
   print, not a recursive dump** (`app/exiv2.cpp:613-675`). Any B11 observation that wanted a recursive
   structure dump cannot have one, and `-pC` output must be read as "profile bytes, if the reader writes
   any".

## What was reviewed, and it is deliberately narrow

Reviewed, at the pinned commit, by reading the source:

- `src/image.cpp:76-117` — the type registry, in table order, with the per-family `AccessMode` for
  Exif / IPTC / XMP / comment, and `src/image.cpp:839-849` (`ImageFactory::open(BasicIo::UniquePtr)`),
  which is what actually decides the handler: the io is opened, the registry is walked in order at position
  0, `isThisType_(*io, false)` **first match wins**, and no match yields `kerFileContainsUnknownImageType`
  (`image.cpp:825-830`).
- `src/gifimage.cpp` (90 lines), `src/tgaimage.cpp` (123 lines), `src/bmpimage.cpp` (133 lines) in full.
- `src/crwimage.cpp` (157 lines) and `src/crwimage_int.cpp` (1030 lines), plus `include/exiv2/image.hpp:38`
  (`PrintStructureOption`) and `include/exiv2/crwimage_int.hpp` (`DataLocId`, `CiffHeader::signature_`).
- `src/quicktimevideo.cpp`, `src/riffvideo.cpp`, `src/matroskavideo.cpp`, `src/asfvideo.cpp`,
  `src/bmffimage.cpp` — constructors, `mimeType()`, `readMetadata()` entry guards, `writeMetadata()`,
  `printStructure()` presence/absence, the five `is*Type()` recognisers, and the count of assignments
  into `exifData_` / `xmpData_` / `iptcData_` / `comment_` / `iccProfile_`.
- `exiv2.md` §3 "FILE TYPES" (lines 66-128), including the trailing bullets.
- `src/tags_int.cpp:44-83` — the IFD group table, which is what says `Thumbnail` is `ifd1Id`, `Image2` is
  `ifd2Id` and `canonId` prints as `Canon`; and `src/canonmn_int.cpp:455-664` for the Canon tag names (three
  of the CIFF-mapped tags, `0x00b5`, `0x00c0`, `0x00c1`, have **no** name and print as `Exif.Canon.0x00b5`
  etc.). `src/exif.cpp:700-735` holds `TiffThumbnail::copy` and `JpegThumbnail::copy`, the two consumers of
  the keys CRW writes.
- `src/exif.cpp:371-441` (`ExifThumbC::copy/writeFile`, `ExifThumb::setJpegThumbnail`) and
  `src/exif.cpp:595-660` (`ExifParser::encode` tag-size triage), plus `Thumbnail::create`
  (`src/exif.cpp:680-694`).
- `app/exiv2.cpp:613-675` (`Params::evalPrint`) and `app/actions.cpp:205-224, 700-715, 836-858,
  1983-1991` for how a `-p…` / `-e…` flag reaches a reader.
- The seed and facts corpora as committed: `docs/research/EXIV2_DOCUMENTED_FORMAT_FACTS.json`
  (31 formats × 6 families, doc blob `149f53b809c182bb4145567e28c466965f7f6505`),
  `docs/research/EXIV2_OPERATION_SEEDS.json` (186 seeds, `denominatorFrozen=false`),
  `docs/research/EXIV2_CENSUS.json` (728 rows, `coverage.enumerated` = 188 format/family cells over 27
  formats + DCP, `coverage.pendingSeedIds` = the 24 cells above, `coverage.inventoryTrack` = 24 formats).

Not reviewed, and out of scope: Exiv2's CI, its unit tests, `test/data/**` (third-party bytes — see the
rights section), any MakerNote dictionary, any XMP packet writer, and every MetadataSharp source file.
Nothing below is a claim about MetadataSharp. Reference facts are facts about what the pinned reference
does; they become parity claims only through a separate verification batch, and B11 rows are written as
`verification: not-assessed` for exactly that reason.

## Rights review — what B11 may put in the repository

| Path | Origin | Decision |
| --- | --- | --- |
| `owned-crw-mapped.crw`, `owned-crw-thumb.crw`, `owned-crw-preview-thumb.crw`, `owned-crw-unmapped.crw` | authored byte-for-byte by `tools/reference/b11_container_fixtures.py` from the CIFF contract read above | **owned-synthetic**; admitted |
| `owned-gif-lsd.gif`, `owned-gif-no-lsd.gif` | same writer, from the GIF signature + logical-screen-descriptor contract | **owned-synthetic**; admitted |
| `owned-tga-full.tga`, `owned-tga-name-only.tga`, `owned-tga-signature-only.targa` | same writer, from the TGA 18-byte header + 26-byte footer contract | **owned-synthetic**; admitted |
| `owned-bmp-dib.bmp`, `owned-bmp-os2.bmp` | same writer, from the BMP `BITMAPINFOHEADER` / `BITMAPCOREHEADER` contract | **owned-synthetic**; admitted |
| `owned-mov-brand.mov`, `owned-mov-unknownbrand.mov`, `owned-mkv-header.mkv`, `owned-avi-avih.avi`, `owned-wav-fmt.wav`, `owned-asf-header.asf` | same writer, header-only stubs built from the five `is*Type()` byte tests | **owned-synthetic**; admitted |
| `tests/fixtures/b11/census-manifest.json` | generated by the writer | admitted; compared whole by `verify()` |
| Exiv2 `test/data/*.{gif,bmp,tga,crw,mov,mp4,mkv,avi,wav,asf}` | upstream GPL-2.0-or-later test corpus, includes camera- and tool-produced files | **not admitted** |
| Any camera-produced CRW/BMP/GIF, any vendor sample | unknown rights, unknown provenance, embedded private data (GPS, serials, names) | **not admitted** |
| Any generated "realistic" media (a rendered photo, an encoded H.264 stream) | would need a licence decision of its own and adds no oracle value | **not admitted** |

Consequences that follow from that table, and are not negotiable in code review:

1. Every B11 fixture is a **recognition skeleton**, not a picture. A GIF fixture with no image descriptor,
   colour map or image blocks is the correct fixture for this batch, because the only documented claim
   about GIF is "the image format is recognized, a MIME type is assigned to it and the height and width of
   the image are determined" (`exiv2.md:123-125`). Rendering pixels would add rights exposure and zero
   adjudication power.
2. Because fixtures are synthetic, their bytes are known **before** any reference runs. The recorder may
   therefore carry structural expectations (digest identities, byte equality, "these N rows share one
   value") — that is what makes a green run meaningful — but never expectations about *how the reference
   behaves*. The behaviour column of every B11 row is filled from observation or not at all.
3. `.gitattributes` gets the B11 fixture pins **in the same commit that adds the fixtures**, as in B10.
   The `-text`/`-diff` pins keep CRLF conversion away from the fixture bytes; the JSON evidence pins keep
   recorded digests stable across platforms. The guard in
   `tests/discovery/test_census.py::test_evidence_files_hashed_by_a_receipt_survive_checkout_conversion`
   reads `.gitattributes` rule text and compares working bytes to `git show HEAD:<path>` bytes; it does not
   parse `git check-attr`, and it must not be "simplified" back to that.
4. `EXIV2_CENSUS.json` is deliberately **not** EOL-pinned, so no test may hash its bytes; nothing in B11
   may change that without changing every recorded digest consistently.

## Rights review — the video half needs no clearance, and that has to be said out loud

No video fixture is *needed* for metadata clearance because B11 authors no video metadata carrier: the six
video stubs are header + minimal atoms/chunks that satisfy a recogniser and nothing else. So there is no
"proprietary layout" question to answer for MOV/ASF/AVI/WAV/MKV in this batch — the reason those formats
get no metadata cells is structural, and it is recorded in `docs/research/` rather than being left as an
inferred absence.

## What the pinned documentation actually says, and where it stops

`exiv2.md` §3 is a table of **31** type rows over six metadata columns
(Exif, IPTC, XMP, Image Comments, ICC Profile, Thumbnail). The four B11 formats are rows in it:

```
BMP  | - | - | - | - | - | -
GIF  | - | - | - | - | - | -
CRW  | Read/Write | - | - | Read/Write | - | Read/Write
TGA  | - | - | - | - | - | -
```

and the following bullets qualify them:

> Support for GIF, TGA and BMP images is minimal: the image format is recognized, a MIME type assigned to
> it and the height and width of the image are determined. (`exiv2.md:123-125`)

> Support of video files is limited. Currently **exiv2** only has some rudimentary support to read metadata
> from quicktime, matroska and riff based video files (e.g. .MOV/.MP4, .MKV, .AVI, .WAV, .ASF).
> (`exiv2.md:120-122`)

Three readings of that text matter for B11, and they are the reason the batch is shaped the way it is:

- **There is no video row in the table.** `MP4`, `MOV`, `AVI`, `WAV`, `MKV`, `WEBM`, `ASX` and `WMV` are
  not rows of §3 at all — they appear only in the prose bullet — so they are absent from
  `EXIV2_DOCUMENTED_FORMAT_FACTS.json`'s 31 formats, and `build_capability_seeds.py`, which expands exactly
  those facts, cannot emit a seed for them. `EXIV2_OPERATION_SEEDS.json` therefore
  contains **no video cells at all**, and video never appears in `pendingSeedIds`. That is not an
  oversight to correct in B11 and it is not evidence that video is unsupported: the seed corpus scope says
  so in words — "Extraction/recognition/video/workflows/tag/encoding/layout cases still need separate
  census." B11 must not fabricate a `documentedClaim` for a video cell, and must not mark a video cell
  `noDocumentedClaim` either, because `noDocumentedClaim` means "a table row exists whose column is `-`".
  Video rows carry no claim at all; the row text says which readers exist at the pin and that the table
  has no row.
- **The bullet's own grouping is imprecise, and B11 should not repeat it.** `.WAV` and `.ASF` are named as
  "riff based video files", but ASF is not RIFF-framed: `AsfVideo` is a separate reader with a separate
  registry entry and its own recogniser that compares the 16-byte ASF Header Object GUID
  (`asfvideo.cpp:95` `Header(0x75B22630, 0x668E, 0x11CF, {0xA6,0xD9,0x00,0xAA,0x00,0x62,0xCE,0x6C})`,
  used by `isAsfType` at `asfvideo.cpp:503` and the `static bool isASFType(const byte[])` helper at `:201`).
  Meanwhile `.WAV` genuinely is handled by the RIFF reader, because `isRiffType` (`riffvideo.cpp:771`)
  compares **only** the four bytes `R` `I` `F` `F` — no fourcc, no list check. So "WAV is RIFF" is a
  statement about dispatch, not about metadata semantics, and the census says it that way.
- **The GIF/TGA/BMP bullet is a *positive* claim sitting under six `-` cells.** The table says these
  formats have no Exif, no IPTC, no XMP, no comments, no ICC and no thumbnail; the bullet says recognition,
  MIME type and width/height work. The six `-` cells are what B11 must enumerate as
  `noDocumentedClaim: true` (the B2-PNG and B3-WebP precedent), and the bullet's three promises are
  exactly what the `inventory` row for each format records — because those promises are *outside* the six
  families the seed grid models. This is the single most important structural point in the batch: the
  observable surface of BMP/GIF/TGA is not metadata at all, it is `mimeType()` + `pixelWidth()/Height()`,
  and a census that only counted metadata families would record three formats as "nothing to test" when the
  reference is in fact claiming three behaviours.

## Reader facts, per format, at the pin

### CRW — Canon CIFF

- `crwimage.cpp:22` constructs `Image(ImageType::crw, mdExif | mdComment, io)`. No IPTC, no XMP, no ICC in
  the supported set; `crwimage.cpp:45` `setIptcData` throws `kerInvalidSettingForImage("IPTC metadata",
  "CRW")`. There is no `setExifData`/`setXmpData`/`setComment` override beyond that, and
  `mimeType()` returns `image/x-canon-crw` (`:25`).
- Registry (`image.cpp:82`) says `amReadWrite, amNone, amNone, amReadWrite` — Exif and comment read/write,
  IPTC and XMP nothing. This agrees with the §3 row for exif/comments and with it for
  iptc/xmp/icc as well. **There is no doc-vs-code conflict for CRW at this pin**, which is worth recording
  explicitly: the CRW cells stay pending not because the reference is contradictory but because no batch
  has observed them.
- Read path: `crwimage.cpp:50-70` — `isCrwType(*io_, false)` or `kerFailedToReadImageData` /
  `kerNotACrwImage`; then `CrwParser::decode(this, io_->mmap(), io_->size())`. Note `io_->mmap()`: CIFF is
  parsed from a memory map of the whole file, so every CIFF offset guard is a guard against a *file-length*
  assumption, not a stream position.
- `isCrwType` (`crwimage.cpp:139-157`) reads **14 bytes** and requires `tmpBuf[0]`/`tmpBuf[1]` to be
  `II` or `MM` (byte-order mark) and `memcmp(tmpBuf + 6, CiffHeader::signature(), 8) == 0`, where
  `CiffHeader::signature_` is `"HEAPCCDR"` (`crwimage_int.hpp:468`). Bytes 2-5 are the root-directory
  offset, checked in `CiffHeader::read` (`crwimage_int.cpp:155`): `size < 14` → `kerNotACrwImage`;
  `offset_ = getULong(pData+2, bo)` must satisfy `offset_ >= 14 && offset_ <= size`; the root directory is
  read at `pData + offset_`; bytes `14 .. offset_` are skipped as padding.
  A synthetic CRW is therefore: `II` + 4-byte offset + `"HEAPCCDR"` + padding + directory — and the
  offset field is the **only** place a fixture has to agree with itself.
- CIFF entries are 10 bytes (`crwimage_int.cpp:184` `CiffComponent::doRead`, guarded
  `enforce(size >= 10 && start <= size - 10)`): `tag_ = getUShort(pData+start)`, then
  `valueData` entries carry `size_ = getULong(+2)` and `offset_ = getULong(+6)` with overlap checks that
  throw `kerOffsetOutOfRange`, while `directoryData` entries carry `size_ = 8` and `offset_ = start + 2`.
  Which of the two a tag is comes from `CiffComponent::dataLocation(tag)` (`crwimage_int.cpp:469`), which
  switches on `tag & 0xc000`: `0x0000` → `valueData`, `0x4000` → `directoryData`, anything else →
  `kerCorruptedMetadata`. **Directory-ness and data location are two different bits**: `0x300a`, `0x2807`,
  `0x3004`, `0x3002` and `0x300b` are directories because of their `0x3800` type bits (`CiffComponent::typeId`,
  `crwimage_int.cpp:450` — the switch `readDirectory` uses at `:255` to decide `CiffDirectory` versus
  `CiffEntry`), and they are `valueData` because their `0xc000` bits are zero. That is why a real CRW stores
  each child region in the parent's payload pool and points at it from the entry, exactly as
  `CiffDirectory::doWrite` does (`:340-379`), and it is the shape the B11 fixtures use. A tag that *does*
  carry `0x4000` gets `size_ = 8` and `offset_ = start + 2` — an inline eight-byte region — and
  `readDirectory` over eight bytes can only ever accept `count == 0`, so an inline directory is necessarily
  empty in this build. Both facts were settled by reading `typeId`, `dataLocation` and `doWrite` together;
  neither was guessable from the entry layout alone.
- Tree shape is fixed by `CrwMap::crwSubDir_` (`crwimage_int.cpp:128-132`): root → `0x300a` →
  {`0x2804`, `0x2807`, `0x3002`, `0x3003`, `0x300b`}, plus `0x3004` under `0x2807`; the comment in the
  source says the list is "arranged bottom-up so that … the (unique) path to root can be determined in a
  single loop". A fixture that puts a mapped tag in the wrong directory is not an invalid file, it is an
  **unmapped** one — see the next bullet.
- Exif output is table-driven, exactly like the Panasonic/TIFF case that made RW2 interesting in B10.
  `CrwMap::crwMapping_[]` (`crwimage_int.cpp:82-110`) is a list of `(crwTag, crwDir, size, exifTag, ifd,
  decodeFct, encodeFct)` rows — the column order the table's own header comment gives — and a CIFF component
  with no matching row is silently dropped while a matching one produces exactly one Exif datum.
  `crwMapping_[]` has **22 live rows and two commented-out ones** (`0x10b4`, kept and re-pointed, and
  `0x1818`, dropped entirely). Of the live rows, **17 target `IfdId::canonId`**, four target
  `IfdId::exifId` (`0x10b4`, `0x1807`, `0x180e`, `0x1810`) and one targets `IfdId::ifd1Id` (`0x2008`); the
  `canonId` group name is
  `Canon` (`tags_int.cpp:65`) — so most CIFF content surfaces as maker-note keys (`Exif.Canon.*`), not as
  standard Exif. Two of the `canonId` rows are special-cased and never use their own `ifd`/`exifTag` fields
  at all — `decode0x0805` writes the comment and `decode0x080a` writes `Exif.Image.Make`/`Model` — so the
  group a row names is not always the group that appears in the output. The rows B11's fixtures exercise are
  below. `decodeBasic` builds the key as
  `ExifKey(pCrwMapping->tag_, Internal::groupName(pCrwMapping->ifdId_))`, so a Canon-group tag with no
  `CanonMakerNote` entry prints under its hex fallback (`Exif.Canon.0x00b5`, `.0x00c0`, `.0x00c1` are
  exactly that — unnamed in `canonmn_int.cpp`) which is the same shape B10 recorded for RW2's `Tag::pana`
  rows. The rows B11's fixtures can exercise:

  | CIFF | maps to | decode function, and what it really does |
  | --- | --- | --- |
  | `0x0805 @ 0x300a` | — (no Exif datum) | `decode0x0805` → `image.setComment(s)`; **this is the comment surface** |
  | `0x080a @ 0x2807` | `Exif.Image.Make`, `Exif.Image.Model` | `decode0x080a` splits one ascii string at the first NUL into two keys: one component, two Exif tags |
  | `0x1810 @ 0x300a` | `Exif.Photo.PixelXDimension` + `PixelYDimension` | `decode0x1810` requires `typeId() == unsignedLong && size() >= 28`; bytes 0-4 and 4-8 become two `ULongValue`s |
  | `0x180e @ 0x300a` | `Exif.Photo.0x9003` = `DateTimeOriginal` | `decode0x180e` |
  | `0x1807 @ 0x3002` | `Exif.Photo.SubjectDistance` (0x9206, `print0x9206`) | `decodeBasic`; note the destination is **`SubjectDistance`**, not `OwnerName` as older CRW notes elsewhere claim |
  | `0x080b @ 0x3004` / `0x0810 @ 0x2807` / `0x0815 @ 0x2804` | `Exif.Canon.FirmwareVersion` / `Exif.Canon.OwnerName` / `Exif.Canon.ImageType` | `decodeBasic` into `canonId` |
  | `0x1817 @ 0x300a` (`size_ = 4` in the table) | `Exif.Canon.FileNumber` | the one row whose `size_` column is non-zero, and `decodeBasic` lets it override the entry's own size (`src/crwimage_int.cpp:808`); the fixture therefore claims 8 bytes and the reader takes 4, which is the size column being observable |
  | `0x10b4 @ 0x300b` | `Exif.Photo.ColorSpace` (0xa001) | moved out of `canonId` (0x00b4 there is also `ColorSpace`); the source comment reads `// Mapped to Exif.Photo.ColorSpace instead` |
  | `0x2008 @ 0x0000` | `Exif.Thumbnail.*` (`ifd1Id`) | `decode0x2008` → `ExifThumb exifThumb(image.exifData()); exifThumb.setJpegThumbnail(ciffComponent.pData(), ciffComponent.size())` |
  | `0x2007 @ 0x0000` | `Exif.Image2.JPEGInterchangeFormat{,Length}` (`ifd2Id`) | not a mapping row: `CrwParser::decode` (`crwimage.cpp:102-115`) injects them after decoding, `// a hack to get absolute offset of preview image inside CRW structure` |

- Two facts in that table drive fixture design:
  - **Dimensions are metadata, not pixels.** `CrwImage::pixelWidth()/Height()` (`crwimage.cpp:29-43`) read
    `Exif.Photo.PixelXDimension`/`PixelYDimension` and return **0** when absent. A CRW with no `0x1810`
    component reports 0×0 even though the container is perfectly readable. `pixelWidth()` is not a pixel
    fact for CRW; the inventory row must not describe it as one.
  - **The comment surface is a CIFF value, and there is no `printStructure` override anywhere in
    `crwimage.cpp`.** So `exiv2 -pS` / `-pC` on a CRW reach `Image::printStructure` (`image.cpp:136-138`),
    which throws `kerUnsupportedImageType` — CRW belongs to the same "structure print throws" family as
    MRW, RAF, RW2, GIF, TGA and BMP, not to BMFF's "prints a box tree" family. Only
    `Internal::CiffComponent::print` (`crwimage_int.cpp:415`) exists, and nothing in the CLI print path
    calls it.
- Preview versus thumbnail. `ExifThumb::setJpegThumbnail(buf, size)` (`exif.cpp:431-437`) stores the
  **actual bytes** in the data area of `Exif.Thumbnail.JPEGInterchangeFormat` (with
  `Exif.Thumbnail.Compression = 6`, `…Length = size`), and `ExifThumbC::writeFile` (`exif.cpp:379-390`)
  writes `thumbnail->copy(exifData_)` to `path + extension`, returning 0 when `Thumbnail::create` finds
  nothing (`exif.cpp:680-694`: `Compression` present and `6` → `JpegThumbnail`; present otherwise →
  `TiffThumbnail`; else `JPEGInterchangeFormat` key present → `JpegThumbnail`; else null). So a CRW with a
  `0x2008@0x0000` component **can** yield a real `-et` artifact of exactly the component's size.
  Because `0x2007` and `0x2008` land in different groups, source reading settles the *mechanism* but not
  the *magnitude*: whether the CLI's `-et` artifact length equals the `0x2008` component size exactly, and
  what the injected `Image2` pair prints as under `-pa` (a `Long` of `preview->pData() - pData`, i.e. an
  absolute file offset) are recorded from the run, not derived here. B11 therefore ships both shapes as
  separate fixtures — `0x2008` alone, and `0x2007` + `0x2008` together — so the pair distinguishes the
  payload key from the offset key in the same batch, and `-pR`-style structure output is not available at all
  (see the `-p` note below).
- Write path exists and is documented (`Read/Write` for exif/comments/thumbnail):
  `crwimage.cpp:72-95` re-reads the file, calls `CrwParser::encode` (`:116-124` → `CrwMap::encode`,
  `header.write(blob)`) and `io_->transfer(tempIo)`. `CrwMap::encode0x0805/0x080a/0x1810/0x2008`
  (`crwimage_int.cpp:861/881/956/994`) are the exact inverses of the decode rows above (`encode0x080a`
  concatenates Make + NUL + Model into one component; `encode0x1810` rebuilds a 28-byte component from
  X/Y/Orientation; `encode0x2008` writes the thumbnail from `ExifThumbC::copy()` or removes the tag).
  **B11 never invokes it.** The CRW write sub-operations stay unobserved, so the CRW `insert`/`replace`/
  `remove` candidate operations from the seed corpus are recorded as unobserved, not as failing and not as
  supported — same policy as B9/B10.
- One general fact noticed while reading `ExifParser::encode` and recorded here so it is not lost: at
  `exif.cpp:648` a write silently **drops** any tag whose `size() > 20480`, or `> 4096` when the tag name
  starts with `0x`, warning `Exif tag … not encoded`, and the `filteredPvTags` sweep at `exif.cpp:604-643` drops preview-offset/length
  pairs whose sum exceeds 32768. It is a reference behaviour, not a MetadataSharp one; any future write
  batch must decide deliberately whether MetadataSharp reproduces a silent drop.

### BMP — the emptiest reader in the registry

`bmpimage.cpp` (133 lines) contains no metadata code whatsoever: no Exif, no IPTC, no XMP, no comment, no
ICC, no thumbnail, no `printStructure` override. `mimeType()` returns `image/x-ms-bmp` with the in-source
explanation `// "image/bmp" is a Generic Bitmap` (`:25-28`). `isBmpType` (`:119`) compares **two bytes**:
`B` `M`. `readMetadata` (`:42`) reads the DIB header size with `getULong(buf + 14, …)` and branches:
`headerSize == 12` (OS/2 `BITMAPCOREHEADER`) takes width/height as two `getUShort` at `+18`/`+20`,
otherwise as `getULong` at `+18`/`+22` (i.e. **negative height**, the Windows top-down flag, is not
interpreted — it is read as a huge unsigned value). `setExifData`/`setIptcData`/`setComment` throw
`kerInvalidSettingForImage` (`:30/:34/:38`) and `writeMetadata` throws
`kerWritingImageFormatUnsupported("BMP")` (`:104`). Registry (`image.cpp:104`) is `amNone` × 4.
Doc `-` × 6, registry `amNone` × 4, code with no metadata path: **three independent agreements**, so BMP
records six `noDocumentedClaim` cells with nothing disputed, plus an inventory row naming the two header
shapes and the `BM`-only recognition test.

### GIF — same shape, one extra hazard

`gifimage.cpp` (90 lines): `mimeType()` → `image/gif` (`:18`); `isGifType` (`:75`) reads 6 bytes and
matches `"GIF87a"` or `"GIF89a"` — the arrays are declared `[8]` but only `len = 6` is compared, so a file
starting `GIF89a` followed by anything passes; `readMetadata` (`:37`) does `clearMetadata()`, consumes 4
bytes past the signature and takes `pixelWidth_`/`pixelHeight_` as little-endian `getShort` pairs (logical
screen descriptor); `setExifData`/`setIptcData`/`setComment` throw `kerInvalidSettingForImage` and
`writeMetadata` throws `kerWritingImageFormatUnsupported("GIF")` (`:60`); registry (`image.cpp:101`) is
`amNone` × 4; no `printStructure` override. Two consequences B11 must record as fixture properties, not as bugs: a GIF whose
four logical-screen bytes are absent (file length exactly 6) leaves `pixelWidth_`/`Height_` at their base
values because the `io_->read(...) == sizeof(buf)` guard fails silently (`:52-57`); and `-pS`/`-pC` throw
for the same `Image::printStructure` reason as CRW and BMP.

### TGA — recognition by file name

`tgaimage.cpp` (123 lines) is the one place in this batch where the reference does something the
documentation's word "recognized" does not describe. `isTgaType(BasicIo& iIo, bool /*advance*/)` (`:99`)
**ignores its `advance` argument and first tests the path**:

```cpp
// not all TARGA files have a signature string, so first just try to match the file name extension
const std::string& path = iIo.path();
if (path.rfind(".tga") != std::string::npos || path.rfind(".TGA") != std::string::npos) {
  return true;
}
byte buf[26];
const size_t curPos = iIo.tell();
if (curPos < 26)
  return false;
iIo.seek(-26, BasicIo::end);
…
bool matched = (memcmp(buf + 8, "TRUEVISION-XFILE", 16) == 0);   // some TARGA files, but not all
```

so **any file whose name ends in `.tga` is a TGA file to this pin, whatever its contents**. Registry order is
gif `image.cpp:101`, psd `:102`, tga `:103`, bmp `:104`, so the name test wins for everything ranked below
tga — a `foo.tga` holding a BMP header is read by `TgaImage`, not `BmpImage` — while `GifImage` still wins
for a `foo.tga` holding a GIF header, because `isGifType` is consulted first and looks at bytes.

The second half is worse for the documentation's claim, and it is the sharpest finding in this review: the
footer route is **unreachable through the normal entry points**. `isTgaType` guards the footer read with
`curPos = iIo.tell()` and `if (curPos < 26) return false;` — but `ImageFactory::open` (`image.cpp:839-849`)
and `TgaImage::readMetadata` (`tgaimage.cpp:39-49`) both call it with `advance = false` on a freshly opened
io, where `tell()` is 0. The guard was evidently meant to test the file length; as written it tests the
stream position, so a correctly footer-signed file named `*.tga` is recognized by its name and a correctly
footer-signed file named anything else is **not recognized at all**. `readMetadata` (`:39`) then
reads an 18-byte header and takes width/height as little-endian `getShort` at `+12`/`+14` — for a
footer-only file that read fails and dimensions stay at their base values. Setters throw
`kerInvalidSettingForImage`, `writeMetadata` throws `kerWritingImageFormatUnsupported("TGA")` (`:84`),
registry (`image.cpp:103`) is `amNone` × 4, no `printStructure` override.

This is why B11 ships **three** TGA fixtures, differing only in name and trailing bytes:
`owned-tga-full.tga` (footer signature + 18-byte header: recognized by name, dimensions read),
`owned-tga-name-only.tga` (23 bytes of text, no footer: recognized by name, and because `readMetadata`
has no short-read guard on the 18-byte header it reads the *dimensions out of the text* — so this fixture
reports plausible-looking numbers for a file with no image at all, which is the sharpest statement of
"recognition" and "metadata" being different claims), and
`owned-tga-signature-only.targa` (the *same* footer-signed bytes under a non-`.tga` name: recognized or not
according to whether the `curPos` guard is reachable). The triple is the census-visible statement of the
difference between "the image format is recognized" as documented and recognition as implemented, and the
third member is the one that turns the source reading above into an observation rather than an opinion.
It also means TGA rows are the only B11 rows whose *recognition outcome depends on the file name*, so the
recorder must key the observation on `(fixture, name)`, never on a content hash alone.

### QuickTime / ASF / RIFF / Matroska — read-only, XMP-only, and structure-print throws

| reader | ctor `supportedMetadata` | registry Exif/IPTC/XMP/Comment | `mimeType()` | `printStructure` override | `writeMetadata()` | `exifData_` sites | assignment sites |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `QuickTimeVideo` (`:542`) | `mdNone` | `amRead / amNone / amRead / amNone` (`image.cpp:109`) | `video/quicktime` (`:549`) | none | empty body (`:553`) | 0 | 132 |
| `AsfVideo` (`:205`) | `mdNone` | `amRead / amNone / amRead / amNone` (`:110`) | `video/asf` (`:208`) | none | empty body (`:212`) | 0 | 27 |
| `RiffVideo` (`:357`) | `mdNone` | `amRead / amNone / amRead / amNone` (`:111`) | `video/riff` (`:360`) | none | empty body (`:364`) | 0 | 37 |
| `MatroskaVideo` (`:604`) | `mdNone` | `amRead / amNone / amRead / amNone` (`:112`) | `video/matroska` (`:607`) | none | empty body (`:611`) | 0 | 17 |

What that table licenses B11 to say, and nothing more:

- **These four readers populate XMP only.** Zero `exifData_[…]` assignment sites, zero `iptcData_`, zero
  `comment_`, zero `iccProfile_`, zero thumbnail handling; every metadata assignment goes into the XMP
  container, written either as `xmpData_[…]` or `xmpData()[…]` — both spellings are counted in the table
  above (e.g. `riffvideo.cpp:382` `xmpData_["Xmp.video.MimeType"] = mimeType()`,
  `asfvideo.cpp:231-232` `xmpData()["Xmp.video.FileSize"]` / `["Xmp.video.MimeType"]`). Yet the registry advertises
  **Exif `amRead`** for all four and `Image::mimeType`-level support is separately declared `mdNone` in
  each constructor. So the reference is internally inconsistent about Exif for these types — the registry
  says readable, the object says no supported metadata, and the reader writes no Exif tag. B11 records
  that as a *reference fact with three sources that disagree*, which is precisely the situation the census
  exists to make visible. It must not be recorded as "Exif read works" (no code path) or as "Exif read
  fails" (nothing throws; the container is simply empty).
- **`-pS` and `-pC` throw for all four.** None of them overrides `printStructure`, so
  `app/actions.cpp:1983-1991` → `Image::printStructure` → `kerUnsupportedImageType`, exit 1. Note what
  `-pC` is at this pin: `Params::evalPrint` (`app/exiv2.cpp:613-675`) maps `-pS`→`pmStructure`→`kpsBasic`,
  `-pC`→`pmIccProfile`→`kpsIccProfile`, `-pc`→`pmComment`, `-pa`→the `kyct` flag list, and **`-pR`
  (`pmRecursive`) is compiled out of Release builds** (`app/exiv2.cpp:654-663` prints "Action not available
  in Release mode" and returns 1 under `NDEBUG`). Since the CI job builds Release, no B11 observation may
  use `-pR`, and `-pC` is an ICC-profile print, not a recursive dump. The flag set for B11 is
  `-pS`, `-pa`, `-pc`, `-pC`, `-et`, `-px`, `-pe`: the first five are B10's set, and `-px`/`-pe` are new
  because they are the two flags that can tell "the reader fills XMP and nothing else" from "no metadata at
  all" — `-px` restricts the listing to the XMP table, `-pe` to the Exif table, `-pa` prints all three.
  Full flag semantics at this pin, so the recorder does not re-derive them:

  | flag | parsed to (`app/exiv2.cpp:613-690`) | reaches |
  | --- | --- | --- |
  | `-ps` | `pmSummary` | `Print::printSummary()` |
  | `-pa` | `pmList` + `kyct` | all three tables; key/type/count/translated columns |
  | `-pe`, `-pt` | `pmList` + `Ekycv`, `Ekyct` | **Exif table only** |
  | `-pi` | `pmList` + `Ikyct` | **IPTC table only** |
  | `-px` | `pmList` + `Xkyct` | **XMP table only** |
  | `-pv`, `-ph` | `pmList` + `Exgnycv`, `Exgnycsh` | all tables, plus group/name/(hex) columns |
  | `-pc` | `pmComment` | `Print::printComment()` |
  | `-pp` | `pmPreview` | preview list |
  | `-pC` | `pmIccProfile` → `kpsIccProfile` | `Image::printStructure`; base64 `data:` chunks when `-b` (`app/actions.cpp:175-199`) |
  | `-pS` | `pmStructure` → `kpsBasic` | `Image::printStructure` |
  | `-pR` | `pmRecursive`, refused under `NDEBUG` | `Image::printStructure` with `kpsRecursive` |

  Note what is *not* in that table: there is no flag that reaches a recursive dump in a Release build, so
  "structure print" for every B11 format means `kpsBasic`, and `-et` is the extract action with the
  thumbnail target (`app/actions.cpp:836-858`), not a print mode.
- **`writeMetadata()` is an empty body, not a refusal.** For MOV/ASF/AVI/MKV, `exiv2 … -o out.mov` on a
  read-modify-write path therefore **silently writes the unchanged file and exits 0**, whereas BMP/GIF/TGA
  throw `kerWritingImageFormatUnsupported` and BMFF throws `kerWritingImageFormatUnsupported("BMFF")`
  (`bmffimage.cpp:779-781`). This is a documented-`-`-vs-silent-no-op divergence in the *write* direction,
  and it belongs to the workflow/transfer batch (B13), not here: B11 authors no write cells for video, so
  the divergence is recorded as a reference fact in this document and not as a census row.
- **BMFF is a different animal and B11 does not re-enumerate it.** `BmffImage` declares
  `mdExif | mdIptc | mdXmp` (`bmffimage.cpp:87`), *does* override `printStructure` (`:743-776`:
  `kpsIccProfile` writes the profile, `kpsBasic`/`kpsRecursive` walk boxes via `boxHandler`, `kpsXMP` is
  `#ifdef EXV_HAVE_XMP_TOOLKIT` and so falls through to "do nothing" in this build configuration — as does
  the `kpsIptcErase` call the erase action makes (`app/actions.cpp:708-710`)), sets `setComment` to throw
  (`:706`), and is the only entry in the registry with
  IPTC `amRead`. Its census rows already exist from B7 (HEIF/HEIC/AVIF/CR3 with the `bmff-*` layouts).
  Re-listing them would double-count a format whose layout enumeration is already frozen for this pin.
- **Dispatch is brand-driven, and the table order is load-bearing.** `image.cpp:106-108` carries the
  comment `// needs to be before bmff because some ftyp files are handled as qt and the rest should fall
  through to bmff`, and `isQTimeType` (`quicktimevideo.cpp:1616-1643`) enforces it: it needs **both** a
  known atom tag at offset 4 (one of `PICT free ftyp junk mdat moov pict pnot skip uuid wide`) **and** the
  brand at offset 8 present in `qTimeFileType` (`:42+`), with the source note `// we only match if we
  actually know the video type. This is done to avoid matching just on ftyp because bmffimage also has
  that header.` `isBmffType` (`bmffimage.cpp:794-810`) is the fall-through: bytes 4-7 == `ftyp` **or**
  `JXL `. `isMkvType` (`matroskavideo.cpp:930`) compares the four bytes `1A 45 DF A3`. So for a synthetic
  `ftyp` file the *brand* alone decides `video/quicktime` versus BMFF, and a brand in neither vocabulary
  is a BMFF file. B11's two MOV stubs pin exactly that: one with a `qTimeFileType` brand (→ QuickTime
  reader) and one with a brand absent from that table (→ BMFF reader, i.e. **the same bytes reach a
  different reader, and therefore a different metadata surface**, which is the only honest way to enumerate
  a format the table doesn't list).

## Oracle policy

1. **Channel.** `pinned-exiv2-b11` (a job appended last in `.github/workflows/build-and-test.yml`) is the
   only source of baseline observations, exactly as in B9/B10: the sandbox has no cmake/ninja/expat/zlib
   headers, so no local Exiv2 build exists and none can be claimed. The receipt's `provenance` block says
   this; it must not be reworded into a cross-environment equality claim.
2. **Flags per group, and why they differ.** The four seed-grid formats get
   `-pS`, `-pa`, `-pc`, `-pC`, `-et`, `-pe`, `-px` (seven); the video stubs get
   `-pS`, `-pa`, `-pC`, `-px`, `-et` (five), because `-pc` for a type with comment `amNone` and `-pe` for a
   reader that writes no Exif tag are both already answered by `-pa`, and the budget is better spent on the
   brand-dispatch pair. The preflight therefore records 11 × 7 + 6 × 5 = **107** flag-runs over the 17
   fixtures, and the census recorder **95 rows**, of which 87 are observed and 8 blocked. Chunk budgets are
   split across the two recorders rather than shared: `observe_b11.chunks` caps at 30 (B10 used 10 for half
   the rows) and `probe_b11_raw` caps at 12, because GitHub's limit is 50 annotations per *check run* and
   the job runs both — 30 + 12 leaves the failure-annotation step its headroom. The payload must fit in one
   stream: a truncated chunk stream looks like GitHub truncation and is not diagnosable after the fact.
3. **What may be predicted.** Only facts computable from bytes the repository wrote: the digest identities
   (`owned-*` header regions), the `sha256` of the comment string carried in `0x0805`, the exact artifact
   length for `-et` when a `0x2008` component exists, and the "these N fixtures share one value" relations
   (e.g. BMP/GIF/TGA `-pC` all reach the same throw). A prediction is a *consistency check on the
   recorder*, never a support assertion.
4. **What may not.** Any statement that a format "is supported", "is not supported", "should", or "fails";
   any exit-code expectation for a documented-`-` family (the `-` means no claim, so a throw is
   *consistent*, not *correct*); any `exitCodeEqualsArtifactBytes: false` inference of failure — by the
   byte-count rule, `-et` with `rc == 0` and no artifact is a **consistent** outcome and must be recorded
   as such, never as a defect.
5. **Vacuity ban.** No assertion whose negation is unreachable. Concretely: `structure print either throws
   or produces output` is not an assertion and must not appear; a `-pe` (Exif-table) listing on a reader that
   writes no Exif tag is recorded as "the listing text equals the recorded text for that fixture", never as
   "the listing is empty", and never as evidence about whether Exif is supported.
6. **ExifTool secondary channel.** Optional, non-adjudicating, and never an oracle. Where ExifTool
   (`212f02cf…`, the pin already used by the `pinned-exiv2-b10` job) has an opinion about a B11 stub it is
   recorded as a `secondaryObservations` block with the tool version; where it warns or refuses to classify
   a stub, the warning text is recorded verbatim. A secondary observation may never change a row's
   `baselineObservation` value.
7. **Verification stays `not-assessed`** on every B11 row, and `denominatorFrozen` stays `false` at the
   top level only — it is not repeated per row, and no row may gain a frozen-denominator flavour.

## Enumeration this review authorizes, and nothing else

`tools/reference/build_census.py` grows one more batch block. Authorized shape:

- **CRW**: `inventory` + six seed families (7 rows) on the CIFF fixture set; exif/comments/thumbnail carry
  the documented `Read/Write` claim as `observed-cli` where the observation exists and as `not-applicable`
  for the write sub-operations; iptc/xmp/icc as `noDocumentedClaim: true`.
- **BMP / GIF / TGA**: `inventory` + six seed families (7 rows each) with `noDocumentedClaim: true` on all
  18 seed cells, and the recognition / MIME / dimension surface described **only** in the inventory row.
  TGA gets three inventory rows (footer-signed and `.tga`-named, name-only, footer-signed but
  wrongly named) because recognition differs between them; BMP and GIF get two each, one per header shape.
- **MOV / MKV / AVI / WAV / ASF**: `inventory` row per format only, `operation: inventory`,
  `family: inventory`, `noDocumentedClaim: false` (there is no claim to negate), with row text naming the
  reader, its `mdNone` supported-metadata, its `amRead` Exif registry field, and the absence of a §3 table
  row. No `exif`/`iptc`/`xmp`/`comments`/`icc`/`thumbnail` cell is created for a video format, and no
  `documentedClaim` is invented for it.
- **MP4**: deliberately **not** added as a format. It has no §3 row, and "which reader gets the file" is a
  property of the brand, not of the extension — enumerating "MP4" as a format would freeze an extension →
  reader mapping that the code does not implement. The brand pair is enumerated under MOV with the layout
  names saying so. If a later batch wants an MP4 label, it has to argue for it in its own review.
- New layouts are appended to **every** family list in `enumerations.layout`, following the B8/B9/B10
  convention (a format's layout appears in all seven lists, including the `-unobserved` variants for the
  cells that stay blocked). Authorized additions, one per fixture:
  `ciff-mapped-v1`, `ciff-thumb-only-v1`, `ciff-preview-plus-thumb-v1`, `ciff-unmapped-only-v1`,
  `ciff-vendor-private-unobserved`, `bmp-dib-v3`, `bmp-os2-coreheader-v1`, `gif-lsd-v1`,
  `gif-truncated-v1`, `tga-full-v1`, `tga-name-only-v1`, `tga-signature-wrong-name-v1`,
  `qtime-known-brand-v1`, `qtime-unknown-brand-v1`, `mkv-ebml-header-v1`, `riff-avi-avih-v1`,
  `riff-wav-fmt-v1`, `asf-header-guid-v1`.
- The `ciff-vendor-private-unobserved` layout is the only blocked row set in this batch: Canon's maker-note
  space beyond the 17 `canonId` mapping rows is not reviewed or cleared, so it is enumerated and left
  `blocked`, exactly as in B9/B10. BMP, GIF and TGA get **no** blocked row, because their readers have no
  vendor-private surface at all — inventing one to match the other formats would be a fabricated cell. The
  five video formats get no blocked row either, for the same structural reason: with no family cells
  enumerated there is no blocked family to carry.
- Row arithmetic, stated so a reviewer can check the generator against it. A (format, layout) pair emits
  `inventory`, six `read` rows and — only where the seed carries a thumbnail claim — one `thumbnail extract`
  row. The extract row is conditional for a structural reason, not a choice: a format whose documented cell
  is `-` for `thumbnail` has no thumbnail to extract, so BMP, GIF and TGA emit **7** rows per layout while
  CRW emits **8**. CRW has five layouts (four fixtures plus the blocked vendor-private cell), so
  5 × 8 + (2 + 2 + 3) × 7 = 40 + 49 = **89 rows** for the seed grid. Each video layout emits **one**
  inventory row: 6 layouts, so **6 rows**. B11 total **95 rows**, taking the census from 728 to **823**
  (the earlier draft of this document said 88 and 94 by counting eight rows for every layout and four
  layouts for CRW; the generator's numbers are the ones above, and this paragraph is the amendment it
  calls for). `coverage.enumerated`
  grows by 7 (CRW) + 7 (BMP) + 7 (GIF) + 7 (TGA) + 1 × 5 (video inventory cells) = **33** cells, from 188
  to 221; `coverage.pendingSeedIds` becomes **empty**; `coverage.inventoryTrack` gains every format that
  gains an inventory row, i.e. `CRW BMP GIF TGA MOV MKV AVI WAV ASF`. If the generator's own bookkeeping
  disagrees with this sentence, the generator is right and this document is amended in the same PR — the
  census is the artifact, not this file.
- **Generator shape, and the trap in it.** `B11_FORMAT_LAYOUTS` (seed-grid formats) stays on the existing
  `{fmt}/{family} for fmt, _ in …` comprehension path, and the video list must be a **separate**
  inventory-only structure. Feeding video layouts through the family path would emit six fabricated
  `exif`/`iptc`/`xmp`/`comments`/`icc`/`thumbnail` cells per video format with no seed and no documented
  claim behind them — exactly the silent denominator growth this census has been defending against since
  B0. The `pendingSeedIds` computation filters on `if 'inventory' not in e`, so an inventory-only
  enumeration for video must not disturb it: after B11 the pending list is empty because the four small
  formats are enumerated, not because anything was excluded.
- Fixture digests are recorded in `tests/fixtures/b11/census-manifest.json` and copied into rows; the
  manifest is compared whole by `verify()`, so `tools/reference/b11_container_fixtures.py generate` must be
  re-run and the census regenerated in the same commit whenever a fixture changes.
- Evidence lands in `docs/research/evidence/e01-b11-census/` (`observations.json`, `ci-observations.json`,
  `master-observations.json`, `receipt.json`, `README.md`) and is never hand-edited. The B10 receipt
  builder is **not** reusable unchanged, as an earlier draft of this document claimed:
  `tools/reference/write_b10_receipt.py` hardcodes its evidence paths, its `batch`, its CI job's display
  name, its two annotation titles and a RAF-specific `authoredComment` check. B11 therefore gets
  `tools/reference/write_b11_receipt.py`, cloned from it with those six things replaced — the
  `--ci-run`/`--master-run` reproduction interface and the "recompute, never retype" discipline carry
  over unchanged, and B11's equivalent of the authored-comment check is the `0x0805` payload digest. Reproduction is claimed only
  in the B10 wording: *N independent CI runs of the same recorder agree on the observation payload
  byte-for-byte; only the build timestamp, the validated head and ExifTool file dates move*, with the
  `differingSections` list computed by the receipt generator rather than asserted here.

## Not claimed

- That CRW/BMP/GIF/TGA "should" behave as documented: B11 records what the pinned binary did.
- That the 24 cells are now *verified*: every B11 row is `verification: not-assessed`, and closing
  `pendingSeedIds` is bookkeeping about enumeration, not about parity.
- That the write path of any format in this batch works or is broken: it was not invoked.
- That video formats are "not a format", "unsupported", or out of scope forever: they are absent from the
  pinned §3 table, present in the registry, and enumerated here at the recognition level only.
- That ExifTool agrees with Exiv2 about any of these stubs, or that either agrees with MetadataSharp.
- That a synthetic stub is a valid media file in any sense beyond "satisfies this pin's recogniser".
- Anything about `tests/data/**` in upstream Exiv2, which B11 does not copy, reference, or re-license.
