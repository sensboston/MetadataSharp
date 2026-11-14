using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Formats.Tiff;
using MetadataSharp.Tests.Iptc;
using Xunit;

namespace MetadataSharp.Tests.Tiff;

/// <summary>
/// Editing the EXIF, GPS, IPTC and XMP of TIFF-based files without moving image data or the
/// MakerNote. Every case runs on a baseline TIFF, a DNG and a NEF-, CR2-, ARW-, PEF- and ORF-like
/// layout, in both byte orders, and states which bytes of the source may differ in the output.
/// </summary>
public sealed class TiffBasedWriteTests
{
    private static readonly ExifTagKey DateTimeOriginal = new(ExifDirectoryKind.Exif, 0x9003);
    private static readonly ExifTagKey DateTimeDigitized = new(ExifDirectoryKind.Exif, 0x9004);
    private static readonly XmpPropertyName Tool = new("http://ns.adobe.com/xap/1.0/", "CreatorTool");
    private static readonly IptcDatasetKey Caption = new(2, 120);
    private const string NewDate = "2021:06:07 08:09:10";
    private const string LongText = "A considerably longer text than the one before";

    public static TheoryData<string, bool> Layouts()
    {
        var data = new TheoryData<string, bool>();
        foreach (string name in TiffBasedFixture.Names)
        {
            data.Add(name, false);
            data.Add(name, true);
        }
        return data;
    }

    private static Endianness Order(bool big) => big ? Endianness.BigEndian : Endianness.LittleEndian;

    private static ExifValue Ascii(string text, bool big = false) =>
        ExifValue.FromRaw(TiffFieldType.Ascii, (uint)text.Length + 1, Order(big), Encoding.ASCII.GetBytes(text + "\0"));

