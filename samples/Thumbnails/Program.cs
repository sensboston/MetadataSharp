using MetadataSharp.Core;
using MetadataSharp.Formats;

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    Console.WriteLine("thumbnails list <file> | extract <file> <directory>");
    return 0;
}
try
{
    if (args[0] == "list" && args.Length == 2)
    {
        byte[] bytes = File.ReadAllBytes(args[1]);
        IReadOnlyList<PreviewImage> previews = Previews.List(bytes);
        for (int i = 0; i < previews.Count; i++)
        {
            PreviewImage p = previews[i];
            Console.WriteLine($"{i + 1}\t{p.MimeType}\t{p.Width?.ToString() ?? "?"}x{p.Height?.ToString() ?? "?"}\t{p.Length} bytes\t{p.Source}");
        }
        return 0;
    }
    if (args[0] == "extract" && args.Length == 3)
    {
        byte[] bytes = File.ReadAllBytes(args[1]);
        IReadOnlyList<PreviewImage> previews = Previews.List(bytes);
        Directory.CreateDirectory(args[2]);
        int saved = 0;
        for (int i = 0; i < previews.Count; i++)
        {
            PreviewImage p = previews[i];
            try
            {
                byte[] content = Previews.Extract(bytes, p);
                string extension = p.MimeType switch { "image/jpeg" => ".jpg", "image/tiff" => ".tif", _ => ".bin" };
                string path = Path.Combine(args[2], $"preview-{i + 1:00}{extension}");
                SampleSupport.WriteAtomic(path, content);
                Console.WriteLine($"{path} ({p.MimeType}, {p.Width}x{p.Height}, {content.Length} bytes)");
                saved++;
            }
            catch (MetadataException e) when (e.Code == MetadataErrorCode.UnsupportedFeature)
            {
                Console.WriteLine($"listed but not extracted: {p.Source} ({p.MimeType})");
            }
        }
        Console.WriteLine($"Extracted {saved} of {previews.Count} preview(s).");
        return 0;
    }
    Console.Error.WriteLine("Use --help for usage.");
    return 2;
}
catch (Exception e) when (e is IOException or MetadataException or ArgumentException or NotSupportedException)
{
    Console.Error.WriteLine($"thumbnails: {e.Message}");
    return 1;
}
