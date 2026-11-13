using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Operations;
using MetadataSharp.Core.Sync;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Operations;

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    Console.WriteLine("stripmetadata [--keep exif,iptc,xmp,comment,icc,thumbnail,orientation,copyright] [--dry-run] image...");
    return 0;
}
try
{
    string? keepText = Value("--keep");
    MetadataFamilies keep = MetadataFamilies.None;
    bool keepOrientation = false, keepCopyright = false;
    bool keepThumbnail = false;
    if (keepText is not null)
        foreach (string item in keepText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            switch (item.ToLowerInvariant())
            {
                case "exif": keep |= MetadataFamilies.Exif; break;
                case "iptc": keep |= MetadataFamilies.Iptc; break;
                case "xmp": keep |= MetadataFamilies.Xmp; break;
                case "comment": keep |= MetadataFamilies.Comment; break;
                case "icc": keep |= MetadataFamilies.IccProfile; break;
                case "thumbnail": keepThumbnail = true; break;
                case "orientation": keepOrientation = true; break;
                case "copyright": keepCopyright = true; break;
                default: throw new ArgumentException($"Unknown keep token '{item}'.");
            }
    if (keepThumbnail) keep |= MetadataFamilies.Exif; // There is no generic remove-all-except-thumbnail operation.
    bool dryRun = args.Contains("--dry-run", StringComparer.Ordinal);
    int keepAt = Array.IndexOf(args, "--keep");
    string[] files = args.Where((a, i) => a != "--dry-run" && i != keepAt && !(keepAt >= 0 && i == keepAt + 1)).ToArray();
    if (files.Length == 0) throw new ArgumentException("Provide one or more images.");
    foreach (string path in files)
    {
        byte[] original = File.ReadAllBytes(path);
        ImageMetadata source = ImageMetadata.Read(original);
        MetadataFamilies remove = MetadataFamilies.All & ~keep;
        byte[] cleaned = MetadataOperations.Delete(original, remove);
        if ((keepOrientation || keepCopyright) && source.Exif is { } exif)
        {
            var edit = new ExifEdit();
            if (keepOrientation && exif.TryGetEntry(new(ExifDirectoryKind.Ifd0, 0x0112), out ExifEntry? orientation))
                edit.SetUnchecked(orientation.Key, orientation.Value);
            if (keepCopyright && exif.TryGetEntry(new(ExifDirectoryKind.Ifd0, 0x8298), out ExifEntry? exifCopyright))
                edit.SetUnchecked(exifCopyright.Key, exifCopyright.Value);
            if (!edit.IsEmpty) cleaned = SampleSupport.SaveExif(cleaned, edit);
        }
        if (keepCopyright && source.Iptc is { } iptc)
        {
            IptcDataset[] copyright = iptc.Datasets.Where(d => d.Key == new IptcDatasetKey(2, 116)).ToArray();
            if (copyright.Length != 0)
            {
                var edit = new IptcEdit(); edit.ReplaceRaw(new(2, 116), copyright.Select(d => d.Value));
                cleaned = SampleSupport.SaveIptc(cleaned, edit);
            }
        }
        Console.WriteLine($"{path}: removed {remove}" + (keepOrientation ? "; keeping orientation" : string.Empty) + (keepCopyright ? "; keeping copyright" : string.Empty) + (dryRun ? " [dry-run]" : string.Empty));
        if (!dryRun) SampleSupport.WriteAtomic(path, cleaned);
    }
    return 0;

    string? Value(string key) { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
}
catch (Exception e) when (e is IOException or MetadataException or ArgumentException)
{
    Console.Error.WriteLine($"stripmetadata: {e.Message}"); return 1;
}
