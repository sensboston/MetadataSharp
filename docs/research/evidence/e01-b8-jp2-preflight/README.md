# B8 JP2 — pinned read-only preflight

The owned lossless 8×8 RGB boxed JP2 was encoded from independently invented
pixels after per-format rights review; exact Pillow/OpenJPEG wheel and manifest
are in `tests/fixtures/b8/jp2-provenance.json`. Local ImageMagick 6.9.11-60
identified and decoded the 8×8 authored pixels exactly; it may share OpenJPEG,
so this is not a fully independent codec-family oracle.

PR #47 head `35010695638589724670749f690f628d7e8b0b39`, CI run 42712354021,
passed 15/15; it regenerated the exact JP2 fixture with the hash-pinned wheel.
All two check-run annotation chunks were recovered into `ci-observations.json`;
`receipt.json` records its hash and artifact metadata. The ZIP was not downloaded.
Pinned Exiv2 0.28.9 gave exit 0 for structure (`-pS`), metadata (`-pa`), comment
(`-pc`) and ICC (`-pC`); only structure had substantive output. `-et` returned
253, no artifact and “No Exif data found”. All five probes left input unchanged.
Pinned ExifTool 12.76 identified JP2, 8×8 and 3 components. The fixture has no
authored metadata or thumbnail, so empty reads or extraction refusal establish
nothing about metadata-present support. No complete codestream conformance or
B8 census observation is asserted. Full command outputs, input hash, executable
hash, configuration and secondary JSON are in the report.
