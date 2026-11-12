using MetadataSharp.Core;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Tests.Containers;
using Xunit;

namespace MetadataSharp.Tests.Jpeg;

/// <summary>
/// Writing the ICC profile of a JPEG: the APP2 chunks before the first scan become none, or the
/// chunks of one profile. The profiles are a header that passes the checks and a pattern after it.
/// </summary>
public sealed class JpegIccProfileTests
{
    private static readonly byte[] Jfif = JpegFixture.Segment(0xE0, [.. "JFIF\0"u8, 1, 2, 0, 0, 1, 0, 1, 0, 0]);
    private static readonly byte[] ExifSegment = JpegFixture.Segment(0xE1, JpegFixture.Exif());
    private static readonly byte[] Resources = JpegFixture.Segment(0xED, [4, 5, 6]);

    private static byte[] Profile(int size, int seed = 1) => IccProfileTests.Profile(size, seed);

    /// <summary>One APP2 segment of a profile: the identifier, the number from one, the count, the data.</summary>
    private static byte[] Chunk(int number, int count, byte[] data) =>
        JpegFixture.Segment(0xE2, [.. "ICC_PROFILE\0"u8, (byte)number, (byte)count, .. data]);

    private static JpegReadOptions? Options(MetadataLimits? limits) => limits is null ? null : new JpegReadOptions { Limits = limits };

    private static byte[] Save(byte[] image, byte[]? profile, MetadataLimits? limits = null)
    {
        using var source = new MemoryStream(image);
        using var document = JpegMetadataDocument.Open(source, Options(limits));
        using var output = new MemoryStream();
        document.SaveIccProfile(output, profile);
        return output.ToArray();
    }

    /// <summary>The refusal of a save, and the proof that it left the destination empty.</summary>
    private static MetadataErrorCode Refusal(byte[] image, byte[]? profile, MetadataLimits? limits = null)
    {
        using var source = new MemoryStream(image);
        using var document = JpegMetadataDocument.Open(source, Options(limits));
        using var output = new MemoryStream();
        MetadataException refusal = Assert.Throws<MetadataException>(() => document.SaveIccProfile(output, profile));
        Assert.Equal(0, output.Length);
        return refusal.Code;
    }

    /// <summary>The chunks of a file before its first scan: the number, the count and the length of the data of each.</summary>
    private static (int Number, int Count, int Length)[] Chunks(byte[] image)
    {
        using var input = new MemoryStream(image);
        JpegScanResult scan = JpegScanner.Scan(input, new MetadataLimits());
        return scan.Segments.Where(segment => segment.IsIccProfile && segment.Start < scan.ScanOffset)
            .Select(segment => ((int)image[segment.PayloadOffset + 12], (int)image[segment.PayloadOffset + 13], segment.PayloadLength - 14)).ToArray();
    }

    [Fact]
    public void AProfileIsInsertedAfterTheLeadingApp0AndApp1Segments()
    {
        byte[] profile = Profile(600);
        byte[] image = JpegFixture.Build([.. Jfif, .. ExifSegment, .. Resources]);

        byte[] saved = Save(image, profile);

        Assert.Equal(JpegFixture.Build([.. Jfif, .. ExifSegment, .. Chunk(1, 1, profile), .. Resources]), saved);
        Assert.Equal(profile, IccProfiles.Read(saved));
        using var document = JpegMetadataDocument.Open(new MemoryStream(saved));
        Assert.Single(document.Exif!.Entries);
    }

    [Fact]
    public void AProfileIsInsertedAtTheStartOfAFileWithoutThoseSegments()
    {
        byte[] profile = Profile(300);

        Assert.Equal(JpegFixture.Build(Chunk(1, 1, profile)), Save(JpegFixture.Build(), profile));
        Assert.Equal(JpegFixture.Build([.. Chunk(1, 1, profile), .. Resources, .. Jfif]), Save(JpegFixture.Build([.. Resources, .. Jfif]), profile));
        Assert.Equal(JpegFixture.Progressive(prefix: Chunk(1, 1, profile)), Save(JpegFixture.Progressive(), profile));
    }

    [Fact]
    public void EveryChunkIsReplacedAndTheNewProfileStandsAtItsPlace()
    {
        byte[] old = Profile(500, seed: 3), profile = Profile(700);
        // The old chunks are out of order, apart from each other, and one stands before the EXIF block.
        byte[] image = JpegFixture.Build([.. Jfif, .. Chunk(2, 2, old[300..]), .. ExifSegment, .. Resources, .. Chunk(1, 2, old[..300])]);
        Assert.Equal(old, IccProfiles.Read(image));

        byte[] saved = Save(image, profile);

        Assert.Equal(JpegFixture.Build([.. Jfif, .. ExifSegment, .. Chunk(1, 1, profile), .. Resources]), saved);
        Assert.Equal(profile, IccProfiles.Read(saved));
    }

