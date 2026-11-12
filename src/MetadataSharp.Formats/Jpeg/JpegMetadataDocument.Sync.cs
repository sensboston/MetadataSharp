using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Sync;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Internal;

namespace MetadataSharp.Formats.Jpeg;

public sealed partial class JpegMetadataDocument
{
    /// <summary>
    /// Writes a copy of the file with the EXIF, XMP and IPTC edits of a conversion or a reconciliation
    /// applied, IPTC with <see cref="SyncEdits.IptcWriteOptions"/>, so that after
    /// <see cref="ImageMetadata.Reconcile"/> the IPTC digest says that IPTC and XMP agree.
    /// A JPEG file keeps the three in separate segments that separate passes write: this call runs
    /// the pass of each family that has a change, one after another, keeping the file between two
    /// passes in memory. Only the last pass writes to <paramref name="destination"/>, so a failure
    /// leaves it untouched. Each pass fails as its own save does
    /// (<see cref="Save(Stream, ExifEdit)"/>, <see cref="SaveXmp(Stream, XmpEdit)"/>,
    /// <see cref="SaveIptc(Stream, IptcEdit, IptcWriteOptions?)"/>). Edits without a change copy the file,
    /// with a stale digest recomputed when the options ask for it.
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
        XmpEdit xmp = edits.Xmp;
        IptcEdit iptc = edits.Iptc;
        IptcWriteOptions write = edits.IptcWriteOptions;
        var steps = new List<(Action<JpegMetadataDocument, Stream> ToStream, Action<JpegMetadataDocument, string> ToPath)>();
        if (!xmp.IsEmpty) steps.Add(((document, stream) => document.SaveXmp(stream, xmp), (document, target) => document.SaveXmpAs(target, xmp)));
        // The pass also runs for edits that only ask for the digest: it recomputes a digest that went stale.
        if (!iptc.IsEmpty || write.UpdateDigest) steps.Add(((document, stream) => document.SaveIptc(stream, iptc, write), (document, target) => document.SaveIptcAs(target, iptc, write)));
        if (!exif.IsEmpty || steps.Count == 0) steps.Insert(0, ((document, stream) => document.Save(stream, exif), (document, target) => document.SaveAs(target, exif)));
        MetadataLimits settings = limits;
        SyncSave.Run(this, stream => Open(stream, new JpegReadOptions { Limits = settings }), destination, path, steps);
    }
}
