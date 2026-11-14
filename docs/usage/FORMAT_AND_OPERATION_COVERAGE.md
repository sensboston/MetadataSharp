# Format and operation coverage

**Reference:** Exiv2 v0.28.9, commit [`b0859fc61fea5dedfc898aece6b15893ddf4314e`](https://github.com/Exiv2/exiv2/tree/b0859fc61fea5dedfc898aece6b15893ddf4314e). This is a comparison of documented and source capabilities of the two libraries, not a claim of complete behavioural equivalence. The upstream format table is in [`exiv2.md`, "3 FILE TYPES"](https://github.com/Exiv2/exiv2/blob/b0859fc61fea5dedfc898aece6b15893ddf4314e/exiv2.md#3-file-types), the factory modes are in [`src/image.cpp`](https://github.com/Exiv2/exiv2/blob/b0859fc61fea5dedfc898aece6b15893ddf4314e/src/image.cpp), and the action/option reference is in [`exiv2.md`](https://github.com/Exiv2/exiv2/blob/b0859fc61fea5dedfc898aece6b15893ddf4314e/exiv2.md). MetadataSharp evidence is linked to its source and tests below. `MetadataCapabilities` is the executable, test-pinned contract for container/family/operation status; a missing row means `NotSupported` by contract.

The subject of this document is the **library**: what it reads, what it writes and where it refuses.
The Exiv2 *command line* - its actions, `-p`/`-P` print modes, target DSL and option layer - is a
separate piece of work on `samples/MetaTool`, tracked in [the MetaTool CLI compatibility
task](METATOOL_CLI_PARITY.md). Nothing here is a claim that the sample tool can replace the `exiv2`
executable.

"Read" means the specified representation is exposed, not that every malformed or vendor-specific
value is decoded. "Write" covers the operation shown, not equivalence of Exiv2's permissive
rewriting. MetadataSharp rejects structures that cannot be bounded or preserved safely. The status
`partial` is intentional whenever an operation has format, layout, or value constraints. What is
deliberately *not* implemented is listed under [non-goals](#non-goals), and it is a constraint of the
preservation contract rather than an open work item.


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

**Previews and thumbnails:** `Previews.List` and `Previews.Extract` cover the bounded Exiv2 0.28.9 loader families integrated for this group: TIFF/EXIF and MakerNote pointers, JPEG/EXV, PSD, BMFF thumbnail items, MRW TTW, RAF embedded JPEG/TIFF, CRW CIFF, JP2 Exif UUID, PGF embedded images, EPS WMF/TIFF/AI7/Photoshop, PNG compressed Raw EXIF profiles/XMP and XMP JPEG thumbnails. JPEG streams are validated; TIFF, WMF, PNM and encoded HEVC/AV1 are returned without decoding, while reduced uncompressed TIFF previews are re-encoded as standalone TIFF. AI7, XMP base64 and PNG compressed Raw EXIF sources are transformed on extraction. `ExifEdit.SetThumbnail`/`RemoveThumbnail` update an existing EXIF thumbnail directory; PSD's complete-set writer handles its JPEG thumbnail resource. There is no generic preview replacement API. See [Preview usage and limits](PREVIEWS_USAGE_AND_LIMITS.md), [`Previews.cs`](../../src/MetadataSharp.Formats/Previews.cs), [`PreviewTests.cs`](../../tests/MetadataSharp.Tests/Containers/PreviewTests.cs), and the pinned differential harness [`run_previews.py`](../../tools/parity/run_previews.py). The pinned `exiv2 -pp` reference text does not include offsets, so the dynamic gate also runs pinned `exiv2 -ep`, reverses direct, EPS-hex, XMP-base64, PNG compressed Raw EXIF and generated-TIFF output bytes to source positions, and compares those positions with the dump's absolute offsets. An unprovable or mismatched offset, any preview-list difference or reader refusal fails parity.

**Registry differences:** the MetadataSharp factory is `MetadataFormat` plus `ImageMetadata.Detect`/`Compose` in `src/MetadataSharp.Formats/MetadataFormat.cs` and `ImageMetadata.cs`. A TIFF signature intentionally groups TIFF-based raw formats; signatures do not reliably distinguish a camera raw from TIFF. MetadataSharp recognizes additional read paths (including video and JXL) and now separately registers the DCP TIFF profile signature. The exact MetadataSharp operation rows, including `Partial` versus `Supported`, are `src/MetadataSharp.Core/MetadataCapabilities.cs`; supporting tests are under `tests/MetadataSharp.Tests/{Capabilities,Containers,Formats,Video}`. No upstream optional-library switch is enabled in MetadataSharp: the managed parsers are part of the build.


## Library functionality

| Feature | Exiv2 0.28.9 | MetadataSharp | Status | Evidence |
|---|---|---|---|---|
| Tag tables and print conversions | Broad Exif/TIFF, IPTC, XMP and vendor group tables; localized/interpreted values and raw forms | `MetadataTagCatalog` records Exiv2 0.28.9 Exif/TIFF and MakerNote group/tag identities plus XMP namespace/property names and prefix aliases; IPTC dataset definitions expose local names, aliases, selected coded meanings and local descriptions. The pinned `-pt` gate is green: the whole corpus (the pinned upstream files, this repository's fixtures and the CC0 raw corpus) compared by the reference's print columns, no differing row; the only row rule left is the reference's XMP sidecar conversion, and the exact code-to-name pairs where the vocabularies differ are named in `docs/usage/TAG_PRINT_REFUSAL_CATALOG.md`. Every key of every tag table of the pinned reference is in the catalog - 6 537 keys in 124 groups, none missing, `tools/parity/run_key_catalog.py` - so a name is never the reason a value prints numerically. Image directories named by tag 0x014A, a repeated MakerNote tag ID and an entry of an unknown field type are read and compared row by row | Partial | Exiv2 `include/exiv2/{tags.hpp,iptc.hpp,properties.hpp,xmp_exiv2.hpp}`; `MetadataTagCatalog`, `ExifTags`, `IptcDatasets`, `XmpPacket`, MakerNotes; `tools/parity/run_tag_print.py` over the pinned corpus and the CC0 raw corpus |
| XMP data model | Standard properties, arrays, structures, qualifiers, language alternatives, namespace registry and custom properties | RDF/XML tree, path-based edits, arrays/structures/qualifiers, namespace registration and unknown-property retention within safety limits | Partial | Exiv2 `include/exiv2/properties.hpp`, `src/xmp.cpp`; `src/MetadataSharp.Core/Xmp`; tests `Xmp`; differences on unrecognized XML/RDF constructions are refused or retained raw rather than normalized |
| MakerNote groups and values | Broad camera-specific decoding, printing and writing | Decoded groups: Canon, Nikon 1/2/3, Fujifilm, Pentax (including AOC and DNG), Sony, Panasonic, Olympus/OM System, Minolta/Konica Minolta, Casio, Samsung and Sigma/Foveon. The raw block is retained, and a named value is edited in place - a text of the length the tag has, a longer text the library appends, and a number inside an array-valued tag, which is what the Minolta and Samsung settings arrays need - and every edit is read back by the reference tool | Partial | `src/MetadataSharp.Core/MakerNotes`; `tests/MetadataSharp.Tests/Tiff/MoreVendorMakerNoteTests.cs`; `tools/reference/makernote_parity.py` in both differential jobs. Notes of vendors not listed here are carried raw; that is a [non-goal](#non-goals), and the key catalog covers them all the same |
| Cross-family conversions | Exiv2 has selected Exif/IPTC/XMP conversions and CLI sidecar conversion | Explicit Metadata Working Group sync/reconcile APIs and operations; only mapped fields are converted, with conflicts/options documented | Partial | `src/MetadataSharp.Core/Sync`, `MetadataOperations`; `tests/MetadataSharp.Tests/Sync`; not a promise to convert every tag |
| Character sets | Exiv2 converts text through iconv; its `-n` option names the character set of a `UNICODE` user comment for the `fc` action, and its print sniffs a byte-order mark and otherwise assumes the file's byte order | `MetadataCharsets` is the matrix of the names `-n` accepts - 38 entries from `US-ASCII` and `UTF-8` through the `windows-125x`, `ISO-8859-x`, IBM, KOI8 and East Asian code pages, with their aliases - resolved to the code pages the .NET runtime carries, with no package reference. It is honoured by `MetadataText` for a `UserComment` and the XP tags and by `IptcPacket.GetText(index, charset)`; every lookup is strict, an unreadable byte raises rather than being replaced, and no lookup changes a byte. IPTC display decoding without a name selects ASCII, valid UTF-8 or an ISO-8859-1 fallback packet-wide, and a declared UTF-8 remains strict. A `JIS` comment body is read as its raw bytes and not written, because the reference prints such a body unconverted | Partial | `docs/usage/TAG_CATALOG_AND_CHARSET_LIMITS.md#character-sets`, `MetadataCharsets`, `MetadataText`, `ExifInterpretationOptions`, `IptcPacket.GetText`; `MetadataTextTests`, and `tools/reference/run_charset.py`, which has the pinned binary convert every entry with `-n <name> fc` and compares the text it reports converting |
| Preview / thumbnail APIs | `PreviewManager` delegates to format-specific loaders; extraction by selected preview and thumbnail edits | Bounded loaders for the Exiv2 source families; direct encoded/native bytes and supported generated thumbnail forms can be extracted; existing EXIF thumbnail edit/remove and PSD resource write | Partial | `include/exiv2/preview.hpp`, `src/preview.cpp`; `Previews.cs`, `ExifEdit`, `PsdMetadata`; usage and tests linked above |
| Sidecars and Extended XMP | XMP sidecars, EXV, converted/raw extraction/insertion, JPEG Extended XMP | XMP sidecar packets, EXV read/create/extract/insert APIs and JPEG Extended XMP read/write within packet limits; conversions are explicit | Partial | Exiv2 `app/actions.cpp`, `src/image.cpp`; `MetadataOperations`, `ExvFile`, JPEG XMP code; no compatible all-target CLI/data-flow |
| BMFF and video | Build-conditional BMFF and read-only video containers; format handlers vary by build | CR3 and HEIC/HEIF/AVIF metadata readers, limited safe edits where declared; bounded read-only video property readers for ASF, Matroska, QuickTime and RIFF | Partial | Exiv2 `src/image.cpp` conditional registry and `exiv2.md` build note; `src/MetadataSharp.Formats/Bmff`, `VideoContainerReader.cs`, tests `Bmff`/`Video`; no general video writer |
| Safety limits and preservation | Exiv2 validates format structures; behavior and error policy differ by handler | Explicit byte/count/depth budgets, checked ranges, preservation-oriented writers, read-back verification and typed `MetadataErrorCode`; unsupported/unsafe edits are refused | Different by design | `MetadataLimits`, `MetadataErrorCode`, capability notes, format usage contracts and hostile-input tests; compare per-operation contracts, not permissive write parity |

## What the widened RAW corpus pins

The corpus described in [build and test](../BUILD_AND_TEST.md) adds 23 camera raw files from
raw.pixls.us (CC0 1.0), two cameras per raw format. Its first runs found six reader gaps that a
one-to-three-file-per-format corpus could not show - RW2 read as a plain TIFF, raw files with several
top-level image directories, Samsung SRW preview directories, two refused Sony raws, an unplaceable
MRW preview and a Fujifilm note copy losing its thumbnail pointers - and all six are closed: the
directories a raw container names are read whole, the embedded picture's metadata is available as
[`EmbeddedImageMetadata`](CRW_MRW_RAF_USAGE_AND_LIMITS.md#the-pictures-a-raw-container-embeds), and
the print merges it the way the reference does. The gate records one difference of its own: the
640x480 preview of `minolta-a2-pict0881.mrw` is listed here and not by the reference, with the reason
the reference misses it. No reader gap is open.


## Non-goals

- **Lens-ID databases.** Raw lens-identification values are retained; no lens-identification database
  work is planned, although Exiv2 has them.
- **Unsafe MakerNote relocation and AVI colour-count handling.** These divergences are kept: the
  library refuses a relocation it cannot prove safe, and preserves the AVI count instead of
  reproducing Exiv2's arithmetic.
- **A generic preview replacement API.** Previews and thumbnails can be listed and extracted for
  every loader family, and an existing EXIF thumbnail can be set or removed; replacing an arbitrary
  preview payload is not exposed. Exiv2 has no such operation either: its two insertion paths are
  `-ie` (a preview out of an EXV sidecar or metadata copy) and `-it` (a JPEG thumbnail read from
  `<path>-thumb.jpg`), and both are per-target actions of its command line rather than a library
  operation on an arbitrary preview. See [preview usage and limits](PREVIEWS_USAGE_AND_LIMITS.md).
- **ICC insertion into a raw TIFF that is not the proven DNG layout.** A profile is written into an
  un-reduced RGB primary with one accounted strip and a separate CFA SubIFD; another raw layout is
  fail-closed unless a valid primary tag `0x8773` already provides a replace/remove target. The
  reason is the preservation contract: a strip or tile layout the writer cannot account for byte for
  byte is refused rather than rewritten.
- **Notes of vendors the library does not decode, and tags a decoded note does not list.** They are
  carried as raw blocks, named by the tag catalog where the reference has a name for them, and
  refused when an edit would require re-encoding them. The catalog holds every key of the pinned
  reference, so this is a limit of decoding, not of naming - see [tag catalog and charset
  limits](TAG_CATALOG_AND_CHARSET_LIMITS.md).
- **Writing or decoding a `JIS` `UserComment`.** The reference prints such a body's bytes without
  converting them; producing text here would not be what that tool shows. The raw value stays
  available.
- **Converting an existing value into another character set.** `-n` and `GetText(index, charset)`
  select how bytes are *read*; no writer converts a value the file already holds.


## Corpus and acceptance

The corpus is the pinned Exiv2 checkout plus this project's own fixtures plus the CC0 raw camera corpus
in `tools/parity/raw-corpus.json` (see [build and test](../BUILD_AND_TEST.md)); the differential
jobs read all three and their summaries above come from those runs. The full CI workflow is the
acceptance test: both target frameworks, the OS matrix, the test suite, the compiled README examples,
sample builds and smoke tests, the offline policy checks and the pinned Exiv2 differential jobs. A
push-only run is not described as full CI; the final PR description records the final green run.
