using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Jpeg;
using Xunit;

namespace MetadataSharp.Tests.Jpeg;

/// <summary>Writing the comment of a JPEG: the COM segments before the first scan become none or one.</summary>
public sealed class JpegCommentTests
{
    private static readonly byte[] Jfif = JpegFixture.Segment(0xE0, [.. "JFIF\0"u8, 1, 2, 0, 0, 1, 0, 1, 0, 0]);
    private static readonly byte[] ExifSegment = JpegFixture.Segment(0xE1, JpegFixture.Exif());
    private static readonly byte[] Resources = JpegFixture.Segment(0xED, [4, 5, 6]);

    private static byte[] Com(string text) => JpegFixture.Segment(0xFE, Encoding.UTF8.GetBytes(text));

    private static byte[] Save(byte[] image, string? comment, MetadataLimits? limits = null)
    {
        using var source = new MemoryStream(image);
        using var document = JpegMetadataDocument.Open(source, limits is null ? null : new JpegReadOptions { Limits = limits });
        using var output = new MemoryStream();
        document.SaveComment(output, comment);
        return output.ToArray();
    }

    /// <summary>The refusal of a save, and the proof that it left the destination empty.</summary>
    private static MetadataErrorCode Refusal(byte[] image, string? comment, MetadataLimits? limits = null)
    {
        using var source = new MemoryStream(image);
        using var document = JpegMetadataDocument.Open(source, limits is null ? null : new JpegReadOptions { Limits = limits });
        using var output = new MemoryStream();
        MetadataException refusal = Assert.Throws<MetadataException>(() => document.SaveComment(output, comment));
        Assert.Equal(0, output.Length);
        return refusal.Code;
    }

    [Fact]
    public void ACommentIsInsertedBeforeTheFirstSegmentThatIsNotAnApplicationSegment()
    {
        byte[] image = JpegFixture.Build([.. Jfif, .. ExifSegment, .. Resources]);

        byte[] saved = Save(image, "a comment");

        Assert.Equal(JpegFixture.Build([.. Jfif, .. ExifSegment, .. Resources, .. Com("a comment")]), saved);
        Assert.Equal(new[] { "a comment" }, Comments.Read(saved));
        using var document = JpegMetadataDocument.Open(new MemoryStream(saved));
        Assert.Single(document.Exif!.Entries);
    }

    [Fact]
    public void ACommentIsInsertedAfterTheStartOfAFileWithoutApplicationSegments()
    {
        Assert.Equal(JpegFixture.Build(Com("first")), Save(JpegFixture.Build(), "first"));
        Assert.Equal(JpegFixture.Arithmetic(prefix: Com("first")), Save(JpegFixture.Arithmetic(), "first"));
    }

    [Fact]
    public void EveryCommentIsReplacedByOneWhereTheFirstStood()
    {
        byte[] image = JpegFixture.Build([.. Jfif, .. Com("one"), .. ExifSegment, .. Com("two"), .. Resources, .. Com("three")]);
        Assert.Equal(new[] { "one", "two", "three" }, Comments.Read(image));

        byte[] saved = Save(image, "only");

        Assert.Equal(JpegFixture.Build([.. Jfif, .. Com("only"), .. ExifSegment, .. Resources]), saved);
        Assert.Equal(new[] { "only" }, Comments.Read(saved));
    }

    [Fact]
    public void ACommentThatStandsBeforeTheApplicationSegmentsStaysThere()
    {
        byte[] image = JpegFixture.Build([.. Com("old"), .. Jfif, .. ExifSegment]);

        Assert.Equal(JpegFixture.Build([.. Com("a longer new comment"), .. Jfif, .. ExifSegment]), Save(image, "a longer new comment"));
    }

    [Fact]
    public void NullRemovesEveryComment()
    {
        byte[] image = JpegFixture.Build([.. Jfif, .. Com("one"), .. ExifSegment, .. Com("two"), .. Resources]);

        byte[] saved = Save(image, null);

        Assert.Equal(JpegFixture.Build([.. Jfif, .. ExifSegment, .. Resources]), saved);
        Assert.Empty(Comments.Read(saved));
    }

    [Fact]
    public void RemovingFromAFileWithoutCommentCopiesIt()
    {
        byte[] image = [.. JpegFixture.Build([.. Jfif, .. ExifSegment]), 9, 8, 7];

        Assert.Equal(image, Save(image, null));
    }

