using System.Buffers.Binary;
using System.Globalization;
using MetadataSharp.Core;
using MetadataSharp.Formats.Bmff;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Formats.Png;
using MetadataSharp.Formats.Tiff;
using MetadataSharp.Formats.WebP;
using MetadataSharp.Tests.Bmff;
using MetadataSharp.Tests.Jpeg;
using Xunit;

namespace MetadataSharp.Tests.Tiff;

/// <summary>
/// An <see cref="ExifEdit"/> of any tag, through each writer that takes one: JPEG, PNG, WebP, a
/// TIFF-based file and a BMFF file, in both byte orders.
/// </summary>
/// <remarks>
/// Every source block is authored, offset by offset, by <see cref="ExifBlockFixture"/>: the tests know
/// where the MakerNote, the thumbnail and each value lie and look for them there in the output.
/// The expected content of a value in a big-endian file is computed here as well.
/// </remarks>
public sealed class ExifAnyTagWriteTests
{
    private static readonly ExifDirectoryKind[] Directories = Enum.GetValues<ExifDirectoryKind>();
    private static readonly TiffFieldType[] Types = Enum.GetValues<TiffFieldType>();
    private static readonly byte[] Note = ExifBlockFixture.Note;
    private static readonly byte[] Picture = ExifBlockFixture.Picture;

    /// <summary>Every writer with a block to edit, in both byte orders.</summary>
    public static IEnumerable<object[]> Writers() =>
        from writer in new[] { "jpeg", "png", "webp", "tiff", "heic" }
        from big in new[] { false, true }
        select new object[] { writer, big };

    /// <summary>The writers that give a file without a block a new one.</summary>
    public static IEnumerable<object[]> Creators() =>
        new[] { "jpeg", "png", "webp", "heic" }.Select(writer => new object[] { writer });

    // ---- the tests ----

