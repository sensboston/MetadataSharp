using System.Security.Cryptography;
using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Reference;

// A check of the PSD, JP2 and EXV write parity that failed; reported as an annotation.
internal sealed class ParityError(string message) : PyException("ParityError", message);

// Bidirectional common-metadata write parity for PSD, JP2 and EXV against pinned Exiv2. Formerly
// tools/reference/psd_jp2_exv_write_parity.py.
//
// PSD's documented Exiv2 capability deliberately excludes ICC; the shared PSD gate therefore compares EXIF/IPTC/XMP
// only. JP2 additionally compares exact ICC bytes in both write directions. The reverse EXV ICC insertion is checked by
// reading its exact bytes back through MetadataSharp; Exiv2's binary ICC print path does not expose an ICC from the
// authored EXV representation. PSD ICC is outside the pinned Exiv2 format support. This is a narrow interoperability
// gate, not whole-format equivalence.
//
// Usage: psd-jp2-exv-write-parity DIRECTORY --exiv2 COMMAND --dump-command COMMAND
internal static class PsdJp2ExvWriteParity
{
    private const string Description = """
        Bidirectional common-metadata write parity for PSD, JP2 and EXV against pinned Exiv2.

        PSD's documented Exiv2 capability deliberately excludes ICC; the shared PSD gate therefore
        compares EXIF/IPTC/XMP only. JP2 additionally compares exact ICC bytes in both write directions.
        The reverse EXV ICC insertion is checked by reading its exact bytes back through MetadataSharp;
        Exiv2's binary ICC print path does not expose an ICC from the authored EXV representation. PSD ICC
        is outside the pinned Exiv2 format support. This is a narrow interoperability gate, not whole-format
        equivalence.
        """;

    public static readonly (string Key, string Value)[] OurExpected =
    [
        ("Exif.Image.Make", "TestCompany"),
        ("Iptc.Application2.Headline", "MSParity"),
        ("Xmp.xmp.CreatorTool", "MSParity"),
    ];

    public static readonly (string Key, string Value)[] Exiv2Expected =
    [
        ("Exif.Image.Make", "Exiv2Make"),
        ("Iptc.Application2.Headline", "Exiv2Headline"),
        ("Xmp.xmp.CreatorTool", "Exiv2Creator"),
    ];

    public static readonly (string Name, string Extension, bool ReadsIcc, bool WritesIcc)[] Formats =
    [
        ("psd", ".psd", false, false), // The pinned Exiv2 matrix excludes PSD ICC.
        ("jp2", ".jp2", true, true),
        ("exv", ".exv", false, true), // Exiv2 writes ICC, but its -pC path does not expose authored EXV ICC.
    ];

    /// <summary>The stdout of a command; a non-zero exit raises ParityError with both outputs.</summary>
    public static byte[] RunBytes(IReadOnlyList<string> argv)
    {
        var result = PyProcess.Run(argv, text: false);
        if (result.ReturnCode != 0)
        {
            byte[] output = result.StdoutBytes, errors = result.StderrBytes;
            throw new ParityError($"command failed ({result.ReturnCode}): {Shlex.Join(argv)}\n"
                                  + $"stdout: {Py.ReprBytes(output.AsSpan(0, Math.Min(1000, output.Length)))}\n"
                                  + $"stderr: {Py.ReprBytes(errors.AsSpan(Math.Max(0, errors.Length - 3000)))}");
        }
        return result.StdoutBytes;
    }

    public static void CheckExiv2Read(List<string> exiv2, string image, bool icc, byte[]? profile)
    {
        string printed = PyText.Utf8Replace(RunBytes([.. exiv2, "-Pkycvt", image]));
        var lines = PyText.SplitLines(printed);
        foreach (var (key, value) in OurExpected)
        {
            if (!lines.Any(line => line.Contains(key, StringComparison.Ordinal) && line.Contains(value, StringComparison.Ordinal)))
                throw new ParityError($"Exiv2 did not report the expected {key}={value} in {PyPath.Name(image)}:\n{printed}");
        }
        if (icc)
        {
            byte[] actual = RunBytes([.. exiv2, "-pC", image]);
            if (profile is null || !actual.AsSpan().SequenceEqual(profile))
                throw new ParityError($"Exiv2 ICC bytes differ for {PyPath.Name(image)}: got {actual.Length} bytes, expected {(profile is null ? 0 : profile.Length)}");
        }
    }

