using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.MakerNotes;
using MetadataSharp.Core.Tiff;
using Xunit;

namespace MetadataSharp.Tests.Tiff;

/// <summary>
/// Minolta, Casio, Samsung and Sigma MakerNotes on synthetic TIFFs. Each layout is told apart by what it
/// would get wrong: a value stored outside the directory proves the base its offset counts from, and a
/// directory written in the other byte order proves which order is read.
/// </summary>
public sealed class MoreVendorMakerNoteTests
{
    private const string Text = "VALUE07\0";

    private static byte[] U16(int value, bool big)
    {
        var bytes = new byte[2];
        if (big) BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
        else BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value);
        return bytes;
    }

    private static byte[] U32(long value, bool big)
    {
        var bytes = new byte[4];
        if (big) BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)value);
        else BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)value);
        return bytes;
    }

    private static byte[] Entry(int tag, int type, long count, byte[] value, bool big) =>
        [.. U16(tag, big), .. U16(type, big), .. U32(count, big), .. value];

    private readonly record struct Field(ushort Tag, ushort Type, uint Count, byte[] Value);

    private static Field Ascii(ushort tag, string text = Text) =>
        new(tag, 2, (uint)text.Length, Encoding.ASCII.GetBytes(text));

    private static Field Short(ushort tag, ushort value, bool big) => new(tag, 3, 1, U16(value, big));

    private static Field Undefined(ushort tag, byte[] value) => new(tag, 7, (uint)value.Length, value);

    /// <summary>
    /// An IFD in <paramref name="big"/> order followed by the values that do not fit an entry. A value
    /// outside the directory is stored as <paramref name="origin"/> plus its position in the result, so
    /// the caller decides what the offsets count from.
    /// </summary>
    private static byte[] Ifd(bool big, int origin, params Field[] fields)
    {
        int head = 2 + (12 * fields.Length) + 4;
        var directory = new List<byte>(U16(fields.Length, big));
        var values = new List<byte>();
        foreach (Field field in fields)
        {
            if (field.Value.Length <= 4)
            {
                byte[] inline = new byte[4];
                field.Value.CopyTo(inline, 0);
                directory.AddRange(Entry(field.Tag, field.Type, field.Count, inline, big));
                continue;
            }
            directory.AddRange(Entry(field.Tag, field.Type, field.Count, U32(origin + head + values.Count, big), big));
            values.AddRange(field.Value);
            if ((field.Value.Length & 1) != 0)
                values.Add(0);
        }
        directory.AddRange(new byte[4]);
        directory.AddRange(values);
        return [.. directory];
    }

    /// <summary>
    /// A TIFF in <paramref name="big"/> order: IFD0 with Make and the Exif IFD pointer, and an Exif IFD
    /// whose only entry is the MakerNote. <paramref name="note"/> is given the MakerNote's own offset.
    /// </summary>
    private static byte[] Tiff(bool big, string make, Func<int, byte[]> note)
    {
        byte[] makeBytes = Encoding.ASCII.GetBytes(make + "\0");
        int makeAt = 8 + 2 + 24 + 4;
        int exifAt = makeAt + makeBytes.Length + (makeBytes.Length & 1);
        int noteAt = exifAt + 2 + 12 + 4;
        byte[] body = note(noteAt);
        var tiff = new List<byte>(big ? "MM\0*"u8.ToArray() : "II*\0"u8.ToArray());
        tiff.AddRange(U32(8, big));
        tiff.AddRange(U16(2, big));
        tiff.AddRange(Entry(0x010F, 2, makeBytes.Length, U32(makeAt, big), big));
        tiff.AddRange(Entry(0x8769, 4, 1, U32(exifAt, big), big));
        tiff.AddRange(new byte[4]);
        tiff.AddRange(makeBytes);
        if ((makeBytes.Length & 1) != 0)
            tiff.Add(0);
        tiff.AddRange(U16(1, big));
        tiff.AddRange(Entry(0x927C, 7, body.Length, U32(noteAt, big), big));
        tiff.AddRange(new byte[4]);
        tiff.AddRange(body);
        return [.. tiff];
    }

    private static MakerNote? Read(byte[] tiff) => ExifTiffReader.Read(tiff).Metadata.MakerNote;

    private static Endianness Order(bool big) => big ? Endianness.BigEndian : Endianness.LittleEndian;

    private static byte[] Raw(ExifValue value)
    {
        byte[] raw = new byte[value.ByteLength];
        value.CopyRawTo(raw);
        return raw;
    }

    private static int Number(ExifValue value)
    {
        byte[] raw = Raw(value);
        bool big = value.ByteOrder == Endianness.BigEndian;
        return raw.Length == 2
            ? big ? BinaryPrimitives.ReadUInt16BigEndian(raw) : BinaryPrimitives.ReadUInt16LittleEndian(raw)
            : (int)(big ? BinaryPrimitives.ReadUInt32BigEndian(raw) : BinaryPrimitives.ReadUInt32LittleEndian(raw));
    }

    /// <summary>The note is recognised as <paramref name="format"/> in <paramref name="order"/>, and its
    /// two entries are the text stored outside the directory and the number 7 stored inside it.</summary>
    private static void AssertTextAndSeven(MakerNote? note, MakerNoteFormat format, Endianness order, string textName,
        string numberName)
    {
        Assert.NotNull(note);
        Assert.Equal(format, note.Format);
        Assert.Equal(order, note.ByteOrder);
        Assert.Equal(2, note.Entries.Count);
        Assert.Equal(textName, note.Entries[0].Name);
        Assert.Equal(Text, Encoding.ASCII.GetString(Raw(note.Entries[0].Value)));
        Assert.Equal(numberName, note.Entries[1].Name);
        Assert.Equal(7, Number(note.Entries[1].Value));
    }

    // ---- Minolta ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Minolta Co., Ltd.", false)]
    [InlineData("Minolta Co., Ltd.", true)]
    [InlineData("MINOLTA CO.,LTD", true)]
    [InlineData("KONICA MINOLTA", false)]
    [InlineData("KONICA MINOLTA", true)]
    [InlineData("Konica Minolta Camera, Inc.", true)]
    public void MinoltaIsABareIfdInTheTiffByteOrderWithTiffOffsets(string make, bool big)
    {
        byte[] tiff = Tiff(big, make, at => Ifd(big, at, Ascii(0x0100), Short(0x0101, 7, big)));
        AssertTextAndSeven(Read(tiff), MakerNoteFormat.Minolta, Order(big), "SceneMode", "ColorMode");
    }

    [Fact]
    public void MinoltaOffsetsDoNotCountFromTheMakerNote()
    {
        // Counted from the MakerNote, the text would be read from the TIFF header's neighbourhood.
        byte[] tiff = Tiff(false, "Minolta Co., Ltd.", _ => Ifd(false, 0, Ascii(0x0100), Short(0x0101, 7, false)));
        MakerNote? note = Read(tiff);
        Assert.NotNull(note);
        Assert.NotEqual(Text, Encoding.ASCII.GetString(Raw(note.Entries[0].Value)));
    }

    [Theory]
    [InlineData("MINOL")]
    [InlineData("CAMER")]
    public void AMinoltaCameraWithAnotherMakersLayoutIsLeftOpaque(string signature)
    {
        byte[] header = Encoding.ASCII.GetBytes(signature.PadRight(8, '\0'));
        byte[] tiff = Tiff(false, "Minolta Co., Ltd.",
            at => [.. header, .. Ifd(false, at + header.Length, Ascii(0x0100), Short(0x0101, 7, false))]);
        Assert.Null(Read(tiff));
    }

    [Fact]
    public void AMinoltaDirectoryInTheOtherByteOrderIsNotRead()
    {
        byte[] tiff = Tiff(false, "KONICA MINOLTA", at => Ifd(true, at, Ascii(0x0100), Short(0x0101, 7, true)));
        Assert.Null(Read(tiff));
    }

    /// <summary>Element <c>i</c> of the array holds <c>1000 + i</c>, big-endian.</summary>
    private static byte[] Elements(int count, int width)
    {
        var raw = new byte[count * width];
        for (int i = 0; i < count; i++)
        {
            if (width == 2) BinaryPrimitives.WriteUInt16BigEndian(raw.AsSpan(i * 2), (ushort)(1000 + i));
            else BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(i * 4), (uint)(1000 + i));
        }
        return raw;
    }

    [Theory]
    [InlineData(0x0001, 4, 50, "ExposureMode", 1, "FlashMetering", 63)]
    [InlineData(0x0003, 4, 50, "ExposureMode", 1, "FlashMetering", 63)]
    [InlineData(0x0004, 2, 26, "ExposureMode", 0, "ZoneMatchingOn", 117)]
    [InlineData(0x0114, 2, 30, "ExposureMode", 10, "ImageStabilization", 189)]
    public void TheMinoltaSettingsArraysAreSplitIntoBigEndianElements(ushort tag, int width, int named, string first,
        int firstIndex, string last, int lastIndex)
    {
        // The MakerNote is little-endian; the arrays are big-endian regardless.
        ExifValue array = ExifValue.FromRaw(TiffFieldType.Undefined, (uint)(200 * width), Endianness.LittleEndian,
            Elements(200, width));
        IReadOnlyList<MakerNoteField> fields = MinoltaMakerNoteArrays.Decompose(tag, array);

        Assert.Equal(named, fields.Count);
        Assert.Equal(first, fields[0].Name);
        Assert.Equal(firstIndex, fields[0].Index);
        Assert.Equal(last, fields[^1].Name);
        Assert.Equal(lastIndex, fields[^1].Index);
        ushort previous = 0;
        foreach (MakerNoteField field in fields)
        {
            Assert.True(field == fields[0] || field.Index > previous, $"{field.Name} is out of order.");
            previous = field.Index;
            Assert.Equal(Endianness.BigEndian, field.Value.ByteOrder);
            Assert.Equal(1U, field.Value.Count);
            Assert.Equal(width, field.Value.ByteLength);
            Assert.Equal(1000 + field.Index, Number(field.Value));
        }
    }

    [Fact]
    public void TheMinoltaSettingsElementsHaveTheTypeOfTheirArray()
    {
        ExifValue longs = ExifValue.FromRaw(TiffFieldType.Undefined, 800, Endianness.LittleEndian, Elements(200, 4));
        Assert.All(MinoltaMakerNoteArrays.Decompose(0x0003, longs), field => Assert.Equal(TiffFieldType.Long, field.Value.FieldType));

        ExifValue shorts = ExifValue.FromRaw(TiffFieldType.Undefined, 400, Endianness.LittleEndian, Elements(200, 2));
        IReadOnlyList<MakerNoteField> settings7D = MinoltaMakerNoteArrays.Decompose(0x0004, shorts);
        Assert.Equal(TiffFieldType.SShort, settings7D.Single(f => f.Name == "ExposureCompensation").Value.FieldType);
        Assert.Equal(TiffFieldType.SShort, settings7D.Single(f => f.Name == "ColorTemperature").Value.FieldType);
        Assert.Equal(24, settings7D.Count(f => f.Value.FieldType == TiffFieldType.Short));

        // Two elements of the 5D array are colour temperatures; the second is told apart by a space.
        IReadOnlyList<MakerNoteField> settings5D = MinoltaMakerNoteArrays.Decompose(0x0114, shorts);
        Assert.Equal(TiffFieldType.SShort, settings5D.Single(f => f.Name == "ColorTemperature").Value.FieldType);
        Assert.Equal((ushort)73, settings5D.Single(f => f.Name == "ColorTemperature").Index);
        Assert.Equal(TiffFieldType.Short, settings5D.Single(f => f.Name == "Color Temperature").Value.FieldType);
        Assert.Equal((ushort)110, settings5D.Single(f => f.Name == "Color Temperature").Index);
    }

    [Fact]
    public void AShortMinoltaArrayListsOnlyTheElementsItHolds()
    {
        // Ten LONGs: elements 1 to 9 are named.
        ExifValue ten = ExifValue.FromRaw(TiffFieldType.Undefined, 40, Endianness.BigEndian, Elements(10, 4));
        IReadOnlyList<MakerNoteField> fields = MinoltaMakerNoteArrays.Decompose(0x0001, ten);
        Assert.Equal(9, fields.Count);
        Assert.Equal("ExposureTime", fields[^1].Name);

        // A trailing half element is not an element.
        ExifValue ragged = ExifValue.FromRaw(TiffFieldType.Undefined, 7, Endianness.BigEndian, Elements(10, 4).AsSpan(0, 7));
        Assert.Empty(MinoltaMakerNoteArrays.Decompose(0x0001, ragged));
        Assert.Empty(MinoltaMakerNoteArrays.Decompose(0x0002, ten));
    }

    [Fact]
    public void AMinoltaMakerNoteCarriesItsSettingsAsFields()
    {
        byte[] tiff = Tiff(false, "KONICA MINOLTA",
            at => Ifd(false, at, Undefined(0x0000, "MLT0"u8.ToArray()), Undefined(0x0114, Elements(190, 2)), Short(0x0115, 7, false)));
        MakerNote? note = Read(tiff);
        Assert.NotNull(note);
        Assert.Equal(MakerNoteFormat.Minolta, note.Format);
        Assert.Equal(new[] { "Version", "CameraSettings5D", "WhiteBalance" }, note.Entries.Select(e => e.Name));
        Assert.Empty(note.Entries[0].Fields);

        MakerNoteEntry settings = note.Entries[1];
        Assert.Equal(380, settings.Value.ByteLength);
        Assert.Equal(30, settings.Fields.Count);
        MakerNoteField quality = settings.Fields.Single(f => f.Name == "Quality");
        Assert.Equal((ushort)13, quality.Index);
        Assert.Equal(1013, Number(quality.Value));
    }

    // ---- Casio -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("CASIO", false)]
    [InlineData("CASIO", true)]
    [InlineData("CASIO COMPUTER CO.,LTD.", false)]
    [InlineData("CASIO COMPUTER CO.,LTD.", true)]
    public void CasioWithoutASignatureIsABareIfdInTheTiffByteOrder(string make, bool big)
    {
        byte[] tiff = Tiff(big, make, at => Ifd(big, at, Ascii(0x0002), Short(0x0003, 7, big)));
        AssertTextAndSeven(Read(tiff), MakerNoteFormat.Casio, Order(big), "Quality", "FocusMode");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Casio2IsBigEndianWhateverTheTiffIs(bool outerBig)
    {
        byte[] tiff = Tiff(outerBig, "CASIO COMPUTER CO.,LTD.",
            at => [.. "QVC\0\0\0"u8, .. Ifd(true, at + 6, Ascii(0x2001), Short(0x3000, 7, true))]);
        AssertTextAndSeven(Read(tiff), MakerNoteFormat.Casio2, Endianness.BigEndian, "FirmwareDate", "RecordMode");
    }

    [Fact]
    public void ALittleEndianDirectoryBehindTheCasio2SignatureIsNotRead()
    {
        byte[] tiff = Tiff(false, "CASIO COMPUTER CO.,LTD.",
            at => [.. "QVC\0\0\0"u8, .. Ifd(false, at + 6, Ascii(0x2001), Short(0x3000, 7, false))]);
        Assert.Null(Read(tiff));
    }

    [Fact]
    public void TheTwoCasioLayoutsNameTheSameIdDifferently()
    {
        Assert.Equal("Quality", VendorMakerNoteNames.Casio(0x0002));
        Assert.Equal("PreviewImageSize", VendorMakerNoteNames.Casio2(0x0002));
        Assert.Equal("RecodingMode", VendorMakerNoteNames.Casio(0x0001));     // as the reference tool spells it
        Assert.Null(VendorMakerNoteNames.Casio2(0x0001));
        Assert.Equal("PrintIM", VendorMakerNoteNames.Casio(0x0E00));
        Assert.Equal("PrintIM", VendorMakerNoteNames.Casio2(0x0E00));
    }

    // ---- Samsung ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("SAMSUNG", false)]
    [InlineData("SAMSUNG", true)]
    [InlineData("SAMSUNG TECHWIN", false)]
    public void Samsung2IsABareIfdWithOffsetsFromTheMakerNote(string make, bool big)
    {
        byte[] tiff = Tiff(big, make, _ => Ifd(big, 0, Ascii(0xA001), Short(0xA011, 7, big)));
        AssertTextAndSeven(Read(tiff), MakerNoteFormat.Samsung2, Order(big), "FirmwareName", "ColorSpace");
    }

    [Fact]
    public void ASamsung2ValueOutsideTheMakerNoteLeavesItOpaque()
    {
        // Offsets written from the TIFF header point past the end of the MakerNote.
        byte[] tiff = Tiff(false, "SAMSUNG", at => Ifd(false, at, Ascii(0xA001), Short(0xA011, 7, false)));
        Assert.Null(Read(tiff));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASamsungMakerNoteWithThePentaxSignatureIsAPentaxOne(bool big)
    {
        byte[] tiff = Tiff(big, "SAMSUNG TECHWIN",
            at => [.. "AOC\0MM"u8, .. Ifd(big, at + 6, Ascii(0x0005), Short(0x0008, 7, big))]);
        AssertTextAndSeven(Read(tiff), MakerNoteFormat.Pentax, Order(big), "ModelID", "Quality");
    }

    [Fact]
    public void ALowerCaseSamsungMakeIsAPhoneAndIsLeftOpaque()
    {
        byte[] tiff = Tiff(false, "samsung", _ => Ifd(false, 0, Ascii(0xA001), Short(0xA011, 7, false)));
        Assert.Null(Read(tiff));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SamsungPictureWizardIsFiveShortsInTheMakerNoteByteOrder(bool big)
    {
        byte[] wizard = [.. U16(1000, big), .. U16(1001, big), .. U16(1002, big), .. U16(1003, big), .. U16(1004, big)];
        byte[] tiff = Tiff(big, "SAMSUNG", _ => Ifd(big, 0, new Field(0x0021, 3, 5, wizard), Short(0xA011, 7, big)));
        MakerNote? note = Read(tiff);
        Assert.NotNull(note);
        Assert.Equal(MakerNoteFormat.Samsung2, note.Format);
        MakerNoteEntry entry = note.Entries[0];
        Assert.Equal("PictureWizard", entry.Name);
        Assert.Equal(new[] { "Mode", "Color", "Saturation", "Sharpness", "Contrast" }, entry.Fields.Select(f => f.Name));
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(i, entry.Fields[i].Index);
            Assert.Equal(TiffFieldType.Short, entry.Fields[i].Value.FieldType);
            Assert.Equal(Order(big), entry.Fields[i].Value.ByteOrder);
            Assert.Equal(1000 + i, Number(entry.Fields[i].Value));
        }
        Assert.Empty(note.Entries[1].Fields);
    }

    // ---- Sigma -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("SIGMA", "SIGMA", false)]
    [InlineData("SIGMA", "SIGMA", true)]
    [InlineData("SIGMA", "FOVEON", false)]
    [InlineData("FOVEON", "FOVEON", true)]
    [InlineData("FOVEON", "SIGMA", false)]
    public void SigmaHasASignatureAndTwoBytesThenAnIfdWithTiffOffsets(string make, string signature, bool big)
    {
        // The signature is padded with NULs to eight bytes; two version bytes follow.
        byte[] header = [.. Encoding.ASCII.GetBytes(signature.PadRight(8, '\0')), 1, 0];
        byte[] tiff = Tiff(big, make, at => [.. header, .. Ifd(big, at + 10, Ascii(0x0002), Short(0x0003, 7, big))]);
        AssertTextAndSeven(Read(tiff), MakerNoteFormat.Sigma, Order(big), "SerialNumber", "DriveMode");
    }

    [Theory]
    [InlineData("SIGMX")]
    [InlineData("FOVEOX")]
    [InlineData("SIGMAS")]
    [InlineData("")]
    public void SigmaWithoutItsSignatureIsLeftOpaque(string signature)
    {
        byte[] header = [.. Encoding.ASCII.GetBytes(signature.PadRight(8, '\0')), 1, 0];
        byte[] tiff = Tiff(false, "SIGMA", at => [.. header, .. Ifd(false, at + 10, Ascii(0x0002), Short(0x0003, 7, false))]);
        Assert.Null(Read(tiff));
    }

    [Fact]
    public void ASigmaDirectoryInTheOtherByteOrderIsNotRead()
    {
        byte[] header = [.. "SIGMA\0\0\0"u8, 1, 0];
        byte[] tiff = Tiff(false, "SIGMA", at => [.. header, .. Ifd(true, at + 10, Ascii(0x0002), Short(0x0003, 7, true))]);
        Assert.Null(Read(tiff));
    }

    // ---- Tables and the enum ---------------------------------------------------------------------------

    [Fact]
    public void TheNewNameTablesAreSortedAndUnique()
    {
        (string Vendor, (ushort Id, string Name)[] Table, int Count)[] tables =
        [
            ("Minolta", VendorMakerNoteNames.MinoltaNames, 27), ("Casio", VendorMakerNoteNames.CasioNames, 19),
            ("Casio2", VendorMakerNoteNames.Casio2Names, 49), ("Samsung2", VendorMakerNoteNames.Samsung2Names, 30),
            ("Sigma", VendorMakerNoteNames.SigmaNames, 23),
        ];
        foreach ((string vendor, (ushort Id, string Name)[] table, int count) in tables)
        {
            ushort[] ids = table.Select(t => t.Id).ToArray();
            Assert.True(ids.SequenceEqual(ids.OrderBy(i => i)), $"{vendor} is not ascending.");
            Assert.Equal(count, ids.Distinct().Count());
            Assert.Equal(count, table.Length);
            Assert.All(table, row => Assert.False(string.IsNullOrWhiteSpace(row.Name)));
        }
    }

    [Theory]
    [InlineData(0x0000, "Version")]
    [InlineData(0x0003, "CameraSettingsStdNew")]
    [InlineData(0x010C, "LensID")]
    [InlineData(0x0111, "ColorCompensationFilter")]
    [InlineData(0x0113, "ImageStabilizationA100")]
    [InlineData(0x0F00, "CameraSettingsZ1")]
    [InlineData(0x010D, null)]
    public void MinoltaTagsAreNamed(ushort tag, string? expected)
    {
        Assert.Equal(expected, VendorMakerNoteNames.Minolta(tag));
    }

    [Fact]
    public void SamsungAndSigmaTagsAreNamed()
    {
        Assert.Equal("Version", VendorMakerNoteNames.Samsung2(0x0001));
        Assert.Equal("LensType", VendorMakerNoteNames.Samsung2(0xA003));
        Assert.Equal("ToneCurve4", VendorMakerNoteNames.Samsung2(0xA043));
        Assert.Null(VendorMakerNoteNames.Samsung2(0x0002));
        Assert.Equal("SerialNumber", VendorMakerNoteNames.Sigma(0x0002));
        Assert.Equal("AutoBracket", VendorMakerNoteNames.Sigma(0x0019));
        Assert.Null(VendorMakerNoteNames.Sigma(0x0013));
    }

    [Fact]
    public void TheNewFormatsAreAppendedToTheEnum()
    {
        // The numeric values of the existing members are part of the public surface.
        Assert.Equal(12, (int)MakerNoteFormat.OMSystem);
        Assert.Equal(13, (int)MakerNoteFormat.Minolta);
        Assert.Equal(14, (int)MakerNoteFormat.Casio);
        Assert.Equal(15, (int)MakerNoteFormat.Casio2);
        Assert.Equal(16, (int)MakerNoteFormat.Samsung2);
        Assert.Equal(17, (int)MakerNoteFormat.Sigma);
        // The dump tool prints a format's name as the reference tool's group name.
        Assert.Equal(new[] { "Minolta", "Casio", "Casio2", "Samsung2", "Sigma" },
            new[] { MakerNoteFormat.Minolta, MakerNoteFormat.Casio, MakerNoteFormat.Casio2, MakerNoteFormat.Samsung2,
                MakerNoteFormat.Sigma }.Select(f => f.ToString()));
    }
}
