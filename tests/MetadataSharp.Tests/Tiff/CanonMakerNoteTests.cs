using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;
using Xunit;

namespace MetadataSharp.Tests.Tiff;

public sealed class CanonMakerNoteTests
{
    private const int MakerNoteStart = 62;

    /// <summary>
    /// IFD0 (Make, ExifIFD pointer) -> Exif IFD (MakerNote) -> a Canon MakerNote IFD with ImageType
    /// (external ASCII), ModelID (inline LONG), FocalLength (external SHORT[4]) and an unknown tag,
    /// followed by the 8-byte footer. <paramref name="shift"/> pretends the MakerNote was written at
    /// MakerNoteStart + shift: its internal offsets and footer say so, its bytes sit at MakerNoteStart.
    /// </summary>
    private static byte[] Build(bool big, string make = "Canon", int shift = 0, bool footer = true,
        ushort? entryCount = null, uint? imageTypeOffset = null)
    {
        var b = new byte[140];
        void P16(int at, int v) { if (big) BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(at), (ushort)v); else BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), (ushort)v); }
        void P32(int at, long v) { if (big) BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at), (uint)v); else BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), (uint)v); }
        void Entry(int at, int tag, int type, long count, long value) { P16(at, tag); P16(at + 2, type); P32(at + 4, count); P32(at + 8, value); }

        b[0] = b[1] = (byte)(big ? 'M' : 'I');
        P16(2, 42);
        P32(4, 8);
        // IFD0 at 8: Make, ExifIFD.
        P16(8, 2);
        Entry(10, 0x010F, 2, 6, 38);
        Entry(22, 0x8769, 4, 1, 44);
        P32(34, 0);
        Encoding.ASCII.GetBytes(make.PadRight(5)[..5]).CopyTo(b, 38);
        // Exif IFD at 44: MakerNote.
        P16(44, 1);
        Entry(46, 0x927C, 7, 140 - MakerNoteStart, MakerNoteStart);
        P32(58, 0);
        // Canon MakerNote at 62.
        int m = MakerNoteStart;
        P16(m, entryCount ?? 4);
        Entry(m + 2, 0x0002, 3, 4, 124 + shift);
        Entry(m + 14, 0x0006, 2, 8, imageTypeOffset ?? (uint)(116 + shift));
        Entry(m + 26, 0x0010, 4, 1, 0x80000175);
        Entry(m + 38, 0x7777, 3, 1, 9);
        P32(m + 50, 0);
        // Values after the directory (62..116): ImageType at 116, FocalLength at 124.
        Encoding.ASCII.GetBytes("EOS 20D\0").CopyTo(b, 116);
        P16(124, 2); P16(126, 95); P16(128, 907); P16(130, 605);
        if (footer)
        {
            b[132] = b[133] = (byte)(big ? 'M' : 'I');
            P16(134, 42);
            P32(136, MakerNoteStart + shift);
        }
        return b;
    }

    private static MakerNote Decode(byte[] tiff)
    {
        ExifMetadata metadata = ExifTiffReader.Read(tiff).Metadata;
        Assert.NotNull(metadata.MakerNote);
        return metadata.MakerNote;
    }

    private static string Ascii(ExifValue value)
    {
        byte[] bytes = new byte[value.ByteLength];
        value.CopyRawTo(bytes);
        return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACanonMakerNoteIsDecodedWithNamesAndOwnedValues(bool big)
    {
        byte[] tiff = Build(big);
        MakerNote note = Decode(tiff);
        Assert.Equal(MakerNoteFormat.Canon, note.Format);
        Assert.Equal(big ? Endianness.BigEndian : Endianness.LittleEndian, note.ByteOrder);
        Assert.Equal(new ushort[] { 0x0002, 0x0006, 0x0010, 0x7777 }, note.Entries.Select(e => e.TagId));

        Assert.True(note.TryGetEntry(0x0006, out MakerNoteEntry? imageType));
        Assert.Equal("ImageType", imageType.Name);
        Assert.Equal(TiffFieldType.Ascii, imageType.Value.FieldType);
        Assert.Equal("EOS 20D", Ascii(imageType.Value));

        Assert.True(note.TryGetEntry(0x0002, out MakerNoteEntry? focal));
        Assert.Equal("FocalLength", focal.Name);
        Assert.Equal(4u, focal.Value.Count);

        Assert.True(note.TryGetEntry(0x0010, out MakerNoteEntry? model));
        byte[] raw = new byte[4];
        model.Value.CopyRawTo(raw);
        Assert.Equal(0x80000175u, big ? BinaryPrimitives.ReadUInt32BigEndian(raw) : BinaryPrimitives.ReadUInt32LittleEndian(raw));

        tiff.AsSpan().Clear();
        Assert.Equal("EOS 20D", Ascii(imageType.Value));
    }

    [Fact]
    public void AnUnknownTagKeepsItsNumberAndHasNoName()
    {
        Assert.True(Decode(Build(false)).TryGetEntry(0x7777, out MakerNoteEntry? unknown));
        Assert.Null(unknown.Name);
        Assert.Equal(TiffFieldType.Short, unknown.Value.FieldType);
    }

    [Theory]
    [InlineData(false, 100)]
    [InlineData(true, 100)]
    [InlineData(false, -40)]
    public void TheFooterCorrectsOffsetsOfAMovedMakerNote(bool big, int shift)
    {
        MakerNote note = Decode(Build(big, shift: shift));
        Assert.True(note.TryGetEntry(0x0006, out MakerNoteEntry? imageType));
        Assert.Equal("EOS 20D", Ascii(imageType.Value));
    }

    [Fact]
    public void WithoutAFooterOffsetsAreTakenAsWritten()
    {
        MakerNote note = Decode(Build(false, footer: false));
        Assert.True(note.TryGetEntry(0x0006, out MakerNoteEntry? imageType));
        Assert.Equal("EOS 20D", Ascii(imageType.Value));
    }

    [Theory]
    [InlineData("Nikon")]
    [InlineData("canon")]
    public void AnotherMakeLeavesTheMakerNoteOpaque(string make)
    {
        ExifMetadata metadata = ExifTiffReader.Read(Build(false, make: make)).Metadata;
        Assert.Null(metadata.MakerNote);
        Assert.True(metadata.TryGetEntry(new(ExifDirectoryKind.Exif, 0x927C), out _));
    }

    [Theory]
    [InlineData(0)]          // An empty directory.
    [InlineData(5000)]       // More entries than the MakerNote can hold.
    public void AMalformedDirectoryLeavesTheMakerNoteOpaqueAndTheReadSucceeds(int count)
    {
        ExifMetadata metadata = ExifTiffReader.Read(Build(false, entryCount: (ushort)count)).Metadata;
        Assert.Null(metadata.MakerNote);
        Assert.Equal(3, metadata.Entries.Count);
    }

    [Theory]
    [InlineData(0xFFFFFFF0u)]
    [InlineData(136u)]       // Starts inside the payload, runs past its end.
    public void AnOutOfRangeValueOffsetLeavesTheMakerNoteOpaque(uint offset)
    {
        ExifMetadata metadata = ExifTiffReader.Read(Build(false, footer: false, imageTypeOffset: offset)).Metadata;
        Assert.Null(metadata.MakerNote);
    }

    [Fact]
    public void ATinyRetainedBudgetLeavesTheMakerNoteOpaque()
    {
        // The EXIF read itself retains 140 + 6 + 4 + 78 bytes; the MakerNote needs 22 more.
        var limits = new MetadataLimits { MaxRetainedMetadataBytes = 234 };
        Assert.Null(ExifTiffReader.Read(Build(false), limits).Metadata.MakerNote);
    }

    [Fact]
    public void CanonNamesAreSortedAndUnique()
    {
        var ids = Core.MakerNotes.CanonMakerNoteReader.Names.Select(n => n.Id).ToArray();
        Assert.Equal(ids.OrderBy(i => i).ToArray(), ids);
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.Equal(ids.Length, Core.MakerNotes.CanonMakerNoteReader.Names.Select(n => n.Name).Distinct().Count());
    }
}