    [Fact]
    public void ASecondSaveOfTheSameCommentChangesNothing()
    {
        byte[] image = JpegFixture.Build([.. Jfif, .. Com("one"), .. ExifSegment, .. Com("two")]);

        byte[] once = Save(image, "the same");
        byte[] inserted = Save(JpegFixture.Build([.. Jfif, .. ExifSegment]), "the same");

        Assert.Equal(once, Save(once, "the same"));
        Assert.Equal(inserted, Save(inserted, "the same"));
        Assert.Equal(Save(once, null), Save(Save(once, null), null));
    }

    [Fact]
    public void InsertingAndRemovingGivesTheSourceBack()
    {
        byte[] image = [.. JpegFixture.Build([.. Jfif, .. ExifSegment, .. Resources]), 1, 2, 3];

        Assert.Equal(image, Save(Save(image, "for a while"), null));
    }

    [Fact]
    public void TheImageDataAndWhatFollowsTheImageAreCopied()
    {
        byte[] entropy = [0x12, 0xFF, 0x00, 0x34, 0x56];
        byte[] image = [.. JpegFixture.Build(Jfif, entropy), 0xFF, 0xD8, 0xFF, 0xFE, 0, 4, 65, 66, 0xFF, 0xD9];
        using var before = new MemoryStream(image);
        long scanAt = JpegScanner.Scan(before, new()).ScanOffset;

        byte[] saved = Save(image, "new");

        Assert.Equal(image[(int)scanAt..], saved[^(image.Length - (int)scanAt)..]);
        Assert.Equal(image.Length + Com("new").Length, saved.Length);
        // The comment of the picture after the image is not one of this file.
        Assert.Equal(new[] { "new" }, Comments.Read(saved));
    }

    [Fact]
    public void ACommentAfterTheFirstScanIsNotOneOfTheFile()
    {
        byte[] between = [.. Com("late"), .. JpegFixture.Segment(0xC4, JpegFixture.HuffmanTable)];
        byte[] image = JpegFixture.Progressive(betweenScans: between);
        Assert.Empty(Comments.Read(image));

        Assert.Equal(image, Save(image, null));
        Assert.Equal(JpegFixture.Progressive(betweenScans: between, prefix: Com("early")), Save(image, "early"));
        Assert.Equal(JpegFixture.Progressive(betweenScans: between), Save(JpegFixture.Progressive(betweenScans: between, prefix: Com("early")), null));
    }

    [Theory]
    [InlineData("plain ASCII")]
    [InlineData("Gr\u00FC\u00DFe aus Z\u00FCrich")]
    [InlineData("\u4E16\u754C, \u043C\u0438\u0440, \uD83C\uDF0D")]
    [InlineData(" spaces kept \r\n and lines ")]
    [InlineData("\uFEFFa mark at the start")]
    public void TheTextIsWrittenAsUtf8AndReadBack(string comment)
    {
        byte[] saved = Save(JpegFixture.Build(Jfif), comment);

        Assert.Equal(JpegFixture.Build([.. Jfif, .. JpegFixture.Segment(0xFE, Encoding.UTF8.GetBytes(comment))]), saved);
        Assert.Equal(new[] { comment }, Comments.Read(saved));
    }

    [Fact]
    public void ACommentInAnotherEncodingIsReplacedWhole()
    {
        byte[] latin = JpegFixture.Segment(0xFE, [0x5A, 0xFC, 0x72, 0x69, 0x63, 0x68, 0]);
        byte[] image = JpegFixture.Build([.. Jfif, .. latin]);
        Assert.Equal(new[] { "Z\u00FCrich" }, Comments.Read(image));

        Assert.Equal(JpegFixture.Build([.. Jfif, .. Com("Z\u00FCrich")]), Save(image, "Z\u00FCrich"));
    }

    [Fact]
    public void FillBytesBeforeACommentGoWithIt()
    {
        byte[] image = JpegFixture.Build([.. Jfif, 0xFF, 0xFF, .. Com("filled"), .. Resources]);

        Assert.Equal(JpegFixture.Build([.. Jfif, .. Com("plain"), .. Resources]), Save(image, "plain"));
        Assert.Equal(JpegFixture.Build([.. Jfif, .. Resources]), Save(image, null));
    }

