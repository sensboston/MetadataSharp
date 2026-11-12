namespace MetadataSharp.Core.Xmp;

/// <summary>Finite logical budgets for XMP input and output.</summary>
public sealed record XmpLimits
{
    /// <summary>The longest packet, in bytes. Default 1,048,576.</summary>
    public int MaxPacketBytes { get; init; } = 1_048_576;
    /// <summary>The most characters of XML read from one packet. Default 1,048,576.</summary>
    public int MaxXmlCharacters { get; init; } = 1_048_576;
    /// <summary>The deepest element nesting. Default 32.</summary>
    public int MaxDepth { get; init; } = 32;
    /// <summary>The most XML nodes in one packet. Default 16,384.</summary>
    public int MaxNodes { get; init; } = 16_384;
    /// <summary>The most attributes in one packet. Default 4,096.</summary>
    public int MaxAttributes { get; init; } = 4_096;
    /// <summary>The most top-level properties. Default 2,048.</summary>
    public int MaxProperties { get; init; } = 2_048;
    /// <summary>The most array items in one packet. Default 8,192.</summary>
    public int MaxArrayItems { get; init; } = 8_192;
    /// <summary>The longest name, prefix or namespace URI, in characters. Default 1,024.</summary>
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
    /// <summary>The budgets of the load.</summary>
    public XmpLimits Limits { get; init; } = new();
}
