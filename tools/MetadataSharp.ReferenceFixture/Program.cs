using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using MetadataSharp.Core;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Tests.Jpeg;

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
else if (args.Length == 3 && args[0] == "noop")
{
    using var doc = JpegMetadataDocument.Open(args[1]); doc.SaveAs(args[2], new ExifEdit());
}
else { Console.Error.WriteLine("Usage: generate DIRECTORY | inspect FILE | noop INPUT OUTPUT"); return 2; }
return 0;
