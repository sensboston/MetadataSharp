# MetadataSharp

A planned MIT-licensed managed .NET library for image metadata editing without
pixel decoding or recompression. **Early implementation: binary primitives and raw EXIF values only; no file parser/editor yet.**
The initial scope is a restricted JPEG EXIF reader/editor, not general format support.

## Start Here

- [Project instructions](METADATASHARP_PROMPT.md): mission and engineering baseline.
- [Accepted initial-release tradeoffs](docs/decisions/INITIAL_RELEASE_ACCEPTED_TRADEOFFS.md):
  what was deliberately restricted, why, and when to revisit it.
- [Approved initial API and safety contracts](docs/design/INITIAL_CONTRACTS.md):
  exact proposed signatures, limits, preservation/failure behavior and acceptance cases.
- [Build and test instructions and current tooling blocker](docs/BUILD_AND_TEST.md).
- [Development, licensing and regression-test policy](docs/DEVELOPMENT.md).
- [Milestones and completion gates](docs/ROADMAP.md).
- [One-stage execution prompts](docs/prompts/README.md).
- [Binary primitive failure rules](docs/design/BINARY_PRIMITIVES_AND_FAILURE_ATOMICITY.md).
- [Raw EXIF ownership and equality](docs/design/RAW_EXIF_VALUE_OWNERSHIP_AND_EQUALITY.md).
- [Progress and approval history](docs/PROGRESS.md).

M00 was approved on 2026-11-07. M01, M02 and M03 passed restore/build/test on
Linux, Windows and macOS. See [progress](docs/PROGRESS.md) for commit/run evidence.
M04 is next eligible when requested, subject to its normative verification gates.
See [LICENSE](LICENSE) for the MIT license.
