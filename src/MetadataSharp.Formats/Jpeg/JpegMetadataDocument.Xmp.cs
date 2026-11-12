using System.Buffers.Binary;
using System.Security.Cryptography;
using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;

namespace MetadataSharp.Formats.Jpeg;

public sealed partial class JpegMetadataDocument
{
    /// <summary>Whether any exactly signed Extended XMP segment was found, including truncated headers.</summary>
    public bool HasExtendedXmp => scan.Segments.Any(s => s.IsExtendedXmp);
    /// <summary>
    /// Reads the XMP of the file, or returns null when the file has none. Extended XMP is read whole:
    /// the properties of the standard packet and of its extension come as one packet, without the
    /// <c>xmpNote:HasExtendedXMP</c> property that joins them.
    /// </summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> when the Extended XMP segments are not
    /// exactly one complete extension that the standard packet names, or a part cannot be rewritten;
    /// <see cref="MetadataErrorCode.AmbiguousMetadata"/> when the file has two standard packets or
    /// a property is in both parts; otherwise as <see cref="XmpPacket.Parse"/> fails.
    /// </exception>
    private async Task VerifyXmpSource(bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        byte[] hash = await Hash(source, scan.Length, limits.CopyBufferBytes, asynchronous, token).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(digest, hash))
            throw new MetadataException(MetadataErrorCode.SourceChanged, "The source content has changed since opening.");
    }
    // An extension segment: the signature, the digest as 32 characters, the length of the whole
    // extension and the offset of this part, both big-endian, then the part.
    private const int ExtensionSignatureBytes = 35;
    private const int ExtensionHeaderBytes = ExtensionSignatureBytes + 32 + 4 + 4;
    private const int ExtensionPartBytes = 65_533 - ExtensionHeaderBytes;
    private const int StandardPacketBytes = 65_504;
    private JpegSegment? UniqueXmp()
    {
        JpegSegment[] segments = scan.Segments.Where(s => s.IsStandardXmp).Take(2).ToArray();
        if (segments.Length > 1) throw new MetadataException(MetadataErrorCode.AmbiguousMetadata, "Multiple standard XMP packets are not supported.");
        return segments.FirstOrDefault();
    }
    /// <summary>
    /// The XMP of the file as one packet. Extension segments are accepted only as one complete
    /// extension under the digest the standard packet names: every segment carries that digest and
    /// the same total length, and the parts cover the extension exactly once.
    /// </summary>
    private async Task<XmpPacket?> LoadWholeXmp(JpegSegment? segment, XmpReadOptions? options, bool asynchronous, CancellationToken token)
    {
        JpegSegment[] extensions = scan.Segments.Where(s => s.IsExtendedXmp).ToArray();
        if (segment is null)
        {
            if (extensions.Length != 0) throw XmpCodec.Unsupported();
            return null;
        }
        XmpPacket packet = await LoadXmp(segment, options, asynchronous, token).ConfigureAwait(false);
        if (extensions.Length == 0)
        {
            // A reference to an extension the file does not have: part of the XMP is missing.
            if (packet.HasExtendedReference) throw XmpCodec.Unsupported();
            return packet;
        }
        XmpLimits settings = (options?.Limits ?? new XmpLimits()) with { };
        string? digest = XmpExtended.ReferenceOf(packet);
        if (digest is null || digest.Length != 32) throw XmpCodec.Unsupported();
        byte[]? whole = null;
        var parts = new List<(long Offset, int Length)>(extensions.Length);
        byte[] header = new byte[ExtensionHeaderBytes - ExtensionSignatureBytes];
        foreach (JpegSegment extension in extensions)
        {
            int length = extension.PayloadLength - ExtensionHeaderBytes;
            if (length <= 0) throw XmpCodec.Unsupported();
            source.Position = extension.PayloadOffset + ExtensionSignatureBytes;
            await ReadExact(source, header, asynchronous, token).ConfigureAwait(false);
            for (int i = 0; i < 32; i++)
                if (header[i] != digest[i]) throw XmpCodec.Unsupported();
            uint total = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(32));
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(36));
            if (whole is null)
            {
                if (total == 0 || total > int.MaxValue) throw XmpCodec.Unsupported();
                CheckXmpBudget(checked((int)Math.Min(int.MaxValue, (long)total + packet.ByteLength)), settings);
                whole = new byte[total];
            }
            if (total != whole.Length || (long)offset + length > whole.Length) throw XmpCodec.Unsupported();
            await ReadExact(source, whole.AsMemory((int)offset, length), asynchronous, token).ConfigureAwait(false);
            parts.Add((offset, length));
        }
        parts.Sort((left, right) => left.Offset.CompareTo(right.Offset));
        long covered = 0;
        foreach ((long offset, int length) in parts)
        {
            if (offset != covered) throw XmpCodec.Unsupported();
            covered += length;
        }
        if (covered != whole!.Length) throw XmpCodec.Unsupported();
        XmpPacket joined = XmpExtended.Join(packet, whole, settings);
        token.ThrowIfCancellationRequested(); return joined;
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
        return await LoadWholeXmp(UniqueXmp(), options, asynchronous, token).ConfigureAwait(false);
    }
    /// <summary>What a save does: the segments that go, and the standard packet and the extension that come.</summary>
    private sealed record XmpPrepared(bool CopyOnly, JpegSegment? Segment, JpegSegment[] Extensions, byte[]? Packet, XmpExtended.Parts? Extended);
    private static readonly XmpPrepared XmpCopy = new(true, null, [], null, null);
    private async Task<XmpPrepared> PrepareXmp(XmpEdit edit, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ArgumentNullException.ThrowIfNull(edit);
        XmpEdit snapshot = edit.Snapshot();
        await VerifyXmpSource(asynchronous, token).ConfigureAwait(false);
        if (snapshot.IsEmpty) return XmpCopy;
        JpegSegment? segment = UniqueXmp();
        JpegSegment[] extensions = scan.Segments.Where(s => s.IsExtendedXmp).ToArray();
        if (snapshot.RemovesAll)
        {
            // Extension segments go only as the extension of the packet that goes with them.
            if (extensions.Length != 0) await LoadWholeXmp(segment, null, asynchronous, token).ConfigureAwait(false);
            return segment is null ? XmpCopy : new(false, segment, extensions, null, null);
        }
        XmpPacket packet = await LoadWholeXmp(segment, null, asynchronous, token).ConfigureAwait(false) ?? XmpPacket.CreateEmpty();
        XmpPacket changed = packet.Apply(snapshot, 0, 0)!;
        // An edit that leaves the packet as it is, or leaves a file without a packet without one, is a copy.
        if (ReferenceEquals(changed, packet)) return XmpCopy;
        // The reference belongs to the division into parts, which is made here and not by an edit.
        if (changed.HasExtendedReference) throw XmpCodec.Unsupported();
        CheckXmpBudget(changed.ByteLength, new());
        token.ThrowIfCancellationRequested();
        if (changed.ByteLength > StandardPacketBytes)
        {
            XmpExtended.Parts parts = XmpExtended.Split(changed, StandardPacketBytes);
            token.ThrowIfCancellationRequested(); return new(false, segment, extensions, parts.Standard, parts);
        }
        byte[] bytes = new byte[changed.ByteLength]; changed.CopyRawTo(bytes);
        return new(false, segment, extensions, bytes, null);
    }
    /// <summary>
    /// Writes the file with the XMP edit applied to a distinct, writable, seekable, empty stream. An edit that changes nothing gives a copy of the source.
    /// The packet is the standard XMP APP1 segment, created when the file has none. A packet longer than the 65,504 bytes one segment
    /// holds is written as Extended XMP: the largest properties go to an extension in further APP1 segments after the standard one,
    /// which names it by its MD5 digest. Extended XMP that is there is replaced whole, and goes when the result fits one segment.
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
        // The new segments stand where the standard packet was, or where a first one belongs; the
        // old packet and every old extension segment are left out, everything else is copied.
        long at = prepared.Segment?.Start ?? scan.InsertionOffset;
        var dropped = new List<JpegSegment>(prepared.Extensions);
        if (prepared.Segment is not null) dropped.Add(prepared.Segment);
        dropped.Sort((left, right) => left.Start.CompareTo(right.Start));
        long position = 0; bool written = false;
        foreach (JpegSegment segment in dropped)
        {
            if (!written && at <= segment.Start)
            {
                await CopyRange(destination, position, at - position, asynchronous, token).ConfigureAwait(false);
                position = at; written = true;
                await EmitXmpSegments(destination, prepared, asynchronous, token).ConfigureAwait(false);
            }
            await CopyRange(destination, position, segment.Start - position, asynchronous, token).ConfigureAwait(false);
            position = segment.Start + segment.Length;
        }
        if (!written)
        {
            await CopyRange(destination, position, at - position, asynchronous, token).ConfigureAwait(false);
            position = at;
            await EmitXmpSegments(destination, prepared, asynchronous, token).ConfigureAwait(false);
        }
        await CopyRange(destination, position, scan.Length - position, asynchronous, token).ConfigureAwait(false);
    }
    private static async Task EmitXmpSegments(Stream destination, XmpPrepared prepared, bool asynchronous, CancellationToken token)
    {
        if (prepared.Packet is not byte[] bytes) return;
        byte[] header = [0xff, 0xe1, 0, 0, .. "http://ns.adobe.com/xap/1.0/\0"u8];
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2), checked((ushort)(bytes.Length + 31)));
        await Write(destination, header, asynchronous, token).ConfigureAwait(false);
        await Write(destination, bytes, asynchronous, token).ConfigureAwait(false);
        if (prepared.Extended is not XmpExtended.Parts parts) return;
        byte[] extension = parts.Extension;
        for (int offset = 0; offset < extension.Length; offset += ExtensionPartBytes)
        {
            int length = Math.Min(ExtensionPartBytes, extension.Length - offset);
            byte[] lead = new byte[4 + ExtensionHeaderBytes];
            lead[0] = 0xff; lead[1] = 0xe1;
            BinaryPrimitives.WriteUInt16BigEndian(lead.AsSpan(2), checked((ushort)(2 + ExtensionHeaderBytes + length)));
            "http://ns.adobe.com/xmp/extension/\0"u8.CopyTo(lead.AsSpan(4));
            for (int i = 0; i < 32; i++) lead[4 + ExtensionSignatureBytes + i] = (byte)parts.Digest[i];
            BinaryPrimitives.WriteUInt32BigEndian(lead.AsSpan(4 + ExtensionSignatureBytes + 32), (uint)extension.Length);
            BinaryPrimitives.WriteUInt32BigEndian(lead.AsSpan(4 + ExtensionSignatureBytes + 36), (uint)offset);
            await Write(destination, lead, asynchronous, token).ConfigureAwait(false);
            await Write(destination, extension.AsMemory(offset, length), asynchronous, token).ConfigureAwait(false);
        }
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
