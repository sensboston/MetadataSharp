using MetadataSharp.Core;
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

    MetadataCopyResult result = MetadataOperations.CopyMetadata(
        File.ReadAllBytes(paths[0]), File.ReadAllBytes(paths[1]),
        new MetadataCopyOptions { Sync = MetadataCopySync.ExistingRules });
    foreach (MetadataCopyNote note in result.Notes)
        Console.WriteLine($"{note.Reason}{(note.Family is null ? string.Empty : $" ({note.Family})")}: {note.Message}");
    if (result.WasRefused)
    {
        Console.Error.WriteLine("copymetadata: the copy was refused; the destination was not changed.");
        return 1;
    }

    Console.WriteLine($"Copied {result.CopiedFamilies} from {paths[0]} to {paths[1]}." +
        (dryRun ? " [dry-run; destination was not written]" : string.Empty));
    if (!dryRun) SampleSupport.WriteAtomic(paths[1], result.DestinationBytes);
    return 0;
}
catch (Exception e) when (e is IOException or MetadataException or ArgumentException or InvalidOperationException or NotSupportedException)
{
    Console.Error.WriteLine($"copymetadata: {e.Message}"); return 1;
}
