using System.Text;

namespace MetadataSharp.Core;

/// <summary>Display-time choices for interpreting EXIF values.</summary>
public sealed class ExifInterpretationOptions
{
    /// <summary>
    /// The character encoding used for the body of an EXIF UserComment marked <c>UNICODE</c> when
    /// the stored bytes are not UCS-2 in the TIFF byte order. This corresponds to Exiv2's <c>-n</c>
    /// option. A byte-order mark is honored when this is null; when an encoding is supplied, it is
    /// used as given. The bytes and their stored value are never changed.
    /// </summary>
    public Encoding? UserCommentEncoding { get; init; }
}
