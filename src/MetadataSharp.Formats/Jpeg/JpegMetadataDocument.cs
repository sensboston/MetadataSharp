using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using MetadataSharp.Core;
using MetadataSharp.Core.Tiff;
using MetadataSharp.Formats.Internal;
using MetadataSharp.Formats.Tiff;

namespace MetadataSharp.Formats.Jpeg;

/// <summary>A validated JPEG source and independently owned EXIF snapshot.</summary>
/// <remarks>Do not concurrently modify or use the source, document or edit. Caller streams remain open.</remarks>
public sealed partial class JpegMetadataDocument : IDisposable, IAsyncDisposable
{
    private readonly Stream source;
    private readonly bool ownsSource;
    private readonly string? sourcePath;
    private readonly MetadataLimits limits;
    private readonly JpegScanResult scan;
    private readonly TiffReadResult? tiff;
    private readonly ExifMetadata? exif;
    private readonly MetadataException? exifRefusal;
    private readonly byte[] digest;
    private bool disposed;

    private JpegMetadataDocument(Stream source, bool ownsSource, string? sourcePath, MetadataLimits limits,
        JpegScanResult scan, TiffReadResult? tiff, ExifMetadata? exif, MetadataException? exifRefusal, byte[] digest)
    {
        this.source = source; this.ownsSource = ownsSource; this.sourcePath = sourcePath; this.limits = limits; this.scan = scan;
        this.tiff = tiff; this.exif = exif; this.exifRefusal = exifRefusal; this.digest = digest;
    }

    /// <summary>
    /// The EXIF metadata, or null when the file has none. A block the editable profile refuses for a
    /// structure that is valid to read is returned with <see cref="ExifMetadata.IsEditable"/> false;
    /// saving an EXIF edit of such a file fails with the error the editable profile raised.
    /// </summary>
    public ExifMetadata? Exif => exif;

    /// <summary>Opens a JPEG file and keeps it open, shared for reading, until the document is disposed. Fails as <see cref="Open(Stream, JpegReadOptions?)"/> does.</summary>
    public static JpegMetadataDocument Open(string path, JpegReadOptions? options = null) =>
        OpenPath(path, SnapshotLimits(options), false, default).GetAwaiter().GetResult();
    /// <summary>Opens a JPEG from a readable, seekable stream at position zero. The stream stays open, and has to stay unchanged, while the document is in use.</summary>
    /// <exception cref="MetadataException">The data is not a file this document reads, or it exceeds a budget of the limits.</exception>
    public static JpegMetadataDocument Open(Stream source, JpegReadOptions? options = null) =>
        OpenCore(source, false, null, SnapshotLimits(options), false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="Open(string, JpegReadOptions?)"/>
    public static Task<JpegMetadataDocument> OpenAsync(string path, JpegReadOptions? options = null, CancellationToken cancellationToken = default) =>
        OpenPath(path, SnapshotLimits(options), true, cancellationToken);
    /// <inheritdoc cref="Open(Stream, JpegReadOptions?)"/>
    public static Task<JpegMetadataDocument> OpenAsync(Stream source, JpegReadOptions? options = null, CancellationToken cancellationToken = default) =>
        OpenCore(source, false, null, SnapshotLimits(options), true, cancellationToken);

    /// <summary>
    /// Opens a document for reading only, as <see cref="ImageMetadata"/> does. An EXIF block its
    /// reader refuses is noted in <paramref name="refusals"/> and the document opens without one,
    /// so that the IPTC and the XMP of the file can still be read; such a document is not for saving.
    /// </summary>
    internal static JpegMetadataDocument Open(Stream source, MetadataLimits? limits, KindRefusals refusals) =>
        OpenCore(source, false, null, SnapshotLimits(limits is null ? null : new JpegReadOptions { Limits = limits }), false, default, refusals)
            .GetAwaiter().GetResult();

    private static MetadataLimits SnapshotLimits(JpegReadOptions? options)
    {
        MetadataLimits settings = options is null ? new() : options.Limits ?? throw new ArgumentException("Limits cannot be null.", nameof(options));
        settings.Validate();
        return settings with { };
    }

    private static async Task<JpegMetadataDocument> OpenPath(string path, MetadataLimits settings, bool asynchronous, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        token.ThrowIfCancellationRequested();
        string fullPath = Path.GetFullPath(path);
        var file = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, settings.CopyBufferBytes,
            FileOptions.SequentialScan | (asynchronous ? FileOptions.Asynchronous : FileOptions.None));
        return await OpenCore(file, true, fullPath, settings, asynchronous, token).ConfigureAwait(false);
    }

