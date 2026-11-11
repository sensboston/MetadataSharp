# E01 B9 — TIFF-derived RAW wave 1 rights and oracle review

B9 covers ARW, CR2, NEF, ORF, PEF and SRW (+ inventory tracks). This ledger records
source and fixture decisions separately, before fixture authoring, as the census
proposal requires (§5 B9: "vendor layout specs needed; no real captures imported; rows
may be `blocked-rights`"). It is research only: no product implementation, write
support, exclusion, coverage percentage, denominator freeze or package publication is
authorized here. Exiv2 0.28.9 (`b0859fc61fea5dedfc898aece6b15893ddf4314e`) is the
pinned external black-box reference; ExifTool 12.76 is secondary, not a normative
oracle.

## What was reviewed, and it is deliberately narrow

Every layout statement below is a **transcription of the pinned reference
implementation's own header readers**, not a vendor format specification and not a
copy of any vendor sample:

* `TiffHeaderBase::read` (`src/tiffimage_int.cpp` at the pin) — accepts `II`/`MM`, then a
  uint16 at bytes 2-3 that must equal 42, with 444 (JPEG-XR) and 17234 (DCP) also
  accepted, and takes the root IFD offset from bytes 4-7.
* `ImageFactory::open` (`src/image.cpp`) — dispatches by scanning the `registry[]` table
  in order and returning the **first** match. `ImageType::arw`, `::nef`, `::pef`, `::srw`
  (and B6's `::dng`) all bind `isTiffType`; only `::cr2` and `::orf` bind distinct header
  checks (`isCr2Type`, `isOrfType`). The file **extension is not consulted** on the
  open path, so a plain-TIFF-header file is read by the generic TIFF reader whatever it
  is called.
* `TiffImage::mimeType` (`src/tiffimage.cpp`) — maps the `Compression` tag to a vendor
  MIME type: 32767 `image/x-sony-arw`, 34713 `image/x-nikon-nef`, 32770
  `image/x-samsung-srw`, 65535 `image/x-pentax-pef` (32769 Epson ERF and 65000 Kodak DCR
  are in the same list).
* `Cr2Header::read` (`src/cr2header_int.cpp`) — 16-byte header, tag 42 at bytes 2-3, root
  IFD offset at 4-7, the literal signature `CR\2\0` at 8-11 and `offset2` at 12-15; the
  constructor defaults are `TiffHeaderBase(42, 16, byteOrder, 0x00000010)` with
  `offset2_ = 0`.
* `OrfHeader::read` (`src/orfimage_int.cpp`) — 8-byte header, uint16 at bytes 2-3 equal to
  `0x4f52` (`OR`) or `0x5352` (`SR`, the SP-560UZ allowance), root IFD offset at 4-7;
  constructor `TiffHeaderBase(0x4f52, 8, byteOrder, 0x00000008)`.
* Registry access modes (`src/image.cpp`) — `arw`, `rw2`, `sr2` and `raf` are
  `amRead/amRead/amRead` (Exif/IPTC/XMP), i.e. read-only in this build; `tiff`, `dng`,
  `nef`, `pef`, `srw`, `cr2` and `orf` are `amReadWrite`. **Comment mode is `amNone` for
  every one of the six B9 formats.** Recorded because it explains expected empty comment
  probes; it is not an accepted exclusion and not a MetadataSharp decision.

The pinned CLI's supported file types and actions are the blob
`149f53b809c182bb4145567e28c466965f7f6505` already recorded in
`EXIV2_CAPABILITY_BASELINE.md`; nothing here contradicts it.

**No vendor layout specification was reviewed**, because none was obtained: the private
IFDs that actually carry Sony/Nikon/Olympus/Pentax/Samsung camera metadata are
proprietary and undocumented in a form this project can rely on. That absence is the
finding, and it is what the blocked cells below record. Tag documentation, where it
exists, would describe *tags*; it would not license a fixture nor act as a byte-layout
oracle.

## Per-format decisions

| Format | Fixture decision | Rights and oracle limits |
| --- | --- | --- |
| ARW | **Permit** one owned synthetic marker: standard TIFF header (`II`, 42, root IFD at 8), `Make=SONY`, `Model=B9 SYNTHETIC ARW`, `Compression=32767`, plus the shared owned carriers. | The `Compression` value is a **recognition marker only**: the strip data is authored uncompressed, so this file is not a decodable ARW and not a Sony layout. No Sony private IFD is authored. Metadata-free of vendor layout ⇒ absence of vendor behavior proves nothing. |
| CR2 | **Permit** one owned synthetic marker using the reference header contract verbatim: 16-byte header with `CR\2\0` at 8-11, `offset2=0`, root IFD at 16, `Make=CANON`, `Compression=1`. | Recognized by signature, so dispatch to `Cr2Image` is a real, checkable property. The Canon IFD3 color-metadata block is **not** authored; no CR2 raw image, no Canon MakerNote interpretation. |
| NEF | **Permit** as ARW with `Make=NIKON CORPORATION`, `Compression=34713`. | Nikon maker note and NEF-verification tags are not authored; `Compression` is a marker over uncompressed data. |
| ORF | **Permit** one owned synthetic marker: `II` + `0x4f52` (`IIRO`), root IFD at 8, `Make=OLYMPUS IMAGING CORP.`, `Compression=1`. | Real Olympus directories use offsets relative to the directory start; our fixture uses file-absolute TIFF offsets because that is what the pinned *header* contract reads. A mis-parse or refusal here is a recorded dispatch outcome, **not** evidence about ORF support. Private IFD not authored. |
| PEF | **Permit** as ARW with `Make=PENTAX`, `Compression=65535`. | Pentax maker note and PENTAX/AE sections not authored. |
| SRW | **Permit** as ARW with `Make=SAMSUNG`, `Compression=32770`. | Samsung OP body-data offsets not authored. |
| All six | **No fixture cleared** for the vendor-private layout. The census keeps `raw-<fmt>-vendor-private-unobserved` rows, recorded `blocked`. | Authoring a plausible-looking private IFD would *invent* evidence instead of observing it, and would launder an undocumented layout into the denominator. Pending, not refused, not not-applicable, not excluded. |

## Shared payload decision

The metadata carriers are the payloads this project already owns and pinned in earlier
batches — the B1 JPEG XMP packet writer, the owned IIM keyword records, the owned
minimal ICC profile, the owned single RGB pixel and the owned baseline JPEG thumbnail
body, all reached through `tools/reference/jpeg_owned_fixtures.py`. Authored token set:
`B9Artist` (EXIF Artist), `B9Tool` (EXIF Software), `B9Comment` (EXIF UserComment). No
image data from any camera, no Exiv2 sample, no Exiv2 test file, no upstream fixture
corpus and no third-party tag-dictionary text is copied into a fixture or into a test.

The EXIF-registered XPComment carrier is authored in UTF-16LE, so a stdout token scan
cannot match it — the same property batch B4 recorded for TIFF. That is why the token
list lives in an ASCII carrier instead, and why a comments probe printing nothing is
written as "printed nothing", never as "no comment present in the file".

## General gate for this batch

* Fixtures are regenerated byte-for-byte by `tools/reference/b9_raw_fixtures.py` with the
  standard library only, and `structure()` re-derives header, directory walk, carrier
  payload digests and pool accounting from the bytes; the offline check therefore proves
  the fixture says what it claims without running the pinned CLI, and CI proves what the
  pinned CLI does with it. Those are two different things and the census keeps them
  separate.
* Observation is record-only: exit codes, stdout digests, produced artifacts and
  input-unchanged flags. No row may be read as support, conformance, or a
  MetadataSharp result; `verification` stays `not-assessed` on every B9 row.
* No B9 write suboperation is enumerated. The registry's `amRead` flag for `arw` is
  recorded as a reference-build fact, not as an exclusion, and `amReadWrite` for
  `nef`/`pef`/`srw`/`cr2`/`orf` authorizes nothing here: no writable fixture is cleared
  and DNG D2 write remains outside authorization.
* Metadata present in a synthetic marker file cannot prove metadata-present reading of a
  real capture, and the reverse is equally true: absence here proves nothing about
  vendor support. CR3 (batch B7) stays blocked, and B10's RW2/SR2/MRW/RAF stay
  untouched: `isRw2Type`/`isRafType`/`isMrwType` dispatch is wave 2 work.
