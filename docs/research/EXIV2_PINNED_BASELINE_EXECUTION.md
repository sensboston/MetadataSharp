# E01 bounded pinned CLI execution

## Scope and independent-implementation policy

This unit provisions **Exiv2 0.28.9** at commit
`b0859fc61fea5dedfc898aece6b15893ddf4314e` as an external black box. No production
DLL, NuGet dependency or public API changes. No upstream parser implementation,
tests, fixture attachments or tag dictionary translated/read for implementation.
Only upstream README/manual build/CLI documentation and license metadata reviewed.
Source/build/executable remain under ignored artifacts/e01, never packaged/uploaded.
Existing required 0.27.6 references remain historical regression oracles unchanged.

The required Ubuntu24.04 job is a **fifteenth** gate, not a replacement for the
six platform/TFM tests, six audits or two historical reference jobs. CMake options
are defined once in tools/reference/exiv2_baseline.py and validated as recognized
BOOL/STRING cache entries (an unused -D value is not accepted). Functional options
include PNG/XMP/BMFF/Brotli/video/filesystem/lensdata/INI; remote web/curl disabled.
Record every effective upstream option, compiler, dependency package version,
linked libraries, cache/binary/license hashes and actual verbose CLI build facts.
Check exact source commit and clean tracked upstream tree before observations.

Measured verbose capability keys are `have_xmptoolkit`, `have_libz`, `have_brotli`
and `enable_bmff`. In this version there are no `enable_xmp`/`enable_png` verbose
keys: assumed names failed the first probe. Dependency presence is not alone a
format-support claim; cache enable flags are separately mandatory and format
functionality needs owned fixtures. PNG/BMFF/Brotli/video are NOT exercised by the
four JPEG cases. Build options/dependency identities are recorded, but Ubuntu apt
patches and binary builds are not asserted bit-reproducible or fully hermetic.

Full verbose output is a short-lived CI artifact; durable receipt filters build
facts and stores command-output hashes, not upstream namespace/tag catalogs. External
license SHA binds COPYING at the pinned source. No claim that all external output
is automatically MIT or GPL; generated JPEG fixture/value/hash evidence is ours.

## Four atomic observations, NOT four supported format cells

Each ID starts `EX0289-JPEG-EXIF-<OPERATION>-IFD0-ARTIST-ASCII-II-01` and links to
seed `EX0289-JPEG-EXIF`. Literal TIFF type 2, ASCII count including trailing NUL,
raw bytes and expected absence are authored independently of reference output.

| Operation | Authored generator input | Expected Artist |
| --- | --- | --- |
| read | ours.jpg, classic II, orientation=1 and Artist | `Alice` / 6 bytes including NUL; whole file unchanged |
| insert | base.jpg, no EXIF | `Alice` / 6 bytes including NUL |
| replace | xmp.jpg, EXIF plus XMP | `Bob` / 4 bytes including NUL |
| remove | xmp.jpg, EXIF plus XMP | Artist absent; other TIFF fields retained |

Fixture ownership: existing repository-authored MIT JpegFixture/ReferenceFixture;
no vendor/camera/upstream samples. Hash each exact source and output. Independent
reader is deliberately restricted to one classic little-endian IFD, ASCII/SHORT,
no MakerNote/thumbnail and this single-scan JPEG framing; NOT a general TIFF or JPEG
validator, pixel decoder or new production format implementation. Offline negative
cases are framing tests, not asserted valid images. Actual CI inputs are the existing
owned coded 8×8 grayscale fixtures used by historical external validation.

ASCII expected bytes are independent constants, not just ours-to-ours round trips
or agreement between Exiv2 print modes. Check raw field type/count/value separately
from CLI printing. Check all other IFD0 fields and scan-through-EOI bytes. Non-EXIF
segment preservation is separately inspected: APP1 is identified by signature,
not all APP1 discarded, so existing XMP/JFIF/comments/tables are visible. Byte change
is not automatically a normative corruption or accepted preservation requirement.

MetadataSharp-generated input alone is not evidence of bidirectional operation
parity. Four variant observations cannot close a whole JPEG family cell or the
186-seed/348-candidate ledger. No denominator freeze, exclusions or percentages.
All other formats/families/encodings/layouts and complete CLI/workflow census remain
OPEN; E03/public contract approval and D1 normative/oracle prerequisites unchanged.

## Execution and receipt recovery

Follow the pinned-exiv2-baseline job in .github/workflows/build-and-test.yml:
install the explicitly listed external build dependencies; checkout the exact
external SHA into ignored artifacts/e01/source; configure with OPTIONS; build only
the exiv2 executable (upstream tests/samples disabled). Source builds need SDK10
and generator net8 runtime. From repository root, set E01_SOURCE_COMMIT to the
actual 40-character source SHA using your shell, then:

```sh
python -m unittest discover -s tests/reference -v
python -m unittest discover -s tests/discovery -v
python tools/reference/build_capability_seeds.py --check
python tools/reference/exiv2_baseline.py --upstream artifacts/e01/source --build artifacts/e01/build
python tools/reference/collect_baseline.py SUCCESSFUL_RUN_ID DESTINATION
```

