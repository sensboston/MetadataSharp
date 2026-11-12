using System.Security.Cryptography;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Sync;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Bmff;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Formats.Png;
using MetadataSharp.Formats.Tiff;
using MetadataSharp.Formats.WebP;
using MetadataSharp.Tests.Bmff;
using MetadataSharp.Tests.Iptc;
using MetadataSharp.Tests.Jpeg;
using MetadataSharp.Tests.Png;
using MetadataSharp.Tests.Tiff;
using MetadataSharp.Tests.WebP;
using Xunit;

namespace MetadataSharp.Tests.Sync;

/// <summary>The digest a file is read with, the reconciliation of a file, and the one save of its edits in each container.</summary>
public sealed class SyncSaveTests
{
    private const string Dc = "http://purl.org/dc/elements/1.1/", Photoshop = "http://ns.adobe.com/photoshop/1.0/";
    private static readonly ExifTagKey Description = new(ExifDirectoryKind.Ifd0, 0x010E), Artist = new(ExifDirectoryKind.Ifd0, 0x013B);

    private static byte[] IptcStream(Action<IptcEdit> change)
    {
        var edit = new IptcEdit();
        change(edit);
        return IptcCodecTests.Raw(IptcPacket.CreateEmpty().Apply(edit)!);
    }

    /// <summary>A JPEG file with an EXIF block that holds an orientation, an IPTC stream, and the digest given.</summary>
    private static byte[] JpegFile(byte[] stream, byte[]? digest)
    {
        byte[] resources = digest is null
            ? IptcCodecTests.Photoshop(IptcCodecTests.Block(0x0404, stream))
            : IptcCodecTests.Photoshop(IptcCodecTests.Block(0x0404, stream), IptcCodecTests.Block(0x0425, digest));
        return JpegFixture.Build([.. JpegFixture.Segment(0xE1, JpegFixture.Exif()), .. JpegFixture.Segment(0xED, resources)]);
    }

    private static XmpEdit Headline(string text)
    {
        var edit = new XmpEdit();
        edit.Set("Xmp.photoshop.Headline", text);
        return edit;
    }

    private static IptcEdit Caption(string text)
    {
        var edit = new IptcEdit();
        edit.SetCaption(text);
        return edit;
    }

    private static string? XmpText(ImageMetadata metadata, string uri, string name)
    {
        XmpEntry? entry = metadata.Xmp?.Entries.FirstOrDefault(e => e.NamespaceUri == uri && e.Path == name);
        if (entry is null) return null;
        return entry.Kind == XmpEntryKind.LangAlt ? entry.Languages.First(l => l.Language == "x-default").Value : string.Join("|", entry.Values);
    }

    private static string? IptcText(ImageMetadata metadata, byte dataset)
    {
        if (metadata.Iptc is not IptcPacket packet) return null;
        for (int i = 0; i < packet.Datasets.Count; i++)
            if (packet.Datasets[i].Key == new IptcDatasetKey(2, dataset)) return packet.GetText(i);
        return null;
    }

    private static string? ExifText(ImageMetadata metadata, ExifTagKey key) =>
        metadata.Exif is ExifMetadata exif && exif.TryGetEntry(key, out ExifEntry? entry) && entry.Value.TryGetString(out string? text) ? text : null;

    private static string Notes(SyncEdits edits) => string.Join("\n", edits.Notes.Select(n => $"{n.Source} -> {n.Target}: {n.Reason}"));

    /// <summary>The file is as reconciled as it can be: nothing is left to write.</summary>
    private static void AssertReconciled(byte[] file)
    {
        SyncEdits edits = ImageMetadata.Read(file).Reconcile();
        Assert.Equal(string.Empty, Notes(edits));
        Assert.True(edits.Exif.IsEmpty, "EXIF edits are left");
        Assert.True(edits.Iptc.IsEmpty, "IPTC edits are left");
        Assert.True(edits.Xmp.IsEmpty, "XMP edits are left");
    }

