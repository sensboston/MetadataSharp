using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Tests.Bmff;
using MetadataSharp.Tests.Jpeg;
using MetadataSharp.Tests.Png;
using MetadataSharp.Tests.WebP;

namespace MetadataSharp.Tests.Tiff;

/// <summary>
/// Authored EXIF blocks for the tests of an edit of any tag, and the files that hold them. Every
/// offset is laid out here and none comes from production code, so a test can say where an entry,
/// a value, the MakerNote or the thumbnail was and look for it there afterwards.
/// </summary>
internal sealed class ExifBlockFixture
{
    /// <summary>A field of a directory: its value in little-endian order, or what it addresses.</summary>
    internal sealed record Field(ushort Tag, TiffFieldType Type, byte[] Value, string? Target = null);

    /// <summary>A directory, and the name of the one its link names.</summary>
    internal sealed record Ifd(string Name, string? Next, Field[] Fields);

    /// <summary>Offsets by the name of a directory and a tag.</summary>
    internal sealed class Offsets
    {
        private readonly Dictionary<(string, ushort), int> map = [];
        internal int this[string directory, ushort tag]
        {
            get => map[(directory, tag)];
            set => map[(directory, tag)] = value;
        }
    }

    /// <summary>The MakerNote of <see cref="Rich"/>: sixteen bytes no maker wrote.</summary>
    internal static readonly byte[] Note = Enumerable.Range(0xA0, 16).Select(value => (byte)value).ToArray();
    /// <summary>The thumbnail of <see cref="Rich"/>: a JPEG stream of its two markers.</summary>
    internal static readonly byte[] Picture = [0xFF, 0xD8, 0xFF, 0xD9];

    /// <summary>The writers a block is tested through, by the container each one writes.</summary>
    internal static readonly string[] Containers = ["jpeg", "png", "webp", "tiff", "heic"];

    private ExifBlockFixture() { }

    internal byte[] Bytes { get; private set; } = [];
    /// <summary>Where the twelve bytes of an entry are.</summary>
    internal Offsets EntryAt { get; } = new();
    /// <summary>Where a value longer than four bytes is.</summary>
    internal Offsets ValueAt { get; } = new();
    /// <summary>Where a named run of bytes is.</summary>
    internal Dictionary<string, int> BlobAt { get; } = [];
    /// <summary>Where the link of a directory to its successor is.</summary>
    internal Dictionary<string, int> NextAt { get; } = [];

    internal static Field Short(ushort tag, ushort value) => new(tag, TiffFieldType.Short, [(byte)value, (byte)(value >> 8)]);
    internal static Field Ascii(ushort tag, string text) => new(tag, TiffFieldType.Ascii, Encoding.ASCII.GetBytes(text + "\0"));
    internal static Field Link(ushort tag, string target) => new(tag, TiffFieldType.Long, [0, 0, 0, 0], target);

    /// <summary>
    /// A block with all five directories. The first one at 8 (Make at 206, Orientation, the Exif
    /// and GPS pointers, and tag 0xF1E9 that no vocabulary lists, three LONG at 218), linked to the
    /// thumbnail directory at 74 (Compression 6, the JPEG stream at 266 of four bytes); the Exif
    /// directory at 116 (DateTimeOriginal at 230, a MakerNote of sixteen bytes at 250, the
    /// Interoperability pointer); the Interoperability directory at 158 (the index "R98"); the GPS
    /// directory at 176 (the version and the latitude reference); 270 bytes in all.
    /// </summary>
    internal static ExifBlockFixture Rich(bool big) => Build(big,
        [
            new Ifd("ifd0", "ifd1",
            [
                Ascii(0x010F, "TestCompany"), Short(0x0112, 1), Link(0x8769, "exif"), Link(0x8825, "gps"),
                new Field(0xF1E9, TiffFieldType.Long, [1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0])
            ]),
            new Ifd("ifd1", null,
            [
                Short(0x0103, 6), Link(0x0201, "thumb"), new Field(0x0202, TiffFieldType.Long, [4, 0, 0, 0])
            ]),
            new Ifd("exif", null,
            [
                Ascii(0x9003, "2020:01:02 03:04:05"), new Field(0x927C, TiffFieldType.Undefined, Note), Link(0xA005, "interop")
            ]),
            new Ifd("interop", null, [Ascii(0x0001, "R98")]),
            new Ifd("gps", null,
            [
                new Field(0x0000, TiffFieldType.Byte, [2, 3, 0, 0]), Ascii(0x0001, "N")
            ])
        ],
        ("thumb", Picture));

    /// <summary>A block of one directory with one tag, Orientation: 26 bytes.</summary>
    internal static ExifBlockFixture Plain(bool big) => Build(big, new Ifd("ifd0", null, [Short(0x0112, 1)]));

    internal static ExifBlockFixture Build(bool big, params Ifd[] directories) => Build(big, directories, []);

