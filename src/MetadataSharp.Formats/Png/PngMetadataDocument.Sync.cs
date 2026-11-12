using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Sync;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Internal;

namespace MetadataSharp.Formats.Png;

public sealed partial class PngMetadataDocument
{
    /// <summary>
    /// Writes a copy of the file with the EXIF, XMP and IPTC edits of a conversion or a reconciliation
    /// applied, IPTC with <see cref="SyncEdits.IptcWriteOptions"/>, so that after
    /// <see cref="ImageMetadata.Reconcile"/> the IPTC digest says that IPTC and XMP agree.
    /// EXIF and XMP are written by one pass. The IPTC profile is written by a pass of its own, which
    /// runs second when both have changes, on the file the first pass left in memory. Only the last
    /// pass writes to <paramref name="destination"/>, so a failure leaves it untouched. Each pass
    /// fails as its own save does (<see cref="SaveExif(Stream, ExifEdit)"/>,
    /// <see cref="SaveXmp(Stream, XmpEdit)"/>, <see cref="SaveIptc(Stream, IptcEdit, IptcWriteOptions?)"/>).
    /// Edits without a change copy the file, with a stale digest recomputed when the options ask for it.
    /// </summary>
    public void SaveAll(Stream destination, SyncEdits edits)
    {
        ArgumentNullException.ThrowIfNull(destination);
        SaveAllCore(destination, null, edits);
    }

    /// <summary>As <see cref="SaveAll(Stream, SyncEdits)"/>, to a new file that appears only when every pass succeeded.</summary>
    public void SaveAllAs(string destinationPath, SyncEdits edits)
    {
        ArgumentException.ThrowIfNullOrEmpty(destinationPath);
        SaveAllCore(null, destinationPath, edits);
    }

    private void SaveAllCore(Stream? destination, string? path, SyncEdits edits)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(edits);
        ExifEdit exif = edits.Exif;
        XmpEdit? xmp = edits.Xmp.IsEmpty ? null : edits.Xmp;
        IptcEdit iptc = edits.Iptc;
        IptcWriteOptions write = edits.IptcWriteOptions;
        var steps = new List<(Action<PngMetadataDocument, Stream> ToStream, Action<PngMetadataDocument, string> ToPath)>();
        // The IPTC pass also runs for edits that only ask for the digest: it recomputes a digest that went stale.
        bool iptcPass = !iptc.IsEmpty || write.UpdateDigest;
        if (!exif.IsEmpty || xmp is not null || !iptcPass)
        {
            steps.Add(((document, stream) => document.SaveCore(stream, exif, xmp, false, default).GetAwaiter().GetResult(),
                (document, target) => document.SavePath(target, exif, xmp, false, default).GetAwaiter().GetResult()));
        }
        if (iptcPass) steps.Add(((document, stream) => document.SaveIptc(stream, iptc, write), (document, target) => document.SaveIptcAs(target, iptc, write)));
        PngReadOptions settings = options;
        SyncSave.Run(this, stream => Open(stream, settings), destination, path, steps);
    }
}
