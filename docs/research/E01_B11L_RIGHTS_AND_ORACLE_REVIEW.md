# E01 B11L — recognition-ladder rights and oracle review

Status: authored, run, and answered — see "What the run showed" at the end for the outcome and for the two
places the model below was wrong. Batch B11L is a **diagnostic batch**: it adds no census rows, no seed cells and
no capability claims, and it changes nothing in `docs/research/EXIV2_CENSUS.json`.
Baseline reviewed: Exiv2 `b0859fc61fea5dedfc898aece6b15893ddf4314e` (`v0.28.9`), build configuration
`tools/reference/exiv2_baseline.OPTIONS` (`EXIV2_ENABLE_VIDEO=ON`, `EXIV2_ENABLE_BMFF=ON`, `EXTERNAL_XMP=OFF`).
Reviewed on 2026-11-10 against sources fetched from that tag; the fetched files are not committed, so every
fact this document relies on is cited by path and line below and must be re-read from the pinned tree to be
re-checked.

## What B11L is, in one paragraph

B11 closed with a finding it deliberately did not explain: six small fixtures whose magic test *does* match at
offset 0 were still handed to no reader (`kerFileContainsUnknownImageType`), and B11 named two candidate causes
— the `Image::good()` re-check inside `newXInstance`, and "an earlier registry entry matching first" — while
saying that a padded-BMP follow-up "belongs to the next batch"
(`docs/research/E01_B11_RIGHTS_AND_ORACLE_REVIEW.md:615-624`). This document *is* that follow-up's gate. B11L
authors a **length ladder**: the same valid header prefix at total sizes 8, 12, 16, 20, 24, 40, 54, 55, 56, 64,
79, 80, 81, 96 and 128 bytes, for three recognizers, and observes which size flips the outcome. Length is the
one variable a black-box probe can control exactly, and the candidates make different predictions about it, so
the flip point — not any statement about BMP or GIF support — is the deliverable.

## Why two candidates are not enough, and what the third one is

Re-reading the pinned source for this review changed the question. The two causes B11 named cannot between them
produce the observed pattern:

* `Image::good()` (`src/image.cpp:713-718`) is only consulted *after* an entry matched, by that entry's
  `newInstance_`. For `owned-bmp-dib.bmp` no entry matched, so `good()` never ran and cannot be the cause. It
  also cannot explain a scan failure in general, because it **reopens the stream** first: `FileIo::open()`
  (`src/basicio.cpp:499-512`) closes and `fopen`s again, which resets the position to 0 and clears the eof
  indicator. A `good()` re-check therefore re-tests magic from a clean stream and would *pass*.
* "An earlier registry entry matching first" requires an earlier entry whose test matches `'BM'`, `'GIF87a'` or a
  44-byte TGA footer. None does: the 30 registry entries (`src/image.cpp:75-116`) before `bmp` at index 23 test
  for their own tags, and B11's own container-facts table records no match for them.

What neither candidate covers is the **stream position and flags** left behind by an earlier entry. Fifteen of
the seventeen recognizers whose bodies this review could read are written in one shape:

```
iIo.read(buf, N);
if (iIo.error() || iIo.eof()) return false;      // <-- returns without restoring
… memcmp …
if (!advance || !matched) iIo.seek(0 - N, cur);  // <-- the only restore
```

