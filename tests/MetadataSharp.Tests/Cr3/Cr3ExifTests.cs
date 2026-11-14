using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Tests.Iptc;
using MetadataSharp.Formats.Cr3;
using Xunit;
using static MetadataSharp.Tests.Cr3.Cr3Fixture;

namespace MetadataSharp.Tests.Cr3;

/// <summary>The CMT boxes: four TIFFs of one directory each, read as one EXIF block.</summary>
public sealed class Cr3ExifTests
{
    private const ExifDirectoryKind Primary = ExifDirectoryKind.Ifd0;
    private const ExifDirectoryKind Photo = ExifDirectoryKind.Exif;
    private const ExifDirectoryKind Position = ExifDirectoryKind.Gps;

    private static ExifTagKey Key(ExifDirectoryKind directory, ushort tag) => new(directory, tag);

    private static byte[] Bytes(ExifValue value)
    {
        var bytes = new byte[value.ByteLength];
        value.CopyRawTo(bytes);
        return bytes;
    }

    private static byte[] Bytes(ExifMetadata exif, ExifDirectoryKind directory, ushort tag)
    {
        Assert.True(exif.TryGetEntry(Key(directory, tag), out ExifEntry? entry));
        return Bytes(entry!.Value);
    }

    private static string Ascii(ExifValue value) => Encoding.ASCII.GetString(Bytes(value)).TrimEnd('\0');

    private static ExifMetadata Exif(byte[] file, MetadataLimits? limits = null)
    {
        ExifMetadata? exif = Cr3Metadata.Read(file, limits).Exif;
        Assert.NotNull(exif);
        return exif;
    }

    private static MetadataErrorCode Refusal(byte[] file, MetadataLimits? limits = null) =>
        Assert.Throws<MetadataException>(() => Cr3Metadata.Read(file, limits)).Code;

    /// <summary>A file whose Canon box holds the given boxes and nothing else.</summary>
    private static byte[] File(params byte[][] boxes) =>
        Join(FileType(), Box("moov", Uuid(CanonBox, boxes), Box("mvhd", new byte[100])), Box("mdat", new byte[16]));

    private static byte[] Ifd0(bool little = true) => TiffBlock(little, Text(0x010F, "Canon"), Shorts(0x0112, 6));

    [Fact]
    public void IptcNaaInThePrimaryCanonDirectoryIsRead()
    {
        byte[] iptc = IptcCodecTests.Raw(IptcPacket.CreateEmpty());
        Cr3Fixture.Layout layout = new() { Ifd0 = TiffBlock(true, Raw(0x83BB, 7, iptc)) };

        Cr3Metadata metadata = Cr3Metadata.Read(Cr3Fixture.Create(layout).Bytes);

        Assert.NotNull(metadata.Iptc);
        Assert.Equal(iptc.Length, metadata.Iptc.ByteLength);
    }

    [Fact]
    public void TheFourBoxesAreReadAsOneBlock()
    {
        ExifMetadata exif = Exif(Create().Bytes);

        Assert.False(exif.IsEditable);
        Assert.Equal(Endianness.LittleEndian, exif.ByteOrder);
        Assert.Equal(
            new[]
            {
                Key(Primary, 0x010F), Key(Primary, 0x0110), Key(Primary, 0x0112), Key(Primary, 0x0132),
                Key(Photo, 0x829A), Key(Photo, 0x8827), Key(Photo, 0x9000), Key(Photo, 0x9003),
                Key(Position, 0x0000), Key(Position, 0x0001), Key(Position, 0x0006),
            },
            exif.Entries.Select(entry => entry.Key));
        Assert.Equal("Canon EOS R6"u8.ToArray().Append((byte)0), Bytes(exif, Primary, 0x0110));
        Assert.Equal(new byte[] { 6, 0 }, Bytes(exif, Primary, 0x0112));
        Assert.Equal(new byte[] { 1, 0, 0, 0, 250, 0, 0, 0 }, Bytes(exif, Photo, 0x829A));
        Assert.Equal("0231"u8.ToArray(), Bytes(exif, Photo, 0x9000));
        Assert.Equal(new byte[] { 2, 3, 0, 0 }, Bytes(exif, Position, 0x0000));
        Assert.Equal(new byte[] { 123, 0, 0, 0, 10, 0, 0, 0 }, Bytes(exif, Position, 0x0006));
        // Nothing points from one box to the next, so the block has no pointer tag and no MakerNote tag.
        Assert.DoesNotContain(exif.Entries, entry => entry.Key.TagId is 0x8769 or 0x8825 or 0x927C);
    }

