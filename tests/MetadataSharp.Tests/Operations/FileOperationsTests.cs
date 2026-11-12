using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Formats.Operations;
using MetadataSharp.Formats.Png;
using MetadataSharp.Formats.WebP;
using MetadataSharp.Tests.Iptc;
using MetadataSharp.Tests.Jpeg;
using MetadataSharp.Tests.Png;
using MetadataSharp.Tests.WebP;
using Xunit;

namespace MetadataSharp.Tests.Operations;

/// <summary>The thumbnail of an EXIF block, and the delete, extract, insert and fix operations on files.</summary>
public sealed class FileOperationsTests
{
    private static readonly ExifTagKey Orientation = new(ExifDirectoryKind.Ifd0, 0x0112), UserComment = new(ExifDirectoryKind.Exif, 0x9286);
    private static readonly byte[] XmpHeader = "http://ns.adobe.com/xap/1.0/\0"u8.ToArray();

    private static byte[] BigEndianTiff => [0x4D, 0x4D, 0, 42, 0, 0, 0, 8, 0, 1, 0x01, 0x12, 0, 3, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0];

    /// <summary>A stream that starts and ends as a JPEG does; the filler tells one thumbnail from another.</summary>
    private static byte[] Picture(byte filler, int length = 12)
    {
        byte[] bytes = new byte[length];
        Array.Fill(bytes, filler);
        // The start of the image, one comment segment that holds the filler, the end of the image.
        bytes[0] = 0xFF; bytes[1] = 0xD8; bytes[2] = 0xFF; bytes[3] = 0xFE;
        bytes[4] = (byte)((length - 6) >> 8); bytes[5] = (byte)(length - 6);
        bytes[^2] = 0xFF; bytes[^1] = 0xD9;
        return bytes;
    }

    private static byte[] Packet(string headline)
    {
        var edit = new XmpEdit();
        edit.Set("Xmp.photoshop.Headline", headline);
        XmpPacket packet = XmpPacket.CreateEmpty().Apply(edit)!;
        byte[] bytes = new byte[packet.ByteLength];
        packet.CopyRawTo(bytes);
        return bytes;
    }

    private static byte[] IptcStream(string caption)
    {
        var edit = new IptcEdit();
        edit.SetCaption(caption);
        return IptcCodecTests.Raw(IptcPacket.CreateEmpty().Apply(edit)!);
    }

    private static byte[] FullJpeg(byte[]? tiff = null) => JpegFixture.Build([
        .. JpegFixture.Segment(0xE1, JpegFixture.Exif(tiff)),
        .. JpegFixture.Segment(0xED, IptcCodecTests.Photoshop(IptcCodecTests.Block(0x0404, IptcStream("caption")))),
        .. JpegFixture.Segment(0xE1, [.. XmpHeader, .. Packet("headline")]),
        .. JpegFixture.Segment(0xFE, "a comment"u8.ToArray())]);

    private static byte[] SaveJpeg(byte[] file, Action<ExifEdit> change)
    {
        var edit = new ExifEdit();
        change(edit);
        using JpegMetadataDocument document = JpegMetadataDocument.Open(new MemoryStream(file));
        var output = new MemoryStream();
        document.Save(output, edit);
        return output.ToArray();
    }

    private static ushort OrientationOf(byte[] file)
    {
        ExifMetadata? exif = ImageMetadata.Read(file).Exif;
        Assert.NotNull(exif);
        Assert.True(exif!.TryGetEntry(Orientation, out ExifEntry? entry));
        Assert.True(entry!.Value.TryGetUInt32(out uint value));
        return (ushort)value;
    }

