using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.MakerNotes;
using MetadataSharp.Core.Operations;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Formats.Tiff;
using MetadataSharp.Tests.Jpeg;
using Xunit;
using static MetadataSharp.Tests.MakerNotes.MakerNoteFixture;

namespace MetadataSharp.Tests.MakerNotes;

/// <summary>Tags of the MakerNote changed where they are: every layout, both byte orders.</summary>
public sealed class MakerNoteWriteTests
{
    private const string First = "0006", Number = "0007", Triple = "0008", Owner = "0009";

    /// <summary>Every layout in both byte orders.</summary>
    public static IEnumerable<object[]> Layouts()
    {
        foreach (Vendor vendor in Vendors)
        {
            yield return [vendor.Name, false];
            yield return [vendor.Name, true];
        }
    }

    private static E[] Common(bool big) =>
        [Ascii(0x0006, "FIRST VALUE"), Short(0x0007, big, 7), Long(0x0008, big, 100, 200, 300), Ascii(0x0009, "OWNER NAME")];

    private static byte[] Block(string vendor, bool big) => Build(Find(vendor), big, Common);

    private static ExifValue AsciiValue(string text) =>
        ExifValue.FromRaw(TiffFieldType.Ascii, (uint)text.Length + 1, Endianness.LittleEndian, [.. Encoding.ASCII.GetBytes(text), 0]);

    private static ExifValue LongValue(params long[] values) =>
        ExifValue.FromRaw(TiffFieldType.Long, (uint)values.Length, Endianness.LittleEndian, values.SelectMany(value => B32(value, false)).ToArray());

    private static ExifValue ShortValue(params int[] values) =>
        ExifValue.FromRaw(TiffFieldType.Short, (uint)values.Length, Endianness.LittleEndian, values.SelectMany(value => B16(value, false)).ToArray());

    private static byte[] Zero(string text) => [.. Encoding.ASCII.GetBytes(text), 0];

    private static MetadataErrorCode Refusal(Action action) => Assert.Throws<MetadataException>(action).Code;

