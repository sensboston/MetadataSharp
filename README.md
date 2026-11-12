# MetadataSharp

An MIT-licensed, fully managed .NET library for image metadata: EXIF, IPTC, XMP and camera
MakerNotes, read and written without decoding or recompressing image data. No native code,
no runtime dependencies. Targets .NET 8 and .NET 10.

The goal is exiv2-level functionality as an independent implementation. **Work in progress:**
see [docs/CURRENT_STATE.md](docs/CURRENT_STATE.md) for what works today and what is next.

## Highlights

- JPEG, PNG, WebP, TIFF, EXV, DNG (read), XMP sidecars; restricted, safety-first editing for
  JPEG, PNG, WebP and TIFF: an edit whose safety cannot be established is refused, never guessed.
- Typed EXIF vocabulary from the CIPA and TIFF specifications.
- Read-only MakerNotes for Canon, Nikon (including encrypted arrays), Fujifilm, Pentax, Sony,
  Panasonic and Olympus/OM System.
- Bounded parsing of hostile input: every length, offset and count is checked and budgeted.
- Continuously measured against the exiv2 0.28.9 test corpus.

## Quick start

Reference `MetadataSharp.Formats`; it brings `MetadataSharp.Core` with it. No package is published
yet, so the reference is to the project in a clone of this repository:

```xml
<ProjectReference Include="MetadataSharp/src/MetadataSharp.Formats/MetadataSharp.Formats.csproj" />
```

The snippets below are the code of
[tests/MetadataSharp.Examples/QuickStart.cs](tests/MetadataSharp.Examples/QuickStart.cs), which CI
compiles and runs on .NET 8 and .NET 10. In them `path`, `source` and `destination` are file paths
and `upload` is a `Stream`. They use these namespaces:

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
        string value = ExifTags.Interpret(exif, entry.Key) ?? $"{entry.Value.FieldType} x {entry.Value.Count}";
        Console.WriteLine($"{entry.Key.Directory}.{name} = {value}");
    }

// A block that is damaged, or outside what the library reads, does not throw: it is
// reported here and the other blocks are still returned.
foreach (MetadataReadError error in image.Errors)
    Console.WriteLine($"{error.Family}: {error.Code}");
```

### Read single tags

```csharp
var model = new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0110);
var exposureTime = new ExifTagKey(ExifDirectoryKind.Exif, 0x829A);
var orientation = new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0112);

ExifMetadata? exif = ImageMetadata.Read(path).Exif;
string? camera = null;
if (exif is not null && exif.TryGetEntry(model, out ExifEntry? entry))
{
    // A value is its raw bytes, with the field type, the count and the byte order.
    byte[] raw = new byte[entry.Value.ByteLength];
    entry.Value.CopyRawTo(raw);
    camera = Encoding.ASCII.GetString(raw).TrimEnd('\0');
}

// Interpret gives the text a metadata tool prints: "1/250 s", "right, top".
string? exposure = exif is null ? null : ExifTags.Interpret(exif, exposureTime);
string? rotation = exif is null ? null : ExifTags.Interpret(exif, orientation);
```

### Read XMP and IPTC

```csharp
var lines = new List<string>();
ImageMetadata image = ImageMetadata.Read(path);

if (image.Xmp is { } xmp)
    foreach (XmpEntry property in xmp.Entries)       // every property; nested ones have a path
        lines.Add($"{property.Prefix}:{property.Path} = {string.Join("; ", property.Values)}");

if (image.Iptc is { } iptc)
    foreach (IptcDataset dataset in iptc.Datasets)
    {
        IptcDatasetNames.TryGetName(dataset.Key, out string? name);
        byte[] raw = new byte[dataset.Value.ByteLength];
        dataset.Value.CopyRawTo(raw);
        string text = IptcDatasetNames.Interpret(dataset.Key, dataset.Value) ?? Encoding.UTF8.GetString(raw);
        lines.Add($"{dataset.Key.Record}:{dataset.Key.Dataset} {name} = {text}");
    }
```

### Edit EXIF in a JPEG

An edit names the tags to set and to remove; everything it does not name stays as it was. A save
writes a new file or an empty stream and never overwrites its source. `ExifEdit.Set` documents the
tags that can be written.

```csharp
var edit = new ExifEdit();
edit.Set(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x013B), Ascii("Jane Doe"));              // Artist
edit.Set(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x8298), Ascii("(c) 2026 Jane Doe"));     // Copyright
edit.Set(new ExifTagKey(ExifDirectoryKind.Exif, 0x9003), Ascii("2026:11:11 09:30:00"));   // DateTimeOriginal
edit.Remove(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0131));                              // Software

using (JpegMetadataDocument document = JpegMetadataDocument.Open(source))
    document.SaveAs(destination, edit);   // a new file; the image data is copied, never re-encoded

static ExifValue Ascii(string text) => ExifValue.FromRaw(
    TiffFieldType.Ascii, (uint)text.Length + 1, Endianness.LittleEndian, Encoding.ASCII.GetBytes(text + '\0'));
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

### Edit IPTC in a JPEG

```csharp
var edit = new IptcEdit();
edit.ReplaceText(new IptcDatasetKey(2, 120), ["Fishing boats leaving the harbour."]);   // caption
edit.ReplaceText(new IptcDatasetKey(2, 25), ["harbour", "dawn"]);                       // keywords
edit.Remove(new IptcDatasetKey(2, 116));                                                // copyright notice

using (JpegMetadataDocument document = JpegMetadataDocument.Open(source))
    document.SaveIptcAs(destination, edit);
```

### Edit a TIFF, a DNG or a raw file

```csharp
// TIFF, DNG and the TIFF-based raw formats (CR2, NEF, ARW, PEF, ORF, RW2).
var edit = new ExifEdit();
edit.Set(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0112),
    ExifValue.FromRaw(TiffFieldType.Short, 1, Endianness.LittleEndian, [6, 0]));   // Orientation: right, top

using (TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(source))
    document.SaveExifAs(destination, edit);   // no byte of the source moves; image data is not written to
```

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

What each format keeps and where its limits are: [docs/usage](docs/usage).

## Documentation

- [Current state and roadmap](docs/CURRENT_STATE.md)
- [Build and test](docs/BUILD_AND_TEST.md)
- [Development policy](docs/DEVELOPMENT.md)
- Usage and preservation limits: [docs/usage](docs/usage)
- Contributor and agent rules: [METADATASHARP_PROMPT.md](METADATASHARP_PROMPT.md)
- Design history: [docs/archive](docs/archive) (read-only)

## License

MIT, see [LICENSE](LICENSE).
