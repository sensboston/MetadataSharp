# E01 B6 DNG — pinned CLI observations

This is a local rehearsal, not a CI artifact or MetadataSharp verification.
`observations.json` was generated using `tools/reference/observe_b6.py` at
repository commit `b1acd7ac4e6f2676c0f7ee99bdf9ca3fb532bff6`, with Exiv2
0.28.9 source pinned at `b0859fc61fea5dedfc898aece6b15893ddf4314e` and
required CMake features checked. The local Linux executable linked zlib 1.3.1
and expat 2.6.2 dynamically from the local dependency prefix; Brotli 1.1.0
and inih r59 were locally built. Effective options, executable hash, verbose
version, inputs, commands and results are in the report. The report SHA-256
is `0a4eb17f861e085732d531bf61832e6acdf90abe0da0358a1985d0308fbcd938`.
CI reruns the batch independently; no CI-artifact equivalence is assumed.

## Scope and outcomes

88 rows: four inventory, four undocumented-comment probes, and read/insert/
replace/remove for each of EXIF, ICC, IPTC, XMP and thumbnail across four
owned synthetic layouts (raw or RGB preview root; little or big endian).
The empty/present fixtures use the independently authored D1 CFA strip,
IFD0 metadata carriers and, for thumbnail read/replace/remove, a chained IFD1
with authored JPEG. The RGB preview root and IFD1 thumbnail are not the same
thing. Full DNG conformance of the IFD1 variant has **not** been independently
established, so it is a structural CLI probe, not a normative safety claim.
Fixtures and provenance: `tests/fixtures/dng/census-manifest.json` and
`census-provenance.json`. There is no duplicate-carrier or invalid-XMP grid
in this batch; its four layouts are bounded representatives, not a complete
catalogue of DNGs.

- 84 `accepted`, 4 `nonzero-exit-artifact-produced`, zero static-not-applicable.
  The four thumbnail reads (`-et`) produced the expected JPEG bytes but returned
  **137**; that is recorded separately, not called a success or refusal.
- All 88 cases had identical ordered CFA strip length/hash before and after.
  This compares stored raw bytes only. It is **not** a raw-decode oracle, does
  not prove rendered pixels or preview preservation, and does not establish
  preservation of other metadata. The raw-decode oracle gap remains a blocker-class
  limitation.
- EXIF Artist and ICC cases check the authored IFD0 bytes independently (ICC
  read checks stdout); thumbnail checks independently inspect IFD1 bytes.
  IPTC/XMP use CLI readback only and are record-only, not independent semantic
  assertions. On these authored IPTC inputs, replace changed the first keyword
  while the second remained. Comments and inventory are also record-only.
- Outcomes describe the pinned CLI on these cases, not implementation parity.
  Every census row remains `verification: not-assessed`, the denominator remains
  unfrozen, and no percentage or DNG write support is implied.