For a file shorter than `N`, `fread` short-counts and sets the eof indicator (`src/basicio.cpp:542-547`), so the
guard fires and the function returns **without seeking back**: the cursor is left at end-of-file with eof set, and
every later entry with the same shape reads 0 bytes and returns false without ever comparing magic. The review
found this shape in `bmpimage.cpp:119` (N=2), `cr2image.cpp:137` (16), `crwimage.cpp:139` (14),
`gifimage.cpp:75` (6), `jpgimage.cpp:1008` (2), `jpgimage.cpp:1055` (7, exv), `mrwimage.cpp:128` (4),
`orfimage.cpp:147` (8), `pgfimage.cpp:296` (3), `pngimage.cpp:699` (8), `psdimage.cpp:688` (6),
`rafimage.cpp:339` (8), `rw2image.cpp:203` (24), `tiffimage.cpp:247` (8) and `src/xmpsidecar.cpp:156-198`
(55, then 25 — both guards precede the single restore at the end). Two are clean: `isEpsType`
(`src/epsimage.cpp:1102-1133`) seeks back explicitly when the read comes up short, and `isJp2Type`
(`src/jp2image.cpp:875`) has no guard. `isTgaType` (`src/tgaimage.cpp:99-122`) is a third shape again: it tests
the *path* for `.tga`/`.TGA` first — ignoring stream state entirely — and otherwise returns false as soon as
`iIo.tell() < 26`.

The consequence is a **staircase, not a single boundary**: the entry that strands the cursor is the earliest one
whose window exceeds the file size, so a format at registry index *k* is invisible below `max(window of entries
0..k-1)`. For `bmp` (23) and `gif` (20) that maximum is `xmp`'s 80-byte window; for `tga` (22) it is also 80.
That is a specific, falsifiable prediction with three regimes, and the ladder is sized to cross all of them:

| rung size | predicted family (any of the three ladders) | why |
|---|---|---|
| 8–24 | the run **throws inside the scan** | `isEpsType` (index 18) reads through `DataBuf FileIo::read(size_t)` (`src/basicio.cpp:530-540`), which throws when the request exceeds the file size or yields nothing; below 26 bytes it does for every format |
| 25–79 | `<path>: The file contains data of an unknown image type` | `isXmpType` reads 55 then 25, strands the cursor at its first guard, and blinds entries 19–29 including gif/tga/bmp |
| ≥ 80 | a reader is reached | no entry before them strands, so `isBmpType`/`isGifType` compare magic at position 0 |

The `.targa` ladder is the control that makes the experiment worth running: `isTgaType`'s name test ignores the
stream, so *in a strand-free world* a `.targa` file with a footer would still be refused by the position test —
and in a stranded world it is refused too. Predicted **flat** across all 15 rungs; if any `.targa` rung is ever
accepted, the scan position was not 0 and stranding is both real and wider than `xmp`. The second control is
`bmp-xmltail`: the plain rung's bytes with `<?xml ` substituted at offset 30 — inside the 55-byte window
`isXmpType` reads before it decides, and past the 26 bytes `BmpImage::readMetadata` consumes, so the only axis
that moves is what a *recognizer* sees and not whether the DIB is valid (a plant inside the header would confound
recognition with header validity). Under the strand model content is never compared, so this rung must behave
exactly like the plain BMP rung at the same size: refused at 40, reached at 96. If it is reached at 40, the
mechanism reads bytes rather than length, and this document's model is wrong.

## One B11 row the model does not explain, stated before the experiment

`owned-gif-lsd-v1.gif` is 13 bytes and reported `corrupted image metadata`. Under the model above a 13-byte file
strands at `cr2` (index 2, window 16) and then `eps` throws at index 18, so the predicted family is
`throws-during-scan`; `kerCorruptedMetadata` is not thrown by `gifimage.cpp`, `bmpimage.cpp`, `tgaimage.cpp` or
`xmpsidecar.cpp` at all — in the sources this review read, it appears in `image.cpp` only inside
`Image::printIFDStructure`/`printTiffStructure` (`src/image.cpp:309-515`) and in the webp, jp2, psd, png, raf and
pgf readers. So this row is evidence that **something** reached a reader or a structure printer for a 13-byte
GIF, and the strand model does not say what. The 6-byte GIF twin (`Failed to read input data`,
`kerInputDataReadFailed` at `src/basicio.cpp:536`) *is* consistent with a strand: it is the "read yielded
nothing" throw in `FileIo::read(size_t)`, reachable from `isEpsType`, which sits after the entry that would have
stranded it. Recording the contradiction here, before any fixture exists, is what keeps the ladder from being
presented as a confirmation exercise: a flat refusal to flip at 80 for the GIF ladder is a real possibility and
would mean the model above is wrong about the format it was built to explain.