    [Fact]
    public void TheDigestOfAFileIsReadWithItsIptc()
    {
        byte[] stream = IptcStream(e => e.SetHeadline("Headline"));
        Assert.Equal(IptcDigestState.Current, ImageMetadata.Read(JpegFile(stream, MD5.HashData(stream))).Iptc!.Digest);
        Assert.Equal(IptcDigestState.Stale, ImageMetadata.Read(JpegFile(stream, new byte[16])).Iptc!.Digest);
        Assert.Equal(IptcDigestState.Absent, ImageMetadata.Read(JpegFile(stream, null)).Iptc!.Digest);
        // A digest that is not 16 bytes says nothing.
        Assert.Equal(IptcDigestState.Absent, ImageMetadata.Read(JpegFile(stream, new byte[4])).Iptc!.Digest);
        using JpegMetadataDocument document = JpegMetadataDocument.Open(new MemoryStream(JpegFile(stream, new byte[16])));
        Assert.Equal(IptcDigestState.Stale, document.ReadIptc()!.Digest);
        Assert.Equal(IptcDigestState.Absent, IptcPacket.Parse(stream).Digest);
    }

    [Fact]
    public void AStaleDigestMakesIptcWinAndOneSaveBringsTheFileIntoAgreement()
    {
        byte[] stream = IptcStream(e => e.SetCaption("From IPTC"));
        byte[] file = JpegFile(stream, new byte[16]);
        // XMP that says something else, written without touching IPTC or its digest.
        var other = new XmpEdit();
        other.SetLanguage("Xmp.dc.description", "x-default", "From XMP");
        other.Set("Xmp.photoshop.Headline", "Only XMP");
        using (JpegMetadataDocument document = JpegMetadataDocument.Open(new MemoryStream(file)))
        {
            using var output = new MemoryStream();
            document.SaveXmp(output, other);
            file = output.ToArray();
        }

        ImageMetadata metadata = ImageMetadata.Read(file);
        Assert.Equal(IptcDigestState.Stale, metadata.Iptc!.Digest);
        SyncEdits edits = metadata.Reconcile();
        Assert.Equal(string.Empty, Notes(edits));
        Assert.Contains(new SyncChoice("Xmp.dc.description", SyncSource.Iptc), edits.Choices);
        Assert.False(edits.Exif.IsEmpty);
        Assert.False(edits.Iptc.IsEmpty);
        Assert.False(edits.Xmp.IsEmpty);
        Assert.True(edits.IptcWriteOptions.UpdateDigest);

        byte[] saved;
        using (JpegMetadataDocument document = JpegMetadataDocument.Open(new MemoryStream(file)))
        {
            using var output = new MemoryStream();
            document.SaveAll(output, edits);
            saved = output.ToArray();
        }
        ImageMetadata result = ImageMetadata.Read(saved);
        Assert.Empty(result.Errors);
        Assert.Equal("From IPTC", XmpText(result, Dc, "description"));
        Assert.Equal("From IPTC", ExifText(result, Description));
        Assert.Equal("From IPTC", IptcText(result, 120));
        Assert.Equal("Only XMP", IptcText(result, 105));
        Assert.Equal("Only XMP", XmpText(result, Photoshop, "Headline"));
        Assert.Equal(IptcDigestState.Current, result.Iptc!.Digest);
        AssertReconciled(saved);

        // With the digest current, the same difference is read the other way: XMP is the newer one.
        using (JpegMetadataDocument document = JpegMetadataDocument.Open(new MemoryStream(saved)))
        {
            using var output = new MemoryStream();
            document.SaveXmp(output, Headline("Newer in XMP"));
            file = output.ToArray();
        }
        SyncEdits second = ImageMetadata.Read(file).Reconcile();
        Assert.Contains(new SyncChoice("Xmp.photoshop.Headline", SyncSource.Xmp), second.Choices);
        Assert.True(second.Xmp.IsEmpty);
        using (JpegMetadataDocument document = JpegMetadataDocument.Open(new MemoryStream(file)))
        {
            using var output = new MemoryStream();
            document.SaveAll(output, second);
            saved = output.ToArray();
        }
        result = ImageMetadata.Read(saved);
        Assert.Equal("Newer in XMP", IptcText(result, 105));
        Assert.Equal(IptcDigestState.Current, result.Iptc!.Digest);
        AssertReconciled(saved);
    }

