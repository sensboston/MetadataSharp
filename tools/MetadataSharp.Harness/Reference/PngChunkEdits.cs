using System.Text;
using System.Text.RegularExpressions;
using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Reference;

/// <summary>A row of a -pS print-out: the chunk type, its length, the data column and the checksum in hexadecimal.</summary>
internal sealed record ChunkRow(string Type, long Length, string Data, string Checksum)
{
    /// <summary>What identifies a chunk: its type, its length and its checksum.</summary>
    public (string Type, long Length, string Checksum) Identity => (Type, Length, Checksum);
}

/// <summary>One row of chunks.tsv. For a comment, Comment is null when the file is to have none; for a profile,
/// Expected is "-" or "iCCP".</summary>
internal sealed record ChunkEntry(string Source, string Edited, string Kind, string Expected, (string Chunk, string Text)? Comment);

// The pinned exiv2 must list the chunks of a PNG as this library's profile or comment edit left them. Formerly
// tools/reference/png_chunk_edits.py.
//
// A separate check from key parity, and the companion of embedded-edits for what the tool cannot print as a value. The
// tool reads the comment of a PNG from the keyword Description; the keyword Comment, which this library writes, it only
// lists. So the tool is asked for the structure of the file (-pS): one row per chunk with its type, its length, the
// first bytes of its data and the checksum it ends with. The generator (png-webp-embedded-edits) writes chunks.tsv with
// one row per edited file:
//
//     source <TAB> edited <TAB> kind <TAB> expected
//
// For the kind "comment", expected is "-" when the edited file is to have no chunk with the keyword Comment, or
// TYPE:HEX, the type of the one chunk it is to have (tEXt or iTXt) and its text as UTF-8 in hexadecimal. The data of
// that chunk is built here from the text, in ISO-8859-1 for tEXt and in UTF-8 after an empty language and translated
// keyword for iTXt, and its length and its CRC-32 are held against the row the tool prints. The chunk has to stand
// where the first such chunk of the source stood, or directly before the first IDAT.
//
// For the kind "profile", expected is "-" when the edited file is to have no iCCP chunk, or "iCCP" when it is to have
// one: named "ICC Profile", before PLTE and IDAT, where that of the source stood or directly after IHDR, and without an
// sRGB chunk beside it. What the chunk inflates to is the business of embedded-edits (-pC).
//
// In both kinds every other chunk of the source has to be listed for the edited file in the same order with the same
// type, length and checksum.
//
// With --skip-unreadable-sources a source whose structure the tool does not print is counted and skipped, for corpus
// files that are damaged on purpose.
//
// Usage: png-chunk-edits DIRECTORY --exiv2 PATH [--skip-unreadable-sources] [--title TEXT]
internal static class PngChunkEdits
{
    public const string None = "-";
    public const string Structure = "-pS";
    public static readonly string[] TextTypes = ["tEXt", "zTXt", "iTXt"];
    public const string ProfileName = "ICC Profile";

    // "%8d | %-5s |%8d | " + 30 characters + " | 0x%08x": the data column may hold any printable character.
    public static readonly Regex Row = new($@"^ *(\d+) \| ({PyRe.NonSpace}{{4}}) +\| *(\d+) \| (.{{30}}) \| 0x([0-9a-f]{{8}})$", RegexOptions.CultureInvariant);

    public static List<ChunkEntry> ParseManifest(string text)
    {
        var entries = new List<ChunkEntry>();
        int number = 0;
        foreach (string line in PyText.SplitLines(text))
        {
            number++;
            if (line.Length == 0) continue;
            var fields = line.Split('\t');
            if (fields.Length != 4 || fields.Any(field => field.Length == 0)) throw new PyException("ValueError", $"manifest line {number} does not have four columns");
            string source = fields[0], edited = fields[1], kind = fields[2], expected = fields[3];
            (string, string)? comment = null;
            if (kind == "comment")
            {
                if (expected != None)
                {
                    var (chunk, _, digits) = PyText.Partition(expected, ":");
                    if (chunk is not ("tEXt" or "iTXt")) throw new PyException("ValueError", $"manifest line {number}: a comment is written as tEXt or iTXt");
                    try
                    {
                        comment = (chunk, new UTF8Encoding(false, true).GetString(PyBytes.FromHex(digits)));
                    }
                    catch (Exception error) when (error is FormatException or DecoderFallbackException)
                    {
                        throw new PyException("ValueError", $"manifest line {number}: the comment is not UTF-8 in hexadecimal");
                    }
                    if (comment.Value.Item2.Length == 0) throw new PyException("ValueError", $"manifest line {number}: the comment is empty");
                }
            }
            else if (kind == "profile")
            {
                if (expected is not (None or "iCCP")) throw new PyException("ValueError", $"manifest line {number}: a profile is '-' or 'iCCP'");
            }
            else
            {
                throw new PyException("ValueError", $"manifest line {number}: the kind is 'comment' or 'profile'");
            }
            entries.Add(new(source, edited, kind, expected, comment));
        }
        if (entries.Count == 0) throw new PyException("ValueError", "the manifest names no edited file");
        return entries;
    }

