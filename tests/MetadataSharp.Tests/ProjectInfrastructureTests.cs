using System.Reflection;
using System.Runtime.Versioning;
using Xunit;

namespace MetadataSharp.Tests;

public sealed class ProjectInfrastructureTests
{
    [Theory]
    [InlineData("MetadataSharp.Core")]
    [InlineData("MetadataSharp.Formats")]
    public void ReferencedLibraryCanBeLoadedAndTargetsNet8(string assemblyName)
    {
        Assembly assembly = Assembly.Load(new AssemblyName(assemblyName));
        TargetFrameworkAttribute? target = assembly.GetCustomAttribute<TargetFrameworkAttribute>();

        Assert.Equal(assemblyName, assembly.GetName().Name);
        Assert.NotNull(target);
        Assert.Equal(".NETCoreApp,Version=v8.0", target.FrameworkName);
    }
}
