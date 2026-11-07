# MetadataSharp

A planned MIT-licensed managed .NET library for image metadata editing without
pixel decoding or recompression. **Infrastructure skeleton only: no metadata implementation yet.**
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
- [Progress and approval history](docs/PROGRESS.md).

M00 was approved on 2026-11-07. M01 restore/build/test passed on Linux, Windows
and macOS in [GitHub Actions](https://github.com/sensboston/MetadataSharp/actions/runs/42416545213).
M02 is next eligible when requested; no metadata implementation exists yet.
See [LICENSE](LICENSE) for the MIT license.
