using System.Text;

namespace MetadataSharp.Core.Iptc;

/// <summary>An IIM record and dataset number, without a schema or prefix alias.</summary>
/// <param name="Record">The IIM record number.</param>
/// <param name="Dataset">The dataset number within the record.</param>
public readonly record struct IptcDatasetKey(byte Record, byte Dataset);
/// <summary>The packet's explicit encoding declaration, never a guessed code page.</summary>
public enum IptcCharacterSet
{
    /// <summary>The packet has no 1:90 dataset, or one that declares ASCII (ESC ( B), which says as much.</summary>
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
    /// <param name="bytes">The raw dataset value bytes.</param>
    /// <returns>An immutable value that owns a copy of the input.</returns>
    /// <exception cref="ArgumentException">The value is longer than 1,048,576 bytes.</exception>
    public static IptcValue FromRaw(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > 1_048_576) throw new ArgumentException("The value exceeds its default byte budget.", nameof(bytes));
        return new(bytes.ToArray());
    }
    /// <summary>The length of the value in bytes.</summary>
    public int ByteLength => bytes.Length;
    /// <summary>Copies the value to the start of the destination, which has to be at least <see cref="ByteLength"/> long.</summary>
    /// <param name="destination">The buffer that receives the raw bytes.</param>
    /// <exception cref="ArgumentException">The destination is shorter than <see cref="ByteLength"/>.</exception>
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

/// <summary>What an IPTC save does beside the stream itself.</summary>
public sealed class IptcWriteOptions
{
    /// <summary>
    /// Whether the IPTC digest of the Photoshop image resources (resource 0x0425, the MD5 of the
    /// stream) is recomputed. False by default: the digest the file has is left as it is, so that it
    /// no longer matches the edited stream, and a reader that follows the Metadata Working Group
    /// guidelines takes the IPTC as newer than the XMP and reads it. Set it when the XMP of the file
    /// is written to say the same as the IPTC; an edit without a change then still recomputes a
    /// digest that does not match the stream. A digest is never added to a file that has none, and
    /// is removed together with the stream, whatever this says.
    /// </summary>
    public bool UpdateDigest { get; init; }
}

internal static class IptcText
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);
}
