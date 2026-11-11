# E01 initial issue/discussion triage

This is a deliberately bounded discovery pass, not exhaustive review of Exiv2.
2026-11-07 API snapshot: latest-updated 15 open issues (179 total), latest-updated
10 closed issues (924 total), 10 most-commented issues (1103 total across states).
These queries produced 35 unique issue signals; selection has recency/popularity
bias. Search totals can change and do not prove software quality. Discussions:
12 latest-updated of 37, hasNextPage=true. Full metadata/provenance is in
EXIV2_DISCOVERY_SNAPSHOT.json. Titles, labels, states and discussion category/answer
metadata were reviewed; bodies/comments/patches/attachments were NOT. Closed does
not prove a defect is fixed in our pinned release; unanswered does not prove demand.
All items below are **unverified requirement candidates**, not reproduced defects.

| Signal source | Independently stated requirement / improvement hypothesis | Owner and verification needed |
| --- | --- | --- |
| [#9482](https://github.com/Exiv2/exiv2/issues/9482), [#9551](https://github.com/Exiv2/exiv2/issues/9551) | Failures, interruption and encoding errors must not silently destroy source/sidecar | E03/E07/E15; derive own fault/cancel/publication tests. Current new-path APIs help only their documented subset; no crash-atomic overwrite claim |
| [#3459](https://github.com/Exiv2/exiv2/issues/3459), [#3071](https://github.com/Exiv2/exiv2/issues/3071), [#9464](https://github.com/Exiv2/exiv2/issues/9464) | Pointer deletion, large fields and narrowing need explicit preflight | E07/E13; compare normative graph/range/size expectations; current M16 graph tests are not proof about these reports |
| [#9563](https://github.com/Exiv2/exiv2/issues/9563), [#9485](https://github.com/Exiv2/exiv2/issues/9485) | Checked nested lengths and finite graph/depth work in new JP2/CIFF readers | E08/E11; construct new boundary fixtures from specs, never copy reporter payloads/tests |
| [#9562](https://github.com/Exiv2/exiv2/issues/9562) | ICC input aliasing and buffer ownership need a managed contract | E14; no existing ICC mutation API, do not claim issue fixed here |
| [#9561](https://github.com/Exiv2/exiv2/issues/9561), [#9510](https://github.com/Exiv2/exiv2/issues/9510) | Sized text and non-finite/out-of-range numeric conversion must be explicit | E06/E07; own invalid Unicode, slice and numeric boundary cases |
| [#2637](https://github.com/Exiv2/exiv2/issues/2637) | Unicode paths and platform-specific filename behavior deserve actual integration tests | E02/E15; .NET strings alone are not proof of OS/filesystem compatibility |
| [discussion #2064](https://github.com/Exiv2/exiv2/discussions/2064), [#9514](https://github.com/Exiv2/exiv2/issues/9514) | Avoid global mutable namespace registration and specify concurrency | E03/E06; immutable per-context registry proposal, then independent stress tests; do not promise concurrent stream/document mutation |
| [discussion #3029](https://github.com/Exiv2/exiv2/discussions/3029) | Consumers need typed cardinality/array discovery, not string heuristics | E03/E06; distinguish component count, RDF containers, repeated datasets and raw byte length |
| [discussion #3093](https://github.com/Exiv2/exiv2/discussions/3093) | Report per-container/compression capability and unavailable dependencies | E03/E09; not one boolean "JXL supported"; no unreviewed dependency |
| [discussion #3058](https://github.com/Exiv2/exiv2/discussions/3058), [#9346](https://github.com/Exiv2/exiv2/discussions/9346) | Stable machine-readable results with source attribution | E15; optional .NET/CLI adapter and diagnostic schema, not cloned CLI syntax |
| [discussion #3068](https://github.com/Exiv2/exiv2/discussions/3068) | Separate opaque preservation from semantic editability for malformed/unsupported XMP | E06/E16; existing refusal remains default; no silent best-effort rewrite |
| [#3418](https://github.com/Exiv2/exiv2/issues/3418), [#2162](https://github.com/Exiv2/exiv2/issues/2162), [#1229](https://github.com/Exiv2/exiv2/issues/1229), [#1893](https://github.com/Exiv2/exiv2/issues/1893) | Modern-container metadata and previews are distinct support dimensions | E09/E14; pinned build/options and owned fixtures, not extension-only detection |
| [#1081](https://github.com/Exiv2/exiv2/issues/1081), [#1941](https://github.com/Exiv2/exiv2/issues/1941), [#2126](https://github.com/Exiv2/exiv2/issues/2126), [discussion #3255](https://github.com/Exiv2/exiv2/discussions/3255) | MakerNote generation and ambiguous lens semantics need per-family coverage | E12; independent vendor provenance and relocation proof, no borrowed tag/lens database |
| [discussion #3525](https://github.com/Exiv2/exiv2/discussions/3525), [#9495](https://github.com/Exiv2/exiv2/issues/9495) | Versioned capability/dependency docs and packaged examples should match actual build | E01/E18; published provenance, actionable errors and verified doc examples |

These hypotheses are not approved new APIs or a promise to outdo Exiv2. E16 admits
only improvements supported by a validated use case, an exact reviewed contract,
independent tests and measured costs where applicable. Threading/Unicode/transaction
safety are requirements to prove, not automatic benefits of using managed C#.
