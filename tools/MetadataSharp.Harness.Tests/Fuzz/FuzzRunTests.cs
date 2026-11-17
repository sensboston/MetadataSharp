using System.Text.RegularExpressions;
using MetadataSharp.Harness.Fuzz;
using MetadataSharp.Harness.Python;
using Xunit;
using static MetadataSharp.Harness.Tests.TestSupport;

namespace MetadataSharp.Harness.Tests.Fuzz;

// A stand-in for the fuzz harness: the name of an input says what it does. It reads its inputs as the harness does
// (a list file after '@'), reports each one between BEGIN and END, and for an input named fail* reports a failure,
// for die* ends the process, for hang* never finishes.
internal static class FuzzStandIn
{
    public const string Command = "fuzz-stand-in";

    private static IEnumerable<string> Inputs(IEnumerable<string> arguments)
    {
        foreach (string argument in arguments)
        {
            if (argument.StartsWith('@'))
            {
                foreach (string line in File.ReadAllLines(argument[1..]).Select(line => line.Trim()).Where(line => line.Length > 0)) yield return line;
            }
            else
            {
                yield return argument;
            }
        }
    }

    /// <summary>The arguments after the command: the mode and target (--replay span), then the inputs.</summary>
    public static int Run(string[] args)
    {
        var output = System.Console.Out;
        foreach (string path in Inputs(args.Skip(2)))
        {
            output.Write("BEGIN " + path + "\n");
            output.Flush();
            string name = Path.GetFileName(path);
            if (name.StartsWith("fail", StringComparison.Ordinal))
            {
                output.Write("FAIL 2 " + path + "\n");
                output.Write("| System.InvalidOperationException: broken at 17\n");
                output.Write("|    at Some.Frame()\n");
            }
            else if (name.StartsWith("die", StringComparison.Ordinal))
            {
                output.Write("Stack overflow.\n");
                output.Flush();
                System.Environment.Exit(134);
            }
            else if (name.StartsWith("hang", StringComparison.Ordinal))
            {
                Thread.Sleep(30_000);
            }
            else
            {
                output.Write("PASS 1 " + path + "\n");
            }
            output.Write("END\n");
            output.Flush();
        }
        return 0;
    }
}

// Offline checks of the fuzz runner (formerly tests/parity/test_fuzz_run.py, of tools/MetadataSharp.Fuzz/run.py):
// seed choice, failure grouping and the replay runner.
public sealed class FuzzRunTests
{
    [Fact]
    public void TheSmallestAndTheMedianArePickedOnce()
    {
        Assert.Equal(["a", "b"], FuzzRun.Pick([(30, "c"), (10, "a"), (20, "b")]));
        Assert.Equal(["only"], FuzzRun.Pick([(5, "only")]));
    }

    [Fact]
    public void FailuresDifferingOnlyInNumbersAreOneFailure()
    {
        var first = PyDict.Of(("kind", "exception"), ("detail", "System.Exception: offset 12\n   at A.B()\n   at C.D()"));
        var second = PyDict.Of(("kind", "exception"), ("detail", "System.Exception: offset 99\n   at A.B()\n   at C.D()"));
        var other = PyDict.Of(("kind", "exception"), ("detail", "System.Exception: offset 12\n   at E.F()"));
        Assert.Equal(FuzzRun.Signature(first), FuzzRun.Signature(second));
        Assert.NotEqual(FuzzRun.Signature(first), FuzzRun.Signature(other));
    }

    [Fact]
    public void AnAflFileNameIsKeptUnderOneTheArtifactUploadAccepts()
    {
        string name = FuzzRun.Portable("id:000010,sig:02,src:000030,time:1479,execs:7820,op:havoc,rep:2");
        Assert.Equal("id_000010_sig_02_src_000030_time_1479_execs_7820_op_havoc_rep_2", name);
        Assert.DoesNotMatch(new Regex("[\\\\\":<>|*?\r\n]"), name);
    }

    [Fact]
    public void AnAflFileNameCarriesItsSignal()
    {
        Assert.Equal(2, FuzzRun.AflSignal("/findings/default/crashes/id:000000,sig:02,src:000075,time:157513,execs:356060,op:flip2,pos:145"));
        Assert.Equal(15, FuzzRun.AflSignal("/findings/default/crashes/id:000000,sig:15,src:000075,time:1,execs:2"));
        Assert.Equal(11, FuzzRun.AflSignal("/findings/default/crashes/id:000000,sig:11,src:000075,time:1,execs:2"));
        Assert.Equal(2, FuzzRun.AflSignal("id:000000,sig:02"));
        Assert.Null(FuzzRun.AflSignal("/findings/default/hangs/id:000000,src:000075,time:1,execs:2"));
        Assert.Null(FuzzRun.AflSignal("/tmp/no-signal"));
    }

