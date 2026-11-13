using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Operations;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Operations;

return Run(args);

static int Run(string[] args)
{
    try
    {
        if (args.Length == 0 || args[0] is "--help" or "-h") { Usage(); return 0; }
        string command = args[0];
        if (command == "-p") return Print(args);
        if (command == "set" && args.Length >= 4) return Modify(args[1], string.Join(' ', args.Skip(2).Take(args.Length - 3)), args[^1]);
        if (command is "del" or "delete" && args.Length == 3) return Modify(args[1], null, args[2]);
        if (command == "rename") return Rename(args);
        if (command == "extract") return Extract(args);
        if (command == "insert") return Insert(args);
        if (command == "fixiso" && args.Length == 2) return FixIso(args[1]);
        if (command == "fixcom" && args.Length is 2 or 3) return FixComment(args[1], args.Length == 3 ? args[2] : "utf-8");
        if (command is "-M" or "-m") return ModifyScript(args);
        Console.Error.WriteLine("Unknown or incomplete command. Use --help.");
        return 2;
    }
    catch (Exception ex) when (ex is IOException or MetadataException or ArgumentException or FormatException or OverflowException)
    {
        Console.Error.WriteLine($"metatool: {ex.Message}");
        return 1;
    }
}

static void Usage() => Console.WriteLine("""
MetaTool — public-API metadata sample
  metatool -p <a|e|i|x|c> [-K key] file
  metatool set <key> <value> file | del <key> file
  metatool rename [pattern] [--dry-run] file...
  metatool extract <thumbnail|xmp> file [output]
  metatool insert <thumbnail|xmp> file payload
  metatool fixiso file | fixcom file [encoding]
  metatool -M 'set Exif.Image.Artist "Ada"' file | -m script.txt file
""");

static int Print(string[] args)
{
    if (args.Length < 3) throw new ArgumentException("-p requires a family selector and file.");
    string selectors = args[1]; string? filter = null; string path = args[^1];
    int keyIndex = Array.IndexOf(args, "-K");
    if (keyIndex >= 0)
    {
        if (keyIndex + 1 >= args.Length - 1) throw new ArgumentException("-K needs a key before the file.");
        filter = args[keyIndex + 1];
    }
    if (selectors.Any(c => !"aeixc".Contains(c))) throw new ArgumentException("-p accepts only a, e, i, x or c.");
    ImageMetadata image = ImageMetadata.Read(path);
    bool all = selectors.Contains('a');
    if ((all || selectors.Contains('e')) && image.Exif is { } exif)
        foreach (ExifEntry entry in exif.Entries)
            PrintLine(MetadataKeys.Format(entry.Key), SampleSupport.ExifValueText(exif, entry), filter);
    if ((all || selectors.Contains('i')) && image.Iptc is { } iptc)
        foreach (IptcDataset dataset in iptc.Datasets)
            PrintLine(MetadataKeys.Format(dataset.Key) ?? $"Iptc.{dataset.Key.Record}.{dataset.Key.Dataset}", SampleSupport.IptcText(dataset), filter);
    if ((all || selectors.Contains('x')) && image.Xmp is { } xmp)
        foreach (XmpEntry entry in xmp.Entries)
            PrintLine(entry.Key, string.Join("; ", entry.Values), filter);
    if (all || selectors.Contains('c'))
        foreach (string comment in image.Comments) PrintLine("Comment", comment, filter);
    foreach (MetadataReadError error in image.Errors) Console.Error.WriteLine($"{error.Family}: {error.Code}");
    return 0;
}

static void PrintLine(string key, string value, string? filter)
{
    if (filter is null || key.Contains(filter, StringComparison.OrdinalIgnoreCase)) Console.WriteLine($"{key} = {value}");
}

static int Modify(string key, string? value, string path)
{
    string line = value is null ? $"del {key}" : $"set {key} {Quote(value)}";
    return ApplyScript(MetadataSharp.Core.Operations.ModifyScript.Parse(line), path);
}

static int ModifyScript(string[] args)
{
    if (args.Length < 3) throw new ArgumentException("-M needs a command and file; -m needs a script file and file.");
    string script = args[0] == "-M" ? args[1] : File.ReadAllText(args[1]);
    return ApplyScript(MetadataSharp.Core.Operations.ModifyScript.Parse(script), args[^1]);
}

