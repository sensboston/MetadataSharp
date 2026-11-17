# MakerNote and XMP interpreted-value parity

Reference: Exiv2 0.28.9, commit `b0859fc61fea5dedfc898aece6b15893ddf4314e`. The reference prints a
value twice, as it stands and as it reads; this document counts the second column. Counts are
translated keys found / values equal; the gap is a text the reference reads and this library does not.

## How it is measured

- `MetadataSharp.Dump` states the reading of every EXIF, MakerNote and XMP row as the sixth field of
  its `TAG` record, empty where the library makes nothing of the value. The field is what the
  comparison sees: a row without it was counted as "no interpretation offered" and so as beyond the
  gate's reach, which is why a green gate could sit beside 1,906 untranslated texts.
- `tools/MetadataSharp.Parity --inventory --require Exif.<group> …` runs over the pinned corpus in the
  `differential-parity` job. A value the reference reads and this library does not is a failure, and so
  is a value read differently; a whole-file refusal the corpus records is excused. `--gaps` posts the
  counts of every group and the list of what remains as notices, so the work can be done from the
  annotations rather than from a job log.
- `MetadataSharp.Core.Xmp.XmpTags.Interpret` reads an XMP property with the reference's own print
  function (`xmpPrintInfo` in `src/properties.cpp`): the set of properties is the reference's, a ratio
  reads as its value or as a float of six significant digits, a date to the second in the EXIF
  spelling, a version with its leading zero dropped, a run of component codes as the colour model it
  names, an enumerated code through the reference's own tables, and a published vocabulary term
  through the code list the standard defines (`printTagVocabulary` and its `_MULTI` form).
- An XMP group is gated on its reading alone. The text of a packet is the packet's, and the reference
  rewrites some of it on the way in: it completes a `exif:GPSTimeStamp` that names no day, so its text
  column and ours differ on that one property by design, and the reading — which is what the gate
  measures — is the same.

## Where the gap was, and where it is

| Order | Group | Baseline found / equal | After found / equal | Remaining |
|---:|---|---:|---:|---|
| 1 | `Exif.CanonSi` | 976 / 491 | 976 / 976 | — |
| 2 | `Exif.CanonCs` | 1495 / 1286 | 1495 / 1495 | — |
| 3 | `Exif.Canon` | 355 / 269 | 355 / 355 | — |
| 4 | `Exif.CanonFi` | 519 / 442 | 521 / 521 | — |
| 5 | `Exif.CanonAfC` | 189 / 176 | 189 / 189 | — |
| 6 | `Exif.CanonPi` | 7 / 0 | 7 / 7 | — |
| 7 | `Exif.CanonPa` | 1 / 0 | 1 / 1 | — |
| — | `Exif.Fujifilm` | 466 / 0 | 466 / 466 | — |
| — | `Exif.PentaxDng` | 517 / 0 | 517 / 517 | — |
| — | `Exif.Pentax` | 480 / 0 | 480 / 480 | — |

The XMP properties the reference reads:

| Group | Found / equal | Remaining |
|---|---:|---|
| `Xmp.exif` | 87 / 87 | — |
| `Xmp.tiff` | 73 / 73 | — |
| `Xmp.iptc` | 4 / 4 | — |
| `Xmp.iptcExt` | 3 / 3 | — |
| `Xmp.plus` | 5 / 5 | — |

Across the 76 groups the gate covers, the reference reads 12,645 texts and all 12,645 are equal: every
MakerNote group and the whole of the XMP families are fully read. Run 43623377370, green with
`--require` on every one of those groups and with nothing excepted. The last 165 were the lens codes of
`Exif.CanonCs`, `Exif.Pentax`, `Exif.PentaxDng`, the Nikon lens data, `Exif.Sony1`, `Exif.Sony2`,
`Exif.SonyMinolta` and `Exif.Minolta`.

## The vocabularies, and their licences

