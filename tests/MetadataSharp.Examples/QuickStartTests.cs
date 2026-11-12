using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats;
using MetadataSharp.Tests.Jpeg;
using MetadataSharp.Tests.Png;
using MetadataSharp.Tests.Tiff;
using Xunit;

namespace MetadataSharp.Examples;

/// <summary>
/// Runs every snippet of the README's "Quick start" on authored files and holds the README to the
/// compiled text, so the section cannot show code that does not build or does not do what it says.
/// </summary>
public sealed class QuickStartTests : IDisposable
{
    private static readonly ExifTagKey Model = new(ExifDirectoryKind.Ifd0, 0x0110);
    private static readonly ExifTagKey Orientation = new(ExifDirectoryKind.Ifd0, 0x0112);
    private static readonly ExifTagKey Software = new(ExifDirectoryKind.Ifd0, 0x0131);
    private static readonly ExifTagKey Artist = new(ExifDirectoryKind.Ifd0, 0x013B);
    private static readonly ExifTagKey Copyright = new(ExifDirectoryKind.Ifd0, 0x8298);
    private static readonly ExifTagKey ExifPointer = new(ExifDirectoryKind.Ifd0, 0x8769);
    private static readonly ExifTagKey ExposureTime = new(ExifDirectoryKind.Exif, 0x829A);
    private static readonly ExifTagKey DateTimeOriginal = new(ExifDirectoryKind.Exif, 0x9003);

    private readonly string directory = Directory.CreateTempSubdirectory("metadatasharp-examples-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void ReadAnyFileReturnsTheFormatAndTheExifOfAJpeg()
    {
        ImageMetadata image = QuickStart.ReadAnyFile(Write("photo.jpg", SampleJpeg()));

        Assert.Equal(MetadataFormat.Jpeg, image.Format);
        Assert.Empty(image.Errors);
        Assert.NotNull(image.Exif);
        Assert.Equal(new[] { Model, Orientation, Software, ExifPointer, ExposureTime }, image.Exif.Entries.Select(entry => entry.Key).ToArray());
    }

    [Fact]
    public void ReadAnyFileReportsADamagedBlockAndDoesNotThrow()
    {
        byte[] damaged = JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif([0x49, 0x49, 42, 0, 200, 0, 0, 0])));

        ImageMetadata image = QuickStart.ReadAnyFile(Write("damaged.jpg", damaged));

