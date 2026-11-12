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
                string value = ExifTags.Interpret(exif, entry.Key) ?? $"{entry.Value.FieldType} x {entry.Value.Count}";
                Console.WriteLine($"{entry.Key.Directory}.{name} = {value}");
            }

        // A block that is damaged, or outside what the library reads, does not throw: it is
        // reported here and the other blocks are still returned.
        foreach (MetadataReadError error in image.Errors)
            Console.WriteLine($"{error.Family}: {error.Code}");
        #endregion
        return image;
    }

    public static (string? Model, string? Exposure, string? Orientation) ReadTags(string path)
    {
        #region read-tags
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
        #endregion
        return (camera, exposure, rotation);
    }

    public static List<string> ReadXmpAndIptc(string path)
    {
        #region read-xmp-and-iptc
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