    [Theory]
    [MemberData(nameof(Writers))]
    public void EveryFieldTypeIsWrittenToEveryDirectoryAndTheSourceStaysWhereItIs(string writer, bool big)
    {
        ExifBlockFixture source = ExifBlockFixture.Rich(big);
        byte[] file = Wrap(writer, source.Bytes, big);
        SortedDictionary<string, string> expected = Values(Read(writer, file));
        var set = new ExifEdit();
        var remove = new ExifEdit();
        foreach (ExifDirectoryKind directory in Directories)
        {
            foreach (TiffFieldType type in Types)
            {
                // One element lies in the entry; three do not, for every type wider than a byte.
                foreach (uint count in new uint[] { 1, 3 })
                {
                    var key = new ExifTagKey(directory, (ushort)(0xF100 + 0x20 * count + (int)type));
                    Assert.Null(ExifTags.Find(key));
                    byte[] little = Pattern((int)count * Width(type), (int)type * 16 + (int)directory);
                    set.Set(key, ExifValue.FromRaw(type, count, Endianness.LittleEndian, little));
                    remove.Remove(key);
                    expected[Name(key)] = Describe(type, count, big ? Swap(type, little) : little);
                }
            }
        }

        byte[] saved = Save(writer, file, set);
        Assert.Equal(Text(expected), Text(Values(Read(writer, saved))));
        // Each directory gained entries, so each was appended as a copy. Nothing of the source moved:
        // but for the header, which names the copy of the first directory, the source is in the output.
        byte[] block = Unwrap(writer, saved);
        OnlyChanged(source.Bytes, block, (4, 4));
        Assert.Equal(Note, block[source.ValueAt["exif", 0x927C]..][..Note.Length]);
        Assert.Equal(Picture, block[source.BlobAt["thumb"]..][..Picture.Length]);

        // Taking the tags out again leaves what the source said, and the same bytes where they were.
        byte[] restored = Save(writer, saved, remove);
        Assert.Equal(Text(Values(Read(writer, file))), Text(Values(Read(writer, restored))));
        OnlyChanged(source.Bytes, Unwrap(writer, restored), (4, 4));
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public void AValueThatFitsIsWrittenWhereTheOldOneIsAndNoOtherByteChanges(string writer, bool big)
    {
        ExifBlockFixture source = ExifBlockFixture.Rich(big);
        byte[] file = Wrap(writer, source.Bytes, big);
        var edit = new ExifEdit();
        edit.Set(new(ExifDirectoryKind.Ifd0, 0x0112), (ushort)6);                 // in its entry
        edit.Set(new(ExifDirectoryKind.Ifd0, 0x010F), "Shorter");                 // over the old text
        edit.Set(new(ExifDirectoryKind.Exif, 0x9003), "2031:12:30 23:59:58");     // the same length
        edit.Set(new(ExifDirectoryKind.Gps, 0x0001), "S");
        edit.Set(new(ExifDirectoryKind.Interop, 0x0001), "THM");
        edit.Set(new(ExifDirectoryKind.Thumbnail, 0x0103), (ushort)6);            // what the file says already

        byte[] block = Unwrap(writer, Save(writer, file, edit));
        Assert.Equal(source.Bytes.Length, block.Length);
        OnlyChanged(source.Bytes, block,
            (source.EntryAt["ifd0", 0x0112] + 8, 2),
            (source.EntryAt["ifd0", 0x010F] + 4, 4), (source.ValueAt["ifd0", 0x010F], 8),
            (source.ValueAt["exif", 0x9003], 20),
            (source.EntryAt["gps", 0x0001] + 8, 2),
            (source.EntryAt["interop", 0x0001] + 8, 4));
        Assert.Equal("Shorter\0"u8.ToArray(), block[source.ValueAt["ifd0", 0x010F]..][..8]);
        Assert.Equal("2031:12:30 23:59:58\0"u8.ToArray(), block[source.ValueAt["exif", 0x9003]..][..20]);
        Assert.Equal((byte)'S', block[source.EntryAt["gps", 0x0001] + 8]);
        Assert.Equal("THM\0"u8.ToArray(), block[(source.EntryAt["interop", 0x0001] + 8)..][..4]);
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public void TheTypedOverloadsWriteTheirValuesInTheByteOrderOfTheFile(string writer, bool big)
    {
        byte[] file = Wrap(writer, ExifBlockFixture.Rich(big).Bytes, big);
        SortedDictionary<string, string> expected = Values(Read(writer, file));
        var edit = new ExifEdit();
        void Expect(ExifDirectoryKind directory, ushort tag, TiffFieldType type, uint count, byte[] little) =>
            expected[Name(new(directory, tag))] = Describe(type, count, big ? Swap(type, little) : little);

        edit.Set(new(ExifDirectoryKind.Ifd0, 0x013B), "Alice");
        Expect(ExifDirectoryKind.Ifd0, 0x013B, TiffFieldType.Ascii, 6, "Alice\0"u8.ToArray());
        edit.Set(new(ExifDirectoryKind.Ifd0, 0x0112), (ushort)8);
        Expect(ExifDirectoryKind.Ifd0, 0x0112, TiffFieldType.Short, 1, [8, 0]);
        edit.Set(new(ExifDirectoryKind.Exif, 0xA002), 70_000u);
        Expect(ExifDirectoryKind.Exif, 0xA002, TiffFieldType.Long, 1, [0x70, 0x11, 0x01, 0x00]);
        edit.Set(new(ExifDirectoryKind.Exif, 0x829A), new UnsignedRational(1, 250));
        Expect(ExifDirectoryKind.Exif, 0x829A, TiffFieldType.Rational, 1, [1, 0, 0, 0, 250, 0, 0, 0]);
        edit.Set(new(ExifDirectoryKind.Exif, 0x9204), new SignedRational(-1, 3));
        Expect(ExifDirectoryKind.Exif, 0x9204, TiffFieldType.SRational, 1, [0xFF, 0xFF, 0xFF, 0xFF, 3, 0, 0, 0]);
        edit.Set(new(ExifDirectoryKind.Exif, 0x8827), new ushort[] { 100, 0x1234 });
        Expect(ExifDirectoryKind.Exif, 0x8827, TiffFieldType.Short, 2, [100, 0, 0x34, 0x12]);
        edit.Set(new(ExifDirectoryKind.Gps, 0x0002),
            new UnsignedRational[] { new(42, 1), new(22, 1), new(3456, 100) });
        Expect(ExifDirectoryKind.Gps, 0x0002, TiffFieldType.Rational, 3,
            [42, 0, 0, 0, 1, 0, 0, 0, 22, 0, 0, 0, 1, 0, 0, 0, 0x80, 0x0D, 0, 0, 100, 0, 0, 0]);
        edit.Set(new(ExifDirectoryKind.Interop, 0xF101), new uint[] { 1, 0x01020304 });
        Expect(ExifDirectoryKind.Interop, 0xF101, TiffFieldType.Long, 2, [1, 0, 0, 0, 4, 3, 2, 1]);
        edit.Set(new(ExifDirectoryKind.Thumbnail, 0xF102), new SignedRational[] { new(-2, 7), new(5, -9) });
        Expect(ExifDirectoryKind.Thumbnail, 0xF102, TiffFieldType.SRational, 2,
            [0xFE, 0xFF, 0xFF, 0xFF, 7, 0, 0, 0, 5, 0, 0, 0, 0xF7, 0xFF, 0xFF, 0xFF]);

        Assert.Equal(Text(expected), Text(Values(Read(writer, Save(writer, file, edit)))));
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public void DirectoriesAreCreatedWithTheirPointersAndDroppedWithTheirLastTag(string writer, bool big)
    {
        ExifBlockFixture source = ExifBlockFixture.Plain(big);
        byte[] file = Wrap(writer, source.Bytes, big);
        var interop = new ExifTagKey(ExifDirectoryKind.Interop, 0x0001);
        var gps = new ExifTagKey(ExifDirectoryKind.Gps, 0x0001);
        var orientation = new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0112);

        // A tag of the Interoperability directory brings the Exif directory that points to it.
        var create = new ExifEdit();
        create.Set(interop, "R98");
        create.Set(gps, "N");
        byte[] created = Save(writer, file, create);
        Assert.Equal("Exif:A005 Gps:0001 Ifd0:0112 Ifd0:8769 Ifd0:8825 Interop:0001", Keys(Read(writer, created)));
        OnlyChanged(source.Bytes, Unwrap(writer, created), (4, 4));

        // The last tag of a directory takes the directory with it, and the directory above it when
        // that one held nothing else.
        var first = new ExifEdit();
        first.Remove(interop);
        byte[] withoutInterop = Save(writer, created, first);
        Assert.Equal("Gps:0001 Ifd0:0112 Ifd0:8825", Keys(Read(writer, withoutInterop)));
        var second = new ExifEdit();
        second.Remove(gps);
        byte[] plain = Save(writer, withoutInterop, second);
        Assert.Equal("Ifd0:0112", Keys(Read(writer, plain)));
        Assert.Equal(Text(Values(Read(writer, file))), Text(Values(Read(writer, plain))));

        // The block of a JPEG, PNG or WebP file may be left with no tag. A TIFF file whose first
        // directory is empty is no image, and the item of a BMFF file is held to the same.
        var last = new ExifEdit();
        last.Remove(orientation);
        if (writer is "tiff" or "heic")
            Assert.Equal(MetadataErrorCode.UnsupportedFeature, Code(() => Save(writer, plain, last)));
        else
            Assert.Equal("", Keys(Read(writer, Save(writer, plain, last))));
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public void TheDirectoryAfterThePrimaryOneIsEditedAndNeitherCreatedNorEmptied(string writer, bool big)
    {
        ExifBlockFixture source = ExifBlockFixture.Rich(big);
        byte[] file = Wrap(writer, source.Bytes, big);
        var orientation = new ExifTagKey(ExifDirectoryKind.Thumbnail, 0x0112);
        var unlisted = new ExifTagKey(ExifDirectoryKind.Thumbnail, 0xF155);
        var edit = new ExifEdit();
        edit.Set(orientation, (ushort)3);
        edit.Set(unlisted, 0xCAFEF00Du);
        byte[] saved = Save(writer, file, edit);
        SortedDictionary<string, string> expected = Values(Read(writer, file));
        expected[Name(orientation)] = Describe(TiffFieldType.Short, 1, big ? [0, 3] : [3, 0]);
        expected[Name(unlisted)] = Describe(TiffFieldType.Long, 1, big ? [0xCA, 0xFE, 0xF0, 0x0D] : [0x0D, 0xF0, 0xFE, 0xCA]);
        Assert.Equal(Text(expected), Text(Values(Read(writer, saved))));
        // The directory gained entries: its copy is appended and the link of the first directory names
        // the copy. The picture is where it was and the copy still addresses it there.
        byte[] block = Unwrap(writer, saved);
        OnlyChanged(source.Bytes, block, (source.NextAt["ifd0"], 4));
        Assert.Equal(Picture, block[source.BlobAt["thumb"]..][..Picture.Length]);
        if (writer == "jpeg")
        {
            using JpegMetadataDocument document = JpegMetadataDocument.Open(new MemoryStream(saved));
            Assert.Equal(Describe(TiffFieldType.Long, 1, Number(big, (uint)source.BlobAt["thumb"])),
                Values(document.Exif)[Name(new(ExifDirectoryKind.Thumbnail, 0x0201))]);
        }

        // A file without that directory is not given one by a tag.
        byte[] bare = Wrap(writer, ExifBlockFixture.Plain(big).Bytes, big);
        var orphan = new ExifEdit();
        orphan.Set(orientation, (ushort)3);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Code(() => Save(writer, bare, orphan)));
        // Removing a tag from a directory that is not there changes nothing.
        var nothing = new ExifEdit();
        nothing.Remove(orientation);
        Assert.Equal(bare, Save(writer, bare, nothing));

        // One that holds a single tag does not lose it: the link to an empty directory would stay.
        byte[] single = Wrap(writer, ExifBlockFixture.Build(big,
            new ExifBlockFixture.Ifd("ifd0", "ifd1", [ExifBlockFixture.Short(0x0112, 1)]),
            new ExifBlockFixture.Ifd("ifd1", null, [ExifBlockFixture.Short(0x0128, 2)])).Bytes, big);
        var emptied = new ExifEdit();
        emptied.Remove(new(ExifDirectoryKind.Thumbnail, 0x0128));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Code(() => Save(writer, single, emptied)));
    }

    [Theory]
    [MemberData(nameof(Creators))]
    public void AFileWithoutABlockGetsOneWithTheTagsThatWereSet(string writer)
    {
        byte[] file = Bare(writer);
        Assert.Null(Read(writer, file));
        var expected = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var edit = new ExifEdit();
        foreach (ExifDirectoryKind directory in Directories.Where(kind => kind != ExifDirectoryKind.Thumbnail))
        {
            foreach (TiffFieldType type in Types)
            {
                var key = new ExifTagKey(directory, (ushort)(0xF100 + (int)type));
                byte[] little = Pattern(3 * Width(type), (int)type);
                edit.Set(key, ExifValue.FromRaw(type, 3, Endianness.LittleEndian, little));
                expected[Name(key)] = Describe(type, 3, little);
            }
        }
        edit.Set(new(ExifDirectoryKind.Ifd0, 0x013B), "Alice");
        expected["Ifd0:013B"] = Describe(TiffFieldType.Ascii, 6, "Alice\0"u8.ToArray());
        edit.Set(new(ExifDirectoryKind.Exif, 0x829A), new UnsignedRational(1, 250));
        expected["Exif:829A"] = Describe(TiffFieldType.Rational, 1, [1, 0, 0, 0, 250, 0, 0, 0]);

        byte[] saved = Save(writer, file, edit);
        ExifMetadata? read = Read(writer, saved);
        Assert.Equal(Text(expected), Text(Values(read)));
        Assert.Equal(Endianness.LittleEndian, read!.ByteOrder);
        Assert.Equal(new byte[] { 0x49, 0x49, 42, 0, 8, 0, 0, 0 }, Unwrap(writer, saved)[..8]);
        foreach (string pointer in new[] { "Ifd0:8769", "Ifd0:8825", "Exif:A005" })
            Assert.Contains(pointer, Keys(read));

        // No thumbnail directory is laid out for a tag, and taking a tag from no block is no change.
        var thumbnail = new ExifEdit();
        thumbnail.Set(new(ExifDirectoryKind.Thumbnail, 0x0112), (ushort)1);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Code(() => Save(writer, file, thumbnail)));
        var remove = new ExifEdit();
        remove.Remove(new(ExifDirectoryKind.Exif, 0x9003));
        Assert.Equal(file, Save(writer, file, remove));
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public void ABlockLongerThanAJpegSegmentHoldsIsRefusedWhereTheContainerSetsThatLength(string writer, bool big)
    {
        ExifBlockFixture source = ExifBlockFixture.Rich(big);
        byte[] file = Wrap(writer, source.Bytes, big);
        var key = new ExifTagKey(ExifDirectoryKind.Exif, 0xF1F1);
        ExifEdit Undefined(int length)
        {
            var edit = new ExifEdit();
            edit.Set(key, ExifValue.FromRaw(TiffFieldType.Undefined, (uint)length, Endianness.LittleEndian, Pattern(length, 3)));
            return edit;
        }

        // The Exif directory gains an entry and is appended as a copy of 2 + 4 * 12 + 4 bytes, beside
        // the value; the pointer to it in the first directory is the one source byte run that changes.
        (int, int) pointer = (source.EntryAt["ifd0", 0x8769] + 8, 4);
        int room = 65_526 - source.Bytes.Length - 54;
        byte[] fits = Save(writer, file, Undefined(room));
        Assert.Equal(Describe(TiffFieldType.Undefined, (uint)room, Pattern(room, 3)), Values(Read(writer, fits))[Name(key)]);
        OnlyChanged(source.Bytes, Unwrap(writer, fits), pointer);
        if (writer is "jpeg" or "png" or "webp")
        {
            Assert.Equal(65_526, Unwrap(writer, fits).Length);
            Assert.Equal(MetadataErrorCode.ExifTooLarge, Code(() => Save(writer, file, Undefined(room + 2))));
            // A file that has no block is held to the same length when it is given one.
            Assert.Equal(MetadataErrorCode.ExifTooLarge, Code(() => Save(writer, Bare(writer), Undefined(65_527))));
        }
        else
        {
            byte[] longer = Save(writer, file, Undefined(70_000));
            Assert.Equal(Describe(TiffFieldType.Undefined, 70_000, Pattern(70_000, 3)), Values(Read(writer, longer))[Name(key)]);
            OnlyChanged(source.Bytes, Unwrap(writer, longer), pointer);
        }
    }

    // ---- the writers ----

    private static byte[] Wrap(string writer, byte[] block, bool big) => ExifBlockFixture.Wrap(writer, block, big);

    private static byte[] Bare(string writer) => ExifBlockFixture.Bare(writer);

    private static int Width(TiffFieldType type) => ExifBlockFixture.Width(type);

    private static byte[] Swap(TiffFieldType type, byte[] bytes) => ExifBlockFixture.Swap(type, bytes);

    private static byte[] Number(bool big, uint value) => ExifBlockFixture.Number(big, value);

    private static byte[] Save(string writer, byte[] file, ExifEdit edit)
    {
        using var input = new MemoryStream(file);
        using var output = new MemoryStream();
        switch (writer)
        {
            case "jpeg": { using var document = JpegMetadataDocument.Open(input); document.Save(output, edit); break; }
            case "png": { using var document = PngMetadataDocument.Open(input); document.SaveExif(output, edit); break; }
            case "webp": { using var document = WebPMetadataDocument.Open(input); document.SaveExif(output, edit); break; }
            case "tiff": { using var document = TiffBasedMetadataDocument.Open(input); document.SaveExif(output, edit); break; }
            default: { using var document = BmffMetadataDocument.Open(input); document.SaveExif(output, edit); break; }
        }
        return output.ToArray();
    }

    private static ExifMetadata? Read(string writer, byte[] file)
    {
        using var input = new MemoryStream(file);
        switch (writer)
        {
            case "jpeg": { using var document = JpegMetadataDocument.Open(input); return document.Exif; }
            case "png": { using var document = PngMetadataDocument.Open(input); return document.ReadExif(); }
            case "webp": { using var document = WebPMetadataDocument.Open(input); return document.ReadExif(); }
            case "tiff": { using var document = TiffBasedMetadataDocument.Open(input); return document.ReadExif(); }
            default: { using var document = BmffMetadataDocument.Open(input); return document.ReadExif(); }
        }
    }

    /// <summary>The TIFF structure a file holds, found by the layout of its container.</summary>
    private static byte[] Unwrap(string writer, byte[] file)
    {
        switch (writer)
        {
            case "jpeg":
                return JpegEditingTests.ExifBlock(file);
            case "png":
                for (int at = 8; at + 12 <= file.Length;)
                {
                    int length = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at));
                    if (file.AsSpan(at + 4, 4).SequenceEqual("eXIf"u8)) return file[(at + 8)..(at + 8 + length)];
                    at += 12 + length;
                }
                break;
            case "webp":
                for (int at = 12; at + 8 <= file.Length;)
                {
                    int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at + 4));
                    if (file.AsSpan(at, 4).SequenceEqual("EXIF"u8)) return file[(at + 8)..(at + 8 + length)];
                    at += 8 + length + (length & 1);
                }
                break;
            case "tiff":
                return file;
            default:
                uint id = BmffFixture.Declared(file).Single(item => item.Type == "Exif").Id;
                return BmffFixture.ItemBytes(file, id)[4..];
        }
        throw new InvalidOperationException("The file has no EXIF block.");
    }

    private static MetadataErrorCode Code(Action action) => Assert.Throws<MetadataException>(action).Code;

    // ---- what a reader returned, as text ----

    private static bool Pointer(ExifTagKey key) => key.TagId is 0x8769 or 0x8825 or 0xA005;

    private static string Name(ExifTagKey key) =>
        key.Directory + ":" + key.TagId.ToString("X4", CultureInfo.InvariantCulture);

    private static string Describe(TiffFieldType type, uint count, byte[] bytes) =>
        type + "*" + count.ToString(CultureInfo.InvariantCulture) + "=" + Convert.ToHexString(bytes);

    /// <summary>Every tag but the pointers, whose values are addresses an edit may change.</summary>
    private static SortedDictionary<string, string> Values(ExifMetadata? metadata)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (ExifEntry entry in metadata!.Entries)
            if (!Pointer(entry.Key))
                values.Add(Name(entry.Key), Describe(entry.Value.FieldType, entry.Value.Count, entry.Value.RawBytes.ToArray()));
        return values;
    }

    private static string Text(SortedDictionary<string, string> values) =>
        string.Join("\n", values.Select(pair => pair.Key + " " + pair.Value));

    /// <summary>The tags a reader returned, pointers among them, in the order of their names.</summary>
    private static string Keys(ExifMetadata? metadata) =>
        string.Join(' ', metadata!.Entries.Select(entry => Name(entry.Key)).OrderBy(name => name, StringComparer.Ordinal));

    /// <summary>
    /// The output begins with the source, byte for byte, but for the runs named: what an edit added
    /// lies after it.
    /// </summary>
    private static void OnlyChanged(byte[] source, byte[] block, params (int Start, int Length)[] allowed)
    {
        Assert.True(block.Length >= source.Length);
        for (int at = 0; at < source.Length; at++)
            if (block[at] != source[at])
                Assert.True(allowed.Any(range => at >= range.Start && at < range.Start + range.Length),
                    "Byte " + at.ToString(CultureInfo.InvariantCulture) + " of the source changed.");
    }

    private static byte[] Pattern(int length, int seed)
    {
        byte[] bytes = new byte[length];
        for (int i = 0; i < length; i++) bytes[i] = (byte)(seed + 1 + i * 7);
        return bytes;
    }
}
