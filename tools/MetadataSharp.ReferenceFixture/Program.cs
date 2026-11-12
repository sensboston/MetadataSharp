using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Tests.Jpeg;
using MetadataSharp.Tests.Png;
using MetadataSharp.Formats.Png;
using MetadataSharp.Formats.WebP;
using MetadataSharp.Formats.Tiff;
using MetadataSharp.Formats.Dng;
using MetadataSharp.Tests.Tiff;
using MetadataSharp.Tests.WebP;
using MetadataSharp.Tests.Dng;
using MetadataSharp.Formats.Bmff;
using MetadataSharp.Tests.Bmff;

if (args.Length == 2 && args[0] == "generate")
{
    Directory.CreateDirectory(args[1]);
    byte[] jfif = [74, 70, 73, 70, 0, 1, 2, 0, 0, 1, 0, 1, 0, 0];
    byte[] baseline = JpegFixture.Build([.. JpegFixture.Segment(0xE0, jfif), .. JpegFixture.Segment(0xFE, Encoding.ASCII.GetBytes("MetadataSharp authored fixture"))]);
    File.WriteAllBytes(Path.Combine(args[1], "base.jpg"), baseline);
    using var doc = JpegMetadataDocument.Open(new MemoryStream(baseline));
    var edit = new ExifEdit();
    edit.Set(new(ExifDirectoryKind.Ifd0, 0x0112), ExifValue.FromRaw(TiffFieldType.Short, 1, Endianness.LittleEndian, [1, 0]));
    edit.Set(new(ExifDirectoryKind.Ifd0, 0x013B), ExifValue.FromRaw(TiffFieldType.Ascii, 6, Endianness.LittleEndian, [65, 108, 105, 99, 101, 0]));
    string output = Path.Combine(args[1], "ours.jpg"); doc.SaveAs(output, edit);
    using var edited = JpegMetadataDocument.Open(output);
    edited.SaveAs(Path.Combine(args[1], "noop.jpg"), new ExifEdit());
    var remove = new ExifEdit(); remove.RemoveAllExif();
    edited.SaveAs(Path.Combine(args[1], "removed.jpg"), remove);
    var xmp = new XmpEdit();
    xmp.Set(new("http://ns.adobe.com/xap/1.0/", "CreatorTool"), XmpValue.CreateText("MetadataSharp"));
    string xmpPath = Path.Combine(args[1], "xmp.jpg"); edited.SaveXmpAs(xmpPath, xmp);
    using var withXmp = JpegMetadataDocument.Open(xmpPath);
    withXmp.SaveXmpAs(Path.Combine(args[1], "xmp-noop.jpg"), new());
    var removeXmp = new XmpEdit(); removeXmp.RemoveAllXmp();
    withXmp.SaveXmpAs(Path.Combine(args[1], "xmp-removed.jpg"), removeXmp);
    var rich = new XmpEdit();
    rich.Set(new("http://purl.org/dc/elements/1.1/", "subject"), XmpValue.CreateArray(XmpValueKind.Bag, ["alpha", "beta"]));
    rich.Set(new("http://purl.org/dc/elements/1.1/", "title"), XmpValue.CreateLanguageAlternative(new Dictionary<string, string> { ["x-default"] = "Hello", ["ru"] = "Привет" }));
    XmpPacket richPacket = withXmp.ReadXmp()!.Apply(rich)!;
    richPacket.SaveAs(Path.Combine(args[1], "xmp-rich.xmp"));
    foreach (int size in new[] { 65502, 65503, 65504 })
    {
        // Measure the actual serializer overhead, then exercise every disputed boundary.
        var empty = new XmpEdit(); empty.Set(new("http://ns.adobe.com/xap/1.0/", "CreatorTool"), XmpValue.CreateText(""));
        int overhead = XmpPacket.CreateEmpty().Apply(empty)!.ByteLength;
        var boundary = new XmpEdit(); boundary.Set(new("http://ns.adobe.com/xap/1.0/", "CreatorTool"), XmpValue.CreateText(new string('a', size - overhead)));
        edited.SaveXmpAs(Path.Combine(args[1], $"xmp-boundary-{size}.jpg"), boundary);
    }


    byte[] pngBase = PngFixture.Build();
    File.WriteAllBytes(Path.Combine(args[1], "base.png"), pngBase);
    using (var png = PngMetadataDocument.Open(new MemoryStream(pngBase))) png.SaveExifAs(Path.Combine(args[1], "exif.png"), edit);
    using (var png = PngMetadataDocument.Open(Path.Combine(args[1], "exif.png"))) png.SaveXmpAs(Path.Combine(args[1], "ours.png"), xmp);
    using (var png = PngMetadataDocument.Open(Path.Combine(args[1], "ours.png")))
    { png.SaveXmpAs(Path.Combine(args[1], "noop.png"), new()); png.SaveXmpAs(Path.Combine(args[1], "removed.png"), removeXmp); }

    for (int kind = 0; kind < 4; kind++)
    {
        byte[] webpBase = WebPFixture.Image(kind); File.WriteAllBytes(Path.Combine(args[1], $"base-{kind}.webp"), webpBase);
        using (var webp = WebPMetadataDocument.Open(new MemoryStream(webpBase))) webp.SaveExifAs(Path.Combine(args[1], $"exif-{kind}.webp"), edit);
        using (var webp = WebPMetadataDocument.Open(Path.Combine(args[1], $"exif-{kind}.webp"))) webp.SaveXmpAs(Path.Combine(args[1], $"ours-{kind}.webp"), xmp);
        using var withWebp = WebPMetadataDocument.Open(Path.Combine(args[1], $"ours-{kind}.webp"));
        withWebp.SaveXmpAs(Path.Combine(args[1], $"noop-{kind}.webp"), new());
        withWebp.SaveXmpAs(Path.Combine(args[1], $"removed-{kind}.webp"), removeXmp);
    }

    foreach (bool big in new[] { false, true }) foreach (bool thumbFixture in new[] { false, true })
    {
        var dng = thumbFixture ? DngFixture.ThumbnailAndRaw(big) : DngFixture.RawOnly(big);
        File.WriteAllBytes(Path.Combine(args[1], $"dng-{(thumbFixture ? "thumb" : "raw")}-{(big ? "mm" : "ii")}.dng"), dng.Bytes);
    }

    byte[] legacyWebp = WebPFixture.Extended(8, WebPFixture.Image(0)[12..], WebPFixture.Chunk("EXIF", [.. "Exif\0\0"u8, .. JpegFixture.OrientationTiff]));
    File.WriteAllBytes(Path.Combine(args[1], "legacy.webp"), legacyWebp);
    using (var legacyDoc = WebPMetadataDocument.Open(new MemoryStream(legacyWebp))) legacyDoc.SaveExifAs(Path.Combine(args[1], "legacy-rebuilt.webp"), edit);

    foreach (bool big in new[] { false, true }) foreach (bool tiles in new[] { false, true })
    {
        var tiff = StandaloneTiffFixture.Create(big, tiles);
        tiff.Directory(8, StandaloneTiffFixture.Fields(tiles).OrderBy(f => f.Tag).ToArray());
        File.WriteAllBytes(Path.Combine(args[1], $"owned-{big}-{tiles}.tif"), tiff.Bytes);
        using var tiffDoc = TiffMetadataDocument.Open(new MemoryStream(tiff.Bytes));
        var tiffEdit = new TiffMetadataEdit();
        tiffEdit.Set(new(tiffDoc.Layout.FirstIfdOffset, 305), ExifValue.FromRaw(TiffFieldType.Ascii, 14, Endianness.LittleEndian, "MetadataSharp\0"u8));
        tiffEdit.Set(new(tiffDoc.Layout.FirstIfdOffset, 315), ExifValue.FromRaw(TiffFieldType.Ascii, 6, Endianness.LittleEndian, "Alice\0"u8));
        string editedTiff = Path.Combine(args[1], $"edited-{big}-{tiles}.tif"); tiffDoc.SaveAs(editedTiff, tiffEdit);
        using var tiffResult = TiffMetadataDocument.Open(editedTiff);
        tiffResult.SaveAs(Path.Combine(args[1], $"edit-noop-{big}-{tiles}.tif"), new());
        var removeArtist = new TiffMetadataEdit(); removeArtist.Remove(new(tiffResult.Layout.FirstIfdOffset, 315));
        tiffResult.SaveAs(Path.Combine(args[1], $"edit-removed-{big}-{tiles}.tif"), removeArtist);
    }

    // Authored resource fixture: keep a copyright flag, update IPTC and its existing digest.
    IptcPacket minimal = IptcPacket.CreateEmpty(); byte[] iim = new byte[minimal.ByteLength]; minimal.CopyRawTo(iim);
    byte[] flag = Resource(0x040a, [1]);
    byte[] photoshop = [.. "Photoshop 3.0\0"u8, .. flag, .. Resource(0x0404, iim), .. Resource(0x0425, MD5.HashData(iim))];
    byte[] xmpImage = File.ReadAllBytes(xmpPath);
    byte[] seed = [.. xmpImage[..2], .. JpegFixture.Segment(0xed, photoshop), .. xmpImage[2..]];
    using var withResources = JpegMetadataDocument.Open(new MemoryStream(seed));
    var iptcEdit = new IptcEdit(); iptcEdit.ReplaceText(new(2, 25), ["alpha", "beta", "alpha"]);
    iptcEdit.ReplaceText(new(2, 120), ["Привет"]);
    string iptcPath = Path.Combine(args[1], "iptc.jpg"); withResources.SaveIptcAs(iptcPath, iptcEdit);
    using var iptcDoc = JpegMetadataDocument.Open(iptcPath);
    iptcDoc.SaveIptcAs(Path.Combine(args[1], "iptc-noop.jpg"), new());
    var removeIptc = new IptcEdit(); removeIptc.RemoveAllIptc();
    iptcDoc.SaveIptcAs(Path.Combine(args[1], "iptc-removed.jpg"), removeIptc);
    File.WriteAllBytes(Path.Combine(args[1], "iptc-expected-removed.jpg"),
        [.. xmpImage[..2], .. JpegFixture.Segment(0xed, [.. "Photoshop 3.0\0"u8, .. flag]), .. xmpImage[2..]]);
    // 15 control bytes + 949 full 69-byte keyword datasets + one 10-byte dataset.
    var full = new IptcEdit(); full.ReplaceText(new(2, 25), Enumerable.Repeat(new string('a', 64), 949).Append("aaaaa"));
    withXmp.SaveIptcAs(Path.Combine(args[1], "iptc-boundary.jpg"), full);

}
else if (args.Length == 2 && args[0] == "inspect-png")
{
    using var doc = PngMetadataDocument.Open(args[1]); ExifMetadata? exif = doc.ReadExif();
    string? artist = null;
    if (exif is not null && exif.TryGetEntry(new(ExifDirectoryKind.Ifd0, 0x013B), out ExifEntry? a))
    { byte[] bytes = new byte[a.Value.ByteLength]; a.Value.CopyRawTo(bytes); artist = Encoding.ASCII.GetString(bytes).TrimEnd('\0'); }
    XmpPacket? packet = doc.ReadXmp();
    string? rawXmp = null;
    if (packet is { IsEditable: false }) { byte[] raw = new byte[packet.ByteLength]; packet.CopyRawTo(raw); rawXmp = Encoding.UTF8.GetString(raw); }
    string? creatorTool = packet is { IsEditable: true } ? packet.Properties[new("http://ns.adobe.com/xap/1.0/", "CreatorTool")].Text : null;
    Console.WriteLine(JsonSerializer.Serialize(new { artist, creatorTool, xmpEditable = packet?.IsEditable, rawXmp }));
}
else if (args.Length == 3 && args[0] == "noop-png")
{
    using var doc = PngMetadataDocument.Open(args[1]); doc.SaveXmpAs(args[2], new());
}
else if (args.Length == 2 && args[0] == "probe-png-opaque")
{
    using var doc = PngMetadataDocument.Open(args[1]); using var output = new MemoryStream();
    var edit = new XmpEdit(); edit.Set(new("http://ns.adobe.com/xap/1.0/", "CreatorTool"), XmpValue.CreateText("refuse"));
    try { doc.SaveXmp(output, edit); return 3; }
    catch (MetadataException error) when (error.Code == MetadataErrorCode.UnsupportedFeature && output.Length == 0) { }
    var remove = new XmpEdit(); remove.RemoveAllXmp(); doc.SaveXmp(output, remove);
    using var reread = PngMetadataDocument.Open(new MemoryStream(output.ToArray()));
    if (reread.ReadXmp() is not null || reread.ReadExif() is null) return 4;
    Console.WriteLine("opaque edit refused before output; whole removal retained EXIF");
}
else if (args.Length == 2 && args[0] == "inspect-webp")
{
    using var doc = WebPMetadataDocument.Open(args[1]); ExifMetadata? exif = doc.ReadExif();
    string? artist = null;
    if (exif is not null && exif.TryGetEntry(new(ExifDirectoryKind.Ifd0, 0x013B), out ExifEntry? a))
    { byte[] bytes = new byte[a.Value.ByteLength]; a.Value.CopyRawTo(bytes); artist = Encoding.ASCII.GetString(bytes).TrimEnd('\0'); }
    XmpPacket? packet = doc.ReadXmp();
    string? rawXmp = null;
    if (packet is { IsEditable: false }) { byte[] raw = new byte[packet.ByteLength]; packet.CopyRawTo(raw); rawXmp = Encoding.UTF8.GetString(raw); }
    string? creatorTool = packet is { IsEditable: true } ? packet.Properties[new("http://ns.adobe.com/xap/1.0/", "CreatorTool")].Text : null;
    Console.WriteLine(JsonSerializer.Serialize(new { artist, creatorTool, xmpEditable = packet?.IsEditable, rawXmp }));
}
else if (args.Length == 3 && args[0] == "noop-webp")
{
    using var doc = WebPMetadataDocument.Open(args[1]); doc.SaveXmpAs(args[2], new());
}
else if (args.Length == 2 && args[0] == "probe-webp-opaque")
{
    using var doc = WebPMetadataDocument.Open(args[1]); using var output = new MemoryStream();
    var edit = new XmpEdit(); edit.Set(new("http://ns.adobe.com/xap/1.0/", "CreatorTool"), XmpValue.CreateText("refuse"));
    try { doc.SaveXmp(output, edit); return 3; }
    catch (MetadataException error) when (error.Code == MetadataErrorCode.UnsupportedFeature && output.Length == 0) { }
    var remove = new XmpEdit(); remove.RemoveAllXmp(); doc.SaveXmp(output, remove);
    using var reread = WebPMetadataDocument.Open(new MemoryStream(output.ToArray()));
    if (reread.ReadXmp() is not null || reread.ReadExif() is null) return 4;
    Console.WriteLine("opaque edit refused before output; whole removal retained EXIF");
}
else if (args.Length == 2 && args[0] == "inspect-tiff-values")
{
    using var doc = TiffMetadataDocument.Open(args[1]);
    Console.WriteLine(JsonSerializer.Serialize(new { artist = RawText(doc.ReadValue(new(doc.Layout.FirstIfdOffset, 315))), software = RawText(doc.ReadValue(new(doc.Layout.FirstIfdOffset, 305))) }));
}
else if (args.Length == 3 && args[0] == "rewrite-tiff")
{
    using var doc = TiffMetadataDocument.Open(args[1]); var edit = new TiffMetadataEdit();
    edit.Set(new(doc.Layout.FirstIfdOffset, 305), ExifValue.FromRaw(TiffFieldType.Ascii, 10, Endianness.LittleEndian, "Refreshed\0"u8)); doc.SaveAs(args[2], edit);
}
else if (args.Length == 2 && args[0] == "inspect-tiff")
{
    var layout = TiffLayout.Read(args[1]); var image = layout.Directories.First(d => d.Image is not null).Image!;
    Console.WriteLine(JsonSerializer.Serialize(new { width = image.Width, height = image.Height, supported = layout.IsSupported,
        segments = image.Segments.Count, storage = image.Storage.ToString() }));
}
else if (args.Length == 2 && args[0] == "inspect-dng")
{
    var layout = DngLayout.Read(args[1]);
    var image = layout.Directories.First(d => d.Image is not null).Image!;
    Console.WriteLine(JsonSerializer.Serialize(new { width = image.Width, height = image.Height,
        bitsPerSample = image.BitsPerSample, samplesPerPixel = image.SamplesPerPixel, supported = layout.IsSupported,
        role = image.Role.ToString(), strips = image.Strips.Count }));
}
else if (args.Length == 2 && args[0] == "inspect")
{
    using var doc = JpegMetadataDocument.Open(args[1]);
    string? artist = null; int? orientation = null;
    if (doc.Exif is not null)
    {
        if (doc.Exif.TryGetEntry(new(ExifDirectoryKind.Ifd0, 0x013B), out ExifEntry? a))
        { byte[] bytes = new byte[a.Value.ByteLength]; a.Value.CopyRawTo(bytes); artist = Encoding.ASCII.GetString(bytes).TrimEnd('\0'); }
        if (doc.Exif.TryGetEntry(new(ExifDirectoryKind.Ifd0, 0x0112), out ExifEntry? o))
        { byte[] bytes = new byte[o.Value.ByteLength]; o.Value.CopyRawTo(bytes); orientation = doc.Exif.ByteOrder == Endianness.LittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes); }
    }
    Console.WriteLine(JsonSerializer.Serialize(new { artist, orientation }));
}
else if (args.Length == 2 && args[0] == "inspect-xmp")
{
    using var doc = JpegMetadataDocument.Open(args[1]);
    XmpPacket? packet = doc.ReadXmp();
    string? creatorTool = packet?.Properties[new("http://ns.adobe.com/xap/1.0/", "CreatorTool")].Text;
    Console.WriteLine(JsonSerializer.Serialize(new { creatorTool }));
}
else if (args.Length == 3 && args[0] == "noop-xmp")
{
    using var doc = JpegMetadataDocument.Open(args[1]); doc.SaveXmpAs(args[2], new());
}
else if (args.Length == 2 && args[0] == "inspect-iptc")
{
    using var doc = JpegMetadataDocument.Open(args[1]); IptcPacket? packet = doc.ReadIptc();
    string? caption = null; var keywords = new List<string>(); string? digest = null;
    if (packet is not null)
    {
        for (int i = 0; i < packet.Datasets.Count; i++)
        {
            if (packet.Datasets[i].Key == new IptcDatasetKey(2, 25)) keywords.Add(packet.GetText(i));
            if (packet.Datasets[i].Key == new IptcDatasetKey(2, 120)) caption = packet.GetText(i);
        }
        byte[] raw = new byte[packet.ByteLength]; packet.CopyRawTo(raw); digest = Convert.ToHexString(MD5.HashData(raw)).ToLowerInvariant();
    }
    Console.WriteLine(JsonSerializer.Serialize(new { caption, keywords, digest }));
}
else if (args.Length == 3 && args[0] == "noop-iptc")
{
    using var doc = JpegMetadataDocument.Open(args[1]); doc.SaveIptcAs(args[2], new());
}
else if (args.Length == 3 && args[0] == "rewrite-iptc")
{
    using var doc = JpegMetadataDocument.Open(args[1]); var edit = new IptcEdit(); edit.ReplaceText(new(2, 120), ["Refreshed"]); doc.SaveIptcAs(args[2], edit);
}
else if (args.Length == 3 && args[0] == "noop")
{
    using var doc = JpegMetadataDocument.Open(args[1]); doc.SaveAs(args[2], new ExifEdit());
}
else if (args.Length == 2 && args[0] == "tiff-based-edits")
{
    // Files for the pinned tool to read after the TIFF-based writer edited them: every authored
    // layout in both byte orders, one file per kind of edit and one with all of them. Each row of
    // the manifest names an edited file, the file it was made from, the keys the edit may change
    // and the text the tool has to print for the edited file.
    Directory.CreateDirectory(args[1]);
    var manifest = new List<string>();
    const string date = "2021:06:07 08:09:10", longText = "A considerably longer text than the one before";
    foreach (string name in TiffBasedFixture.Names)
    {
        foreach (bool big in new[] { false, true })
        {
            TiffBasedFixture fixture = TiffBasedFixture.Create(name, big);
            string stem = name + (big ? "-be" : "-le"), extension = name == "tiff" ? ".tif" : "." + name;
            string source = stem + extension;
            File.WriteAllBytes(Path.Combine(args[1], source), fixture.Bytes);
            string textKey = fixture.TextTag == 0x010F ? "Exif.Image.Make" : "Exif.Image.Model";

            var inPlace = new ExifEdit();
            inPlace.Set(new(ExifDirectoryKind.Exif, 0x9003), AsciiValue(date));
            var grow = new ExifEdit();
            grow.Set(new(ExifDirectoryKind.Ifd0, (ushort)fixture.TextTag), AsciiValue(longText));
            grow.Set(new(ExifDirectoryKind.Exif, 0x9004), AsciiValue(date));
            var gps = new ExifEdit();
            gps.Set(new(ExifDirectoryKind.Gps, 0), ExifValue.FromRaw(TiffFieldType.Byte, 4, Endianness.LittleEndian, [2, 3, 0, 0]));
            gps.Set(new(ExifDirectoryKind.Gps, 1), AsciiValue("N"));
            gps.Set(new(ExifDirectoryKind.Gps, 2), ExifValue.FromRaw(TiffFieldType.Rational, 3, Endianness.LittleEndian,
                [42, 0, 0, 0, 1, 0, 0, 0, 22, 0, 0, 0, 1, 0, 0, 0, 30, 0, 0, 0, 1, 0, 0, 0]));
            gps.Set(new(ExifDirectoryKind.Gps, 3), AsciiValue("W"));
            gps.Set(new(ExifDirectoryKind.Gps, 4), ExifValue.FromRaw(TiffFieldType.Rational, 3, Endianness.LittleEndian,
                [71, 0, 0, 0, 1, 0, 0, 0, 14, 0, 0, 0, 1, 0, 0, 0, 15, 0, 0, 0, 1, 0, 0, 0]));
            var tool = new XmpEdit();
            tool.Set(new("http://ns.adobe.com/xap/1.0/", "CreatorTool"), XmpValue.CreateText("MetadataSharp"));
            var caption = new IptcEdit();
            caption.ReplaceText(new(2, 120), ["A caption"]);

            string Edited(string from, string kind, Action<TiffBasedMetadataDocument, string> save)
            {
                string target = stem + "-" + kind + extension;
                using TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(Path.Combine(args[1], from));
                save(document, Path.Combine(args[1], target));
                return target;
            }
            void Row(string target, string changed, string expected) => manifest.Add(string.Join('\t', source, target, changed, expected));

            const string gpsText = "Exif.GPSInfo.GPSLatitudeRef|Exif.GPSInfo.GPSLatitude |Exif.GPSInfo.GPSLongitudeRef|Exif.GPSInfo.GPSLongitude ";
            Row(Edited(source, "inplace", (document, path) => document.SaveExifAs(path, inPlace)), "Exif.Photo.DateTimeOriginal", date);
            Row(Edited(source, "grow", (document, path) => document.SaveExifAs(path, grow)), textKey + ",Exif.Image.ExifTag",
                longText + "|Exif.Photo.DateTimeDigitized");
            Row(Edited(source, "gps", (document, path) => document.SaveExifAs(path, gps)), "Exif.Image.GPSTag", gpsText);
            Row(Edited(source, "xmp", (document, path) => document.SaveXmpAs(path, tool)), "", "Xmp.xmp.CreatorTool|MetadataSharp");
            Row(Edited(source, "iptc", (document, path) => document.SaveIptcAs(path, caption)), "", "Iptc.Application2.Caption|A caption");

            // One edit after another, each on the output of the one before.
            string step = Edited(source, "all-1", (document, path) => document.SaveExifAs(path, inPlace));
            step = Edited(step, "all-2", (document, path) => document.SaveExifAs(path, grow));
            step = Edited(step, "all-3", (document, path) => document.SaveExifAs(path, gps));
            step = Edited(step, "all-4", (document, path) => document.SaveXmpAs(path, tool));
            step = Edited(step, "all", (document, path) => document.SaveIptcAs(path, caption));
            Row(step, "Exif.Photo.DateTimeOriginal," + textKey + ",Exif.Image.ExifTag,Exif.Image.GPSTag",
                date + "|" + longText + "|Exif.Photo.DateTimeDigitized|" + gpsText + "|Xmp.xmp.CreatorTool|Iptc.Application2.Caption");
        }
    }
    File.WriteAllLines(Path.Combine(args[1], "manifest.tsv"), manifest);
}
else if (args.Length == 3 && args[0] == "tiff-based-corpus")
{
    // The same check on files this repository did not author: every TIFF-based file under a
    // directory is edited four ways, each from the file as it is. A refusal is counted by its code;
    // a file that was written goes into the manifest for the pinned tool to read. Anything that is
    // not a refusal - an exception of another type - is a defect of the writer and fails the run.
    // The options are the default ones. A file they refuse for a budget is reported with what is
    // large in it, read with the wide budgets below, which serve no other purpose.
    var wide = new TiffReadOptions
    {
        Limits = new MetadataLimits { MaxDirectories = 256, MaxEntriesPerDirectory = 2_048, MaxTotalEntries = 16_384, MaxValueBytes = 268_435_456, MaxRetainedMetadataBytes = 1_073_741_824 }
    };
    var refusals = new List<(string File, string Kind, string Reason)>();
    var budgets = new List<string>();
    Directory.CreateDirectory(args[2]);
    var manifest = new List<string>();
    var outcomes = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var crashes = new List<string>();
    string[] extensions = [".tif", ".tiff", ".dng", ".cr2", ".nef", ".nrw", ".arw", ".sr2", ".pef", ".orf", ".rw2", ".srw", ".raw"];
    const string date = "2021:06:07 08:09:10", artist = "An artist whose name is longer than any slot";
    const string gpsKeys = "Exif.GPSInfo.GPSVersionID,Exif.GPSInfo.GPSLatitudeRef,Exif.GPSInfo.GPSLatitude,Exif.GPSInfo.GPSLongitudeRef,Exif.GPSInfo.GPSLongitude";
    int files = 0, number = 0;
    void Count(string outcome) => outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
    foreach (string path in Directory.EnumerateFiles(args[1], "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (!extensions.Contains(extension) || new FileInfo(path).Length > 64 * 1024 * 1024) continue;
        TiffBasedMetadataDocument document;
        try { document = TiffBasedMetadataDocument.Open(path); }
        catch (MetadataException) { continue; } // not a TIFF structure, whatever its name says
        files++;
        using (document)
        {
            void Try(string kind, string changed, string expected, Action<string> save)
            {
                string target = Path.Combine(args[2], $"c{++number:D5}-{kind}{extension}");
                try
                {
                    save(target);
                    manifest.Add(string.Join('\t', Path.GetFullPath(path), Path.GetFileName(target), changed, expected));
                    Count(kind + " written");
                }
                catch (MetadataException refusal)
                {
                    Count(kind + " refused " + refusal.Code);
                    refusals.Add((Path.GetFileName(path), kind, $"{refusal.Code}: {refusal.Message}"));
                }
                catch (Exception defect) when (defect is not IOException and not UnauthorizedAccessException)
                {
                    crashes.Add($"{Path.GetFileName(path)} {kind}: {defect.GetType().Name}: {defect.Message}");
                }
            }

            var inPlace = new ExifEdit();
            inPlace.Set(new(ExifDirectoryKind.Exif, 0x9003), AsciiValue(date));
            var grow = new ExifEdit();
            grow.Set(new(ExifDirectoryKind.Ifd0, 0x013B), AsciiValue(artist));
            grow.Set(new(ExifDirectoryKind.Exif, 0x9004), AsciiValue(date));
            grow.Set(new(ExifDirectoryKind.Gps, 0), ExifValue.FromRaw(TiffFieldType.Byte, 4, Endianness.LittleEndian, [2, 3, 0, 0]));
            grow.Set(new(ExifDirectoryKind.Gps, 1), AsciiValue("N"));
            grow.Set(new(ExifDirectoryKind.Gps, 2), ExifValue.FromRaw(TiffFieldType.Rational, 3, Endianness.LittleEndian,
                [42, 0, 0, 0, 1, 0, 0, 0, 22, 0, 0, 0, 1, 0, 0, 0, 30, 0, 0, 0, 1, 0, 0, 0]));
            grow.Set(new(ExifDirectoryKind.Gps, 3), AsciiValue("W"));
            grow.Set(new(ExifDirectoryKind.Gps, 4), ExifValue.FromRaw(TiffFieldType.Rational, 3, Endianness.LittleEndian,
                [71, 0, 0, 0, 1, 0, 0, 0, 14, 0, 0, 0, 1, 0, 0, 0, 15, 0, 0, 0, 1, 0, 0, 0]));
            var tool = new XmpEdit();
            tool.Set(new("http://ns.adobe.com/xap/1.0/", "CreatorTool"), XmpValue.CreateText("MetadataSharp"));
            var caption = new IptcEdit();
            caption.ReplaceText(new(2, 120), ["A caption"]);

            // Which default budget a file does not fit: the same edit in place with the default
            // options, then with one budget widened at a time; the wide read says what is large.
            string? LimitRefusal(TiffReadOptions? options)
            {
                try
                {
                    using var probe = TiffBasedMetadataDocument.Open(path, options);
                    probe.SaveExif(new MemoryStream(), inPlace);
                    return null;
                }
                catch (MetadataException refusal) when (refusal.Code == MetadataErrorCode.LimitExceeded)
                {
                    return $"{refusal.Message} (offset {refusal.SourceOffset})";
                }
                catch (MetadataException) { return null; }
            }
            if (LimitRefusal(null) is { } byDefault)
            {
                MetadataLimits usual = new TiffReadOptions().Limits;
                (string Name, MetadataLimits Limits)[] single =
                [
                    ("MaxDirectories", usual with { MaxDirectories = wide.Limits.MaxDirectories }),
                    ("MaxEntriesPerDirectory", usual with { MaxEntriesPerDirectory = wide.Limits.MaxEntriesPerDirectory }),
                    ("MaxTotalEntries", usual with { MaxTotalEntries = wide.Limits.MaxTotalEntries }),
                    ("MaxValueBytes", usual with { MaxValueBytes = wide.Limits.MaxValueBytes }),
                    ("MaxRetainedMetadataBytes", usual with { MaxRetainedMetadataBytes = wide.Limits.MaxRetainedMetadataBytes }),
                ];
                string enough = string.Join(", ", single.Where(one => LimitRefusal(new TiffReadOptions { Limits = one.Limits }) is null).Select(one => one.Name));
                string large;
                try
                {
                    var entries = TiffExif.Read(File.ReadAllBytes(path), wide.Limits).Entries;
                    large = $"{entries.Count} entries, {entries.Sum(entry => (long)entry.Value.ByteLength)} value bytes; over {usual.MaxValueBytes}: "
                        + string.Join(", ", entries.Where(entry => entry.Value.ByteLength > usual.MaxValueBytes)
                            .Select(entry => $"{entry.Key.Directory} 0x{entry.Key.TagId:X4} {entry.Value.FieldType} {entry.Value.ByteLength}"));
                }
                catch (MetadataException unread) { large = "not read: " + unread.Message; }
                budgets.Add($"{Path.GetFileName(path)} ({new FileInfo(path).Length} bytes): {byDefault} Widening one budget is enough: {(enough.Length == 0 ? "none" : enough)}. {large}");
            }

            Try("inplace", "Exif.Photo.DateTimeOriginal", date, target => document.SaveExifAs(target, inPlace));
            Try("grow", "Exif.Image.Artist,Exif.Image.ExifTag,Exif.Image.GPSTag,Exif.Photo.DateTimeDigitized," + gpsKeys, artist,
                target => document.SaveExifAs(target, grow));
            Try("xmp", "Xmp.*,Exif.Image.XMLPacket", "Xmp.xmp.CreatorTool", target => document.SaveXmpAs(target, tool));
            Try("iptc", "Iptc.*,Exif.Image.IPTCNAA", "A caption", target => document.SaveIptcAs(target, caption));
        }
    }
    File.WriteAllLines(Path.Combine(args[2], "manifest.tsv"), manifest);
    Console.WriteLine($"::notice title=TIFF-based corpus edits::{files} TIFF-based files; " + string.Join("; ", outcomes.Select(pair => pair.Key + " " + pair.Value)));
    // Why each file was refused, one line for a file and a reason: the counts do not say which file it was.
    string[] reasons = refusals.GroupBy(refusal => (refusal.File, refusal.Reason))
        .Select(group => $"{group.Key.File} [{string.Join(' ', group.Select(refusal => refusal.Kind))}]: {group.Key.Reason}"
            .Replace("%", "%25").Replace("\r", " ").Replace("\n", " "))
        .ToArray();
    foreach (string[] chunk in reasons.Chunk(20))
        Console.WriteLine("::notice title=TIFF-based corpus refusals::" + string.Join("%0A", chunk));
    Console.WriteLine("::notice title=TIFF-based corpus budgets::" + (budgets.Count == 0
        ? "no file is refused for a budget of the default options"
        : string.Join("%0A", budgets.Select(line => line.Replace("%", "%25")))));
    foreach (string crash in crashes.Take(10)) Console.WriteLine("::error title=TIFF-based writer defect::" + crash);
    if (crashes.Count != 0) return 1;
}
else if (args.Length == 2 && args[0] == "bmff-edits")
{
    // Files for the pinned tool to read after the BMFF writer edited them: every authored HEIC and
    // AVIF layout whose item location box the tool reads (32-bit offsets and lengths, one extent
    // for every item, items addressed from the start of the file), one file per kind of edit and
    // one with all of them. A file without metadata has no print-out to compare with: its rows
    // name "-" as their source, and the tool has to read the edited file and print what was added.
    Directory.CreateDirectory(args[1]);
    var manifest = new List<string>();
    const string date = "2021:06:07 08:09:10", longText = "A considerably longer text than the one before";
    const string gpsText = "Exif.GPSInfo.GPSLatitudeRef|Exif.GPSInfo.GPSLatitude |Exif.GPSInfo.GPSLongitudeRef|Exif.GPSInfo.GPSLongitude ";
    var inPlace = new ExifEdit();
    inPlace.Set(new(ExifDirectoryKind.Exif, 0x9003), AsciiValue(date));
    var grow = new ExifEdit();
    grow.Set(new(ExifDirectoryKind.Ifd0, 0x010F), AsciiValue(longText));
    grow.Set(new(ExifDirectoryKind.Exif, 0x9004), AsciiValue(date));
    var gps = new ExifEdit();
    gps.Set(new(ExifDirectoryKind.Gps, 0), ExifValue.FromRaw(TiffFieldType.Byte, 4, Endianness.LittleEndian, [2, 3, 0, 0]));
    gps.Set(new(ExifDirectoryKind.Gps, 1), AsciiValue("N"));
    gps.Set(new(ExifDirectoryKind.Gps, 2), ExifValue.FromRaw(TiffFieldType.Rational, 3, Endianness.LittleEndian,
        [42, 0, 0, 0, 1, 0, 0, 0, 22, 0, 0, 0, 1, 0, 0, 0, 30, 0, 0, 0, 1, 0, 0, 0]));
    gps.Set(new(ExifDirectoryKind.Gps, 3), AsciiValue("W"));
    gps.Set(new(ExifDirectoryKind.Gps, 4), ExifValue.FromRaw(TiffFieldType.Rational, 3, Endianness.LittleEndian,
        [71, 0, 0, 0, 1, 0, 0, 0, 14, 0, 0, 0, 1, 0, 0, 0, 15, 0, 0, 0, 1, 0, 0, 0]));
    var tool = new XmpEdit();
    tool.Set(new("http://ns.adobe.com/xap/1.0/", "CreatorTool"), XmpValue.CreateText("MetadataSharp"));
    var created = new ExifEdit();
    created.Set(new(ExifDirectoryKind.Ifd0, 0x010E), AsciiValue("A caption"));
    created.Set(new(ExifDirectoryKind.Exif, 0x9003), AsciiValue(date));

    foreach (string name in BmffFixture.Names)
    {
        bool bare = BmffFixture.Bare.Contains(name);
        foreach (bool big in bare ? new[] { false } : new[] { false, true })
        {
            BmffFixture fixture = BmffFixture.Create(name, big);
            if (!fixture.Plan.ToolReads) continue;
            string stem = name + (bare ? "" : big ? "-be" : "-le"), extension = "." + fixture.Plan.Brand;
            string source = stem + extension;
            File.WriteAllBytes(Path.Combine(args[1], source), fixture.Bytes);

            string Edited(string from, string kind, Action<BmffMetadataDocument, string> save)
            {
                string target = stem + "-" + kind + extension;
                using BmffMetadataDocument document = BmffMetadataDocument.Open(Path.Combine(args[1], from));
                save(document, Path.Combine(args[1], target));
                return target;
            }
            void Row(string target, string changed, string expected) =>
                manifest.Add(string.Join('\t', bare ? "-" : source, target, changed, expected));

            if (bare)
            {
                const string exifText = "Exif.Image.ImageDescription|A caption|Exif.Photo.DateTimeOriginal|" + date;
                Row(Edited(source, "exif", (document, path) => document.SaveExifAs(path, created)), "", exifText);
                string packet = Edited(source, "xmp", (document, path) => document.SaveXmpAs(path, tool));
                Row(packet, "", "Xmp.xmp.CreatorTool|MetadataSharp");
                Row(Edited(packet, "both", (document, path) => document.SaveExifAs(path, created)), "", exifText + "|Xmp.xmp.CreatorTool|MetadataSharp");
                continue;
            }
            Row(Edited(source, "inplace", (document, path) => document.SaveExifAs(path, inPlace)), "Exif.Photo.DateTimeOriginal", date);
            Row(Edited(source, "grow", (document, path) => document.SaveExifAs(path, grow)), "Exif.Image.Make,Exif.Image.ExifTag",
                longText + "|Exif.Photo.DateTimeDigitized");
            Row(Edited(source, "gps", (document, path) => document.SaveExifAs(path, gps)), "Exif.Image.GPSTag", gpsText);
            Row(Edited(source, "xmp", (document, path) => document.SaveXmpAs(path, tool)), "", "Xmp.xmp.CreatorTool|MetadataSharp|Xmp.xmp.Label");

            // One edit after another, each on the output of the one before.
            string step = Edited(source, "all-1", (document, path) => document.SaveExifAs(path, inPlace));
            step = Edited(step, "all-2", (document, path) => document.SaveExifAs(path, grow));
            step = Edited(step, "all-3", (document, path) => document.SaveExifAs(path, gps));
            step = Edited(step, "all", (document, path) => document.SaveXmpAs(path, tool));
            Row(step, "Exif.Photo.DateTimeOriginal,Exif.Image.Make,Exif.Image.ExifTag,Exif.Image.GPSTag",
                date + "|" + longText + "|Exif.Photo.DateTimeDigitized|" + gpsText + "|Xmp.xmp.CreatorTool|Xmp.xmp.Label");
        }
    }
    File.WriteAllLines(Path.Combine(args[1], "manifest.tsv"), manifest);
}
else if (args.Length == 3 && args[0] == "bmff-corpus")
{
    // The same check on files this repository did not author: every HEIC, HEIF and AVIF file under
    // a directory is edited three ways, each from the file as it is. A refusal is counted by its
    // code; a file that was written goes into the manifest for the pinned tool to read. Anything
    // that is not a refusal - an exception of another type - is a defect of the writer and fails
    // the run. The options are the default ones.
    var refusals = new List<(string File, string Kind, string Reason)>();
    Directory.CreateDirectory(args[2]);
    var manifest = new List<string>();
    var outcomes = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var crashes = new List<string>();
    string[] extensions = [".heic", ".heif", ".hif", ".avif"];
    const string date = "2021:06:07 08:09:10", artist = "An artist whose name is longer than any slot";
    const string gpsKeys = "Exif.GPSInfo.GPSVersionID,Exif.GPSInfo.GPSLatitudeRef,Exif.GPSInfo.GPSLatitude,Exif.GPSInfo.GPSLongitudeRef,Exif.GPSInfo.GPSLongitude";
    int files = 0, number = 0;
    void Count(string outcome) => outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
    foreach (string path in Directory.EnumerateFiles(args[1], "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (!extensions.Contains(extension) || new FileInfo(path).Length > 64 * 1024 * 1024) continue;
        BmffMetadataDocument document;
        try { document = BmffMetadataDocument.Open(path); }
        catch (MetadataException) { continue; } // not a BMFF file, whatever its name says
        files++;
        using (document)
        {
            void Try(string kind, string changed, string expected, Action<string> save)
            {
                string target = Path.Combine(args[2], $"b{++number:D5}-{kind}{extension}");
                try
                {
                    save(target);
                    manifest.Add(string.Join('\t', Path.GetFullPath(path), Path.GetFileName(target), changed, expected));
                    Count(kind + " written");
                }
                catch (MetadataException refusal)
                {
                    Count(kind + " refused " + refusal.Code);
                    refusals.Add((Path.GetFileName(path), kind, $"{refusal.Code}: {refusal.Message}"));
                }
                catch (Exception defect) when (defect is not IOException and not UnauthorizedAccessException)
                {
                    crashes.Add($"{Path.GetFileName(path)} {kind}: {defect.GetType().Name}: {defect.Message}");
                }
            }

            var inPlace = new ExifEdit();
            inPlace.Set(new(ExifDirectoryKind.Exif, 0x9003), AsciiValue(date));
            var grow = new ExifEdit();
            grow.Set(new(ExifDirectoryKind.Ifd0, 0x013B), AsciiValue(artist));
            grow.Set(new(ExifDirectoryKind.Exif, 0x9004), AsciiValue(date));
            grow.Set(new(ExifDirectoryKind.Gps, 0), ExifValue.FromRaw(TiffFieldType.Byte, 4, Endianness.LittleEndian, [2, 3, 0, 0]));
            grow.Set(new(ExifDirectoryKind.Gps, 1), AsciiValue("N"));
            grow.Set(new(ExifDirectoryKind.Gps, 2), ExifValue.FromRaw(TiffFieldType.Rational, 3, Endianness.LittleEndian,
                [42, 0, 0, 0, 1, 0, 0, 0, 22, 0, 0, 0, 1, 0, 0, 0, 30, 0, 0, 0, 1, 0, 0, 0]));
            grow.Set(new(ExifDirectoryKind.Gps, 3), AsciiValue("W"));
            grow.Set(new(ExifDirectoryKind.Gps, 4), ExifValue.FromRaw(TiffFieldType.Rational, 3, Endianness.LittleEndian,
                [71, 0, 0, 0, 1, 0, 0, 0, 14, 0, 0, 0, 1, 0, 0, 0, 15, 0, 0, 0, 1, 0, 0, 0]));
            var tool = new XmpEdit();
            tool.Set(new("http://ns.adobe.com/xap/1.0/", "CreatorTool"), XmpValue.CreateText("MetadataSharp"));

            Try("inplace", "Exif.Photo.DateTimeOriginal,Exif.Image.ExifTag", date, target => document.SaveExifAs(target, inPlace));
            Try("grow", "Exif.Image.Artist,Exif.Image.ExifTag,Exif.Image.GPSTag,Exif.Photo.DateTimeDigitized," + gpsKeys, artist,
                target => document.SaveExifAs(target, grow));
            Try("xmp", "Xmp.*", "Xmp.xmp.CreatorTool", target => document.SaveXmpAs(target, tool));
        }
    }
    File.WriteAllLines(Path.Combine(args[2], "manifest.tsv"), manifest);
    Console.WriteLine($"::notice title=BMFF corpus edits::{files} BMFF files; " + string.Join("; ", outcomes.Select(pair => pair.Key + " " + pair.Value)));
    // Why each file was refused, one line for a file and a reason: the counts do not say which file it was.
    string[] reasons = refusals.GroupBy(refusal => (refusal.File, refusal.Reason))
        .Select(group => $"{group.Key.File} [{string.Join(' ', group.Select(refusal => refusal.Kind))}]: {group.Key.Reason}"
            .Replace("%", "%25").Replace("\r", " ").Replace("\n", " "))
        .ToArray();
    foreach (string[] chunk in reasons.Chunk(20))
        Console.WriteLine("::notice title=BMFF corpus refusals::" + string.Join("%0A", chunk));
    foreach (string crash in crashes.Take(10)) Console.WriteLine("::error title=BMFF writer defect::" + crash);
    if (crashes.Count != 0) return 1;
}
else if (args.Length == 2 && args[0] == "jpeg-embedded-edits")
{
    // Files for the pinned tool to read after the JPEG writer gave them a profile or a comment,
    // replaced it or took it out. Each row of the manifest names the file an edit started from,
    // the edited file, the profile the tool has to print for it (a file of the directory, "-" for
    // none, "=" for what it prints for the source) and its comment (UTF-8 bytes in hexadecimal,
    // "-" or "=").
    Directory.CreateDirectory(args[1]);
    var manifest = new List<string>();
    string At(string name) => Path.Combine(args[1], name);
    string Stored(string name, byte[] profile) { File.WriteAllBytes(At(name), profile); return name; }
    string Edited(string from, string name, Action<JpegMetadataDocument, string> save)
    {
        using JpegMetadataDocument document = JpegMetadataDocument.Open(At(from));
        save(document, At(name));
        return name;
    }
    void Row(string source, string edited, string profile, string comment) => manifest.Add(string.Join('\t', source, edited, profile, comment));
    static string Hex(string value) => Convert.ToHexString(Encoding.UTF8.GetBytes(value));

    byte[] small = IccProfile(3_144, 1), two = IccProfile(70_000, 2), tail = IccProfile(65_520, 3), large = IccProfile(1_000_000, 4);
    string smallIcc = Stored("small.icc", small), twoIcc = Stored("two.icc", two), tailIcc = Stored("tail.icc", tail), largeIcc = Stored("large.icc", large);
    const string text = "A comment", wide = "Комментарий — ü", later = "Written after the profile";
    string longest = new string('c', 65_533);

    const string plain = "plain.jpg";
    File.WriteAllBytes(At(plain), JpegFixture.Build(JpegFixture.Segment(0xE0, [74, 70, 73, 70, 0, 1, 2, 0, 0, 1, 0, 1, 0, 0])));
    Row(plain, Edited(plain, "plain-icc.jpg", (document, path) => document.SaveIccProfileAs(path, small)), smallIcc, "-");
    Row(plain, Edited(plain, "plain-icc-two.jpg", (document, path) => document.SaveIccProfileAs(path, two)), twoIcc, "-");
    Row(plain, Edited(plain, "plain-icc-large.jpg", (document, path) => document.SaveIccProfileAs(path, large)), largeIcc, "-");
    // 65,520 bytes are one more than a chunk holds. The tool stops reading the segments of a file
    // at a chunk with fewer than four bytes, so the comment after these chunks is the evidence
    // that the last chunk is not such a one.
    string tailed = Edited(plain, "plain-icc-tail.jpg", (document, path) => document.SaveIccProfileAs(path, tail));
    Row(plain, tailed, tailIcc, "-");
    Row(plain, Edited(tailed, "plain-icc-tail-comment.jpg", (document, path) => document.SaveCommentAs(path, later)), tailIcc, Hex(later));
    Row(plain, Edited(plain, "plain-comment.jpg", (document, path) => document.SaveCommentAs(path, text)), "-", Hex(text));
    Row(plain, Edited(plain, "plain-comment-utf8.jpg", (document, path) => document.SaveCommentAs(path, wide)), "-", Hex(wide));
    Row(plain, Edited(plain, "plain-comment-longest.jpg", (document, path) => document.SaveCommentAs(path, longest)), "-", Hex(longest));

    // A file with EXIF, XMP and IPTC data, then a comment and a profile: what the tool prints of
    // the three families may not change with any edit of the other two.
    var exif = new ExifEdit();
    exif.Set(new(ExifDirectoryKind.Ifd0, 0x0112), ExifValue.FromRaw(TiffFieldType.Short, 1, Endianness.LittleEndian, [1, 0]));
    exif.Set(new(ExifDirectoryKind.Ifd0, 0x013B), AsciiValue("Alice"));
    var tool = new XmpEdit();
    tool.Set(new("http://ns.adobe.com/xap/1.0/", "CreatorTool"), XmpValue.CreateText("MetadataSharp"));
    var words = new IptcEdit();
    words.ReplaceText(new(2, 25), ["alpha", "beta"]);
    string step = Edited(plain, "rich-1.jpg", (document, path) => document.SaveAs(path, exif));
    step = Edited(step, "rich-2.jpg", (document, path) => document.SaveXmpAs(path, tool));
    string bare = Edited(step, "rich-3.jpg", (document, path) => document.SaveIptcAs(path, words));
    step = Edited(bare, "rich-4.jpg", (document, path) => document.SaveCommentAs(path, text));
    Row(bare, step, "-", Hex(text));
    string rich = Edited(step, "rich.jpg", (document, path) => document.SaveIccProfileAs(path, small));
    Row(bare, rich, smallIcc, Hex(text));
    Row(rich, Edited(rich, "rich-icc-replaced.jpg", (document, path) => document.SaveIccProfileAs(path, two)), twoIcc, "=");
    Row(rich, Edited(rich, "rich-icc-removed.jpg", (document, path) => document.SaveIccProfileAs(path, null)), "-", "=");
    Row(rich, Edited(rich, "rich-comment-replaced.jpg", (document, path) => document.SaveCommentAs(path, wide)), "=", Hex(wide));
    Row(rich, Edited(rich, "rich-comment-removed.jpg", (document, path) => document.SaveCommentAs(path, null)), "=", "-");
    string same = Edited(rich, "rich-same.jpg", (document, path) => document.SaveIccProfileAs(path, small));
    Row(rich, same, "=", "=");
    string none = Edited(Edited(rich, "rich-none-1.jpg", (document, path) => document.SaveIccProfileAs(path, null)),
        "rich-none.jpg", (document, path) => document.SaveCommentAs(path, null));
    Row(rich, none, "-", "-");
    File.WriteAllLines(At("manifest.tsv"), manifest);

    // Two things the tool cannot say: saving what is there changes no byte, and taking both out
    // gives back the file they were put into.
    if (!File.ReadAllBytes(At(same)).AsSpan().SequenceEqual(File.ReadAllBytes(At(rich))))
    { Console.WriteLine("::error title=JPEG profile and comment edits::saving the profile a file has changed the file"); return 1; }
    if (!File.ReadAllBytes(At(none)).AsSpan().SequenceEqual(File.ReadAllBytes(At(bare))))
    { Console.WriteLine("::error title=JPEG profile and comment edits::removing the profile and the comment did not give the source back"); return 1; }
}
else if (args.Length == 3 && args[0] == "jpeg-embedded-corpus")
{
    // The same check on JPEG files this repository did not author: each is given a comment, given
    // a profile, and has each taken out, every time from the file as it is. A refusal is counted
    // by its code; a file that was written and differs from its source goes into the manifest for
    // the pinned tool to read. An exception of another type is a defect of the writer and fails
    // the run. The options are the default ones.
    Directory.CreateDirectory(args[2]);
    var manifest = new List<string>();
    var outcomes = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var refusals = new List<(string File, string Kind, string Reason)>();
    var crashes = new List<string>();
    byte[] profile = IccProfile(70_000, 7);
    File.WriteAllBytes(Path.Combine(args[2], "given.icc"), profile);
    const string text = "A comment written by MetadataSharp";
    string hex = Convert.ToHexString(Encoding.UTF8.GetBytes(text));
    int files = 0, number = 0;
    void Count(string outcome) => outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
    foreach (string path in Directory.EnumerateFiles(args[1], "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if ((extension != ".jpg" && extension != ".jpeg") || new FileInfo(path).Length > 64 * 1024 * 1024) continue;
        byte[] bytes = File.ReadAllBytes(path);
        JpegMetadataDocument document;
        try { document = JpegMetadataDocument.Open(new MemoryStream(bytes)); }
        catch (MetadataException) { continue; } // not a JPEG structure, whatever its name says
        files++;
        using (document)
        {
            void Try(string kind, string expectedProfile, string expectedComment, Action<Stream> save)
            {
                try
                {
                    using var output = new MemoryStream();
                    save(output);
                    byte[] written = output.ToArray();
                    if (written.AsSpan().SequenceEqual(bytes)) { Count(kind + " nothing to change"); return; }
                    string target = $"j{++number:D5}-{kind}.jpg";
                    File.WriteAllBytes(Path.Combine(args[2], target), written);
                    manifest.Add(string.Join('\t', Path.GetFullPath(path), target, expectedProfile, expectedComment));
                    Count(kind + " written");
                }
                catch (MetadataException refusal)
                {
                    Count(kind + " refused " + refusal.Code);
                    refusals.Add((Path.GetFileName(path), kind, $"{refusal.Code}: {refusal.Message}"));
                }
                catch (Exception defect) when (defect is not IOException and not UnauthorizedAccessException)
                {
                    crashes.Add($"{Path.GetFileName(path)} {kind}: {defect.GetType().Name}: {defect.Message}");
                }
            }

            Try("comment", "=", hex, output => document.SaveComment(output, text));
            Try("icc", "given.icc", "=", output => document.SaveIccProfile(output, profile));
            Try("no-comment", "=", "-", output => document.SaveComment(output, null));
            Try("no-icc", "-", "=", output => document.SaveIccProfile(output, null));
        }
    }
    File.WriteAllLines(Path.Combine(args[2], "manifest.tsv"), manifest);
    Console.WriteLine($"::notice title=JPEG corpus profile and comment edits::{files} JPEG files; " + string.Join("; ", outcomes.Select(pair => pair.Key + " " + pair.Value)));
    string[] reasons = refusals.GroupBy(refusal => (refusal.File, refusal.Reason))
        .Select(group => $"{group.Key.File} [{string.Join(' ', group.Select(refusal => refusal.Kind))}]: {group.Key.Reason}"
            .Replace("%", "%25").Replace("\r", " ").Replace("\n", " "))
        .ToArray();
    foreach (string[] chunk in reasons.Chunk(20))
        Console.WriteLine("::notice title=JPEG corpus profile and comment refusals::" + string.Join("%0A", chunk));
    foreach (string crash in crashes.Take(10)) Console.WriteLine("::error title=JPEG writer defect::" + crash);
    if (crashes.Count != 0) return 1;
}
else if (args.Length == 2 && args[0] == "png-webp-embedded-edits")
{
    // Files for the pinned tool to read after the PNG writer gave them a profile or a comment and
    // the WebP writer a profile, replaced it or took it out. manifest.tsv is that of the JPEG
    // command: the source, the edited file, the profile the tool has to print for it, and "=" for
    // the comment, because the tool reads the comment of a PNG from another keyword and a WebP has
    // none. chunks.tsv names, for the PNG files, the chunk the tool has to list: the kind of the
    // edit and "iCCP", the type of the comment chunk with its text (UTF-8 in hexadecimal), or "-".
    Directory.CreateDirectory(args[1]);
    var manifest = new List<string>();
    var chunks = new List<string>();
    string At(string name) => Path.Combine(args[1], name);
    string Stored(string name, byte[] profile) { File.WriteAllBytes(At(name), profile); return name; }
    string Authored(string name, byte[] file) { File.WriteAllBytes(At(name), file); return name; }
    string EditPng(string from, string name, Action<PngMetadataDocument, string> save)
    {
        using PngMetadataDocument document = PngMetadataDocument.Open(At(from));
        save(document, At(name));
        return name;
    }
    string EditWebP(string from, string name, Action<WebPMetadataDocument, string> save)
    {
        using WebPMetadataDocument document = WebPMetadataDocument.Open(At(from));
        save(document, At(name));
        return name;
    }
    string PngIcc(string from, string name, byte[]? profile) => EditPng(from, name, (document, path) => document.SaveIccProfileAs(path, profile));
    string PngComment(string from, string name, string? comment) => EditPng(from, name, (document, path) => document.SaveCommentAs(path, comment));
    string WebPIcc(string from, string name, byte[]? profile) => EditWebP(from, name, (document, path) => document.SaveIccProfileAs(path, profile));
    // A profile row of a PNG goes to both lists; of a WebP, to the first.
    void Profile(string source, string edited, string profile, string? chunk)
    {
        manifest.Add(string.Join('\t', source, edited, profile, "="));
        if (chunk is not null) chunks.Add(string.Join('\t', source, edited, "profile", chunk));
    }
    // A comment edit may change nothing of what the tool prints as profile, comment and keys.
    void Comment(string source, string edited, string chunk)
    {
        manifest.Add(string.Join('\t', source, edited, "=", "="));
        chunks.Add(string.Join('\t', source, edited, "comment", chunk));
    }
    static string Hex(string value) => Convert.ToHexString(Encoding.UTF8.GetBytes(value));
    bool Same(string one, string other) => File.ReadAllBytes(At(one)).AsSpan().SequenceEqual(File.ReadAllBytes(At(other)));

    byte[] small = IccProfile(3_144, 1), odd = IccProfile(70_001, 2), large = IccProfile(1_000_000, 4), noise = IccProfile(200_000, 5);
    // A profile that does not compress: the header fields, and bytes without a pattern after them.
    new Random(5).NextBytes(noise.AsSpan(128));
    string smallIcc = Stored("small.icc", small), oddIcc = Stored("odd.icc", odd), largeIcc = Stored("large.icc", large), noiseIcc = Stored("noise.icc", noise);
    // ISO-8859-1; not ISO-8859-1; and ISO-8859-1 whose bytes are well-formed UTF-8 for another text.
    const string text = "A comment", narrow = "Caf\u00E9 in M\u00FCnchen", wide = "Комментарий — ü", twoWays = "\u00C3\u00A9";
    byte[] gamma = PngFixture.Chunk("gAMA", [0, 1, 0x86, 0xA0]), srgb = PngFixture.Chunk("sRGB", [0]), palette = PngFixture.Chunk("PLTE", [1, 2, 3, 4, 5, 6]);
    byte[] time = PngFixture.Chunk("tIME", [7, 0xEA, 11, 11, 10, 9, 8]);

    string plain = Authored("plain.png", PngFixture.Build([.. gamma, .. palette], time));
    string plainIcc = PngIcc(plain, "plain-icc.png", small);
    Profile(plain, plainIcc, smallIcc, "iCCP");
    Profile(plain, PngIcc(plain, "plain-icc-odd.png", odd), oddIcc, "iCCP");
    Profile(plain, PngIcc(plain, "plain-icc-large.png", large), largeIcc, "iCCP");
    Profile(plain, PngIcc(plain, "plain-icc-noise.png", noise), noiseIcc, "iCCP");
    string plainComment = PngComment(plain, "plain-comment.png", text);
    Comment(plain, plainComment, "tEXt:" + Hex(text));
    Comment(plain, PngComment(plain, "plain-comment-latin1.png", narrow), "tEXt:" + Hex(narrow));
    Comment(plain, PngComment(plain, "plain-comment-utf8.png", wide), "iTXt:" + Hex(wide));
    Comment(plain, PngComment(plain, "plain-comment-two-ways.png", twoWays), "iTXt:" + Hex(twoWays));

    // The chunk of another writer, between other chunks: its name and its compression go, its place stays.
    string foreign = Authored("foreign.png", PngFixture.Build([.. gamma,
        .. PngFixture.Chunk("iCCP", [.. "Adobe RGB (1998)\0"u8, 0, .. PngFixture.Compress(IccProfile(560, 6), System.IO.Compression.CompressionLevel.Fastest)]),
        .. palette], time));
    Profile(foreign, PngIcc(foreign, "foreign-icc-replaced.png", small), smallIcc, "iCCP");
    Profile(foreign, PngIcc(foreign, "foreign-icc-removed.png", null), "-", "-");
    Comment(foreign, PngComment(foreign, "foreign-comment.png", text), "tEXt:" + Hex(text));

    // A file with EXIF and XMP data, a description (which the tool prints as the comment), an sRGB
    // chunk and two comments, one compressed before the image data and one after it.
    var exif = new ExifEdit();
    exif.Set(new(ExifDirectoryKind.Ifd0, 0x0112), ExifValue.FromRaw(TiffFieldType.Short, 1, Endianness.LittleEndian, [1, 0]));
    exif.Set(new(ExifDirectoryKind.Ifd0, 0x013B), AsciiValue("Alice"));
    var tool = new XmpEdit();
    tool.Set(new("http://ns.adobe.com/xap/1.0/", "CreatorTool"), XmpValue.CreateText("MetadataSharp"));
    string step = Authored("rich-0.png", PngFixture.Build(
        [.. srgb, .. gamma, .. PngFixture.Chunk("tEXt", [.. "Description\0"u8, .. "A description"u8]),
            .. PngFixture.Chunk("zTXt", [.. "Comment\0"u8, 0, .. PngFixture.Compress("an old comment"u8.ToArray())])],
        [.. PngFixture.Chunk("iTXt", [.. "Comment\0"u8, 0, 0, 0, 0, .. "a second one"u8]), .. time]));
    step = EditPng(step, "rich-1.png", (document, path) => document.SaveExifAs(path, exif));
    string rich = EditPng(step, "rich.png", (document, path) => document.SaveXmpAs(path, tool));
    string richIcc = PngIcc(rich, "rich-icc.png", small);
    Profile(rich, richIcc, smallIcc, "iCCP");
    Profile(richIcc, PngIcc(richIcc, "rich-icc-replaced.png", odd), oddIcc, "iCCP");
    Profile(richIcc, PngIcc(richIcc, "rich-icc-removed.png", null), "-", "-");
    string richSame = PngIcc(richIcc, "rich-icc-same.png", small);
    Profile(richIcc, richSame, "=", "iCCP");
    Comment(rich, PngComment(rich, "rich-comment.png", text), "tEXt:" + Hex(text));
    Comment(rich, PngComment(rich, "rich-comment-utf8.png", wide), "iTXt:" + Hex(wide));
    Comment(rich, PngComment(rich, "rich-comment-removed.png", null), "-");
    Comment(richIcc, PngComment(richIcc, "rich-icc-comment.png", narrow), "tEXt:" + Hex(narrow));
    string plainBack = PngComment(PngIcc(PngComment(plainIcc, "plain-both.png", text), "plain-back-1.png", null), "plain-back.png", null);

    // WebP: each layout of the fixtures, then a file with EXIF and XMP data.
    var layouts = new (string Name, int Kind)[] { ("lossy", 0), ("lossless", 1), ("extended", 2), ("lossless-alpha", 3) };
    foreach ((string name, int kind) in layouts)
    {
        string source = Authored(name + ".webp", WebPFixture.Image(kind));
        string given = WebPIcc(source, name + "-icc.webp", small);
        Profile(source, given, smallIcc, null);
        Profile(given, WebPIcc(given, name + "-icc-replaced.webp", odd), oddIcc, null);
        Profile(given, WebPIcc(given, name + "-icc-removed.webp", null), "-", null);
    }
    Profile("lossy.webp", WebPIcc("lossy.webp", "lossy-icc-large.webp", large), largeIcc, null);
    string webp = EditWebP("extended.webp", "rich-1.webp", (document, path) => document.SaveExifAs(path, exif));
    webp = EditWebP(webp, "rich.webp", (document, path) => document.SaveXmpAs(path, tool));
    string webpIcc = WebPIcc(webp, "rich-icc.webp", small);
    Profile(webp, webpIcc, smallIcc, null);
    Profile(webpIcc, WebPIcc(webpIcc, "rich-icc-replaced.webp", odd), oddIcc, null);
    string webpBack = WebPIcc(webpIcc, "rich-icc-removed.webp", null);
    Profile(webpIcc, webpBack, "-", null);
    string webpSame = WebPIcc(webpIcc, "rich-icc-same.webp", small);
    Profile(webpIcc, webpSame, "=", null);
    File.WriteAllLines(At("manifest.tsv"), manifest);
    File.WriteAllLines(At("chunks.tsv"), chunks);

    // What the tool cannot say: saving the profile a file has changes no byte, and taking out what
    // was put in gives back the file, once a WebP has its extended header.
    const string title = "::error title=PNG and WebP profile and comment edits::";
    if (!Same(richSame, richIcc) || !Same(webpSame, webpIcc))
    { Console.WriteLine(title + "saving the profile a file has changed the file"); return 1; }
    if (!Same(plainBack, plain) || !Same(webpBack, webp) || !Same("extended-icc-removed.webp", "extended.webp"))
    { Console.WriteLine(title + "removing the profile and the comment did not give the source back"); return 1; }
}
else if (args.Length == 3 && args[0] == "png-webp-embedded-corpus")
{
    // The same check on PNG and WebP files this repository did not author: each PNG is given a
    // comment, given a profile, and has each taken out; each WebP is given a profile and has it
    // taken out; every time from the file as it is. A refusal is counted by its code; a file that
    // was written and differs from its source goes into the lists for the pinned tool. An
    // exception of another type is a defect of the writer and fails the run. Default options.
    Directory.CreateDirectory(args[2]);
    var manifest = new List<string>();
    var chunks = new List<string>();
    var outcomes = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var refusals = new List<(string File, string Kind, string Reason)>();
    var crashes = new List<string>();
    byte[] profile = IccProfile(70_001, 7);
    File.WriteAllBytes(Path.Combine(args[2], "given.icc"), profile);
    const string text = "A comment written by MetadataSharp";
    string expectedComment = "tEXt:" + Convert.ToHexString(Encoding.UTF8.GetBytes(text));
    int pngs = 0, webps = 0, number = 0;
    void Count(string outcome) => outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
    foreach (string path in Directory.EnumerateFiles(args[1], "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if ((extension != ".png" && extension != ".webp") || new FileInfo(path).Length > 64 * 1024 * 1024) continue;
        byte[] bytes = File.ReadAllBytes(path);
        void Try(string kind, string expectedProfile, string? chunkKind, string? chunk, Action<Stream> save)
        {
            try
            {
                using var output = new MemoryStream();
                save(output);
                byte[] written = output.ToArray();
                if (written.AsSpan().SequenceEqual(bytes)) { Count(kind + " nothing to change"); return; }
                string target = $"e{++number:D5}-{kind}{extension}";
                File.WriteAllBytes(Path.Combine(args[2], target), written);
                manifest.Add(string.Join('\t', Path.GetFullPath(path), target, expectedProfile, "="));
                if (chunkKind is not null) chunks.Add(string.Join('\t', Path.GetFullPath(path), target, chunkKind, chunk));
                Count(kind + " written");
            }
            catch (MetadataException refusal)
            {
                Count(kind + " refused " + refusal.Code);
                refusals.Add((Path.GetFileName(path), kind, $"{refusal.Code}: {refusal.Message}"));
            }
            catch (Exception defect) when (defect is not IOException and not UnauthorizedAccessException)
            {
                crashes.Add($"{Path.GetFileName(path)} {kind}: {defect.GetType().Name}: {defect.Message}");
            }
        }

        if (extension == ".png")
        {
            PngMetadataDocument document;
            try { document = PngMetadataDocument.Open(new MemoryStream(bytes)); }
            catch (MetadataException refusal) { Count("png not opened " + refusal.Code); continue; } // damaged, animated, or no PNG at all
            pngs++;
            using (document)
            {
                Try("png-comment", "=", "comment", expectedComment, output => document.SaveComment(output, text));
                Try("png-icc", "given.icc", "profile", "iCCP", output => document.SaveIccProfile(output, profile));
                Try("png-no-comment", "=", "comment", "-", output => document.SaveComment(output, null));
                Try("png-no-icc", "-", "profile", "-", output => document.SaveIccProfile(output, null));
            }
        }
        else
        {
            WebPMetadataDocument document;
            try { document = WebPMetadataDocument.Open(new MemoryStream(bytes)); }
            catch (MetadataException refusal) { Count("webp not opened " + refusal.Code); continue; } // damaged, animated, or no WebP at all
            webps++;
            using (document)
            {
                Try("webp-icc", "given.icc", null, null, output => document.SaveIccProfile(output, profile));
                Try("webp-no-icc", "-", null, null, output => document.SaveIccProfile(output, null));
            }
        }
    }
    File.WriteAllLines(Path.Combine(args[2], "manifest.tsv"), manifest);
    File.WriteAllLines(Path.Combine(args[2], "chunks.tsv"), chunks);
    Console.WriteLine($"::notice title=PNG and WebP corpus profile and comment edits::{pngs} PNG files and {webps} WebP files opened; "
        + string.Join("; ", outcomes.Select(pair => pair.Key + " " + pair.Value)));
    string[] reasons = refusals.GroupBy(refusal => (refusal.File, refusal.Reason))
        .Select(group => $"{group.Key.File} [{string.Join(' ', group.Select(refusal => refusal.Kind))}]: {group.Key.Reason}"
            .Replace("%", "%25").Replace("\r", " ").Replace("\n", " "))
        .ToArray();
    foreach (string[] part in reasons.Chunk(20))
        Console.WriteLine("::notice title=PNG and WebP corpus profile and comment refusals::" + string.Join("%0A", part));
    foreach (string crash in crashes.Take(10)) Console.WriteLine("::error title=PNG or WebP writer defect::" + crash);
    if (crashes.Count != 0) return 1;
}
else { Console.Error.WriteLine("Usage: generate DIRECTORY | tiff-based-edits DIRECTORY | tiff-based-corpus CORPUS DIRECTORY | bmff-edits DIRECTORY | bmff-corpus CORPUS DIRECTORY | jpeg-embedded-edits DIRECTORY | jpeg-embedded-corpus CORPUS DIRECTORY | png-webp-embedded-edits DIRECTORY | png-webp-embedded-corpus CORPUS DIRECTORY | inspect[-xmp|-iptc] FILE | noop[-xmp|-iptc] INPUT OUTPUT | rewrite-iptc INPUT OUTPUT"); return 2; }
return 0;


static byte[] Resource(ushort id, byte[] data)
{
    byte[] result = new byte[12 + data.Length + (data.Length & 1)]; "8BIM"u8.CopyTo(result);
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(4), id);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(8), (uint)data.Length); data.CopyTo(result, 12); return result;
}

static ExifValue AsciiValue(string text) =>
    ExifValue.FromRaw(TiffFieldType.Ascii, (uint)text.Length + 1, Endianness.LittleEndian, Encoding.ASCII.GetBytes(text + "\0"));

static string? RawText(ExifValue? value)
{
    if (value is null) return null; byte[] bytes = new byte[value.ByteLength]; value.CopyRawTo(bytes); return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
}

static byte[] IccProfile(int size, int seed)
{
    // The header fields every reader checks, and bytes that depend on the seed around them.
    byte[] profile = new byte[size];
    for (int index = 0; index < size; index++) profile[index] = (byte)(index * 31 + seed * 17 + (index >> 8));
    BinaryPrimitives.WriteUInt32BigEndian(profile, (uint)size);
    "acsp"u8.CopyTo(profile.AsSpan(36));
    return profile;
}
