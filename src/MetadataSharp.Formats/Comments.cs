using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Formats.Internal;
using MetadataSharp.Formats.Png;

namespace MetadataSharp.Formats;

/// <summary>
/// Read-only access to the free-text comments a file carries outside its EXIF, IPTC and XMP
/// metadata: the COM segments of a JPEG and the text chunks of a PNG whose keyword is
/// <c>Comment</c>. In a PGF header region holding a PNG, the reference tool's <c>Description</c>
/// keyword is also read as its comment. Nothing is written.
/// </summary>
public static class Comments
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// Return the comments of a complete file in the order the file has them; the list is empty
    /// when the file has none or is of a kind that has no comment this reader knows.
    /// <list type="bullet">
    /// <item>JPEG, and EXV, which is framed the same way: every COM segment before the first scan.</item>
    /// <item>PNG: every <c>tEXt</c>, <c>zTXt</c> and <c>iTXt</c> chunk whose keyword is exactly
    /// <c>Comment</c>, before or after the image data; compressed text is inflated within the budget.
    /// Within a PGF header region, the <c>Description</c> keyword the reference tool reads as a
    /// comment is included too.</item>
    /// </list>
    /// The zero bytes that end a comment are dropped, and a comment that is empty without them is
    /// not listed. The bytes are read as UTF-8 when they are well-formed UTF-8 and as ISO-8859-1
    /// otherwise: neither a COM segment nor, in practice, a <c>tEXt</c> chunk says which it holds.
    /// </summary>
    /// <param name="file">The whole file.</param>
    /// <param name="limits">
    /// The budgets. Unless given, those of a whole file: <see cref="MetadataLimits.MaxValueBytes"/>
    /// 16,777,216, which bounds one comment, and <see cref="MetadataLimits.MaxRetainedMetadataBytes"/>
    /// 67,108,864, which bounds all of them together.
    /// </param>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidPng"/> when a comment chunk of a PNG fails its checksum,
    /// names a compression method other than deflate, lacks a field its type has, or does not inflate.
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when the file, the number of its segments or
    /// chunks, one comment or the comments together exceed a budget.
    /// </exception>
    public static IReadOnlyList<string> Read(ReadOnlySpan<byte> file, MetadataLimits? limits = null)
    {
        limits = EmbeddedFile.Checked(file, limits);
        if (ImageMetadata.Detect(file) == MetadataFormat.Pgf)
        {
            Pgf.PgfMetadata pgf = Pgf.PgfMetadata.Read(file, limits);
            int regionStart = file.Length > 20 && file[20] == 2 ? 24 + 256 * 3 : 24;
            var embeddedComments = new List<string>();
            Png(file.Slice(regionStart, checked((int)pgf.ImageOffset) - regionStart), limits, embeddedComments,
                includeReferenceDescription: true);
            return embeddedComments.AsReadOnly();
        }
        var found = new List<string>();
        switch (EmbeddedFile.Detect(file))
        {
            case FileKind.Jpeg:
                Segments(file, 2, limits, found);
                break;
            case FileKind.Exv:
                Segments(file, 7, limits, found);
                break;
            case FileKind.Png:
                Png(file, limits, found);
                break;
        }
        return found.AsReadOnly();
    }

    /// <summary>UTF-8 when the bytes are well-formed UTF-8, ISO-8859-1 otherwise.</summary>
    internal static string Decode(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    /// <summary>List one comment, without the zero bytes that end it, unless nothing is left; charge it to the budgets.</summary>
    private static void Add(ReadOnlySpan<byte> text, MetadataLimits limits, ref long retained, long offset, List<string> found)
    {
        text = text.TrimEnd((byte)0);
        if (text.IsEmpty) return;
        if (text.Length > limits.MaxValueBytes)
            throw EmbeddedFile.Limit("A comment exceeds the budget of one value.", offset);
        retained += text.Length;
        if (retained > limits.MaxRetainedMetadataBytes)
            throw EmbeddedFile.Limit("The comments exceed the budget of retained metadata.", offset);
        found.Add(Decode(text));
    }

    // ---- JPEG and EXV: the COM segments ----

    private static void Segments(ReadOnlySpan<byte> data, int at, MetadataLimits limits, List<string> found)
    {
        long retained = 0;
        for (int count = 0; EmbeddedFile.Segment(data, ref at, out byte marker, out int payload, out int end); count++)
        {
            if (count == limits.MaxSegments)
                throw EmbeddedFile.Limit("The segment count exceeds its budget.", payload - 4);
            if (marker == 0xFE) Add(data.Slice(payload, end - payload), limits, ref retained, payload, found);
        }
    }

    // ---- PNG: tEXt, zTXt and iTXt chunks with the keyword "Comment" ----

    private const uint TextChunk = 0x74455874, CompressedTextChunk = 0x7A545874, InternationalTextChunk = 0x69545874;

    private static MetadataException Bad(string message, long offset) => new(MetadataErrorCode.InvalidPng, message, offset);

    private static void Png(ReadOnlySpan<byte> data, MetadataLimits limits, List<string> found,
        bool includeReferenceDescription = false)
    {
        int at = 8;
        long retained = 0;
        for (int count = 0; EmbeddedFile.Chunk(data, ref at, out uint type, out int payload, out int length); count++)
        {
            if (count == limits.MaxSegments)
                throw EmbeddedFile.Limit("The chunk count exceeds its budget.", payload - 8);
            if (type == EmbeddedFile.PngEnd) break;
            if (type is not (TextChunk or CompressedTextChunk or InternationalTextChunk)) continue;
            ReadOnlySpan<byte> chunk = data.Slice(payload, length);
            int keywordLength;
            if (chunk.StartsWith("Comment\0"u8)) keywordLength = 8;
            else if (includeReferenceDescription && chunk.StartsWith("Description\0"u8)) keywordLength = 12;
            else continue;
            if (!EmbeddedFile.ChecksumHolds(data, payload, length))
                throw Bad("A comment chunk fails its checksum.", payload - 8);
            ReadOnlySpan<byte> rest = chunk[keywordLength..];
            int textAt = payload + keywordLength;
            if (type == TextChunk)
            {
                Add(rest, limits, ref retained, textAt, found);
            }
            else if (type == CompressedTextChunk)
            {
                // The compression method, then a zlib stream.
                if (rest.IsEmpty || rest[0] != 0)
                    throw Bad("A zTXt chunk names a compression method other than deflate.", textAt);
                Add(Inflate(rest[1..], limits, retained, textAt + 1), limits, ref retained, textAt + 1, found);
            }
            else
            {
                // Whether the text is compressed, the compression method, a language tag and the keyword
                // in that language, each ended by a zero, then the text.
                if (rest.Length < 2 || rest[0] > 1 || (rest[0] == 1 && rest[1] != 0))
                    throw Bad("An iTXt chunk has no compression fields or names a method other than deflate.", textAt);
                bool compressed = rest[0] == 1;
                rest = rest[2..];
                textAt += 2;
                for (int field = 0; field < 2; field++)
                {
                    int zero = rest.IndexOf((byte)0);
                    if (zero < 0)
                        throw Bad("An iTXt chunk lacks its language tag or its translated keyword.", textAt);
                    rest = rest[(zero + 1)..];
                    textAt += zero + 1;
                }
                if (compressed) Add(Inflate(rest, limits, retained, textAt), limits, ref retained, textAt, found);
                else Add(rest, limits, ref retained, textAt, found);
            }
        }
    }

    /// <summary>The text of a compressed chunk, inflated within what the budgets still allow.</summary>
    private static byte[] Inflate(ReadOnlySpan<byte> stream, MetadataLimits limits, long retained, long offset)
    {
        long room = Math.Min(limits.MaxValueBytes, limits.MaxRetainedMetadataBytes - retained);
        try
        {
            return BoundedZlib.Expand(stream.ToArray(), (int)Math.Max(room, 0), default);
        }
        catch (MetadataException failure) when (failure.Code == MetadataErrorCode.InvalidPng)
        {
            throw Bad("The compressed text of a comment chunk is not a whole zlib stream.", offset);
        }
    }
}
