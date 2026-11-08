# MetadataSharp

An MIT-licensed managed .NET library for image metadata editing without
pixel decoding or recompression. **Early implementation: restricted JPEG EXIF/IPTC/XMP, static PNG/WebP EXIF/XMP editing and XMP sidecars with conservative safety refusals.**
This is not general image-format or full RDF/XMP support.

## Start Here

- [Project instructions](METADATASHARP_PROMPT.md): mission and engineering baseline.
- [Accepted initial-release tradeoffs](docs/decisions/INITIAL_RELEASE_ACCEPTED_TRADEOFFS.md):
  what was deliberately restricted, why, and when to revisit it.
- [Approved initial API and safety contracts](docs/design/INITIAL_CONTRACTS.md):
  exact proposed signatures, limits, preservation/failure behavior and acceptance cases.
- [Build and test instructions and CI evidence](docs/BUILD_AND_TEST.md).
- [Development, licensing and regression-test policy](docs/DEVELOPMENT.md).
- [Milestones and completion gates](docs/ROADMAP.md).
- [One-stage execution prompts](docs/prompts/README.md).
- [Binary primitive failure rules](docs/design/BINARY_PRIMITIVES_AND_FAILURE_ATOMICITY.md).
- [Raw EXIF ownership and equality](docs/design/RAW_EXIF_VALUE_OWNERSHIP_AND_EQUALITY.md).
- [TIFF/EXIF normative review and read restrictions](docs/design/TIFF_EXIF_NORMATIVE_SOURCES_AND_READ_POLICY.md).
- [EXIF layout and safe rebuild refusals](docs/design/EXIF_LAYOUT_PLANNING_AND_SAFE_REBUILD_REFUSALS.md).
- [Serializer preflight/output guarantees](docs/design/EXIF_SERIALIZATION_PREFLIGHT_AND_OUTPUT_GUARANTEES.md).
- [Progress and approval history](docs/PROGRESS.md).

M00 was approved on 2026-11-07. M01–M14 passed their cross-platform implementation checkpoints; the
required Exiv2/ExifTool job also passed. See [progress](docs/PROGRESS.md) and the
[first-release audit and remaining publication gates](docs/audits/INITIAL_JPEG_RELEASE_AUDIT.md).
PNG/WebP share a final group audit/merge gate recorded in progress.
This is not a published package or a full EXIF/JPEG conformance claim. The minimal
EXIF creation profile has a documented missing-default-tag validation warning.

- [JPEG supported scanning subset](docs/design/JPEG_MARKER_SCANNING_AND_SUPPORTED_SUBSET.md).
- [JPEG editing, ownership and failure semantics](docs/design/JPEG_EXIF_EDITING_OWNERSHIP_AND_FAILURE_SEMANTICS.md).

- [XMP usage, ownership and preservation limits](docs/design/XMP_USAGE_AND_PRESERVATION_LIMITS.md).
- [XMP normative review and restricted profile](docs/design/XMP_NORMATIVE_REVIEW_AND_RESTRICTED_PROFILE.md).

- [IPTC usage, ownership and preservation limits](docs/design/IPTC_USAGE_OWNERSHIP_AND_PRESERVATION_LIMITS.md).
- [IPTC IIM and Photoshop normative review](docs/design/IPTC_IIM_AND_PHOTOSHOP_NORMATIVE_REVIEW.md).

- [PNG/WebP usage and preservation limits](docs/design/PNG_AND_WEBP_USAGE_AND_PRESERVATION_LIMITS.md).
- [PNG/WebP normative review and reference discrepancies](docs/design/PNG_AND_WEBP_NORMATIVE_REVIEW.md).
- [M15 read-only TIFF proposal — approval pending](docs/design/STANDALONE_TIFF_READ_ONLY_CONTRACT_PROPOSAL.md).

See [LICENSE](LICENSE) for the MIT license.
