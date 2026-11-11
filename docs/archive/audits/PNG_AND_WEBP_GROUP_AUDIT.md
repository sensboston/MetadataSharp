# M13–M14: PNG/WebP Group Audit and Remaining Gates

## Approved scope and completed checkpoints (2026-11-07)

The developer approved the exact PNG/WebP contract, one grouped PR with separate
units/gates, then requested attempting M15 afterward. No M15 public/safety contract
was included. Work remains on the fixed Arena branch; no master checkout or push.

| Checkpoint | Actual source head | Required passing run |
| --- | --- | --- |
| M13 PNG before WebP began | c2f3c58ca415bb002071ae7a004f38f1862f462e | [42429048302](https://github.com/sensboston/MetadataSharp/actions/runs/42429048302), three platforms/reference, 338-case minimum |
| M14 WebP initial implementation | cc8b8700e63cd922f9e30b60db83cd6c534be81f | [42429453288](https://github.com/sensboston/MetadataSharp/actions/runs/42429453288), three platforms/reference, 352-case minimum |
| Group code/failure/reference audit | da259516449cec78d17e3b9f3790a73c3237d4e4 | [42429946046](https://github.com/sensboston/MetadataSharp/actions/runs/42429946046), 361 passing tests/platform, Python 12/12, required references green |

This final documentation/evidence commit itself must also pass head CI before
merging [PR #9](https://github.com/sensboston/MetadataSharp/pull/9). Final-head run
IDs/SHA and merge evidence are posted there after actual completion; predecessor
results alone never authorize a red final-head merge. No package publication.
Local dotnet is unavailable; .NET claims above are observed Actions results, not
local builds. `python -m unittest discover -s tests/reference -q` and
`git diff --check` also pass locally.

## Implemented safety boundary

- Concrete independent PNG/WebP facades, immutable option snapshots, borrowed/owned
  source rules, owned EXIF/XMP results, actual async I/O, cooperative cancellation.
  Existing JPEG public APIs/production dependencies are unchanged.
- PNG streams every accepted CRC, validates structure/order and static headers,
  refuses APNG/unknown critical chunks. IDAT is never decoded. Safe unknown chunks
  survive edits; unsafe unknown ancillary chunks block effective edits, not no-op.
- PNG XMP is exact-keyword bounded iTXt. Empty language/translation profile; zlib
  framing has an independently authored RFC1951 walk, bounded framework expansion,
  explicit Adler checksum and exact end/trailing-data checks. Changed packets are
  uncompressed. Nonzero method is ignored for flag zero, invalid for flag one.
  Immutable fixed trees avoid rebuilding bookkeeping for hostile empty fixed blocks.
- WebP strictly accounts RIFF sizes/padding, static VP8/VP8L headers, VP8X flags/
  canvas, ICC/ALPH placement and raw-alpha size. Simple promotion is supported;
  metadata removal retains VP8X. Lossless alpha hints do not prove actual pixels.
  Animation and effective unknown-chunk edits refuse rather than flatten/drop data.
- Raw TIFF remains capped at 65,527 bytes; safely framed larger EXIF supports exact
  copying or uniquely targeted whole removal only. WebP can ingest the legacy
  Exif-NUL-NUL wrapper, retains it on no-op and rebuilds canonical raw TIFF.
- Independent metadata families, lazy semantic parsing and duplicate ambiguity.
  Unknown/malformed other-family semantics are preserved, not synchronized.
  Empty/missing-property removal is byte-identical; uniquely targeted malformed
  whole removal bypasses semantic parsing, never structural/source/safety checks.
- Source identity and result/safety/limit preflight precede writes. Stream emission
  can leave caller-owned partial data after I/O failure/cancellation. Path output
  is temporary/CreateNew/flush/close/non-overwrite publication with best-effort
  cleanup. Race-winner preservation and injected emission failures are tested.
  No crash durability, storage-wrapper alias proof or concurrent mutation guarantee.

## Independent evidence and licensing

Required reference pins: Exiv2 0.27.6, ExifTool 12.76 and dwebp 1.3.2, provisioned
separately on ubuntu-24.04. Version drift/missing tools/nonzero exits/unclassified
warnings/value or byte mismatches fail; no optional skip or widened warning list
was used to green the gate. See the earlier JPEG audit for exact report/retention
policy. The existing minimal-JPEG-default-tag warning limitation remains unchanged.

PNG tests use owned one-pixel bytes and independent bitwise CRC. Required Python
checks independently account CRC/chunks, decompress IDAT and compare encoded bytes.
WebP's four RGB/RGBA lossy/lossless fixtures derive from owned pixels encoded once
by pinned test-only Pillow 12.0.0/libwebp 1.6.0. Wheel URL/digest, input/output hashes,
recipe and relevant tool notices are in tests/fixtures/webp. No codec binary/source,
GPL tests, camera image or new runtime package is imported. dwebp independently
compares decoded PAM files before/after edits, alongside exact coded-chunk identity.
Explicit legacy-WebP EXIF ingress/rebuilt output is also read by ExifTool.

ExifTool 12.76 writes PNG XMP with a read-only xpacket trailer. The already approved
XMP profile treats that as owned opaque data: raw packet facts, exact no-op,
pre-output property refusal and whole removal are tested. Unsupported semantic
reverse property reads are explicitly recorded in report limitations, not passed
off as editable round trips. WebP reference packets are classified by the same
strict rule if a read-only wrapper is emitted. Neither toolkit is an absolute
truth oracle or a substitute for the normative profile review.

## Remaining boundaries

No full PNG/WebP/EXIF/XMP conformance, image decode/re-encode, APNG/WebP animation,
color-profile reconciliation, PNG generic text editor, IPTC mapping/synchronization,
private pointer relocation, comprehensive fuzzing proof or measured performance/
allocation improvement is claimed. Custom framing/mutation tests are finite tests,
not a security proof. Release packaging/notice/retention review remains M18.

M15 is **proposal only**: see STANDALONE_TIFF_READ_ONLY_CONTRACT_PROPOSAL.md. It
requires explicit approval of immutable classic TIFF layout/range accounting,
pointer graph/overlap rules and unsupported-layout reporting before implementation.
No M15 TIFF code, M16 writing, BigTIFF or DNG support is included in this group.
