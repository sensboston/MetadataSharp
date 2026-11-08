using MetadataSharp.Core;
using Xunit;

namespace MetadataSharp.Tests.Capabilities;

/// <summary>
/// Pins the shipped, test-evidenced capability table. The expectation list below
/// is authored independently of the manifest and must be updated only together
/// with the reviewed contract change that justifies it.
/// </summary>
public sealed class MetadataCapabilitiesTests
{
    private static readonly (MetadataContainer Container, MetadataFamily Family, MetadataOperation Operation, CapabilityStatus Status, string Note)[] Expected =
    [
        (MetadataContainer.Jpeg, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Supported, "Classic TIFF/EXIF within the approved baseline-JPEG subset; unsupported layouts are refused."),
        (MetadataContainer.Jpeg, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Approved writable tag subset only (descriptive IFD0/EXIF/GPS tags); unsupported tags are refused."),
        (MetadataContainer.Jpeg, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Approved writable tag subset only (descriptive IFD0/EXIF/GPS tags); unsupported tags are refused."),
        (MetadataContainer.Jpeg, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-tag removal within the approved subset; whole-EXIF removal is also supported."),
        (MetadataContainer.Jpeg, MetadataFamily.IptcIim, MetadataOperation.Read, CapabilityStatus.Supported, "IIM within the approved IPTC usage contract; declared or UTF-8 encoding; finite packet limits enforced."),
        (MetadataContainer.Jpeg, MetadataFamily.IptcIim, MetadataOperation.Insert, CapabilityStatus.Partial, "Approved IPTC dataset usage subset; unsupported records are refused, not silently rewritten."),
        (MetadataContainer.Jpeg, MetadataFamily.IptcIim, MetadataOperation.Replace, CapabilityStatus.Partial, "Approved IPTC dataset usage subset; unsupported records are refused, not silently rewritten."),
        (MetadataContainer.Jpeg, MetadataFamily.IptcIim, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-dataset removal within the approved IPTC usage subset."),
        (MetadataContainer.Jpeg, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "Bounded restricted XMP profile (UTF-8, finite packet limits); raw bytes retained for unmodeled constructs."),
        (MetadataContainer.Jpeg, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Bounded restricted XMP profile; Extended XMP and unreviewed structures are refused."),
        (MetadataContainer.Jpeg, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Bounded restricted XMP profile; Extended XMP and unreviewed structures are refused."),
        (MetadataContainer.Jpeg, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-property removal within the restricted profile; whole-XMP removal is also supported."),
        (MetadataContainer.Png, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Supported, "eXIf chunk in static PNG within the approved chunk policy; unsupported layouts are refused."),
        (MetadataContainer.Png, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "eXIf in static PNG; approved writable tag subset only; unsupported tags are refused."),
        (MetadataContainer.Png, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "eXIf in static PNG; approved writable tag subset only; unsupported tags are refused."),
        (MetadataContainer.Png, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "eXIf in static PNG; per-tag removal within the approved subset; whole-EXIF removal is also supported."),
        (MetadataContainer.Png, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "XMP in static PNG within the approved chunk/text policy; raw bytes retained for unmodeled constructs."),
        (MetadataContainer.Png, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "XMP in static PNG; bounded restricted profile; Extended XMP is refused."),
        (MetadataContainer.Png, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "XMP in static PNG; bounded restricted profile; Extended XMP is refused."),
        (MetadataContainer.Png, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "XMP in static PNG; per-property removal within the restricted profile; whole-XMP removal is also supported."),
        (MetadataContainer.WebP, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Supported, "EXIF in static (non-animated) WebP within the approved RIFF policy; unsupported layouts are refused."),
        (MetadataContainer.WebP, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "EXIF in static WebP; approved writable tag subset only; unsupported tags are refused."),
        (MetadataContainer.WebP, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "EXIF in static WebP; approved writable tag subset only; unsupported tags are refused."),
        (MetadataContainer.WebP, MetadataFamily.Exif, MetadataOperation.Remove, CapabilityStatus.Partial, "EXIF in static WebP; per-tag removal within the approved subset; whole-EXIF removal is also supported."),
        (MetadataContainer.WebP, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "XMP in static WebP within the approved RIFF policy; raw bytes retained for unmodeled constructs."),
        (MetadataContainer.WebP, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "XMP in static WebP; bounded restricted profile; Extended XMP is refused."),
        (MetadataContainer.WebP, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "XMP in static WebP; bounded restricted profile; Extended XMP is refused."),
        (MetadataContainer.WebP, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "XMP in static WebP; per-property removal within the restricted profile; whole-XMP removal is also supported."),
        (MetadataContainer.Tiff, MetadataFamily.Exif, MetadataOperation.Read, CapabilityStatus.Partial, "Standalone classic (non-BigTIFF) TIFF only; read-only range accounting and raw values, not general TIFF support."),
        (MetadataContainer.Tiff, MetadataFamily.Exif, MetadataOperation.Insert, CapabilityStatus.Partial, "Ten descriptive IFD0 tags via append-preserve rebuild; obsolete bytes retained; unsupported schemas refused."),
        (MetadataContainer.Tiff, MetadataFamily.Exif, MetadataOperation.Replace, CapabilityStatus.Partial, "Ten descriptive IFD0 tags via append-preserve rebuild; obsolete bytes retained; unsupported schemas refused."),
        (MetadataContainer.XmpSidecar, MetadataFamily.Xmp, MetadataOperation.Read, CapabilityStatus.Supported, "Load/save of XMP packets and .xmp sidecars within the bounded restricted profile."),
        (MetadataContainer.XmpSidecar, MetadataFamily.Xmp, MetadataOperation.Insert, CapabilityStatus.Partial, "Bounded restricted XMP profile; Extended XMP and unreviewed structures are refused."),
        (MetadataContainer.XmpSidecar, MetadataFamily.Xmp, MetadataOperation.Replace, CapabilityStatus.Partial, "Bounded restricted XMP profile; Extended XMP and unreviewed structures are refused."),
        (MetadataContainer.XmpSidecar, MetadataFamily.Xmp, MetadataOperation.Remove, CapabilityStatus.Partial, "Per-property removal within the restricted profile; whole-XMP removal is also supported."),
    ];

    [Fact]
    public void All_ContainsExactlyThePinnedEvidencedRows()
    {
        Assert.Equal(Expected.Length, MetadataCapabilities.All.Count);
        for (int i = 0; i < Expected.Length; i++)
        {
            (MetadataContainer container, MetadataFamily family, MetadataOperation operation, CapabilityStatus status, string note) = Expected[i];
            Assert.Equal(new CapabilityDescriptor(container, family, operation, status, note), MetadataCapabilities.All[i]);
        }
    }

    [Fact]
    public void All_IsStableStrictlyOrderedAndNonTrivial()
    {
        Assert.Same(MetadataCapabilities.All, MetadataCapabilities.All);
        Assert.NotEmpty(MetadataCapabilities.All);
        for (int i = 1; i < MetadataCapabilities.All.Count; i++)
        {
            CapabilityDescriptor previous = MetadataCapabilities.All[i - 1];
            CapabilityDescriptor current = MetadataCapabilities.All[i];
            int comparison = previous.Container.CompareTo(current.Container);
            if (comparison == 0) comparison = previous.Family.CompareTo(current.Family);
            if (comparison == 0) comparison = previous.Operation.CompareTo(current.Operation);
            Assert.True(comparison < 0, $"Rows out of order at index {i}.");
        }
    }

    [Fact]
    public void All_ContainsOnlyDefinedNonDefaultRows()
    {
        foreach (CapabilityDescriptor row in MetadataCapabilities.All)
        {
            Assert.True(Enum.IsDefined(row.Container));
            Assert.True(Enum.IsDefined(row.Family));
            Assert.True(Enum.IsDefined(row.Operation));
            Assert.NotEqual(CapabilityStatus.NotSupported, row.Status);
            Assert.True(Enum.IsDefined(row.Status));
            Assert.NotNull(row.Note);
            Assert.InRange(row.Note!.Length, 1, 240);
            Assert.DoesNotContain(row.Note, ch => char.IsControl(ch));
        }
    }

    [Fact]
    public void TryGet_ReturnsExactDescriptorForDeclaredRows()
    {
        foreach ((MetadataContainer container, MetadataFamily family, MetadataOperation operation, CapabilityStatus status, string note) in Expected)
        {
            Assert.True(MetadataCapabilities.TryGet(container, family, operation, out CapabilityDescriptor descriptor));
            Assert.Equal(new CapabilityDescriptor(container, family, operation, status, note), descriptor);
        }
    }

    [Theory]
    [InlineData(MetadataContainer.Tiff, MetadataFamily.Xmp, MetadataOperation.Read)]
    [InlineData(MetadataContainer.Tiff, MetadataFamily.Exif, MetadataOperation.Remove)]
    [InlineData(MetadataContainer.Jpeg, MetadataFamily.Exif, MetadataOperation.Extract)]
    [InlineData(MetadataContainer.Jpeg, MetadataFamily.Icc, MetadataOperation.Read)]
    [InlineData(MetadataContainer.Jpeg, MetadataFamily.Thumbnail, MetadataOperation.Extract)]
    [InlineData(MetadataContainer.Png, MetadataFamily.IptcIim, MetadataOperation.Read)]
    [InlineData(MetadataContainer.WebP, MetadataFamily.IptcIim, MetadataOperation.Read)]
    [InlineData(MetadataContainer.Dng, MetadataFamily.Exif, MetadataOperation.Read)]
    [InlineData(MetadataContainer.Arw, MetadataFamily.Exif, MetadataOperation.Read)]
    [InlineData(MetadataContainer.XmpSidecar, MetadataFamily.Exif, MetadataOperation.Read)]
    [InlineData(MetadataContainer.Avif, MetadataFamily.Exif, MetadataOperation.Read)]
    [InlineData(MetadataContainer.Jxl, MetadataFamily.Xmp, MetadataOperation.Read)]
    public void TryGet_AbsentRow_FailsClosed(MetadataContainer container, MetadataFamily family, MetadataOperation operation)
    {
        Assert.False(MetadataCapabilities.TryGet(container, family, operation, out CapabilityDescriptor descriptor));
        Assert.Equal(default, descriptor);
        Assert.Equal(CapabilityStatus.NotSupported, MetadataCapabilities.GetStatus(container, family, operation));
    }

    [Theory]
    [InlineData((MetadataContainer)999, MetadataFamily.Exif, MetadataOperation.Read)]
    [InlineData(MetadataContainer.Jpeg, (MetadataFamily)999, MetadataOperation.Read)]
    [InlineData(MetadataContainer.Jpeg, MetadataFamily.Exif, (MetadataOperation)999)]
    [InlineData((MetadataContainer)255, (MetadataFamily)255, (MetadataOperation)255)]
    public void UndefinedEnumValues_FailClosedWithoutThrowing(MetadataContainer container, MetadataFamily family, MetadataOperation operation)
    {
        Assert.False(MetadataCapabilities.TryGet(container, family, operation, out CapabilityDescriptor descriptor));
        Assert.Equal(default, descriptor);
        Assert.Equal(CapabilityStatus.NotSupported, MetadataCapabilities.GetStatus(container, family, operation));
    }

    [Fact]
    public void EnumDomains_CoverTheDocumentedBaseline()
    {
        Assert.Equal(32, Enum.GetValues<MetadataContainer>().Length); // Unknown + the 31 documented baseline rows (XMP represented as XmpSidecar)
        Assert.Equal(7, Enum.GetValues<MetadataFamily>().Length);
        Assert.Equal(5, Enum.GetValues<MetadataOperation>().Length);
        Assert.Equal(0, (int)MetadataContainer.Unknown);
        Assert.Equal(0, (int)MetadataFamily.Exif);
        Assert.Equal(0, (int)MetadataOperation.Read);
        Assert.Equal(0, (int)CapabilityStatus.NotSupported);
    }
}
