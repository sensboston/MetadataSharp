using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Reference;

/// <summary>One row of manifest.tsv: the source, the edited file, the profile ("-", "=" or a file name) and the comment
/// ("-", "=", or the bytes it has to print).</summary>
internal sealed record EmbeddedEntry(string Source, string Edited, string Profile, string? CommentMode, byte[]? Comment);

// The pinned exiv2 must print the profile and the comment a file was given by this library. Formerly
// tools/reference/embedded_edits.py.
//
// A separate check from key parity: nothing here is compared with what this library reads. The fixture generator
// (jpeg-embedded-edits) writes authored JPEG files, the same files after their ICC profile or their comment was
// inserted, replaced or removed, and manifest.tsv with one row per edited file:
//
//     source <TAB> edited <TAB> profile <TAB> comment
//
// The profile is the name of a file of the directory that holds the bytes the tool has to print for the edited file
// with -pC, "-" when it has to print none, or "=" when it has to print what it prints for the source. The comment is
// the bytes the tool has to print with -pc, in hexadecimal, or "-" or "=" in the same sense.
//
// For each row the tool is run three ways on both files. The edited file passes when the tool exits with 0 for -pC and
// -pc, prints exactly the expected profile and the expected comment, and prints with -pa the lines, and exits with the
// code, it did for the source: an edit of the profile or the comment changes nothing of the Exif, IPTC and XMP data.
//
// The png-webp-embedded-edits command writes the same manifest for PNG and WebP files. Their comment column is always
// "=": a WebP has no comment, and the tool reads that of a PNG from the keyword Description, not from the keyword
// Comment this library writes, so an edit may not change what -pc prints. The comment chunk itself is the business of
// png-chunk-edits.
//
// The tiff-based-icc-edits and bmff-icc-edits commands write the same manifest for TIFF-based files and for HEIC and
// AVIF files, whose comment column is always "=" too: this library writes no comment into them.
//
// A TIFF-based file holds its profile in a field of its first directory, and the tool prints that field with -pa as
// well, under a key. Such a key is named with --profile-key. Its lines are taken out of both -pa print-outs before
// they are compared, and where -pa exits with 0 for the edited file, it has to print one line of such a key when the
// file is to have a profile and none when it is to have none.
//
// The tool begins the -pC print-out of an ORF and of an RW2 file with a line that names the format. That line is no
// part of the profile: it is taken off the print-out of a file with that extension.
//
// The generator's jpeg-embedded-corpus, png-webp-embedded-corpus, tiff-based-icc-corpus and bmff-icc-corpus commands
// write the same manifest for files this repository did not author. Many of those are damaged on purpose, so with
// --skip-unreadable-sources a source for which -pC or -pc does not exit with 0 is counted and skipped: the rule is
// about files the tool could read before the edit. Without the option such a source is a broken fixture, and a
// failure.
//
// Usage: embedded-edits DIRECTORY --exiv2 PATH [--skip-unreadable-sources] [--title TEXT] [--profile-key KEY]...
internal static class EmbeddedEdits
{
    public const string None = "-";
    public const string Same = "=";
    public const string Profile = "-pC";
    public const string CommentOption = "-pc";
    public const string Keys = "-pa";

    public static readonly Dictionary<string, byte[]> Banners = new(StringComparer.Ordinal)
    {
        [".orf"] = "ORF IMAGE\n"u8.ToArray(),
        [".rw2"] = "RW2 IMAGE\n"u8.ToArray(),
    };

    public static List<EmbeddedEntry> ParseManifest(string text)
    {
        var entries = new List<EmbeddedEntry>();
        int number = 0;
        foreach (string line in PyText.SplitLines(text))
        {
            number++;
            if (line.Length == 0) continue;
            var fields = line.Split('\t');
            if (fields.Length != 4 || fields.Any(field => field.Length == 0)) throw new PyException("ValueError", $"manifest line {number} does not have four columns");
            string comment = fields[3];
            if (comment is None or Same)
            {
                entries.Add(new(fields[0], fields[1], fields[2], comment, null));
                continue;
            }
            byte[] bytes;
            try
            {
                bytes = PyBytes.FromHex(comment);
            }
            catch (FormatException)
            {
                throw new PyException("ValueError", $"manifest line {number}: the comment is not hexadecimal");
            }
            entries.Add(new(fields[0], fields[1], fields[2], null, bytes));
        }
        if (entries.Count == 0) throw new PyException("ValueError", "the manifest names no edited file");
        return entries;
    }

