namespace MetadataSharp.Core.Xmp;

/// <summary>Finite logical budgets for XMP input and output.</summary>
public sealed record XmpLimits
{
    public int MaxPacketBytes { get; init; } = 1_048_576;
    public int MaxXmlCharacters { get; init; } = 1_048_576;
    public int MaxDepth { get; init; } = 32;
    public int MaxNodes { get; init; } = 16_384;
    public int MaxAttributes { get; init; } = 4_096;
    public int MaxProperties { get; init; } = 2_048;
    public int MaxArrayItems { get; init; } = 8_192;
    public int MaxNameCharacters { get; init; } = 1_024;

    internal void Validate()
    {
        if (MaxPacketBytes <= 0 || MaxXmlCharacters <= 0 || MaxDepth <= 0 || MaxNodes <= 0 ||
            MaxAttributes <= 0 || MaxProperties <= 0 || MaxArrayItems <= 0 || MaxNameCharacters <= 0)
            throw new ArgumentOutOfRangeException(nameof(XmpLimits), "XMP limits must be positive.");
    }
}

/// <summary>Options captured by each packet load.</summary>
public sealed class XmpReadOptions
{
    public XmpLimits Limits { get; init; } = new();
}
