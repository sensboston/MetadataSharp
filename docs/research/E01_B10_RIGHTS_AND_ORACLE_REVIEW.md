# E01 B10 — TIFF-derived RAW wave 2 rights and oracle review

B10 covers MRW, RW2, SR2 and RAF (+ inventory tracks). As the programme requires (§5 B10:
"Wave 2: MRW/RW2/SR2/RAF (+inventory) | RAF documented as partial in baseline (recorded) |
High (same)"), this ledger is written **before** any fixture is authored and records source
and rights decisions separately. It is research only: no product implementation, no write
support, no exclusion decision, no coverage percentage, no denominator freeze and no package
publication is authorized here. Exiv2 0.28.9
(`b0859fc61fea5dedfc898aece6b15893ddf4314e`, "the pin") is the pinned external black-box
reference; ExifTool 12.76 (`212f02cf22b9484674752181cad3b73089ce9694`) is secondary, never a
normative oracle.

## What was reviewed, and it is deliberately narrow

Every layout statement below is a **transcription of the pinned reference implementation's own
readers and of the CLI action code**, obtained by reading the files at the pin through the
GitHub contents API. **No vendor format specification was reviewed.** Where the pinned code
points at third-party documentation, that pointer is recorded and the documentation itself was
*not* read, so the cells it would cover stay blocked:

* `src/image.cpp:83,90,91,98` — the registry rows for this batch. All four are
  `{…, amRead, amRead, amRead, amNone}`: Exif, IPTC and XMP are **read-only** and Comment mode
  is `amNone` for MRW, RW2, SR2 and RAF alike. MRW/RW2/RAF get dedicated classes
  (`newMrwInstance`/`isMrwType`, `newRw2Instance`/`isRw2Type`, `newRafInstance`/`isRafType`);
  **SR2 is registered as `{ImageType::sr2, newTiffInstance, isTiffType, …}`**, i.e. in this
  build SR2 content is handled by the generic TIFF reader, exactly like ARW/NEF/PEF/SRW in B9.
  This is a fact about the pinned build; it is not an accepted exclusion and not a MetadataSharp
  product decision. Because all four formats are read-only in the registry, **no B10 write
  suboperation is enumerable** and the batch is record-only, the same rule B9 applied.
* `TiffHeaderBase::read` (`src/tiffimage_int.cpp`) — requires a non-null buffer of ≥ 8 bytes,
  accepts `II` (little endian) or `MM` (big endian) by comparing bytes 0 and 1, then takes
  `t = getUShort(pData+2)` and accepts iff `t == 444 || t == 17234 || tag_ == t`, where `tag_`
  is the magic the subclass was constructed with; the root IFD offset is read from bytes 4-7
  **unchecked**. The two constants 444 (JPEG-XR) and 17234 (DCP) are accepted by *every*
  TIFF-derived header, which is the mechanism behind batch B8's DCP observation and is again
  relevant here: a fixture carrying magic 444 would pass `isTiffType`, `isRw2Type` and any
  other TIFF-family check in this build. The B10 fixtures therefore use each format's own
  magic and never these two.
* `Image::isTiffType` (`src/tiffimage.cpp:247`) — reads 8 bytes and applies that rule with
  `tag_ = 42`. Consequence for dispatch: a file starting `49 49 55 00` (RW2's magic, 85) is
  *rejected* by `isTiffType`, so the registry order (`tiff` at `:84` precedes `rw2` at `:90`)
  does not swallow RW2 — the file reaches `Rw2Image`. That is the only way the RW2-specific
  code becomes observable through the CLI, and it is a difference from B9's marker layouts,
  which deliberately reached the generic TIFF reader.
* `TiffImage::mimeType()` (`src/tiffimage.cpp:59`) with `mimeTypeList` (`src/tiffimage.cpp:54`)
  — `{32767 arw, 32769 erf, 32770 srw, 34713 nef, 65000 dcr, 65535 pef}`. **There is no entry
  for SR2**, so an SR2 fixture handled by the generic reader reports `image/tiff` no matter what
  `Compression` says, and 32769 belongs to Epson ERF, not to Sony. That is a pinned-build fact
  about observable identity, and it is why SR2's identity cells are handled as vendor-private
  here rather than "observed" through a generic path.
* `app/actions.cpp` — the CLI's own `-et` implementation: `Extract::run` (`:782`),
  `Extract::writeThumbnail` (`:823`), `rc = static_cast<int>(exifThumb.writeFile(thumb))`
  (`:858`), the "does not contain a thumbnail" message guarded by `if (rc == 0)` (`:860`) and
  `ExifThumbC::writeFile` (`src/exif.cpp:379,389`) returning `Exiv2::writeFile`
  (`src/basicio.cpp:1752,1757`), which returns the **byte count** written. See
  "The `-et` exit code is a size, not a status" below — it changes how extract rows must be
  classified in this batch.

License handling: the pinned reference files carry `SPDX-License-Identifier: GPL-2.0-or-later`.
Nothing is copied from them: this document transcribes numeric facts (magic values, offsets,
comparison lengths, error identifiers) and the fixtures are generated from stdlib byte
operations in this repository. That is also why the reference stays an external black box in CI
and never a product dependency. The ExifTool module consulted below
(`lib/Image/ExifTool/MinoltaRaw.pm`) is likewise read for fact only.

## MRW — Minolta RAW

* Accept test `isMrwType` (`src/mrwimage.cpp:128`) reads 4 bytes and requires
  `memcmp(buf, "\0MRM", 4)` — the file must begin with `00 4D 52 4D`; on failure the position is
  restored. Note the first byte is a **NUL**, not `FF`; the secondary reference agrees on this
  shape: `MinoltaRaw.pm:410` matches `/^\0MR([MI])/`, where `M` means big-endian MRW images and
  `I` the little-endian MRWInfo block found inside ARW (so ExifTool accepts both byte orders and
  exiv2 only `\0MRM`). Neither of us reviewed a vendor spec: the pinned code and the pinned
  secondary reference agree, which is the strongest statement this batch can make about the
  header.
* `MrwImage::mimeType()` (`:21`) is the constant `image/x-minolta-mrw`; the class declares
  support `mdExif | mdIptc | mdXmp` (`:18`).
* `MrwImage::readMetadata` (`:60`–`:112`): reads the first 8-byte block header; `end` is
  `getULong(tmp+4, bigEndian)`; enforces `8 <= end` (`:80`); then walks 8-byte block headers
  `while (memcmp(tmp + 1, "TTW", 3) != 0)` (`:85`), each iteration taking `siz =
  getULong(tmp+4, BE)` and enforcing `siz <= end - pos` before seeking; on finding a block whose
  bytes 1-3 are `TTW` it enforces `siz <= io_->size()`, reads `siz` bytes and calls
  `TiffParser::decode(exifData_, iptcData_, xmpData_, …)`. **The TTW block is a self-contained
  TIFF blob**, and all three metadata families come out of it in one call.
* `setExifData`/`setIptcData`/`setComment` throw `kerInvalidSettingForImage` and
  `writeMetadata` throws `kerWritingImageFormatUnsupported` (`:115`).
* There is **no `printStructure` override**, so `-pS` reaches `Image::printStructure`
  (`src/image.cpp:136`), which throws `kerUnsupportedImageType`. That is a per-format
  inventory difference worth having on the record: MRW answers `-pS` with an error, where B9's
  marker layouts printed a structure dump.
* The raw picture data (`\0DAT`) and the camera-private blocks are never read by the pinned
  build. They stay vendor-private.

## RW2 — Panasonic RAW

* `Rw2Header::Rw2Header()` is `TiffHeaderBase(0x0055, 24, littleEndian, 0x00000018)`
  (`src/rw2image_int.cpp:6`): magic 85, header size 24, root offset 24. `isRw2Type`
  (`src/rw2image.cpp:203`) reads 24 bytes and calls that header's `read`, so only bytes 0-7
  decide acceptance while the read length is 24 — a file shorter than 24 bytes fails even if the
  magic is right. The same rule also means the root offset is **whatever bytes 4-7 say**; the
  constructor's 24 is not enforced against them (the identical weakness B9 recorded, and the
  reason the fixture writes 24 in both places and the oracle asserts the pair agrees).
* `Rw2Image::mimeType()` (`:29`) is `image/x-panasonic-rw2`.
* `Rw2Image::printStructure` (`:62`) prints the literal line `RW2 IMAGE` and then
  `printTiffStructure`. This is the one self-declaring identity observable in this batch, so
  the inventory row for RW2 is not merely "did it parse".
* `Rw2Image::readMetadata` (`:78`) decodes via `Rw2Parser::decode` →
  `TiffParserWorker::decode(…, Tag::pana, TiffMapping::findDecoder, &rw2Header)`, then runs
  `PreviewManager`: **more than one preview triggers
  `EXV_WARNING "RW2 image contains more than one preview. None used."` (`:103`) and returns
  without merging; exactly one preview is opened through `ImageFactory::open(preview.pData(),
  preview.size())` and its non-duplicate EXIF is merged (`:106`–`:120`).
* `setExifData`/`setIptcData`/`setComment` throw `kerInvalidSettingForImage` (`:47`–`:60`) and
  `writeMetadata` throws `kerWritingImageFormatUnsupported` (`:184`).
* What stays blocked: the RW2 raw image itself and the Panasonic-private tags are not
  reproduced by us; the *naming* of tags under `Tag::pana` is decided by the pinned mapping, so
  the review deliberately does **not** predict which keys `-pa` will print — only which tags
  exist, with what values and in which IFD. The name mapping is an observation, not an oracle
  claim.

## RAF — Fujifilm RAW

* Accept test `isRafType` (`src/rafimage.cpp:339`): first 8 bytes must equal `FUJIFILM`.
  `RafImage::mimeType()` (`:26`) is `image/x-fuji-raf`; support mask `mdExif | mdIptc | mdXmp`.
* `RafImage::readMetadata` (`:224`) is a pure offset chase, all big-endian:
  seek **84** → JPEG preview offset, **88** → its length; enforce `offset + length <= size` and
  `length >= 12` (`:256`); read that range and parse it as a JPEG in a `try { … } catch (const
  Exiv2::Error&) {}` that **swallows failures**, copying its Exif, IPTC, XMP **and comment**
  (`:265`–`:277`); then *inject* `Exif.Image2.JPEGInterchangeFormat` and
  `Exif.Image2.JPEGInterchangeFormatLength` from the two header fields (`:281`–`:282`).
  Then seek **100** → embedded-TIFF offset, **104** → its length; enforce the sum lies inside the
  file; require the 4 bytes at that offset to be `II*\0` or `MM\0*` (`:314`) and only then
  `TiffParser::decode(…)` on that block (`:319`).
* The code itself records what it does *not* do: `// Todo: parse the proprietary metadata
  structure at offset 92 for pixelWidth_ & pixelHeight_` followed by pointers to
  `libopenraw.freedesktop.org/formats/raf/` and `exiftool.org/TagNames/FujiFilm.html#RAF`.
  **Neither page was reviewed for this batch.** So the offset-92 block is authored as owned
  filler, is never called a CFA structure, and the two cells the baseline describes as "partial"
  (pixel dimensions from that block, and the private metadata it holds) are recorded as
  vendor-private/blocked. `RafImage::pixelWidth()` (`:30`) confirms the fallback: it reads
  `Exif.Fujifilm.RawImageFullWidth` from the parsed block instead.
* `RafImage::printStructure` (`:65`–`:222`) dumps fixed ranges of the file — 4 bytes at 8,
  8 at 12, 32 at 20 (labelled `camera`), 4 at 60 (`version`), 20 at 64 (`unknown`) — then
  `TIFF Offset`/`TIFF Length`, and 16-byte hex previews of the JPEG, CFA and embedded-TIFF
  regions. Because those are literal slices of the file, the RAF inventory row is predictable
  *byte for byte* from the fixture recipe, which is what makes it a good oracle target.
* `setExifData`/`setIptcData`/`setComment` throw `kerInvalidSettingForImage` (`:52`–`:62`) and
  `writeMetadata` throws `kerWritingImageFormatUnsupported` (`:326`).
* Divergence to observe, not to assume: the registry says Comment `amNone` for RAF, yet
  `readMetadata` copies the preview JPEG's comment. Batch B4 established that
  `TiffImage::comment()` reads `Exif.Image.ImageDescription`, and the RAF comment does **not**
  come through that tag — it is set directly. So `-pc` on RAF is an open question with two
  documented sides, and the census must record whichever way the pinned CLI answers.

## SR2 — Sony RAW type 2

* No `Sr2Image` exists at the pin: SR2 is served by `newTiffInstance`/`isTiffType`, and
  `mimeTypeList` has no SR2 entry. The observable contract for SR2 content is therefore exactly
  the generic TIFF contract plus `amRead/amNone` modes, and `image/x-sony-sr2` is not reachable
  through this build's TIFF path.
* The parts of SR2 that make it SR2 — the `CT`/`SATD`/`FMT` directory structures and the raw
  data — are read by nothing in the pinned build. Inventing "SR2-looking" bytes would produce an
  observation about the generic reader while implying the vendor layout had been covered, which
  is exactly the failure mode this programme forbids. So B10 authors an SR2 fixture that is
  **only** a plain little-endian TIFF marker (`II` + 42 + root 8, owned EXIF carriers, nothing
  else), and its observation rows are labelled in the report as generic-path results: what is
  recorded is "the pinned build sees no SR2-specific structure here", not "SR2 is supported".
* The one genuinely Sony-specific thing inside this path is the maker-note mapping —
  `IfdId::sony1CsId`/`sony1Cs2Id`/`sony2FpId`, `sonyTagDecipher` and the `sony2FpDef` table at
  `src/tiffimage_int.cpp:953-993`, reached from the generic TIFF reader and not from any SR2
  container parser. **B10 authors no maker-note payload**: a `Sony2*` blob would be a batch-B12
  fixture wearing a B10 label. The SR2 vendor-private layout cells stay blocked, and the
  maker-note cells are simply not enumerated here (B12 owns them).

## The `-et` exit code is a size, not a status

B9's evidence recorded `-et` with `rc=137` next to a 137-byte artifact and classified it
`nonzero-exit-artifact-produced` without diagnosis, as batch B0 required. The pinned source
explains it: `Extract::writeThumbnail` returns `static_cast<int>(exifThumb.writeFile(thumb))`
and that call chain ends in `file.write(...)`, i.e. **the number of bytes written**. The
message for a *missing* thumbnail is printed only when that count is 0
(`app/actions.cpp:860`), so a failed extraction exits 0 and a successful one exits
*(bytes mod 256)*. All six B9 rows satisfy `rc == artifact length`, and that invariant is now
asserted in `tests/discovery/test_census.py` across all three B9 samples, so the reading is
checkable rather than prose. Consequence for B10: extract rows are classified by that rule
(`extracted`, `no-thumbnail`, `other`) and never by the sign of the exit code.

## Per-format decisions

| Format | Fixture decision | Why |
| --- | --- | --- |
| MRW | **author** `\0MRM` + segment walk + `\0TTW` embedded TIFF | accept test, block walk and `TiffParser::decode` blob are fully transcribed above; the block walk is MRW-specific, not generic TIFF |
| RW2 | **author** `II` + magic 85 + root 24 + one preview IFD; second variant with two previews to trigger the pinned warning | header rule and preview branch are code-observable, and `RW2 IMAGE` gives an independent identity signal |
| RAF | **author** `FUJIFILM` + owned version/format/camera bytes at 8/12/20/60/64 + BE offsets 84/88 and 100/104 + owned preview JPEG + owned embedded TIFF | every byte the reader touches is fixed by the code above; offset 92 stays owned filler and is *not* called a CFA structure |
| SR2 | **author a plain generic-TIFF marker only** — no SR2 container structure is invented | the pinned build reads SR2 content with the generic TIFF reader; inventing `CT`/`SATD`/`FMT` bytes would fake coverage, and the only Sony-specific code in that path belongs to B12 |

Rights, for all four: fixtures are synthesized from stdlib byte operations in this repository,
contain no imported capture, no vendor sample, no camera output and no third-party corpus
excerpt; the only payloads are the owned `B9`-family token pattern reused as `B10*` tokens, the
owned IIM/XMP/ICC packets and the owned 137-byte JPEG already used in B9 (its provenance is
recorded in `tests/fixtures/b9/census-manifest.json` and reused under the same owned terms).
Clearance is for MIT-licensed, read-only, CI-local research use inside this repository, nothing
more.

## Oracle policy

`tools/reference/b10_raw_fixtures.py` will:

1. build each fixture from an explicit layout table (block type, length, payload, pool spans)
   so the file is a pure function of the recipe, and emit
   `tests/fixtures/b10/census-manifest.json` that `verify()` compares **whole** (the B7/B8 rule:
   any fixture change requires `generate` plus a census regeneration);
2. re-derive the container facts by an **independent** re-parse: each accept test transcribed
   above is re-executed against the finished bytes (`\0MRM`, magic 85 with size ≥ 24, `FUJIFILM`,
   and for RAF the `II*\0`/`MM\0*` gate at the embedded-TIFF offset), the MRW segment walk is
   replayed and must land on the authored `\0TTW` block with `siz <= end - pos` at every step,
   the RAF offset pairs must point at the authored regions and satisfy both `enforce` bounds, and
   the embedded payloads (TIFF blobs, preview JPEG) must be byte-equal to their owned sources;
3. assert the file length and the pad-byte rule (`trailingPadBytes <= 1`) as in B9, so a
   fixture that still parses but whose declared ranges lie outside the file fails review;
4. refuse to be satisfied by the two wildcard magics: a fixture may not carry 444 or 17234,
   because passing on those would prove nothing about the format being claimed.

Where the pinned build's naming/mapping decisions are involved (`Tag::pana` key names, whether
the MRW TTW blob surfaces `Exif.Thumbnail.*`, whether RAF's copied comment reaches `-pc`, whether
the generic reader reaches any `Sony2*` mapping without a maker note, whether ExifTool classifies
these authored headers at all) the review records them as **open**: CI
observes, the oracle does not predict, and `static-not-applicable` is never used as a stand-in
for an unanswered question.

