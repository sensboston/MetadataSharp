using MetadataSharp.Core;
using MetadataSharp.Core.Sync;
using MetadataSharp.Core.Xmp;

namespace MetadataSharp.Formats.WebP;

public sealed partial class WebPMetadataDocument
{
    /// <summary>
    /// Writes a copy of the file with the EXIF and XMP edits of a conversion or a reconciliation
    /// applied, both in one pass. A WebP file has no place for IPTC: edits that hold an IPTC change
    /// fail with <see cref="MetadataErrorCode.UnsupportedFeature"/> before anything is written.
    /// Otherwise the call fails as <see cref="SaveExif(Stream, ExifEdit)"/> and
    /// <see cref="SaveXmp(Stream, XmpEdit)"/> do. Edits without a change copy the file.
    /// </summary>
    /// <param name="destination">Empty writable, seekable destination stream for the combined metadata edit; it must not be the source stream.</param>
    /// <param name="edits">EXIF, IPTC and XMP changes to apply together to this WebP image.</param>
    /// <exception cref="ArgumentNullException">The destination stream or edit set is null.</exception>
    /// <exception cref="MetadataException">The WebP source is invalid or the edit requests unsupported IPTC metadata.</exception>
    public void SaveAll(Stream destination, SyncEdits edits)
    {
        ArgumentNullException.ThrowIfNull(destination);
        (ExifEdit exif, XmpEdit? xmp) = SyncParts(edits);
        SaveCore(destination, exif, xmp, false, default).GetAwaiter().GetResult();
    }

    /// <summary>As <see cref="SaveAll(Stream, SyncEdits)"/>, to a new file.</summary>
    /// <param name="destinationPath">Path for publishing combined metadata edits in WebP format.</param>
    /// <param name="edits">EXIF, IPTC and XMP changes to apply together to this WebP image.</param>
    /// <exception cref="ArgumentException">The destination path is null or empty.</exception>
    /// <exception cref="ArgumentNullException">The edit set is null.</exception>
    /// <exception cref="MetadataException">The WebP source is invalid or the edit requests unsupported IPTC metadata.</exception>
    public void SaveAllAs(string destinationPath, SyncEdits edits)
    {
        ArgumentException.ThrowIfNullOrEmpty(destinationPath);
        (ExifEdit exif, XmpEdit? xmp) = SyncParts(edits);
        SavePath(destinationPath, exif, xmp, false, default).GetAwaiter().GetResult();
    }

    // Keep SyncParts focused on WebP and its format-specific validation contract.
    private static (ExifEdit Exif, XmpEdit? Xmp) SyncParts(SyncEdits edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        if (!edits.Iptc.IsEmpty)
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "A WebP file holds no IPTC; the edits have an IPTC change.");
        return (edits.Exif, edits.Xmp.IsEmpty ? null : edits.Xmp);
    }
}
