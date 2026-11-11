using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;
using Xunit;

namespace MetadataSharp.Tests.Tiff;

public sealed class VendorMakerNoteTests
{
    private const int MakerNoteStart = 66;

    private static void U16(byte[] b, int at, int v, bool big)
    {
        if (big) BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(at), (ushort)v);
        else BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), (ushort)v);
    }

    private static void U32(byte[] b, int at, long v, bool big)
    {
        if (big) BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at), (uint)v);
        else BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), (uint)v);
    }

    private static void Entry(byte[] b, int at, int tag, int type, long count, long value, bool big)
    {
        U16(b, at, tag, big); U16(b, at + 2, type, big); U32(b, at + 4, count, big); U32(b, at + 8, value, big);
    }

    /// <summary>A TIFF whose IFD0 Make is <paramref name="make"/> (exactly 9 characters + NUL), with
    /// <paramref name="note"/> as the Exif IFD's MakerNote at offset 66.</summary>
    private static byte[] Wrap(bool big, string make, byte[] note)
    {
        var b = new byte[MakerNoteStart + note.Length + (note.Length & 1)];
        b[0] = b[1] = (byte)(big ? 'M' : 'I');
        U16(b, 2, 42, big); U32(b, 4, 8, big);
        U16(b, 8, 2, big);
        Entry(b, 10, 0x010F, 2, 10, 38, big);
        Entry(b, 22, 0x8769, 4, 1, 48, big);
        Encoding.ASCII.GetBytes(make.PadRight(9)[..9]).CopyTo(b, 38);
        U16(b, 48, 1, big);
        Entry(b, 50, 0x927C, 7, note.Length, MakerNoteStart, big);
        note.CopyTo(b, MakerNoteStart);
        return b;
    }

    /// <summary>
    /// A MakerNote of <paramref name="header"/>, then a one-entry IFD in <paramref name="big"/> order
    /// holding tag <paramref name="tag"/> as ASCII[8] "VALUE07" at offset <paramref name="stored"/>
    /// (stored value), the string sitting right after the IFD.
    /// </summary>
    private static byte[] Note(byte[] header, bool big, ushort tag, Func<int, long> stored, bool nextPointer = true)
    {
        int ifd = header.Length;
        int ifdLength = 2 + 12 + (nextPointer ? 4 : 0);
        int text = ifd + ifdLength + ((ifd + ifdLength) & 1);
        var note = new byte[text + 8];
        header.CopyTo(note, 0);
        U16(note, ifd, 1, big);
        Entry(note, ifd + 2, tag, 2, 8, stored(text), big);
        Encoding.ASCII.GetBytes("VALUE07\0").CopyTo(note, text);
        return note;
    }

    private static MakerNoteEntry Single(byte[] tiff, MakerNoteFormat format, Endianness order)
    {
        MakerNote? note = ExifTiffReader.Read(tiff).Metadata.MakerNote;
        Assert.NotNull(note);
        Assert.Equal(format, note.Format);
        Assert.Equal(order, note.ByteOrder);
        Assert.Equal(MakerNoteStart, note.Offset);
        MakerNoteEntry entry = Assert.Single(note.Entries);
        byte[] raw = new byte[entry.Value.ByteLength];
        entry.Value.CopyRawTo(raw);
        Assert.Equal("VALUE07\0", Encoding.ASCII.GetString(raw));
        return entry;
    }

    private static byte[] Header(string signature, params byte[] tail) =>
        [.. Encoding.ASCII.GetBytes(signature), .. tail];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FujifilmIsLittleEndianWithOffsetsFromTheMakerNote(bool outerBig)
    {
        byte[] note = Note(Header("FUJIFILM", 12, 0, 0, 0), false, 0x1000, text => text);
        MakerNoteEntry entry = Single(Wrap(outerBig, "FUJIFILM", note), MakerNoteFormat.Fujifilm, Endianness.LittleEndian);
        Assert.Equal("Quality", entry.Name);
    }

    [Fact]
    public void FujifilmRejectsAnIfdOffsetOutsideTheMakerNote()
    {
        byte[] note = Note(Header("FUJIFILM", 12, 0, 0, 0), false, 0x1000, text => text);
        note[8] = 0xFF;
        Assert.Null(ExifTiffReader.Read(Wrap(false, "FUJIFILM", note)).Metadata.MakerNote);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PentaxAocUsesTheTiffByteOrderAndTiffOffsets(bool big)
    {
        // The two bytes after the signature say "MM" either way: they are not trusted.
        byte[] note = Note(Header("AOC\0MM"), big, 0x0005, text => MakerNoteStart + text);
        MakerNoteEntry entry = Single(Wrap(big, "PENTAX Co", note), MakerNoteFormat.Pentax,
            big ? Endianness.BigEndian : Endianness.LittleEndian);
        Assert.Equal("ModelID", entry.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PentaxDngUsesTheTiffByteOrderAndOffsetsFromTheMakerNote(bool big)
    {
        // The two bytes after the signature say "MM" either way: they are not trusted.
        byte[] note = Note(Header("PENTAX \0MM"), big, 0x0005, text => text);
        Single(Wrap(big, "PENTAX Co", note), MakerNoteFormat.PentaxDng,
            big ? Endianness.BigEndian : Endianness.LittleEndian);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANoteInDngPrivateDataReportsWhereThatValueStarts(bool big)
    {
        // IFD0 holds Make and DNGPrivateData and there is no Exif directory at all: the note is found
        // through tag 0xC634, and its offset is the offset of that value.
        byte[] note = Note(Header("PENTAX \0MM"), big, 0x0005, text => text);
        const int start = 48;
        var tiff = new byte[start + note.Length];
        tiff[0] = tiff[1] = (byte)(big ? 'M' : 'I');
        U16(tiff, 2, 42, big); U32(tiff, 4, 8, big);
        U16(tiff, 8, 2, big);
        Entry(tiff, 10, 0x010F, 2, 10, 38, big);
        Entry(tiff, 22, 0xC634, 1, note.Length, start, big);
        Encoding.ASCII.GetBytes("PENTAX Co").CopyTo(tiff, 38);
        note.CopyTo(tiff, start);

        MakerNote? read = ExifTiffReader.Read(tiff).Metadata.MakerNote;
        Assert.NotNull(read);
        Assert.Equal(MakerNoteFormat.PentaxDng, read.Format);
        Assert.Equal(start, read.Offset);
        Assert.Single(read.Entries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SonyWithAndWithoutItsSignature(bool big)
    {
        Endianness order = big ? Endianness.BigEndian : Endianness.LittleEndian;
        MakerNoteEntry one = Single(Wrap(big, "SONY     ", Note(Header("SONY DSC \0\0\0"), big, 0x0102, t => MakerNoteStart + t)),
            MakerNoteFormat.Sony1, order);
        Assert.Equal("Quality", one.Name);
        Single(Wrap(big, "SONY     ", Note([], big, 0x0102, t => MakerNoteStart + t)), MakerNoteFormat.Sony2, order);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PanasonicHasNoNextIfdPointer(bool big)
    {
        byte[] note = Note(Header("Panasonic\0\0\0"), big, 0x0001, t => MakerNoteStart + t, nextPointer: false);
        MakerNoteEntry entry = Single(Wrap(big, "Panasonic", note), MakerNoteFormat.Panasonic,
            big ? Endianness.BigEndian : Endianness.LittleEndian);
        Assert.Equal("Quality", entry.Name);
    }

    [Theory]
    [InlineData("FUJIFILM", "FUJIFILX")]
    [InlineData("Panasonic", "Panasonix")]
    [InlineData("PENTAX Co", "AOX")]
    public void AMissingSignatureLeavesTheMakerNoteOpaque(string make, string signature)
    {
        byte[] note = Note(Header(signature.PadRight(12, '\0')), false, 0x0001, t => MakerNoteStart + t);
        Assert.Null(ExifTiffReader.Read(Wrap(false, make, note)).Metadata.MakerNote);
    }

    [Fact]
    public void VendorNameTablesAreSortedAndUnique()
    {
        foreach (var table in new[] { Core.MakerNotes.VendorMakerNoteNames.FujifilmNames, Core.MakerNotes.VendorMakerNoteNames.PentaxNames,
                     Core.MakerNotes.VendorMakerNoteNames.SonyNames, Core.MakerNotes.VendorMakerNoteNames.PanasonicNames })
        {
            var ids = table.Select(t => t.Id).ToArray();
            Assert.Equal(ids.OrderBy(i => i), ids);
            Assert.Equal(ids.Length, ids.Distinct().Count());
        }
    }
}

public sealed class SonyMakerNoteArrayTests
{
    private static byte[] Encipher(byte[] plain) =>
        plain.Select(p => p < 249 ? (byte)(p * p * p % 249) : p).ToArray();

    private static ExifValue Undefined(byte[] raw) =>
        ExifValue.FromRaw(TiffFieldType.Undefined, (uint)raw.Length, Endianness.BigEndian, raw);

    [Fact]
    public void TheDecipherInvertsTheCubeSubstitution()
    {
        byte[] plain = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        Assert.Equal(plain, Core.MakerNotes.SonyMakerNoteArrays.Plain(Encipher(plain)));
    }

    [Fact]
    public void Misc2bIsChosenByItsDecipheredBytesAndIsLittleEndian()
    {
        byte[] plain = new byte[40];
        plain[0] = 9; plain[3] = 2; plain[12] = 4;
        BinaryPrimitives.WriteUInt16LittleEndian(plain.AsSpan(30), 300);
        IReadOnlyList<MakerNoteField> fields = Core.MakerNotes.SonyMakerNoteArrays.Decompose(0x9404, Undefined(Encipher(plain)), "ILCE-7M3"u8);
        Assert.Equal(new[] { "ExposureProgram", "IntelligentAuto", "LensZoomPosition", "FocusPosition2" }, fields.Select(f => f.Name));
        MakerNoteField zoom = fields.Single(f => f.Name == "LensZoomPosition");
        Assert.Equal(Endianness.LittleEndian, zoom.Value.ByteOrder);
        byte[] raw = new byte[2];
        zoom.Value.CopyRawTo(raw);
        Assert.Equal(300, BinaryPrimitives.ReadUInt16LittleEndian(raw));

        plain[3] = 3;
        Assert.Empty(Core.MakerNotes.SonyMakerNoteArrays.Decompose(0x9404, Undefined(Encipher(plain)), "ILCE-7M3"u8));
    }

    [Theory]
    [InlineData("ILCE-7M3", 4)]
    [InlineData("SLT-A77", 0)]
    [InlineData("ILCA-77M2", 0)]
    public void FocusPositionDependsOnTheModel(string model, int expected)
    {
        IReadOnlyList<MakerNoteField> fields = Core.MakerNotes.SonyMakerNoteArrays.Decompose(0x9402, Undefined(Encipher(new byte[48])),
            Encoding.ASCII.GetBytes(model));
        Assert.Equal(expected, fields.Count);
    }

    [Fact]
    public void ShotInfoIsNotEnciphered()
    {
        byte[] raw = new byte[68];
        Encoding.ASCII.GetBytes("2020:01:02 03:04:05\0").CopyTo(raw, 6);
        MakerNoteField date = Core.MakerNotes.SonyMakerNoteArrays.Decompose(0x3000, Undefined(raw), default).Single(f => f.Name == "SonyDateTime");
        byte[] text = new byte[20];
        date.Value.CopyRawTo(text);
        Assert.Equal("2020:01:02 03:04:05\0", Encoding.ASCII.GetString(text));
    }

    [Fact]
    public void Info2010eNeedsAKnownModel()
    {
        byte[] stored = Encipher(new byte[6400]);
        Assert.NotEmpty(Core.MakerNotes.SonyMakerNoteArrays.Decompose(0x2010, Undefined(stored), "NEX-6\0"u8));
        Assert.Empty(Core.MakerNotes.SonyMakerNoteArrays.Decompose(0x2010, Undefined(stored), "ILCE-7M3"u8));
    }
}

public sealed class OlympusMakerNoteTests
{
    private static void U16(byte[] b, int at, int v, bool big) { if (big) BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(at), (ushort)v); else BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), (ushort)v); }
    private static void U32(byte[] b, int at, long v, bool big) { if (big) BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at), (uint)v); else BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), (uint)v); }
    private static void Entry(byte[] b, int at, int tag, int type, long count, long value, bool big) { U16(b, at, tag, big); U16(b, at + 2, type, big); U32(b, at + 4, count, big); U32(b, at + 8, value, big); }

    /// <summary>
    /// TIFF (IFD0 Make "OLYMPUS", ExifIFD) whose MakerNote is "OLYMPUS\0" + mark + 03 00, a main IFD
    /// at 12 with CameraSettings (type 13, IFD) pointing at 32, and a CameraSettings IFD there with
    /// one SHORT entry. All MakerNote offsets count from the MakerNote; its order is <paramref name="innerBig"/>.
    /// </summary>
    private static byte[] Build(bool outerBig, bool innerBig, uint pointer = 32)
    {
        var note = new byte[60];
        "OLYMPUS\0"u8.CopyTo(note);
        note[8] = note[9] = (byte)(innerBig ? 'M' : 'I');
        note[10] = 3;
        U16(note, 12, 1, innerBig);
        Entry(note, 14, 0x2020, 13, 1, pointer, innerBig);
        U32(note, 26, 0, innerBig);
        U16(note, 32, 1, innerBig);
        Entry(note, 34, 0x0100, 3, 1, 0, innerBig);
        U16(note, 42, 7, innerBig);
        U32(note, 46, 0, innerBig);

        const int at = 66;
        var b = new byte[at + note.Length];
        b[0] = b[1] = (byte)(outerBig ? 'M' : 'I');
        U16(b, 2, 42, outerBig); U32(b, 4, 8, outerBig);
        U16(b, 8, 2, outerBig);
        Entry(b, 10, 0x010F, 2, 8, 38, outerBig);
        Entry(b, 22, 0x8769, 4, 1, 48, outerBig);
        "OLYMPUS\0"u8.CopyTo(b.AsSpan(38));
        U16(b, 48, 1, outerBig);
        Entry(b, 50, 0x927C, 7, note.Length, at, outerBig);
        note.CopyTo(b, at);
        return b;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Olympus2SubDirectoriesBecomeFieldsOfTheirPointerTag(bool outerBig, bool innerBig)
    {
        MakerNote? note = ExifTiffReader.Read(Build(outerBig, innerBig)).Metadata.MakerNote;
        Assert.NotNull(note);
        Assert.Equal(MakerNoteFormat.Olympus2, note.Format);
        Assert.True(note.TryGetEntry(0x2020, out MakerNoteEntry? settings));
        Assert.Equal("CameraSettings", settings.Name);
        Assert.Equal(TiffFieldType.Long, settings.Value.FieldType);
        MakerNoteField field = Assert.Single(settings.Fields);
        Assert.Equal((ushort)0x0100, field.Index);
        byte[] raw = new byte[2];
        field.Value.CopyRawTo(raw);
        Assert.Equal(7, innerBig ? BinaryPrimitives.ReadUInt16BigEndian(raw) : BinaryPrimitives.ReadUInt16LittleEndian(raw));
    }

    [Fact]
    public void ASubDirectoryOutsideTheMakerNoteIsLeftUnsplit()
    {
        MakerNote? note = ExifTiffReader.Read(Build(false, false, pointer: 5000)).Metadata.MakerNote;
        Assert.NotNull(note);
        Assert.True(note.TryGetEntry(0x2020, out MakerNoteEntry? settings));
        Assert.Empty(settings.Fields);
    }

    [Fact]
    public void OlympusNameTablesAreSortedAndUnique()
    {
        foreach (var table in new[] { Core.MakerNotes.OlympusMakerNoteNames.MainNames, Core.MakerNotes.OlympusMakerNoteNames.CsNames,
                     Core.MakerNotes.OlympusMakerNoteNames.EqNames, Core.MakerNotes.OlympusMakerNoteNames.IpNames })
        {
            var ids = table.Select(t => t.Id).ToArray();
            Assert.Equal(ids.OrderBy(i => i), ids);
            Assert.Equal(ids.Length, ids.Distinct().Count());
        }
    }
}
