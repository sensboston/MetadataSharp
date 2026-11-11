using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Formats.Tiff;
using Xunit;

namespace MetadataSharp.Tests.Tiff;

public sealed class TiffExifTests
{
    private const int LargeLength = 70_000;
    private const int PastApp1 = 66_000; // beyond both the 65,527-byte APP1 cap and 64 KiB
    private const int PastTheEnd = 0x00F0_0000; // an offset no file built here reaches

    /// <summary>
    /// Authored synthetic TIFF bytes: every offset is written by the test, none comes from production
    /// code. A SHORT with one value is stored in the value field itself; everything else is stored as
    /// the given 32-bit number, which is an inline LONG or the offset of an external value.
    /// </summary>
    private sealed class Layout(int length, bool big)
    {
        internal byte[] Bytes { get; } = new byte[length];

        internal Layout Header(int magic = 42, long first = 8)
        {
            Bytes[0] = Bytes[1] = big ? (byte)0x4D : (byte)0x49;
            U16(2, magic);
            U32(4, first);
            return this;
        }

        internal Layout Directory(int at, long next, params (int Tag, int Type, long Count, long Value)[] entries)
        {
            U16(at, entries.Length);
            for (int i = 0; i < entries.Length; i++)
            {
                int p = at + 2 + i * 12;
                U16(p, entries[i].Tag);
                U16(p + 2, entries[i].Type);
                U32(p + 4, entries[i].Count);
                if (entries[i].Type == 3 && entries[i].Count == 1)
                    U16(p + 8, (int)entries[i].Value);
                else
                    U32(p + 8, entries[i].Value);
            }
            U32(at + 2 + entries.Length * 12, next);
            return this;
        }

        internal Layout Ascii(int at, string text)
        {
            Encoding.ASCII.GetBytes(text).CopyTo(Bytes, at);
            return this;
        }

        internal Layout Raw(int at, params byte[] bytes)
        {
            bytes.CopyTo(Bytes, at);
            return this;
        }

        internal Layout U16(int at, int value)
        {
            if (big) BinaryPrimitives.WriteUInt16BigEndian(Bytes.AsSpan(at), (ushort)value);
            else BinaryPrimitives.WriteUInt16LittleEndian(Bytes.AsSpan(at), (ushort)value);
            return this;
        }

        internal Layout U32(int at, long value)
        {
            if (big) BinaryPrimitives.WriteUInt32BigEndian(Bytes.AsSpan(at), (uint)value);
            else BinaryPrimitives.WriteUInt32LittleEndian(Bytes.AsSpan(at), (uint)value);
            return this;
        }
    }

    /// <summary>One inline Orientation entry in the primary directory: the smallest readable file.</summary>
    private static byte[] Orientation(bool big = false) =>
        new Layout(26, big).Header().Directory(8, 0, (0x0112, 3, 1, 6)).Bytes;

    /// <summary>
    /// IFD0 at 8 (Orientation, ExifIFD) and an Exif IFD at 66,000 whose DateTimeOriginal is stored at
    /// 66,100, in a file of 70,000 bytes. Read in full it retains 74 bytes: 30 and 18 of directory and
    /// 2 + 4 + 20 of values.
    /// </summary>
    private static byte[] Large(bool big = false) =>
        new Layout(LargeLength, big).Header()
            .Directory(8, 0, (0x0112, 3, 1, 6), (0x8769, 4, 1, PastApp1))
            .Directory(PastApp1, 0, (0x9003, 2, 20, PastApp1 + 100))
            .Ascii(PastApp1 + 100, "2026:11:11 10:20:30\0")
            .Bytes;

