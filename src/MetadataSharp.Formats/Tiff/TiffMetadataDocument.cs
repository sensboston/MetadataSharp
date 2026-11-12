using System.Security.Cryptography;
using MetadataSharp.Core;
using MetadataSharp.Formats.Internal;

namespace MetadataSharp.Formats.Tiff;

/// <summary>Restricted baseline TIFF editing by append-preserve replacement of the active graph.</summary>
/// <remarks>Removed/obsolete metadata remains physically present. No concurrent use is supported.</remarks>
public sealed class TiffMetadataDocument : IDisposable, IAsyncDisposable
{
    private readonly Stream source;
    private readonly bool owns;
    private readonly string? sourcePath;
    private readonly TiffEditOptions options;
    private readonly byte[] digest;
    private bool disposed;
    private TiffMetadataDocument(Stream source, bool owns, string? path, TiffEditOptions options, TiffLayout layout, byte[] digest)
    { this.source = source; this.owns = owns; sourcePath = path; this.options = options; Layout = layout; this.digest = digest; }
    /// <summary>The layout of the source: its directories, the ranges they account for, and the issues found.</summary>
    public TiffLayout Layout { get; }
    /// <summary>Opens a baseline TIFF from a readable, seekable stream at position zero. The stream stays open, and has to stay unchanged, while the document is in use.</summary>
    /// <exception cref="MetadataException">The data is not a file this document reads, or it exceeds a budget of the limits.</exception>
    public static TiffMetadataDocument Open(Stream source, TiffEditOptions? options = null) => OpenCore(source, false, null, (options ?? new()).Snapshot(), false, default).GetAwaiter().GetResult();
    /// <summary>Opens a baseline TIFF file and keeps it open, shared for reading, until the document is disposed. Fails as <see cref="Open(Stream, TiffEditOptions?)"/> does.</summary>
    public static TiffMetadataDocument Open(string path, TiffEditOptions? options = null) => OpenPath(path, (options ?? new()).Snapshot(), false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="Open(Stream, TiffEditOptions?)"/>
    public static Task<TiffMetadataDocument> OpenAsync(Stream source, TiffEditOptions? options = null, CancellationToken cancellationToken = default) => OpenCore(source, false, null, (options ?? new()).Snapshot(), true, cancellationToken);
    /// <inheritdoc cref="Open(string, TiffEditOptions?)"/>
    public static Task<TiffMetadataDocument> OpenAsync(string path, TiffEditOptions? options = null, CancellationToken cancellationToken = default) => OpenPath(path, (options ?? new()).Snapshot(), true, cancellationToken);
    private static async Task<TiffMetadataDocument> OpenPath(string path, TiffEditOptions options, bool asynchronous, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrEmpty(path); token.ThrowIfCancellationRequested(); string full = Path.GetFullPath(path);
        var file = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, options.ReadOptions.Limits.CopyBufferBytes, asynchronous ? FileOptions.Asynchronous : FileOptions.None);
        return await OpenCore(file, true, full, options, asynchronous, token).ConfigureAwait(false);
    }
    private static async Task<TiffMetadataDocument> OpenCore(Stream source, bool owns, string? path, TiffEditOptions options, bool asynchronous, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            TiffLayout layout = await TiffLayoutReader.Read(source, options.ReadOptions, asynchronous, token).ConfigureAwait(false);
            byte[] hash = await ContainerIo.Hash(source, layout.SourceLength, options.ReadOptions.Limits.CopyBufferBytes, asynchronous, token).ConfigureAwait(false);
            return new(source, owns, path, options, layout, hash);
        }
        catch { if (owns) await source.DisposeAsync().ConfigureAwait(false); throw; }
    }
    private async Task Verify(bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        byte[] hash = await ContainerIo.Hash(source, Layout.SourceLength, options.ReadOptions.Limits.CopyBufferBytes, asynchronous, token).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(hash, digest)) throw ContainerIo.Changed();
    }
    private async Task<byte[]> ReadAt(long offset, int count, bool asynchronous, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); source.Position = offset; byte[] bytes = new byte[count];
        await ContainerIo.Read(source, bytes, asynchronous, token).ConfigureAwait(false); return bytes;
    }
    /// <summary>Reads a descriptive field of a source directory, or returns null when the directory does not have it.</summary>
    /// <exception cref="ArgumentException">The key names no directory of the source, or a tag outside the descriptive set of <see cref="TiffMetadataEdit"/>.</exception>
    /// <exception cref="MetadataException"><see cref="MetadataErrorCode.UnsupportedFeature"/> when the stored field does not have the shape or content the set allows.</exception>
    public ExifValue? ReadValue(TiffFieldKey key) => ReadValueCore(key, false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="ReadValue"/>
    public Task<ExifValue?> ReadValueAsync(TiffFieldKey key, CancellationToken cancellationToken = default) => ReadValueCore(key, true, cancellationToken);
    private async Task<ExifValue?> ReadValueCore(TiffFieldKey key, bool asynchronous, CancellationToken token)
    {
        TiffEditPolicy.Key(key); await Verify(asynchronous, token).ConfigureAwait(false);
        TiffDirectoryInfo directory = Layout.Directories.FirstOrDefault(d => d.Offset == key.DirectoryOffset) ?? throw new ArgumentException("Key does not identify a reachable source IFD.", nameof(key));
        TiffFieldInfo? field = directory.Fields.FirstOrDefault(f => f.TagId == key.TagId); if (field is null) return null;
        if (!TiffEditPolicy.Shape(field, 1, 0, 0)) throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "Unsupported descriptive target shape.");
        byte[] bytes = await ReadAt(field.ValueRange.Offset, (int)field.ValueRange.Length, asynchronous, token).ConfigureAwait(false);
        ExifValue value = ExifValue.FromRaw((TiffFieldType)field.FieldType, field.Count, Layout.ByteOrder, bytes);
        if (!TiffEditPolicy.Value(key.TagId, value)) throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "Unsupported descriptive target content.");
        token.ThrowIfCancellationRequested(); return value;
    }
    private async Task<TiffAppendPlan?> Prepare(TiffMetadataEdit edit, bool asynchronous, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(edit); var changes = edit.Snapshot(options.ReadOptions.Limits.MaxRetainedMetadataBytes);
        await Verify(asynchronous, token).ConfigureAwait(false);
        return await new TiffAppendPlanner(Layout, options, (offset, count) => ReadAt(offset, count, asynchronous, token), token).Create(changes).ConfigureAwait(false);
    }
    private async Task Emit(Stream destination, TiffAppendPlan? plan, bool asynchronous, CancellationToken token)
    {
        int buffer = options.ReadOptions.Limits.CopyBufferBytes;
        if (plan is null) { await ContainerIo.Copy(source, destination, 0, Layout.SourceLength, buffer, asynchronous, token).ConfigureAwait(false); return; }
        await ContainerIo.Copy(source, destination, 0, 4, buffer, asynchronous, token).ConfigureAwait(false);
        await ContainerIo.Write(destination, plan.Root, asynchronous, token).ConfigureAwait(false);
        await ContainerIo.Copy(source, destination, 8, Layout.SourceLength - 8, buffer, asynchronous, token).ConfigureAwait(false);
        await ContainerIo.Write(destination, plan.Tail, asynchronous, token).ConfigureAwait(false);
    }
    /// <summary>
    /// Writes the file with the edit applied to a distinct, writable, seekable, empty stream. An edit that changes nothing gives a copy of the source.
    /// No byte of the source moves: changed directories are appended, and the bytes they replace stay
    /// in the file, no longer addressed.
    /// </summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.SourceChanged"/> when the source is no longer the data that was
    /// opened; <see cref="MetadataErrorCode.UnsupportedFeature"/> or
    /// <see cref="MetadataErrorCode.UnsafeRelocation"/> when the layout is outside the profile that
    /// can be appended to safely; <see cref="MetadataErrorCode.LimitExceeded"/> when the output
    /// would exceed a budget.
    /// </exception>
    public void Save(Stream destination, TiffMetadataEdit edit) => SaveCore(destination, edit, false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="Save(Stream, TiffMetadataEdit)"/>
    public Task SaveAsync(Stream destination, TiffMetadataEdit edit, CancellationToken cancellationToken = default) => SaveCore(destination, edit, true, cancellationToken);
    private async Task SaveCore(Stream destination, TiffMetadataEdit edit, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ContainerIo.Output(source, destination);
        TiffAppendPlan? plan = await Prepare(edit, asynchronous, token).ConfigureAwait(false); await Emit(destination, plan, asynchronous, token).ConfigureAwait(false);
    }
    /// <summary>As <see cref="Save(Stream, TiffMetadataEdit)"/>, to a new path; the file appears there complete or not at all.</summary>
    /// <exception cref="IOException">The destination exists or is the source.</exception>
    public void SaveAs(string destinationPath, TiffMetadataEdit edit) => SavePath(destinationPath, edit, false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveAs(string, TiffMetadataEdit)"/>
    public Task SaveAsAsync(string destinationPath, TiffMetadataEdit edit, CancellationToken cancellationToken = default) => SavePath(destinationPath, edit, true, cancellationToken);
    private async Task SavePath(string path, TiffMetadataEdit edit, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this); string target = ContainerIo.Target(path, sourcePath);
        TiffAppendPlan? plan = await Prepare(edit, asynchronous, token).ConfigureAwait(false);
        await ContainerIo.Publish(target, options.ReadOptions.Limits.CopyBufferBytes, asynchronous, token, destination => Emit(destination, plan, asynchronous, token)).ConfigureAwait(false);
    }
    /// <summary>Closes the file when the document opened it from a path; a stream the caller gave stays open.</summary>
    public void Dispose() { if (disposed) return; disposed = true; if (owns) source.Dispose(); }
    /// <inheritdoc cref="Dispose"/>
    public async ValueTask DisposeAsync() { if (disposed) return; disposed = true; if (owns) await source.DisposeAsync().ConfigureAwait(false); }
}
