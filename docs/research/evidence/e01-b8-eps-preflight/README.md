# B8 EPS — pinned read-only preflight

The tiny owned textual EPSF 3.0 fixture is an original header and rectangle
program, authored after format-specific rights review. Its generator and
SHA-256 manifest are under `tests/fixtures/b8/`. No PostScript was executed by
the MetadataSharp header inspector or this read-only metadata preflight. No
rendered-output oracle or full EPS/DSC conformance is claimed.

PR #47 head `3759862e2f414255fe6c555e01f32ba4e3c92ccc`, run 42713667205,
passed 15/15. All two check-run annotation chunks were recovered into
`ci-observations.json`, with the JSON hash and artifact metadata in `receipt.json`.
Artifact ZIP bytes were not retrieved. Exiv2 0.28.9 returned 1 / unsupported for
`-pS` and `-pC`, returned 0 with empty `-pa` and `-pc` output, and returned
253 with no Exif data for `-et`; none produced artifacts or changed the input.
ExifTool 12.76 reported EPS and BoundingBox `0 0 10 12`. The fixture has no
authored metadata or thumbnail; empty reads do not establish metadata-present
support. This preflight is not an E01 census closure or a safety guarantee for
executing untrusted PostScript.

The immediately preceding run 42713118803 failed the Python reference-policy
step on two Windows jobs; available annotations reported only exit 1, and logs
were not retrievable. Git checkout line-ending conversion of the text fixture
was the suspected cause, not a proven diagnosis. Explicit LF attributes were
added, and the subsequent full run above passed both Windows jobs without
relaxing the fixture hash test.
