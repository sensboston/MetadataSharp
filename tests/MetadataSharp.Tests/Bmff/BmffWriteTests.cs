using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Bmff;
using Xunit;

namespace MetadataSharp.Tests.Bmff;

/// <summary>
/// Editing the EXIF, GPS and XMP items of HEIC, HEIF and AVIF files without moving the data of any
/// item. Every case states which bytes of the source may differ in the output, and finds the items
/// of the output with a reader of boxes that shares nothing with production code.
/// </summary>
public sealed class BmffWriteTests
{
    private static readonly ExifTagKey Make = new(ExifDirectoryKind.Ifd0, 0x010F);
    private static readonly ExifTagKey Description = new(ExifDirectoryKind.Ifd0, 0x010E);
    private static readonly ExifTagKey DateTimeOriginal = new(ExifDirectoryKind.Exif, 0x9003);
    private static readonly ExifTagKey DateTimeDigitized = new(ExifDirectoryKind.Exif, 0x9004);
    private static readonly XmpPropertyName Tool = new("http://ns.adobe.com/xap/1.0/", "CreatorTool");
    private static readonly XmpPropertyName Label = new("http://ns.adobe.com/xap/1.0/", "Label");
    private const string OldDate = "2020:01:02 03:04:05";
    private const string NewDate = "2021:06:07 08:09:10";
    private const string LongText = "A considerably longer text than the one before";

    public static TheoryData<string, bool> Layouts()
    {
        var data = new TheoryData<string, bool>();
        foreach (string name in BmffFixture.WithMetadata)
        {
            data.Add(name, false);
            data.Add(name, true);
        }
        return data;
    }

    public static TheoryData<string> BareLayouts()
    {
        var data = new TheoryData<string>();
        foreach (string name in BmffFixture.Bare) data.Add(name);
        return data;
    }

    private static ExifValue Ascii(string text) =>
        ExifValue.FromRaw(TiffFieldType.Ascii, (uint)text.Length + 1, Endianness.LittleEndian, Encoding.ASCII.GetBytes(text + "\0"));

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

    private static byte[] Raw(ExifValue value)
    {
        byte[] raw = new byte[value.ByteLength];
        value.CopyRawTo(raw);
        return raw;
    }

    private static byte[] Raw(XmpPacket packet)
    {
        byte[] raw = new byte[packet.ByteLength];
        packet.CopyRawTo(raw);
        return raw;
    }

