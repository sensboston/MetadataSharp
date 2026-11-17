using MetadataSharp.Core;
using MetadataSharp.Formats;
using MetadataSharp.Formats.WebP;
using MetadataSharp.Tests.Containers;
using MetadataSharp.Tests.Jpeg;
using Xunit;

namespace MetadataSharp.Tests.WebP;

/// <summary>Writing the ICC profile of a WebP: the ICCP chunk becomes none or one, and the VP8X flag follows it.</summary>
public sealed class WebPIccProfileTests
{
    /// <summary>The image chunk of the lossy fixture, of the lossless one, and of the lossless one that says it uses alpha.</summary>
    private static readonly byte[] Lossy = WebPFixture.Image(0)[12..], Lossless = WebPFixture.Image(1)[12..], LosslessAlpha = WebPFixture.Image(3)[12..];

    /// <summary>The ALPH and VP8 chunks of the fixture that has a VP8X chunk.</summary>
    private static readonly byte[] AlphaAndLossy = WebPFixture.Image(2)[30..];

    private static readonly byte[] ExifChunk = WebPFixture.Chunk("EXIF", JpegFixture.OrientationTiff);

    private static byte[] Profile(int size, int seed = 1) => IccProfileTests.Profile(size, seed);

    private static byte[] Iccp(byte[] profile) => WebPFixture.Chunk("ICCP", profile);

    private static WebPReadOptions? Options(MetadataLimits? limits) => limits is null ? null : new WebPReadOptions { Limits = limits };

    private static byte[] Save(byte[] image, byte[]? profile, MetadataLimits? limits = null)
    {
        using var source = new MemoryStream(image);
        using var document = WebPMetadataDocument.Open(source, Options(limits));
        using var output = new MemoryStream();
        document.SaveIccProfile(output, profile);
        return output.ToArray();
    }

    /// <summary>The refusal of a save, and the proof that it left the destination empty.</summary>
    private static MetadataErrorCode Refusal(byte[] image, byte[]? profile, MetadataLimits? limits = null)
    {
        using var source = new MemoryStream(image);
        using var document = WebPMetadataDocument.Open(source, Options(limits));
        using var output = new MemoryStream();
        MetadataException refusal = Assert.Throws<MetadataException>(() => document.SaveIccProfile(output, profile));
        Assert.Equal(0, output.Length);
        return refusal.Code;
    }

    [Fact]
    public void AFileInTheSimpleLayoutIsGivenAnExtendedHeaderAndTheProfileAfterIt()
    {
        byte[] profile = Profile(560);

        byte[] lossy = Save(WebPFixture.Image(0), profile);
        byte[] lossless = Save(WebPFixture.Image(1), profile);
        byte[] alpha = Save(WebPFixture.Image(3), profile);

        // The canvas of the one-pixel fixtures is written as width and height less one: zeros.
        Assert.Equal(WebPFixture.Extended(0x20, Iccp(profile), Lossy), lossy);
        Assert.Equal(WebPFixture.Extended(0x20, Iccp(profile), Lossless), lossless);
        // A lossless image that says it uses alpha gets the alpha flag, as the other saves give it.
        Assert.Equal(WebPFixture.Extended(0x30, Iccp(profile), LosslessAlpha), alpha);
        Assert.Equal(profile, IccProfiles.Read(lossy));
        Assert.Equal(profile, IccProfiles.Read(alpha));
    }

    [Fact]
    public void TheProfileFollowsTheExtendedHeaderAndItsFlagIsSet()
    {
        byte[] profile = Profile(560);
        byte[] image = WebPFixture.Extended(0x18, AlphaAndLossy, ExifChunk);

        byte[] saved = Save(image, profile);

        Assert.Equal(WebPFixture.Extended(0x38, Iccp(profile), AlphaAndLossy, ExifChunk), saved);
        Assert.Equal(profile, IccProfiles.Read(saved));
        using var document = WebPMetadataDocument.Open(new MemoryStream(saved));
        Assert.NotNull(document.ReadExif());
    }