    [Fact]
    public void NullRemovesEveryChunk()
    {
        byte[] old = Profile(500);
        byte[] image = JpegFixture.Build([.. Jfif, .. Chunk(1, 2, old[..300]), .. ExifSegment, .. Chunk(2, 2, old[300..]), .. Resources]);

        byte[] saved = Save(image, null);

        Assert.Equal(JpegFixture.Build([.. Jfif, .. ExifSegment, .. Resources]), saved);
        Assert.Null(IccProfiles.Read(saved));
    }

    [Fact]
    public void RemovingFromAFileWithoutProfileCopiesIt()
    {
        byte[] image = [.. JpegFixture.Build([.. Jfif, .. ExifSegment]), 9, 8, 7];

        Assert.Equal(image, Save(image, null));
    }

    [Fact]
    public void ASecondSaveOfTheSameProfileChangesNothing()
    {
        byte[] old = Profile(500, seed: 3), profile = Profile(140_000);
        byte[] image = JpegFixture.Build([.. Jfif, .. Chunk(1, 1, old), .. ExifSegment, .. Resources]);

        byte[] once = Save(image, profile);

        Assert.Equal(once, Save(once, profile));
        Assert.Equal(once, Save(Save(JpegFixture.Build([.. Jfif, .. ExifSegment, .. Resources]), profile), profile));
        Assert.Equal(Save(once, null), Save(Save(once, null), null));
    }

    [Fact]
    public void InsertingAndRemovingGivesTheSourceBack()
    {
        byte[] image = [.. JpegFixture.Build([.. Jfif, .. ExifSegment, .. Resources]), 1, 2, 3];

        Assert.Equal(image, Save(Save(image, Profile(70_000)), null));
    }

    [Theory]
    [InlineData(128, new[] { 128 })]
    [InlineData(65_519, new[] { 65_519 })]
    [InlineData(65_523, new[] { 65_519, 4 })]
    [InlineData(131_038, new[] { 65_519, 65_519 })]
    [InlineData(200_000, new[] { 65_519, 65_519, 65_519, 3_443 })]
    public void AProfileIsCutIntoNumberedChunksOfAtMost65519Bytes(int size, int[] lengths)
    {
        byte[] profile = Profile(size);

        byte[] saved = Save(JpegFixture.Build([.. Jfif, .. Resources]), profile);

        Assert.Equal(lengths.Select((length, index) => (index + 1, lengths.Length, length)), Chunks(saved));
        Assert.Equal(profile, IccProfiles.Read(saved));
        // The chunks stand together after the JFIF segment, and the rest of the file follows them.
        byte[] image = JpegFixture.Build([.. Jfif, .. Resources]);
        Assert.Equal(image[..(2 + Jfif.Length)], saved[..(2 + Jfif.Length)]);
        Assert.Equal(image[(2 + Jfif.Length)..], saved[^(image.Length - 2 - Jfif.Length)..]);
        Assert.Equal(image.Length + size + 18 * lengths.Length, saved.Length);
    }

    [Theory]
    [InlineData(65_520, new[] { 65_516, 4 })]
    [InlineData(65_521, new[] { 65_517, 4 })]
    [InlineData(65_522, new[] { 65_518, 4 })]
    [InlineData(196_558, new[] { 65_519, 65_519, 65_516, 4 })]
    public void TheLastChunkHoldsAtLeastFourBytes(int size, int[] lengths)
    {
        // exiv2 reads four bytes of every chunk and stops reading the segments of a file at a shorter one.
        byte[] profile = Profile(size);

        byte[] saved = Save(JpegFixture.Build(Jfif), profile);

        Assert.Equal(lengths.Select((length, index) => (index + 1, lengths.Length, length)), Chunks(saved));
        Assert.Equal(profile, IccProfiles.Read(saved));
        Assert.Equal(saved, Save(saved, profile));
    }

