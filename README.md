# MetadataSharp

An MIT-licensed managed .NET library for image metadata editing without
pixel decoding or recompression. **Early implementation: restricted JPEG EXIF/IPTC/XMP, static PNG/WebP EXIF/XMP editing and XMP sidecars with conservative safety refusals; classic TIFF layout accounting and restricted descriptive editing (obsolete bytes retained).**
This is not general image-format or full RDF/XMP support.

## Current expansion programme

[Independent Exiv2 capability coverage and .NET 8/10 programme](docs/roadmaps/EXIV2_PARITY_AND_DOTNET_PROGRAM.md)
is the active roadmap; the 2026-11-08 directive (reach Exiv2-level capability, avoid
external dependencies) is broken into gated subtasks in
[the master plan](docs/design/EXIV2_PARITY_MASTER_PLAN.md). Near-complete parity is a
goal, not an implemented claim; net8.0/net10.0 dual targeting is in place and .NET
Framework 4.8.x is deferred (not cheap). The DNG D0 contract was approved
2026-11-07, and the E03 architecture/compatibility contracts were approved 2026-11-08
and implemented (additive `MetadataCapabilities` discovery API; zero third-party
runtime dependencies remain enforced by a test).

## Start Here

- [Current state and recovery instructions](docs/CURRENT_STATE.md): approved scope,
  completed gates, next actions and checks for resuming without chat history.

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

M00 was approved on 2026-11-07. M01–M16 passed their cross-platform implementation checkpoints; the
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
- [M15 approved read-only TIFF contract](docs/design/STANDALONE_TIFF_READ_ONLY_CONTRACT_PROPOSAL.md).

- [Standalone TIFF usage and limitations](docs/design/STANDALONE_TIFF_USAGE_AND_LIMITATIONS.md).
- [Standalone TIFF normative review and support matrix](docs/design/STANDALONE_TIFF_NORMATIVE_REVIEW_AND_SUPPORT.md).

- [M15 audit and M16 constraints](docs/audits/STANDALONE_TIFF_READ_ONLY_AUDIT.md).

See [LICENSE](LICENSE) for the MIT license.

- [M16 TIFF editing usage and preservation/failure boundaries](docs/design/STANDALONE_TIFF_EDITING_USAGE_AND_LIMITS.md).
- [M16 implementation audit and final-gate status](docs/audits/STANDALONE_TIFF_EDITING_AUDIT.md).

## Final roadmap audits (M17–M18)

- [DNG feasibility and approval-pending future sequence](docs/design/DNG_SCOPE.md):
  **no DNG implementation**; existing TIFF refusal is unchanged.
- [Measured performance and local packaging audit](docs/audits/PERFORMANCE_AND_PACKAGING_AUDIT.md):
  three-platform evidence, allocation/throughput limits and remaining release gates.
  **No package publication authorized.** Final grouped gate/merge is tracked in PR #12.

## Runtime targets

Libraries target **.NET 8 and .NET 10**; source builds require SDK 10, with both
runtimes/SDKs installed for full tests. .NET 8 package consumers are independently
checked using SDK 8. New deployments should prefer .NET 10 LTS; net8.0 assets do
not extend Microsoft's .NET 8 servicing beyond 2026-11-10. Framework 4.8.x is not
supported. See [E02 support/audit](docs/audits/DOTNET_8_10_SUPPORT_AND_E02_AUDIT.md)
for exact platform evidence and limitations. Packages remain local audit candidates,
not published releases. Specify `-f net8.0` or `-f net10.0` when running tool projects.
