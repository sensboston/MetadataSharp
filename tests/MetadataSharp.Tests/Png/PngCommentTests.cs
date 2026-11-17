using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Png;
using MetadataSharp.Tests.Jpeg;
using Xunit;

namespace MetadataSharp.Tests.Png;

/// <summary>Writing the comment of a PNG: the text chunks with the keyword Comment become none or one.</summary>
public sealed class PngCommentTests
{
    private static readonly byte[] Gamma = PngFixture.Chunk("gAMA", [0, 1, 0x86, 0xA0]);
    private static readonly byte[] Time = PngFixture.Chunk("tIME", [7, 0xEA, 11, 11, 10, 9, 8]);
    private static readonly byte[] Title = PngFixture.Chunk("tEXt", [.. "Title\0"u8, .. "a title"u8]);

    /// <summary>A tEXt chunk: the keyword, a zero, the text in ISO-8859-1.</summary>
    private static byte[] Plain(string text) => PngFixture.Chunk("tEXt", [.. "Comment\0"u8, .. Encoding.Latin1.GetBytes(text)]);

    /// <summary>A zTXt chunk: the keyword, a zero, the method, the text deflated.</summary>
    private static byte[] Packed(string text) => PngFixture.Chunk("zTXt", [.. "Comment\0"u8, 0, .. PngFixture.Compress(Encoding.Latin1.GetBytes(text))]);

    /// <summary>An iTXt chunk: the keyword, a zero, not compressed, the method, no language, no translated keyword, UTF-8.</summary>
    private static byte[] Wide(string text) => PngFixture.Chunk("iTXt", [.. "Comment\0"u8, 0, 0, 0, 0, .. Encoding.UTF8.GetBytes(text)]);

    private static PngReadOptions? Options(MetadataLimits? limits) => limits is null ? null : new PngReadOptions { Limits = limits };

    private static byte[] Save(byte[] image, string? comment, MetadataLimits? limits = null)
    {
        using var source = new MemoryStream(image);
        using var document = PngMetadataDocument.Open(source, Options(limits));
        using var output = new MemoryStream();
        document.SaveComment(output, comment);
        return output.ToArray();
    }

    /// <summary>The refusal of a save, and the proof that it left the destination empty.</summary>
    private static MetadataErrorCode Refusal(byte[] image, string? comment, MetadataLimits? limits = null)
    {
        using var source = new MemoryStream(image);
        using var document = PngMetadataDocument.Open(source, Options(limits));
        using var output = new MemoryStream();
        MetadataException refusal = Assert.Throws<MetadataException>(() => document.SaveComment(output, comment));
        Assert.Equal(0, output.Length);
        return refusal.Code;
    }

    [Fact]
    public void ACommentIsInsertedDirectlyBeforeTheImageData()
    {
        byte[] image = PngFixture.Build([.. Gamma, .. Title], Time);

        byte[] saved = Save(image, "a comment");

        Assert.Equal(PngFixture.Build([.. Gamma, .. Title, .. Plain("a comment")], Time), saved);
        Assert.Equal(new[] { "a comment" }, Comments.Read(saved));
    }

    [Fact]
    public void EveryCommentChunkIsReplacedByOneWhereTheFirstStood()
    {
        byte[] image = PngFixture.Build([.. Plain("one"), .. Gamma, .. Packed("two")], [.. Wide("three"), .. Time]);
        Assert.Equal(new[] { "one", "two", "three" }, Comments.Read(image));

        byte[] saved = Save(image, "only");

        Assert.Equal(PngFixture.Build([.. Plain("only"), .. Gamma], Time), saved);
        Assert.Equal(new[] { "only" }, Comments.Read(saved));
    }

    [Fact]
    public void ACommentAfterTheImageDataStaysThere()
    {
        byte[] image = PngFixture.Build(Gamma, [.. Time, .. Packed("old")]);

        Assert.Equal(PngFixture.Build(Gamma, [.. Time, .. Plain("a longer new comment")]), Save(image, "a longer new comment"));
    }

    [Fact]
    public void NullRemovesEveryComment()
    {
        byte[] image = PngFixture.Build([.. Plain("one"), .. Gamma, .. Wide("two")], [.. Packed("three"), .. Time]);

        byte[] saved = Save(image, null);

        Assert.Equal(PngFixture.Build(Gamma, Time), saved);
        Assert.Empty(Comments.Read(saved));
    }

    [Fact]
    public void RemovingFromAFileWithoutCommentCopiesIt()
    {
        byte[] image = PngFixture.Build([.. Gamma, .. Title], Time);

        Assert.Equal(image, Save(image, null));
    }