    [Fact]
    public void AProfileOf255FullChunksIsWrittenAndOneByteMoreRefused()
    {
        const int Most = 255 * 65_519;
        byte[] image = JpegFixture.Build(Jfif);
        var limits = new MetadataLimits { MaxRetainedMetadataBytes = Most, MaxValueBytes = Most };
        byte[] profile = Profile(Most);

        byte[] saved = Save(image, profile, limits);

        (int Number, int Count, int Length)[] chunks = Chunks(saved);
        Assert.Equal(255, chunks.Length);
        Assert.All(chunks, chunk => Assert.Equal((255, 65_519), (chunk.Count, chunk.Length)));
        Assert.Equal(Enumerable.Range(1, 255), chunks.Select(chunk => chunk.Number));
        Assert.True(profile.AsSpan().SequenceEqual(IccProfiles.Read(saved, limits)));
        // The capacity of the container is not a budget: no limits make room for a 256th chunk.
        Assert.Equal(MetadataErrorCode.IccProfileTooLarge, Refusal(image, Profile(Most + 1), limits with { MaxRetainedMetadataBytes = Most + 1 }));
        Assert.Equal(MetadataErrorCode.IccProfileTooLarge, Refusal(image, Profile(Most + 1)));
    }

    [Fact]
    public void ChunksThatMakeNoWholeProfileAreReplacedOrRemovedAsAnyOther()
    {
        byte[] old = Profile(500, seed: 3), profile = Profile(400);
        byte[][] broken =
        [
            JpegFixture.Build([.. Jfif, .. Chunk(2, 3, old[100..300])]),
            JpegFixture.Build([.. Jfif, .. Chunk(1, 2, old[..300]), .. Chunk(1, 2, old[..300]), .. Chunk(2, 2, old[300..])]),
            JpegFixture.Build([.. Jfif, .. Chunk(1, 2, old[..300]), .. Chunk(2, 3, old[300..])]),
            JpegFixture.Build([.. Jfif, .. Chunk(0, 0, old)]),
            JpegFixture.Build([.. Jfif, .. Chunk(1, 1, old[..100])]),
        ];

        foreach (byte[] image in broken)
        {
            Assert.Equal(MetadataErrorCode.InvalidIccProfile, Assert.Throws<MetadataException>(() => IccProfiles.Read(image)).Code);
            Assert.Equal(JpegFixture.Build([.. Jfif, .. Chunk(1, 1, profile)]), Save(image, profile));
            Assert.Equal(JpegFixture.Build(Jfif), Save(image, null));
        }
    }

    [Fact]
    public void AnApp2SegmentThatIsNoChunkIsLeftWhereItIs()
    {
        byte[] flashpix = JpegFixture.Segment(0xE2, [.. "FPXR\0"u8, 0, 1, 2]);
        byte[] nearly = JpegFixture.Segment(0xE2, [.. "ICC_PROFILE "u8, 1, 1, 5, 6, 7]);
        // The identifier and one byte: too short to carry a number and a count, so no reader takes it for a chunk.
        byte[] stub = JpegFixture.Segment(0xE2, [.. "ICC_PROFILE\0"u8, 1]);
        byte[] other = JpegFixture.Segment(0xE3, [.. "ICC_PROFILE\0"u8, 1, 1, 5, 6, 7]);
        byte[] old = Profile(300, seed: 3), profile = Profile(200);
        byte[] image = JpegFixture.Build([.. Jfif, .. flashpix, .. Chunk(1, 1, old), .. nearly, .. stub, .. other]);

        Assert.Equal(JpegFixture.Build([.. Jfif, .. Chunk(1, 1, profile), .. flashpix, .. nearly, .. stub, .. other]), Save(image, profile));
        Assert.Equal(JpegFixture.Build([.. Jfif, .. flashpix, .. nearly, .. stub, .. other]), Save(image, null));
    }

    [Fact]
    public void AChunkAfterTheFirstScanIsNotOneOfTheFile()
    {
        byte[] profile = Profile(200);
        byte[] between = [.. Chunk(1, 1, Profile(300, seed: 3)), .. JpegFixture.Segment(0xC4, JpegFixture.HuffmanTable)];
        byte[] image = JpegFixture.Progressive(betweenScans: between);
        Assert.Null(IccProfiles.Read(image));

        Assert.Equal(image, Save(image, null));
        byte[] saved = Save(image, profile);
        Assert.Equal(JpegFixture.Progressive(betweenScans: between, prefix: Chunk(1, 1, profile)), saved);
        Assert.Equal(profile, IccProfiles.Read(saved));
        Assert.Equal(image, Save(saved, null));
    }

