# M17: DNG feasibility and proposed follow-up scope

Status: feasibility review complete; **future implementation plan NOT approved**.
The developer authorized M17 and independent M18 in one PR, not a DNG implementation.
Current TiffLayout/TiffMetadataDocument deliberately reject DNGVersion. Keep that
refusal. TIFF range accounting and append-preserve are not DNG safety certificates.

## Source and review provenance

Reviewed Adobe Digital Negative specification **1.7.1.0, September 2023**, 126 pages:
https://github.com/visualcommons/gamut/blob/master/references/dng/DNG_Spec_1_7_1_0.pdf
Mirror blob `5e4d714a2aa6b935286c6177991a72768fbaec0c`, SHA-256
`9cdfe43d44751b662d1f0ace81fff84f6a927321f5d18d79054ce78dd499abcd`.
Mirror identity is pinned, not publisher authenticity or a claim it is the latest
edition. Review used prose/tag definitions, not SDK implementation or GPL tests.
The PDF has restrictive redistribution terms: it and extracted text stay ignored
review artifacts, never copied into source/packages. This document is an independent
summary. Existing TIFF6/CIPA hashes and restrictions remain in the TIFF reviews.

Reviewed format overview pp.13–19; TIFF differences pp.20–23; DNG tag families
pp.24–84; opcode processing chapter 7 and compatibility appendix A. Future work
must separately pin normative TIFF/EP dependencies, codec rules and any digest
algorithm not specified completely here. This review is not legal/patent clearance.
The edition's p.24 DNGVersion description still mentions 1.7.0.0 despite the 1.7.1.0
cover. Do not infer supported version bytes from the cover or silently repair this
editorial inconsistency. DNGBackwardVersion alone also does not establish support.

## Dependency and preservation inventory

| Family | Why generic TIFF handling is insufficient | Initial decision |
| --- | --- | --- |
| DNGVersion 50706 / DNGBackwardVersion 50707 | BYTE/4 at IFD0; backwards compatibility also depends on types, counts and features | Explicit version/feature whitelist, unknown future versions refuse |
| SubIFDs, NewSubFileType, raw/preview roles | IFD0 can be thumbnail; highest-quality raw need not be root; DNG recommends SubIFD trees and does not support SubIFD chains | Validate DNG-specific roles/topology, not the broader TIFF DAG acceptance |
| Raw strips/tiles and sample geometry | CFA photometric, packed depths, linear raw, compression and predictor semantics differ from M15 | Preserve encoded bytes, offsets/count arrays, geometry and order; never decode/recompress in metadata writer |
| CFA pattern, color matrices, black/white levels, ActiveArea, crop, calibration and orientation | These determine rendering/coordinates, not merely descriptive text | Keep type/count/content and association; no edits initially |
| Previews/thumbnails and their settings | Multiple IFD images; cached render can be stale even when raw bytes match | Inventory every image extent; no preview regeneration or render-affecting edits |
| ExtraCameraProfiles 50933 | Additional camera-profile directory structures have their own framing/base rules | Exclude initially; ordinary SubIFD rewriting is not sufficient |
| OpcodeList1/2/3 (51008/51009/51022) | UNDEFINED programs at different raw processing stages; internal lengths, version/optional flags and geometry dependencies | Refuse initially, including unknown optional opcodes; future bounded opcode inventory needs separate approval |
| DNGPrivateData 50740 / MakerNoteSafety 50741 / MakerNote | Spec requires private block self-containment/base independence; declaration alone cannot prove a hostile vendor payload obeys it | No vendor private data/MakerNote in initial candidate; safe flag alone is not relocation proof |
| RawDataUniqueID 50781 | Raw identity is not an entire-file hash | Preserve for descriptive-only edits only after reviewed dependency proof; no arbitrary regeneration |
| RawImageDigest 50972 | MD5 over normalized pixel samples, not compressed byte ranges or whole file | Cannot validate by hashing coded strips; no digest refresh without approved algorithm/decoder scope |
| NewRawImageDigest 51111 | Spec delegates algorithm detail to SDK source | Open independent algorithm/spec/licensing question; exclude initially, do not translate SDK code |
| OriginalRawFileData 50828 / OriginalRawFileDigest 50973 | Embedded original file and its digest are separate from active raw image | Exclude initially; finite opaque-data framing/dependency proof required |
| PreviewSettingsDigest 50969 | Identifies conversion settings, not file integrity or generic raw hash | Preserve only with unchanged rendering settings; not a validation substitute |
| XMP 700, IPTC 33723, EXIF/GPS, vendor extensions | Multiple metadata systems and stale embedded previews; synchronization not implicit | Independent future addressed contracts; no RemoveAll/privacy promise |
| Semantic masks, depth/enhanced images, gain tables, proxy DNG and newer rendering features | Additional images/coordinate spaces/version requirements | Explicit exclusions; do not treat as harmless unknown tags |
| BigTIFF | DNG discusses the extension, but current classic engine has uint pointers and a 4-GiB ceiling | Exclude; never reinterpret 64-bit layout through classic TIFF |

