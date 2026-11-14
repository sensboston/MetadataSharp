using System.Buffers.Binary;
using System.Text;

namespace MetadataSharp.Core.Iptc;

/// <summary>A bounded raw IIM snapshot preserving occurrences and untouched framing.</summary>
public sealed partial class IptcPacket
{
    private readonly byte[] bytes;
    private readonly IptcLimits limits;
    private readonly MetadataException? refusal;
    private IptcPacket(byte[] bytes, IptcLimits limits, List<IptcDataset> datasets, IptcCharacterSet characterSet, MetadataException? refusal)
    {
        this.bytes = bytes; this.limits = limits; Datasets = datasets.AsReadOnly(); CharacterSet = characterSet;
        DetectedTextEncoding = DetectTextEncoding(datasets, characterSet); this.refusal = refusal;
    }
    /// <summary>The character set the stream declares in dataset 1:90, not an inferred encoding.</summary>
    public IptcCharacterSet CharacterSet { get; }
    /// <summary>The encoding selected by the content-aware Exiv2-compatible display policy.</summary>
    public IptcTextEncoding DetectedTextEncoding { get; }
    /// <summary>
    /// False when a container returned the stream for reading only: the stream itself, or the
    /// resources that hold it, has a form the editable profile refuses. <see cref="Apply"/> then
    /// refuses a change, and so does the container's save, with the error the strict read gave.
    /// </summary>
    public bool IsEditable => refusal is null;
    /// <summary>
    /// What the digest the file keeps beside the stream (Photoshop resource 0x0425) said when a
    /// document read this stream: that IPTC is as it was when XMP was last brought into agreement, or
    /// that it was changed since. <see cref="Sync.IptcDigestState.Absent"/> for a file without a
    /// digest, and for a stream that was parsed from bytes or returned by <see cref="Apply"/>.
    /// </summary>
    public Sync.IptcDigestState Digest { get; private set; }
    /// <summary>Records what the stored digest says; null when the container holds none. Called once, by the reader, before the packet is handed out.</summary>
    internal IptcPacket WithDigest(byte[]? stored)
    {
        Digest = stored is null ? Sync.IptcDigestState.Absent
            : stored.AsSpan().SequenceEqual(System.Security.Cryptography.MD5.HashData(bytes)) ? Sync.IptcDigestState.Current : Sync.IptcDigestState.Stale;
        return this;
    }
    /// <summary>The datasets, in the order they occur.</summary>
    public IReadOnlyList<IptcDataset> Datasets { get; }
    /// <summary>The length of the stream in bytes.</summary>
    public int ByteLength => bytes.Length;
    /// <summary>Copies the stream to the start of the destination, which has to be at least <see cref="ByteLength"/> long.</summary>
    /// <param name="destination">The buffer that receives the complete IPTC stream.</param>
    /// <exception cref="ArgumentException">The destination is shorter than <see cref="ByteLength"/>.</exception>
    public void CopyRawTo(Span<byte> destination)
    {
        if (destination.Length < bytes.Length) throw new ArgumentException("The destination is too small.", nameof(destination));
        bytes.CopyTo(destination);
    }
    // Use one stable error for every packet, dataset-count, value, and aggregate write budget.
    internal static MetadataException Limit() => new(MetadataErrorCode.LimitExceeded, "IPTC exceeds a resource budget.");
    private static MetadataException Invalid() => new(MetadataErrorCode.InvalidIptc, "Malformed or truncated IIM framing.");
    // Snapshot and validate options so later caller mutations cannot change this packet's read budget.
    internal static IptcLimits Settings(IptcReadOptions? options)
    {
        IptcLimits limits = options is null ? new() : options.Limits ?? throw new ArgumentException("Limits cannot be null.", nameof(options));
        limits.Validate(); return limits with { };
    }
    /// <summary>Creates a stream that declares UTF-8 and record version 4 and holds no other dataset.</summary>
    /// <param name="options">The limits for the new packet; null uses the defaults.</param>
    /// <returns>An empty packet with the required character-set and record-version declarations.</returns>
    /// <exception cref="ArgumentException">The options contain null or invalid limits.</exception>
    /// <exception cref="MetadataException"><see cref="MetadataErrorCode.LimitExceeded"/> if the configured packet limit is smaller than the required declarations.</exception>
    public static IptcPacket CreateEmpty(IptcReadOptions? options = null) => Parse([0x1c, 1, 90, 0, 3, 0x1b, 0x25, 0x47, 0x1c, 2, 0, 0, 2, 0, 4], options);
    /// <summary>Reads an IPTC stream from a copy of the bytes.</summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidIptc"/> for malformed or truncated framing;
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when the stream exceeds a budget;
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> for a length descriptor wider than four bytes.
    /// </exception>
    /// <param name="bytes">The encoded IPTC IIM stream.</param>
    /// <param name="options">The parsing limits; null uses the defaults.</param>
    /// <returns>An immutable packet that owns a copy of the stream.</returns>
    /// <exception cref="ArgumentException">The options contain null or invalid limits.</exception>
    public static IptcPacket Parse(ReadOnlySpan<byte> bytes, IptcReadOptions? options = null)
    {
        IptcLimits limits = Settings(options);
        if (bytes.Length > limits.MaxPacketBytes) throw Limit();
        return Read(bytes.ToArray(), limits, null);
    }
    /// <summary>
    /// Reads a stream for reading only. The strict rules of <see cref="Parse"/> run first. A stream
    /// they refuse as malformed or ambiguous is read again under two wider rules: zero bytes after the
    /// last dataset are padding, and a character set or record version that is repeated with the same
    /// value is not ambiguous. The result is then not editable. <paramref name="container"/> is the
    /// error of a container that could read the stream and could not edit it; it too makes the result
    /// not editable.
    /// </summary>
    internal static IptcPacket Inspect(ReadOnlySpan<byte> bytes, IptcReadOptions? options = null, MetadataException? container = null)
    {
        IptcLimits limits = Settings(options);
        if (bytes.Length > limits.MaxPacketBytes) throw Limit();
        byte[] owned = bytes.ToArray();
        if (container is null)
        {
            try { return Read(owned, limits, null); }
            catch (MetadataException failure) when (failure.Code is MetadataErrorCode.InvalidIptc or MetadataErrorCode.AmbiguousMetadata) { container = failure; }
        }
        return Read(owned, limits, container);
    }
    // Walk each length frame before slicing; the optional relaxed pass preserves refused streams without making them editable.
    private static IptcPacket Read(byte[] bytes, IptcLimits limits, MetadataException? refusal)
    {
        var datasets = new List<IptcDataset>(); int at = 0, charsetCount = 0, versionCount = 0;
        IptcCharacterSet charset = IptcCharacterSet.Unspecified;
        bool readOnly = refusal is not null; IptcValue? firstCharset = null, firstVersion = null;
        while (at < bytes.Length)
        {
            if (datasets.Count >= limits.MaxDatasets) throw Limit();
            int start = at;
            // Read-only: zero bytes from the end of the last dataset to the end of the stream are padding.
            if (readOnly && datasets.Count != 0 && bytes.AsSpan(at).IndexOfAnyExcept((byte)0) < 0) break;
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
                // Read-only: a repeat that says the same thing as the first declaration is not ambiguous.
                if (++charsetCount > 1 && !(readOnly && value.Span.SequenceEqual(firstCharset!.Span)))
                    throw new MetadataException(MetadataErrorCode.AmbiguousMetadata, "Duplicate IIM character set declarations.");
                firstCharset ??= value;
                // ESC ( B designates ASCII: it says of the bytes what a stream without a declaration says.
                charset = value.Span.SequenceEqual(new byte[] { 0x1b, 0x25, 0x47 }) ? IptcCharacterSet.Utf8
                    : value.Span.SequenceEqual(new byte[] { 0x1b, 0x28, 0x42 }) ? IptcCharacterSet.Unspecified : IptcCharacterSet.Unsupported;
            }
            if (key == new IptcDatasetKey(2, 0))
            {
                if (++versionCount > 1 && !(readOnly && value.Span.SequenceEqual(firstVersion!.Span)))
                    throw new MetadataException(MetadataErrorCode.AmbiguousMetadata, "Duplicate IIM record version controls.");
                firstVersion ??= value;
            }
        }
        return new(bytes, limits, datasets, charset, refusal);
    }
    /// <summary>Decodes the dataset at an index of <see cref="Datasets"/> as text.</summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> when the dataset is not one IIM 4.2 defines
    /// as text in records 1 and 2 (see <see cref="IptcDatasets"/>); <see cref="MetadataErrorCode.InvalidIptc"/>
    /// when a packet declares UTF-8 but the dataset is not valid UTF-8. Otherwise the packet-wide
    /// detection selects ASCII, UTF-8, or ISO-8859-1 fallback, matching Exiv2's text conversion policy.
    /// </exception>
    /// <param name="datasetIndex">The zero-based index in <see cref="Datasets"/>.</param>
    /// <returns>The text decoded with the packet's detected ASCII, UTF-8, or ISO-8859-1 fallback encoding.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="datasetIndex"/> is outside the dataset list.</exception>
    public string GetText(int datasetIndex)
    {
        if ((uint)datasetIndex >= (uint)Datasets.Count) throw new ArgumentOutOfRangeException(nameof(datasetIndex));
        IptcDataset dataset = Datasets[datasetIndex];
        if (!IsText(dataset.Key))
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "The dataset is not a text dataset of IIM records 1 and 2.");
        return DecodeText(dataset);
    }

    // Decode a dataset using the packet-wide Exiv2-compatible charset decision.
    private string DecodeText(IptcDataset dataset)
    {
        try
        {
            return DetectedTextEncoding switch
            {
                IptcTextEncoding.Ascii => Encoding.ASCII.GetString(dataset.Value.Span),
                IptcTextEncoding.Utf8 => IptcText.Utf8.GetString(dataset.Value.Span),
                _ => Encoding.Latin1.GetString(dataset.Value.Span),
            };
        }
        catch (DecoderFallbackException) { throw new MetadataException(MetadataErrorCode.InvalidIptc, "The packet declares UTF-8 but this dataset contains invalid UTF-8 bytes."); }
    }

    // Match Exiv2's packet-wide charset selection: declared UTF-8 wins; otherwise valid UTF-8 is detected,
    // and byte sequences that are not UTF-8 use the ISO-8859-1 conversion fallback.
    private static IptcTextEncoding DetectTextEncoding(IReadOnlyList<IptcDataset> datasets, IptcCharacterSet declared)
    {
        if (declared == IptcCharacterSet.Utf8) return IptcTextEncoding.Utf8;
        bool ascii = true, validUtf8 = true;
        foreach (IptcDataset dataset in datasets)
        {
            ReadOnlySpan<byte> value = dataset.Value.Span;
            if (ascii && ContainsHighBit(value)) ascii = false;
            if (validUtf8)
            {
                try { _ = IptcText.Utf8.GetString(value); }
                catch (DecoderFallbackException) { validUtf8 = false; }
            }
            if (!ascii && !validUtf8) return IptcTextEncoding.Latin1Fallback;
        }
        if (ascii) return IptcTextEncoding.Ascii;
        return validUtf8 ? IptcTextEncoding.Utf8 : IptcTextEncoding.Latin1Fallback;
    }

    private static bool IsText(IptcDatasetKey key) =>
        IptcDatasets.TryGet(key, out IptcDatasetDefinition? definition) && !definition.IsManaged
        && definition.Rule is not (IptcRule.UInt16 or IptcRule.Binary);
    private static bool ContainsHighBit(ReadOnlySpan<byte> value)
    { foreach (byte b in value) if (b >= 0x80) return true; return false; }
    private readonly record struct Item(IptcDatasetKey Key, ReadOnlyMemory<byte> Frame, IptcValue Value, bool Kept);
    /// <summary>
    /// Returns the stream with the edit applied: null when the edit removes all IPTC, this stream
    /// when the edit changes no byte.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Order. A dataset the edit does not name keeps its bytes, framing included, and its place; so
    /// does every dataset of a record other than 1 and 2. The occurrences of a dataset the edit names
    /// are written together where the first of them stood; an occurrence the edit did not replace
    /// keeps its bytes. A dataset the stream did not have is written after the last dataset of its
    /// record or of a lower one, so that record 1 stays before record 2 and both before object data.
    /// </para>
    /// <para>
    /// Character set. Text is written as UTF-8. The declaration of the stream (1:90) is kept as it
    /// is, with one exception: a stream that declares nothing, or declares ASCII (ESC ( B), and is
    /// given text beyond ASCII gains the UTF-8 declaration. That is refused, with <see cref="MetadataErrorCode.UnsupportedFeature"/>,
    /// when a text dataset that stays in the stream holds bytes above 0x7F - they were written in an
    /// encoding nobody declared, and the new declaration would rename them. A text write to a stream
    /// that declares another character set is refused with the same code. Raw writes are not text.
    /// </para>
    /// </remarks>
    /// <exception cref="MetadataException">
    /// The stream is not editable; <see cref="MetadataErrorCode.InvalidIptcEdit"/> for an index past
    /// the occurrences of a dataset; <see cref="MetadataErrorCode.UnsupportedFeature"/> for the
    /// character set refusals; <see cref="MetadataErrorCode.LimitExceeded"/> when the result exceeds a budget.
    /// </exception>
    /// <param name="edit">The ordered dataset operations to apply.</param>
    /// <returns><see langword="null"/> when the edit removes the stream; otherwise, the resulting packet.</returns>
    public IptcPacket? Apply(IptcEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (edit.RemovesAll) return null;
        IReadOnlyList<IptcEdit.Operation> operations = edit.Operations;
        if (operations.Count == 0) return this;
        if (refusal is not null) throw new MetadataException(refusal.Code, refusal.Message);

        var lists = new Dictionary<IptcDatasetKey, List<Item>>(); var order = new List<IptcDatasetKey>();
        bool text = false, wide = false;
        foreach (IptcEdit.Operation operation in operations)
        {
            if (!lists.TryGetValue(operation.Key, out List<Item>? list))
            {
                list = [];
                foreach (IptcDataset dataset in Datasets)
                    if (dataset.Key == operation.Key) list.Add(new(dataset.Key, dataset.Framing, dataset.Value, true));
                lists.Add(operation.Key, list); order.Add(operation.Key);
            }
            switch (operation.Kind)
            {
                case IptcEdit.OperationKind.ReplaceAll:
                    list.Clear();
                    foreach (IptcValue value in operation.Values) list.Add(Frame(operation.Key, value));
                    break;
                case IptcEdit.OperationKind.Append:
                    list.Add(Frame(operation.Key, operation.Values[0]));
                    break;
                case IptcEdit.OperationKind.Insert:
                    if (operation.Index > list.Count) throw Occurrence(operation, list.Count);
                    list.Insert(operation.Index, Frame(operation.Key, operation.Values[0]));
                    break;
                case IptcEdit.OperationKind.SetFirst:
                    if (list.Count == 0) list.Add(Frame(operation.Key, operation.Values[0]));
                    else list[0] = Frame(operation.Key, operation.Values[0]);
                    break;
                default:
                    if (operation.Index >= list.Count) throw Occurrence(operation, list.Count);
                    list.RemoveAt(operation.Index);
                    break;
            }
            if (list.Count > limits.MaxDatasets) throw Limit();
            if (!operation.Text) continue;
            foreach (IptcValue value in operation.Values) { text = true; wide |= ContainsHighBit(value.Span); }
        }

        var output = new List<Item>(); var emitted = new HashSet<IptcDatasetKey>();
        foreach (IptcDataset dataset in Datasets)
        {
            if (!lists.TryGetValue(dataset.Key, out List<Item>? list)) output.Add(new(dataset.Key, dataset.Framing, dataset.Value, true));
            else if (emitted.Add(dataset.Key)) output.AddRange(list);
        }
        foreach (IptcDatasetKey key in order)
            if (emitted.Add(key)) output.InsertRange(EndOfRecord(output, key.Record), lists[key]);

        if (text && CharacterSet == IptcCharacterSet.Unsupported)
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "A text edit would require an unapproved encoding conversion.");
        if (text && wide && CharacterSet == IptcCharacterSet.Unspecified)
        {
            foreach (Item item in output)
            {
                if (item.Kept && IsText(item.Key) && ContainsHighBit(item.Value.Span))
                    throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                        "The stream declares no character set and holds text beyond ASCII; UTF-8 text would mix two encodings.");
            }
            Item declaration = Frame(new(1, 90), new IptcValue(new byte[] { 0x1b, 0x25, 0x47 }));
            // A declaration of ASCII (ESC ( B) is replaced where it stands; a stream without one gains it.
            int declared = output.FindIndex(item => item.Key == new IptcDatasetKey(1, 90));
            if (declared >= 0) output[declared] = declaration; else output.Insert(EndOfRecord(output, 1), declaration);
        }

        long size = 0;
        foreach (Item item in output) size += item.Frame.Length;
        if (output.Count > limits.MaxDatasets || size > limits.MaxPacketBytes) throw Limit();
        byte[] result = new byte[(int)size]; int offset = 0;
        foreach (Item item in output) { item.Frame.Span.CopyTo(result.AsSpan(offset)); offset += item.Frame.Length; }
        return result.AsSpan().SequenceEqual(bytes) ? this : Read(result, limits, null);
    }
    /// <summary>The index after the last dataset of the record or of a lower one.</summary>
    private static int EndOfRecord(List<Item> items, byte record)
    {
        for (int i = items.Count - 1; i >= 0; i--)
            if (items[i].Key.Record <= record) return i + 1;
        return 0;
    }
    private static MetadataException Occurrence(IptcEdit.Operation operation, int count) =>
        new(MetadataErrorCode.InvalidIptcEdit,
            "Dataset " + operation.Key.Record + ":" + operation.Key.Dataset + " has " + count + " occurrences; index " + operation.Index + " is past them.");
    // Frame new values in canonical IIM length form while untouched datasets keep their original bytes.
    private Item Frame(IptcDatasetKey key, IptcValue value)
    {
        if (value.ByteLength > limits.MaxValueBytes || 9L + value.ByteLength > limits.MaxPacketBytes) throw Limit();
        int width = value.ByteLength <= 32767 ? 0 : value.ByteLength <= 65535 ? 2 : value.ByteLength <= 0xffffff ? 3 : 4;
        byte[] frame = new byte[5 + width + value.ByteLength]; frame[0] = 0x1c; frame[1] = key.Record; frame[2] = key.Dataset;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(3), width == 0 ? (ushort)value.ByteLength : (ushort)(0x8000 | width));
        for (int i = 0; i < width; i++) frame[5 + i] = (byte)((uint)value.ByteLength >> (8 * (width - i - 1)));
        value.Span.CopyTo(frame.AsSpan(5 + width)); return new(key, frame, value, false);
    }
}