    [Fact]
    public void TheThirdBoxIsDecodedAsTheCanonMakerNote()
    {
        ExifMetadata exif = Exif(Create().Bytes);

        MakerNote? note = exif.MakerNote;
        Assert.NotNull(note);
        Assert.Equal(MakerNoteFormat.Canon, note.Format);
        Assert.Equal(Endianness.LittleEndian, note.ByteOrder);
        // Counted from the TIFF header of the box the note was read from, where its directory starts.
        Assert.Equal(8L, note.Offset);
        Assert.Equal(new ushort[] { 0x0001, 0x0006, 0x0007, 0x0010 }, note.Entries.Select(entry => entry.TagId));
        Assert.Equal(new string?[] { "CameraSettings", "ImageType", "FirmwareVersion", "ModelID" }, note.Entries.Select(entry => entry.Name));
        Assert.Equal("IMG:EOS R6 CR3", Ascii(note.Entries[1].Value));
        Assert.Equal("Firmware Version 1.0.0", Ascii(note.Entries[2].Value));
        Assert.Equal(new byte[] { 0x53, 0x04, 0x00, 0x80 }, Bytes(note.Entries[3].Value));
        // The array of the first tag is split into the fields the Canon reader knows, as in a JPEG.
        MakerNoteEntry settings = note.Entries[0];
        Assert.Equal(10u, settings.Value.Count);
        Assert.Equal(new[] { "Macro", "Selftimer", "Quality", "FlashMode", "DriveMode", "FocusMode", "RecordMode" },
            settings.Fields.Select(field => field.Name));
        Assert.Equal(new byte[] { 4, 0 }, Bytes(settings.Fields[5].Value));
    }

    [Fact]
    public void BigEndianBoxesAreReadInTheirOrder()
    {
        byte[] file = File(
            Box("CMT1", Ifd0(false)),
            Box("CMT2", TiffBlock(false, Shorts(0x8827, 100))),
            Box("CMT3", TiffBlock(false, Text(0x0006, "IMG:EOS R6 CR3"), Longs(0x0010, 0x80000453))),
            Box("CMT4", TiffBlock(false, Text(0x0001, "N"))));

        ExifMetadata exif = Exif(file);

        Assert.Equal(Endianness.BigEndian, exif.ByteOrder);
        Assert.Equal(new byte[] { 0, 6 }, Bytes(exif, Primary, 0x0112));
        Assert.Equal(new byte[] { 0, 100 }, Bytes(exif, Photo, 0x8827));
        Assert.Equal(new byte[] { (byte)'N', 0 }, Bytes(exif, Position, 0x0001));
        Assert.NotNull(exif.MakerNote);
        Assert.Equal(Endianness.BigEndian, exif.MakerNote.ByteOrder);
        Assert.Equal(new byte[] { 0x80, 0x00, 0x04, 0x53 }, Bytes(exif.MakerNote.Entries[1].Value));
    }

    [Theory]
    [InlineData("CMT1", ExifDirectoryKind.Ifd0)]
    [InlineData("CMT2", ExifDirectoryKind.Exif)]
    [InlineData("CMT4", ExifDirectoryKind.Gps)]
    public void ABoxAloneIsReadAsItsDirectory(string box, ExifDirectoryKind kind)
    {
        ExifMetadata exif = Exif(File(Box("free", new byte[4]), Box(box, TiffBlock(true, Shorts(0x0001, 7), Longs(0x0002, 9)))));

        Assert.Equal(new[] { Key(kind, 0x0001), Key(kind, 0x0002) }, exif.Entries.Select(entry => entry.Key));
        Assert.Null(exif.MakerNote);
    }

    [Fact]
    public void TheMakerNoteBoxAloneIsABlockWithoutEntries()
    {
        ExifMetadata exif = Exif(File(Box("CMT3", TiffBlock(false, Text(0x0006, "IMG:EOS R6 CR3")))));

        Assert.Empty(exif.Entries);
        Assert.Equal(Endianness.BigEndian, exif.ByteOrder);
        Assert.NotNull(exif.MakerNote);
        Assert.Equal("ImageType", Assert.Single(exif.MakerNote.Entries).Name);
    }

