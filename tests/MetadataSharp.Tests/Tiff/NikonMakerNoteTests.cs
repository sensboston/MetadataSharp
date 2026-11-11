using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;
using Xunit;

namespace MetadataSharp.Tests.Tiff;

public sealed class NikonMakerNoteTests
{
    private const int MakerNoteStart = 62;

    /// <summary>IFD0 (Make, ExifIFD) -> Exif IFD (MakerNote at 62) -> the given MakerNote bytes.</summary>
    private static byte[] Wrap(bool big, byte[] makerNote, string make = "NIKON")
    {
        var b = new byte[MakerNoteStart + makerNote.Length + (makerNote.Length & 1)];
        var w = new Writer(b, big);
        b[0] = b[1] = (byte)(big ? 'M' : 'I');
        w.U16(2, 42); w.U32(4, 8);
        w.U16(8, 2);
        w.Entry(10, 0x010F, 2, 6, 38);
        w.Entry(22, 0x8769, 4, 1, 44);
        Encoding.ASCII.GetBytes(make.PadRight(5)[..5]).CopyTo(b, 38);
        w.U16(44, 1);
        w.Entry(46, 0x927C, 7, (uint)makerNote.Length, MakerNoteStart);
        makerNote.CopyTo(b, MakerNoteStart);
        return b;
    }