    private static bool Holds(byte[] file, byte[] part) => file.AsSpan().IndexOf(part) >= 0;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AThumbnailIsCreatedReplacedAndRemovedInAJpegFile(bool bigEndian)
    {
        byte[] file = JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif(bigEndian ? BigEndianTiff : null)));
        Assert.Null(MetadataOperations.ExtractThumbnail(file));

        byte[] first = SaveJpeg(file, edit => edit.SetThumbnail(Picture(0x11)));
        Assert.Equal(Picture(0x11), MetadataOperations.ExtractThumbnail(first));
        Assert.Equal(1, OrientationOf(first));

        byte[] second = SaveJpeg(first, edit => edit.SetThumbnail(Picture(0x22, 20)));
        Assert.Equal(Picture(0x22, 20), MetadataOperations.ExtractThumbnail(second));
        Assert.False(Holds(second, Picture(0x11)));

        byte[] third = SaveJpeg(second, edit => edit.RemoveThumbnail());
        Assert.Null(MetadataOperations.ExtractThumbnail(third));
        Assert.False(Holds(third, Picture(0x22, 20)));
        Assert.Equal(1, OrientationOf(third));
        Assert.Empty(ImageMetadata.Read(third).Errors);
    }

    [Fact]
    public void AReplacedThumbnailKeepsTheOtherTagsOfItsDirectory()
    {
        // IFD0 with an orientation; IFD1 at 26 with Compression, Orientation 6, and the stream at 80.
        byte[] stream = Picture(0x31);
        byte[] tiff = [
            0x49, 0x49, 42, 0, 8, 0, 0, 0,
            1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, 1, 0, 0, 0, 26, 0, 0, 0,
            4, 0,
            0x03, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0,
            0x12, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0,
            0x01, 2, 4, 0, 1, 0, 0, 0, 80, 0, 0, 0,
            0x02, 2, 4, 0, 1, 0, 0, 0, (byte)stream.Length, 0, 0, 0,
            0, 0, 0, 0,
            .. stream];
        Assert.Equal(80, tiff.Length - stream.Length);
        byte[] file = JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif(tiff)));
        Assert.Equal(stream, MetadataOperations.ExtractThumbnail(file));

        byte[] saved = SaveJpeg(file, edit => edit.SetThumbnail(Picture(0x32, 16)));
        Assert.Equal(Picture(0x32, 16), MetadataOperations.ExtractThumbnail(saved));
        Assert.False(Holds(saved, stream));
        ExifMetadata exif = ImageMetadata.Read(saved).Exif!;
        Assert.True(exif.TryGetEntry(new ExifTagKey(ExifDirectoryKind.Thumbnail, 0x0112), out ExifEntry? kept));
        Assert.True(kept!.Value.TryGetUInt32(out uint orientation));
        Assert.Equal(6u, orientation);
        // No resolution is added to a directory that is replaced.
        Assert.False(exif.TryGetEntry(new ExifTagKey(ExifDirectoryKind.Thumbnail, 0x011A), out _));
    }

    [Fact]
    public void AnInsertedPacketArrivesWithItsStructures()
    {
        var edit = new XmpEdit();
        edit.Set("Xmp.photoshop.Headline", "structured");
        edit.AppendStruct("Xmp.xmpMM.History", XmpValueKind.Sequence);
        edit.Set("Xmp.xmpMM.History[1]/stEvt:action", "saved");
        XmpPacket packet = XmpPacket.CreateEmpty().Apply(edit)!;
        byte[] sidecar = new byte[packet.ByteLength];
        packet.CopyRawTo(sidecar);

        byte[] target = MetadataOperations.InsertXmpSidecar(FullJpeg(), sidecar, convert: false);
        XmpPacket arrived = ImageMetadata.Read(target).Xmp!;
        Assert.Contains(arrived.Entries, entry => entry.Key == "Xmp.xmpMM.History[1]/stEvt:action" && entry.Values.Contains("saved"));
        Assert.Contains(arrived.Entries, entry => entry.Key == "Xmp.photoshop.Headline" && entry.Values.Contains("structured"));
        Assert.DoesNotContain(arrived.Entries, entry => entry.Values.Contains("headline"));
    }

    [Fact]
    public void AThumbnailAndATagAreSetInOneEdit()
    {
        byte[] file = JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif()));
        byte[] saved = SaveJpeg(file, edit =>
        {
            edit.Set(Orientation, (ushort)6);
            edit.SetThumbnail(Picture(0x33));
        });
        Assert.Equal(Picture(0x33), MetadataOperations.ExtractThumbnail(saved));
        Assert.Equal(6, OrientationOf(saved));
    }

    [Fact]
    public void AFileWithoutAnExifBlockGetsOneForItsThumbnail()
    {
        byte[] saved = SaveJpeg(JpegFixture.Build(), edit => edit.SetThumbnail(Picture(0x44)));
        Assert.Equal(Picture(0x44), MetadataOperations.ExtractThumbnail(saved));
        Assert.Empty(ImageMetadata.Read(saved).Errors);

        byte[] withTag = SaveJpeg(JpegFixture.Build(), edit =>
        {
            edit.Set(Orientation, (ushort)3);
            edit.SetThumbnail(Picture(0x45));
        });
        Assert.Equal(Picture(0x45), MetadataOperations.ExtractThumbnail(withTag));
        Assert.Equal(3, OrientationOf(withTag));
    }

    [Fact]
    public void RemovingTheThumbnailOfAFileWithoutOneChangesNothing()
    {
        byte[] file = JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif()));
        Assert.Equal(file, SaveJpeg(file, edit => edit.RemoveThumbnail()));
        byte[] bare = JpegFixture.Build();
        Assert.Equal(bare, SaveJpeg(bare, edit => edit.RemoveThumbnail()));
    }

    [Fact]
    public void AThumbnailIsWrittenToPngAndWebPFiles()
    {
        var edit = new ExifEdit();
        edit.SetThumbnail(Picture(0x55));

        byte[] png = PngFixture.Build([.. PngFixture.Chunk("eXIf", JpegFixture.OrientationTiff)]);
        var output = new MemoryStream();
        using (PngMetadataDocument document = PngMetadataDocument.Open(new MemoryStream(png))) document.SaveExif(output, edit);
        Assert.Equal(Picture(0x55), MetadataOperations.ExtractThumbnail(output.ToArray()));
        Assert.Equal(1, OrientationOf(output.ToArray()));

        byte[] webp = WebPFixture.Extended(0x08, WebPFixture.Image(0)[12..], WebPFixture.Chunk("EXIF", JpegFixture.OrientationTiff));
        output = new MemoryStream();
        using (WebPMetadataDocument document = WebPMetadataDocument.Open(new MemoryStream(webp))) document.SaveExif(output, edit);
        Assert.Equal(Picture(0x55), MetadataOperations.ExtractThumbnail(output.ToArray()));
    }

    [Fact]
    public void AThumbnailThatIsNoJpegStreamOrTooLongIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new ExifEdit().SetThumbnail([1, 2, 3, 4]));
        byte[] file = JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif()));
        // It starts as a JPEG stream and does not end as one: the reader would not return it.
        MetadataException open = Assert.Throws<MetadataException>(() => SaveJpeg(file, edit => edit.SetThumbnail([0xFF, 0xD8, 0, 0, 0, 0])));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, open.Code);
        MetadataException budget = Assert.Throws<MetadataException>(() => SaveJpeg(file, edit => edit.SetThumbnail(Picture(0x66, 61_442))));
        Assert.Equal(MetadataErrorCode.LimitExceeded, budget.Code);
        // A block that is long already: with the thumbnail it passes the 64 KiB of one segment.
        byte[] described = SaveJpeg(file, edit => edit.Set(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x010E), new string('d', 10_000)));
        MetadataException segment = Assert.Throws<MetadataException>(() => SaveJpeg(described, edit => edit.SetThumbnail(Picture(0x66, 60_000))));
        Assert.Equal(MetadataErrorCode.ExifTooLarge, segment.Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(999)]
    [InlineData(1_001)]
    [InlineData(10_000)]
    [InlineData(60_000)]
    public void ALongTextIsSetAsItIs(int length)
    {
        // Lengths on both sides of the point where the runtime replaces a running loop with its
        // optimised form: on .NET 8 a text of 10,000 characters was once refused as a value without bytes.
        var key = new ExifTagKey(ExifDirectoryKind.Ifd0, 0x010E);
        byte[] file = JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif()));
        byte[] saved = SaveJpeg(file, edit => edit.Set(key, new string('d', length)));
        Assert.True(ImageMetadata.Read(saved).Exif!.TryGetEntry(key, out ExifEntry? entry));
        Assert.Equal((uint)length + 1, entry!.Value.Count);
    }

    [Fact]
    public void DeleteRemovesTheKindsNamedAndKeepsTheOthers()
    {
        byte[] file = FullJpeg();
        ImageMetadata all = ImageMetadata.Read(MetadataOperations.Delete(file));
        Assert.Null(all.Exif);
        Assert.Null(all.Iptc);
        Assert.Null(all.Xmp);
        Assert.Empty(all.Comments);

        ImageMetadata some = ImageMetadata.Read(MetadataOperations.Delete(file, MetadataFamilies.Xmp | MetadataFamilies.Comment));
        Assert.NotNull(some.Exif);
        Assert.NotNull(some.Iptc);
        Assert.Null(some.Xmp);
        Assert.Empty(some.Comments);

        byte[] bare = JpegFixture.Build();
        Assert.Equal(bare, MetadataOperations.Delete(bare));

        byte[] thumbnail = SaveJpeg(file, edit => edit.SetThumbnail(Picture(0x77)));
        byte[] without = MetadataOperations.Delete(thumbnail, MetadataFamilies.Thumbnail);
        Assert.Null(MetadataOperations.ExtractThumbnail(without));
        Assert.Equal(1, OrientationOf(without));
        Assert.NotNull(ImageMetadata.Read(without).Xmp);

        var source = new MemoryStream(file);
        var destination = new MemoryStream();
        MetadataOperations.Delete(source, destination, MetadataFamilies.Exif);
        Assert.Null(ImageMetadata.Read(destination.ToArray()).Exif);
    }

    [Fact]
    public void DeleteRefusesAContainerThatIsNotWritten()
    {
        byte[] exv = MetadataOperations.ExtractExv(FullJpeg());
        MetadataException refusal = Assert.Throws<MetadataException>(() => MetadataOperations.Delete(exv));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, refusal.Code);
    }

    [Fact]
    public void TheMetadataOfAJpegFileTravelsThroughAnExvFile()
    {
        byte[] source = SaveJpeg(FullJpeg(), edit => edit.SetThumbnail(Picture(0x12)));
        byte[] exv = MetadataOperations.ExtractExv(source);
        ImageMetadata held = ImageMetadata.Read(exv);
        Assert.Equal(MetadataFormat.Exv, held.Format);
        Assert.NotNull(held.Exif);
        Assert.NotNull(held.Iptc);
        Assert.NotNull(held.Xmp);

        ImageMetadata narrow = ImageMetadata.Read(MetadataOperations.ExtractExv(source, MetadataFamilies.Iptc));
        Assert.Null(narrow.Exif);
        Assert.NotNull(narrow.Iptc);
        Assert.Null(narrow.Xmp);

        byte[] target = MetadataOperations.InsertExv(JpegFixture.Build(), exv, MetadataFamilies.All, out IReadOnlyList<string> notes);
        Assert.Empty(notes);
        ImageMetadata arrived = ImageMetadata.Read(target);
        Assert.Empty(arrived.Errors);
        Assert.Equal(1, OrientationOf(target));
        Assert.Equal("caption", arrived.Iptc!.GetText(arrived.Iptc.Datasets.ToList().FindIndex(dataset => dataset.Key == new IptcDatasetKey(2, 120))));
        Assert.Contains(arrived.Xmp!.Entries, entry => entry.Key == "Xmp.photoshop.Headline" && entry.Values.Contains("headline"));
        Assert.Equal(new[] { "a comment" }, arrived.Comments);
        Assert.Equal(Picture(0x12), MetadataOperations.ExtractThumbnail(target));
    }

    [Fact]
    public void InsertReplacesTheKindWholeAndLeavesTheOthers()
    {
        byte[] exv = ExvFile.Create(null, null, XmpPacket.Parse(Packet("new")));
        byte[] target = MetadataOperations.InsertExv(FullJpeg(), exv, MetadataFamilies.All, out _);
        ImageMetadata arrived = ImageMetadata.Read(target);
        Assert.Contains(arrived.Xmp!.Entries, entry => entry.Key == "Xmp.photoshop.Headline" && entry.Values.Contains("new"));
        Assert.DoesNotContain(arrived.Xmp.Entries, entry => entry.Values.Contains("headline"));
        Assert.NotNull(arrived.Iptc);
        Assert.Equal(1, OrientationOf(target));
    }

    [Fact]
    public void AnExvFileIsBuiltFromTheMetadataOfAnotherContainer()
    {
        byte[] png = PngFixture.Build([.. PngFixture.Chunk("eXIf", BigEndianTiff), .. PngFixture.Xmp(Packet("from png"))]);
        byte[] exv = MetadataOperations.ExtractExv(png);
        ImageMetadata held = ImageMetadata.Read(exv);
        Assert.Equal(MetadataFormat.Exv, held.Format);
        Assert.Empty(held.Errors);
        Assert.True(held.Exif!.TryGetEntry(Orientation, out _));
        Assert.Contains(held.Xmp!.Entries, entry => entry.Values.Contains("from png"));

        byte[] made = ExvFile.Create(null, IptcPacket.Parse(IptcStream("made")), null, "said", Picture(0x21));
        ImageMetadata read = ImageMetadata.Read(made);
        Assert.NotNull(read.Iptc);
        Assert.Equal(new[] { "said" }, read.Comments);
        Assert.Equal(Picture(0x21), MetadataOperations.ExtractThumbnail(made));
    }

    [Fact]
    public void ASidecarIsExtractedAndInserted()
    {
        Assert.Null(MetadataOperations.ExtractXmpSidecar(JpegFixture.Build()));
        byte[] plain = MetadataOperations.ExtractXmpSidecar(FullJpeg(), convert: false)!;
        Assert.Equal(Packet("headline"), plain);

        byte[] converted = MetadataOperations.ExtractXmpSidecar(FullJpeg())!;
        XmpPacket packet = XmpPacket.Parse(converted);
        Assert.Contains(packet.Entries, entry => entry.Key == "Xmp.tiff.Orientation");
        Assert.Contains(packet.Entries, entry => entry.Key == "Xmp.photoshop.Headline");

        byte[] target = MetadataOperations.InsertXmpSidecar(JpegFixture.Build(), Packet("inserted"), convert: false);
        ImageMetadata arrived = ImageMetadata.Read(target);
        Assert.Contains(arrived.Xmp!.Entries, entry => entry.Values.Contains("inserted"));
        Assert.Null(arrived.Exif);

        byte[] both = MetadataOperations.InsertXmpSidecar(JpegFixture.Build(), converted);
        Assert.Equal(1, OrientationOf(both));
    }

    [Fact]
    public void AUserCommentInAnotherEncodingIsRewrittenAsUcs2()
    {
        byte[] latin = [.. "UNICODE\0"u8, 0xE9, 0x21];
        byte[] file = SaveJpeg(JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif())),
            edit => edit.SetUnchecked(UserComment, ExifValue.FromRaw(TiffFieldType.Undefined, (uint)latin.Length, Endianness.LittleEndian, latin)));
        ExifMetadata exif = ImageMetadata.Read(file).Exif!;
        ExifEdit? fix = MetadataOperations.FixUserComment(exif, Encoding.Latin1);
        Assert.NotNull(fix);
        using JpegMetadataDocument document = JpegMetadataDocument.Open(new MemoryStream(file));
        var output = new MemoryStream();
        document.Save(output, fix!);
        ExifMetadata after = ImageMetadata.Read(output.ToArray()).Exif!;
        Assert.True(after.TryGetEntry(UserComment, out ExifEntry? entry));
        Assert.Equal(new byte[] { (byte)'U', (byte)'N', (byte)'I', (byte)'C', (byte)'O', (byte)'D', (byte)'E', 0, 0xE9, 0, 0x21, 0 }, entry!.Value.RawBytes.ToArray());

        // Already UCS-2 when read as UCS-2: nothing to do; and no comment, nothing to do.
        Assert.Null(MetadataOperations.FixUserComment(after, Encoding.Unicode));
        Assert.Null(MetadataOperations.FixUserComment(ImageMetadata.Read(FullJpeg()).Exif!, Encoding.Latin1));
    }

    [Fact]
    public void FixIsoHasNothingToDoWithoutAMakerNoteOrWithTheTag()
    {
        Assert.Null(MetadataOperations.FixIso(ImageMetadata.Read(FullJpeg()).Exif!));
        byte[] file = SaveJpeg(FullJpeg(), edit => edit.Set(new ExifTagKey(ExifDirectoryKind.Exif, 0x8827), (ushort)200));
        Assert.Null(MetadataOperations.FixIso(ImageMetadata.Read(file).Exif!));
    }
}