    /// <summary>The comment of a -pc print-out: the tool ends it with one line break; null when it does not.</summary>
    public static byte[]? PrintedComment(byte[] output) => output.Length > 0 && output[^1] == (byte)'\n' ? output[..^1] : null;

    /// <summary>The profile of a -pC print-out: without the line the tool puts before that of an ORF or an RW2 file.</summary>
    public static byte[] PrintedProfile(string name, byte[] output)
    {
        byte[]? banner = Banners.GetValueOrDefault(PyPath.Suffix(name).ToLowerInvariant());
        return banner is not null && output.AsSpan().StartsWith(banner) ? output[banner.Length..] : output;
    }

    /// <summary>A -pa print-out without the lines of the given keys, and the number of those lines.</summary>
    public static (byte[] Output, int Count) WithoutKeys(byte[] output, IReadOnlyList<string> keys)
    {
        if (keys.Count == 0) return (output, 0);
        var kept = new List<byte>();
        int count = 0;
        foreach (byte[] line in EditChecks.SplitLinesKeepEnds(output))
        {
            byte[]? first = EditChecks.FirstField(line);
            if (first is not null && keys.Contains(PyText.Utf8Replace(first))) count++;
            else kept.AddRange(line);
        }
        return (kept.ToArray(), count);
    }

    /// <summary>Bytes for a message: their length, and the first of them.</summary>
    public static string Short(byte[] data) => $"{data.Length} bytes {EditChecks.Hex(data, 24)}" + (data.Length > 24 ? "..." : "");

    private static bool Same2(byte[] left, byte[] right) => left.AsSpan().SequenceEqual(right);

    /// <summary>
    /// What the print-outs of an edited file lack; an empty list when they lack nothing. Each of profile, comment and
    /// keys is (exit code, standard output, standard error) of one run on the edited file; sourceKeys is the same for
    /// -pa on the source. profileKeys are the keys under which -pa prints the profile itself.
    /// </summary>
    public static List<string> Compare(byte[] expectedProfile, byte[] expectedComment, BytesResult sourceKeys, BytesResult profile, BytesResult comment, BytesResult keys,
                                       IReadOnlyList<string>? profileKeys = null)
    {
        profileKeys ??= [];
        var problems = new List<string>();
        var (sourceLines, _) = WithoutKeys(sourceKeys.Stdout, profileKeys);
        var (lines, own) = WithoutKeys(keys.Stdout, profileKeys);
        if (profile.Code != 0) problems.Add($"{Profile} does not read the file (exit {profile.Code}): {PyText.Head(PyText.Strip(profile.Stderr), 300)}");
        else if (!Same2(profile.Stdout, expectedProfile)) problems.Add($"{Profile} prints {Short(profile.Stdout)}, expected {Short(expectedProfile)}");
        if (comment.Code != 0)
        {
            problems.Add($"{CommentOption} does not read the file (exit {comment.Code}): {PyText.Head(PyText.Strip(comment.Stderr), 300)}");
        }
        else
        {
            byte[]? found = PrintedComment(comment.Stdout);
            if (found is null) problems.Add($"{CommentOption} prints no line: {Short(comment.Stdout)}");
            else if (!Same2(found, expectedComment)) problems.Add($"{CommentOption} prints {Short(found)}, expected {Short(expectedComment)}");
        }
        if (keys.Code != sourceKeys.Code) problems.Add($"{Keys} exits with {keys.Code}, with {sourceKeys.Code} for the source");
        if (!Same2(lines, sourceLines))
        {
            var before = EditChecks.SplitLines(sourceLines);
            var after = EditChecks.SplitLines(lines);
            var gone = before.Where(line => !after.Any(other => Same2(other, line))).ToList();
            var added = after.Where(line => !before.Any(other => Same2(other, line))).ToList();
            byte[] detail = gone.Count > 0 ? gone[0] : added.Count > 0 ? added[0] : "the same lines in another order"u8.ToArray();
            problems.Add($"{Keys} prints {gone.Count} line(s) fewer and {added.Count} more than for the source: " + PyText.Head(PyText.Utf8Replace(detail), 200));
        }
        int wanted = expectedProfile.Length > 0 ? 1 : 0;
        if (profileKeys.Count > 0 && keys.Code == 0 && own != wanted)
            problems.Add($"{Keys} prints {own} line(s) of {string.Join(" or ", profileKeys)}, expected {wanted}");
        return problems;
    }

