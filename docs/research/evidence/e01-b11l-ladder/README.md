# E01 B11L — recognition-ladder observations (pinned CLI, run 42846898701)

Recovered from the CI job that built the pinned executable and ran the ladder in run `42846898701` (check-run
`129936691971`, head `a16fc0473a30dae743e1cf9922ea77c5d0ddef75`) through the annotation channel, because the
artifact host is not reachable from the environment that recovers this repository's evidence:

```
gh api repos/sensboston/MetadataSharp/check-runs/129936691971/annotations --paginate
# titles `B12Z observations.json <i>` (5 chunks) + `B12Z observations.json count` -> 5
# message = base64(zlib(json)); concatenate in index order, verify the count marker, then
# json.dumps(doc, indent=2) + "\n" -> observations.json (this file)
```

**On the two names in this directory.** The batch was committed under the number **B12**, which the master plan
allocates to the MakerNotes applicability overlay; it was renamed to **B11L** before merge, and the plan, the
census and the code are consistent again. The payloads themselves still carry the old label inside them —
`scope: "B12 recognition ladder only; no census rows, no support claims"`, the job title and the `B12Z`
annotation channel above — because those strings are what the recorded runs emitted, and rewriting a recovered
payload to fix a name would make it no longer a recovery. Three kinds of string in the payloads carry it: the
`scope` field above, `prediction.basis` — `docs/research/E01_B12_RIGHTS_AND_ORACLE_REVIEW.md`, the review this
run was gated by, whose file is now named `E01_B11L_…` — and the scratch paths echoed in the recorded stdout and
stderr, `artifacts/e01/b12-ladder/work/…`, which is the directory that run actually used. The current recorder
prints `B11LZ`, reports `scope: "B11L recognition ladder only…"`, and works in `artifacts/e01/b11l-ladder/`.
Both names describe the same 47 rungs: every derived field in these files is recomputed from the committed
fixtures by `tests/reference/test_probe_b11l_ladder.py`, so nothing here has to be taken on trust.

`observations.json` is the recorder's report verbatim in structure: one record per rung (47), one row per
rung × flag (94), each with the exit code, the verbatim stdout/stderr, `inputUnchanged`, the recorded
prediction and a per-row `matchesPrediction`. `status` is `passed`, `verification` is `not-assessed`,
`denominatorFrozen` is `false`, and `inputUnchangedEverywhere` and `noArtifactsEverywhere` are both true — a
length probe that wrote to its input would not have measured anything.

## Reproducibility: a second run, a different head, one answer

`prior-run-observations.json` is the same recorder's report from run `42848072634` (check-run
`129940874430`, head `5f58153c8261b62355b73012bc07461d412c6ca3` — the commit that added the payload above), so
the two samples come from **different commits** and **different compilations** of the same pinned source. They
agree byte-for-byte on `observations`, `summary`, `fixtureManifest`, `prediction`, `environment`,
`recordCount`, `familyCounts`, `inputUnchangedEverywhere` and `noArtifactsEverywhere`; the canonical `observations`
digest is `6d76a222f935…` in both. The two sections that differ are `baseline` and `sourceCommit`, and inside
`baseline` exactly two fields moved — `executableSha256` and `verboseVersion` — while `cmakeCacheSha256` and
`effectiveCmake` are equal, which is the statement that the *configuration policy* did not drift between the
runs. The boundary is therefore a property of the pinned build, not of one runner.

`tests/reference/test_probe_b11l_ladder.py` recomputes all of that from the two files (including the
"which sections differ" set, so this paragraph cannot go stale silently), and requires each payload's recorded
prediction to equal what `b11l_recognition_ladder.predicted_outcome` derives today — an edited-after-the-fact
expectation fails there.

## What the run showed

**1. The boundary is at 80 bytes, and it is the same boundary for both formats that were supposed to differ.**
`bmp` and `gif` each flip between 79 and 80 (`flipSize` 80, `lastRefusal` 79, `monotonic` true), and their
families agree at all 15 sizes despite different magic (`BM` vs `GIF87a`), different recognizer windows (2
bytes vs 6) and different registry positions (23 vs 20). That is what a shared upstream cause looks like: the
80-byte figure is the size of the window `isXmpType` reads before it decides
(`strlen(xmlHeader) + 1` = 55, then `80 - 55`), and its two bail-outs precede its only restore.

**2. Content does not matter, only length.** The `bmp-xmltail` control — the plain rung with `<?xml `
substituted at offset 30, inside that window — has **zero** disagreeing rows: refused at 40, reader reached at
96, exactly like its twin. No recognizer's *comparison* is being skipped; the reads before it are.

**3. Below 25 bytes the run stops being about the format at all, and that is what B11 could not explain.**
At sizes 8, 12, 16 and 20 all three ladders print the *same* message: `Failed to read input data` at 8, 16, 20
and `corrupted image metadata` at 12. The `.targa` ladder — a file that cannot be recognised by name and has
no header a BMP/GIF test would agree with — behaves identically, so the band is a property of the scan, not of
BMP, GIF or TGA. This also dissolves the counter-evidence B11L's review had to record before running it: B11's
`owned-gif-lsd-v1.gif` (13 bytes) reported `corrupted image metadata`, which no GIF code throws — 13 falls
inside the size-12 band, so that row was never about GIF.

**4. Where the pre-run model was wrong.** It placed the throw band below 26 bytes (the longest
`EpsImage::epsFirstLine` entry), but 24 already behaves like the ≥ 25 band and refuses; and it did not predict
the `corrupted image metadata` message at all, nor why 12 differs from 16. Which registry entry owns each of
those three bands is **not established** — the ladder measures sizes, and the only entries it can implicate by
name are the ones whose windows fall in the gaps.

**5. The control that could have fired, didn't.** `isTgaType` tests the file name before it touches the stream
and otherwise requires `tell() >= 26`, so a `.targa` rung would be recognised if the scan position were ever
past 26. **No** `.targa` rung reached a reader at any size, including 128 — consistent with a scan that never
advances the cursor for that entry, and it rules out the alternative that a *matched-but-null-instance* path is
what produced B11's refusals for the small files: that path needs the footer route to fire, and it never did.

## What this file is not

No support claim, in either direction, for BMP, GIF, TGA or any format in the registry; no claim that
`isXmpType` is defective (a 55-byte read that does not restore on a short file is a description of how this
code reads, not a judgement of how it should); no claim about real files, which are none of them 80 bytes; and
no census row — `docs/research/EXIV2_CENSUS.json` is unchanged at 823 rows by this batch, and the two
`tests/reference/test_b11l_*` files are what keeps that checked rather than asserted.