The reference reads the IPTC and PLUS properties through code lists of its own. Those lists are not
its invention: they are the published controlled vocabularies, so this library carries them
(`MetadataSharp.Core.Xmp.XmpVocabularies`), with attribution here.

| Property | Vocabulary | Source and licence |
|---|---|---|
| `Xmp.iptc.Scene` | IPTC Scene NewsCodes, 24 terms | `https://cv.iptc.org/newscodes/scene/` — Copyright IPTC, CC BY 4.0 |
| `Xmp.iptc.SubjectCode` | IPTC Subject Codes, 1,404 terms | `https://cv.iptc.org/newscodes/subjectcode/` — Copyright IPTC, CC BY 4.0 |
| `Xmp.iptcExt.DigitalSourceType` | IPTC Digital Source Type, 5 of the 20 terms | `https://cv.iptc.org/newscodes/digitalsourcetype/` — Copyright IPTC, CC BY 4.0 |
| `Xmp.plus.AdultContentWarning`, `CopyrightStatus`, `CreditLineRequired`, `ImageAlterationConstraints`, `ImageDuplicationConstraints`, `ImageFileConstraints`, `ImageFileFormatAsDelivered`, `ImageFileSizeAsDelivered`, `ImageType`, `LicensorTelephoneType1`, `LicensorTelephoneType2`, `MinorModelAgeDisclosure`, `ModelReleaseStatus`, `PropertyReleaseStatus` | PLUS License Data Format vocabularies, 76 terms | `https://ns.useplus.org/LDF/ldf-XMPSpecification` (PLUS Version 2.0.1) — the PLUS Coalition publishes the LDF and its controlled vocabularies for use |

The Subject Codes were taken from a complete export of the vocabulary (1,404 terms, the number the
scheme itself states) and then checked term by term against the reference's rendering of
`test/data/issue_1959_poc.xmp`, which carries every code of the vocabulary: 1,403 of the 1,404 names
were already identical, and the one difference was the export's double space in `16000000` where the
published label, `unrest, conflicts and war`, has one. That is the extent of the cross-check: the
reference's output is the measurement, not the source.

Where the reference's text and the current publication differ, the table carries the reference's text,
because a reading is defined as the reference's reading; each of the four cases is named here:

- The Scene labels are published in lower case (`headshot`, `rear view`, `under-water`). The reference
  capitalises the first letter of all of them but `rear view`, and the table follows it.
- `Xmp.iptcExt.DigitalSourceType` is published with newer labels (`digitalCapture` reads *Digital
  capture sampled from real life* since 2024, `softwareImage` is retired and reads *Created by
  software*). The reference was built against the earlier labels and reads `softwareImage` as that
  term's definition, and the table follows it. The vocabulary's other 15 terms, most of them added or
  retired since, are not carried: naming them here would make a value that the reference prints as
  the bare term read differently, and parity is the point.
- `plus:CreditLineRequired` `CR-NRQ` is published as *Not Required*; the reference prints *Not
  Require*, and the table follows it.
- The telephone types are published in lower case (`work`, `cell`, `fax`, `home`, `pager`); the
  reference capitalises them, and the table follows it.

The reference also maps two fields that the published LDF XMP Reference does not define, so no term
list could be read from the standard and neither is carried: `plus:Reuse` (`RE-NAP`, `RE-REU`) and
`plus:DataMining` (the `DMI-*` terms), which belong to a later version of the LDF than the one
published at `ns.useplus.org`. A packet that carries them reads them as text. The reference's
`Xmp.iptcExt.DigitalSourcefileType` list is not carried either: the field is deprecated in the IPTC
Extension and its vocabulary URI, `http://cv.iptc.org/newscodes/digitalsourcefiletype/`, answers 404
from the IPTC controlled-vocabulary server (checked 2026-11-14), so its terms could not be taken from
the standard. No corpus file carries either.

## The lens codes

