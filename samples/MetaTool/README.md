# MetaTool

An approximate Exiv2 command line, written only against the public API of `MetadataSharp`: the same
nine actions, the same option letters, the same kinds of report, and none of Exiv2's code. Output is
close enough to the reference that its users feel at home; it is not byte-for-byte identical, and every
difference is named in [the CLI parity document](../../docs/usage/METATOOL_CLI_PARITY.md).

```sh
dotnet build samples/MetaTool
dotnet run --project samples/MetaTool -- -p a photo.jpg
```

## Actions

| Action | Names | What it does | Example |
|---|---|---|---|
| print | `pr`, `print` | Report metadata; `-p` chooses the report, `-P` the columns | `metatool -pa photo.jpg` |
| extract | `ex`, `extract` | Write a thumbnail, previews, the ICC profile, an XMP sidecar or an EXV payload | `metatool -e t photo.jpg` |
| insert | `in`, `insert` | Insert the same kinds of payload back into the file | `metatool -i x photo.jpg` |
| delete | `rm`, `delete` | Delete whole metadata families from the file | `metatool -d a photo.jpg` |
| adjust | `ad`, `adjust` | Move the EXIF timestamps | `metatool -Y 1 -D 2 ad photo.jpg` |
| modify | `mo`, `modify` | Apply `-M`/`-m` commands and set the comment of `-c` | `metatool -M 'set Exif.Image.Artist "Ada"' photo.jpg` |
| rename | `mv`, `rename` | Rename the file after its EXIF timestamp, `-r` names the pattern | `metatool -r ':basename:%Y%m%d_%H%M%S' photo.jpg` |
| fixiso | `fi`, `fixiso` | Set the ISO speed from the MakerNote | `metatool fi photo.jpg` |
| fixcom | `fc`, `fixcom`, `fixcomment` | Rewrite the EXIF user comment in the charset of `-n` | `metatool -n cp1251 fc photo.jpg` |

A run with no action prints the summary of the file, as the reference does: `metatool photo.jpg`.

## Print modes: `-p`

| Mode | What it prints | Example |
|---|---|---|
| `s` | The report: file name, size, media type, pixel size, thumbnail, and the common camera settings | `metatool -ps photo.jpg` |
| `a` | Every EXIF tag, MakerNote tags included, then every IPTC dataset and every XMP property | `metatool -pa photo.jpg` |
| `e` | EXIF rows with key, type, count and the value as stored | `metatool -pe photo.jpg` |
| `t` | EXIF rows with the value as the tag table reads it | `metatool -pt photo.jpg` |
| `v` | EXIF rows with tag number, group, name, type, count and the stored value | `metatool -pv photo.jpg` |
| `h` | EXIF rows with a hexadecimal dump of every value, one 16-byte line each | `metatool -ph photo.jpg` |
| `i` | IPTC rows | `metatool -pi photo.jpg` |
| `x` | XMP rows | `metatool -px photo.jpg` |
| `c` | The free-text comment | `metatool -pc photo.jpg` |
| `p` | The preview listing: number, media type, pixel size, byte count | `metatool -pp photo.jpg` |
| `C` | The ICC profile bytes, to standard output | `metatool -pC -e - photo.jpg > profile.icc` |
| `X` | The XMP packet bytes, to standard output | `metatool -pX photo.jpg > packet.xmp` |

`-p R` (recursive structure) and `-p S` (structure) are refused with exit code 2 and a diagnostic: the
library exposes metadata, not a container structure dump. The reference refuses `-p R` in its own
release builds for the same reason.

## Print columns: `-P`

| Flag | Column | Flag | Column |
|---|---|---|---|
| `E` `I` `X` | the families: EXIF, IPTC, XMP | `y` | the type, `Ascii`, `Long`, `LangAlt` |
| `x` | the tag number, `0x010f` | `c` | the number of values |
| `g` | the group, `Image`, `Exif`, `CanonCs` | `s` | the size in bytes |
| `k` | the key, `Exif.Image.Make` | `v` | the value as stored |
| `l` | the label of the tag table | `V` | `set`, the key, the type and the value of a modify command |
| `d` | the description of the tag table | `t` | the value as the tag table reads it |
| `n` | the name of the tag in the tag table | `h` | a hexadecimal dump of the value |

Without `-P` a list print uses `kyct`; `-pa` and `-P` without a family flag cover all three families.
The widths are the reference's: group 12, key 44, name 27, label 30, description 30, type 9, count 3,
size 3, and two spaces before a value or a dump.

