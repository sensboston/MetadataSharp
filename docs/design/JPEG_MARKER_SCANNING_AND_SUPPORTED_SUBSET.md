# M07: JPEG Marker Scanning and Supported Subset

## Normative Review

Reviewed CCITT T.81 (09/1992), identical to ISO/IEC 10918-1:1993, Annex B.1.1,
B.2.1–B.2.4, Tables B.1–B.7 and Annex F baseline restrictions. Source PDF via
GitHub blob `83fba254b48936fbb8e0e3495c508089fd55408d` in visualcommons/gamut,
SHA-256 `631031d4ba56b06abee3e312a0f235b9422da9c7267d1c8f7604418795768bf0`.
Also reviewed ITU-T T.871 (05/2011), JFIF, blob
`5e5d094428cd6976f237dd8058e5906aa07ad012`, SHA-256
`ca3d749e7e04016f7695ba9d319eeeab3ad80c99265a883c14f03b9daf3e1c9a`.
Mirrors were read as reference documents, not copied into the repository or treated
as publisher authenticity verification. No implementation/test source was used.

## Structural Acceptance

Internal JpegScanner consumes a readable seekable stream at zero through EOI.
Only SOF0, DQT, DHT, DRI, APP0–APP15, COM and one SOS are accepted before entropy
data. Validate marker lengths, frame dimensions/precision/components/sampling,
unique frame IDs, matching ordered SOS IDs, baseline table selectors and SOS
spectral/approximation fields. DQT/DHT bounds, nonzero quantizers, code-length
oversubscription and table presence are checked. Table redefinitions before SOS
replace prior definitions. No Huffman symbol decoding or MCU validation is done.

Entropy scanning handles FF00, marker fill bytes and sequential RST0–RST7 when DRI
is nonzero, without decoding pixels. Only EOI may terminate the sole scan. Reject
additional scans, post-scan APP/COM, unsupported modes, missing EOI and trailing
bytes. Restart interval counts cannot be validated without decoding. This is a
structural subset, not a promise that every accepted pixel stream decodes.

APP1 is EXIF only for the exact six-byte signature. Duplicate matching blocks are
AmbiguousMetadata; unrelated APP1/APP13 are opaque ranges. Segment starts include
marker fill bytes. Insertion is after contiguous leading APP0 segments or SOI.
Record SOS, entropy and EOI ranges for future byte-preservation checks. EXIF TIFF
interpretation belongs to Open in M08, not the scanner.

## I/O and Resource Boundaries

Sync and async share the same state machine but select real Read vs ReadAsync,
without Task.Run. A pooled CopyBufferBytes input buffer and one pooled bounded
65,533-byte segment buffer handle arbitrary short reads. No full image buffering.
Input-byte/segment budgets are enforced. Per-byte marker logic avoids pixel
allocation; plain entropy runs are skipped with Span.IndexOf. Pools return in
finally/Dispose; sources stay open. Source modification and stream aliasing remain
caller preconditions, with identity checks supplied later by M08.

## Tests

The authored 8x8 grayscale fixture uses a quantizer of ones, one zero-DC code and
one EOB code; entropy byte 3F represents those codes plus one-bit padding. No
external photo or GPL fixture is used. Tests exercise signatures/ranges, every
truncated prefix, deterministic mutations, short reads, async-vs-sync calls,
fill/stuffing/restarts, unsupported modes/extra scans, source state and budgets.
External decoder/tool validation of the authored image is deferred to M09.