    /// <summary>Rationals written in little-endian order, whatever the order of the file they go to.</summary>
    private static ExifValue Rationals(params (uint Numerator, uint Denominator)[] values)
    {
        byte[] bytes = new byte[values.Length * 8];
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 8), values[i].Numerator);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 8 + 4), values[i].Denominator);
        }
        return ExifValue.FromRaw(TiffFieldType.Rational, (uint)values.Length, Endianness.LittleEndian, bytes);
    }

    private static uint[] Longs(ExifValue value)
    {
        byte[] raw = Raw(value);
        var numbers = new uint[raw.Length / 4];
        for (int i = 0; i < numbers.Length; i++)
            numbers[i] = value.ByteOrder == Endianness.BigEndian
                ? BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(i * 4))
                : BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(i * 4));
        return numbers;
    }

    private static byte[] Raw(ExifValue value)
    {
        byte[] raw = new byte[value.ByteLength];
        value.CopyRawTo(raw);
        return raw;
    }

    private static string Text(ExifValue value) => Encoding.ASCII.GetString(Raw(value)).TrimEnd('\0', ' ');

    private static string Text(ExifMetadata metadata, ExifTagKey key)
    {
        Assert.True(metadata.TryGetEntry(key, out ExifEntry? entry));
        return Text(entry.Value);
    }

    private static ExifEdit Edit(Action<ExifEdit> change)
    {
        var edit = new ExifEdit();
        change(edit);
        return edit;
    }

    private static ExifEdit Gps() => Edit(edit =>
    {
        edit.Set(new(ExifDirectoryKind.Gps, 0), ExifValue.FromRaw(TiffFieldType.Byte, 4, Endianness.LittleEndian, [2, 3, 0, 0]));
        edit.Set(new(ExifDirectoryKind.Gps, 1), Ascii("N"));
        edit.Set(new(ExifDirectoryKind.Gps, 2), Rationals((42, 1), (22, 1), (3456, 100)));
        edit.Set(new(ExifDirectoryKind.Gps, 3), Ascii("W"));
        edit.Set(new(ExifDirectoryKind.Gps, 4), Rationals((71, 1), (14, 1), (789, 100)));
    });

    private static XmpEdit Xmp(string tool)
    {
        var edit = new XmpEdit();
        edit.Set(Tool, XmpValue.CreateText(tool));
        return edit;
    }

    private static IptcEdit Iptc(string caption)
    {
        var edit = new IptcEdit();
        edit.ReplaceText(Caption, [caption]);
        return edit;
    }

    private static byte[] Save(byte[] file, ExifEdit edit, TiffReadOptions? options = null)
    {
        using TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(file), options);
        using var output = new MemoryStream();
        document.SaveExif(output, edit);
        return output.ToArray();
    }

    private static byte[] Save(byte[] file, XmpEdit edit)
    {
        using TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(file));
        using var output = new MemoryStream();
        document.SaveXmp(output, edit);
        return output.ToArray();
    }

    private static byte[] Save(byte[] file, IptcEdit edit)
    {
        using TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(file));
        using var output = new MemoryStream();
        document.SaveIptc(output, edit);
        return output.ToArray();
    }

    /// <summary>The text of the one 2:120 dataset of the packet.</summary>
    private static string CaptionOf(IptcPacket packet)
    {
        for (int i = 0; i < packet.Datasets.Count; i++)
            if (packet.Datasets[i].Key == Caption) return packet.GetText(i);
        throw new InvalidOperationException("The packet has no caption.");
    }

    internal static MetadataErrorCode Code(Action action) => Assert.Throws<MetadataException>(action).Code;

    /// <summary>The first directory of the file, read from its header.</summary>
    internal static int Primary(byte[] file, bool big) => (int)TiffBasedFixture.U32(file, big, 4);

    /// <summary>The entry of a tag of the primary directory of the file.</summary>
    internal static int PrimaryEntry(byte[] file, bool big, int tag)
    {
        int entry = TiffBasedFixture.EntryAt(file, big, Primary(file, big), tag);
        Assert.True(entry >= 0, $"Tag 0x{tag:X4} is not in the primary directory.");
        return entry;
    }

    /// <summary>
    /// Every byte of the source is in the output where it was, except inside the given ranges, and
    /// inside each of them something did change.
    /// </summary>
    internal static void AssertSourceKeptExcept(byte[] source, byte[] output, params (int Start, int Length)[] allowed)
    {
        Assert.True(output.Length >= source.Length);
        var touched = new bool[allowed.Length];
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == output[i]) continue;
            int range = Array.FindIndex(allowed, a => i >= a.Start && i < a.Start + a.Length);
            Assert.True(range >= 0, $"Byte {i} of the source changed.");
            touched[range] = true;
        }
        Assert.DoesNotContain(false, touched);
    }

    /// <summary>
    /// Image data and the MakerNote hold the bytes they held, at the offsets they had, and the
    /// directories of the output still address them there.
    /// </summary>
    internal static void AssertImageAndMakerNoteUntouched(TiffBasedFixture fixture, byte[] output)
    {
        byte[] source = fixture.Bytes;
        bool big = fixture.Big;
        foreach ((int start, int length) in fixture.Image)
            Assert.True(source.AsSpan(start, length).SequenceEqual(output.AsSpan(start, length)), $"Image data at {start} changed.");
        if (fixture.Note.Length != 0)
            Assert.True(source.AsSpan(fixture.Note.Start, fixture.Note.Length)
                .SequenceEqual(output.AsSpan(fixture.Note.Start, fixture.Note.Length)), "The MakerNote changed.");
        if (fixture.PrivateData.Length != 0)
            Assert.True(source.AsSpan(fixture.PrivateData.Start, fixture.PrivateData.Length)
                .SequenceEqual(output.AsSpan(fixture.PrivateData.Start, fixture.PrivateData.Length)), "DNGPrivateData changed.");

        ExifMetadata before = TiffExif.Read(source), after = TiffExif.Read(output);
        foreach (ExifEntry entry in before.Entries)
        {
            if (entry.Key.TagId is not (0x0111 or 0x0117 or 0x014A or 0x0201 or 0x0202 or 0x927C or 0xC634)) continue;
            Assert.True(after.TryGetEntry(entry.Key, out ExifEntry? same));
            Assert.Equal(Raw(entry.Value), Raw(same.Value));
        }
        Assert.Equal(before.MakerNote?.Offset, after.MakerNote?.Offset);
        Assert.Equal(before.MakerNote?.Entries.Count, after.MakerNote?.Entries.Count);

        // The same, without the reader: the entries of the output, wherever its directories now are.
        int primary = Primary(output, big);
        int strips = TiffBasedFixture.EntryAt(source, big, fixture.Primary, 0x0111);
        if (strips >= 0)
            Assert.Equal(TiffBasedFixture.U32(source, big, strips + 8), TiffBasedFixture.U32(output, big, PrimaryEntry(output, big, 0x0111) + 8));
        int exif = (int)TiffBasedFixture.U32(output, big, PrimaryEntry(output, big, 0x8769) + 8);
        if (fixture.Note.Length != 0)
        {
            int note = TiffBasedFixture.EntryAt(output, big, exif, 0x927C);
            Assert.True(note >= 0);
            Assert.Equal(fixture.Note.Length, TiffBasedFixture.U32(output, big, note + 4));
            Assert.Equal(fixture.Note.Start, TiffBasedFixture.U32(output, big, note + 8));
        }
        if (fixture.PrivateData.Length != 0)
        {
            int privateData = PrimaryEntry(output, big, 0xC634);
            Assert.True(privateData >= 0);
            Assert.Equal(fixture.PrivateData.Length, TiffBasedFixture.U32(output, big, privateData + 4));
            Assert.Equal(fixture.PrivateData.Start, TiffBasedFixture.U32(output, big, privateData + 8));
        }
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void AValueOfTheSameLengthIsWrittenWhereTheOldOneIs(string name, bool big)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Create(name, big);
        byte[] output = Save(fixture.Bytes, Edit(edit => edit.Set(DateTimeOriginal, Ascii(NewDate))));

        Assert.Equal(fixture.Bytes.Length, output.Length);
        AssertSourceKeptExcept(fixture.Bytes, output, (fixture.Date, 20));
        Assert.Equal(NewDate + "\0", Encoding.ASCII.GetString(output, fixture.Date, 20));
        Assert.Equal(NewDate, Text(TiffExif.Read(output), DateTimeOriginal));
        AssertImageAndMakerNoteUntouched(fixture, output);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void AShorterValueStaysInTheSlotOfTheOldOne(string name, bool big)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Create(name, big);
        var key = new ExifTagKey(ExifDirectoryKind.Ifd0, (ushort)fixture.TextTag);
        byte[] output = Save(fixture.Bytes, Edit(edit => edit.Set(key, Ascii("Short"))));

        int entry = PrimaryEntry(fixture.Bytes, big, fixture.TextTag);
        Assert.Equal(fixture.Bytes.Length, output.Length);
        AssertSourceKeptExcept(fixture.Bytes, output, (entry, 12), (fixture.Text.Start, 6));
        Assert.Equal(6, TiffBasedFixture.U32(output, big, entry + 4));
        Assert.Equal(fixture.Text.Start, TiffBasedFixture.U32(output, big, entry + 8));
        Assert.Equal("Short", Text(TiffExif.Read(output), key));
        AssertImageAndMakerNoteUntouched(fixture, output);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void ALongerValueIsAppendedAndTheOldBytesStay(string name, bool big)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Create(name, big);
        var key = new ExifTagKey(ExifDirectoryKind.Ifd0, (ushort)fixture.TextTag);
        byte[] output = Save(fixture.Bytes, Edit(edit => edit.Set(key, Ascii(LongText))));

        // The directory keeps its entry count, so it stays where it is and one entry changes.
        int entry = PrimaryEntry(fixture.Bytes, big, fixture.TextTag);
        AssertSourceKeptExcept(fixture.Bytes, output, (entry, 12));
        Assert.Equal(fixture.Primary, Primary(output, big));
        long value = TiffBasedFixture.U32(output, big, entry + 8);
        Assert.Equal(fixture.Bytes.Length, value);
        Assert.Equal(fixture.Bytes.Length + LongText.Length + 1, output.Length);
        Assert.Equal(LongText + "\0", Encoding.ASCII.GetString(output, (int)value, LongText.Length + 1));
        Assert.Equal(LongText, Text(TiffExif.Read(output), key));
        AssertImageAndMakerNoteUntouched(fixture, output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppendedDataStartsAtAnEvenOffset(bool big)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Tiff(big);
        byte[] odd = [.. fixture.Bytes, 0xEE];
        var key = new ExifTagKey(ExifDirectoryKind.Ifd0, 0x010F);
        byte[] output = Save(odd, Edit(edit => edit.Set(key, Ascii(LongText))));

        int entry = PrimaryEntry(odd, big, 0x010F);
        AssertSourceKeptExcept(odd, output, (entry, 12));
        Assert.Equal(odd.Length + 1, TiffBasedFixture.U32(output, big, entry + 8));
        Assert.Equal(0, output[odd.Length]);
        Assert.Equal(LongText, Text(TiffExif.Read(output), key));
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void ATagAddedToTheExifDirectoryCopiesItToTheEndAndLeavesTheMakerNoteWhereItIs(string name, bool big)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Create(name, big);
        byte[] output = Save(fixture.Bytes, Edit(edit => edit.Set(DateTimeDigitized, Ascii(NewDate))));

        // The primary directory keeps its place; its pointer to the Exif directory is all that changes.
        int pointer = PrimaryEntry(fixture.Bytes, big, 0x8769);
        AssertSourceKeptExcept(fixture.Bytes, output, (pointer + 8, 4));
        int exif = (int)TiffBasedFixture.U32(output, big, pointer + 8);
        Assert.True(exif >= fixture.Bytes.Length);
        Assert.Equal(TiffBasedFixture.U16(fixture.Bytes, big, fixture.Exif) + 1, TiffBasedFixture.U16(output, big, exif));

        ExifMetadata before = TiffExif.Read(fixture.Bytes), after = TiffExif.Read(output);
        Assert.Equal(NewDate, Text(after, DateTimeDigitized));
        Assert.Equal("2020:01:02 03:04:05", Text(after, DateTimeOriginal));
        Assert.Equal(before.Entries.Count + 1, after.Entries.Count);
        AssertImageAndMakerNoteUntouched(fixture, output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheMakerNoteOfANefLikeFileReadsTheSameAfterItsDirectoryMoved(bool big)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Nef(big);
        MakerNote? before = TiffExif.Read(fixture.Bytes).MakerNote;
        Assert.NotNull(before);
        Assert.Equal(MakerNoteFormat.Nikon3, before.Format);

        MakerNote? after = TiffExif.Read(Save(fixture.Bytes, Edit(edit => edit.Set(DateTimeDigitized, Ascii(NewDate))))).MakerNote;
        Assert.NotNull(after);
        Assert.Equal(MakerNoteFormat.Nikon3, after.Format);
        Assert.Equal(fixture.Note.Start, after.Offset);
        Assert.Equal(before.Entries.Select(entry => entry.TagId), after.Entries.Select(entry => entry.TagId));
        Assert.True(after.TryGetEntry(0x0004, out MakerNoteEntry? quality));
        Assert.Equal("FINE", Text(quality.Value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheMakerNoteOfACr2LikeFileReadsTheSameAfterItsDirectoryMoved(bool big)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Cr2(big);
        MakerNote? before = TiffExif.Read(fixture.Bytes).MakerNote;
        Assert.NotNull(before);
        Assert.Equal(MakerNoteFormat.Canon, before.Format);

        // Canon counts the offsets inside the note from the start of the file: they hold only
        // while the note stays where it is.
        MakerNote? after = TiffExif.Read(Save(fixture.Bytes, Edit(edit => edit.Set(DateTimeDigitized, Ascii(NewDate))))).MakerNote;
        Assert.NotNull(after);
        Assert.Equal(fixture.Note.Start, after.Offset);
        Assert.Equal(before.Entries.Select(entry => entry.TagId), after.Entries.Select(entry => entry.TagId));
        Assert.True(after.TryGetEntry(0x0006, out MakerNoteEntry? imageType));
        Assert.Equal("EOS 20D", Text(imageType.Value));
    }

    [Theory]
    [InlineData("arw", false, MakerNoteFormat.Sony1)]
    [InlineData("arw", true, MakerNoteFormat.Sony1)]
    [InlineData("pef", false, MakerNoteFormat.Pentax)]
    [InlineData("pef", true, MakerNoteFormat.Pentax)]
    [InlineData("orf", false, MakerNoteFormat.Olympus)]
    [InlineData("orf", true, MakerNoteFormat.Olympus)]
    public void TheMakerNoteOfAnArwPefOrOrfLikeFileReadsTheSameAfterItsDirectoryMoved(string name, bool big, MakerNoteFormat format)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Create(name, big);
        MakerNote? before = TiffExif.Read(fixture.Bytes).MakerNote;
        Assert.NotNull(before);
        Assert.Equal(format, before.Format);

        // These notes count their offsets from the start of the file, as Canon's does: the text in
        // the note is found only while the note is where it was.
        byte[] output = Save(fixture.Bytes, Edit(edit => edit.Set(DateTimeDigitized, Ascii(NewDate))));
        Assert.Equal(fixture.Bytes.AsSpan(0, 4).ToArray(), output.AsSpan(0, 4).ToArray());
        MakerNote? after = TiffExif.Read(output).MakerNote;
        Assert.NotNull(after);
        Assert.Equal(format, after.Format);
        Assert.Equal(fixture.Note.Start, after.Offset);
        Assert.Equal(before.Entries.Select(entry => entry.TagId), after.Entries.Select(entry => entry.TagId));
        Assert.True(after.TryGetEntry(0x7F01, out MakerNoteEntry? text));
        Assert.Equal("NOTE OK", Text(text.Value));
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void AddingGpsWritesItsDirectoryAtTheEnd(string name, bool big)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Create(name, big);
        byte[] output = Save(fixture.Bytes, Gps());

        if (fixture.Gps != 0)
        {
            // There is a GPS directory and it gains tags: a copy at the end, one pointer changed.
            AssertSourceKeptExcept(fixture.Bytes, output, (PrimaryEntry(fixture.Bytes, big, 0x8825) + 8, 4));
            Assert.Equal(fixture.Primary, Primary(output, big));
        }
        else
        {
            // The primary directory gains the pointer, so it is copied too and the header leads to the copy.
            AssertSourceKeptExcept(fixture.Bytes, output, (4, 4));
            Assert.True(Primary(output, big) >= fixture.Bytes.Length);
            Assert.Equal(TiffBasedFixture.U16(fixture.Bytes, big, fixture.Primary) + 1, TiffBasedFixture.U16(output, big, Primary(output, big)));
        }
        int gps = (int)TiffBasedFixture.U32(output, big, PrimaryEntry(output, big, 0x8825) + 8);
        Assert.True(gps >= fixture.Bytes.Length);
        Assert.Equal(5, TiffBasedFixture.U16(output, big, gps));

        ExifMetadata after = TiffExif.Read(output);
        Assert.Equal(Order(big), after.ByteOrder);
        Assert.Equal("N", Text(after, new(ExifDirectoryKind.Gps, 1)));
        Assert.Equal("W", Text(after, new(ExifDirectoryKind.Gps, 3)));
        Assert.True(after.TryGetEntry(new(ExifDirectoryKind.Gps, 2), out ExifEntry? latitude));
        Assert.Equal(new uint[] { 42, 1, 22, 1, 3456, 100 }, Longs(latitude.Value));
        Assert.True(after.TryGetEntry(new(ExifDirectoryKind.Gps, 4), out ExifEntry? longitude));
        Assert.Equal(new uint[] { 71, 1, 14, 1, 789, 100 }, Longs(longitude.Value));
        Assert.True(after.TryGetEntry(new(ExifDirectoryKind.Gps, 0), out ExifEntry? version));
        Assert.Equal(new byte[] { 2, 3, 0, 0 }, Raw(version.Value));
        Assert.Equal("2020:01:02 03:04:05", Text(after, DateTimeOriginal));
        AssertImageAndMakerNoteUntouched(fixture, output);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void AddingXmpStoresThePacketAsTag700(string name, bool big)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Create(name, big);
        using (TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(fixture.Bytes)))
            Assert.Null(document.ReadXmp());
        byte[] output = Save(fixture.Bytes, Xmp("MetadataSharp"));

        AssertSourceKeptExcept(fixture.Bytes, output, (4, 4));
        int entry = PrimaryEntry(output, big, 0x02BC);
        Assert.Equal(1, TiffBasedFixture.U16(output, big, entry + 2));
        long length = TiffBasedFixture.U32(output, big, entry + 4), at = TiffBasedFixture.U32(output, big, entry + 8);
        Assert.True(at >= fixture.Bytes.Length && at + length <= output.Length);
        Assert.Contains("MetadataSharp", Encoding.UTF8.GetString(output, (int)at, (int)length));

        using (TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(output)))
        {
            Assert.Equal("MetadataSharp", document.ReadXmp()!.Properties[Tool].Text);
            Assert.Equal(TiffExif.Read(fixture.Bytes).Entries.Count + 1, document.ReadExif().Entries.Count);
        }
        AssertImageAndMakerNoteUntouched(fixture, output);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void AddingIptcStoresTheStreamAsTag83BB(string name, bool big)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Create(name, big);
        using (TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(fixture.Bytes)))
            Assert.Null(document.ReadIptc());
        byte[] output = Save(fixture.Bytes, Iptc("A caption"));

        AssertSourceKeptExcept(fixture.Bytes, output, (4, 4));
        int entry = PrimaryEntry(output, big, 0x83BB);
        Assert.Equal(7, TiffBasedFixture.U16(output, big, entry + 2));
        Assert.True(TiffBasedFixture.U32(output, big, entry + 8) >= fixture.Bytes.Length);

        using (TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(output)))
        {
            Assert.Equal("A caption", CaptionOf(document.ReadIptc()!));
        }
        AssertImageAndMakerNoteUntouched(fixture, output);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void ASecondIdenticalEditChangesNothing(string name, bool big)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Create(name, big);
        var key = new ExifTagKey(ExifDirectoryKind.Ifd0, (ushort)fixture.TextTag);
        ExifEdit exif = Edit(edit =>
        {
            edit.Set(key, Ascii(LongText));
            edit.Set(DateTimeOriginal, Ascii(NewDate));
            edit.Set(DateTimeDigitized, Ascii(NewDate));
        });

        byte[] once = Save(fixture.Bytes, exif);
        Assert.Equal(once, Save(once, exif));

        once = Save(once, Gps());
        Assert.Equal(once, Save(once, Gps()));

        once = Save(once, Xmp("MetadataSharp"));
        Assert.Equal(once, Save(once, Xmp("MetadataSharp")));

        once = Save(once, Iptc("A caption"));
        Assert.Equal(once, Save(once, Iptc("A caption")));

        // Four saves later every kind of metadata is there, and the image and the note are where they were.
        using TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(once));
        ExifMetadata metadata = document.ReadExif();
        Assert.Equal(LongText, Text(metadata, key));
        Assert.Equal(NewDate, Text(metadata, DateTimeDigitized));
        Assert.Equal("W", Text(metadata, new(ExifDirectoryKind.Gps, 3)));
        Assert.Equal("MetadataSharp", document.ReadXmp()!.Properties[Tool].Text);
        Assert.Equal("A caption", CaptionOf(document.ReadIptc()!));
        AssertImageAndMakerNoteUntouched(fixture, once);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void AnEditThatChangesNothingWritesACopyOfTheSource(string name, bool big)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Create(name, big);
        Assert.Equal(fixture.Bytes, Save(fixture.Bytes, new ExifEdit()));
        Assert.Equal(fixture.Bytes, Save(fixture.Bytes, new XmpEdit()));
        Assert.Equal(fixture.Bytes, Save(fixture.Bytes, new IptcEdit()));
        // The value the file already has, a tag it does not have, a packet it does not have.
        Assert.Equal(fixture.Bytes, Save(fixture.Bytes, Edit(edit => edit.Set(DateTimeOriginal, Ascii("2020:01:02 03:04:05")))));
        Assert.Equal(fixture.Bytes, Save(fixture.Bytes, Edit(edit => edit.Remove(new(ExifDirectoryKind.Ifd0, 0x8298)))));
        var removeXmp = new XmpEdit();
        removeXmp.RemoveAllXmp();
        Assert.Equal(fixture.Bytes, Save(fixture.Bytes, removeXmp));
        var removeIptc = new IptcEdit();
        removeIptc.RemoveAllIptc();
        Assert.Equal(fixture.Bytes, Save(fixture.Bytes, removeIptc));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovingATagRewritesItsDirectoryInPlace(bool big)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Tiff(big);
        var exposure = new ExifTagKey(ExifDirectoryKind.Exif, 0x829A);
        byte[] output = Save(fixture.Bytes, Edit(edit => edit.Remove(exposure)));

        Assert.Equal(fixture.Bytes.Length, output.Length);
        AssertSourceKeptExcept(fixture.Bytes, output, (fixture.Exif, 30));
        Assert.Equal(1, TiffBasedFixture.U16(output, big, fixture.Exif));
        ExifMetadata after = TiffExif.Read(output);
        Assert.False(after.TryGetEntry(exposure, out _));
        Assert.Equal("2020:01:02 03:04:05", Text(after, DateTimeOriginal));
        AssertImageAndMakerNoteUntouched(fixture, output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovingTheLastTagOfADirectoryRemovesItsPointer(bool big)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Tiff(big);
        byte[] output = Save(fixture.Bytes, Edit(edit =>
        {
            edit.Remove(new(ExifDirectoryKind.Exif, 0x829A));
            edit.Remove(DateTimeOriginal);
        }));

        // Only the primary directory is written: the Exif directory stays, with nothing leading to it.
        Assert.Equal(fixture.Bytes.Length, output.Length);
        AssertSourceKeptExcept(fixture.Bytes, output, (8, 114));
        Assert.Equal(8, TiffBasedFixture.U16(output, big, 8));
        Assert.Equal(-1, TiffBasedFixture.EntryAt(output, big, 8, 0x8769));
        ExifMetadata after = TiffExif.Read(output);
        Assert.DoesNotContain(after.Entries, entry => entry.Key.Directory == ExifDirectoryKind.Exif);
        Assert.Equal("TestCompany", Text(after, new(ExifDirectoryKind.Ifd0, 0x010F)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnExifDirectoryIsCreatedForAFileWithoutOne(bool big)
    {
        byte[] file = TiffBasedFixture.Flat(big, (0x010F, 2, Encoding.ASCII.GetBytes("TestCompany\0")));
        byte[] output = Save(file, Edit(edit => edit.Set(DateTimeOriginal, Ascii(NewDate))));

        AssertSourceKeptExcept(file, output, (4, 4));
        ExifMetadata after = TiffExif.Read(output);
        Assert.Equal(NewDate, Text(after, DateTimeOriginal));
        Assert.Equal("TestCompany", Text(after, new(ExifDirectoryKind.Ifd0, 0x010F)));
        Assert.Equal(output, Save(output, Edit(edit => edit.Set(DateTimeOriginal, Ascii(NewDate)))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnXmpPacketThatIsThereIsEditedAndKeepsItsOtherProperties(bool big)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Tiff(big);
        byte[] first = Save(fixture.Bytes, Xmp("first"));
        var rights = new XmpPropertyName("http://ns.adobe.com/xap/1.0/", "Label");
        int entry = PrimaryEntry(first, big, 0x02BC);

        // A new packet carries padding, so a small change is written where the packet is: the file
        // keeps its length and the entry is not touched.
        var small = new XmpEdit();
        small.Set(rights, XmpValue.CreateText("short"));
        byte[] inPlace = Save(first, small);
        Assert.Equal(first.Length, inPlace.Length);
        Assert.Equal(first.AsSpan(entry, 12).ToArray(), inPlace.AsSpan(entry, 12).ToArray());
        using (TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(inPlace)))
        {
            XmpPacket packet = document.ReadXmp()!;
            Assert.Equal("first", packet.Properties[Tool].Text);
            Assert.Equal("short", packet.Properties[rights].Text);
        }

        string label = new('l', 3000);
        var edit = new XmpEdit();
        edit.Set(rights, XmpValue.CreateText(label));
        byte[] second = Save(first, edit);

        // The packet outgrew its padding: the new one is at the end, the directory stays, one entry changes.
        AssertSourceKeptExcept(first, second, (entry, 12));
        Assert.True(TiffBasedFixture.U32(second, big, entry + 8) >= first.Length);
        using (TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(second)))
        {
            XmpPacket packet = document.ReadXmp()!;
            Assert.Equal("first", packet.Properties[Tool].Text);
            Assert.Equal(label, packet.Properties[rights].Text);
        }

        var removeAll = new XmpEdit();
        removeAll.RemoveAllXmp();
        byte[] third = Save(second, removeAll);
        Assert.Equal(second.Length, third.Length);
        Assert.Equal(-1, TiffBasedFixture.EntryAt(third, big, Primary(third, big), 0x02BC));
        using (TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(third)))
            Assert.Null(document.ReadXmp());
        AssertImageAndMakerNoteUntouched(fixture, third);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnIptcStreamStoredAsLongsStaysLongsAndIsPaddedToFourBytes(bool big)
    {
        // 2:120 "hello" is ten bytes: stored as LONGs it is three of them, the last two bytes zero.
        byte[] stream = [0x1C, 2, 120, 0, 5, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o', 0, 0];
        byte[] file = TiffBasedFixture.Flat(big, (0x010F, 2, Encoding.ASCII.GetBytes("TestCompany\0")), (0x83BB, 4, stream));
        byte[] output = Save(file, Iptc("a caption of another length"));

        int entry = PrimaryEntry(output, big, 0x83BB);
        Assert.Equal(4, TiffBasedFixture.U16(output, big, entry + 2));
        long count = TiffBasedFixture.U32(output, big, entry + 4), at = TiffBasedFixture.U32(output, big, entry + 8);
        Assert.Equal(file.Length, at);
        Assert.Equal(output.Length, at + count * 4);
        // The directory stays; the stream that was there is still in the file, unaddressed.
        AssertSourceKeptExcept(file, output, (PrimaryEntry(file, big, 0x83BB), 12));

        Assert.Equal("a caption of another length", CaptionOf(TiffIptc.Read(output)!));
        Assert.Equal(output, Save(output, Iptc("a caption of another length")));

        // A shorter stream goes back into the slot it fits.
        byte[] shorter = Save(output, Iptc("hi"));
        Assert.Equal(output.Length, shorter.Length);
        Assert.Equal(at, TiffBasedFixture.U32(shorter, big, entry + 8));
        Assert.Equal("hi", CaptionOf(TiffIptc.Read(shorter)!));
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void ATagADatasetAndAPropertyAreRemovedAndRemovingAllIptcRemovesItsTag(string name, bool big)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Create(name, big);

        // The tag that was added is taken out again: the directory is rewritten where it is.
        byte[] added = Save(fixture.Bytes, Edit(edit => edit.Set(DateTimeDigitized, Ascii(NewDate))));
        byte[] removed = Save(added, Edit(edit => edit.Remove(DateTimeDigitized)));
        Assert.Equal(added.Length, removed.Length);
        ExifMetadata exif = TiffExif.Read(removed);
        Assert.False(exif.TryGetEntry(DateTimeDigitized, out _));
        Assert.Equal("2020:01:02 03:04:05", Text(exif, DateTimeOriginal));
        Assert.Equal(TiffExif.Read(fixture.Bytes).Entries.Count, exif.Entries.Count);
        AssertImageAndMakerNoteUntouched(fixture, removed);

        var title = new IptcDatasetKey(2, 5);
        var both = new IptcEdit();
        both.ReplaceText(Caption, ["A caption"]);
        both.ReplaceText(title, ["A title"]);
        byte[] file = Save(fixture.Bytes, both);

        // Without the title the stream is shorter: it is written where the longer one was.
        var removeTitle = new IptcEdit();
        removeTitle.Remove(title);
        byte[] one = Save(file, removeTitle);
        Assert.Equal(file.Length, one.Length);
        IptcPacket packet = TiffIptc.Read(one)!;
        Assert.Equal("A caption", CaptionOf(packet));
        Assert.DoesNotContain(packet.Datasets, dataset => dataset.Key == title);

        var removeAll = new IptcEdit();
        removeAll.RemoveAllIptc();
        byte[] none = Save(one, removeAll);
        Assert.Equal(one.Length, none.Length);
        Assert.Equal(-1, TiffBasedFixture.EntryAt(none, big, Primary(none, big), 0x83BB));
        Assert.Null(TiffIptc.Read(none));
        AssertImageAndMakerNoteUntouched(fixture, none);

        var label = new XmpPropertyName("http://ns.adobe.com/xap/1.0/", "Label");
        var two = new XmpEdit();
        two.Set(Tool, XmpValue.CreateText("MetadataSharp"));
        two.Set(label, XmpValue.CreateText("a label"));
        var removeLabel = new XmpEdit();
        removeLabel.Remove(label);
        byte[] oneProperty = Save(Save(fixture.Bytes, two), removeLabel);
        using TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(oneProperty));
        XmpPacket xmp = document.ReadXmp()!;
        Assert.Equal("MetadataSharp", xmp.Properties[Tool].Text);
        Assert.False(xmp.Properties.ContainsKey(label));
        AssertImageAndMakerNoteUntouched(fixture, oneProperty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFileWithAValueLargerThanAJpegSegmentIsEditedWithTheDefaultOptions(bool big)
    {
        // An ICC profile of 70,000 bytes. The value budget of MetadataLimits is the 65,527 bytes of
        // a JPEG APP1 payload; ReaganLargeTiff.tiff of the reference corpus has a profile of 1,613,600.
        byte[] profile = new byte[70_000];
        for (int i = 0; i < profile.Length; i++) profile[i] = (byte)(1 + i % 251);
        byte[] file = TiffBasedFixture.Flat(big, (0x010F, 2, Encoding.ASCII.GetBytes("TestCompany\0")), (0x8773, 7, profile));
        var make = new ExifTagKey(ExifDirectoryKind.Ifd0, 0x010F);
        int makeAt = (int)TiffBasedFixture.U32(file, big, PrimaryEntry(file, big, 0x010F) + 8);
        int profileAt = (int)TiffBasedFixture.U32(file, big, PrimaryEntry(file, big, 0x8773) + 8);

        byte[] output = Save(file, Edit(edit => edit.Set(make, Ascii("Other Maker"))));
        Assert.Equal(file.Length, output.Length);
        AssertSourceKeptExcept(file, output, (makeAt, 12));
        Assert.Equal("Other Maker", Text(TiffExif.Read(output), make));

        byte[] grown = Save(file, Edit(edit => edit.Set(make, Ascii(LongText))));
        Assert.True(profile.AsSpan().SequenceEqual(grown.AsSpan(profileAt, profile.Length)));
        Assert.Equal(profileAt, TiffBasedFixture.U32(grown, big, PrimaryEntry(grown, big, 0x8773) + 8));
        Assert.Equal(LongText, Text(TiffExif.Read(grown), make));

        // Budgets the caller names are the budgets: those of an EXIF block refuse the file.
        var narrow = new TiffReadOptions { Limits = new MetadataLimits() };
        Assert.Equal(MetadataErrorCode.LimitExceeded, Code(() => Save(file, Edit(edit => edit.Set(make, Ascii("Other Maker"))), narrow)));
    }

    [Fact]
    public void IptcThatLivesInPhotoshopResourcesIsEditedThereAndNotShadowedByANewTag()
    {
        byte[] stream = [0x1C, 2, 120, 0, 5, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o'];
        byte[] other = IptcCodecTests.Block(0x0424, [1, 2, 3], [65, 66], 0x7e);
        byte[] digest = IptcCodecTests.Block(0x0425, new byte[16]);
        byte[] block = [.. other, .. IptcCodecTests.Block(0x0404, stream, [78]), .. digest];
        byte[] file = TiffBasedFixture.Flat(false, (0x010F, 2, Encoding.ASCII.GetBytes("TestCompany\0")), (0x8649, 1, block));
        ExifTagKey naa = new(ExifDirectoryKind.Ifd0, 0x83BB), held = new(ExifDirectoryKind.Ifd0, 0x8649);
        static PhotoshopResources Resources(byte[] tiff, ExifTagKey key)
        {
            Assert.True(TiffExif.Read(tiff).TryGetEntry(key, out ExifEntry? entry));
            return PhotoshopResources.Parse([.. "Photoshop 3.0\0"u8.ToArray(), .. entry!.Value.RawBytes.ToArray()], new());
        }

        Assert.Equal("hello", TiffIptc.Read(file)!.Caption);
        // The stream is replaced inside the resources: no 0x83BB tag appears beside it, every other
        // resource keeps its bytes and its place, and the digest is left as it was.
        byte[] output = Save(file, Iptc("other"));
        Assert.Equal("other", TiffIptc.Read(output)!.Caption);
        Assert.False(TiffExif.Read(output).TryGetEntry(naa, out _));
        PhotoshopResources edited = Resources(output, held);
        Assert.Equal(new ushort[] { 0x0424, 0x0404, 0x0425 }, edited.Resources.Select(r => r.Id));
        Assert.Equal(other, edited.Resources[0].Block.ToArray());
        Assert.Equal(digest, edited.Resources[2].Block.ToArray());
        Assert.Equal(IptcCodecTests.Block(0x0404, stream, [78])[..8], edited.Resources[1].NameHeader.ToArray());
        // The option recomputes the digest.
        using (TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(file)))
        {
            using var updated = new MemoryStream();
            document.SaveIptc(updated, Iptc("other"), new IptcWriteOptions { UpdateDigest = true });
            PhotoshopResources fresh = Resources(updated.ToArray(), held);
            Assert.Equal(System.Security.Cryptography.MD5.HashData(fresh.Iptc!.Data.Span), fresh.Resources[2].Data.ToArray());
        }
        // An edit that changes nothing is a copy; removal takes the stream and its digest out and keeps the rest.
        Assert.Equal(file, Save(file, Iptc("hello")));
        var remove = new IptcEdit(); remove.RemoveAllIptc();
        byte[] removed = Save(file, remove);
        Assert.Null(TiffIptc.Read(removed));
        Assert.Equal(other, Assert.Single(Resources(removed, held).Resources).Block.ToArray());
        // The other kinds of metadata of such a file are edited.
        Assert.Equal(LongText, Text(TiffExif.Read(Save(file, Edit(edit => edit.Set(new(ExifDirectoryKind.Ifd0, 0x010F), Ascii(LongText))))),
            new(ExifDirectoryKind.Ifd0, 0x010F)));
    }

    [Fact]
    public void AFileWhoseRawDataRunsToItsEndIsEditedInPlaceAndNeverAppendedTo()
    {
        TiffBasedFixture fixture = TiffBasedFixture.Rw2();
        byte[] output = Save(fixture.Bytes, Edit(edit => edit.Set(DateTimeOriginal, Ascii(NewDate))));
        Assert.Equal(fixture.Bytes.Length, output.Length);
        AssertSourceKeptExcept(fixture.Bytes, output, (fixture.Date, 20));
        Assert.Equal(NewDate, Text(TiffExif.Read(output), DateTimeOriginal));

        // A tag is removed in place as well: its directory is rewritten where it is.
        var make = new ExifTagKey(ExifDirectoryKind.Ifd0, 0x010F);
        byte[] removed = Save(fixture.Bytes, Edit(edit => edit.Remove(make)));
        Assert.Equal(fixture.Bytes.Length, removed.Length);
        Assert.True(fixture.Bytes.AsSpan(200, 100).SequenceEqual(removed.AsSpan(200, 100)));
        Assert.False(TiffExif.Read(removed).TryGetEntry(make, out _));
        Assert.Equal("2020:01:02 03:04:05", Text(TiffExif.Read(removed), DateTimeOriginal));

        // Anything that grows would be appended, and the raw data would then run over it.
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, Code(() => Save(fixture.Bytes, Edit(edit => edit.Set(DateTimeDigitized, Ascii(NewDate))))));
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, Code(() => Save(fixture.Bytes, Gps())));
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, Code(() => Save(fixture.Bytes, Xmp("MetadataSharp"))));
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, Code(() => Save(fixture.Bytes, Iptc("A caption"))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFileThatAddressesBytesPastItsEndIsEditedInPlaceAndNeverAppendedTo(bool big)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Tiff(big, stripLength: 1000);
        byte[] output = Save(fixture.Bytes, Edit(edit => edit.Set(DateTimeOriginal, Ascii(NewDate))));
        AssertSourceKeptExcept(fixture.Bytes, output, (fixture.Date, 20));
        Assert.Equal(fixture.Bytes.Length, output.Length);

        var key = new ExifTagKey(ExifDirectoryKind.Ifd0, 0x010F);
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, Code(() => Save(fixture.Bytes, Edit(edit => edit.Set(key, Ascii(LongText))))));
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, Code(() => Save(fixture.Bytes, Xmp("MetadataSharp"))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADirectoryReachedFromTwoPointersIsEditedInPlaceAndNeverCopied(bool big)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Dng(big, sharedExif: true);
        byte[] output = Save(fixture.Bytes, Edit(edit => edit.Set(DateTimeOriginal, Ascii(NewDate))));
        AssertSourceKeptExcept(fixture.Bytes, output, (fixture.Date, 20));

        // A copy would leave the other pointer at the old directory.
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, Code(() => Save(fixture.Bytes, Edit(edit => edit.Set(DateTimeDigitized, Ascii(NewDate))))));
    }

    [Fact]
    public void LayoutsTheWriterCannotAccountForAreReadAndNotWritten()
    {
        // A field type of unknown width: the bytes it addresses are unknown.
        TiffBasedFixture unknown = TiffBasedFixture.Tiff(false, extraType: 99);
        using (TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(unknown.Bytes)))
            Assert.Equal("2020:01:02 03:04:05", Text(document.ReadExif(), DateTimeOriginal));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Code(() => Save(unknown.Bytes, Edit(edit => edit.Set(DateTimeOriginal, Ascii(NewDate))))));
        // An edit that changes nothing still copies such a file.
        Assert.Equal(unknown.Bytes, Save(unknown.Bytes, new ExifEdit()));

        byte[] bigTiff = TiffBasedFixture.Tiff(false).Bytes;
        bigTiff[2] = 43;
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Code(() => Save(bigTiff, Edit(edit => edit.Set(DateTimeOriginal, Ascii(NewDate))))));

        Assert.Equal(MetadataErrorCode.InvalidTiff, Code(() => TiffBasedMetadataDocument.Open(new MemoryStream(new byte[64]))));
        Assert.Equal(MetadataErrorCode.InvalidTiff, Code(() => TiffBasedMetadataDocument.Open(new MemoryStream([0x49, 0x49, 42, 0]))));
    }

    [Fact]
    public void RemovingAllExifIsRefused()
    {
        var edit = new ExifEdit();
        edit.RemoveAllExif();
        foreach (string name in TiffBasedFixture.Names)
            Assert.Equal(MetadataErrorCode.UnsupportedFeature, Code(() => Save(TiffBasedFixture.Create(name, false).Bytes, edit)));
    }

    [Fact]
    public void AnOutputOverTheInputBudgetIsRefusedAndAnEditInPlaceIsNot()
    {
        TiffBasedFixture fixture = TiffBasedFixture.Nef(false);
        var options = new TiffReadOptions { Limits = new MetadataLimits { MaxInputBytes = fixture.Bytes.Length } };
        Assert.Equal(MetadataErrorCode.LimitExceeded, Code(() => Save(fixture.Bytes, Gps(), options)));
        byte[] output = Save(fixture.Bytes, Edit(edit => edit.Set(DateTimeOriginal, Ascii(NewDate))), options);
        Assert.Equal(fixture.Bytes.Length, output.Length);

        Assert.Equal(MetadataErrorCode.LimitExceeded, Code(() => TiffBasedMetadataDocument.Open(new MemoryStream(fixture.Bytes),
            new TiffReadOptions { Limits = new MetadataLimits { MaxInputBytes = fixture.Bytes.Length - 1 } })));
    }

    [Fact]
    public void PointersImageDataAddressesAndTheMakerNoteAreNotEditable()
    {
        TiffBasedFixture fixture = TiffBasedFixture.Nef(false);
        var limits = new MetadataLimits();
        TiffBasedFileMap map = TiffBasedFileMap.Read(fixture.Bytes, limits, 16_384);
        var value = new TiffBasedChange(4, 1, [1, 0, 0, 0]);
        foreach ((TiffBasedRole role, ushort tag) in new (TiffBasedRole, ushort)[]
                 {
                     (TiffBasedRole.Primary, 0x0111), (TiffBasedRole.Primary, 0x0117), (TiffBasedRole.Primary, 0x014A),
                     (TiffBasedRole.Primary, 0x8769), (TiffBasedRole.Primary, 0x8825), (TiffBasedRole.Primary, 0xC634),
                     (TiffBasedRole.Primary, 0x0201), (TiffBasedRole.Primary, 0x0144), (TiffBasedRole.Exif, 0x927C),
                     (TiffBasedRole.Exif, 0xA005)
                 })
        {
            var set = new Dictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?> { [(role, tag)] = value };
            Assert.Equal(MetadataErrorCode.TagNotEditable, Code(() => TiffBasedWritePlanner.Create(fixture.Bytes, map, set, limits)));
            var remove = new Dictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?> { [(role, tag)] = null };
            Assert.Equal(MetadataErrorCode.TagNotEditable, Code(() => TiffBasedWritePlanner.Create(fixture.Bytes, map, remove, limits)));
        }
    }

    [Theory]
    [InlineData(604)] // a byte of the thumbnail strip
    [InlineData(700)] // a byte of the raw strip
    [InlineData(400)] // a byte of the MakerNote
    [InlineData(121)] // a byte of Make, a tag the edit did not name
    [InlineData(112)] // a byte no directory addresses
    [InlineData(302)] // the tag of the first entry of the Exif directory
    [InlineData(-1)]  // the last appended byte
    public void AnOutputThatIsNotThePlanFailsVerification(int offset)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Nef(false);
        var limits = new MetadataLimits();
        TiffBasedFileMap map = TiffBasedFileMap.Read(fixture.Bytes, limits, 16_384);
        ExifMetadata before = ExifTiffReader.InspectFile(fixture.Bytes, limits);
        var changes = new Dictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?>
        {
            [(TiffBasedRole.Primary, 0x0110)] = new(2, (uint)LongText.Length + 1, Encoding.ASCII.GetBytes(LongText + "\0"))
        };
        TiffBasedWritePlan plan = TiffBasedWritePlanner.Create(fixture.Bytes, map, changes, limits);
        byte[] output = plan.Apply(fixture.Bytes);
        TiffBasedWriteVerifier.Verify(fixture.Bytes, map, before, output, plan, limits, 16_384);

        byte[] damaged = (byte[])output.Clone();
        damaged[offset < 0 ? damaged.Length + offset : offset] ^= 0x55;
        Assert.Equal(MetadataErrorCode.UnsafeRelocation,
            Code(() => TiffBasedWriteVerifier.Verify(fixture.Bytes, map, before, damaged, plan, limits, 16_384)));
    }

    [Theory]
    [InlineData(121)] // the value of Make, a tag the plan does not name
    [InlineData(54)]  // the address of the thumbnail strip, in the primary directory
    [InlineData(604)] // a byte of the thumbnail strip
    [InlineData(400)] // a byte of the MakerNote
    public void APlanThatWritesWhereItMustNotFailsVerification(int offset)
    {
        TiffBasedFixture fixture = TiffBasedFixture.Nef(false);
        var limits = new MetadataLimits();
        TiffBasedFileMap map = TiffBasedFileMap.Read(fixture.Bytes, limits, 16_384);
        ExifMetadata before = ExifTiffReader.InspectFile(fixture.Bytes, limits);
        // A plan no planner makes: its output is exactly what it says, and what it says is wrong.
        var plan = new TiffBasedWritePlan(fixture.Bytes.Length, [(offset, new[] { (byte)(fixture.Bytes[offset] ^ 0x55) })], [], [], []);
        byte[] output = plan.Apply(fixture.Bytes);
        Assert.Equal(MetadataErrorCode.UnsafeRelocation,
            Code(() => TiffBasedWriteVerifier.Verify(fixture.Bytes, map, before, output, plan, limits, 16_384)));
    }

    [Fact]
    public void AnOutputOfAnotherLengthFailsVerification()
    {
        TiffBasedFixture fixture = TiffBasedFixture.Nef(false);
        var limits = new MetadataLimits();
        TiffBasedFileMap map = TiffBasedFileMap.Read(fixture.Bytes, limits, 16_384);
        ExifMetadata before = ExifTiffReader.InspectFile(fixture.Bytes, limits);
        var changes = new Dictionary<(TiffBasedRole Role, ushort Tag), TiffBasedChange?>
        {
            [(TiffBasedRole.Primary, 0x0110)] = new(2, (uint)LongText.Length + 1, Encoding.ASCII.GetBytes(LongText + "\0"))
        };
        TiffBasedWritePlan plan = TiffBasedWritePlanner.Create(fixture.Bytes, map, changes, limits);
        byte[] longer = [.. plan.Apply(fixture.Bytes), 0, 0];
        Assert.Equal(MetadataErrorCode.UnsafeRelocation,
            Code(() => TiffBasedWriteVerifier.Verify(fixture.Bytes, map, before, longer, plan, limits, 16_384)));
    }

    [Fact]
    public void TheMapFindsEveryDirectoryImageRangeAndTheMakerNoteOfEachLayout()
    {
        foreach (string name in TiffBasedFixture.Names)
        {
            TiffBasedFixture fixture = TiffBasedFixture.Create(name, false);
            TiffBasedFileMap map = TiffBasedFileMap.Read(fixture.Bytes, new MetadataLimits(), 16_384);
            Assert.False(map.HasRangePastEnd);
            Assert.False(map.HasOpenEndedImageData);
            Assert.Equal((uint)fixture.Primary, map.Find(TiffBasedRole.Primary)!.Offset);
            Assert.Equal((uint)fixture.Exif, map.Find(TiffBasedRole.Exif)!.Offset);
            foreach ((int start, int length) in fixture.Image)
                Assert.Contains(map.Ranges, range => range.Kind == TiffBasedRangeKind.ImageData && range.Start <= start && range.End >= start + length);
            if (fixture.Note.Length != 0)
                Assert.Contains(new TiffBasedRange(TiffBasedRangeKind.MakerNote, fixture.Note.Start, fixture.Note.Length), map.Ranges);
        }
        Assert.Equal(5, TiffBasedFileMap.Read(TiffBasedFixture.Nef(false).Bytes, new MetadataLimits(), 16_384).Directories.Count);
        Assert.Equal(4, TiffBasedFileMap.Read(TiffBasedFixture.Cr2(false).Bytes, new MetadataLimits(), 16_384).Directories.Count);
        Assert.True(TiffBasedFileMap.Read(TiffBasedFixture.Rw2().Bytes, new MetadataLimits(), 16_384).HasOpenEndedImageData);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Code(() => TiffBasedFileMap.Read(TiffBasedFixture.Nef(false).Bytes, new MetadataLimits(), 4)));
    }

    [Fact]
    public async Task TheAsynchronousAndPathSavesWriteTheSameBytes()
    {
        TiffBasedFixture fixture = TiffBasedFixture.Cr2(false);
        byte[] expected = Save(fixture.Bytes, Gps());
        string directory = Path.Combine(Path.GetTempPath(), "metadatasharp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "source.cr2"), target = Path.Combine(directory, "target.cr2");
            await File.WriteAllBytesAsync(source, fixture.Bytes);
            await using (TiffBasedMetadataDocument document = await TiffBasedMetadataDocument.OpenAsync(source))
            {
                using var stream = new MemoryStream();
                await document.SaveExifAsync(stream, Gps());
                Assert.Equal(expected, stream.ToArray());

                await document.SaveExifAsAsync(target, Gps());
                Assert.Equal(expected, await File.ReadAllBytesAsync(target));
                // Neither the source nor an existing file is written over.
                await Assert.ThrowsAsync<IOException>(() => document.SaveExifAsAsync(source, Gps()));
                await Assert.ThrowsAsync<IOException>(() => document.SaveExifAsAsync(target, Gps()));
                Assert.Throws<IOException>(() => document.SaveXmpAs(target, Xmp("MetadataSharp")));
            }
            Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(source));
            Assert.Equal(2, Directory.GetFiles(directory).Length);

            using TiffBasedMetadataDocument reopened = TiffBasedMetadataDocument.Open(target);
            string other = Path.Combine(directory, "other.cr2");
            reopened.SaveIptcAs(other, Iptc("A caption"));
            Assert.NotNull(TiffIptc.Read(File.ReadAllBytes(other)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void ARefusedSaveWritesNothingAndTheDestinationRulesHold()
    {
        TiffBasedFixture fixture = TiffBasedFixture.Rw2();
        using TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(fixture.Bytes));
        using var output = new MemoryStream();
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, Code(() => document.SaveExif(output, Gps())));
        Assert.Equal(0, output.Length);

        using var used = new MemoryStream([1, 2, 3]);
        Assert.Throws<ArgumentException>(() => document.SaveExif(used, new ExifEdit()));
        Assert.Throws<ArgumentNullException>(() => document.SaveExif(output, null!));
        Assert.Throws<ArgumentNullException>(() => document.SaveXmp(output, null!));
        Assert.Throws<ArgumentNullException>(() => document.SaveIptc(output, null!));

        document.Dispose();
        Assert.Throws<ObjectDisposedException>(() => document.SaveExif(output, new ExifEdit()));
        Assert.Throws<ObjectDisposedException>(() => document.ReadExif());
    }
}