static int ApplyScript(ModifyScript script, string path)
{
    byte[] original = File.ReadAllBytes(path);
    byte[] output = SampleSupport.SaveScript(original, script);
    SampleSupport.WriteAtomic(path, output);
    Console.WriteLine($"Updated {path} ({script.Commands.Count} command(s)).");
    return 0;
}

static int Rename(string[] args)
{
    bool dry = args.Contains("--dry-run", StringComparer.Ordinal);
    string? pattern = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : null;
    string[] files = args.Skip(pattern is null ? 1 : 2).Where(a => a != "--dry-run").ToArray();
    if (files.Length == 0) throw new ArgumentException("rename needs one or more files.");
    foreach (string path in files)
    {
        DateTime? taken = ImageMetadata.Read(path).Exif?.DateTaken;
        if (taken is null) { Console.WriteLine($"skip (no date): {path}"); continue; }
        string destination = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, MetadataFileNames.Format(pattern ?? MetadataFileNames.DefaultFormat, taken.Value, path));
        if (!string.Equals(path, destination, StringComparison.Ordinal)) destination = AvailableName(destination, files);
        Console.WriteLine($"{path} -> {destination}");
        if (!dry && !string.Equals(path, destination, StringComparison.Ordinal)) File.Move(path, destination);
    }
    return 0;
}

static string AvailableName(string path, string[] originalFiles)
{
    if (!File.Exists(path) || originalFiles.Contains(path, StringComparer.OrdinalIgnoreCase)) return path;
    string dir = Path.GetDirectoryName(path)!; string stem = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
    for (int i = 1; ; i++) { string candidate = Path.Combine(dir, $"{stem}_{i}{ext}"); if (!File.Exists(candidate)) return candidate; }
}

static int Extract(string[] args)
{
    if (args.Length is < 3 or > 4) throw new ArgumentException("extract needs kind, file, and optional output.");
    byte[] file = File.ReadAllBytes(args[2]);
    byte[]? payload = args[1] switch
    {
        "thumbnail" => MetadataOperations.ExtractThumbnail(file),
        "xmp" => MetadataOperations.ExtractXmpSidecar(file),
        _ => throw new ArgumentException("extract kind is thumbnail or xmp.")
    };
    if (payload is null) { Console.WriteLine("No matching payload."); return 0; }
    string output = args.Length == 4 ? args[3] : Path.ChangeExtension(args[2], args[1] == "thumbnail" ? ".thumbnail.jpg" : ".xmp");
    SampleSupport.WriteAtomic(output, payload);
    Console.WriteLine(output);
    return 0;
}

static int Insert(string[] args)
{
    if (args.Length != 4) throw new ArgumentException("insert needs kind, image file, and payload file.");
    byte[] original = File.ReadAllBytes(args[2]); byte[] payload = File.ReadAllBytes(args[3]); byte[] output;
    if (args[1] == "xmp") output = MetadataOperations.InsertXmpSidecar(original, payload);
    else if (args[1] == "thumbnail")
    {
        var edit = new ExifEdit(); edit.SetThumbnail(payload); output = SampleSupport.SaveExif(original, edit);
    }
    else throw new ArgumentException("insert kind is thumbnail or xmp.");
    SampleSupport.WriteAtomic(args[2], output);
    return 0;
}

static int FixIso(string path)
{
    ImageMetadata image = ImageMetadata.Read(path);
    ExifEdit? edit = image.Exif is null ? null : MetadataOperations.FixIso(image.Exif);
    if (edit is not null) SampleSupport.WriteAtomic(path, SampleSupport.SaveExif(File.ReadAllBytes(path), edit));
    Console.WriteLine(edit is null ? "No ISO fix applies." : "ISO set from the MakerNote.");
    return 0;
}

static int FixComment(string path, string encodingName)
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    Encoding encoding = Encoding.GetEncoding(encodingName);
    ImageMetadata image = ImageMetadata.Read(path);
    ExifEdit? edit = image.Exif is null ? null : MetadataOperations.FixUserComment(image.Exif, encoding);
    if (edit is not null) SampleSupport.WriteAtomic(path, SampleSupport.SaveExif(File.ReadAllBytes(path), edit));
    Console.WriteLine(edit is null ? "No UserComment fix applies." : "UserComment rewritten.");
    return 0;
}

static string Quote(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
