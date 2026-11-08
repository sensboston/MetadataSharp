using System.Reflection;
using System.Runtime.Versioning;
using Xunit;

namespace MetadataSharp.Tests;

public sealed class ProjectInfrastructureTests
{
    [Theory]
    [InlineData("MetadataSharp.Core")]
    [InlineData("MetadataSharp.Formats")]
    public void ReferencedLibraryCanBeLoadedAndTargetsExecutingFramework(string assemblyName)
    {
        Assembly assembly = Assembly.Load(new AssemblyName(assemblyName));
        TargetFrameworkAttribute? target = assembly.GetCustomAttribute<TargetFrameworkAttribute>();

        Assert.Equal(assemblyName, assembly.GetName().Name);
        Assert.NotNull(target);
        Assert.Equal(GetType().Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName, target.FrameworkName);
    }

    [Fact]
    public void SourceProjectsDeclareNoPackageReferences()
    {
        string root = FindRepositoryRoot();
        string sourceRoot = Path.Combine(root, "src");
        string[] projects = Directory.GetFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsUnderBuildOutput(path))
            .ToArray();

        Assert.NotEmpty(projects);
        foreach (string project in projects)
        {
            string text = File.ReadAllText(project);
            Assert.DoesNotContain("<PackageReference", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool IsUnderBuildOutput(string path)
    {
        string[] segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("obj", StringComparer.OrdinalIgnoreCase) ||
               segments.Contains("bin", StringComparer.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MetadataSharp.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root (MetadataSharp.sln) was not found from the test output directory.");
    }
}
