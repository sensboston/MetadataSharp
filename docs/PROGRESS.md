## Progress Update (2026-11-09)

- **Task 1 (Thumbnail read policy):** SHIPPED (#65).
- **Roadmap 4.2c (Free fixes):** COMPLETED (#71). Ascii NUL-trim + UTF-8 preference; Undefined charset prefixes for UserComment/GPSProcessingMethod/GPSAreaInformation.
- **Task 3 (Naming/Alias on EXV corpus):** QUEUED (PR #72).
- **New Feature (ISO BMFF metadata reading):** IMPLEMENTED (commit `4530344`).
- **IPTC/XMP Naming Fixes (Queue after BMFF):**
  - **IPTC:** Added dataset names for records 1 and 2 (IIM 4.2 spec) to `tools/MetadataSharp.Dump/Program.cs`. Keys now print as `Iptc.Envelope.<Name>` / `Iptc.Application2.<Name>`. Unknown datasets remain `0xNN`.
  - **XMP:** Updated prefix mapping in `tools/MetadataSharp.Dump/Program.cs` (added `crs`, confirmed `aux`, `xmpMM`, `dc`, `exif`, `tiff`, `photoshop`, etc.).
  - **Report:** ~148/2082 XMP keys moved to standard prefixes; 0/267 IPTC keys matched initially (now naming coverage improved for records 1/2).

**Current State:** Branch `arena/65ee8b05-metadatasharp` updated with IPTC/XMP naming fixes. Ready for PR.
