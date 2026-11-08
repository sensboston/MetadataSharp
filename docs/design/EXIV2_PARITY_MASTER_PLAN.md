# Programme 2 execution plan: from current scope to Exiv2-level capability

Status: **planning record for the directive of 2026-11-08** (“bring MetadataSharp
functionality up to Exiv2; avoid external dependencies where possible”). It does
not approve any public contract, implementation or exclusion. Where it conflicts
with an approved contract or `CURRENT_STATE.md`, the approved record wins and this
document must be corrected, not worked around.

## 1. What the directive means in this repository

Binding interpretation, consistent with
[EXIV2_PARITY_AND_DOTNET_PROGRAM](../roadmaps/EXIV2_PARITY_AND_DOTNET_PROGRAM.md):

1. **User-visible parity, not implementation or binary parity.** Near-complete
   coverage of the EXIF/IPTC/XMP/ICC/comment/preview/thumbnail operations across
   the documented v0.28.9 format set, expressed as idiomatic managed .NET APIs.
   Not C++ ABI/class-hierarchy/CLI syntax cloning, not GPL code, not bug-for-bug
   compatibility.
2. **The denominator comes first.** No "100%" claim is valid until E01 freezes
   the reviewed atomic ledger and the acceptance corpus. Counting today's four
   JPEG observations, or the 186 seeds/348 candidates, would fabricate progress.
3. **Refusal remains a first-class outcome.** Where a safe write or supportable
   read cannot be established, the correct result is a documented refusal, not a
   best-effort pass. A refusal never closes a parity row; it stays visible.
4. **Independent implementation stays binding.** Algorithms derive from laws and
   specifications; pinned Exiv2 is a black-box differential oracle only. No
   translation of implementation, tests, tag dictionaries or attachments.
5. **No new public contract without explicit approval.** This plan sequences the
   approvals; it does not grant them.

## 2. External dependency policy (binding for this goal)

Goal: **zero third-party runtime dependencies** in `MetadataSharp.Core` and
`MetadataSharp.Formats` for the whole programme. Current measured state (2026-11-08):
neither project declares a single `PackageReference`; only the test project has
four (xUnit, private test assets — not shipped). Audit tools have none.

Rules:

- Implementation uses the .NET BCL only (spans, BinaryPrimitives, XML, streams,
  crypto for digests, compression where legitimately needed — already used for
  PNG zlib). No P/Invoke, no native bridges, no COM, no embedded Exiv2/LibRaw/
  libexiv2-cli or other executables in packages.
- No *analyzer* or *source-generator* packages either; API-surface control is
  enforced by the reflection audit in `tools/MetadataSharp.Audit` (see E03
  proposal), keeping the dependency count at zero end to end.
- Test-only dependencies are permitted (xUnit) and stay private; they are never
  package dependencies.
- Candidate exception process (rare, e.g. a reviewed permissively licensed XML or
  zerocompress component, if BCL proves insufficient): named need, license and
  maintenance review, per-TFM verification, notice inclusion, **explicit developer
  approval in a contract proposal** — never as a drive-by addition. Default answer
  is “write it or refuse.”
- External tools (Exiv2 CLI, validators) remain **CI-only black-box oracles**;
  never dependencies of the library, never bundled.
