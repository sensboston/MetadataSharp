using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Parity;

// The run cannot be trusted; nothing should be concluded from its numbers.
internal sealed class PreviewParityError(string message) : Exception(message);

// Preview parity: what MetadataSharp.Dump --previews lists against what exiv2 -pp printed.
//
// The pinned exiv2 tree keeps the output of its own preview test in
// test/data/test_reference_files/preview-test.out: for every input a block
//
//     -----> NAME <-----
//
//     Command: exiv2 -pp NAME
//     Preview 1: image/jpeg, 160x120 pixels, 4158 bytes
//     Exit code: 0
//
// followed by the block of the extraction command, which says nothing about the list. That file is
// read as text; no external code is configured, built or run. The comparison is of a different kind
// from the key-by-key one in the C# key-parity report and shares none of its rules: a preview is the
// triple (MIME type, width x height, byte count), and a file's previews are compared as a multiset.
//
//     match    a reference preview we list with the same type, dimensions and length
//     missing  a reference preview we do not list
//     extra    a preview we list that the reference does not
//
// The order is judged separately: a file is "ordered" when the previews both sides list come in the
// same order on both.
//
// With --fixtures and --exiv2-command the expectation is not that text: the pinned tool lists each
// file of the given directories in the same run, as the C# key-parity report does for key parity.
// This is how a file is measured that the pinned tree holds but its preview test does not name.
//
// (Formerly tools/parity/run_previews.py; the arguments, reports and annotations are unchanged.)
internal static class Previews
{
    public const string Kind = "Preview";
    public const string Title = "PARITY preview";
    public static readonly string Reference = PyPath.Join("test", "data", "test_reference_files", "preview-test.out");