    private static string Text(ExifMetadata metadata, ExifTagKey key)
    {
        Assert.True(metadata.TryGetEntry(key, out ExifEntry? entry));
        return Encoding.ASCII.GetString(Raw(entry.Value)).TrimEnd('\0', ' ');
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

    private static byte[] Save(byte[] file, ExifEdit edit, BmffReadOptions? options = null)
    {
        using BmffMetadataDocument document = BmffMetadataDocument.Open(new MemoryStream(file), options);
        using var output = new MemoryStream();
        document.SaveExif(output, edit);
        return output.ToArray();
    }

    private static byte[] Save(byte[] file, XmpEdit edit, BmffReadOptions? options = null)
    {
        using BmffMetadataDocument document = BmffMetadataDocument.Open(new MemoryStream(file), options);
        using var output = new MemoryStream();
        document.SaveXmp(output, edit);
        return output.ToArray();
    }

    private static MetadataErrorCode Code(Action action) => Assert.Throws<MetadataException>(action).Code;

    /// <summary>
    /// Every byte of the source is in the output where it was, except inside the given ranges, and
    /// inside each of them something did change.
    /// </summary>
    private static void AssertSourceKeptExcept(byte[] source, byte[] output, params (int Start, int Length)[] allowed)
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

    /// <summary>Where a byte of an item is in the file, by the extents the fixture gave the item.</summary>
    private static int FileOffset(BmffFixture fixture, uint id, int inItem)
    {
        foreach ((int start, int length) in fixture.Positions[id])
        {
            if (inItem < length) return start + inItem;
            inItem -= length;
        }
        throw new ArgumentOutOfRangeException(nameof(inItem));
    }

    /// <summary>
    /// Every item of the source but the named one is addressed in the output exactly as it was - the
    /// same method, the same place in the file - and holds the same bytes.
    /// </summary>
    private static void AssertOtherItemsStay(BmffFixture fixture, byte[] output, uint edited)
    {
        foreach (BmffFixture.Item item in fixture.Plan.Items)
        {
            if (item.Id == edited) continue;
            (int method, (int Start, int Length)[] extents) = BmffFixture.Locate(fixture.Bytes, item.Id);
            (int methodNow, (int Start, int Length)[] extentsNow) = BmffFixture.Locate(output, item.Id);
            Assert.Equal(method, methodNow);
            // An item of the item data box is where that box is; every other one is where it was.
            if (method == 0) Assert.Equal(extents, extentsNow);
            Assert.Equal(item.Data, BmffFixture.ItemBytes(output, item.Id));
        }
    }

    /// <summary>The file gained one media data box after its last box, and no box of it moved or changed its length.</summary>
    private static BmffFixture.BoxInfo AssertOneMediaDataBoxAppended(byte[] source, byte[] output)
    {
        List<BmffFixture.BoxInfo> before = BmffFixture.TopLevel(source), after = BmffFixture.TopLevel(output);
        Assert.Equal(BmffFixture.Types(before) + " mdat", BmffFixture.Types(after));
        for (int i = 0; i < before.Count; i++) Assert.Equal(before[i], after[i]);
        Assert.Equal(source.Length, after[^1].Start);
        return after[^1];
    }

    private static BmffFixture.BoxInfo Child(byte[] file, string type) =>
        BmffFixture.MetaChildren(file).Single(box => box.Type == type);

    /// <summary>The item is one extent of the file itself, at the given place.</summary>
    private static void AssertOneExtent(byte[] file, uint id, int start, int length)
    {
        (int method, (int Start, int Length)[] extents) = BmffFixture.Locate(file, id);
        Assert.Equal(0, method);
        Assert.Equal(new[] { (start, length) }, extents);
    }

    /// <summary>The items the file declares, in order: "1:hvc1 2:Exif".</summary>
    private static string Declared(byte[] file) =>
        string.Join(' ', BmffFixture.Declared(file).Select(item => $"{item.Id}:{item.Type}"));

    // ---- an item that is there ----

    [Theory]
    [MemberData(nameof(Layouts))]
    public void AValueOfTheSameLengthIsWrittenWhereTheItemIs(string name, bool big)
    {
        BmffFixture fixture = BmffFixture.Create(name, big);
        byte[] output = Save(fixture.Bytes, Edit(edit => edit.Set(DateTimeOriginal, Ascii(NewDate))));

        // Not a box changes: the nineteen characters of the date are the only bytes that differ.
        Assert.Equal(fixture.Bytes.Length, output.Length);
        int date = FileOffset(fixture, BmffFixture.ExifId, fixture.Plan.ExifPrefix + BmffFixture.DateAt);
        AssertSourceKeptExcept(fixture.Bytes, output, (date, BmffFixture.DateLength - 1));
        Assert.Equal(NewDate + "\0", Encoding.ASCII.GetString(output, date, BmffFixture.DateLength));

        AssertOtherItemsStay(fixture, output, BmffFixture.ExifId);
        BmffMetadata after = BmffMetadata.Read(output);
        Assert.Equal(NewDate, Text(after.Exif!, DateTimeOriginal));
        Assert.Equal("TestCompany", Text(after.Exif!, Make));
        Assert.Equal(BmffFixture.Xmp("bmff"), Raw(after.Xmp!));
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void ALongerValueSendsTheItemToANewMediaDataBoxAndOnlyTheItemLocationBoxChanges(string name, bool big)
    {
        BmffFixture fixture = BmffFixture.Create(name, big);
        byte[] source = fixture.Bytes;
        byte[] output = Save(source, Edit(edit => edit.Set(Make, Ascii(LongText))));

        BmffFixture.BoxInfo added = AssertOneMediaDataBoxAppended(source, output);
        BmffFixture.BoxInfo locations = Child(source, "iloc");
        AssertSourceKeptExcept(source, output, (locations.Start, locations.Length));
        AssertOtherItemsStay(fixture, output, BmffFixture.ExifId);

        // The item is one extent of this file: the payload of the new box, all of it.
        AssertOneExtent(output, BmffFixture.ExifId, added.Payload, added.Length - 8);
        Assert.Equal(output.Length, added.End);

        // Inside the item nothing moved either: one entry of its first directory points at the new
        // text, which follows the old block; the MakerNote is at the offset it had.
        byte[] was = BmffFixture.ItemBytes(source, BmffFixture.ExifId), now = BmffFixture.ItemBytes(output, BmffFixture.ExifId);
        int tiff = fixture.Plan.ExifPrefix;
        Assert.Equal(was.Length + LongText.Length + 1, now.Length);
        Assert.Equal(was[..(tiff + 10)], now[..(tiff + 10)]);
        Assert.Equal(was[(tiff + 22)..], now[(tiff + 22)..was.Length]);
        Assert.Equal(LongText + "\0", Encoding.ASCII.GetString(now, was.Length, LongText.Length + 1));
        for (int i = 0; i < BmffFixture.NoteLength; i++) Assert.Equal(0xA0 + i, now[tiff + BmffFixture.NoteAt + i]);

        // The bytes the item had are still in the file, no longer addressed.
        foreach ((int start, int length) in fixture.Positions[BmffFixture.ExifId])
            Assert.Equal(source[start..(start + length)], output[start..(start + length)]);

        BmffMetadata after = BmffMetadata.Read(output);
        Assert.Equal(LongText, Text(after.Exif!, Make));
        Assert.Equal(OldDate, Text(after.Exif!, DateTimeOriginal));
        Assert.Equal(BmffMetadata.Read(source).Exif!.Entries.Count, after.Exif!.Entries.Count);
        Assert.Equal(BmffFixture.Xmp("bmff"), Raw(after.Xmp!));
    }

    [Fact]
    public void AnItemLocationEntryThatGetsShorterLeavesAFreeBoxInTheBytesItHad()
    {
        // The EXIF item of this file is two extents; the edited one is a single extent, so its
        // entry is eight bytes shorter, and those eight bytes become a free box.
        BmffFixture fixture = BmffFixture.Create("heic-extents", false);
        byte[] output = Save(fixture.Bytes, Edit(edit => edit.Set(Make, Ascii(LongText))));

        BmffFixture.BoxInfo before = Child(fixture.Bytes, "iloc"), after = Child(output, "iloc");
        Assert.Equal(before.Start, after.Start);
        Assert.Equal(before.Length - 8, after.Length);
        List<BmffFixture.BoxInfo> children = BmffFixture.MetaChildren(output);
        BmffFixture.BoxInfo free = children[children.IndexOf(after) + 1];
        Assert.Equal(new BmffFixture.BoxInfo("free", after.End, 8, 8), free);
        Assert.Equal(BmffFixture.Types(BmffFixture.MetaChildren(fixture.Bytes)).Replace("iloc", "iloc free"), BmffFixture.Types(children));
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void AddingGpsToAnItemKeepsTheMakerNoteAtItsOffset(string name, bool big)
    {
        BmffFixture fixture = BmffFixture.Create(name, big);
        byte[] output = Save(fixture.Bytes, Gps());

        AssertOneMediaDataBoxAppended(fixture.Bytes, output);
        AssertOtherItemsStay(fixture, output, BmffFixture.ExifId);
        byte[] now = BmffFixture.ItemBytes(output, BmffFixture.ExifId);
        for (int i = 0; i < BmffFixture.NoteLength; i++)
            Assert.Equal(0xA0 + i, now[fixture.Plan.ExifPrefix + BmffFixture.NoteAt + i]);

        ExifMetadata before = BmffMetadata.Read(fixture.Bytes).Exif!, after = BmffMetadata.Read(output).Exif!;
        Assert.True(after.TryGetEntry(new(ExifDirectoryKind.Gps, 1), out ExifEntry? reference));
        Assert.Equal("N\0"u8.ToArray(), Raw(reference.Value));
        Assert.True(after.TryGetEntry(new(ExifDirectoryKind.Gps, 0), out ExifEntry? version));
        Assert.Equal(new byte[] { 2, 3, 0, 0 }, Raw(version.Value));
        Assert.Equal(OldDate, Text(after, DateTimeOriginal));
        Assert.True(before.TryGetEntry(new(ExifDirectoryKind.Exif, 0x927C), out ExifEntry? note));
        Assert.True(after.TryGetEntry(new(ExifDirectoryKind.Exif, 0x927C), out ExifEntry? same));
        Assert.Equal(Raw(note.Value), Raw(same.Value));
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void AnXmpPacketThatIsThereIsEditedAndKeepsItsOtherProperties(string name, bool big)
    {
        BmffFixture fixture = BmffFixture.Create(name, big);
        byte[] source = fixture.Bytes;
        byte[] output = Save(source, Xmp("MetadataSharp"));

        BmffFixture.BoxInfo added = AssertOneMediaDataBoxAppended(source, output);
        BmffFixture.BoxInfo locations = Child(source, "iloc");
        AssertSourceKeptExcept(source, output, (locations.Start, locations.Length));
        AssertOtherItemsStay(fixture, output, BmffFixture.XmpId);
        AssertOneExtent(output, BmffFixture.XmpId, added.Payload, added.Length - 8);

        BmffMetadata after = BmffMetadata.Read(output);
        Assert.Equal("MetadataSharp", after.Xmp!.Properties[Tool].Text);
        Assert.Equal("bmff", after.Xmp.Properties[Label].Text);
        Assert.Equal(Raw(after.Xmp), BmffFixture.ItemBytes(output, BmffFixture.XmpId));
        Assert.Equal(OldDate, Text(after.Exif!, DateTimeOriginal));
    }

    [Fact]
    public void AnXmpPacketOfTheSameLengthIsWrittenWhereTheItemIs()
    {
        BmffFixture fixture = BmffFixture.Create("heic", false);
        var edit = new XmpEdit();
        edit.Set(Label, XmpValue.CreateText("heic"));
        // The first save writes the packet in the form of the library; the second one changes four
        // characters of it and nothing else.
        byte[] first = Save(fixture.Bytes, Xmp("MetadataSharp"));
        byte[] second = Save(first, edit);

        Assert.Equal(first.Length, second.Length);
        (int start, int length) = BmffFixture.Locate(first, BmffFixture.XmpId).Extents[0];
        AssertOneExtent(second, BmffFixture.XmpId, start, length);
        for (int i = 0; i < first.Length; i++)
            if (first[i] != second[i]) Assert.InRange(i, start, start + length - 1);
        Assert.Equal("heic", BmffMetadata.Read(second).Xmp!.Properties[Label].Text);
        Assert.Equal("MetadataSharp", BmffMetadata.Read(second).Xmp!.Properties[Tool].Text);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void AnEditedFileIsEditedAgain(string name, bool big)
    {
        BmffFixture fixture = BmffFixture.Create(name, big);
        byte[] first = Save(fixture.Bytes, Edit(edit => edit.Set(Make, Ascii(LongText))));
        byte[] second = Save(first, Xmp("MetadataSharp"));
        byte[] third = Save(second, Gps());
        // A tag is removed inside the directory it is in: the item keeps its length and its place.
        byte[] fourth = Save(third, Edit(edit => edit.Remove(Make)));

        Assert.Equal(third.Length, fourth.Length);
        Assert.Equal(BmffFixture.Types(BmffFixture.TopLevel(fixture.Bytes)) + " mdat mdat mdat",
            BmffFixture.Types(BmffFixture.TopLevel(fourth)));
        BmffMetadata after = BmffMetadata.Read(fourth);
        Assert.False(after.Exif!.TryGetEntry(Make, out _));
        Assert.True(after.Exif.TryGetEntry(new(ExifDirectoryKind.Gps, 2), out _));
        Assert.Equal(OldDate, Text(after.Exif, DateTimeOriginal));
        Assert.Equal("MetadataSharp", after.Xmp!.Properties[Tool].Text);
        // The image is where the first file had it, through four saves.
        Assert.Equal(BmffFixture.Locate(fixture.Bytes, BmffFixture.ImageId).Extents, BmffFixture.Locate(fourth, BmffFixture.ImageId).Extents);
        Assert.Equal(fixture.Plan.Items[0].Data, BmffFixture.ItemBytes(fourth, BmffFixture.ImageId));
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void AnEditThatChangesNothingWritesACopyOfTheSource(string name, bool big)
    {
        BmffFixture fixture = BmffFixture.Create(name, big);
        Assert.Equal(fixture.Bytes, Save(fixture.Bytes, new ExifEdit()));
        Assert.Equal(fixture.Bytes, Save(fixture.Bytes, new XmpEdit()));
        Assert.Equal(fixture.Bytes, Save(fixture.Bytes, Edit(edit => edit.Set(DateTimeOriginal, Ascii(OldDate)))));
        Assert.Equal(fixture.Bytes, Save(fixture.Bytes, Edit(edit => edit.Remove(DateTimeDigitized))));
        var absent = new XmpEdit();
        absent.Remove(Tool);
        Assert.Equal(fixture.Bytes, Save(fixture.Bytes, absent));

        // The same edit a second time has nothing left to do.
        byte[] once = Save(fixture.Bytes, Gps());
        Assert.Equal(once, Save(once, Gps()));
        byte[] packet = Save(fixture.Bytes, Xmp("MetadataSharp"));
        Assert.Equal(packet, Save(packet, Xmp("MetadataSharp")));
    }

    // ---- an item that is not there ----

    [Theory]
    [MemberData(nameof(BareLayouts))]
    public void AFileWithoutMetadataGainsAnExifItemThatDescribesThePrimaryItem(string name)
    {
        BmffFixture fixture = BmffFixture.Create(name, false);
        byte[] source = fixture.Bytes;
        Assert.Null(BmffMetadata.Read(source).Exif);
        byte[] output = Save(source, Edit(edit =>
        {
            edit.Set(Description, Ascii("A caption"));
            edit.Set(DateTimeOriginal, Ascii(NewDate));
        }));

        AssertMetaReplaced(fixture, output);
        AssertOtherItemsStay(fixture, output, 0);
        Assert.Equal("1:hvc1 2:Exif", Declared(output));
        AssertDescribesThePrimaryItem(output, 2);

        // The item starts with the offset of its TIFF header, zero, and is a media data box of its own.
        byte[] item = BmffFixture.ItemBytes(output, 2);
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0x49, 0x49, 0x2A, 0 }, item[..8]);
        BmffFixture.BoxInfo data = BmffFixture.TopLevel(output).Last(box => box.Type == "mdat");
        AssertOneExtent(output, 2, data.Payload, data.Length - 8);

        BmffMetadata after = BmffMetadata.Read(output);
        Assert.Equal("A caption", Text(after.Exif!, Description));
        Assert.Equal(NewDate, Text(after.Exif!, DateTimeOriginal));
        Assert.Null(after.Xmp);
    }

    [Theory]
    [MemberData(nameof(BareLayouts))]
    public void AFileWithoutMetadataGainsAnXmpItemThatDescribesThePrimaryItem(string name)
    {
        BmffFixture fixture = BmffFixture.Create(name, false);
        byte[] output = Save(fixture.Bytes, Xmp("MetadataSharp"));

        AssertMetaReplaced(fixture, output);
        AssertOtherItemsStay(fixture, output, 0);
        Assert.Equal("1:hvc1 2:mime", Declared(output));
        AssertDescribesThePrimaryItem(output, 2);
        Assert.Contains("MetadataSharp", Encoding.UTF8.GetString(BmffFixture.ItemBytes(output, 2)));

        BmffMetadata after = BmffMetadata.Read(output);
        Assert.Equal("MetadataSharp", after.Xmp!.Properties[Tool].Text);
        Assert.Null(after.Exif);

        // And the EXIF item after it: the third item of the file.
        byte[] both = Save(output, Edit(edit => edit.Set(DateTimeOriginal, Ascii(NewDate))));
        Assert.Equal("1:hvc1 2:mime 3:Exif", Declared(both));
        AssertDescribesThePrimaryItem(both, 3);
        Assert.Equal(BmffFixture.ItemBytes(output, 2), BmffFixture.ItemBytes(both, 2));
        Assert.Equal(BmffFixture.Locate(fixture.Bytes, BmffFixture.ImageId).Extents, BmffFixture.Locate(both, BmffFixture.ImageId).Extents);
        Assert.Equal(NewDate, Text(BmffMetadata.Read(both).Exif!, DateTimeOriginal));
        Assert.Equal("MetadataSharp", BmffMetadata.Read(both).Xmp!.Properties[Tool].Text);
    }

    /// <summary>
    /// A meta box that is the last box of the file grew where it is and the data follows it; one that
    /// is not became a free box of the same length, and the new one is the last box of the file.
    /// </summary>
    private static void AssertMetaReplaced(BmffFixture fixture, byte[] output)
    {
        byte[] source = fixture.Bytes;
        List<BmffFixture.BoxInfo> before = BmffFixture.TopLevel(source), after = BmffFixture.TopLevel(output);
        BmffFixture.BoxInfo meta = BmffFixture.Meta(source);
        if (fixture.Plan.MetaLast)
        {
            Assert.Equal(BmffFixture.Types(before) + " mdat", BmffFixture.Types(after));
            Assert.Equal(source[..meta.Start], output[..meta.Start]);
            Assert.Equal(meta.Start, BmffFixture.Meta(output).Start);
            Assert.True(BmffFixture.Meta(output).Length > meta.Length);
        }
        else
        {
            Assert.Equal(BmffFixture.Types(before).Replace("meta", "free") + " mdat meta", BmffFixture.Types(after));
            // Four bytes of the source changed: the type of the old meta box.
            AssertSourceKeptExcept(source, output, (meta.Start + 4, 4));
            for (int i = 0; i < before.Count; i++)
                Assert.Equal((before[i].Start, before[i].Length), (after[i].Start, after[i].Length));
        }
        // Every child the old box had is in the new one, in the same order, with one more item
        // reference box where the file had none.
        string children = BmffFixture.Types(BmffFixture.MetaChildren(source));
        string expected = children.Contains("iref") ? children : children.Replace("iinf", "iinf iref");
        Assert.Equal(expected, BmffFixture.Types(BmffFixture.MetaChildren(output)));
    }

    /// <summary>The last reference of the item reference box is a description (cdsc) of item 1 by the given item.</summary>
    private static void AssertDescribesThePrimaryItem(byte[] file, uint id)
    {
        BmffFixture.BoxInfo box = Child(file, "iref");
        Assert.Equal(0, file[box.Payload]);
        BmffFixture.BoxInfo last = BmffFixture.Boxes(file, box.Payload + 4, box.End)[^1];
        Assert.Equal("cdsc", last.Type);
        Assert.Equal(BmffFixture.Concat(BmffFixture.U16(id), BmffFixture.U16(1), BmffFixture.U16(BmffFixture.ImageId)),
            file[last.Payload..last.End]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnXmpItemAddedToAFileWithExifLeavesTheExifItemWhereItIs(bool metaLast)
    {
        BmffFixture.Layout layout = BmffFixture.LayoutOf("heic", false);
        layout.MetaLast = metaLast;
        layout.Items.RemoveAt(2);
        BmffFixture fixture = BmffFixture.Build("heic-exif", false, layout);
        byte[] output = Save(fixture.Bytes, Xmp("MetadataSharp"));

        AssertMetaReplaced(fixture, output);
        AssertOtherItemsStay(fixture, output, 0);
        Assert.Equal("1:hvc1 2:Exif 3:mime", Declared(output));
        AssertDescribesThePrimaryItem(output, 3);
        // The reference the EXIF item had is still the first one.
        BmffFixture.BoxInfo was = Child(fixture.Bytes, "iref"), now = Child(output, "iref");
        Assert.Equal(fixture.Bytes[was.Payload..was.End], output[now.Payload..(now.Payload + was.Length - 8)]);

        BmffMetadata after = BmffMetadata.Read(output);
        Assert.Equal("MetadataSharp", after.Xmp!.Properties[Tool].Text);
        Assert.Equal(OldDate, Text(after.Exif!, DateTimeOriginal));
    }

    [Fact]
    public void ANewItemOfAFileWithWideItemIdsIsLocatedAndReferencedWithTheFieldSizesOfTheFile()
    {
        // 32-bit IDs in the primary item box and the reference box, a version 2 item location box
        // with 64-bit fields and a base offset: the new entry is written with the same field sizes.
        BmffFixture.Layout layout = BmffFixture.LayoutOf("heif-wide", false);
        layout.Items.RemoveAt(2);
        BmffFixture fixture = BmffFixture.Build("heif-wide-exif", false, layout);
        byte[] output = Save(fixture.Bytes, Xmp("MetadataSharp"));

        AssertOtherItemsStay(fixture, output, 0);
        Assert.Equal("1:hvc1 2:Exif 3:mime", Declared(output));
        BmffFixture.BoxInfo locations = Child(output, "iloc"), was = Child(fixture.Bytes, "iloc");
        Assert.Equal(fixture.Bytes[was.Payload..(was.Payload + 6)], output[locations.Payload..(locations.Payload + 6)]);
        BmffFixture.BoxInfo references = Child(output, "iref");
        Assert.Equal(1, output[references.Payload]);
        BmffFixture.BoxInfo last = BmffFixture.Boxes(output, references.Payload + 4, references.End)[^1];
        Assert.Equal(BmffFixture.Concat(BmffFixture.U32(3), BmffFixture.U16(1), BmffFixture.U32(BmffFixture.ImageId)),
            output[last.Payload..last.End]);
        Assert.Equal("MetadataSharp", BmffMetadata.Read(output).Xmp!.Properties[Tool].Text);
    }

    [Theory]
    [MemberData(nameof(BareLayouts))]
    public void RemovingFromAFileWithoutTheItemChangesNothing(string name)
    {
        BmffFixture fixture = BmffFixture.Create(name, false);
        var all = new XmpEdit();
        all.RemoveAllXmp();
        var one = new XmpEdit();
        one.Remove(Tool);
        Assert.Equal(fixture.Bytes, Save(fixture.Bytes, all));
        Assert.Equal(fixture.Bytes, Save(fixture.Bytes, one));
        Assert.Equal(fixture.Bytes, Save(fixture.Bytes, Edit(edit => edit.RemoveAllExif())));
        Assert.Equal(fixture.Bytes, Save(fixture.Bytes, Edit(edit => edit.Remove(Make))));
    }

    // ---- what the file does not allow ----

    [Fact]
    public void RemovingAWholeItemIsRefused()
    {
        BmffFixture fixture = BmffFixture.Create("heic", false);
        var all = new XmpEdit();
        all.RemoveAllXmp();
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Code(() => Save(fixture.Bytes, Edit(edit => edit.RemoveAllExif()))));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Code(() => Save(fixture.Bytes, all)));
    }

    [Fact]
    public void BytesThatAnotherItemAlsoAddressesAreNotWrittenOver()
    {
        // A second item addresses the bytes of the EXIF item. An edit of the same length cannot be
        // made in place: the EXIF item goes to a new box, and the other item still reads what it read.
        BmffFixture.Layout layout = BmffFixture.LayoutOf("heic", false);
        layout.Items.Add(new BmffFixture.Item(5, "copy", layout.Items[1].Data) { SameAs = BmffFixture.ExifId });
        BmffFixture fixture = BmffFixture.Build("heic-shared", false, layout);
        byte[] output = Save(fixture.Bytes, Edit(edit => edit.Set(DateTimeOriginal, Ascii(NewDate))));

        AssertOneMediaDataBoxAppended(fixture.Bytes, output);
        AssertOtherItemsStay(fixture, output, BmffFixture.ExifId);
        Assert.Equal(layout.Items[1].Data, BmffFixture.ItemBytes(output, 5));
        Assert.Equal(NewDate, Text(BmffMetadata.Read(output).Exif!, DateTimeOriginal));
    }

    [Fact]
    public void AMediaDataBoxThatRunsToTheEndOfTheFileTakesTheNewBytesAndAllowsNoNewMetaBox()
    {
        BmffFixture.Layout layout = BmffFixture.LayoutOf("heic", false);
        layout.OpenData = true;
        BmffFixture fixture = BmffFixture.Build("heic-open", false, layout);
        byte[] source = fixture.Bytes;
        byte[] output = Save(source, Xmp("MetadataSharp"));

        // No box is added: the box that runs to the end of the file is as long as the file is.
        List<BmffFixture.BoxInfo> before = BmffFixture.TopLevel(source), after = BmffFixture.TopLevel(output);
        Assert.Equal(BmffFixture.Types(before), BmffFixture.Types(after));
        Assert.Equal(output.Length, after[^1].End);
        BmffFixture.BoxInfo locations = Child(source, "iloc");
        AssertSourceKeptExcept(source, output, (locations.Start, locations.Length));
        AssertOtherItemsStay(fixture, output, BmffFixture.XmpId);
        AssertOneExtent(output, BmffFixture.XmpId, source.Length, output.Length - source.Length);
        Assert.Equal("MetadataSharp", BmffMetadata.Read(output).Xmp!.Properties[Tool].Text);

        // A new item needs a longer meta box, and nothing can follow the open one.
        BmffFixture.Layout bare = BmffFixture.LayoutOf("heic-bare", false);
        bare.OpenData = true;
        byte[] open = BmffFixture.Build("heic-bare-open", false, bare).Bytes;
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, Code(() => Save(open, Xmp("MetadataSharp"))));
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, Code(() => Save(open, Edit(edit => edit.Set(Make, Ascii("A"))))));
    }

    [Fact]
    public void AFileThatAddressesBytesPastItsEndIsEditedInPlaceAndNeverAppendedTo()
    {
        // The last item of the file says it is longer than the file is.
        BmffFixture.Layout layout = BmffFixture.LayoutOf("heic", false);
        layout.Items.Add(new BmffFixture.Item(5, "hvc1", BmffFixture.Pattern(64, 11)) { StatedLength = 1_000_000 });
        BmffFixture fixture = BmffFixture.Build("heic-truncated", false, layout);

        byte[] output = Save(fixture.Bytes, Edit(edit => edit.Set(DateTimeOriginal, Ascii(NewDate))));
        Assert.Equal(fixture.Bytes.Length, output.Length);
        Assert.Equal(NewDate, Text(BmffMetadata.Read(output).Exif!, DateTimeOriginal));
        // Appended bytes would become part of the item the file says it has.
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, Code(() => Save(fixture.Bytes, Edit(edit => edit.Set(Make, Ascii(LongText))))));
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, Code(() => Save(fixture.Bytes, Xmp("MetadataSharp"))));
    }

