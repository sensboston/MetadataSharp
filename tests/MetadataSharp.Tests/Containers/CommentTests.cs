using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Formats;
using MetadataSharp.Tests.Jpeg;
using MetadataSharp.Tests.Png;
using MetadataSharp.Tests.WebP;
using Xunit;

namespace MetadataSharp.Tests.Containers;

/// <summary>Synthetic files with the comments a JPEG and a PNG can carry, and the ways they can be wrong.</summary>
public sealed class CommentTests
{
    // ---- builders ----

    private static byte[] Join(params byte[][] parts) => parts.SelectMany(part => part).ToArray();

    private static byte[] Com(string text) => JpegFixture.Segment(0xFE, Encoding.Latin1.GetBytes(text));

    private static byte[] Jpeg(params byte[][] segments) => JpegFixture.Build(Join(segments));

    private static byte[] Text(string keyword, string text) =>
        PngFixture.Chunk("tEXt", [.. Encoding.Latin1.GetBytes(keyword), 0, .. Encoding.Latin1.GetBytes(text)]);

    private static byte[] Compressed(string keyword, string text, byte method = 0) =>
        PngFixture.Chunk("zTXt", [.. Encoding.Latin1.GetBytes(keyword), 0, method, .. PngFixture.Compress(Encoding.Latin1.GetBytes(text))]);