    [Fact]
    public void ASecondSaveOfTheSameCommentChangesNothing()
    {
        byte[] image = PngFixture.Build([.. Plain("one"), .. Gamma], Wide("two"));

        byte[] once = Save(image, "the same");
        byte[] inserted = Save(PngFixture.Build(Gamma), "the same");

        Assert.Equal(once, Save(once, "the same"));
        Assert.Equal(inserted, Save(inserted, "the same"));
        Assert.Equal(Save(once, null), Save(Save(once, null), null));
    }

    [Fact]
    public void InsertingAndRemovingGivesTheSourceBack()
    {
        byte[] image = PngFixture.Build([.. Gamma, .. Title], Time);

        Assert.Equal(image, Save(Save(image, "for a while"), null));
        Assert.Equal(image, Save(Save(image, "на время"), null));
    }

    [Theory]
    [InlineData("plain ASCII")]
    [InlineData("caf\u00E9 \u00FC\u00DF \u00FF")]
    [InlineData("two\nlines")]
    public void ATextOfIso88591IsWrittenAsATextChunk(string comment)
    {
        byte[] saved = Save(PngFixture.Build(), comment);

        Assert.Equal(PngFixture.Build(Plain(comment)), saved);
        Assert.Equal(new[] { comment }, Comments.Read(saved));
    }

    [Theory]
    [InlineData("Комментарий — ü")]
    [InlineData("注釈 \U0001F600")]
    [InlineData("\u0100")]
    // These two are ISO-8859-1, and their bytes there are well-formed UTF-8 for another text
    // ("é" and "€"): a reader that tries UTF-8 first would not give them back from a tEXt chunk.
    [InlineData("\u00C3\u00A9")]
    [InlineData("\u00E2\u0082\u00AC")]
    public void AnyOtherTextIsWrittenAsAnInternationalChunkInUtf8(string comment)
    {
        byte[] saved = Save(PngFixture.Build(), comment);

        Assert.Equal(PngFixture.Build(Wide(comment)), saved);
        Assert.Equal(new[] { comment }, Comments.Read(saved));
    }

    [Fact]
    public void OnlyTheKeywordCommentIsAComment()
    {
        byte[] others =
        [
            .. PngFixture.Chunk("tEXt", [.. "Description\0"u8, .. "d"u8]),
            .. PngFixture.Chunk("tEXt", [.. "Commentary\0"u8, .. "c"u8]),
            .. PngFixture.Chunk("tEXt", [.. "comment\0"u8, .. "c"u8]),
            .. PngFixture.Chunk("iTXt", [.. "Comments\0"u8, 0, 0, 0, 0, .. "c"u8]),
        ];
        byte[] image = PngFixture.Build(others);

        Assert.Equal(image, Save(image, null));
        Assert.Equal(PngFixture.Build([.. others, .. Plain("the comment")]), Save(image, "the comment"));
    }

    [Fact]
    public void TheOtherMetadataIsCopiedAsItIs()
    {
        var edit = new XmpEdit();
        edit.Set(new("http://ns.adobe.com/xap/1.0/", "CreatorTool"), XmpValue.CreateText("kept"));
        XmpPacket packet = XmpPacket.CreateEmpty().Apply(edit)!;
        byte[] xmp = new byte[packet.ByteLength];
        packet.CopyRawTo(xmp);
        byte[] profile = PngFixture.Chunk("iCCP", [.. "icc\0"u8, 0, .. PngFixture.Compress(Containers.IccProfileTests.Profile(300))]);
        byte[] image = PngFixture.Build([.. profile, .. PngFixture.Xmp(xmp)], Time);

        byte[] saved = Save(image, "beside the rest");

        Assert.Equal(PngFixture.Build([.. profile, .. PngFixture.Xmp(xmp), .. Plain("beside the rest")], Time), saved);
        Assert.Equal(Containers.IccProfileTests.Profile(300), IccProfiles.Read(saved));
    }

    [Fact]
    public void ATextAReaderWouldNotGiveBackIsNotAccepted()
    {
        using var source = new MemoryStream(PngFixture.Build());
        using var document = PngMetadataDocument.Open(source);
        using var output = new MemoryStream();

        Assert.Throws<ArgumentException>(() => document.SaveComment(output, ""));
        Assert.Throws<ArgumentException>(() => document.SaveComment(output, "a\0b"));
        Assert.Throws<ArgumentException>(() => document.SaveComment(output, "ends with zero\0"));
        Assert.Throws<ArgumentException>(() => document.SaveComment(output, "half \uD83D of a pair"));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void TheRetainedBudgetBoundsTheChunk()
    {
        byte[] image = PngFixture.Build();
        // The data of the chunk: the keyword and its zero, then the text.
        string fits = new('c', 1_992), over = new('c', 1_993);

        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(image, over, new MetadataLimits { MaxRetainedMetadataBytes = 2_000 }));
        Assert.Equal(new[] { fits }, Comments.Read(Save(image, fits, new MetadataLimits { MaxRetainedMetadataBytes = 2_000 })));
    }

