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
    public void SaveAll(Stream destination, SyncEdits edits)
    {
        ArgumentNullException.ThrowIfNull(destination);
        (ExifEdit exif, XmpEdit? xmp) = SyncParts(edits);
        SaveCore(destination, exif, xmp, false, default).GetAwaiter().GetResult();
    }

    /// <summary>As <see cref="SaveAll(Stream, SyncEdits)"/>, to a new file.</summary>
    public void SaveAllAs(string destinationPath, SyncEdits edits)
    {
        ArgumentException.ThrowIfNullOrEmpty(destinationPath);
        (ExifEdit exif, XmpEdit? xmp) = SyncParts(edits);
        SavePath(destinationPath, exif, xmp, false, default).GetAwaiter().GetResult();
    }

    private static (ExifEdit Exif, XmpEdit? Xmp) SyncParts(SyncEdits edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        if (!edits.Iptc.IsEmpty)
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "A WebP file holds no IPTC; the edits have an IPTC change.");
        return (edits.Exif, edits.Xmp.IsEmpty ? null : edits.Xmp);
    }
}