    /// <summary>
    /// Lays a block out: the header, the directories in the order given, the values longer than four
    /// bytes in the order of their entries, then the named byte runs. Every part starts on an even
    /// offset.
    /// </summary>
    internal static ExifBlockFixture Build(bool big, Ifd[] directories, params (string Name, byte[] Bytes)[] blobs)
    {
        var block = new ExifBlockFixture();
        var directoryAt = new Dictionary<string, int>();
        int cursor = 8;
        foreach (Ifd directory in directories)
        {
            directoryAt[directory.Name] = cursor;
            for (int i = 0; i < directory.Fields.Length; i++)
                block.EntryAt[directory.Name, directory.Fields[i].Tag] = cursor + 2 + 12 * i;
            cursor += 2 + 12 * directory.Fields.Length + 4;
        }
        foreach (Ifd directory in directories)
            foreach (Field field in directory.Fields)
                if (field.Target is null && field.Value.Length > 4)
                {
                    block.ValueAt[directory.Name, field.Tag] = cursor;
                    cursor += field.Value.Length + (field.Value.Length & 1);
                }
        foreach ((string name, byte[] bytes) in blobs)
        {
            block.BlobAt[name] = cursor;
            cursor += bytes.Length + (bytes.Length & 1);
        }

        byte[] data = new byte[cursor];
        void Put16(int at, int value)
        {
            if (big) BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(at), (ushort)value);
            else BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at), (ushort)value);
        }
        void Put32(int at, int value) => Number(big, (uint)value).CopyTo(data, at);

        data[0] = data[1] = big ? (byte)0x4D : (byte)0x49;
        Put16(2, 42);
        Put32(4, 8);
        foreach (Ifd directory in directories)
        {
            int at = directoryAt[directory.Name];
            Put16(at, directory.Fields.Length);
            foreach (Field field in directory.Fields)
            {
                int entry = block.EntryAt[directory.Name, field.Tag];
                byte[] value = big ? Swap(field.Type, field.Value) : field.Value;
                Put16(entry, field.Tag);
                Put16(entry + 2, (int)field.Type);
                Put32(entry + 4, field.Value.Length / Width(field.Type));
                if (field.Target is string target)
                    Put32(entry + 8, directoryAt.TryGetValue(target, out int there) ? there : block.BlobAt[target]);
                else if (value.Length <= 4)
                    value.CopyTo(data, entry + 8);
                else
                {
                    Put32(entry + 8, block.ValueAt[directory.Name, field.Tag]);
                    value.CopyTo(data, block.ValueAt[directory.Name, field.Tag]);
                }
            }
            block.NextAt[directory.Name] = at + 2 + 12 * directory.Fields.Length;
            Put32(block.NextAt[directory.Name], directory.Next is null ? 0 : directoryAt[directory.Next]);
        }
        foreach ((string name, byte[] bytes) in blobs) bytes.CopyTo(data, block.BlobAt[name]);
        block.Bytes = data;
        return block;
    }

    /// <summary>
    /// The file of a container with the block in it: a JPEG APP1 segment, a PNG eXIf chunk, a WebP
    /// EXIF chunk, the Exif item of a HEIC file, or the block itself as a TIFF file.
    /// </summary>
    internal static byte[] Wrap(string container, byte[] block, bool big)
    {
        switch (container)
        {
            case "jpeg": return JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif(block)));
            case "png": return PngFixture.Build(PngFixture.Chunk("eXIf", block));
            case "webp": return WebPFixture.Extended(8, WebPFixture.Image(0)[12..], WebPFixture.Chunk("EXIF", block));
            case "tiff": return block;
            case "heic":
                BmffFixture.Layout layout = BmffFixture.LayoutOf("heic", big);
                int index = layout.Items.FindIndex(item => item.Id == BmffFixture.ExifId);
                layout.Items[index] = layout.Items[index] with { Data = [0, 0, 0, 0, .. block] };
                return BmffFixture.Build("heic", big, layout).Bytes;
            default: throw new ArgumentOutOfRangeException(nameof(container));
        }
    }

    /// <summary>The file of a container with no EXIF block; a TIFF file is never without one.</summary>
    internal static byte[] Bare(string container) => container switch
    {
        "jpeg" => JpegFixture.Build(),
        "png" => PngFixture.Build(),
        "webp" => WebPFixture.Image(0),
        "heic" => BmffFixture.Create("heic-bare", false).Bytes,
        _ => throw new ArgumentOutOfRangeException(nameof(container))
    };

    internal static int Width(TiffFieldType type) => type switch
    {
        TiffFieldType.Short or TiffFieldType.SShort => 2,
        TiffFieldType.Long or TiffFieldType.SLong or TiffFieldType.Float => 4,
        TiffFieldType.Rational or TiffFieldType.SRational or TiffFieldType.Double => 8,
        _ => 1
    };

    /// <summary>The same numbers in the other byte order: a rational is two numbers of four bytes.</summary>
    internal static byte[] Swap(TiffFieldType type, byte[] bytes)
    {
        int unit = type is TiffFieldType.Rational or TiffFieldType.SRational ? 4 : Width(type);
        byte[] swapped = (byte[])bytes.Clone();
        for (int at = 0; at + unit <= swapped.Length; at += unit) Array.Reverse(swapped, at, unit);
        return swapped;
    }

    internal static byte[] Number(bool big, uint value)
    {
        byte[] bytes = new byte[4];
        if (big) BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        else BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }
}