    /// <summary>An iTXt chunk: the keyword, whether the text is compressed, the method, a language tag, the keyword in that language, the text in UTF-8.</summary>
    private static byte[] International(string keyword, string text, bool compressed = false, byte method = 0, string language = "", string translated = "")
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        return PngFixture.Chunk("iTXt", [.. Encoding.Latin1.GetBytes(keyword), 0, (byte)(compressed ? 1 : 0), method,
            .. Encoding.ASCII.GetBytes(language), 0, .. Encoding.UTF8.GetBytes(translated), 0, .. compressed ? PngFixture.Compress(bytes) : bytes]);
    }

    private static string[] Read(byte[] file, MetadataLimits? limits = null) => Comments.Read(file, limits).ToArray();

    private static MetadataErrorCode Refusal(byte[] file, MetadataLimits? limits = null) =>
        Assert.Throws<MetadataException>(() => Comments.Read(file, limits)).Code;

    // ---- JPEG ----

    [Fact]
    public void TheCommentSegmentsOfAJpegAreListedInTheirOrder()
    {
        byte[] file = Jpeg(Com("the first"), JpegFixture.Segment(0xE1, JpegFixture.Exif()), Com("the second"));

        Assert.Equal(new[] { "the first", "the second" }, Read(file));
    }

    [Fact]
    public void TheZeroBytesThatEndACommentAreDroppedAndAnEmptyCommentIsNotListed()
    {
        byte[] file = Jpeg(Com("\0\0"), Com(""), Com("Created with The GIMP\0"), Com("two\0words\0\0"));

        Assert.Equal(new[] { "Created with The GIMP", "two\0words" }, Read(file));
    }

    [Fact]
    public void ACommentIsReadAsUtf8WhenItIsWellFormedAndAsLatin1Otherwise()
    {
        byte[] utf8 = JpegFixture.Segment(0xFE, Encoding.UTF8.GetBytes("Z\u00FCrich, 1000 m"));
        byte[] latin1 = JpegFixture.Segment(0xFE, Encoding.Latin1.GetBytes("Z\u00FCrich, 1000 m"));
        // 0xC3 opens a sequence of two bytes in UTF-8 and the space after it does not continue one.
        byte[] broken = JpegFixture.Segment(0xFE, [0xC3, 0x20, 0x41]);

        Assert.Equal(new[] { "Z\u00FCrich, 1000 m", "Z\u00FCrich, 1000 m", "\u00C3 A" }, Read(Jpeg(utf8, latin1, broken)));
    }

    [Fact]
    public void AnExvFileIsFramedAsAJpegIs()
    {
        byte[] file = Join([0xFF, 0x01, .. "Exiv2"u8], Com("Value 1\0"), [0xFF, 0xD9]);

        Assert.Equal(new[] { "Value 1" }, Read(file));
    }

    [Fact]
    public void AFileWithoutACommentAndAFileOfAKindThatHasNoneGiveAnEmptyList()
    {
        Assert.Empty(Comments.Read(JpegFixture.Build()));
        Assert.Empty(Comments.Read(PngFixture.Build()));
        Assert.Empty(Comments.Read(WebPFixture.Image(0)));
        Assert.Empty(Comments.Read([1, 2, 3]));
        Assert.Empty(Comments.Read([]));
    }

    [Fact]
    public void ACommentAboveTheBudgetOfAValueAndCommentsAboveTheRetainedBudgetAreRefused()
    {
        byte[] file = Jpeg(Com(new string('a', 600)), Com(new string('b', 600)));

        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, new MetadataLimits { MaxValueBytes = 599 }));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, new MetadataLimits { MaxRetainedMetadataBytes = 1_199 }));
        Assert.Equal(2, Comments.Read(file, new MetadataLimits { MaxValueBytes = 600, MaxRetainedMetadataBytes = 1_200 }).Count);
    }

    [Fact]
    public void AFileAboveTheInputBudgetAndASegmentCountAboveItsOwnAreRefused()
    {
        byte[] file = Jpeg(Com("a comment"));

        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, new MetadataLimits { MaxInputBytes = file.Length - 1 }));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, new MetadataLimits { MaxSegments = 3 }));
        Assert.Single(Comments.Read(file, new MetadataLimits { MaxSegments = 4 }));
    }

    // ---- PNG ----

    [Fact]
    public void TheCommentChunksOfAPngAreListedInTheirOrderBeforeAndAfterTheImageData()
    {
        byte[] file = PngFixture.Build(
            Join(Text("Comment", "plain"), Compressed("Comment", "deflated")),
            Join(International("Comment", "Gr\u00FC\u00DFe"), International("Comment", "deflated Gr\u00FC\u00DFe", compressed: true, language: "de", translated: "Kommentar")));

        Assert.Equal(new[] { "plain", "deflated", "Gr\u00FC\u00DFe", "deflated Gr\u00FC\u00DFe" }, Read(file));
    }

    [Fact]
    public void ATextChunkWithAnotherKeywordIsNotAComment()
    {
        byte[] file = PngFixture.Build(
            Join(Text("Description", "a description"), Text("Software", "a tool"), Text("comment", "another case")),
            Join(Text("Commentary", "a longer keyword"), International("Title", "a title"), Compressed("Description", "deflated")));

        Assert.Empty(Comments.Read(file));
    }

    [Fact]
    public void TheTextOfAPngCommentIsReadAsThatOfAJpegIs()
    {
        byte[] file = PngFixture.Build(Join(Text("Comment", "Z\u00FCrich\0"), Text("Comment", ""), Text("Comment", "\0")));

        Assert.Equal(new[] { "Z\u00FCrich" }, Read(file));
    }

    [Fact]
    public void ACommentChunkThatFailsItsChecksumIsRefused()
    {
        byte[] chunk = Text("Comment", "Can you read me?");
        chunk[^1] ^= 1;
        byte[] other = Text("Software", "a tool");
        other[^1] ^= 1;

        Assert.Equal(MetadataErrorCode.InvalidPng, Refusal(PngFixture.Build(chunk)));
        // A chunk that holds no comment is not read at all.
        Assert.Empty(Comments.Read(PngFixture.Build(other)));
    }

    [Fact]
    public void ACompressedCommentWithAnotherMethodOrABrokenStreamIsRefused()
    {
        byte[] stream = PngFixture.Compress(Encoding.Latin1.GetBytes(new string('c', 500) + new string('d', 500)));
        byte[] cut = PngFixture.Chunk("zTXt", [.. "Comment"u8, 0, 0, .. stream[..(stream.Length - 3)]]);

        Assert.Equal(MetadataErrorCode.InvalidPng, Refusal(PngFixture.Build(Compressed("Comment", "text", 1))));
        Assert.Equal(MetadataErrorCode.InvalidPng, Refusal(PngFixture.Build(PngFixture.Chunk("zTXt", [.. "Comment"u8, 0]))));
        Assert.Equal(MetadataErrorCode.InvalidPng, Refusal(PngFixture.Build(cut)));
        Assert.Equal(MetadataErrorCode.InvalidPng, Refusal(PngFixture.Build(International("Comment", "text", compressed: true, method: 1))));
    }

    [Fact]
    public void AnInternationalCommentWithoutItsFieldsIsRefused()
    {
        byte[] noFields = PngFixture.Chunk("iTXt", [.. "Comment"u8, 0, 0]);
        byte[] noKeyword = PngFixture.Chunk("iTXt", [.. "Comment"u8, 0, 0, 0, .. "en"u8, 0, .. "text"u8]);
        byte[] flag = PngFixture.Chunk("iTXt", [.. "Comment"u8, 0, 2, 0, 0, 0, .. "text"u8]);

        Assert.Equal(MetadataErrorCode.InvalidPng, Refusal(PngFixture.Build(noFields)));
        Assert.Equal(MetadataErrorCode.InvalidPng, Refusal(PngFixture.Build(noKeyword)));
        Assert.Equal(MetadataErrorCode.InvalidPng, Refusal(PngFixture.Build(flag)));
    }

    [Fact]
    public void ACommentThatInflatesBeyondTheBudgetIsRefused()
    {
        byte[] file = PngFixture.Build(Join(Text("Comment", new string('a', 100)), Compressed("Comment", new string('b', 50_000))));

        Assert.True(file.Length < 2_000);
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, new MetadataLimits { MaxValueBytes = 49_999 }));
        // The first comment has taken 100 bytes of what may be retained.
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, new MetadataLimits { MaxRetainedMetadataBytes = 50_099 }));
        Assert.Equal(2, Comments.Read(file, new MetadataLimits { MaxRetainedMetadataBytes = 50_100 }).Count);
    }

    [Fact]
    public void AChunkCountAboveItsBudgetIsRefused()
    {
        byte[] file = PngFixture.Build(after: Text("Comment", "after the image"));

        // The header, the image data, the comment and the end: four chunks.
        Assert.Equal(MetadataErrorCode.LimitExceeded, Refusal(file, new MetadataLimits { MaxSegments = 3 }));
        Assert.Single(Comments.Read(file, new MetadataLimits { MaxSegments = 4 }));
    }
}