    [Fact]
    public void AProfileOfOddLengthIsFollowedByOnePaddingByte()
    {
        byte[] profile = Profile(561);

        byte[] saved = Save(WebPFixture.Image(0), profile);

        Assert.Equal(WebPFixture.Extended(0x20, Iccp(profile), Lossy), saved);
        Assert.Equal(0, saved.Length % 2);
        Assert.Equal(profile, IccProfiles.Read(saved));
    }

    [Fact]
    public void TheChunkIsReplacedWhereItStands()
    {
        byte[] profile = Profile(900, 3);
        byte[] image = WebPFixture.Extended(0x38, Iccp(Profile(301, 2)), AlphaAndLossy, ExifChunk);

        byte[] saved = Save(image, profile);

        Assert.Equal(WebPFixture.Extended(0x38, Iccp(profile), AlphaAndLossy, ExifChunk), saved);
        // Bytes that are no profile are replaced as any other.
        byte[] broken = WebPFixture.Extended(0x20, Iccp([1, 2, 3]), Lossy);
        Assert.Equal(WebPFixture.Extended(0x20, Iccp(profile), Lossy), Save(broken, profile));
    }

    [Fact]
    public void NullRemovesTheChunkClearsTheFlagAndKeepsTheExtendedHeader()
    {
        byte[] image = WebPFixture.Extended(0x38, Iccp(Profile(301)), AlphaAndLossy, ExifChunk);

        byte[] saved = Save(image, null);

        Assert.Equal(WebPFixture.Extended(0x18, AlphaAndLossy, ExifChunk), saved);
        Assert.Null(IccProfiles.Read(saved));
        Assert.Equal(WebPFixture.Extended(0, Lossy), Save(WebPFixture.Extended(0x20, Iccp(Profile(300)), Lossy), null));
    }

    [Fact]
    public void RemovingFromAFileWithoutProfileCopiesIt()
    {
        byte[] extended = WebPFixture.Extended(0x18, AlphaAndLossy, ExifChunk);

        Assert.Equal(extended, Save(extended, null));
        // A file in the simple layout stays in it.
        Assert.Equal(WebPFixture.Image(0), Save(WebPFixture.Image(0), null));
        Assert.Equal(WebPFixture.Image(1), Save(WebPFixture.Image(1), null));
    }

    [Fact]
    public void AFileThatHoldsExactlyTheProfileIsCopied()
    {
        byte[] profile = Profile(561);
        byte[] image = WebPFixture.Extended(0x20, Iccp(profile), Lossless);

        Assert.Equal(image, Save(image, profile));
        byte[] other = (byte[])profile.Clone();
        other[^1] ^= 1;
        Assert.Equal(WebPFixture.Extended(0x20, Iccp(other), Lossless), Save(image, other));
    }

    [Fact]
    public void InsertingAndRemovingGivesTheSourceBackOnceItHasAnExtendedHeader()
    {
        byte[] extended = WebPFixture.Extended(0x18, AlphaAndLossy, ExifChunk);

        Assert.Equal(extended, Save(Save(extended, Profile(700)), null));
        // The simple layout is not restored: the header a profile needed stays.
        Assert.Equal(WebPFixture.Extended(0, Lossy), Save(Save(WebPFixture.Image(0), Profile(700)), null));
    }

