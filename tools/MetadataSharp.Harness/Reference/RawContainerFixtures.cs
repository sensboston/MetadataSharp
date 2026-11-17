using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Reference;

// MRW and RAF files authored from files of the pinned corpus, and its CRW files, for key parity. Formerly
// tools/reference/raw_container_fixtures.py.
//
//     raw-container-fixtures CORPUS OUT
//
// The pinned corpus has no MRW file with an expectation and only two RAF files of 120 bytes. This writes, into OUT:
//
//   * NAME.mrw for TIFF files of CORPUS: the header block of an MRW file, a PRD block, and a TTW block whose content
//     is the TIFF file, whole;
//   * NAME.raf for JPEG files of CORPUS: the header of a RAF file, the JPEG file, whole, and after it an authored
//     TIFF structure of one tag (DocumentName);
//   * a copy of every CRW file of CORPUS.
//
// Nothing here is derived from a camera file of these formats: the containers are authored, and what they hold are
// files of the corpus as they are. The names are kept short; the output maps them.
internal static class RawContainerFixtures
{
    public const long Limit = 300 * 1024;
    public const int Count = 12;
    public static readonly string[] Defects = ["poc", "issue", "bug", "ghsa", "cve", "fuzz", "crash"];

    public static byte[] Mrw(byte[] tiff)
    {
        byte[] prd = [.. "\0PRD"u8, .. PyStruct.Pack(">I", 24), .. "27730001"u8, .. new byte[16]];
        byte[] ttw = [.. "\0TTW"u8, .. PyStruct.Pack(">I", tiff.Length), .. tiff];
        return [.. "\0MRM"u8, .. PyStruct.Pack(">I", prd.Length + ttw.Length), .. prd, .. ttw, .. new byte[64]];
    }

    public static byte[] Raf(byte[] jpeg, byte[] tiff)
    {
        var header = new byte[108];
        "FUJIFILMCCD-RAW "u8.CopyTo(header);
        "0201"u8.CopyTo(header.AsSpan(16));
        "FF389501"u8.CopyTo(header.AsSpan(20));
        "X-T3"u8.CopyTo(header.AsSpan(28)); // ljust(32, b"\0") over bytes that are already zero
        PyStruct.PackInto(">II", header, 84, 108, jpeg.Length);
        PyStruct.PackInto(">II", header, 100, 108 + jpeg.Length, tiff.Length);
        return [.. header, .. jpeg, .. tiff];
    }

    /// <summary>A TIFF structure of one directory with one tag, DocumentName.</summary>
    public static byte[] TiffStructure()
    {
        byte[] text = "raf structure\0"u8.ToArray();
        return [.. "II*\0"u8, .. PyStruct.Pack("<IH", 8, 1), .. PyStruct.Pack("<HHII", 0x010D, 2, text.Length, 26), .. PyStruct.Pack("<I", 0), .. text];
    }

    public static bool IsTiff(byte[] data) => data.AsSpan(0, Math.Min(4, data.Length)) is var head && (head.SequenceEqual("II*\0"u8) || head.SequenceEqual("MM\0*"u8));

    public static int Run(string[] argv)
    {
        // main() read sys.argv[1] and [2]: a missing argument ended in an IndexError.
        if (argv.Length < 2) throw new PyException("IndexError", "list index out of range");
        string corpus = argv[0], output = argv[1];
        PyFile.MakeDirs(output);
        var names = new List<string>();
        var small = PyPath.IterDir(corpus).Where(path => File.Exists(path) && new FileInfo(path).Length <= Limit).ToList();
        small.Sort(PyPath.Comparer);
        // Files written to show a defect of a reader come last: the tool reads few of them.
        small = small.Select((path, index) => (path, index))
            .OrderBy(pair => Defects.Any(word => PyPath.Name(pair.path).ToLowerInvariant().Contains(word, StringComparison.Ordinal)))
            .ThenBy(pair => PyPath.Name(pair.path), Comparer<string>.Create(Py.CompareStrings))
            .ThenBy(pair => pair.index)
            .Select(pair => pair.path).ToList();
        var tiffs = small.Where(path => PyPath.Suffix(path).ToLowerInvariant() is ".tif" or ".tiff" && IsTiff(PyFile.ReadBytes(path))).Take(Count).ToList();
        for (int number = 0; number < tiffs.Count; number++)
        {
            PyFile.WriteBytes(PyPath.Join(output, $"m{number:00}.mrw"), Mrw(PyFile.ReadBytes(tiffs[number])));
            names.Add($"m{number:00}.mrw\t{PyPath.Name(tiffs[number])}");
        }

        byte[] structure = TiffStructure();
        var jpegs = small.Where(path => PyPath.Suffix(path).ToLowerInvariant() == ".jpg" && PyPath.Name(path).StartsWith("exiv2-", StringComparison.Ordinal)
                                        && PyFile.ReadBytes(path).AsSpan() is var data && data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8).Take(Count).ToList();
        for (int number = 0; number < jpegs.Count; number++)
        {
            PyFile.WriteBytes(PyPath.Join(output, $"r{number:00}.raf"), Raf(PyFile.ReadBytes(jpegs[number]), structure));
            names.Add($"r{number:00}.raf\t{PyPath.Name(jpegs[number])}");
        }

        var crws = PyPath.Glob(corpus, "*.crw");
        for (int number = 0; number < crws.Count; number++)
        {
            PyFile.WriteBytes(PyPath.Join(output, $"c{number:00}.crw"), PyFile.ReadBytes(crws[number]));
            names.Add($"c{number:00}.crw\t{PyPath.Name(crws[number])}");
        }

        Out.Print(string.Join("\n", names));
        Out.Print($"{tiffs.Count} MRW, {jpegs.Count} RAF and {names.Count - tiffs.Count - jpegs.Count} CRW files");
        return tiffs.Count > 0 && jpegs.Count > 0 ? 0 : 1;
    }
}