    /// <summary>
    /// IFD0 at 8 (Make, StripOffsets, ExifIFD) -> Exif IFD (MakerNote) -> a Canon MakerNote laid out as
    /// in <see cref="CanonMakerNoteTests"/>: ImageType (external ASCII), ModelID (inline LONG),
    /// FocalLength (external SHORT[4]), an unknown tag, then the 8-byte footer. The Exif IFD sits at
    /// <paramref name="exifAt"/> and the MakerNote 100 bytes after it; its offsets count from the header.
    /// </summary>
    private static byte[] Canon(bool big, int exifAt, int length)
    {
        int note = exifAt + 100;
        return new Layout(length, big).Header()
            .Directory(8, 0, (0x010F, 2, 6, 60), (0x0111, 4, 1, 1024), (0x8769, 4, 1, exifAt))
            .Ascii(60, "Canon\0")
            .Directory(exifAt, 0, (0x927C, 7, 78, note))
            .Directory(note, 0, (0x0002, 3, 4, note + 62), (0x0006, 2, 8, note + 54),
                (0x0010, 4, 1, 0x80000175), (0x7777, 3, 1, 9))
            .Ascii(note + 54, "EOS 20D\0")
            .U16(note + 62, 2).U16(note + 64, 95).U16(note + 66, 907).U16(note + 68, 605)
            .Raw(note + 70, big ? (byte)0x4D : (byte)0x49, big ? (byte)0x4D : (byte)0x49)
            .U16(note + 72, 42).U32(note + 74, note)
            .Bytes;
    }

    /// <summary>IFD0 at 8 (Orientation) chained to an IFD at 26 that addresses a 4-byte JPEG at 70.</summary>
    private static byte[] WithThumbnail(bool big = false, long thumbnailAt = 70) =>
        new Layout(74, big).Header()
            .Directory(8, 26, (0x0112, 3, 1, 6))
            .Directory(26, 0, (0x0103, 3, 1, 6), (0x0201, 4, 1, thumbnailAt), (0x0202, 4, 1, 4))
            .Raw(70, 0xFF, 0xD8, 0xFF, 0xD9)
            .Bytes;

    private static string Ascii(ExifValue value)
    {
        byte[] bytes = new byte[value.ByteLength];
        value.CopyRawTo(bytes);
        return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
    }