    [Fact]
    public void AProfileThatFailsTheHeaderCheckIsRefused()
    {
        byte[] image = WebPFixture.Image(0);
        byte[] longer = Profile(300);
        longer[3] = 45; // the size field says 301
        byte[] unsigned = Profile(300);
        unsigned[36] = (byte)'A';

        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(image, new byte[127]));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(image, []));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(image, longer));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(image, unsigned));
    }

    [Fact]
    public void TheRetainedBudgetBoundsTheProfile()
    {
        byte[] image = WebPFixture.Image(0);
        byte[] profile = Profile(2_000);

        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(image, profile, new MetadataLimits { MaxRetainedMetadataBytes = 1_999 }));
        Assert.Equal(profile, IccProfiles.Read(Save(image, profile, new MetadataLimits { MaxRetainedMetadataBytes = 2_000 })));
        // The default budget of a WebP document is one mebibyte.
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(image, Profile(1_048_577)));
        Assert.Equal(Profile(1_048_576), IccProfiles.Read(Save(image, Profile(1_048_576))));
    }

    [Fact]
    public void TheResultHasToBeAFileTheSameBudgetsRead()
    {
        // One chunk, the image. The header and the profile make three, of 18 and 8 + 400 bytes.
        byte[] image = WebPFixture.Image(0);
        byte[] profile = Profile(400);

        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(image, profile, new MetadataLimits { MaxSegments = 2 }));
        Assert.Equal(profile, IccProfiles.Read(Save(image, profile, new MetadataLimits { MaxSegments = 3 })));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(image, profile, new MetadataLimits { MaxInputBytes = image.Length + 425 }));
        Assert.Equal(image.Length + 426, Save(image, profile, new MetadataLimits { MaxInputBytes = image.Length + 426 }).Length);
    }

    [Fact]
    public void AChunkOfAnUnknownKindRefusesAChange()
    {
        byte[] profile = Profile(400);
        byte[] junk = WebPFixture.Chunk("JUNK", [0]);
        byte[] image = WebPFixture.Extended(0x20, Iccp(profile), Lossy, junk);

        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(image, Profile(400, 2)));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(image, null));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(WebPFixture.Extended(0, Lossy, junk), profile));
        // Nothing to change is no change.
        Assert.Equal(image, Save(image, profile));
        Assert.Equal(WebPFixture.Extended(0, Lossy, junk), Save(WebPFixture.Extended(0, Lossy, junk), null));
    }

    [Fact]
    public async Task TheArrayGivenIsCopiedBeforeTheSaveReturns()
    {
        byte[] profile = Profile(400), given = Profile(400);
        using var source = new JpegIccProfileTests.Held(WebPFixture.Image(0));
        using var document = WebPMetadataDocument.Open(source);
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
        using var source = new MemoryStream(WebPFixture.Extended(0x20, Iccp(Profile(300)), Lossy).ToArray());
        using var document = WebPMetadataDocument.Open(source);
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
        using var source = new MemoryStream(WebPFixture.Image(0));
        var document = WebPMetadataDocument.Open(source);
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
        byte[] profile = Profile(70_001);
        byte[] image = WebPFixture.Extended(0x38, Iccp(Profile(301, 2)), AlphaAndLossy, ExifChunk);
        using var source = new JpegScannerTests.ShortReads(image, true);
        await using var document = await WebPMetadataDocument.OpenAsync(source);
        using var output = new JpegCommentTests.AsyncOnly();

        await document.SaveIccProfileAsync(output, profile);

        Assert.Equal(WebPFixture.Extended(0x38, Iccp(profile), AlphaAndLossy, ExifChunk), output.ToArray());
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
        using var source = new JpegScannerTests.ShortReads(WebPFixture.Image(1), false);
        using var document = WebPMetadataDocument.Open(source);
        using var output = new MemoryStream();

        document.SaveIccProfile(output, profile);

        Assert.Equal(WebPFixture.Extended(0x20, Iccp(profile), Lossless), output.ToArray());
    }

    [Fact]
    public async Task ASaveToAPathPublishesAWholeFileOrNone()
    {
        byte[] profile = Profile(900);
        byte[] image = WebPFixture.Extended(0x20, Iccp(Profile(300, 2)), Lossy);
        string directory = Path.Combine(Path.GetTempPath(), "webp-icc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string first = Path.Combine(directory, "first.webp"), second = Path.Combine(directory, "second.webp");
            using var document = WebPMetadataDocument.Open(new MemoryStream(image));

            document.SaveIccProfileAs(first, profile);
            await document.SaveIccProfileAsAsync(second, null);

            Assert.Equal(WebPFixture.Extended(0x20, Iccp(profile), Lossy), File.ReadAllBytes(first));
            Assert.Equal(WebPFixture.Extended(0, Lossy), File.ReadAllBytes(second));
            // An existing file is not replaced, and a refusal leaves nothing behind.
            Assert.Throws<IOException>(() => document.SaveIccProfileAs(first, profile));
            await Assert.ThrowsAsync<IOException>(() => document.SaveIccProfileAsAsync(second, profile));
            string third = Path.Combine(directory, "third.webp");
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