    [Fact]
    public void AFileWithoutTheBoxesHasNoExif()
    {
        Assert.Null(Cr3Metadata.Read(File(Box("CNCV", new byte[30]))).Exif);
        Assert.Null(Cr3Metadata.Read(Join(FileType(), Box("mdat", new byte[16]))).Exif);
        Assert.Null(Cr3Metadata.Read(Create(new Layout { Ifd0 = null, Exif = null, MakerNote = null, Gps = null }).Bytes).Exif);
    }

    [Fact]
    public void TheMakerNoteIsCanonWhateverTheMakeTagSays()
    {
        byte[] note = TiffBlock(true, Text(0x0006, "IMG:EOS R6 CR3"));

        MakerNote? unnamed = Exif(File(Box("CMT1", TiffBlock(true, Shorts(0x0112, 1))), Box("CMT3", note))).MakerNote;
        MakerNote? renamed = Exif(File(Box("CMT1", TiffBlock(true, Text(0x010F, "NIKON CORPORATION"))), Box("CMT3", note))).MakerNote;

        Assert.NotNull(unnamed);
        Assert.NotNull(renamed);
        Assert.Equal(MakerNoteFormat.Canon, unnamed.Format);
        Assert.Equal(MakerNoteFormat.Canon, renamed.Format);
    }

    [Fact]
    public void TheMakerNoteMayHaveAByteOrderOfItsOwn()
    {
        ExifMetadata exif = Exif(File(Box("CMT1", Ifd0()), Box("CMT3", TiffBlock(false, Longs(0x0010, 0x80000453)))));

        Assert.Equal(Endianness.LittleEndian, exif.ByteOrder);
        Assert.NotNull(exif.MakerNote);
        Assert.Equal(Endianness.BigEndian, exif.MakerNote.ByteOrder);
    }

    [Fact]
    public void TheDirectoryOfABoxNeedNotFollowItsHeader()
    {
        // The header leads to a directory at 16; the eight bytes between are not read.
        byte[] tiff = Join(new byte[] { 0x49, 0x49, 42, 0, 16, 0, 0, 0 }, new byte[8], Ifd(true, 16, 0, Text(0x0006, "IMG:EOS R6 CR3")));

        ExifMetadata exif = Exif(File(Box("CMT2", tiff), Box("CMT3", tiff)));

        Assert.Equal("IMG:EOS R6 CR3", Ascii(Assert.Single(exif.Entries).Value));
        Assert.NotNull(exif.MakerNote);
        Assert.Equal(16L, exif.MakerNote.Offset);
        Assert.Equal("IMG:EOS R6 CR3", Ascii(Assert.Single(exif.MakerNote.Entries).Value));
    }

    [Fact]
    public void AnExifBoxMayLeadToAnInteroperabilityDirectory()
    {
        int at = TiffBlock(true, Longs(0xA005, 0)).Length;
        byte[] tiff = Join(TiffBlock(true, Longs(0xA005, (uint)at)), Ifd(true, at, 0, Text(0x0001, "R98")));

        ExifMetadata exif = Exif(File(Box("CMT2", tiff)));

        Assert.Equal(new[] { Key(Photo, 0xA005), Key(ExifDirectoryKind.Interop, 0x0001) }, exif.Entries.Select(entry => entry.Key));
    }

    [Fact]
    public void APrimaryBoxMayCarryDirectoriesOfItsOwnWhenNoOtherBoxDeclaresThem()
    {
        int at = TiffBlock(true, Longs(0x8769, 0)).Length;
        byte[] primary = Join(TiffBlock(true, Longs(0x8769, (uint)at)), Ifd(true, at, 0, Shorts(0x8827, 200)));

        ExifMetadata exif = Exif(File(Box("CMT1", primary), Box("CMT4", TiffBlock(true, Text(0x0001, "S")))));

        Assert.Equal(new[] { Key(Primary, 0x8769), Key(Photo, 0x8827), Key(Position, 0x0001) }, exif.Entries.Select(entry => entry.Key));
    }

