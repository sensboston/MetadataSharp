# DNG D1 fixtures and independent oracles

Status: **Gate 2 of D1 (D0 §5) closed by this document.** Records fixture
provenance, the independent oracle minimum, and the one oracle gap that is
accepted deliberately and must never count as validation. This document changes
D1 gates, not the D0 contract.

## 1. Owned synthetic fixtures

Four owned, synthetic, minimal DNG files in exactly two shapes × two byte
orders. No real camera file, no Adobe sample, no imported corpus. Pixel data is
author-chosen constants: the raw image is 4×2 16-bit unsigned values
`0x0000,0x1000,...,0x7000`; the thumbnail is 4×2 RGB bytes `0x00..0x17`.

| File | Bytes | SHA-256 | Shape / byte order |
|---|---|---|---|
| `dng-raw-ii.dng` | 598 | `474952a49242556b9bec981483df2afc9c06546a0c7705a529f789c9ad52d2a0` | raw-rooted, II |
| `dng-raw-mm.dng` | 598 | `64dee07029c8ed6fb9fcddac5a578ec79141789e6834fdf22809f8bca08685f8` | raw-rooted, MM |
| `dng-thumb-ii.dng` | 1040 | `887ae87d6d1513c0d26ba6e5dcaf89bc7a027f086d3bc4ed1bb43f68f7ab2bd5` | RGB thumbnail root + SubIFD raw leaf, II |
| `dng-thumb-mm.dng` | 1040 | `51cb2c3dcba0a96c3946f0e789b2a5623a0ec65e05a61a482e8bf8367a62381c` | RGB thumbnail root + SubIFD raw leaf, MM |

Pinned layout (both byte orders): header 8 B; first IFD at 8; raw-rooted shape
= one 31-entry IFD (body 378 B), external payloads two-byte aligned in tag
order, raw strip at 582, file length 598. Thumbnail shape = 21-entry thumbnail
IFD0 (RGB, NewSubFileType 1) then 24 B RGB strip at 426, raw leaf IFD at 450
(31 entries, identity DNG tags duplicated at both levels), raw strip at 1024,
file length 1040. Absolute offsets are asserted by unit tests, so any layout
drift fails loudly.

Two independent, deterministically generated writers produce these bytes:

1. `tools/reference/dng_fixtures.py` (Python stdlib, CI reference job),
2. `tests/MetadataSharp.Tests/Dng/DngFixture.cs` (C#, linked into the test
   assembly and `tools/MetadataSharp.ReferenceFixture`).

`tools/reference/harness.py` rebuilds all four files with the Python writer and
requires byte equality with the C#-written outputs; mismatch fails the
reference job. The offline unittest `tests/reference/test_dng_fixtures.py` pins
lengths, SHA-256, offsets, tag order and structure without external tools.
`tests/fixtures/dng/manifest.json` + `provenance.json` commit the receipt
(pinned bytes, rights statement, recipe); the `.dng` binaries themselves are
not committed — the builders are the source of truth.

A TIFF-with-DNGVersion fixture is explicitly **not** used (D0 §5).

## 2. Independent oracles (minimum two, complementary)

| # | Oracle | What it proves | How it is pinned |
|---|---|---|---|
| 1 | Python structural oracle (inline `dng_check` in `harness.py`): classic magic 42 II/MM, sorted unique tags, two-byte payload alignment, exact strip byte counts, DNGVersion `01 01 00 00` on the raw leaf, CFA photometric 32803 on the leaf | File framing and arithmetic independent of both writers and of the library under test | Code review of `harness.py`; runs inside the required `external-reference` job on every push |
| 2 | ExifTool 12.76 (`-j -n` inventory assertions: DNGVersion, ImageWidth, ImageLength, Compression, SamplesPerPixel, PhotometricInterpretation) | An established independent metadata tool reads the owned fixtures as DNG 1.1.0.0 with the expected geometry | apt-provisioned pinned version; version checked exactly in `harness.py` (`check_version`); historical regression oracle, not a DNG 1.7 conformance proof |
| 3 | `MetadataSharp.ReferenceFixture inspect-dng` (built from the same commit) + C# unit tests asserting known-byte SHA-256, absolute offsets and mutation boundaries | Library-level structural accounting agrees with oracles 1–2 on the owned corpus | `.github/workflows/build-and-test.yml` `external-reference` job; xunit suite |

Exiv2 0.27.6 remains pinned for the JPEG/PNG/WebP/TIFF reference rows and is
deliberately not an oracle for the DNG profile: its minimal-DNG inventory
output is not contract-pinned here, and adding it would need a separate
pinned-output review. Tracked as a future enhancement, not a blocker.

## 3. Oracle gap — raw decode (blocker-class, accepted)

**No raw-decode or DNG-conformance oracle exists for this profile.** Exiv2 and
ExifTool are metadata oracles; no Adobe DNG SDK, LibRaw wrapper, or vendor
validator is permitted in this repository, and none may be added privately to
make this gap disappear quietly. D1 therefore verifies: framing, field shapes,
directory graph, strip byte accounting and accounted ranges — and **never
pixel decode/render fidelity**. Any claim of "DNG decoded correctly" is out
of scope for D1 and for its tests; proposing decode work requires a new gate,
a new contract and explicit approval. This gap is recorded as blocker-class for
rendering claims and is not a skip that counts as successful DNG validation.

## 4. CI wiring

`.github/workflows/build-and-test.yml`, `external-reference` job (Ubuntu,
Docker-free, `LC_ALL=C`):

1. `dotnet build` Release of `tools/MetadataSharp.ReferenceFixture` for
   `net8.0` and `net10.0` (matrix via `METADATASHARP_REFERENCE_TFM`).
2. `tools/reference/harness.py --required` builds fixtures, cross-checks the
   two writers byte-for-byte, runs oracle 1 inline, runs oracle 2 assertions on
   `dng-raw-ii.dng` and `dng-thumb-ii.dng`, runs `inspect-dng` assertions,
   and writes `artifacts/reference/report.json` (artifact uploaded on every
   run; `report.json` records the SHA-256 of the recipe files including
   `dng_fixtures.py` and `DngFixture.cs`).
3. Non-zero tool exits, unclassified warnings and any byte mismatch fail the
   job; the report is the receipt.

Unit tests (`tests/MetadataSharp.Tests/Dng/`) run in all matrix jobs including
`net8.0` and `net10.0`; the CI "tests actually ran" floor (≥ 450 TRX) is kept
green by the DNG suite.

## 5. Fixture integrity rules

Hashes and offsets are immutable once merged, except through a new gate that
regenerates them deliberately. Fixture drift (generator change without re-pin)
fails `test_dng_fixtures.py`, the C# known-byte tests and the reference job.
The owned fixtures are MIT, authored here; they never derive from Adobe
specification text, SDK output or vendor samples. Do not add camera-originated
DNGs to this corpus without a new gate and explicit approval.

## 6. Maintenance

Oracle versions live in `harness.py` (`EXPECTED_EXIFTOOL`) and the workflow;
this document references, never duplicates mutable pins. If ExifTool is
unavailable at a given pin, the reference job fails closed (required mode),
it does not skip the DNG rows silently.
