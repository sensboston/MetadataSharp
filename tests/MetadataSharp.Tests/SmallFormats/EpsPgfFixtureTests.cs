using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Core.Sync;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Eps;
using MetadataSharp.Formats.Pgf;
using Xunit;

namespace MetadataSharp.Tests.SmallFormats;

/// <summary>
/// The EPS and PGF files this project authors, read through the library. The bytes are the ones
/// <c>tools/reference/eps_pgf_fixtures.py</c> writes and CI regenerates, so a change to either side
/// that the other does not follow fails here rather than in the parity job.
/// </summary>
public sealed class EpsPgfFixtureTests
{
    /// <summary>A file of the authored fixture directory, by name.</summary>
    private static byte[] Fixture(string name)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MetadataSharp.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllBytes(Path.Combine(dir!.FullName, "tests", "fixtures", "b11eps", name));
    }

    [Theory]
    [InlineData("eps-xmp.eps")]
    [InlineData("eps-xmp-space.eps")]
    [InlineData("eps-xmp-31.eps")]
    [InlineData("eps-xmp-crlf.eps")]
    [InlineData("dos-eps-xmp.eps")]
    public void EveryAuthoredPacketIsRead(string name)
    {
        ImageMetadata metadata = ImageMetadata.Read(Fixture(name));
        Assert.Equal(MetadataFormat.Eps, metadata.Format);
        Assert.Empty(metadata.Errors);
        Assert.NotNull(metadata.Xmp);
        // The authored packet names its tool in xmp:CreatorTool, which is the one property here.
        Assert.Contains(metadata.Xmp!.Entries, entry => entry.Path == "CreatorTool");
        Assert.Null(metadata.Exif);
        Assert.Null(metadata.Iptc);
    }

    [Fact]
    public void AFileWithoutAPacketHasNoneAndIsStillRead()
    {
        ImageMetadata metadata = ImageMetadata.Read(Fixture("eps-plain.eps"));
        Assert.Equal(MetadataFormat.Eps, metadata.Format);
        Assert.Empty(metadata.Errors);
        Assert.Null(metadata.Xmp);
    }

    [Fact]
    public void AHeaderThatSaysThereIsNoPacketIsBelieved()
    {
        // The file holds a packet and the comment block says NoMain: the reference does not search,
        // and neither does this reader.
        ImageMetadata metadata = ImageMetadata.Read(Fixture("eps-xmp-nomain.eps"));
        Assert.Equal(MetadataFormat.Eps, metadata.Format);
        Assert.Empty(metadata.Errors);
        Assert.Null(metadata.Xmp);
    }

    [Fact]
    public void AReadOnlyTrailerIsRefusedUnderTheXmpKindAlone()
    {
        ImageMetadata metadata = ImageMetadata.Read(Fixture("eps-xmp-readonly.eps"));
        Assert.Equal(MetadataFormat.Eps, metadata.Format);
        Assert.Null(metadata.Xmp);
        MetadataReadError error = Assert.Single(metadata.Errors);
        Assert.Equal(MetadataFamily.Xmp, error.Family);
        Assert.Equal(MetadataErrorCode.InvalidEps, error.Code);
    }

    [Fact]
    public void EpsWriteRefusesReadOnlyPacketsBeforeReturningOutput()
    {
        var edit = new XmpEdit();
        edit.Set("Xmp.xmp.CreatorTool", "not safe to overwrite");
        MetadataException error = Assert.Throws<MetadataException>(() =>
            EpsMetadata.WriteXmp(Fixture("eps-xmp-readonly.eps"), edit));
        Assert.Equal(MetadataErrorCode.InvalidEps, error.Code);
    }

    [Fact]
    public void TheDosFramingPlacesBothSections()
    {
        byte[] file = Fixture("dos-eps-xmp.eps");
        EpsMetadata eps = EpsMetadata.Read(file);
        Assert.True(eps.IsDosEps);
        Assert.Equal(30, eps.PostScriptOffset);
        // The TIFF section begins where the PostScript section ends.
        Assert.Equal(30 + eps.PostScriptLength, eps.TiffOffset);
        Assert.True(eps.TiffLength > 0);
        Assert.Null(eps.WmfOffset);
    }

    [Fact]
    public void EpsWriteRefusesOverlappingDosSections()
    {
        byte[] source = Fixture("dos-eps-xmp.eps");
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(20), 31);
        var edit = new XmpEdit();
        edit.Set("Xmp.xmp.CreatorTool", "not safe to relocate");
        MetadataException error = Assert.Throws<MetadataException>(() => EpsMetadata.WriteXmp(source, edit));
        Assert.Equal(MetadataErrorCode.InvalidEps, error.Code);
    }

    [Theory]
    [InlineData("pgf-png.pgf", 320u, 211u)]
    [InlineData("pgf-colour-table.pgf", 64u, 48u)]
    public void EveryAuthoredPgfCarriesTheMetadataOfItsRegion(string name, uint width, uint height)
    {
        ImageMetadata metadata = ImageMetadata.Read(Fixture(name));
        Assert.Equal(MetadataFormat.Pgf, metadata.Format);
        Assert.Empty(metadata.Errors);
        Assert.NotNull(metadata.Exif);
        // The authored TIFF of the carrier holds the Orientation and the Artist the other fixtures of
        // this project pin, and it names no camera maker: the region gives what its own reader gives.
        Assert.Equal(ExifOrientation.TopLeft, metadata.Exif!.Orientation);
        Assert.True(metadata.Exif.TryGetEntry(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x013B), out ExifEntry? artist));
        Assert.NotNull(artist);
        Assert.NotNull(metadata.Xmp);
        // The authored packet names its tool in xmp:CreatorTool, which is the one entry here.
        Assert.Contains(metadata.Xmp!.Entries, entry => entry.Path == "CreatorTool");

        PgfMetadata pgf = PgfMetadata.Read(Fixture(name));
        Assert.Equal(width, pgf.Width);
        Assert.Equal(height, pgf.Height);
    }

    [Fact]
    public void EpsXmpWritesInsertReplaceAndRemoveWithReadback()
    {
        byte[] plain = Fixture("eps-plain.eps");
        var add = new XmpEdit();
        add.Set("Xmp.xmp.CreatorTool", "first");
        byte[] inserted = EpsMetadata.WriteXmp(plain, add);
        Assert.Contains(EpsMetadata.Read(inserted).Xmp!.Entries, entry => entry.Values.Contains("first"));

        var replace = new XmpEdit();
        replace.Set("Xmp.xmp.CreatorTool", "second");
        byte[] changed = EpsMetadata.WriteXmp(inserted, replace);
        Assert.Contains(EpsMetadata.Read(changed).Xmp!.Entries, entry => entry.Values.Contains("second"));

        var remove = new XmpEdit();
        remove.RemoveAllXmp();
        byte[] removed = EpsMetadata.WriteXmp(changed, remove);
        Assert.Null(EpsMetadata.Read(removed).Xmp);
        Assert.Equal("%ADO_ContainsXMP: NoMain", System.Text.Encoding.ASCII.GetString(removed).Split('\n').Single(line => line.StartsWith("%ADO_ContainsXMP:")));
    }

    [Fact]
    public void DosEpsXmpWriteKeepsThePreviewAndReframesItsOffset()
    {
        byte[] source = Fixture("dos-eps-xmp.eps");
        EpsMetadata before = EpsMetadata.Read(source);
        byte[] preview = source.AsSpan((int)before.TiffOffset!.Value, (int)before.TiffLength).ToArray();
        var edit = new XmpEdit();
        edit.Set("Xmp.xmp.CreatorTool", "a longer writer value");
        byte[] output = EpsMetadata.WriteXmp(source, edit);
        EpsMetadata after = EpsMetadata.Read(output);
        Assert.Equal((long)output.Length, after.TiffOffset!.Value + after.TiffLength);
        Assert.Equal(preview, output.AsSpan((int)after.TiffOffset.Value, (int)after.TiffLength).ToArray());
        Assert.Contains(after.Xmp!.Entries, entry => entry.Values.Contains("a longer writer value"));
    }

    [Fact]
    public void PgfWriteReusesPngWriterAndLeavesThePgfImageSuffixUntouched()
    {
        byte[] source = [.. Fixture("pgf-png.pgf"), 0xA5, 0x5A];
        PgfMetadata before = PgfMetadata.Read(source);
        byte[] suffix = source.AsSpan((int)before.ImageOffset).ToArray();
        var edits = new SyncEdits();
        edits.Xmp.Set("Xmp.xmp.CreatorTool", "PGF writer");
        byte[] output = PgfMetadata.Write(source, edits);
        PgfMetadata after = PgfMetadata.Read(output);
        Assert.Equal(before.Width, after.Width);
        Assert.Equal(before.Height, after.Height);
        Assert.Equal(suffix, output.AsSpan((int)after.ImageOffset).ToArray());
        Assert.Contains(after.Xmp!.Entries, entry => entry.Values.Contains("PGF writer"));

        byte[] commented = PgfMetadata.Write(output, new SyncEdits(), "inside PGF", replaceComment: true);
        Assert.Equal(new[] { "inside PGF" }, Comments.Read(commented));
    }

    [Fact]
    public void APgfHeaderRegionOfNoLengthHasNoMetadataAndIsNotARefusal()
    {
        ImageMetadata metadata = ImageMetadata.Read(Fixture("pgf-empty-region.pgf"));
        Assert.Equal(MetadataFormat.Pgf, metadata.Format);
        Assert.Empty(metadata.Errors);
        Assert.Null(metadata.Exif);
        Assert.Null(metadata.Xmp);

        PgfMetadata pgf = PgfMetadata.Read(Fixture("pgf-empty-region.pgf"));
        Assert.Equal(8u, pgf.Width);
        Assert.Equal(8u, pgf.Height);
    }
}
