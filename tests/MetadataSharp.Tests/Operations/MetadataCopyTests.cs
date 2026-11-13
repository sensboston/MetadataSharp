using MetadataSharp.Core;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Operations;
using MetadataSharp.Tests.Bmff;
using MetadataSharp.Tests.Dng;
using MetadataSharp.Tests.Jpeg;
using MetadataSharp.Tests.Png;
using MetadataSharp.Tests.Tiff;
using MetadataSharp.Tests.WebP;
using Xunit;

namespace MetadataSharp.Tests.Operations;

public sealed class MetadataCopyTests
{
    private static byte[] SourceJpeg()
    {
        byte[] tiff = JpegFixture.OrientationTiff.ToArray();
        tiff[18] = 6;
        return JpegFixture.Build(prefix: JpegFixture.Segment(0xE1, JpegFixture.Exif(tiff)));
    }

    [Fact]
    public void JpegExifCopiesToJpegPngWebpTiffAndHeicWithoutChangingImageData()
    {
        byte[] source = SourceJpeg();
        byte[][] destinations =
        [
            JpegFixture.Build(),
            PngFixture.Build(),
            WebPFixture.Image(0),
            StandaloneTiffFixture.Create().Bytes,
            DngFixture.RawOnly().Bytes,
            BmffFixture.Create("heic-bare", false).Bytes
        ];

        foreach (byte[] destination in destinations)
        {
            MetadataCopyResult result = MetadataOperations.CopyMetadata(source, destination,
                new MetadataCopyOptions { Families = MetadataCopyFamilies.Exif, MakerNote = MetadataMakerNoteMode.Skip });
            Assert.False(result.WasRefused, string.Join(Environment.NewLine, result.Notes.Select(note => note.Message)));
            Assert.True(result.CopiedFamilies.HasFlag(MetadataCopyFamilies.Exif));
            ImageMetadata arrived = ImageMetadata.Read(result.DestinationBytes);
            Assert.Empty(arrived.Errors);
            Assert.True(arrived.Exif!.TryGetEntry(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0112), out ExifEntry? orientation));
            ushort value = orientation!.Value.ByteOrder == Endianness.LittleEndian
                ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(orientation.Value.RawBytes)
                : System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(orientation.Value.RawBytes);
            Assert.Equal((ushort)6, value);

            if (destination.Length >= 2 && destination[0] == 0xFF && destination[1] == 0xD8)
                Assert.Equal(ScanData(destination), ScanData(result.DestinationBytes));
            else if (destination.Length >= 8 && destination.AsSpan(0, 8).SequenceEqual([137, 80, 78, 71, 13, 10, 26, 10]))
                Assert.Contains(ChunkBytes(result.DestinationBytes, "IDAT"), chunk =>
                    chunk.SequenceEqual(PngFixture.Chunk("IDAT", PngFixture.Compress([0, 10, 20, 30]))));
            else if (destination.AsSpan().StartsWith("RIFF"u8))
                Assert.True(result.DestinationBytes.AsSpan().IndexOf(WebPFixture.Image(0).AsSpan(12)) >= 0);
            else if (destination.Length >= 2304 && destination[0] == 0x49 && destination[1] == 0x49 &&
                     destination[2] == 42 && destination[3] == 0)
                Assert.Equal(destination.AsSpan(2048, 256).ToArray(), result.DestinationBytes.AsSpan(2048, 256).ToArray());
        }
    }

    [Fact]
    public void DngMetadataCopiesToJpeg()
    {
        byte[] source = DngFixture.RawOnly().Bytes;
        byte[] destination = JpegFixture.Build();
        MetadataCopyResult result = MetadataOperations.CopyMetadata(source, destination,
            new MetadataCopyOptions { Families = MetadataCopyFamilies.Exif, MakerNote = MetadataMakerNoteMode.Skip });
        Assert.False(result.WasRefused, string.Join(Environment.NewLine, result.Notes.Select(note => note.Message)));
        ImageMetadata before = ImageMetadata.Read(source), after = ImageMetadata.Read(result.DestinationBytes);
        Assert.True(before.Exif!.TryGetEntry(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x013B), out ExifEntry? artist));
        Assert.True(after.Exif!.TryGetEntry(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x013B), out ExifEntry? copied));
        Assert.Equal(artist!.Value.RawBytes.ToArray(), copied!.Value.RawBytes.ToArray());
    }

    [Fact]
    public void UndecodedOpaqueMakerNoteIsSkippedWithAStableReason()
    {
        byte[] tiff =
        [
            0x49, 0x49, 42, 0, 8, 0, 0, 0,
            1, 0, 0x69, 0x87, 4, 0, 1, 0, 0, 0, 26, 0, 0, 0, 0, 0, 0, 0,
            1, 0, 0x7C, 0x92, 7, 0, 8, 0, 0, 0, 44, 0, 0, 0, 0, 0, 0, 0,
            (byte)'N', (byte)'O', (byte)'P', (byte)'E', 0, 1, 2, 3
        ];
        byte[] source = JpegFixture.Build(prefix: JpegFixture.Segment(0xE1, JpegFixture.Exif(tiff)));
        MetadataCopyResult result = MetadataOperations.CopyMetadata(source, JpegFixture.Build(),
            new MetadataCopyOptions { Families = MetadataCopyFamilies.Exif, MakerNote = MetadataMakerNoteMode.Copy });
        Assert.False(result.WasRefused);
        Assert.Contains(result.Notes, note => note.Reason == MetadataCopyReason.MakerNoteNotDecoded);
        Assert.DoesNotContain(ImageMetadata.Read(result.DestinationBytes).Exif!.Entries,
            entry => entry.Key == new ExifTagKey(ExifDirectoryKind.Exif, 0x927C));
    }

    [Fact]
    public void MalformedRequestedSourceAndUnknownDestinationAreTransactionalRefusals()
    {
        byte[] destination = JpegFixture.Build();
        byte[] malformed = JpegFixture.Build(prefix: JpegFixture.Segment(0xE1,
            [.. "Exif\0\0"u8, 0x49, 0x49, 0x2A, 0, 0, 0, 0, 0]));
        MetadataCopyResult sourceRefusal = MetadataOperations.CopyMetadata(malformed, destination,
            new MetadataCopyOptions { Families = MetadataCopyFamilies.Exif });
        Assert.True(sourceRefusal.WasRefused);
        Assert.Equal(destination, sourceRefusal.DestinationBytes);
        Assert.Contains(sourceRefusal.Notes, note => note.Reason == MetadataCopyReason.MalformedSourceFamily);

        MetadataCopyResult destinationRefusal = MetadataOperations.CopyMetadata(SourceJpeg(), [1, 2, 3],
            new MetadataCopyOptions { Families = MetadataCopyFamilies.Exif });
        Assert.True(destinationRefusal.WasRefused);
        Assert.Equal(new byte[] { 1, 2, 3 }, destinationRefusal.DestinationBytes);
    }

    private static byte[] ScanData(byte[] jpeg)
    {
        for (int i = 0; i <= jpeg.Length - 2; i++)
            if (jpeg[i] == 0xFF && jpeg[i + 1] == 0xDA) return jpeg.AsSpan(i).ToArray();
        throw new InvalidOperationException("The JPEG scan marker is missing.");
    }

    private static IEnumerable<byte[]> ChunkBytes(byte[] png, string type)
    {
        int at = 8;
        while (at <= png.Length - 12)
        {
            int length = checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at)));
            int total = checked(length + 12);
            if (total > png.Length - at) yield break;
            if (png.AsSpan(at + 4, 4).SequenceEqual(System.Text.Encoding.ASCII.GetBytes(type)))
                yield return png.AsSpan(at, total).ToArray();
            at += total;
        }
    }
}