    [Fact]
    public void ANotReproducedCrashThatDiedByAnAsynchronousSignalIsNotAFinding()
    {
        var notReproduced = PyDict.Of(("kind", "crash-not-reproduced"), ("path", "/findings/default/crashes/id:000000,sig:02,src:000075"),
            ("detail", "afl-fuzz saved this crash; replayed alone it passes in 5 ms."));
        var reproduced = PyDict.Of(("kind", "crash"), ("path", "/findings/default/crashes/id:000000,sig:02,src:000075"), ("detail", "Boom"));
        var otherSignal = PyDict.Of(("kind", "crash-not-reproduced"), ("path", "/findings/default/crashes/id:000000,sig:11,src:000075"),
            ("detail", "afl-fuzz saved this crash; replayed alone it passes in 5 ms."));
        var hang = PyDict.Of(("kind", "hang-not-reproduced"), ("path", "/findings/default/hangs/id:000000,sig:02"), ("detail", "x"));
        Assert.True(FuzzRun.IsNotAFinding(notReproduced, "/findings/default/crashes/id:000000,sig:02,src:000075"));
        Assert.True(FuzzRun.IsNotAFinding(PyDict.Of(("kind", "crash-not-reproduced"), ("path", "/findings/default/crashes/id:000000,sig:15,src:000075")),
            "/findings/default/crashes/id:000000,sig:15,src:000075"));
        Assert.False(FuzzRun.IsNotAFinding(reproduced, "/findings/default/crashes/id:000000,sig:02,src:000075"));
        Assert.False(FuzzRun.IsNotAFinding(otherSignal, "/findings/default/crashes/id:000000,sig:11,src:000075"));
        Assert.False(FuzzRun.IsNotAFinding(hang, "/findings/default/hangs/id:000000,sig:02"));
    }

    [Fact]
    public void AnAnnotationEscapesWhatTheWorkflowCommandReserves()
    {
        var (_, stdout, _) = Capture(() =>
        {
            FuzzRun.Annotate("error", "Fuzz span: a, b", "100%\nnext");
            return 0;
        });
        Assert.Equal("::error title=Fuzz span%3A a%2C b::100%25%0Anext\n", stdout);
    }

    /// <summary>The dotnet the tests run under, to start this assembly as the stand-in.</summary>
    private static string DotNet() => System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet";

    [Fact]
    public void TheReplayReportsEachFailureAndRestartsAfterADeathOrAHang()
    {
        using var folder = Temp();
        string[] names = ["ok-1", "fail-1", "die-1", "ok-2", "hang-1", "ok-3"];
        var paths = new List<string>();
        foreach (string name in names)
        {
            string path = folder[name];
            File.WriteAllBytes(path, "x"u8.ToArray());
            paths.Add(path);
        }
        double saved = FuzzRun.BaseBudget;
        FuzzRun.BaseBudget = 2.0;
        Replay replay;
        try
        {
            using var log = FuzzRun.OpenLog(folder["replay.log"]);
            replay = new Replay([DotNet(), typeof(FuzzStandIn).Assembly.Location, FuzzStandIn.Command, "--replay", "span"], null, log);
            replay.Run(paths, folder["replay.list"]);
        }
        finally
        {
            FuzzRun.BaseBudget = saved;
        }
        var status = replay.Results.ToDictionary(pair => PyPath.Name(pair.Key), pair => pair.Value.Status);
        Assert.Equal(new Dictionary<string, string> { ["ok-1"] = "pass", ["fail-1"] = "fail", ["die-1"] = "died", ["ok-2"] = "pass", ["hang-1"] = "hang", ["ok-3"] = "pass" }, status);
        var kinds = replay.Failures.Cast<PyDict>().ToDictionary(failure => PyPath.Name((string)failure["path"]!), failure => (string)failure["kind"]!);
        Assert.Equal(new Dictionary<string, string> { ["fail-1"] = "exception", ["die-1"] = "died", ["hang-1"] = "hang" }, kinds);
        var exception = replay.Failures.Cast<PyDict>().First(failure => Py.Eq(failure["kind"], "exception"));
        Assert.Contains("InvalidOperationException", (string)exception["detail"]!);
        var died = replay.Failures.Cast<PyDict>().First(failure => Py.Eq(failure["kind"], "died"));
        Assert.Contains("Stack overflow", (string)died["detail"]!);
    }
}
