using System.Buffers;
using System.Collections.ObjectModel;

namespace MetadataSharp.Core.Xmp;

/// <summary>An owned UTF-8 XMP snapshot. Unsupported RDF can be copied but not rewritten.</summary>
/// <remarks>No concurrent use of borrowed streams or mutable edits is supported.</remarks>
public sealed partial class XmpPacket
{
    private readonly byte[] bytes;
    private readonly XmpLimits limits;
    private readonly IReadOnlyDictionary<XmpPropertyName, XmpValue> properties;
    private XmpPacket(byte[] bytes, XmpLimits limits, XmpTree? tree, Dictionary<XmpPropertyName, XmpValue> properties, bool hasExtendedReference, IReadOnlyList<XmpEntry> entries)
    { HasExtendedReference = hasExtendedReference; this.bytes = bytes; this.limits = limits; Tree = tree; this.properties = new ReadOnlyDictionary<XmpPropertyName, XmpValue>(properties); Entries = entries; }
    internal bool HasExtendedReference { get; }
    /// <summary>The packet's tree when <see cref="IsEditable"/>, else null. It is shared and never changed: an edit works on a copy.</summary>
    internal XmpTree? Tree { get; }
    /// <summary>The budgets the packet was read under.</summary>
    internal XmpLimits Limits => limits;
    /// <summary>
    /// True when the packet can be written again without loss, which is what an edit needs: every
    /// property is RDF in a form XMP defines (simple values, arrays, structures, qualifiers, at any
    /// depth, in any namespace). False for RDF outside those forms, a property that occurs twice,
    /// node identifiers and datatypes, descriptions about different subjects, content beside the RDF
    /// and a packet marked read-only; such a packet can be copied and listed through
    /// <see cref="Entries"/>, and not changed.
    /// </summary>
    public bool IsEditable => Tree is not null;
    /// <summary>The length of the packet in bytes.</summary>
    public int ByteLength => bytes.Length;
    /// <summary>
    /// The top-level properties of an editable packet that are text, an array of texts or a language
    /// alternative, by expanded name. A structure, an array of structures and a qualified value are
    /// not among them; <see cref="Entries"/> lists everything.
    /// </summary>
    /// <exception cref="MetadataException"><see cref="MetadataErrorCode.UnsupportedFeature"/> when <see cref="IsEditable"/> is false.</exception>
    public IReadOnlyDictionary<XmpPropertyName, XmpValue> Properties => IsEditable ? properties : throw XmpCodec.Unsupported();
    /// <summary>
    /// Every property of the packet as a flat, read-only list in packet order, whether or not the packet
    /// is editable: structures, arrays of structures and qualifiers are reported under path keys, in
    /// both the element and the attribute form of RDF. A property whose RDF is not a form XMP defines
    /// has no entries.
    /// </summary>
    public IReadOnlyList<XmpEntry> Entries { get; }
    /// <summary>Copies the packet to the start of the destination, which has to be at least <see cref="ByteLength"/> long.</summary>
    public void CopyRawTo(Span<byte> destination)
    {
        if (destination.Length < bytes.Length) throw new ArgumentException("Destination is too small.", nameof(destination));
        bytes.CopyTo(destination);
    }
    private static XmpLimits Settings(XmpReadOptions? options)
    {
        XmpLimits value = options is null ? new() : options.Limits ?? throw new ArgumentException("Limits cannot be null.", nameof(options));
        value.Validate(); return value with { };
    }
    /// <summary>Creates a packet without properties.</summary>
    public static XmpPacket CreateEmpty(XmpReadOptions? options = null)
    {
        XmpLimits limits = Settings(options);
        return ParseOwned(XmpWriter.Write(new XmpTree(), limits), limits, default);
    }
    /// <summary>Reads a packet from a copy of the bytes.</summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidXmp"/> when the bytes are not a packet;
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when the packet exceeds a budget.
    /// </exception>
    public static XmpPacket Parse(ReadOnlySpan<byte> bytes, XmpReadOptions? options = null)
    {
        XmpLimits limits = Settings(options);
        if (bytes.Length > limits.MaxPacketBytes) throw XmpCodec.Limit();
        return ParseOwned(bytes.ToArray(), limits, default);
    }
    private static XmpPacket ParseOwned(byte[] bytes, XmpLimits limits, CancellationToken token)
    {
        var result = XmpCodec.Read(bytes, limits, token);
        token.ThrowIfCancellationRequested();
        return new(bytes, limits, result.Tree, result.Properties, result.HasExtendedReference, result.Entries);
    }
    /// <summary>Reads a packet, for example a sidecar file, from the stream to its end and leaves the stream open. Fails as <see cref="Parse"/> does.</summary>
    public static XmpPacket Load(Stream source, XmpReadOptions? options = null) => LoadCore(source, Settings(options), false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="Load(Stream, XmpReadOptions?)"/>
    public static Task<XmpPacket> LoadAsync(Stream source, XmpReadOptions? options = null, CancellationToken cancellationToken = default) => LoadCore(source, Settings(options), true, cancellationToken);
    /// <summary>Reads a packet from a file, for example an <c>.xmp</c> sidecar. Fails as <see cref="Parse"/> does.</summary>
    public static XmpPacket Load(string path, XmpReadOptions? options = null) => LoadPath(path, Settings(options), false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="Load(string, XmpReadOptions?)"/>
    public static Task<XmpPacket> LoadAsync(string path, XmpReadOptions? options = null, CancellationToken cancellationToken = default) => LoadPath(path, Settings(options), true, cancellationToken);
    private static async Task<XmpPacket> LoadPath(string path, XmpLimits limits, bool asynchronous, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrEmpty(path); token.ThrowIfCancellationRequested();
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192,
            FileOptions.SequentialScan | (asynchronous ? FileOptions.Asynchronous : FileOptions.None));
        return await LoadCore(source, limits, asynchronous, token).ConfigureAwait(false);
    }
    private static async Task<XmpPacket> LoadCore(Stream source, XmpLimits limits, bool asynchronous, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("Source must be readable.", nameof(source));
        token.ThrowIfCancellationRequested();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            using var output = new MemoryStream();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                // Read at most one excess byte to distinguish exact-budget EOF from overrun.
                int request = (int)Math.Min(8192L, (long)limits.MaxPacketBytes - output.Length + 1);
                int count = asynchronous ? await source.ReadAsync(buffer.AsMemory(0, request), token).ConfigureAwait(false) : source.Read(buffer, 0, request);
                if (count == 0) break;
                if (output.Length + count > limits.MaxPacketBytes) throw XmpCodec.Limit();
                output.Write(buffer, 0, count);
            }
            return ParseOwned(output.ToArray(), limits, token);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
    /// <summary>
    /// Returns the packet with the edit applied: null when the edit removes all XMP, this packet when
    /// the edit changes nothing, otherwise a packet written anew. The new packet holds every property
    /// the edit did not name with its value, structure and prefix, in one canonical RDF form; it is
    /// read back and refused unless it holds exactly what was meant.
    /// </summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> when <see cref="IsEditable"/> is false;
    /// <see cref="MetadataErrorCode.InvalidXmpPath"/> when a change does not apply to the packet;
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when the result exceeds a budget.
    /// </exception>
    public XmpPacket? Apply(XmpEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return Apply(edit.Snapshot(), 0, 0);
    }

    /// <summary>
    /// <see cref="Apply(XmpEdit)"/> for a container that keeps a packet in a slot. A result that is
    /// no longer than <paramref name="slotBytes"/> is padded to exactly that length, so that it is
    /// written over the old packet; a longer one is given <paramref name="paddingBytes"/> of padding,
    /// so that the next edit finds room. The edit is a snapshot the caller owns.
    /// </summary>
    internal XmpPacket? Apply(XmpEdit edit, int slotBytes, int paddingBytes)
    {
        if (edit.RemovesAll) return null;
        if (edit.IsEmpty) return this;
        // A replacement needs nothing of this packet, so it also replaces one that cannot be edited.
        if (Tree is null && edit.Replacement is null) throw XmpCodec.Unsupported();
        XmpTree changed = (edit.Replacement ?? Tree!).Clone();
        XmpEditor.Apply(changed, edit);
        if (Tree is not null && XmpTree.Same(Tree, changed)) return this;
        return FromTree(changed, limits, edit.Namespaces, slotBytes, paddingBytes);
    }

    /// <summary>
    /// The packet of a tree, read back and refused unless it reads as that tree. Padding is left out
    /// when it would exceed the packet budget.
    /// </summary>
    internal static XmpPacket FromTree(XmpTree tree, XmpLimits limits, IReadOnlyDictionary<string, string>? registered = null,
        int slotBytes = 0, int paddingBytes = 0, bool wrapper = true)
    {
        byte[] output = XmpWriter.Write(tree, limits, registered, wrapper);
        if (wrapper)
        {
            long padded = output.Length <= slotBytes ? slotBytes : (long)output.Length + paddingBytes;
            if (padded <= limits.MaxPacketBytes) output = XmpWriter.Pad(output, (int)padded);
        }
        XmpPacket packet = ParseOwned(output, limits, default);
        if (packet.Tree is null || !XmpTree.Same(packet.Tree, tree))
            throw new MetadataException(MetadataErrorCode.UnsafeRelocation, "The XMP packet as written does not read back as the edit; nothing was written.");
        return packet;
    }
    /// <summary>Writes the packet to a writable, seekable, empty stream.</summary>
    /// <exception cref="ArgumentException">The destination is not such a stream.</exception>
    public void Save(Stream destination) => SaveCore(destination, false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="Save(Stream)"/>
    public Task SaveAsync(Stream destination, CancellationToken cancellationToken = default) => SaveCore(destination, true, cancellationToken);
    private async Task SaveCore(Stream destination, bool asynchronous, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite || !destination.CanSeek || destination.Length != 0 || destination.Position != 0)
            throw new ArgumentException("Destination must be writable, seekable, empty and at zero.", nameof(destination));
        token.ThrowIfCancellationRequested();
        for (int offset = 0; offset < bytes.Length;)
        {
            token.ThrowIfCancellationRequested();
            int count = Math.Min(8192, bytes.Length - offset);
            if (asynchronous) await destination.WriteAsync(bytes.AsMemory(offset, count), token).ConfigureAwait(false);
            else destination.Write(bytes, offset, count);
            offset += count;
        }
    }
    /// <summary>Writes the packet to a new file; the file appears there complete or not at all.</summary>
    /// <exception cref="IOException">The destination exists.</exception>
    public void SaveAs(string destinationPath) => SavePath(destinationPath, false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveAs(string)"/>
    public Task SaveAsAsync(string destinationPath, CancellationToken cancellationToken = default) => SavePath(destinationPath, true, cancellationToken);
    private async Task SavePath(string path, bool asynchronous, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrEmpty(path); token.ThrowIfCancellationRequested();
        string destination = Path.GetFullPath(path);
        if (File.Exists(destination)) throw new IOException("The destination already exists.");
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".metadatasharp-xmp-" + Guid.NewGuid().ToString("N") + ".tmp");
        bool ownsTemporary = false;
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192,
                asynchronous ? FileOptions.Asynchronous : FileOptions.None))
            {
                ownsTemporary = true;
                await SaveCore(output, asynchronous, token).ConfigureAwait(false);
                if (asynchronous) await output.FlushAsync(token).ConfigureAwait(false); else output.Flush();
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: false);
        }
        finally
        {
            if (ownsTemporary) { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }
}
