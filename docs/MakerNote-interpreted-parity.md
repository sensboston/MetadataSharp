# MakerNote interpreted-value parity plan

The reference is Exiv2 0.28.9 at commit `b0859fc61fea5dedfc898aece6b15893ddf4314e`. The starting counts below are the pinned corpus's interpreted checks: keys with a reference translation found, and those whose displayed text agrees. The remaining gap is the sum of untranslated translated values and translated-text mismatches. The ranking therefore uses the complete interpretation gap, not only rows where both sides already print a different phrase.

| Order | Group | Baseline translated keys found / equal | Full interpretation gap |
|---:|---|---:|---:|
| 1 | `Exif.PentaxDng` | 517 / 0 | 517 |
| 2 | `Exif.CanonSi` | 976 / 491 | 485 |
| 3 | `Exif.Pentax` | 480 / 0 | 480 |
| 4 | `Exif.Fujifilm` | 466 / 0 | 466 |
| 5 | `Exif.NikonLd3` | 432 / 36 | 396 |
| 6 | `Exif.Sony1` | 1503 / 1247 | 256 |
| 7 | `Exif.CanonCs` | 1495 / 1286 | 209 |
| 8 | `Exif.NikonFl7` | 504 / 295 | 209 |
| 9 | `Exif.Panasonic` | 153 / 0 | 153 |
| 10 | `Exif.Sony2` | 828 / 676 | 152 |
| 11 | `Exif.OlympusCs` | 111 / 0 | 111 |

The current PR starts with `Exif.PentaxDng`; later vendor groups remain planned work, not claims of coverage. Within a group, the parity job is the arbiter: every reference key/value must match, all reference-translated text must match, and the report must be complete rather than row-truncated. The only raw extras admitted are unknown tags that this library exposes under a numeric `0xNNNN` key while Exiv2 has no named key. The two malformed/over-budget source files refused by the reader are listed as exact, reasoned file exceptions in the gate. Lens-name differences caused solely by Exiv2's external lens-ID database are documented per key; this project retains the raw value and does not add a lens database.

The existing `MakerNote.Interpret` and `MakerNote.Interpret(entry, field)` surface is sufficient. No public API is needed for the planned renderings, so no separate draft-review stop is required. Interpreted rules stay handwritten in the project's own structures; individual vendor reference facts may guide tag IDs, value formats and short labels, but vendor tables or descriptive text are not copied or generated into this project.
