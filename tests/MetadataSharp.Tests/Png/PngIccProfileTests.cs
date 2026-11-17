using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Png;
using MetadataSharp.Tests.Containers;
using MetadataSharp.Tests.Jpeg;
using Xunit;

namespace MetadataSharp.Tests.Png;

/// <summary>
/// Writing the ICC profile of a PNG: the iCCP chunk becomes none or one. The compressed bytes are
/// those of the platform's zlib and are not pinned; what a chunk inflates to is.
/// </summary>
public sealed class PngIccProfileTests
{
    private static readonly byte[] Gamma = PngFixture.Chunk("gAMA", [0, 1, 0x86, 0xA0]);
    private static readonly byte[] Palette = PngFixture.Chunk("PLTE", [1, 2, 3, 4, 5, 6]);
    private static readonly byte[] Srgb = PngFixture.Chunk("sRGB", [0]);
    private static readonly byte[] Time = PngFixture.Chunk("tIME", [7, 0xEA, 11, 11, 10, 9, 8]);

    private static byte[] Profile(int size, int seed = 1) => IccProfileTests.Profile(size, seed);

    /// <summary>An iCCP chunk as another writer makes it: its own name and compression level.</summary>
    private static byte[] Iccp(byte[] profile, string name = "icc") =>
        PngFixture.Chunk("iCCP", [.. Encoding.Latin1.GetBytes(name), 0, 0, .. PngFixture.Compress(profile, CompressionLevel.Fastest)]);

    private static PngReadOptions? Options(MetadataLimits? limits) => limits is null ? null : new PngReadOptions { Limits = limits };

    private static byte[] Save(byte[] image, byte[]? profile, MetadataLimits? limits = null)
    {
        using var source = new MemoryStream(image);
        using var document = PngMetadataDocument.Open(source, Options(limits));
        using var output = new MemoryStream();
        document.SaveIccProfile(output, profile);
        return output.ToArray();
    }

    /// <summary>The refusal of a save, and the proof that it left the destination empty.</summary>
    private static MetadataErrorCode Refusal(byte[] image, byte[]? profile, MetadataLimits? limits = null)
    {
        using var source = new MemoryStream(image);
        using var document = PngMetadataDocument.Open(source, Options(limits));
        using var output = new MemoryStream();
        MetadataException refusal = Assert.Throws<MetadataException>(() => document.SaveIccProfile(output, profile));
        Assert.Equal(0, output.Length);
        return refusal.Code;
    }

