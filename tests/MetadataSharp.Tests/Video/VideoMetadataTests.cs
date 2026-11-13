using MetadataSharp.Core;
using MetadataSharp.Formats;
using Xunit;

namespace MetadataSharp.Tests.Video;

public sealed class VideoMetadataTests
{
    private static byte[] Fixture(string directory, string name)
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MetadataSharp.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllBytes(Path.Combine(root!.FullName, "tests", "fixtures", "video", directory, name));
    }

    [Theory]
    [InlineData("authored-asf.asf", MetadataFormat.Asf, "Xmp.video.FileLength", "123456")]
    [InlineData("authored-webm.webm", MetadataFormat.Matroska, "Xmp.video.Title", "Fixture WebM")]
    [InlineData("authored-quicktime.mp4", MetadataFormat.QuickTime, "Xmp.video.MajorBrand", "MP4 Base Media v1 [IS0 14496-12:2003]")]
    [InlineData("authored-wave.wav", MetadataFormat.Riff, "Xmp.video.FileType", "WAVE")]
    [InlineData("authored-avi.avi", MetadataFormat.Riff, "Xmp.video.FileType", "AVI ")]
    public void AuthoredFilesAreDetectedAndReadThroughTheUnifiedEntryPoint(
        string name, MetadataFormat expectedFormat, string expectedKey, string expectedValue)
    {
        ImageMetadata image = ImageMetadata.Read(Fixture("valid", name));
        Assert.Equal(expectedFormat, image.Format);
        Assert.Empty(image.Errors);
        Assert.NotNull(image.Video);
        Assert.Equal(expectedFormat, image.Video!.Format);
        Assert.Equal(expectedValue, image.Video[expectedKey]!.Value);
        Assert.Null(image.Exif);
        Assert.Null(image.Iptc);
        Assert.Null(image.Xmp);
    }

    [Theory]
    [InlineData("asf.asf", MetadataFormat.Asf)]
    [InlineData("ebml.webm", MetadataFormat.Matroska)]
    [InlineData("quicktime.mov", MetadataFormat.QuickTime)]
    [InlineData("riff.wav", MetadataFormat.Riff)]
    public void MalformedVideoIsReportedAsVideoRefusalWithoutEscapingTheUnifiedReader(string name, MetadataFormat format)
    {
        ImageMetadata image = ImageMetadata.Read(Fixture("malformed", name));
        Assert.Equal(format, image.Format);
        Assert.Null(image.Video);
        MetadataReadError error = Assert.Single(image.Errors);
        Assert.Equal(MetadataFamily.Video, error.Family);
        Assert.Contains(error.Code, new[] { MetadataErrorCode.InvalidVideoContainer, MetadataErrorCode.LimitExceeded });
    }

    [Fact]
    public void ReadUsesInputAndStructureBudgets()
    {
        byte[] input = Fixture("valid", "authored-webm.webm");
        Assert.Equal(MetadataErrorCode.LimitExceeded,
            Assert.Throws<MetadataException>(() => VideoMetadata.Read(input, new MetadataLimits { MaxInputBytes = input.Length - 1 })).Code);
        Assert.Equal(MetadataErrorCode.LimitExceeded,
            Assert.Throws<MetadataException>(() => VideoMetadata.Read(input, new MetadataLimits { MaxSegments = 1 })).Code);
    }

    [Fact]
    public void QuickTimeSequenceRetainsItemsAndLookupUsesTheFirstExactKey()
    {
        VideoMetadata video = VideoMetadata.Read(Fixture("valid", "authored-quicktime.mp4"));
        VideoProperty brands = Assert.IsType<VideoProperty>(video["Xmp.video.CompatibleBrands"]);
        Assert.NotNull(brands.Items);
        Assert.Equal(new[] { "MP4 Base Media v1 [IS0 14496-12:2003]", "MP4 v2 [ISO 14496-14]" }, brands.Items);
        Assert.Null(video["xmp.video.CompatibleBrands"]);
    }

    [Theory]
    [InlineData(MetadataContainer.Asf)]
    [InlineData(MetadataContainer.Matroska)]
    [InlineData(MetadataContainer.QuickTime)]
    [InlineData(MetadataContainer.Riff)]
    public void CapabilityIsReadOnly(MetadataContainer container)
    {
        Assert.Equal(CapabilityStatus.Partial, MetadataCapabilities.GetStatus(container, MetadataFamily.Video, MetadataOperation.Read));
        Assert.Equal(CapabilityStatus.NotSupported, MetadataCapabilities.GetStatus(container, MetadataFamily.Video, MetadataOperation.Insert));
        Assert.Equal(CapabilityStatus.NotSupported, MetadataCapabilities.GetStatus(container, MetadataFamily.Video, MetadataOperation.Replace));
        Assert.Equal(CapabilityStatus.NotSupported, MetadataCapabilities.GetStatus(container, MetadataFamily.Video, MetadataOperation.Remove));
    }
}
