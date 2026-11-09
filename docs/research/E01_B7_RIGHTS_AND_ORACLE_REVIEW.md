# E01 B7 — per-format rights and oracle review (in progress)

B7 is a read-only census/probe batch, not authorization for a production
format API or write support. The developer authorized continuation without
another confirmation, but the approved census design still requires a separate
rights review for each format before authoring its fixtures. This ledger records
review progress; it does not yet clear any format fixture or imported sample.
The 30 B7 metadata seed cells and inventory remain pending. Do not treat this
table as an observation, a refusal, or an exclusion.

## Source and sample policy

- Use independent authored bytes or item-level verified CC0/public-domain
  samples only. Record recipe, origin, terms, hashes, limits, and independent
  expected facts. Do not copy Exiv2 tests, issue attachments, camera files,
  source code, or unrelated data. A repository-level license is not proof of
  rights for every image, specification, patent, or sample.
- Pinned Exiv2 0.28.9 is the external black-box baseline; ExifTool 12.76 is
  the secondary differential reference. The Git tag `12.76` resolves to
  `212f02cf22b9484674752181cad3b73089ce9694`; a prior local external checkout
  reported version 12.76. It was not vendored and is not present in every
  workspace. CI setup, version verification, and invocations remain to be
  designed; availability is not an observation.
- Neither tool's source, code, tag dictionary, tests, or outputs are a
  normative oracle or a production dependency. Recognition or metadata read
  does not prove an image decodes. BMFF item extents and relationships require
  bounded structural checks; no independent raw/coded-image decoder has yet
  been established as an oracle.

## Separate format ledger

| Format | Source examined / rights status | Remaining before authoring a format fixture |
| --- | --- | --- |
| AVIF | [AOM AVIF repository](https://github.com/AOMediaCodec/av1-avif/tree/bf4c18d1f3971069b75e87d6ee469790589f4f09), README and LICENSE: repository describes an ISO-BMFF/HEIF/MIAF-based AV1 image format and has a BSD-2-Clause root license. This is not a sample-level license or a complete normative/rights review. | Pin applicable AVIF and underlying BMFF/HEIF/MIAF rules, independently design bounded item relationships and owned data, and record rights/provenance and expectations. Do not copy repository sample files. |
| HEIF | No format-specific normative document or sample rights reviewed yet. HEIF is a container family, not a guarantee of codec availability. | Identify the applicable specification edition and sample/codec rights; then document item/extent expectations and codec limitations. |
| HEIC | No format-specific normative document or sample rights reviewed yet. HEIC and generic HEIF are not interchangeable claims. | Review format/codec sources and rights separately from HEIF; verify any encoder output is owned and redistributable; document structural and decode-oracle gaps. |
| CR3 | No Canon-specific normative layout or individual capture rights reviewed. Vendor camera samples and Exiv2 fixtures are not authorized. | Establish a rights-safe documented layout and source; if unavailable, record blocked/pending rather than fabricate a representative capture or claim valid RAW decoding. |
| JXL | [JPEG XL format overview](https://github.com/libjxl/libjxl/blob/eaae1abb2478c23b2fc487359619740cb4b72259/doc/format_overview.md) describes distinct bare codestream and ISO-BMFF container forms. The repository's BSD-3-Clause label does not establish individual sample rights or complete normative coverage. | Review pinned ISO/IEC 18181-1/-2 sources, independent container and codestream cases separately, provenance, and a suitable decoder/metadata oracle; do not copy libjxl test material. |

The Exiv2 operation seed ledger records read claims for EXIF, IPTC, XMP, and
thumbnail, and no documented claim for comments and ICC, for each of these
five formats. A no-claim cell remains in scope and must be attempted or honestly
blocked, not converted to a claim of absence. The generic `bmff_structure.py`
helper checks flat box bounds, `ftyp` structure, and JXL signatures only; toy
unit-test bytes are not format fixtures and cannot prove conformance or support.
Before B7 is marked complete, record per-format rights decisions, fixture
provenance, pinned CLI invocations and build options, secondary-oracle results
or explicit gaps, and honest statuses. Denominators remain unfrozen and
MetadataSharp verification remains not-assessed.
