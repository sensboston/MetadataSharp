using System.Buffers.Binary;
using System.Security.Cryptography;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;

namespace MetadataSharp.Formats.Jpeg;

public sealed partial class JpegMetadataDocument
{
    /// <summary>
    /// Reads the IPTC stream of the Photoshop APP13 resources, or returns null when there is none.
    /// A stream that can be read and not edited comes back with <see cref="IptcPacket.IsEditable"/>
    /// false: resources spread over several segments, a repeated stream or digest, a digest without a
    /// stream, padding after the last dataset. <see cref="SaveIptc(Stream, IptcEdit)"/> refuses a change to such a file.
    /// </summary>
    public IptcPacket? ReadIptc(IptcReadOptions? options = null) => ReadIptcCore(options, false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="ReadIptc"/>
    public Task<IptcPacket?> ReadIptcAsync(IptcReadOptions? options = null, CancellationToken cancellationToken = default) => ReadIptcCore(options, true, cancellationToken);
    // Compare the staged result with the source to confirm unrelated JPEG data stayed unchanged.
    private async Task VerifyIptcSource(bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        byte[] hash = await Hash(source, scan.Length, limits.CopyBufferBytes, asynchronous, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!CryptographicOperations.FixedTimeEquals(digest, hash))
            throw new MetadataException(MetadataErrorCode.SourceChanged, "The source content has changed since opening.");
    }
    private static MetadataException SeveralSegments() =>
        new(MetadataErrorCode.AmbiguousMetadata, "Multiple Photoshop APP13 segments are unsupported for IPTC operations.");
    // Keep UniquePhotoshop focused on JPEG and its format-specific validation contract.
    private JpegSegment? UniquePhotoshop()
    {
        JpegSegment[] matches = scan.Segments.Where(s => s.IsPhotoshop).Take(2).ToArray();
        if (matches.Length > 1) throw SeveralSegments();
        return matches.FirstOrDefault();
    }
    // Reject invalid JPEG ranges, overlaps, or field shapes before accepting the data.
    private void CheckIptcBudget(int photoshopBytes, IptcLimits settings)
    {
        long retained = (scan.Exif?.PayloadLength - 6 ?? 0) + scan.Segments.Where(s => s.IsStandardXmp).Sum(s => (long)s.PayloadLength - 29);
        if (photoshopBytes > settings.MaxPacketBytes || retained + photoshopBytes > limits.MaxRetainedMetadataBytes) throw IptcPacket.Limit();
    }
    // Keep LoadPhotoshop focused on JPEG and its format-specific validation contract.
    private async Task<PhotoshopResources> LoadPhotoshop(JpegSegment segment, IptcLimits settings, bool forEdit, bool asynchronous, CancellationToken token)
    {
        CheckIptcBudget(segment.PayloadLength, settings);
        byte[] payload = new byte[segment.PayloadLength]; source.Position = segment.PayloadOffset;
        await ReadExact(source, payload, asynchronous, token).ConfigureAwait(false);
        PhotoshopResources resources = PhotoshopResources.Parse(payload, settings);
        if (forEdit) resources.ValidateTargets();
        token.ThrowIfCancellationRequested(); return resources;
    }
    /// <summary>
    /// Reads every Photoshop APP13 segment and joins their resource data, as a writer splits one resource
    /// block across consecutive segments. Used for reading only: editing still needs a single segment.
    /// </summary>
    private async Task<PhotoshopResources> LoadJoinedPhotoshop(JpegSegment[] segments, IptcLimits settings, bool asynchronous, CancellationToken token)
    {
        const int Signature = 14; // "Photoshop 3.0\0"
        long total = Signature + segments.Sum(s => (long)s.PayloadLength - Signature);
        if (total > int.MaxValue) throw IptcPacket.Limit();
        CheckIptcBudget((int)total, settings);
        byte[] joined = new byte[total];
        "Photoshop 3.0\0"u8.CopyTo(joined);
        int at = Signature;
        foreach (JpegSegment segment in segments)
        {
            byte[] payload = new byte[segment.PayloadLength]; source.Position = segment.PayloadOffset;
            await ReadExact(source, payload, asynchronous, token).ConfigureAwait(false);
            payload.AsSpan(Signature).CopyTo(joined.AsSpan(at)); at += payload.Length - Signature;
        }
        PhotoshopResources resources = PhotoshopResources.Parse(joined, settings);
        token.ThrowIfCancellationRequested(); return resources;
    }
    private async Task<IptcPacket?> ReadIptcCore(IptcReadOptions? options, bool asynchronous, CancellationToken token)
    {
        IptcLimits settings = IptcPacket.Settings(options);
        await VerifyIptcSource(asynchronous, token).ConfigureAwait(false);
        JpegSegment[] photoshop = scan.Segments.Where(s => s.IsPhotoshop).ToArray();
        if (photoshop.Length == 0) return null;
        PhotoshopResources resources = photoshop.Length == 1
            ? await LoadPhotoshop(photoshop[0], settings, false, asynchronous, token).ConfigureAwait(false)
            : await LoadJoinedPhotoshop(photoshop, settings, asynchronous, token).ConfigureAwait(false);
        if (resources.IptcForReading() is not PhotoshopResources.Resource iptc) return null;
        // What an edit of this file fails with, so the packet can say it is for reading only.
        MetadataException? refusal = photoshop.Length > 1 ? SeveralSegments() : resources.EditRefusal();
        IptcPacket packet = IptcPacket.Inspect(iptc.Data.Span, new() { Limits = settings }, refusal).WithDigest(resources.StoredDigest());
        token.ThrowIfCancellationRequested(); return packet;
    }
    // Represent the IptcPrepared state used by JPEG parsing and output.
    private sealed record IptcPrepared(bool CopyOnly, JpegSegment? Segment, byte[]? Payload);
    private async Task<IptcPrepared> PrepareIptc(IptcEdit edit, IptcWriteOptions? write, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ArgumentNullException.ThrowIfNull(edit);
        IptcEdit captured = edit.Capture();
        if (!captured.IsEmpty || !(write?.UpdateDigest ?? false)) return await PrepareIptcCore(captured, write, asynchronous, token).ConfigureAwait(false);
        // An edit without a change that asks for the digest: the digest alone is recomputed where the
        // resources can be edited, and the file is copied where they cannot, as it is without the option.
        IptcPrepared copy = await PrepareIptcCore(captured, null, asynchronous, token).ConfigureAwait(false);
        try { return await PrepareIptcCore(captured, write, asynchronous, token).ConfigureAwait(false); }
        catch (MetadataException) { return copy; }
    }
    private async Task<IptcPrepared> PrepareIptcCore(IptcEdit captured, IptcWriteOptions? write, bool asynchronous, CancellationToken token)
    {
        bool refresh = write?.UpdateDigest ?? false;
        await VerifyIptcSource(asynchronous, token).ConfigureAwait(false);
        if (captured.IsEmpty && !refresh) return new(true, null, null);
        JpegSegment? segment = UniquePhotoshop(); IptcLimits settings = new();
        PhotoshopResources resources = segment is null ? PhotoshopResources.Empty : await LoadPhotoshop(segment, settings, true, asynchronous, token).ConfigureAwait(false);
        if (captured.RemovesAll)
        {
            if (resources.Iptc is null) return new(true, null, null);
            byte[]? removed = resources.Rewrite(null);
            return new(false, segment, removed);
        }
        IptcPacket packet = resources.Iptc is PhotoshopResources.Resource existing ? IptcPacket.Parse(existing.Data.Span) : IptcPacket.CreateEmpty();
        IptcPacket changed = packet.Apply(captured)!;
        // Nothing was added to an absent stream, or no byte of the stored one was changed.
        if (ReferenceEquals(packet, changed) && !(refresh && resources.DigestIsStale())) return new(true, null, null);
        // Even a minimal resource needs 14 signature bytes, 12 framing bytes and even data.
        if (changed.ByteLength + (changed.ByteLength & 1) + 26L > 65533)
            throw new MetadataException(MetadataErrorCode.IptcTooLarge, "IPTC cannot fit in one Photoshop APP13 segment.");
        byte[] bytes = new byte[changed.ByteLength]; changed.CopyRawTo(bytes);
        byte[]? result = resources.Rewrite(bytes, write?.UpdateDigest ?? false);
        if (result is not null) CheckIptcBudget(result.Length, settings);
        token.ThrowIfCancellationRequested(); return new(false, segment, result);
    }
    /// <summary>
    /// Writes the file with the IPTC edit applied to a distinct, writable, seekable, empty stream. An edit that changes nothing gives a copy of the source.
    /// The stream is resource 0x0404 of the Photoshop APP13 segment, created when the file has none; every
    /// other resource keeps its bytes and its place. The IPTC digest (resource 0x0425) is left as the
    /// file has it, so that it no longer matches and a reader takes the IPTC as newer than the XMP;
    /// <see cref="IptcWriteOptions.UpdateDigest"/> recomputes it. It is removed with the stream and never added.
    /// </summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.SourceChanged"/> when the source is no longer the data that was
    /// opened; otherwise the code of the reason the edit is refused.
    /// </exception>
    public void SaveIptc(Stream destination, IptcEdit edit) => SaveIptcCore(destination, edit, null, false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveIptc(Stream, IptcEdit)"/>
    public Task SaveIptcAsync(Stream destination, IptcEdit edit, CancellationToken cancellationToken = default) => SaveIptcCore(destination, edit, null, true, cancellationToken);
    /// <summary>As <see cref="SaveIptc(Stream, IptcEdit)"/>, with options; null options are the defaults.</summary>
    public void SaveIptc(Stream destination, IptcEdit edit, IptcWriteOptions? options) => SaveIptcCore(destination, edit, options, false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveIptc(Stream, IptcEdit, IptcWriteOptions)"/>
    public Task SaveIptcAsync(Stream destination, IptcEdit edit, IptcWriteOptions? options, CancellationToken cancellationToken = default) => SaveIptcCore(destination, edit, options, true, cancellationToken);
    // Write the prepared JPEG edit only after validation, preserving unrelated payloads.
    private async Task SaveIptcCore(Stream destination, IptcEdit edit, IptcWriteOptions? write, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ArgumentNullException.ThrowIfNull(destination);
        if (ReferenceEquals(source, destination) || !destination.CanWrite || !destination.CanSeek || destination.Position != 0 || destination.Length != 0)
            throw new ArgumentException("Destination must be a distinct writable, seekable, empty stream at zero.", nameof(destination));
        IptcPrepared prepared = await PrepareIptc(edit, write, asynchronous, token).ConfigureAwait(false);
        await EmitIptc(destination, prepared, asynchronous, token).ConfigureAwait(false);
    }
    // Write the prepared JPEG edit only after validation, preserving unrelated payloads.
    private async Task EmitIptc(Stream destination, IptcPrepared prepared, bool asynchronous, CancellationToken token)
    {
        if (prepared.CopyOnly) { await CopyRange(destination, 0, scan.Length, asynchronous, token).ConfigureAwait(false); return; }
        long start = prepared.Segment?.Start ?? scan.InsertionOffset;
        long end = prepared.Segment is null ? start : start + prepared.Segment.Length;
        await CopyRange(destination, 0, start, asynchronous, token).ConfigureAwait(false);
        if (prepared.Payload is byte[] payload)
        {
            byte[] header = [0xff, 0xed, 0, 0]; BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2), checked((ushort)(payload.Length + 2)));
            await Write(destination, header, asynchronous, token).ConfigureAwait(false);
            await Write(destination, payload, asynchronous, token).ConfigureAwait(false);
        }
        await CopyRange(destination, end, scan.Length - end, asynchronous, token).ConfigureAwait(false);
    }
    /// <summary>As <see cref="SaveIptc(Stream, IptcEdit)"/>, to a new path; the file appears there complete or not at all.</summary>
    /// <exception cref="IOException">The destination exists or is the source.</exception>
    public void SaveIptcAs(string destinationPath, IptcEdit edit) => SaveIptcPath(destinationPath, edit, null, false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveIptcAs(string, IptcEdit)"/>
    public Task SaveIptcAsAsync(string destinationPath, IptcEdit edit, CancellationToken cancellationToken = default) => SaveIptcPath(destinationPath, edit, null, true, cancellationToken);
    /// <summary>As <see cref="SaveIptcAs(string, IptcEdit)"/>, with options; null options are the defaults.</summary>
    /// <exception cref="IOException">The destination exists or is the source.</exception>
    public void SaveIptcAs(string destinationPath, IptcEdit edit, IptcWriteOptions? options) => SaveIptcPath(destinationPath, edit, options, false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveIptcAs(string, IptcEdit, IptcWriteOptions)"/>
    public Task SaveIptcAsAsync(string destinationPath, IptcEdit edit, IptcWriteOptions? options, CancellationToken cancellationToken = default) => SaveIptcPath(destinationPath, edit, options, true, cancellationToken);
    // Write the prepared JPEG edit only after validation, preserving unrelated payloads.
    private async Task SaveIptcPath(string path, IptcEdit edit, IptcWriteOptions? write, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ArgumentException.ThrowIfNullOrEmpty(path);
        string target = Path.GetFullPath(path); StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if ((sourcePath is not null && string.Equals(sourcePath, target, comparison)) || File.Exists(target) || Directory.Exists(target))
            throw new IOException("The destination must be a new path distinct from the source.");
        IptcPrepared prepared = await PrepareIptc(edit, write, asynchronous, token).ConfigureAwait(false);
        string temporary = Path.Combine(Path.GetDirectoryName(target)!, ".metadatasharp-iptc-" + Guid.NewGuid().ToString("N") + ".tmp"); bool created = false;
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, limits.CopyBufferBytes,
                asynchronous ? FileOptions.Asynchronous : FileOptions.None))
            {
                created = true; await EmitIptc(output, prepared, asynchronous, token).ConfigureAwait(false);
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