    /// <summary>The rows of a -pS print-out; null when it is not a whole PNG.</summary>
    public static List<ChunkRow>? Chunks(byte[] output)
    {
        var rows = new List<ChunkRow>();
        foreach (string line in PyText.Latin1(output).Split('\n'))
        {
            var found = Row.Match(line);
            if (found.Success) rows.Add(new(found.Groups[2].Value, long.Parse(found.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture), found.Groups[4].Value, found.Groups[5].Value));
        }
        if (rows.Count < 3 || rows[0].Type != "IHDR" || rows[^1].Type != "IEND") return null;
        return rows;
    }

    /// <summary>A text chunk whose keyword is Comment: the tool prints the zero after it as a dot, and not at all when it
    /// ends the data.</summary>
    public static bool IsComment(ChunkRow row) =>
        TextTypes.Contains(row.Type) && (row.Data.StartsWith("Comment.", StringComparison.Ordinal) || (row.Length == 8 && PyText.RStrip(row.Data, " ") == "Comment"));

    /// <summary>The type, the length and the checksum of the chunk a comment is written as; null when a tEXt chunk
    /// cannot hold the text.</summary>
    public static (string Type, long Length, string Checksum)? CommentChunk(string kind, string text)
    {
        byte[] body;
        if (kind == "tEXt")
        {
            if (text.Any(c => c > 0xFF)) return null;
            body = [.. "Comment\0"u8, .. Encoding.Latin1.GetBytes(text)];
        }
        else
        {
            body = [.. "Comment\0\0\0\0\0"u8, .. Encoding.UTF8.GetBytes(text)];
        }
        byte[] typed = [.. Encoding.ASCII.GetBytes(kind), .. body];
        return (kind, body.Length, Zlib.Crc32(typed).ToString("x8", System.Globalization.CultureInfo.InvariantCulture));
    }

    public static string FirstDifference(List<(string Type, long Length, string Checksum)> before, List<(string Type, long Length, string Checksum)> after)
    {
        for (int index = 0; index < Math.Min(before.Count, after.Count); index++)
        {
            var (was, now) = (before[index], after[index]);
            if (was != now)
                return $"chunk {index + 1} of the others was {was.Type} of {was.Length} bytes (0x{was.Checksum}) and is {now.Type} of {now.Length} bytes (0x{now.Checksum})";
        }
        var (longer, name) = before.Count > after.Count ? (before, "fewer") : (after, "more");
        var extra = longer[Math.Min(before.Count, after.Count)];
        return $"{Math.Abs(before.Count - after.Count)} chunk(s) {name} than the edit names, the first a {extra.Type} of {extra.Length} bytes";
    }

    private static bool SameIdentities(List<(string, long, string)> left, List<(string, long, string)> right) => left.SequenceEqual(right);

    /// <summary>row[index] with Python's negative indexes.</summary>
    private static ChunkRow At(List<ChunkRow> rows, int index) => rows[index < 0 ? rows.Count + index : index];

    /// <summary>What the chunk list of the edited file lacks for a comment edit (null: the file is to have none); an empty
    /// list when nothing.</summary>
    public static List<string> CompareComment((string Chunk, string Text)? expected, List<ChunkRow> source, List<ChunkRow> edited)
    {
        var problems = new List<string>();
        var before = source.Where(row => !IsComment(row)).Select(row => row.Identity).ToList();
        var after = edited.Where(row => !IsComment(row)).Select(row => row.Identity).ToList();
        if (!SameIdentities(before, after)) problems.Add("the chunks that are no comment changed: " + FirstDifference(before, after));
        var found = edited.Select((row, index) => (row, index)).Where(pair => IsComment(pair.row)).Select(pair => pair.index).ToList();
        if (expected is null)
        {
            if (found.Count > 0) problems.Add($"{found.Count} chunk(s) with the keyword Comment are left");
            return problems;
        }
        if (found.Count != 1)
        {
            problems.Add($"{found.Count} chunks with the keyword Comment, expected one");
            return problems;
        }
        int at = found[0];
        var wanted = CommentChunk(expected.Value.Chunk, expected.Value.Text);
        if (wanted is null)
        {
            problems.Add("the manifest asks for a tEXt chunk of a text that is not ISO-8859-1");
            return problems;
        }
        if (edited[at].Identity != wanted.Value)
        {
            var row = edited[at];
            problems.Add($"the comment is a {row.Type} of {row.Length} bytes (0x{row.Checksum}), "
                         + $"expected a {wanted.Value.Type} of {wanted.Value.Length} bytes (0x{wanted.Value.Checksum})");
        }
        var old = source.Select((row, index) => (row, index)).Where(pair => IsComment(pair.row)).Select(pair => pair.index).ToList();
        int othersBefore = edited.Take(at).Count(row => !IsComment(row));
        if (old.Count > 0)
        {
            if (othersBefore != source.Take(old[0]).Count(row => !IsComment(row))) problems.Add("the comment does not stand where the first comment of the source stood");
        }
        else if (edited[at + 1].Type != "IDAT" || At(edited, at - 1).Type == "IDAT")
        {
            problems.Add("the comment does not stand directly before the first IDAT chunk");
        }
        return problems;
    }