    private static async Task<JpegMetadataDocument> OpenCore(Stream source, bool owns, string? path, MetadataLimits settings, bool asynchronous, CancellationToken token,
        KindRefusals? refusals = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            JpegScanResult scan = asynchronous ? await JpegScanner.ScanAsync(source, settings, token).ConfigureAwait(false) : JpegScanner.Scan(source, settings);
            TiffReadResult? tiff = null;
            ExifMetadata? metadata = null;
            MetadataException? refusal = null;
            if (scan.Exif is JpegSegment exif)
            {
                try
                {
                    int size = exif.PayloadLength - 6;
                    if (size > settings.MaxRetainedMetadataBytes)
                        throw new MetadataException(MetadataErrorCode.LimitExceeded, "The EXIF input exceeds its budget.");
                    byte[] bytes = new byte[size];
                    source.Position = exif.PayloadOffset + 6;
                    await ReadExact(source, bytes, asynchronous, token).ConfigureAwait(false);
                    try
                    {
                        tiff = ExifTiffReader.Read(bytes, settings, exif.PayloadOffset + 6);
                        metadata = tiff.Metadata;
                    }
                    catch (MetadataException failure) when (ExifTiffReader.IsRelaxable(failure))
                    {
                        // Readable, not editable: the strict result stays absent and its error is kept for Save.
                        metadata = ExifTiffReader.ReadRelaxed(bytes, settings, exif.PayloadOffset + 6);
                        refusal = failure;
                    }
                }
                catch (MetadataException unread) when (refusals is not null)
                {
                    // Only for a read of every kind: the block is absent and the other kinds stand.
                    tiff = null; metadata = null; refusal = null;
                    refusals.Exif = unread;
                }
            }
            byte[] hash = await Hash(source, scan.Length, settings.CopyBufferBytes, asynchronous, token).ConfigureAwait(false);
            return new(source, owns, path, settings, scan, tiff, metadata, refusal, hash);
        }
        catch
        {
            if (owns) { if (asynchronous) await source.DisposeAsync().ConfigureAwait(false); else source.Dispose(); }
            throw;
        }
    }

    /// <summary>
    /// Writes the file with the EXIF edit applied to a distinct, writable, seekable, empty stream. An edit that changes nothing gives a copy of the source.
    /// The EXIF block is the <c>Exif</c> APP1 segment, created when the file has none.
    /// </summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.SourceChanged"/> when the source is no longer the data that was
    /// opened; otherwise the code of the reason the edit is refused.
    /// </exception>
    public void Save(Stream destination, ExifEdit edit) => SaveCore(destination, edit, false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="Save(Stream, ExifEdit)"/>
    public Task SaveAsync(Stream destination, ExifEdit edit, CancellationToken cancellationToken = default) => SaveCore(destination, edit, true, cancellationToken);
    /// <summary>As <see cref="Save(Stream, ExifEdit)"/>, to a new path; the file appears there complete or not at all.</summary>
    /// <exception cref="IOException">The destination exists or is the source.</exception>
    public void SaveAs(string destinationPath, ExifEdit edit) => SavePath(destinationPath, edit, false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveAs(string, ExifEdit)"/>
    public Task SaveAsAsync(string destinationPath, ExifEdit edit, CancellationToken cancellationToken = default) => SavePath(destinationPath, edit, true, cancellationToken);

    private async Task SaveCore(Stream destination, ExifEdit edit, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(edit);
        if (ReferenceEquals(source, destination) || !destination.CanWrite || !destination.CanSeek || destination.Position != 0 || destination.Length != 0)
            throw new ArgumentException("The destination must be a distinct writable, seekable, empty stream at zero.", nameof(destination));
        Prepared prepared = await Prepare(edit.Snapshot(), asynchronous, token).ConfigureAwait(false);
        await Emit(destination, prepared, asynchronous, token).ConfigureAwait(false);
    }

    private async Task SavePath(string destinationPath, ExifEdit edit, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(destinationPath);
        ArgumentNullException.ThrowIfNull(edit);
        string target = Path.GetFullPath(destinationPath);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if ((sourcePath is not null && string.Equals(target, sourcePath, comparison)) || File.Exists(target) || Directory.Exists(target))
            throw new IOException("The destination must be a new path distinct from the source.");
        Prepared prepared = await Prepare(edit.Snapshot(), asynchronous, token).ConfigureAwait(false);
        string temporary = Path.Combine(Path.GetDirectoryName(target)!, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        bool created = false;
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                limits.CopyBufferBytes, asynchronous ? FileOptions.Asynchronous : FileOptions.None))
            {
                created = true;
                await Emit(output, prepared, asynchronous, token).ConfigureAwait(false);
                if (asynchronous) await output.FlushAsync(token).ConfigureAwait(false); else output.Flush();
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: false);
            created = false; // Publication is the commit point. Never check cancellation after it.
        }
        finally
        {
            if (created)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private sealed record Prepared(bool CopyOnly, byte[]? Tiff);

    private async Task<Prepared> Prepare(ExifEdit.EditSnapshot edit, bool asynchronous, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        byte[] current = await Hash(source, scan.Length, limits.CopyBufferBytes, asynchronous, token).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(digest, current))
            throw new MetadataException(MetadataErrorCode.SourceChanged, "The source content has changed since opening.");
        if (edit.IsEmpty) return new(true, null);
        // The block was opened for reading only. Editing it refuses as opening it used to.
        if (exifRefusal is not null) throw new MetadataException(exifRefusal.Code, exifRefusal.Message, exifRefusal.SourceOffset);
        if (edit.RemoveAll) return new(scan.Exif is null, null);
        byte[]? bytes;
        if (scan.Exif is JpegSegment segment)
        {
            // The block is edited where it lies: nothing in it moves, and what grows is appended.
            byte[] block = new byte[segment.PayloadLength - 6];
            source.Position = segment.PayloadOffset + 6;
            await ReadExact(source, block, asynchronous, token).ConfigureAwait(false);
            bytes = ExifBlockEditor.Edit(block, segment.PayloadOffset + 6, edit, limits);
        }
        else
        {
            bytes = ExifBlockEditor.Create(edit, limits, ExifTiffReader.SegmentCeiling, out _);
        }
        token.ThrowIfCancellationRequested();
        return bytes is null ? new(true, null) : new(false, bytes);
    }

    private async Task Emit(Stream output, Prepared prepared, bool asynchronous, CancellationToken token)
    {
        if (prepared.CopyOnly)
        { await CopyRange(output, 0, scan.Length, asynchronous, token).ConfigureAwait(false); return; }
        long start = scan.Exif?.Start ?? scan.InsertionOffset;
        long end = scan.Exif is null ? start : scan.Exif.Start + scan.Exif.Length;
        await CopyRange(output, 0, start, asynchronous, token).ConfigureAwait(false);
        if (prepared.Tiff is byte[] bytes)
        {
            byte[] header = [0xFF, 0xE1, 0, 0, 69, 120, 105, 102, 0, 0];
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2), (ushort)(bytes.Length + 8));
            await Write(output, header, asynchronous, token).ConfigureAwait(false);
            await Write(output, bytes, asynchronous, token).ConfigureAwait(false);
        }
        await CopyRange(output, end, scan.Length - end, asynchronous, token).ConfigureAwait(false);
    }

    private async Task CopyRange(Stream output, long at, long count, bool asynchronous, CancellationToken token)
    {
        source.Position = at;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(limits.CopyBufferBytes);
        try
        {
            while (count > 0)
            {
                token.ThrowIfCancellationRequested();
                int size = (int)Math.Min(count, limits.CopyBufferBytes);
                int n = asynchronous ? await source.ReadAsync(buffer.AsMemory(0, size), token).ConfigureAwait(false) : source.Read(buffer, 0, size);
                if (n == 0) throw new IOException("The source ended during copying.");
                await Write(output, buffer.AsMemory(0, n), asynchronous, token).ConfigureAwait(false);
                count -= n;
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private static async ValueTask Write(Stream output, ReadOnlyMemory<byte> bytes, bool asynchronous, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (asynchronous) await output.WriteAsync(bytes, token).ConfigureAwait(false);
        else output.Write(bytes.Span);
    }

    private static async Task ReadExact(Stream input, Memory<byte> bytes, bool asynchronous, CancellationToken token)
    {
        while (!bytes.IsEmpty)
        {
            token.ThrowIfCancellationRequested();
            int n = asynchronous ? await input.ReadAsync(bytes, token).ConfigureAwait(false) : input.Read(bytes.Span);
            if (n == 0) throw new MetadataException(MetadataErrorCode.SourceChanged, "The source ended during EXIF reading.");
            bytes = bytes[n..];
        }
    }

    private static async Task<byte[]> Hash(Stream input, long expectedLength, int capacity, bool asynchronous, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (input.Length != expectedLength) throw new MetadataException(MetadataErrorCode.SourceChanged, "The source length has changed.");
        input.Position = 0;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(capacity);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            long remaining = expectedLength;
            while (remaining > 0)
            {
                token.ThrowIfCancellationRequested();
                int size = (int)Math.Min(remaining, capacity);
                int n = asynchronous ? await input.ReadAsync(buffer.AsMemory(0, size), token).ConfigureAwait(false) : input.Read(buffer, 0, size);
                if (n == 0) throw new MetadataException(MetadataErrorCode.SourceChanged, "The source ended during identity verification.");
                hash.AppendData(buffer, 0, n); remaining -= n;
            }
            if (input.Length != expectedLength) throw new MetadataException(MetadataErrorCode.SourceChanged, "The source length changed during identity verification.");
            return hash.GetHashAndReset();
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    /// <summary>Closes the file when the document opened it from a path; a stream the caller gave stays open.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (ownsSource) source.Dispose();
    }

    /// <inheritdoc cref="Dispose"/>
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        if (ownsSource) await source.DisposeAsync().ConfigureAwait(false);
    }
}