*(Resolved after the run, and not by the model: the same message appears at size 12 for **every** ladder,
including the `.targa` twin, so that row was a length observation in a GIF filename — see "What the run
showed", point 3.)*

## Rights

Every byte in `tests/fixtures/b11l/` is generated by `tools/reference/b11l_recognition_ladder.py` from
`struct.pack` and zero fill. There is no third-party sample, no camera capture, no vendor test file, no rendered
image, no decodable media and no embedded artwork: a 54-byte `BITMAPINFOHEADER`, a 13-byte GIF header, an
18-byte TGA header plus its 26-byte footer, and zeros. Consequences reviewed:

* **Copyright and licence.** Header field layouts are functional data; nothing here copies an expression — no
  author's file, no upstream `samples/` file, no Exiv2 test asset. The upstream repository's own small-format
  fixtures are therefore *not* used, and none of these bytes would match one bit for bit: pixel dimensions
  (4×2, `planes=1`, `depth=24`), the declared file size, and the zero-filled bodies are this module's choice.
* **Trademarks.** `'BM'`, `'GIF87a'`, `'GIF89a'` and `'TRUEVISION-XFILE'` are format signatures used for
  interoperability in a read-only probe; no file is presented as being from, endorsed by, or produced by any
  vendor, and the file names carry the `owned-` prefix this repository uses for authored fixtures.
* **Personal data, secrets, geolocation.** None: there is no metadata carrier in any B11L fixture. This is a
  stronger position than B11's, where the CRW rows needed a vendor-private CIFF discussion; B11L authors no
  Exif, IPTC or XMP at all, so no `blocked-rights` cell is needed and none is invented.
* **Repo hygiene.** 47 rungs × ≤ 128 bytes plus one manifest, all committed; no executables, no build output,
  no artifacts directory in the tree.

## Oracle review: what may be compared, and with what

* **No external oracle is used, and ExifTool is deliberately absent from B11L.** ExifTool 12.76 is this
  repository's *secondary observation*, never an oracle, and for a 24-byte header stub its answer would be
  meaningless in a way that is easy to misread as authority. Excluding it also removes the only
  `System:File*Date`-driven nondeterminism from this recorder, which is why B11L's CI job checks out no ExifTool
  tree at all. The comparison being made is *our prediction against the pinned build's own output*, and both
  sides are recorded.
* **Message families are the unit of observation**, because they are the only thing a black box exposes about a
  scan. `-pS` is `pmStructure` → `Print::printStructure` → `ImageFactory::open(path)` then
  `image->printStructure(out, kpsBasic)` (`app/actions.cpp:1983-1991`, and `app/exiv2.cpp:663-668` for the flag
  mapping). Exactly nine classes override `printStructure` — cr2, jp2, jpg, orf, png, raf, rw2, tiff, webp
  (`grep 'void .*::printStructure'` over the pinned `src/`) — so for BMP, GIF and TGA the base implementation
  (`src/image.cpp:136-138`, `throw Error(kerUnsupportedImageType, io_->path())`) is reached *only if a reader was
  reached*. That makes `-pS` a clean binary for these three formats:
  `Image type <path> is not supported` = matched and dispatched; `<path>: The file contains data of an unknown
  image type` = the scan produced nothing (`src/image.cpp:825-830`).
  Two families are *not* separable and this document says so: an entry that matches but whose `newInstance_`
  returns no image hits the same unknown-type throw (`src/image.cpp:843-848`), so "no match" and "matched then
  dropped" are one message. The `.targa` rungs live entirely inside that ambiguity by construction, since
  `newTgaInstance` re-checks through `good()` from position 0.
* `-pe` is the second flag and it carries the *positive* half of the signal: an image whose reader ran and found
  nothing prints an empty listing with exit 0 (B11 recorded exactly this for `owned-tga-full.tga`). So the pair
  `(-pS, -pe)` distinguishes "refused by the scan" from "reached a reader that reports nothing" without any
  write-capable flag.
* **No other flag is in scope.** `-pa`, `-px`, `-pc`, `-pC`, `-et`, `-ee` and every action that writes are
  excluded: a ladder about *recognition* must not be disturbed by a failure mode in which a write half-happens,
  and `inputUnchanged` is asserted for every row (the copy in a scratch directory is re-read and digested after
  each run).
* **What may be claimed after B11L:** that at *these* sizes this *pinned build* produced *these* message families,
  and where each ladder's flip sits; that the ladder agrees or disagrees with the prediction recorded in
  `ladder-manifest.json`; that the two `bmp-xmltail` rungs do or do not track their plain twins; and that one
  B11 row's mechanism is or is not reproduced by a rung of the same shape.
* **What may not:** any statement that a format "is", "is not", "should", or "fails to be" supported; any
  inference that a refusal is a parser defect, a bug report, or an upstream claim — the strand shape is a
  description of how this code reads, not a judgement of how it should; any generalisation from a
  40–128-byte stub to real files; and any use of a B11L row as census coverage. A rung under 26 bytes is
  additionally unable to support *any* format conclusion, because `eps` throws there regardless of content.

## Method and evidence contract

* `tools/reference/b11l_recognition_ladder.py` (`generate` | `verify`) owns the bytes. `verify` re-derives every
  rung, checks size and digest against the manifest, and enforces the two structural invariants that make a
  flip interpretable: BMP and GIF rungs are a chain of **prefixes** (each rung extends the one below it), and
  `.targa` rungs keep both fixed regions at their canonical offsets with **only the middle padding** varying —
  appending after the footer would move the signature and over-determine every refusal. `bmp-xmltail` must
  differ from its plain twin at exactly the planted window.
* `tools/reference/probe_b11l_ladder.py` runs the pinned executable built in CI and writes
  `artifacts/e01/b11l-ladder/observations.json`: one record per rung, one row per rung×flag, each carrying the
  verbatim stdout/stderr (truncated), the exit code, `inputUnchanged`, the file length and digest, and the
  recorded prediction with a `matchesPrediction` verdict per row. It never edits a fixture, never reads the
  census, and never calls `census.build()`.
* Because the artifact host and the Actions log are unreachable from the environment that recovers this
  repository's evidence, the report is also emitted as base64+zlib chunks in annotations under the `B11LZ`
  channel with a separate `count::` marker, exactly as B11's recorders do, and the chunk count is a hard limit:
  a report too large for `CHUNK_LIMIT` fails the step rather than truncating silently.
* `verification` stays `not-assessed` and `denominatorFrozen` stays false. The ladder is 47 fixtures × 2 flags
  = 94 rows; that number is *reported*, never used as a denominator.
* **No B11 file is modified**, so all 823 census rows, the 87-observation payload, the receipt and its
  reproducibility claims are unaffected — the two new tests are additive and `build_census.py --check` must stay
  green with an unchanged 823-row payload.

## What the run showed (added after run `42846898701`; the sections above are the pre-run gate, unchanged)

Recovered to `docs/research/evidence/e01-b11l-ladder/observations.json` (47 rungs, 94 rows, every input
unchanged, no artifact anywhere) and read there with its README. In short:

* **The flip is at 80 bytes, and it is the same flip for both ladders.** `bmp` and `gif` each refuse up to 79
  and reach a reader from 80 on, monotonically, and their families agree at all 15 sizes — two formats with
  different magic, different recognizer windows (2 bytes vs 6) and different registry positions (23 vs 20).
  The figure matches `isXmpType`'s window, which is the only thing in the review that predicts the same number
  for both.
* **Length, not content.** `bmp-xmltail` — the same rung with `<?xml ` planted inside the window xmp reads —
  has zero disagreeing rows. Content is never consulted, which is what the strand model said and what the
  "the model reads bytes" alternative said would not happen.
* **B11's unexplained GIF row is resolved, and not in the way either candidate predicted.** Below 25 bytes the
  message is the same for all three ladders: `Failed to read input data` at 8, 16 and 20, and `corrupted image
  metadata` at 12. B11's `owned-gif-lsd-v1.gif` is 13 bytes, inside that band, and no GIF code throws
  `kerCorruptedMetadata` — so that row was never about GIF. A 13-byte file is a *length* observation.
* **Two places the model was wrong, stated plainly.** The eps band is not "below 26": size 24 already behaves
  like the ≥ 25 band, so the constant `EPS_READ_BYTES` over-estimated the window by at least one rung, and the
  8/12/16/20 alternation (including why 12 alone yields `corrupted image metadata`) was not predicted at all.
  Which entry owns each of those bands stays **not established**: the ladder measures sizes, and it can only
  implicate entries whose windows fall in the gaps between rungs. Narrowing that is a different instrument —
  rungs at every byte from 8 to 26 — and it would be its own batch, not a retroactive edit to this
  prediction table.
* **The `.targa` control did not fire**: no rung reached a reader at any size, including 128. That is what the
  model predicted for it, and it is also the one result that argues against `newInstance_` returning null as
  the explanation for B11's small-file refusals — that path needs the footer route to be reached, and it never
  was.
* The ambiguity this document opened with stays open and is *not* papered over by the confirmation: "no entry
  matched" and "an entry matched and produced no image" remain one message (`src/image.cpp:843-848`); what the
  ladder adds is that for BMP and GIF the *length* at which they part is 80 bytes, which no reader-side check
  could produce.

## Follow-ups this document rules in or out

* **In scope for B13, not here:** the write-direction divergence B11 recorded for containers
  (`writeMetadata()` empty body vs `kerWritingImageFormatUnsupported`) and the `-o` semantics it touches.
* **Ruled out:** extending the ladder to formats before index 19. The staircase's first steps (`pgf` at 3 bytes,
  `mrw` at 4, `jpeg`/`exv` at 2/7) are already implied by the same reading, and a probe of them would test the
  model's *left* edge only — but it also needs a `DataBuf` throw path that this batch already covers with
  the 8–24 band, so the marginal information is small and the fixture count is not.
