# E01 B12 — recognition-ladder observations (pinned CLI, run 42846898701)

Recovered from the `pinned-exiv2-b12` job of run `42846898701` (check-run `129936691971`, head
`a16fc0473a30dae743e1cf9922ea77c5d0ddef75`) through the annotation channel, because the artifact host is not
reachable from the environment that recovers this repository's evidence:

```
gh api repos/sensboston/MetadataSharp/check-runs/129936691971/annotations --paginate
# titles `B12Z observations.json <i>` (5 chunks) + `B12Z observations.json count` -> 5
# message = base64(zlib(json)); concatenate in index order, verify the count marker, then
# json.dumps(doc, indent=2) + "\n" -> observations.json (this file)
```

`observations.json` is the recorder's report verbatim in structure: one record per rung (47), one row per
rung × flag (94), each with the exit code, the verbatim stdout/stderr, `inputUnchanged`, the recorded
prediction and a per-row `matchesPrediction`. `status` is `passed`, `verification` is `not-assessed`,
`denominatorFrozen` is `false`, and `inputUnchangedEverywhere` and `noArtifactsEverywhere` are both true — a
length probe that wrote to its input would not have measured anything.

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
BMP, GIF or TGA. This also dissolves the counter-evidence B12's review had to record before running it: B11's
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
`tests/reference/test_b12_*` files are what keeps that checked rather than asserted.