    /// <summary>The chunks of a file: the type and the whole chunk, in file order.</summary>
    private static (string Type, byte[] Bytes)[] Chunks(byte[] image)
    {
        var chunks = new List<(string, byte[])>();
        for (int at = 8; at < image.Length;)
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(at)) + 12;
            chunks.Add((Encoding.ASCII.GetString(image, at + 4, 4), image[at..(at + length)]));
            at += length;
        }
        return chunks.ToArray();
    }

    private static string Types(byte[] image) => string.Join(' ', Chunks(image).Select(chunk => chunk.Type));

    /// <summary>The file without its chunks of one type.</summary>
    private static byte[] Without(byte[] image, string type) =>
        [.. image.AsSpan(0, 8), .. Chunks(image).Where(chunk => chunk.Type != type).SelectMany(chunk => chunk.Bytes)];

    /// <summary>The name of the iCCP chunk of a file and what its stream inflates to, read without the library.</summary>
    private static (string Name, byte[] Profile) Written(byte[] image)
    {
        byte[] whole = Assert.Single(Chunks(image), chunk => chunk.Type == "iCCP").Bytes;
        byte[] data = whole[8..^4];
        int name = Array.IndexOf(data, (byte)0);
        Assert.Equal(0, data[name + 1]);
        using var zlib = new ZLibStream(new MemoryStream(data[(name + 2)..]), CompressionMode.Decompress);
        using var inflated = new MemoryStream();
        zlib.CopyTo(inflated);
        return (Encoding.Latin1.GetString(data, 0, name), inflated.ToArray());
    }

    [Fact]
    public void AProfileIsInsertedDirectlyAfterTheHeaderChunk()
    {
        byte[] image = PngFixture.Build([.. Gamma, .. Palette], Time);
        byte[] profile = Profile(3_144);

        byte[] saved = Save(image, profile);

        Assert.Equal("IHDR iCCP gAMA PLTE IDAT tIME IEND", Types(saved));
        Assert.Equal("ICC Profile", Written(saved).Name);
        Assert.Equal(PngMetadataDocument.IccProfileName, Written(saved).Name);
        Assert.Equal(profile, Written(saved).Profile);
        Assert.Equal(image, Without(saved, "iCCP"));
        Assert.Equal(profile, IccProfiles.Read(saved));
    }

    [Fact]
    public void TheChunkIsReplacedWhereItStandsAndItsNameWithIt()
    {
        byte[] old = Profile(500, 2), profile = Profile(900, 3);
        byte[] image = PngFixture.Build([.. Gamma, .. Iccp(old, "the old profile"), .. Palette], Time);

        byte[] saved = Save(image, profile);

        Assert.Equal("IHDR gAMA iCCP PLTE IDAT tIME IEND", Types(saved));
        Assert.Equal("ICC Profile", Written(saved).Name);
        Assert.Equal(profile, Written(saved).Profile);
        Assert.Equal(Without(image, "iCCP"), Without(saved, "iCCP"));
        Assert.Equal(profile, IccProfiles.Read(saved));
    }

    [Fact]
    public void NullRemovesTheChunk()
    {
        byte[] image = PngFixture.Build([.. Gamma, .. Iccp(Profile(500)), .. Palette], Time);

        byte[] saved = Save(image, null);

        Assert.Equal(PngFixture.Build([.. Gamma, .. Palette], Time), saved);
        Assert.Null(IccProfiles.Read(saved));
    }

    [Fact]
    public void RemovingFromAFileWithoutProfileCopiesIt()
    {
        byte[] image = PngFixture.Build([.. Srgb, .. Gamma], Time);

        Assert.Equal(image, Save(image, null));
    }

    [Fact]
    public void AFileThatHoldsExactlyTheProfileIsCopied()
    {
        // The chunk of another writer: another name, another compression. It is not rewritten.
        byte[] profile = Profile(3_144);
        byte[] image = PngFixture.Build([.. Gamma, .. Iccp(profile, "Adobe RGB (1998)")], Time);

        Assert.Equal(image, Save(image, profile));
        byte[] written = Save(PngFixture.Build(Gamma), profile);
        Assert.Equal(written, Save(written, profile));
        // One byte of difference is another profile.
        byte[] other = (byte[])profile.Clone();
        other[^1] ^= 1;
        Assert.NotEqual(image, Save(image, other));
        Assert.Equal(other, IccProfiles.Read(Save(image, other)));
    }

    [Fact]
    public void InsertingAndRemovingGivesTheSourceBack()
    {
        byte[] image = PngFixture.Build([.. Gamma, .. Palette], Time);

        Assert.Equal(image, Save(Save(image, Profile(700)), null));
    }

    [Fact]
    public void WritingAProfileTakesTheSrgbChunkOut()
    {
        byte[] profile = Profile(400);
        byte[] image = PngFixture.Build([.. Gamma, .. Srgb, .. Palette]);

        byte[] saved = Save(image, profile);

        Assert.Equal("IHDR iCCP gAMA PLTE IDAT IEND", Types(saved));
        Assert.Equal(Without(image, "sRGB"), Without(saved, "iCCP"));
        // A file that holds the profile and the sRGB chunk is not copied: the chunk goes.
        byte[] both = PngFixture.Build([.. Srgb, .. Iccp(profile)]);
        byte[] one = Save(both, profile);
        Assert.Equal("IHDR iCCP IDAT IEND", Types(one));
        Assert.Equal(profile, IccProfiles.Read(one));
    }

    [Fact]
    public void RemovingAProfileLeavesTheSrgbChunk()
    {
        byte[] image = PngFixture.Build([.. Srgb, .. Iccp(Profile(400)), .. Gamma]);

        Assert.Equal(PngFixture.Build([.. Srgb, .. Gamma]), Save(image, null));
    }

    [Theory]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(70_000)]
    [InlineData(1_048_576)]
    public void AProfileOfAnyLengthWithinTheBudgetIsReadBack(int size)
    {
        byte[] profile = Profile(size);

        Assert.Equal(profile, IccProfiles.Read(Save(PngFixture.Build(), profile)));
    }

    [Fact]
    public void AProfileThatDoesNotCompressIsReadBack()
    {
        byte[] profile = new byte[200_000];
        new Random(7).NextBytes(profile);
        BinaryPrimitives.WriteUInt32BigEndian(profile, (uint)profile.Length);
        "acsp"u8.CopyTo(profile.AsSpan(36));

        byte[] saved = Save(PngFixture.Build(), profile);

        Assert.True(saved.Length > 200_000);
        Assert.Equal(profile, Written(saved).Profile);
        Assert.Equal(profile, IccProfiles.Read(saved));
    }

    [Fact]
    public void AProfileThatFailsTheHeaderCheckIsRefused()
    {
        byte[] image = PngFixture.Build();
        byte[] longer = Profile(300);
        BinaryPrimitives.WriteUInt32BigEndian(longer, 301);
        byte[] shorter = Profile(300);
        BinaryPrimitives.WriteUInt32BigEndian(shorter, 299);
        byte[] unsigned = Profile(300);
        unsigned[36] = (byte)'A';

        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(image, new byte[127]));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(image, []));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(image, longer));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(image, shorter));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(image, unsigned));
    }

    [Fact]
    public void TheRetainedBudgetBoundsTheProfile()
    {
        byte[] image = PngFixture.Build();
        byte[] profile = Profile(2_000);

        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(image, profile, new MetadataLimits { MaxRetainedMetadataBytes = 1_999 }));
        Assert.Equal(profile, IccProfiles.Read(Save(image, profile, new MetadataLimits { MaxRetainedMetadataBytes = 2_000 })));
        // The default budget of a PNG document is one mebibyte.
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(image, Profile(1_048_577)));
    }

    [Fact]
    public void TheResultHasToBeAFileTheSameBudgetsRead()
    {
        // Three chunks: the header, the image data, the end. The profile makes four.
        byte[] image = PngFixture.Build();
        byte[] profile = Profile(400);

        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(image, profile, new MetadataLimits { MaxSegments = 3 }));
        Assert.Equal(profile, IccProfiles.Read(Save(image, profile, new MetadataLimits { MaxSegments = 4 })));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(image, profile, new MetadataLimits { MaxInputBytes = image.Length + 26 }));
    }

    [Fact]
    public void AnUnknownChunkThatIsNotSafeToCopyRefusesAChange()
    {
        byte[] unsafeChunk = PngFixture.Chunk("vpAG", [1]);
        byte[] profile = Profile(400);
        byte[] image = PngFixture.Build([.. Iccp(profile), .. unsafeChunk]);

        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(image, Profile(400, 2)));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(image, null));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(PngFixture.Build(unsafeChunk), profile));
        // Nothing to change is no change.
        Assert.Equal(image, Save(image, profile));
        Assert.Equal(PngFixture.Build(unsafeChunk), Save(PngFixture.Build(unsafeChunk), null));
    }

    [Fact]
    public void TheProfileAndTheCommentAreSavedOneAfterTheOther()
    {
        byte[] profile = Profile(600);
        byte[] image = PngFixture.Build(Gamma, Time);

        byte[] first = SaveComment(Save(image, profile), "after the profile");
        byte[] second = Save(SaveComment(image, "after the profile"), profile);

        Assert.Equal(first, second);
        Assert.Equal("IHDR iCCP gAMA tEXt IDAT tIME IEND", Types(first));
        Assert.Equal(profile, IccProfiles.Read(first));
        Assert.Equal(new[] { "after the profile" }, Comments.Read(first));
        Assert.Equal(image, SaveComment(Save(first, null), null));

        static byte[] SaveComment(byte[] bytes, string? comment)
        {
            using var document = PngMetadataDocument.Open(new MemoryStream(bytes));
            using var output = new MemoryStream();
            document.SaveComment(output, comment);
            return output.ToArray();
        }
    }

    [Fact]
    public async Task TheArrayGivenIsCopiedBeforeTheSaveReturns()
    {
        byte[] profile = Profile(400), given = Profile(400);
        using var source = new JpegIccProfileTests.Held(PngFixture.Build());
        using var document = PngMetadataDocument.Open(source);
        using var output = new MemoryStream();
        source.Hold();

        Task save = document.SaveIccProfileAsync(output, given);
        // The save waits for its first read of the source; the caller is free to reuse the array.
        Assert.False(save.IsCompleted);
        Array.Clear(given);
        source.Release();
        await save;

        Assert.Equal(profile, IccProfiles.Read(output.ToArray()));
    }

    [Fact]
    public void AChangedSourceIsRefusedBeforeAnythingIsWritten()
    {
        using var source = new MemoryStream(PngFixture.Build(Iccp(Profile(300))).ToArray());
        using var document = PngMetadataDocument.Open(source);
        source.Position = source.Length - 3;
        source.WriteByte(0x2F);
        using var output = new MemoryStream();

        Assert.Equal(MetadataErrorCode.SourceChanged, Assert.Throws<MetadataException>(() => document.SaveIccProfile(output, Profile(300, 2))).Code);
        Assert.Equal(MetadataErrorCode.SourceChanged, Assert.Throws<MetadataException>(() => document.SaveIccProfile(output, null)).Code);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void TheDestinationHasToBeAnotherEmptyStreamAndTheDocumentOpen()
    {
        byte[] profile = Profile(300);
        using var source = new MemoryStream(PngFixture.Build());
        var document = PngMetadataDocument.Open(source);
        using var used = new MemoryStream(new byte[] { 7 });

        Assert.Throws<ArgumentException>(() => document.SaveIccProfile(source, profile));
        Assert.Throws<ArgumentException>(() => document.SaveIccProfile(used, profile));
        Assert.Throws<ArgumentNullException>(() => document.SaveIccProfile(null!, profile));
        Assert.Equal(new byte[] { 7 }, used.ToArray());
        document.Dispose();
        using var output = new MemoryStream();
        Assert.Throws<ObjectDisposedException>(() => document.SaveIccProfile(output, profile));
        Assert.Throws<ObjectDisposedException>(() => document.SaveIccProfileAs(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), profile));
    }

    [Fact]
    public async Task TheAsynchronousSaveReadsAndWritesWithoutSynchronousCalls()
    {
        byte[] profile = Profile(70_000);
        byte[] image = PngFixture.Build([.. Gamma, .. Iccp(Profile(300, 2))], Time);
        using var source = new JpegScannerTests.ShortReads(image, true);
        await using var document = await PngMetadataDocument.OpenAsync(source);
        using var output = new JpegCommentTests.AsyncOnly();

        await document.SaveIccProfileAsync(output, profile);

        Assert.Equal(Save(image, profile), output.ToArray());
        Assert.Equal(profile, IccProfiles.Read(output.ToArray()));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var empty = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => document.SaveIccProfileAsync(empty, null, cancelled.Token));
        Assert.Equal(0, empty.Length);
    }

    [Fact]
    public void TheSynchronousSaveWorksOnASourceThatGivesAFewBytesAtATime()
    {
        byte[] profile = Profile(5_000);
        byte[] image = PngFixture.Build([.. Gamma, .. Iccp(Profile(300, 2))], Time);
        using var source = new JpegScannerTests.ShortReads(image, false);
        using var document = PngMetadataDocument.Open(source);
        using var output = new MemoryStream();

        document.SaveIccProfile(output, profile);

        Assert.Equal(Save(image, profile), output.ToArray());
    }

    [Fact]
    public async Task ASaveToAPathPublishesAWholeFileOrNone()
    {
        byte[] profile = Profile(900);
        byte[] image = PngFixture.Build([.. Gamma, .. Iccp(Profile(300, 2))]);
        string directory = Path.Combine(Path.GetTempPath(), "png-icc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string first = Path.Combine(directory, "first.png"), second = Path.Combine(directory, "second.png");
            using var document = PngMetadataDocument.Open(new MemoryStream(image));

            document.SaveIccProfileAs(first, profile);
            await document.SaveIccProfileAsAsync(second, null);

            Assert.Equal(profile, IccProfiles.Read(File.ReadAllBytes(first)));
            Assert.Equal(PngFixture.Build(Gamma), File.ReadAllBytes(second));
            // An existing file is not replaced, and a refusal leaves nothing behind.
            Assert.Throws<IOException>(() => document.SaveIccProfileAs(first, profile));
            await Assert.ThrowsAsync<IOException>(() => document.SaveIccProfileAsAsync(second, profile));
            string third = Path.Combine(directory, "third.png");
            Assert.Equal(MetadataErrorCode.InvalidIccProfile,
                Assert.Throws<MetadataException>(() => document.SaveIccProfileAs(third, new byte[200])).Code);
            Assert.False(File.Exists(third));
            Assert.Equal(2, Directory.GetFiles(directory).Length);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