```sh
metatool -P Exgnycv photo.jpg                      # tag, group, name, type, count, value of EXIF
metatool -P Ikyct -K Iptc.Application2.Caption photo.jpg
metatool -P Xkyct -g '^Xmp\.dc' photo.jpg
```

## Filters

| Option | Meaning | Example |
|---|---|---|
| `-K key` | Print only the row whose key is exactly this one; repeatable, any of them matches | `metatool -pe -K Exif.Image.Make -K Exif.Image.Model a.jpg b.jpg` |
| `-g pattern` | Print only the rows whose key matches this regular expression; repeatable | `metatool -pa -g '^Exif\.Photo' photo.jpg` |

A run whose filters match nothing exits with code 1, so a script can test for it.

## Data targets: `-d`, `-e`, `-i`

| Target | `-d` delete | `-e` extract | `-i` insert | Payload |
|---|---|---|---|---|
| `a` | EXIF, IPTC, XMP, comment | writes an EXV payload | reads an EXV payload | `photo.exv` |
| `e` `i` `x` `c` | that family | an EXV payload of that family | that family of an EXV payload | `photo.exv` |
| `t` | the EXIF thumbnail | `photo-thumb.jpg` | the thumbnail from the payload | `photo-thumb.jpg` |
| `C` | the ICC profile | `photo.icc` | the profile from the payload | `photo.icc` |
| `I` | the IPTC stream | `photo.iptc`, the raw IIM bytes | refused, exit code 4 | `photo.iptc` |
| `X` | — | `photo.xmp`, a sidecar converted for editing | the sidecar into the file | `photo.xmp` |
| `XX` | — | `photo.xmp`, the packet as it is | the packet as it is | `photo.xmp` |
| `p[n,...]` | — | `photo-preview1.jpg`, or only the numbers given | — | `photo-preview1.jpg` |
| `-` | write the result to standard output | write the payload to standard output | read the payload from standard input | stdout/stdin |

`-l dir` puts the payloads elsewhere, `-S .suf` names the payload of insert explicitly, and `-f`
overwrites a payload that exists while the default numbers it (`photo_1.exv`) so that nothing is lost.

```sh
metatool -e a photo.jpg                   # photo.exv
metatool -e t -e C photo.jpg              # photo-thumb.jpg and photo.icc
metatool -e p1,3 photo.jpg                # previews 1 and 3
metatool -i x -S other.xmp photo.jpg      # insert another.xmp
metatool -pX photo.jpg | metatool -i XX- copy.jpg
```

## Modify commands: `-M` and `-m`

A command is `set key [[type] value]`, `add key [[type] value]`, `del key` or `reg prefix namespace`,
one per line; `-M` gives a line, `-m` gives a file, `-m -` reads the lines from standard input. The
grammar is the library's [`ModifyScript`](../../src/MetadataSharp.Core/Operations/ModifyScript.cs).

```sh
metatool -M 'set Exif.Image.Artist "Ada Lovelace"' photo.jpg
metatool -M 'set Exif.Photo.DateTimeOriginal "2024:01:01 10:00:00"' -M 'set Exif.Image.Copyright "Ada"' photo.jpg
metatool -m script.txt photo.jpg
printf 'set Xmp.dc.title "hello"\n' | metatool -m - photo.jpg
metatool -c 'a JPEG comment' photo.jpg
```

## Time, names and file timestamps

| Option | Meaning | Example |
|---|---|---|
| `-a time` | Move every timestamp by `[-]HH[:MM[:SS]]` | `metatool -a 1:30 ad photo.jpg` |
| `-Y years` `-O months` `-D days` | Move every timestamp by whole years, months or days | `metatool -Y 1 -O -2 ad photo.jpg` |
| `-r format` | The name of a renamed file: strftime conversions, `:basename:`, `:basesuffix:`, `:dirname:`, `:parentname:`, and `:Exif.Key:` | `metatool -r '%Y%m%d_%H%M%S' mv photo.jpg` |
| `-k` | Keep the file timestamps of a file that is updated | `metatool -k -M 'set Exif.Image.Artist "Ada"' photo.jpg` |
| `-t` | Set the file time from the EXIF time, as part of rename | `metatool -r '%Y%m%d' -t mv photo.jpg` |
| `-T` | Set the file time from the EXIF time and do nothing else | `metatool -T mv photo.jpg` |

A name that is taken is numbered (`photo_1.jpg`) unless `-f` asks to overwrite it, so a batch run never
asks a question and never loses a file.

## Other options