    [Fact]
    public void TheResultHasToBeAFileTheSameBudgetsRead()
    {
        // Three chunks: the header, the image data, the end. The comment makes four, of 12 + 8 + 9 bytes.
        byte[] image = PngFixture.Build();

        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(image, "a comment", new MetadataLimits { MaxSegments = 3 }));
        Assert.Equal(new[] { "a comment" }, Comments.Read(Save(image, "a comment", new MetadataLimits { MaxSegments = 4 })));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(image, "a comment", new MetadataLimits { MaxInputBytes = image.Length + 28 }));
        Assert.Equal(image.Length + 29, Save(image, "a comment", new MetadataLimits { MaxInputBytes = image.Length + 29 }).Length);
    }

    [Fact]
    public void AnUnknownChunkThatIsNotSafeToCopyRefusesAChange()
    {
        byte[] unsafeChunk = PngFixture.Chunk("vpAG", [1]);
        byte[] image = PngFixture.Build([.. unsafeChunk, .. Plain("old")]);

        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(image, "new"));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Refusal(image, null));
        // Nothing to change is no change.
        byte[] without = PngFixture.Build(unsafeChunk);
        Assert.Equal(without, Save(without, null));
        // A chunk that says it is safe to copy is copied.
        byte[] safe = PngFixture.Build([.. PngFixture.Chunk("vpAg", [1]), .. Plain("old")]);
        Assert.Equal(PngFixture.Build([.. PngFixture.Chunk("vpAg", [1]), .. Plain("new")]), Save(safe, "new"));
    }

    [Fact]
    public void AChangedSourceIsRefusedBeforeAnythingIsWritten()
    {
        using var source = new MemoryStream(PngFixture.Build(Plain("old")).ToArray());
        using var document = PngMetadataDocument.Open(source);
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
        using var source = new MemoryStream(PngFixture.Build());
        var document = PngMetadataDocument.Open(source);
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
        byte[] image = PngFixture.Build([.. Gamma, .. Packed("old")], Time);
        using var source = new JpegScannerTests.ShortReads(image, true);
        await using var document = await PngMetadataDocument.OpenAsync(source);
        using var output = new JpegCommentTests.AsyncOnly();

        await document.SaveCommentAsync(output, "asynchronous");

        Assert.Equal(PngFixture.Build([.. Gamma, .. Plain("asynchronous")], Time), output.ToArray());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var empty = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => document.SaveCommentAsync(empty, "never", cancelled.Token));
        Assert.Equal(0, empty.Length);
    }

    [Fact]
    public void TheSynchronousSaveWorksOnASourceThatGivesAFewBytesAtATime()
    {
        byte[] image = PngFixture.Build([.. Gamma, .. Packed("old")], Time);
        using var source = new JpegScannerTests.ShortReads(image, false);
        using var document = PngMetadataDocument.Open(source);
        using var output = new MemoryStream();

        document.SaveComment(output, "in pieces");

        Assert.Equal(PngFixture.Build([.. Gamma, .. Plain("in pieces")], Time), output.ToArray());
    }

    [Fact]
    public async Task ASaveToAPathPublishesAWholeFileOrNone()
    {
        byte[] image = PngFixture.Build([.. Gamma, .. Plain("old")]);
        string directory = Path.Combine(Path.GetTempPath(), "png-comment-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string first = Path.Combine(directory, "first.png"), second = Path.Combine(directory, "second.png");
            using var document = PngMetadataDocument.Open(new MemoryStream(image));

            document.SaveCommentAs(first, "to a path");
            await document.SaveCommentAsAsync(second, null);

            Assert.Equal(new[] { "to a path" }, Comments.Read(File.ReadAllBytes(first)));
            Assert.Equal(PngFixture.Build(Gamma), File.ReadAllBytes(second));
            // An existing file is not replaced, and a refusal leaves nothing behind.
            Assert.Throws<IOException>(() => document.SaveCommentAs(first, "again"));
            await Assert.ThrowsAsync<IOException>(() => document.SaveCommentAsAsync(second, "again"));
            string third = Path.Combine(directory, "third.png");
            Assert.Equal(MetadataErrorCode.LimitExceeded,
                Assert.Throws<MetadataException>(() => document.SaveCommentAs(third, new string('c', 1_048_576))).Code);
            Assert.False(File.Exists(third));
            Assert.Equal(2, Directory.GetFiles(directory).Length);
            Assert.Equal(new[] { "to a path" }, Comments.Read(File.ReadAllBytes(first)));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