    [Fact]
    public void EditsWithoutAChangeStillRecomputeAStaleDigestAndOtherwiseCopyTheFile()
    {
        byte[] stream = IptcStream(e => e.SetHeadline("Headline"));
        byte[] file = JpegFile(stream, new byte[16]);
        using (JpegMetadataDocument document = JpegMetadataDocument.Open(new MemoryStream(file)))
        {
            using var output = new MemoryStream();
            document.SaveXmp(output, Headline("Headline"));
            file = output.ToArray();
        }
        ImageMetadata metadata = ImageMetadata.Read(file);
        Assert.Equal(IptcDigestState.Stale, metadata.Iptc!.Digest);
        SyncEdits edits = metadata.Reconcile();
        Assert.True(edits.IsEmpty);

        byte[] saved;
        using (JpegMetadataDocument document = JpegMetadataDocument.Open(new MemoryStream(file)))
        {
            using var output = new MemoryStream();
            document.SaveAll(output, edits);
            saved = output.ToArray();
            // An IPTC save of its own does the same, and without the option the file is copied.
            using var alone = new MemoryStream();
            document.SaveIptc(alone, new IptcEdit(), new IptcWriteOptions { UpdateDigest = true });
            Assert.Equal(saved, alone.ToArray());
            using var copied = new MemoryStream();
            document.SaveIptc(copied, new IptcEdit());
            Assert.Equal(file, copied.ToArray());
        }
        Assert.Equal(file.Length, saved.Length);
        Assert.Equal(IptcDigestState.Current, ImageMetadata.Read(saved).Iptc!.Digest);

        // A current digest: the same call copies the file byte for byte.
        using (JpegMetadataDocument document = JpegMetadataDocument.Open(new MemoryStream(saved)))
        {
            using var output = new MemoryStream();
            document.SaveAll(output, ImageMetadata.Read(saved).Reconcile());
            Assert.Equal(saved, output.ToArray());
        }
    }