    public static void CheckDump(List<string> dump, string image)
    {
        string printed = PyText.Utf8Replace(RunBytes([.. dump, image]));
        var lines = PyText.SplitLines(printed);
        foreach (var (key, value) in Exiv2Expected)
        {
            if (!lines.Any(line => line.Contains($"\t{key}\t", StringComparison.Ordinal) && line.Contains(value, StringComparison.Ordinal)))
                throw new ParityError($"MetadataSharp did not report the expected {key}={value} in {PyPath.Name(image)}:\n{printed}");
        }
    }

    public static void CheckDumpIcc(List<string> dump, string image, byte[] profile)
    {
        string records = PyText.Utf8Replace(RunBytes([.. dump, "--icc", image]));
        string expected = $"ICC\t{profile.Length}\t{PyText.Hex(MD5.HashData(profile))}";
        if (!records.Contains(expected, StringComparison.Ordinal))
            throw new ParityError($"MetadataSharp ICC result differs for {PyPath.Name(image)}; wanted {Py.ReprString(expected)}, got {Py.ReprString(records)}");
    }

    public static int Run(string[] argv)
    {
        try
        {
            return Execute(argv);
        }
        catch (ParityError error)
        {
            string message = error.Message.Replace("%", "%25", StringComparison.Ordinal).Replace("\r", "%0D", StringComparison.Ordinal).Replace("\n", "%0A", StringComparison.Ordinal);
            Out.Error($"write parity failed: {error.Message}");
            Out.Error($"::error title=PSD JP2 EXV write parity::{message}");
            return 1;
        }
    }

    public static int Execute(IReadOnlyList<string> argv)
    {
        var parser = new ArgParser("psd-jp2-exv-write-parity", Description.ReplaceLineEndings("\n"))
            .Add("directory", help: "files generated by ReferenceFixture")
            .Add("--exiv2", required: true, help: "pinned Exiv2 executable")
            .Add("--dump-command", required: true, help: "MetadataSharp.Dump command and arguments");
        if (!parser.TryParse(argv, out var args, out int exitCode)) return exitCode;
        string root = PyPath.Resolve(args.Str("directory"));
        var exiv2 = Shlex.Split(args.Str("exiv2"));
        var dump = Shlex.Split(args.Str("dump_command"));
        if (exiv2.Count == 0 || dump.Count == 0) throw new ParityError("both tool commands must be nonempty");
        byte[] profile = PyFile.ReadBytes(PyPath.Join(root, "profile.icc"));
        int passed = 0;

        // MetadataSharp-authored outputs must be consumable by Exiv2. The binary ICC assertion is kept to JP2; PSD and
        // EXV ICC are independently pinned by MetadataSharp's format-specific tests.
        foreach (var (name, extension, readsIcc, _) in Formats)
        {
            CheckExiv2Read(exiv2, PyPath.Join(root, $"ours-{name}{extension}"), readsIcc, readsIcc ? profile : null);
            passed++;
        }

        // Exiv2-authored outputs must be consumable by MetadataSharp. Keep metadata edits and the ICC insert as separate
        // targets: the former tests metadata read-back, the latter tests ICC bytes. Exiv2's profile option reads a
        // sidecar named for the target stem; PSD has no ICC operation in the pinned format matrix.
        using (var temporary = PyFile.TempDir("metadatasharp-exiv2-write-"))
        {
            string work = temporary.Path;
            foreach (var (name, extension, _, writesIcc) in Formats)
            {
                string target = PyPath.Join(work, $"theirs-{name}{extension}");
                PyShutil.CopyFile(PyPath.Join(root, $"base-{name}{extension}"), target);
                RunBytes([.. exiv2, "-m", PyPath.Join(root, "modify.txt"), "mo", target]);
                CheckDump(dump, target);
                passed++;
                if (writesIcc)
                {
                    string iccTarget = PyPath.Join(work, $"theirs-{name}-icc{extension}");
                    PyShutil.CopyFile(PyPath.Join(root, $"base-{name}{extension}"), iccTarget);
                    PyFile.WriteBytes(PyPath.Join(work, $"{PyPath.Stem(iccTarget)}.icc"), profile);
                    RunBytes([.. exiv2, "-iC", "-l", work, "in", iccTarget]);
                    CheckDumpIcc(dump, iccTarget, profile);
                    passed++;
                }
            }
        }
        Out.Print($"PSD/JP2/EXV bidirectional write parity: {passed} checks passed; "
                  + "EXIF/IPTC/XMP all formats, exact ICC both ways JP2 and Exiv2-to-MetadataSharp for EXV.");
        return 0;
    }
}
