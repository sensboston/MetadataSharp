# E01 requirements review supplement: selected report bodies

Observed 2026-11-07. Supplements, not replaces, the earlier title-only snapshot.
Sampling: 25 open issue API results sorted by comment count (PR results discarded),
plus 15 discussions ordered by last update. Selected eight issue bodies and six
initial discussion bodies for requirements analysis below. This is a biased,
non-exhaustive sample. Comments, linked source, patches, SDK/test code and linked
attachments were NOT followed/imported. No report was reproduced; reported behavior
may concern old releases and is NOT asserted to be a defect in v0.28.9. Requirements
are independently phrased, not copied implementation or test recipes. Raw bodies
stay ignored artifacts, not repository deliverables; externally supplied images
have not acquired redistribution rights merely through publication in a report.

| Source body reviewed | Independently stated candidate | Owner / independent proof |
| --- | --- | --- |
| [issue 2637](https://github.com/Exiv2/exiv2/issues/2637) | Non-ASCII filenames must work across supported OS/filesystems | E02/E15: own paths including supplementary Unicode, normalization and long-path limits; actual filesystem tests |
| [issue 2542](https://github.com/Exiv2/exiv2/issues/2542) | Editing unrelated text must not corrupt vendor-private metadata | E12/E13: own vendor-specific base/offset proof or explicit refusal; do not import linked camera image or assume M16 handles it |
| [issue 2670](https://github.com/Exiv2/exiv2/issues/2670) | Integration failures must not silently save stripped metadata or break supported consumers | E03/E07: clear unsupported/write errors and independent staged-output fault tests, not imitation of a historical regression |
| [issue 2162](https://github.com/Exiv2/exiv2/issues/2162) | Metadata discovery must resolve container item references/extents, not merely recognize HEIF | E09: specification-based generated multi-item examples, fragmented extent bounds and independent expected facts; no attachment used |
| [issue 147](https://github.com/Exiv2/exiv2/issues/147) | Ancillary text visibility/preservation and editability should be distinct capabilities | E07: own PNG tEXt/zTXt/iTXt fixtures, expansion limits and binary preservation; no blanket "PNG supported" boolean |
| [issue 1416](https://github.com/Exiv2/exiv2/issues/1416) | Date/time parse/format/round-trip rules need explicit contracts | E06/E15: own culture/timezone/precision tests. Body is only an old tracker reference, so no root cause inferred |
| [issue 1401](https://github.com/Exiv2/exiv2/issues/1401) | Invalid metadata encoding must return actionable failures before publication, not only logging | E03/E06: own invalid-name cases from XML rules; linked source/snippets not used as implementation material |
| [issue 3225](https://github.com/Exiv2/exiv2/issues/3225) | Avoid mutable process culture and specify concurrent operation boundaries | E03/E16: invariant numeric/time conversions, owned per-context state; report's crash cause remains unverified |
| [discussion 2064](https://github.com/Exiv2/exiv2/discussions/2064) | Independent documents must not contend over a global namespace-prefix registry | E03/E06: per-document namespace URI identity and independent stress fixtures; concurrent use of a single mutable stream still excluded |
| [discussion 3029](https://github.com/Exiv2/exiv2/discussions/3029) | Typed cardinality must distinguish strings, numeric vectors, RDF arrays and repeated IPTC datasets | E03/E06: explicit type/count/byte-length contracts, raw vs interpreted values and known original fixtures |
| [discussion 3019](https://github.com/Exiv2/exiv2/discussions/3019) | Raw values must be accessible separately from localized/pretty output | E06/E12: lossless primitive values with opt-in invariant or culture-specific rendering; no copied lens database |
| [discussion 3093](https://github.com/Exiv2/exiv2/discussions/3093) | Capability discovery must include optional compression/dependency availability | E03/E09: runtime capability descriptors vs per-input support decisions; no new Brotli/native dependency implied |
| [discussion 3068](https://github.com/Exiv2/exiv2/discussions/3068) | Distinguish preserving opaque invalid XMP from parsing/editing/repairing it | E06: strict secure parsing stays default; any repair/loss policy needs separate approval and unmodified-original retention |
| [discussion 3058](https://github.com/Exiv2/exiv2/discussions/3058) | Offer structured machine-readable extraction and explicit stream requirements | E15: managed result schema first, optional adapter later; no promise nonseekable full-image editing is trivial |

These are candidate improvements, not commitments that each must ship and not
claims that MetadataSharp already fixes each case. Assign approved requirements
and atomic acceptance rows before implementation. Full parity census, closed-item
resolution review and current pinned-build observations remain OPEN. No unsupported
performance, security, licensing or upstream quality ranking is drawn from counts.