A lens code is a number several lenses share, so each vendor's code is read with a table of its own and
a rule that picks between the rows of one code. The tables are `LensTables`, one per vendor, ordered by
the code and — within one code — in the order the alternatives were recorded, because the rules read
that order. Lens names are the manufacturers' own and are kept verbatim; the selection rule is written
here from the behaviour of the reference, not copied from it.

- **Canon** (`Exif.CanonCs.LensType`): the camera settings hold the focal range (`Lens`) and the widest
  aperture (`MaxAperture`), and the lens whose name states that range and an aperture the shot fits is
  the one. Several lenses of one code that fit are joined with ` *OR* `; a code with no fit reads as
  `Unknown Lens (<code>)`, one whose settings are absent as the same. The code `65535` is the code of
  no lens and reads from the table, except on the one camera that writes it for a lens it knows (Canon
  EOS 30D with `Lens` `24 24 1` and `MaxAperture` 95, the EF-S 24mm f/2.8 STM).
- **Nikon F mount** (`Exif.NikonLd*.LensIDNumber`): a lens is eight bytes of the lens data array — the
  id, the f-stops, the focal length at each end, the widest aperture at each end, the microcomputer
  version — and the lens type of the note. A row of Sigma keeps its f-stops free, because they move
  with the focal length of some of its zooms. No row, or an array or lens type that is not there,
  reads as the id in parentheses.
- **Nikon Z mount** (`Exif.NikonLd4.LensID`): the id names the lens directly.
- **Pentax** (`Exif.Pentax.LensType` and `Exif.PentaxDng.LensType`): the two bytes of the tag are one
  number, and the lens info array (`0x0207`), the camera model and the focal length of the picture pick
  one of the alternatives a few numbers carry — the alternatives are recorded with the number and the
  conditions that select them, so the order is the record.
- **Sony and Minolta** (`Exif.Sony1.LensID`, `Exif.Sony2.LensID`, `Exif.SonyMinolta.LensID`,
  `Exif.Minolta.LensID`): one label per id, which for some ids holds several lenses separated by `|`.
  The camera model, the lens model, the focal length, the 35 mm focal length and the widest aperture
  pick one of them; the id of the lens no camera reports names the first of its three.

The data was cross-checked against ExifTool's own lens tables. Nikon: of the fifty F-mount byte keys
both tables hold, 43 name the same lens and the other seven differ only in how it is written
(`Tamron SP AF 90mm f/2.8 [Di] Macro 1:1 (172E/272E)` against `Tamron SP AF 90mm F2.8 Macro 1:1`,
`Zeiss Milvus 2/50M` against `Zeiss Milvus 50mm f/2 Macro`). Pentax: of the 251 `series model` numbers
both tables hold, 216 name the same lens and the rest differ in spacing or brackets alone
(`smc PENTAX-FA* 85mm F1.4 [IF]`) that no number explains. Canon: 155 of the 234 shared codes name the
same set of lenses; the others are codes where ExifTool also lists third-party lenses the reference
does not, or the reverse.

A lens name is a fact about the lens and is reproduced verbatim — the reference's spelling, because
that is what the gate compares — while the tables, their ordering and the selection rules are this
project's own. The reference's F-mount block carries a restrictive header that its Z-mount block and
all the other tables lack; a code-to-name pair is a fact and was taken from the reference's tables
after the cross-check above, and read from ExifTool's where the two agree, which is what the majority
of the rows do.

## Exceptions

- None. Every text the reference reads is read here.
- Exact pinned reader refusals: `issue_1706_poc.exv` (`LimitExceeded`), `issue_1920_poc.tiff`
  (`InvalidExif`), `issue_ghsa_h9x9_4f77_336w_poc.exv` (`InvalidExif`), and `IMGP0006-min.jpg`
  (`InvalidJpeg`). Their rows are `not-read` and the gate excuses them.
- Unknown numeric `0xNNNN` tags remain raw extras where Exiv2 has no named key.
- A code the reference's table does not name reads as the bare number in parentheses, as it does there.