    [Fact]
    public void TheImageDataAndWhatFollowsTheImageAreCopied()
    {
        byte[] entropy = [0x12, 0xFF, 0x00, 0x34, 0x56];
        byte[] tail = [0xFF, 0xD8, .. Chunk(1, 1, Profile(150, seed: 9)), 0xFF, 0xD9];
        byte[] image = [.. JpegFixture.Build([.. Jfif, .. Chunk(1, 1, Profile(300, seed: 3))], entropy), .. tail];
        using var before = new MemoryStream(image);
        int fromScan = image.Length - (int)JpegScanner.Scan(before, new MetadataLimits()).ScanOffset;
        byte[] profile = Profile(900);

        byte[] saved = Save(image, profile);

        Assert.Equal(image[^fromScan..], saved[^fromScan..]);
        Assert.Equal(image.Length + 600, saved.Length);
        Assert.Equal(profile, IccProfiles.Read(saved));
        Assert.Equal(image.Length - 318, Save(image, null).Length);
    }

    [Fact]
    public void AProfileThatFailsTheHeaderCheckIsRefused()
    {
        byte[] image = JpegFixture.Build([.. Jfif, .. Chunk(1, 1, Profile(300, seed: 3))]);
        byte[] unsigned = Profile(200);
        unsigned[36] = (byte)'x';
        byte[] padded = [.. Profile(200), 0, 0, 0];
        byte[] cut = Profile(200)[..199];
        byte[] headerOnly = Profile(128);

        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(image, []));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(image, Profile(200)[..127]));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(image, unsigned));
        // The size field has to be the length of what is given: padding is not written, and a cut profile is not one.
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(image, padded));
        Assert.Equal(MetadataErrorCode.InvalidIccProfile, Refusal(image, cut));
        // A header alone passes: nothing after it is looked at.
        Assert.Equal(headerOnly, IccProfiles.Read(Save(image, headerOnly)));
    }

    [Fact]
    public void TheRetainedBudgetBoundsTheProfile()
    {
        byte[] image = JpegFixture.Build(Jfif);
        byte[] profile = Profile(2_000);

        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(image, profile, new MetadataLimits { MaxRetainedMetadataBytes = 1_999 }));
        Assert.Equal(profile, IccProfiles.Read(Save(image, profile, new MetadataLimits { MaxRetainedMetadataBytes = 2_000 })));
        // The default budget of a JPEG document is one mebibyte.
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(image, Profile(1_048_577)));
        Assert.Equal(17, Chunks(Save(image, Profile(1_048_576))).Length);
    }

    [Fact]
    public void TheResultHasToBeAFileTheSameBudgetsRead()
    {
        // Five segments: JFIF, the tables, the frame, the scan. Two chunks make seven.
        byte[] image = JpegFixture.Build(Jfif);
        byte[] profile = Profile(70_000);

        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(image, profile, new MetadataLimits { MaxSegments = 6 }));
        Assert.Equal(profile, IccProfiles.Read(Save(image, profile, new MetadataLimits { MaxSegments = 7 })));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(image, profile, new MetadataLimits { MaxInputBytes = image.Length + 70_035 }));
        Assert.Equal(image.Length + 70_036, Save(image, profile, new MetadataLimits { MaxInputBytes = image.Length + 70_036 }).Length);
    }

    [Fact]
    public void BytesThatAreNoSegmentBeforeThePlaceOfTheProfileRefuseIt()
    {
        byte[] profile = Profile(200), old = Profile(300, seed: 3);
        byte[] strayFirst = JpegFixture.Build([1, 2, 3, .. Jfif, .. Chunk(1, 1, old)]);
        byte[] strayLater = JpegFixture.Build([.. Jfif, 1, 2, 3, .. Chunk(1, 1, old)]);
        // The reader stops at the bytes, as any reader that takes segment after segment does.
        Assert.Null(IccProfiles.Read(strayLater));

        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(strayFirst, profile));
        // Taking chunks out needs no place, and a place before the bytes is reached.
        Assert.Equal(JpegFixture.Build([1, 2, 3, .. Jfif]), Save(strayFirst, null));
        Assert.Equal(JpegFixture.Build([.. Jfif, .. Chunk(1, 1, profile), 1, 2, 3]), Save(strayLater, profile));
    }

    [Fact]
    public void TheProfileAndTheCommentAreSavedOneAfterTheOther()
    {
        byte[] profile = Profile(70_000);
        byte[] image = JpegFixture.Build([.. Jfif, .. ExifSegment]);

        byte[] withProfile = Save(image, profile);
        using var document = JpegMetadataDocument.Open(new MemoryStream(withProfile));
        using var output = new MemoryStream();
        document.SaveComment(output, "both");
        byte[] both = output.ToArray();

        Assert.Equal(profile, IccProfiles.Read(both));
        Assert.Equal(new[] { "both" }, Comments.Read(both));
        Assert.Equal(JpegFixture.Build([.. Jfif, .. ExifSegment, .. Chunk(1, 2, profile[..65_519]), .. Chunk(2, 2, profile[65_519..]),
            .. JpegFixture.Segment(0xFE, "both"u8.ToArray())]), both);
        // Each save leaves what the other wrote.
        Assert.Equal(new[] { "both" }, Comments.Read(Save(both, null)));
        Assert.Equal(new[] { "both" }, Comments.Read(Save(both, Profile(300))));
    }

    [Fact]
    public async Task TheArrayGivenIsCopiedBeforeTheSaveReturns()
    {
        byte[] profile = Profile(400), given = Profile(400);
        using var source = new Held(JpegFixture.Build(Jfif));
        using var document = JpegMetadataDocument.Open(source);
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
        using var source = new MemoryStream(JpegFixture.Build(Chunk(1, 1, Profile(300))).ToArray());
        using var document = JpegMetadataDocument.Open(source);
        source.Position = source.Length - 3;
        source.WriteByte(0x2F);
        using var output = new MemoryStream();

        Assert.Equal(MetadataErrorCode.SourceChanged, Assert.Throws<MetadataException>(() => document.SaveIccProfile(output, Profile(200))).Code);
        Assert.Equal(MetadataErrorCode.SourceChanged, Assert.Throws<MetadataException>(() => document.SaveIccProfile(output, null)).Code);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void TheDestinationHasToBeAnotherEmptyStreamAndTheDocumentOpen()
    {
        byte[] profile = Profile(200);
        using var source = new MemoryStream(JpegFixture.Build());
        var document = JpegMetadataDocument.Open(source);
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
        byte[] profile = Profile(500);
        byte[] image = JpegFixture.Build([.. Jfif, .. Chunk(1, 1, Profile(300, seed: 3)), .. ExifSegment]);
        using var source = new JpegScannerTests.ShortReads(image, true);
        await using var document = await JpegMetadataDocument.OpenAsync(source);
        using var output = new JpegCommentTests.AsyncOnly();

        await document.SaveIccProfileAsync(output, profile);

        Assert.Equal(JpegFixture.Build([.. Jfif, .. ExifSegment, .. Chunk(1, 1, profile)]), output.ToArray());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var empty = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => document.SaveIccProfileAsync(empty, profile, cancelled.Token));
        Assert.Equal(0, empty.Length);
    }

    [Fact]
    public void TheSynchronousSaveWorksOnASourceThatGivesAFewBytesAtATime()
    {
        byte[] profile = Profile(500);
        byte[] image = JpegFixture.Build([.. Jfif, .. Chunk(1, 1, Profile(300, seed: 3)), .. ExifSegment]);
        using var source = new JpegScannerTests.ShortReads(image, false);
        using var document = JpegMetadataDocument.Open(source);
        using var output = new MemoryStream();

        document.SaveIccProfile(output, profile);

        Assert.Equal(JpegFixture.Build([.. Jfif, .. ExifSegment, .. Chunk(1, 1, profile)]), output.ToArray());
    }

    [Fact]
    public async Task ASaveToAPathPublishesAWholeFileOrNone()
    {
        byte[] profile = Profile(70_000);
        byte[] image = JpegFixture.Build([.. Jfif, .. Chunk(1, 1, Profile(300, seed: 3))]);
        string directory = Path.Combine(Path.GetTempPath(), "jpeg-icc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string first = Path.Combine(directory, "first.jpg"), second = Path.Combine(directory, "second.jpg");
            using var document = JpegMetadataDocument.Open(new MemoryStream(image));

            document.SaveIccProfileAs(first, profile);
            await document.SaveIccProfileAsAsync(second, null);

            Assert.Equal(profile, IccProfiles.Read(File.ReadAllBytes(first)));
            Assert.Equal(JpegFixture.Build(Jfif), File.ReadAllBytes(second));
            // An existing file is not replaced, and a refusal leaves nothing behind.
            Assert.Throws<IOException>(() => document.SaveIccProfileAs(first, profile));
            await Assert.ThrowsAsync<IOException>(() => document.SaveIccProfileAsAsync(second, profile));
            string third = Path.Combine(directory, "third.jpg");
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

    /// <summary>A source whose asynchronous reads wait while it is held.</summary>
    private sealed class Held(byte[] data) : MemoryStream(data)
    {
        private TaskCompletionSource? gate;

        internal void Hold() => gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Release() => gate?.SetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (gate is not null) await gate.Task.ConfigureAwait(false);
            return Read(buffer.Span);
        }
    }
}
