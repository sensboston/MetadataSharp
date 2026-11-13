# Release-readiness audit: Exiv2 0.28.9 and MetadataSharp

**Reference:** Exiv2 v0.28.9, commit [`b0859fc61fea5dedfc898aece6b15893ddf4314e`](https://github.com/Exiv2/exiv2/tree/b0859fc61fea5dedfc898aece6b15893ddf4314e). This is a comparison of documented/source capabilities, not a claim of complete behavioural equivalence. The upstream format table is in [`exiv2.md`, “3 FILE TYPES”](https://github.com/Exiv2/exiv2/blob/b0859fc61fea5dedfc898aece6b15893ddf4314e/exiv2.md#3-file-types), the factory modes are in [`src/image.cpp`](https://github.com/Exiv2/exiv2/blob/b0859fc61fea5dedfc898aece6b15893ddf4314e/src/image.cpp), and the action/option reference is in [`exiv2.md`](https://github.com/Exiv2/exiv2/blob/b0859fc61fea5dedfc898aece6b15893ddf4314e/exiv2.md). MetadataSharp evidence is linked to its source and tests below. `MetadataCapabilities` is the executable, test-pinned contract for container/family/operation status; a missing row means `NotSupported` by contract.

“Read” means the specified representation is exposed, not that every malformed or vendor-specific value is decoded. “Write” covers the operation shown, not equivalence of Exiv2's permissive rewriting. MetadataSharp rejects structures that cannot be bounded or preserved safely. The status `partial` is intentional whenever an operation has format, layout, or value constraints.

## Format factory and metadata families

Exiv2's rows below reproduce its documented file-type table. `R/W` and `R` describe Exiv2's documented family support; `—` means no support is listed. The Exiv2 preview column is its thumbnail column, not a guarantee that every native preview loader applies to that format. Exiv2's BMFF and video readers are build-conditional as documented in that same section.

| Exiv2 format | EXIF | IPTC | XMP | Comment | ICC | Thumbnail | MetadataSharp mapping and disposition |
|---|---:|---:|---:|---:|---:|---:|---|
| ARW | R/W | R/W | R/W | — | R/W | R/W | TIFF family, read and restricted editing; capability rows `Arw` |
| AVIF | R | R | R | — | — | R | BMFF reader; restricted metadata writes; capability rows `Avif` |
| BMP | — | — | — | — | — | — | Recognizes BMP and dimensions only |
| CR2 | R/W | R/W | R/W | — | R/W | R/W | TIFF family, read and restricted editing; `Cr2` |
| CR3 | R | R | R | — | — | R | Separate BMFF/CR3 parser; EXIF and XMP read, not a general Exiv2-style writer |
| CRW | R/W | — | — | R/W | — | R/W | CRW/CIFF parser and limited EXIF/comment writer; per-file MakerNote and preview details are in `docs/usage/CRW_MRW_RAF_USAGE_AND_LIMITS.md` |
| DCP | R/W | — | — | — | — | — | **Missing:** no DCP handler or DCP-specific recognition |
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

**Registry differences:** the MetadataSharp factory is `MetadataFormat` plus `ImageMetadata.Detect`/`Compose` in `src/MetadataSharp.Formats/MetadataFormat.cs` and `ImageMetadata.cs`. A TIFF signature intentionally groups TIFF-based raw formats; signatures do not reliably distinguish a camera raw from TIFF. MetadataSharp recognizes additional read paths (including video and JXL) but does not register DCP. The exact MetadataSharp operation rows, including `Partial` versus `Supported`, are `src/MetadataSharp.Core/MetadataCapabilities.cs`; supporting tests are under `tests/MetadataSharp.Tests/{Capabilities,Containers,Formats,Video}`. No upstream optional-library switch is enabled in MetadataSharp: the managed parsers are part of the build.

### MetadataSharp family coverage

This matrix describes reader paths in `ImageMetadata.Compose` and writer APIs, not an Exiv2 CLI. `RW*` means read and restricted writing, as described in `MetadataCapabilities`; `R` means a reader exists but no writer is declared; `R†` marks a reader currently absent from the capability manifest, so `GetStatus` incorrectly reports `NotSupported`; `—` means no reader/writer for that family. The last column is the generic `Previews` API (`L` = list; `L/E*` = list and extract supported media types; `L` alone may include coded media that `Extract` refuses). ICC `R` means profile bytes are returned, not interpreted.

| MetadataSharp container | EXIF | IPTC | XMP | Comment | ICC | Preview / thumbnail | Source/test evidence |
|---|---|---|---|---|---|---|---|
| JPEG | RW* | RW* | RW* | RW | RW | L/E*; EXIF thumbnail set/remove | `ImageMetadata`, `JpegMetadataDocument`; tests `Jpeg`, `Previews`, `Icc`, `Comments` |
| PNG | RW* | RW* | RW* | RW | RW | L/E*; EXIF thumbnail set/remove | `PngMetadataDocument`; tests `Png`, `Previews`, `Icc`, `Comments` |
| WebP | RW* | — | RW* | — | RW | L/E*; EXIF thumbnail set/remove | `WebPMetadataDocument`; tests `WebP`, `Previews`, `Icc` |
| TIFF | RW* | RW* | RW* | — | RW* | L/E*; EXIF thumbnail set/remove | `TiffBasedMetadataDocument`; tests `Tiff`, `Previews` |
| DNG | RW* | RW* | RW* | — | R | L/E*; EXIF thumbnail set/remove | `Dng` capability rows; TIFF-based ICC writer intentionally refuses DNG |
| ARW, CR2, NEF, ORF, PEF | RW* | RW* | RW* | — | R | L/E*; EXIF thumbnail set/remove | Matching container rows in `MetadataCapabilities`; TIFF-based tests and raw fixtures |
| RW2 | R / replace / remove* | R† | R† | — | R | L/E*; EXIF thumbnail set/remove | `Rw2` rows restrict EXIF to in-place replace/remove; `TiffExif`/`TiffIptc` readers; no IPTC/XMP rows |
| SR2, SRW | R† | R† | R† | — | R† | L/E*; EXIF thumbnail set/remove | TIFF signature path in `ImageMetadata`; no family-specific capability rows or verified writer contract |
| CRW | RW* | — | — | RW | — | Not listed by generic `Previews`; `CrwMetadata` exposes preview/thumbnail ranges | `CrwMetadata`, `CrwMetadataDocument`; tests `Crw`, `CrwPreview` |
| MRW | R | R | R | — | — | — | `MrwMetadata`; `Mrw` rows and `RawContainers` tests |
| RAF | R | R | R | — | — | — | `RafMetadata`; `Raf` rows and `RawContainers` tests |
| CR3 | R | — | R | — | — | BMFF scanner range listing; media extraction limits apply | `Cr3Metadata`; read-only `Cr3` capability rows and `Cr3` tests |
| AVIF, HEIC, HEIF | RW* | — | RW* | — | RW* | L/E*; coded BMFF previews may only be listed | BMFF family rows; tests `Bmff`, `BmffIcc`, `Previews` |
| PSD | RW* | RW* | RW* | RW* | R | L/E*; JPEG thumbnail resource write | `PsdMetadata.Write` rewrites editable EXIF/IPTC/XMP/ICC resources and a JPEG thumbnail as a complete set; tests `Psd` and `Previews` pin resource preservation and read-back |
| JP2 | RW* | RW* | RW* | RW* | R | — | `Jp2Metadata.Read`/`Write` reads and writes recognized UUID metadata boxes and ICC `colr` children; tests `Jp2`, `Icc`; opaque XML boxes and codestream bytes are preserved |
| JXL | R | — | R | — | — | — | `JxlMetadata`; `Jxl` capability rows and tests; no writer |
| EXV | R† | R† | R† | RW | R | L/E* where the EXV segment scanner finds a supported range | `ExvMetadata`, `ExvFile`, `IccProfiles`, `Comments`, `Previews`; `Exv` tests; creation can include a validated ICC profile as numbered APP2 segments, but is not an in-place writer |
| EPS | — | — | RW* | — | — | — | `EpsMetadataDocument`; `Eps` capability rows and tests |
| PGF | RW* | RW* | RW* | RW* (carrier-specific) | — | — | `PgfMetadata`; `Pgf` capability rows; EPS/PGF tests |
| XMP sidecar | — | — | RW* | — | — | — | `XmpPacket` and sidecar operations; tests `Xmp`, `Operations` |
| GIF, BMP, TGA | — | — | — | — | — | — | Dimension recognition only; `SmallFormats` tests |
| ASF, Matroska, QuickTime, RIFF | — | — | — | — | — | — | Separate `VideoMetadata` properties only; read-only `Video` capability rows and tests |

`†` is a release-readiness defect in the discoverability table, not absence of the parser. `MetadataCapabilities` explicitly promises that absent rows mean `NotSupported`, yet these reader paths are reachable via public APIs. Add test-pinned read rows for PSD, JP2, EXV, and TIFF raw aliases, then make preview/thumbnail extraction discoverable at the format/family granularity the scanner actually supports. **Estimated fix:** 1–2 days for the read rows and exact tests, plus 2–3 days to model preview media-type distinctions without claiming coded BMFF bytes are extractable. Until that change lands, callers must use the reader APIs and source matrix rather than infer parser availability from `GetStatus` for the marked rows.

**Previews and thumbnails:** Exiv2's documented per-format thumbnail column and preview loaders (`include/exiv2/preview.hpp`, `src/preview.cpp`) are broader than a single EXIF thumbnail. MetadataSharp lists file ranges through `Previews.List` and extracts only supported JPEG previews and uncompressed RGB samples through `Previews.Extract`; listed coded BMFF thumbnails are not extractable. Its scanner handles JPEG/EXV segments, TIFF-based files, PNG, WebP, PSD and BMFF structures; it does not expose a format-independent equivalent of every Exiv2 loader (notably MRW/RAF/CRW/JP2 native preview loaders). `ExifEdit.SetThumbnail`/`RemoveThumbnail` edit an existing EXIF thumbnail directory and do not create a directory. Preview extraction coverage and limits are in `src/MetadataSharp.Formats/Previews.cs` and the preview tests. **Gap estimate:** 2–4 weeks for broader loader parity and format-by-format differential fixtures; thumbnail/preview writing is a separate feature, estimated 2–3 weeks.

## Exiv2 actions, print modes, options, and data targets

Exiv2 action names and aliases below are from the pinned `exiv2.md` “4 ACTIONS”; option inventory is from “5 COMMAND SUMMARY” and “6 OPTIONS”. MetadataSharp's `samples/MetaTool/Program.cs` is an illustrative public-API sample, not a compatible replacement for the Exiv2 executable. The underlying library operations are available where indicated.

| Feature | Exiv2 0.28.9 | MetadataSharp | Status | Evidence / note |
|---|---|---|---|---|
| Actions | `pr/print`, `ex/extract`, `in/insert`, `rm/delete`, `ad/adjust`, `mo/modify`, `mv/rename`, `fi/fixiso`, `fc/fixcom` | MetaTool implements a small print/set/delete/rename/extract/insert/fixiso/fixcom/modify subset; library operations cover several more | Partial | `samples/MetaTool/Program.cs`; `MetadataOperations`, `MetadataSync`, document writers; full CLI compatibility estimate 6–10 weeks |
| Print modes `-p` | `s,a,e,t,v,h,i,x,c,p,C,R,S,X` | MetaTool exposes `a,e,i,x,c`; API returns typed snapshots, errors, comments, ICC bytes and preview descriptors, not Exiv2's report/hex/structure formats | Partial | Pinned `exiv2.md` “-p”; `MetaTool.Program.Print`; `ImageMetadata`; report renderer estimate 2–3 weeks |
| Fine print flags `-P` | `E,I,X,x,g,k,l,d,n,y,c,s,v,V,t,h` | Key formatting and typed/raw readers exist; no general Exiv2-compatible column renderer or `-P` flag parser | Missing (CLI) | Pinned `exiv2.md` “-P”; `MetadataKeys`, `ExifTags`, `IptcDatasetNames`, `XmpPacket`; estimate 2–3 weeks |
| Exact and substring filters | `-K key`, `-g str`; repeatable | MetaTool has one case-insensitive substring `-K` option; library exposes metadata entries for caller filtering | Partial | `MetaTool.Program.Print`; estimate 1–2 days for exact/multiple filters and tested CLI contract |
| Delete targets | `a,e,t,i,x,c,C,I,-` | `Delete` removes selected families; EXIF thumbnail and XMP/EXV operations exist; no common CLI target grammar for all Exiv2 targets | Partial | Pinned `exiv2.md` “-d”; `MetadataOperations.Delete`, `ExifEdit.RemoveThumbnail`; full target/stream filter behavior estimate 1–2 weeks |
| Extract targets | `a,e,t,i,x,c,p[n,...],C,X,XX,-`; location, filtering and stdout | `ExtractExv`, `ExtractXmpSidecar`, `ExtractThumbnail`, ICC and previews by APIs; MetaTool only thumbnail/XMP file output | Partial | Pinned `exiv2.md` “-e”; `MetadataOperations`, `IccProfiles`, `Previews`; full extraction CLI estimate 2–3 weeks |
| Insert targets | `a,e,t,i,x,c,C,X,XX,-`; location/suffix and stream filter | EXV/XMP-sidecar insert, metadata-family document saves, ICC save, EXIF thumbnail set; MetaTool only thumbnail/XMP | Partial | Pinned `exiv2.md` “-i”; `MetadataOperations`, `ExifEdit`, document APIs; full insert CLI estimate 2–3 weeks |
| Modify commands | `set`, `add`, `del`, `reg`, typed values, repeated commands and stdin/file scripts | EXIF/IPTC/XMP `ModifyScript` supports safe typed edits and namespace registration, but not every Exiv2 key/type/value behavior or its entire parser/CLI | Partial | Pinned `exiv2.md` “11 MODIFY COMMANDS”; `ModifyScript`, `ExifEdit`, `IptcEdit`, `XmpEdit`; parity additions estimated 2–4 weeks |
| Rename patterns | Exiv2 keywords and strftime conversions, file collision prompting/force, timestamp options | `MetadataFileNames.Format` implements documented deterministic keyword/format subset; MetaTool chooses a collision-free suffix and offers dry-run, not Exiv2 prompt/overwrite semantics | Partial | Pinned `exiv2.md` “-r”; `src/MetadataSharp.Core/Operations/MetadataFileNames.cs`; unsupported conversions are documented in its XML docs; full option semantics estimate 1–2 weeks |
| Date adjustment | `-a`, `-Y`, `-O`, `-D`, timezone environment behavior | `MetadataOperations` provides date operations on metadata edits; no complete Exiv2 CLI timestamp, warning or timezone behavior | Partial | Pinned `exiv2.md` action and options; `MetadataOperations`; CLI parity estimate 1–2 weeks |
| `fixiso`, `fixcom` | Dedicated `fi` and `fc`; `-n enc` selects user-comment decoding charset | `MetadataOperations.FixIso` and `FixUserComment` are library operations; MetaTool has simple commands; encoding provider required for code pages | Partial | Pinned `exiv2.md` actions/`-n`; `MetadataOperations`; test paths under `tests/MetadataSharp.Tests/Operations` |
| `-c txt` comment option | Set a JPEG comment as part of the `modify` action | JPEG/PNG comment writes are exposed by document APIs; MetaTool has no `-c` option or comment-edit command | Missing (CLI) | Pinned `exiv2.md` “-c”; `JpegMetadataDocument.SaveComment`, `PngMetadataDocument.SaveComment`; estimate 1–2 days |
| `-b` obsolete option | Obsolete; reserved for upstream test suite | Not implemented by MetaTool and has no library operation | Missing (obsolete CLI option) | Pinned `exiv2.md` command summary; estimate not applicable (intentionally omitted) |
| `-k` / file timestamps | Keep timestamps on update; `-t`/`-T` set file time on rename | No shared Exiv2-style update/rename timestamp option contract; callers control filesystem timestamps | Missing (CLI) | Pinned `exiv2.md` “-k”, “-t”, “-T”; estimate 3–5 days |
| Encodings and output controls | `-n`, `-q`, `-Q`, `-u`, `-v`, `-V`, `-h`, `-f`, `-F`, `-l`, `-S`, plus time offsets `-a/-Y/-O/-D` | Charset handling exists for specific metadata values and operations; sample does not provide general Exiv2 logging, unknown-tag, force, location/suffix or version semantics | Partial / missing CLI | Complete option list is in the pinned command-summary table. Library-level charset behavior is below; compatible CLI option layer estimate 3–5 weeks |

### Print detail inventory (`-p`, `-P`)

The shorthand modes are `s` summary; `a` all EXIF/IPTC/XMP; `e` raw EXIF tags; `t` translated EXIF values; `v` raw EXIF values; `h` EXIF hex; `i` IPTC; `x` XMP; `c` JPEG comments; `p` preview listing; `C` ICC; `R` recursive structure; `S` structure; `X` raw XMP. The `-P` flags select families (`E,I,X`), numeric tag (`x`), group/key/label/description/name (`g,k,l,d,n`), type/count/size (`y,c,s`), raw/value-as-modify-command/translated output (`v,V,t`), and hex (`h`). MetadataSharp exposes values and typed interpretations, not these stable CLI text layouts. It does not claim text-output parity.

### Data target inventory

The targets are not interchangeable: Exiv2 has `a` (all supported), `e` (EXIF), `t` (EXIF thumbnail), `i` (IPTC), `x` (XMP), `c` (comment), `C` (ICC), `I` (all IPTC), `X` (XMP sidecar with conversion), `XX` (EXV/raw sidecar), `p` (one or more previews), and `-` (stdin/stdout filter) across the extract/insert/delete actions. MetadataSharp exposes corresponding operations as separate APIs and typed edits; it does not implement the entire action/target DSL. ICC profiles are returned/written as bytes, not interpreted. Sidecar conversion is explicit; a raw EXV file's MakerNote and image-data-address policy differs from an in-place edit. See `docs/usage/OPERATIONS_USAGE_AND_LIMITS.md` and `docs/usage/XMP_USAGE_AND_PRESERVATION_LIMITS.md`.

## Library functionality

| Feature | Exiv2 0.28.9 | MetadataSharp | Status | Evidence / gap estimate |
|---|---|---|---|---|
| Tag tables and print conversions | Broad Exif/TIFF, IPTC, XMP and vendor group tables; localized/interpreted values and raw forms | Typed CIPA/TIFF Exif definitions, IPTC IIM dataset definitions, XMP property tree, selected interpretation tables and MakerNote groups; not all upstream tag aliases, descriptions, translations or vendor values | Partial | Exiv2 `include/exiv2/{tags.hpp,iptc.hpp,properties.hpp,xmp_exiv2.hpp}`; `ExifTags`, `IptcDatasets`, `XmpPacket`, MakerNotes; gap: complete table/translation inventory 4–8 weeks, then ongoing vendor data |
| XMP data model | Standard properties, arrays, structures, qualifiers, language alternatives, namespace registry and custom properties | RDF/XML tree, path-based edits, arrays/structures/qualifiers, namespace registration and unknown-property retention within safety limits | Partial | Exiv2 `include/exiv2/properties.hpp`, `src/xmp.cpp`; `src/MetadataSharp.Core/Xmp`; tests `Xmp`; differences on unrecognized XML/RDF constructions are refused or retained raw rather than normalized |
| MakerNote groups and values | Broad camera-specific decoding, printing and writing | Recognized Canon, Nikon, Fujifilm, Pentax, Sony, Panasonic and Olympus/OM System groups; raw block retained; named-value edits are in place and read-back verified | Partial | `src/MetadataSharp.Core/MakerNotes`; MakerNote tests and pinned differential jobs. Vendor/tag coverage remains incomplete; incremental estimate 2–6 weeks per major vendor family |
| Cross-family conversions | Exiv2 has selected Exif/IPTC/XMP conversions and CLI sidecar conversion | Explicit Metadata Working Group sync/reconcile APIs and operations; only mapped fields are converted, with conflicts/options documented | Partial | `src/MetadataSharp.Core/Sync`, `MetadataOperations`; `tests/MetadataSharp.Tests/Sync`; not a promise to convert every tag |
| Character sets | Exiv2 decodes and writes according to its Exif/IPTC/XMP policies and CLI charset option | IPTC charset is declared or UTF-8 only; no guessing of undeclared non-ASCII; Exif user comments use explicit encodings; selected code pages require provider registration | Partial | `docs/usage/IPTC_USAGE_OWNERSHIP_AND_PRESERVATION_LIMITS.md`, `MetadataOperations.FixUserComment`, codec tests; broader legacy charset parity estimated 2–4 weeks |
| Preview / thumbnail APIs | `PreviewManager` delegates to format-specific loaders; extraction by selected preview and thumbnail edits | Bounded range listing and extraction for supported JPEG/RGB samples; coded BMFF entries can be listed but not extracted; EXIF thumbnail edit/remove at an existing directory | Partial | `include/exiv2/preview.hpp`, `src/preview.cpp`; `src/MetadataSharp.Formats/Previews.cs`, `ExifEdit`; see format gap estimates above |
| Sidecars and Extended XMP | XMP sidecars, EXV, converted/raw extraction/insertion, JPEG Extended XMP | XMP sidecar packets, EXV read/create/extract/insert APIs and JPEG Extended XMP read/write within packet limits; conversions are explicit | Partial | Exiv2 `app/actions.cpp`, `src/image.cpp`; `MetadataOperations`, `ExvFile`, JPEG XMP code; no compatible all-target CLI/data-flow |
| BMFF and video | Build-conditional BMFF and read-only video containers; format handlers vary by build | CR3 and HEIC/HEIF/AVIF metadata readers, limited safe edits where declared; bounded read-only video property readers for ASF, Matroska, QuickTime and RIFF | Partial | Exiv2 `src/image.cpp` conditional registry and `exiv2.md` build note; `src/MetadataSharp.Formats/Bmff`, `VideoContainerReader.cs`, tests `Bmff`/`Video`; no general video writer |
| Safety limits and preservation | Exiv2 validates format structures; behavior and error policy differ by handler | Explicit byte/count/depth budgets, checked ranges, preservation-oriented writers, read-back verification and typed `MetadataErrorCode`; unsupported/unsafe edits are refused | Different by design | `MetadataLimits`, `MetadataErrorCode`, capability notes, format usage contracts and hostile-input tests; compare per-operation contracts, not permissive write parity |
| Lens-ID databases | Upstream includes camera/lens identification data | **Excluded:** raw lens-identification values are retained; no lens-ID database work in this audit | Excluded by request | No implementation planned |
| Documented divergences | AVI colour-count handling and MakerNote relocation depend on Exiv2 behavior | **Excluded:** keep the documented AVI colour-count and unsafe MakerNote-relocation divergences; do not emulate unsafe relocation | Excluded by request | No implementation planned; preserve explicit refusal/preservation behavior |

## Documentation and examples

The libraries emit XML documentation on every build, and `Directory.Build.props` makes missing public XML summaries a compiler error (`GenerateDocumentationFile` plus warnings as errors). That does not require `<param>`, `<returns>` or `<exception>` elements, and it does not check purpose comments on internal/private methods and types. An exploratory parser scan found candidates but was not reliable enough to count omissions (partial types, inherited docs and declaration forms); no completeness percentage is reported. **Open documentation gap:** complete a validated declaration-by-declaration audit, add accurate parameter/return/exception documentation where missing, and add purpose/invariant comments to nontrivial internal/private declarations. **Estimate:** 1–2 weeks for a reliable Roslyn-based inventory and source review across Core and Formats. README examples are the code in `tests/MetadataSharp.Examples/QuickStart.cs`; the full CI workflow compiles/runs them on both target frameworks.

## Release-readiness actions and non-goals

The small documentation/API-surface corrections in this change are behavior-neutral; the items above that remain partial or missing are not silently treated as parity. Each known functional gap has an estimate in its table or the grouped estimates below. The largest work is the Exiv2-compatible CLI layer and full reference data; these are multi-week efforts and are not safe one-day additions. DCP format support is also missing; implement only with a dedicated parser, bounded-write policy, capability rows and fixtures (estimate 1–2 weeks), not by treating an unverified DCP as arbitrary TIFF.

Remaining format-level gaps include broader TIFF/raw camera-file differential fixtures, broader MRW/RAF and CRW corpora, JXL IPTC reading (upstream lists no JXL writer), CR3 IPTC reading where present in the pinned contract, and AVIF/HEIF/HEIC IPTC reading where present in the pinned contract. TIFF-based RAW ICC insertion remains refused unless the target image and preservation safety are demonstrated; a valid existing primary tag 0x8773 is now an explicit replace/remove target. The authored DNG and camera-RAW fixtures without such a primary profile remain refusal cases, while both byte orders of the DNG existing-profile replacement/removal path pin image and MakerNote preservation in `TiffBasedIccProfileTests`. PSD/JP2/EXV common-metadata write interoperability is gated in both directions against the pinned Exiv2 0.28.9 binary by `tools/reference/psd_jp2_exv_write_parity.py`; it compares EXIF/IPTC/XMP for all three and ICC bytes for JP2/EXV (Exiv2 documents PSD ICC as unsupported). EXV ICC creation remains a create-only API, not an in-place EXV writer.

The full CI workflow is the acceptance test: both target frameworks, OS matrix on PR, the test suite, compiled README examples, sample builds/smoke tests, offline policy checks and pinned Exiv2 differential jobs. A successful push-only run is not described as full CI. The final PR description records the final green run and its link.
