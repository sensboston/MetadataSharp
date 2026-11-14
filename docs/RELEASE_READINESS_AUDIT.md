# Release-readiness audit: Exiv2 0.28.9 and MetadataSharp

**Reference:** Exiv2 v0.28.9, commit [`b0859fc61fea5dedfc898aece6b15893ddf4314e`](https://github.com/Exiv2/exiv2/tree/b0859fc61fea5dedfc898aece6b15893ddf4314e). This is a comparison of documented and source capabilities of the two libraries, not a claim of complete behavioural equivalence. The upstream format table is in [`exiv2.md`, “3 FILE TYPES”](https://github.com/Exiv2/exiv2/blob/b0859fc61fea5dedfc898aece6b15893ddf4314e/exiv2.md#3-file-types), the factory modes are in [`src/image.cpp`](https://github.com/Exiv2/exiv2/blob/b0859fc61fea5dedfc898aece6b15893ddf4314e/src/image.cpp), and the action/option reference is in [`exiv2.md`](https://github.com/Exiv2/exiv2/blob/b0859fc61fea5dedfc898aece6b15893ddf4314e/exiv2.md). MetadataSharp evidence is linked to its source and tests below. `MetadataCapabilities` is the executable, test-pinned contract for container/family/operation status; a missing row means `NotSupported` by contract.

The subject of this document is the **library**: what it reads, what it writes and where it refuses. The Exiv2 *command line* — its actions, `-p`/`-P` print modes, target DSL and option layer — is a separate piece of work on `samples/MetaTool`, tracked in [the MetaTool CLI compatibility task](usage/METATOOL_CLI_PARITY.md). Nothing here is a claim that the sample tool can replace the `exiv2` executable.

“Read” means the specified representation is exposed, not that every malformed or vendor-specific value is decoded. “Write” covers the operation shown, not equivalence of Exiv2's permissive rewriting. MetadataSharp rejects structures that cannot be bounded or preserved safely. The status `partial` is intentional whenever an operation has format, layout, or value constraints.

## Format factory and metadata families

Exiv2's rows below reproduce its documented file-type table. `R/W` and `R` describe Exiv2's documented family support; `—` means no support is listed. The Exiv2 preview column is its thumbnail column, not a guarantee that every native preview loader applies to that format. Exiv2's BMFF and video readers are build-conditional as documented in that same section.

| Exiv2 format | EXIF | IPTC | XMP | Comment | ICC | Thumbnail | MetadataSharp mapping and disposition |
|---|---:|---:|---:|---:|---:|---:|---|
| ARW | R/W | R/W | R/W | — | R/W | R/W | TIFF family, read and restricted editing; capability rows `Arw` |
| AVIF | R | R | R | — | — | R | BMFF reader; restricted metadata writes; capability rows `Avif` |
| BMP | — | — | — | — | — | — | Recognizes BMP and dimensions only |
| CR2 | R/W | R/W | R/W | — | R/W | R/W | TIFF family, read and restricted editing; `Cr2` |
| CR3 | R | R | R | — | — | R | Separate BMFF/CR3 parser; EXIF, IPTC and XMP read, not a general Exiv2-style writer |
| CRW | R/W | — | — | R/W | — | R/W | CRW/CIFF parser and limited EXIF/comment writer; per-file MakerNote and preview details are in `docs/usage/CRW_MRW_RAF_USAGE_AND_LIMITS.md` |
| DCP | R/W | — | — | — | — | — | DCP TIFF profile recognized by magic `0x4352`; MetadataSharp uses bounded TIFF EXIF reading and restricted EXIF editing, without DCP color-transform semantics |
| DNG | R/W | R/W | R/W | — | R/W | R/W | TIFF family, read and restricted editing; capability rows `Dng` |
| EPS | — | — | R/W | — | — | — | EPS/XMP support; partial on malformed or unsupported packet layouts |
| EXV | R/W | R/W | R/W | R/W | R/W | R/W | EXV metadata reader/writer; general ICC/comment/preview APIs apply only where their scanner recognizes the representation |
| GIF | — | — | — | — | — | — | Recognizes GIF and dimensions only |
| HEIC | R | R | R | — | — | R | BMFF reader; limited read/write for EXIF, XMP and ICC; capability rows `Heic` |
| HEIF | R | R | R | — | — | R | BMFF reader; limited read/write for EXIF, XMP and ICC; capability rows `Heif` |
| JP2 | R/W | R/W | R/W | — | R/W | R/W | `Jp2Metadata` reads recognized EXIF/IPTC/XMP UUID boxes and writes complete-set UUID/ICC boxes; no JP2 preview extraction |
| JPEG | R/W | R/W | R/W | R/W | R/W | R/W | Broad reader and family-specific writers; operation limits are in the `Jpeg` capability rows |
| JXL | R | R | R | — | — | R | JPEG XL reader; EXIF/XMP only to the extent represented by the current parser/capabilities; no writer |
| MRW | R | R | R | — | — | R | MRW reader; read-only and partial by capability rows `Mrw` |
| NEF | R/W | R/W | R/W | — | R/W | R/W | TIFF family, read and restricted editing; capability rows `Nef` |
| ORF | R/W | R/W | R/W | — | — | R/W | TIFF family, read and restricted editing; capability rows `Orf` |
| PEF | R/W | R/W | R/W | — | R/W | R/W | TIFF family, read and restricted editing; capability rows `Pef` |
| PGF | R/W | R/W | R/W | R/W | — | — | PGF/carrier reader and limited writes; no general native preview support |
| PNG | — | R/W | R/W | — | R/W | R/W | EXIF, IPTC, XMP, ICC and comment APIs are implemented with format-specific limits; capability rows `Png` |
| PSD | R/W | R/W | R/W | — | — | R/W | `PsdMetadata.Write` writes editable EXIF/IPTC/XMP/ICC resources and a JPEG thumbnail as a complete set; generic stream/path document operations remain absent |
| RAF | R | R | R | — | — | R | RAF reader; read-only and partial by capability rows `Raf` |
| RW2 | R | R | R | — | — | R | TIFF family; read plus limited EXIF replace/remove, no general insert; `Rw2` |
| SR2 | R | R | R | — | — | R | TIFF family; limited read by `Tiff` reader, no separate full SR2 writer contract |
| SRW | R/W | R/W | R/W | — | — | R/W | TIFF family; supported where the TIFF-based structure satisfies the safe writer contract |
| TGA | — | — | — | — | — | — | Recognizes TGA (path detection or version-2 footer) and dimensions only |
| TIFF | R/W | R/W | R/W | — | R/W | R/W | TIFF-based reader/writer; structural constraints and preservation rules in both standalone TIFF usage documents |
| WEBP | R/W | — | R/W | — | R/W | R/W | EXIF, XMP and ICC read/write with format-specific limits; no IPTC or comment family writer |
| XMP | — | — | R/W | — | — | — | XMP sidecar read/write as a packet; no EXIF/IPTC conversion is implicit |
| ASF / Matroska / QuickTime / RIFF video | Read-only, build-dependent | Read-only, build-dependent | Read-only, build-dependent | — | — | — | Read-only properties with bounded parsers; capability rows `Asf`, `Matroska`, `QuickTime`, `Riff` |

**Registry differences:** the MetadataSharp factory is `MetadataFormat` plus `ImageMetadata.Detect`/`Compose` in `src/MetadataSharp.Formats/MetadataFormat.cs` and `ImageMetadata.cs`. A TIFF signature intentionally groups TIFF-based raw formats; signatures do not reliably distinguish a camera raw from TIFF. MetadataSharp recognizes additional read paths (including video and JXL) and now separately registers the DCP TIFF profile signature. The exact MetadataSharp operation rows, including `Partial` versus `Supported`, are `src/MetadataSharp.Core/MetadataCapabilities.cs`; supporting tests are under `tests/MetadataSharp.Tests/{Capabilities,Containers,Formats,Video}`. No upstream optional-library switch is enabled in MetadataSharp: the managed parsers are part of the build.

### MetadataSharp family coverage

This matrix describes reader paths in `ImageMetadata.Compose` and writer APIs, not an Exiv2 CLI. `RW*` means read and restricted writing, as described in `MetadataCapabilities`; `R` means a reader exists but no writer is declared; `—` means no reader/writer for that family. The last column is the generic `Previews` API (`L` = list; `L/E*` = list and extract supported media types, including encoded items copied without decoding). ICC `R` means profile bytes are returned, not interpreted.

| MetadataSharp container | EXIF | IPTC | XMP | Comment | ICC | Preview / thumbnail | Source/test evidence |
|---|---|---|---|---|---|---|---|
| JPEG | RW* | RW* | RW* | RW | RW | L/E*; EXIF thumbnail set/remove | `ImageMetadata`, `JpegMetadataDocument`; tests `Jpeg`, `Previews`, `Icc`, `Comments` |
| PNG | RW* | RW* | RW* | RW | RW | L/E*; EXIF thumbnail set/remove | `PngMetadataDocument`; tests `Png`, `Previews`, `Icc`, `Comments` |
| WebP | RW* | — | RW* | — | RW | L/E*; EXIF thumbnail set/remove | `WebPMetadataDocument`; tests `WebP`, `Previews`, `Icc` |
| TIFF | RW* | RW* | RW* | — | RW* | L/E*; EXIF thumbnail set/remove | `TiffBasedMetadataDocument`; tests `Tiff`, `Previews` |
| DNG | RW* | RW* | RW* | — | RW* | L/E*; EXIF thumbnail set/remove | `Dng` capability rows; ICC insertion only for a proven un-reduced, uncompressed RGB primary with an accounted strip and separate CFA SubIFD |
| DCP | RW* | R | R | — | — | — | `MetadataFormat.Dcp`; `0x4352` TIFF profile identifier, bounded TIFF EXIF/IPTC/XMP reads and restricted edits; effective writes use classic TIFF magic; DCP color semantics are not interpreted |
| ARW, CR2, NEF, ORF, PEF | RW* | RW* | RW* | — | R | L/E*; EXIF thumbnail set/remove | Matching container rows in `MetadataCapabilities`; TIFF-based tests and raw fixtures |
| RW2 | R / replace / remove* | R | R | — | R | L/E*; EXIF thumbnail set/remove | `Rw2` capability rows include TIFF EXIF/IPTC/XMP readers; EXIF writing is restricted to replace/remove |
| SR2, SRW | R | R | R | — | R | L/E*; EXIF thumbnail set/remove | TIFF signature path and explicit EXIF/IPTC/XMP read rows; ICC extract is partial |
| CRW | RW* | — | — | RW | — | L/E JPEG CIFF preview and thumbnail ranges | `Previews`; `CrwMetadata`, `CrwMetadataDocument`; tests `Crw`, `Previews` |
| MRW | R | R | R | — | — | L/E JPEG/TIFF previews through TTW | `Previews`; `MrwMetadata`; `Mrw` rows, `RawContainers` and `Previews` tests |
| RAF | R | R | R | — | — | L/E embedded JPEG and TIFF previews | `Previews`; `RafMetadata`; `Raf` rows, `RawContainers` and `Previews` tests |
| CR3 | R | R | R | — | — | L/E JPEG and encoded HEVC/AV1 thumbnail item bytes | `Cr3Metadata`; read-only EXIF/IPTC-from-CMT/XMP capability rows and `Previews` tests |
| AVIF, HEIC, HEIF | RW* | R | RW* | — | RW* | L/E encoded HEVC/AV1 item bytes; no decode | BMFF family rows include read-only IPTC from TIFF Exif items; tests `Bmff`, `BmffIcc`, `Previews` |
| PSD | RW* | RW* | RW* | — | R | L/E*; JPEG thumbnail resource write | `PsdMetadata.Write` rewrites editable EXIF/IPTC/XMP/ICC resources and a JPEG thumbnail as a complete set; tests `Psd` and `Previews` pin resource preservation and read-back |
| JP2 | RW* | RW* | RW* | — | R | L/E JPEG in Exif UUID boxes | `Jp2Metadata.Read`/`Write` reads and writes recognized UUID metadata boxes and ICC `colr` children; tests `Jp2`, `Icc`, `Previews`; opaque XML boxes and codestream bytes are preserved |
| JXL | R | R | R | — | — | — | `JxlMetadata`; read-only IPTC from the TIFF Exif box; capability rows and tests; no writer |
| EXV | R | R | R | RW | R | L/E* where the EXV segment scanner finds a supported range | `ExvMetadata`, `ExvFile`, `IccProfiles`, `Comments`, `Previews`; explicit EXIF/IPTC/XMP read rows; creation can include ICC but is not an in-place writer |
| EPS | — | — | RW* | — | — | L/E native WMF/TIFF/AI7/Photoshop previews | `EpsMetadataDocument`; `Previews`; `Eps` capability rows and tests |
| PGF | RW* | RW* | RW* | RW* (carrier-specific) | — | L/E embedded image previews | `PgfMetadata`; `Pgf` capability rows; EPS/PGF and `Previews` tests |
| XMP sidecar | — | — | RW* | — | — | L/E embedded xmpGImg/xapGImg JPEG thumbnail | `XmpPacket`, `Previews` and sidecar operations; tests `Xmp`, `Operations`, `Previews` |
| GIF, BMP, TGA | — | — | — | — | — | — | Dimension recognition only; `SmallFormats` tests |
| ASF, Matroska, QuickTime, RIFF | — | — | — | — | — | — | Separate `VideoMetadata` properties only; read-only `Video` capability rows and tests |

PSD, JP2, EXV, DCP and TIFF-based RAW metadata read paths are represented by test-pinned capability rows. Preview extraction is a separate API with a per-media byte contract; it does not imply decoder support.

**Previews and thumbnails:** `Previews.List` and `Previews.Extract` cover the bounded Exiv2 0.28.9 loader families integrated for this group: TIFF/EXIF and MakerNote pointers, JPEG/EXV, PSD, BMFF thumbnail items, MRW TTW, RAF embedded JPEG/TIFF, CRW CIFF, JP2 Exif UUID, PGF embedded images, EPS WMF/TIFF/AI7/Photoshop, PNG compressed Raw EXIF profiles/XMP and XMP JPEG thumbnails. JPEG streams are validated; TIFF, WMF, PNM and encoded HEVC/AV1 are returned without decoding, while reduced uncompressed TIFF previews are re-encoded as standalone TIFF. AI7, XMP base64 and PNG compressed Raw EXIF sources are transformed on extraction. `ExifEdit.SetThumbnail`/`RemoveThumbnail` update an existing EXIF thumbnail directory; PSD's complete-set writer handles its JPEG thumbnail resource. There is no generic preview replacement API. See [Preview usage and limits](usage/PREVIEWS_USAGE_AND_LIMITS.md), [`Previews.cs`](../src/MetadataSharp.Formats/Previews.cs), [`PreviewTests.cs`](../tests/MetadataSharp.Tests/Containers/PreviewTests.cs), and the pinned differential harness [`run_previews.py`](../tools/parity/run_previews.py). The pinned `exiv2 -pp` reference text does not include offsets, so the dynamic gate also runs pinned `exiv2 -ep`, reverses direct, EPS-hex, XMP-base64, PNG compressed Raw EXIF and generated-TIFF output bytes to source positions, and compares those positions with the dump's absolute offsets. An unprovable or mismatched offset, any preview-list difference or reader refusal fails parity.

## Actions, print modes and data targets

Not part of this audit. The library operations those Exiv2 actions and options drive are in the table
below and in [operations usage and limits](usage/OPERATIONS_USAGE_AND_LIMITS.md); the command-line
compatibility work, with the full option inventory and the data-target DSL, is
[the MetaTool CLI compatibility task](usage/METATOOL_CLI_PARITY.md).

## Library functionality

| Feature | Exiv2 0.28.9 | MetadataSharp | Status | Evidence |
|---|---|---|---|---|
| Tag tables and print conversions | Broad Exif/TIFF, IPTC, XMP and vendor group tables; localized/interpreted values and raw forms | `MetadataTagCatalog` records Exiv2 0.28.9 Exif/TIFF and MakerNote group/tag identities plus XMP namespace/property names and prefix aliases; IPTC dataset definitions expose local names, aliases, selected coded meanings and local descriptions. The pinned `-pt` gate is green: the whole corpus (the pinned upstream files, this repository's fixtures and the CC0 raw corpus) compared by the reference's print columns, no differing row; the only row rule left is the reference's XMP sidecar conversion, and the exact code-to-name pairs where the vocabularies differ are named in `docs/usage/TAG_PRINT_REFUSAL_CATALOG.md`. Image directories named by tag 0x014A, a repeated MakerNote tag ID and an entry of an unknown field type are read and compared row by row | Partial | Exiv2 `include/exiv2/{tags.hpp,iptc.hpp,properties.hpp,xmp_exiv2.hpp}`; `MetadataTagCatalog`, `ExifTags`, `IptcDatasets`, `XmpPacket`, MakerNotes; `tools/parity/run_tag_print.py` over the pinned corpus and the CC0 raw corpus |
| XMP data model | Standard properties, arrays, structures, qualifiers, language alternatives, namespace registry and custom properties | RDF/XML tree, path-based edits, arrays/structures/qualifiers, namespace registration and unknown-property retention within safety limits | Partial | Exiv2 `include/exiv2/properties.hpp`, `src/xmp.cpp`; `src/MetadataSharp.Core/Xmp`; tests `Xmp`; differences on unrecognized XML/RDF constructions are refused or retained raw rather than normalized |
| MakerNote groups and values | Broad camera-specific decoding, printing and writing | Recognized Canon, Nikon, Fujifilm, Pentax, Sony, Panasonic and Olympus/OM System groups; raw block retained; named-value edits are in place and read-back verified | Partial | `src/MetadataSharp.Core/MakerNotes`; MakerNote tests and pinned differential jobs. Vendor/tag coverage remains incomplete |
| Cross-family conversions | Exiv2 has selected Exif/IPTC/XMP conversions and CLI sidecar conversion | Explicit Metadata Working Group sync/reconcile APIs and operations; only mapped fields are converted, with conflicts/options documented | Partial | `src/MetadataSharp.Core/Sync`, `MetadataOperations`; `tests/MetadataSharp.Tests/Sync`; not a promise to convert every tag |
| Character sets | Exiv2 decodes and writes according to its Exif/IPTC/XMP policies and CLI charset option | IPTC display decoding selects ASCII, valid UTF-8 or ISO-8859-1 fallback packet-wide; explicit UTF-8 remains strict. EXIF `UserComment` accepts an optional caller-provided `Encoding` for `-n`-style decoding, and a body without a printable character is left to its raw bytes, as the reference does. Write conversion remains conservative; the end-to-end charset matrix is still unverified | Partial | `docs/usage/IPTC_USAGE_OWNERSHIP_AND_PRESERVATION_LIMITS.md`, `ExifInterpretationOptions`, IPTC/EXIF codec tests; full legacy charset and writer parity remains open |
| Preview / thumbnail APIs | `PreviewManager` delegates to format-specific loaders; extraction by selected preview and thumbnail edits | Bounded loaders for the Exiv2 source families; direct encoded/native bytes and supported generated thumbnail forms can be extracted; existing EXIF thumbnail edit/remove and PSD resource write | Partial | `include/exiv2/preview.hpp`, `src/preview.cpp`; `Previews.cs`, `ExifEdit`, `PsdMetadata`; usage and tests linked above |
| Sidecars and Extended XMP | XMP sidecars, EXV, converted/raw extraction/insertion, JPEG Extended XMP | XMP sidecar packets, EXV read/create/extract/insert APIs and JPEG Extended XMP read/write within packet limits; conversions are explicit | Partial | Exiv2 `app/actions.cpp`, `src/image.cpp`; `MetadataOperations`, `ExvFile`, JPEG XMP code; no compatible all-target CLI/data-flow |
| BMFF and video | Build-conditional BMFF and read-only video containers; format handlers vary by build | CR3 and HEIC/HEIF/AVIF metadata readers, limited safe edits where declared; bounded read-only video property readers for ASF, Matroska, QuickTime and RIFF | Partial | Exiv2 `src/image.cpp` conditional registry and `exiv2.md` build note; `src/MetadataSharp.Formats/Bmff`, `VideoContainerReader.cs`, tests `Bmff`/`Video`; no general video writer |
| Safety limits and preservation | Exiv2 validates format structures; behavior and error policy differ by handler | Explicit byte/count/depth budgets, checked ranges, preservation-oriented writers, read-back verification and typed `MetadataErrorCode`; unsupported/unsafe edits are refused | Different by design | `MetadataLimits`, `MetadataErrorCode`, capability notes, format usage contracts and hostile-input tests; compare per-operation contracts, not permissive write parity |

## What the widened RAW corpus found

The corpus described in [build and test](BUILD_AND_TEST.md) added 23 camera raw files from raw.pixls.us
(CC0 1.0), two cameras per raw format. Reading them against the pinned 0.28.9 binary changed the picture
this document had: 352 files and 34 764 matched rows, with 13 raw files still failing, and the failures
group into six reader gaps that the one-to-three-file-per-format corpus could not show.

1. ~~**RW2 is not read as a Panasonic raw.**~~ **Closed.** In a `.RW2` the first directory is the
   `PanasonicRaw` directory, the file names no directory after it, and its `ExifTag` leads to the EXIF
   directory; the `Exif.Image.*` and `Exif.Thumbnail.*` rows the reference prints come from the JPEG the
   file embeds. The read now stops at the container's own directories, the print names them
   `PanasonicRaw`, and the embedded picture's metadata is available as
   [`EmbeddedImageMetadata`](usage/CRW_MRW_RAF_USAGE_AND_LIMITS.md#the-pictures-a-raw-container-embeds)
   and merged by the print the way the reference merges it.
2. **A raw file with more than one top-level image directory loses the later ones.** Canon CR2 keeps the
   raw image in the second directory and a second reduced image in the third, Pentax PEF keeps its
   preview in the second; those entries (`Exif.Image2.*`, `Exif.Image3.*`) are missing while the first
   directory matches row for row.
3. **Samsung SRW preview directories are not synthesized.** Exiv2 publishes `Exif.SamsungPreview.*` and
   `Exif.SubThumb1.*` for the pointers inside a Samsung note; nothing is reported for the NX10 and NX100
   files here.
4. **Two Sony raws are refused outright.** The A100 `.ARW` stops at `LimitExceeded`, the A700 `.ARW` and
   the R1 `.SR2` at `InvalidExif`, where the reference prints their tables. All three are TIFF-based and
   should read with the same code as the other raws.
5. **One MRW preview cannot be placed.** `exiv2 -pp` lists previews for the DiMAGE A1 that the extraction
   oracle cannot locate in the file, so the offset comparison of the preview gate has nothing to compare
   against.
6. **A Fujifilm note copy loses its thumbnail pointers.** Copying the note out of the S2Pro and S3Pro
   files drops `Exif.Image2.JPEGInterchangeFormat` and its length, which the reference keeps.

These are the work items the corpus was added to expose, and they are listed here rather than in an
exception table: `raw-corpus-parity` fails on all six, and it has to pass on its whole corpus before
this document claims the raw read paths are established.

## Open library gaps

Everything below is a real difference from Exiv2 that remains after this PR. Each item states what is
missing, what is already there, and what would close it; none of them is a missing capability row.

1. **MakerNote vendor breadth.** Canon, Nikon, Fujifilm, Pentax, Sony, Panasonic and Olympus/OM System
   groups are decoded and their named-value edits are in-place and read-back verified. Other vendors'
   notes, and unlisted tags inside the decoded ones, are carried as raw blocks and refused when an
   edit would require re-encoding them. Closing it means adding the remaining vendor tag tables and
   their offset/layout rules, vendor by vendor.
2. **Character-set end-to-end matrix.** IPTC display decoding picks ASCII, valid UTF-8 or an
   ISO-8859-1 fallback per packet, and EXIF `UserComment` accepts a caller-supplied `Encoding`;
   write-side conversion stays conservative, and the codepage matrix that Exiv2 supports through
   `-n` (behind its own optional library) has no equivalent table here. The current limits are
   documented in [tag catalog and charset limits](usage/TAG_CATALOG_AND_CHARSET_LIMITS.md) and
   [IPTC usage and preservation limits](usage/IPTC_USAGE_OWNERSHIP_AND_PRESERVATION_LIMITS.md).
3. **Descriptive breadth of the tag catalog.** The printed values match the reference on the pinned
   corpus (see the `-pt` row above); what is thinner is descriptive prose and vendor-group breadth for
   keys the corpus never exercises. The exact code-to-name pairs where the two vocabularies differ are
   named in [the tag print refusal catalog](usage/TAG_PRINT_REFUSAL_CATALOG.md), and that list, not a
   percentage, is the gap.
4. **No generic preview replacement API.** Previews and thumbnails can be listed and extracted for
   every loader family, and an existing EXIF thumbnail can be set or removed; replacing an arbitrary
   preview payload is not exposed, while Exiv2 offers `-ie`/`-it` style insertion per target.
5. **ICC insertion into TIFF-based raw files** is enabled only for the proven DNG layout of an
   un-reduced RGB primary with one accounted strip and a separate CFA SubIFD; other raw layouts are
   fail-closed unless a valid primary tag `0x8773` already provides a replace/remove target. This is a
   deliberate constraint of the preservation contract, not an unimplemented reader.

## Non-goals

- **Lens-ID databases.** Raw lens-identification values are retained; no lens-identification database
  work is planned.
- **Unsafe MakerNote relocation and AVI colour-count handling.** These divergences are kept: the
  library refuses a relocation it cannot prove safe, and preserves the AVI count instead of
  reproducing Exiv2's arithmetic.

## Corpus and acceptance

The corpus is the pinned Exiv2 checkout plus this project's own fixtures plus the CC0 raw camera corpus
in `tools/parity/raw-corpus.json` (see [build and test](../docs/BUILD_AND_TEST.md)); the differential
jobs read all three and their summaries above come from those runs. The full CI workflow is the
acceptance test: both target frameworks, the OS matrix, the test suite, the compiled README examples,
sample builds and smoke tests, the offline policy checks and the pinned Exiv2 differential jobs. A
push-only run is not described as full CI; the final PR description records the final green run.
