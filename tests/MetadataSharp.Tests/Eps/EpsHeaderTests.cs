using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Formats.Eps;
using Xunit;

namespace MetadataSharp.Tests.Eps;

public sealed class EpsHeaderTests
{
    private static byte[] Fixture()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MetadataSharp.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllBytes(Path.Combine(dir!.FullName, "tests", "fixtures", "b8", "owned-vector.eps"));
    }
    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);
    private static MetadataErrorCode Error(string text) => Assert.Throws<MetadataException>(() => EpsHeader.Read(Ascii(text))).Code;
    private const string Header = "%!PS-Adobe-3.0 EPSF-3.0\n";

    [Fact]
    public void OwnedEpsHeaderHasDeclaredCoordinates()
    {
        EpsHeader header = EpsHeader.Read(Fixture());
        Assert.Equal(0, header.Left); Assert.Equal(0, header.Bottom);
        Assert.Equal(10, header.Right); Assert.Equal(12, header.Top);
    }

    [Fact]
    public void SupportsNegativeCoordinatesAndMixedNewlines()
    {
        EpsHeader header = EpsHeader.Read(Ascii("%!PS-Adobe-3.0 EPSF-3.0\r\n%%BoundingBox: -12 -5 +10 40\r%%EndComments\n"));
        Assert.Equal(-12, header.Left); Assert.Equal(-5, header.Bottom);
        Assert.Equal(10, header.Right); Assert.Equal(40, header.Top);
    }

    [Fact]
    public void DoesNotExamineOrExecuteBody()
    {
        byte[] header = Ascii(Header + "%%BoundingBox: 0 0 1 1\n%%EndComments\n");
        EpsHeader result = EpsHeader.Read([.. header, 0, 255, 0, 13]);
        Assert.Equal(1, result.Right);
    }

    [Fact]
    public void RefusesMissingDuplicateInvalidAndDeferredBoundingBoxes()
    {
        Assert.Equal(MetadataErrorCode.InvalidEps, Error(Header + "%%EndComments\n"));
        Assert.Equal(MetadataErrorCode.AmbiguousMetadata, Error(Header + "%%BoundingBox: 0 0 1 1\n%%BoundingBox: 0 0 1 1\n%%EndComments\n"));
        Assert.Equal(MetadataErrorCode.InvalidEps, Error(Header + "%%BoundingBox: 1 0 0 1\n%%EndComments\n"));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Error(Header + "%%BoundingBox: (atend)\n%%EndComments\n"));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Error(Header + "%%BoundingBox: 0 0 2.5 3\n%%EndComments\n"));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Error(Header + "%%BoundingBox: 0 0 2147483648 3\n%%EndComments\n"));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Error("%!PS-Adobe-2.0 EPSF-2.0\n"));
    }

    [Fact]
    public void TruncatedAndExcessiveHeadersAreRejected()
    {
        byte[] bytes = Fixture();
        int end = Encoding.ASCII.GetString(bytes).IndexOf("%%EndComments\n", StringComparison.Ordinal) + "%%EndComments\n".Length;
        for (int i = 0; i < end; i++)
            Assert.Throws<MetadataException>(() => EpsHeader.Read(bytes.AsSpan(0, i)));
        Assert.Equal(MetadataErrorCode.LimitExceeded, Error(Header + "%%" + new string('a', 4096) + "\n"));
        Assert.Equal(MetadataErrorCode.InvalidEps, Error(Header + "%%BoundingBox: 0 0 1 1"));
    }
}
