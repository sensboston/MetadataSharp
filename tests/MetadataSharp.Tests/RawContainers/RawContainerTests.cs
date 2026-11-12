using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.MakerNotes;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Crw;
using MetadataSharp.Formats.Mrw;
using MetadataSharp.Formats.Raf;
using MetadataSharp.Tests.Jpeg;
using Xunit;
using static MetadataSharp.Tests.Cr3.Cr3Fixture;

namespace MetadataSharp.Tests.RawContainers;

/// <summary>The readers of the RAW containers that are no TIFF file: Canon CRW, Minolta MRW and Fujifilm RAF, on authored files.</summary>
public sealed class RawContainerTests
{
    private static byte[] Le16(int value) => [(byte)value, (byte)(value >> 8)];

    private static byte[] Le32(uint value) => [(byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24)];

    /// <summary>A CIFF directory: the values, then the entries, then where the entries start.</summary>
    private static byte[] Heap(params (ushort Tag, byte[] Value)[] records)
    {
        var values = new List<byte>();
        var entries = new List<byte>();
        foreach ((ushort tag, byte[] value) in records)
        {
            if ((tag & 0xC000) == 0x4000)
            {
                entries.AddRange([.. Le16(tag), .. value, .. new byte[8 - value.Length]]);
                continue;
            }
            entries.AddRange([.. Le16(tag), .. Le32((uint)value.Length), .. Le32((uint)values.Count)]);
            values.AddRange(value);
        }
        return [.. values, .. Le16(records.Length), .. entries, .. Le32((uint)values.Count)];
    }

    private static byte[] Crw(params (ushort Tag, byte[] Value)[] root) =>
        [.. "II"u8, .. Le32(14), .. "HEAPCCDR"u8, .. Heap(root)];

    private static byte[] Shorts16(params ushort[] values) => values.SelectMany(value => Le16(value)).ToArray();

    private static byte[] ShotInfo()
    {
        ushort[] values = new ushort[34];
        values[0] = 68;
        values[21] = 99;
        values[22] = 126;
        return Shorts16(values);
    }

    private static byte[] Camera() => Crw(
        (0x2008, [0xFF, 0xD8, 0xFF, 0xD9]),
        (0x2007, [0xFF, 0xD8, 1, 2, 0xFF, 0xD9]),
        (0x2807, Heap(
            (0x080A, "Canon\0Canon PowerShot S40\0"u8.ToArray()),
            (0x0810, "Owner\0padding"u8.ToArray()))),
        (0x300A, Heap(
            (0x0805, "A comment\0\0\0"u8.ToArray()),
            (0x180E, [.. Le32(1000000000), .. Le32(0), .. Le32(0)]),
            (0x1810, [.. Le32(2272), .. Le32(1704), .. Le32(0), .. Le32(270), .. Le32(8), .. Le32(3), .. Le32(0)]),
            (0x5817, Le32(1171771)),
            (0x3002, Heap((0x5807, Le32(0x40000000)))),
            (0x3004, Heap((0x080B, "Firmware Version 1.10\0"u8.ToArray()))),
            (0x300B, Heap(
                (0x102A, ShotInfo()),
                (0x102D, Shorts16(8, 2, 0, 5)),
                (0x50B4, Le16(1)))))));

    private static string Ascii(ExifMetadata exif, ExifDirectoryKind directory, ushort tag)
    {
        Assert.True(exif.TryGetEntry(new ExifTagKey(directory, tag), out ExifEntry? entry));
        Assert.Equal(TiffFieldType.Ascii, entry.Value.FieldType);
        return Encoding.ASCII.GetString(entry.Value.RawBytes);
    }

