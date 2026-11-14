using System.Text;

namespace MetadataSharp.Core;

/// <summary>Display-time choices for interpreting EXIF values.</summary>
public sealed class ExifInterpretationOptions
{
    private string? userCommentCharset;

    /// <summary>
    /// The character encoding used for the body of an EXIF UserComment marked <c>UNICODE</c> when
    /// the stored bytes are not UCS-2 in the TIFF byte order. This corresponds to Exiv2's <c>-n</c>
    /// option. A byte-order mark is honored when this is null; when an encoding is supplied, it is
    /// used as given. The bytes and their stored value are never changed.
    /// </summary>
    public Encoding? UserCommentEncoding { get; init; }

    /// <summary>
    /// The same choice as <see cref="UserCommentEncoding"/>, named as Exiv2's <c>-n</c> names it: a
    /// name of <see cref="MetadataCharsets"/>, <c>latin1</c> or <c>windows-1251</c>. The name is
    /// resolved on every interpretation, so a name the matrix does not hold is refused when the value
    /// is read rather than silently ignored. When both are given, the name is the one that counts.
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
