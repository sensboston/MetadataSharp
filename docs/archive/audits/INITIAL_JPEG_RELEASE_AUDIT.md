# M09: Initial JPEG EXIF Audit and Remaining Release Gates

## Scope and evidence (2026-11-07)

M01–M09 implement the approved restricted JPEG EXIF slice, not the entire roadmap.
M07 passed run 42420076893, M08 passed 42420352483, and corrected M09 passed
[42420756667](https://github.com/sensboston/MetadataSharp/actions/runs/42420756667)
at commit `cbb0a2b8a76b53bec53c3029da70b8e8ef72327d`: three OS builds/tests
(192-case minimum), offline harness tests, and the required external-reference job.
Final documentation/evidence hardening still requires a green final-head run before
merging PR #6. Package publication is not authorized or performed.

## Reproduction and provisioning

Use a serviced .NET 8 SDK, Python 3 without `-O`, and the exact upstream CLI
versions **Exiv2 0.27.6** and **ExifTool 12.76**. The reference CI job uses
`ubuntu-24.04` and Ubuntu packages `exiv2` / `libimage-exiftool-perl`. Apt packaging
revisions may receive security updates; they are recorded, not frozen to an old
binary. The harness rejects upstream version drift. Update a pin only in a
reviewed change with discrepancy revalidation; never replace a failed check with
an optional skip. SDK servicing and OS images are not bit-for-bit pinned.

From a clean checkout with an unused output directory:

```sh
dotnet build MetadataSharp.sln --configuration Release
python -m unittest discover -s tests/reference -v
python tools/reference/harness.py --required --output artifacts/reference
```

Without `--required`, missing tools produce an explicit `skipped` report, not a
validation pass. CI always uses required mode. Missing tools, mismatched versions,
nonzero commands, unexpected values, unclassified warnings, validation errors or
byte differences fail. Offline tests exercise these policy boundaries, including
main-path skip/failure and nonzero-exit evidence. Optimized Python is refused so
assertions cannot be silently disabled. Do not reuse an output directory containing
fixtures: the public path writer deliberately refuses overwriting destinations.

`report.json` records status, locale C, source commit, recipe hashes, tool versions,
argv/exit/stdout/stderr, classified warnings and SHA-256 fixture manifests. CI also
records package revisions, installed package copyright notices and `dotnet --info`.
Evidence and per-platform TRX files are retained as Actions artifacts for seven
days. Before a release, archive the chosen run evidence under a release retention
policy; current CI artifacts are not a permanent archival service.

## Fixture provenance and license boundary

All JPEGs are generated from the repository's authored `JpegFixture.cs` and the
public-API console consumer `tools/MetadataSharp.ReferenceFixture/Program.cs`.
They contain an 8x8 synthetic grayscale image and fixed synthetic text, not a
camera photo, personal metadata, imported corpus or external reference test.
The generator and fixtures are covered by the repository MIT license. Each output
is hashed and attributed in the manifest; reverse-edit outputs derive from those
same owned inputs. No binary fixture or reference-tool implementation is vendored.
Generated output and Python caches remain ignored.

Exiv2 is invoked only as an external GPL CLI black box. ExifTool is similarly an
external CLI under its installed distribution's Perl license terms; retain/review
package copyright notices when redistributing tools. Neither tool is linked,
packaged, translated or a production dependency. Tool output is not automatically
GPL, but output/fixtures still need their own content-rights review. Future fixture
imports require origin, author/license/redistribution permission and hash review
before admission. Never copy GPL implementation or tests into MIT source.

## Independent checks and discrepancy disposition

- Both tools read Artist `Alice` and numeric Orientation `1`; Exiv2 additionally
  verifies raw type/count. Its `-P kycv` means key/type/count/plain value; interpreted
  display strings are not an oracle. The initial incorrect `ktcv` flags failed
  run 42420600312 and were corrected, not worked around by dropping type checks.
- Each tool writes a different Artist to a copy; the public MetadataSharp reader
  reads it and its no-op writer must preserve that external result byte-for-byte.
- Authored no-op is exact; whole EXIF removal equals the original base JPEG;
  SOS-through-EOI bytes remain identical across authored and external edits.
  The tail locator is deliberately fixture-specific, not a second generic parser.
- ExifTool structural validation produced **Missing required JPEG IFD0 tag
  0x011a XResolution**, recorded as `documented-minimal-exif-default-tag`.
  This is a real minimal-EXIF conformance limitation, not proof that the tool is
  wrong. The exact tag/name pairs XResolution/0x011a, YResolution/0x011b,
  ResolutionUnit/0x0128 and YCbCrPositioning/0x0213 are the only reviewed category;
  only the first warning was observed in the passing run. No arbitrary missing-tag
  warning is accepted. Do not describe this generated minimal EXIF as fully
  standards-conforming. A stricter creation profile needs reviewed defaults and
  tests, not a broader warning suppression.
- Values and preservation checks passed after normalization correction. An external
  tool is independent evidence, not absolute truth. New discrepancies require a
  recorded specification/fixture/implementation/tool diagnosis before acceptance.

## Safety and API audit

The approved addressed EXIF edit model, conservative relocation refusals, borrowed
versus owned streams, actual async I/O, cancellation, SHA-256 source preflight,
CreateNew temporary path publication and failure cleanup are implemented and tested.
See the M07/M08 design documents for exact behavior. Premature EOI was corrected
to InvalidJpeg during this audit and has a regression case. No safety refusal was
relaxed for external-tool compatibility.

Remaining restrictions: structural single-scan baseline JPEG only; no pixel/MCU
validation; no progressive/multiscan support; strict duplicate/signature policies;
raw reviewed writable tag shapes rather than full date/GPS/domain semantics;
no final Exif 2.32 or complete Exif 3 conformance claim; no general MakerNote/opaque
pointer relocation. Unknown unsafe metadata can be copied unchanged or removed
whole, not silently relocated. Caller streams can contain partial output after
I/O failure/cancellation; distinct wrappers aliasing storage are a caller obligation.
Whole EXIF removal is not removal of XMP/IPTC/other personal metadata.

Core/Formats have no runtime NuGet dependencies; the console tool adds none.
Existing test-only packages remain outside the production dependency boundary.
Packaging is disabled. There is no measured performance/allocation claim, fuzzing
campaign, broad real-camera corpus validation or security certification.

## Release checklist

Completed in this scope:
- [x] Approved M00 contracts; M01–M08 implementation gates and cross-platform tests.
- [x] Owned reproducible fixtures, raw/semantic comparison and reverse-tool edits.
- [x] Required external CI with exact upstream pins and explicit skip/failure policy.
- [x] Preservation/failure regressions; discrepancy review and documented limitations.
- [x] No imported GPL source/tests or new production package dependency.

Required before publishing a package (not authorized by this milestone):
- [ ] Archive final-head run, manifests, package notices and TRX beyond seven days.
- [ ] Decide/document whether minimal-EXIF warning profile is acceptable for a release,
      or implement and independently validate a stricter approved creation profile.
- [ ] Review NuGet identity/version, public API/XML docs, package contents, license
      notices and dependency/security inventory; enable packaging only after review.
- [ ] Establish reproducible package/release checks and user-facing release notes.
- [ ] Complete planned M18 performance/allocation measurements before such claims;
      decide additional hostile-input fuzzing and fixture/camera coverage requirements.
- [ ] Obtain explicit publication approval. M10+ still needs new scope/contracts.
