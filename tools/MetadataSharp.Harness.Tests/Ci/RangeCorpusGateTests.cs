using MetadataSharp.Harness.Ci;
using MetadataSharp.Harness.Python;
using Xunit;
using static MetadataSharp.Harness.Tests.TestSupport;

namespace MetadataSharp.Harness.Tests.Ci;

// The corpus gate must fail closed on mismatches, wrong formats and regressions (formerly
// tests/parity/test_range_corpus_gate.py).
public sealed class RangeCorpusGateTests
{
    [Fact]
    public void CompleteCorpusAndFailures()
    {
        using var root = Temp();
        foreach (string name in RangeCorpusGate.Exiv2Samples.Values) File.WriteAllBytes(root[name], new byte[1_000_000]);
        string preview = Write(root["preview", "embedded.jpg"], "x");
        string reference = Write(root["test_reference_files", "expected.out"], "output");
        var calls = new List<(string Option, List<string> Paths)>();
        string mode = "ok";
        Dictionary<string, List<List<string>>> FakeRun(IReadOnlyList<string> command, string option, IReadOnlyList<string> paths)
        {
            calls.Add((option, paths.ToList()));
            var blocks = new Dictionary<string, List<List<string>>>(StringComparer.Ordinal);
            foreach (string path in paths)
            {
                if (option == "--equivalence")
                {
                    List<string> verdict = mode == "mismatch" && path == preview ? ["EQUIV", "mismatch"] : ["EQUIV", "ok"];
                    blocks[path] = [verdict, ["STATUS", "ok"]];
                }
                else
                {
                    string format = RangeCorpusGate.Exiv2Samples.First(pair => pair.Value == PyPath.Name(path)).Key;
                    string detected = mode == "wrong-format" ? "Unknown" : RangeCorpusGate.Expected[format];
                    string count = mode == "excess" && format == "HEIC" ? "1000000" : "4096";
                    string overlaps = mode == "overlap" && format == "PNG" ? "1" : "0";
                    blocks[path] = [["BYTES", detected, count, "1000000", overlaps], ["STATUS", "ok"]];
                }
            }
            return blocks;
        }
        var original = RangeCorpusGate.RunDump;
        RangeCorpusGate.RunDump = FakeRun;
        try
        {
            string[] argv = ["--kind", "exiv2", "--corpus", root.Path, "--dump-command", "fake", "--out", root["report.md"]];
            Assert.Equal(0, Capture(() => RangeCorpusGate.Run(argv)).Code);
            Assert.Contains(preview, calls[0].Paths);
            Assert.DoesNotContain(reference, calls[0].Paths);
            foreach (string failure in new[] { "mismatch", "wrong-format", "excess", "overlap" })
            {
                mode = failure;
                Assert.Equal((failure, 1), (failure, Capture(() => RangeCorpusGate.Run(argv)).Code));
            }
        }
        finally
        {
            RangeCorpusGate.RunDump = original;
        }
    }
}
