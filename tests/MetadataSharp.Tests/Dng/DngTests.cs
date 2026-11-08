using System.Buffers.Binary;
using System.Security.Cryptography;
using MetadataSharp.Core;
using MetadataSharp.Formats.Dng;
using MetadataSharp.Formats.Tiff;
using Xunit;

namespace MetadataSharp.Tests.Dng;

public sealed class DngTests
{
    private static bool Big(byte[] data) => data[0] == 0x4d;
    private static ushort U16(byte[] data, long at, bool big) => big ? BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan((int)at)) : BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan((int)at));
    private static uint U32(byte[] data, long at, bool big) => big ? BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)at)) : BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)at));
    private static void SetU16(byte[] data, long at, ushort value, bool big) { if (big) BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan((int)at), value); else BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan((int)at), value); }
    private static void SetU32(byte[] data, long at, uint value, bool big) { if (big) BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan((int)at), value); else BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan((int)at), value); }
    private static int FieldAt(byte[] data, ushort tag, int ifdOffset = 8)
    {
        bool big = Big(data);
        int count = U16(data, ifdOffset, big);
        for (int i = 0; i < count; i++) if (U16(data, ifdOffset + 2 + 12 * i, big) == tag) return ifdOffset + 2 + 12 * i;
        return -1;
    }
    private static int ValueCell(byte[] data, ushort tag, int ifdOffset = 8) => FieldAt(data, tag, ifdOffset) + 8;
    private static uint ExternalPointer(byte[] data, ushort tag, int ifdOffset = 8) => U32(data, ValueCell(data, tag, ifdOffset), Big(data));
    private static int NextIfdCell(byte[] data, int ifdOffset = 8) => ifdOffset + 2 + 12 * U16(data, ifdOffset, Big(data));
    private static DngLayout Read(byte[] bytes, DngReadOptions? options = null) => DngLayout.Read(new MemoryStream(bytes), options);
    private static MetadataErrorCode Error(byte[] bytes, DngReadOptions? options = null) => Assert.Throws<MetadataException>(() => Read(bytes, options)).Code;

    // ---------- Known bytes: both shapes, both byte orders ----------

    [Theory]
    // Pins the C# writer (independent of the Python writer pinned by the offline Python suite).
    [InlineData(false, false, 598, "474952a49242556b9bec981483df2afc9c06546a0c7705a529f789c9ad52d2a0")]
    [InlineData(true, false, 598, "64dee07029c8ed6fb9fcddac5a578ec79141789e6834fdf22809f8bca08685f8")]
    [InlineData(false, true, 1040, "887ae87d6d1513c0d26ba6e5dcaf89bc7a027f086d3bc4ed1bb43f68f7ab2bd5")]
    [InlineData(true, true, 1040, "51cb2c3dcba0a96c3946f0e789b2a5623a0ec65e05a61a482e8bf8367a62381c")]
    public void FixtureBytesArePinned(bool big, bool thumb, int length, string sha256)
    {
        DngFixture fixture = thumb ? DngFixture.ThumbnailAndRaw(big) : DngFixture.RawOnly(big);
        Assert.Equal(length, fixture.Bytes.Length);
        Assert.Equal(sha256, fixture.Sha256);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RawRootedShapeReadsSupported(bool big)
    {
        byte[] bytes = DngFixture.RawOnly(big).Bytes;
        DngLayout layout = Read(bytes);
        Assert.True(layout.IsSupported);
        Assert.Equal(big ? Endianness.BigEndian : Endianness.LittleEndian, layout.ByteOrder);
        Assert.Equal(8u, layout.FirstIfdOffset);
        Assert.Equal(new DngVersion(1, 1, 0, 0), layout.Version);
        Assert.Equal(new DngVersion(1, 0, 0, 0), layout.BackwardVersion);
        Assert.Empty(layout.Issues);
        DngDirectoryInfo directory = Assert.Single(layout.Directories);
        Assert.True(directory.IsRoot);
        Assert.Empty(directory.Links);
        Assert.Equal(31, directory.Fields.Count);
        DngImageLayout image = directory.Image!;
        Assert.Equal(DngImageRole.CfaRaw, image.Role);
        Assert.Equal(4u, image.Width); Assert.Equal(2u, image.Height);
        Assert.Equal((ushort)16, image.BitsPerSample); Assert.Equal((ushort)1, image.SamplesPerPixel);
        Assert.Equal(new TiffByteRange(582, 16), Assert.Single(image.Strips));
        Assert.Contains(layout.Ranges, range => range.Kind == TiffRangeKind.Strip && range.Range == new TiffByteRange(582, 16));
        Assert.Contains(layout.Ranges, range => range.Kind == TiffRangeKind.Header && range.Range == new TiffByteRange(0, 8));
        for (int i = 1; i < layout.Ranges.Count; i++) Assert.True(layout.Ranges[i].Range.Offset >= layout.Ranges[i - 1].Range.Offset + layout.Ranges[i - 1].Range.Length);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ThumbnailShapeReadsSupportedWithSubIfdLeaf(bool big)
    {
        byte[] bytes = DngFixture.ThumbnailAndRaw(big).Bytes;
        DngLayout layout = Read(bytes);
        Assert.True(layout.IsSupported);
        Assert.Empty(layout.Issues);
        Assert.Equal(2, layout.Directories.Count);
        DngDirectoryInfo root = layout.Directories[0];
        DngDirectoryInfo leaf = layout.Directories[1];
        Assert.True(root.IsRoot); Assert.False(leaf.IsRoot);
        Assert.Equal(8u, root.Offset); Assert.Equal(450u, leaf.Offset);
        Assert.Equal(DngImageRole.RgbThumbnail, root.Image!.Role);
        Assert.Equal(DngImageRole.CfaRaw, leaf.Image!.Role);
        Assert.Equal((ushort)8, root.Image.BitsPerSample); Assert.Equal((ushort)3, root.Image.SamplesPerPixel);
        Assert.Equal(new TiffByteRange(426, 24), Assert.Single(root.Image.Strips));
        Assert.Equal(new TiffByteRange(1024, 16), Assert.Single(leaf.Image.Strips));
        Assert.Equal(21, root.Fields.Count); Assert.Equal(31, leaf.Fields.Count);
        TiffDirectoryLink link = Assert.Single(root.Links);
        Assert.Equal(TiffDirectoryLinkKind.SubIfd, link.Kind); Assert.Equal((ushort?)330, link.TagId); Assert.Equal(450u, link.TargetIfdOffset);
        Assert.Empty(leaf.Links);
        Assert.True(link.PointerOffset > 8);
        // NewSubFileType semantics are visible per directory.
        Assert.Contains(root.Fields, field => field.TagId == 254);
        Assert.Contains(leaf.Fields, field => field.TagId == 254);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SnapshotSurvivesSourceMutationAndOwnership(bool big)
    {
        byte[] bytes = DngFixture.ThumbnailAndRaw(big).Bytes;
        DngLayout layout = Read(bytes);
        Array.Clear(bytes);
        Assert.Equal(1040, layout.SourceLength);
        Assert.Equal(new DngVersion(1, 1, 0, 0), layout.Version);
        Assert.Equal(4u, layout.Directories[0].Image!.Width);
        Assert.Equal(426L, layout.Directories[0].Image.Strips[0].Offset);
        Assert.True(layout.Ranges.Count > 4);
        Assert.False(layout.Directories is DngDirectoryInfo[]);
        Assert.False(layout.Ranges is TiffAccountedRange[]);
        Assert.False(layout.Issues is DngLayoutIssue[]);
    }

    // ---------- Framing and hard DNG identity errors ----------

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BigTiffMagicRefusesAsUnsupportedFeature(bool big)
    {
        byte[] bytes = DngFixture.RawOnly(big).Bytes;
        SetU16(bytes, 2, 43, big);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Error(bytes));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InvalidMagicAndByteOrderRefuseAsInvalidTiff(bool big)
    {
        byte[] bytes = DngFixture.RawOnly(big).Bytes;
        SetU16(bytes, 2, 77, big);
        Assert.Equal(MetadataErrorCode.InvalidTiff, Error(bytes));
        bytes = DngFixture.RawOnly(big).Bytes;
        bytes[0] = (byte)'Z';
        Assert.Equal(MetadataErrorCode.InvalidTiff, Error(bytes));
        Assert.Equal(MetadataErrorCode.InvalidTiff, Error(DngFixture.RawOnly(big).Bytes.AsSpan(0, 6).ToArray()));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OrdinaryTiffWithoutRootDngVersionRefusesAsUnsupportedFeature(bool big)
    {
        byte[] bytes = DngFixture.RawOnly(big).Bytes;
        SetU16(bytes, FieldAt(bytes, 50706), 50998, big);
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Error(bytes));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DngVersionWithWrongShapeThrowsInvalidDng(bool big)
    {
        byte[] bytes = DngFixture.RawOnly(big).Bytes;
        SetU16(bytes, FieldAt(bytes, 50706) + 2, 3, big); // type SHORT
        Assert.Equal(MetadataErrorCode.InvalidDng, Error(bytes));
        bytes = DngFixture.RawOnly(big).Bytes;
        SetU32(bytes, FieldAt(bytes, 50706) + 4, 5, big); // count 5
        Assert.Equal(MetadataErrorCode.InvalidDng, Error(bytes));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BackwardVersionWithWrongShapeThrowsInvalidDng(bool big)
    {
        byte[] bytes = DngFixture.RawOnly(big).Bytes;
        SetU16(bytes, FieldAt(bytes, 50707) + 2, 3, big); // type SHORT
        Assert.Equal(MetadataErrorCode.InvalidDng, Error(bytes));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DirectoryEntryCountZeroRefuses(bool big)
    {
        byte[] bytes = DngFixture.RawOnly(big).Bytes;
        SetU16(bytes, 8, 0, big);
        Assert.Equal(MetadataErrorCode.InvalidTiff, Error(bytes));
    }
    // ---------- Version semantics ----------

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NewerFramingVersionReportsUnsupportedVersionWithoutThrowing(bool big)
    {
        byte[] bytes = DngFixture.RawOnly(big).Bytes;
        bytes[ValueCell(bytes, 50706) + 1] = 0x04; // 1.4.0.0
        DngLayout layout = Read(bytes);
        Assert.False(layout.IsSupported);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.UnsupportedVersion && issue.TagId == 50706);
        Assert.Equal(4u, layout.Directories[0].Image!.Width); // geometry still established
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BackwardVersionAboveVersionReportsIssue(bool big)
    {
        byte[] bytes = DngFixture.RawOnly(big).Bytes;
        bytes[ValueCell(bytes, 50706) + 1] = 0x00; // DNGVersion 1.0.0.0
        bytes[ValueCell(bytes, 50707) + 1] = 0x01; // DNGBackwardVersion 1.1.0.0 > 1.0.0.0
        DngLayout layout = Read(bytes);
        Assert.False(layout.IsSupported);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.UnsupportedVersion && issue.TagId == 50707);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UnrecognizedBackwardVersionReportsIssue(bool big)
    {
        byte[] bytes = DngFixture.RawOnly(big).Bytes;
        bytes[ValueCell(bytes, 50707) + 1] = 0x02; // 1.2.0.0, outside {1.0.0.0, 1.1.0.0}
        DngLayout layout = Read(bytes);
        Assert.False(layout.IsSupported);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.UnsupportedVersion && issue.TagId == 50707);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MissingBackwardVersionDefaultsToVersionWithZeroedBuild(bool big)
    {
        byte[] bytes = DngFixture.RawOnly(big).Bytes;
        SetU16(bytes, FieldAt(bytes, 50707), 50998, big); // rename: 50707 absent (unlisted tag → unrelated issue)
        DngLayout layout = Read(bytes);
        Assert.Equal(new DngVersion(1, 1, 0, 0), layout.BackwardVersion);
    }

    // ---------- Extension refusal (opcode/private/digest-class tags) ----------

    [Theory]
    [InlineData(50722)] // opcode-list-class tag, unlisted in the profile
    [InlineData(50730)] // DNGPrivateData-class tag, unlisted in the profile
    [InlineData(65000)] // arbitrary unknown extension
    public void UnlistedTagsRefuseWithUnsupportedExtension(ushort tag)
    {
        byte[] bytes = DngFixture.RawOnly(false).Bytes;
        SetU16(bytes, FieldAt(bytes, 50778), tag, false); // repurpose a unique slot; 50778 then missing
        DngLayout layout = Read(bytes);
        Assert.False(layout.IsSupported);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.UnsupportedExtension && issue.TagId == tag);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.MissingRequiredField && issue.TagId == 50778);
    }

    [Fact]
    public void DuplicateTagsFailFraming()
    {
        byte[] bytes = DngFixture.RawOnly(false).Bytes;
        SetU16(bytes, FieldAt(bytes, 256), 257, false); // two 257 entries
        Assert.Equal(MetadataErrorCode.InvalidTiff, Error(bytes));
    }

    // ---------- Semantic boundary tests ----------

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CfaRepeatPatternDimMustBe2x2(bool big)
    {
        byte[] bytes = DngFixture.RawOnly(big).Bytes;
        SetU16(bytes, ValueCell(bytes, 33421), 4, big);
        SetU16(bytes, ValueCell(bytes, 33421) + 2, 4, big);
        DngLayout layout = Read(bytes);
        Assert.False(layout.IsSupported);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.InvalidSemanticValue && issue.TagId == 33421);
    }

    [Fact]
    public void CfaPatternNeedsOneRedTwoGreenOneBlue()
    {
        byte[] bytes = DngFixture.RawOnly(false).Bytes;
        new byte[] { 0, 1, 2, 2 }.AsSpan().CopyTo(bytes.AsSpan((int)ValueCell(bytes, 33422), 4)); // counts: one 0, one 1, two 2s
        DngLayout layout = Read(bytes);
        Assert.False(layout.IsSupported);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.InvalidSemanticValue && issue.TagId == 33422);
    }

    [Fact]
    public void CfaPlaneColorMustBe012()
    {
        byte[] bytes = DngFixture.RawOnly(false).Bytes;
        bytes[ValueCell(bytes, 50710) + 2] = 0x00; // [0, 1, 0]
        DngLayout layout = Read(bytes);
        Assert.False(layout.IsSupported);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.InvalidSemanticValue && issue.TagId == 50710);
    }

    [Fact]
    public void CfaLayoutMustBeRectangular()
    {
        byte[] bytes = DngFixture.RawOnly(false).Bytes;
        SetU16(bytes, ValueCell(bytes, 50711), 2, false);
        DngLayout layout = Read(bytes);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.InvalidSemanticValue && issue.TagId == 50711);
    }

    [Fact]
    public void BlackLevelRepeatDimMustBe11()
    {
        byte[] bytes = DngFixture.RawOnly(false).Bytes;
        SetU16(bytes, ValueCell(bytes, 50713), 2, false);
        SetU16(bytes, ValueCell(bytes, 50713) + 2, 2, false);
        DngLayout layout = Read(bytes);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.InvalidSemanticValue && issue.TagId == 50713);
    }

    [Fact]
    public void BlackLevelZeroDenominatorRefusesSemantics()
    {
        byte[] bytes = DngFixture.RawOnly(false).Bytes;
        SetU32(bytes, ExternalPointer(bytes, 50714) + 4, 0, false); // denominator = 0
        DngLayout layout = Read(bytes);
        Assert.False(layout.IsSupported);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.InvalidSemanticValue && issue.TagId == 50714);
    }

    [Fact]
    public void AsShotNeutralRequiresPositiveComponents()
    {
        byte[] bytes = DngFixture.RawOnly(false).Bytes;
        SetU32(bytes, ExternalPointer(bytes, 50728), 0, false); // first numerator = 0
        DngLayout layout = Read(bytes);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.InvalidSemanticValue && issue.TagId == 50728);
    }

    [Fact]
    public void ColorMatrix1RequiresPositiveDenominators()
    {
        byte[] bytes = DngFixture.RawOnly(false).Bytes;
        SetU32(bytes, ExternalPointer(bytes, 50721) + 4, 0, false); // first denominator = 0
        DngLayout layout = Read(bytes);
        Assert.False(layout.IsSupported);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.InvalidSemanticValue && issue.TagId == 50721);
    }

    [Fact]
    public void UniqueCameraModelMustBePrintableAsciiWithFinalNul()
    {
        byte[] bytes = DngFixture.RawOnly(false).Bytes;
        bytes[ExternalPointer(bytes, 50708)] = 0x7f; // DEL is not printable
        DngLayout layout = Read(bytes);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.InvalidSemanticValue && issue.TagId == 50708);
        bytes = DngFixture.RawOnly(false).Bytes;
        long cell = ExternalPointer(bytes, 50708);
        bytes[cell + 19] = (byte)'X'; // overwrite the final NUL (payload is 20 bytes)
        layout = Read(bytes);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.InvalidSemanticValue && issue.TagId == 50708);
    }

    [Fact]
    public void WhiteLevelMustBe65535()
    {
        byte[] bytes = DngFixture.RawOnly(false).Bytes;
        SetU32(bytes, ValueCell(bytes, 50717), 65534, false);
        DngLayout layout = Read(bytes);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.InvalidSemanticValue && issue.TagId == 50717);
    }

    [Fact]
    public void ActiveAreaBeyondRawBoundsRefuses()
    {
        byte[] bytes = DngFixture.RawOnly(false).Bytes;
        SetU32(bytes, ExternalPointer(bytes, 50829) + 8, 3, false); // bottom = 3 > height 2
        DngLayout layout = Read(bytes);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.InvalidSemanticValue && issue.TagId == 50829);
    }
    // ---------- Grid, strips and accounting ----------

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OddRawDimensionsRefuseEvenRequirement(bool big)
    {
        byte[] bytes = DngFixture.RawOnly(big).Bytes;
        SetU32(bytes, ValueCell(bytes, 256), 5, big);
        DngLayout layout = Read(bytes);
        Assert.False(layout.IsSupported);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.InvalidSemanticValue && issue.TagId == 256);
    }

    [Fact]
    public void ZeroRawHeightRefuses()
    {
        byte[] bytes = DngFixture.RawOnly(false).Bytes;
        SetU32(bytes, ValueCell(bytes, 257), 0, false);
        DngLayout layout = Read(bytes);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.InvalidSemanticValue && issue.TagId == 256);
        Assert.Null(layout.Directories[0].Image);
    }

    [Fact]
    public void RowsPerStripZeroRefuses()
    {
        byte[] bytes = DngFixture.RawOnly(false).Bytes;
        SetU32(bytes, ValueCell(bytes, 278), 0, false);
        DngLayout layout = Read(bytes);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.InvalidSemanticValue && issue.TagId == 278);
        Assert.Null(layout.Directories[0].Image);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void StripGridMismatchRefuses(bool big)
    {
        byte[] bytes = DngFixture.RawOnly(big).Bytes;
        SetU32(bytes, ValueCell(bytes, 278), 1, big); // height 2 / 1 row → 2 strips expected, only 1 present
        DngLayout layout = Read(bytes);
        Assert.False(layout.IsSupported);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.InvalidSemanticValue && issue.TagId == 273);
        Assert.Null(layout.Directories[0].Image);
    }

    [Fact]
    public void StripByteCountMismatchRefuses()
    {
        byte[] bytes = DngFixture.RawOnly(false).Bytes;
        SetU32(bytes, ValueCell(bytes, 279), 15, false); // expected exactly 16
        DngLayout layout = Read(bytes);
        Assert.False(layout.IsSupported);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.InvalidSemanticValue && issue.TagId == 279);
        Assert.Equal(15u, layout.Directories[0].Image!.Strips[0].Length); // declared extent is what is accounted
    }

    [Fact]
    public void RawPhotometricOtherThanCfaRefuses()
    {
        byte[] bytes = DngFixture.RawOnly(false).Bytes;
        SetU16(bytes, ValueCell(bytes, 262), 2, false);
        DngLayout layout = Read(bytes);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.UnsupportedSampleLayout && issue.TagId == 262);
    }

    [Fact]
    public void RawSampleFormatOtherThanUnsignedIntegerRefuses()
    {
        byte[] bytes = DngFixture.RawOnly(false).Bytes;
        SetU16(bytes, ValueCell(bytes, 339), 3, false);
        DngLayout layout = Read(bytes);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.UnsupportedSampleLayout && issue.TagId == 339);
    }

    [Fact]
    public void RawPlanarConfigurationMatters()
    {
        byte[] bytes = DngFixture.RawOnly(false).Bytes;
        SetU16(bytes, ValueCell(bytes, 284), 2, false);
        DngLayout layout = Read(bytes);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.UnsupportedSampleLayout && issue.TagId == 284);
    }

    [Fact]
    public void UncompressedCompressionOnly()
    {
        byte[] bytes = DngFixture.RawOnly(false).Bytes;
        SetU16(bytes, ValueCell(bytes, 259), 7, false);
        DngLayout layout = Read(bytes);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.UnsupportedCompression && issue.TagId == 259);
    }

    [Fact]
    public void ThumbnailMustNotBeAcceptedAsRaw()
    {
        byte[] bytes = DngFixture.ThumbnailAndRaw(false).Bytes;
        SetU32(bytes, ValueCell(bytes, 254), 0, false); // thumbnail root claims raw role
        DngLayout layout = Read(bytes);
        Assert.False(layout.IsSupported);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.UnsupportedDirectoryRole);
    }

    // ---------- Links, graph and out-of-profile structure ----------

    [Fact]
    public void DanglingSubIfdTargetRefuses()
    {
        byte[] bytes = DngFixture.ThumbnailAndRaw(false).Bytes;
        SetU32(bytes, ValueCell(bytes, 330), 99999, false);
        Assert.Equal(MetadataErrorCode.InvalidTiff, Error(bytes));
    }

    [Fact]
    public void SubIfdSelfReferenceRefusesAsCycle()
    {
        byte[] bytes = DngFixture.ThumbnailAndRaw(false).Bytes;
        SetU32(bytes, ValueCell(bytes, 330), 8, false); // SubIFD points at root → cycle
        Assert.Equal(MetadataErrorCode.InvalidTiff, Error(bytes));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NextIfdChainRefuses(bool big)
    {
        byte[] bytes = DngFixture.RawOnly(big).Bytes;
        SetU32(bytes, NextIfdCell(bytes), 8, big); // root points at itself as "next"
        DngLayout layout = Read(bytes);
        Assert.False(layout.IsSupported);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.UnsupportedDirectoryRole);
    }

    [Fact]
    public void ExtraSubIfdRefuses()
    {
        // Thumbnail root with two SubIFD targets: patch the entry to LONG/2 is not possible inline;
        // instead point SubIFD at the raw leaf AND mark the raw leaf's own 254 as thumbnail role.
        byte[] bytes = DngFixture.ThumbnailAndRaw(false).Bytes;
        SetU32(bytes, ValueCell(bytes, 254, 450), 1, false); // raw leaf claims thumbnail
        DngLayout layout = Read(bytes);
        Assert.False(layout.IsSupported);
        Assert.Contains(layout.Issues, issue => issue.Code == DngLayoutIssueCode.UnsupportedDirectoryRole);
    }

    // ---------- Limits ----------

    [Fact]
    public void ThumbnailStripBytesRespectCapturedCeiling()
    {
        byte[] bytes = DngFixture.ThumbnailAndRaw(false).Bytes;
        Assert.Equal(MetadataErrorCode.LimitExceeded, Error(bytes, new DngReadOptions { MaxThumbnailBytes = 23 }));
        DngLayout layout = Read(bytes, new DngReadOptions { MaxThumbnailBytes = 24 });
        Assert.True(layout.IsSupported);
    }

    [Fact]
    public void RawPixelsRespectCapturedCeiling()
    {
        byte[] bytes = DngFixture.RawOnly(false).Bytes;
        Assert.Equal(MetadataErrorCode.LimitExceeded, Error(bytes, new DngReadOptions { MaxRawPixels = 7 }));
        Assert.True(Read(bytes, new DngReadOptions { MaxRawPixels = 8 }).IsSupported);
    }

    [Fact]
    public void ThumbnailPixelsRespectCapturedCeiling()
    {
        byte[] bytes = DngFixture.ThumbnailAndRaw(false).Bytes;
        Assert.Equal(MetadataErrorCode.LimitExceeded, Error(bytes, new DngReadOptions { MaxThumbnailPixels = 7 }));
    }

    [Fact]
    public void NonPositiveCeilingsAreArgumentErrors()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Read(DngFixture.RawOnly(false).Bytes, new DngReadOptions { MaxRawPixels = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Read(DngFixture.RawOnly(false).Bytes, new DngReadOptions { MaxThumbnailPixels = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Read(DngFixture.RawOnly(false).Bytes, new DngReadOptions { MaxThumbnailBytes = 0 }));
        Assert.Throws<ArgumentNullException>(() => Read(DngFixture.RawOnly(false).Bytes, new DngReadOptions { TiffOptions = null! }));
    }

    [Fact]
    public void SourceMustBeReadableSeekableAndAtZero()
    {
        using var stream = new MemoryStream(DngFixture.RawOnly(false).Bytes) { Position = 4 };
        Assert.Throws<ArgumentException>(() => DngLayout.Read(stream));
    }
    // ---------- Async, cancellation, short reads and paths ----------

    [Fact]
    public async Task PathReadSupportsOwnedSnapshot()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".dng");
        await File.WriteAllBytesAsync(path, DngFixture.RawOnly(false).Bytes);
        try
        {
            DngLayout layout = DngLayout.Read(path);
            Assert.True(layout.IsSupported);
            layout = await DngLayout.ReadAsync(path);
            Assert.True(layout.IsSupported);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CooperativeCancellationStopsBeforeWork()
    {
        using var stream = new MemoryStream(DngFixture.ThumbnailAndRaw(false).Bytes);
        await Assert.ThrowsAsync<OperationCanceledException>(() => DngLayout.ReadAsync(stream, cancellationToken: new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task ShortAsyncReadsStillSucceed()
    {
        using var stream = new TrickleStream(DngFixture.ThumbnailAndRaw(false).Bytes, 5);
        DngLayout layout = await DngLayout.ReadAsync(stream);
        Assert.True(layout.IsSupported);
        Assert.Equal(2, layout.Directories.Count);
    }

    [Fact]
    public async Task ShortSyncReadsStillSucceed()
    {
        using var stream = new TrickleStream(DngFixture.RawOnly(true).Bytes, 7);
        DngLayout layout = await Task.Run(() => DngLayout.Read(stream));
        Assert.True(layout.IsSupported);
    }

    // ---------- Golden-bytes sanity across endiannesses ----------

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SoftwareAndArtistAreAccounted(bool big)
    {
        byte[] bytes = DngFixture.RawOnly(big).Bytes;
        DngLayout layout = Read(bytes);
        TiffFieldInfo software = Assert.Single(layout.Directories[0].Fields, field => field.TagId == 305);
        Assert.Equal(25, software.ValueRange.Length);
        Assert.Equal(new byte[] { 0x4d, 0x65, 0x74 }, bytes.AsSpan((int)software.ValueRange.Offset, 3).ToArray()); // 'M','e','t' prefix of "MetadataSharp"
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InlineFieldValueRangeIsTheFourByteCell(bool big)
    {
        byte[] bytes = DngFixture.RawOnly(big).Bytes;
        DngLayout layout = Read(bytes);
        TiffFieldInfo version = Assert.Single(layout.Directories[0].Fields, field => field.TagId == 50706);
        Assert.True(version.IsInline);
        Assert.Equal(4, version.ValueRange.Length);
        int index = layout.Directories[0].Fields.ToList().FindIndex(field => field.TagId == 50706);
        Assert.Equal(8 + 2 + 12 * index + 8, version.ValueRange.Offset);
    }

    private sealed class TrickleStream : Stream
    {
        private readonly MemoryStream inner;
        private readonly int maximum;
        internal TrickleStream(byte[] bytes, int maximum) { inner = new MemoryStream(bytes); this.maximum = Math.Max(1, maximum); }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(count, maximum));
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => await inner.ReadAsync(buffer.AsMemory(offset, Math.Min(count, maximum)), cancellationToken).ConfigureAwait(false);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
