using MetadataSharp.Core;

namespace MetadataSharp.Formats;

/// <summary>
/// One kind of metadata that <see cref="ImageMetadata"/> could not read, and why. The other kinds
/// of the same file are read all the same.
/// </summary>
public sealed class MetadataReadError
{
    internal MetadataReadError(MetadataFamily family, MetadataException exception)
    {
        Family = family;
        Exception = exception;
    }

    /// <summary>The kind of metadata that was not read.</summary>
    public MetadataFamily Family { get; }

    /// <summary>The refusal, as the reader of that kind raised it.</summary>
    public MetadataException Exception { get; }

    /// <summary>The code of the refusal.</summary>
    public MetadataErrorCode Code => Exception.Code;

    /// <summary>The kind, the code and the message, for a log line.</summary>
    public override string ToString() => Family + ": " + Code + ": " + Exception.Message;
}