Missing CLI/dependency, wrong commit/version/features, command failure or unexpected
byte/field result must fail, not skip. Collector checks job identity, successful run,
complete bounded zlib/base64 annotation chunks, no duplicate/mixed counts, no trailing
stream, source binding, exact baseline and all four unique operations. Failed runs
remain failed; their bounded diagnostic annotation is not a successful receipt.

## Measured result and preservation differences

Implementation **601a20b0c1e6d6e1a78b489269f687445ca7827b**, required
[42449222307](https://github.com/sensboston/MetadataSharp/actions/runs/42449222307):
**15/15 jobs green**, including actual external source/configure/build/execution.
Complete source-bound JSON and validated small receipt are in
[evidence/e01-pinned-jpeg](evidence/e01-pinned-jpeg/). Local .NET/CMake not available;
Python policies passed locally (12 historical + 26 discovery/observation/collector).
Existing 450 .NET cases per OS/TFM and all six audits/both references still pass.

A successful observation receipt means the **documented bounded observations**
matched independent expectations, INCLUDING explicitly detected known differences.
It does **not** mean all preservation flags are true or a safe writing parity gate
passed. The read row has exact byte/order preservation; all THREE write rows have
`nonExifBytesAndOrderExact=false` and `nonExifPayloadBytesExact=false`:

| Row | Known reference differences | Independent facts retained |
| --- | --- | --- |
| read | none | Whole input unchanged, expected Artist raw bytes/print |
| insert | D001 | Expected Artist; scan and remaining segment payloads exact |
| replace | D001 + D002 | Expected Artist; scan/other IFD0; literal XMP CreatorTool |
| remove | D001 + D002 | Artist absent; complete remaining EXIF inventory is Orientation Short 1 1; scan/other IFD0; literal XMP CreatorTool |

**D001**: the exact repository-authored COM payload
`MetadataSharp authored fixture` (marker length 0x0020) becomes the same bytes plus
one NUL (length 0x0021), and segments move. Classifier accepts ONLY that exact single
owned COM replacement and preserves all other segment multiplicities/content. Not
a general NUL-stripping, arbitrary comment allowance or whole APP-family waiver.

**D002**: the owned single-property XMP packet is reserialized (packet wrapper BOM,
toolkit annotation, whitespace/padding/namespace serialization differ). Independent
stdlib XML check requires exactly one xmpmeta/RDF/Description, empty subject and
one CreatorTool=MetadataSharp text property, attribute or child syntax. Reject all
other properties/qualifiers/structures/DTDs/unknown value, accept only UTF-8. Root
`xmptk` serialization annotation is visible but not claimed byte-preserved. All
segments outside the exact known COM and single XMP block still match raw bytes
with multiplicity. This is not a general RDF equivalence or XMP preservation proof.

Default/read verifier remains strict; negative tests show permutation/known-difference
modes still reject changed/dropped/duplicated unrelated payloads. No production
writer was changed. Exiv2 output is not normative truth: these differences are not
automatically corruption, accepted target behavior or justification to modify our
preservation contract. They are visible requirements/discrepancies for E07/E16/E17,
not approved exclusions or coverage numerator entries.

## First executions and diagnostics

All original fourteen gates passed throughout. Initial 6991c02/42447844906 built
exact external CLI but failed probe. 862fad4/42448019127 diagnosed our assumed
verbose key names. 879cea0/42448200101 hit non-EXIF bytes; 371fd0a/42448387840
located ordering; 043f01e/42448594200 identified exact COM replacement; 6cc9b96/
42448793704 identified XMP reencoding. 2ed0025/42449006766 verified remove bytes but
an absent-key filtered print returned nonzero. Final probe uses documented full
EXIF inventory instead; every invoked command must still exit zero. No failed run
relabeled successful, skipped case, broad exit-code waiver or production relaxation.
Blob logs were inaccessible; explicit bounded annotations enabled diagnosis without
repeated failed downloads. Before each classification the finding/plan was recorded
in PROGRESS. Runtime key correction did not mask missing dependency/features.

## Remaining work / final gate

PR #16 final documentation/evidence head still needs its OWN fifteen-job CI before
merge. Exact final SHA/run/merge receipts are posted in that PR to avoid circular
source receipts; stored 601a20b evidence deliberately keeps its immutable identity.
E01 full census remains OPEN. This configuration disables web/curl: these optional
capabilities are **unobserved in this build**, NOT excluded from the eventual full
baseline/accepted-target denominators. Enabled video/BMFF/PNG/Brotli is likewise not
functional fixture coverage. Continue format/metadata/operation/encoding/layout and
CLI workflow inventory with fixture rights and independent results, then submit the
finite denominator/residual gaps for review. E03 remains a separate exact contract
proposal; unchanged D0 is approved, D1 still blocked by normative/oracle/E03 gates.
