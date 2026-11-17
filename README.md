# MetadataSharp

[![NuGet](https://img.shields.io/nuget/v/MetadataSharp)](https://www.nuget.org/packages/MetadataSharp)
[![CI](https://github.com/sensboston/MetadataSharp/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/sensboston/MetadataSharp/actions/workflows/build-and-test.yml)
[![.NET](https://img.shields.io/badge/.NET-8%20%7C%2010-512BD4)](https://dotnet.microsoft.com)
![Platforms](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey)
[![License](https://img.shields.io/badge/license-MIT-blue)][license]

A fully managed .NET library for image metadata: EXIF with camera MakerNotes, IPTC, XMP, ICC
profiles, comments and previews, read and written without decoding or recompressing image data.
No native code and no dependencies beyond the .NET base library. For .NET 8 and later.

```
dotnet add package MetadataSharp
```

## Exiv2

[Exiv2](https://exiv2.org) is the reference for what this library does: the formats it reads, the
tags it knows, the text it prints for a value and the edits it allows. MetadataSharp is not a port
of it. The code is an independent implementation written from the specifications (CIPA DC-008,
TIFF 6.0, IPTC IIM, XMP, ISO/IEC 14496-12 and others) and from the facts the formats themselves
state. What exiv2 gives this project is a measure: CI fetches exiv2 0.28.9 at a pinned commit, runs
both tools over its test corpus and over this repository's own files, and compares the output tag
by tag. Nothing from exiv2 is copied into this repository.

Thanks to the exiv2 team for two decades of careful work on a hard problem, and to Phil Harvey,
whose ExifTool documents more camera formats than any other source. The lens identification
tables are facts gathered by both projects.

## Why

- **Fully managed.** No P/Invoke, no native binary: it runs wherever .NET runs, including
  containers, cloud functions, ARM64 and trimmed or native AOT applications.
- **Hostile input is the normal case.** Every length, offset and count is checked before it is
  used, pointer graphs are treated as possibly cyclic, and the work a file may cause is bounded
  by `MetadataLimits`. Every parser and writer is fuzzed in CI.
- **An edit is proven safe or refused.** A write is analysed, planned and checked before a byte
  is written. When its safety cannot be established, for example a MakerNote whose offsets would
  break if it moved, the edit is refused with a `MetadataErrorCode` instead of done on a guess.
- **Made for .NET.** Immutable results, streams and async reads, nullable annotations and XML
  documentation on every public member.

## Formats

- **Read:** JPEG, PNG, WebP, TIFF, DNG and the TIFF-based raw formats (CR2, NEF, ARW, PEF, ORF,
  RW2, SRW and others), CRW, MRW, RAF, CR3, HEIC/HEIF/AVIF, JPEG XL, JPEG 2000, PSD, EXV, EPS, PGF,
  XMP sidecars and the metadata of common video containers (MP4/MOV, AVI, MKV, ASF, WAV). BMP, GIF
  and TGA give their image dimensions.
- **Edit:** EXIF, IPTC, XMP and ICC in JPEG, PNG, WebP, TIFF-based files, HEIC/HEIF/AVIF, PSD,
  JPEG 2000 and EXV, and CRW in place. What each format allows is in the
  [capability table][audit] and the [usage limits][usage].
- **MakerNotes** of Canon, Nikon (including the encrypted arrays), Sony, Fujifilm, Pentax,
  Panasonic, Olympus/OM System and others are read and interpreted, and edited tag by tag where
  the note lies; a note is never moved or rebuilt unless its offsets are proven to survive.
- **A typed tag catalog** for EXIF, GPS, Interoperability, the MakerNotes, IPTC and XMP, with the
  print conversion a metadata tool shows ("1/250 s", "F2.8", "right, top").

## Quick start

The snippets below are the code of [tests/MetadataSharp.Examples/QuickStart.cs][examples], which
CI compiles and runs on .NET 8 and .NET 10. In them `path`, `source` and `destination` are file
paths and `upload` is a `Stream`. They use these namespaces:

```csharp
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Formats.Png;
using MetadataSharp.Formats.Tiff;
```

### Read any file

`ImageMetadata.Read` tells the format by the signature of the file, not by its name, and returns
everything the library reads from it: EXIF with the MakerNote, IPTC, XMP, the ICC profile, the
comments and the list of previews. It takes a path, a stream or the bytes.

```csharp
ImageMetadata image = ImageMetadata.Read(path);
Console.WriteLine(image.Format);                  // Jpeg, Png, WebP, Tiff, Bmff, ...

if (image.Exif is { } exif)
    foreach (ExifEntry entry in exif.Entries)
    {
        string name = ExifTags.Find(entry.Key)?.Name ?? $"0x{entry.Key.TagId:X4}";
        string value = ExifTags.Interpret(exif, entry.Key)               // "1/250 s", "right, top"
            ?? (entry.Value.TryGetString(out string? text) ? text : $"{entry.Value.FieldType} x {entry.Value.Count}");
        Console.WriteLine($"{entry.Key.Directory}.{name} = {value}");
    }

// A block that is damaged, or outside what the library reads, does not throw: it is
// reported here and the other blocks are still returned.
foreach (MetadataReadError error in image.Errors)
    Console.WriteLine($"{error.Family}: {error.Code}");
```

### Read single tags

`ExifMetadata` has a property for each of the tags an application asks for first: `Make`, `Model`,
`LensModel`, `DateTimeOriginal`, `DateTaken`, `Orientation`, `ExposureTime`, `FNumber`, `IsoSpeed`,
`FocalLength`, `PixelWidth`, `PixelHeight` and `GpsPosition`, the two dates also with their offset
from UTC. For every other tag `ExifValue` has a `TryGet…` accessor per field type.

```csharp
ExifMetadata? exif = ImageMetadata.Read(path).Exif;

// The tags asked for first are properties. Each is null when the file has no such tag, or
// has something in it that cannot be read as the property's type; none of them throws.
string? camera = exif?.Model;                          // "Example 100"
DateTime? taken = exif?.DateTaken;                     // DateTimeOriginal, else DateTimeDigitized, else DateTime
DateTimeOffset? instant = exif?.DateTakenWithOffset;   // with the OffsetTime tag of that date, when the file has it
UnsignedRational? exposure = exif?.ExposureTime;       // 1/250, the fraction the file stores
double? aperture = exif?.FNumber;                      // 2.8
int? iso = exif?.IsoSpeed;                             // 400
ExifOrientation? orientation = exif?.Orientation;      // RightTop
GpsPosition? position = exif?.GpsPosition;             // decimal degrees; south and west are negative

// Any other tag is found by its key and read with the accessor for its field type.
string? software = null;
if (exif is not null && exif.TryGetEntry(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0131), out ExifEntry? entry) &&
    entry.Value.TryGetString(out string? text))
    software = text;

// Interpret gives the text a metadata tool prints for a tag: "1/250 s", "F2.8", "right, top".
string? printed = exif is null ? null : ExifTags.Interpret(exif, new ExifTagKey(ExifDirectoryKind.Exif, 0x829A));
```

### Read XMP and IPTC

`XmpPacket` has `Title`, `Description`, `Creators`, `Rights`, `Subjects` and `Rating`; `IptcPacket`
has `ObjectName`, `Headline`, `Caption`, `Keywords`, `Bylines` and `Copyright`. Everything else is
in `Entries` and `Datasets`.

```csharp
var lines = new List<string>();
ImageMetadata image = ImageMetadata.Read(path);

if (image.Xmp is { } xmp)
{
    // The descriptive properties of Dublin Core and the rating; a text in several languages
    // gives its x-default. A list the packet does not have is empty, a text is null.
    lines.Add($"title: {xmp.Title}");
    lines.Add($"creators: {string.Join(", ", xmp.Creators)}");
    lines.Add($"subjects: {string.Join(", ", xmp.Subjects)}");
    lines.Add($"rating: {xmp.Rating}");

    foreach (XmpEntry property in xmp.Entries)       // every property; nested ones have a path
        lines.Add($"{property.Prefix}:{property.Path} = {string.Join("; ", property.Values)}");
}

if (image.Iptc is { } iptc)
{
    lines.Add($"headline: {iptc.Headline}");
    lines.Add($"caption: {iptc.Caption}");
    lines.Add($"keywords: {string.Join(", ", iptc.Keywords)}");

    foreach (IptcDataset dataset in iptc.Datasets)   // every dataset, by record and number
    {
        IptcDatasetNames.TryGetName(dataset.Key, out string? name);
        byte[] raw = new byte[dataset.Value.ByteLength];
        dataset.Value.CopyRawTo(raw);
        string text = IptcDatasetNames.Interpret(dataset.Key, dataset.Value) ?? Encoding.UTF8.GetString(raw);
        lines.Add($"{dataset.Key.Record}:{dataset.Key.Dataset} {name} = {text}");
    }
}
```

### Edit EXIF in a JPEG

An edit names the tags to set and to remove; everything it does not name stays as it was. A save
writes a new file or an empty stream and never overwrites its source. Any tag of the IFD0, EXIF, GPS,
Interoperability and thumbnail directories can be set: text and numbers through the typed overloads,
anything else as a raw `ExifValue`. `Set` holds a tag the Exif vocabulary lists to the field type and
count it prescribes; `SetUnchecked` writes the value as given. The pointers between directories, the
addresses of image data and the MakerNote are not edited as values (`ExifEdit.IsEditable`); a camera's
MakerNote stays where it is, and only the tags of it that are named by key (`SetByKey`, `SetTextByKey`,
`RemoveByKey`: `Exif.Canon.OwnerName`) change. A thumbnail directory is edited where a file has one and is
not created.

```csharp
var edit = new ExifEdit();
edit.Set(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x013B), "Jane Doe");               // Artist
edit.Set(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x8298), "(c) 2026 Jane Doe");      // Copyright
edit.Set(new ExifTagKey(ExifDirectoryKind.Exif, 0x9003), "2026:11:11 09:30:00");    // DateTimeOriginal
edit.Set(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0112), (ushort)1);                // Orientation: top, left
edit.Set(new ExifTagKey(ExifDirectoryKind.Gps, 0x0006), new UnsignedRational(1255, 10));   // GPSAltitude: 125.5 m
edit.Remove(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0131));                        // Software

using (JpegMetadataDocument document = JpegMetadataDocument.Open(source))
    document.SaveAs(destination, edit);   // a new file; the image data is copied, never re-encoded
```

### Edit XMP in a PNG

`JpegMetadataDocument`, `WebPMetadataDocument`, `TiffBasedMetadataDocument` and
`BmffMetadataDocument` (HEIC, HEIF, AVIF) take the same `XmpEdit`.

```csharp
const string DublinCore = "http://purl.org/dc/elements/1.1/";

var edit = new XmpEdit();
edit.Set(new XmpPropertyName(DublinCore, "title"),
    XmpValue.CreateLanguageAlternative([new("x-default", "Harbour at dawn")]));
edit.Set(new XmpPropertyName(DublinCore, "subject"),
    XmpValue.CreateArray(XmpValueKind.Bag, ["harbour", "dawn"]));
edit.Set(new XmpPropertyName("http://ns.adobe.com/xap/1.0/", "Rating"), XmpValue.CreateText("4"));

using (PngMetadataDocument document = PngMetadataDocument.Open(source))
    document.SaveXmpAs(destination, edit);
```

### Edit IPTC (JPEG, TIFF-based files, PNG)

Any dataset of `IptcDatasets.All` can be set (`edit.Set(new IptcDatasetKey(2, 10), "5")`), with typed
setters for the common ones (`SetCaption`, `SetKeywords`, `SetDateCreated`, ...); the TIFF-based and
PNG documents have the same `SaveIptc` methods.

```csharp
var edit = new IptcEdit();
edit.ReplaceText(new IptcDatasetKey(2, 120), ["Fishing boats leaving the harbour."]);   // caption
edit.ReplaceText(new IptcDatasetKey(2, 25), ["harbour", "dawn"]);                       // keywords
edit.Remove(new IptcDatasetKey(2, 116));                                                // copyright notice

using (JpegMetadataDocument document = JpegMetadataDocument.Open(source))
    document.SaveIptcAs(destination, edit);
```

### Keep EXIF, IPTC and XMP in agreement

`MetadataSync` (namespace `MetadataSharp.Core.Sync`) converts between the three: `ExifToXmp`,
`XmpToExif`, `IptcToXmp`, `XmpToIptc`, and `Reconcile`, which reads the shared fields (caption,
creator, copyright, keywords, the dates and more) the way the Metadata Working Group guidelines
do and brings the blocks that exist into agreement. Each returns `SyncEdits`: one edit per block,
`Notes` for every value that could not be converted, `Choices` for the block each reconciled field
was taken from. Nothing is written until the edits are passed to `SaveAll` / `SaveAllAs` of the
JPEG, PNG, WebP, BMFF or TIFF-based document, which writes the blocks in one pass.
`ImageMetadata.Reconcile()` does it for a file that was read. The mapping tables are public
(`MetadataSync.ExifMappings`, `IptcMappings`). Details and limits: `docs/usage/SYNC_USAGE_AND_LIMITS.md`.

### Do what the exiv2 command line does

`MetadataOperations` (`MetadataSharp.Formats.Operations`) has the file operations of the tool:
`Delete` (`exiv2 rm -d`), `ExtractExv` (`ex -e`), `InsertExv` (`in -i`). `ModifyScript.Parse`
(`MetadataSharp.Core.Operations`) turns the tool's modify commands (`-M "set Exif.Image.Artist Ann"`)
into edits for `SaveAll`.

Also: shifting timestamps (`MetadataTimestamps.Adjust`), file names from the date taken
(`MetadataFileNames.Format`), thumbnails (`ExifEdit.SetThumbnail`), XMP sidecars, `FixIso`,
`FixUserComment`. The table of operations, the differences from exiv2 and the measured parity:
`docs/usage/OPERATIONS_USAGE_AND_LIMITS.md`. The `exiv2` command line itself — actions, `-p`/`-P`
modes, targets and options — is not reproduced by the library or the sample tool; that work is
`docs/usage/METATOOL_CLI_PARITY.md`.

### Edit a TIFF, a DNG or a raw file

```csharp
// TIFF, DNG and the TIFF-based raw formats (CR2, NEF, ARW, PEF, ORF, RW2).
var edit = new ExifEdit();
edit.Set(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0112), (ushort)6);   // Orientation: right, top

using (TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(source))
    document.SaveExifAs(destination, edit);   // no byte of the source moves; image data is not written to
```

Canon CRW files are no TIFF structure: `CrwMetadataDocument` (`MetadataSharp.Formats.Crw`) writes
the tags and the comment back into the records they were read from, with the same `ExifEdit`.
Minolta MRW and Fujifilm RAF are read (`ImageMetadata.Read`), not written. Details:
`docs/usage/CRW_MRW_RAF_USAGE_AND_LIMITS.md`.

### Read untrusted input

```csharp
// Every length, offset and count of the input is checked against a budget before it is used.
var limits = new MetadataLimits
{
    MaxInputBytes = 32 * 1024 * 1024,       // a longer file: MetadataException, LimitExceeded
    MaxTotalEntries = 1_000,                // a block over a budget: left out, listed in Errors
    MaxRetainedMetadataBytes = 512 * 1024,
};
ImageMetadata image = await ImageMetadata.ReadAsync(upload, limits, cancellationToken);
bool overBudget = image.Errors.Any(error => error.Code == MetadataErrorCode.LimitExceeded);
```

### A refused edit

```csharp
var edit = new ExifEdit();
edit.RemoveAllExif();
MetadataErrorCode? refusal = null;
try
{
    using PngMetadataDocument document = PngMetadataDocument.Open(source);
    document.SaveExifAs(destination, edit);
}
catch (MetadataException e)
{
    // An edit whose safety cannot be established is refused, not guessed: the code
    // says why, and no destination file is left behind.
    refusal = e.Code;
}
```

What each format keeps and where its limits are: [docs/usage][usage].

## Beyond exiv2

- `Previews.List` reports bounded absolute offsets, lengths, MIME types and dimensions; `Previews.Extract`
  returns validated JPEG data, generated TIFF thumbnails, encoded BMFF HEVC/AV1 bytes, native EPS/TIFF/WMF data, and the
  generated EPS/XMP thumbnail forms without decoding image pixels. The usage and limits, including
  the transformations and write scope, are in [PREVIEWS_USAGE_AND_LIMITS.md](https://github.com/sensboston/MetadataSharp/blob/HEAD/docs/usage/PREVIEWS_USAGE_AND_LIMITS.md).
  Range assertions live in [PreviewTests.cs](https://github.com/sensboston/MetadataSharp/blob/HEAD/tests/MetadataSharp.Tests/Containers/PreviewTests.cs);
  the strict pinned `exiv2 -pp` list gate and `-ep`-derived offset comparison are in
  [Previews.cs](https://github.com/sensboston/MetadataSharp/blob/HEAD/tools/MetadataSharp.Harness/Parity/Previews.cs) and [PreviewsTests.cs](https://github.com/sensboston/MetadataSharp/blob/HEAD/tools/MetadataSharp.Harness.Tests/Parity/PreviewsTests.cs).
- The PSD resource writer can add a checked ICC profile and a JPEG thumbnail, preserving unrelated
  resource and image bytes ([preservation and round-trip test](https://github.com/sensboston/MetadataSharp/blob/HEAD/tests/MetadataSharp.Tests/Psd/PsdMetadataTests.cs)).
- The JP2 complete-set writer updates EXIF, IPTC, XMP and ICC boxes while preserving unknown boxes
  and codestream bytes. The EXV creator can carry ICC profiles in numbered APP2 segments.
- TIFF-based RAW ICC editing fails closed unless the primary image target is proven. One bounded
  DNG layout accepts insertion: an un-reduced, uncompressed RGB primary with an accounted strip and a separate
  CFA SubIFD; other DNG/camera-RAW layouts still require an existing valid primary tag 0x8773 for
  replacement/removal. Tests pin the image and MakerNote bytes; this is not general DNG write support.
- DCP profiles use their `0x4352` TIFF identifier for bounded EXIF reading and restricted,
  offset-preserving EXIF edits; DCP-specific color-profile semantics remain outside the API
  ([reader and writer tests](https://github.com/sensboston/MetadataSharp/blob/HEAD/tests/MetadataSharp.Tests/Tiff/TiffBasedWriteTests.cs)).
- IPTC IIM stored in TIFF EXIF carriers is read from JXL, CR3 and HEIF-family BMFF files; these
  formats remain read-only for IPTC ([JXL](https://github.com/sensboston/MetadataSharp/blob/HEAD/tests/MetadataSharp.Tests/Jxl/JxlMetadataTests.cs),
  [CR3](https://github.com/sensboston/MetadataSharp/blob/HEAD/tests/MetadataSharp.Tests/Cr3/Cr3ExifTests.cs),
  [BMFF](https://github.com/sensboston/MetadataSharp/blob/HEAD/tests/MetadataSharp.Tests/Bmff/BmffMetadataTests.cs)).
- `PSD/JP2/EXV` common-metadata writer parity is gated both ways against pinned Exiv2 0.28.9:
  EXIF/IPTC/XMP for all three, exact ICC bytes both ways for JP2, and Exiv2 ICC insertion/read-back
  for EXV. Exiv2 0.28.9 documents no PSD ICC support and its ICC print path does not expose the
  authored EXV ICC representation; PSD ICC remains on MetadataSharp's format-specific tests.
- A whole TIFF-based file lists the image directories its primary directory names with tag 0x014A as
  `Exif.SubImage1.*`, `Exif.SubImage2.*` in `ExifMetadata.SubImages`, apart from the metadata; a
  MakerNote directory that states one tag ID twice lists both entries, and an entry of a field type
  TIFF does not define is read one byte per element with the code the file states (such a block is
  not editable).
- `MetadataTagCatalog` exposes the checked Exif/TIFF and MakerNote group/tag identities plus the
  registered XMP schema/property names and prefix aliases. Descriptive prose in the catalog is written
  locally and is not copied from Exiv2. The pinned `-pt` differential gate compares 329 corpus files
  column by column with Exiv2 0.28.9 and reports no differing row; the reference rows
  the local reader does not answer, and the exact code-to-name pairs where the two vocabularies differ,
  are named by file and key in the [tag print refusal catalog](https://github.com/sensboston/MetadataSharp/blob/HEAD/docs/usage/TAG_PRINT_REFUSAL_CATALOG.md).
- lens identification data cross-checked against Exiv2 and ExifTool
- IPTC text reads select a packet-wide ASCII, UTF-8 or ISO-8859-1 fallback for display while preserving
  the source bytes; malformed data under an explicit UTF-8 marker remains an error. EXIF `UserComment`
  interpretation accepts an optional caller-supplied `Encoding` for Exiv2 `-n`-style decoding; neither
  option changes the stored metadata. See the [tag catalog and charset limits](https://github.com/sensboston/MetadataSharp/blob/HEAD/docs/usage/TAG_CATALOG_AND_CHARSET_LIMITS.md), the IPTC usage limits and `ExifInterpretationOptions`.

## Samples

Small, standalone .NET 10 programs that use the public API (no `InternalsVisibleTo`). Each project has its own run instructions under [`samples/`](https://github.com/sensboston/MetadataSharp/tree/HEAD/samples):

- [MetaTool](https://github.com/sensboston/MetadataSharp/tree/HEAD/samples/MetaTool) — an approximate Exiv2 command line: the nine actions, the `-p` modes and `-P` columns, the extract/insert/delete targets, modify scripts and rename patterns, all on the public API ([every action and option](https://github.com/sensboston/MetadataSharp/blob/HEAD/samples/MetaTool/README.md), [where it differs from the reference](https://github.com/sensboston/MetadataSharp/blob/HEAD/docs/usage/METATOOL_CLI_PARITY.md)).
- [GeoTag](https://github.com/sensboston/MetadataSharp/tree/HEAD/samples/GeoTag) — attach the nearest GPX coordinate by capture time.
- [BatchRename](https://github.com/sensboston/MetadataSharp/tree/HEAD/samples/BatchRename) — collision-safe date-based rename plans.
- [StripMetadata](https://github.com/sensboston/MetadataSharp/tree/HEAD/samples/StripMetadata) — remove metadata families or preserve selected fields.
- [CopyMetadata](https://github.com/sensboston/MetadataSharp/tree/HEAD/samples/CopyMetadata) — copy supported metadata, including the MakerNote when its offsets can be preserved, and reconcile mapped values.
- [Thumbnails](https://github.com/sensboston/MetadataSharp/tree/HEAD/samples/Thumbnails) — list and extract embedded previews.
- [MetadataViewer](https://github.com/sensboston/MetadataSharp/tree/HEAD/samples/MetadataViewer) — a read-only Avalonia metadata browser.

Every CLI project is smoke-tested in CI; the viewer is built on .NET 10/Linux without a UI test.

## Status

Measured in CI against exiv2 0.28.9 on its own test corpus, on a pinned corpus of CC0 camera raw
files and on the authored files of this repository:

- The tag table (`exiv2 -pt`) of 329 corpus files: no differing row. The rows one side does not
  answer, and the code-to-name pairs where the two vocabularies differ, are named by file and key
  in the [tag print refusal catalog][refusals].
- Interpreted MakerNote values: 12,645 of 12,645 ([details][makernotes]).
- Preview lists (`exiv2 -pp`): 46 of 46 files.
- Edits, copies and deletions: compared with what exiv2 writes for the same operation, both ways
  where exiv2 reads the result.

Tested on Windows, Linux and macOS with .NET 8 and .NET 10.

## Building and testing

```
dotnet build MetadataSharp.sln -c Release
dotnet test -c Release
```

The differential gates need exiv2 and the corpora, which CI fetches at pinned versions; how to
run them locally is in [Build and test][build].

## Documentation

- [Build and test][build]
- [Development policy][development]
- Usage and preservation limits: [docs/usage][usage]
- [Format and operation coverage][audit] against exiv2 0.28.9

## License

MIT, see [LICENSE][license]. The package carries the IPTC NewsCodes vocabularies (CC BY 4.0) and
the PLUS License Data Format vocabularies; their notices are in [THIRD-PARTY-NOTICES.txt][notices].

<!-- This file is also the README of the NuGet packages, where a relative link leads nowhere: every
     link into the repository is absolute and starts with MetadataSharpRepositoryUrl of
     Directory.Build.props. QuickStartTests.ReadmeLinksAreAbsoluteAndLeadIntoTheRepository holds them to it. -->
[examples]: https://github.com/sensboston/MetadataSharp/blob/HEAD/tests/MetadataSharp.Examples/QuickStart.cs
[usage]: https://github.com/sensboston/MetadataSharp/tree/HEAD/docs/usage
[audit]: https://github.com/sensboston/MetadataSharp/blob/HEAD/docs/usage/FORMAT_AND_OPERATION_COVERAGE.md
[refusals]: https://github.com/sensboston/MetadataSharp/blob/HEAD/docs/usage/TAG_PRINT_REFUSAL_CATALOG.md
[makernotes]: https://github.com/sensboston/MetadataSharp/blob/HEAD/docs/MakerNote-interpreted-parity.md
[build]: https://github.com/sensboston/MetadataSharp/blob/HEAD/docs/BUILD_AND_TEST.md
[development]: https://github.com/sensboston/MetadataSharp/blob/HEAD/docs/DEVELOPMENT.md
[notices]: https://github.com/sensboston/MetadataSharp/blob/HEAD/THIRD-PARTY-NOTICES.txt
[license]: https://github.com/sensboston/MetadataSharp/blob/HEAD/LICENSE