    [Fact]
    public void AnItemThatRunsToTheEndOfTheFileIsNotAppendedTo()
    {
        BmffFixture.Layout layout = BmffFixture.LayoutOf("heic", false);
        layout.Items[0] = layout.Items[0] with { StatedLength = 0 };
        BmffFixture fixture = BmffFixture.Build("heic-to-end", false, layout);

        // The image is everything from its offset on, the metadata items included: not a byte of
        // them is written over, and nothing is appended.
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, Code(() => Save(fixture.Bytes, Edit(edit => edit.Set(DateTimeOriginal, Ascii(NewDate))))));
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, Code(() => Save(fixture.Bytes, Xmp("MetadataSharp"))));
    }

    [Fact]
    public void ATopLevelBoxOfAnUnknownKindIsNotFollowedByNewData()
    {
        BmffFixture.Layout layout = BmffFixture.LayoutOf("heic", false);
        layout.After = BmffFixture.Box("abcd", BmffFixture.Pattern(24, 3));
        BmffFixture fixture = BmffFixture.Build("heic-unknown", false, layout);

        byte[] output = Save(fixture.Bytes, Edit(edit => edit.Set(DateTimeOriginal, Ascii(NewDate))));
        Assert.Equal(fixture.Bytes.Length, output.Length);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Code(() => Save(fixture.Bytes, Edit(edit => edit.Set(Make, Ascii(LongText))))));

        // Boxes whose content does not depend on what follows them are followed.
        layout.After = BmffFixture.Concat(BmffFixture.Box("free", new byte[12]), BmffFixture.Box("uuid", BmffFixture.Pattern(32, 9)));
        byte[] known = BmffFixture.Build("heic-known", false, layout).Bytes;
        Assert.Equal(LongText, Text(BmffMetadata.Read(Save(known, Edit(edit => edit.Set(Make, Ascii(LongText))))).Exif!, Make));
    }

    [Fact]
    public void AFileWithoutAPrimaryItemOrWithoutAMetaBoxGainsNoItem()
    {
        BmffFixture.Layout layout = BmffFixture.LayoutOf("heic-bare", false);
        layout.Primary = false;
        byte[] unnamed = BmffFixture.Build("heic-no-primary", false, layout).Bytes;
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Code(() => Save(unnamed, Xmp("MetadataSharp"))));

        byte[] sequence = BmffFixture.Concat(
            BmffFixture.Box("ftyp", BmffFixture.Ascii("avis"), BmffFixture.U32(0), BmffFixture.Ascii("avis")),
            BmffFixture.Box("mdat", BmffFixture.Pattern(64, 1)));
        Assert.Null(BmffMetadata.Read(sequence).Exif);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Code(() => Save(sequence, Xmp("MetadataSharp"))));
        Assert.Equal(sequence, Save(sequence, new XmpEdit()));
    }

    [Fact]
    public void AnOutputOverTheInputBudgetIsRefusedAndAnEditInPlaceIsNot()
    {
        BmffFixture fixture = BmffFixture.Create("heic", false);
        var options = new BmffReadOptions { Limits = new MetadataLimits { MaxInputBytes = fixture.Bytes.Length + 16 } };
        Assert.Equal(MetadataErrorCode.LimitExceeded, Code(() => Save(fixture.Bytes, Edit(edit => edit.Set(Make, Ascii(LongText))), options)));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Code(() => Save(fixture.Bytes, Xmp("MetadataSharp"), options)));
        byte[] output = Save(fixture.Bytes, Edit(edit => edit.Set(DateTimeOriginal, Ascii(NewDate))), options);
        Assert.Equal(fixture.Bytes.Length, output.Length);
    }

    [Fact]
    public void AnExifBlockLongerThanTheReaderAcceptsIsRefused()
    {
        BmffFixture fixture = BmffFixture.Create("heic", false);
        string text = new('x', 65_500);
        Assert.Equal(MetadataErrorCode.ExifTooLarge, Code(() => Save(fixture.Bytes, Edit(edit => edit.Set(Description, Ascii(text))))));
    }

    [Fact]
    public void DataThatIsNotABmffFileIsNotOpened()
    {
        Assert.Equal(MetadataErrorCode.InvalidBmff, Code(() => BmffMetadataDocument.Open(new MemoryStream(new byte[64]))));
        Assert.Equal(MetadataErrorCode.InvalidBmff, Code(() => BmffMetadataDocument.Open(new MemoryStream(new byte[] { 0, 0, 0, 8 }))));
        BmffFixture fixture = BmffFixture.Create("heic", false);
        var options = new BmffReadOptions { Limits = new MetadataLimits { MaxInputBytes = fixture.Bytes.Length - 1 } };
        Assert.Equal(MetadataErrorCode.LimitExceeded, Code(() => BmffMetadataDocument.Open(new MemoryStream(fixture.Bytes), options)));
    }

    [Fact]
    public void AFileTheReaderRefusesIsNotWrittenAndAnEmptyEditStillCopiesIt()
    {
        // Two item location boxes: neither the reader nor the writer chooses between them.
        BmffFixture fixture = BmffFixture.Create("heic", false);
        byte[] source = fixture.Bytes;
        BmffFixture.BoxInfo meta = BmffFixture.Meta(source), locations = Child(source, "iloc");
        byte[] second = source[locations.Start..locations.End];
        byte[] grown = BmffFixture.Concat(source[..meta.End], second, source[meta.End..]);
        BinaryPrimitives.WriteUInt32BigEndian(grown.AsSpan(meta.Start), (uint)(meta.Length + second.Length));

        using BmffMetadataDocument document = BmffMetadataDocument.Open(new MemoryStream(grown));
        MetadataException read = Assert.Throws<MetadataException>(() => document.ReadExif());
        MetadataException save = Assert.Throws<MetadataException>(() => document.SaveXmp(new MemoryStream(), Xmp("MetadataSharp")));
        Assert.Equal(MetadataErrorCode.InvalidBmff, read.Code);
        Assert.Equal(MetadataErrorCode.InvalidBmff, save.Code);
        using var empty = new MemoryStream();
        document.SaveXmp(empty, new XmpEdit());
        Assert.Equal(grown, empty.ToArray());
    }

    // ---- the check of a planned output ----

    /// <summary>
    /// A planned edit of the XMP item of the usual layout, and four bytes of the file that the edit
    /// has no business with: one of the image, the first letter of the type of the image in the item
    /// information box, one of the EXIF item, and (as -1) the last appended byte.
    /// </summary>
    private static (byte[] Source, BmffFileMap Map, BmffMetadata Before, BmffWritePlan Plan, int[] Places) Planned()
    {
        BmffFixture fixture = BmffFixture.Create("heic", false);
        byte[] source = fixture.Bytes;
        var limits = new MetadataLimits();
        BmffFileMap map = BmffFileMap.Read(source, limits);
        BmffItem item = map.Find(BmffFileMap.MimeItem, BmffFileMap.XmpContentType)!;
        BmffWritePlan plan = BmffWritePlanner.Create(source, map, BmffItemKind.Xmp, item,
            BmffFixture.Xmp("a longer label than the one before"), limits);
        int[] places =
        [
            fixture.Positions[BmffFixture.ImageId][0].Start + 5,
            Child(source, "iinf").Start + 30,
            FileOffset(fixture, BmffFixture.ExifId, fixture.Plan.ExifPrefix + BmffFixture.MakeAt),
            -1
        ];
        return (source, map, BmffMetadata.Read(source), plan, places);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AnOutputThatIsNotThePlanFailsVerification(int place)
    {
        (byte[] source, BmffFileMap map, BmffMetadata before, BmffWritePlan plan, int[] places) = Planned();
        var limits = new MetadataLimits();
        byte[] output = plan.Apply(source);
        BmffWriteVerifier.Verify(source, map, before, output, plan, limits, null, null);

        int at = places[place] < 0 ? output.Length - 1 : places[place];
        output[at] ^= 0x01;
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, Code(() => BmffWriteVerifier.Verify(source, map, before, output, plan, limits, null, null)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void APlanThatWritesWhereItMustNotFailsVerification(int place)
    {
        // The output is the plan, byte for byte; the plan itself changes a byte of another item or
        // of a box the edit did not name. The checks that read the output again refuse it.
        (byte[] source, BmffFileMap map, BmffMetadata before, BmffWritePlan plan, int[] places) = Planned();
        var limits = new MetadataLimits();
        List<(int Offset, byte[] Bytes)> patches = plan.Patches.ToList();
        patches.Add((places[place], new[] { (byte)(source[places[place]] ^ 0x01) }));
        var wrong = new BmffWritePlan(source.Length, patches, plan.Tail, plan.Kind, plan.ItemId, plan.Added, plan.Payload, plan.Placement);
        byte[] output = wrong.Apply(source);
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, Code(() => BmffWriteVerifier.Verify(source, map, before, output, wrong, limits, null, null)));
    }

    [Fact]
    public void EachLayoutIsPlannedTheWayItsMetaBoxAllows()
    {
        var limits = new MetadataLimits();
        BmffMetaPlacement Placement(BmffFixture fixture, bool add, byte[] payload)
        {
            BmffFileMap map = BmffFileMap.Read(fixture.Bytes, limits);
            BmffItem? item = add ? null : map.Find(BmffFileMap.MimeItem, BmffFileMap.XmpContentType);
            return BmffWritePlanner.Create(fixture.Bytes, map, BmffItemKind.Xmp, item, payload, limits).Placement;
        }
        foreach (string name in BmffFixture.WithMetadata)
        {
            BmffFixture fixture = BmffFixture.Create(name, false);
            Assert.Equal(BmffMetaPlacement.Untouched, Placement(fixture, false, BmffFixture.Xmp("heic")));
            Assert.Equal(BmffMetaPlacement.InPlace, Placement(fixture, false, BmffFixture.Xmp("a longer label")));
        }
        Assert.Equal(BmffMetaPlacement.Moved, Placement(BmffFixture.Create("heic-bare", false), true, BmffFixture.Xmp("new")));
        Assert.Equal(BmffMetaPlacement.Extended, Placement(BmffFixture.Create("heic-bare-last", false), true, BmffFixture.Xmp("new")));
    }

    // ---- the document ----

    [Fact]
    public void TheDocumentReadsWhatTheReaderReads()
    {
        BmffFixture fixture = BmffFixture.Create("avif-idat", true);
        using BmffMetadataDocument document = BmffMetadataDocument.Open(new MemoryStream(fixture.Bytes));
        Assert.Equal(OldDate, Text(document.ReadExif()!, DateTimeOriginal));
        Assert.Equal("bmff", document.ReadXmp()!.Properties[Label].Text);

        using BmffMetadataDocument bare = BmffMetadataDocument.Open(new MemoryStream(BmffFixture.Create("heic-bare", false).Bytes));
        Assert.Null(bare.ReadExif());
        Assert.Null(bare.ReadXmp());
    }

    [Fact]
    public async Task TheAsynchronousAndPathSavesWriteTheSameBytes()
    {
        BmffFixture fixture = BmffFixture.Create("heic", false);
        byte[] expected = Save(fixture.Bytes, Gps());
        string directory = Path.Combine(Path.GetTempPath(), "metadatasharp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "source.heic"), target = Path.Combine(directory, "target.heic");
            await File.WriteAllBytesAsync(source, fixture.Bytes);
            await using (BmffMetadataDocument document = await BmffMetadataDocument.OpenAsync(source))
            {
                using var stream = new MemoryStream();
                await document.SaveExifAsync(stream, Gps());
                Assert.Equal(expected, stream.ToArray());

                await document.SaveExifAsAsync(target, Gps());
                Assert.Equal(expected, await File.ReadAllBytesAsync(target));
                // Neither the source nor an existing file is written over.
                await Assert.ThrowsAsync<IOException>(() => document.SaveExifAsAsync(source, Gps()));
                await Assert.ThrowsAsync<IOException>(() => document.SaveXmpAsAsync(target, Xmp("MetadataSharp")));
                Assert.Throws<IOException>(() => document.SaveExifAs(target, Gps()));
            }
            Assert.Equal(fixture.Bytes, await File.ReadAllBytesAsync(source));
            Assert.Equal(2, Directory.GetFiles(directory).Length);

            using BmffMetadataDocument reopened = BmffMetadataDocument.Open(target);
            string other = Path.Combine(directory, "other.heic");
            reopened.SaveXmpAs(other, Xmp("MetadataSharp"));
            Assert.Equal("MetadataSharp", BmffMetadata.Read(File.ReadAllBytes(other)).Xmp!.Properties[Tool].Text);

            await using BmffMetadataDocument streamed = await BmffMetadataDocument.OpenAsync(new MemoryStream(fixture.Bytes));
            using var asynchronous = new MemoryStream();
            await streamed.SaveXmpAsync(asynchronous, Xmp("MetadataSharp"));
            Assert.Equal(Save(fixture.Bytes, Xmp("MetadataSharp")), asynchronous.ToArray());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void ARefusedSaveWritesNothingAndTheDestinationRulesHold()
    {
        BmffFixture fixture = BmffFixture.Create("heic", false);
        using BmffMetadataDocument document = BmffMetadataDocument.Open(new MemoryStream(fixture.Bytes));
        using var output = new MemoryStream();
        Assert.Throws<MetadataException>(() => document.SaveExif(output, Edit(edit => edit.RemoveAllExif())));
        Assert.Equal(0, output.Length);

        Assert.Throws<ArgumentNullException>(() => document.SaveExif(output, null!));
        Assert.Throws<ArgumentNullException>(() => document.SaveXmp(output, null!));
        using var used = new MemoryStream(new byte[] { 1, 2, 3 });
        Assert.Throws<ArgumentException>(() => document.SaveXmp(used, Xmp("MetadataSharp")));
        Assert.Equal(new byte[] { 1, 2, 3 }, used.ToArray());

        document.Dispose();
        Assert.Throws<ObjectDisposedException>(() => document.ReadExif());
        Assert.Throws<ObjectDisposedException>(() => document.SaveXmp(new MemoryStream(), Xmp("MetadataSharp")));
    }
}