    /// <summary>The three runs on one file, the banner of its format taken off the profile.</summary>
    public static BytesResult[] Read(BytesRunner runner, string exiv2, string directory, string name)
    {
        string path = PyPath.Join(directory, name);
        var (code, output, errors) = runner([exiv2, Profile], path);
        return [new(code, PrintedProfile(name, output), errors), runner([exiv2, CommentOption], path), runner([exiv2, Keys], path)];
    }

    /// <summary>Every problem found, as (file, message); the edited files checked; those skipped for their source.</summary>
    public static (List<(string Name, string Message)> Problems, int Checked, int Skipped) Check(string directory, string exiv2, BytesRunner runner,
                                                                                               bool skipUnreadable = false, IReadOnlyList<string>? profileKeys = null)
    {
        profileKeys ??= [];
        directory = PyPath.Normalize(directory);
        var entries = ParseManifest(PyFile.ReadText(PyPath.Join(directory, "manifest.tsv")));
        var problems = new List<(string, string)>();
        var sources = new Dictionary<string, BytesResult[]>(StringComparer.Ordinal);
        var profiles = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        int checkedCount = 0, skipped = 0;
        foreach (var (source, edited, profile, commentMode, comment) in entries)
        {
            if (!sources.TryGetValue(source, out var read)) sources[source] = read = Read(runner, exiv2, directory, source);
            var (sourceProfile, sourceComment, sourceKeys) = (read[0], read[1], read[2]);
            var unread = new List<string>();
            if (sourceProfile.Code != 0) unread.Add(Profile);
            if (sourceComment.Code != 0) unread.Add(CommentOption);
            if (unread.Count == 0 && PrintedComment(sourceComment.Stdout) is null) unread = [CommentOption];
            if (unread.Count > 0)
            {
                if (skipUnreadable) skipped++;
                else problems.Add((source, $"the source is not read with {string.Join(" and ", unread)}"));
                continue;
            }
            byte[] expectedProfile;
            if (profile == Same) expectedProfile = sourceProfile.Stdout;
            else if (profile == None) expectedProfile = [];
            else
            {
                if (!profiles.TryGetValue(profile, out expectedProfile!)) profiles[profile] = expectedProfile = PyFile.ReadBytes(PyPath.Join(directory, profile));
            }
            byte[] expectedComment = commentMode == Same ? PrintedComment(sourceComment.Stdout)! : commentMode == None ? [] : comment!;
            checkedCount++;
            var results = Read(runner, exiv2, directory, edited);
            foreach (string problem in Compare(expectedProfile, expectedComment, sourceKeys, results[0], results[1], results[2], profileKeys)) problems.Add((edited, problem));
        }
        return (problems, checkedCount, skipped);
    }

    public static int Run(string[] argv) => Execute(argv, EditChecks.RunBytes);

    public static int Execute(IReadOnlyList<string> argv, BytesRunner runner)
    {
        var parser = new ArgParser("embedded-edits", "The pinned exiv2 must print the profile and the comment a file was given by this library.")
            .Add("directory")
            .Add("--exiv2", required: true)
            .Add("--skip-unreadable-sources", ArgAction.StoreTrue)
            .Add("--title", @default: "JPEG profile and comment edits")
            .Add("--profile-key", ArgAction.Append, @default: new List<string>());
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        string title = args.Str("title");
        var keys = args.List("profile_key");
        var (problems, count, skipped) = Check(args.Str("directory"), args.Str("exiv2"), runner, args.Flag("skip_unreadable_sources"), keys);
        if (EditChecks.Report(title, problems, count, "edited file(s)")) return 1;
        string leftOut = skipped > 0 ? $" ({skipped} left out: the tool does not read their source)" : "";
        string also = keys.Count > 0 ? $", but for {string.Join(" and ", keys)}, which it prints where there is a profile and nowhere else" : "";
        Out.Print($"::notice title={title}::{count} edited files: the pinned tool prints the profile ({Profile}) and the "
                  + $"comment ({CommentOption}) each was to have, and with {Keys} what it prints for the source{also}{leftOut}");
        return 0;
    }
}