        Assert.Equal(MetadataFormat.Jpeg, image.Format);
        Assert.Null(image.Exif);
        MetadataReadError error = Assert.Single(image.Errors);
        Assert.Equal(MetadataFamily.Exif, error.Family);
        Assert.Equal(MetadataErrorCode.InvalidExif, error.Code);
    }

    [Fact]
    public void ReadTagsGivesTheRawTextAndTheInterpretedValues()
    {
        var (model, exposure, orientation) = QuickStart.ReadTags(Write("photo.jpg", SampleJpeg()));

        Assert.Equal("Example 100", model);
        Assert.Equal("1/250 s", exposure);
        Assert.Equal("right, top", orientation);
    }

    [Fact]
    public void EditExifWritesTheNamedTagsAndKeepsEverythingElse()
    {
        byte[] original = SampleJpeg();
        string source = Write("photo.jpg", original), destination = At("photo-edited.jpg");

        QuickStart.EditExif(source, destination);

        Assert.Equal(original, File.ReadAllBytes(source));
        ExifMetadata exif = ImageMetadata.Read(destination).Exif!;
        Assert.Equal("Jane Doe", Text(exif, Artist));
        Assert.Equal("(c) 2026 Jane Doe", Text(exif, Copyright));
        Assert.Equal("2026:11:11 09:30:00", Text(exif, DateTimeOriginal));
        Assert.False(exif.TryGetEntry(Software, out _));
        Assert.Equal("Example 100", Text(exif, Model));
        Assert.Equal("1/250 s", ExifTags.Interpret(exif, ExposureTime));
        // Everything from the first table segment on is the source's, byte for byte.
        byte[] edited = File.ReadAllBytes(destination);
        int tail = original.Length - original.AsSpan().IndexOf(new byte[] { 0xFF, 0xDB });
        Assert.Equal(original[^tail..], edited[^tail..]);
    }

    [Fact]
    public void EditXmpWritesAPacketIntoAPngThatHadNone()
    {
        string source = Write("picture.png", PngFixture.Build()), destination = At("picture-edited.png");

        QuickStart.EditXmp(source, destination);

        ImageMetadata image = ImageMetadata.Read(destination);
        Assert.Equal(MetadataFormat.Png, image.Format);
        Assert.Empty(image.Errors);
        var properties = image.Xmp!.Properties;
        Assert.Equal("Harbour at dawn", properties[new XmpPropertyName("http://purl.org/dc/elements/1.1/", "title")].Languages["x-default"]);
        Assert.Equal(new[] { "harbour", "dawn" }, properties[new XmpPropertyName("http://purl.org/dc/elements/1.1/", "subject")].Items);
        Assert.Equal("4", properties[new XmpPropertyName("http://ns.adobe.com/xap/1.0/", "Rating")].Text);
    }

    [Fact]
    public void EditIptcWritesTheNamedDatasets()
    {
        string source = Write("photo.jpg", SampleJpeg()), destination = At("photo-edited.jpg");

        QuickStart.EditIptc(source, destination);

        IptcPacket iptc = ImageMetadata.Read(destination).Iptc!;
        var texts = Enumerable.Range(0, iptc.Datasets.Count)
            .Where(index => iptc.Datasets[index].Key.Record == 2 && iptc.Datasets[index].Key.Dataset != 0)
            .Select(index => (iptc.Datasets[index].Key.Dataset, iptc.GetText(index))).OrderBy(pair => pair.Dataset).ToArray();
        Assert.Equal(new[] { ((byte)25, "harbour"), ((byte)25, "dawn"), ((byte)120, "Fishing boats leaving the harbour.") }, texts);
        Assert.NotNull(ImageMetadata.Read(destination).Exif);
    }

    [Fact]
    public void ReadXmpAndIptcListsWhatTheEditsWrote()
    {
        string png = At("picture-edited.png"), jpeg = At("photo-edited.jpg");
        QuickStart.EditXmp(Write("picture.png", PngFixture.Build()), png);
        QuickStart.EditIptc(Write("photo.jpg", SampleJpeg()), jpeg);

        List<string> xmp = QuickStart.ReadXmpAndIptc(png), iptc = QuickStart.ReadXmpAndIptc(jpeg);

        Assert.Contains("xmp:Rating = 4", xmp);
        Assert.Contains("dc:subject = harbour; dawn", xmp);
        Assert.Contains("2:25 Keywords = harbour", iptc);
        Assert.Contains("2:120 Caption = Fishing boats leaving the harbour.", iptc);
    }

    [Fact]
    public void EditRawFileSetsTheOrientationAndLeavesImageDataAndMakerNoteWhereTheyWere()
    {
        TiffBasedFixture raw = TiffBasedFixture.Create("nef", big: false);
        string source = Write("shot.nef", raw.Bytes), destination = At("shot-edited.nef");

        QuickStart.EditRawFile(source, destination);

        byte[] edited = File.ReadAllBytes(destination);
        ImageMetadata image = ImageMetadata.Read(edited);
        Assert.Equal(MetadataFormat.Tiff, image.Format);
        Assert.Empty(image.Errors);
        Assert.Equal("right, top", ExifTags.Interpret(image.Exif!, Orientation));
        Assert.NotNull(image.Exif!.MakerNote);
        foreach ((int start, int length) in raw.Image.Append(raw.Note))
            Assert.Equal(raw.Bytes[start..(start + length)], edited[start..(start + length)]);
    }

    [Fact]
    public async Task ReadUntrustedAsyncReadsWithinTheBudgets()
    {
        using var stream = new MemoryStream(SampleJpeg());

        ImageMetadata image = await QuickStart.ReadUntrustedAsync(stream, CancellationToken.None);

        Assert.Empty(image.Errors);
        Assert.Equal("Example 100", Text(image.Exif!, Model));
    }

    [Fact]
    public async Task ReadUntrustedAsyncListsABlockOverABudgetAndThrowsForAFileOverTheInputBudget()
    {
        using var stream = new MemoryStream(JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif(CrowdedTiff()))));
        ImageMetadata image = await QuickStart.ReadUntrustedAsync(stream, CancellationToken.None);
        Assert.Equal(MetadataFormat.Jpeg, image.Format);
        Assert.Null(image.Exif);
        MetadataReadError error = Assert.Single(image.Errors);
        Assert.Equal((MetadataFamily.Exif, MetadataErrorCode.LimitExceeded), (error.Family, error.Code));

        byte[] oversized = new byte[32 * 1024 * 1024 + 1];
        SampleJpeg().CopyTo(oversized, 0);
        using var large = new MemoryStream(oversized);
        MetadataException refused = await Assert.ThrowsAsync<MetadataException>(() => QuickStart.ReadUntrustedAsync(large, CancellationToken.None));
        Assert.Equal(MetadataErrorCode.LimitExceeded, refused.Code);
    }

    [Fact]
    public void RefusedEditNamesTheReasonAndLeavesNoFile()
    {
        // A private chunk marked unsafe to copy: it may depend on data the edit would change.
        byte[] png = PngFixture.Build([.. PngFixture.Chunk("eXIf", JpegFixture.OrientationTiff), .. PngFixture.Chunk("vpAG", [1])]);
        string source = Write("picture.png", png), destination = At("picture-edited.png");

        Assert.Equal(MetadataErrorCode.UnsupportedFeature, QuickStart.RefusedEdit(source, destination));
        Assert.False(File.Exists(destination));

        // The same edit of a file without that chunk is carried out.
        string plain = Write("plain.png", PngFixture.Build(PngFixture.Chunk("eXIf", JpegFixture.OrientationTiff)));
        Assert.Null(QuickStart.RefusedEdit(plain, At("plain-edited.png")));
        Assert.Null(ImageMetadata.Read(At("plain-edited.png")).Exif);
    }

    [Fact]
    public void ReadmeShowsTheCompiledSnippets()
    {
        string root = RepositoryRoot();
        string[] compiled = Regions(File.ReadAllText(Path.Combine(root, "tests", "MetadataSharp.Examples", "QuickStart.cs")));
        string[] shown = CodeBlocks(File.ReadAllText(Path.Combine(root, "README.md")));

        Assert.Equal(10, compiled.Length);
        Assert.Equal(compiled, shown);
    }

    /// <summary>The bodies of the regions of a source file, in order, without their common indent.</summary>
    private static string[] Regions(string source)
    {
        var regions = new List<string>();
        List<string>? open = null;
        foreach (string line in Lines(source))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("#region", StringComparison.Ordinal)) open = [];
            else if (trimmed == "#endregion") { regions.Add(Unindented(open!)); open = null; }
            else open?.Add(line);
        }
        return regions.ToArray();
    }

    /// <summary>The C# code blocks of the README's "Quick start" section, in order.</summary>
    private static string[] CodeBlocks(string readme)
    {
        var blocks = new List<string>();
        List<string>? open = null;
        bool section = false;
        foreach (string line in Lines(readme))
        {
            if (open is null && line.StartsWith("## ", StringComparison.Ordinal)) section = line == "## Quick start";
            else if (!section) continue;
            else if (open is null && line == "```csharp") open = [];
            else if (open is not null && line == "```") { blocks.Add(Unindented(open)); open = null; }
            else open?.Add(line);
        }
        return blocks.ToArray();
    }

    private static string[] Lines(string text) => text.Replace("\r\n", "\n").Split('\n');

    private static string Unindented(List<string> lines)
    {
        int indent = lines.Where(line => line.Trim().Length != 0).Min(line => line.Length - line.TrimStart().Length);
        return string.Join("\n", lines.Select(line => line.Trim().Length == 0 ? "" : line[indent..].TrimEnd()));
    }

    private static string RepositoryRoot()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
            if (File.Exists(Path.Combine(at.FullName, "MetadataSharp.sln"))) return at.FullName;
        throw new InvalidOperationException("MetadataSharp.sln was not found above the test output directory.");
    }

    private string At(string name) => Path.Combine(directory, name);

    private string Write(string name, byte[] bytes)
    {
        File.WriteAllBytes(At(name), bytes);
        return At(name);
    }

    private static string Text(ExifMetadata exif, ExifTagKey key)
    {
        Assert.True(exif.TryGetEntry(key, out ExifEntry? entry));
        byte[] raw = new byte[entry.Value.ByteLength];
        entry.Value.CopyRawTo(raw);
        return Encoding.ASCII.GetString(raw).TrimEnd('\0');
    }

    /// <summary>An EXIF block whose one directory has 600 entries: more than a directory may have under the limits used here.</summary>
    private static byte[] CrowdedTiff()
    {
        byte[] tiff = new byte[8 + 2 + 600 * 12 + 4];
        "II"u8.CopyTo(tiff);
        tiff[2] = 42; tiff[4] = 8;
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(8), 600);
        for (int entry = 0; entry < 600; entry++)
        {
            Span<byte> field = tiff.AsSpan(10 + entry * 12, 12);
            BinaryPrimitives.WriteUInt16LittleEndian(field, (ushort)(0xC000 + entry));
            field[2] = 1; field[4] = 1;                                // BYTE, count 1, value 0
        }
        return tiff;
    }

    /// <summary>The authored JPEG of the main suite with an EXIF block written out below.</summary>
    private static byte[] SampleJpeg() => JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif(SampleTiff())));

    /// <summary>
    /// A little-endian EXIF block: Model, Orientation, Software and the Exif pointer in the first
    /// directory, ExposureTime in the Exif directory. Every offset is written out by hand so that
    /// the examples start from bytes this library did not produce.
    /// </summary>
    private static byte[] SampleTiff()
    {
        byte[] tiff = new byte[112];
        "II"u8.CopyTo(tiff);
        U16(2, 42); U32(4, 8);
        U16(8, 4);                                                   // first directory: four entries
        Entry(10, 0x0110, 2, 12, 62);                                // Model, ASCII, at 62
        Entry(22, 0x0112, 3, 1, 6);                                  // Orientation: right, top
        Entry(34, 0x0131, 2, 12, 74);                                // Software, ASCII, at 74
        Entry(46, 0x8769, 4, 1, 86);                                 // Exif directory at 86
        "Example 100\0"u8.CopyTo(tiff.AsSpan(62));
        "Firmware 1 \0"u8.CopyTo(tiff.AsSpan(74));
        U16(86, 1);                                                  // Exif directory: one entry
        Entry(88, 0x829A, 5, 1, 104);                                // ExposureTime, RATIONAL, at 104
        U32(104, 1); U32(108, 250);
        return tiff;

        void U16(int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(at), value);
        void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(at), value);
        void Entry(int at, ushort tag, ushort type, uint count, uint value)
        {
            U16(at, tag); U16(at + 2, type); U32(at + 4, count);
            if (type == 3) U16(at + 8, (ushort)value); else U32(at + 8, value);
        }
    }
}
