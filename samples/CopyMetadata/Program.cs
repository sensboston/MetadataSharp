using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Sync;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Operations;

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    Console.WriteLine("copymetadata [--dry-run] source-image destination-image");
    return 0;
}
try
{
    bool dryRun = args.Contains("--dry-run", StringComparer.Ordinal);
    string[] paths = args.Where(a => a != "--dry-run").ToArray();
    if (paths.Length != 2) throw new ArgumentException("Provide a source and destination image.");
    byte[] sourceBytes = File.ReadAllBytes(paths[0]);
    byte[] destinationBytes = File.ReadAllBytes(paths[1]);
    ImageMetadata source = ImageMetadata.Read(sourceBytes);
    ImageMetadata destination = ImageMetadata.Read(destinationBytes);
    if (source.Errors.Count != 0) throw source.Errors[0].Exception;
    if (destination.Errors.Count != 0) throw destination.Errors[0].Exception;
    byte[] result = Copy(sourceBytes, destinationBytes, source);
    Console.WriteLine($"Copied readable EXIF, IPTC, XMP and ICC metadata from {paths[0]} to {paths[1]}." + (dryRun ? " [dry-run]" : string.Empty));
    if (source.Exif?.MakerNote is not null) Console.WriteLine("Notice: MakerNote was not copied; see docs/samples/API_PROPOSAL.md.");
    if (!dryRun) SampleSupport.WriteAtomic(paths[1], result);
    return 0;
}
catch (Exception e) when (e is IOException or MetadataException or ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine($"copymetadata: {e.Message}"); return 1;
}

static byte[] Copy(byte[] sourceBytes, byte[] destinationBytes, ImageMetadata source)
{
    MetadataFamilies replace = MetadataFamilies.Exif | MetadataFamilies.Iptc | MetadataFamilies.Xmp | MetadataFamilies.IccProfile;
    byte[] cleared = MetadataOperations.Delete(destinationBytes, replace);
    var edits = new SyncEdits();
    if (source.Exif is { } exif)
        foreach (ExifEntry entry in exif.Entries)
            if (ExifEdit.IsEditable(entry.Key)) edits.Exif.SetUnchecked(entry.Key, entry.Value);
    if (source.Iptc is { } iptc)
        foreach (IGrouping<IptcDatasetKey, IptcDataset> group in iptc.Datasets.GroupBy(d => d.Key))
            if (group.Key is not { Record: 1, Dataset: 90 } and not { Record: 2, Dataset: 0 })
                edits.Iptc.ReplaceRaw(group.Key, group.Select(d => d.Value));
    if (source.Xmp is { } xmp) edits.Xmp.ReplaceWith(xmp);

    byte[] result = SampleSupport.SaveAll(cleared, edits);
    if (source.IccProfile.Length != 0) result = SampleSupport.SaveIcc(result, source.IccProfile.ToArray());
    ImageMetadata copied = ImageMetadata.Read(result);
    SyncEdits reconcile = MetadataSync.Reconcile(copied.Exif, copied.Iptc, copied.Xmp, copied.Iptc?.Digest);
    if (!reconcile.IsEmpty) result = SampleSupport.SaveAll(result, reconcile);

    // A JPEG thumbnail is a separate edit; containers that model reduced TIFF images do not treat it as one.
    if (sourceBytes.Length > 0 && source.Exif is not null)
    {
        byte[]? thumbnail = MetadataOperations.ExtractThumbnail(sourceBytes);
        if (thumbnail is not null && SampleSupport.Detect(result) is MetadataFormat.Jpeg or MetadataFormat.Png or MetadataFormat.WebP)
        {
            var thumbnailEdit = new ExifEdit(); thumbnailEdit.SetThumbnail(thumbnail);
            result = SampleSupport.SaveExif(result, thumbnailEdit);
        }
    }
    return result;
}
