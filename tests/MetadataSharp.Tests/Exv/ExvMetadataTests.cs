using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Formats.Exv;
using Xunit;

namespace MetadataSharp.Tests.Exv;

public sealed class ExvMetadataTests
{
    private static readonly byte[] Signature = [0xFF, 0x01, (byte)'E', (byte)'x', (byte)'i', (byte)'v', (byte)'2'];

    // A little-endian TIFF with one IFD0 entry: Orientation = 6.
    private static readonly byte[] Orientation =
        [0x49, 0x49, 42, 0, 8, 0, 0, 0, 1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0];

    private static byte[] Segment(byte marker, byte[] payload)
    {
        int length = payload.Length + 2;
        return [0xFF, marker, (byte)(length >> 8), (byte)length, .. payload];
    }

    private static byte[] Exif() => Segment(0xE1, [.. "Exif\0\0"u8, .. Orientation]);

    private static byte[] Xmp() => Segment(0xE1, [.. "http://ns.adobe.com/xap/1.0/\0"u8, .. Encoding.UTF8.GetBytes(
        "<?xpacket begin='﻿' id='W5M0MpCehiHzreSzNTczkc9d'?><x:xmpmeta xmlns:x='adobe:ns:meta/'>" +
        "<rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#' xmlns:p='urn:owned:test'>" +
        "<rdf:Description rdf:about=''><p:title>x</p:title></rdf:Description></rdf:RDF></x:xmpmeta><?xpacket end='w'?>")]);

    private static byte[] Eoi => [0xFF, 0xD9];

    [Fact]
    public void ExifAndXmpAreReadAndOtherSegmentsAreSkipped()
    {
        byte[] file = [.. Signature, .. Segment(0xFE, "a comment"u8.ToArray()), .. Exif(), .. Xmp(), .. Eoi];
        ExvMetadata metadata = ExvMetadata.Read(file);
        Assert.NotNull(metadata.Exif);
        ExifEntry entry = Assert.Single(metadata.Exif.Entries);
        Assert.Equal(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0112), entry.Key);
        Assert.NotNull(metadata.Xmp);
        Assert.Null(metadata.Iptc);
    }

    [Fact]
    public void ExifWithAFieldOfUnknownTypeIsReadAsNotEditable()
    {
        // Orientation followed by an entry of field type 14, which TIFF 6.0 does not define.
        byte[] tiff = [0x49, 0x49, 42, 0, 8, 0, 0, 0, 2, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0,
            0x28, 1, 14, 0, 1, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0];
        ExvMetadata metadata = ExvMetadata.Read([.. Signature, .. Segment(0xE1, [.. "Exif\0\0"u8, .. tiff]), .. Eoi]);
        Assert.NotNull(metadata.Exif);
        Assert.False(metadata.Exif.IsEditable);
        Assert.Equal(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0112), Assert.Single(metadata.Exif.Entries).Key);
        Assert.True(ExvMetadata.Read([.. Signature, .. Exif(), .. Eoi]).Exif!.IsEditable);
    }

    [Fact]
    public void AFileWithoutEoiEndsAtItsLastSegment()
    {
        ExvMetadata metadata = ExvMetadata.Read([.. Signature, .. Exif()]);
        Assert.NotNull(metadata.Exif);
    }

    [Fact]
    public void BytesAfterEoiAreIgnored()
    {
        ExvMetadata metadata = ExvMetadata.Read([.. Signature, .. Exif(), .. Eoi, 1, 2, 3]);
        Assert.NotNull(metadata.Exif);
    }

    [Fact]
    public void AnEmptyExvHasNoMetadata()
    {
        ExvMetadata metadata = ExvMetadata.Read(Signature);
        Assert.Null(metadata.Exif);
        Assert.Null(metadata.Xmp);
    }

    public static TheoryData<byte[]> Malformed => new()
    {
        { [0xFF, 0xD8, 0xFF, 0xD9] },                                  // A JPEG, not an EXV.
        { [.. Signature, 0x00, 0x01] },                                // Not a marker.
        { [.. Signature, 0xFF, 0xE1, 0x00] },                          // A truncated segment header.
        { [.. Signature, 0xFF, 0xE1, 0x00, 0x40, 0x01] },              // A length past the end.
        { [.. Signature, 0xFF, 0xE1, 0x00, 0x01] },                    // A length below two.
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void MalformedFilesAreRefusedAsExv(byte[] file)
    {
        var failure = Assert.Throws<MetadataException>(() => ExvMetadata.Read(file));
        Assert.Equal(MetadataErrorCode.InvalidExv, failure.Code);
    }

    [Fact]
    public void TheSegmentBudgetIsEnforced()
    {
        var file = new List<byte>(Signature);
        for (int i = 0; i < 20; i++) file.AddRange(Segment(0xFE, [1]));
        var failure = Assert.Throws<MetadataException>(() => ExvMetadata.Read(file.ToArray(), new MetadataLimits { MaxSegments = 10 }));
        Assert.Equal(MetadataErrorCode.LimitExceeded, failure.Code);
    }
}
