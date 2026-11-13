# MakerNote interpreted-value parity

Reference: Exiv2 0.28.9, commit `b0859fc61fea5dedfc898aece6b15893ddf4314e`. Counts are translated keys found / values equal; the gap is untranslated translated values plus mismatches.

| Order | Vendor group | Baseline found / equal | After found / equal | Baseline gap |
|---:|---|---:|---:|---:|
| 1 | `Exif.CanonSi` | 976 / 491 | pending | 485 |
| 2 | `Exif.Fujifilm` | 466 / 0 | 466 / 466 | 466 |
| 3 | `Exif.CanonCs` | 1495 / 1286 | pending | 209 |
| 4 | `Exif.Canon` | 355 / 269 | pending | 86 |
| 5 | `Exif.CanonFi` | 519 / 442 | pending | 77 |
| 6 | `Exif.CanonAfC` | 189 / 176 | pending | 13 |
| 7 | `Exif.CanonPi` | 7 / 0 | pending | 7 |
| 8 | `Exif.CanonPa` | 1 / 0 | pending | 1 |
| — | `Exif.PentaxDng` | 517 / 0 | 517 / 502 | 517 |
| — | `Exif.Pentax` | 480 / 0 | 480 / 467 | 480 |

## Exceptions

- `Exif.CanonCs.LensType`, `Exif.Pentax.LensType`, and `Exif.PentaxDng.LensType`: external lens-ID database-derived names are not reproduced; raw values are retained, and no lens database is added.
- Exact pinned reader refusals: `issue_1706_poc.exv` (`LimitExceeded`), `issue_1920_poc.tiff` (`InvalidExif`), `issue_ghsa_h9x9_4f77_336w_poc.exv` (`InvalidExif`), and `IMGP0006-min.jpg` (`InvalidJpeg`).
- Unknown numeric `0xNNNN` tags remain raw extras where Exiv2 has no named key.
