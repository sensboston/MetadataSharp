# MetaTool CLI compatibility task

This is the task list for making `samples/MetaTool` behave like the Exiv2 command line. It is not part
of the library contract and it is not part of [format and operation
coverage](FORMAT_AND_OPERATION_COVERAGE.md): that document covers what the library can read and write,
and a command-line front end is a separate piece of work with its own acceptance conditions.

**Reference:** Exiv2 v0.28.9, commit
[`b0859fc61fea5dedfc898aece6b15893ddf4314e`](https://github.com/Exiv2/exiv2/tree/b0859fc61fea5dedfc898aece6b15893ddf4314e):
action names and aliases are from `exiv2.md` “4 ACTIONS”, the option inventory from “5 COMMAND
SUMMARY” and “6 OPTIONS”, and the modify grammar from “11 MODIFY COMMANDS”.

The tool itself is documented in [the MetaTool README](../../samples/MetaTool/README.md): every action,
every option and one example of each. This document records what the reference does, what MetaTool does
instead, and why they differ where they do.

## Actions, print modes, options and data targets

| Feature | Exiv2 0.28.9 | MetaTool now | Status |
|---|---|---|---|
| Actions | `pr/print`, `ex/extract`, `in/insert`, `rm/delete`, `ad/adjust`, `mo/modify`, `mv/rename`, `fi/fixiso`, `fc/fixcom` | All nine, with the short and long names of the reference; a run with no action prints the summary | Implemented |
| Print modes `-p` | `s,a,e,t,v,h,i,x,c,p,C,R,S,X` | `s,a,e,t,v,h,i,x,c,p,C,X`; `R` and `S` are refused with exit code 2 and a diagnostic | Implemented, two modes refused |
| Fine print flags `-P` | `E,I,X,x,g,k,l,d,n,y,c,s,v,V,t,h` | Every flag, the whole column set of the reference, its widths and its column order | Implemented |
| Exact and substring filters | `-K key`, `-g str`; repeatable | Both, repeatable; `-K` is exact, `-g` is a .NET regular expression | Implemented |
| Delete targets | `a,e,t,i,x,c,C,I,-` | Every letter; `I` deletes the IPTC stream as datasets | Implemented |
| Extract targets | `a,e,t,i,x,c,p[n,...],C,X,XX,-`; location, filtering and stdout | Every letter, `-l`, `-S`, stdout of `-`, preview numbers of `-ep1,3` | Implemented |
| Insert targets | `a,e,t,i,x,c,C,X,XX,-`; location/suffix and stream filter | Every letter but `I`, which is refused with exit code 4 | Implemented, one target refused |
| Modify commands | `set`, `add`, `del`, `reg`, typed values, repeated commands and stdin/file scripts | The library's `ModifyScript` grammar: `set`, `add`, `del`, `reg`, typed values, `-M` repeated, `-m file`, `-m -` | Implemented |
| Rename patterns | Exiv2 keywords and strftime conversions, collision prompting/force, timestamp options | `MetadataFileNames` keyword and strftime subset, `:Exif.Key:` tokens, `-r`, `-t`, `-T`, `-k`, numbering instead of prompting | Implemented |
| Date adjustment | `-a`, `-Y`, `-O`, `-D` | Every unit, on `Exif.Image.DateTime`, `Exif.Photo.DateTimeOriginal`, `Exif.Image.DateTimeOriginal` and `Exif.Photo.DateTimeDigitized`, and on the IPTC and XMP dates | Implemented |
| `fixiso`, `fixcom` | Dedicated `fi` and `fc`; `-n enc` selects the user-comment charset | Both, with `-n` for any charset the runtime knows | Implemented |
| `-c txt` comment option | Set a JPEG or PNG comment as part of `modify` | Same, with the escapes `\\ \n \r \t \uXXXX` | Implemented |
| `-k` / file timestamps | Keep timestamps on update; `-t`/`-T` set the file time | Same three meanings | Implemented |
| Encodings and output controls | `-n`, `-q`, `-Q`, `-u`, `-v`, `-V`, `-h`, `-f`, `-F`, `-l`, `-S`, time offsets | All of them; `-Q m` implies `-q`; `-b` is accepted and ignored | Implemented |
| `-b` obsolete option | Obsolete; reserved for the upstream test suite | Accepted and ignored, so that an old script keeps running | Implemented as a no-op |

### Print detail inventory (`-p`, `-P`)

The shorthand modes are `s` summary; `a` all EXIF/IPTC/XMP; `e` raw EXIF tags; `t` translated EXIF
values; `v` raw EXIF values; `h` EXIF hex; `i` IPTC; `x` XMP; `c` JPEG comments; `p` preview listing;
`C` ICC; `R` recursive structure; `S` structure; `X` raw XMP. The `-P` flags select families
(`E,I,X`), numeric tag (`x`), group/key/label/description/name (`g,k,l,d,n`), type/count/size
(`y,c,s`), raw/value-as-modify-command/translated output (`v,V,t`), and hex (`h`).

`-p C` and `-p X` write the ICC profile and the XMP packet as the bytes they are, so those two modes
are exact by construction. The row modes use the reference's column widths and spacing: group 12, key
44, name 27, label 30, description 30, type 9, count 3, size 3, two spaces before a value, and the
hexadecimal dump of `-p h` with a fixed text column. What differs is the text inside the columns, and
that is listed under [divergences](#divergences-from-the-reference).

### Data target inventory

The targets are not interchangeable: Exiv2 has `a` (all supported), `e` (EXIF), `t` (EXIF thumbnail),
`i` (IPTC), `x` (XMP), `c` (comment), `C` (ICC), `I` (all IPTC), `X` (XMP sidecar with conversion),
`XX` (raw packet), `p` (one or more previews), and `-` (stdin/stdout filter) across the
extract/insert/delete actions. MetaTool implements the same letters with the same payload files beside
the image (`photo.exv`, `photo.xmp`, `photo-thumb.jpg`, `photo.icc`, `photo.iptc`,
`photo-preview1.jpg`), the same `-l` location and `-S` suffix, and the same `-` filter. ICC profiles and
the raw XMP packet are copied as bytes, not interpreted. Sidecar conversion is explicit (`X` converts,
`XX` does not), and an EXV payload keeps the difference between an in-place edit and a detached copy.
See [operations usage and limits](OPERATIONS_USAGE_AND_LIMITS.md) and
[XMP usage and preservation limits](XMP_USAGE_AND_PRESERVATION_LIMITS.md).

## Divergences from the reference

Each row is a difference a user can observe, and the reason it is there. Everything not listed behaves
as the reference does, including the option letters, the target letters, the column widths, the order
of the columns and the payload file names.

| Divergence | What MetaTool does | Why |
|---|---|---|
| `-p R`, `-p S` | Refused: exit code 2, “print mode … is not offered” | The library exposes metadata, not the structure of a container. The reference itself refuses `-p R` in its release builds |
| `-i I` | Refused: exit code 4 | The library replaces IPTC datasets through an edit; it has no operation that swaps a whole IIM stream. An EXV payload (`-i a`) carries the same datasets |
| `-g pattern` | A .NET regular expression | No POSIX regex engine in the runtime. The subset that command lines use (`^`, `$`, `.`, `[]`, `*`, `+`, `?`, `|`) behaves the same |
| `-u` | Hides the rows whose tag has no name | The reference's manual says “show unknown tags”, but its 0.28.9 binary hides them when `-u` is given (`unknown_ = false`). MetaTool follows the binary, because a script written against the binary is what has to keep working |
| A name that is taken | Numbered (`photo_1.exv`, `photo_1.jpg`), with `-f` overwriting | The reference asks “Overwrite? [y/N]”, which blocks a batch run and cannot be answered in a pipe. Numbering neither blocks nor loses data; `-F` keeps the reference's rename policy explicitly |
| `-p s` “Image size” | From the container header (JPEG, PNG, WebP, TIFF), or from the EXIF pixel tags, or `0 x 0` | A metadata snapshot does not describe pixels. `ImageGeometry` reads the header; containers it does not measure fall back to the EXIF tags |
| `-p s` “MIME type” | From the signature: `image/tiff` for every TIFF-based raw file | The reference names the raw format a TIFF structure holds (`image/x-canon-cr2` and so on). Telling them apart takes the Make and DNGVersion tags, and the container is what the bytes say |
| `-p s` vendor fields | “File number”, “Flash bias”, “Macro mode” and “Image quality” read the Canon groups the library names (`Exif.Canon.FileNumber`, `Exif.CanonSi.FlashBias`, `Exif.CanonCs.Macro`, `Exif.CanonCs.Quality`), so they are empty for other makes | Same fields, same labels; the group names are this library's, which the audit document describes |
| `-P l`, `-P d`, `-pt` text | The labels, descriptions and one-line interpretations of this library's own tag tables | No Exiv2 prose is copied; the tables are locally authored, as the audit requires |
| `-Q level` | `m` mutes everything `-q` mutes; `d`, `i`, `w` and `e` are accepted and print the same warnings | The tool has one class of diagnostic. A finer ladder would be a facade over nothing |
| `-b` | Accepted, ignored | Obsolete in the reference and used only by its own test suite |

## The two library additions this task needed

A sample may only use the public API, so anything the reports needed and the library did not expose was
proposed here first and then added. Nothing else in the library changed.

1. **`ImageGeometry`** (`src/MetadataSharp.Formats/ImageGeometry.cs`). `exiv2 -p s` prints
   `Image size: 6000 x 4000` from the container, and a metadata snapshot describes tags, not pixels.
   The new reader returns the pixel size a header states — the first frame header of a JPEG, the `IHDR`
   chunk of a PNG, the header chunks of a WebP (`VP8X`, `VP8`, `VP8L`), and `ImageWidth`/`ImageLength`
   of the first directory of a TIFF structure. It never decodes a picture and never fails: a format it
   does not measure returns `null` and the caller decides what to print.
2. **`MetadataMimeTypes` and `ImageMetadata.MimeType`** (`src/MetadataSharp.Formats/MetadataMimeTypes.cs`).
   `exiv2 -p s` prints `MIME type`, which the library knew only implicitly, as `MetadataFormat`. The
   table names the media type of each signature and the property exposes it on the snapshot.

Both are covered by the tool's own tests
(`MetaToolActionTests.The_summary_names_the_file_type_and_the_pixel_size`) and by the smoke job.

## Acceptance conditions for this task

1. `-p` modes `s,a,e,t,v,h,i,x,c,p,C,R,S,X` and `-P` keys render the reference's columns byte for
   byte on the pinned corpus, or the divergence is named in a catalog document with its reason.
   *Met*: the columns, widths and order are the reference's (`RowWriter`, `HexDump`); `C` and `X` are
   the payload bytes; `R` and `S` are refused; the divergences are the table above, and the layouts are
   pinned by `MetaToolActionTests` (`A_row_holds_the_key_the_type_the_count_and_the_value`,
   `The_columns_of_the_flags_are_written_in_the_order_of_the_reference`,
   `A_hex_column_dumps_the_value_on_its_own_line`,
   `The_summary_names_the_file_type_and_the_pixel_size`).*
2. Every action and target in the tables above is either implemented or refused with a documented exit
   code and diagnostic.
   *Met*: `-p R`, `-p S` (code 2) and `-i I` (code 4) are the only refusals, and both print a
   diagnostic naming what is missing. The exit codes are listed in the README and each of them has a
   test (`MetaToolSupportTests.Exit_codes_are_the_documented_ones`).*
3. The argument grammar (repeatable filters, stdin scripts, `--` file lists) is pinned by tests, not
   only by the sample's usage text.
   *Met*: the grammar lives in `samples/MetaTool/Cli/CommandLine.cs`, is linked into the test project
   (`tests/MetadataSharp.Tests/Samples/MetaToolCliTests.cs`), and is covered by
   `MetaToolGrammarTests` — attached and separate arguments, bundled flags, repeatable `-K`/`-g`,
   `--` file lists, every action alias, the target letters, the preview numbers, the conflicts and the
   usage errors — and by `MetaToolActionTests.A_modify_script_reads_from_standard_input` for `-m -`.*

The actions themselves are smoke-tested in CI for every action, print mode, target and documented exit
code by [`tools/ci/metatool_smoke.sh`](../../tools/ci/metatool_smoke.sh), which the sample job of
`.github/workflows/build-and-test.yml` runs on the generated fixtures after building every sample.
