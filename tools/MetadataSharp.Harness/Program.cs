using System.Text;
using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness;

// The entry point: one command per gate, each the port of a former Python script with the same
// arguments, output, reports and exit status.
internal static class Program
{
    // Every command by name.
    internal static readonly SortedDictionary<string, Func<string[], int>> Commands = new(StringComparer.Ordinal)
    {
        ["annotate-chunks"] = Ci.Annotations.Chunks,
        ["annotate-tail"] = Ci.Annotations.Tail,
        ["charset-parity"] = Reference.CharsetParity.Run,
        ["crw-parity"] = Reference.CrwParity.Run,
        ["dng-census-fixtures"] = Reference.DngCensusFixtures.Run,
        ["dng-fixtures"] = Reference.DngFixtures.Run,
        ["doc-inventory-report"] = Ci.DocInventoryReport.Run,
        ["embedded-edits"] = Reference.EmbeddedEdits.Run,
        ["eps-pgf-fixtures"] = Reference.EpsPgfFixtures.Run,
        ["eps-pgf-write-parity"] = Reference.EpsPgfWriteParity.Run,
        ["external-reference"] = Reference.ExternalReference.Run,
        ["fuzz-run"] = Fuzz.FuzzRun.Run,
        ["fuzz-summary"] = Fuzz.FuzzSummary.Run,
        ["icc"] = Parity.Icc.Run,
        ["iptc-edits"] = Reference.IptcEdits.Run,
        ["jpeg-owned-fixtures"] = Reference.JpegOwnedFixtures.Run,
        ["key-catalog"] = Parity.KeyCatalog.Run,
        ["makernote-parity"] = Reference.MakerNoteParity.Run,
        ["makernote-render-gate"] = Parity.MakerNoteRenderGate.Run,
        ["operations-parity"] = Reference.OperationsParity.Run,
        ["package-audit"] = Ci.PackageAudit.Run,
        ["png-chunk-edits"] = Reference.PngChunkEdits.Run,
        ["png-owned-fixtures"] = Reference.PngOwnedFixtures.Run,
        ["previews"] = Parity.Previews.Run,
        ["psd-jp2-exv-write-parity"] = Reference.PsdJp2ExvWriteParity.Run,
        ["range-corpus-gate"] = Ci.RangeCorpusGate.Run,
        ["raw-container-fixtures"] = Reference.RawContainerFixtures.Run,
        ["raw-corpus"] = Parity.RawCorpus.Run,
        ["read-cost-table"] = Ci.ReadCostTable.Run,
        ["sweep"] = Parity.Sweep.Run,
        ["sync-parity"] = Reference.SyncParity.Run,
        ["tag-print"] = Parity.TagPrint.Run,
        ["tiff-based-edits"] = Reference.TiffBasedEdits.Run,
        ["tiff-owned-fixtures"] = Reference.TiffOwnedFixtures.Run,
        ["tiff-thumbnail-parity"] = Reference.TiffThumbnailParity.Run,
        ["trx-count"] = Ci.Trx.Count,
        ["trx-failures"] = Ci.Trx.Failures,
        ["video-fixtures"] = Reference.VideoFixtures.Run,
        ["video-gate"] = Parity.VideoGate.Run,
        ["webp-owned-fixtures"] = Reference.WebpOwnedFixtures.Run,
        ["xmp-edits"] = Reference.XmpEdits.Run,
        ["xmp-owned-fixtures"] = Reference.XmpOwnedFixtures.Run,
    };

    public static int Main(string[] args)
    {
        var utf8 = new UTF8Encoding(false);
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true });
        if (args.Length == 0 || !Commands.TryGetValue(args[0], out var command))
        {
            Console.Error.Write("usage: MetadataSharp.Harness COMMAND [ARGS...]\ncommands:\n" + string.Concat(Commands.Keys.Select(name => "  " + name + "\n")));
            return 2;
        }
        try
        {
            return command(args[1..]);
        }
        catch (SystemExit exit)
        {
            if (exit.Message.Length > 0) Out.Error(exit.Message);
            return exit.Code;
        }
        catch (Exception error)
        {
            // An unexpected failure ends the run as an uncaught Python exception did: a trace, then the
            // exception's type and message as the last line, and status 1.
            Out.Error(PyException.TraceOf(error));
            return 1;
        }
    }
}

// raise SystemExit(message) or sys.exit(code): ends the command with a status, printing the message to stderr.
internal sealed class SystemExit(int code, string message = "") : Exception(message)
{
    public int Code { get; } = code;

    /// <summary>raise SystemExit("text"): status 1 and the text on stderr.</summary>
    public static SystemExit WithMessage(string message) => new(1, message);
}
