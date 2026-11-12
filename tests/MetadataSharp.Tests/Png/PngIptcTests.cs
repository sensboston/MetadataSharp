using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Formats.Png;
using MetadataSharp.Tests.Iptc;
using Xunit;

namespace MetadataSharp.Tests.Png;

/// <summary>IPTC written to a PNG as the "Raw profile type iptc" text chunk, the way exiv2 writes it.</summary>
public sealed class PngIptcTests
{
    private const string Keyword = "Raw profile type iptc";
    private static readonly byte[] Stream = [0x1c, 2, 120, 0, 2, 104, 105];
    private static readonly byte[] Other = IptcCodecTests.Block(0x0424, [1, 2, 3], [65, 66], 0x7e);
    private static readonly byte[] Digest = IptcCodecTests.Block(0x0425, new byte[16]);

    private static IptcEdit Caption(string value) { var edit = new IptcEdit(); edit.SetCaption(value); return edit; }
    private static IptcEdit RemoveAll() { var edit = new IptcEdit(); edit.RemoveAllIptc(); return edit; }
    private static byte[] Save(byte[] bytes, IptcEdit edit, IptcWriteOptions? options = null)
    {
        using var doc = PngMetadataDocument.Open(new MemoryStream(bytes)); using var output = new MemoryStream();
        doc.SaveIptc(output, edit, options); return output.ToArray();
    }
    private static IptcPacket? Read(byte[] bytes)
    {
        using var doc = PngMetadataDocument.Open(new MemoryStream(bytes)); return doc.ReadIptc();
    }
    private static byte[] Profile(byte[] bytes, bool compressed)
    {
        string hex = string.Join("\n", Convert.ToHexString(bytes).ToLowerInvariant().Chunk(72).Select(line => new string(line)));
        string count = bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(8);
        byte[] text = Encoding.ASCII.GetBytes("\niptc\n" + count + "\n" + hex + "\n");
        byte[] keyword = Encoding.ASCII.GetBytes(Keyword);
        return compressed ? PngFixture.Chunk("zTXt", [.. keyword, 0, 0, .. PngFixture.Compress(text)]) : PngFixture.Chunk("tEXt", [.. keyword, 0, .. text]);
    }
    private static List<(string Type, byte[] Data)> Chunks(byte[] png)
    {
        var chunks = new List<(string, byte[])>();
        for (int at = 8; at < png.Length;)
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at));
            chunks.Add((Encoding.ASCII.GetString(png, at + 4, 4), png.AsSpan(at + 8, length).ToArray()));
            at += length + 12;
        }
        return chunks;
    }
    /// <summary>The type, the text and the decoded bytes of the one IPTC profile chunk; decoded here, not by the library.</summary>
    private static (string Type, string Text, byte[] Bytes) Stored(byte[] png)
    {
        (string type, byte[] data) = Assert.Single(Chunks(png), c => c.Type is "tEXt" or "zTXt" && c.Data.AsSpan().StartsWith(Encoding.ASCII.GetBytes(Keyword + "\0")));
        int at = Keyword.Length + 1; string text;
        if (type == "zTXt")
        {
            Assert.Equal(0, data[at]);
            using var inflated = new MemoryStream();
            using (var zlib = new ZLibStream(new MemoryStream(data, at + 1, data.Length - at - 1), CompressionMode.Decompress)) zlib.CopyTo(inflated);
            text = Encoding.ASCII.GetString(inflated.ToArray());
        }
        else text = Encoding.ASCII.GetString(data, at, data.Length - at);
        string[] lines = text.Split('\n');
        Assert.Equal("", lines[0]); Assert.Equal("iptc", lines[1]); Assert.Equal(8, lines[2].Length); Assert.Equal("", lines[^1]);
        byte[] bytes = Convert.FromHexString(string.Concat(lines[3..^1]));
        Assert.Equal(bytes.Length, int.Parse(lines[2], System.Globalization.CultureInfo.InvariantCulture));
        // Thirty-six bytes to a line, as exiv2 and ImageMagick break them.
        Assert.All(lines[3..^1].SkipLast(1), line => Assert.Equal(72, line.Length));
        return (type, text, bytes);
    }
    private static PhotoshopResources Resources(byte[] profile) =>
        PhotoshopResources.Parse(profile.AsSpan().StartsWith("Photoshop 3.0\0"u8) ? profile : IptcCodecTests.Photoshop(profile), new());

    [Fact]
    public void AFileWithoutTheChunkGetsACompressedOneBeforeTheImageDataHoldingOneResource()
    {
        byte[] original = PngFixture.Build();
        byte[] added = Save(original, Caption("Привет, " + new string('x', 200)));
        List<(string Type, byte[] Data)> chunks = Chunks(added);
        Assert.Equal(new[] { "IHDR", "zTXt", "IDAT", "IEND" }, chunks.Select(c => c.Type));
        (string type, _, byte[] profile) = Stored(added);
        Assert.Equal("zTXt", type);
        // The form exiv2 writes: Photoshop resources without the APP13 signature, the stream in 0x0404.
        Assert.True(profile.AsSpan().StartsWith(new byte[] { (byte)'8', (byte)'B', (byte)'I', (byte)'M', 4, 4, 0, 0 }));
        PhotoshopResources.Resource only = Assert.Single(Resources(profile).Resources);
        IptcPacket stream = IptcPacket.Parse(only.Data.Span);
        Assert.Equal(IptcCharacterSet.Utf8, stream.CharacterSet);

        IptcPacket read = Assert.IsType<IptcPacket>(Read(added));
        Assert.True(read.IsEditable);
        Assert.Equal("Привет, " + new string('x', 200), read.Caption);
        // The chunk is then replaced where it stands, and removing the stream takes the chunk out again.
        byte[] changed = Save(added, Caption("new"));
        Assert.Equal("new", Read(changed)!.Caption);
        Assert.Equal(new[] { "IHDR", "zTXt", "IDAT", "IEND" }, Chunks(changed).Select(c => c.Type));
        Assert.Equal(original, Save(changed, RemoveAll()));
    }

    [Fact]
    public void AnEditThatChangesNothingIsACopy()
    {
        byte[] original = PngFixture.Build();
        Assert.Equal(original, Save(original, new IptcEdit()));
        Assert.Equal(original, Save(original, RemoveAll()));
        var removal = new IptcEdit(); removal.Remove(new(2, 25));
        Assert.Equal(original, Save(original, removal));
        byte[] held = PngFixture.Build(Profile(Stream, false));
        Assert.Equal(held, Save(held, Caption("hi")));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ResourcesKeepTheirFormTheirOtherResourcesAndByDefaultTheirDigest(bool compressed, bool signed)
    {
        byte[] resources = [.. Other, .. IptcCodecTests.Block(0x0404, Stream, [78]), .. Digest];
        byte[] profile = signed ? IptcCodecTests.Photoshop(resources) : resources;
        byte[] after = PngFixture.Chunk("tEXt", [.. "Title\0"u8, .. "kept"u8]);
        byte[] original = PngFixture.Build([.. Profile(profile, compressed), .. after]);

        byte[] changed = Save(original, Caption("changed"));
        (string type, _, byte[] stored) = Stored(changed);
        Assert.Equal(compressed ? "zTXt" : "tEXt", type);
        Assert.Equal(signed, stored.AsSpan().StartsWith("Photoshop 3.0\0"u8));
        PhotoshopResources parsed = Resources(stored);
        Assert.Equal(new ushort[] { 0x0424, 0x0404, 0x0425 }, parsed.Resources.Select(r => r.Id));
        Assert.Equal(Other, parsed.Resources[0].Block.ToArray());
        Assert.Equal(Digest, parsed.Resources[2].Block.ToArray());           // stale on purpose
        Assert.Equal(IptcCodecTests.Block(0x0404, Stream, [78])[..8], parsed.Resources[1].NameHeader.ToArray());
        Assert.Equal("changed", Read(changed)!.Caption);
        Assert.Equal(new[] { "IHDR", type, "tEXt", "IDAT", "IEND" }, Chunks(changed).Select(c => c.Type));

        PhotoshopResources fresh = Resources(Stored(Save(original, Caption("changed"), new IptcWriteOptions { UpdateDigest = true })).Bytes);
        Assert.Equal(MD5.HashData(fresh.Iptc!.Data.Span), fresh.Resources[2].Data.ToArray());

        // Removal takes the stream and its digest out; the chunk stays for the resource that is left.
        byte[] removed = Save(original, RemoveAll());
        Assert.Null(Read(removed));
        Assert.Equal(Other, Assert.Single(Resources(Stored(removed).Bytes).Resources).Block.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABareStreamStaysBareAndItsChunkGoesWithIt(bool compressed)
    {
        byte[] original = PngFixture.Build(Profile(Stream, compressed));
        byte[] changed = Save(original, Caption("changed"));
        (string type, _, byte[] stored) = Stored(changed);
        Assert.Equal(compressed ? "zTXt" : "tEXt", type);
        Assert.Equal(new byte[] { 0x1c, 2, 120, 0, 7 }, stored[..5]);
        Assert.Equal("changed", Read(changed)!.Caption);
        Assert.Equal(PngFixture.Build(), Save(original, RemoveAll()));
    }

    [Fact]
    public void WhatCannotBeEditedSafelyIsRefused()
    {
        byte[] twice = PngFixture.Build([.. Profile(Stream, false), .. Profile(Stream, true)]);
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Assert.Throws<MetadataException>(() => Save(twice, Caption("x"))).Code);
        // A digest without a stream, and a stream twice.
        byte[] orphan = PngFixture.Build(Profile(Digest, false));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => Save(orphan, Caption("x"))).Code);
        byte[] block = IptcCodecTests.Block(0x0404, Stream);
        byte[] repeated = PngFixture.Build(Profile([.. block, .. block], false));
        Assert.False(Read(repeated)!.IsEditable);
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Assert.Throws<MetadataException>(() => Save(repeated, Caption("x"))).Code);
        // An unknown chunk that is not safe to copy, as for every other PNG write.
        byte[] unsafeChunk = PngFixture.Build(PngFixture.Chunk("abCd", [1]));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => Save(unsafeChunk, Caption("x"))).Code);
        Assert.Equal(unsafeChunk, Save(unsafeChunk, new IptcEdit()));
        // A broken value is refused before anything is written.
        Assert.Throws<ArgumentException>(() => new IptcEdit().SetCountryCode("Latvia"));
    }
}
