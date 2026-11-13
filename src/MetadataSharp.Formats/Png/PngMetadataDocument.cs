using System.Security.Cryptography;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Internal;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Formats.Tiff;

namespace MetadataSharp.Formats.Png;

/// <summary>
/// A validated static PNG source with independently addressed EXIF and XMP operations. EXIF is the
/// eXIf chunk and XMP the XML:com.adobe.xmp iTXt chunk; both can be edited. PNG defines no chunk for
/// IPTC: it is read from and written to the "Raw profile type iptc" text chunk, as exiv2 and
/// ImageMagick do. EXIF held in a "Raw profile type" text chunk is read and not edited.
/// </summary>
/// <remarks>Borrowed streams remain open. No concurrent source/document/edit use is supported.</remarks>
public sealed partial class PngMetadataDocument : IDisposable, IAsyncDisposable
{
    private readonly Stream source;
    private readonly bool owns;
    private readonly string? sourcePath;
    private readonly PngReadOptions options;
    private readonly PngScan scan;
    private readonly byte[] digest;
    private bool disposed;
    private PngMetadataDocument(Stream source, bool owns, string? path, PngReadOptions options, PngScan scan, byte[] digest)
    { this.source = source; this.owns = owns; sourcePath = path; this.options = options; this.scan = scan; this.digest = digest; }
    /// <summary>Opens a PNG from a readable, seekable stream at position zero. The stream stays open, and has to stay unchanged, while the document is in use.</summary>
    /// <exception cref="MetadataException">The data is not a file this document reads, or it exceeds a budget of the limits.</exception>
    public static PngMetadataDocument Open(Stream source, PngReadOptions? options = null) => OpenCore(source, false, null, (options ?? new()).Snapshot(), false, default).GetAwaiter().GetResult();
    /// <summary>Opens a PNG file and keeps it open, shared for reading, until the document is disposed. Fails as <see cref="Open(Stream, PngReadOptions?)"/> does.</summary>
    public static PngMetadataDocument Open(string path, PngReadOptions? options = null) => OpenPath(path, (options ?? new()).Snapshot(), false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="Open(Stream, PngReadOptions?)"/>
    public static Task<PngMetadataDocument> OpenAsync(Stream source, PngReadOptions? options = null, CancellationToken cancellationToken = default) => OpenCore(source, false, null, (options ?? new()).Snapshot(), true, cancellationToken);
    /// <inheritdoc cref="Open(string, PngReadOptions?)"/>
    public static Task<PngMetadataDocument> OpenAsync(string path, PngReadOptions? options = null, CancellationToken cancellationToken = default) => OpenPath(path, (options ?? new()).Snapshot(), true, cancellationToken);
    // Open the source with the selected options, keeping stream ownership and cancellation explicit.
    private static async Task<PngMetadataDocument> OpenPath(string path, PngReadOptions options, bool asynchronous, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrEmpty(path); token.ThrowIfCancellationRequested(); string full = Path.GetFullPath(path);
        var file = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, options.Limits.CopyBufferBytes, FileOptions.SequentialScan | (asynchronous ? FileOptions.Asynchronous : FileOptions.None));
        return await OpenCore(file, true, full, options, asynchronous, token).ConfigureAwait(false);
    }
    // Open the source with the selected options, keeping stream ownership and cancellation explicit.
    private static async Task<PngMetadataDocument> OpenCore(Stream source, bool owns, string? path, PngReadOptions options, bool asynchronous, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            PngScan scan;
            try { scan = await PngScanner.Scan(source, options.Limits, asynchronous, token).ConfigureAwait(false); }
            catch (EndOfStreamException) { throw PngScanner.Invalid(); }
            byte[] digest = await ContainerIo.Hash(source, scan.Length, options.Limits.CopyBufferBytes, asynchronous, token).ConfigureAwait(false);
            return new(source, owns, path, options, scan, digest);
        }
        catch { if (owns) await source.DisposeAsync().ConfigureAwait(false); throw; }
    }
    // Compare the staged result with the source to confirm unrelated PNG data stayed unchanged.
    private async Task Verify(bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        byte[] current = await ContainerIo.Hash(source, scan.Length, options.Limits.CopyBufferBytes, asynchronous, token).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(digest, current)) throw ContainerIo.Changed();
    }
    // Keep Unique focused on PNG and its format-specific validation contract.
    private PngChunk? Unique(bool xmp)
    {
        PngChunk[] chunks = scan.Chunks.Where(c => xmp ? c.Xmp : c.Type == "eXIf").Take(2).ToArray();
        if (chunks.Length > 1) throw new MetadataException(MetadataErrorCode.AmbiguousMetadata, "Duplicate target PNG metadata chunks.");
        return chunks.FirstOrDefault();
    }
    // Enforce the configured PNG count, depth, or retained-byte budget before keeping more data.
    private void Budget(long bytes)
    { if (bytes > options.Limits.MaxRetainedMetadataBytes) throw new MetadataException(MetadataErrorCode.LimitExceeded, "PNG metadata exceeds its budget."); }
    // Extract PNG from the already-validated representation.
    private async Task<byte[]> Payload(PngChunk chunk, bool asynchronous, CancellationToken token)
    {
        Budget(chunk.Size); byte[] bytes = new byte[chunk.Size]; source.Position = chunk.Payload;
        await ContainerIo.Read(source, bytes, asynchronous, token).ConfigureAwait(false); return bytes;
    }
    // Keep Exif focused on PNG and its format-specific validation contract.
    private async Task<TiffReadResult> Exif(PngChunk chunk, bool asynchronous, CancellationToken token)
    {
        if (chunk.Size > 65527) throw new MetadataException(MetadataErrorCode.ExifTooLarge, "EXIF exceeds the approved classic TIFF profile cap.");
        byte[] bytes = await Payload(chunk, asynchronous, token).ConfigureAwait(false);
        var result = ExifTiffReader.Read(bytes, options.Limits, chunk.Payload); token.ThrowIfCancellationRequested(); return result;
    }
    /// <summary>The bytes of the EXIF block and where it begins in the file.</summary>
    private async Task<(byte[] Block, long At)> ExifBlock(PngChunk chunk, bool asynchronous, CancellationToken token)
    {
        if (chunk.Size > 65527) throw new MetadataException(MetadataErrorCode.ExifTooLarge, "EXIF exceeds the approved classic TIFF profile cap.");
        return (await Payload(chunk, asynchronous, token).ConfigureAwait(false), chunk.Payload);
    }
    private static MetadataException ProfileIsReadOnly(string kind) =>
        new(MetadataErrorCode.UnsupportedFeature, kind + " held in a raw profile text chunk can be read and not edited.");
    // Keep Profile focused on PNG and its format-specific validation contract.
    private PngChunk? Profile(string name)
    {
        PngChunk[] chunks = scan.Chunks.Where(c => c.Profile == name).Take(2).ToArray();
        if (chunks.Length > 1) throw new MetadataException(MetadataErrorCode.AmbiguousMetadata, "Duplicate raw profile PNG text chunks.");
        return chunks.FirstOrDefault();
    }
    // Keep ProfileBytes focused on PNG and its format-specific validation contract.
    private async Task<byte[]> ProfileBytes(PngChunk chunk, bool asynchronous, CancellationToken token)
    {
        byte[] payload = await Payload(chunk, asynchronous, token).ConfigureAwait(false);
        int at = Array.IndexOf(payload, (byte)0) + 1;
        if (at <= 0) throw PngScanner.Invalid("Malformed raw profile text chunk.");
        byte[] text;
        if (chunk.Type == "zTXt")
        {
            if (at >= payload.Length || payload[at] != 0) throw PngScanner.Invalid("Invalid zTXt compression method.");
            text = BoundedZlib.Expand(payload[(at + 1)..], options.Limits.MaxRetainedMetadataBytes, token);
        }
        else text = payload[at..];
        Budget((long)payload.Length + text.Length);
        byte[] profile = PngRawProfile.Decode(text); token.ThrowIfCancellationRequested(); return profile;
    }
    private async Task<ExifMetadata?> ProfileExif(bool asynchronous, CancellationToken token)
    {
        if (Profile("exif") is not PngChunk chunk) return null;
        byte[] block = await ProfileBytes(chunk, asynchronous, token).ConfigureAwait(false);
        int skip = block.AsSpan().StartsWith("Exif\0\0"u8) ? 6 : 0;
        // The read-only rules, whatever the block holds: its offsets count from the decoded profile,
        // which is not a range of the file, and no chunk of this file would be rewritten from it.
        return ExifTiffReader.ReadRelaxed(block.AsSpan(skip), options.Limits);
    }
    private static IptcPacket? ProfileIptc(byte[] profile, IptcLimits limits)
    {
        ReadOnlySpan<byte> stream = profile; MetadataException? refusal = null; byte[]? digest = null;
        // The profile is the IIM stream itself, or the Photoshop resources that hold it, which a
        // writer stores with or without the signature an APP13 segment begins with.
        if (profile.Length != 0 && profile[0] != 0x1C)
        {
            ReadOnlySpan<byte> signature = "Photoshop 3.0\0"u8;
            byte[] payload = stream.StartsWith(signature) ? profile : [.. signature, .. profile];
            PhotoshopResources resources = PhotoshopResources.Parse(payload, limits);
            if (resources.IptcForReading() is not PhotoshopResources.Resource resource) return null;
            // What an edit of these resources fails with, so the packet can say it is for reading only.
            stream = resource.Data.Span; refusal = resources.EditRefusal(); digest = resources.StoredDigest();
        }
        return IptcPacket.Inspect(stream, new() { Limits = limits }, refusal).WithDigest(digest);
    }
    /// <summary>
    /// Reads the IPTC stream of the "Raw profile type iptc" text chunk, or returns null when the file
    /// has none. The profile is the IIM stream itself or Photoshop image resources that hold it. A
    /// stream that can be read and not edited comes back with <see cref="IptcPacket.IsEditable"/>
    /// false, and <see cref="SaveIptc(Stream, IptcEdit)"/> refuses a change to it.
    /// </summary>
    public IptcPacket? ReadIptc(IptcReadOptions? options = null) => ReadIptcCore(options, false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="ReadIptc"/>
    public Task<IptcPacket?> ReadIptcAsync(IptcReadOptions? options = null, CancellationToken cancellationToken = default) => ReadIptcCore(options, true, cancellationToken);
    // Read IPTC core from bounded PNG data, validating its declared extent before consuming fields.
    private async Task<IptcPacket?> ReadIptcCore(IptcReadOptions? settings, bool asynchronous, CancellationToken token)
    {
        IptcLimits limits = IptcPacket.Settings(settings);
        await Verify(asynchronous, token).ConfigureAwait(false);
        if (Profile("iptc") is not PngChunk chunk) return null;
        byte[] profile = await ProfileBytes(chunk, asynchronous, token).ConfigureAwait(false);
        return ProfileIptc(profile, limits);
    }
    /// <summary>
    /// Reads the EXIF block of the eXIf chunk. A file without that chunk is searched for a
    /// "Raw profile type exif" text chunk, whose block comes back with
    /// <see cref="ExifMetadata.IsEditable"/> false; <see cref="SaveExif"/> refuses a change to it.
    /// </summary>
    public ExifMetadata? ReadExif() => ReadExifCore(false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="ReadExif"/>
    public Task<ExifMetadata?> ReadExifAsync(CancellationToken cancellationToken = default) => ReadExifCore(true, cancellationToken);
    private async Task<ExifMetadata?> ReadExifCore(bool asynchronous, CancellationToken token)
    {
        await Verify(asynchronous, token).ConfigureAwait(false); PngChunk? chunk = Unique(false);
        if (chunk is null) return await ProfileExif(asynchronous, token).ConfigureAwait(false);
        try { return (await Exif(chunk, asynchronous, token).ConfigureAwait(false)).Metadata; }
        catch (MetadataException failure) when (ExifTiffReader.IsRelaxable(failure))
        {
            // Read-only rules for a block the editable profile refuses; SaveExif keeps refusing it.
            byte[] bytes = await Payload(chunk, asynchronous, token).ConfigureAwait(false);
            ExifMetadata relaxed = ExifTiffReader.ReadRelaxed(bytes, options.Limits, chunk.Payload); token.ThrowIfCancellationRequested(); return relaxed;
        }
    }
    /// <summary>Reads the packet of the XML:com.adobe.xmp iTXt chunk, or returns null when the file has none.</summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.AmbiguousMetadata"/> when the file has two such chunks;
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when the packet, compressed or not, exceeds a
    /// budget; otherwise as <see cref="XmpPacket.Parse"/> fails.
    /// </exception>
    public XmpPacket? ReadXmp(XmpReadOptions? options = null) => ReadXmpCore(options, false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="ReadXmp"/>
    public Task<XmpPacket?> ReadXmpAsync(XmpReadOptions? options = null, CancellationToken cancellationToken = default) => ReadXmpCore(options, true, cancellationToken);
    // Read XMP core from bounded PNG data, validating its declared extent before consuming fields.
    private async Task<XmpPacket?> ReadXmpCore(XmpReadOptions? settings, bool asynchronous, CancellationToken token)
    {
        await Verify(asynchronous, token).ConfigureAwait(false);
        if (settings is not null) (settings.Limits ?? throw new ArgumentException("XMP limits cannot be null.")).Validate();
        PngChunk? chunk = Unique(true); return chunk is null ? null : await Xmp(chunk, settings, asynchronous, token).ConfigureAwait(false);
    }
    private async Task<XmpPacket> Xmp(PngChunk chunk, XmpReadOptions? settings, bool asynchronous, CancellationToken token)
    {
        // Bound the encoded input before allocating its payload.
        if (chunk.Size < 22) throw PngScanner.Invalid("Malformed XMP iTXt fields.");
        byte[] mode = new byte[2]; source.Position = chunk.Payload + 18;
        await ContainerIo.Read(source, mode, asynchronous, token).ConfigureAwait(false);
        if (mode[0] == 1 && chunk.Size > (long)options.MaxCompressedXmpBytes + 22) throw BoundedZlib.Limit();
        byte[] payload = await Payload(chunk, asynchronous, token).ConfigureAwait(false);
        const int keyword = 18; // 17-byte keyword plus NUL.
        if (payload.Length < keyword + 4 || !payload.AsSpan().StartsWith("XML:com.adobe.xmp\0"u8)) throw PngScanner.Invalid("Malformed XMP iTXt fields.");
        int flag = payload[keyword], method = payload[keyword + 1], at = keyword + 2;
        int language = Array.IndexOf(payload, (byte)0, at); if (language < 0) throw PngScanner.Invalid();
        int translated = Array.IndexOf(payload, (byte)0, language + 1); if (translated < 0 || flag > 1) throw PngScanner.Invalid();
        if (language != at || translated != language + 1) throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "XMP language and translated keyword must be empty.");
        at = translated + 1;
        int maximum = Math.Min(options.MaxDecompressedXmpBytes, Math.Min(options.Limits.MaxRetainedMetadataBytes, settings?.Limits.MaxPacketBytes ?? new XmpLimits().MaxPacketBytes));
        byte[] text;
        if (flag == 0)
        {
            // PNG decoders ignore compression method for uncompressed iTXt.
            if (payload.Length - at > maximum) throw BoundedZlib.Limit(); text = payload[at..];
        }
        else
        {
            if (method != 0) throw PngScanner.Invalid("Invalid XMP iTXt compression method.");
            if (payload.Length - at > options.MaxCompressedXmpBytes) throw BoundedZlib.Limit();
            text = BoundedZlib.Expand(payload[at..], maximum, token);
        }
        Budget((long)payload.Length + text.Length + scan.Chunks.Where(c => c.Type == "eXIf").Sum(c => (long)c.Size));
        XmpPacket result = XmpPacket.Parse(text, settings); token.ThrowIfCancellationRequested(); return result;
    }
    private sealed record Prepared(bool Copy, PngChunk? Target, byte[]? Chunk);
    private async Task<Prepared> Prepare(ExifEdit? exif, XmpEdit? xmp, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var es = exif?.Snapshot(); var xs = xmp?.Snapshot();
        await Verify(asynchronous, token).ConfigureAwait(false);
        bool isXmp = xmp is not null;
        bool empty = isXmp ? xs!.IsEmpty : es!.IsEmpty;
        if (empty) return new(true, null, null);
        PngChunk? chunk = Unique(isXmp);
        // Writing an eXIf chunk beside a raw profile would leave the file with two EXIF blocks.
        if (!isXmp && chunk is null && Profile("exif") is not null) throw ProfileIsReadOnly("EXIF");
        bool remove = isXmp ? xs!.RemovesAll : es!.RemoveAll;
        if (remove && chunk is null) return new(true, null, null);
        byte[]? bytes = null;
        if (!remove && isXmp)
        {
            XmpPacket original = chunk is null ? XmpPacket.CreateEmpty() : await Xmp(chunk, null, asynchronous, token).ConfigureAwait(false);
            XmpPacket changed = original.Apply(xs!, 0, 0)!;
            // An edit that leaves the packet as it is, or leaves a file without a packet without one, is a copy.
            if (ReferenceEquals(original, changed)) return new(true, null, null);
            Budget((long)changed.ByteLength + original.ByteLength + scan.Chunks.Where(c => c.Type == "eXIf").Sum(c => (long)c.Size));
            if (changed.ByteLength > options.MaxDecompressedXmpBytes) throw BoundedZlib.Limit();
            bytes = new byte[changed.ByteLength + 22]; "XML:com.adobe.xmp\0"u8.CopyTo(bytes); changed.CopyRawTo(bytes.AsSpan(22));
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
        if (scan.Chunks.Any(c => c.UnsafeUnknown)) throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "An unknown unsafe-to-copy PNG chunk prevents editing.");
        byte[]? rebuilt = bytes is null ? null : Crc.Chunk(isXmp ? "iTXt" : "eXIf", bytes);
        token.ThrowIfCancellationRequested(); return new(false, chunk, rebuilt);
    }
    // Write the prepared PNG edit only after validation, preserving unrelated payloads.
    private async Task Emit(Stream output, Prepared prepared, bool asynchronous, CancellationToken token)
    {
        if (prepared.Copy) { await ContainerIo.Copy(source, output, 0, scan.Length, options.Limits.CopyBufferBytes, asynchronous, token).ConfigureAwait(false); return; }
        long start = prepared.Target?.Start ?? scan.Insertion, end = prepared.Target is null ? start : start + prepared.Target.Length;
        await ContainerIo.Copy(source, output, 0, start, options.Limits.CopyBufferBytes, asynchronous, token).ConfigureAwait(false);
        if (prepared.Chunk is not null) await ContainerIo.Write(output, prepared.Chunk, asynchronous, token).ConfigureAwait(false);
        await ContainerIo.Copy(source, output, end, scan.Length - end, options.Limits.CopyBufferBytes, asynchronous, token).ConfigureAwait(false);
    }
    /// <summary>
    /// Writes the file with the EXIF edit applied to a distinct, writable, seekable, empty stream. An edit that changes nothing gives a copy of the source.
    /// The EXIF block is the eXIf chunk, created when the file has none.
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
    /// The packet is the XML:com.adobe.xmp iTXt chunk, created when the file has none.
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
    // Write the prepared PNG edit only after validation, preserving unrelated payloads.
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