    /// <summary>What the chunk list of the edited file lacks for a profile edit; an empty list when nothing.</summary>
    public static List<string> CompareProfile(string expected, List<ChunkRow> source, List<ChunkRow> edited)
    {
        var problems = new List<string>();
        string[] gone = expected == None ? ["iCCP"] : ["iCCP", "sRGB"];
        var before = source.Where(row => !gone.Contains(row.Type)).Select(row => row.Identity).ToList();
        var after = edited.Where(row => row.Type != "iCCP").Select(row => row.Identity).ToList();
        if (!SameIdentities(before, after)) problems.Add("the chunks that are no profile changed: " + FirstDifference(before, after));
        var found = edited.Select((row, index) => (row, index)).Where(pair => pair.row.Type == "iCCP").Select(pair => pair.index).ToList();
        if (expected == None)
        {
            if (found.Count > 0) problems.Add("an iCCP chunk is left");
            return problems;
        }
        if (found.Count != 1)
        {
            problems.Add($"{found.Count} iCCP chunks, expected one");
            return problems;
        }
        int at = found[0];
        // The name, its zero and the compression method 0, then the zlib stream.
        if (!edited[at].Data.StartsWith(ProfileName + "..", StringComparison.Ordinal))
            problems.Add($"the iCCP chunk is not named {Py.ReprString(ProfileName)} with the compression method 0: {Py.ReprString(PyText.RStrip(edited[at].Data))}");
        if (edited.Take(at).Any(row => row.Type is "PLTE" or "IDAT")) problems.Add("the iCCP chunk follows PLTE or IDAT");
        var old = source.Select((row, index) => (row, index)).Where(pair => pair.row.Type == "iCCP").Select(pair => pair.index).ToList();
        if (old.Count > 0)
        {
            if (edited.Take(at).Count(row => row.Type != "iCCP") != source.Take(old[0]).Count(row => !gone.Contains(row.Type)))
                problems.Add("the iCCP chunk does not stand where that of the source stood");
        }
        else if (at != 1)
        {
            problems.Add("the iCCP chunk does not stand directly after IHDR");
        }
        return problems;
    }

    /// <summary>Every problem found, as (file, message); the edited files checked; those skipped for their source.</summary>
    public static (List<(string Name, string Message)> Problems, int Checked, int Skipped) Check(string directory, string exiv2, BytesRunner runner, bool skipUnreadable = false)
    {
        directory = PyPath.Normalize(directory);
        var entries = ParseManifest(PyFile.ReadText(PyPath.Join(directory, "chunks.tsv")));
        var problems = new List<(string, string)>();
        var sources = new Dictionary<string, List<ChunkRow>?>(StringComparer.Ordinal);
        int checkedCount = 0, skipped = 0;
        foreach (var (source, edited, kind, expected, comment) in entries)
        {
            if (!sources.TryGetValue(source, out var before))
            {
                var (sourceCode, sourceOutput, _) = runner([exiv2, Structure], PyPath.Join(directory, source));
                sources[source] = before = sourceCode == 0 ? Chunks(sourceOutput) : null;
            }
            if (before is null)
            {
                if (skipUnreadable) skipped++;
                else problems.Add((source, $"the structure of the source is not printed with {Structure}"));
                continue;
            }
            checkedCount++;
            var (code, output, error) = runner([exiv2, Structure], PyPath.Join(directory, edited));
            var after = code == 0 ? Chunks(output) : null;
            if (after is null)
            {
                problems.Add((edited, $"{Structure} does not print the structure of the file (exit {code}): {PyText.Head(PyText.Strip(error), 300)}"));
                continue;
            }
            var found = kind == "comment" ? CompareComment(comment, before, after) : CompareProfile(expected, before, after);
            foreach (string problem in found) problems.Add((edited, problem));
        }
        return (problems, checkedCount, skipped);
    }

    public static int Run(string[] argv) => Execute(argv, EditChecks.RunBytes);

    public static int Execute(IReadOnlyList<string> argv, BytesRunner runner)
    {
        var parser = new ArgParser("png-chunk-edits", "The pinned exiv2 must list the chunks of a PNG as this library's profile or comment edit left them.")
            .Add("directory")
            .Add("--exiv2", required: true)
            .Add("--skip-unreadable-sources", ArgAction.StoreTrue)
            .Add("--title", @default: "PNG profile and comment chunks");
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        string title = args.Str("title");
        var (problems, count, skipped) = Check(args.Str("directory"), args.Str("exiv2"), runner, args.Flag("skip_unreadable_sources"));
        if (EditChecks.Report(title, problems, count, "edited file(s)")) return 1;
        string leftOut = skipped > 0 ? $" ({skipped} left out: the tool does not print the structure of their source)" : "";
        Out.Print($"::notice title={title}::{count} edited PNG files: the pinned tool lists ({Structure}) the comment or the "
                  + $"profile chunk each was to have, at its place, and every other chunk of the source unchanged{leftOut}");
        return 0;
    }
}
