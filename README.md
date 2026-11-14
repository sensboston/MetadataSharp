# MetadataSharp

An MIT-licensed, fully managed .NET library for image metadata: EXIF, IPTC, XMP and camera
MakerNotes, read and written without decoding or recompressing image data. No native code,
no runtime dependencies. Targets .NET 8 and .NET 10.

The goal is exiv2-level functionality as an independent implementation. **Work in progress:**
see [docs/CURRENT_STATE.md][state] for what works today and what is next.

## Highlights

- Reads JPEG, PNG, WebP, TIFF-based files (including DNG and camera raw formats), CRW, MRW, RAF,
  CR3, HEIC/HEIF/AVIF, PSD, JP2, JXL, EXV, EPS, PGF, XMP sidecars and selected video containers.
  BMP, GIF and TGA recognition is limited to image dimensions. Editing is container-, family- and
  operation-specific; the [capability table][audit] and [usage limits][usage] describe the safe subset.
  An edit whose safety cannot be established is refused, never guessed.
- Typed EXIF vocabulary from the CIPA and TIFF specifications.
- MakerNotes for Canon, Nikon (including encrypted arrays), Fujifilm, Pentax, Sony, Panasonic and
  Olympus/OM System: read, and edited tag by tag where the note lies (`ExifEdit.SetByKey`); the
  note itself is never moved or rebuilt.
- Bounded parsing of hostile input: every length, offset and count is checked and budgeted.
- Continuously measured against the exiv2 0.28.9 test corpus.

## Beyond exiv2

- The PSD resource writer can add a checked ICC profile and a JPEG thumbnail, preserving unrelated
  resource and image bytes ([preservation and round-trip test](https://github.com/sensboston/MetadataSharp/blob/HEAD/tests/MetadataSharp.Tests/Psd/PsdMetadataTests.cs)).
- The JP2 complete-set writer updates EXIF, IPTC, XMP and ICC boxes while preserving unknown boxes
  and codestream bytes. The EXV creator can carry ICC profiles in numbered APP2 segments.
- TIFF-based RAW ICC editing fails closed for insertion unless the primary image target is proven.
  A valid existing primary tag 0x8773 can be replaced or removed; those edits keep image and
  MakerNote bytes fixed. DNG and camera RAW support here is this narrow ICC case, not general DNG
  write support.
- `PSD/JP2/EXV` common-metadata writer parity is gated both ways against pinned Exiv2 0.28.9:
  EXIF/IPTC/XMP for all three, exact ICC bytes both ways for JP2, and Exiv2 ICC insertion/read-back
  for EXV. Exiv2 0.28.9 documents no PSD ICC support and its ICC print path does not expose the
  authored EXV ICC representation; PSD ICC remains on MetadataSharp's format-specific tests.

## Quick start

Reference `MetadataSharp.Formats`; it brings `MetadataSharp.Core` with it. No package is published
yet, so the reference is to the project in a clone of this repository:

```xml
<ProjectReference Include="MetadataSharp/src/MetadataSharp.Formats/MetadataSharp.Formats.csproj" />
```

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
`docs/usage/OPERATIONS_USAGE_AND_LIMITS.md`.

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

## Samples

Small, standalone .NET 10 programs that use the public API (no `InternalsVisibleTo`). Each project has its own run instructions under [`samples/`](https://github.com/sensboston/MetadataSharp/tree/HEAD/samples):

- [MetaTool](https://github.com/sensboston/MetadataSharp/tree/HEAD/samples/MetaTool) — metadata print/edit operations in an exiv2-like CLI.
- [GeoTag](https://github.com/sensboston/MetadataSharp/tree/HEAD/samples/GeoTag) — attach the nearest GPX coordinate by capture time.
- [BatchRename](https://github.com/sensboston/MetadataSharp/tree/HEAD/samples/BatchRename) — collision-safe date-based rename plans.
- [StripMetadata](https://github.com/sensboston/MetadataSharp/tree/HEAD/samples/StripMetadata) — remove metadata families or preserve selected fields.
- [CopyMetadata](https://github.com/sensboston/MetadataSharp/tree/HEAD/samples/CopyMetadata) — copy supported metadata, including the MakerNote when its offsets can be preserved, and reconcile mapped values.
- [Thumbnails](https://github.com/sensboston/MetadataSharp/tree/HEAD/samples/Thumbnails) — list and extract embedded previews.
- [MetadataViewer](https://github.com/sensboston/MetadataSharp/tree/HEAD/samples/MetadataViewer) — a read-only Avalonia metadata browser.

Every CLI project is smoke-tested in CI; the viewer is built on .NET 10/Linux without a UI test.

## Documentation

- [Current state and roadmap][state]
- [Build and test][build]
- [Development policy][development]
- Usage and preservation limits: [docs/usage][usage]
- [Release-readiness audit against pinned Exiv2 0.28.9][audit] (format, family, CLI and library gaps)
- Contributor and agent rules: [METADATASHARP_PROMPT.md][rules]
- Design history: [docs/archive][archive] (read-only)

## License

MIT, see [LICENSE][license].

<!-- This file is also the README of the NuGet packages, where a relative link leads nowhere: every
     link is absolute and starts with MetadataSharpRepositoryUrl of Directory.Build.props.
     QuickStartTests.ReadmeLinksAreAbsoluteAndLeadIntoTheRepository holds them to it. -->
[state]: https://github.com/sensboston/MetadataSharp/blob/HEAD/docs/CURRENT_STATE.md
[examples]: https://github.com/sensboston/MetadataSharp/blob/HEAD/tests/MetadataSharp.Examples/QuickStart.cs
[usage]: https://github.com/sensboston/MetadataSharp/tree/HEAD/docs/usage
[audit]: https://github.com/sensboston/MetadataSharp/blob/HEAD/docs/RELEASE_READINESS_AUDIT.md
[build]: https://github.com/sensboston/MetadataSharp/blob/HEAD/docs/BUILD_AND_TEST.md
[development]: https://github.com/sensboston/MetadataSharp/blob/HEAD/docs/DEVELOPMENT.md
[rules]: https://github.com/sensboston/MetadataSharp/blob/HEAD/METADATASHARP_PROMPT.md
[archive]: https://github.com/sensboston/MetadataSharp/tree/HEAD/docs/archive
[license]: https://github.com/sensboston/MetadataSharp/blob/HEAD/LICENSE
