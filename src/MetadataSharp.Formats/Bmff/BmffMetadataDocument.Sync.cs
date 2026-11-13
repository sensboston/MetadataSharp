using MetadataSharp.Core;
using MetadataSharp.Core.Sync;
using MetadataSharp.Core.Xmp;

namespace MetadataSharp.Formats.Bmff;

public sealed partial class BmffMetadataDocument
{
    /// <summary>
    /// Writes a copy of the file with the EXIF and XMP edits of a conversion or a reconciliation
    /// applied, both in one pass. IPTC is not written to these files: edits that hold an IPTC change
    /// fail with <see cref="MetadataErrorCode.UnsupportedFeature"/> before anything is written.
    /// Otherwise the call fails as <see cref="SaveExif(Stream, ExifEdit)"/> and
    /// <see cref="SaveXmp(Stream, XmpEdit)"/> do. Edits without a change copy the file.
    /// </summary>
    /// <param name="destination">Empty writable, seekable destination stream for the combined metadata edit; it must not be the source stream.</param>
    /// <param name="edits">EXIF, IPTC and XMP changes to apply together to this BMFF image.</param>
    public void SaveAll(Stream destination, SyncEdits edits) => SaveCore(destination, SyncParts(edits), false, default).GetAwaiter().GetResult();

    /// <summary>As <see cref="SaveAll(Stream, SyncEdits)"/>, to a new file.</summary>
    /// <param name="destinationPath">Path for publishing combined metadata edits in BMFF format.</param>
    /// <param name="edits">EXIF, IPTC and XMP changes to apply together to this BMFF image.</param>
    public void SaveAllAs(string destinationPath, SyncEdits edits) => SavePath(destinationPath, SyncParts(edits), false, default).GetAwaiter().GetResult();

    // Keep SyncParts focused on BMFF and its format-specific validation contract.
    private static Pending SyncParts(SyncEdits edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        if (!edits.Iptc.IsEmpty)
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "IPTC is not written to a BMFF file; the edits have an IPTC change.");
        return new(edits.Exif.Snapshot(), edits.Xmp.Snapshot());
    }
}
