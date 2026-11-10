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
Length-bearing segments of every process are accepted: SOF0–SOF3, SOF5–SOF7,
SOF9–SOF11 and SOF13–SOF15 (the reserved 0xC8 code and every other marker are
not), DQT, DHT, DAC, DRI, DNL, COM and APP0–APP15, in front of as well as behind
entropy-coded data. Validate marker grammar, segment lengths against the declared
count, frame precision in 2..16, nonzero width, component counts and ids, sampling
factors 1..4, table selector ranges, oversubscribed Huffman codes, zero quantizer
elements and the standalone-marker cases outside scan data. Frame and scan headers
are checked for the rules a metadata edit can actually depend on; process-specific
decode constraints (which precision a given SOF permits, MCU layout, predictor and
restart-interval consistency) are not enforced, because refusing an undecodable but
structurally sound file would discard recoverable metadata for no safety gain.
Table redefinitions replace prior definitions and may appear between scans.
Multiple frames and multiple scans are allowed; the last frame read is the one whose
zero-height and component references the scan checks use. No Huffman or arithmetic
symbol decoding, and no pixel validation, is done.

Spectral, approximation and predictor fields are checked against the process the
frame marker declares: lossy frames need Ss ≤ Se ≤ 63 and an interleaved scan must
cover every band, while lossless frames instead name a predictor in Ss (1..7) with
Se zero and carry no quantization table, and arithmetic frames take their table
definitions from DAC rather than DHT. So a scan header that is valid for progressive
coding is still refused for lossless coding, and an arithmetic file needs no DHT.
A zero-height frame is accepted only when a nonzero DNL line count follows.

Entropy scanning handles FF00, marker fill bytes and sequential RST0–RST7 when DRI
is nonzero, without decoding pixels, and stops at the first marker that is not scan
data; that marker is then parsed as a segment, so a file may hold any number of
scans. Only EOI ends the file, and any trailing byte after EOI is refused. Restart
interval counts cannot be validated without decoding. This is a structural subset,
not a promise that every accepted pixel stream decodes.

APP1 is EXIF only for the exact six-byte signature. Duplicate matching blocks are
AmbiguousMetadata; unrelated APP1/APP13 are opaque ranges. Segment starts include
marker fill bytes. Insertion is after contiguous leading APP0 segments or SOI.
Record SOS, entropy and EOI ranges plus scan and frame counts for byte-preservation
checks. EXIF TIFF interpretation belongs to Open in M08, not the scanner.

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
one EOB code; entropy byte 3F represents those codes plus one-bit padding. The
progressive fixture is a 12-bit SOF2 frame with three scans (DC band, AC band with a
redefined Huffman table between them, DC successive-approximation refinement); the
arithmetic fixture is a SOF9 frame with a DAC table and no DHT. No external photo or
GPL fixture is used. Tests cover every frame-type marker, multi-scan and post-scan
segments, DNL-completed zero height, DAC and lossless predictor rules, plus the
refusals that remain: reserved and non-length-bearing markers, unbacked table
references, bad restart sequencing, fill-before-stuff, empty entropy, trailing bytes
and short segment lengths. Every truncated prefix of all three shapes must fail, and
bounded random byte mutation may only fail with MetadataException. Short reads and
sync-vs-async parity are exercised on the progressive shape as well as the baseline
one. Editing tests assert that a metadata edit on progressive, arithmetic,
differential and lossless frames leaves every byte outside the EXIF APP1 identical.
External decoder/tool validation of the authored images is deferred to the
differential harness job.

## Amendment — 2026-11-10

This unit supersedes the "single image … one SOS … 8-bit baseline SOF0" wording in
`INITIAL_CONTRACTS.md` §"JPEG acceptance subset" and the "do not broaden the M07
single-scan baseline acceptance subset" sentence in
`XMP_PACKET_SIDECAR_AND_JPEG_CONTRACT_PROPOSAL.md` §5; both files carry a dated note
pointing here. The public API is unchanged. No new normative source was read for the
amendment: the field rules above come from the T.81 clauses already reviewed at the
top of this file, and the policy for what to enforce is the read-only-metadata
boundary this document has always stated.
