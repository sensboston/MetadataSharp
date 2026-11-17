using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Formats.Jpeg;
using Xunit;

namespace MetadataSharp.Tests.Iptc;

public sealed class IptcCodecTests
{
    private static readonly IptcDatasetKey Keywords = new(2, 25);
    internal static byte[] Dataset(byte record, byte id, params byte[] data) => [0x1c, record, id, (byte)(data.Length >> 8), (byte)data.Length, .. data];
    internal static byte[] Raw(IptcPacket packet) { byte[] bytes = new byte[packet.ByteLength]; packet.CopyRawTo(bytes); return bytes; }
    internal static byte[] Block(ushort id, byte[] data, byte[]? name = null, byte pad = 0)
    {
        name ??= [];
        byte[] result = new byte[6 + ((1 + name.Length + 1) & ~1) + 4 + data.Length + (data.Length & 1)];
        "8BIM"u8.CopyTo(result); BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(4), id);
        result[6] = (byte)name.Length; name.CopyTo(result, 7); int sizeAt = 6 + ((1 + name.Length + 1) & ~1);
        if ((name.Length & 1) == 0) result[sizeAt - 1] = pad;
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(sizeAt), (uint)data.Length); data.CopyTo(result, sizeAt + 4);
        if ((data.Length & 1) != 0) result[^1] = pad;
        return result;
    }
    internal static byte[] Photoshop(params byte[][] resources) => [.. "Photoshop 3.0\0"u8, .. resources.SelectMany(b => b)];
    [Fact]
    public void MinimalUtf8ControlsHaveIndependentKnownBytes()
    {
        Assert.Equal(new byte[] { 0x1c, 1, 90, 0, 3, 0x1b, 0x25, 0x47, 0x1c, 2, 0, 0, 2, 0, 4 }, Raw(IptcPacket.CreateEmpty()));
        Assert.Equal(IptcCharacterSet.Utf8, IptcPacket.CreateEmpty().CharacterSet);
    }
    [Fact]
    public void ReplacementsPreserveOccurrencesUnknownDatasetsAndDeepOwnership()
    {
        byte[] bytes = [.. Dataset(2, 25, 65), .. Dataset(7, 200, 9, 8), .. Dataset(2, 25, 66), .. Dataset(2, 250, 6)];
        byte[] expected = bytes.ToArray(); IptcPacket packet = IptcPacket.Parse(bytes); bytes.AsSpan().Clear();
        Assert.Equal(expected, Raw(packet)); Assert.Equal("A", packet.GetText(0));
        byte[] value = [67]; IptcValue owned = IptcValue.FromRaw(value); value[0] = 99;
        var edit = new IptcEdit(); edit.ReplaceRaw(Keywords, [owned, IptcValue.FromRaw([68])]);
        byte[] changed = [0x1c, 2, 25, 0, 1, 67, 0x1c, 2, 25, 0, 1, 68, .. Dataset(7, 200, 9, 8), .. Dataset(2, 250, 6)];
        Assert.Equal(changed, Raw(packet.Apply(edit)!));
        Assert.Throws<NotSupportedException>(() => ((IList<IptcDataset>)packet.Datasets).Clear());
    }
    [Fact]
    public void UntouchedNonminimalExtendedFramingSurvivesAnUnrelatedEdit()
    {
        byte[] original = [0x1c, 2, 250, 0x80, 4, 0, 0, 0, 1, 77];
        IptcPacket packet = IptcPacket.Parse(original); var edit = new IptcEdit(); edit.ReplaceText(Keywords, ["alpha", "alpha"]);
        byte[] changed = [.. original, .. Dataset(2, 25, Encoding.ASCII.GetBytes("alpha")), .. Dataset(2, 25, Encoding.ASCII.GetBytes("alpha"))];
        Assert.Equal(changed, Raw(packet.Apply(edit)!));
        Assert.Same(packet, packet.Apply(new()));
    }
    [Theory]
    [InlineData(32767, 0)]
    [InlineData(32768, 2)]
    [InlineData(65536, 3)]
    public void ModifiedRawLengthsUseMinimalDescriptors(int size, int width)
    {
        var edit = new IptcEdit(); edit.ReplaceRaw(new(2, 250), [IptcValue.FromRaw(new byte[size])]);
        IptcPacket packet = IptcPacket.Parse([]).Apply(edit)!; byte[] bytes = Raw(packet);
        Assert.Equal(width == 0 ? size : 0x8000 | width, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(3)));
        Assert.Equal(size, Assert.Single(packet.Datasets).Value.ByteLength);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void AllSupportedExtendedWidthsReadWithoutNormalization(int width)
    {
        byte[] encoded = [0x1c, 2, 250, 0x80, (byte)width, .. new byte[width - 1], 1, 90];
        Assert.Equal(encoded, Raw(IptcPacket.Parse(encoded)));
    }
    [Theory]
    [InlineData(new byte[] { 0x1c })]
    [InlineData(new byte[] { 0x1c, 2, 25, 0, 2, 65 })]
    [InlineData(new byte[] { 0x1c, 2, 25, 0x80, 0 })]
    [InlineData(new byte[] { 0x1c, 2, 25, 0x80, 2, 0 })]
    [InlineData(new byte[] { 0 })]
    public void TruncationZeroDescriptorsAndTrailingGarbageAreInvalid(byte[] bytes) =>
        Assert.Equal(MetadataErrorCode.InvalidIptc, Assert.Throws<MetadataException>(() => IptcPacket.Parse(bytes)).Code);
    [Fact]
    public void UnsupportedWidthAndHugeUnsignedLengthFailStructurally()
    {
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => IptcPacket.Parse([0x1c, 2, 25, 0x80, 5])).Code);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => IptcPacket.Parse([0x1c, 2, 25, 0x80, 4, 255, 255, 255, 255])).Code);
    }
    [Fact]
    public void CharsetDetectionAndFallbackMatchExiv2()
    {
        IptcPacket legacy = IptcPacket.Parse(Dataset(2, 25, 0xe9));
        Assert.Equal(IptcCharacterSet.Unspecified, legacy.CharacterSet);
        Assert.Equal(IptcTextEncoding.Latin1Fallback, legacy.DetectedTextEncoding);
        Assert.Equal("é", legacy.GetText(0));

        IptcPacket guessedUtf8 = IptcPacket.Parse(Dataset(2, 25, 0xc3, 0xa9));
        Assert.Equal(IptcTextEncoding.Utf8, guessedUtf8.DetectedTextEncoding);
        Assert.Equal("é", guessedUtf8.GetText(0));

        IptcPacket invalidButUnmarked = IptcPacket.Parse(Dataset(2, 25, 0xc3, 0x28));
        Assert.Equal("Ã(", invalidButUnmarked.GetText(0));
        IptcPacket invalidDeclared = IptcPacket.Parse([.. Raw(IptcPacket.CreateEmpty()), .. Dataset(2, 25, 0xc3, 0x28)]);
        Assert.Equal(MetadataErrorCode.InvalidIptc, Assert.Throws<MetadataException>(() => invalidDeclared.GetText(2)).Code);

        IptcPacket unknown = IptcPacket.Parse([.. Dataset(1, 90, 0x1b, 0x2e, 0x41), .. Dataset(2, 25, 65)]);
        Assert.Equal(IptcCharacterSet.Unsupported, unknown.CharacterSet);
        Assert.Equal("A", unknown.GetText(1));
        var edit = new IptcEdit(); edit.ReplaceText(Keywords, ["ASCII"]);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => unknown.Apply(edit)).Code);
        edit.ReplaceRaw(Keywords, [IptcValue.FromRaw([66])]); Assert.Equal(IptcCharacterSet.Unsupported, unknown.Apply(edit)!.CharacterSet);
    }
    [Fact]
    public void TextFieldsValidateByteLimitsCardinalityAndControlsBeforeMutation()
    {
        var edit = new IptcEdit();
        Assert.Throws<ArgumentException>(() => edit.ReplaceText(Keywords, [new string('я', 33)])); Assert.True(edit.IsEmpty);
        Assert.Throws<ArgumentException>(() => edit.ReplaceText(new(2, 5), ["one", "two"]));
        Assert.Throws<ArgumentException>(() => edit.ReplaceText(Keywords, ["a\nb"]));
        Assert.Throws<ArgumentException>(() => edit.ReplaceText(Keywords, ["\ud800"]));
        edit.ReplaceText(new(2, 120), ["Привет\r\n世界"]);
        Assert.Equal("Привет\r\n世界", IptcPacket.CreateEmpty().Apply(edit)!.GetText(2));
        var ascii = new IptcEdit(); ascii.ReplaceText(Keywords, ["non-ASCII я"]);
        // A stream that declares nothing gains the UTF-8 declaration with its first text beyond ASCII.
        Assert.Equal(IptcCharacterSet.Utf8, IptcPacket.Parse([]).Apply(ascii)!.CharacterSet);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => IptcPacket.Parse(Dataset(2, 120, 0xe9)).Apply(ascii)).Code);
    }
    [Fact]
    public void DuplicateControlsAreAmbiguous()
    {
        byte[] charset = Dataset(1, 90, 0x1b, 0x25, 0x47);
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Assert.Throws<MetadataException>(() => IptcPacket.Parse([.. charset, .. charset])).Code);
        byte[] version = Dataset(2, 0, 0, 4);
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Assert.Throws<MetadataException>(() => IptcPacket.Parse([.. version, .. version])).Code);
    }
    [Fact]
    public void ReadOnlyRulesAcceptPaddingAndControlsRepeatedWithTheSameValue()
    {
        byte[] keyword = Dataset(2, 25, 65);
        Assert.True(IptcPacket.Parse(keyword).IsEditable);
        Assert.True(IptcPacket.Inspect(keyword).IsEditable);
        IptcPacket padded = IptcPacket.Inspect([.. keyword, 0, 0, 0]);
        Assert.False(padded.IsEditable); Assert.Single(padded.Datasets); Assert.Equal(9, padded.ByteLength);
        byte[] charset = Dataset(1, 90, 0x1b, 0x25, 0x47), version = Dataset(2, 0, 0, 4);
        IptcPacket repeated = IptcPacket.Inspect([.. charset, .. charset, .. version, .. version, .. keyword]);
        Assert.False(repeated.IsEditable); Assert.Equal(IptcCharacterSet.Utf8, repeated.CharacterSet); Assert.Equal(5, repeated.Datasets.Count);
        // A repeat that says something else is as ambiguous as before.
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Assert.Throws<MetadataException>(() => IptcPacket.Inspect([.. charset, .. Dataset(1, 90, 0x1b, 0x25, 0x48)])).Code);
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Assert.Throws<MetadataException>(() => IptcPacket.Inspect([.. version, .. Dataset(2, 0, 0, 2)])).Code);
        // Zeros with no dataset before them, a nonzero byte after the last dataset, a truncated dataset.
        Assert.Equal(MetadataErrorCode.InvalidIptc, Assert.Throws<MetadataException>(() => IptcPacket.Inspect([0, 0])).Code);
        Assert.Equal(MetadataErrorCode.InvalidIptc, Assert.Throws<MetadataException>(() => IptcPacket.Inspect([.. keyword, 0, 7])).Code);
        Assert.Equal(MetadataErrorCode.InvalidIptc, Assert.Throws<MetadataException>(() => IptcPacket.Inspect([.. keyword, 0x1c, 2])).Code);
    }
    [Fact]
    public void APacketReadForReadingOnlyRefusesAChangeWithTheErrorThatMadeItSo()
    {
        var edit = new IptcEdit(); edit.ReplaceRaw(Keywords, [IptcValue.FromRaw([66])]);
        var remove = new IptcEdit(); remove.RemoveAllIptc();
        IptcPacket padded = IptcPacket.Inspect([.. Dataset(2, 25, 65), 0]);
        Assert.Equal(MetadataErrorCode.InvalidIptc, Assert.Throws<MetadataException>(() => padded.Apply(edit)).Code);
        Assert.Same(padded, padded.Apply(new IptcEdit()));
        Assert.Null(padded.Apply(remove));
        // A container that can read the stream and not write it back says so through the packet.
        IptcPacket contained = IptcPacket.Inspect(Dataset(2, 25, 65), null, new MetadataException(MetadataErrorCode.UnsupportedFeature, "read only"));
        Assert.False(contained.IsEditable);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => contained.Apply(edit)).Code);
    }
    [Fact]
    public void ResourcesFramedWithAnotherAdobeSignatureKeepTheirFramingOnRewrite()
    {
        byte[] iptc = Block(0x0404, Raw(IptcPacket.CreateEmpty())); "AgHg"u8.CopyTo(iptc);
        byte[] other = Block(0x1234, [1, 2]); "PHUT"u8.CopyTo(other);
        PhotoshopResources parsed = PhotoshopResources.Parse(Photoshop(other, iptc), new());
        Assert.Equal(2, parsed.Resources.Count);
        byte[] stream = Dataset(2, 25, 65);
        PhotoshopResources again = PhotoshopResources.Parse(parsed.Rewrite(stream)!, new());
        Assert.Equal(other, again.Resources[0].Block.ToArray());
        Assert.Equal("AgHg"u8.ToArray(), again.Resources[1].Block[..4].ToArray());
        Assert.Equal(stream, again.Iptc!.Data.ToArray());
    }
    [Fact]
    public void ForReadingRepeatedResourcesWithTheSameBytesAreOneStreamAndTheDigestIsNotConsulted()
    {
        byte[] block = Block(0x0404, Dataset(2, 25, 65)), digest = Block(0x0425, new byte[16]);
        Assert.Null(PhotoshopResources.Parse(Photoshop(block), new()).EditRefusal());
        PhotoshopResources twice = PhotoshopResources.Parse(Photoshop(block, digest, block, digest), new());
        Assert.Equal(Dataset(2, 25, 65), twice.IptcForReading()!.Data.ToArray());
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, twice.EditRefusal()!.Code);
        PhotoshopResources orphan = PhotoshopResources.Parse(Photoshop(digest), new());
        Assert.Null(orphan.IptcForReading());
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, orphan.EditRefusal()!.Code);
        PhotoshopResources differing = PhotoshopResources.Parse(Photoshop(block, Block(0x0404, [])), new());
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Assert.Throws<MetadataException>(() => differing.IptcForReading()).Code);
    }
    [Fact]
    public void LastWinsMissingRemovalAndWholeRemovalAreExplicit()
    {
        IptcPacket packet = IptcPacket.CreateEmpty(); var edit = new IptcEdit();
        edit.Remove(Keywords); Assert.Same(packet, packet.Apply(edit)); edit.ReplaceText(Keywords, ["last"]);
        Assert.Equal("last", packet.Apply(edit)!.GetText(2)); edit.Remove(Keywords); Assert.Same(packet, packet.Apply(edit));
        Assert.Throws<ArgumentException>(() => edit.RemoveAllIptc());
        var all = new IptcEdit(); all.RemoveAllIptc(); all.RemoveAllIptc(); Assert.Null(packet.Apply(all));
        Assert.Throws<ArgumentException>(() => all.Remove(Keywords)); Assert.Throws<ArgumentException>(() => new IptcEdit().Remove(new(1, 90)));
        Assert.Throws<ArgumentException>(() => new IptcEdit().ReplaceRaw(new(2, 0), []));
    }
    [Fact]
    public void PacketValueDatasetAndEnumerationBudgetsAreFinite()
    {
        byte[] data = Dataset(2, 25, 65, 66);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => IptcPacket.Parse(data, new() { Limits = new() { MaxPacketBytes = 1 } })).Code);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => IptcPacket.Parse(data, new() { Limits = new() { MaxValueBytes = 1 } })).Code);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => IptcPacket.CreateEmpty(new() { Limits = new() { MaxDatasets = 1 } })).Code);
        Assert.Throws<ArgumentOutOfRangeException>(() => IptcPacket.Parse([], new() { Limits = new() { MaxResources = 0 } }));
        var edit = new IptcEdit(); Assert.Throws<ArgumentException>(() => edit.ReplaceRaw(Keywords, Enumerable.Repeat(IptcValue.FromRaw([]), 4097))); Assert.True(edit.IsEmpty);
        IptcPacket packet = IptcPacket.Parse(data, new() { Limits = new() { MaxPacketBytes = data.Length } }); edit.ReplaceText(Keywords, ["too big"]);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => packet.Apply(edit)).Code); Assert.Equal(data, Raw(packet));
    }
    [Fact]
    public void PhotoshopNamesPaddingUnknownResourcesAndDigestArePreservedOrUpdated()
    {
        byte[] unknown = Block(0x1234, [9, 8, 7], [65, 66], 0x7e);
        byte[] iptc = Block(0x0404, [1], [88], 0x7f); byte[] digest = Block(0x0425, new byte[16], [68, 71]);
        PhotoshopResources source = PhotoshopResources.Parse(Photoshop(unknown, iptc, digest), new()); source.ValidateTargets();
        // The digest is left as it is - stale on purpose - unless the caller asks for it.
        PhotoshopResources stale = PhotoshopResources.Parse(source.Rewrite(Encoding.ASCII.GetBytes("abc"))!, new());
        Assert.Equal(digest, stale.Resources[2].Block.ToArray());
        byte[] result = source.Rewrite(Encoding.ASCII.GetBytes("abc"), updateDigest: true)!;
        PhotoshopResources changed = PhotoshopResources.Parse(result, new());
        Assert.Equal(unknown, changed.Resources[0].Block.ToArray());
        Assert.Equal(Convert.FromHexString("900150983CD24FB0D6963F7D28E17F72"), changed.Resources[2].Data.ToArray());
        Assert.Equal(iptc[..8], changed.Resources[1].NameHeader.ToArray());
        Assert.Equal(Photoshop(unknown), source.Rewrite(null));
        Assert.Null(PhotoshopResources.Parse(Photoshop(iptc, digest), new()).Rewrite(null));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EveryPascalNameParityAndDataParityIsHandled(int nameLength)
    {
        foreach (int dataLength in new[] { 0, 1, 2, 3 })
        {
            byte[] block = Block(0x7777, new byte[dataLength], new byte[nameLength], 0xaa);
            PhotoshopResources parsed = PhotoshopResources.Parse(Photoshop(block), new());
            Assert.Equal(block, Assert.Single(parsed.Resources).Block.ToArray()); Assert.Equal(dataLength, parsed.Resources[0].Data.Length);
        }
    }
    [Fact]
    public void MalformedResourcesAndDuplicateOrOrphanDigestsAreRefused()
    {
        byte[] block = Block(0x0404, [65]);
        for (int n = 1; n < block.Length; n++)
            Assert.Equal(MetadataErrorCode.InvalidPhotoshopResources, Assert.Throws<MetadataException>(() => PhotoshopResources.Parse(Photoshop(block[..n]), new())).Code);
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Assert.Throws<MetadataException>(() => PhotoshopResources.Parse(Photoshop(block, block), new()).ValidateTargets()).Code);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => PhotoshopResources.Parse(Photoshop(Block(0x0425, new byte[16])), new()).ValidateTargets()).Code);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => PhotoshopResources.Parse(Photoshop(block, Block(0x0425, [1])), new()).ValidateTargets()).Code);
        byte[] unsupported = Block(0x0404, []); unsupported[3] = (byte)'X';
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => PhotoshopResources.Parse(Photoshop(unsupported), new())).Code);
    }
    [Fact]
    public void ResourceCountAndApp13CapacityAreCheckedBeforeSerialization()
    {
        byte[] unknown = Block(0x1234, []);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => PhotoshopResources.Parse(Photoshop(unknown, unknown), new() { MaxResources = 1 })).Code);
        PhotoshopResources empty = PhotoshopResources.Empty;
        Assert.Equal(65532, empty.Rewrite(new byte[65506])!.Length);
        Assert.Equal(MetadataErrorCode.IptcTooLarge, Assert.Throws<MetadataException>(() => empty.Rewrite(new byte[65507])).Code);
    }
    [Fact]
    public void ResourceAndDatasetGrowthRespectCapturedBudgets()
    {
        byte[] unknown = Block(0x1234, []);
        PhotoshopResources one = PhotoshopResources.Parse(Photoshop(unknown), new() { MaxResources = 1 });
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => one.Rewrite([1])).Code);
        PhotoshopResources bytes = PhotoshopResources.Parse(Photoshop(unknown), new() { MaxPacketBytes = Photoshop(unknown).Length });
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => bytes.Rewrite([1])).Code);
        IptcPacket packet = IptcPacket.Parse(Dataset(2, 25, 65), new() { Limits = new() { MaxDatasets = 1 } });
        var edit = new IptcEdit(); edit.ReplaceRaw(Keywords, [IptcValue.FromRaw([1]), IptcValue.FromRaw([2])]);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => packet.Apply(edit)).Code);
    }
    [Fact]
    public void BoundedOwnedMutationSmokeNeverLeaksIncidentalRangeErrors()
    {
        var random = new Random(12007);
        for (int i = 0; i < 256; i++)
        {
            byte[] iim = Dataset(2, 25, 65, 66, 67); iim[random.Next(iim.Length)] = (byte)random.Next(256);
            Exception? iimError = Record.Exception(() => IptcPacket.Parse(iim)); Assert.True(iimError is null or MetadataException);
            byte[] irb = Photoshop(Block(0x0404, Dataset(2, 25, 65))); irb[random.Next(irb.Length)] = (byte)random.Next(256);
            Exception? irbError = Record.Exception(() => PhotoshopResources.Parse(irb, new())); Assert.True(irbError is null or MetadataException);
        }
    }

}