## Census enumeration this review authorizes

4 layouts × 8 rows = **32 observation-eligible rows** plus 4 blocked layouts × 8 rows = **32
blocked rows**: **64 new rows**, census total 664 → **728**, appended as the last batch so every
existing `rowId` stays stable.

| Layout | Formats | Rows |
| --- | --- | --- |
| `raw-mrw-ttw-embedded-tiff` | MRW | 8 (inventory, 4 read families, comment read, icc read, thumbnail extract) |
| `raw-rw2-ii-55-root-24` | RW2 | 8 |
| `raw-raf-fujifilm-be-offsets` | RAF | 8 |
| `raw-tiff-ep-ii-sr2` | SR2 | 8 — generic-TIFF observation cells only; no SR2 identity, no maker-note payload |
| `raw-<fmt>-vendor-private-unobserved` | MRW, RW2, SR2, RAF | 8 each, all `blocked-rights` |

`noDocumentedClaim` attaches to the cells this review could not ground: the comment rows for
MRW/RW2/SR2 (registry `amNone` and no reader that fills `comment_`), the ICC rows for MRW and
SR2 (no documented carrier reviewed), and RAF's two cells the baseline calls partial (pixel
dimensions and the offset-92 structure). Every new row keeps `verification: not-assessed`,
`denominatorFrozen: false`, and no coverage percentage is computed anywhere in this batch.

## Not claimed

* That any authored file is a valid capture of the vendor format, or that a camera would read it.
* That `blocked-rights` means unsupported: it means unreviewed, and the vendor-private cells stay
  visible in the census.
* That read-only registry modes are accepted exclusions for MetadataSharp, or that a
  `not-applicable` cell exists anywhere in this batch.
* Any denominator, coverage figure, support claim, conformance claim, or write contract.
* That ExifTool's `File:FileType` proves anything: its file-type lookup consults the extension,
  so for authored markers it is recorded as a secondary observation only (the B9 rule).