    /// <summary>Everything of the note but the named paths reads as before, and nothing before the note changed.</summary>
    private static void AssertRestKept(byte[] before, byte[] after, params string[] named)
    {
        Assert.Equal(before.Length, after.Length);
        MakerNote old = Note(before), now = Note(after);
        Assert.Equal(old.Format, now.Format);
        Assert.Equal(old.Offset, now.Offset);
        Assert.Equal(old.ByteOrder, now.ByteOrder);
        Assert.Equal(before.AsSpan(0, (int)old.Offset).ToArray(), after.AsSpan(0, (int)now.Offset).ToArray());
        foreach (MakerNoteTarget target in MakerNoteKeys.Enumerate(old))
        {
            if (named.Any(path => path == target.Path || path.StartsWith(target.Path + "/", StringComparison.Ordinal) ||
                target.Path.StartsWith(path + "/", StringComparison.Ordinal)))
                continue;
            Assert.Equal(target.Value.RawBytes.ToArray(), Raw(after, target.Path));
        }
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void TheFixtureReadsAsTheLayout(string vendor, bool big)
    {
        byte[] block = Block(vendor, big);
        MakerNote note = Note(block);
        Assert.Equal(Find(vendor).Format, note.Format);
        Assert.Equal(4, note.Entries.Count);
        Assert.Equal(Zero("OWNER NAME"), Raw(block, Owner));
        Assert.Equal(Zero("FIRST VALUE"), Raw(block, First));
        Assert.StartsWith("Exif." + MakerNoteKeys.Group(note.Format) + ".", Key(block, Owner), StringComparison.Ordinal);
        Assert.True(ExifEdit.IsMakerNoteKey(Key(block, Owner)));
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void AValueOfTheSameLengthIsWrittenInPlace(string vendor, bool big)
    {
        byte[] block = Block(vendor, big);
        byte[] output = Apply(block, edit =>
        {
            edit.SetByKey(Key(block, Owner), AsciiValue("NEW OWNER!"));
            edit.SetTextByKey(Key(block, Number), "9");
            edit.SetByKey(Key(block, Triple), LongValue(1, 2, 0x01020304));
        });
        bool noteBig = Note(block).ByteOrder == Endianness.BigEndian;
        Assert.Equal(Zero("NEW OWNER!"), Raw(output, Owner));
        Assert.Equal(B16(9, noteBig), Raw(output, Number));
        Assert.Equal([.. B32(1, noteBig), .. B32(2, noteBig), .. B32(0x01020304, noteBig)], Raw(output, Triple));
        AssertRestKept(block, output, Owner, Number, Triple);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void AShorterValueStaysInItsPlaceOrMovesIntoTheEntry(string vendor, bool big)
    {
        byte[] block = Block(vendor, big);
        byte[] output = Apply(block, edit =>
        {
            edit.SetTextByKey(Key(block, Owner), "ME");
            edit.SetTextByKey(Key(block, First), "SHORTER");
            edit.SetByKey(Key(block, Triple), LongValue(5, 6));
        });
        bool noteBig = Note(block).ByteOrder == Endianness.BigEndian;
        Assert.Equal(Zero("ME"), Raw(output, Owner));
        Assert.Equal(Zero("SHORTER"), Raw(output, First));
        Assert.Equal([.. B32(5, noteBig), .. B32(6, noteBig)], Raw(output, Triple));
        AssertRestKept(block, output, Owner, First, Triple);
        // The bytes the longer values had are cleared.
        Assert.DoesNotContain("OWNER NAME", Encoding.Latin1.GetString(output), StringComparison.Ordinal);
        Assert.DoesNotContain("FIRST VALUE", Encoding.Latin1.GetString(output), StringComparison.Ordinal);
        // And the result is edited again as any note is.
        byte[] again = Apply(output, edit => edit.SetTextByKey(Key(output, First), "ANOTHER"));
        Assert.Equal(Zero("ANOTHER"), Raw(again, First));
        AssertRestKept(output, again, First);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void AValueThatFitsTheEntryIsWrittenThere(string vendor, bool big)
    {
        byte[] block = Block(vendor, big);
        byte[] output = Apply(block, edit => edit.SetByKey(Key(block, Number), ShortValue(1, 2)));
        bool noteBig = Note(block).ByteOrder == Endianness.BigEndian;
        Assert.Equal([.. B16(1, noteBig), .. B16(2, noteBig)], Raw(output, Number));
        AssertRestKept(block, output, Number);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void AValueThatGrowsIsRefused(string vendor, bool big)
    {
        byte[] block = Block(vendor, big);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(() => Apply(block, edit => edit.SetTextByKey(Key(block, Owner), "OWNER NAME AND MORE"))));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(() => Apply(block, edit => edit.SetByKey(Key(block, Number), ShortValue(1, 2, 3)))));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(() => Apply(block, edit => edit.SetByKey(Key(block, Triple), LongValue(1, 2, 3, 4)))));
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void ATagTheNoteDoesNotHoldAndAValueOfAnotherTypeAreRefused(string vendor, bool big)
    {
        byte[] block = Block(vendor, big);
        string group = MakerNoteKeys.Group(Note(block).Format);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(() => Apply(block, edit => edit.SetTextByKey("Exif." + group + ".0x7777", "1"))));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(() => Apply(block, edit => edit.RemoveByKey("Exif." + group + ".0x7777"))));
        string other = group == "Canon" ? "Exif.Sigma.0x0009" : "Exif.Canon.OwnerName";
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(() => Apply(block, edit => edit.SetTextByKey(other, "1"))));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(() => Apply(block, edit => edit.SetByKey(Key(block, Owner), ShortValue(1)))));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(() => Apply(block, edit => edit.SetTextByKey(Key(block, Number), "not a number"))));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(() => Apply(block, edit => edit.SetTextByKey(Key(block, Number), "70000"))));
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void ATagIsRemovedByMovingTheEntriesAfterItUp(string vendor, bool big)
    {
        byte[] block = Block(vendor, big);
        byte[] output = Apply(block, edit => edit.RemoveByKey(Key(block, Number)));
        Assert.Null(Raw(output, Number));
        Assert.Equal(3, Note(output).Entries.Count);
        AssertRestKept(block, output, Number);

        // A tag whose value lies outside its entry, and then one more: the directory keeps reading.
        byte[] second = Apply(output, edit => edit.RemoveByKey(Key(output, First)));
        Assert.Null(Raw(second, First));
        Assert.DoesNotContain("FIRST VALUE", Encoding.Latin1.GetString(second), StringComparison.Ordinal);
        AssertRestKept(output, second, First);
        byte[] third = Apply(second, edit => { edit.RemoveByKey(Key(second, Owner)); edit.SetTextByKey(Key(second, Triple), "7 8 9"); });
        Assert.Equal([0x0008], Note(third).Entries.Select(entry => entry.TagId).ToArray());

        // The last tag stays: a directory without tags does not read as one.
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(() => Apply(third, edit => edit.RemoveByKey(Key(third, Triple)))));
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void RemovalKeepsTheFourBytesAfterTheEntries(string vendor, bool big)
    {
        Vendor layout = Find(vendor);
        if (!layout.Next) return;
        byte[] block = Block(vendor, big);
        MakerNote note = Note(block);
        int directory = (int)note.Offset + layout.Header(big).Length;
        // The offset of a next directory, whatever it is: its four bytes are kept as they are.
        byte[] marked = (byte[])block.Clone();
        int next = directory + 2 + (4 * 12);
        marked[next] = 0xA1; marked[next + 1] = 0xB2; marked[next + 2] = 0xC3; marked[next + 3] = 0xD4;
        if (ExifOrNull(marked) is null) return; // A layout whose reader follows that offset.
        byte[] output = Apply(marked, edit => edit.RemoveByKey(Key(marked, Number)));
        Assert.Equal(new byte[] { 0xA1, 0xB2, 0xC3, 0xD4 }, output.AsSpan(next - 12, 4).ToArray());
        Assert.Equal(new byte[12], output.AsSpan(next - 8, 12).ToArray());
    }

    private static MakerNote? ExifOrNull(byte[] block)
    {
        try { return Exif(block).MakerNote; }
        catch (MetadataException) { return null; }
    }

    [Fact]
    public void TheKeysAreThoseTheDumpPrints()
    {
        byte[] canon = Block("Canon", false);
        Assert.Equal("Exif.Canon.OwnerName", Key(canon, Owner));
        Assert.Equal("Exif.Canon.FirmwareVersion", Key(canon, Number));
        Assert.Equal("Exif.Nikon3.FlashDevice", Key(Block("Nikon3", true), Owner));
        Assert.Equal("Exif.Fujifilm.0x0009", Key(Block("Fujifilm", false), Owner));
        Assert.Equal("Exif.PentaxDng.Size", Key(Block("PentaxDng", false), Owner));
        Assert.Equal("Exif.Sigma.MeteringMode", Key(Block("Sigma", false), Owner));
        Assert.True(ExifEdit.IsMakerNoteKey("Exif.CanonCs.Quality"));
        Assert.True(ExifEdit.IsMakerNoteKey("Exif.OlympusCs.ExposureMode"));
        Assert.False(ExifEdit.IsMakerNoteKey("Exif.Photo.MakerNote"));
        Assert.False(ExifEdit.IsMakerNoteKey("Exif.Image.Make"));
        Assert.False(ExifEdit.IsMakerNoteKey("Exif.Canon"));
        Assert.False(ExifEdit.IsMakerNoteKey(null));
    }

    [Fact]
    public void TheEditTakesAKeyOfEitherKind()
    {
        var edit = new ExifEdit();
        Assert.True(edit.IsEmpty);
        edit.SetTextByKey("Exif.Image.Artist", "A name");
        edit.SetByKey("Exif.Canon.OwnerName", AsciiValue("B"));
        edit.RemoveByKey("Exif.Canon.OwnerName");
        ExifEdit.EditSnapshot snapshot = edit.Snapshot();
        Assert.Single(snapshot.Changes);
        // The last change of a key counts.
        ExifEdit.MakerNoteChange change = Assert.Single(snapshot.MakerNote);
        Assert.Null(change.Value);
        Assert.Null(change.Text);
        Assert.Throws<ArgumentException>(() => edit.SetByKey("Exif.Nowhere.Tag", AsciiValue("B")));
        Assert.Throws<ArgumentException>(() => edit.SetTextByKey("Canon.OwnerName", "B"));
        Assert.Throws<ArgumentException>(() => edit.RemoveByKey("Xmp.dc.title"));
        var only = new ExifEdit();
        only.RemoveByKey("Exif.Canon.OwnerName");
        Assert.False(only.IsEmpty);
        Assert.Throws<ArgumentException>(only.RemoveAllExif);
    }

    // ---- Arrays -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFieldOfACanonArrayIsWrittenInTheArray(bool big)
    {
        byte[] block = Build(Find("Canon"), big, order =>
            [Short(0x0001, order, 24, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11), Ascii(0x0009, "OWNER NAME")]);
        Assert.Equal("Exif.CanonCs.Quality", Key(block, "0001/0003"));
        byte[] output = Apply(block, edit => edit.SetTextByKey("Exif.CanonCs.Quality", "500"));
        Assert.Equal(B16(500, big), Raw(output, "0001/0003"));
        Assert.Equal(B16(2, big), Raw(output, "0001/0002"));
        AssertRestKept(block, output, "0001/0003");
        // An array has no place for fewer fields or more elements.
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(() => Apply(block, edit => edit.RemoveByKey("Exif.CanonCs.Quality"))));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(() => Apply(block, edit => edit.SetTextByKey("Exif.CanonCs.Quality", "1 2"))));
        // The whole array is a tag like any other.
        byte[] whole = Apply(block, edit => edit.SetByKey("Exif.Canon.CameraSettings", ShortValue(24, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9)));
        Assert.Equal(B16(9, big), Raw(whole, "0001/0003"));
        AssertRestKept(block, whole, "0001");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACanonNoteWithAFooterIsEditedThroughItsCorrection(bool big)
    {
        // The note was written at offset 1000 and now lies elsewhere: its offsets still count from there.
        const int original = 1000;
        byte[] block = Build(big, "Canon Inc.", "MODEL ONE", start =>
            [.. Ifd(big, start, start - original, true, Common(big)), .. big ? new byte[] { 0x4D, 0x4D, 0, 0x2A } : [0x49, 0x49, 0x2A, 0], .. B32(original, big)]);
        Assert.Equal(Zero("OWNER NAME"), Raw(block, Owner));
        byte[] output = Apply(block, edit =>
        {
            edit.SetByKey("Exif.Canon.OwnerName", AsciiValue("NEW OWNER!"));
            edit.SetTextByKey("Exif.Canon.ImageType", "TYPE");
            edit.RemoveByKey("Exif.Canon.FirmwareVersion");
        });
        Assert.Equal(Zero("NEW OWNER!"), Raw(output, Owner));
        Assert.Equal(Zero("TYPE"), Raw(output, First));
        Assert.Null(Raw(output, Number));
        AssertRestKept(block, output, Owner, First, Number);
        Assert.Equal(block.AsSpan(block.Length - 8).ToArray(), output.AsSpan(output.Length - 8).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFieldOfAnEncryptedNikonArrayIsEncryptedAgainWithTheSameKey(bool big)
    {
        const uint serial = 1234567, shutter = 5000;
        byte[] plain = [.. "0204"u8, .. Enumerable.Range(4, 26).Select(i => (byte)(i * 3))];
        byte[] stored = NikonMakerNoteCipher.Decrypt(plain, 4, shutter, serial);
        Assert.NotEqual(plain, stored);
        byte[] block = Build(Find("Nikon3"), big, order =>
            [Ascii(0x001D, "1234567"), Undefined(0x0098, stored), Long(0x00A7, order, shutter), Ascii(0x0009, "OWNER NAME")]);
        Assert.Equal("Exif.NikonLd3.FocalLength", Key(block, "0098/000b"));
        Assert.Equal(new[] { plain[11] }, Raw(block, "0098/000b"));

        byte[] output = Apply(block, edit => edit.SetTextByKey("Exif.NikonLd3.FocalLength", "77"));
        Assert.Equal(new byte[] { 77 }, Raw(output, "0098/000b"));
        AssertRestKept(block, output, "0098/000b");
        byte[] expected = (byte[])plain.Clone();
        expected[11] = 77;
        Assert.Equal(expected, NikonMakerNoteCipher.Decrypt(Raw(output, "0098")!, 4, shutter, serial));
        // The version, stored in the clear, is written in the clear.
        byte[] version = Apply(block, edit => edit.SetByKey("Exif.NikonLd3.Version", ExifValue.FromRaw(TiffFieldType.Undefined, 4, Endianness.LittleEndian, "0204"u8)));
        Assert.Equal(block, version);

        // The two halves of the key are not edited: the arrays would no longer decrypt.
        Assert.Equal(MetadataErrorCode.TagNotEditable, Refusal(() => Apply(block, edit => edit.SetTextByKey("Exif.Nikon3.SerialNumber", "7654321"))));
        Assert.Equal(MetadataErrorCode.TagNotEditable, Refusal(() => Apply(block, edit => edit.SetTextByKey("Exif.Nikon3.ShutterCount", "1"))));
        Assert.Equal(MetadataErrorCode.TagNotEditable, Refusal(() => Apply(block, edit => edit.RemoveByKey("Exif.Nikon3.ShutterCount"))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFieldOfAnEncipheredSonyArrayIsEncipheredAgain(bool big)
    {
        byte[] plain = [1, 2, 3, 4, 5, 20, 7, 8, 9, 10];
        byte[] stored = SonyMakerNoteArrays.Stored(plain);
        Assert.Equal(plain, SonyMakerNoteArrays.Plain(stored));
        byte[] block = Build(Find("Sony2"), big, order => [Short(0x0007, order, 7), Undefined(0x9403, stored), Ascii(0x0009, "OWNER NAME")]);
        Assert.Equal("Exif.SonyMisc1.CameraTemperature", Key(block, "9403/0005"));
        Assert.Equal(new byte[] { 20 }, Raw(block, "9403/0005"));

        byte[] output = Apply(block, edit => edit.SetTextByKey("Exif.SonyMisc1.CameraTemperature", "-3"));
        Assert.Equal(new byte[] { 0xFD }, Raw(output, "9403/0005"));
        AssertRestKept(block, output, "9403/0005");
        byte[] expected = (byte[])plain.Clone();
        expected[5] = 0xFD;
        Assert.Equal(expected, SonyMakerNoteArrays.Plain(Raw(output, "9403")!));
    }

    // ---- Sub-directories and addresses --------------------------------------------------------------------

    private static byte[] Olympus(string vendor, bool big)
    {
        Vendor layout = Find(vendor);
        return Build(big, layout.Make, "MODEL ONE", start =>
        {
            byte[] header = layout.Header(big);
            // The main directory has two entries, an offset of the next one and one value of six bytes.
            int sub = header.Length + 2 + 24 + 4 + 6;
            byte[] main = Ifd(big, start + header.Length, start, true, Ascii(0x0207, "E-M10"), new E(0x2020, 13, 1, B32(sub, big)));
            Assert.Equal(sub, header.Length + main.Length);
            return
            [
                .. header, .. main,
                .. Ifd(big, start + sub, start, true, Long(0x0101, big, 500), Short(0x0200, big, 1), Short(0x0201, big, 0), Ascii(0x0999, "SUB VALUE")),
            ];
        });
    }

    [Theory]
    [InlineData("Olympus2", false)]
    [InlineData("Olympus2", true)]
    [InlineData("OMSystem", false)]
    [InlineData("OMSystem", true)]
    public void ATagOfAnOlympusSubDirectoryIsEdited(string vendor, bool big)
    {
        byte[] block = Olympus(vendor, big);
        Assert.Equal("Exif.OlympusCs.ExposureMode", Key(block, "2020/0200"));
        Assert.Equal("Exif.OlympusCs.0x0999", Key(block, "2020/0999"));
        byte[] output = Apply(block, edit =>
        {
            edit.SetTextByKey("Exif.OlympusCs.ExposureMode", "3");
            edit.SetTextByKey("Exif.OlympusCs.0x0999", "NEW VALUE");
            edit.SetTextByKey(Key(block, "0207"), "E-M1");
        });
        Assert.Equal(B16(3, big), Raw(output, "2020/0200"));
        Assert.Equal(Zero("NEW VALUE"), Raw(output, "2020/0999"));
        Assert.Equal(Zero("E-M1"), Raw(output, "0207"));
        AssertRestKept(block, output, "2020/0200", "2020/0999", "0207");

        byte[] removed = Apply(block, edit => edit.RemoveByKey("Exif.OlympusCs.AELock"));
        Assert.Null(Raw(removed, "2020/0201"));
        Assert.Equal(3, Note(removed).Entries.Single(entry => entry.TagId == 0x2020).Fields.Count);
        AssertRestKept(block, removed, "2020/0201");

        // What leads to the directory, and what addresses data from inside it, is not a value to set.
        Assert.Equal(MetadataErrorCode.TagNotEditable, Refusal(() => Apply(block, edit => edit.SetTextByKey(Key(block, "2020"), "8"))));
        Assert.Equal(MetadataErrorCode.TagNotEditable, Refusal(() => Apply(block, edit => edit.RemoveByKey(Key(block, "2020")))));
        Assert.Equal(MetadataErrorCode.TagNotEditable, Refusal(() => Apply(block, edit => edit.SetTextByKey("Exif.OlympusCs.PreviewImageStart", "8"))));
    }

    [Fact]
    public void ADirectoryWithEntriesTheReaderSkipsLosesNoEntry()
    {
        // The second entry has a type TIFF does not define: what its four bytes address is not known.
        byte[] block = Build(Find("Canon"), false, order =>
            [Ascii(0x0006, "FIRST VALUE"), new E(0x0077, 99, 1, [1, 2, 3, 4]), Short(0x0007, order, 7), Ascii(0x0009, "OWNER NAME")]);
        Assert.Equal(3, Note(block).Entries.Count);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(() => Apply(block, edit => edit.RemoveByKey("Exif.Canon.FirmwareVersion"))));
        // Setting moves nothing, so it is done.
        byte[] output = Apply(block, edit => edit.SetTextByKey("Exif.Canon.FirmwareVersion", "8"));
        Assert.Equal(B16(8, false), Raw(output, Number));
        AssertRestKept(block, output, Number);
    }

    // ---- Files --------------------------------------------------------------------------------------------

    private static byte[] Jpeg(byte[] block) => JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif(block)));

    private static byte[] Save(byte[] file, ExifEdit edit)
    {
        using JpegMetadataDocument document = JpegMetadataDocument.Open(new MemoryStream(file));
        var output = new MemoryStream();
        document.Save(output, edit);
        return output.ToArray();
    }

    [Theory]
    [InlineData("Canon", false)]
    [InlineData("Nikon3", true)]
    [InlineData("Fujifilm", false)]
    [InlineData("PentaxDng", true)]
    [InlineData("Panasonic", false)]
    public void AJpegIsSavedWithTheNoteChangedAndWhereItWas(string vendor, bool big)
    {
        byte[] block = Block(vendor, big);
        var edit = new ExifEdit();
        edit.SetTextByKey(Key(block, Owner), "IN A FILE!");
        byte[] output = Save(Jpeg(block), edit);
        ExifMetadata? exif = ImageMetadata.Read(output).Exif;
        Assert.NotNull(exif);
        MakerNote note = exif!.MakerNote!;
        Assert.Equal(Note(block).Offset, note.Offset);
        Assert.True(note.TryGetEntry(0x0009, out MakerNoteEntry? owner));
        Assert.Equal("IN A FILE!", Encoding.ASCII.GetString(owner!.Value.RawBytes).TrimEnd('\0'));
        Assert.True(note.TryGetEntry(0x0006, out MakerNoteEntry? first));
        Assert.Equal(Zero("FIRST VALUE"), first!.Value.RawBytes.ToArray());
    }

    [Theory]
    [InlineData("Canon", false)]
    [InlineData("Nikon3", true)]
    [InlineData("Olympus", false)]
    public void ATagOfTheNoteAndAStandardTagAreChangedInOneSave(string vendor, bool big)
    {
        byte[] block = Block(vendor, big);
        var edit = new ExifEdit();
        edit.SetTextByKey(Key(block, Owner), "IN A FILE!");
        edit.SetTextByKey("Exif.Image.Artist", "A photographer with a long name");
        byte[] output = Save(Jpeg(block), edit);
        ExifMetadata exif = ImageMetadata.Read(output).Exif!;
        Assert.True(exif.TryGetEntry(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x013B), out ExifEntry? artist));
        Assert.Equal("A photographer with a long name", Encoding.ASCII.GetString(artist!.Value.RawBytes).TrimEnd('\0'));
        MakerNote note = exif.MakerNote!;
        Assert.Equal(Note(block).Offset, note.Offset);
        Assert.True(note.TryGetEntry(0x0009, out MakerNoteEntry? owner));
        Assert.Equal(Zero("IN A FILE!"), owner!.Value.RawBytes.ToArray());
        Assert.True(note.TryGetEntry(0x0008, out MakerNoteEntry? triple));
        Assert.Equal(Raw(block, Triple), triple!.Value.RawBytes.ToArray());
    }

    [Fact]
    public void AFileWithoutANoteAndAContainerThatIsNotABlockRefuse()
    {
        var edit = new ExifEdit();
        edit.SetTextByKey("Exif.Canon.OwnerName", "NOBODY");
        // No EXIF block at all.
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(() => Save(JpegFixture.Build(), edit)));
        // A block whose note is of a maker this library does not decode.
        byte[] unknown = Build(false, "ACME CAMERAS", "MODEL ONE", start => Ifd(false, start, 0, true, Common(false)));
        Assert.Null(Exif(unknown).MakerNote);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(() => Save(Jpeg(unknown), edit)));
        // A TIFF-based file and a BMFF item plan their changes through here.
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(() => ExifBlockEditor.AddChanges(edit.Snapshot(), Endianness.LittleEndian, [])));
    }

    [Fact]
    public void AScriptNamesTagsOfTheNote()
    {
        byte[] block = Block("Canon", true);
        ModifyScript script = ModifyScript.Parse(
            "set Exif.Canon.OwnerName NEW OWNER!\n" +
            "del Exif.Canon.FirmwareVersion\n" +
            "set Exif.Canon.FileNumber Long 5 6 7\n" +
            "add Exif.Canon.ImageType \"A TYPE\"\n");
        Assert.False(script.IsEmpty);
        byte[] output = MakerNoteWriter.Apply(block, script.Exif.Snapshot().MakerNote, new MetadataLimits());
        Assert.Equal(Zero("NEW OWNER!"), Raw(output, Owner));
        Assert.Null(Raw(output, Number));
        Assert.Equal([.. B32(5, true), .. B32(6, true), .. B32(7, true)], Raw(output, Triple));
        Assert.Equal(Zero("A TYPE"), Raw(output, First));
        Assert.Throws<ModifyScriptException>(() => ModifyScript.Parse("set Exif.Canon.OwnerName Comment charset=Ascii text"));
        Assert.Throws<ModifyScriptException>(() => ModifyScript.Parse("set Exif.Canon.FileNumber Long five"));
    }
}