Keeping original byte offsets can preserve known storage but cannot prove hidden
vendor pointers, signatures or integrity dependencies are valid after the active
IFD graph/header changes. An append-preserve candidate retains obsolete data and
grows files; it is NOT forensic removal or compaction. A read-only inventory is
feasible; safe general DNG editing is NOT established by this milestone.

## Narrow candidate and required contracts (proposal only)

1. **D0 – standalone bounded read-only DNG inventory proposal**, then approval before
   implementation. Separate facade/options/result model from current TIFF APIs.
   Original relative offsets and IFD roles, version-feature decisions, ranges/issues,
   ownership, async/cancellation, finite value/image/opcode/private limits and errors
   must be exact. No public signatures are implemented/approved by this document.
2. **D1 – implemented D0 subset after explicit API approval.** Candidate source:
   classic II/MM, DNG 1.1.0.0 with compatible backward version, exactly one 16-bit
   unsigned uncompressed CFA raw image and at most one uncompressed RGB thumbnail,
   bounded strips, no tiles initially. Explicit CFA/layout/calibration required-tag
   whitelist from the spec, consistent counts/roles and no partial aliases/cycles.
   Exclude private data, MakerNotes, embedded originals, extra profiles, opcodes,
   digests, enhanced/depth/mask/proxy images and unrecognized tags/features. Synthetic
   files must be independently judged DNG-valid, not TIFF fixtures with a DNG tag.
3. **D2 – descriptive-write feasibility/contract proposal only**, after independent
   D1 gate. Candidate addressed ASCII Software/Artist only; no orientation, dates,
   XMP/IPTC, render controls, raw identity/digest changes, directory creation/deletion
   or repair. Prove every affected pointer/dependency, version/required-tag
   constraints, source SHA-256/length, output namespace, budgets and new-path failure
   semantics. Decide append-preserve vs alternative explicitly; do not inherit M16
   authorization. Exact public signatures and no-op/refusal rules need approval.
4. Any compressed/raw breadth, digest computation or private-scheme support is a
   later separately licensed/spec-reviewed unit, not an implicit extension of D1/D2.

## Fixtures, independent tests and open blockers

- Generate original minimal pixels/calibration data ourselves from prose, both
  byte orders, with documented provenance and independent known-byte directory/range
  expectations. No internet/camera/SDK test corpus without item-level redistribution
  rights; a manufacturer's downloadable file is not automatically MIT-compatible.
- Independently validate candidate DNG conformance and decoded sample hashes using
  at least two legally reviewed, version-pinned tools where possible. Exiv2 and
  ExifTool metadata agreement alone is NOT raw-decoding/digest/conformance proof.
  Consider DNG validator/raw decoder as external black boxes only after tooling
  license/availability review. Do not copy their implementations/tests into MIT.
- Test required-tag/version mismatches, incorrect CFA/black-level/color counts,
  multiple raw/preview roles, unknown/private tags, opcode nested sizes, digest
  presence, disguised BigTIFF, shared/partial ranges, uint overflow, graph cycles,
  cancellation, source mutation, identity, temp publication races and retained old
  metadata. Bound fuzz/mutation campaigns and state unexecuted checks honestly.
- Blocking before a writer: independently specified dependency coverage and digest
  policy, complete candidate required-tag table and sample oracle, tool availability
  and fixture rights. No DNG files have been decoded/edited/validated in M17.

Approval requested: the D0 → D1 → D2 **follow-up sequence**, with a separate exact
public contract review before each implementation. It is not required to finish
the independent M18 audit, does not authorize implementation now, and is not
implied by merging this feasibility document. See the proposed follow-up prompts.