    private readonly ref struct Writer(byte[] b, bool big)
    {
        public void U16(int at, int v) { if (big) BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(at), (ushort)v); else BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), (ushort)v); }
        public void U32(int at, long v) { if (big) BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at), (uint)v); else BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), (uint)v); }
        public void Entry(int at, int tag, int type, long count, long value) { U16(at, tag); U16(at + 2, type); U32(at + 4, count); U32(at + 8, value); }
    }

    /// <summary>
    /// A two-entry IFD at <paramref name="ifdAt"/> inside <paramref name="note"/>: ISOSpeed (inline SHORT[2])
    /// and Quality (ASCII[8] at <paramref name="qualityAt"/> in the note), whose stored offset is
    /// <paramref name="qualityOffset"/>.
    /// </summary>
    private static void Ifd(byte[] note, bool big, int ifdAt, int qualityAt, long qualityOffset)
    {
        var w = new Writer(note, big);
        w.U16(ifdAt, 2);
        w.Entry(ifdAt + 2, 0x0002, 3, 2, 0);
        w.U16(ifdAt + 2 + 8, 0); w.U16(ifdAt + 2 + 10, 200);
        w.Entry(ifdAt + 14, 0x0004, 2, 8, qualityOffset);
        w.U32(ifdAt + 26, 0);
        Encoding.ASCII.GetBytes("FINE   \0").CopyTo(note, qualityAt);
    }

    /// <summary>"Nikon\0" 02 10 00 00, an embedded TIFF header in <paramref name="innerBig"/> order, IFD at 8.</summary>
    private static byte[] Nikon3(bool innerBig, long? qualityOffset = null)
    {
        var note = new byte[10 + 8 + 30 + 8];
        "Nikon\0"u8.CopyTo(note);
        note[6] = 0x02; note[7] = 0x10;
        var w = new Writer(note, innerBig);
        note[10] = note[11] = (byte)(innerBig ? 'M' : 'I');
        w.U16(12, 42); w.U32(14, 8);
        Ifd(note, innerBig, 18, 48, qualityOffset ?? 38); // 48 is 38 past the embedded header at 10.
        return note;
    }

    private static string Text(ExifValue value)
    {
        byte[] raw = new byte[value.ByteLength];
        value.CopyRawTo(raw);
        return Encoding.ASCII.GetString(raw).TrimEnd('\0', ' ');
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Nikon3UsesTheEmbeddedHeaderForOrderAndOffsets(bool outerBig, bool innerBig)
    {
        MakerNote? note = ExifTiffReader.Read(Wrap(outerBig, Nikon3(innerBig))).Metadata.MakerNote;
        Assert.NotNull(note);
        Assert.Equal(MakerNoteFormat.Nikon3, note.Format);
        Assert.Equal(innerBig ? Endianness.BigEndian : Endianness.LittleEndian, note.ByteOrder);
        Assert.True(note.TryGetEntry(0x0004, out MakerNoteEntry? quality));
        Assert.Equal("Quality", quality.Name);
        Assert.Equal("FINE", Text(quality.Value));
        Assert.True(note.TryGetEntry(0x0002, out MakerNoteEntry? iso));
        Assert.Equal("ISOSpeed", iso.Name);
        Assert.Equal(2u, iso.Value.Count);
    }

    /// <summary>
    /// A Nikon3 note whose only tag is the preview pointer 0x0011, stored as <paramref name="pointer"/>.
    /// The preview directory sits 26 bytes past the embedded header: Compression 6, XResolution 300/1
    /// (stored after the directory) and a tag no TIFF table names.
    /// </summary>
    private static byte[] Nikon3WithPreview(bool innerBig, long pointer = 26)
    {
        var note = new byte[86];
        "Nikon\0"u8.CopyTo(note);
        note[6] = 0x02; note[7] = 0x10;
        var w = new Writer(note, innerBig);
        note[10] = note[11] = (byte)(innerBig ? 'M' : 'I');
        w.U16(12, 42); w.U32(14, 8);
        w.U16(18, 1);
        w.Entry(20, 0x0011, 4, 1, pointer);
        w.U16(36, 3);
        w.Entry(38, 0x0103, 3, 1, 0); w.U16(46, 6);
        w.Entry(50, 0x011A, 5, 1, 68);
        w.Entry(62, 0x8888, 3, 1, 0); w.U16(70, 7);
        w.U32(78, 300); w.U32(82, 1);
        return note;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TheNikon3PreviewDirectoryIsReadAsTheFieldsOfItsPointer(bool outerBig, bool innerBig)
    {
        MakerNote? note = ExifTiffReader.Read(Wrap(outerBig, Nikon3WithPreview(innerBig))).Metadata.MakerNote;
        Assert.NotNull(note);
        Assert.True(note.TryGetEntry(0x0011, out MakerNoteEntry? preview));
        Assert.Equal("Preview", preview.Name);
        Assert.Equal(TiffFieldType.Long, preview.Value.FieldType);   // the pointer itself is kept as stored
        Assert.Equal(1u, preview.Value.Count);
        // The fields are ordinary TIFF fields, named by the TIFF table and indexed by their tag numbers.
        Assert.Equal(new[] { "Compression", "XResolution", "0x8888" }, preview.Fields.Select(field => field.Name).ToArray());
        Assert.Equal(new ushort[] { 0x0103, 0x011A, 0x8888 }, preview.Fields.Select(field => field.Index).ToArray());
        Assert.Equal(TiffFieldType.Short, preview.Fields[0].Value.FieldType);
        Assert.Equal(TiffFieldType.Rational, preview.Fields[1].Value.FieldType);
        byte[] resolution = new byte[8];
        preview.Fields[1].Value.CopyRawTo(resolution);
        Assert.Equal(300u, innerBig ? BinaryPrimitives.ReadUInt32BigEndian(resolution) : BinaryPrimitives.ReadUInt32LittleEndian(resolution));
    }

    [Theory]
    [InlineData(0L)]      // Not a directory offset at all.
    [InlineData(4L)]      // Inside the embedded header.
    [InlineData(75L)]     // Inside the note, with no room for a directory behind it.
    [InlineData(4096L)]   // Beyond the note.
    public void ANikon3PreviewPointerThatLeadsNowhereStaysAPlainNumber(long pointer)
    {
        MakerNote? note = ExifTiffReader.Read(Wrap(false, Nikon3WithPreview(false, pointer))).Metadata.MakerNote;
        Assert.NotNull(note);
        Assert.True(note.TryGetEntry(0x0011, out MakerNoteEntry? preview));
        Assert.Empty(preview.Fields);
    }

    [Theory]
    [InlineData(1000L)]   // Beyond the MakerNote.
    [InlineData(-10L)]    // Before the embedded header (wraps to a huge unsigned offset).
    public void Nikon3OffsetsMustStayInsideTheMakerNote(long offset)
    {
        Assert.Null(ExifTiffReader.Read(Wrap(false, Nikon3(false, offset))).Metadata.MakerNote);
    }

    [Fact]
    public void Nikon3WithABrokenEmbeddedHeaderIsOpaque()
    {
        byte[] note = Nikon3(false);
        note[12] = 43;
        ExifMetadata metadata = ExifTiffReader.Read(Wrap(false, note)).Metadata;
        Assert.Null(metadata.MakerNote);
        Assert.True(metadata.TryGetEntry(new(ExifDirectoryKind.Exif, 0x927C), out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Nikon2HasASignatureAndTiffRelativeOffsets(bool big)
    {
        var note = new byte[8 + 30 + 8];
        "Nikon\0\u0001\0"u8.CopyTo(note);
        Ifd(note, big, 8, 38, MakerNoteStart + 38);
        MakerNote? decoded = ExifTiffReader.Read(Wrap(big, note)).Metadata.MakerNote;
        Assert.NotNull(decoded);
        Assert.Equal(MakerNoteFormat.Nikon2, decoded.Format);
        Assert.True(decoded.TryGetEntry(0x0004, out MakerNoteEntry? quality));
        Assert.Equal("FINE", Text(quality.Value));
        Assert.Equal("ColorMode", Core.MakerNotes.NikonMakerNoteNames.Nikon2(0x0004));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Nikon1IsAHeaderlessDirectory(bool big)
    {
        var note = new byte[30 + 8];
        Ifd(note, big, 0, 30, MakerNoteStart + 30);
        MakerNote? decoded = ExifTiffReader.Read(Wrap(big, note)).Metadata.MakerNote;
        Assert.NotNull(decoded);
        Assert.Equal(MakerNoteFormat.Nikon1, decoded.Format);
        Assert.True(decoded.TryGetEntry(0x0004, out MakerNoteEntry? quality));
        Assert.Equal("Quality", quality.Name);
        Assert.Equal("FINE", Text(quality.Value));
    }

    [Fact]
    public void ANikonSignatureWithAnUnknownVersionIsOpaque()
    {
        byte[] note = Nikon3(false);
        note[6] = 0x07;
        Assert.Null(ExifTiffReader.Read(Wrap(false, note)).Metadata.MakerNote);
    }

    [Fact]
    public void TheMakeDecidesTheVendor()
    {
        Assert.Null(ExifTiffReader.Read(Wrap(false, Nikon3(false), make: "Sony ")).Metadata.MakerNote);
        Assert.NotNull(ExifTiffReader.Read(Wrap(false, Nikon3(false), make: "Nikon")).Metadata.MakerNote);
    }

    [Fact]
    public void NikonNameTablesAreSortedAndUnique()
    {
        foreach (var table in new[] { Core.MakerNotes.NikonMakerNoteNames.Nikon1Names,
                     Core.MakerNotes.NikonMakerNoteNames.Nikon2Names, Core.MakerNotes.NikonMakerNoteNames.Nikon3Names })
        {
            var ids = table.Select(t => t.Id).ToArray();
            Assert.Equal(ids.OrderBy(i => i), ids);
            Assert.Equal(ids.Length, ids.Distinct().Count());
            Assert.Equal(ids.Length, table.Select(t => t.Name).Distinct().Count());
        }
    }

    /// <summary>
    /// A little-endian TIFF with Make NIKON, the given Model and a Nikon3 note that holds
    /// <paramref name="tags"/> (ascending; type 4 is LONG, anything else is one byte per element).
    /// </summary>
    private static byte[] Nikon3File(string model, params (int Tag, int Type, byte[] Data)[] tags)
    {
        int dataAt = 18 + 2 + 12 * tags.Length + 4;
        var note = new byte[dataAt + tags.Sum(t => t.Data.Length > 4 ? t.Data.Length + (t.Data.Length & 1) : 0)];
        "Nikon\0"u8.CopyTo(note);
        note[6] = 0x02; note[7] = 0x10;
        var w = new Writer(note, false);
        note[10] = note[11] = (byte)'I';
        w.U16(12, 42); w.U32(14, 8);
        w.U16(18, tags.Length);
        int at = 20;
        foreach ((int tag, int type, byte[] data) in tags)
        {
            // Offsets count from the embedded header, ten bytes into the note.
            w.Entry(at, tag, type, data.Length / (type == 4 ? 4 : 1), data.Length > 4 ? dataAt - 10 : 0);
            if (data.Length > 4)
            {
                data.CopyTo(note, dataAt);
                dataAt += data.Length + (data.Length & 1);
            }
            else
                data.CopyTo(note, at + 8);
            at += 12;
        }

        byte[] name = Encoding.ASCII.GetBytes(model + "\0");
        Assert.InRange(name.Length, 5, 24);
        var b = new byte[98 + note.Length];
        var outer = new Writer(b, false);
        b[0] = b[1] = (byte)'I';
        outer.U16(2, 42); outer.U32(4, 8);
        outer.U16(8, 3);
        outer.Entry(10, 0x010F, 2, 6, 50);
        outer.Entry(22, 0x0110, 2, name.Length, 56);
        outer.Entry(34, 0x8769, 4, 1, 80);
        "NIKON\0"u8.CopyTo(b.AsSpan(50));
        name.CopyTo(b, 56);
        outer.U16(80, 1);
        outer.Entry(82, 0x927C, 7, note.Length, 98);
        note.CopyTo(b, 98);
        return b;
    }

    [Theory]
    [InlineData("3001234", "NIKON D850", 3001234u)]
    [InlineData("N/A", "NIKON D850", 0x60u)]  // A serial number that is not a number: the model's default key.
    [InlineData("N/A", "NIKON D50", 0x22u)]
    [InlineData(null, "NIKON D850", null)]    // No serial number in the note: the array is stored in the clear.
    public void EncryptedArraysAreReadWithTheNotesKeyAndPlainOnesWithoutASerialNumber(string? serial, string model, uint? key)
    {
        byte[] plain = new byte[20];
        "0204"u8.CopyTo(plain);
        plain[12] = 0x8A; // LensIDNumber
        byte[] stored = key is uint k ? Core.MakerNotes.NikonMakerNoteCipher.Decrypt(plain, 4, 777, k) : plain;
        var tags = new List<(int, int, byte[])>();
        if (serial is not null)
            tags.Add((0x001D, 2, Encoding.ASCII.GetBytes(serial.PadRight(7) + "\0")));
        tags.Add((0x0098, 7, stored));
        tags.Add((0x00A7, 4, new byte[] { 0x09, 0x03, 0x00, 0x00 })); // ShutterCount 777

        MakerNote? note = ExifTiffReader.Read(Nikon3File(model, [.. tags])).Metadata.MakerNote;
        Assert.NotNull(note);
        Assert.True(note.TryGetEntry(0x0098, out MakerNoteEntry? lens));
        byte[] one = new byte[1];
        lens.Fields.Single(field => field.Name == "LensIDNumber").Value.CopyRawTo(one);
        Assert.Equal(0x8A, one[0]);
    }

    [Fact]
    public void WithoutAShutterCountTheArraysAreLeftWhole()
    {
        byte[] stored = new byte[20];
        "0204"u8.CopyTo(stored);
        MakerNote? note = ExifTiffReader.Read(Nikon3File("NIKON D850", (0x0098, 7, stored))).Metadata.MakerNote;
        Assert.NotNull(note);
        Assert.True(note.TryGetEntry(0x0098, out MakerNoteEntry? lens));
        Assert.Empty(lens.Fields);
    }
}

public sealed class NikonMakerNoteArrayTests
{
    private static ExifValue Bytes(Endianness order, params byte[] raw) =>
        ExifValue.FromRaw(TiffFieldType.Undefined, (uint)raw.Length, order, raw);

    private static IReadOnlyList<MakerNoteField> Split(ushort tag, ExifValue value) =>
        Core.MakerNotes.NikonMakerNoteArrays.Decompose(tag, value);

    private static byte[] Versioned(string version, int length)
    {
        byte[] raw = new byte[length];
        Encoding.ASCII.GetBytes(version).CopyTo(raw, 0);
        for (int i = 4; i < length; i++) raw[i] = (byte)i;
        return raw;
    }

    [Fact]
    public void FlashInfoIsChosenByVersionAndIsBigEndian()
    {
        byte[] raw = Versioned("0107", 70);
        raw[6] = 0x01; raw[7] = 0x02;
        IReadOnlyList<MakerNoteField> fields = Split(0x00A8, Bytes(Endianness.LittleEndian, raw));
        Assert.Contains(fields, f => f.Name == "WirelessFlashData");
        MakerNoteField firmware = fields.Single(f => f.Name == "ExternalFlashFirmware");
        Assert.Equal(Endianness.BigEndian, firmware.Value.ByteOrder);
        Assert.Equal(TiffFieldType.Short, firmware.Value.FieldType);

        IReadOnlyList<MakerNoteField> old = Split(0x00A8, Bytes(Endianness.LittleEndian, Versioned("0102", 20)));
        Assert.DoesNotContain(old, f => f.Name is "FlashColorFilter" or "WirelessFlashData");
        Assert.Contains(old, f => f.Name == "FlashGNDistance");
    }

    [Fact]
    public void AnUnknownVersionOrSizeIsNotSplit()
    {
        Assert.Empty(Split(0x00A8, Bytes(Endianness.LittleEndian, Versioned("0999", 70))));
        Assert.Empty(Split(0x00B7, Bytes(Endianness.LittleEndian, Versioned("0100", 31))));
        Assert.Empty(Split(0x0098, Bytes(Endianness.LittleEndian, Versioned("0204", 40)))); // Encrypted.
        Assert.Empty(Split(0x0023, ExifValue.FromRaw(TiffFieldType.Short, 2, Endianness.LittleEndian, new byte[4])));
    }

    [Fact]
    public void PictureControlTextFieldsAndATruncatedArray()
    {
        byte[] raw = Versioned("0100", 58);
        Encoding.ASCII.GetBytes("STANDARD\0").CopyTo(raw, 4);
        IReadOnlyList<MakerNoteField> fields = Split(0x0023, Bytes(Endianness.BigEndian, raw));
        MakerNoteField name = fields.Single(f => f.Name == "Name");
        Assert.Equal(TiffFieldType.Ascii, name.Value.FieldType);
        Assert.Equal(20u, name.Value.Count);
        Assert.Contains(fields, f => f.Name == "ToningSaturation");

        IReadOnlyList<MakerNoteField> cut = Split(0x0023, Bytes(Endianness.BigEndian, raw[..50]));
        Assert.Equal("QuickAdjust", cut[^1].Name); // Sharpness at byte 50 is past the 50-byte array.
        Assert.DoesNotContain(cut, f => f.Index >= 50);
    }

    [Fact]
    public void TheFirstGenerationShotInfoIsStoredInTheClearAndIsBigEndian()
    {
        byte[] raw = Versioned("0103", 465);
        BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(106), 123456);
        raw[117] = 2;
        IReadOnlyList<MakerNoteField> fields = Split(0x0091, Bytes(Endianness.LittleEndian, raw));
        Assert.Equal(new[] { "Version", "ShutterCount1", "DeletedImageCount", "VibrationReduction", "VibrationReduction1",
            "ShutterCount2", "VibrationReduction2" }, fields.Select(f => f.Name));
        MakerNoteField count = fields[1];
        Assert.Equal(TiffFieldType.Long, count.Value.FieldType);
        Assert.Equal(Endianness.BigEndian, count.Value.ByteOrder);
        byte[] four = new byte[4];
        count.Value.CopyRawTo(four);
        Assert.Equal(123456u, BinaryPrimitives.ReadUInt32BigEndian(four));
        Assert.Equal((ushort)430, fields[^1].Index);

        // The 02xx generation is encrypted: without the note's key it is not split.
        Assert.Empty(Split(0x0091, Bytes(Endianness.LittleEndian, Versioned("0210", 465))));
        // A short array lists only what it holds.
        Assert.Equal(4, Split(0x0091, Bytes(Endianness.BigEndian, Versioned("0100", 120))).Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ColorBalance0103HoldsItsLevelsInTheNotesByteOrder(bool big)
    {
        byte[] raw = Versioned("0103", 140);
        Endianness order = big ? Endianness.BigEndian : Endianness.LittleEndian;
        if (big) BinaryPrimitives.WriteUInt16BigEndian(raw.AsSpan(20), 516);
        else BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(20), 516);
        IReadOnlyList<MakerNoteField> fields = Split(0x0097, Bytes(order, raw));
        Assert.Equal(new[] { "Version", "WB_RGBGLevels" }, fields.Select(f => f.Name));
        MakerNoteField levels = fields[1];
        Assert.Equal((ushort)20, levels.Index);
        Assert.Equal(TiffFieldType.Short, levels.Value.FieldType);
        Assert.Equal(4u, levels.Value.Count);
        Assert.Equal(order, levels.Value.ByteOrder);
        byte[] eight = new byte[8];
        levels.Value.CopyRawTo(eight);
        Assert.Equal(516, big ? BinaryPrimitives.ReadUInt16BigEndian(eight) : BinaryPrimitives.ReadUInt16LittleEndian(eight));

        Assert.Empty(Split(0x0097, Bytes(order, Versioned("0102", 140))));
    }

    [Fact]
    public void ISOInfoIsBigEndianWhateverTheMakerNoteOrder()
    {
        byte[] raw = [100, 0, 0, 0, 0x01, 0x02, 0, 0, 0, 0, 0x03, 0x04, 0, 0];
        MakerNoteField expansion = Split(0x0025, Bytes(Endianness.LittleEndian, raw)).Single(f => f.Name == "ISOExpansion");
        Assert.Equal(Endianness.BigEndian, expansion.Value.ByteOrder);
    }
}

public sealed class NikonMakerNoteCipherTests
{
    [Fact]
    public void TheCipherIsItsOwnInverseAndLeavesThePrefix()
    {
        byte[] plain = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
        byte[] once = Core.MakerNotes.NikonMakerNoteCipher.Decrypt(plain, 4, 12345, 0x60);
        Assert.Equal(plain[..4], once[..4]);
        Assert.NotEqual(plain[4..], once[4..]);
        Assert.Equal(plain, Core.MakerNotes.NikonMakerNoteCipher.Decrypt(once, 4, 12345, 0x60));
        Assert.NotEqual(once, Core.MakerNotes.NikonMakerNoteCipher.Decrypt(plain, 4, 12346, 0x60));
    }

    [Theory]
    [InlineData("3001234\0", "NIKON D3", 3001234u)]
    [InlineData("No= 30015\0", "NIKON D50", 0x22u)]
    [InlineData("ABC\0", "NIKON D70", 0x60u)]
    public void TheSerialKeyIsTheNumberOrAModelDefault(string serial, string model, uint expected) =>
        Assert.Equal(expected, Core.MakerNotes.NikonMakerNoteCipher.SerialKey(Encoding.ASCII.GetBytes(serial), Encoding.ASCII.GetBytes(model)));

    [Fact]
    public void WithoutANumericSerialOrAModelThereIsNoKey() =>
        Assert.Null(Core.MakerNotes.NikonMakerNoteCipher.SerialKey("ABC\0"u8, default));

    [Fact]
    public void EncryptedLensDataIsSplitAfterDecryption()
    {
        byte[] plain = new byte[20];
        "0204"u8.CopyTo(plain);
        plain[12] = 0x8A; // LensIDNumber
        plain[14] = 0x18; // MinFocalLength
        byte[] stored = Core.MakerNotes.NikonMakerNoteCipher.Decrypt(plain, 4, 777, 3001234);
        var value = ExifValue.FromRaw(TiffFieldType.Undefined, (uint)stored.Length, Endianness.LittleEndian, stored);
        Assert.Empty(Core.MakerNotes.NikonMakerNoteArrays.Decompose(0x0098, value));
        IReadOnlyList<MakerNoteField> fields = Core.MakerNotes.NikonMakerNoteArrays.DecomposeEncrypted(0x0098, value, 777, 3001234);
        byte[] one = new byte[1];
        fields.Single(f => f.Name == "LensIDNumber").Value.CopyRawTo(one);
        Assert.Equal(0x8A, one[0]);
        fields.Single(f => f.Name == "MinFocalLength").Value.CopyRawTo(one);
        Assert.Equal(0x18, one[0]);
        Assert.Empty(Core.MakerNotes.NikonMakerNoteArrays.DecomposeEncrypted(0x0098,
            ExifValue.FromRaw(TiffFieldType.Undefined, 20, Endianness.LittleEndian, [.. "0999"u8, .. new byte[16]]), 777, 1));
    }

    [Fact]
    public void ColorBalance0212HasItsOwnLevelsOffset()
    {
        byte[] plain = new byte[320];
        "0212"u8.CopyTo(plain);
        BinaryPrimitives.WriteUInt16BigEndian(plain.AsSpan(294), 398);
        byte[] stored = Core.MakerNotes.NikonMakerNoteCipher.Decrypt(plain, 284, 24715, 6028018);
        Assert.Equal(plain[..284], stored[..284]);
        var value = ExifValue.FromRaw(TiffFieldType.Undefined, 320, Endianness.BigEndian, stored);
        IReadOnlyList<MakerNoteField> fields = Core.MakerNotes.NikonMakerNoteArrays.DecomposeEncrypted(0x0097, value, 24715, 6028018);
        Assert.Equal(new[] { "Version", "WB_GRBGLevels" }, fields.Select(f => f.Name));
        Assert.Equal((ushort)294, fields[1].Index);
        byte[] raw = new byte[8];
        fields[1].Value.CopyRawTo(raw);
        Assert.Equal(398, BinaryPrimitives.ReadUInt16BigEndian(raw));
        // Without the key it is not split at all.
        Assert.Empty(Core.MakerNotes.NikonMakerNoteArrays.Decompose(0x0097, value));
    }

    [Fact]
    public void ColorBalanceDecryptionStartsLate()
    {
        byte[] plain = new byte[300];
        "0219"u8.CopyTo(plain);
        BinaryPrimitives.WriteUInt16LittleEndian(plain.AsSpan(290), 512);
        byte[] stored = Core.MakerNotes.NikonMakerNoteCipher.Decrypt(plain, 284, 5, 6);
        Assert.Equal(plain[..284], stored[..284]);
        var value = ExifValue.FromRaw(TiffFieldType.Undefined, 300, Endianness.LittleEndian, stored);
        MakerNoteField levels = Core.MakerNotes.NikonMakerNoteArrays.DecomposeEncrypted(0x0097, value, 5, 6).Single(f => f.Name == "WB_RGGBLevels");
        byte[] raw = new byte[8];
        levels.Value.CopyRawTo(raw);
        Assert.Equal(512, BinaryPrimitives.ReadUInt16LittleEndian(raw));
    }
}