    private static uint[] Numbers(ExifMetadata exif, ExifDirectoryKind directory, ushort tag, TiffFieldType type)
    {
        Assert.True(exif.TryGetEntry(new ExifTagKey(directory, tag), out ExifEntry? entry));
        Assert.Equal(type, entry.Value.FieldType);
        byte[] bytes = entry.Value.RawBytes.ToArray();
        int width = type == TiffFieldType.Short ? 2 : 4;
        return Enumerable.Range(0, bytes.Length / width).Select(i => width == 2
            ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i * 2))
            : BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * 4))).ToArray();
    }

    [Fact]
    public void TheRecordsOfACrwFileAreGivenAsTheTagsTheReferenceMapsThemTo()
    {
        byte[] file = Camera();

        CrwMetadata crw = CrwMetadata.Read(file);

        ExifMetadata exif = Assert.IsType<ExifMetadata>(crw.Exif);
        Assert.False(exif.IsEditable);
        Assert.Equal(Endianness.LittleEndian, exif.ByteOrder);
        Assert.Equal("Canon\0", Ascii(exif, ExifDirectoryKind.Ifd0, 0x010F));
        Assert.Equal("Canon PowerShot S40\0", Ascii(exif, ExifDirectoryKind.Ifd0, 0x0110));
        // 2001-09-09 01:46:40 UTC is second 1000000000.
        Assert.Equal("2001:09:09 01:46:40\0", Ascii(exif, ExifDirectoryKind.Exif, 0x9003));
        Assert.Equal(new uint[] { 2272u }, Numbers(exif, ExifDirectoryKind.Exif, 0xA002, TiffFieldType.Long));
        Assert.Equal(new uint[] { 1704u }, Numbers(exif, ExifDirectoryKind.Exif, 0xA003, TiffFieldType.Long));
        // A rotation of 270 degrees is orientation 8.
        Assert.Equal(new uint[] { 8u }, Numbers(exif, ExifDirectoryKind.Ifd0, 0x0112, TiffFieldType.Short));
        // A value that lies in its entry is the eight bytes of the entry, as the reference has it.
        Assert.Equal(new uint[] { 1u, 0u, 0u, 0u }, Numbers(exif, ExifDirectoryKind.Exif, 0xA001, TiffFieldType.Short));
        // The values the reference prints for the PowerShot S40 file of its corpus: aperture 99 and shutter speed 126.
        Assert.Equal(new uint[] { 730459u, 250000u }, Numbers(exif, ExifDirectoryKind.Exif, 0x829D, TiffFieldType.Rational));
        Assert.Equal(new uint[] { 1u, 15u }, Numbers(exif, ExifDirectoryKind.Exif, 0x829A, TiffFieldType.Rational));
        Assert.Equal(new uint[] { 6u }, Numbers(exif, ExifDirectoryKind.Thumbnail, 0x0103, TiffFieldType.Short));
        Assert.Equal(new uint[] { 0u }, Numbers(exif, ExifDirectoryKind.Thumbnail, 0x0201, TiffFieldType.Long));
        Assert.Equal(new uint[] { 4u }, Numbers(exif, ExifDirectoryKind.Thumbnail, 0x0202, TiffFieldType.Long));

        Assert.Equal("A comment", crw.Comment);
        Assert.Equal(4, crw.ThumbnailLength);
        Assert.Equal(6, crw.PreviewLength);
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }, file.AsSpan((int)crw.ThumbnailOffset!.Value, 4).ToArray());
        Assert.Equal(new byte[] { 0xFF, 0xD8, 1, 2, 0xFF, 0xD9 }, file.AsSpan((int)crw.PreviewOffset!.Value, 6).ToArray());
    }

    [Fact]
    public void TheCanonRecordsOfACrwFileAreACanonMakerNote()
    {
        MakerNote note = Assert.IsType<MakerNote>(CrwMetadata.Read(Camera()).Exif!.MakerNote);

        Assert.Equal(MakerNoteFormat.Canon, note.Format);
        Assert.Equal(new ushort[] { 0x0001, 0x0004, 0x0007, 0x0008, 0x0009 }, note.Entries.Select(entry => entry.TagId).ToArray());
        Assert.True(note.TryGetEntry(0x0009, out MakerNoteEntry? owner));
        // A text ends at its first zero byte, whatever fills the record after it.
        Assert.Equal("Owner\0", Encoding.ASCII.GetString(owner.Value.RawBytes));
        Assert.Equal("Exif.Canon.OwnerName", note.GetKey(owner));
        Assert.True(note.TryGetEntry(0x0008, out MakerNoteEntry? number));
        Assert.Equal(TiffFieldType.Long, number.Value.FieldType);
        Assert.Equal(1171771u, BinaryPrimitives.ReadUInt32LittleEndian(number.Value.RawBytes));
        Assert.True(note.TryGetEntry(0x0004, out MakerNoteEntry? shot));
        Assert.Equal(TiffFieldType.Short, shot.Value.FieldType);
        Assert.Equal(34u, shot.Value.Count);
        MakerNoteField aperture = Assert.Single(shot.Fields, field => field.Index == 21);
        Assert.Equal("Exif.CanonSi.ApertureValue", note.GetKey(shot, aperture));
        Assert.Equal(99, BinaryPrimitives.ReadUInt16LittleEndian(aperture.Value.RawBytes));
    }

    [Fact]
    public void ACrwFileWithoutMappedRecordsHasNoExif()
    {
        CrwMetadata crw = CrwMetadata.Read(Crw((0x1234, [1, 2])));

        Assert.Null(crw.Exif);
        Assert.Null(crw.Comment);
        Assert.Null(crw.PreviewOffset);
        Assert.Null(crw.ThumbnailOffset);
    }

    [Fact]
    public void ACrwFileWhoseStructureLeavesItsBlockIsRefused()
    {
        static MetadataErrorCode Refusal(byte[] file, MetadataLimits? limits = null) =>
            Assert.Throws<MetadataException>(() => CrwMetadata.Read(file, limits)).Code;

        byte[] good = Camera();
        Assert.Equal(MetadataErrorCode.InvalidCrw, Refusal(good.AsSpan(0, 12).ToArray()));
        Assert.Equal(MetadataErrorCode.InvalidCrw, Refusal([.. "II"u8, .. Le32(14), .. "HEAPJPGM"u8, .. Heap()]));

        // The header leads past the end of the file.
        byte[] header = (byte[])good.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(2), (uint)good.Length + 1);
        Assert.Equal(MetadataErrorCode.InvalidCrw, Refusal(header));

        // The entries are said to start past the end of the directory.
        byte[] table = (byte[])good.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(table.Length - 4), (uint)good.Length);
        Assert.Equal(MetadataErrorCode.InvalidCrw, Refusal(table));

        // More entries than the directory has room for.
        byte[] count = Crw((0x1234, [1, 2]));
        BinaryPrimitives.WriteUInt16LittleEndian(count.AsSpan(14 + 2), 2);
        Assert.Equal(MetadataErrorCode.InvalidCrw, Refusal(count));

        // A record longer than its directory.
        byte[] length = Crw((0x1234, [1, 2]));
        BinaryPrimitives.WriteUInt32LittleEndian(length.AsSpan(14 + 2 + 2 + 2), 5000);
        Assert.Equal(MetadataErrorCode.InvalidCrw, Refusal(length));

        // A directory that is its own record: its entry would be read without end.
        byte[] self = Crw((0x300A, new byte[4]));
        BinaryPrimitives.WriteUInt32LittleEndian(self.AsSpan(14 + 4 + 2 + 2), (uint)(self.Length - 14));
        BinaryPrimitives.WriteUInt32LittleEndian(self.AsSpan(14 + 4 + 2 + 6), 0);
        Assert.Equal(MetadataErrorCode.InvalidCrw, Refusal(self));

        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(good, new MetadataLimits { MaxInputBytes = good.Length - 1 }));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(good, new MetadataLimits { MaxTotalEntries = 5 }));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(good, new MetadataLimits { MaxDirectoryDepth = 1 }));
    }

    private static readonly byte[] Tiff = TiffBlock(true, Text(0x010F, "Minolta Co., Ltd."), Shorts(0x0112, 6));

    private static byte[] Mrw(byte[] tiff, bool withTtw = true)
    {
        byte[] prd = [0, (byte)'P', (byte)'R', (byte)'D', .. Be32(4), 1, 2, 3, 4];
        byte[] ttw = withTtw ? [0, (byte)'T', (byte)'T', (byte)'W', .. Be32((uint)tiff.Length), .. tiff] : [];
        return [0, (byte)'M', (byte)'R', (byte)'M', .. Be32((uint)(prd.Length + ttw.Length)), .. prd, .. ttw, 9, 9, 9, 9];
    }

    [Fact]
    public void TheTtwBlockOfAnMrwFileIsReadAsATiffFile()
    {
        byte[] file = Mrw(Tiff);

        MrwMetadata mrw = MrwMetadata.Read(file);

        Assert.Equal(8 + 12 + 8, mrw.TiffOffset);
        Assert.Equal(Tiff.Length, mrw.TiffLength);
        ExifMetadata exif = Assert.IsType<ExifMetadata>(mrw.Exif);
        Assert.Equal("Minolta Co., Ltd.\0", Ascii(exif, ExifDirectoryKind.Ifd0, 0x010F));
        Assert.Equal(new uint[] { 6u }, Numbers(exif, ExifDirectoryKind.Ifd0, 0x0112, TiffFieldType.Short));
        Assert.Null(mrw.Iptc);
        Assert.Null(mrw.Xmp);
    }

    [Fact]
    public void TheXmpPacketOfAnMrwFileIsTheOneOfItsPrimaryDirectory()
    {
        MrwMetadata mrw = MrwMetadata.Read(Mrw(TiffBlock(true, Raw(0x02BC, 1, Packet("minolta")))));

        Assert.NotNull(mrw.Xmp);
    }

    [Fact]
    public void AnMrwFileWithoutAUsableTtwBlockIsRefused()
    {
        static MetadataErrorCode Refusal(byte[] file) => Assert.Throws<MetadataException>(() => MrwMetadata.Read(file)).Code;

        Assert.Equal(MetadataErrorCode.InvalidMrw, Refusal([0, (byte)'M', (byte)'R', (byte)'M']));
        Assert.Equal(MetadataErrorCode.InvalidMrw, Refusal(Mrw(Tiff, withTtw: false)));
        // The TTW block is said to be longer than the file.
        byte[] file = Mrw(Tiff);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(8 + 12 + 4), (uint)file.Length);
        Assert.Equal(MetadataErrorCode.InvalidMrw, Refusal(file));
        // A block that is no TIFF structure is the TIFF reader's to refuse.
        Assert.Equal(MetadataErrorCode.InvalidTiff, Refusal(Mrw(new byte[16])));
    }

    private static byte[] Raf(byte[] jpeg, byte[] tiff)
    {
        byte[] header = new byte[108];
        "FUJIFILMCCD-RAW 0201FF389501X-T3"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(84), 108);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(88), (uint)jpeg.Length);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(100), (uint)(108 + jpeg.Length));
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(104), (uint)tiff.Length);
        return [.. header, .. jpeg, .. tiff];
    }

    private static byte[] RafJpeg() => JpegFixture.Build([
        .. JpegFixture.Segment(0xE1, JpegFixture.Exif()),
        .. JpegFixture.Segment(0xE1, [.. "http://ns.adobe.com/xap/1.0/\0"u8, .. Packet("fuji")]),
        .. JpegFixture.Segment(0xFE, "from the camera"u8.ToArray())]);

    [Fact]
    public void ARafFileGivesTheMetadataOfItsJpegFileAndTheTagsOnlyItsTiffStructureHas()
    {
        byte[] jpeg = RafJpeg();
        byte[] tiff = TiffBlock(true, Longs(0x00FE, 0), Shorts(0x0112, 3));

        RafMetadata raf = RafMetadata.Read(Raf(jpeg, tiff));

        Assert.Equal(108, raf.JpegOffset);
        Assert.Equal(jpeg.Length, raf.JpegLength);
        Assert.Equal(108 + jpeg.Length, raf.TiffOffset);
        Assert.Equal(tiff.Length, raf.TiffLength);
        ExifMetadata exif = Assert.IsType<ExifMetadata>(raf.Exif);
        Assert.False(exif.IsEditable);
        // The orientation is that of the JPEG file; the tag it does not have comes from the TIFF structure.
        Assert.Equal(new uint[] { 1u }, Numbers(exif, ExifDirectoryKind.Ifd0, 0x0112, TiffFieldType.Short));
        Assert.Equal(new uint[] { 0u }, Numbers(exif, ExifDirectoryKind.Ifd0, 0x00FE, TiffFieldType.Long));
        Assert.NotNull(raf.Xmp);
        Assert.Null(raf.Iptc);
        Assert.Equal("from the camera", Assert.Single(raf.Comments));
    }

    [Fact]
    public void ARafFileOfAnOlderModelHasNoTiffStructure()
    {
        RafMetadata raf = RafMetadata.Read(Raf(RafJpeg(), new byte[32]));

        Assert.Null(raf.TiffOffset);
        Assert.Equal(0, raf.TiffLength);
        Assert.Equal(new uint[] { 1u }, Numbers(raf.Exif!, ExifDirectoryKind.Ifd0, 0x0112, TiffFieldType.Short));
    }

    [Fact]
    public void ARafFileWhoseJpegFileIsNoJpegFileIsReadWithoutIt()
    {
        byte[] tiff = TiffBlock(true, Longs(0x00FE, 0));

        RafMetadata raf = RafMetadata.Read(Raf(new byte[12], tiff));

        Assert.Equal(12, raf.JpegLength);
        Assert.Equal(new uint[] { 0u }, Numbers(raf.Exif!, ExifDirectoryKind.Ifd0, 0x00FE, TiffFieldType.Long));
        Assert.Empty(raf.Comments);
        Assert.Empty(ImageMetadata.Read(Raf(new byte[12], tiff)).Errors);
    }

    [Fact]
    public void ARafFileWhoseHeaderLeavesTheFileIsRefused()
    {
        static MetadataErrorCode Refusal(byte[] file) => Assert.Throws<MetadataException>(() => RafMetadata.Read(file)).Code;

        byte[] good = Raf(RafJpeg(), new byte[32]);
        Assert.Equal(MetadataErrorCode.InvalidRaf, Refusal(good.AsSpan(0, 100).ToArray()));
        byte[] jpeg = (byte[])good.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(jpeg.AsSpan(88), (uint)good.Length);
        Assert.Equal(MetadataErrorCode.InvalidRaf, Refusal(jpeg));
        byte[] tiff = (byte[])good.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(100), 0xFFFFFFF0);
        Assert.Equal(MetadataErrorCode.InvalidRaf, Refusal(tiff));
    }

    [Fact]
    public void TheThreeContainersAreToldByTheirSignaturesAndReadAsAnyFileIs()
    {
        byte[] crw = Camera(), mrw = Mrw(Tiff), raf = Raf(RafJpeg(), new byte[32]);

        Assert.Equal(MetadataFormat.Crw, ImageMetadata.Detect(crw));
        Assert.Equal(MetadataFormat.Mrw, ImageMetadata.Detect(mrw));
        Assert.Equal(MetadataFormat.Raf, ImageMetadata.Detect(raf));
        // A TIFF file starts with the byte order too, and stays one.
        Assert.Equal(MetadataFormat.Tiff, ImageMetadata.Detect(Tiff));

        ImageMetadata canon = ImageMetadata.Read(crw);
        Assert.Equal(MetadataFormat.Crw, canon.Format);
        Assert.Empty(canon.Errors);
        Assert.NotNull(canon.Exif!.MakerNote);
        Assert.Equal("A comment", Assert.Single(canon.Comments));

        ImageMetadata minolta = ImageMetadata.Read(mrw);
        Assert.Equal(MetadataFormat.Mrw, minolta.Format);
        Assert.Empty(minolta.Errors);
        Assert.NotNull(minolta.Exif);

        ImageMetadata fuji = ImageMetadata.Read(raf);
        Assert.Equal(MetadataFormat.Raf, fuji.Format);
        Assert.Empty(fuji.Errors);
        Assert.NotNull(fuji.Exif);
        Assert.NotNull(fuji.Xmp);
        Assert.Equal("from the camera", Assert.Single(fuji.Comments));
    }

    [Fact]
    public void AContainerItsReaderRefusesIsReportedAndNotThrown()
    {
        ImageMetadata canon = ImageMetadata.Read([.. "II"u8, .. Le32(900), .. "HEAPCCDR"u8, .. Heap()]);
        Assert.Null(canon.Exif);
        Assert.Equal(MetadataErrorCode.InvalidCrw, Assert.Single(canon.Errors).Exception.Code);

        ImageMetadata minolta = ImageMetadata.Read(Mrw(Tiff, withTtw: false));
        Assert.Equal(3, minolta.Errors.Count);
        Assert.All(minolta.Errors, error => Assert.Equal(MetadataErrorCode.InvalidMrw, error.Exception.Code));
    }
}
