using System.Buffers.Binary;
using System.Security.Cryptography;
using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;

namespace MetadataSharp.Formats.Jpeg;

public sealed partial class JpegMetadataDocument
{
    /// <summary>Whether any exactly signed Extended XMP segment was found, including truncated headers.</summary>
    public bool HasExtendedXmp => scan.Segments.Any(s => s.IsExtendedXmp);
    /// <summary>Reads the standard XMP packet, or returns null when the file has none.</summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> when the file has Extended XMP;
    /// <see cref="MetadataErrorCode.AmbiguousMetadata"/> when it has two standard packets; otherwise
    /// as <see cref="XmpPacket.Parse"/> fails.
    /// </exception>
    public XmpPacket? ReadXmp(XmpReadOptions? options = null) => ReadXmpCore(options, false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="ReadXmp"/>
    public Task<XmpPacket?> ReadXmpAsync(XmpReadOptions? options = null, CancellationToken cancellationToken = default) => ReadXmpCore(options, true, cancellationToken);

    private async Task VerifyXmpSource(bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        byte[] hash = await Hash(source, scan.Length, limits.CopyBufferBytes, asynchronous, token).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(digest, hash))
            throw new MetadataException(MetadataErrorCode.SourceChanged, "The source content has changed since opening.");
    }
    private JpegSegment? UniqueXmp()
    {
        if (HasExtendedXmp) throw XmpCodec.Unsupported();
        JpegSegment[] segments = scan.Segments.Where(s => s.IsStandardXmp).Take(2).ToArray();
        if (segments.Length > 1) throw new MetadataException(MetadataErrorCode.AmbiguousMetadata, "Multiple standard XMP packets are not supported.");
        return segments.FirstOrDefault();
    }
    private void CheckXmpBudget(int packetBytes, XmpLimits xmpLimits)
    {
        if (packetBytes > xmpLimits.MaxPacketBytes || (long)packetBytes + (scan.Exif?.PayloadLength - 6 ?? 0) > limits.MaxRetainedMetadataBytes)
            throw XmpCodec.Limit();
    }
    private async Task<XmpPacket> LoadXmp(JpegSegment segment, XmpReadOptions? options, bool asynchronous, CancellationToken token)
    {
        XmpLimits settings = options is null ? new() : options.Limits ?? throw new ArgumentException("Limits cannot be null.", nameof(options));
        settings.Validate(); settings = settings with { };
        int length = segment.PayloadLength - 29;
        CheckXmpBudget(length, settings);
        byte[] bytes = new byte[length]; source.Position = segment.PayloadOffset + 29;
        await ReadExact(source, bytes, asynchronous, token).ConfigureAwait(false);
        XmpPacket packet = XmpPacket.Parse(bytes, new() { Limits = settings });
        token.ThrowIfCancellationRequested(); return packet;
    }
    private async Task<XmpPacket?> ReadXmpCore(XmpReadOptions? options, bool asynchronous, CancellationToken token)
    {
        await VerifyXmpSource(asynchronous, token).ConfigureAwait(false);
        // Validate options even when no packet exists.
        if (options is not null) (options.Limits ?? throw new ArgumentException("Limits cannot be null.", nameof(options))).Validate();
        JpegSegment? segment = UniqueXmp();
        if (segment is null) return null;
        XmpPacket packet = await LoadXmp(segment, options, asynchronous, token).ConfigureAwait(false);
        if (packet.HasExtendedReference) throw XmpCodec.Unsupported();
        return packet;
    }
    private sealed record XmpPrepared(bool CopyOnly, JpegSegment? Segment, byte[]? Packet);
    private async Task<XmpPrepared> PrepareXmp(XmpEdit edit, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ArgumentNullException.ThrowIfNull(edit);
        XmpEdit snapshot = edit.Snapshot();
        await VerifyXmpSource(asynchronous, token).ConfigureAwait(false);
        if (snapshot.IsEmpty) return new(true, null, null);
        JpegSegment? segment = UniqueXmp();
        if (snapshot.RemovesAll) return new(segment is null, segment, null);
        XmpPacket packet = segment is null ? XmpPacket.CreateEmpty() : await LoadXmp(segment, null, asynchronous, token).ConfigureAwait(false);
        if (packet.HasExtendedReference) throw XmpCodec.Unsupported();
        XmpPacket changed = packet.Apply(snapshot, 0, 0)!;
        // An edit that leaves the packet as it is, or leaves a file without a packet without one, is a copy.
        if (ReferenceEquals(changed, packet)) return new(true, null, null);
        if (changed.HasExtendedReference) throw XmpCodec.Unsupported();
        if (changed.ByteLength > 65_504) throw new MetadataException(MetadataErrorCode.XmpTooLarge, "Standard JPEG XMP exceeds 65,504 bytes.");
        CheckXmpBudget(changed.ByteLength, new());
        byte[] bytes = new byte[changed.ByteLength]; changed.CopyRawTo(bytes);
        token.ThrowIfCancellationRequested(); return new(false, segment, bytes);
    }
    /// <summary>
    /// Writes the file with the XMP edit applied to a distinct, writable, seekable, empty stream. An edit that changes nothing gives a copy of the source.
    /// The packet is the standard XMP APP1 segment, created when the file has none; a file with Extended XMP is refused.
    /// </summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.SourceChanged"/> when the source is no longer the data that was
    /// opened; otherwise the code of the reason the edit is refused.
    /// </exception>
    public void SaveXmp(Stream destination, XmpEdit edit) => SaveXmpCore(destination, edit, false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveXmp(Stream, XmpEdit)"/>
    public Task SaveXmpAsync(Stream destination, XmpEdit edit, CancellationToken cancellationToken = default) => SaveXmpCore(destination, edit, true, cancellationToken);
    private async Task SaveXmpCore(Stream destination, XmpEdit edit, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ArgumentNullException.ThrowIfNull(destination);
        if (ReferenceEquals(source, destination) || !destination.CanWrite || !destination.CanSeek || destination.Position != 0 || destination.Length != 0)
            throw new ArgumentException("Destination must be a distinct writable, seekable, empty stream at zero.", nameof(destination));
        XmpPrepared prepared = await PrepareXmp(edit, asynchronous, token).ConfigureAwait(false);
        await EmitXmp(destination, prepared, asynchronous, token).ConfigureAwait(false);
    }
    private async Task EmitXmp(Stream destination, XmpPrepared prepared, bool asynchronous, CancellationToken token)
    {
        if (prepared.CopyOnly) { await CopyRange(destination, 0, scan.Length, asynchronous, token).ConfigureAwait(false); return; }
        long start = prepared.Segment?.Start ?? scan.InsertionOffset;
        long end = prepared.Segment is null ? start : start + prepared.Segment.Length;
        await CopyRange(destination, 0, start, asynchronous, token).ConfigureAwait(false);
        if (prepared.Packet is byte[] bytes)
        {
            byte[] header = [0xff, 0xe1, 0, 0, .. "http://ns.adobe.com/xap/1.0/\0"u8];
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2), checked((ushort)(bytes.Length + 31)));
            await Write(destination, header, asynchronous, token).ConfigureAwait(false);
            await Write(destination, bytes, asynchronous, token).ConfigureAwait(false);
        }
        await CopyRange(destination, end, scan.Length - end, asynchronous, token).ConfigureAwait(false);
    }
    /// <summary>As <see cref="SaveXmp(Stream, XmpEdit)"/>, to a new path; the file appears there complete or not at all.</summary>
    /// <exception cref="IOException">The destination exists or is the source.</exception>
    public void SaveXmpAs(string destinationPath, XmpEdit edit) => SaveXmpPath(destinationPath, edit, false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveXmpAs(string, XmpEdit)"/>
    public Task SaveXmpAsAsync(string destinationPath, XmpEdit edit, CancellationToken cancellationToken = default) => SaveXmpPath(destinationPath, edit, true, cancellationToken);
    private async Task SaveXmpPath(string path, XmpEdit edit, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ArgumentException.ThrowIfNullOrEmpty(path);
        string target = Path.GetFullPath(path);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if ((sourcePath is not null && string.Equals(sourcePath, target, comparison)) || File.Exists(target) || Directory.Exists(target))
            throw new IOException("The destination must be a new path distinct from the source.");
        XmpPrepared prepared = await PrepareXmp(edit, asynchronous, token).ConfigureAwait(false);
        string temporary = Path.Combine(Path.GetDirectoryName(target)!, ".metadatasharp-jpeg-xmp-" + Guid.NewGuid().ToString("N") + ".tmp");
        bool created = false;
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, limits.CopyBufferBytes,
                asynchronous ? FileOptions.Asynchronous : FileOptions.None))
            {
                created = true; await EmitXmp(output, prepared, asynchronous, token).ConfigureAwait(false);
                if (asynchronous) await output.FlushAsync(token).ConfigureAwait(false); else output.Flush();
            }
            token.ThrowIfCancellationRequested(); File.Move(temporary, target, overwrite: false); created = false;
        }
        finally
        {
            if (created) { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }
}
