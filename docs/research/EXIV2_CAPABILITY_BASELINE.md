# E01 initial capability baseline — not a completed parity inventory

Observed 2026-11-07. Reference release v0.28.9, commit
b0859fc61fea5dedfc898aece6b15893ddf4314e. Sources reviewed: README blob
fe3b55bae70d1dc2fa5ef00d0c53f9e239afdcff (features/build/license/BMFF warnings),
CLI manual blob 149f53b809c182bb4145567e28c466965f7f6505 (file types/actions).
https://github.com/Exiv2/exiv2/blob/v0.28.9/exiv2.md
https://github.com/Exiv2/exiv2/blob/v0.28.9/README.md
License text and README identify GPL v2-or-later; GitHub repository license API
reported NOASSERTION, which is not permission to reuse code. No implementation,
header/tag dictionary, tests, issue patches or sample image data were imported.

The pinned manual documents **31 image/file-type rows × six metadata categories**.
EXIV2_DOCUMENTED_FORMAT_FACTS.json records these capability facts with provenance.
This is a discovery grid, NOT 186 verified tests or a frozen parity denominator.
Each read/write cell needs separate atomic behavior/layout/encoding cases and
applicable build options. Empty/'-' means no documentation claim, not impossibility.
The manual's PNG Exif column makes no support claim even though MetadataSharp
implements restricted PNG eXIf: distinguish standards-based additions from parity.
BMFF is a build option; naked JXL codestreams differ from JXL containers. Video is
only described as rudimentary reading, not general editing. No v0.28.9 executable
was installed/run in this work; old required CI references remain 0.27.6.

## MetadataSharp baseline from existing source/contracts, not new measurements

P = partial concrete implementation; N = no public feature implementation;
I = inventory only. These are broad discovery labels, never parity percentages.

| Family | Existing status | Missing baseline work / programme owner |
| --- | --- | --- |
| JPEG EXIF/IPTC/XMP | P: addressed restricted editing, typed payloads, independent metadata systems | Full tag/encoding/layout breadth, Extended XMP, transfer semantics: E06/E07/E15 |
| PNG | P: EXIF/XMP static subset | IPTC mapping, ICC, all documented thumbnail operations, broader chunks: E07/E14 |
| WebP | P: static EXIF/XMP | ICC, thumbnail breadth, wider variants: E07/E14; no animation claim |
| Classic TIFF | P: read-only image ranges and ten descriptive writable tags | NOT general TIFF EXIF/IPTC/XMP editing; full schemas/private dependencies: E07/E13 |
| XMP sidecars | P: bounded RDF subset, arrays/language alternatives | General structured/qualified RDF, mappings, valid/opaque distinction: E06/E15 |
| Generic values/tag vocabulary | P: raw TIFF values, partial EXIF model/IPTC/XMP | Full semantic names, types, encodings, pretty printing and independent dictionary provenance: E06/E12 |
| DNG | N: current TIFF facade rejects DNGVersion | D0 proposal E04, read-only E05, separate D2 write gate E13 |
| ARW/CR2/NEF/ORF/PEF/SRW | N: no supported vendor RAW API | TIFF-like does not imply safe support; independent E10/E12/E13 gates |
| MRW/RAF/RW2/SR2 | N | Match documented read breadth first; RAF explicitly partial in baseline: E10 |
| CRW/CIFF | N | Legacy graph/framing and applicable writes: E11 then E13 |
| JP2/PSD/PGF/EPS/EXV/DCP | N | Per-format applicable metadata operations only; DCP not ordinary image TIFF: E08 |
| AVIF/HEIF/HEIC/CR3/JXL | N | Documented read-only baseline; item extents/compression/capabilities and legal review: E09 |
| BMP/GIF/TGA | N | Detection/MIME/dimensions, NOT implied metadata editing: E11 |
| MOV/MP4/MKV/AVI/WAV/ASF families | N | Version/build-dependent inventory must be reconciled before contracts: E11 |
| Comments / ICC profiles | N as general metadata APIs | Preserving opaque bytes is not reading/extracting/validating an ICC profile: E07/E14 |
| Previews/thumbnails | P/I: embedded EXIF primitive/range accounting | Not general preview enumeration/extraction/replacement across formats: E14 |
| Vendor MakerNotes/lens rendering | N as broad supported schemes | Per-maker generation/base/layout, rights and ambiguity; raw BLOB copy insufficient: E12 |
| Timestamp adjust/fix/copy/rename workflows | N as dedicated services | Define .NET equivalents or reviewed exclusions; no need to reproduce CLI flags: E15 |
| Remote I/O / stdin / batch command tools | N as comprehensive workflows | Inventory from baseline docs/options; consider security/ownership before optional adapter; E01/E15 |

## Remaining E01 acceptance work

1. Reconcile every documented format/category with a pinned licensed v0.28.9 CLI
   build and recorded feature switches. Distinguish documentation gaps, observed
   behavior and normative validity; neither Exiv2 nor its manual is infallible.
2. Expand into versioned IDs for read/insert/replace/delete/extract, duplicates,
   text/array/rational/binary values, vendor/tag breadth, sidecars, comments, ICC,
   previews, file/workflow handling, structural diagnostics and supported video.
   Add independent expected results and fixture rights before scoring coverage.
3. Identify unsupported/unsafe dependencies and propose exclusions to developer;
   keep both full-baseline and accepted-target denominators visible. No score yet.
4. Use two independent references where feasible, not just ours→ours round trips;
   coded image/prefix/offset preservation is a separate test from visible tag facts.
5. Investigate bodies of selected issues/discussions as needed through a requirements
   extraction process without importing attached implementation/tests/fixtures.
   Initial triage below used titles/labels/status only, not deep bug reproduction.

Full E01 is OPEN. This first pass is enough to identify bounded work and prevent
"100%" from silently meaning only the currently supported formats.

Follow-up: selected initial bodies have now been reviewed as requirements in
EXIV2_BODY_REVIEW_SUPPLEMENT.md; that later pass does not retroactively change
the provenance/scope of the initial title-only JSON snapshot. No defects reproduced.