public sealed class CanonMakerNoteArrayTests
{
    private static ExifValue Shorts(bool big, params ushort[] values)
    {
        byte[] raw = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++)
            if (big) BinaryPrimitives.WriteUInt16BigEndian(raw.AsSpan(i * 2), values[i]);
            else BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(i * 2), values[i]);
        return ExifValue.FromRaw(TiffFieldType.Short, (uint)values.Length, big ? Endianness.BigEndian : Endianness.LittleEndian, raw);
    }

    private static IReadOnlyList<MakerNoteField> Split(ushort tag, ExifValue value) =>
        Core.MakerNotes.CanonMakerNoteArrays.Decompose(tag, value);

    [Fact]
    public void CameraSettingsLensSpansThreeElementsAndHidesTheElementsItCovers()
    {
        ushort[] cs = new ushort[30];
        for (int i = 0; i < cs.Length; i++) cs[i] = (ushort)(100 + i);
        IReadOnlyList<MakerNoteField> fields = Split(0x0001, Shorts(false, cs));
        MakerNoteField macro = fields.First(f => f.Name == "Macro");
        Assert.Equal((ushort)1, macro.Index);
        MakerNoteField lens = fields.Single(f => f.Name == "Lens");
        Assert.Equal((ushort)23, lens.Index);
        Assert.Equal(3u, lens.Value.Count);
        Assert.DoesNotContain(fields, f => f.Name is "ShortFocal" or "FocalUnits");
        Assert.Contains(fields, f => f.Name == "MaxAperture");
        // Index 6 has no known meaning and is not reported; nothing past the array's end is either.
        Assert.DoesNotContain(fields, f => f.Index == 6);
        Assert.DoesNotContain(fields, f => f.Index >= 30);
    }

    [Theory]
    [InlineData(false, 2146202u)]
    [InlineData(true, 3214540832u)]
    public void FileInfoFileNumberIsOneLongOverTwoElements(bool big, uint expected)
    {
        IReadOnlyList<MakerNoteField> fields = Split(0x0093, Shorts(big, 32, 49050, 32, 0));
        MakerNoteField number = fields.Single(f => f.Name == "FileNumber");
        Assert.Equal(TiffFieldType.Long, number.Value.FieldType);
        byte[] raw = new byte[4];
        number.Value.CopyRawTo(raw);
        Assert.Equal(expected, big ? BinaryPrimitives.ReadUInt32BigEndian(raw) : BinaryPrimitives.ReadUInt32LittleEndian(raw));
        MakerNoteField bracket = fields.Single(f => f.Name == "BracketMode");
        Assert.Equal(TiffFieldType.SShort, bracket.Value.FieldType);
        Assert.DoesNotContain(fields, f => f.Name == "BracketValue"); // Index 4 is past the 4-element array.
    }

    [Fact]
    public void AnUnknownTagOrANonShortValueIsNotSplit()
    {
        Assert.Empty(Split(0x0002, Shorts(false, 1, 2, 3, 4)));
        Assert.Empty(Split(0x0001, ExifValue.FromRaw(TiffFieldType.Long, 1, Endianness.LittleEndian, new byte[4])));
        Assert.Empty(Split(0x0001, Shorts(false)));
    }

    [Fact]
    public void LayoutIndexesAscendAndAreUnique()
    {
        foreach (var (_, indexes) in Core.MakerNotes.CanonMakerNoteArrays.LayoutsForTests())
        {
            Assert.Equal(indexes.OrderBy(i => i), indexes);
            Assert.Equal(indexes.Count, indexes.Distinct().Count());
        }
    }
}
