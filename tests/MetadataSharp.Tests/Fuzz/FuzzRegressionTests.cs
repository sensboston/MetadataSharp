using System.Text.RegularExpressions;
using MetadataSharp.Fuzz;
using Xunit;

namespace MetadataSharp.Tests.Fuzz;

/// <summary>
/// The fuzz targets of tools/MetadataSharp.Fuzz are compiled into this project (FuzzTargets.cs is
/// linked, not copied), so the ordinary test run holds every file of tests/fixtures to the contract
/// the fuzzer holds mutated files to: the minimized failures the campaigns found, under
/// tests/fixtures/fuzz/TARGET, which keeps each fixed bug fixed on every platform and framework, and
/// the well-formed fixtures, which keeps the invariants of the harness honest. A target either
/// succeeds or is refused with a <see cref="Core.MetadataException"/> that has a defined code;
/// anything else that escapes it is a failure.
/// </summary>
public sealed class FuzzRegressionTests
{
    /// <summary>The targets the harness defines.</summary>
    /// <returns>One row a target.</returns>
    public static TheoryData<string> Targets()
    {
        var data = new TheoryData<string>();
        foreach (string target in FuzzTargets.Names) data.Add(target);
        return data;
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public void Every_fixture_keeps_the_contract_of_the_target(string target)
    {
        string root = RepositoryRoot();
        var failures = new List<string>();
        int files = 0;
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "tests", "fixtures"), "*", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            files++;
            try { FuzzTargets.Run(target, File.ReadAllBytes(path)); }
            catch (Exception exception)
            {
                failures.Add(Path.GetRelativePath(root, path) + ": " + exception.GetType().Name + ": " + exception.Message);
            }
        }
        Assert.True(files > 100, "The fixtures were not found.");
        Assert.True(failures.Count == 0, target + " failed on:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void Every_minimized_failure_is_filed_under_the_target_that_found_it()
    {
        string fuzz = Path.Combine(RepositoryRoot(), "tests", "fixtures", "fuzz");
        if (!Directory.Exists(fuzz)) return;
        foreach (string directory in Directory.EnumerateDirectories(fuzz))
            Assert.Contains(Path.GetFileName(directory), FuzzTargets.Names);
        Assert.DoesNotContain(Directory.EnumerateFiles(fuzz), path => !path.EndsWith(".md", StringComparison.Ordinal));
    }

    [Fact]
    public void The_minimized_failures_are_checked_out_byte_for_byte()
    {
        // The Windows runners check text out with CRLF line ends: a minimized failure git took for
        // text would reach the test with other bytes than the ones that failed.
        string attributes = File.ReadAllText(Path.Combine(RepositoryRoot(), ".gitattributes"));
        Assert.Matches(new Regex(@"^tests/fixtures/fuzz/\*/\* -text\s*$", RegexOptions.Multiline), attributes);
    }

    [Fact]
    public void The_fuzz_workflow_runs_every_target()
    {
        string workflow = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "fuzz.yml"));
        Match matrix = Regex.Match(workflow, @"^\s*target:\s*\[(?<names>[^\]]*)\]", RegexOptions.Multiline);
        Assert.True(matrix.Success, "fuzz.yml has no target matrix.");
        string[] listed = matrix.Groups["names"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(FuzzTargets.Names, listed);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MetadataSharp.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
