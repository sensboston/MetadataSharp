using System.Security.Cryptography;
using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Internal;
using MetadataSharp.Formats.Tiff;

namespace MetadataSharp.Formats.WebP;

/// <summary>A validated static WebP source with independently addressed EXIF and XMP operations.</summary>
/// <remarks>Borrowed streams remain open. No concurrent source/document/edit use is supported.</remarks>
public sealed partial class WebPMetadataDocument : IDisposable, IAsyncDisposable
{
    private readonly Stream source;
    private readonly bool owns;
    private readonly string? sourcePath;
    private readonly WebPReadOptions options;
    private readonly WebPScan scan;
    private readonly byte[] digest;
    private bool disposed;
    private WebPMetadataDocument(Stream source, bool owns, string? path, WebPReadOptions options, WebPScan scan, byte[] digest)
    { this.source = source; this.owns = owns; sourcePath = path; this.options = options; this.scan = scan; this.digest = digest; }
    /// <summary>Opens a WebP from a readable, seekable stream at position zero. The stream stays open, and has to stay unchanged, while the document is in use.</summary>
    /// <exception cref="MetadataException">The data is not a file this document reads, or it exceeds a budget of the limits.</exception>
    public static WebPMetadataDocument Open(Stream source, WebPReadOptions? options = null) => OpenCore(source, false, null, (options ?? new()).Snapshot(), false, default).GetAwaiter().GetResult();
    /// <summary>Opens a WebP file and keeps it open, shared for reading, until the document is disposed. Fails as <see cref="Open(Stream, WebPReadOptions?)"/> does.</summary>
    public static WebPMetadataDocument Open(string path, WebPReadOptions? options = null) => OpenPath(path, (options ?? new()).Snapshot(), false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="Open(Stream, WebPReadOptions?)"/>
    public static Task<WebPMetadataDocument> OpenAsync(Stream source, WebPReadOptions? options = null, CancellationToken cancellationToken = default) => OpenCore(source, false, null, (options ?? new()).Snapshot(), true, cancellationToken);
    /// <inheritdoc cref="Open(string, WebPReadOptions?)"/>
    public static Task<WebPMetadataDocument> OpenAsync(string path, WebPReadOptions? options = null, CancellationToken cancellationToken = default) => OpenPath(path, (options ?? new()).Snapshot(), true, cancellationToken);
    private static async Task<WebPMetadataDocument> OpenPath(string path, WebPReadOptions options, bool asynchronous, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrEmpty(path); token.ThrowIfCancellationRequested(); string full = Path.GetFullPath(path);
        var file = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, options.Limits.CopyBufferBytes, FileOptions.SequentialScan | (asynchronous ? FileOptions.Asynchronous : FileOptions.None));
        return await OpenCore(file, true, full, options, asynchronous, token).ConfigureAwait(false);
    }
    private static async Task<WebPMetadataDocument> OpenCore(Stream source, bool owns, string? path, WebPReadOptions options, bool asynchronous, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            WebPScan scan;
            try { scan = await WebPScanner.Scan(source, options.Limits, asynchronous, token).ConfigureAwait(false); }
            catch (EndOfStreamException) { throw WebPScanner.Invalid(); }
            byte[] digest = await ContainerIo.Hash(source, scan.Length, options.Limits.CopyBufferBytes, asynchronous, token).ConfigureAwait(false);
            return new(source, owns, path, options, scan, digest);
        }
        catch { if (owns) await source.DisposeAsync().ConfigureAwait(false); throw; }
    }
    private async Task Verify(bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        byte[] current = await ContainerIo.Hash(source, scan.Length, options.Limits.CopyBufferBytes, asynchronous, token).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(digest, current)) throw ContainerIo.Changed();
    }
    private WebPChunk? Unique(bool xmp)
    {
        WebPChunk[] chunks = scan.Chunks.Where(c => xmp ? c.Xmp : c.Type == "EXIF").Take(2).ToArray();
        if (chunks.Length > 1) throw new MetadataException(MetadataErrorCode.AmbiguousMetadata, "Duplicate target WebP metadata chunks.");
        return chunks.FirstOrDefault();
    }
    private void Budget(long bytes)
    { if (bytes > options.Limits.MaxRetainedMetadataBytes) throw new MetadataException(MetadataErrorCode.LimitExceeded, "WebP metadata exceeds its budget."); }
    private async Task<byte[]> Payload(WebPChunk chunk, bool asynchronous, CancellationToken token)
    {
        Budget(chunk.Size); byte[] bytes = new byte[(int)chunk.Size]; source.Position = chunk.Payload;
        await ContainerIo.Read(source, bytes, asynchronous, token).ConfigureAwait(false); return bytes;
    }
    private async Task<TiffReadResult> Exif(WebPChunk chunk, bool asynchronous, CancellationToken token)
    {
        if (chunk.Size > 65533) throw new MetadataException(MetadataErrorCode.ExifTooLarge, "EXIF exceeds the approved classic TIFF profile cap.");
        byte[] prefix = new byte[(int)Math.Min(chunk.Size, 6)]; source.Position = chunk.Payload;
        await ContainerIo.Read(source, prefix, asynchronous, token).ConfigureAwait(false);
        int skip = prefix.AsSpan().SequenceEqual("Exif\0\0"u8) ? 6 : 0;
        if (chunk.Size - skip > 65527) throw new MetadataException(MetadataErrorCode.ExifTooLarge, "EXIF exceeds the approved classic TIFF profile cap.");
        byte[] bytes = await Payload(chunk, asynchronous, token).ConfigureAwait(false);
        var result = ExifTiffReader.Read(bytes.AsSpan(skip), options.Limits, chunk.Payload + skip); token.ThrowIfCancellationRequested(); return result;
    }
    /// <summary>The bytes of the EXIF block, without the identifier some writers put before it, and where it begins in the file.</summary>
    private async Task<(byte[] Block, long At)> ExifBlock(WebPChunk chunk, bool asynchronous, CancellationToken token)
    {
        if (chunk.Size > 65533) throw new MetadataException(MetadataErrorCode.ExifTooLarge, "EXIF exceeds the approved classic TIFF profile cap.");
        byte[] bytes = await Payload(chunk, asynchronous, token).ConfigureAwait(false);
        int skip = bytes.AsSpan().StartsWith("Exif\0\0"u8) ? 6 : 0;
        if (bytes.Length - skip > 65527) throw new MetadataException(MetadataErrorCode.ExifTooLarge, "EXIF exceeds the approved classic TIFF profile cap.");
        return (bytes[skip..], chunk.Payload + skip);
    }

    /// <summary>
    /// Reads the EXIF block of the <c>EXIF</c> chunk, or returns null when the file has none. A block
    /// the editable profile refuses for a structure that is valid to read comes back with
    /// <see cref="ExifMetadata.IsEditable"/> false; <see cref="SaveExif"/> refuses a change to it.
    /// </summary>
    public ExifMetadata? ReadExif() => ReadExifCore(false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="ReadExif"/>
    public Task<ExifMetadata?> ReadExifAsync(CancellationToken cancellationToken = default) => ReadExifCore(true, cancellationToken);
    private async Task<ExifMetadata?> ReadExifCore(bool asynchronous, CancellationToken token)
    {
        await Verify(asynchronous, token).ConfigureAwait(false); WebPChunk? chunk = Unique(false);
        if (chunk is null) return null;
        try { return (await Exif(chunk, asynchronous, token).ConfigureAwait(false)).Metadata; }
        catch (MetadataException failure) when (ExifTiffReader.IsRelaxable(failure))
        {
            // Read-only rules for a block the editable profile refuses; SaveExif keeps refusing it.
            byte[] bytes = await Payload(chunk, asynchronous, token).ConfigureAwait(false);
            int skip = bytes.AsSpan().StartsWith("Exif\0\0"u8) ? 6 : 0;
            ExifMetadata relaxed = ExifTiffReader.ReadRelaxed(bytes.AsSpan(skip), options.Limits, chunk.Payload + skip); token.ThrowIfCancellationRequested(); return relaxed;
        }
    }
    /// <summary>Reads the packet of the <c>XMP </c> chunk, or returns null when the file has none.</summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.AmbiguousMetadata"/> when the file has two such chunks; otherwise
    /// as <see cref="XmpPacket.Parse"/> fails.
    /// </exception>
    public XmpPacket? ReadXmp(XmpReadOptions? options = null) => ReadXmpCore(options, false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="ReadXmp"/>
    public Task<XmpPacket?> ReadXmpAsync(XmpReadOptions? options = null, CancellationToken cancellationToken = default) => ReadXmpCore(options, true, cancellationToken);
    private async Task<XmpPacket?> ReadXmpCore(XmpReadOptions? settings, bool asynchronous, CancellationToken token)
    {
        await Verify(asynchronous, token).ConfigureAwait(false);
        if (settings is not null) (settings.Limits ?? throw new ArgumentException("XMP limits cannot be null.")).Validate();
        WebPChunk? chunk = Unique(true); return chunk is null ? null : await Xmp(chunk, settings, asynchronous, token).ConfigureAwait(false);
    }
    private async Task<XmpPacket> Xmp(WebPChunk chunk, XmpReadOptions? settings, bool asynchronous, CancellationToken token)
    {
        Budget(chunk.Size + scan.Chunks.Where(c => c.Type == "EXIF").Sum(c => c.Size));
        int maximum = settings?.Limits.MaxPacketBytes ?? new XmpLimits().MaxPacketBytes;
        if (chunk.Size > maximum) throw new MetadataException(MetadataErrorCode.LimitExceeded, "WebP XMP exceeds its packet budget.");
        byte[] payload = await Payload(chunk, asynchronous, token).ConfigureAwait(false);
        XmpPacket result = XmpPacket.Parse(payload, settings); token.ThrowIfCancellationRequested(); return result;
    }
    private sealed record Prepared(bool Copy, WebPChunk? Target, byte[]? Chunk, byte[]? Extended = null, byte[]? Riff = null, long Insertion = 0);
    private async Task<Prepared> Prepare(ExifEdit? exif, XmpEdit? xmp, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var es = exif?.Snapshot(); var xs = xmp?.Snapshot();
        await Verify(asynchronous, token).ConfigureAwait(false);
        bool isXmp = xmp is not null;
        bool empty = isXmp ? !xs!.Value.RemoveAll && xs.Value.Changes.Count == 0 : es!.IsEmpty;
        if (empty) return new(true, null, null);
        WebPChunk? chunk = Unique(isXmp);
        bool remove = isXmp ? xs!.Value.RemoveAll : es!.RemoveAll;
        if (remove && chunk is null) return new(true, null, null);
        byte[]? bytes = null;
        if (!remove && isXmp)
        {
            var capture = new XmpEdit(); foreach (var pair in xs!.Value.Changes) { if (pair.Value is null) capture.Remove(pair.Key); else capture.Set(pair.Key, pair.Value); }
            if (chunk is null && xs.Value.Changes.Values.All(v => v is null)) return new(true, null, null);
            XmpPacket original = chunk is null ? XmpPacket.CreateEmpty() : await Xmp(chunk, null, asynchronous, token).ConfigureAwait(false);
            XmpPacket changed = original.Apply(capture)!;
            if (ReferenceEquals(original, changed) && chunk is not null) return new(true, null, null);
            Budget((long)changed.ByteLength + original.ByteLength + scan.Chunks.Where(c => c.Type == "EXIF").Sum(c => (long)c.Size));
            bytes = new byte[changed.ByteLength]; changed.CopyRawTo(bytes);
        }
        else if (!remove)
        {
            // A block the file has is edited where it lies: nothing in it moves, and what grows is appended.
            (byte[] Block, long At)? original = chunk is null ? null : await ExifBlock(chunk, asynchronous, token).ConfigureAwait(false);
            bytes = original is null ? ExifBlockEditor.Create(es!, options.Limits, ExifTiffReader.SegmentCeiling, out _)
                : ExifBlockEditor.Edit(original.Value.Block, original.Value.At, es!, options.Limits);
            if (bytes is null) return new(true, null, null);
            Budget((long)bytes.Length + (chunk?.Size ?? 0) + scan.Chunks.Where(c => c.Xmp).Sum(c => (long)c.Size));
        }
        if (scan.Unknown) throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "Unknown WebP chunks prevent effective editing.");
        byte[]? rebuilt = bytes is null ? null : WebPScanner.Chunk(isXmp ? "XMP " : "EXIF", bytes);
        byte[] extended = scan.ExtendedPayload?.ToArray() ?? new byte[10];
        if (scan.ExtendedPayload is null)
        { WebPScanner.Put24(extended.AsSpan(4), scan.Width - 1); WebPScanner.Put24(extended.AsSpan(7), scan.Height - 1); if (scan.ImageAlphaHint) extended[0] |= 0x10; }
        int mask = isXmp ? 4 : 8; extended[0] = (byte)((extended[0] & ~mask) | (rebuilt is null ? 0 : mask));
        long length = scan.Length - (chunk?.Length ?? 0) + (rebuilt?.Length ?? 0) + (scan.ExtendedPayload is null ? 18 : 0);
        if (length > uint.MaxValue - 1L) throw new MetadataException(MetadataErrorCode.LimitExceeded, "Edited WebP exceeds the RIFF size limit.");
        byte[] riff = new byte[12]; "RIFF"u8.CopyTo(riff); "WEBP"u8.CopyTo(riff.AsSpan(8)); BinaryPrimitives.WriteUInt32LittleEndian(riff.AsSpan(4), (uint)(length - 8));
        long insertion = isXmp ? scan.Length : scan.Chunks.Where(c => c.Xmp && c.Start >= scan.ImageEnd).Select(c => c.Start).DefaultIfEmpty(scan.Length).Min();
        token.ThrowIfCancellationRequested(); return new(false, chunk, rebuilt, WebPScanner.Chunk("VP8X", extended), riff, insertion);
    }
    private async Task Emit(Stream output, Prepared prepared, bool asynchronous, CancellationToken token)
    {
        if (prepared.Copy) { await ContainerIo.Copy(source, output, 0, scan.Length, options.Limits.CopyBufferBytes, asynchronous, token).ConfigureAwait(false); return; }
        await ContainerIo.Write(output, prepared.Riff!, asynchronous, token).ConfigureAwait(false);
        await ContainerIo.Write(output, prepared.Extended!, asynchronous, token).ConfigureAwait(false);
        bool inserted = false;
        foreach (WebPChunk chunk in scan.Chunks)
        {
            if (chunk.Type == "VP8X") continue;
            if (prepared.Target is null && !inserted && chunk.Start >= prepared.Insertion)
            { if (prepared.Chunk is not null) await ContainerIo.Write(output, prepared.Chunk, asynchronous, token).ConfigureAwait(false); inserted = true; }
            if (prepared.Target?.Start == chunk.Start)
            { if (prepared.Chunk is not null) await ContainerIo.Write(output, prepared.Chunk, asynchronous, token).ConfigureAwait(false); }
            else await ContainerIo.Copy(source, output, chunk.Start, chunk.Length, options.Limits.CopyBufferBytes, asynchronous, token).ConfigureAwait(false);
        }
        if (prepared.Target is null && !inserted && prepared.Chunk is not null) await ContainerIo.Write(output, prepared.Chunk, asynchronous, token).ConfigureAwait(false);
    }
    /// <summary>
    /// Writes the file with the EXIF edit applied to a distinct, writable, seekable, empty stream. An edit that changes nothing gives a copy of the source.
    /// The EXIF block is the <c>EXIF</c> chunk, created when the file has none.
    /// </summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.SourceChanged"/> when the source is no longer the data that was
    /// opened; otherwise the code of the reason the edit is refused.
    /// </exception>
    public void SaveExif(Stream destination, ExifEdit edit) { ArgumentNullException.ThrowIfNull(edit); SaveCore(destination, edit, null, false, default).GetAwaiter().GetResult(); }
    /// <inheritdoc cref="SaveExif(Stream, ExifEdit)"/>
    public Task SaveExifAsync(Stream destination, ExifEdit edit, CancellationToken cancellationToken = default) { ArgumentNullException.ThrowIfNull(edit); return SaveCore(destination, edit, null, true, cancellationToken); }
    /// <summary>
    /// Writes the file with the XMP edit applied to a distinct, writable, seekable, empty stream. An edit that changes nothing gives a copy of the source.
    /// The packet is the <c>XMP </c> chunk, created when the file has none.
    /// </summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.SourceChanged"/> when the source is no longer the data that was
    /// opened; otherwise the code of the reason the edit is refused.
    /// </exception>
    public void SaveXmp(Stream destination, XmpEdit edit) { ArgumentNullException.ThrowIfNull(edit); SaveCore(destination, null, edit, false, default).GetAwaiter().GetResult(); }
    /// <inheritdoc cref="SaveXmp(Stream, XmpEdit)"/>
    public Task SaveXmpAsync(Stream destination, XmpEdit edit, CancellationToken cancellationToken = default) { ArgumentNullException.ThrowIfNull(edit); return SaveCore(destination, null, edit, true, cancellationToken); }
    private async Task SaveCore(Stream destination, ExifEdit? exif, XmpEdit? xmp, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ContainerIo.Output(source, destination);
        Prepared prepared = await Prepare(exif, xmp, asynchronous, token).ConfigureAwait(false); await Emit(destination, prepared, asynchronous, token).ConfigureAwait(false);
    }
    /// <summary>As <see cref="SaveExif(Stream, ExifEdit)"/>, to a new path; the file appears there complete or not at all.</summary>
    /// <exception cref="IOException">The destination exists or is the source.</exception>
    public void SaveExifAs(string destinationPath, ExifEdit edit) { ArgumentNullException.ThrowIfNull(edit); SavePath(destinationPath, edit, null, false, default).GetAwaiter().GetResult(); }
    /// <inheritdoc cref="SaveExifAs(string, ExifEdit)"/>
    public Task SaveExifAsAsync(string destinationPath, ExifEdit edit, CancellationToken cancellationToken = default) { ArgumentNullException.ThrowIfNull(edit); return SavePath(destinationPath, edit, null, true, cancellationToken); }
    /// <summary>As <see cref="SaveXmp(Stream, XmpEdit)"/>, to a new path; the file appears there complete or not at all.</summary>
    /// <exception cref="IOException">The destination exists or is the source.</exception>
    public void SaveXmpAs(string destinationPath, XmpEdit edit) { ArgumentNullException.ThrowIfNull(edit); SavePath(destinationPath, null, edit, false, default).GetAwaiter().GetResult(); }
    /// <inheritdoc cref="SaveXmpAs(string, XmpEdit)"/>
    public Task SaveXmpAsAsync(string destinationPath, XmpEdit edit, CancellationToken cancellationToken = default) { ArgumentNullException.ThrowIfNull(edit); return SavePath(destinationPath, null, edit, true, cancellationToken); }
    private async Task SavePath(string path, ExifEdit? exif, XmpEdit? xmp, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this); string target = ContainerIo.Target(path, sourcePath);
        Prepared prepared = await Prepare(exif, xmp, asynchronous, token).ConfigureAwait(false);
        await ContainerIo.Publish(target, options.Limits.CopyBufferBytes, asynchronous, token, output => Emit(output, prepared, asynchronous, token)).ConfigureAwait(false);
    }
    /// <summary>Closes the file when the document opened it from a path; a stream the caller gave stays open.</summary>
    public void Dispose() { if (disposed) return; disposed = true; if (owns) source.Dispose(); }
    /// <inheritdoc cref="Dispose"/>
    public async ValueTask DisposeAsync() { if (disposed) return; disposed = true; if (owns) await source.DisposeAsync().ConfigureAwait(false); }
}
