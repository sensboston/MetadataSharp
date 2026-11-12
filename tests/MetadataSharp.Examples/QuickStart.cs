// The code of the "Quick start" section of README.md. Every region below is one snippet of that
// section, line for line (QuickStartTests.ReadmeShowsTheCompiledSnippets), and QuickStartTests
// runs each of them. This assembly sees the public API only: no InternalsVisibleTo names it.
#region usings
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Formats.Png;
using MetadataSharp.Formats.Tiff;
#endregion

namespace MetadataSharp.Examples;

public static class QuickStart
{
    public static ImageMetadata ReadAnyFile(string path)
    {
        #region read-any-file
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
        #endregion
        return image;
    }

    public static TagsRead ReadTags(string path)
    {
        #region read-tags
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
        #endregion
        return new TagsRead(camera, taken, instant, exposure, aperture, iso, orientation, position, software, printed);
    }

    /// <summary>What the "read-tags" snippet read, for the test that runs it.</summary>
    public sealed record TagsRead(
        string? Camera, DateTime? Taken, DateTimeOffset? Instant, UnsignedRational? Exposure, double? Aperture, int? Iso,
        ExifOrientation? Orientation, GpsPosition? Position, string? Software, string? Printed);

    public static List<string> ReadXmpAndIptc(string path)
    {
        #region read-xmp-and-iptc
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
        #endregion
        return lines;
    }

    public static void EditExif(string source, string destination)
    {
        #region edit-exif
        var edit = new ExifEdit();
        edit.Set(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x013B), Ascii("Jane Doe"));              // Artist
        edit.Set(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x8298), Ascii("(c) 2026 Jane Doe"));     // Copyright
        edit.Set(new ExifTagKey(ExifDirectoryKind.Exif, 0x9003), Ascii("2026:11:11 09:30:00"));   // DateTimeOriginal
        edit.Remove(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0131));                              // Software

        using (JpegMetadataDocument document = JpegMetadataDocument.Open(source))
            document.SaveAs(destination, edit);   // a new file; the image data is copied, never re-encoded

        static ExifValue Ascii(string text) => ExifValue.FromRaw(
            TiffFieldType.Ascii, (uint)text.Length + 1, Endianness.LittleEndian, Encoding.ASCII.GetBytes(text + '\0'));
        #endregion
    }

    public static void EditXmp(string source, string destination)
    {
        #region edit-xmp
        const string DublinCore = "http://purl.org/dc/elements/1.1/";

        var edit = new XmpEdit();
        edit.Set(new XmpPropertyName(DublinCore, "title"),
            XmpValue.CreateLanguageAlternative([new("x-default", "Harbour at dawn")]));
        edit.Set(new XmpPropertyName(DublinCore, "subject"),
            XmpValue.CreateArray(XmpValueKind.Bag, ["harbour", "dawn"]));
        edit.Set(new XmpPropertyName("http://ns.adobe.com/xap/1.0/", "Rating"), XmpValue.CreateText("4"));

        using (PngMetadataDocument document = PngMetadataDocument.Open(source))
            document.SaveXmpAs(destination, edit);
        #endregion
    }

    public static void EditIptc(string source, string destination)
    {
        #region edit-iptc
        var edit = new IptcEdit();
        edit.ReplaceText(new IptcDatasetKey(2, 120), ["Fishing boats leaving the harbour."]);   // caption
        edit.ReplaceText(new IptcDatasetKey(2, 25), ["harbour", "dawn"]);                       // keywords
        edit.Remove(new IptcDatasetKey(2, 116));                                                // copyright notice

        using (JpegMetadataDocument document = JpegMetadataDocument.Open(source))
            document.SaveIptcAs(destination, edit);
        #endregion
    }

    public static void EditRawFile(string source, string destination)
    {
        #region edit-tiff-based
        // TIFF, DNG and the TIFF-based raw formats (CR2, NEF, ARW, PEF, ORF, RW2).
        var edit = new ExifEdit();
        edit.Set(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0112),
            ExifValue.FromRaw(TiffFieldType.Short, 1, Endianness.LittleEndian, [6, 0]));   // Orientation: right, top

        using (TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(source))
            document.SaveExifAs(destination, edit);   // no byte of the source moves; image data is not written to
        #endregion
    }

    public static async Task<ImageMetadata> ReadUntrustedAsync(Stream upload, CancellationToken cancellationToken)
    {
        #region read-untrusted
        // Every length, offset and count of the input is checked against a budget before it is used.
        var limits = new MetadataLimits
        {
            MaxInputBytes = 32 * 1024 * 1024,       // a longer file: MetadataException, LimitExceeded
            MaxTotalEntries = 1_000,                // a block over a budget: left out, listed in Errors
            MaxRetainedMetadataBytes = 512 * 1024,
        };
        ImageMetadata image = await ImageMetadata.ReadAsync(upload, limits, cancellationToken);
        bool overBudget = image.Errors.Any(error => error.Code == MetadataErrorCode.LimitExceeded);
        #endregion
        _ = overBudget;
        return image;
    }

    public static MetadataErrorCode? RefusedEdit(string source, string destination)
    {
        #region refused-edit
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
        #endregion
        return refusal;
    }
}
