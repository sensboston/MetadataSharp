using MetadataSharp.Core;

namespace MetadataSharp.Formats.Tiff;

/// <summary>Compatibility facade for the unified <see cref="TiffBasedMetadataDocument"/> TIFF-family model.</summary>
/// <remarks>Use <see cref="TiffBasedMetadataDocument"/> for new code. This facade preserves the former baseline-TIFF editing API.</remarks>
[Obsolete("Use TiffBasedMetadataDocument; this compatibility facade will be removed in a future major release.")]
public sealed class TiffMetadataDocument : IDisposable, IAsyncDisposable
{
    private readonly TiffBasedMetadataDocument document;

    private TiffMetadataDocument(TiffBasedMetadataDocument document) => this.document = document;

    /// <summary>The layout of the source: its directories, accounted ranges, and issues found.</summary>
    public TiffLayout Layout => document.LegacyLayout;

    /// <summary>Opens a baseline TIFF from a readable, seekable stream at position zero. The stream stays open and must remain unchanged while in use.</summary>
    /// <param name="source">Readable, seekable input stream positioned at the start; the operation leaves it open.</param>
    /// <param name="options">Operation settings; null selects the documented defaults.</param>
    /// <returns>An open TiffMetadataDocument bound to the source and captured read options.</returns>
    public static TiffMetadataDocument Open(Stream source, TiffEditOptions? options = null) =>
        new(TiffBasedMetadataDocument.OpenLegacy(source, options ?? new()));

    /// <summary>Opens a baseline TIFF file and keeps it open, shared for reading, until the document is disposed.</summary>
    /// <param name="path">Path to the source file to identify or open.</param>
    /// <param name="options">Operation settings; null selects the documented defaults.</param>
    /// <returns>An open TiffMetadataDocument bound to the source and captured read options.</returns>
    public static TiffMetadataDocument Open(string path, TiffEditOptions? options = null) =>
        new(TiffBasedMetadataDocument.OpenLegacy(path, options ?? new()));

    /// <inheritdoc cref="Open(Stream, TiffEditOptions?)"/>
    public static async Task<TiffMetadataDocument> OpenAsync(Stream source, TiffEditOptions? options = null,
        CancellationToken cancellationToken = default) =>
        new(await TiffBasedMetadataDocument.OpenLegacyAsync(source, options ?? new(), cancellationToken).ConfigureAwait(false));

    /// <inheritdoc cref="Open(string, TiffEditOptions?)"/>
    public static async Task<TiffMetadataDocument> OpenAsync(string path, TiffEditOptions? options = null,
        CancellationToken cancellationToken = default) =>
        new(await TiffBasedMetadataDocument.OpenLegacyAsync(path, options ?? new(), cancellationToken).ConfigureAwait(false));

    /// <summary>Reads an allowed descriptive field of the addressed source directory, or null when absent.</summary>
    /// <param name="key">Original-source directory offset and tag ID identifying the descriptive TIFF field.</param>
    /// <returns>The addressed descriptive field value, or null when the field is absent.</returns>
    public ExifValue? ReadValue(TiffFieldKey key) => document.ReadLegacyValue(key);

    /// <inheritdoc cref="ReadValue"/>
    public Task<ExifValue?> ReadValueAsync(TiffFieldKey key, CancellationToken cancellationToken = default) =>
        document.ReadLegacyValueAsync(key, cancellationToken);

    /// <summary>Writes addressed descriptive changes to an empty destination stream, preserving all existing bytes.</summary>
    /// <param name="destination">Writable, seekable, empty output stream, distinct from the source stream.</param>
    /// <param name="edit">metadata changes to apply to the file.</param>
    public void Save(Stream destination, TiffMetadataEdit edit) =>
        document.SaveLegacyAsync(destination, edit, false, default).GetAwaiter().GetResult();

    /// <inheritdoc cref="Save(Stream, TiffMetadataEdit)"/>
    public Task SaveAsync(Stream destination, TiffMetadataEdit edit, CancellationToken cancellationToken = default) =>
        document.SaveLegacyAsync(destination, edit, true, cancellationToken);

    /// <summary>As <see cref="Save(Stream, TiffMetadataEdit)"/>, to a new path.</summary>
    /// <param name="destinationPath">Path at which to publish the completed output file.</param>
    /// <param name="edit">metadata changes to apply to the file.</param>
    public void SaveAs(string destinationPath, TiffMetadataEdit edit) =>
        document.SaveLegacyAsAsync(destinationPath, edit, false, default).GetAwaiter().GetResult();

    /// <inheritdoc cref="SaveAs(string, TiffMetadataEdit)"/>
    public Task SaveAsAsync(string destinationPath, TiffMetadataEdit edit, CancellationToken cancellationToken = default) =>
        document.SaveLegacyAsAsync(destinationPath, edit, true, cancellationToken);

    /// <summary>Closes the retained source if the document opened it from a path; caller-provided streams stay open.</summary>
    public void Dispose() => document.Dispose();

    /// <inheritdoc cref="Dispose"/>
    public ValueTask DisposeAsync() => document.DisposeAsync();
}
