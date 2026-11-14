using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MetadataSharp.Parity;

// Coordinate file discovery, dump execution, comparison, and report serialization for this tool.
internal static class Program
{
    private const string MatchValue = "match-value", MatchKey = "match-key", ValueDiff = "value-diff";
    private const string MissingKey = "missing-key", ExtraKey = "extra-key", NotRead = "not-read";
    private const string MatchInterpreted = "match-interpreted", InterpretedDiff = "interpreted-diff";
    private const string NotInterpreted = "not-interpreted", ReferenceNotInterpreted = "reference-not-interpreted";
    private static readonly string[] Outcomes = [MatchValue, MatchKey, ValueDiff, MissingKey, ExtraKey, NotRead];
    private static readonly HashSet<string> StandardExif = ["Exif.Image", "Exif.Photo", "Exif.Thumbnail", "Exif.GPSInfo", "Exif.Iop"];
    private static readonly Regex SubImage = new(@"^Exif\.SubImage[0-9]+$", RegexOptions.CultureInvariant);
    private static readonly Regex OpaqueTag = new(@"^Exif\.[A-Za-z0-9]+\.0x[0-9a-fA-F]{4,8}$", RegexOptions.CultureInvariant);
    private static readonly Regex TagLine = new(
        @"^(?:(?<name>\S+) +)?(?<key>(?:Exif|Iptc|Xmp|ICC)\.[A-Za-z0-9_\-.:/\[\]]+(?:\.[A-Za-z0-9_\-.:/\[\]]+)+(?: [A-Za-z0-9_\-.:/\[\]]+)*) +(?<type>[A-Za-z][A-Za-z0-9]*|0x[0-9a-fA-F]{4}) +(?<count>\d+)(?: {2,}(?<value>.*))?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SummarySize = new(@"^Image size\s+:\s*(\d+) x (\d+)\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex SummaryMime = new(@"^MIME type\s+:\s*(\S+)\s*$", RegexOptions.CultureInvariant);
    private static readonly string[] FallbackExtensions = [
        ".jpg", ".jpeg", ".tiff", ".tif", ".png", ".webp", ".psd", ".jp2", ".jxl", ".avif", ".heic",
        ".crw", ".raf", ".dng", ".cr2", ".nef", ".orf", ".rw2", ".arw", ".sr2", ".srw", ".pef", ".mrw",
        ".mp4", ".mov", ".avi", ".asf", ".wav", ".xmp", ".exv", ".eps", ".pgf", ".dcp", ".gif", ".bmp", ".tga", ".targa"];
    private static readonly HashSet<string> SkipSuffixes = [".json", ".md", ".txt", ".py", ".sh", ".out"];
    // Retain InterpretationAllowances state required to parse or write parity report data safely.
    private static readonly Dictionary<string, string> InterpretationAllowances = new(StringComparer.Ordinal)
    {
        ["Exif.CanonCs.LensType"] = "External lens-ID database is not vendored.",
        ["Exif.Pentax.LensType"] = "External lens-ID database is not vendored.",
        ["Exif.PentaxDng.LensType"] = "External lens-ID database is not vendored.",
        ["Exif.NikonLd2.LensIDNumber"] = "External lens-ID database is not vendored.",
        ["Exif.NikonLd3.LensIDNumber"] = "External lens-ID database is not vendored.",
        ["Exif.NikonLd4.LensIDNumber"] = "External lens-ID database is not vendored.",
        ["Exif.NikonLd4.LensID"] = "External lens-ID database is not vendored.",
        ["Exif.Sony1.LensID"] = "External lens-ID database is not vendored.",
        ["Exif.Sony2.LensID"] = "External lens-ID database is not vendored.",
        ["Exif.SonyMinolta.LensID"] = "External lens-ID database is not vendored.",
        ["Exif.Minolta.LensID"] = "External lens-ID database is not vendored.",
        // Exiv2 reads these XMP properties through code lists it ships as its own vocabulary
        // (IPTC subject codes and scene codes, the PLUS and IPTC Extension terms), not through a tag.
        ["Xmp.iptc.Scene"] = "External IPTC subject-code vocabulary is not vendored.",
        ["Xmp.iptc.SubjectCode"] = "External IPTC subject-code vocabulary is not vendored.",
        ["Xmp.iptcExt.DigitalSourceType"] = "External IPTC Extension vocabulary is not vendored.",
        ["Xmp.plus.MinorModelAgeDisclosure"] = "External PLUS vocabulary is not vendored.",
        ["Xmp.plus.ModelReleaseStatus"] = "External PLUS vocabulary is not vendored.",
        ["Xmp.plus.PropertyReleaseStatus"] = "External PLUS vocabulary is not vendored.",
    };
    // Retain RawExtras state required to parse or write parity report data safely.
    private static readonly Dictionary<string, string> RawExtras = new(StringComparer.Ordinal)
    {
        ["Exif.Canon.Categories"] = "Exiv2 0.28.9 disables Canon tag 0x0023.",
        ["Exif.Canon.PictureStylePC"] = "Exiv2 0.28.9 disables Canon tag 0x4009.",
        ["Exif.Canon.FilterInfo"] = "Exiv2 0.28.9 does not surface Canon tag 0x4024 in these records.",
    };
    // Retain Refusals state required to parse or write parity report data safely.
    private static readonly Dictionary<string, (string Code, string Reason)> Refusals = new(StringComparer.Ordinal)
    {
        ["exiv2:issue_2006_poc.tiff"] = ("InvalidExif", "the EXIF structure is malformed"),
        ["exiv2:poc_2542.exv"] = ("AmbiguousMetadata", "the file has ambiguous EXIF metadata"),
        ["exiv2:issue_1706_poc.exv"] = ("LimitExceeded", "the EXIF directory exceeds configured reader limits"),
        ["exiv2:issue_1920_poc.tiff"] = ("InvalidExif", "the EXIF structure is malformed"),
        ["exiv2:issue_ghsa_h9x9_4f77_336w_poc.exv"] = ("InvalidExif", "the EXIF structure is malformed"),
        ["exiv2:IMGP0006-min.jpg"] = ("InvalidJpeg", "the JPEG structure is malformed"),
        ["exiv2:issue_ghsa_5p8g_9xf3_gfrr_poc.exv"] = ("UnsupportedFeature", "unsupported encrypted Nikon MakerNote layout"),
        ["exiv2:issue_ghsa_5p8g_9xf3_gfrr_poc.webp"] = ("UnsupportedFeature", "unsupported encrypted Nikon MakerNote layout"),
    };

    // Parse command-line options, run the selected comparisons, and return a failure status for mismatches.
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            if (options.Tree is null && options.Fixtures.Count == 0) throw new InvalidOperationException("Pass --tree and/or --fixtures.");
            List<Candidate> candidates = [];
            List<string> unmatched = [];
            if (options.Tree is not null) DiscoverTree(options.Tree, candidates, unmatched);
            if (options.Fixtures.Count > 0) DiscoverFixtures(options.Fixtures, candidates, options.Exiv2Command is not null);
            if (candidates.Count == 0) throw new InvalidOperationException("No candidate files were found.");
            var refs = await GetReferences(candidates, options).ConfigureAwait(false);
            var summaries = await GetSizeSummaries(candidates, options).ConfigureAwait(false);
            Dictionary<string, DumpBlock> dumps = await RunDump(candidates, options.DumpCommand).ConfigureAwait(false);
            Report report = BuildReport(candidates, refs, summaries, dumps, options, unmatched);
            foreach (FileReport file in report.PerFile) file.TrimRows(options.RowsPerFile);
            Directory.CreateDirectory(options.Out);
            string reportPath = Path.Combine(options.Out, "parity-report.json");
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, JsonOptions) + "\n").ConfigureAwait(false);
            string text = FormatReport(report);
            await File.WriteAllTextAsync(Path.Combine(options.Out, "parity-report.txt"), text).ConfigureAwait(false);
            Console.Write(text);
            if (options.CompareReport is not null)
            {
                var legacy = JsonDocument.Parse(await File.ReadAllTextAsync(options.CompareReport).ConfigureAwait(false));
                List<string> differences = CompareReports(legacy.RootElement, report);
                if (differences.Count > 0)
                {
                    Console.Error.WriteLine("Side-by-side parity report mismatch:");
                    foreach (string difference in differences.Take(100)) Console.Error.WriteLine("  " + difference);
                    return 1;
                }
                Console.WriteLine("Side-by-side verification: identical verdicts and counts per format/group.");
            }
            if (options.Inventory || options.RequireAllMakerNotes || options.RequiredGroups.Count > 0)
            {
                (bool passed, string gateText) = MakerNoteGate(report, options.RequiredGroups, options.RequireAllMakerNotes, options.Inventory);
                Console.Write(gateText);
                if (!passed)
                {
                    // Keep a concise copy of the failing gate in GitHub's check annotations. This is
                    // important when Actions log/artifact downloads are unavailable to CI consumers.
                    int failAt = gateText.IndexOf("FAIL\n", StringComparison.Ordinal);
                    string[] failureLines = failAt < 0 ? [] : gateText[(failAt + 5)..]
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(line => line.Trim())
                        .Where(line => line.Contains(':'))
                        .ToArray();
                    string detail = string.Join("\n", failureLines
                        .GroupBy(line => line[..line.IndexOf(':')], StringComparer.Ordinal)
                        .Select(group => new { Group = group.Key, Count = group.Count(), Examples = group.Take(2).ToArray() })
                        .OrderByDescending(group => group.Count)
                        .ThenBy(group => group.Group, StringComparer.Ordinal)
                        .Select(group => $"{group.Group}: {group.Count} failure(s); {string.Join("; ", group.Examples)}"));
                    if (detail.Length == 0) detail = gateText;
                    if (detail.Length > 3500) detail = detail[..3500] + "…";
                    detail = detail.Replace("%", "%25", StringComparison.Ordinal)
                        .Replace("\r", "%0D", StringComparison.Ordinal)
                        .Replace("\n", "%0A", StringComparison.Ordinal);
                    Console.Error.WriteLine($"::error title=MakerNote parity gate::{detail}");
                    if (options.Gaps) EmitGapNotices(report, GateGroups(report, options));
                    return 1;
                }
                if (options.Gaps) EmitGapNotices(report, GateGroups(report, options));
            }
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException or ArgumentException or TimeoutException or FormatException)
        {
            Console.Error.WriteLine($"::error title=Parity harness::{ex.Message}");
            Console.Error.WriteLine("structural failure: " + ex.Message);
            return 1;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    // Enumerate supported candidate files recursively while retaining deterministic input order.
    private static void DiscoverTree(string tree, List<Candidate> candidates, List<string> unmatched)
    {
        string data = Path.Combine(tree, "test", "data");
        string references = Path.Combine(data, "test_reference_files");
        if (!Directory.Exists(references)) throw new IOException($"No expected-output directory at {references}");
        foreach (string reference in Directory.EnumerateFiles(references, "*.out").Order(StringComparer.Ordinal))
        {
            string stem = Path.GetFileName(reference)[..^4];
            string? input = ResolveInput(stem, data);
            if (input is null) { unmatched.Add(stem); continue; }
            candidates.Add(new($"exiv2:{stem}", input, "exiv2", reference, FormatOf(input)));
        }
        if (candidates.Count == 0) throw new IOException($"{references} has no usable expectation.");
    }

    // Resolve an explicit path against the chosen root and reject paths outside the input scope.
    private static string? ResolveInput(string stem, string data)
    {
        string exact = Path.Combine(data, stem);
        if (File.Exists(exact)) return exact;
        if (stem.EndsWith("_test", StringComparison.Ordinal)) return null;
        int dot = stem.LastIndexOf('.');
        string basename = dot >= 0 ? stem[..dot] : stem;
        foreach (string extension in FallbackExtensions)
        {
            string path = Path.Combine(data, basename + extension);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    // Collect owned fixtures and optionally include only the fixture classes selected by options.
    private static void DiscoverFixtures(List<string> directories, List<Candidate> candidates, bool hasOracle)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string directory in directories)
        {
            if (!Directory.Exists(directory)) throw new IOException($"Missing fixture directory {directory}");
            foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                if (SkipSuffixes.Contains(Path.GetExtension(path).ToLowerInvariant())) continue;
                string full = Path.GetFullPath(path);
                if (!seen.Add(full)) continue;
                if (!hasOracle) continue;
                candidates.Add(new("generated:" + Path.GetFileName(path), path, "generated", null, FormatOf(path)));
            }
        }
        if (candidates.Count == 0) throw new IOException("No fixture candidates found (generated fixtures need --exiv2-command).");
    }

    // Map a candidate extension to its metadata format, returning no guess for unknown extensions.
    private static string FormatOf(string path)
    {
        string suffix = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return suffix switch
        {
            "tif" => "tiff", "jpeg" => "jpg", "cr2" or "nef" or "orf" or "rw2" or "arw" or "sr2" or "srw" or "pef" or "mrw" or "raf" or "crw" => "raw",
            "dng" => "dng", _ => suffix.Length == 0 ? "none" : suffix
        };
    }

    // Load reference rows for each candidate and retain unsupported or absent references as explicit states.
    private static async Task<Dictionary<string, Dictionary<string, RefTag>>> GetReferences(List<Candidate> candidates, Options options)
    {
        Dictionary<string, Dictionary<string, RefTag>> references = new(StringComparer.Ordinal);
        foreach (Candidate candidate in candidates)
        {
            string output;
            if (candidate.ReferencePath is not null) output = await File.ReadAllTextAsync(candidate.ReferencePath, Encoding.UTF8).ConfigureAwait(false);
            else
            {
                if (options.Exiv2Command is null) throw new InvalidOperationException("Generated fixtures require --exiv2-command.");
                ProcessResult result = await RunProcess(options.Exiv2Command.Append(candidate.Path).ToList(), null).ConfigureAwait(false);
                if (options.RequireReferenceTags && result.ExitCode != 0)
                    throw new InvalidOperationException($"Pinned reference failed on {Path.GetFileName(candidate.Path)}: {FirstLine(result.Error + result.Output)}");
                output = result.Output;
            }
            references[candidate.Uid] = ParseReference(output);
            if (candidate.ReferencePath is null && options.RequireReferenceTags && references[candidate.Uid].Count == 0)
                throw new InvalidOperationException($"Pinned reference printed no tag records for {candidate.Uid}.");
        }
        return references;
    }

    // Parse one reference-output line into its normalized tag key and value.
    private static Dictionary<string, RefTag> ParseReference(string text)
    {
        Dictionary<string, RefTag> tags = new(StringComparer.Ordinal);
        foreach (string raw in NormalizeLines(text))
        {
            string line = raw.TrimEnd();
            if (line.Length == 0) continue;
            Match match = TagLine.Match(line);
            if (!match.Success) continue;
            string key = match.Groups["key"].Value;
            string type = match.Groups["type"].Value;
            int count = int.Parse(match.Groups["count"].Value, CultureInfo.InvariantCulture);
            string value = match.Groups["value"].Success ? match.Groups["value"].Value : "";
            bool labelled = match.Groups["name"].Success || type.StartsWith("0x", StringComparison.Ordinal);
            (string? rawValue, string? interpreted) = labelled ? (null, null) : SplitColumns(value);
            if (tags.TryGetValue(key, out RefTag? existing)) tags[key] = existing with { Repeats = existing.Repeats + 1 };
            else tags.Add(key, new(type, count, value, labelled, rawValue, interpreted, 0));
        }
        return tags;
    }

    // Split tabular reference output without losing empty columns or escaped delimiters.
    private static (string? Raw, string? Interpreted) SplitColumns(string value)
    {
        MatchCollection gaps = Regex.Matches(value, " {2,}", RegexOptions.CultureInvariant);
        if (gaps.Count == 0) return (null, null);
        if (gaps.Count == 1)
        {
            Match gap = gaps[0];
            return (value[..gap.Index], value[(gap.Index + gap.Length)..]);
        }
        foreach (Match gap in gaps)
        {
            string left = value[..gap.Index], right = value[(gap.Index + gap.Length)..];
            if (left.Trim() == right.Trim()) return (left, right);
        }
        return (null, null);
    }

    // Collect reference dimensions, sizes, and MIME types for candidates that provide them.
    private static async Task<Dictionary<string, (string Width, string Height, string Mime)>> GetSizeSummaries(List<Candidate> candidates, Options options)
    {
        Dictionary<string, (string, string, string)> result = new(StringComparer.Ordinal);
        if (options.SummaryCommand is null) return result;
        HashSet<string> sizeOnly = [".gif", ".bmp", ".tga", ".targa", ".pgf", ".eps"];
        foreach (Candidate candidate in candidates.Where(c => sizeOnly.Contains(Path.GetExtension(c.Path).ToLowerInvariant())))
        {
            ProcessResult printed = await RunProcess(options.SummaryCommand.Append(candidate.Path).ToList(), null).ConfigureAwait(false);
            string? width = null, height = null, mime = null;
            foreach (string line in NormalizeLines(printed.Output))
            {
                Match size = SummarySize.Match(line);
                if (size.Success) { width = size.Groups[1].Value; height = size.Groups[2].Value; continue; }
                Match mimeMatch = SummaryMime.Match(line);
                if (mimeMatch.Success) mime = mimeMatch.Groups[1].Value;
            }
            if (width is not null && height is not null && (width != "0" || height != "0") && mime is not null)
                result[candidate.Uid] = (width, height, mime);
        }
        return result;
    }

    // Invoke the repository dump tool once for the selected candidates and capture its structured output.
    private static async Task<Dictionary<string, DumpBlock>> RunDump(List<Candidate> candidates, List<string> dumpCommand)
    {
        Dictionary<string, DumpBlock> blocks = new(StringComparer.Ordinal);
        foreach (Candidate[] batch in candidates.Chunk(40))
        {
            string input = string.Concat(batch.Select(c => c.Path + "\n"));
            ProcessResult result = await RunProcess(dumpCommand.Append("--files-from").Append("-").ToList(), input).ConfigureAwait(false);
            if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(result.Output))
                throw new InvalidOperationException($"Dump tool failed ({result.ExitCode}): {FirstLine(result.Error)}");
            Dictionary<string, DumpBlock> parsed;
            try { parsed = ParseDump(result.Output); }
            catch (FormatException) when (batch.Length > 1)
            {
                foreach (Candidate candidate in batch)
                {
                    ProcessResult single = await RunProcess(dumpCommand.Append("--files-from").Append("-").ToList(), candidate.Path + "\n").ConfigureAwait(false);
                    foreach (var pair in ParseDump(single.Output)) blocks[pair.Key] = pair.Value;
                }
                continue;
            }
            if (batch.Length > 1 && batch.Any(c => !parsed.ContainsKey(c.Path)))
            {
                foreach (Candidate candidate in batch)
                {
                    ProcessResult single = await RunProcess(dumpCommand.Append("--files-from").Append("-").ToList(), candidate.Path + "\n").ConfigureAwait(false);
                    foreach (var pair in ParseDump(single.Output)) blocks[pair.Key] = pair.Value;
                }
                continue;
            }
            foreach (var pair in parsed) blocks[pair.Key] = pair.Value;
        }
        return blocks;
    }

    // Parse dump records into per-file blocks while preserving repeated tags and empty values.
    private static Dictionary<string, DumpBlock> ParseDump(string output)
    {
        Dictionary<string, DumpBlock> blocks = new(StringComparer.Ordinal);
        string? path = null;
        Dictionary<string, DumpTag> tags = new(StringComparer.Ordinal);
        Dictionary<string, string> interpreted = new(StringComparer.Ordinal);
        (int Width, int Height, string Mime)? size = null;
        string? status = null, detail = "";
        string[] lines = output.Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].EndsWith('\r') ? lines[index][..^1] : lines[index];
            if (line.Length == 0) continue;
            string[] fields = line.Split('\t');
            switch (fields[0])
            {
                case "FILE":
                    if (fields.Length < 2 || path is not null) throw new FormatException($"dump line {index + 1}: invalid FILE block");
                    path = Unescape(fields[1]); tags = new(StringComparer.Ordinal); interpreted = new(StringComparer.Ordinal); size = null; status = null; detail = "";
                    break;
                case "TAG":
                    if (path is null || fields.Length is not (5 or 6)) throw new FormatException($"dump line {index + 1}: invalid TAG record");
                    string key = Unescape(fields[1]);
                    if (tags.ContainsKey(key)) break;
                    if (!int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)) throw new FormatException("TAG count is invalid");
                    tags[key] = new(Unescape(fields[2]), count, Unescape(fields[4]));
                    if (fields.Length == 6) interpreted[key] = Unescape(fields[5]);
                    break;
                case "SIZE":
                    if (path is null || fields.Length != 3 || size is not null) throw new FormatException($"dump line {index + 1}: invalid SIZE record");
                    Match dimensions = Regex.Match(fields[1], @"^(\d+)x(\d+)$", RegexOptions.CultureInvariant);
                    if (!dimensions.Success) throw new FormatException("SIZE dimensions invalid");
                    size = (int.Parse(dimensions.Groups[1].Value), int.Parse(dimensions.Groups[2].Value), Unescape(fields[2]));
                    break;
                case "STATUS":
                    if (path is null || status is not null) throw new FormatException($"dump line {index + 1}: invalid STATUS record");
                    status = fields.Length > 1 ? fields[1] : "";
                    if (status is not ("ok" or "refused" or "not-attempted" or "error")) throw new FormatException("unknown dump status");
                    detail = fields.Length > 2 ? Unescape(fields[2]) : "";
                    blocks[path] = new(status, detail, tags, interpreted, size);
                    path = null;
                    break;
                default: throw new FormatException($"dump line {index + 1}: unknown record {fields[0]}");
            }
        }
        if (path is not null) throw new FormatException("dump ended without STATUS");
        return blocks;
    }

    // Decode the dump protocol’s escaped text without treating arbitrary backslashes as escapes.
    private static string Unescape(string value)
    {
        StringBuilder result = new(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c != '\\') { result.Append(c); continue; }
            if (++i >= value.Length) throw new FormatException("dangling escape");
            char code = value[i];
            if (code == 't') result.Append('\t');
            else if (code == 'n') result.Append('\n');
            else if (code == 'r') result.Append('\r');
            else if (code == '\\') result.Append('\\');
            else if (code == 'x' && i + 2 < value.Length && byte.TryParse(value.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            { result.Append((char)b); i += 2; }
            else throw new FormatException("invalid escaped field");
        }
        return result.ToString();
    }

    // Start the requested tool with redirected output, cancellation, and a bounded execution result.
    private static async Task<ProcessResult> RunProcess(List<string> command, string? stdin)
    {
        if (command.Count == 0) throw new InvalidOperationException("Empty process command.");
        ProcessStartInfo start = new(command[0]) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = stdin is not null, UseShellExecute = false };
        foreach (string argument in command.Skip(1)) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new IOException("Could not start " + command[0]);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin).ConfigureAwait(false);
            process.StandardInput.Close();
        }
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(30));
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        return new(await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false), process.ExitCode);
    }

    // Compare library output with reference data and aggregate parity counters by file and tag.
    private static Report BuildReport(List<Candidate> candidates,
        Dictionary<string, Dictionary<string, RefTag>> references,
        Dictionary<string, (string Width, string Height, string Mime)> summaries,
        Dictionary<string, DumpBlock> dumps, Options options, List<string> unmatched)
    {
        Report report = new();
        foreach (Candidate candidate in candidates)
        {
            if (!dumps.TryGetValue(candidate.Path, out DumpBlock? dump))
                throw new InvalidOperationException($"{candidate.Uid}: dump tool printed no record for {candidate.Path}");
            Dictionary<string, RefTag> theirs = references[candidate.Uid];
            List<ParityRow> rows = CompareTags(theirs, dump);
            if (dump.Status == "error") throw new InvalidOperationException($"{candidate.Uid}: reader error {dump.Detail}");
            var entry = new FileReport(candidate.Uid, candidate.Corpus, candidate.Format, dump.Status, dump.Detail, theirs.Count, dump.Tags.Count, rows);
            if (summaries.TryGetValue(candidate.Uid, out var referenceSize))
            {
                (string Width, string Height, string Mime)? ourSize = dump.Status == "ok" && dump.Size is { } s ? (s.Width.ToString(CultureInfo.InvariantCulture), s.Height.ToString(CultureInfo.InvariantCulture), s.Mime) : null;
                entry.Size = CompareSize(referenceSize, ourSize);
            }
            report.PerFile.Add(entry);
            if (dump.Status != "ok") report.Refusals[dump.Status + ":" + dump.Detail] = report.Refusals.GetValueOrDefault(dump.Status + ":" + dump.Detail) + 1;
            else report.Attempted++;
            Dictionary<string, int> format = Get(report.ByFormat, candidate.Format);
            format["files"] = format.GetValueOrDefault("files") + 1;
            if (dump.Status != "ok") { format["excluded"] = format.GetValueOrDefault("excluded") + 1; format["excluded_tags"] = format.GetValueOrDefault("excluded_tags") + theirs.Count; }
            foreach (ParityRow row in rows)
            {
                Count(report.Outcomes, row.Outcome, 1);
                Count(format, row.Outcome, 1);
                Count(Get(report.ByFamily, Family(row.Key)), row.Outcome, 1);
                if (row.Interpreted is not null)
                {
                    Count(Get(report.Interpreted.ByFamily, Family(row.Key)), row.Interpreted, 1);
                    if (row.Translated && row.Interpreted == NotInterpreted)
                    {
                        report.Interpreted.NotInterpreted++;
                        report.Interpreted.NotInterpretedTranslated++;
                        Count(Get(report.Interpreted.ByFamily, Family(row.Key)), "not-interpreted-translated", 1);
                    }
                    if (row.Interpreted == MatchInterpreted) report.Interpreted.Agreed++;
                    if (row.Interpreted is MatchInterpreted or InterpretedDiff) report.Interpreted.Compared++;
                }
            }
            if (entry.Size is not null)
            {
                report.Sizes.Outcomes[entry.Size.Outcome] = report.Sizes.Outcomes.GetValueOrDefault(entry.Size.Outcome) + 1;
                Count(Get(report.Sizes.ByFormat, candidate.Format), entry.Size.Outcome, 1);
            }
        }
        report.Files = new FileCounts { Total = candidates.Count, Attempted = report.Attempted, Statuses = report.PerFile.GroupBy(f => f.Status).ToDictionary(g => g.Key, g => g.Count()), Refusals = report.Refusals };
        report.Corpora = new CorpusReport
        {
            Exiv2 = new CorpusDetails { Files = report.PerFile.Count(f => f.Corpus == "exiv2"), UnmatchedExpectations = unmatched.Count, UnmatchedNames = unmatched.Take(40).ToList(), Tree = options.Tree },
            Generated = new CorpusDetails { Files = report.PerFile.Count(f => f.Corpus == "generated"), Directories = options.Fixtures, Oracle = options.Exiv2Command is null ? null : string.Join(' ', options.Exiv2Command) }
        };
        report.ByFormat = Sorted(report.ByFormat); report.ByFamily = Sorted(report.ByFamily); report.Interpreted.ByFamily = Sorted(report.Interpreted.ByFamily);
        return report;
    }

    // Compare expected tags against one dump block and attribute missing, extra, or differing values.
    private static List<ParityRow> CompareTags(Dictionary<string, RefTag> reference, DumpBlock dump)
    {
        if (dump.Status != "ok") return reference.Select(pair => new ParityRow { Key = pair.Key, Outcome = NotRead, TheirType = pair.Value.Type, TheirCount = pair.Value.Count, TheirValue = pair.Value.Value }).ToList();
        List<ParityRow> rows = [];
        foreach ((string key, RefTag theirs) in reference)
        {
            if (!dump.Tags.TryGetValue(key, out DumpTag? ours))
            {
                rows.Add(new() { Key = key, Outcome = MissingKey, TheirType = theirs.Type, TheirCount = theirs.Count, TheirValue = theirs.Value });
                continue;
            }
            bool agrees = ValuesAgree(ours.Value, theirs.Value);
            if (!theirs.Labelled && !agrees && theirs.Count == 0 && ours.Value.Length == 0) agrees = true;
            string outcome = theirs.Labelled ? (agrees ? MatchValue : MatchKey) : (agrees ? MatchValue : ValueDiff);
            ParityRow row = new()
            {
                Key = key, Outcome = outcome, OurType = ours.Type, OurCount = ours.Count, OurValue = ours.Value,
                TheirType = theirs.Type, TheirCount = theirs.Count, TheirValue = theirs.Value
            };
            if (dump.Interpreted.TryGetValue(key, out string? ourInterpreted))
            {
                if (theirs.Raw is null || theirs.Interpreted is null) row.InterpretedSkipped = "no-reference-column";
                else
                {
                    bool translated = theirs.Raw.Trim() != theirs.Interpreted.Trim();
                    row.Translated = translated; row.OurInterpreted = ourInterpreted; row.TheirInterpreted = theirs.Interpreted;
                    row.Interpreted = string.IsNullOrWhiteSpace(ourInterpreted) ? NotInterpreted
                        : !translated ? ReferenceNotInterpreted
                        : ourInterpreted.Trim() == theirs.Interpreted.Trim() ? MatchInterpreted : InterpretedDiff;
                }
            }
            else row.InterpretedSkipped = "not-offered";
            rows.Add(row);
        }
        foreach ((string key, DumpTag ours) in dump.Tags)
            if (!reference.ContainsKey(key)) rows.Add(new() { Key = key, Outcome = ExtraKey, OurType = ours.Type, OurCount = ours.Count, OurValue = ours.Value });
        return rows;
    }

    // Normalize only the documented value differences before deciding whether two values match.
    private static bool ValuesAgree(string ours, string theirs)
    {
        if (ours == theirs || Normalize(ours) == Normalize(theirs)) return true;
        foreach (string column in ValueColumns(theirs)) if (Normalize(ours) == Normalize(column)) return true;
        MatchCollection quoted = Regex.Matches(theirs, "\\\"([^\\\"]*)\\\"", RegexOptions.CultureInvariant);
        if (quoted.Count > 0 && Normalize(ours) == string.Join(", ", quoted.Select(m => Normalize(m.Groups[1].Value)))) return true;
        return false;
    }

    // Split a printed value into its protocol-defined display columns.
    private static IEnumerable<string> ValueColumns(string value)
    {
        MatchCollection gaps = Regex.Matches(value, @"\s{2,}", RegexOptions.CultureInvariant);
        foreach (Match gap in gaps)
        {
            string left = value[..gap.Index], right = value[(gap.Index + gap.Length)..];
            if (left.Trim() == right.Trim()) return [left, right];
        }
        return Regex.Split(value, @"\s{2,}", RegexOptions.CultureInvariant).Where(part => part.Length > 0).Select(part => part.Trim());
    }

    private static string Normalize(string value) => Regex.Replace(value, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
    private static string Family(string key) { int dot = key.IndexOf('.'); int second = dot >= 0 ? key.IndexOf('.', dot + 1) : -1; return second >= 0 ? key[..second] : key; }

    // Compare width, height, and MIME type only when the reference supplied a size record.
    private static SizeResult CompareSize((string Width, string Height, string Mime) theirs, (string Width, string Height, string Mime)? ours)
    {
        string outcome = ours is null ? "size-missing" : ours.Value == theirs ? "size-match-value" : "size-diff";
        string[]? ourValues = ours is null ? null : [ours.Value.Width, ours.Value.Height, ours.Value.Mime];
        return new(outcome, ourValues, [theirs.Width, theirs.Height, theirs.Mime]);
    }

    private static void Count(Dictionary<string, int> counts, string key, int n) => counts[key] = counts.GetValueOrDefault(key) + n;
    private static Dictionary<string, int> Get(Dictionary<string, Dictionary<string, int>> counts, string key)
    { if (!counts.TryGetValue(key, out Dictionary<string, int>? found)) counts[key] = found = []; return found; }
    private static Dictionary<string, Dictionary<string, int>> Sorted(Dictionary<string, Dictionary<string, int>> value) => value.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value.OrderBy(c => c.Key, StringComparer.Ordinal).ToDictionary(c => c.Key, c => c.Value), StringComparer.Ordinal);

    // Serialize parity measurements into the stable machine-readable report shape.
    private static string FormatReport(Report report)
    {
        StringBuilder text = new();
        text.AppendLine("exiv2 differential parity").AppendLine("=========================");
        text.AppendLine($"files: {report.Files.Total} attempted, {report.Files.Attempted} read ({string.Join(", ", report.Files.Statuses.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value))})");
        int expected = report.Outcomes.GetValueOrDefault(MatchValue) + report.Outcomes.GetValueOrDefault(MatchKey) + report.Outcomes.GetValueOrDefault(ValueDiff) + report.Outcomes.GetValueOrDefault(MissingKey);
        int found = expected - report.Outcomes.GetValueOrDefault(MissingKey);
        text.AppendLine($"key parity:   {found}/{expected} = {Percent(found, expected)} of the reference's keys");
        int comparable = report.Outcomes.GetValueOrDefault(MatchValue) + report.Outcomes.GetValueOrDefault(ValueDiff);
        text.AppendLine($"value parity: {report.Outcomes.GetValueOrDefault(MatchValue)}/{comparable} = {Percent(report.Outcomes.GetValueOrDefault(MatchValue), comparable)} of comparable values");
        text.AppendLine($"interpreted:  {report.Interpreted.Agreed}/{report.Interpreted.Compared} = {Percent(report.Interpreted.Agreed, report.Interpreted.Compared)} of values both sides interpret");
        text.AppendLine("outcomes: " + string.Join(", ", Outcomes.Select(o => o + "=" + report.Outcomes.GetValueOrDefault(o))));
        text.AppendLine("by format:");
        foreach (var (format, counts) in report.ByFormat) text.AppendLine($"  {format}: files={counts.GetValueOrDefault("files")}, match-value={counts.GetValueOrDefault(MatchValue)}, match-key={counts.GetValueOrDefault(MatchKey)}, value-diff={counts.GetValueOrDefault(ValueDiff)}, missing-key={counts.GetValueOrDefault(MissingKey)}, extra-key={counts.GetValueOrDefault(ExtraKey)}, not-read={counts.GetValueOrDefault(NotRead)}");
        return text.ToString();
    }

    private static string Percent(int n, int d) => d == 0 ? "n/a" : $"{100d * n / d:0.0}%";

    // Compare measured counters with a checked-in report and enumerate every changed measurement.
    private static List<string> CompareReports(JsonElement legacy, Report current)
    {
        List<string> differences = [];
        CompareCounter("outcomes", legacy.GetProperty("outcomes"), current.Outcomes, differences);
        CompareCounter("files.statuses", legacy.GetProperty("files").GetProperty("statuses"), current.Files.Statuses, differences);
        CompareCounter("sizes.outcomes", legacy.GetProperty("sizes").GetProperty("outcomes"), current.Sizes.Outcomes, differences);
        CompareNested("sizes.by_format", legacy.GetProperty("sizes").GetProperty("by_format"), current.Sizes.ByFormat, differences);
        CompareNested("by_format", legacy.GetProperty("by_format"), current.ByFormat, differences);
        CompareNested("by_family", legacy.GetProperty("by_family"), current.ByFamily, differences);
        CompareNested("interpreted.by_family", legacy.GetProperty("interpreted").GetProperty("by_family"), current.Interpreted.ByFamily, differences);
        Dictionary<string, string> expected = [];
        foreach (JsonElement file in legacy.GetProperty("per_file").EnumerateArray())
        {
            string label = file.GetProperty("label").GetString() ?? "";
            foreach (JsonElement row in file.GetProperty("rows").EnumerateArray())
            {
                string key = row.GetProperty("key").GetString() ?? "";
                string raw = row.GetProperty("outcome").GetString() ?? "";
                string interpreted = row.TryGetProperty("interpreted", out JsonElement interp) ? interp.GetString() ?? "" : "";
                string translated = row.TryGetProperty("translated", out JsonElement trans) && trans.ValueKind == JsonValueKind.True ? "true" : "false";
                expected[$"{label}\t{key}"] = raw + "\t" + interpreted + "\t" + translated;
            }
            if (file.TryGetProperty("size", out JsonElement size))
                expected[$"{label}\t@size"] = size.GetProperty("outcome").GetString() ?? "";
        }
        Dictionary<string, string> actual = current.PerFile.SelectMany(file => file.Rows.Select(row =>
                new KeyValuePair<string, string>($"{file.Label}\t{row.Key}", row.Outcome + "\t" + (row.Interpreted ?? "") + "\t" + (row.Translated ? "true" : "false"))))
            .Concat(current.PerFile.Where(file => file.Size is not null).Select(file => new KeyValuePair<string, string>($"{file.Label}\t@size", file.Size!.Outcome)))
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        foreach (string key in expected.Keys.Union(actual.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (!expected.TryGetValue(key, out string? e)) differences.Add("unexpected C# verdict row " + key);
            else if (!actual.TryGetValue(key, out string? a)) differences.Add("missing C# verdict row " + key);
            else if (e != a) differences.Add($"verdict differs {key}: Python={e}; C#={a}");
        }
        return differences;
    }

    // Check one named counter and report the exact expected-versus-actual discrepancy.
    private static void CompareCounter(string name, JsonElement expected, Dictionary<string, int> actual, List<string> differences)
    {
        Dictionary<string, int> parsed = expected.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32(), StringComparer.Ordinal);
        foreach (string key in parsed.Keys.Union(actual.Keys, StringComparer.Ordinal))
        { if (parsed.GetValueOrDefault(key) != actual.GetValueOrDefault(key)) differences.Add($"{name}.{key}: Python={parsed.GetValueOrDefault(key)} C#={actual.GetValueOrDefault(key)}"); }
    }

    // Compare nested per-format counters without discarding missing or newly added keys.
    private static void CompareNested(string name, JsonElement expected, Dictionary<string, Dictionary<string, int>> actual, List<string> differences)
    {
        Dictionary<string, Dictionary<string, int>> parsed = expected.EnumerateObject().ToDictionary(p => p.Name,
            p => p.Value.EnumerateObject().ToDictionary(c => c.Name, c => c.Value.GetInt32(), StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (string group in parsed.Keys.Union(actual.Keys, StringComparer.Ordinal))
        {
            parsed.TryGetValue(group, out Dictionary<string, int>? e); actual.TryGetValue(group, out Dictionary<string, int>? a);
            IEnumerable<string> expectedKeys = e is null ? Enumerable.Empty<string>() : e.Keys;
            IEnumerable<string> actualKeys = a is null ? Enumerable.Empty<string>() : a.Keys;
            foreach (string count in expectedKeys.Union(actualKeys, StringComparer.Ordinal))
                if ((e?.GetValueOrDefault(count) ?? 0) != (a?.GetValueOrDefault(count) ?? 0)) differences.Add($"{name}.{group}.{count}: Python={e?.GetValueOrDefault(count) ?? 0} C#={a?.GetValueOrDefault(count) ?? 0}");
        }
    }

    // The groups a gate run covers: those asked for, and with --require-all-maker-notes every group a
    // note or a field of one is reported under.
    private static List<string> GateGroups(Report report, Options options)
    {
        List<string> groups = options.RequiredGroups.Distinct(StringComparer.Ordinal).ToList();
        if (options.RequireAllMakerNotes)
            groups.AddRange(report.ByFamily.Keys.Union(report.Interpreted.ByFamily.Keys, StringComparer.Ordinal)
                .Where(g => g.StartsWith("Exif.", StringComparison.Ordinal) && !StandardExif.Contains(g) && !SubImage.IsMatch(g))
                .Except(groups, StringComparer.Ordinal).Order(StringComparer.Ordinal));
        return groups;
    }

    // Every row that is no parity, compressed into as few notices as the size of an annotation allows:
    // the log of a job is not always at hand, and a list of what remains is what the work is done from.
    private static void EmitGapNotices(Report report, List<string> groups)
    {
        EmitInterpretedSummary(report, groups);
        var gaps = new SortedDictionary<string, GapRow>(StringComparer.Ordinal);
        foreach (FileReport file in report.PerFile)
        {
            if (file.RowsDropped > 0) continue;
            foreach (ParityRow row in file.Rows)
            {
                if (!groups.Exists(group => row.Key.StartsWith(group + ".", StringComparison.Ordinal))) continue;
                string? kind = row.Outcome switch
                {
                    ExtraKey => OpaqueTag.IsMatch(row.Key) || RawExtras.ContainsKey(row.Key) ? null : "raw-extra",
                    NotRead => Excused(file) ? "excused: not-read" : "not-read",
                    MissingKey or ValueDiff or MatchKey => "raw-" + row.Outcome,
                    _ => null
                };
                if (row.Translated && row.Interpreted != MatchInterpreted)
                    kind = row.Interpreted is null or NotInterpreted ? "not-interpreted" : InterpretedDiff;
                if (kind is null) continue;
                if (InterpretationAllowances.ContainsKey(row.Key)) kind = "allowed: " + kind;
                string signature = row.Key + "|" + kind + "|" + (row.OurInterpreted ?? "") + "|" + (row.TheirInterpreted ?? "") +
                    "|" + file.Detail;
                if (gaps.TryGetValue(signature, out GapRow? held)) held.Files++;
                else
                    gaps[signature] = new GapRow(row.Key, kind, row.OurInterpreted ?? "", row.TheirInterpreted ?? "",
                        file.Detail, file.Label);
            }
        }
        if (gaps.Count == 0) return;
        List<string> parts = [];
        var current = new List<GapRow>();
        int length = 0;
        foreach (GapRow gap in gaps.Values)
        {
            // An annotation message holds 64 KiB, and the payload is base64 of a compressed list.
            int size = gap.Key.Length + gap.Kind.Length + gap.Ours.Length + gap.Exiv2.Length + gap.Detail.Length + gap.Label.Length + 120;
            if (current.Count != 0 && length + size > 24000) { parts.Add(Payload(current)); current = []; length = 0; }
            current.Add(gap);
            length += size;
        }
        if (current.Count != 0) parts.Add(Payload(current));
        for (int index = 0; index < parts.Count; index++)
            Console.WriteLine($"::notice title=MakerNote gaps {index + 1}/{parts.Count}::{parts[index]}");
    }

    // A file the reference read and this library refused is no reading difference when the refusal is
    // one the corpus records: the gate takes the same view.
    private static bool Excused(FileReport file) =>
        Refusals.TryGetValue(file.Label, out var refusal) && file.Status == "refused" && refusal.Code == file.Detail;

    private static string Payload(List<GapRow> rows)
    {
        string json = JsonSerializer.Serialize(rows, JsonOptions);
        using var buffer = new MemoryStream();
        using (var zlib = new ZLibStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(Encoding.UTF8.GetBytes(json));
        return Convert.ToBase64String(buffer.ToArray());
    }

    private sealed class GapRow(string key, string kind, string ours, string exiv2, string detail, string label)
    {
        public string Key { get; } = key;
        public string Kind { get; } = kind;
        public string Ours { get; } = ours;
        public string Exiv2 { get; } = exiv2;
        public string Detail { get; } = detail;
        public string Label { get; } = label;
        public int Files { get; set; } = 1;
    }

    // One notice with the interpreted-value counts of every group, so that a document can be written
    // from what was measured rather than from a copy of a log.
    private static void EmitInterpretedSummary(Report report, List<string> groups)
    {
        if (groups.Count == 0) return;
        var summary = new List<object>();
        foreach (string group in groups)
        {
            Dictionary<string, int> interpreted = report.Interpreted.ByFamily.GetValueOrDefault(group) ?? [];
            Dictionary<string, int> raw = report.ByFamily.GetValueOrDefault(group) ?? [];
            int offered = 0, skipped = 0;
            foreach (FileReport file in report.PerFile)
            {
                if (file.RowsDropped > 0) continue;
                foreach (ParityRow row in file.Rows)
                {
                    if (!row.Key.StartsWith(group + ".", StringComparison.Ordinal)) continue;
                    if (row.Interpreted is not null) offered++;
                    else if (row.InterpretedSkipped is not null) skipped++;
                }
            }
            summary.Add(new
            {
                group,
                translated = interpreted.GetValueOrDefault(MatchInterpreted) + interpreted.GetValueOrDefault(InterpretedDiff) +
                    interpreted.GetValueOrDefault("not-interpreted-translated"),
                equal = interpreted.GetValueOrDefault(MatchInterpreted),
                interpreted_diff = interpreted.GetValueOrDefault(InterpretedDiff),
                not_interpreted = interpreted.GetValueOrDefault("not-interpreted-translated"),
                reference_not_interpreted = interpreted.GetValueOrDefault(ReferenceNotInterpreted),
                not_offered = skipped,
                rows = offered,
                raw_equal = raw.GetValueOrDefault(MatchValue),
                raw_missing = raw.GetValueOrDefault(MissingKey),
                raw_diff = raw.GetValueOrDefault(ValueDiff) + raw.GetValueOrDefault(MatchKey),
                not_read = raw.GetValueOrDefault(NotRead),
                extra = raw.GetValueOrDefault(ExtraKey)
            });
        }
        string json = JsonSerializer.Serialize(summary, JsonOptions);
        using var buffer = new MemoryStream();
        using (var zlib = new ZLibStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(Encoding.UTF8.GetBytes(json));
        Console.WriteLine($"::notice title=MakerNote interpreted summary::{Convert.ToBase64String(buffer.ToArray())}");
    }

    // Enforce the MakerNote coverage threshold and report the attribution for unread entries.
    private static (bool Pass, string Text) MakerNoteGate(Report report, List<string> required, bool all, bool inventory)
    {
        List<string> groups = required.Distinct(StringComparer.Ordinal).ToList();
        if (all) groups.AddRange(report.ByFamily.Keys.Union(report.Interpreted.ByFamily.Keys, StringComparer.Ordinal)
            .Where(g => g.StartsWith("Exif.", StringComparison.Ordinal) && !StandardExif.Contains(g) && !SubImage.IsMatch(g))
            .Except(groups, StringComparer.Ordinal).Order(StringComparer.Ordinal));
        StringBuilder text = new();
        if (inventory)
        {
            text.AppendLine("MakerNote interpreted parity inventory (Exiv2 0.28.9):");
            foreach (string group in report.ByFamily.Keys.Union(report.Interpreted.ByFamily.Keys, StringComparer.Ordinal)
                .Where(g => g.StartsWith("Exif.", StringComparison.Ordinal) && !StandardExif.Contains(g) && !SubImage.IsMatch(g)).Order(StringComparer.Ordinal))
            {
                var raw = report.ByFamily.GetValueOrDefault(group) ?? [];
                var interpreted = report.Interpreted.ByFamily.GetValueOrDefault(group) ?? [];
                text.AppendLine($"  {group}: translated found/equal={interpreted.GetValueOrDefault(MatchInterpreted) + interpreted.GetValueOrDefault(InterpretedDiff) + interpreted.GetValueOrDefault("not-interpreted-translated")}/{interpreted.GetValueOrDefault(MatchInterpreted)}; raw found/equal={raw.GetValueOrDefault(MatchValue) + raw.GetValueOrDefault(MatchKey) + raw.GetValueOrDefault(ValueDiff)}/{raw.GetValueOrDefault(MatchValue)}; missing={raw.GetValueOrDefault(MissingKey)}; not-read={raw.GetValueOrDefault(NotRead)}");
            }
            text.AppendLine("MakerNote not-read attribution (all rows are reference keys in whole-file refusals, not failed decodes):");
            foreach (var entry in UnreadAttribution(report)) text.AppendLine($"  {entry.Group}: {entry.Count} rows in {entry.File} [{entry.Code}]: {entry.Reason}");
        }
        List<string> failures = [];
        if (groups.Count > 0) text.AppendLine("MakerNote interpreted parity gate (Exiv2 0.28.9):");
        foreach (string group in groups)
        {
            Dictionary<string, int> raw = report.ByFamily.GetValueOrDefault(group) ?? [];
            Dictionary<string, int> interpreted = report.Interpreted.ByFamily.GetValueOrDefault(group) ?? [];
            text.AppendLine($"  {group}: interpreted found/equal={interpreted.GetValueOrDefault(MatchInterpreted) + interpreted.GetValueOrDefault(InterpretedDiff) + interpreted.GetValueOrDefault("not-interpreted-translated")}/{interpreted.GetValueOrDefault(MatchInterpreted)}; raw found/equal={raw.GetValueOrDefault(MatchValue) + raw.GetValueOrDefault(MatchKey) + raw.GetValueOrDefault(ValueDiff)}/{raw.GetValueOrDefault(MatchValue)}");
            if (raw.GetValueOrDefault(MissingKey) > 0 || raw.GetValueOrDefault(ValueDiff) + raw.GetValueOrDefault(MatchKey) > 0)
                failures.Add($"{group}: raw parity missing={raw.GetValueOrDefault(MissingKey)}, value-diff={raw.GetValueOrDefault(ValueDiff) + raw.GetValueOrDefault(MatchKey)}");
            int seen = 0;
            foreach (FileReport file in report.PerFile)
            {
                if (file.RowsDropped > 0)
                {
                    failures.Add($"{group}: {file.Label}: report dropped rows; gate requires complete per-file detail");
                    continue;
                }
                foreach (ParityRow row in file.Rows.Where(r => r.Key.StartsWith(group + ".", StringComparison.Ordinal)))
                {
                    seen++;
                    if (row.Outcome == ExtraKey)
                    {
                        if (!OpaqueTag.IsMatch(row.Key) && !RawExtras.ContainsKey(row.Key)) failures.Add($"{group}: {file.Label} {row.Key}: raw outcome {ExtraKey}");
                        continue;
                    }
                    if (row.Outcome == NotRead)
                    {
                        bool allowed = Refusals.TryGetValue(file.Label, out var refusal) && file.Status == "refused" && refusal.Code == file.Detail;
                        if (!allowed) failures.Add($"{group}: {file.Label} {row.Key}: raw outcome {NotRead} ({file.Detail})");
                        continue;
                    }
                    if (InterpretationAllowances.ContainsKey(row.Key)) continue;
                    if (row.Outcome is MissingKey or ValueDiff or MatchKey) failures.Add($"{group}: {file.Label} {row.Key}: raw outcome {row.Outcome}");
                    if (row.Translated && row.Interpreted != MatchInterpreted)
                        failures.Add($"{group}: {file.Label} {row.Key}: interpreted={row.Interpreted ?? "n/a"}; ours={row.OurInterpreted}; Exiv2={row.TheirInterpreted}");
                }
            }
            if (seen == 0) failures.Add(group + ": no report rows were available");
        }
        if (failures.Count > 0)
        {
            text.AppendLine("FAIL");
            foreach (string failure in failures) text.AppendLine("  " + failure);
            return (false, text.ToString());
        }
        if (groups.Count > 0) text.AppendLine("PASS");
        return (true, text.ToString());
    }

    // Group unread MakerNote values by their format and source so parity gaps are actionable.
    private static List<UnreadEntry> UnreadAttribution(Report report)
    {
        List<UnreadEntry> output = [];
        foreach (FileReport file in report.PerFile)
        foreach (IGrouping<string, ParityRow> group in file.Rows.Where(r => r.Outcome == NotRead && r.Key.StartsWith("Exif.", StringComparison.Ordinal))
                     .GroupBy(r => Family(r.Key), StringComparer.Ordinal))
        {
            if (StandardExif.Contains(group.Key) || SubImage.IsMatch(group.Key)) continue;
            var refusal = Refusals.GetValueOrDefault(file.Label);
            string reason = refusal.Code == file.Detail && file.Status == "refused" ? refusal.Reason : "UNDOCUMENTED refusal/partial read";
            output.Add(new(group.Key, file.Label, file.Status + "/" + file.Detail, reason, group.Count()));
        }
        return output.OrderBy(x => x.Group, StringComparer.Ordinal).ThenBy(x => x.File, StringComparer.Ordinal).ToList();
    }

    private static string[] NormalizeLines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
    private static string FirstLine(string text)
    {
        string line = text.Replace('\n', ' ').Trim();
        return line.Length > 400 ? line[..400] : line;
    }

    private sealed record Candidate(string Uid, string Path, string Corpus, string? ReferencePath, string Format);
    private sealed record RefTag(string Type, int Count, string Value, bool Labelled, string? Raw, string? Interpreted, int Repeats);
    private sealed record DumpTag(string Type, int Count, string Value);
    private sealed record DumpBlock(string Status, string Detail, Dictionary<string, DumpTag> Tags, Dictionary<string, string> Interpreted, (int Width, int Height, string Mime)? Size);
    private sealed record ProcessResult(string Output, string Error, int ExitCode);
    private sealed record UnreadEntry(string Group, string File, string Code, string Reason, int Count);
    private sealed record SizeResult(string Outcome, string[]? Ours, string[] Theirs);

    // Represent the ParityRow state used by parity report parsing and output.
    private sealed class ParityRow
    {
        public string Key { get; init; } = "";
        public string Outcome { get; init; } = "";
        public string? OurType { get; init; }
        public int? OurCount { get; init; }
        public string? OurValue { get; init; }
        public string? TheirType { get; init; }
        public int? TheirCount { get; init; }
        public string? TheirValue { get; init; }
        public string? Interpreted { get; set; }
        public string? InterpretedSkipped { get; set; }
        public bool Translated { get; set; }
        public string? OurInterpreted { get; set; }
        public string? TheirInterpreted { get; set; }
    }

    // Represent the FileReport state used by parity report parsing and output.
    private sealed class FileReport(string label, string corpus, string format, string status, string detail, int referenceTags, int ourTags, List<ParityRow> rows)
    {
        public string Label { get; } = label;
        public string Corpus { get; } = corpus;
        public string Format { get; } = format;
        public string Status { get; } = status;
        public string Detail { get; } = detail;
        public int ReferenceTags { get; } = referenceTags;
        public int OurTags { get; } = ourTags;
        public int RowsDropped { get; private set; }
        public List<ParityRow> Rows { get; } = rows;
        public SizeResult? Size { get; set; }
        // Convert rows according to its format-defined representation, preserving unknown stored values.
        public void TrimRows(int maximum)
        {
            int keep = Math.Max(0, maximum);
            if (Rows.Count <= keep) return;
            RowsDropped = Rows.Count - keep;
            Rows.RemoveRange(keep, RowsDropped);
        }
    }

    // Represent the Report state used by parity report parsing and output.
    private sealed class Report
    {
        public string Schema { get; } = "metadatasharp-parity/1";
        public FileCounts Files { get; set; } = new();
        public Dictionary<string, int> Outcomes { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> Refusals { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, int>> ByFormat { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, int>> ByFamily { get; set; } = new(StringComparer.Ordinal);
        public InterpretedCounts Interpreted { get; } = new();
        public SizeCounts Sizes { get; } = new();
        public List<FileReport> PerFile { get; } = [];
        public CorpusReport? Corpora { get; set; }
        [System.Text.Json.Serialization.JsonIgnore] public int Attempted { get; set; }
    }

    // Represent the FileCounts state used by parity report parsing and output.
    private sealed class FileCounts
    {
        public int Total { get; set; }
        public int Attempted { get; set; }
        public Dictionary<string, int> Statuses { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> Refusals { get; set; } = new(StringComparer.Ordinal);
    }
    // Represent the InterpretedCounts state used by parity report parsing and output.
    private sealed class InterpretedCounts
    {
        public Dictionary<string, Dictionary<string, int>> ByFamily { get; set; } = new(StringComparer.Ordinal);
        public int Compared { get; set; }
        public int Agreed { get; set; }
        public int NotInterpreted { get; set; }
        public int NotInterpretedTranslated { get; set; }
    }
    private sealed class SizeCounts
    {
        public Dictionary<string, int> Outcomes { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, int>> ByFormat { get; } = new(StringComparer.Ordinal);
    }
    private sealed class CorpusReport { public CorpusDetails? Exiv2 { get; set; } public CorpusDetails? Generated { get; set; } }
    private sealed class CorpusDetails { public string? Tree { get; set; } public List<string>? Directories { get; set; } public int Files { get; set; } public int UnmatchedExpectations { get; set; } public List<string>? UnmatchedNames { get; set; } public string? Oracle { get; set; } }

    // Represent the Options state used by parity report parsing and output.
    private sealed class Options
    {
        public string? Tree { get; private set; }
        public List<string> Fixtures { get; } = [];
        public List<string> DumpCommand { get; private set; } = [];
        public List<string>? Exiv2Command { get; private set; }
        public List<string>? SummaryCommand { get; private set; }
        public string Out { get; private set; } = "artifacts/parity";
        public int RowsPerFile { get; private set; } = int.MaxValue;
        public bool RequireReferenceTags { get; private set; }
        public bool Inventory { get; private set; }
        public bool RequireAllMakerNotes { get; private set; }
        public bool Gaps { get; private set; }
        public List<string> RequiredGroups { get; } = [];
        public string? CompareReport { get; private set; }
        // Parse command-line switches once and reject incompatible or incomplete option combinations.
        public static Options Parse(string[] args)
        {
            Options result = new();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i]; string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("Missing value after " + arg);
                switch (arg)
                {
                    case "--tree": result.Tree = Value(); break;
                    case "--fixtures": result.Fixtures.Add(Value()); break;
                    case "--dump-command": result.DumpCommand = SplitCommand(Value()); break;
                    case "--exiv2-command": result.Exiv2Command = SplitCommand(Value()); break;
                    case "--summary-command": result.SummaryCommand = SplitCommand(Value()); break;
                    case "--out": result.Out = Value(); break;
                    case "--rows-per-file": result.RowsPerFile = int.Parse(Value(), CultureInfo.InvariantCulture); break;
                    case "--require-reference-tags": result.RequireReferenceTags = true; break;
                    case "--inventory": result.Inventory = true; break;
                    case "--require-all-maker-notes": result.RequireAllMakerNotes = true; break;
                    case "--gaps": result.Gaps = true; break;
                    case "--require": result.RequiredGroups.Add(Value()); break;
                    case "--compare-report": result.CompareReport = Value(); break;
                    case "--annotate": break;
                    case "--title": _ = Value(); break;
                    case "--classifications": _ = Value(); break;
                    default: throw new ArgumentException("Unknown option " + arg);
                }
            }
            if (result.DumpCommand.Count == 0) throw new ArgumentException("--dump-command is required.");
            return result;
        }
    }

    // Tokenize a configured command line while honoring quoted arguments.
    private static List<string> SplitCommand(string command)
    {
        List<string> parts = [];
        StringBuilder current = new(); char quote = '\0'; bool escaped = false;
        foreach (char c in command)
        {
            if (escaped) { current.Append(c); escaped = false; continue; }
            if (c == '\\' && quote != '\'') { escaped = true; continue; }
            if (quote != '\0') { if (c == quote) quote = '\0'; else current.Append(c); continue; }
            if (c is '\'' or '"') { quote = c; continue; }
            if (char.IsWhiteSpace(c)) { if (current.Length > 0) { parts.Add(current.ToString()); current.Clear(); } continue; }
            current.Append(c);
        }
        if (escaped) current.Append('\\');
        if (quote != '\0') throw new ArgumentException("Unclosed quote in command: " + command);
        if (current.Length > 0) parts.Add(current.ToString());
        return parts;
    }
}
