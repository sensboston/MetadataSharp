## Progress Update (2026-11-09)

- **Task 1 (Thumbnail read policy):** SHIPPED (#65).
- **Roadmap 4.2c (Free fixes):** COMPLETED (#71). Ascii NUL-trim + UTF-8 preference; Undefined charset prefixes for UserComment/GPSProcessingMethod/GPSAreaInformation.
- **Task 3 (Naming/Alias on EXV corpus):** QUEUED (PR #72).
- **New Feature (ISO BMFF metadata reading):** IMPLEMENTED.
  - Added `src/MetadataSharp.Formats/Bmff/BmffMetadata.cs` (parse ftyp, meta (hdlr pict), iinf/infe, iloc, pitm; return ExifMetadata/XmpPacket; bounded limits; InvalidBmff errors).
  - Updated `tools/MetadataSharp.Dump/Program.cs` to wire ISO BMFF parsing (replacing "not-attempted iso-bmff").
  - Added unit tests in `tests/MetadataSharp.Tests/Bmff/BmffMetadataTests.cs` with synthetic boxes.
  - Updated `.csproj` files to include Bmff folder and tests.
  - Public API signatures documented in PR description (to be created).

**Current State:** Branch `arena/65ee8b05-metadatasharp` updated with BMFF implementation and tests. Ready for PR.
