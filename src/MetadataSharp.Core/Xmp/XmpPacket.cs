using System.Buffers;
using System.Collections.ObjectModel;

namespace MetadataSharp.Core.Xmp;

/// <summary>An owned UTF-8 XMP snapshot. Unsupported RDF can be copied but not rewritten.</summary>
/// <remarks>No concurrent use of borrowed streams or mutable edits is supported.</remarks>
public sealed class XmpPacket
{
    private readonly byte[] bytes;
    private readonly XmpLimits limits;
    private readonly IReadOnlyDictionary<XmpPropertyName, XmpValue> properties;
    private XmpPacket(byte[] bytes, XmpLimits limits, bool editable, Dictionary<XmpPropertyName, XmpValue> properties, bool hasExtendedReference)
    { HasExtendedReference = hasExtendedReference; this.bytes = bytes; this.limits = limits; IsEditable = editable; this.properties = new ReadOnlyDictionary<XmpPropertyName, XmpValue>(properties); }
    internal bool HasExtendedReference { get; }
    public bool IsEditable { get; }
    public int ByteLength => bytes.Length;
    public IReadOnlyDictionary<XmpPropertyName, XmpValue> Properties => IsEditable ? properties : throw XmpCodec.Unsupported();
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
    public static XmpPacket CreateEmpty(XmpReadOptions? options = null)
    {
        XmpLimits limits = Settings(options);
        return ParseOwned(XmpCodec.Write(new Dictionary<XmpPropertyName, XmpValue>(), limits), limits, default);
    }
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
        return new(bytes, limits, result.Editable, result.Properties, result.HasExtendedReference);
    }
    public static XmpPacket Load(Stream source, XmpReadOptions? options = null) => LoadCore(source, Settings(options), false, default).GetAwaiter().GetResult();
    public static Task<XmpPacket> LoadAsync(Stream source, XmpReadOptions? options = null, CancellationToken cancellationToken = default) => LoadCore(source, Settings(options), true, cancellationToken);
    public static XmpPacket Load(string path, XmpReadOptions? options = null) => LoadPath(path, Settings(options), false, default).GetAwaiter().GetResult();
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
    public XmpPacket? Apply(XmpEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var snapshot = edit.Snapshot();
        if (snapshot.RemoveAll) return null;
        if (snapshot.Changes.Count == 0) return this;
        if (!IsEditable) throw XmpCodec.Unsupported();
        var changed = properties.ToDictionary(p => p.Key, p => p.Value);
        bool effective = false;
        foreach (var pair in snapshot.Changes)
        {
            if (pair.Value is null) effective |= changed.Remove(pair.Key);
            else { changed[pair.Key] = pair.Value; effective = true; }
        }
        if (!effective) return this;
        if (changed.Count > limits.MaxProperties) throw XmpCodec.Limit();
        return ParseOwned(XmpCodec.Write(changed, limits), limits, default);
    }
    public void Save(Stream destination) => SaveCore(destination, false, default).GetAwaiter().GetResult();
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
    public void SaveAs(string destinationPath) => SavePath(destinationPath, false, default).GetAwaiter().GetResult();
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
