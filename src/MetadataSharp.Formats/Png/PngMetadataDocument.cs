using System.Security.Cryptography;
using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Core.Tiff.Serialization;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Internal;

namespace MetadataSharp.Formats.Png;

/// <summary>A validated static PNG source with independently addressed EXIF and XMP operations.</summary>
/// <remarks>Borrowed streams remain open. No concurrent source/document/edit use is supported.</remarks>
public sealed class PngMetadataDocument : IDisposable, IAsyncDisposable
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
    public static PngMetadataDocument Open(Stream source, PngReadOptions? options = null) => OpenCore(source, false, null, (options ?? new()).Snapshot(), false, default).GetAwaiter().GetResult();
    public static PngMetadataDocument Open(string path, PngReadOptions? options = null) => OpenPath(path, (options ?? new()).Snapshot(), false, default).GetAwaiter().GetResult();
    public static Task<PngMetadataDocument> OpenAsync(Stream source, PngReadOptions? options = null, CancellationToken cancellationToken = default) => OpenCore(source, false, null, (options ?? new()).Snapshot(), true, cancellationToken);
    public static Task<PngMetadataDocument> OpenAsync(string path, PngReadOptions? options = null, CancellationToken cancellationToken = default) => OpenPath(path, (options ?? new()).Snapshot(), true, cancellationToken);
    private static async Task<PngMetadataDocument> OpenPath(string path, PngReadOptions options, bool asynchronous, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrEmpty(path); token.ThrowIfCancellationRequested(); string full = Path.GetFullPath(path);
        var file = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, options.Limits.CopyBufferBytes, FileOptions.SequentialScan | (asynchronous ? FileOptions.Asynchronous : FileOptions.None));
        return await OpenCore(file, true, full, options, asynchronous, token).ConfigureAwait(false);
    }
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
    private async Task Verify(bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        byte[] current = await ContainerIo.Hash(source, scan.Length, options.Limits.CopyBufferBytes, asynchronous, token).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(digest, current)) throw ContainerIo.Changed();
    }
    private PngChunk? Unique(bool xmp)
    {
        PngChunk[] chunks = scan.Chunks.Where(c => xmp ? c.Xmp : c.Type == "eXIf").Take(2).ToArray();
        if (chunks.Length > 1) throw new MetadataException(MetadataErrorCode.AmbiguousMetadata, "Duplicate target PNG metadata chunks.");
        return chunks.FirstOrDefault();
    }
    private void Budget(long bytes)
    { if (bytes > options.Limits.MaxRetainedMetadataBytes) throw new MetadataException(MetadataErrorCode.LimitExceeded, "PNG metadata exceeds its budget."); }
    private async Task<byte[]> Payload(PngChunk chunk, bool asynchronous, CancellationToken token)
    {
        Budget(chunk.Size); byte[] bytes = new byte[chunk.Size]; source.Position = chunk.Payload;
        await ContainerIo.Read(source, bytes, asynchronous, token).ConfigureAwait(false); return bytes;
    }
    private async Task<TiffReadResult> Exif(PngChunk chunk, bool asynchronous, CancellationToken token)
    {
        if (chunk.Size > 65527) throw new MetadataException(MetadataErrorCode.ExifTooLarge, "EXIF exceeds the approved classic TIFF profile cap.");
        byte[] bytes = await Payload(chunk, asynchronous, token).ConfigureAwait(false);
        var result = ExifTiffReader.Read(bytes, options.Limits, chunk.Payload); token.ThrowIfCancellationRequested(); return result;
    }
    public ExifMetadata? ReadExif() => ReadExifCore(false, default).GetAwaiter().GetResult();
    public Task<ExifMetadata?> ReadExifAsync(CancellationToken cancellationToken = default) => ReadExifCore(true, cancellationToken);
    private async Task<ExifMetadata?> ReadExifCore(bool asynchronous, CancellationToken token)
    {
        await Verify(asynchronous, token).ConfigureAwait(false); PngChunk? chunk = Unique(false);
        return chunk is null ? null : (await Exif(chunk, asynchronous, token).ConfigureAwait(false)).Metadata;
    }
    public XmpPacket? ReadXmp(XmpReadOptions? options = null) => ReadXmpCore(options, false, default).GetAwaiter().GetResult();
    public Task<XmpPacket?> ReadXmpAsync(XmpReadOptions? options = null, CancellationToken cancellationToken = default) => ReadXmpCore(options, true, cancellationToken);
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
        bool empty = isXmp ? !xs!.Value.RemoveAll && xs.Value.Changes.Count == 0 : es!.IsEmpty;
        if (empty) return new(true, null, null);
        PngChunk? chunk = Unique(isXmp);
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
            Budget((long)changed.ByteLength + original.ByteLength + scan.Chunks.Where(c => c.Type == "eXIf").Sum(c => (long)c.Size));
            if (changed.ByteLength > options.MaxDecompressedXmpBytes) throw BoundedZlib.Limit();
            bytes = new byte[changed.ByteLength + 22]; "XML:com.adobe.xmp\0"u8.CopyTo(bytes); changed.CopyRawTo(bytes.AsSpan(22));
        }
        else if (!remove)
        {
            TiffReadResult? original = chunk is null ? null : await Exif(chunk, asynchronous, token).ConfigureAwait(false);
            if (es!.Changes.All(pair => pair.Value is null && (original is null || !original.Metadata.TryGetEntry(pair.Key, out _)))) return new(true, null, null);
            var desired = es!.Apply(original, original?.Metadata.ByteOrder ?? Endianness.LittleEndian, options.Limits);
            if (original is null && desired.Entries.Count == 0) return new(true, null, null);
            ExifWritePlan plan = ExifLayoutPlanner.Create(original, desired, options.Limits); Budget((long)plan.Length + (chunk?.Size ?? 0) + scan.Chunks.Where(c => c.Xmp).Sum(c => (long)c.Size));
            bytes = new byte[plan.Length]; ExifTiffSerializer.Write(plan, bytes);
        }
        if (scan.Chunks.Any(c => c.UnsafeUnknown)) throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "An unknown unsafe-to-copy PNG chunk prevents editing.");
        byte[]? rebuilt = bytes is null ? null : Crc.Chunk(isXmp ? "iTXt" : "eXIf", bytes);
        token.ThrowIfCancellationRequested(); return new(false, chunk, rebuilt);
    }
    private async Task Emit(Stream output, Prepared prepared, bool asynchronous, CancellationToken token)
    {
        if (prepared.Copy) { await ContainerIo.Copy(source, output, 0, scan.Length, options.Limits.CopyBufferBytes, asynchronous, token).ConfigureAwait(false); return; }
        long start = prepared.Target?.Start ?? scan.Insertion, end = prepared.Target is null ? start : start + prepared.Target.Length;
        await ContainerIo.Copy(source, output, 0, start, options.Limits.CopyBufferBytes, asynchronous, token).ConfigureAwait(false);
        if (prepared.Chunk is not null) await ContainerIo.Write(output, prepared.Chunk, asynchronous, token).ConfigureAwait(false);
        await ContainerIo.Copy(source, output, end, scan.Length - end, options.Limits.CopyBufferBytes, asynchronous, token).ConfigureAwait(false);
    }
    public void SaveExif(Stream destination, ExifEdit edit) { ArgumentNullException.ThrowIfNull(edit); SaveCore(destination, edit, null, false, default).GetAwaiter().GetResult(); }
    public Task SaveExifAsync(Stream destination, ExifEdit edit, CancellationToken cancellationToken = default) { ArgumentNullException.ThrowIfNull(edit); return SaveCore(destination, edit, null, true, cancellationToken); }
    public void SaveXmp(Stream destination, XmpEdit edit) { ArgumentNullException.ThrowIfNull(edit); SaveCore(destination, null, edit, false, default).GetAwaiter().GetResult(); }
    public Task SaveXmpAsync(Stream destination, XmpEdit edit, CancellationToken cancellationToken = default) { ArgumentNullException.ThrowIfNull(edit); return SaveCore(destination, null, edit, true, cancellationToken); }
    private async Task SaveCore(Stream destination, ExifEdit? exif, XmpEdit? xmp, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ContainerIo.Output(source, destination);
        Prepared prepared = await Prepare(exif, xmp, asynchronous, token).ConfigureAwait(false); await Emit(destination, prepared, asynchronous, token).ConfigureAwait(false);
    }
    public void SaveExifAs(string destinationPath, ExifEdit edit) { ArgumentNullException.ThrowIfNull(edit); SavePath(destinationPath, edit, null, false, default).GetAwaiter().GetResult(); }
    public Task SaveExifAsAsync(string destinationPath, ExifEdit edit, CancellationToken cancellationToken = default) { ArgumentNullException.ThrowIfNull(edit); return SavePath(destinationPath, edit, null, true, cancellationToken); }
    public void SaveXmpAs(string destinationPath, XmpEdit edit) { ArgumentNullException.ThrowIfNull(edit); SavePath(destinationPath, null, edit, false, default).GetAwaiter().GetResult(); }
    public Task SaveXmpAsAsync(string destinationPath, XmpEdit edit, CancellationToken cancellationToken = default) { ArgumentNullException.ThrowIfNull(edit); return SavePath(destinationPath, null, edit, true, cancellationToken); }
    private async Task SavePath(string path, ExifEdit? exif, XmpEdit? xmp, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this); string target = ContainerIo.Target(path, sourcePath);
        Prepared prepared = await Prepare(exif, xmp, asynchronous, token).ConfigureAwait(false);
        await ContainerIo.Publish(target, options.Limits.CopyBufferBytes, asynchronous, token, output => Emit(output, prepared, asynchronous, token)).ConfigureAwait(false);
    }
    public void Dispose() { if (disposed) return; disposed = true; if (owns) source.Dispose(); }
    public async ValueTask DisposeAsync() { if (disposed) return; disposed = true; if (owns) await source.DisposeAsync().ConfigureAwait(false); }
}