* **Label collision: resolved by moving this batch, not the plan.** When this document was written it carried
  the number B12, and the master plan already allocates B12 — "MakerNotes applicability overlay (all
  RAW/JPEG/TIFF rows)" (`docs/design/EXIV2_E01_CENSUS_AND_DENOMINATOR_PROPOSAL.md:141-157`), which is what
  B10's blocked-rights notes point at — on the eight `EX0289-SR2-*` rows, `EXIV2_CENSUS.json` quotes
  "the Sony maker-note tables batch B12 owns" verbatim. B13 and B14 are allocated too, and the freeze is
  defined as "after B14", so the sequence is not mine to renumber and a diagnostic batch should not occupy a
  planned number at all. This batch is therefore **B11L** — the ladder is B11's own promised follow-up, and a
  side-batch label says so instead of stealing a slot. Renamed in this PR before merge: the review document,
  the fixture module and its directory (`tests/fixtures/b11l/`), the recorder, the two test files, the CI job
  (`pinned-exiv2-b11l`) and the evidence directory (`docs/research/evidence/e01-b11l-ladder/`).
  What is *not* rewritten: the plan's B12, the census notes that reference it, and the two recovered payloads,
  whose embedded `scope`/annotation labels are the historical text their runs emitted (`B12 …`, channel
  `B12Z`) — payload bytes are evidence, and `tests/reference/test_b11l_ladder_fixtures.py` pins the census
  sentence so neither half can drift quietly.
* **Deferred on evidence, not convenience:** separating "no entry matched" from "an entry matched and returned no
  image". Both produce one message, and no CLI flag distinguishes them. If B11L's ladder lands on the predicted
  staircase, that ambiguity is the one thing this method cannot close. It is recorded as
  `not-establishable-by-this-method` — in the "What the run showed" section and in the evidence
  README — rather than in a receipt, which this batch has no census rows to warrant.
