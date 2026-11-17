using System.Reflection;
using System.Runtime.Versioning;
using Xunit;

namespace MetadataSharp.Tests.Runtime;

public sealed class TargetRuntimeTests
{
    [Fact]
    public void TestsActuallyExecuteOnTheirDeclaredRuntime()
    {
#if NET10_0
        const int major = 10;
#else
        const int major = 8;
#endif
        Assert.Equal(major, Environment.Version.Major);
        string expected = $".NETCoreApp,Version=v{major}.0";
        Assert.Equal(expected, GetType().Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName);
        Assert.Equal(expected, typeof(MetadataSharp.Core.ExifValue).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName);
        Assert.Equal(expected, typeof(MetadataSharp.Formats.Tiff.TiffLayout).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName);
    }
}
