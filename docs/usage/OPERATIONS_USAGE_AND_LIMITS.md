# Operations of the exiv2 command line: usage and limits

What `exiv2` does from the command line (pinned 0.28.9) is library API here. The functional
reference is the tool's help and its behaviour; no code was taken.

| exiv2 | Here |
| --- | --- |
| `ad -a -Y -O -D` | `MetadataTimestamps.Adjust(exif, iptc, xmp, shift)` → `SyncEdits` |
| `mv -r format` | `MetadataFileNames.Format(format, taken, path)` → the name; the caller renames |
| `-m file`, `-M "command"` | `ModifyScript.Parse(script, byteOrder)` → `Exif`, `Iptc`, `Xmp` edits |
| `rm -d a/e/i/x/c/C/t` | `MetadataOperations.Delete(source, destination, families)` |
| `ex -e a/e/i/x/c` | `MetadataOperations.ExtractExv(file, families)` |
| `ex -e X` (`-e XX`) | `MetadataOperations.ExtractXmpSidecar(file, convert)` |
| `ex -e t` | `MetadataOperations.ExtractThumbnail(file)` |
| `in -i a/e/i/x/c/t` | `MetadataOperations.InsertExv(file, exv, families, out notes)` |
| `in -i X` (`-i XX`) | `MetadataOperations.InsertXmpSidecar(file, sidecar, convert)` |
| `in -i t` | `ExifEdit.SetThumbnail(jpeg)`; `ExifEdit.RemoveThumbnail()` |
| `fi` | `MetadataOperations.FixIso(exif)` → `ExifEdit?` |
| `fc -n charset` | `MetadataOperations.FixUserComment(exif, encoding)` → `ExifEdit?` |
| EXV files | `ExvFile.Create(exif, iptc, xmp, comment, thumbnail)`; read by `ImageMetadata.Read` |

`MetadataKeys` formats and parses keys as the tool prints them. Core
(`MetadataSharp.Core.Operations`) computes edits and writes nothing; `MetadataSharp.Formats.Operations`
works on files. Edits are written by the documents (`SaveAll`, `Save`).

```csharp
// exiv2 ad -a 1:30 -D 2 photo.jpg
ImageMetadata read = ImageMetadata.Read(bytes);
SyncEdits edits = MetadataTimestamps.Adjust(read.Exif, read.Iptc, read.Xmp,
    new TimestampShift(0, 0, 2, new TimeSpan(1, 30, 0)));
using var document = JpegMetadataDocument.Open(path);
document.SaveAll(output, edits);

// exiv2 -M "set Iptc.Application2.Keywords travel" -M "del Exif.Image.Software"
ModifyScript script = ModifyScript.Parse("set Iptc.Application2.Keywords travel\ndel Exif.Image.Software");

// exiv2 rm -d xc ; exiv2 ex -e a ; exiv2 in -i a
MetadataOperations.Delete(source, destination, MetadataFamilies.Xmp | MetadataFamilies.Comment);
byte[] exv = MetadataOperations.ExtractExv(bytes);
byte[] filled = MetadataOperations.InsertExv(other, exv, MetadataFamilies.All, out IReadOnlyList<string> notes);
```

## Where this differs from exiv2, on purpose

- **Adjust** moves the XMP and IPTC counterparts of the EXIF timestamps with them, each block its
  own value; the tool moves the three EXIF tags only. SubSec and OffsetTime stay. Month arithmetic
  is the tool's (31 January + 1 month = 3 March). A value that is no timestamp is a note, and the
  others still move (the tool fails the file).
- **File names**: the C-locale subset of `strftime` and `:basename:`, `:basesuffix:`, `:dirname:`,
  `:parentname:`. Conversions that depend on locale or zone are refused, and so is a result with a
  path separator. A name that is taken is the caller's matter.
- **Modify**: EXIF `add` is `set` (no duplicate tags). IPTC `set` replaces the first occurrence and
  `add` appends, as the tool does. A key of a MakerNote group (`Exif.Canon.OwnerName`,
  `Exif.CanonCs.Quality`) changes a tag the note of the file has, where it is; see below. A stream that is created
  declares UTF-8 (1:90) and its record version (2:0). The type of an XMP key without one comes
  from the sync tables (`MetadataSync.ExifMappings`, `IptcMappings`) and the Dublin Core arrays;
  any other key is text, where the tool has a table of every schema it knows: name the type
  (`XmpBag`, `XmpSeq`, `LangAlt`) for an array outside those. The first bad line throws
  `ModifyScriptException` with its `Line`.
- **The MakerNote is never written anew.** Its bytes stay where they are in every operation; the
  tool writes it again with every save, which changes offsets it holds and sometimes its byte
  order. For the same reason **`InsertExv` and `ExvFile.Create` do not carry the MakerNote** or
  the tags of image data (the tool carries the MakerNote); `InsertExv` says so in its notes.
