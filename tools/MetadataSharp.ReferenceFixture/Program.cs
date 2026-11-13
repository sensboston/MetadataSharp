#pragma warning disable CS0618 // Reference fixtures exercise the retained compatibility editor.
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Eps;
using MetadataSharp.Formats.Pgf;
using MetadataSharp.Core.Sync;
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
        // An empty value is written as an empty element, so the measure is taken with one character.
        var empty = new XmpEdit(); empty.Set(new("http://ns.adobe.com/xap/1.0/", "CreatorTool"), XmpValue.CreateText("a"));
        int overhead = XmpPacket.CreateEmpty().Apply(empty)!.ByteLength - 1;
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
else if (args.Length == 3 && args[0] == "small-format-writes")
{
    string fixtures = args[1], output = args[2];
    Directory.CreateDirectory(output);
    byte[] ReadFixture(string name) => File.ReadAllBytes(Path.Combine(fixtures, name));
    XmpEdit Xmp(string value) { var edit = new XmpEdit(); edit.Set("Xmp.xmp.CreatorTool", value); return edit; }

    File.WriteAllBytes(Path.Combine(output, "ours-eps-insert.eps"), EpsMetadata.WriteXmp(ReadFixture("eps-plain.eps"), Xmp("Parity tool")));
    File.WriteAllBytes(Path.Combine(output, "ours-eps-replace.eps"), EpsMetadata.WriteXmp(ReadFixture("eps-xmp.eps"), Xmp("Parity tool")));
    var remove = new XmpEdit(); remove.RemoveAllXmp();
    File.WriteAllBytes(Path.Combine(output, "ours-eps-remove.eps"), EpsMetadata.WriteXmp(ReadFixture("eps-xmp.eps"), remove));
    File.WriteAllBytes(Path.Combine(output, "ours-dos-eps.eps"), EpsMetadata.WriteXmp(ReadFixture("dos-eps-xmp.eps"), Xmp("Parity tool")));

    var edits = new SyncEdits();
    edits.Exif.Set(new(ExifDirectoryKind.Ifd0, 0x013B), "Parity Artist");
    edits.Iptc.SetHeadline("Parity headline");
    edits.Xmp.Set("Xmp.xmp.CreatorTool", "Parity tool");
    File.WriteAllBytes(Path.Combine(output, "ours-pgf.pgf"), PgfMetadata.Write(ReadFixture("pgf-png.pgf"), edits, "Parity comment", replaceComment: true));
}
else if (args.Length == 2 && args[0] == "inspect-small-format")
{
    ImageMetadata image = ImageMetadata.Read(args[1]);
    string? artist = null, headline = null, creatorTool = null;
    if (image.Exif is ExifMetadata exif && exif.TryGetEntry(new(ExifDirectoryKind.Ifd0, 0x013B), out ExifEntry? entry))
    { byte[] bytes = new byte[entry.Value.ByteLength]; entry.Value.CopyRawTo(bytes); artist = Encoding.ASCII.GetString(bytes).TrimEnd('\0'); }
    if (image.Iptc is not null)
    {
        int index = image.Iptc.Datasets.ToList().FindIndex(item => item.Key == new IptcDatasetKey(2, 105));
        if (index >= 0) headline = image.Iptc.GetText(index);
    }
    creatorTool = image.Xmp?.Entries.FirstOrDefault(item => item.Path == "CreatorTool")?.Values.FirstOrDefault();
    Console.WriteLine(JsonSerializer.Serialize(new { format = image.Format.ToString(), artist, headline, creatorTool, comments = image.Comments }));
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
else if (args.Length == 2 && args[0] == "xmp-edits")
{
    // Files for the pinned tool to read after an XMP edit by path: every node kind, a namespace of
    // the edit's own, then a second edit of the result, in a JPEG, a PNG, a WebP, a TIFF and a HEIC
    // file and a sidecar; a JPEG file whose packet is Extended XMP; a JPEG file with qualifiers.
    // manifest.tsv has one row per file: the file, then every line the tool has to print for its
    // XMP as "key type count value", |-separated. A value of * stands for any value.
    Directory.CreateDirectory(args[1]);
    var manifest = new List<string>();
    string XmpAt(string name) => Path.Combine(args[1], name);
    string TextLine(string key, string value) => $"{key} XmpText {Encoding.UTF8.GetByteCount(value)} {value}";
    // The tool prints a structure or an array of structures as a text that names its form.
    string NodeLine(string key, string form) => TextLine(key, $"type=\"{form}\"");
    const string acme = "https://example.com/ns/acme/1.0/";
    const string title = "Harbour at dawn", french = "Le port \u00E0 l'aube", note = "custom & <escaped>";

    var build = new XmpEdit();
    build.RegisterNamespace("acme", acme);
    build.Set("Xmp.xmp.Rating", "4");
    build.Set("Xmp.xmp.CreatorTool", "MetadataSharp");
    build.Append("Xmp.dc.subject", XmpValueKind.Bag, "harbour");
    build.Append("Xmp.dc.subject", XmpValueKind.Bag, "dawn");
    build.Append("Xmp.dc.creator", XmpValueKind.Sequence, "Ana Mart\u00EDn");
    build.SetLanguage("Xmp.dc.title", "x-default", title);
    build.SetLanguage("Xmp.dc.title", "fr-FR", french);
    build.Set("Xmp.xmpMM.DerivedFrom/stRef:documentID", "xmp.did:1");
    build.Set("Xmp.iptc.CreatorContactInfo/Iptc4xmpCore:CiAdrCity", "Boston");
    build.AppendStruct("Xmp.xmpMM.History", XmpValueKind.Sequence);
    build.Set("Xmp.xmpMM.History[last()]/stEvt:action", "created");
    build.AppendStruct("Xmp.xmpMM.History", XmpValueKind.Sequence);
    build.Set("Xmp.xmpMM.History[last()]/stEvt:action", "saved");
    build.Set("Xmp.acme.note", note);
    build.Set("Xmp.acme.job/acme:id", "J-1");
    build.Append("Xmp.acme.steps", XmpValueKind.Sequence, "one");
    build.Append("Xmp.acme.steps", XmpValueKind.Sequence, "two");
    string[] built =
    [
        TextLine("Xmp.xmp.Rating", "4"), TextLine("Xmp.xmp.CreatorTool", "MetadataSharp"),
        "Xmp.dc.subject XmpBag 2 harbour, dawn", "Xmp.dc.creator XmpSeq 1 Ana Mart\u00EDn",
        $"Xmp.dc.title LangAlt 2 lang=\"x-default\" {title}, lang=\"fr-FR\" {french}",
        NodeLine("Xmp.xmpMM.DerivedFrom", "Struct"), TextLine("Xmp.xmpMM.DerivedFrom/stRef:documentID", "xmp.did:1"),
        NodeLine("Xmp.iptc.CreatorContactInfo", "Struct"), TextLine("Xmp.iptc.CreatorContactInfo/Iptc4xmpCore:CiAdrCity", "Boston"),
        NodeLine("Xmp.xmpMM.History", "Seq"),
        NodeLine("Xmp.xmpMM.History[1]", "Struct"), TextLine("Xmp.xmpMM.History[1]/stEvt:action", "created"),
        NodeLine("Xmp.xmpMM.History[2]", "Struct"), TextLine("Xmp.xmpMM.History[2]/stEvt:action", "saved"),
        TextLine("Xmp.acme.note", note),
        NodeLine("Xmp.acme.job", "Struct"), TextLine("Xmp.acme.job/acme:id", "J-1"),
        "Xmp.acme.steps XmpSeq 2 one, two"
    ];

    // The second edit names what the first wrote by the keys the packet lists; "acme" is now a
    // prefix of the packet. The structure that loses its only field goes with it.
    var change = new XmpEdit();
    change.Set("Xmp.xmp.Rating", "5");
    change.RemovePath("Xmp.dc.subject[1]");
    change.Insert("Xmp.dc.subject", 1, "quay");
    change.RemoveLanguage("Xmp.dc.title", "fr-FR");
    change.Set("Xmp.xmpMM.History[1]/stEvt:action", "derived");
    change.RemovePath("Xmp.xmpMM.History[2]");
    change.RemovePath("Xmp.acme.job/acme:id");
    change.Set("Xmp.xmpMM.DerivedFrom/stRef:instanceID", "xmp.iid:2");
    change.Set("Xmp.acme.steps[last()]", "three");
    string[] changed =
    [
        TextLine("Xmp.xmp.Rating", "5"), TextLine("Xmp.xmp.CreatorTool", "MetadataSharp"),
        "Xmp.dc.subject XmpBag 2 quay, dawn", "Xmp.dc.creator XmpSeq 1 Ana Mart\u00EDn",
        $"Xmp.dc.title LangAlt 1 lang=\"x-default\" {title}",
        NodeLine("Xmp.xmpMM.DerivedFrom", "Struct"), TextLine("Xmp.xmpMM.DerivedFrom/stRef:documentID", "xmp.did:1"),
        TextLine("Xmp.xmpMM.DerivedFrom/stRef:instanceID", "xmp.iid:2"),
        NodeLine("Xmp.iptc.CreatorContactInfo", "Struct"), TextLine("Xmp.iptc.CreatorContactInfo/Iptc4xmpCore:CiAdrCity", "Boston"),
        NodeLine("Xmp.xmpMM.History", "Seq"),
        NodeLine("Xmp.xmpMM.History[1]", "Struct"), TextLine("Xmp.xmpMM.History[1]/stEvt:action", "derived"),
        TextLine("Xmp.acme.note", note),
        "Xmp.acme.steps XmpSeq 2 one, three"
    ];
    void XmpRow(string file, string[] lines) => manifest.Add(file + "\t" + string.Join('|', lines));

    byte[] jpeg = JpegFixture.Build(JpegFixture.Segment(0xE0, [74, 70, 73, 70, 0, 1, 2, 0, 0, 1, 0, 1, 0, 0]));
    using (var document = JpegMetadataDocument.Open(new MemoryStream(jpeg))) document.SaveXmpAs(XmpAt("built.jpg"), build);
    using (var document = JpegMetadataDocument.Open(XmpAt("built.jpg"))) document.SaveXmpAs(XmpAt("changed.jpg"), change);
    using (var document = PngMetadataDocument.Open(new MemoryStream(PngFixture.Build()))) document.SaveXmpAs(XmpAt("built.png"), build);
    using (var document = PngMetadataDocument.Open(XmpAt("built.png"))) document.SaveXmpAs(XmpAt("changed.png"), change);
    using (var document = WebPMetadataDocument.Open(new MemoryStream(WebPFixture.Image(0)))) document.SaveXmpAs(XmpAt("built.webp"), build);
    using (var document = WebPMetadataDocument.Open(XmpAt("built.webp"))) document.SaveXmpAs(XmpAt("changed.webp"), change);
    using (var document = TiffBasedMetadataDocument.Open(new MemoryStream(TiffBasedFixture.Tiff(false).Bytes))) document.SaveXmpAs(XmpAt("built.tif"), build);
    using (var document = TiffBasedMetadataDocument.Open(XmpAt("built.tif"))) document.SaveXmpAs(XmpAt("changed.tif"), change);
    BmffFixture heic = BmffFixture.Bare.Select(name => BmffFixture.Create(name, false)).First(fixture => fixture.Plan.ToolReads);
    using (var document = BmffMetadataDocument.Open(new MemoryStream(heic.Bytes))) document.SaveXmpAs(XmpAt("built.heic"), build);
    using (var document = BmffMetadataDocument.Open(XmpAt("built.heic"))) document.SaveXmpAs(XmpAt("changed.heic"), change);
    XmpPacket sidecar = XmpPacket.CreateEmpty().Apply(build)!;
    sidecar.SaveAs(XmpAt("built.xmp"));
    XmpPacket.Load(XmpAt("built.xmp")).Apply(change)!.SaveAs(XmpAt("changed.xmp"));
    foreach (string extension in new[] { "jpg", "png", "webp", "tif", "heic", "xmp" })
    {
        XmpRow("built." + extension, built);
        XmpRow("changed." + extension, changed);
    }

    // Extended XMP: the tool reads the standard packet, which keeps everything but the one large
    // property and names the extension by its digest.
    var large = new XmpEdit();
    large.Set("Xmp.acme.large", string.Concat(Enumerable.Range(0, 5000).Select(i => "chunk " + i.ToString("D5") + ". ")));
    using (var document = JpegMetadataDocument.Open(XmpAt("built.jpg"))) document.SaveXmpAs(XmpAt("extended.jpg"), large);
    XmpRow("extended.jpg", [.. built, "Xmp.xmpNote.HasExtendedXMP XmpText 32 *"]);
    // An edit of a file that has Extended XMP, after which the packet is one segment again.
    var small = new XmpEdit();
    small.RemovePath("Xmp.acme.large");
    small.Set("Xmp.xmp.Rating", "5");
    using (var document = JpegMetadataDocument.Open(XmpAt("extended.jpg"))) document.SaveXmpAs(XmpAt("extended-undone.jpg"), small);
    XmpRow("extended-undone.jpg", [TextLine("Xmp.xmp.Rating", "5"), .. built.Skip(1)]);

    var qualified = new XmpEdit();
    qualified.RegisterNamespace("acme", acme);
    qualified.Set("Xmp.acme.note", note);
    qualified.Set("Xmp.acme.note/?acme:role", "caption");
    using (var document = JpegMetadataDocument.Open(new MemoryStream(jpeg))) document.SaveXmpAs(XmpAt("qualified.jpg"), qualified);
    XmpRow("qualified.jpg", [TextLine("Xmp.acme.note", note), TextLine("Xmp.acme.note/?acme:role", "caption")]);
    File.WriteAllLines(XmpAt("manifest.tsv"), manifest);
}
else if (args.Length == 2 && args[0] == "iptc-edits")
{
    // Files for the pinned tool to read after an IPTC edit: datasets of every class of records 1 and
    // 2 and one the specification does not define, then a second edit of the result that appends,
    // inserts and removes occurrences, in a JPEG, a TIFF and a PNG file; and a TIFF file whose
    // stream lives in its Photoshop resources (tag 0x8649) beside another resource and a digest.
    // manifest.tsv has one row per file: the file, then every line the tool has to print for its
    // IPTC as "key type count value", |-separated. A value of * stands for any value.
    Directory.CreateDirectory(args[1]);
    var manifest = new List<string>();
    string IptcAt(string name) => Path.Combine(args[1], name);
    string Text(string key, string value) => $"Iptc.{key} String {Encoding.UTF8.GetByteCount(value)} {value}";
    string Day(string key, string value) => $"Iptc.{key} Date 8 {value}";
    string Clock(string key, string value) => $"Iptc.{key} Time 11 {value}";
    string Number(string key, int value) => $"Iptc.{key} Short 1 {value}";
    var keywords = new IptcDatasetKey(2, 25);
    const string caption = "\u041F\u0440\u0438\u0432\u0435\u0442, \u043F\u043E\u0440\u0442", creator = "Ana Mart\u00EDn", city = "R\u012Bga";

    var build = new IptcEdit();
    build.SetObjectName("Harbour at dawn");
    build.SetKeywords(["harbour", "dawn"]);
    build.AddKeyword("boats");
    build.SetBylines([creator]);
    build.SetBylineTitles(["Staff"]);
    build.SetHeadline("Boats leave");
    build.SetCaption(caption);
    build.SetCopyright("(c) 2026 Example");
    build.SetCredit("Example");
    build.SetSource("Desk");
    build.SetCity(city);
    build.SetSubLocation("Old town");
    build.SetProvinceState("Vidzeme");
    build.SetCountryCode("LVA");
    build.SetCountryName("Latvia");
    build.SetDateCreated(new DateTimeOffset(2026, 11, 12, 9, 5, 7, TimeSpan.FromHours(2)));
    build.Set(new(2, 10), "5");
    build.Set(new(2, 15), "SPO");
    build.ReplaceText(new(2, 20), ["football", "cup"]);
    build.Set(new(2, 12), "IPTC:15054000:sport:soccer");
    build.Set(new(2, 130), "3Y");
    build.SetDate(new(2, 30), new DateOnly(2026, 12, 1));
    build.SetTime(new(2, 35), new TimeOnly(8, 0, 0), new TimeSpan(-5, -30, 0));
    build.SetUInt16(new(1, 20), 11);
    build.Set(new(1, 30), "MSHARP");
    build.Set(new(1, 40), "00000042");
    build.SetDate(new(1, 70), new DateOnly(2026, 11, 13));
    build.ReplaceRaw(new(2, 250), [IptcValue.FromRaw("opaque"u8)]);
    string[] common =
    [
        "Iptc.Envelope.CharacterSet String 3 *", Number("Envelope.FileFormat", 11), Text("Envelope.ServiceId", "MSHARP"),
        Text("Envelope.EnvelopeNumber", "00000042"), Day("Envelope.DateSent", "2026-11-13"),
        Number("Application2.RecordVersion", 4), Text("Application2.ObjectName", "Harbour at dawn"),
        Text("Application2.BylineTitle", "Staff"), Text("Application2.Headline", "Boats leave"),
        Text("Application2.Copyright", "(c) 2026 Example"), Text("Application2.Credit", "Example"), Text("Application2.Source", "Desk"),
        Text("Application2.SubLocation", "Old town"), Text("Application2.ProvinceState", "Vidzeme"),
        Text("Application2.CountryCode", "LVA"), Text("Application2.CountryName", "Latvia"),
        Text("Application2.Urgency", "5"), Text("Application2.Category", "SPO"),
        Text("Application2.Subject", "IPTC:15054000:sport:soccer"), Text("Application2.ImageType", "3Y"),
        Day("Application2.ReleaseDate", "2026-12-01"), Clock("Application2.ReleaseTime", "08:00:00-05:30"),
        Text("Application2.0x00fa", "opaque"), Text("Application2.Byline", creator),
    ];
    string[] built =
    [
        .. common,
        Text("Application2.Keywords", "harbour"), Text("Application2.Keywords", "dawn"), Text("Application2.Keywords", "boats"),
        Text("Application2.Caption", caption), Text("Application2.City", city),
        Day("Application2.DateCreated", "2026-11-12"), Clock("Application2.TimeCreated", "09:05:07+02:00"),
        Text("Application2.SuppCategory", "football"), Text("Application2.SuppCategory", "cup"),
    ];

    // The second edit works on occurrences: one removed, one inserted, one appended; a repeatable
    // dataset removed whole; a date set alone, which takes the time out; a raw occurrence appended.
    var change = new IptcEdit();
    change.RemoveAt(keywords, 0);
    change.Insert(keywords, 1, "quay");
    change.Append(new(2, 80), "Li Wei");
    change.SetCity("Riga");
    change.Remove(new(2, 20));
    change.SetDateCreated(new DateOnly(2027, 1, 2));
    change.SetCaption(null);
    change.AppendRaw(new(2, 250), IptcValue.FromRaw("second"u8));
    string[] changed =
    [
        .. common,
        Text("Application2.Keywords", "dawn"), Text("Application2.Keywords", "quay"), Text("Application2.Keywords", "boats"),
        Text("Application2.Byline", "Li Wei"), Text("Application2.City", "Riga"),
        Day("Application2.DateCreated", "2027-01-02"), Text("Application2.0x00fa", "second"),
    ];
    void IptcRow(string file, string[] lines) => manifest.Add(file + "\t" + string.Join('|', lines));

    byte[] jpeg = JpegFixture.Build(JpegFixture.Segment(0xE0, [74, 70, 73, 70, 0, 1, 2, 0, 0, 1, 0, 1, 0, 0]));
    using (var document = JpegMetadataDocument.Open(new MemoryStream(jpeg))) document.SaveIptcAs(IptcAt("built.jpg"), build);
    using (var document = JpegMetadataDocument.Open(IptcAt("built.jpg"))) document.SaveIptcAs(IptcAt("changed.jpg"), change);
    using (var document = TiffBasedMetadataDocument.Open(new MemoryStream(TiffBasedFixture.Tiff(false).Bytes))) document.SaveIptcAs(IptcAt("built.tif"), build);
    using (var document = TiffBasedMetadataDocument.Open(IptcAt("built.tif"))) document.SaveIptcAs(IptcAt("changed.tif"), change);
    using (var document = PngMetadataDocument.Open(new MemoryStream(PngFixture.Build()))) document.SaveIptcAs(IptcAt("built.png"), build);
    using (var document = PngMetadataDocument.Open(IptcAt("built.png"))) document.SaveIptcAs(IptcAt("changed.png"), change);
    foreach (string extension in new[] { "jpg", "tif", "png" })
    {
        IptcRow("built." + extension, built);
        IptcRow("changed." + extension, changed);
    }

    // The stream of this TIFF file is resource 0x0404 of tag 0x8649, between another resource and a
    // digest; it declares no character set and is given ASCII, so it still declares none.
    byte[] held = [0x1C, 2, 120, 0, 5, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o', 0x1C, 2, 25, 0, 3, (byte)'o', (byte)'l', (byte)'d'];
    byte[] block = [.. Resource(0x0424, [1, 2, 3, 4]), .. Resource(0x0404, held), .. Resource(0x0425, new byte[16])];
    var inResources = new IptcEdit();
    inResources.SetCaption("changed");
    inResources.AddKeyword("new");
    using (var document = TiffBasedMetadataDocument.Open(new MemoryStream(TiffBasedFixture.Flat(false, (0x010F, 2, Encoding.ASCII.GetBytes("TestCompany\0")), (0x8649, 1, block)))))
        document.SaveIptcAs(IptcAt("resources.tif"), inResources);
    IptcRow("resources.tif", [Text("Application2.Caption", "changed"), Text("Application2.Keywords", "old"), Text("Application2.Keywords", "new")]);
    // The same stream in the resources of a JPEG file, with the digest recomputed on request.
    using (var document = JpegMetadataDocument.Open(new MemoryStream(JpegFixture.Build(JpegFixture.Segment(0xED, [.. "Photoshop 3.0\0"u8, .. block])))))
        document.SaveIptcAs(IptcAt("digest.jpg"), inResources, new IptcWriteOptions { UpdateDigest = true });
    IptcRow("digest.jpg", [Text("Application2.Caption", "changed"), Text("Application2.Keywords", "old"), Text("Application2.Keywords", "new")]);
    File.WriteAllLines(IptcAt("manifest.tsv"), manifest);
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
else if (args.Length == 2 && args[0] == "exif-tag-edits")
{
    // Files for the pinned tool to read after an edit of any tag: an authored block with all five
    // directories, a MakerNote, a tag no vocabulary lists and a thumbnail, in a JPEG, a PNG, a WebP,
    // a TIFF and a HEIC file and in both byte orders; then the same files after tags of every
    // directory were set, after values were written in place, after the tags were taken out again,
    // after two directories lost their last tag; a block of one tag after three directories were
    // added to it; and a file without a block after it was given one. manifest.tsv is that of
    // tiff-based-edits.
    Directory.CreateDirectory(args[1]);
    var manifest = new List<string>();
    string At(string name) => Path.Combine(args[1], name);
    const string pointers = "Exif.Image.ExifTag,Exif.Image.GPSTag,Exif.Photo.InteroperabilityTag";

    var tags = new ExifEdit();
    tags.Set(new(ExifDirectoryKind.Ifd0, 0x013B), "Alice");
    tags.Set(new(ExifDirectoryKind.Ifd0, 0x0112), (ushort)6);
    tags.Set(new(ExifDirectoryKind.Ifd0, 0x011A), new UnsignedRational(300, 1));
    tags.Set(new(ExifDirectoryKind.Ifd0, 0xF1E0), new uint[] { 7, 70_000 });
    tags.Set(new(ExifDirectoryKind.Exif, 0x829A), new UnsignedRational(1, 250));
    tags.Set(new(ExifDirectoryKind.Exif, 0x9204), new SignedRational(-1, 3));
    tags.Set(new(ExifDirectoryKind.Exif, 0x8827), (ushort)400);
    tags.Set(new(ExifDirectoryKind.Exif, 0xA002), 70_000u);
    tags.Set(new(ExifDirectoryKind.Exif, 0x9286),
        ExifValue.FromRaw(TiffFieldType.Undefined, 13, Endianness.LittleEndian, "ASCII\0\0\0Hello"u8));
    tags.Set(new(ExifDirectoryKind.Exif, 0xF1E1), ExifValue.FromRaw(TiffFieldType.SShort, 2, Endianness.LittleEndian, [0xFE, 0xFF, 5, 0]));
    tags.Set(new(ExifDirectoryKind.Gps, 0x0001), "S");
    tags.Set(new(ExifDirectoryKind.Gps, 0x0002), new UnsignedRational[] { new(42, 1), new(22, 1), new(30, 1) });
    tags.Set(new(ExifDirectoryKind.Gps, 0x0003), "W");
    tags.Set(new(ExifDirectoryKind.Gps, 0x0004), new UnsignedRational[] { new(71, 1), new(14, 1), new(15, 1) });
    tags.Set(new(ExifDirectoryKind.Interop, 0x0001), "THM");
    tags.Set(new(ExifDirectoryKind.Interop, 0x0002), ExifValue.FromRaw(TiffFieldType.Undefined, 4, Endianness.LittleEndian, "0100"u8));
    tags.Set(new(ExifDirectoryKind.Thumbnail, 0x0112), (ushort)3);
    tags.Set(new(ExifDirectoryKind.Thumbnail, 0x011A), new UnsignedRational(72, 1));
    const string tagsChanged = pointers + ",Exif.Image.Orientation,Exif.GPSInfo.GPSLatitudeRef,Exif.Iop.InteroperabilityIndex";
    const string tagsText = "Exif.Image.Artist|Alice|right, top|Exif.Image.XResolution|Exif.Image.0xf1e0|7 70000|" +
        "Exif.Photo.ExposureTime|1/250 s|Exif.Photo.ExposureBiasValue|-1/3 EV|Exif.Photo.ISOSpeedRatings|400|" +
        "Exif.Photo.PixelXDimension|70000|Exif.Photo.UserComment|Hello|Exif.Photo.0xf1e1|-2 5|" +
        "Exif.GPSInfo.GPSLatitudeRef|South|Exif.GPSInfo.GPSLatitude |Exif.GPSInfo.GPSLongitudeRef|West|Exif.GPSInfo.GPSLongitude |" +
        "Exif.Iop.InteroperabilityIndex|THM|Exif.Iop.InteroperabilityVersion|1.00|" +
        "Exif.Thumbnail.Orientation|bottom, right|Exif.Thumbnail.XResolution|72";

    // What the first edit set is taken out again, and what it changed is set back.
    var back = new ExifEdit();
    foreach ((ExifDirectoryKind directory, ushort tag) in new (ExifDirectoryKind, ushort)[]
             {
                 (ExifDirectoryKind.Ifd0, 0x013B), (ExifDirectoryKind.Ifd0, 0x011A), (ExifDirectoryKind.Ifd0, 0xF1E0),
                 (ExifDirectoryKind.Exif, 0x829A), (ExifDirectoryKind.Exif, 0x9204), (ExifDirectoryKind.Exif, 0x8827),
                 (ExifDirectoryKind.Exif, 0xA002), (ExifDirectoryKind.Exif, 0x9286), (ExifDirectoryKind.Exif, 0xF1E1),
                 (ExifDirectoryKind.Gps, 0x0002), (ExifDirectoryKind.Gps, 0x0003), (ExifDirectoryKind.Gps, 0x0004),
                 (ExifDirectoryKind.Interop, 0x0002), (ExifDirectoryKind.Thumbnail, 0x0112), (ExifDirectoryKind.Thumbnail, 0x011A)
             })
        back.Remove(new(directory, tag));
    back.Set(new(ExifDirectoryKind.Ifd0, 0x0112), (ushort)1);
    back.Set(new(ExifDirectoryKind.Gps, 0x0001), "N");
    back.Set(new(ExifDirectoryKind.Interop, 0x0001), "R98");

    var inPlace = new ExifEdit();
    inPlace.Set(new(ExifDirectoryKind.Ifd0, 0x0112), (ushort)6);
    inPlace.Set(new(ExifDirectoryKind.Ifd0, 0x010F), "Shorter");
    inPlace.Set(new(ExifDirectoryKind.Exif, 0x9003), "2031:12:30 23:59:58");
    inPlace.Set(new(ExifDirectoryKind.Gps, 0x0001), "S");
    inPlace.Set(new(ExifDirectoryKind.Interop, 0x0001), "THM");
    const string inPlaceChanged = "Exif.Image.Orientation,Exif.Image.Make,Exif.Photo.DateTimeOriginal,Exif.GPSInfo.GPSLatitudeRef,Exif.Iop.InteroperabilityIndex";
    const string inPlaceText = "right, top|Shorter|2031:12:30 23:59:58|South|THM";

    var drop = new ExifEdit();
    drop.Remove(new(ExifDirectoryKind.Gps, 0x0000));
    drop.Remove(new(ExifDirectoryKind.Gps, 0x0001));
    drop.Remove(new(ExifDirectoryKind.Interop, 0x0001));
    const string dropChanged = "Exif.GPSInfo.*,Exif.Image.GPSTag,Exif.Iop.*,Exif.Photo.InteroperabilityTag";

    var create = new ExifEdit();
    create.Set(new(ExifDirectoryKind.Ifd0, 0x013B), "Alice");
    create.Set(new(ExifDirectoryKind.Exif, 0x829A), new UnsignedRational(1, 250));
    create.Set(new(ExifDirectoryKind.Exif, 0xF1E1), ExifValue.FromRaw(TiffFieldType.SShort, 2, Endianness.LittleEndian, [0xFE, 0xFF, 5, 0]));
    create.Set(new(ExifDirectoryKind.Gps, 0x0001), "S");
    create.Set(new(ExifDirectoryKind.Gps, 0x0002), new UnsignedRational[] { new(42, 1), new(22, 1), new(30, 1) });
    create.Set(new(ExifDirectoryKind.Interop, 0x0001), "R98");
    const string createText = "Exif.Image.Artist|Alice|Exif.Photo.ExposureTime|1/250 s|Exif.Photo.0xf1e1|-2 5|" +
        "Exif.GPSInfo.GPSLatitudeRef|South|Exif.GPSInfo.GPSLatitude |Exif.Iop.InteroperabilityIndex|R98";

    void Save(string container, string from, string to, ExifEdit edit)
    {
        switch (container)
        {
            case "jpeg": { using var document = JpegMetadataDocument.Open(At(from)); document.SaveAs(At(to), edit); break; }
            case "png": { using var document = PngMetadataDocument.Open(At(from)); document.SaveExifAs(At(to), edit); break; }
            case "webp": { using var document = WebPMetadataDocument.Open(At(from)); document.SaveExifAs(At(to), edit); break; }
            case "tiff": { using var document = TiffBasedMetadataDocument.Open(At(from)); document.SaveExifAs(At(to), edit); break; }
            default: { using var document = BmffMetadataDocument.Open(At(from)); document.SaveExifAs(At(to), edit); break; }
        }
    }

    foreach (string container in ExifBlockFixture.Containers)
    {
        string extension = container switch { "jpeg" => ".jpg", "tiff" => ".tif", _ => "." + container };
        foreach (bool big in new[] { false, true })
        {
            string stem = container + (big ? "-be" : "-le");
            string rich = stem + "-rich" + extension, plain = stem + "-plain" + extension;
            File.WriteAllBytes(At(rich), ExifBlockFixture.Wrap(container, ExifBlockFixture.Rich(big).Bytes, big));
            File.WriteAllBytes(At(plain), ExifBlockFixture.Wrap(container, ExifBlockFixture.Plain(big).Bytes, big));
            string Edited(string from, string kind, ExifEdit edit)
            {
                string target = stem + "-" + kind + extension;
                Save(container, from, target, edit);
                return target;
            }

            string withTags = Edited(rich, "tags", tags);
            manifest.Add(string.Join('\t', rich, withTags, tagsChanged, tagsText));
            // Every line of the source again: the pointers alone name other addresses.
            manifest.Add(string.Join('\t', rich, Edited(withTags, "back", back), pointers, "Exif.Image.Make|TestCompany"));
            manifest.Add(string.Join('\t', rich, Edited(rich, "inplace", inPlace), inPlaceChanged, inPlaceText));
            manifest.Add(string.Join('\t', rich, Edited(rich, "drop", drop), dropChanged, "Exif.Photo.DateTimeOriginal"));
            manifest.Add(string.Join('\t', plain, Edited(plain, "create", create), "", createText));
        }
        if (container == "tiff") continue;
        string bare = container + "-bare" + extension, given = container + "-bare-new" + extension;
        File.WriteAllBytes(At(bare), ExifBlockFixture.Bare(container));
        Save(container, bare, given, create);
        manifest.Add(string.Join('\t', "-", given, "", createText));
    }
    File.WriteAllLines(At("manifest.tsv"), manifest);
}
else if (args.Length == 3 && args[0] == "exif-tag-corpus")
{
    // The same check on files this repository did not author: every JPEG, PNG and WebP file under
    // a directory is edited four ways, each from the file as it is. The EXIF block of these three
    // is edited where it lies, so what the pinned tool prints of a MakerNote has to be what it
    // printed before. A refusal is counted by its code; a file that was written goes into the
    // manifest for the pinned tool to read. An exception of another type is a defect of the writer
    // and fails the run. A file without an EXIF block gains one, and the tool may have printed
    // nothing of such a source, which the check then leaves out: its edited files are named a
    // second time with "-" as their source, so that the tool has to read them and print what was set.
    Directory.CreateDirectory(args[2]);
    var manifest = new List<string>();
    var outcomes = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var refusals = new List<(string File, string Kind, string Reason)>();
    var crashes = new List<string>();
    int gained = 0;
    const string date = "2021:06:07 08:09:10", artist = "An artist whose name is longer than any slot";
    const string gpsKeys = "Exif.GPSInfo.GPSVersionID,Exif.GPSInfo.GPSLatitudeRef,Exif.GPSInfo.GPSLatitude,Exif.GPSInfo.GPSLongitudeRef,Exif.GPSInfo.GPSLongitude";

    var inPlace = new ExifEdit();
    inPlace.Set(new(ExifDirectoryKind.Exif, 0x9003), date);
    var grow = new ExifEdit();
    grow.Set(new(ExifDirectoryKind.Ifd0, 0x013B), artist);
    grow.Set(new(ExifDirectoryKind.Exif, 0x9004), date);
    grow.Set(new(ExifDirectoryKind.Exif, 0xF1E1), ExifValue.FromRaw(TiffFieldType.SShort, 2, Endianness.LittleEndian, [0xFE, 0xFF, 5, 0]));
    grow.Set(new(ExifDirectoryKind.Gps, 0), ExifValue.FromRaw(TiffFieldType.Byte, 4, Endianness.LittleEndian, [2, 3, 0, 0]));
    grow.Set(new(ExifDirectoryKind.Gps, 1), "N");
    grow.Set(new(ExifDirectoryKind.Gps, 2), new UnsignedRational[] { new(42, 1), new(22, 1), new(30, 1) });
    grow.Set(new(ExifDirectoryKind.Gps, 3), "W");
    grow.Set(new(ExifDirectoryKind.Gps, 4), new UnsignedRational[] { new(71, 1), new(14, 1), new(15, 1) });
    grow.Set(new(ExifDirectoryKind.Interop, 0x0001), "R98");
    var thumbnail = new ExifEdit();
    thumbnail.Set(new(ExifDirectoryKind.Thumbnail, 0x0112), (ushort)3);
    thumbnail.Set(new(ExifDirectoryKind.Thumbnail, 0xF1E2), 70_000u);
    var remove = new ExifEdit();
    remove.Remove(new(ExifDirectoryKind.Ifd0, 0x0131));
    remove.Remove(new(ExifDirectoryKind.Exif, 0x9286));
    remove.Remove(new(ExifDirectoryKind.Gps, 0x0012));

    int files = 0, number = 0;
    void Count(string outcome) => outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
    foreach (string path in Directory.EnumerateFiles(args[1], "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        string container = extension switch { ".jpg" or ".jpeg" => "jpeg", ".png" => "png", ".webp" => "webp", _ => "" };
        if (container.Length == 0 || new FileInfo(path).Length > 64 * 1024 * 1024) continue;
        IDisposable document;
        try
        {
            document = container switch
            {
                "jpeg" => JpegMetadataDocument.Open(path),
                "png" => PngMetadataDocument.Open(path),
                _ => WebPMetadataDocument.Open(path)
            };
        }
        catch (MetadataException) { continue; } // not that container, whatever its name says
        files++;
        using (document)
        {
            bool bare;
            try
            {
                bare = document switch
                {
                    JpegMetadataDocument jpeg => jpeg.Exif,
                    PngMetadataDocument png => png.ReadExif(),
                    _ => ((WebPMetadataDocument)document).ReadExif()
                } is null;
            }
            catch (MetadataException) { bare = false; } // a block is there; the edits below say why it is not read

            void Try(string kind, string changed, string expected, ExifEdit edit)
            {
                string target = Path.Combine(args[2], $"c{++number:D5}-{kind}{extension}");
                try
                {
                    switch (document)
                    {
                        case JpegMetadataDocument jpeg: jpeg.SaveAs(target, edit); break;
                        case PngMetadataDocument png: png.SaveExifAs(target, edit); break;
                        case WebPMetadataDocument webp: webp.SaveExifAs(target, edit); break;
                    }
                    manifest.Add(string.Join('\t', Path.GetFullPath(path), Path.GetFileName(target), changed, expected));
                    if (bare && kind != "remove")
                    {
                        manifest.Add(string.Join('\t', "-", Path.GetFileName(target), changed, expected));
                        gained++;
                    }
                    Count(container + " " + kind + " written");
                }
                catch (MetadataException refusal)
                {
                    Count(container + " " + kind + " refused " + refusal.Code);
                    refusals.Add((Path.GetFileName(path), kind, $"{refusal.Code}: {refusal.Message}"));
                }
                catch (Exception defect) when (defect is not IOException and not UnauthorizedAccessException)
                {
                    crashes.Add($"{Path.GetFileName(path)} {kind}: {defect.GetType().Name}: {defect.Message}");
                }
            }

            Try("inplace", "Exif.Image.ExifTag,Exif.Photo.DateTimeOriginal", date, inPlace);
            Try("grow", "Exif.Image.Artist,Exif.Image.ExifTag,Exif.Image.GPSTag,Exif.Photo.InteroperabilityTag,Exif.Photo.DateTimeDigitized," +
                "Exif.Photo.0xf1e1,Exif.Iop.InteroperabilityIndex," + gpsKeys, artist + "|Exif.Photo.0xf1e1|Exif.GPSInfo.GPSLongitude ", grow);
            Try("thumbnail", "Exif.Thumbnail.Orientation,Exif.Thumbnail.0xf1e2", "Exif.Thumbnail.0xf1e2", thumbnail);
            Try("remove", "Exif.Image.Software,Exif.Photo.UserComment,Exif.GPSInfo.GPSMapDatum,Exif.Image.ExifTag,Exif.Image.GPSTag", "", remove);
        }
    }
    File.WriteAllLines(Path.Combine(args[2], "manifest.tsv"), manifest);
    Console.WriteLine($"::notice title=Any-tag EXIF corpus edits::{files} JPEG, PNG and WebP files; " + string.Join("; ", outcomes.Select(pair => pair.Key + " " + pair.Value)) +
        $"; {gained} written files gained their EXIF block");
    // Why each file was refused, one line for a reason and the number of files it was given for.
    string[] reasons = refusals.GroupBy(refusal => refusal.Reason, StringComparer.Ordinal)
        .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal)
        .Select(group => $"{group.Select(refusal => refusal.File).Distinct().Count()} file(s), first {group.First().File} [{string.Join(' ', group.Select(refusal => refusal.Kind).Distinct())}]: {group.Key}"
            .Replace("%", "%25").Replace("\r", " ").Replace("\n", " "))
        .ToArray();
    foreach (string[] chunk in reasons.Chunk(20))
        Console.WriteLine("::notice title=Any-tag EXIF corpus refusals::" + string.Join("%0A", chunk));
    foreach (string crash in crashes.Take(10)) Console.WriteLine("::error title=EXIF block writer defect::" + crash);
    if (crashes.Count != 0) return 1;
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
else if (args.Length == 2 && args[0] == "tiff-based-icc-edits")
{
    // Files for the pinned tool to read after the TIFF-based writer gave them an ICC profile,
    // replaced it or took it out: every authored plain TIFF in both byte orders. The authored DNG
    // and raw layouts are to be refused, and leave no file for the tool. manifest.tsv is that
    // of the JPEG command: the source, the edited file, the profile the tool has to print for it,
    // and "=" for the comment, which this library does not write into such a file.
    Directory.CreateDirectory(args[1]);
    var manifest = new List<string>();
    string At(string name) => Path.Combine(args[1], name);
    string Stored(string name, byte[] profile) { File.WriteAllBytes(At(name), profile); return name; }
    bool Same(string one, string other) => File.ReadAllBytes(At(one)).AsSpan().SequenceEqual(File.ReadAllBytes(At(other)));
    byte[] small = IccProfile(3_144, 1), odd = IccProfile(70_001, 2), brief = IccProfile(560, 3), large = IccProfile(1_000_000, 4);
    string smallIcc = Stored("small.icc", small), oddIcc = Stored("odd.icc", odd), briefIcc = Stored("brief.icc", brief), largeIcc = Stored("large.icc", large);
    const string date = "2021:06:07 08:09:10", longText = "A considerably longer text than the one before";
    const string title = "::error title=TIFF-based profile edits::";

    var raw = new List<(string Name, byte[] Bytes)> { ("rw2-le.rw2", TiffBasedFixture.Rw2().Bytes) };
    foreach (string layout in TiffBasedFixture.Names.Except(TiffBasedFixture.Plain))
        foreach (bool big in new[] { false, true })
            raw.Add((layout + (big ? "-be." : "-le.") + layout, TiffBasedFixture.Create(layout, big).Bytes));
    foreach ((string file, byte[] bytes) in raw)
    {
        using TiffBasedMetadataDocument unsuitable = TiffBasedMetadataDocument.Open(new MemoryStream(bytes));
        foreach (byte[]? given in new[] { small, null })
        {
            using var output = new MemoryStream();
            try
            {
                unsuitable.SaveIccProfile(output, given);
                Console.WriteLine($"{title}{file} is a DNG or a raw file and its profile was written or removed");
                return 1;
            }
            catch (MetadataException refusal) when (refusal.Code == MetadataErrorCode.UnsupportedFeature && output.Length == 0)
            {
            }
        }
    }
    Console.WriteLine($"::notice title=TIFF-based profile refusals::{raw.Count} authored DNG and raw files refused, with a profile and with none");

    foreach (string name in TiffBasedFixture.Plain)
    {
        foreach (bool big in new[] { false, true })
        {
            TiffBasedFixture fixture = TiffBasedFixture.Create(name, big);
            string stem = name + (big ? "-be" : "-le"), extension = ".tif";
            string source = stem + extension;
            File.WriteAllBytes(At(source), fixture.Bytes);
            var grow = new ExifEdit();
            grow.Set(new(ExifDirectoryKind.Ifd0, (ushort)fixture.TextTag), AsciiValue(longText));
            grow.Set(new(ExifDirectoryKind.Exif, 0x9004), AsciiValue(date));

            string Edited(string from, string kind, Action<TiffBasedMetadataDocument, string> save)
            {
                string target = stem + "-" + kind + extension;
                using TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(At(from));
                save(document, At(target));
                return target;
            }
            string Icc(string from, string kind, byte[]? profile) => Edited(from, kind, (document, path) => document.SaveIccProfileAs(path, profile));
            void Row(string from, string edited, string profile) => manifest.Add(string.Join('\t', from, edited, profile, "="));

            // A new field; then a value that has to be appended, one that fits the bytes of the
            // old one, none, and the one the file has.
            string given = Icc(source, "icc", small);
            Row(source, given, smallIcc);
            Row(given, Icc(given, "icc-longer", odd), oddIcc);
            Row(given, Icc(given, "icc-shorter", brief), briefIcc);
            Row(given, Icc(given, "icc-removed", null), "-");
            string same = Icc(given, "icc-same", small);
            Row(given, same, "=");
            if (name == "tiff") Row(source, Icc(source, "icc-large", large), largeIcc);

            // After an edit of the EXIF data that moved the directory, and before one: the file
            // that was edited afterwards still has the profile, and gives it up or takes another.
            string first = Edited(source, "exif", (document, path) => document.SaveExifAs(path, grow));
            Row(first, Icc(first, "exif-icc", small), smallIcc);
            string then = Edited(given, "icc-exif", (document, path) => document.SaveExifAs(path, grow));
            string kept = Icc(then, "icc-exif-same", small);
            Row(then, kept, smallIcc);
            Row(then, Icc(then, "icc-exif-longer", odd), oddIcc);
            Row(then, Icc(then, "icc-exif-removed", null), "-");

            // What the tool cannot say: saving the profile a file has changes no byte.
            if (!Same(same, given) || !Same(kept, then))
            {
                Console.WriteLine($"{title}saving the profile {source} has changed the file");
                return 1;
            }
        }
    }
    File.WriteAllLines(At("manifest.tsv"), manifest);
}
else if (args.Length == 3 && args[0] == "tiff-based-icc-corpus")
{
    // The same check on TIFF-based files this repository did not author: each is given a profile
    // and has its profile taken out, each time from the file as it is. Default options.
    string[] extensions = [".tif", ".tiff", ".dng", ".cr2", ".nef", ".nrw", ".arw", ".sr2", ".pef", ".orf", ".rw2", ".srw", ".raw"];
    return IccCorpus(args[1], args[2], "TIFF-based", extensions, path =>
    {
        TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(path);
        return (document, document.SaveIccProfile);
    });
}
else if (args.Length == 2 && args[0] == "bmff-icc-edits")
{
    // The same files for the BMFF writer: every authored HEIC and AVIF layout whose item location
    // box the pinned tool reads, then a file whose profile another writer stored as a restricted
    // one that two items share, beside colour information that is no profile.
    Directory.CreateDirectory(args[1]);
    var manifest = new List<string>();
    string At(string name) => Path.Combine(args[1], name);
    string Stored(string name, byte[] profile) { File.WriteAllBytes(At(name), profile); return name; }
    bool Same(string one, string other) => File.ReadAllBytes(At(one)).AsSpan().SequenceEqual(File.ReadAllBytes(At(other)));
    string Edited(string from, string target, Action<BmffMetadataDocument, string> save)
    {
        using BmffMetadataDocument document = BmffMetadataDocument.Open(At(from));
        save(document, At(target));
        return target;
    }
    string Icc(string from, string target, byte[]? profile) => Edited(from, target, (document, path) => document.SaveIccProfileAs(path, profile));
    void Row(string from, string edited, string profile) => manifest.Add(string.Join('\t', from, edited, profile, "="));
    const string title = "::error title=BMFF profile edits::";

    byte[] small = IccProfile(3_144, 1), odd = IccProfile(70_001, 2), brief = IccProfile(560, 3), large = IccProfile(1_000_000, 4), twin = IccProfile(3_144, 8);
    string smallIcc = Stored("small.icc", small), oddIcc = Stored("odd.icc", odd), briefIcc = Stored("brief.icc", brief), largeIcc = Stored("large.icc", large);
    string twinIcc = Stored("twin.icc", twin);
    const string date = "2021:06:07 08:09:10", longText = "A considerably longer text than the one before";
    var grow = new ExifEdit();
    grow.Set(new(ExifDirectoryKind.Ifd0, 0x010F), AsciiValue(longText));
    grow.Set(new(ExifDirectoryKind.Exif, 0x9004), AsciiValue(date));
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
            File.WriteAllBytes(At(source), fixture.Bytes);
            string Named(string kind) => stem + "-" + kind + extension;

            // A new property; then a profile that needs a longer meta box, one that leaves free
            // space, one of the same length, none, one in a file whose property is free space,
            // and the one the file has.
            string given = Icc(source, Named("icc"), small);
            Row(source, given, smallIcc);
            Row(given, Icc(given, Named("icc-longer"), odd), oddIcc);
            Row(given, Icc(given, Named("icc-shorter"), brief), briefIcc);
            Row(given, Icc(given, Named("icc-twin"), twin), twinIcc);
            string removed = Icc(given, Named("icc-removed"), null);
            Row(given, removed, "-");
            Row(removed, Icc(removed, Named("icc-again"), odd), oddIcc);
            string same = Icc(given, Named("icc-same"), small);
            Row(given, same, "=");
            if (name == "heic") Row(source, Icc(source, Named("icc-large"), large), largeIcc);

            // After an edit of the EXIF data and before one, as for the TIFF-based files; a file
            // without metadata is given its EXIF item by that edit.
            ExifEdit edit = bare ? created : grow;
            string first = Edited(source, Named("exif"), (document, path) => document.SaveExifAs(path, edit));
            Row(first, Icc(first, Named("exif-icc"), small), smallIcc);
            string then = Edited(given, Named("icc-exif"), (document, path) => document.SaveExifAs(path, edit));
            string kept = Icc(then, Named("icc-exif-same"), small);
            Row(then, kept, smallIcc);
            Row(then, Icc(then, Named("icc-exif-longer"), odd), oddIcc);
            Row(then, Icc(then, Named("icc-exif-removed"), null), "-");

            if (!Same(same, given) || !Same(kept, then))
            { Console.WriteLine(title + $"saving the profile {source} has changed the file"); return 1; }
        }
    }

    byte[] stored = IccProfile(560, 6);
    BmffFixture.Layout layout = BmffFixture.LayoutOf("heic", false);
    layout.Items.Add(new BmffFixture.Item(5, "hvc1", BmffFixture.Pattern(64, 3)));
    layout.Properties.Insert(0, BmffFixture.Colour("rICC", stored));
    layout.Properties.Add(BmffFixture.Colour("nclx", [0, 1, 0, 13, 0, 6, 0x80]));
    layout.Associations.Clear();
    layout.Associations.Add((BmffFixture.ImageId, new[] { (true, 1), (true, 2), (false, 3) }));
    layout.Associations.Add((5, new[] { (true, 2), (true, 1) }));
    const string shared = "shared.heic";
    File.WriteAllBytes(At(shared), BmffFixture.Build("heic", false, layout).Bytes);
    Row(shared, Icc(shared, "shared-icc-replaced.heic", small), smallIcc);
    // Eight bytes shorter fits the bytes of the item properties box; four bytes shorter does not.
    Row(shared, Icc(shared, "shared-icc-shorter.heic", IccProfile(552, 9)), Stored("shorter.icc", IccProfile(552, 9)));
    Row(shared, Icc(shared, "shared-icc-nearly.heic", IccProfile(556, 10)), Stored("nearly.icc", IccProfile(556, 10)));
    Row(shared, Icc(shared, "shared-icc-removed.heic", null), "-");
    string unchanged = Icc(shared, "shared-icc-same.heic", stored);
    Row(shared, unchanged, "=");
    File.WriteAllLines(At("manifest.tsv"), manifest);
    if (!Same(unchanged, shared))
    { Console.WriteLine(title + "saving the profile a file has as a restricted one changed the file"); return 1; }
}
else if (args.Length == 3 && args[0] == "bmff-icc-corpus")
{
    // The same check on the HEIC, HEIF and AVIF files this repository did not author.
    return IccCorpus(args[1], args[2], "BMFF", [".heic", ".heif", ".hif", ".avif"], path =>
    {
        BmffMetadataDocument document = BmffMetadataDocument.Open(path);
        return (document, document.SaveIccProfile);
    });
}
else if (args.Length == 2 && args[0] == "sync-parity")
{
    // The conversions of this library on the files the comparer (tools/reference/sync_parity.py)
    // prepared in the directory: for every NAME.jpg, NAME.ours.xmp holds what ExifToXmp makes of the
    // EXIF block of the file, starting from no XMP; and for every NAME.xmp, which the pinned tool
    // wrote from the same file, NAME.back.jpg is a file whose EXIF block is what XmpToExif makes of
    // that sidecar, starting from no EXIF. Nothing is compared here. A file this library refuses is
    // listed with the code of the refusal; an exception of another type is a defect and fails the run.
    string directory = args[1];
    var manifest = new List<string>();
    string? sample = null;
    var crashes = new List<string>();
    var outcomes = new SortedDictionary<string, int>(StringComparer.Ordinal);
    void Count(string outcome) => outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
    string Flat(string text) => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    string NotesOf(MetadataSharp.Core.Sync.SyncEdits edits) =>
        Flat(string.Join(" | ", edits.Notes.Select(note => $"{note.Source} -> {note.Target}: {note.Reason}")));
    byte[] blank = JpegFixture.Build();
    foreach (string path in Directory.EnumerateFiles(directory, "*.jpg").Order(StringComparer.Ordinal))
    {
        string name = Path.GetFileNameWithoutExtension(path);
        if (name.EndsWith(".back", StringComparison.Ordinal)) continue;
        try
        {
            // The unified reader: what it refuses is listed, and the other kinds are still read.
            MetadataSharp.Formats.ImageMetadata read = MetadataSharp.Formats.ImageMetadata.Read(path);
            if (read.Errors.FirstOrDefault(error => error.Family == MetadataFamily.Exif) is { } refused)
            {
                manifest.Add($"{name}\tto-xmp\trefused {refused.Exception.Code}\t{Flat(refused.Exception.Message)}"); Count("to-xmp refused " + refused.Exception.Code);
            }
            else if (read.Exif is not ExifMetadata exif) { manifest.Add($"{name}\tto-xmp\tno-exif\t"); Count("to-xmp no-exif"); }
            else
            {
                MetadataSharp.Core.Sync.SyncEdits edits = MetadataSharp.Core.Sync.MetadataSync.ExifToXmp(exif);
                if (edits.Xmp.IsEmpty) { manifest.Add($"{name}\tto-xmp\tnothing\t{NotesOf(edits)}"); Count("to-xmp nothing"); }
                else
                {
                    XmpPacket.CreateEmpty().Apply(edits.Xmp)!.SaveAs(Path.Combine(directory, name + ".ours.xmp"));
                    manifest.Add($"{name}\tto-xmp\twritten\t{NotesOf(edits)}"); Count("to-xmp written");
                }
            }
        }
        catch (MetadataException refusal) { manifest.Add($"{name}\tto-xmp\trefused {refusal.Code}\t{Flat(refusal.Message)}"); Count("to-xmp refused " + refusal.Code); }
        catch (Exception defect) when (defect is not IOException and not UnauthorizedAccessException)
        {
            crashes.Add($"{name} to-xmp: {defect.GetType().Name}: {defect.Message}");
        }

        string sidecar = Path.Combine(directory, name + ".xmp");
        // The tool leaves an empty sidecar for a file it found nothing in.
        if (!File.Exists(sidecar) || new FileInfo(sidecar).Length == 0) continue;
        // Or a packet wrapper with no RDF in it: that is no XMP either, and the reader is right to refuse it.
        string held = File.ReadAllText(sidecar);
        if (!held.Contains("<rdf:RDF", StringComparison.Ordinal) && !held.Contains(":RDF ", StringComparison.Ordinal) && !held.Contains(":RDF>", StringComparison.Ordinal))
        {
            Count("to-exif sidecar without RDF");
            sample ??= $"{name}.xmp, {held.Length} characters: {Flat(held.Length > 160 ? held[..160] : held)}";
            continue;
        }
        try
        {
            XmpPacket theirs = XmpPacket.Load(sidecar);
            MetadataSharp.Core.Sync.SyncEdits back = MetadataSharp.Core.Sync.MetadataSync.XmpToExif(theirs);
            using JpegMetadataDocument target = JpegMetadataDocument.Open(new MemoryStream(blank));
            target.SaveAs(Path.Combine(directory, name + ".back.jpg"), back.Exif);
            manifest.Add($"{name}\tto-exif\twritten\t{NotesOf(back)}"); Count("to-exif written");
        }
        catch (MetadataException refusal) { manifest.Add($"{name}\tto-exif\trefused {refusal.Code}\t{Flat(refusal.Message)}"); Count("to-exif refused " + refusal.Code); }
        catch (Exception defect) when (defect is not IOException and not UnauthorizedAccessException)
        {
            crashes.Add($"{name} to-exif: {defect.GetType().Name}: {defect.Message}");
        }
    }
    File.WriteAllLines(Path.Combine(directory, "ours.tsv"), manifest);
    Console.WriteLine("::notice title=Sync parity fixture::" + string.Join("; ", outcomes.Select(pair => pair.Key + " " + pair.Value)));
    if (sample is not null) Console.WriteLine("::notice title=Sync parity fixture - a sidecar without RDF::" + sample.Replace("%", "%25"));
    foreach (string crash in crashes.Take(10)) Console.WriteLine("::error title=Sync conversion defect::" + crash.Replace("%", "%25").Replace("\r", " ").Replace("\n", " "));
    return crashes.Count == 0 ? 0 : 1;
}
else if (args.Length == 2 && args[0] == "makernote-parity")
{
    // Edits of MakerNote tags on the files the comparer (tools/reference/makernote_parity.py) prepared
    // in the directory: for every sNNNNN.EXT, up to three text tags and two SHORT tags of the note are
    // set, each in a copy of its own, NAME.N.ours.EXT. A text is set to one of its own length ("same")
    // and to a longer one ("grow"); a number to another number ("short"). Nothing is compared here.
    // ours.tsv lists NAME, N, the operation, the key, the status ("written" or "refused CODE") and
    // the value the tool has to print for the key (for a refusal, its message). An exception that is
    // no refusal is a defect.
    string directory = args[1];
    var manifest = new List<string>();
    var crashes = new List<string>();
    var outcomes = new SortedDictionary<string, int>(StringComparer.Ordinal);
    string Flat(string text) => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    foreach (string path in Directory.EnumerateFiles(directory, "s*.*").Order(StringComparer.Ordinal))
    {
        string name = Path.GetFileNameWithoutExtension(path), extension = Path.GetExtension(path).TrimStart('.');
        if (name.Contains('.') || extension is "tsv" or "txt") continue;
        byte[] source = File.ReadAllBytes(path);
        MakerNote? note;
        try { note = MetadataSharp.Formats.ImageMetadata.Read(source).Exif?.MakerNote; }
        catch (MetadataException) { note = null; }
        if (note is null)
        {
            manifest.Add($"{name}\t0\tnone\t\tno note\t");
            continue;
        }
        bool jpeg = extension is "jpg";
        int number = 0;
        void Run(string operation, string key, string text)
        {
            number++;
            string status;
            try
            {
                var edit = new ExifEdit();
                edit.SetTextByKey(key, text);
                var output = new MemoryStream();
                if (jpeg)
                {
                    using JpegMetadataDocument document = JpegMetadataDocument.Open(new MemoryStream(source));
                    document.Save(output, edit);
                }
                else
                {
                    using var document = MetadataSharp.Formats.Tiff.TiffBasedMetadataDocument.Open(new MemoryStream(source));
                    document.SaveExif(output, edit);
                }
                File.WriteAllBytes(Path.Combine(directory, $"{name}.{number}.ours.{extension}"), output.ToArray());
                status = "written";
            }
            catch (MetadataException refusal) { status = "refused " + refusal.Code; text = refusal.Message; }
            catch (Exception defect) when (defect is not IOException and not UnauthorizedAccessException)
            {
                crashes.Add($"{name} {operation} {key}: {defect.GetType().Name}: {defect.Message}");
                return;
            }
            manifest.Add($"{name}\t{number}\t{operation}\t{key}\t{status}\t{Flat(text)}");
            string counted = $"{note.Format} {operation} {status}";
            outcomes[counted] = outcomes.GetValueOrDefault(counted) + 1;
        }

        int texts = 0, numbers = 0;
        foreach (MakerNoteEntry entry in note.Entries)
        {
            string key;
            try { key = note.GetKey(entry); }
            catch (ArgumentException) { continue; }
            if (entry.Value.FieldType == TiffFieldType.Ascii && entry.Value.Count is > 4 and < 200 && texts < 3)
            {
                texts++;
                int length = (int)entry.Value.Count - 1;
                Run("same", key, new string('P', length));
                Run("grow", key, new string('G', length + 15));
            }
            else if (entry.Value.FieldType == TiffFieldType.Short && entry.Value.Count == 1 && entry.Fields.Count == 0 && numbers < 2)
            {
                numbers++;
                byte[] raw = new byte[2];
                entry.Value.CopyRawTo(raw);
                int value = entry.Value.ByteOrder == Endianness.BigEndian ? (raw[0] << 8) | raw[1] : raw[0] | (raw[1] << 8);
                Run("short", key, (value ^ 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        if (number == 0) manifest.Add($"{name}\t0\tnone\t\tno tag to set\t");
    }

    File.WriteAllLines(Path.Combine(directory, "ours.tsv"), manifest);

    // Also copy one decoded source MakerNote into an authored JPEG through the public transactional
    // API. The comparer checks every MakerNote key/value that pinned Exiv2 prints on both files.
    string copiedName = string.Empty;
    var copyReasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
    foreach (string path in Directory.EnumerateFiles(directory, "s*.*").Order(StringComparer.Ordinal))
    {
        string name = Path.GetFileNameWithoutExtension(path);
        if (name.Contains('.')) continue;
        byte[] sourceBytes = File.ReadAllBytes(path);
        try
        {
            ImageMetadata sourceMetadata = ImageMetadata.Read(sourceBytes);
            if (sourceMetadata.Exif?.MakerNote is null) continue;
            MetadataSharp.Formats.Operations.MetadataCopyResult copy =
                MetadataSharp.Formats.Operations.MetadataOperations.CopyMetadata(sourceBytes, JpegFixture.Build(),
                    new MetadataSharp.Formats.Operations.MetadataCopyOptions
                    {
                        Families = MetadataSharp.Formats.Operations.MetadataCopyFamilies.Exif
                    });
            if (copy.WasRefused || ImageMetadata.Read(copy.DestinationBytes).Exif?.MakerNote is null)
            {
                foreach (MetadataSharp.Formats.Operations.MetadataCopyNote note in copy.Notes)
                {
                    string reason = $"{note.Reason}: {note.Message}".Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
                    copyReasons[reason] = copyReasons.GetValueOrDefault(reason) + 1;
                }
                continue;
            }
            File.WriteAllBytes(Path.Combine(directory, $"{name}.copy.ours.jpg"), copy.DestinationBytes);
            copiedName = name;
            break;
        }
        catch (MetadataException) { }
    }
    File.WriteAllText(Path.Combine(directory, "copy.tsv"), copiedName.Length != 0
        ? copiedName
        : "\n" + string.Join("\n", copyReasons.OrderByDescending(pair => pair.Value).Take(12)
            .Select(pair => $"{pair.Value}x {pair.Key}")));
    foreach (KeyValuePair<string, int> outcome in outcomes) Console.WriteLine($"{outcome.Key}: {outcome.Value}");
    foreach (string crash in crashes.Take(10)) Console.WriteLine("::error title=MakerNote parity defect::" + crash.Replace("%", "%25").Replace("\r", " ").Replace("\n", " "));
    return crashes.Count == 0 ? 0 : 1;
}
else if (args.Length == 2 && args[0] == "crw-parity")
{
    // Edits of CRW files the comparer (tools/reference/crw_parity.py) prepared in the directory: for
    // every sNNNNN.crw each tag of the list below that the file has is set in a copy of its own,
    // NAME.N.ours.crw, and so is the comment. Nothing is compared here. ours.tsv lists NAME, N, the
    // operation, the key ("Comment" for the comment), the status ("written" or "refused CODE") and
    // the value the tool has to print (for a refusal, its message). An exception that is no refusal
    // is a defect.
    string directory = args[1];
    var manifest = new List<string>();
    var crashes = new List<string>();
    var outcomes = new SortedDictionary<string, int>(StringComparer.Ordinal);
    string Flat(string text) => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    foreach (string path in Directory.EnumerateFiles(directory, "s*.crw").Order(StringComparer.Ordinal))
    {
        string name = Path.GetFileNameWithoutExtension(path);
        if (name.Contains('.')) continue;
        byte[] source = File.ReadAllBytes(path);
        MetadataSharp.Formats.Crw.CrwMetadata? read;
        try { read = MetadataSharp.Formats.Crw.CrwMetadata.Read(source); }
        catch (MetadataException) { read = null; }
        if (read is null)
        {
            manifest.Add($"{name}\t0\tnone\t\tnot read\t");
            continue;
        }
        MetadataSharp.Formats.Crw.CrwMetadata metadata = read;
        int number = 0;
        void Run(string operation, string key, string text, Action<MetadataSharp.Formats.Crw.CrwMetadataDocument, Stream> save)
        {
            number++;
            string status;
            try
            {
                var output = new MemoryStream();
                using (var document = MetadataSharp.Formats.Crw.CrwMetadataDocument.Open(new MemoryStream(source)))
                    save(document, output);
                File.WriteAllBytes(Path.Combine(directory, $"{name}.{number}.ours.crw"), output.ToArray());
                status = "written";
            }
            catch (MetadataException refusal) { status = "refused " + refusal.Code; text = refusal.Message; }
            catch (Exception defect) when (defect is not IOException and not UnauthorizedAccessException)
            {
                crashes.Add($"{name} {operation} {key}: {defect.GetType().Name}: {defect.Message}");
                return;
            }
            manifest.Add($"{name}\t{number}\t{operation}\t{key}\t{status}\t{Flat(text)}");
            string counted = $"{operation} {status}";
            outcomes[counted] = outcomes.GetValueOrDefault(counted) + 1;
        }
        void Tag(string operation, string key, string text, Action<ExifEdit> change) =>
            Run(operation, key, text, (document, output) =>
            {
                var edit = new ExifEdit();
                change(edit);
                document.SaveExif(output, edit);
            });
        bool Has(ExifDirectoryKind kind, ushort tag) => metadata.Exif is not null && metadata.Exif.TryGetEntry(new ExifTagKey(kind, tag), out _);
        bool Canon(ushort tag) => metadata.Exif?.MakerNote is MakerNote canon && canon.TryGetEntry(tag, out _);

        if (Has(ExifDirectoryKind.Ifd0, 0x010F) && Has(ExifDirectoryKind.Ifd0, 0x0110))
        {
            Tag("exif", "Exif.Image.Make", "Parity Make", edit => edit.Set(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x010F), "Parity Make"));
            Tag("exif", "Exif.Image.Model", "Parity Model 40", edit => edit.Set(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0110), "Parity Model 40"));
        }
        if (Has(ExifDirectoryKind.Ifd0, 0x0112))
            Tag("exif", "Exif.Image.Orientation", "6", edit => edit.Set(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0112), (ushort)6));
        if (Has(ExifDirectoryKind.Exif, 0xA002))
            Tag("exif", "Exif.Photo.PixelXDimension", "1234", edit => edit.Set(new ExifTagKey(ExifDirectoryKind.Exif, 0xA002), 1234u));
        if (Has(ExifDirectoryKind.Exif, 0x9003))
            Tag("exif", "Exif.Photo.DateTimeOriginal", "2011:12:13 14:15:16", edit => edit.Set(new ExifTagKey(ExifDirectoryKind.Exif, 0x9003), "2011:12:13 14:15:16"));
        if (Canon(0x0009))
        {
            Tag("canon", "Exif.Canon.OwnerName", "Owner", edit => edit.SetTextByKey("Exif.Canon.OwnerName", "Owner"));
            Tag("canon", "Exif.Canon.OwnerName", "A Parity Owner With A Longer Name", edit => edit.SetTextByKey("Exif.Canon.OwnerName", "A Parity Owner With A Longer Name"));
        }
        if (Canon(0x0008))
            Tag("canon", "Exif.Canon.FileNumber", "1234567", edit => edit.SetTextByKey("Exif.Canon.FileNumber", "1234567"));
        if (Canon(0x0001))
            Tag("canon", "Exif.CanonCs.Macro", "1", edit => edit.SetTextByKey("Exif.CanonCs.Macro", "1"));
        Run("comment", "Comment", "a parity comment", (document, output) => document.SaveComment(output, "a parity comment"));
    }

    File.WriteAllLines(Path.Combine(directory, "ours.tsv"), manifest);

    // Also copy one decoded source MakerNote into an authored JPEG through the public transactional
    // API. The comparer checks every MakerNote key/value that pinned Exiv2 prints on both files.
    string copiedName = string.Empty;
    foreach (string path in Directory.EnumerateFiles(directory, "s*.*").Order(StringComparer.Ordinal))
    {
        string name = Path.GetFileNameWithoutExtension(path), extension = Path.GetExtension(path).TrimStart('.');
        if (name.Contains('.') || extension is "tsv" or "txt") continue;
        byte[] sourceBytes = File.ReadAllBytes(path);
        try
        {
            ImageMetadata sourceMetadata = ImageMetadata.Read(sourceBytes);
            if (sourceMetadata.Exif?.MakerNote is null) continue;
            MetadataSharp.Formats.Operations.MetadataCopyResult copy =
                MetadataSharp.Formats.Operations.MetadataOperations.CopyMetadata(sourceBytes, JpegFixture.Build(),
                    new MetadataSharp.Formats.Operations.MetadataCopyOptions
                    {
                        Families = MetadataSharp.Formats.Operations.MetadataCopyFamilies.Exif
                    });
            if (copy.WasRefused || ImageMetadata.Read(copy.DestinationBytes).Exif?.MakerNote is null) continue;
            File.WriteAllBytes(Path.Combine(directory, $"{name}.copy.ours.jpg"), copy.DestinationBytes);
            copiedName = name;
            break;
        }
        catch (MetadataException) { }
    }
    File.WriteAllText(Path.Combine(directory, "copy.tsv"), copiedName);
    foreach (KeyValuePair<string, int> outcome in outcomes) Console.WriteLine($"{outcome.Key}: {outcome.Value}");
    foreach (string crash in crashes.Take(10)) Console.WriteLine("::error title=CRW parity defect::" + crash.Replace("%", "%25").Replace("\r", " ").Replace("\n", " "));
    return crashes.Count == 0 ? 0 : 1;
}
else if (args.Length == 2 && args[0] == "operations-parity-blank")
{
    // A JPEG file without metadata, for the comparer to insert into, and to insert as a thumbnail.
    File.WriteAllBytes(args[1], JpegFixture.Build());
}
else if (args.Length == 2 && args[0] == "operations-parity")
{
    // The operations of this library on the files the comparer (tools/reference/operations_parity.py)
    // prepared in the directory: for every NAME.jpg, NAME.OPERATION.ours.jpg (or .exv) is what the
    // operation makes of it. Nothing is compared here. ours.tsv lists NAME, OPERATION, the status
    // ("written", "unchanged" for an operation with nothing to do, in which case the file is a copy,
    // or "refused CODE") and a text. An exception that is no refusal is a defect and fails the run.
    string directory = args[1];
    var manifest = new List<string>();
    var crashes = new List<string>();
    var outcomes = new SortedDictionary<string, int>(StringComparer.Ordinal);
    string Flat(string text) => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    byte[] blank = File.ReadAllBytes(Path.Combine(directory, "blank.jpg"));
    string script = File.ReadAllText(Path.Combine(directory, "modify.txt"));
    string format = File.ReadAllText(Path.Combine(directory, "rename.txt")).Trim();
    var shift = new MetadataSharp.Core.Operations.TimestampShift(1, 1, 2, new TimeSpan(1, 30, 15));

    static byte[] SaveExif(byte[] file, ExifEdit edit)
    {
        using JpegMetadataDocument document = JpegMetadataDocument.Open(new MemoryStream(file));
        var output = new MemoryStream();
        document.Save(output, edit);
        return output.ToArray();
    }

    foreach (string path in Directory.EnumerateFiles(directory, "s*.jpg").Order(StringComparer.Ordinal))
    {
        string name = Path.GetFileNameWithoutExtension(path);
        if (name.Contains('.')) continue;
        byte[] source = File.ReadAllBytes(path);
        void Run(string operation, string extension, Func<(byte[]? Bytes, string Text)> make)
        {
            string status, text = "";
            try
            {
                (byte[]? bytes, text) = make();
                if (extension.Length != 0) File.WriteAllBytes(Path.Combine(directory, $"{name}.{operation}.ours.{extension}"), bytes ?? source);
                status = bytes is null && extension.Length != 0 ? "unchanged" : "written";
            }
            catch (MetadataException refusal) { status = "refused " + refusal.Code; text = refusal.Message; }
            catch (MetadataSharp.Core.Operations.ModifyScriptException refusal) { status = "refused script"; text = refusal.Message; }
            catch (Exception defect) when (defect is not IOException and not UnauthorizedAccessException)
            {
                crashes.Add($"{name} {operation}: {defect.GetType().Name}: {defect.Message}");
                return;
            }
            manifest.Add($"{name}\t{operation}\t{status}\t{Flat(text)}");
            string counted = operation + " " + status;
            outcomes[counted] = outcomes.GetValueOrDefault(counted) + 1;
        }
        MetadataSharp.Formats.ImageMetadata Read()
        {
            MetadataSharp.Formats.ImageMetadata read = MetadataSharp.Formats.ImageMetadata.Read(source);
            if (read.Errors.Count != 0) throw read.Errors[0].Exception;
            return read;
        }

        Run("adjust", "jpg", () =>
        {
            MetadataSharp.Formats.ImageMetadata read = Read();
            MetadataSharp.Core.Sync.SyncEdits edits = MetadataSharp.Core.Operations.MetadataTimestamps.Adjust(read.Exif, read.Iptc, read.Xmp, shift);
            string notes = string.Join(" | ", edits.Notes.Select(note => $"{note.Source}: {note.Reason}"));
            if (edits.IsEmpty) return (null, notes);
            using JpegMetadataDocument document = JpegMetadataDocument.Open(new MemoryStream(source));
            var output = new MemoryStream();
            document.SaveAll(output, edits);
            return (output.ToArray(), notes);
        });
        Run("modify", "jpg", () =>
        {
            MetadataSharp.Formats.ImageMetadata read = Read();
            var parsed = MetadataSharp.Core.Operations.ModifyScript.Parse(script, read.Exif?.ByteOrder ?? Endianness.LittleEndian);
            byte[] bytes = source;
            if (!parsed.Exif.IsEmpty) bytes = SaveExif(bytes, parsed.Exif);
            if (!parsed.Iptc.IsEmpty)
            {
                using JpegMetadataDocument document = JpegMetadataDocument.Open(new MemoryStream(bytes));
                var output = new MemoryStream();
                document.SaveIptc(output, parsed.Iptc);
                bytes = output.ToArray();
            }
            if (!parsed.Xmp.IsEmpty)
            {
                using JpegMetadataDocument document = JpegMetadataDocument.Open(new MemoryStream(bytes));
                var output = new MemoryStream();
                document.SaveXmp(output, parsed.Xmp);
                bytes = output.ToArray();
            }
            return (bytes, "");
        });
        Run("delete-a", "jpg", () => (MetadataSharp.Formats.Operations.MetadataOperations.Delete(source), ""));
        Run("delete-t", "jpg", () => (MetadataSharp.Formats.Operations.MetadataOperations.Delete(source, MetadataSharp.Formats.Operations.MetadataFamilies.Thumbnail), ""));
        Run("delete-xc", "jpg", () => (MetadataSharp.Formats.Operations.MetadataOperations.Delete(source,
            MetadataSharp.Formats.Operations.MetadataFamilies.Xmp | MetadataSharp.Formats.Operations.MetadataFamilies.Comment), ""));
        Run("extract", "exv", () => (MetadataSharp.Formats.Operations.MetadataOperations.ExtractExv(source), ""));
        string theirs = Path.Combine(directory, name + ".insert.tool.exv");
        if (File.Exists(theirs))
        {
            Run("insert", "jpg", () =>
            {
                byte[] made = MetadataSharp.Formats.Operations.MetadataOperations.InsertExv(blank, File.ReadAllBytes(theirs),
                    MetadataSharp.Formats.Operations.MetadataFamilies.All, out IReadOnlyList<string> notes);
                return (made, string.Join(" | ", notes.Select(note => note.StartsWith("Exif.", StringComparison.Ordinal) ? "a tag is not carried: " + note[(note.IndexOf(':') + 1)..] : note).Distinct()));
            });
        }
        Run("thumbnail", "jpg", () =>
        {
            var edit = new ExifEdit();
            edit.SetThumbnail(blank);
            return (SaveExif(source, edit), "");
        });
        Run("fixiso", "jpg", () =>
        {
            ExifMetadata? exif = Read().Exif;
            ExifEdit? fix = exif is null ? null : MetadataSharp.Formats.Operations.MetadataOperations.FixIso(exif);
            return (fix is null ? null : SaveExif(source, fix), "");
        });
        Run("fixcom", "jpg", () =>
        {
            ExifMetadata? exif = Read().Exif;
            ExifEdit? fix = exif is null ? null : MetadataSharp.Formats.Operations.MetadataOperations.FixUserComment(exif, Encoding.Latin1);
            return (fix is null ? null : SaveExif(source, fix), "");
        });
        Run("rename", "", () =>
        {
            DateTime? taken = Read().Exif?.DateTaken;
            if (taken is null) return (null, ""); // no name: the comparer counts the file as one without a date
            return (null, MetadataSharp.Core.Operations.MetadataFileNames.Format(format, taken.Value, path));
        });
    }
    File.WriteAllLines(Path.Combine(directory, "ours.tsv"), manifest);
    Console.WriteLine("::notice title=Operations parity fixture::" + string.Join("; ", outcomes.Select(pair => pair.Key + " " + pair.Value)));
    foreach (string crash in crashes.Take(10)) Console.WriteLine("::error title=Operation defect::" + crash.Replace("%", "%25").Replace("\r", " ").Replace("\n", " "));
    return crashes.Count == 0 ? 0 : 1;
}
else { Console.Error.WriteLine("Usage: generate DIRECTORY | tiff-based-edits DIRECTORY | tiff-based-corpus CORPUS DIRECTORY | bmff-edits DIRECTORY | bmff-corpus CORPUS DIRECTORY | jpeg-embedded-edits DIRECTORY | jpeg-embedded-corpus CORPUS DIRECTORY | png-webp-embedded-edits DIRECTORY | png-webp-embedded-corpus CORPUS DIRECTORY | tiff-based-icc-edits DIRECTORY | tiff-based-icc-corpus CORPUS DIRECTORY | bmff-icc-edits DIRECTORY | bmff-icc-corpus CORPUS DIRECTORY | sync-parity DIRECTORY | operations-parity-blank FILE | operations-parity DIRECTORY | inspect[-xmp|-iptc] FILE | noop[-xmp|-iptc] INPUT OUTPUT | rewrite-iptc INPUT OUTPUT"); return 2; }
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

// Every file of a kind under a directory is given a profile and has its profile taken out, each
// time from the file as it is and with the options it was opened with. A file that does not open
// is counted by the code of its refusal, and so is an edit that is refused; an edit that changes
// nothing is counted; a file that was written goes into manifest.tsv for the pinned tool, with
// the profile it has to print. An exception that is no refusal is a defect of the writer and
// fails the run.
static int IccCorpus(string corpus, string directory, string kind, string[] extensions,
    Func<string, (IDisposable Document, Action<Stream, byte[]?> Save)> open)
{
    Directory.CreateDirectory(directory);
    var manifest = new List<string>();
    var outcomes = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var refusals = new List<(string File, string Edit, string Reason)>();
    var crashes = new List<string>();
    byte[] profile = IccProfile(70_001, 7);
    File.WriteAllBytes(Path.Combine(directory, "given.icc"), profile);
    int files = 0, number = 0;
    void Count(string outcome) => outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
    foreach (string path in Directory.EnumerateFiles(corpus, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (!extensions.Contains(extension) || new FileInfo(path).Length > 64 * 1024 * 1024) continue;
        byte[] bytes = File.ReadAllBytes(path);
        (IDisposable Document, Action<Stream, byte[]?> Save) opened;
        try { opened = open(path); }
        catch (MetadataException refusal) { Count("not opened " + refusal.Code); continue; } // damaged, or not such a file at all
        files++;
        using (opened.Document)
        {
            void Try(string edit, string expected, byte[]? given)
            {
                try
                {
                    using var output = new MemoryStream();
                    opened.Save(output, given);
                    byte[] written = output.ToArray();
                    if (written.AsSpan().SequenceEqual(bytes)) { Count(edit + " nothing to change"); return; }
                    string target = $"i{++number:D5}-{edit}{extension}";
                    File.WriteAllBytes(Path.Combine(directory, target), written);
                    manifest.Add(string.Join('\t', Path.GetFullPath(path), target, expected, "="));
                    Count(edit + " written");
                }
                catch (MetadataException refusal)
                {
                    Count(edit + " refused " + refusal.Code);
                    refusals.Add((Path.GetFileName(path), edit, $"{refusal.Code}: {refusal.Message}"));
                }
                catch (Exception defect) when (defect is not IOException and not UnauthorizedAccessException)
                {
                    crashes.Add($"{Path.GetFileName(path)} {edit}: {defect.GetType().Name}: {defect.Message}");
                }
            }

            Try("icc", "given.icc", profile);
            Try("no-icc", "-", null);
        }
    }
    File.WriteAllLines(Path.Combine(directory, "manifest.tsv"), manifest);
    Console.WriteLine($"::notice title={kind} corpus profile edits::{files} {kind} files opened; "
        + string.Join("; ", outcomes.Select(pair => pair.Key + " " + pair.Value)));
    string[] reasons = refusals.GroupBy(refusal => (refusal.File, refusal.Reason))
        .Select(group => $"{group.Key.File} [{string.Join(' ', group.Select(refusal => refusal.Edit))}]: {group.Key.Reason}"
            .Replace("%", "%25").Replace("\r", " ").Replace("\n", " "))
        .ToArray();
    foreach (string[] part in reasons.Chunk(20))
        Console.WriteLine($"::notice title={kind} corpus profile refusals::" + string.Join("%0A", part));
    foreach (string crash in crashes.Take(10)) Console.WriteLine($"::error title={kind} profile writer defect::" + crash);
    return crashes.Count == 0 ? 0 : 1;
}
