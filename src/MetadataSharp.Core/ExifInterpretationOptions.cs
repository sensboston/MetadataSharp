using System.Text;

namespace MetadataSharp.Core;

/// <summary>Display-time choices for interpreting EXIF values.</summary>
public sealed class ExifInterpretationOptions
{
    private string? userCommentCharset;

    /// <summary>
    /// The character encoding of the body of an EXIF UserComment marked <c>UNICODE</c>, for a file
    /// whose bytes are not UCS-2 in the TIFF byte order. A byte-order mark is honored when this is
    /// null, which is what the reference tool's own print does (<c>detectCharset</c> in
    /// <c>src/value.cpp</c>); when an encoding is supplied it is used as given, which is the
    /// statement Exiv2's <c>-n</c> option makes for its <c>fc</c> action. The bytes and their stored
    /// value are never changed.
    /// </summary>
    public Encoding? UserCommentEncoding { get; init; }

    /// <summary>
    /// The same choice as <see cref="UserCommentEncoding"/>, named the way Exiv2's <c>-n</c> names a
    /// character set: a name of <see cref="MetadataCharsets"/>, <c>latin1</c> or <c>windows-1251</c>.
    /// The name is resolved when the options are constructed, so a name the matrix does not hold is
    /// refused there rather than silently ignored. When both are given, the name is the one that
    /// counts.
    /// </summary>
    /// <exception cref="ArgumentException">The name is not a character set of <see cref="MetadataCharsets"/>.</exception>
    public string? UserCommentCharset
    {
        get => userCommentCharset;
        init
        {
            if (value is not null)
                MetadataCharsets.Get(value);
            userCommentCharset = value;
        }
    }
}