    private static readonly Regex BlockPattern = new(@"^-----> (?<name>.+) <-----$", RegexOptions.CultureInvariant);
    private static readonly Regex CommandPattern = new(@"^Command: exiv2 (?<arguments>.+)$", RegexOptions.CultureInvariant);
    private static readonly Regex PreviewPattern = new(@"^Preview \d+: (?<mime>" + PyRe.NonSpace + @"+), (?<width>\d+)x(?<height>\d+) pixels, (?<bytes>\d+) bytes$", RegexOptions.CultureInvariant);
    private static readonly Regex OutputNumber = new(@"-preview(\d+)(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SizePattern = new(@"^(\d+)x(\d+)\z", RegexOptions.CultureInvariant);

    public static readonly HashSet<string> PreviewFixtureSuffixes = new(StringComparer.Ordinal)
    {
        ".jpg", ".jpeg", ".tif", ".tiff", ".png", ".webp", ".crw", ".mrw", ".raf", ".rw2", ".sr2",
        ".jp2", ".psd", ".eps", ".pgf", ".jxl", ".avif", ".heic", ".heif", ".cr3",
    };

    // Explicit, reviewable exceptions for authored fixtures that Exiv2 0.28.9 cannot use as preview
    // parity inputs. Do not replace these entries with directory- or extension-wide exclusions.
    public static readonly PyDict FixtureOracleExceptions = PyDict.Of(
        ("b11eps/dos-eps-xmp.eps", "Exiv2 0.28.9 rejects this DOS EPS layout: explicit %%Page conflicts with its implicit page; its TIFF carrier remains covered by the EPS unit test."),
        ("b11eps/eps-plain.eps", "Metadata-only EPS parser fixture with no native preview; Exiv2 0.28.9 rejects its minimal DSC page layout before listing previews."),
        ("b11eps/eps-reference-write.eps", "Writer-parity EPS fixture with no preview payload; retained for the dedicated EPS write/read gate, not preview parity."),
        ("b11eps/eps-xmp-31.eps", "Metadata-only Illustrator 3.1/XMP parser fixture with no native preview; retained in EPS metadata tests."),
        ("b11eps/eps-xmp-crlf.eps", "Metadata-only CRLF/XMP parser fixture with no native preview; retained in EPS metadata tests."),
        ("b11eps/eps-xmp-nomain.eps", "Metadata-only NoMain/XMP parser fixture with no native preview; retained in EPS metadata tests."),
        ("b11eps/eps-xmp-readonly.eps", "Metadata-only read-only XMP parser fixture with no native preview; retained in EPS metadata tests."),
        ("b11eps/eps-xmp-space.eps", "Metadata-only OpenOffice/XMP parser fixture with no native preview; retained in EPS metadata tests."),
        ("b11eps/eps-xmp.eps", "Metadata-only XMP parser fixture with no native preview; Exiv2 0.28.9 rejects its minimal DSC page layout before listing previews."),
        ("b7/owned-codestream.jxl", "Standalone JPEG XL codestream, not a container image type recognized by Exiv2 0.28.9; the JXL container fixture remains gated."),
        ("b8/owned-flat-rgb.psd", "52-byte minimal PSD parser fixture is not recognized as an image by Exiv2 0.28.9; the metadata-bearing PSD fixture remains gated."));

    // Previews a file holds that the pinned tool does not list. Each entry names one file and one preview
    // exactly, with the reason the pinned tool misses it; a preview that stops being listed fails the gate
    // again, because an entry does not silence a difference, it records the one that remains.
    public static readonly Dictionary<string, (PyTuple Preview, string Reason)> ListedAndNotInReference = new(StringComparer.Ordinal)
    {
        ["minolta-a2-pict0881.mrw"] = (
            new PyTuple("image/jpeg", 640L, 480L, 56593L),
            "The note of this MRW counts its thumbnail from the start of the block the note lives in, and this "
            + "reader follows that reading: the pinned tool needs it for minolta-a1-raw_minolta_a1.mrw of the same "
            + "corpus, where its own reading holds no stream, and lists nothing here although the file holds this "
            + "picture at the address the note names."),
    };

    /// <summary>A preview: (MIME type, width, height, byte count).</summary>
    public static PyTuple Preview(string mime, long width, long height, long bytes) => new(mime, width, height, bytes);

    /// <summary>The previews exiv2 -pp listed for every file named in the expectation text, in its order.</summary>
    public static OrderedDictionary<string, List<PyTuple>> ParseReference(string text)
    {
        var expected = new OrderedDictionary<string, List<PyTuple>>(StringComparer.Ordinal);
        string? name = null;
        bool listing = false;
        foreach (string line in PyText.SplitLines(text))
        {
            var block = BlockPattern.Match(line);
            if (block.Success)
            {
                name = block.Groups["name"].Value;
                if (expected.ContainsKey(name)) throw new PreviewParityError($"the expectation names {Py.ReprString(name)} twice");
                expected[name] = [];
                listing = false;
                continue;
            }
            var command = CommandPattern.Match(line);
            if (command.Success)
            {
                // Only the listing command describes the previews; the extraction command repeats nothing.
                listing = PyText.Split(command.Groups["arguments"].Value)[0] == "-pp";
                continue;
            }
            var preview = PreviewPattern.Match(line);
            if (preview.Success)
            {
                if (name is null) throw new PreviewParityError("a preview line comes before any file block");
                if (listing)
                {
                    expected[name].Add(Preview(preview.Groups["mime"].Value, PyText.ParseInt(preview.Groups["width"].Value),
                                               PyText.ParseInt(preview.Groups["height"].Value), PyText.ParseInt(preview.Groups["bytes"].Value)));
                }
            }
            else if (line.StartsWith("Preview ", StringComparison.Ordinal))
            {
                throw new PreviewParityError($"a preview line does not parse: {Py.ReprString(line)}");
            }
        }
        return expected;
    }

    /// <summary>What the pinned tool printed for each file, read by the rules of the expectation text.</summary>
    /// <remarks>A file the tool printed nothing for expects no preview: whatever we list for it is extra.</remarks>
    public static OrderedDictionary<string, List<PyTuple>> OracleReference(IEnumerable<KeyValuePair<string, string>> outputs)
    {
        var blocks = outputs.Select(pair => $"-----> {pair.Key} <-----\nCommand: exiv2 -pp {pair.Key}\n{pair.Value}\n");
        return ParseReference(string.Concat(blocks));
    }

    private static List<long> CompactHexOffsets(byte[] source, int start, int end, byte[] encoded)
    {
        var digits = new List<byte>();
        var offsets = new List<int>();
        for (int at = start; at < end; at++)
        {
            byte value = source[at];
            if (PyText.IsSpace((char)value)) continue;
            if (!IsHexDigit(value))
            {
                digits.Clear();
                offsets.Clear();
                continue;
            }
            digits.Add(value);
            offsets.Add(at);
        }
        byte[] token = PyBytes.Upper(encoded);
        var found = PyBytes.FindAll(PyBytes.Upper(digits.ToArray()), token);
        return found.Select(at => (long)offsets[at]).ToList();
    }

    private static bool IsHexDigit(byte value) =>
        value is (>= (byte)'0' and <= (byte)'9') or (>= (byte)'a' and <= (byte)'f') or (>= (byte)'A' and <= (byte)'F');

    private static bool IsBase64Character(byte value) =>
        value is (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z') or (>= (byte)'0' and <= (byte)'9')
            or (byte)'+' or (byte)'/' or (byte)'=';

    private static List<long> CompactBase64Offsets(byte[] source, byte[] encoded)
    {
        byte[] token = Encoding.ASCII.GetBytes(Convert.ToBase64String(encoded));
        var compact = new List<byte>();
        var offsets = new List<int>();
        var found = new List<long>();
        for (int at = 0; at < source.Length; at++)
        {
            byte value = source[at];
            if (PyText.IsSpace((char)value)) continue;
            if (!IsBase64Character(value))
            {
                found.AddRange(PyBytes.FindAll(compact.ToArray(), token).Select(index => (long)offsets[index]));
                compact.Clear();
                offsets.Clear();
                continue;
            }
            compact.Add(value);
            offsets.Add(at);
        }
        found.AddRange(PyBytes.FindAll(compact.ToArray(), token).Select(index => (long)offsets[index]));
        return found;
    }

    private static readonly Dictionary<long, int> TiffWidths = new()
    {
        [1] = 1, [2] = 1, [3] = 2, [4] = 4, [5] = 8, [6] = 1, [7] = 1, [8] = 2, [9] = 4, [10] = 8, [11] = 4, [12] = 8, [13] = 4,
    };

    // struct format characters by TIFF type, for the values the first strip is read from.
    private static readonly Dictionary<long, char> TiffFormats = new()
    {
        [1] = 'B', [3] = 'H', [4] = 'I', [13] = 'I', [6] = 'B', [7] = 'B', [8] = 'h', [9] = 'i',
    };

    /// <summary>The first image strip of a standalone TIFF generated for Exiv2's reduced-image preview.</summary>
    public static byte[]? TiffFirstStrip(byte[] data)
    {
        if (data.Length < 8 || !(data.AsSpan(0, 2).SequenceEqual("II"u8) || data.AsSpan(0, 2).SequenceEqual("MM"u8))) return null;
        bool little = data.AsSpan(0, 2).SequenceEqual("II"u8);
        if (U16(data, 2, little) != 42) return null;
        long ifd = U32(data, 4, little);
        if (ifd > data.Length - 2) return null;
        long count = U16(data, (int)ifd, little);
        if (count > 4096 || ifd + 2 + count * 12 > data.Length) return null;
        var fields = new Dictionary<long, (long Kind, long Number, long Value)>();
        for (long index = 0; index < count; index++)
        {
            int at = (int)(ifd + 2 + index * 12);
            long tag = U16(data, at, little), kind = U16(data, at + 2, little), number = U32(data, at + 4, little), value = U32(data, at + 8, little);
            if (!TiffWidths.ContainsKey(kind)) continue;
            fields[tag] = (kind, number, value);
        }
        if (!fields.TryGetValue(273, out var offsets) || !fields.TryGetValue(279, out var counts) || offsets.Number == 0 || offsets.Number != counts.Number)
            return null;

        // Locate each field's inline value independently; external arrays use their TIFF offsets.
        List<long> FieldValues(long tag, (long Kind, long Number, long Pointer) field)
        {
            for (long index = 0; index < count; index++)
            {
                int entry = (int)(ifd + 2 + index * 12);
                if (U16(data, entry, little) != tag) continue;
                long length = TiffWidths[field.Kind] * field.Number;
                long start = length <= 4 ? entry + 8 : field.Pointer;
                if (!TiffFormats.TryGetValue(field.Kind, out char format) || start < 0 || start > data.Length - length) return [];
                var values = new List<long>((int)field.Number);
                int size = TiffWidths[field.Kind];
                for (long item = 0; item < field.Number; item++)
                {
                    int position = (int)(start + item * size);
                    values.Add(format switch
                    {
                        'B' => data[position],
                        'H' => U16(data, position, little),
                        'I' => U32(data, position, little),
                        'h' => little ? BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(position)) : BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(position)),
                        _ => little ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(position)) : BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(position)),
                    });
                }
                return values;
            }
            return [];
        }

        var stripOffsets = FieldValues(273, offsets);
        var stripCounts = FieldValues(279, counts);
        if (stripOffsets.Count == 0 || stripCounts.Count == 0 || stripOffsets[0] > data.Length - stripCounts[0]) return null;
        return PyBytes.Slice(data, stripOffsets[0], stripOffsets[0] + stripCounts[0]);
    }

    private static long U16(byte[] data, int at, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at)) : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at));

    private static long U32(byte[] data, int at, bool little) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at)) : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));

    /// <summary>Map a PNG Raw profile thumbnail back to its compressed zTXt profile payload.</summary>
    public static List<long> PngRawExifProfileOffsets(byte[] source, byte[] extracted)
    {
        byte[] signature = [0x89, (byte)'P', (byte)'N', (byte)'G', (byte)'\r', (byte)'\n', 0x1A, (byte)'\n'];
        var candidates = new List<long>();
        int search = 0;
        int png;
        while ((png = PyBytes.Find(source, signature, search)) >= 0)
        {
            search = png + signature.Length;
            long at = search;
            while (at + 12 <= source.Length)
            {
                long length = BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan((int)at));
                if (length > source.Length - at - 12) break;
                byte[] kind = source[(int)(at + 4)..(int)(at + 8)];
                byte[] payload = source[(int)(at + 8)..(int)(at + 8 + length)];
                if (kind.AsSpan().SequenceEqual("zTXt"u8))
                {
                    var (keyword, separator, rest) = PyBytes.Partition(payload, [0]);
                    if (keyword.AsSpan().SequenceEqual("Raw profile type exif"u8) && separator.Length > 0 && rest.Length > 0 && rest[0] == 0)
                    {
                        try
                        {
                            byte[] text = Zlib.Decompress(rest.AsSpan(1));
                            var lines = PyBytes.Split(PyBytes.LStrip(text, "\r\n"u8), "\n"u8, 2);
                            if (lines.Count == 3 && PyBytes.Lower(PyBytes.Strip(lines[0])).AsSpan().SequenceEqual("exif"u8))
                            {
                                if (!PyBytes.TryParseInt(PyBytes.Strip(lines[1]), out long expected)) throw new FormatException("invalid literal for int()");
                                byte[] digits = lines[2].Where(value => value is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')).ToArray();
                                byte[] decoded = PyBytes.FromHex(PyBytes.DecodeAscii(digits));
                                if (decoded.Length == expected && PyBytes.Contains(decoded, extracted)) candidates.Add(at + 8 + keyword.Length + 2);
                            }
                        }
                        catch (Exception error) when (error is FormatException or DecoderFallbackException or ZlibError)
                        {
                        }
                    }
                }
                at += 12 + length;
                if (kind.AsSpan().SequenceEqual("IEND"u8)) break;
            }
        }
        return candidates;
    }

    public static List<long> SourceOffsets(byte[] source, byte[] extracted, string mime, long width, long height)
    {
        var exact = PyBytes.FindAll(source, extracted).Select(at => (long)at).ToList();
        if (exact.Count > 0) return exact;
        if (mime == "image/tiff")
        {
            byte[]? firstStrip = TiffFirstStrip(extracted);
            if (firstStrip is { Length: > 0 }) return PyBytes.FindAll(source, firstStrip).Select(at => (long)at).ToList();
        }
        if (mime == "image/jpeg")
        {
            // The reference repairs a thumbnail whose JPEG header is damaged by forcing its first byte to
            // 0xFF before writing it out, so the bytes after that one, and only those, are the file's own.
            if (extracted.Length > 16 && extracted[0] == 0xFF)
            {
                var repaired = PyBytes.FindAll(source, extracted.AsSpan(1)).Where(begin => begin > 0).Select(begin => (long)begin - 1).ToList();
                if (repaired.Count > 0) return repaired;
            }
            // Photoshop EPS stores JPEG data as hexadecimal IRB data; XMP thumbnails use base64.
            int begin = PyBytes.Find(source, "%BeginPhotoshop:"u8);
            if (begin >= 0)
            {
                int line = PyBytes.Find(source, "\n"u8, begin);
                int finish = line >= 0 ? PyBytes.Find(source, "%EndPhotoshop"u8, line + 1) : -1;
                if (finish >= 0)
                {
                    var hexFound = CompactHexOffsets(source, line + 1, finish, Encoding.ASCII.GetBytes(PyText.Hex(extracted)));
                    if (hexFound.Count > 0) return [line + 1];
                }
            }
            var found = CompactBase64Offsets(source, extracted);
            if (found.Count > 0) return found;
            found = PngRawExifProfileOffsets(source, extracted);
            if (found.Count > 0) return found;
        }
        else if (mime == "image/x-portable-anymap" && extracted.AsSpan().StartsWith("P6\n"u8))
        {
            byte[] header = Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n");
            if (extracted.AsSpan().StartsWith(header))
            {
                int begin = PyBytes.Find(source, "%%BeginData:"u8);
                int line = begin >= 0 ? PyBytes.Find(source, "\n"u8, begin) : -1;
                int finish = line >= 0 ? PyBytes.Find(source, "%%EndData"u8, line + 1) : -1;
                if (finish >= 0)
                {
                    var found = CompactHexOffsets(source, line + 1, finish, Encoding.ASCII.GetBytes(PyText.Hex(extracted.AsSpan(header.Length))));
                    if (found.Count > 0) return [line + 1];
                }
            }
        }
        return [];
    }

    /// <summary>Infer file-relative source offsets from Exiv2's own -ep byte outputs.</summary>
    /// <remarks>
    /// The pinned -pp text has no offset column. Direct items are found byte-for-byte; XMP and EPS
    /// filters are reversed as base64/hex at their source payload. Ambiguous repeated bytes are kept as
    /// a set and the MetadataSharp offset must be one of those positions.
    /// </remarks>
    public static List<List<long>> OracleOffsets(IReadOnlyList<string> exiv2Command, string path, List<PyTuple> previews)
    {
        using var temporary = PyFile.TempDir("metadata-preview-");
        Completed finished;
        try
        {
            finished = PyProcess.Run(exiv2Command.Concat(["-f", "-l", temporary.Path, "-ep", PyPath.Resolve(path)]).ToList(), cwd: temporary.Path, timeout: 300L);
        }
        catch (PyOSError error) when (error.Errno == 2)
        {
            throw new PreviewParityError($"the pinned extraction command is not runnable: {error.Message}");
        }
        catch (PyTimeoutExpired)
        {
            throw new PreviewParityError($"the pinned extraction command timed out on {PyPath.Name(path)}");
        }
        if (finished.ReturnCode != 0)
        {
            string detail = PyText.Head(PyText.Strip((string)Py.Or(Py.Or(finished.Stderr, finished.Stdout), "")!).Replace("\n", " ", StringComparison.Ordinal), 400);
            throw new PreviewParityError($"pinned exiv2 -ep failed on {PyPath.Name(path)}: {detail}");
        }
        var outputs = new List<(long Number, string Path)>();
        foreach (string output in PyPath.IterDir(temporary.Path))
        {
            var match = OutputNumber.Match(PyPath.Name(output));
            if (match.Success && File.Exists(output)) outputs.Add((PyText.ParseInt(match.Groups[1].Value), output));
        }
        outputs = Py.Sorted(outputs, item => item.Number);
        if (!outputs.Select(item => item.Number).SequenceEqual(Enumerable.Range(1, previews.Count).Select(number => (long)number)))
            throw new PreviewParityError($"exiv2 -ep produced {outputs.Count} previews for {PyPath.Name(path)}, but -pp listed {previews.Count}");
        byte[] source = File.ReadAllBytes(path);
        var offsets = new List<List<long>>();
        for (int index = 0; index < previews.Count; index++)
        {
            int number = index + 1;
            var preview = previews[index];
            string mime = preview.S(0);
            long width = preview.L(1), height = preview.L(2);
            byte[] extracted = File.ReadAllBytes(outputs[index].Path);
            var candidates = SourceOffsets(source, extracted, mime, width, height);
            if (candidates.Count == 0)
            {
                string starts = PyText.Hex(extracted.AsSpan(0, Math.Min(4, extracted.Length)));
                throw new PreviewParityError(
                    $"cannot locate exiv2 preview bytes in {PyPath.Name(path)}; offset parity is unprovable "
                    + $"(preview {number} of {previews.Count}: {mime} {width}x{height}, {extracted.Length} bytes, "
                    + $"starts {(starts.Length > 0 ? starts : "empty")}, file {source.Length} bytes)");
            }
            offsets.Add(candidates);
        }
        return offsets;
    }

    // One FILE block of the dump: its previews, their offsets and sources, and its status.
    public sealed class DumpBlock
    {
        public List<PyTuple> Previews { get; } = [];

        public List<long> Offsets { get; } = [];

        public List<string> Sources { get; } = [];

        public string? Status { get; set; }

        public string Detail { get; set; } = "";
    }

    /// <summary>FILE / PREVIEW / STATUS records of MetadataSharp.Dump --previews, by path.</summary>
    public static Dictionary<string, DumpBlock> ParseDump(string text)
    {
        var blocks = new Dictionary<string, DumpBlock>(StringComparer.Ordinal);
        DumpBlock? current = null;
        int number = 0;
        foreach (string line in PyText.SplitLines(text))
        {
            number++;
            if (line.Length == 0) continue;
            string[] fields = line.Split('\t');
            if (fields[0] == "FILE" && fields.Length == 2)
            {
                if (current is not null && current.Status is null)
                    throw new PreviewParityError($"dump:{number}: FILE record before the previous one was closed");
                current = new DumpBlock();
                if (blocks.ContainsKey(fields[1])) throw new PreviewParityError($"dump:{number}: duplicate FILE record {Py.ReprString(fields[1])}");
                blocks[fields[1]] = current;
            }
            else if (fields[0] == "PREVIEW" && fields.Length == 7 && current is not null && current.Status is null)
            {
                var size = SizePattern.Match(fields[3]);
                if (!size.Success || !PyText.IsDigit(fields[4]) || !PyText.IsDigit(fields[5]))
                    throw new PreviewParityError($"dump:{number}: PREVIEW record does not parse: {Py.ReprString(line)}");
                current.Previews.Add(Preview(fields[2], PyText.ParseInt(size.Groups[1].Value), PyText.ParseInt(size.Groups[2].Value), PyText.ParseInt(fields[4])));
                current.Offsets.Add(PyText.ParseInt(fields[5]));
                current.Sources.Add(fields[6]);
            }
            else if (fields[0] == "STATUS" && fields.Length >= 2 && current is not null && current.Status is null)
            {
                current.Status = fields[1];
                current.Detail = fields.Length > 2 ? fields[2] : "";
            }
            else
            {
                throw new PreviewParityError($"dump:{number}: unexpected record: {Py.ReprString(line)}");
            }
        }
        if (current is not null && current.Status is null) throw new PreviewParityError("the dump ends inside a FILE block");
        return blocks;
    }

    // The outcome of comparing one file's previews.
    public sealed record FileOutcome(int Match, List<PyTuple> Missing, List<PyTuple> Extra, bool Ordered);

    /// <summary>Multiset comparison of one file's previews, and whether the common ones come in the same order.</summary>
    public static FileOutcome CompareFile(List<PyTuple> expected, List<PyTuple> listed)
    {
        var remaining = new Counter(listed);
        var matched = new List<PyTuple>();
        var missing = new List<PyTuple>();
        foreach (var preview in expected)
        {
            if (remaining.CountOf(preview) > 0)
            {
                remaining.Add(preview, -1);
                matched.Add(preview);
            }
            else
            {
                missing.Add(preview);
            }
        }
        var extra = new List<PyTuple>();
        foreach (var pair in remaining.Items())
            for (long i = 0; i < pair.Value; i++) extra.Add((PyTuple)pair.Key);
        var common = new Counter(matched);
        var ours = new List<PyTuple>();
        foreach (var preview in listed)
        {
            if (common.CountOf(preview) > 0)
            {
                common.Add(preview, -1);
                ours.Add(preview);
            }
        }
        return new FileOutcome(matched.Count, missing, extra, Py.Eq(ours.Cast<object?>().ToList(), matched.Cast<object?>().ToList()));
    }

    private static List<object?> AsList(PyTuple preview) => preview.ToList();

    public static PyDict Compare(OrderedDictionary<string, List<PyTuple>> expected, Dictionary<string, DumpBlock> dumped,
                                 IReadOnlyDictionary<string, string?> paths, IReadOnlyDictionary<string, List<List<long>>>? expectedOffsets = null)
    {
        var totals = new Counter();
        var statuses = new Counter();
        var formatTotals = new Dictionary<string, Counter>(StringComparer.Ordinal);
        var files = new List<object?>();
        foreach (string name in Py.Sorted(expected.Keys))
        {
            string? path = paths.TryGetValue(name, out string? found) ? found : null;
            DumpBlock? block = path is not null && dumped.TryGetValue(PyPath.Normalize(path), out var dumpedBlock) ? dumpedBlock : null;
            string status;
            List<PyTuple> listed;
            if (path is null)
            {
                (status, listed) = ("no-input", []);
            }
            else if (block is null)
            {
                throw new PreviewParityError($"the dump has no block for {name}");
            }
            else
            {
                (status, listed) = (block.Status!, block.Previews);
                if (status == "ok")
                {
                    long fileSize = new FileInfo(path).Length;
                    var offsets = block.Offsets;
                    var sources = block.Sources;
                    if (offsets.Count != listed.Count || sources.Count != listed.Count)
                        throw new PreviewParityError($"{name}: the dump has no offset/source for every listed preview");
                    for (int i = 0; i < listed.Count; i++)
                    {
                        var preview = listed[i];
                        long offset = offsets[i];
                        string source = sources[i];
                        bool generated = source is "EPS.AI7.Hex" or "EPS.Photoshop.Hex" || source.StartsWith("Xmp.Thumbnail", StringComparison.Ordinal);
                        if (offset < 0 || offset >= fileSize || preview.L(3) <= 0 || (!generated && offset + preview.L(3) > fileSize))
                        {
                            throw new PreviewParityError($"{name}: preview {i + 1} has an invalid file range: "
                                                         + $"offset={offset}, length={preview.L(3)}, file={fileSize}, source={source}");
                        }
                    }
                }
            }
            var outcome = CompareFile(expected[name], status == "ok" ? listed : []);
            var accepted = new List<object?>();
            var extra = outcome.Extra;
            if (ListedAndNotInReference.TryGetValue(name, out var known))
            {
                var kept = new List<PyTuple>();
                foreach (var preview in extra)
                {
                    if (Py.Eq(preview, known.Preview)) accepted.Add(PyDict.Of(("file", name), ("preview", AsList(preview)), ("reason", known.Reason)));
                    else kept.Add(preview);
                }
                extra = kept;
            }
            string suffix = PyPath.Suffix(name).TrimStart('.').ToUpperInvariant();
            string formatName = suffix.Length > 0 ? suffix : "UNKNOWN";
            if (!formatTotals.TryGetValue(formatName, out var formatCount)) formatTotals[formatName] = formatCount = new Counter();
            formatCount.Add("files", 1);
            formatCount.Add("found", expected[name].Count);
            formatCount.Add("matched", outcome.Match);
            formatCount.Add("missing", outcome.Missing.Count);
            formatCount.Add("extra", extra.Count);
            var offsetMismatch = new List<object?>();
            List<List<long>>? referenceOffsets = expectedOffsets is not null && expectedOffsets.TryGetValue(name, out var reference) ? reference : null;
            if (status == "ok" && referenceOffsets is not null)
            {
                if (referenceOffsets.Count != expected[name].Count)
                    throw new PreviewParityError($"{name}: the offset oracle count differs from the -pp list");
                var actualOffsets = block!.Offsets;
                if (actualOffsets.Count != listed.Count) throw new PreviewParityError($"{name}: the dump has no offset for every listed preview");
                var available = new Dictionary<PyTuple, List<int>>();
                for (int index = 0; index < listed.Count; index++)
                {
                    if (!available.TryGetValue(listed[index], out var indexes)) available[listed[index]] = indexes = [];
                    indexes.Add(index);
                }
                for (int index = 0; index < expected[name].Count; index++)
                {
                    var preview = expected[name][index];
                    if (!available.TryGetValue(preview, out var candidates) || candidates.Count == 0)
                        continue; // list parity records this as missing; compare offsets for common items only
                    int actualIndex = candidates[0];
                    candidates.RemoveAt(0);
                    long actualOffset = actualOffsets[actualIndex];
                    var allowed = referenceOffsets[index];
                    if (!allowed.Contains(actualOffset))
                        offsetMismatch.Add(PyDict.Of(("preview", AsList(preview)), ("actual", actualOffset), ("expected_any", allowed.Cast<object?>().ToList())));
                }
                totals.Add("offset_mismatch", offsetMismatch.Count);
            }
            statuses.Add(status, 1);
            totals.Add("expected", expected[name].Count);
            totals.Add("match", outcome.Match);
            totals.Add("missing", outcome.Missing.Count);
            totals.Add("extra", extra.Count);
            totals.Add("files", 1);
            totals.Add("files_equal", outcome.Missing.Count == 0 && extra.Count == 0 ? 1 : 0);
            totals.Add("files_ordered", outcome.Ordered ? 1 : 0);
            var entry = PyDict.Of(("file", name), ("status", status), ("match", (long)outcome.Match), ("ordered", outcome.Ordered),
                                  ("listed_offsets", block is not null ? block.Offsets.Cast<object?>().ToList() : new List<object?>()),
                                  ("listed_sources", block is not null ? block.Sources.Cast<object?>().ToList() : new List<object?>()));
            if (outcome.Missing.Count > 0)
            {
                entry["missing"] = outcome.Missing.Select(preview => (object?)AsList(preview)).ToList();
                if (referenceOffsets is not null)
                {
                    // Where the reference's own bytes lie: the number a listing that left the preview out needs.
                    entry["missing_at"] = expected[name]
                        .Select((preview, index) => (preview, index))
                        .Where(item => outcome.Missing.Any(missing => Py.Eq(missing, item.preview)))
                        .Select(item => (object?)PyDict.Of(("preview", AsList(item.preview)), ("offsets", referenceOffsets[item.index].Cast<object?>().ToList())))
                        .ToList();
                }
            }
            if (extra.Count > 0) entry["extra"] = extra.Select(preview => (object?)AsList(preview)).ToList();
            if (offsetMismatch.Count > 0) entry["offset_mismatch"] = offsetMismatch;
            if (accepted.Count > 0) entry["accepted"] = accepted;
            if (status == "error") entry["detail"] = block is not null ? block.Detail : "";
            files.Add(entry);
        }
        object? rate = totals.CountOf("expected") != 0 ? Py.Round((double)totals.CountOf("match") / totals.CountOf("expected"), 6) : null;
        var formats = new PyDict();
        foreach (string name in Py.Sorted(formatTotals.Keys)) formats[name] = formatTotals[name].Copy();
        return PyDict.Of(("kind", Kind), ("totals", totals.Copy()), ("rate", rate), ("statuses", statuses.Copy()),
                         ("formats", formats), ("files", files));
    }

    // The rows whose value is an address of a picture: the one a missing preview would be named by.
    private static readonly Regex AddressRow = new(@"(JPEGInterchangeFormat|PreviewImage|PreviewOffset|PreviewImageStart|Thumbnail"
                                                   + @"|ThumbnailOffset|StripOffsets|NewSubfileType|MakerNote.Offset|Diagnostic)[.A-Za-z]*$",
                                                   RegexOptions.CultureInvariant);

    /// <summary>The rows of a file that can address a picture, for a file whose preview list does not agree.</summary>
    /// <remarks>
    /// A listing that leaves a preview out says which picture is missing, not which structure the reader
    /// failed to follow: the rows are read once more, without the preview mode, and the ones whose key
    /// names an address are kept beside the difference they explain.
    /// </remarks>
    public static Dictionary<string, List<string>> DumpAddresses(IReadOnlyList<string> dumpCommand, IEnumerable<string?> paths)
    {
        string listing = string.Concat(paths.Select(path => $"{(path is null ? "None" : PyPath.Normalize(path))}\n"));
        Completed finished;
        try
        {
            finished = PyProcess.Run(dumpCommand.Concat(["--refusal-detail", "--files-from", "-"]).ToList(), input: listing, timeout: 1800L);
        }
        catch (Exception error) when (error is PyOSError { Errno: 2 } or PyTimeoutExpired)
        {
            return new Dictionary<string, List<string>>(StringComparer.Ordinal);
        }
        var addresses = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        string? current = null;
        foreach (string line in PyText.SplitLines(finished.Stdout))
        {
            string[] fields = line.Split('\t');
            if (fields[0] == "FILE" && fields.Length == 2)
            {
                current = fields[1];
                if (!addresses.ContainsKey(current)) addresses[current] = [];
            }
            else if (fields[0] == "STATUS" && fields.Length >= 3 && current is not null)
            {
                addresses[current].Add($"status: {fields[2]}");
            }
            else if (fields[0] == "TAG" && fields.Length >= 5 && current is not null && AddressRow.IsMatch(fields[1]))
            {
                // A small value is the address itself, and it is what a reader that lists nothing was given.
                string value = Py.Len(fields[4]) <= 24 ? $" = {fields[4]}" : "";
                addresses[current].Add($"{fields[1]}({fields[2]} {fields[3]}{value})");
            }
        }
        return addresses;
    }

    /// <summary>Fail closed for list mismatches, missing corpus input, or reader refusals.</summary>
    public static int GateExitCode(PyDict report)
    {
        var statuses = report.Dict("statuses");
        var totals = report.Dict("totals");
        return Py.Truthy(statuses.Get("error")) || Py.Truthy(totals.Get("missing")) || Py.Truthy(totals.Get("extra"))
               || Py.Truthy(totals.Get("offset_mismatch")) || Py.Truthy(statuses.Get("refused")) || Py.Truthy(statuses.Get("no-input"))
            ? 1
            : 0;
    }

    private static bool Differs(PyDict entry) =>
        entry.ContainsKey("missing") || entry.ContainsKey("extra") || entry.ContainsKey("offset_mismatch") || !(bool)entry["ordered"]!
        || entry.Str("status") != "ok";

    public static string TextReport(PyDict report)
    {
        var totals = report.Dict("totals");
        var lines = new List<string>
        {
            $"{Kind} parity against exiv2 -pp",
            $"  files: {Py.Str(totals.Get("files", 0L))} ({Py.Str(totals.Get("files_equal", 0L))} with the same previews, "
            + $"{Py.Str(totals.Get("files_ordered", 0L))} in the same order); statuses: {Py.Str(report["statuses"])}",
            $"  previews: {Py.Str(totals.Get("expected", 0L))} expected, {Py.Str(totals.Get("match", 0L))} match, "
            + $"{Py.Str(totals.Get("missing", 0L))} missing, {Py.Str(totals.Get("extra", 0L))} extra, "
            + $"{Py.Str(totals.Get("offset_mismatch", 0L))} offset mismatches; rate {Py.Str(report["rate"])}",
        };
        if (Py.Truthy(report.Get("formats")))
        {
            lines.Add("  format: Exiv2 found / MetadataSharp matched previews");
            foreach (var pair in report.Dict("formats"))
            {
                var counts = (PyDict)pair.Value!;
                lines.Add($"    {pair.Key}: {Py.Str(counts["found"])}/{Py.Str(counts["matched"])} "
                          + $"({Py.Str(counts["files"])} files; missing {Py.Str(counts["missing"])}, extra {Py.Str(counts["extra"])})");
            }
        }
        foreach (PyDict exception in ((List<object?>?)report.Get("exceptions") ?? []).Cast<PyDict>())
            lines.Add($"  oracle exception: {exception["file"]}: {exception["reason"]}");
        foreach (PyDict entry in report.List("files").Cast<PyDict>())
        {
            if (!Differs(entry)) continue;
            lines.Add($"  {entry["file"]} [{entry["status"]}] missing={Py.Str(entry.Get("missing", new List<object?>()))} "
                      + $"extra={Py.Str(entry.Get("extra", new List<object?>()))} offset_mismatch={Py.Str(entry.Get("offset_mismatch", new List<object?>()))} "
                      + $"ordered={Py.Str(entry["ordered"])}");
            if (Py.Truthy(entry.Get("addresses")))
                lines.Add($"    rows that address a picture: {string.Join(", ", ((List<object?>)entry["addresses"]!).Cast<string>())}");
        }
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>The part of the report that travels as annotations: the totals and every file that differs.</summary>
    public static PyDict Summary(PyDict report)
    {
        var differing = report.List("files").Cast<PyDict>().Where(Differs).Cast<object?>().ToList();
        return PyDict.Of(("kind", Kind), ("totals", report["totals"]), ("rate", report["rate"]), ("statuses", report["statuses"]),
                         ("formats", report.Get("formats", new PyDict())), ("exceptions", report.Get("exceptions", new List<object?>())),
                         ("differing", differing));
    }

    public static Dictionary<string, DumpBlock> RunDump(IReadOnlyList<string> dumpCommand, IEnumerable<string> paths)
    {
        string listing = string.Concat(paths.Select(path => $"{PyPath.Normalize(path)}\n"));
        Completed finished;
        try
        {
            finished = PyProcess.Run(dumpCommand.Concat(["--previews", "--files-from", "-"]).ToList(), input: listing, timeout: 1800L);
        }
        catch (PyOSError error) when (error.Errno == 2)
        {
            throw new PreviewParityError($"the dump command is not runnable: {error.Message}");
        }
        catch (PyTimeoutExpired)
        {
            throw new PreviewParityError("the dump tool timed out");
        }
        if (finished.ReturnCode != 0)
            throw new PreviewParityError($"the dump tool failed with {finished.ReturnCode}: {PyText.Head(PyText.Strip(finished.Stderr), 400)}");
        return ParseDump(finished.Stdout);
    }

    public static int Run(string[] argv)
    {
        var parser = new ArgParser("previews", "Compare our preview list with exiv2's own expectations.")
            .Add("--tree", help: "root of a pinned exiv2 checkout, for its preview expectation")
            .Add("--fixtures", ArgAction.Append, help: "a directory of files the pinned tool lists in this run instead; repeatable")
            .Add("--exiv2-command", help: "pinned exiv2 command (normally 'exiv2 -pp'); used as the list oracle for --fixtures and as -ep source-offset oracle when supplied")
            .Add("--dump-command", required: true, help: "how to run MetadataSharp.Dump, split POSIX-style on spaces")
            .Add("--out", @default: Repo.Path("artifacts/parity/preview"), help: "report directory")
            .Add("--annotate", ArgAction.StoreTrue, help: "emit the summary as chunked workflow annotations")
            .Add("--title", @default: Title, help: "annotation title");
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        string? tree = args.StrOrNull("tree");
        var fixtures = args.List("fixtures");
        if ((tree is null) == (fixtures.Count == 0)) return parser.Fail("pass either --tree or --fixtures");
        try
        {
            return Execute(args, tree, fixtures);
        }
        catch (Exception failure) when (failure is PreviewParityError or StructuralError or CorpusError)
        {
            Out.Error($"::error title={Title} harness::{failure.Message}");
            Out.Error($"structural failure: {failure.Message}");
            return 1;
        }
    }

    private static int Execute(ParsedArgs args, string? tree, List<string> fixtures)
    {
        var paths = new OrderedDictionary<string, string?>(StringComparer.Ordinal);
        var fixtureExceptions = new List<object?>();
        OrderedDictionary<string, List<PyTuple>> expected;
        string? exiv2Command = args.StrOrNull("exiv2_command");
        if (fixtures.Count > 0)
        {
            if (!Py.Truthy(exiv2Command)) throw new PreviewParityError("--fixtures needs --exiv2-command: the files have no stored expectation");
            var oracle = Shlex.Split(exiv2Command!);
            foreach (var candidate in Corpus.DiscoverGenerated(fixtures.ToArray()))
            {
                if (!PreviewFixtureSuffixes.Contains(PyPath.Suffix(candidate.Path).ToLowerInvariant()))
                    continue; // the fixture directories also hold non-image parser tests
                if (paths.ContainsKey(candidate.Label)) throw new PreviewParityError($"two files are named {candidate.Label}");
                string exceptionName = $"{PyPath.Name(PyPath.Parent(candidate.Path))}/{candidate.Label}";
                if (FixtureOracleExceptions.ContainsKey(exceptionName))
                {
                    fixtureExceptions.Add(PyDict.Of(("file", exceptionName), ("reason", FixtureOracleExceptions[exceptionName])));
                    continue;
                }
                paths[candidate.Label] = candidate.Path;
            }
            if (paths.Count == 0) throw new PreviewParityError("the fixture directories contain no supported preview images");
            expected = OracleReference(paths.Where(pair => pair.Value is not null)
                                            .Select(pair => new KeyValuePair<string, string>(pair.Key, ParitySupport.RunReference(oracle, pair.Value!, requireSuccess: true)))
                                            .ToList());
        }
        else
        {
            string reference = PyPath.Join(tree!, Reference);
            if (!File.Exists(reference)) throw new PreviewParityError($"no preview expectation at {reference}");
            expected = ParseReference(PyFile.ReadText(reference, replace: true));
            if (expected.Count == 0) throw new PreviewParityError($"{reference} names no file");
            string data = PyPath.Join(tree!, "test", "data");
            foreach (string name in expected.Keys) paths[name] = File.Exists(PyPath.Join(data, name)) ? PyPath.Join(data, name) : null;
        }
        var present = paths.Values.Where(path => path is not null).Cast<string>().ToList();
        if (present.Count == 0) throw new PreviewParityError("none of the files the expectation names is in the tree");
        var expectedOffsets = new Dictionary<string, List<List<long>>>(StringComparer.Ordinal);
        if (Py.Truthy(exiv2Command))
        {
            var extractionCommand = Shlex.Split(exiv2Command!).Where(part => part != "-pp").ToList();
            if (extractionCommand.Count == 0) throw new PreviewParityError("--exiv2-command must name the pinned exiv2 executable");
            string executable = extractionCommand[0];
            if (!PyPath.IsAbsolute(executable) && PyPath.Exists(executable)) extractionCommand[0] = PyPath.Resolve(executable);
            foreach (var pair in paths)
                if (pair.Value is not null) expectedOffsets[pair.Key] = OracleOffsets(extractionCommand, pair.Value, expected[pair.Key]);
        }
        var dumpCommand = Shlex.Split(args.Str("dump_command"));
        var report = Compare(expected, RunDump(dumpCommand, present), paths, Py.Truthy(exiv2Command) ? expectedOffsets : null);
        var exceptions = new List<object?>(fixtureExceptions);
        foreach (PyDict entry in report.List("files").Cast<PyDict>())
            if (entry.Get("accepted") is List<object?> accepted) exceptions.AddRange(accepted);
        report["exceptions"] = exceptions;
        // A file that differs gets its address rows beside the difference: what a reader can see, and what
        // it made of it, in one place.
        var differs = report.List("files").Cast<PyDict>().Where(entry => Py.Truthy(entry.Get("missing")) || Py.Truthy(entry.Get("extra"))).ToList();
        if (differs.Count > 0)
        {
            var addresses = DumpAddresses(dumpCommand, differs.Select(entry => paths[entry.Str("file")]));
            foreach (var entry in differs)
            {
                string? file = paths[entry.Str("file")];
                string key = file is null ? "None" : PyPath.Normalize(file);
                entry["addresses"] = (addresses.TryGetValue(key, out var rows) ? rows : []).Cast<object?>().ToList();
            }
        }

        string outDirectory = args.Str("out");
        Directory.CreateDirectory(outDirectory);
        PyFile.WriteText(PyPath.Join(outDirectory, "preview-report.json"), PyJson.Dumps(report, indent: 1, sortKeys: true) + "\n");
        string text = TextReport(report);
        PyFile.WriteText(PyPath.Join(outDirectory, "preview-report.txt"), text);
        Console.Out.Write(text);
        if (args.Flag("annotate")) ParitySupport.EmitAnnotations(Summary(report), args.Str("title"));
        return GateExitCode(report);
    }
}