| Option | Meaning |
|---|---|
| `-n charset` | Charset of the EXIF user comment, `utf-8` by default; `fc` and `-pt` use it |
| `-q` | Silence warnings |
| `-Q level` | Log level: `d`, `i`, `w`, `e` or `m` (mute) |
| `-u` | Hide tags that have no name in the tag table |
| `-v` | Say what is being done, on standard output |
| `-V` | Print the version and exit |
| `-h` | Print the usage text and exit |
| `--` | Read every later word as a file name, whatever it begins with |
| `-b` | Obsolete in the reference; accepted and ignored so that old scripts keep running |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Everything asked for was done |
| 1 | An action failed: a file could not be read, written or changed, or a filter matched nothing |
| 2 | The command line cannot be read: an unknown option, a missing argument, a missing action or target |
| 3 | The file was read, but the metadata the action needs is not there |
| 4 | The action asks for something this tool knowingly does not offer; nothing was changed |

## Exiv2 option map

Every option of Exiv2 0.28.9 that this tool accepts, the letter it keeps, and where it behaves
differently.

| Exiv2 | MetaTool | Note |
|---|---|---|
| `-p mode` | `-p mode` | The twelve modes of the reference minus `R` and `S`, which are refused |
| `-P flags` | `-P flags` | Same flags, same column widths, same order |
| `-d/-e/-i/-C target` | `-d/-e/-i/-C target` | Same letters; `-i I` is refused with code 4 |
| `-K key`, `-g str` | `-K`, `-g` | `-g` is a .NET regular expression, not a POSIX one; both repeat |
| `-M cmd`, `-m file` | `-M`, `-m` | Same grammar, same stdin script of `-m -` |
| `-c txt` | `-c txt` | Same escapes: `\\`, `\n`, `\r`, `\t`, `\uXXXX` |
| `-r fmt` | `-r fmt` | `:Exif.Key:` tokens carry the interpreted value; separators become `_` |
| `-a`, `-Y`, `-O`, `-D` | same | Same units; no time-zone variable is consulted |
| `-k`, `-t`, `-T` | same | Same meanings |
| `-l dir`, `-S suffix` | same | Same meanings |
| `-n encoding` | same | Any charset the .NET runtime knows, code pages included |
| `-q`, `-Q level`, `-v`, `-V`, `-h` | same | `-Q m` implies `-q` |
| `-u` | `-u` | Hides unnamed tags, as the reference binary does |
| `-f`, `-F` | `-f`, `-F` | `-f` overwrites; the default numbers the name instead of asking |
| `-b` | `-b` | Accepted and ignored; the reference keeps it for its own test suite |

## What MetaTool does that the reference does not

Each improvement is covered by a test that fails if the behaviour goes away.

| Improvement | Covered by |
|---|---|
| A payload or a renamed file whose name is taken is numbered, never silently replaced and never prompted for, so a batch run neither blocks nor loses data | `tools/ci/metatool_smoke.sh` (extract twice, rename twice) |
| `-` works for print and extract too, not only for the EXV filter, so any payload can be piped | `tools/ci/metatool_smoke.sh` (`-pX … \| -i XX- …`) |
| Every exit code is documented and covered: 0, 1, 2, 3 and 4 each have a test | `MetaToolSupportTests` (`Exit_codes_*`), `MetaToolActionTests` |
| `-p s` prints the pixel size from the container header, so a file whose EXIF has no pixel tags still reports its size | `MetaToolActionTests.The_summary_names_the_file_type_and_the_pixel_size` |
| MakerNote tags are rows of `-pa` and `-pe`, with the library's interpretations | `MetaToolActionTests.MakerNote_tags_are_rows_of_the_print` |
| Filters repeat and combine, and a match on any of them prints the row | `MetaToolGrammarTests.Filters_repeat` |
| A run over several files prefixes every line with the file name | `MetaToolActionTests.A_run_over_two_files_prefixes_every_line` |
| One unreadable file does not stop the others of the same run | `MetaToolActionTests.A_missing_file_does_not_stop_the_next_one` |

## The library behind it

Nothing here reaches into the library's internals. The actions use `ImageMetadata`, the format
documents through `SampleSupport`, and the operations of `MetadataOperations`, `IccProfiles`,
`Previews`, `MetadataTimestamps`, `MetadataFileNames`, `ModifyScript` and `ExifEdit`. Two facts a
metadata snapshot cannot give were added to the library for this tool, proposed first in
[the CLI parity document](../../docs/usage/METATOOL_CLI_PARITY.md):

- `ImageGeometry` reads the pixel size a file states in its own header (JPEG frame header, PNG `IHDR`,
  WebP header chunks, the first directory of a TIFF structure), so `-p s` can print `Image size`
  without decoding a picture.
- `MetadataMimeTypes` and `ImageMetadata.MimeType` name the media type of a signature, so `-p s` can
  print `MIME type`.
