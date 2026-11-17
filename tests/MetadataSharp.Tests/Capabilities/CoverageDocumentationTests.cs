using MetadataSharp.Core;
using Xunit;

namespace MetadataSharp.Tests.Capabilities;

/// <summary>The published capability matrix is an executable representation of every registry cell.</summary>
public sealed class CoverageDocumentationTests
{
    [Fact]
    public void Coverage_table_matches_every_container_family_and_operation()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MetadataSharp.sln"))) root = root.Parent;
        Assert.NotNull(root);
        string path = Path.Combine(root!.FullName, "docs", "usage", "FORMAT_AND_OPERATION_COVERAGE.md");
        string text = File.ReadAllText(path);
        const string start = "<!-- capability-matrix:start -->";
        const string end = "<!-- capability-matrix:end -->";
        Assert.Equal(1, text.Split(start, StringSplitOptions.None).Length - 1);
        Assert.Equal(1, text.Split(end, StringSplitOptions.None).Length - 1);
        int first = text.IndexOf(start, StringComparison.Ordinal) + start.Length;
        int last = text.IndexOf(end, first, StringComparison.Ordinal);
        Assert.True(last > first);
        string[] lines = text[first..last].Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string[][] cells = lines.Select(line => line.Split('|', StringSplitOptions.TrimEntries)[1..^1]).ToArray();
        MetadataFamily[] families = Enum.GetValues<MetadataFamily>();
        MetadataOperation[] operations = Enum.GetValues<MetadataOperation>();
        MetadataContainer[] containers = Enum.GetValues<MetadataContainer>();
        Assert.Equal(new[] { "Container" }.Concat(families.Select(f => f.ToString())), cells[0]);
        Assert.Equal(5, operations.Length);
        Assert.Equal(containers.Length + 2, cells.Length);
        for (int i = 0; i < containers.Length; i++)
        {
            string[] row = cells[i + 2];
            Assert.Equal(families.Length + 1, row.Length);
            Assert.Equal(containers[i].ToString(), row[0]);
            for (int f = 0; f < families.Length; f++)
            {
                string expected = new(operations.Select(operation => MetadataCapabilities.GetStatus(containers[i], families[f], operation) switch
                {
                    CapabilityStatus.Supported => 'S',
                    CapabilityStatus.Partial => 'P',
                    CapabilityStatus.NotSupported => '-',
                    _ => throw new InvalidOperationException("Unknown capability status")
                }).ToArray());
                Assert.True(row[f + 1] == expected,
                    $"{containers[i]}/{families[f]}: documented {row[f + 1]}, registry {expected}");
            }
        }
    }
}
