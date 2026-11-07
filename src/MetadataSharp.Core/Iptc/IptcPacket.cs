using System.Buffers.Binary;
using System.Text;

namespace MetadataSharp.Core.Iptc;

/// <summary>A bounded raw IIM snapshot preserving occurrences and untouched framing.</summary>
public sealed class IptcPacket
{
    private readonly byte[] bytes;
    private readonly IptcLimits limits;
    private IptcPacket(byte[] bytes, IptcLimits limits, List<IptcDataset> datasets, IptcCharacterSet characterSet)
    { this.bytes = bytes; this.limits = limits; Datasets = datasets.AsReadOnly(); CharacterSet = characterSet; }
    public IptcCharacterSet CharacterSet { get; }
    public IReadOnlyList<IptcDataset> Datasets { get; }
    public int ByteLength => bytes.Length;
    public void CopyRawTo(Span<byte> destination)
    {
        if (destination.Length < bytes.Length) throw new ArgumentException("The destination is too small.", nameof(destination));
        bytes.CopyTo(destination);
    }
    internal static MetadataException Limit() => new(MetadataErrorCode.LimitExceeded, "IPTC exceeds a resource budget.");
    private static MetadataException Invalid() => new(MetadataErrorCode.InvalidIptc, "Malformed or truncated IIM framing.");
    internal static IptcLimits Settings(IptcReadOptions? options)
    {
        IptcLimits limits = options is null ? new() : options.Limits ?? throw new ArgumentException("Limits cannot be null.", nameof(options));
        limits.Validate(); return limits with { };
    }
    public static IptcPacket CreateEmpty(IptcReadOptions? options = null) => Parse([0x1c, 1, 90, 0, 3, 0x1b, 0x25, 0x47, 0x1c, 2, 0, 0, 2, 0, 4], options);
    public static IptcPacket Parse(ReadOnlySpan<byte> bytes, IptcReadOptions? options = null)
    {
        IptcLimits limits = Settings(options);
        if (bytes.Length > limits.MaxPacketBytes) throw Limit();
        return Read(bytes.ToArray(), limits);
    }
    private static IptcPacket Read(byte[] bytes, IptcLimits limits)
    {
        var datasets = new List<IptcDataset>(); int at = 0, charsetCount = 0, versionCount = 0;
        IptcCharacterSet charset = IptcCharacterSet.Unspecified;
        while (at < bytes.Length)
        {
            if (datasets.Count >= limits.MaxDatasets) throw Limit();
            int start = at;
            if (bytes.Length - at < 5 || bytes[at] != 0x1c) throw Invalid();
            var key = new IptcDatasetKey(bytes[at + 1], bytes[at + 2]);
            uint length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(at + 3)); at += 5;
            if ((length & 0x8000) != 0)
            {
                int width = (int)(length & 0x7fff);
                if (width == 0) throw Invalid();
                if (width > 4) throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "IIM length descriptors wider than four bytes are unsupported.");
                if (bytes.Length - at < width) throw Invalid();
                length = 0;
                for (int i = 0; i < width; i++) length = (length << 8) | bytes[at++];
            }
            if (length > limits.MaxValueBytes) throw Limit();
            if (length > bytes.Length - at) throw Invalid();
            var value = new IptcValue(bytes.AsMemory(at, (int)length)); at += (int)length;
            datasets.Add(new(key, value, bytes.AsMemory(start, at - start)));
            if (key == new IptcDatasetKey(1, 90))
            {
                if (++charsetCount > 1) throw new MetadataException(MetadataErrorCode.AmbiguousMetadata, "Duplicate IIM character set declarations.");
                charset = value.Span.SequenceEqual(new byte[] { 0x1b, 0x25, 0x47 }) ? IptcCharacterSet.Utf8 : IptcCharacterSet.Unsupported;
            }
            if (key == new IptcDatasetKey(2, 0) && ++versionCount > 1)
                throw new MetadataException(MetadataErrorCode.AmbiguousMetadata, "Duplicate IIM record version controls.");
        }
        return new(bytes, limits, datasets, charset);
    }
    public string GetText(int datasetIndex)
    {
        if ((uint)datasetIndex >= (uint)Datasets.Count) throw new ArgumentOutOfRangeException(nameof(datasetIndex));
        IptcDataset dataset = Datasets[datasetIndex];
        try { IptcText.Rule(dataset.Key); }
        catch (ArgumentException) { throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "The dataset is outside the text profile."); }
        if (CharacterSet == IptcCharacterSet.Unsupported || (CharacterSet == IptcCharacterSet.Unspecified && ContainsHighBit(dataset.Value.Span)))
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "The text encoding is unsupported or unspecified for non-ASCII data.");
        try { return IptcText.Utf8.GetString(dataset.Value.Span); }
        catch (DecoderFallbackException) { throw new MetadataException(MetadataErrorCode.InvalidIptc, "The dataset contains invalid UTF-8 text."); }
    }
    private static bool ContainsHighBit(ReadOnlySpan<byte> value)
    { foreach (byte b in value) if (b >= 0x80) return true; return false; }
    public IptcPacket? Apply(IptcEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var snapshot = edit.Snapshot(); if (snapshot.RemoveAll) return null;
        if (snapshot.Changes.Count == 0) return this;
        var frames = new List<ReadOnlyMemory<byte>>(); var emitted = new HashSet<IptcDatasetKey>();
        long size = 0; int count = 0; bool effective = false;
        void Add(ReadOnlyMemory<byte> frame)
        {
            if (++count > limits.MaxDatasets || (size += frame.Length) > limits.MaxPacketBytes) throw Limit();
            frames.Add(frame);
        }
        void Replace(IptcDatasetKey key, IptcEdit.Replacement replacement)
        {
            foreach (IptcValue value in replacement.Values)
            {
                if (value.ByteLength > limits.MaxValueBytes) throw Limit();
                if (replacement.Text && (CharacterSet == IptcCharacterSet.Unsupported || (CharacterSet == IptcCharacterSet.Unspecified && ContainsHighBit(value.Span))))
                    throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "A text edit would require an unapproved encoding conversion.");
                // Bound the frame before allocating it.
                int width = value.ByteLength <= 32767 ? 0 : value.ByteLength <= 65535 ? 2 : value.ByteLength <= 0xffffff ? 3 : 4;
                if (size + 5L + width + value.ByteLength > limits.MaxPacketBytes) throw Limit();
                byte[] frame = new byte[5 + width + value.ByteLength]; frame[0] = 0x1c; frame[1] = key.Record; frame[2] = key.Dataset;
                BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(3), width == 0 ? (ushort)value.ByteLength : (ushort)(0x8000 | width));
                for (int i = 0; i < width; i++) frame[5 + i] = (byte)((uint)value.ByteLength >> (8 * (width - i - 1)));
                value.Span.CopyTo(frame.AsSpan(5 + width)); Add(frame);
            }
        }
        foreach (IptcDataset dataset in Datasets)
        {
            if (!snapshot.Changes.TryGetValue(dataset.Key, out var replacement)) { Add(dataset.Framing); continue; }
            effective = true;
            if (emitted.Add(dataset.Key)) Replace(dataset.Key, replacement);
        }
        foreach (var pair in snapshot.Changes)
            if (emitted.Add(pair.Key) && pair.Value.Values.Length != 0) { effective = true; Replace(pair.Key, pair.Value); }
        if (!effective) return this;
        byte[] output = new byte[(int)size]; int offset = 0;
        foreach (var frame in frames) { frame.Span.CopyTo(output.AsSpan(offset)); offset += frame.Length; }
        return Read(output, limits);
    }
}