    private static uint Long(ExifValue value, int index = 0)
    {
        byte[] bytes = new byte[value.ByteLength];
        value.CopyRawTo(bytes);
        return value.ByteOrder == Endianness.BigEndian
            ? BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(index * 4))
            : BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index * 4));
    }

    private static MetadataErrorCode Code(Action read) => Assert.Throws<MetadataException>(read).Code;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFileLargerThanTheApp1CapIsReadWithItsExifDirectoryPast64KiB(bool big)
    {
        byte[] file = Large(big);
        Assert.True(file.Length > 65_527);

        ExifMetadata metadata = TiffExif.Read(file);
        Assert.Equal(big ? Endianness.BigEndian : Endianness.LittleEndian, metadata.ByteOrder);
        Assert.Equal(
            new ExifTagKey[] { new(ExifDirectoryKind.Ifd0, 0x0112), new(ExifDirectoryKind.Ifd0, 0x8769), new(ExifDirectoryKind.Exif, 0x9003) },
            metadata.Entries.Select(entry => entry.Key));
        Assert.True(metadata.TryGetEntry(new(ExifDirectoryKind.Exif, 0x9003), out ExifEntry? taken));
        Assert.Equal("2026:11:11 10:20:30", Ascii(taken.Value));
        Assert.Null(metadata.MakerNote);

        // The snapshot owns its values.
        file.AsSpan().Clear();
        Assert.Equal("2026:11:11 10:20:30", Ascii(taken.Value));
    }

    [Fact]
    public void TheApp1PathKeepsItsSizeCap()
    {
        Assert.Equal(MetadataErrorCode.ExifTooLarge, Code(() => ExifTiffReader.Read(Large())));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StripTileAndSubIfdTagsAreReturnedAsEntriesAndNeverFollowed(bool big)
    {
        // The strip and the SubIFD lie past the end of the file: reading either would fail.
        byte[] file = new Layout(96, big).Header()
            .Directory(8, 0, (0x0111, 4, 1, PastTheEnd), (0x0117, 4, 1, 4096), (0x0144, 4, 2, 80),
                (0x0145, 4, 2, 88), (0x014A, 4, 1, PastTheEnd))
            .U32(80, 1024).U32(84, 2048).U32(88, 512).U32(92, 256)
            .Bytes;

        ExifMetadata metadata = TiffExif.Read(file);
        Assert.Equal(new ushort[] { 0x0111, 0x0117, 0x0144, 0x0145, 0x014A }, metadata.Entries.Select(entry => entry.Key.TagId));
        Assert.All(metadata.Entries, entry => Assert.Equal(ExifDirectoryKind.Ifd0, entry.Key.Directory));
        Assert.All(metadata.Entries, entry => Assert.Equal(TiffFieldType.Long, entry.Value.FieldType));

        Assert.True(metadata.TryGetEntry(new(ExifDirectoryKind.Ifd0, 0x0111), out ExifEntry? strip));
        Assert.Equal((uint)PastTheEnd, Long(strip.Value));
        Assert.True(metadata.TryGetEntry(new(ExifDirectoryKind.Ifd0, 0x0144), out ExifEntry? tiles));
        Assert.Equal(2u, tiles.Value.Count);
        Assert.Equal(1024u, Long(tiles.Value));
        Assert.Equal(2048u, Long(tiles.Value, 1));
        Assert.True(metadata.TryGetEntry(new(ExifDirectoryKind.Ifd0, 0x014A), out ExifEntry? subIfds));
        Assert.Equal((uint)PastTheEnd, Long(subIfds.Value));

        Assert.Equal(new[] { ExifDirectoryKind.Ifd0 }, ExifTiffReader.ReadFile(file).Directories);
    }

    [Theory]
    [InlineData(0x0111)]
    [InlineData(0x0117)]
    [InlineData(0x0144)]
    [InlineData(0x0145)]
    [InlineData(0x014A)]
    public void TheApp1PathStillRefusesStripTileAndSubIfdTags(int tag)
    {
        byte[] file = new Layout(26, false).Header().Directory(8, 0, (tag, 4, 1, 8)).Bytes;

        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Code(() => ExifTiffReader.Read(file)));
        Assert.Equal((ushort)tag, Assert.Single(TiffExif.Read(file).Entries).Key.TagId);
    }

    [Theory]
    [InlineData(0x49, 0x49, 0x52, 0x4F)] // "IIRO": ORF, little-endian
    [InlineData(0x4D, 0x4D, 0x4F, 0x52)] // "MMOR": ORF, big-endian
    [InlineData(0x49, 0x49, 0x55, 0x00)] // RW2, little-endian
    [InlineData(0x4D, 0x4D, 0x00, 0x55)] // the RW2 identifier in big-endian order
    public void OrfAndRw2HeadersAreAcceptedInAWholeFileOnly(int b0, int b1, int b2, int b3)
    {
        bool big = b0 == 0x4D;
        byte[] file = Orientation(big);
        new byte[] { (byte)b0, (byte)b1, (byte)b2, (byte)b3 }.CopyTo(file, 0);

        ExifMetadata metadata = TiffExif.Read(file);
        Assert.Equal(big ? Endianness.BigEndian : Endianness.LittleEndian, metadata.ByteOrder);
        Assert.Equal(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0112), Assert.Single(metadata.Entries).Key);

        Assert.Equal(MetadataErrorCode.InvalidExif, Code(() => ExifTiffReader.Read(file)));
    }

    [Theory]
    [InlineData(0x49, 0x49, 0x29, 0x00)] // identifier 41
    [InlineData(0x49, 0x4D, 0x2A, 0x00)] // mixed byte order mark
    [InlineData(0x00, 0x00, 0x2A, 0x00)] // no byte order mark
    [InlineData(0x49, 0x49, 0x4F, 0x52)] // "IIOR": the ORF letters in the other order
    [InlineData(0x4D, 0x4D, 0x52, 0x4F)] // "MMRO"
    [InlineData(0x49, 0x49, 0x52, 0x53)] // "IIRS"
    [InlineData(0x49, 0x49, 0x00, 0x55)] // the RW2 identifier byte-swapped
    [InlineData(0xFF, 0xD8, 0xFF, 0xE1)] // JPEG
    [InlineData(0x89, 0x50, 0x4E, 0x47)] // PNG
    public void ANonTiffHeaderIsRefusedWithInvalidTiff(int b0, int b1, int b2, int b3)
    {
        byte[] file = Orientation();
        new byte[] { (byte)b0, (byte)b1, (byte)b2, (byte)b3 }.CopyTo(file, 0);

        MetadataException error = Assert.Throws<MetadataException>(() => TiffExif.Read(file));
        Assert.Equal(MetadataErrorCode.InvalidTiff, error.Code);
        Assert.Equal(0L, error.SourceOffset);
    }

    [Fact]
    public void ATruncatedHeaderIsNotATiffHeader()
    {
        byte[] file = Orientation();
        for (int length = 0; length < 8; length++)
        {
            byte[] prefix = file[..length];
            Assert.Equal(MetadataErrorCode.InvalidTiff, Code(() => TiffExif.Read(prefix)));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BigTiffIsRecognisedAndUnsupported(bool big)
    {
        byte[] file = new Layout(26, big).Header(magic: 43).Directory(8, 0, (0x0112, 3, 1, 6)).Bytes;
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Code(() => TiffExif.Read(file)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACanonMakerNoteIsDecodedInAWholeFile(bool big)
    {
        byte[] file = Canon(big, PastApp1, LargeLength);

        ExifMetadata metadata = TiffExif.Read(file);
        Assert.True(metadata.TryGetEntry(new(ExifDirectoryKind.Ifd0, 0x0111), out _));
        Assert.True(metadata.TryGetEntry(new(ExifDirectoryKind.Exif, 0x927C), out ExifEntry? raw));
        Assert.Equal(78, raw.Value.ByteLength);

        MakerNote? note = metadata.MakerNote;
        Assert.NotNull(note);
        Assert.Equal(MakerNoteFormat.Canon, note.Format);
        Assert.Equal(big ? Endianness.BigEndian : Endianness.LittleEndian, note.ByteOrder);
        Assert.Equal(new ushort[] { 0x0002, 0x0006, 0x0010, 0x7777 }, note.Entries.Select(entry => entry.TagId));

        Assert.True(note.TryGetEntry(0x0006, out MakerNoteEntry? imageType));
        Assert.Equal("ImageType", imageType.Name);
        Assert.Equal("EOS 20D", Ascii(imageType.Value));
        Assert.True(note.TryGetEntry(0x0002, out MakerNoteEntry? focal));
        Assert.Equal("FocalLength", focal.Name);
        Assert.Equal(4u, focal.Value.Count);
        Assert.True(note.TryGetEntry(0x0010, out MakerNoteEntry? model));
        Assert.Equal("ModelID", model.Name);
        Assert.Equal(0x80000175u, Long(model.Value));
        Assert.True(note.TryGetEntry(0x7777, out MakerNoteEntry? unknown));
        Assert.Null(unknown.Name);

        file.AsSpan().Clear();
        Assert.Equal("EOS 20D", Ascii(imageType.Value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectoriesChainedAfterTheSecondAreNeitherReadNorRefused(bool big)
    {
        // The third directory lies past the end of the file: reading it would fail.
        byte[] file = new Layout(44, big).Header()
            .Directory(8, 26, (0x0112, 3, 1, 6))
            .Directory(26, PastTheEnd, (0x0100, 3, 1, 160))
            .Bytes;

        TiffReadResult result = ExifTiffReader.ReadFile(file);
        Assert.Equal(new[] { ExifDirectoryKind.Ifd0, ExifDirectoryKind.Thumbnail }, result.Directories);
        Assert.Equal(
            new ExifTagKey[] { new(ExifDirectoryKind.Ifd0, 0x0112), new(ExifDirectoryKind.Thumbnail, 0x0100) },
            TiffExif.Read(file).Entries.Select(entry => entry.Key));

        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Code(() => ExifTiffReader.Read(file)));
    }

    [Fact]
    public void AnExifDirectoryThatChainsToAnotherIsStillRefused()
    {
        byte[] file = new Layout(64, false).Header()
            .Directory(8, 0, (0x8769, 4, 1, 26))
            .Directory(26, 40)
            .Bytes;

        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Code(() => TiffExif.Read(file)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheThumbnailLogicIsTheSameAsOnTheApp1Path(bool big)
    {
        byte[] file = WithThumbnail(big);
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }, ExifTiffReader.ReadFile(file).Thumbnail);
        Assert.Equal(ExifTiffReader.Read(file).Thumbnail, ExifTiffReader.ReadFile(file).Thumbnail);
        Assert.Equal(4, TiffExif.Read(file).Entries.Count);
    }

    [Theory]
    [InlineData(PastTheEnd)] // outside the file
    [InlineData(71)]         // unaligned, and no longer a whole JPEG
    [InlineData(40)]         // inside the file, but not a JPEG
    public void AFailingThumbnailIsDroppedAndTheMetadataIsStillRead(int thumbnailAt)
    {
        byte[] file = WithThumbnail(thumbnailAt: thumbnailAt);
        Assert.Null(ExifTiffReader.ReadFile(file).Thumbnail);
        Assert.Equal(4, TiffExif.Read(file).Entries.Count);
    }

    [Fact]
    public void TheInputBudgetBoundsTheFile()
    {
        byte[] file = Orientation();
        Assert.Single(TiffExif.Read(file, new MetadataLimits { MaxInputBytes = file.Length }).Entries);
        Assert.Equal(MetadataErrorCode.LimitExceeded,
            Code(() => TiffExif.Read(file, new MetadataLimits { MaxInputBytes = file.Length - 1 })));
        // The budget is checked before the header: bytes over budget are not inspected at all.
        Assert.Equal(MetadataErrorCode.LimitExceeded,
            Code(() => TiffExif.Read(new byte[64], new MetadataLimits { MaxInputBytes = 63 })));
    }

    [Fact]
    public void TheRetainedBudgetCountsWhatIsReadRatherThanTheFile()
    {
        byte[] file = Large();
        // 74 bytes of directories and values are retained out of a 70,000-byte file.
        Assert.Equal(3, TiffExif.Read(file, new MetadataLimits { MaxRetainedMetadataBytes = 74 }).Entries.Count);
        Assert.Equal(MetadataErrorCode.LimitExceeded,
            Code(() => TiffExif.Read(file, new MetadataLimits { MaxRetainedMetadataBytes = 73 })));
    }

    [Fact]
    public void TheValueBudgetIsNotRaisedForAWholeFile()
    {
        // One BYTE value of 65,528 bytes: a byte over the default value budget.
        byte[] file = new Layout(LargeLength, false).Header().Directory(8, 0, (0xC000, 1, 65_528, 1024)).Bytes;

        Assert.Equal(MetadataErrorCode.LimitExceeded, Code(() => TiffExif.Read(file)));
        var raised = new MetadataLimits { MaxValueBytes = 65_528 };
        Assert.Equal(65_528, Assert.Single(TiffExif.Read(file, raised).Entries).Value.ByteLength);
    }

    [Fact]
    public void InvalidLimitsAreACallerError()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TiffExif.Read(Orientation(), new MetadataLimits { MaxDirectories = 0 }));
    }

    [Theory]
    [InlineData("past-ifd", MetadataErrorCode.InvalidExif)]
    [InlineData("zero-ifd", MetadataErrorCode.InvalidExif)]
    [InlineData("bad-value", MetadataErrorCode.InvalidExif)]
    [InlineData("type", MetadataErrorCode.UnsupportedFeature)]
    [InlineData("pointer-type", MetadataErrorCode.InvalidExif)]
    public void DirectoryProblemsKeepTheCodesOfTheExifReader(string mutation, MetadataErrorCode code)
    {
        var layout = new Layout(40, false);
        layout.Header().Directory(8, 0, (0x0112, 3, 1, 6));
        switch (mutation)
        {
            case "past-ifd": layout.U32(4, PastTheEnd); break;
            case "zero-ifd": layout.U32(4, 0); break;
            case "bad-value": layout.U32(14, 3).U32(18, PastTheEnd); break;
            case "type": layout.U16(12, 13); break;
            case "pointer-type": layout.U16(10, 0x8769); break;
        }
        Assert.Equal(code, Code(() => TiffExif.Read(layout.Bytes)));
    }

    [Fact]
    public void EveryTruncatedPrefixIsRefusedWithAMetadataError()
    {
        // The MakerNote value ends with the file, so every shorter prefix cuts a structure short.
        byte[] file = Canon(false, 100, 278);
        Assert.NotNull(TiffExif.Read(file).MakerNote);
        for (int length = 0; length < file.Length; length++)
        {
            byte[] prefix = file[..length];
            MetadataException error = Assert.Throws<MetadataException>(() => TiffExif.Read(prefix));
            Assert.Equal(length < 8 ? MetadataErrorCode.InvalidTiff : MetadataErrorCode.InvalidExif, error.Code);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundedMutationSmokeTestNeverLeaksIndexOrOverflowExceptions(bool big)
    {
        var random = new Random(8192);
        foreach (byte[] source in new[] { Canon(big, 100, 278), WithThumbnail(big) })
        {
            for (int i = 0; i < 512; i++)
            {
                byte[] file = (byte[])source.Clone();
                file[random.Next(file.Length)] = (byte)random.Next(256);
                try { TiffExif.Read(file); }
                catch (MetadataException) { }
            }
        }
    }
}
