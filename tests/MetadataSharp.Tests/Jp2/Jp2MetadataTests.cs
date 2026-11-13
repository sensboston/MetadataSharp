using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Jp2;
using Xunit;

namespace MetadataSharp.Tests.Jp2;

public sealed class Jp2MetadataTests
{
    private static byte[] Fixture()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MetadataSharp.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllBytes(Path.Combine(dir!.FullName, "tests", "fixtures", "b8", "owned-rgb.jp2"));
    }
    private static MetadataErrorCode Error(byte[] data) => Assert.Throws<MetadataException>(() => Jp2Metadata.Read(data)).Code;

    private static byte[] FirstUuidBox(byte[] file, byte[] uuid)
    {
        int at = 0;
        while (at < file.Length)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at, 4)));
            if (BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at + 4, 4)) == 0x75756964 && length >= 24 &&
                file.AsSpan(at + 8, 16).SequenceEqual(uuid)) return file.AsSpan(at, length).ToArray();
            at += length;
        }
        throw new InvalidOperationException("No matching UUID box.");
    }

    private static int CountUuid(byte[] file, byte[] uuid)
    {
        int count = 0, at = 0;
        while (at < file.Length)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at, 4)));
            if (BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at + 4, 4)) == 0x75756964 && length >= 24 &&
                file.AsSpan(at + 8, 16).SequenceEqual(uuid)) count++;
            at += length;
        }
        return count;
    }

    private static byte[] Codestream(byte[] file)
    {
        int at = 0;
        while (at < file.Length)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at, 4)));
            if (BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at + 4, 4)) == 0x6a703263)
                return file.AsSpan(at + 8, length - 8).ToArray();
            at += length;
        }
        throw new InvalidOperationException("No codestream box.");
    }

    [Fact]
    public void CompleteSetWriterAddsMetadataAndIccBoxesAndKeepsCodestream()
    {
        byte[] source = Fixture();
        var image = MetadataSharp.Formats.ImageMetadata.Read(
            MetadataSharp.Tests.Jpeg.JpegFixture.Build(prefix: MetadataSharp.Tests.Jpeg.JpegFixture.Segment(0xE1,
                MetadataSharp.Tests.Jpeg.JpegFixture.Exif())));
        IptcPacket iptc = IptcPacket.Parse([0x1C, 0x02, 0x05, 0, 1, (byte)'X']);
        XmpPacket xmp = XmpPacket.Parse(Encoding.UTF8.GetBytes(
            "<?xpacket begin='﻿' id='W5M0MpCehiHzreSzNTczkc9d'?><x:xmpmeta xmlns:x='adobe:ns:meta/'>" +
            "<rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#' xmlns:p='urn:owned:test'>" +
            "<rdf:Description rdf:about=''><p:title>x</p:title></rdf:Description></rdf:RDF></x:xmpmeta><?xpacket end='w'?>"));
        byte[] profile = MetadataSharp.Tests.Containers.IccProfileTests.Profile(560, 11);
        byte[] exifUuid = "JpgTiffExif->JP2"u8.ToArray();
        byte[] iptcUuid = [0x33, 0xC7, 0xA4, 0xD2, 0xB8, 0x1D, 0x47, 0x23, 0xA0, 0xBA, 0xF1, 0xA3, 0xE0, 0x97, 0xAD, 0x38];
        byte[] xmpUuid = [0xBE, 0x7A, 0xCF, 0xCB, 0x97, 0xA9, 0x42, 0xE8, 0x9C, 0x71, 0x99, 0x94, 0x91, 0xE3, 0xAF, 0xAC];

        byte[] output = Jp2Metadata.Write(source, image.Exif, iptc, xmp, profile);

        Assert.Equal(profile, IccProfiles.Read(output));
        Assert.Equal(1, CountUuid(output, exifUuid));
        Assert.Equal(1, CountUuid(output, iptcUuid));
        Assert.Equal(1, CountUuid(output, xmpUuid));
        Assert.Equal(Codestream(source), Codestream(output));
        Assert.Equal(8u, Jp2Metadata.Read(output).Width);
        var readBack = MetadataSharp.Formats.ImageMetadata.Read(output);
        Assert.NotNull(readBack.Exif);
        Assert.Equal(image.Exif!.Entries.Select(entry => entry.Key), readBack.Exif!.Entries.Select(entry => entry.Key));
        Assert.NotNull(readBack.Iptc);
        Assert.Equal(iptc.Datasets.Select(dataset => dataset.Key), readBack.Iptc!.Datasets.Select(dataset => dataset.Key));
        Assert.NotNull(readBack.Xmp);
        Assert.Equal(xmp.Entries.Select(entry => entry.Key), readBack.Xmp!.Entries.Select(entry => entry.Key));

        byte[] removed = Jp2Metadata.Write(output);
        Assert.Null(IccProfiles.Read(removed));
        Assert.Equal(0, CountUuid(removed, exifUuid));
        Assert.Equal(0, CountUuid(removed, iptcUuid));
        Assert.Equal(0, CountUuid(removed, xmpUuid));
        Assert.Equal(Codestream(source), Codestream(removed));

        byte[] malformed = (byte[])profile.Clone(); malformed[36] = (byte)'x';
        Assert.Equal(MetadataErrorCode.InvalidIccProfile,
            Assert.Throws<MetadataException>(() => Jp2Metadata.Write(source, iccProfile: malformed)).Code);
        Assert.Throws<MetadataException>(() => Jp2Metadata.Write(source[..^1]));
        byte[] duplicateExif = [.. output, .. FirstUuidBox(output, exifUuid)];
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata,
            Assert.Throws<MetadataException>(() => Jp2Metadata.Write(duplicateExif)).Code);
    }

    [Fact]
    public void OwnedBoxedJp2YieldsDeclaredDimensions()
    {
        Jp2Metadata result = Jp2Metadata.Read(Fixture());
        Assert.Equal(8u, result.Width); Assert.Equal(8u, result.Height);
        Assert.Equal(0, result.XmlBoxCount);
        Assert.Throws<IndexOutOfRangeException>(() => result.GetXmlBox(0));
    }

    [Fact]
    public void RawXmlBoxesAreCopiedAndNotAutomaticallyCalledXmp()
    {
        byte[] data = Fixture();
        byte[] payload = "<owned>test</owned>"u8.ToArray();
        byte[] box = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        "xml "u8.CopyTo(box.AsSpan(4)); payload.CopyTo(box.AsSpan(8));
        Jp2Metadata result = Jp2Metadata.Read([.. data, .. box]);
        Assert.Equal(1, result.XmlBoxCount);
        byte[] copied = result.GetXmlBox(0);
        Assert.Equal("<owned>test</owned>", Encoding.UTF8.GetString(copied));
        copied[0] = 0;
        Assert.Equal((byte)'<', result.GetXmlBox(0)[0]);
    }

    [Fact]
    public void EveryTruncationIsRejected()
    {
        byte[] data = Fixture();
        for (int i = 0; i < data.Length; i++)
            Assert.Throws<MetadataException>(() => Jp2Metadata.Read(data.AsSpan(0, i)));
    }

    [Fact]
    public void BoxCountBudgetIsEnforced()
    {
        byte[] original = Fixture();
        byte[] data = new byte[original.Length + 1025 * 8];
        original.CopyTo(data, 0);
        for (int i = original.Length; i < data.Length; i += 8)
        {
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(i, 4), 8);
            "free"u8.CopyTo(data.AsSpan(i + 4, 4));
        }
        Assert.Equal(MetadataErrorCode.LimitExceeded, Error(data));
    }

    [Fact]
    public void BadLengthsOrderingAndUnsupportedBoxesAreRefused()
    {
        byte[] original = Fixture();
        byte[] x = (byte[])original.Clone(); x[0] = 0; x[1] = 0; x[2] = 0; x[3] = 1;
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Error(x));
        x = (byte[])original.Clone(); x[3] = 0;
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Error(x));
        x = (byte[])original.Clone(); x[3] = 7;
        Assert.Equal(MetadataErrorCode.InvalidJp2, Error(x));
        x = (byte[])original.Clone(); x[8] ^= 1;
        Assert.Equal(MetadataErrorCode.InvalidJp2, Error(x));
        x = (byte[])original.Clone(); x[20] = (byte)'x';
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Error(x));
        x = (byte[])original.Clone(); x[40] = 0; x[41] = 0; x[42] = 0; x[43] = 7;
        Assert.Equal(MetadataErrorCode.InvalidJp2, Error(x));
        x = (byte[])original.Clone(); x[48] = 0; x[49] = 0; x[50] = 0; x[51] = 0;
        Assert.Equal(MetadataErrorCode.InvalidJp2, Error(x));
        Assert.Equal(MetadataErrorCode.InvalidJp2, Error([.. original, .. original.AsSpan(0, 12).ToArray()]));
    }
}
