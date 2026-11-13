# MakerNote interpreted-value parity

Reference: Exiv2 0.28.9, commit `b0859fc61fea5dedfc898aece6b15893ddf4314e`. Counts are translated keys found / values equal; the full gap is untranslated translated values plus mismatches.

| Order | Vendor group(s) | Baseline found / equal | After found / equal | Baseline gap |
|---:|---|---:|---:|---:|
| 1 | `Exif.PentaxDng` + `Exif.Pentax` | 997 / 0 | 997 / 969 | 997 |
| 2 | `Exif.CanonSi` + `Exif.CanonCs` | 2471 / 1777 | — | 694 |
| 3 | `Exif.Fujifilm` | 466 / 0 | — | 466 |
| 4 | `Exif.NikonLd3` | 432 / 36 | — | 396 |
| 5 | `Exif.Sony1` | 1503 / 1247 | — | 256 |
| 6 | `Exif.NikonFl7` | 504 / 295 | — | 209 |
| 7 | `Exif.Panasonic` | 153 / 0 | — | 153 |
| 8 | `Exif.Sony2` | 828 / 676 | — | 152 |
| 9 | `Exif.OlympusCs` | 111 / 0 | — | 111 |

## Exceptions

- `Exif.Pentax.LensType` and `Exif.PentaxDng.LensType`: Exiv2's external lens-ID database; keep the raw encoded value, do not add a database.
- Exact pinned reader refusals: `issue_1706_poc.exv` (`LimitExceeded`), `issue_1920_poc.tiff` (`InvalidExif`), `issue_ghsa_h9x9_4f77_336w_poc.exv` (`InvalidExif`), and `IMGP0006-min.jpg` (`InvalidJpeg`).
- Unknown numeric `0xNNNN` tags remain raw extras when Exiv2 has no named key.