    [Fact]
    public void ADirectoryTwoBoxesDeclareIsRefused()
    {
        int at = TiffBlock(true, Longs(0x8769, 0)).Length;
        byte[] withExif = Join(TiffBlock(true, Longs(0x8769, (uint)at)), Ifd(true, at, 0, Shorts(0x8827, 200)));
        byte[] withGps = Join(TiffBlock(true, Longs(0x8825, (uint)at)), Ifd(true, at, 0, Text(0x0001, "S")));

        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Refusal(File(Box("CMT1", withExif), Box("CMT2", TiffBlock(true, Shorts(0x8827, 100))))));
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Refusal(File(Box("CMT1", withGps), Box("CMT4", TiffBlock(true, Text(0x0001, "N"))))));
    }

    [Fact]
    public void BoxesOfDifferentByteOrdersAreRefused()
    {
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(File(Box("CMT1", Ifd0(true)), Box("CMT2", TiffBlock(false, Shorts(0x8827, 100))))));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(File(Box("CMT2", TiffBlock(false, Shorts(0x8827, 100))), Box("CMT4", TiffBlock(true, Text(0x0001, "N"))))));
    }

    public static TheoryData<string, string, byte[], MetadataErrorCode> Malformed()
    {
        byte[] good = TiffBlock(true, Text(0x0006, "IMG:EOS R6 CR3"), Longs(0x0010, 5));
        byte[] Patched(int at, params byte[] bytes)
        {
            byte[] copy = (byte[])good.Clone();
            bytes.CopyTo(copy, at);
            return copy;
        }
        int second = TiffBlock(true, Longs(0x8769, 0)).Length;
        byte[] pointer = Join(TiffBlock(true, Longs(0x8769, (uint)second)), Ifd(true, second, 0, Shorts(0x8827, 200)));
        var data = new TheoryData<string, string, byte[], MetadataErrorCode>();
        foreach (string box in new[] { "CMT1", "CMT2", "CMT3", "CMT4" })
        {
            data.Add(box, "empty", [], MetadataErrorCode.InvalidExif);
            data.Add(box, "shorter than a header", good[..7], MetadataErrorCode.InvalidExif);
            data.Add(box, "no byte order mark", Patched(0, 0x49, 0x4D), MetadataErrorCode.InvalidExif);
            data.Add(box, "another identifier", Patched(2, 0x55), MetadataErrorCode.InvalidExif);
            data.Add(box, "no directory", Patched(4, 0), MetadataErrorCode.InvalidExif);
            data.Add(box, "directory beyond the box", Patched(4, (byte)(good.Length - 1)), MetadataErrorCode.InvalidExif);
        }
        foreach (string box in new[] { "CMT1", "CMT2", "CMT4" })
        {
            data.Add(box, "value beyond the box", Patched(18, (byte)(good.Length - 14)), MetadataErrorCode.InvalidExif);
            data.Add(box, "tags out of order", TiffBlock(true, Longs(0x0010, 5), Shorts(0x0006, 1)), MetadataErrorCode.InvalidExif);
            data.Add(box, "a tag twice", TiffBlock(true, Shorts(0x0006, 1), Shorts(0x0006, 2)), MetadataErrorCode.AmbiguousMetadata);
            data.Add(box, "BigTIFF", Patched(2, 43), MetadataErrorCode.UnsupportedFeature);
        }
        // A pointer belongs to the directory that owns it, and only the primary one is followed by another.
        data.Add("CMT2", "an Exif pointer in the Exif directory", pointer, MetadataErrorCode.InvalidExif);
        data.Add("CMT4", "an Exif pointer in the GPS directory", pointer, MetadataErrorCode.InvalidExif);
        data.Add("CMT1", "an interoperability pointer in the primary directory", TiffBlock(true, Longs(0xA005, 8)), MetadataErrorCode.InvalidExif);
        data.Add("CMT2", "a second directory after the Exif directory", TiffBlock(true, 8, [Shorts(0x8827, 100)]), MetadataErrorCode.UnsupportedFeature);
        data.Add("CMT1", "a pointer to the directory itself", TiffBlock(true, Longs(0x8769, 8)), MetadataErrorCode.InvalidExif);
        return data;
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public void AMalformedBoxIsRefused(string box, string what, byte[] tiff, MetadataErrorCode code)
    {
        Assert.False(string.IsNullOrEmpty(what));
        // A sound primary box comes first, unless the primary box is the one under test.
        byte[] file = box == "CMT1" ? File(Box(box, tiff)) : File(Box("CMT1", Ifd0()), Box(box, tiff));

        Assert.Equal(code, Refusal(file));
    }

    [Fact]
    public void ARefusalOfADirectorySaysWhereInTheFile()
    {
        byte[] tiff = TiffBlock(true, Longs(0x0010, 5), Shorts(0x0006, 1));
        byte[] file = File(Box("CMT1", Ifd0()), Box("CMT2", tiff));
        int box = 24 + 8 + 8 + 16 + 8 + Ifd0().Length + 8;

        MetadataException refusal = Assert.Throws<MetadataException>(() => Cr3Metadata.Read(file));

        // The second entry of the directory: the header, the count and one entry further.
        Assert.Equal(box + 8 + 2 + 12L, refusal.SourceOffset);
        Assert.Equal(tiff, file.AsSpan(box, tiff.Length).ToArray());
    }

    [Theory]
    [InlineData(0xFFFF)]
    [InlineData(0)]
    [InlineData(3)]
    public void AMakerNoteDirectoryThatDoesNotHoldLeavesTheNoteUndecoded(int count)
    {
        // A header that holds, then a directory that states more entries than the box has room for,
        // or none at all: the MakerNote reader gives up, and the other boxes are read as before.
        byte[] note = Join(new byte[] { 0x49, 0x49, 42, 0, 8, 0, 0, 0, (byte)count, (byte)(count >> 8) }, new byte[30]);

        ExifMetadata exif = Exif(File(Box("CMT1", Ifd0()), Box("CMT3", note)));

        Assert.Null(exif.MakerNote);
        Assert.Equal(2, exif.Entries.Count);
    }

    [Fact]
    public void TheRetainedBudgetBoundsTheBoxesTogetherAndTheNoteTakesWhatIsLeft()
    {
        var plan = new Layout { Xmp = null };
        byte[] file = Create(plan).Bytes;
        var limits = new MetadataLimits();
        ExifMetadata whole = Exif(file, limits);
        Assert.NotNull(whole.MakerNote);
        // Each box counts as its bytes and, for the three the directory reader reads, its values again.
        int needed = plan.Ifd0!.Length + plan.Exif!.Length + plan.Gps!.Length + plan.MakerNote!.Length +
            whole.Entries.Sum(entry => entry.Value.ByteLength);

        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, limits with { MaxRetainedMetadataBytes = needed - 1 }));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, limits with { MaxRetainedMetadataBytes = plan.Ifd0.Length }));
        ExifMetadata tight = Exif(file, limits with { MaxRetainedMetadataBytes = needed });
        Assert.Equal(whole.Entries.Count, tight.Entries.Count);
        // Decoding the note is what the budget had no room left for: it is given up, not refused.
        Assert.Null(tight.MakerNote);
        int decoded = 2 * whole.MakerNote.Entries.Sum(entry => entry.Value.ByteLength);
        Assert.Null(Exif(file, limits with { MaxRetainedMetadataBytes = needed + decoded - 1 }).MakerNote);
        Assert.NotNull(Exif(file, limits with { MaxRetainedMetadataBytes = needed + decoded }).MakerNote);
    }

    [Fact]
    public void TheEntryAndValueBudgetsBoundTheBoxes()
    {
        byte[] file = Create().Bytes;
        var limits = new MetadataLimits();

        // Four entries, four entries and three: eleven in all.
        Assert.Equal(11, Exif(file, limits with { MaxTotalEntries = 11 }).Entries.Count);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, limits with { MaxTotalEntries = 10 }));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, limits with { MaxTotalEntries = 8 }));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, limits with { MaxTotalEntries = 3 }));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, limits with { MaxEntriesPerDirectory = 3 }));
        Assert.Equal(11, Exif(file, limits with { MaxEntriesPerDirectory = 4 }).Entries.Count);
        // The longest value of the three boxes is the date, twenty bytes.
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, limits with { MaxValueBytes = 19 }));
        Assert.Equal(11, Exif(file, limits with { MaxValueBytes = 20 }).Entries.Count);
    }

    [Fact]
    public void EveryTruncationIsReadOrRefused()
    {
        byte[] file = Create().Bytes;
        for (int length = 0; length < file.Length; length++)
        {
            try { Cr3Metadata.Read(file.AsSpan(0, length)); }
            catch (MetadataException refusal) { Assert.Equal(MetadataErrorCode.InvalidBmff, refusal.Code); }
        }
    }

    [Fact]
    public void ADamagedByteIsReadOrRefused()
    {
        byte[] file = Create().Bytes;
        int read = 0, refused = 0;
        foreach (byte damage in new byte[] { 0x00, 0x01, 0x7F, 0xFF })
            for (int at = 0; at < file.Length; at++)
            {
                byte[] copy = (byte[])file.Clone();
                copy[at] = damage;
                try
                {
                    Cr3Metadata.Read(copy);
                    read++;
                }
                catch (MetadataException)
                {
                    refused++;
                }
            }
        Assert.True(read > 0 && refused > 0);
    }
}