    [Fact]
    public void ACommentAsLongAsASegmentHoldsIsWrittenAndALongerOneRefused()
    {
        byte[] image = JpegFixture.Build(Jfif);
        string full = new('c', 65_533);

        byte[] saved = Save(image, full);

        Assert.Equal(new[] { full }, Comments.Read(saved, new MetadataLimits { MaxValueBytes = 65_533 }));
        Assert.Equal(image.Length + 4 + 65_533, saved.Length);
        Assert.Equal(MetadataErrorCode.CommentTooLarge, Refusal(image, full + "c"));
        // 21,845 characters of three bytes each are two bytes too many; 21,844 fit.
        Assert.Equal(MetadataErrorCode.CommentTooLarge, Refusal(image, new string('\u4E16', 21_845)));
        Assert.Equal(image.Length + 4 + 65_532, Save(image, new string('\u4E16', 21_844)).Length);
    }

    [Fact]
    public void ATextAReaderWouldNotGiveBackIsNotAccepted()
    {
        // Empty, a zero at the end, alone or inside, and half a surrogate pair at either end.
        string[] texts = ["", "\0", "ends with a zero\0", "a zero\0inside", "half a pair \uD83C", "\uDF0D the other half"];
        using var document = JpegMetadataDocument.Open(new MemoryStream(JpegFixture.Build(Com("kept"))));
        using var output = new MemoryStream();

        foreach (string text in texts)
        {
            ArgumentException refusal = Assert.Throws<ArgumentException>(() => document.SaveComment(output, text));
            Assert.Equal("comment", refusal.ParamName);
            Assert.Equal(0, output.Length);
        }
    }

