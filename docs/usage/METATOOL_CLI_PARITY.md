# MetaTool CLI compatibility task

This is the task list for making `samples/MetaTool` behave like the Exiv2 command line. It is not part
of the library contract and it is not part of the release-readiness audit any more: the audit covers
what the library can read and write, and a command-line front end is a separate piece of work with its
own acceptance conditions.

The library operations behind the rows below already exist and are covered by the audit and by the
tests it links. What is missing is Exiv2's argument grammar, its text output and its exit codes.

**Reference:** Exiv2 v0.28.9, commit
[`b0859fc61fea5dedfc898aece6b15893ddf4314e`](https://github.com/Exiv2/exiv2/tree/b0859fc61fea5dedfc898aece6b15893ddf4314e):
action names and aliases are from `exiv2.md` “4 ACTIONS”, the option inventory from “5 COMMAND
SUMMARY” and “6 OPTIONS”, and the modify grammar from “11 MODIFY COMMANDS”.

`MetadataSharp`'s own `samples/MetaTool/Program.cs` is an illustrative public-API sample, not a
compatible replacement for the Exiv2 executable.

## Actions, print modes, options and data targets

| Feature | Exiv2 0.28.9 | MetadataSharp today | Status | Evidence / note |
|---|---|---|---|---|
| Actions | `pr/print`, `ex/extract`, `in/insert`, `rm/delete`, `ad/adjust`, `mo/modify`, `mv/rename`, `fi/fixiso`, `fc/fixcom` | MetaTool implements a small print/set/delete/rename/extract/insert/fixiso/fixcom/modify subset; library operations cover several more | Partial | `samples/MetaTool/Program.cs`; `MetadataOperations`, `MetadataSync`, document writers |
| Print modes `-p` | `s,a,e,t,v,h,i,x,c,p,C,R,S,X` | MetaTool exposes `a,e,i,x,c`; the API returns typed snapshots, errors, comments, ICC bytes and preview descriptors, not Exiv2's report/hex/structure formats | Partial | Pinned `exiv2.md` “-p”; `MetaTool.Program.Print`; `ImageMetadata` |
| Fine print flags `-P` | `E,I,X,x,g,k,l,d,n,y,c,s,v,V,t,h` | Key formatting and typed/raw readers exist; no Exiv2-compatible column renderer or `-P` flag parser | Missing (CLI) | Pinned `exiv2.md` “-P”; `MetadataKeys`, `ExifTags`, `IptcDatasetNames`, `XmpPacket` |
| Exact and substring filters | `-K key`, `-g str`; repeatable | One case-insensitive substring `-K`; the library exposes metadata entries for caller filtering | Partial | `MetaTool.Program.Print` |
| Delete targets | `a,e,t,i,x,c,C,I,-` | `Delete` removes selected families; EXIF thumbnail and XMP/EXV operations exist; no common target grammar | Partial | Pinned `exiv2.md` “-d”; `MetadataOperations.Delete`, `ExifEdit.RemoveThumbnail` |
| Extract targets | `a,e,t,i,x,c,p[n,...],C,X,XX,-`; location, filtering and stdout | `ExtractExv`, `ExtractXmpSidecar`, `ExtractThumbnail`, ICC and previews by APIs; MetaTool only thumbnail/XMP file output | Partial | Pinned `exiv2.md` “-e”; `MetadataOperations`, `IccProfiles`, `Previews` |
| Insert targets | `a,e,t,i,x,c,C,X,XX,-`; location/suffix and stream filter | EXV/XMP-sidecar insert, metadata-family document saves, ICC save, EXIF thumbnail set; MetaTool only thumbnail/XMP | Partial | Pinned `exiv2.md` “-i”; `MetadataOperations`, `ExifEdit`, document APIs |
| Modify commands | `set`, `add`, `del`, `reg`, typed values, repeated commands and stdin/file scripts | EXIF/IPTC/XMP `ModifyScript` supports safe typed edits and namespace registration, but not every Exiv2 key/type/value behavior or its whole parser | Partial | Pinned `exiv2.md` “11 MODIFY COMMANDS”; `ModifyScript`, `ExifEdit`, `IptcEdit`, `XmpEdit` |
| Rename patterns | Exiv2 keywords and strftime conversions, collision prompting/force, timestamp options | `MetadataFileNames.Format` implements a documented deterministic keyword/format subset; MetaTool chooses a collision-free suffix and offers dry-run, not Exiv2's prompt/overwrite semantics | Partial | Pinned `exiv2.md` “-r”; `src/MetadataSharp.Core/Operations/MetadataFileNames.cs`; unsupported conversions are documented in its XML docs |
| Date adjustment | `-a`, `-Y`, `-O`, `-D`, timezone environment behavior | `MetadataOperations` provides date operations on metadata edits; no complete Exiv2 CLI timestamp, warning or timezone behavior | Partial | Pinned `exiv2.md` action and options; `MetadataOperations` |
| `fixiso`, `fixcom` | Dedicated `fi` and `fc`; `-n enc` selects user-comment decoding charset | `MetadataOperations.FixIso` and `FixUserComment` are library operations; MetaTool has simple commands; an encoding provider is required for code pages | Partial | Pinned `exiv2.md` actions/`-n`; `MetadataOperations`; `tests/MetadataSharp.Tests/Operations` |
| `-c txt` comment option | Set a JPEG comment as part of the `modify` action | JPEG/PNG comment writes are exposed by document APIs; MetaTool has no `-c` option or comment-edit command | Missing (CLI) | Pinned `exiv2.md` “-c”; `JpegMetadataDocument.SaveComment`, `PngMetadataDocument.SaveComment` |
| `-b` obsolete option | Obsolete; reserved for the upstream test suite | Not implemented by MetaTool and has no library operation | Missing (obsolete CLI option) | Pinned `exiv2.md` command summary; intentionally omitted |
| `-k` / file timestamps | Keep timestamps on update; `-t`/`-T` set file time on rename | No shared Exiv2-style update/rename timestamp option contract; callers control filesystem timestamps | Missing (CLI) | Pinned `exiv2.md` “-k”, “-t”, “-T” |
| Encodings and output controls | `-n`, `-q`, `-Q`, `-u`, `-v`, `-V`, `-h`, `-f`, `-F`, `-l`, `-S`, plus time offsets `-a/-Y/-O/-D` | Charset handling exists for specific metadata values and operations; the sample does not provide Exiv2 logging, unknown-tag, force, location/suffix or version semantics | Partial / missing CLI | Complete option list is in the pinned command-summary table; library-level charset behavior is in the audit |

### Print detail inventory (`-p`, `-P`)

The shorthand modes are `s` summary; `a` all EXIF/IPTC/XMP; `e` raw EXIF tags; `t` translated EXIF
values; `v` raw EXIF values; `h` EXIF hex; `i` IPTC; `x` XMP; `c` JPEG comments; `p` preview listing;
`C` ICC; `R` recursive structure; `S` structure; `X` raw XMP. The `-P` flags select families
(`E,I,X`), numeric tag (`x`), group/key/label/description/name (`g,k,l,d,n`), type/count/size
(`y,c,s`), raw/value-as-modify-command/translated output (`v,V,t`), and hex (`h`). The library
exposes values and typed interpretations, not these stable CLI text layouts; text-output parity is
not claimed anywhere.

### Data target inventory

The targets are not interchangeable: Exiv2 has `a` (all supported), `e` (EXIF), `t` (EXIF thumbnail),
`i` (IPTC), `x` (XMP), `c` (comment), `C` (ICC), `I` (all IPTC), `X` (XMP sidecar with conversion),
`XX` (EXV/raw sidecar), `p` (one or more previews), and `-` (stdin/stdout filter) across the
extract/insert/delete actions. The library exposes the corresponding operations as separate APIs and
typed edits; the action/target DSL is what this task would add. ICC profiles are returned and written
as bytes, not interpreted. Sidecar conversion is explicit; a raw EXV file's MakerNote and
image-data-address policy differs from an in-place edit. See
[operations usage and limits](OPERATIONS_USAGE_AND_LIMITS.md) and
[XMP usage and preservation limits](XMP_USAGE_AND_PRESERVATION_LIMITS.md).

## Acceptance conditions for this task

1. `-p` modes `s,a,e,t,v,h,i,x,c,p,C,R,S,X` and `-P` keys render the reference's columns byte for
   byte on the pinned corpus, or the divergence is named in a catalog document with its reason.
2. Every action and target in the tables above is either implemented or refused with a documented
   exit code and diagnostic.
3. The argument grammar (repeatable filters, stdin scripts, `--` file lists) is pinned by tests, not
   only by the sample's usage text.