- **Tags of the MakerNote are changed where they are** (`ExifEdit.Set(string, ExifValue)`,
  `SetText`, `Remove(string)`, and the same keys in a modify script), in the EXIF block of a JPEG,
  PNG or WebP file. The note keeps its offset and its length and nothing in it moves, so no offset
  it holds changes, whatever the maker counts it from. What is done: a value of the stored type
  that is no longer than the stored one (or fits the four bytes of its entry) is written in its
  place and the rest cleared; a field of an array is written into the array, which is deciphered
  and enciphered again where it is stored so (Nikon with the key of the file, Sony); a tag is
  removed by moving the entries after it up, the count less one, the offset of the next directory
  kept, the freed bytes cleared. What is refused: a tag the note does not hold, a value of another
  type, a value that grows, a file without a note the library decodes, TIFF-based and BMFF files
  (`UnsupportedFeature`); the tags that address data or a directory, their lengths, and Nikon's
  SerialNumber and ShutterCount, which key the cipher (`TagNotEditable`); the last tag of a
  directory and a directory with entries the reader skips (removal only). The tool writes the
  whole note again and takes any of these. After the change the block is read again: every tag,
  field and element that was not named reads byte for byte as before, the named ones as set, and
  every changed byte lies where a change was planned; otherwise `UnsafeRelocation` and nothing is
  written.
- **Insert**: the IPTC stream written declares UTF-8 and the writer's record version, not those of
  the EXV file; a dataset whose text is not UTF-8, from a stream without a declared character
  set, and a dataset outside records 1 and 2 are not carried, and noted. A value of no elements
  or of a field type TIFF does not define is not written. The XMP packet is carried whole
  (`XmpEdit.ReplaceWith`), with its structures. In a TIFF-based file the tags are set over the
  existing ones, since the tags that describe its image cannot be removed.
- **Thumbnail**: a directory that is created states 72 dpi (XResolution, YResolution,
  ResolutionUnit), which Exif requires; the tool writes the compression and the stream only. A
  directory that is replaced keeps its other tags. The block has to fit one APP1 segment
  (`ExifTooLarge` otherwise). JPEG, PNG and WebP; TIFF-based and BMFF documents refuse the edit.
- **Extract**: a JPEG or EXV source is copied segment by segment, so the block is byte for byte the
  source's. Any other source is built anew by `ExvFile.Create` (see the MakerNote above), and its
  ICC profile is not carried.
- **Delete** removes a family whose reader failed (the bytes go, unread), skips a family the
  container cannot hold, and refuses containers no document writes (EXV, PSD, sidecars).
- A file whose structure cannot be established is refused where the tool writes it anyway:
  duplicate tags, several XMP packets, IPTC resources that differ, malformed directories.

## Measured against the tool

CI job "Exiv2 0.28.9 / operations parity" (`tools/reference/operations_parity.py`, push runs; run
43290037609): each operation is run by the tool and by this library on a copy of each of the
152 JPEG-named files of the tool's corpus, and `exiv2 -pa` of both results is compared key by key.
The print-out of the source says who is responsible for a difference; a difference that is not
documented in the script fails the job.

| Operation | Files compared | Tool failed | Refused here | Same keys | Documented differences |
| --- | ---: | ---: | ---: | ---: | --- |
| adjust | 56 | 73 | 23 | 4,689 | 29 XMP/IPTC dates we also move; 10 MakerNote |
| modify | 96 | 14 | 42 | 5,137 | 166 declared character set and record version; 30 MakerNote |
| delete all | 110 | 13 | 29 | nothing left on either side | |
| delete thumbnail | 119 | 13 | 20 | 5,772 | 27 MakerNote; 1 tag the tool drops |
| delete XMP + comment | 125 | 13 | 14 | 5,465 | 14 MakerNote; 1 tag the tool drops |
| extract | 136 | 9 | 7 | 6,890 | 43 MakerNote; 3 the tool drops; 2 values of a rebuilt EXV |
| insert | 134 | 9 | 9 | 3,858 | 2,384 not carried (2,352 MakerNote; the rest tags of a thumbnail not read as a JPEG stream and IPTC text that is not UTF-8); 20 IPTC declarations of the writer; 35 keys only here (thumbnail resolution, IPTC declarations); 7 where the tool's result differs from its own EXV file; 11 empty or undefined values |
| set thumbnail | 117 | 13 | 22 | 5,784 | 231 resolution tags; 13 MakerNote; 1 the tool drops |
| fixiso | 65 | 70 | 17 | 5,688 | 8 MakerNote; 1 the tool drops |
| fixcom | 65 | 68 | 19 | 5,692 | |
| rename | 53 | 88 | 11 | 53 names | |

The tool fails mostly on files without EXIF (59, for adjust, fixiso, fixcom, rename) and files
without a timestamp tag (18, rename). This library refuses malformed or ambiguous files (see the
last item above); the reasons are listed in the job's annotations.