- Deterministic behavior requirement: capability reporting must not depend on
  machine-specific optional native components (see
  [EXIV2_REQUIREMENTS_TRIAGE](../research/EXIV2_REQUIREMENTS_TRIAGE.md),
  discussion #3093 — report per-container/compression capability honestly).

## 3. Where we actually are (measured, not aspirational)

Current focused surface (2026-11-08): ~4.4k lines of production code; 43 public
types; zero runtime dependencies; dual-targeted net8.0/net10.0; 450 tests per
six OS/TFM combinations; reflection-signature audit hash `a9cd2ae9…`.

Coverage labels from
[EXIV2_CAPABILITY_BASELINE](../research/EXIV2_CAPABILITY_BASELINE.md) (P =
partial concrete implementation, N = none, I = inventory):

| Family / area | Status today | Target owner |
| --- | --- | --- |
| JPEG EXIF/IPTC/XMP | P: restricted, addressed editing | E06/E07/E15 |
| PNG | P: eXIf/XMP static subset | E07/E14 |
| WebP | P: static EXIF/XMP | E07/E14 |
| Classic TIFF | P: layout accounting + reacted descriptive tags | E07/E13 |
| XMP sidecars | P: bounded RDF subset | E06/E15 |
| Generic values / tag vocabulary | P: raw model only | E06/E12 |
| DNG | N (rejected) | E04/D0 approved; E05 gated |
| Vendor RAW (ARW/CR2/NEF/ORF/PEF/SRW/…) | N | E10/E12/E13 |
| Legacy CRW/CIFF, recognition-only (BMP/GIF/TGA), video | N | E11 |
| JP2/PSD/PGF/EPS/EXV/DCP | N | E08 |
| AVIF/HEIF/HEIC/CR3/JXL | N (BMFF build option) | E09 |
| Comments / ICC / previews / thumbnails | N general APIs | E07/E14 |
| MakerNotes | N | E12 |
| Workflows (timestamp/copy/fix/CLI) | N | E15 |

Reading this honestly: the current library is a correct, safe **subset**, not a
partial clone of Exiv2 with most boxes ticked. Parity is a programme of dozens
of gated units, not one milestone. The order below respects the binding rule
“E01 splits umbrella slots into finite prompts before any code” and “approval
before implementation.”

## 4. Subtask split of the parity goal

IDs are planning identifiers for proposals/PRs; they are not claims of scope
approval. “Approval?” lists the required gate before implementation.

| ID | Bounded deliverable | Depends on | Approval? |
| --- | --- | --- | --- |
| E01-CEN | Finite atomic denominator design: schema for variant rows (encoding, layout, duplicates, sidecars, ICC/comment/preview presence), fixture provenance workflow, and the proposed split of the 31×6 grid into per-format execution batches; two denominators (full baseline + accepted-target) — proposed in [EXIV2_E01_CENSUS_AND_DENOMINATOR_PROPOSAL](EXIV2_E01_CENSUS_AND_DENOMINATOR_PROPOSAL.md) | E01 pinned build (done) | Scope only; no code |
| E03 | **Exact .NET architecture & compatibility contracts** — see [DOTNET_ARCHITECTURE_AND_COMPATIBILITY_CONTRACT_PROPOSAL](DOTNET_ARCHITECTURE_AND_COMPATIBILITY_CONTRACT_PROPOSAL.md) | E02 (done) | Yes — before any refactor |
| E05/D1 | Approved narrow read-only DNG (unchanged D0 contract) | E02, E03, normative ledger, owned fixtures/oracles | D0 approved; impl gated |
| E06a | EXIF/GPS/Interop typed vocabulary: names, enum
subtypes, rational/date handling, card invariants; independent provenance table | E03 | Yes |
| E06b | IPTC encodings/repetitions breadth beyond current UTF-8 subset | E03 | Yes |
| E06c | XMP structures/qualifiers/arrays beyond bounded profile; valid-vs-opaque distinction | E03 | Yes |
| E07a | JPEG gaps: ICC/comments/thumbs/EXIF variant layouts, Extended XMP policy, duplicates | E03, E06 | Yes |
| E07b | PNG gaps: ICC, text breadth, thumbnail ops | E03, E06 | Yes |
| E07c | WebP gaps: ICC/thumbnail breadth | E03, E06 | Yes |
| E07d | TIFF EXIF dependency review beyond standalone accounting: full schema read breadth | E03, E06 | Yes |
| E08 | Per-format non-RAW: JP2, PSD, PGF, EPS, EXV, DCP — one PR each, owned fixtures each | E03, E01-CEN batch | Yes, per format |
| E09 | BMFF read-only: AVIF/HEIF/HEIC/CR3 + JXL container vs codestream; optional-compression gating; rights review each | E03, E01-CEN batch | Yes, per format |
| E10 | TIFF-derived RAW read-only: ARW, CR2, NEF, ORF, PEF, SRW, MRW, RW2, SR2, RAF — one unit per family | E03, E10-normative per family | Yes, per family |
| E11 | Legacy CRW/CIFF; BMP/GIF/TGA recognition; video inventory reconciliation (MOV/MP4/MKV/AVI/WAV/ASF/…) then read-only contracts | E03, rights review | Yes |
| E12 | MakerNotes/lens per-vendor coverage; unknown schemes stay refused; no imported databases | E03, per-vendor provenance | Yes, per vendor |
| E13 | Extended safe editing: D2 proposal first (DNG write), then per-format editing gates; retain visible blocked rows | E05, E06–E12 per target | Yes, per unit |
| E14 | Previews/ICC/thumbnail extraction+insertion breadth across applicable containers | E03, E07–E10 | Yes |
| E15 | Workflow equivalents: timestamp adjust, copy, transfer mappings, fix ops, Unicode paths, optional machine-readable adapter/CLI — explicit loss/sync/overwrite policies | E03, E06–E07 | Yes |
| E16 | Selected evidence-backed improvements (threading, capability diagnostics, transaction failure) — proposals with independent tests, no “better than Exiv2” claims | E17 inputs | Yes, per item |
| E17 | Full reconciliation: frozen denominator, two-way reference facts, independently justified differences, fuzz/limits, approved residual gaps | All above | Yes |
| E18 | Release readiness: docs/examples, versioning, notices, packages, symbols, reproducibility, six-target checks, perf deltas; publication needs separate approval | E17 | Yes + publication approval |

Grouping is allowed with separate per-unit records and gates; it never bypasses a
predecessor or an approval.

## 5. Required order of work

1. **E03 approval round** (proposal submitted with this plan). Until approved, no
   public surface changes and no refactor — this is the binding stop rule.
2. **E01-CEN**: freeze the census schema and propose the first batch of
   format/family execution PRs. Until the denominator is reviewed, no parity
   percentage exists and no “feature-complete” claim is possible.
3. Implementation batches in dependency order (E06 → E07 → E08 → E09 → E10 → E11
   → E12 → E13 → E14 → E15), each: proposal → explicit approval → implementation
   → cross-platform CI + differential reference jobs where applicable → rights
   review → docs/progress update → merge only green.
4. **E17** reconciliation to close the denominator; **E18** release audit.

The pinned 0.28.9 CLI stays the differential oracle throughout; the 0.27.6 CI
references remain historical regression gates, never parity proof.

## 6. Definition of done for the parity goal

- E01 denominator frozen and reviewed by the developer (two numbers: full
  baseline applicable behaviors; accepted-target applicable behaviors after
  reviewed exclusions).
- Every denominator row has an ID, normative source, baseline observation or an
  explicit “not run/not applicable with reason”, fixture with recorded rights,
  independent expected result, MetadataSharp status and test evidence; unknown
  rows stay visible.
- All writes: preservation guarantees independently tested per container/layout;
  refusals counted as refusals, never as support.
- Zero third-party runtime dependencies (or each approved exception listed in
  the release notes with license/notices).
- Two-way reference facts gathered; differences individually classified
  (our defect / reference defect / unsupported / formatting variation).
- E17 reconciliation and E18 release audit signed off by the developer, with
  package publication authorized separately.

## 7. What this document is not

- Not approval of any listed unit, signature, enum member, format or exclusion.
- Not a schedule or estimate; the sequence is dependency-driven.
- Not a change to any safety refusal, limit, ownership rule or approved contract.
- Not a parity claim of any kind.