    [Fact]
    public void TheRetainedBudgetBoundsTheComment()
    {
        byte[] image = JpegFixture.Build(Jfif);

        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(image, "twelve bytes", new MetadataLimits { MaxRetainedMetadataBytes = 11 }));
        Assert.Equal(new[] { "twelve bytes" }, Comments.Read(Save(image, "twelve bytes", new MetadataLimits { MaxRetainedMetadataBytes = 12 })));
    }

    [Fact]
    public void TheResultHasToBeAFileTheSameBudgetsRead()
    {
        // Five segments: JFIF, the tables, the frame, the scan. A sixth is one too many to read again.
        byte[] image = JpegFixture.Build(Jfif);
        var limits = new MetadataLimits { MaxSegments = 5 };

        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(image, "one more", limits));
        Assert.Equal(new[] { "one more" }, Comments.Read(Save(image, "one more", limits with { MaxSegments = 6 })));
        // Replacing adds no segment, and removing takes one away.
        byte[] commented = JpegFixture.Build([.. Jfif, .. Com("old")]);
        Assert.Equal(new[] { "new" }, Comments.Read(Save(commented, "new", limits with { MaxSegments = 6 })));
        Assert.Equal(image, Save(commented, null, limits with { MaxSegments = 6 }));
        // The file may grow past the input budget it was opened with; then it is not written.
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(image, "grows", new MetadataLimits { MaxInputBytes = image.Length + 8 }));
        Assert.Equal(image.Length + 9, Save(image, "grows", new MetadataLimits { MaxInputBytes = image.Length + 9 }).Length);
    }

    [Fact]
    public void BytesThatAreNoSegmentBeforeThePlaceOfTheCommentRefuseIt()
    {
        byte[] stray = JpegFixture.Build([.. Jfif, 1, 2, 3]);
        byte[] strayBeforeComment = JpegFixture.Build([.. Jfif, 1, 2, 3, .. Com("hidden")]);
        byte[] strayAfterComment = JpegFixture.Build([.. Jfif, .. Com("seen"), 1, 2, 3]);
        // The reader stops at the bytes, as any reader that takes segment after segment does.
        Assert.Empty(Comments.Read(strayBeforeComment));

        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(stray, "new"));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(strayBeforeComment, "new"));
        // Taking comments out needs no place, and a place before the bytes is reached.
        Assert.Equal(stray, Save(stray, null));
        Assert.Equal(stray, Save(strayBeforeComment, null));
        Assert.Equal(JpegFixture.Build([.. Jfif, .. Com("new"), 1, 2, 3]), Save(strayAfterComment, "new"));
    }

    [Fact]
    public void AChangedSourceIsRefusedBeforeAnythingIsWritten()
    {
        using var source = new MemoryStream(JpegFixture.Build(Com("old")).ToArray());
        using var document = JpegMetadataDocument.Open(source);
        source.Position = source.Length - 3;
        source.WriteByte(0x2F);
        using var output = new MemoryStream();

        Assert.Equal(MetadataErrorCode.SourceChanged, Assert.Throws<MetadataException>(() => document.SaveComment(output, "new")).Code);
        Assert.Equal(MetadataErrorCode.SourceChanged, Assert.Throws<MetadataException>(() => document.SaveComment(output, null)).Code);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void TheDestinationHasToBeAnotherEmptyStreamAndTheDocumentOpen()
    {
        using var source = new MemoryStream(JpegFixture.Build());
        var document = JpegMetadataDocument.Open(source);
        using var used = new MemoryStream(new byte[] { 7 });

        Assert.Throws<ArgumentException>(() => document.SaveComment(source, "x"));
        Assert.Throws<ArgumentException>(() => document.SaveComment(used, "x"));
        Assert.Throws<ArgumentNullException>(() => document.SaveComment(null!, "x"));
        Assert.Equal(new byte[] { 7 }, used.ToArray());
        document.Dispose();
        using var output = new MemoryStream();
        Assert.Throws<ObjectDisposedException>(() => document.SaveComment(output, "x"));
        Assert.Throws<ObjectDisposedException>(() => document.SaveCommentAs(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), "x"));
    }

    [Fact]
    public async Task TheAsynchronousSaveReadsAndWritesWithoutSynchronousCalls()
    {
        byte[] image = JpegFixture.Build([.. Jfif, .. Com("old"), .. ExifSegment]);
        using var source = new JpegScannerTests.ShortReads(image, true);
        await using var document = await JpegMetadataDocument.OpenAsync(source);
        using var output = new AsyncOnly();

        await document.SaveCommentAsync(output, "asynchronous");

        Assert.Equal(JpegFixture.Build([.. Jfif, .. Com("asynchronous"), .. ExifSegment]), output.ToArray());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var empty = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => document.SaveCommentAsync(empty, "never", cancelled.Token));
        Assert.Equal(0, empty.Length);
    }

    [Fact]
    public void TheSynchronousSaveWorksOnASourceThatGivesAFewBytesAtATime()
    {
        byte[] image = JpegFixture.Build([.. Jfif, .. Com("old"), .. ExifSegment]);
        using var source = new JpegScannerTests.ShortReads(image, false);
        using var document = JpegMetadataDocument.Open(source);
        using var output = new MemoryStream();

        document.SaveComment(output, "in pieces");

        Assert.Equal(JpegFixture.Build([.. Jfif, .. Com("in pieces"), .. ExifSegment]), output.ToArray());
    }

    [Fact]
    public async Task ASaveToAPathPublishesAWholeFileOrNone()
    {
        byte[] image = JpegFixture.Build([.. Jfif, .. Com("old")]);
        string directory = Path.Combine(Path.GetTempPath(), "jpeg-comment-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string first = Path.Combine(directory, "first.jpg"), second = Path.Combine(directory, "second.jpg");
            using var document = JpegMetadataDocument.Open(new MemoryStream(image));

            document.SaveCommentAs(first, "to a path");
            await document.SaveCommentAsAsync(second, null);

            Assert.Equal(new[] { "to a path" }, Comments.Read(File.ReadAllBytes(first)));
            Assert.Equal(JpegFixture.Build(Jfif), File.ReadAllBytes(second));
            // An existing file is not replaced, and a refusal leaves nothing behind.
            Assert.Throws<IOException>(() => document.SaveCommentAs(first, "again"));
            await Assert.ThrowsAsync<IOException>(() => document.SaveCommentAsAsync(second, "again"));
            string third = Path.Combine(directory, "third.jpg");
            Assert.Equal(MetadataErrorCode.CommentTooLarge,
                Assert.Throws<MetadataException>(() => document.SaveCommentAs(third, new string('c', 65_534))).Code);
            Assert.False(File.Exists(third));
            Assert.Equal(2, Directory.GetFiles(directory).Length);
            Assert.Equal(new[] { "to a path" }, Comments.Read(File.ReadAllBytes(first)));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A destination that refuses every synchronous write.</summary>
    internal sealed class AsyncOnly : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("A synchronous write was used.");
        public override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("A synchronous write was used.");

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] bytes = buffer.ToArray();
            base.Write(bytes, 0, bytes.Length);
            return ValueTask.CompletedTask;
        }
    }
}