    [Fact]
    public void SaveAllAsWritesANewFileAndRefusesAnExistingOne()
    {
        byte[] file = JpegFile(IptcStream(e => e.SetCaption("From IPTC")), null);
        string directory = Path.Combine(Path.GetTempPath(), "metadatasharp-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string target = Path.Combine(directory, "saved.jpg");
            using (JpegMetadataDocument document = JpegMetadataDocument.Open(new MemoryStream(file)))
            {
                SyncEdits edits = ImageMetadata.Read(file).Reconcile();
                document.SaveAllAs(target, edits);
                Assert.Throws<IOException>(() => document.SaveAllAs(target, edits));
            }
            ImageMetadata result = ImageMetadata.Read(File.ReadAllBytes(target));
            Assert.Equal("From IPTC", XmpText(result, Dc, "description"));
            Assert.Equal("From IPTC", ExifText(result, Description));
            Assert.Equal(new[] { "saved.jpg" }, Directory.GetFiles(directory).Select(path => Path.GetFileName(path)!).ToArray());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void APngFileIsSavedByTwoPassesInOneCall()
    {
        byte[] file = PngFixture.Build();
        using (PngMetadataDocument document = PngMetadataDocument.Open(new MemoryStream(file)))
        {
            using var output = new MemoryStream();
            document.SaveIptc(output, Caption("From IPTC"));
            file = output.ToArray();
        }
        using (PngMetadataDocument document = PngMetadataDocument.Open(new MemoryStream(file)))
        {
            using var output = new MemoryStream();
            document.SaveXmp(output, Headline("Only XMP"));
            file = output.ToArray();
        }

        SyncEdits edits = ImageMetadata.Read(file).Reconcile();
        Assert.Equal(string.Empty, Notes(edits));
        Assert.True(edits.Exif.IsEmpty);
        Assert.False(edits.Iptc.IsEmpty);
        Assert.False(edits.Xmp.IsEmpty);
        byte[] saved;
        using (PngMetadataDocument document = PngMetadataDocument.Open(new MemoryStream(file)))
        {
            using var output = new MemoryStream();
            document.SaveAll(output, edits);
            saved = output.ToArray();
        }
        ImageMetadata result = ImageMetadata.Read(saved);
        Assert.Empty(result.Errors);
        Assert.Null(result.Exif);
        Assert.Equal("From IPTC", XmpText(result, Dc, "description"));
        Assert.Equal("Only XMP", IptcText(result, 105));
        AssertReconciled(saved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATiffFileIsSavedInOnePass(bool big)
    {
        byte[] file = TiffBasedFixture.Create("tiff", big).Bytes;
        using (TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(file)))
        {
            using var output = new MemoryStream();
            document.SaveIptc(output, Caption("From IPTC"));
            file = output.ToArray();
        }
        using (TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(file)))
        {
            using var output = new MemoryStream();
            document.SaveXmp(output, Headline("Only XMP"));
            file = output.ToArray();
        }

        SyncEdits edits = ImageMetadata.Read(file).Reconcile();
        Assert.Equal(string.Empty, Notes(edits));
        Assert.False(edits.Exif.IsEmpty);
        Assert.False(edits.Iptc.IsEmpty);
        Assert.False(edits.Xmp.IsEmpty);
        byte[] saved;
        using (TiffBasedMetadataDocument document = TiffBasedMetadataDocument.Open(new MemoryStream(file)))
        {
            using var output = new MemoryStream();
            document.SaveAll(output, edits);
            saved = output.ToArray();
        }
        ImageMetadata result = ImageMetadata.Read(saved);
        Assert.Empty(result.Errors);
        Assert.Equal("From IPTC", XmpText(result, Dc, "description"));
        Assert.Equal("From IPTC", ExifText(result, Description));
        Assert.Equal("Only XMP", IptcText(result, 105));
        AssertReconciled(saved);
    }

    [Fact]
    public void AWebPFileTakesExifAndXmpInOnePassAndNoIptc()
    {
        byte[] file = WebPFixture.Image(0);
        var artist = new ExifEdit();
        artist.Set(Artist, "Ana");
        using (WebPMetadataDocument document = WebPMetadataDocument.Open(new MemoryStream(file)))
        {
            using var output = new MemoryStream();
            document.SaveExif(output, artist);
            file = output.ToArray();
        }
        using (WebPMetadataDocument document = WebPMetadataDocument.Open(new MemoryStream(file)))
        {
            using var output = new MemoryStream();
            document.SaveXmp(output, Headline("Only XMP"));
            file = output.ToArray();
        }

        ImageMetadata metadata = ImageMetadata.Read(file);
        SyncEdits edits = metadata.Reconcile();
        Assert.Equal(string.Empty, Notes(edits));
        Assert.True(edits.Exif.IsEmpty);
        Assert.True(edits.Iptc.IsEmpty);
        byte[] saved;
        using (WebPMetadataDocument document = WebPMetadataDocument.Open(new MemoryStream(file)))
        {
            using var output = new MemoryStream();
            document.SaveAll(output, edits);
            saved = output.ToArray();
            // Asked to create IPTC, the edits hold a stream this container has no place for.
            SyncEdits withIptc = metadata.Reconcile(new ReconcileOptions { CreateIptc = true });
            Assert.False(withIptc.Iptc.IsEmpty);
            using var refused = new MemoryStream();
            MetadataException failure = Assert.Throws<MetadataException>(() => document.SaveAll(refused, withIptc));
            Assert.Equal(MetadataErrorCode.UnsupportedFeature, failure.Code);
            Assert.Equal(0L, refused.Length);
        }
        ImageMetadata result = ImageMetadata.Read(saved);
        Assert.Empty(result.Errors);
        Assert.Equal("Ana", XmpText(result, Dc, "creator"));
        Assert.Equal("Only XMP", XmpText(result, Photoshop, "Headline"));
        Assert.Equal("Ana", ExifText(result, Artist));
        AssertReconciled(saved);
    }

    [Fact]
    public void ABmffFileTakesExifAndXmpInOnePassAndNoIptc()
    {
        byte[] file = BmffFixture.Create("heic", false).Bytes;
        ImageMetadata metadata = ImageMetadata.Read(file);
        using BmffMetadataDocument document = BmffMetadataDocument.Open(new MemoryStream(file));
        using var output = new MemoryStream();
        document.SaveAll(output, metadata.Reconcile());
        ImageMetadata result = ImageMetadata.Read(output.ToArray());
        Assert.Empty(result.Errors);
        AssertReconciled(output.ToArray());

        SyncEdits withIptc = MetadataSync.XmpToIptc(XmpPacket.CreateEmpty().Apply(Headline("Headline"))!);
        Assert.False(withIptc.Iptc.IsEmpty);
        using var refused = new MemoryStream();
        MetadataException failure = Assert.Throws<MetadataException>(() => document.SaveAll(refused, withIptc));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, failure.Code);
        Assert.Equal(0L, refused.Length);
    }
}
