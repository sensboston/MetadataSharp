using System.Text;

namespace MetadataSharp.Core.Iptc;

/// <summary>An IIM record and dataset number, without a schema or prefix alias.</summary>
public readonly record struct IptcDatasetKey(byte Record, byte Dataset);
/// <summary>The packet's explicit encoding declaration, never a guessed code page.</summary>
public enum IptcCharacterSet
{
    /// <summary>The packet has no 1:90 dataset.</summary>
    Unspecified,
    /// <summary>The packet declares UTF-8 (ESC % G).</summary>
    Utf8,
    /// <summary>The packet declares another character set; its text is not decoded.</summary>
    Unsupported
}

/// <summary>An immutable, independently owned raw value.</summary>
public sealed class IptcValue
{
    private readonly ReadOnlyMemory<byte> bytes;
    internal IptcValue(ReadOnlyMemory<byte> bytes) => this.bytes = bytes;
    /// <summary>Creates a value from a copy of the bytes.</summary>
    /// <exception cref="ArgumentException">The value is longer than 1,048,576 bytes.</exception>
    public static IptcValue FromRaw(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > 1_048_576) throw new ArgumentException("The value exceeds its default byte budget.", nameof(bytes));
        return new(bytes.ToArray());
    }
    /// <summary>The length of the value in bytes.</summary>
    public int ByteLength => bytes.Length;
    /// <summary>Copies the value to the start of the destination, which has to be at least <see cref="ByteLength"/> long.</summary>
    public void CopyRawTo(Span<byte> destination)
    {
        if (destination.Length < bytes.Length) throw new ArgumentException("The destination is too small.", nameof(destination));
        bytes.Span.CopyTo(destination);
    }
    internal ReadOnlySpan<byte> Span => bytes.Span;
}

/// <summary>An owned dataset in its original occurrence order.</summary>
public sealed class IptcDataset
{
    internal IptcDataset(IptcDatasetKey key, IptcValue value, ReadOnlyMemory<byte> framing)
    { Key = key; Value = value; Framing = framing; }
    /// <summary>The record and dataset number.</summary>
    public IptcDatasetKey Key { get; }
    /// <summary>The raw value.</summary>
    public IptcValue Value { get; }
    internal ReadOnlyMemory<byte> Framing { get; }
}

/// <summary>Finite logical budgets for IIM packets and Photoshop resource framing.</summary>
public sealed record IptcLimits
{
    /// <summary>The longest IPTC stream, in bytes. Default 1,048,576.</summary>
    public int MaxPacketBytes { get; init; } = 1_048_576;
    /// <summary>The most datasets in one stream. Default 4,096.</summary>
    public int MaxDatasets { get; init; } = 4_096;
    /// <summary>The longest value of one dataset, in bytes. Default 1,048,576.</summary>
    public int MaxValueBytes { get; init; } = 1_048_576;
    /// <summary>The most Photoshop image resources read to find the stream. Default 1,024.</summary>
    public int MaxResources { get; init; } = 1_024;
    internal void Validate()
    {
        if (MaxPacketBytes <= 0 || MaxDatasets <= 0 || MaxValueBytes <= 0 || MaxResources <= 0)
            throw new ArgumentOutOfRangeException(nameof(IptcLimits), "IPTC limits must be positive.");
    }
}
/// <summary>Options captured by each IPTC read.</summary>
public sealed class IptcReadOptions
{
    /// <summary>The budgets of the read.</summary>
    public IptcLimits Limits { get; init; } = new();
}

internal static class IptcText
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);
}
