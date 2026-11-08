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
using MetadataSharp.Tests.Tiff;
using MetadataSharp.Tests.WebP;

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
else { Console.Error.WriteLine("Usage: generate DIRECTORY | inspect[-xmp|-iptc] FILE | noop[-xmp|-iptc] INPUT OUTPUT | rewrite-iptc INPUT OUTPUT"); return 2; }
return 0;


static byte[] Resource(ushort id, byte[] data)
{
    byte[] result = new byte[12 + data.Length + (data.Length & 1)]; "8BIM"u8.CopyTo(result);
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(4), id);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(8), (uint)data.Length); data.CopyTo(result, 12); return result;
}

static string? RawText(ExifValue? value)
{
    if (value is null) return null; byte[] bytes = new byte[value.ByteLength]; value.CopyRawTo(bytes); return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
}
