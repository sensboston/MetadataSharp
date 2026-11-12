using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
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
    public void ReadTagsGivesTheTypedValuesOfACameraFile()
    {
        QuickStart.TagsRead read = QuickStart.ReadTags(Write("camera.jpg", CameraJpeg()));

        Assert.Equal("Example 100", read.Camera);
        Assert.Equal(new DateTime(2026, 5, 17, 6, 12, 40), read.Taken);
        Assert.Equal(new DateTimeOffset(2026, 5, 17, 6, 12, 40, TimeSpan.FromHours(2)), read.Instant);
        Assert.Equal(new UnsignedRational(1, 250), read.Exposure);
        Assert.Equal(2.8, read.Aperture);
        Assert.Equal(400, read.Iso);
        Assert.Equal(ExifOrientation.RightTop, read.Orientation);
        Assert.Equal(new GpsPosition(54.3525, 18.66, null, null), read.Position);
        Assert.Equal("Firmware 1.2", read.Software);
        Assert.Equal("1/250 s", read.Printed);
    }

    [Fact]
    public void ReadTagsAnswersNullForWhatAFileDoesNotHave()
    {
        // The small sample: a model, an orientation, a software name and an exposure time, nothing else.
        QuickStart.TagsRead read = QuickStart.ReadTags(Write("photo.jpg", SampleJpeg()));
        Assert.Equal(
            new QuickStart.TagsRead("Example 100", null, null, new UnsignedRational(1, 250), null, null, ExifOrientation.RightTop, null, "Firmware 1 ", "1/250 s"),
            read);

        // A file without an EXIF block.
        read = QuickStart.ReadTags(Write("picture.png", PngFixture.Build()));
        Assert.Equal(new QuickStart.TagsRead(null, null, null, null, null, null, null, null, null, null), read);
    }

    [Fact]
    public void ReadTagsFallsBackToTheLaterDatesOfAnEditedFile()
    {
        // No DateTimeOriginal: the date of the last change stands in, with its own offset.
        byte[] tiff = Tiff(
            [(0x0132, 2, Ascii("2026:06:01 20:15:00"))],
            [(0x9010, 2, Ascii("-05:00"))],
            []);
        QuickStart.TagsRead read = QuickStart.ReadTags(Write("edited.jpg", JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif(tiff)))));

        Assert.Equal(new DateTime(2026, 6, 1, 20, 15, 0), read.Taken);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 20, 15, 0, TimeSpan.FromHours(-5)), read.Instant);
        Assert.Null(ImageMetadata.Read(At("edited.jpg")).Exif!.DateTimeOriginal);
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
        Assert.Equal("top, left", ExifTags.Interpret(exif, Orientation));
        Assert.True(exif.TryGetEntry(new ExifTagKey(ExifDirectoryKind.Gps, 0x0006), out ExifEntry? altitude));
        Assert.Equal(TiffFieldType.Rational, altitude.Value.FieldType);
        byte[] rational = new byte[8];
        altitude.Value.CopyRawTo(rational);
        Assert.Equal(1255u, BitConverter.ToUInt32(rational, 0)); // the block of the sample is little-endian
        Assert.Equal(10u, BitConverter.ToUInt32(rational, 4));
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

        // The properties first, in the order the snippet asks for them.
        Assert.Equal(new[] { "title: Harbour at dawn", "creators: ", "subjects: harbour, dawn", "rating: 4" }, xmp.Take(4).ToArray());
        Assert.Contains("xmp:Rating = 4", xmp);
        Assert.Contains("dc:subject = harbour; dawn", xmp);
        Assert.Contains("dc:title = Harbour at dawn", xmp);
        Assert.Equal(7, xmp.Count);

        Assert.Equal(new[] { "headline: ", "caption: Fishing boats leaving the harbour.", "keywords: harbour, dawn" }, iptc.Take(3).ToArray());
        Assert.Contains("2:25 Keywords = harbour", iptc);
        Assert.Contains("2:25 Keywords = dawn", iptc);
        Assert.Contains("2:120 Caption = Fishing boats leaving the harbour.", iptc);
    }

    [Fact]
    public void ReadXmpAndIptcOfAFileWithNeitherListsNothing()
    {
        Assert.Empty(QuickStart.ReadXmpAndIptc(Write("photo.jpg", SampleJpeg())));
        Assert.Empty(QuickStart.ReadXmpAndIptc(Write("picture.png", PngFixture.Build())));
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

    [Fact]
    public void ReadmeLinksAreAbsoluteAndLeadIntoTheRepository()
    {
        // The README is packed into the NuGet packages, where a relative link leads nowhere. Every
        // link has to start with the one property that names the repository, so that a move of
        // the repository is that property and the link block of the README, and this test says so.
        string root = RepositoryRoot();
        Match property = Regex.Match(File.ReadAllText(Path.Combine(root, "Directory.Build.props")),
            "<MetadataSharpRepositoryUrl[^>]*>([^<]+)</MetadataSharpRepositoryUrl>");
        Assert.True(property.Success, "Directory.Build.props does not define MetadataSharpRepositoryUrl");
        string repository = property.Groups[1].Value.Trim();
        Assert.StartsWith("https://", repository);
        Assert.False(repository.EndsWith('/'));

        // Code is not prose: fenced blocks and code spans are taken out before links are looked for.
        string prose = Regex.Replace(File.ReadAllText(Path.Combine(root, "README.md")).Replace("\r\n", "\n"), "(?ms)^```.*?^```[^\n]*$", "");
        prose = Regex.Replace(prose, "`[^`\n]*`", "");

        var definitions = Regex.Matches(prose, @"(?m)^\[([^\]\n]+)\]:[ \t]*(\S+)[ \t]*$").ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value);
        string[] inline = Regex.Matches(prose, @"\]\(([^)\s]*)\)").Select(match => match.Groups[1].Value).ToArray();
        string[] referenced = Regex.Matches(prose, @"\]\[([^\]\n]+)\]").Select(match => match.Groups[1].Value).Distinct().ToArray();

        Assert.NotEmpty(definitions);
        // A label that is used is defined, and the other way round: no dead reference, no stray definition.
        Assert.Equal(definitions.Keys.Order(StringComparer.Ordinal).ToArray(), referenced.Order(StringComparer.Ordinal).ToArray());

        foreach (string target in inline.Concat(definitions.Values))
        {
            string file = repository + "/blob/HEAD/", directory = repository + "/tree/HEAD/";
            if (target.StartsWith(file, StringComparison.Ordinal))
                Assert.True(File.Exists(Path.Combine(root, target[file.Length..])), $"{target}: no such file in the repository");
            else if (target.StartsWith(directory, StringComparison.Ordinal))
                Assert.True(Directory.Exists(Path.Combine(root, target[directory.Length..])), $"{target}: no such directory in the repository");
            else
                Assert.Fail($"{target}: a README link starts with {file} or {directory}");
        }
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
        Assert.True(entry.Value.TryGetString(out string? text));
        return text;
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

    /// <summary>The authored JPEG of the main suite with the EXIF block of a camera: the tags the typed properties read.</summary>
    private static byte[] CameraJpeg() => JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif(Tiff(
        [
            (0x010F, 2, Ascii("Example")), (0x0110, 2, Ascii("Example 100")), (0x0112, 3, [6, 0]),      // Make, Model, Orientation
            (0x0131, 2, Ascii("Firmware 1.2")),                                                          // Software
        ],
        [
            (0x829A, 5, Longs(1, 250)), (0x829D, 5, Longs(28, 10)), (0x8827, 3, [0x90, 0x01]),           // 1/250 s, F2.8, ISO 400
            (0x9003, 2, Ascii("2026:05:17 06:12:40")), (0x9011, 2, Ascii("+02:00")),                     // DateTimeOriginal and its offset
            (0x920A, 5, Longs(50, 1)),                                                                   // 50 mm
        ],
        [
            (0x0001, 2, Ascii("N")), (0x0002, 5, Longs(54, 1, 21, 1, 9, 1)),                             // 54 deg 21' 9" N
            (0x0003, 2, Ascii("E")), (0x0004, 5, Longs(18, 1, 39, 1, 36, 1)),                            // 18 deg 39' 36" E
        ]))));

    private static byte[] Ascii(string text) => [.. Encoding.ASCII.GetBytes(text), 0];

    private static byte[] Longs(params uint[] numbers)
    {
        byte[] bytes = new byte[numbers.Length * 4];
        for (int i = 0; i < numbers.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), numbers[i]);
        return bytes;
    }

    /// <summary>
    /// A little-endian EXIF block laid out here, not by the library: the first directory, which is
    /// given the pointers to the other two, then the Exif and the GPS directory, then the values
    /// that do not fit the four bytes of an entry. Entries are (tag, field type, value bytes) in
    /// ascending tag order; the count follows from the type.
    /// </summary>
    private static byte[] Tiff(params (ushort Tag, ushort Type, byte[] Value)[][] directories)
    {
        Assert.Equal(3, directories.Length);
        int[] starts = new int[3];
        int at = 8;
        for (int d = 0; d < 3; d++)
        {
            starts[d] = at;
            at += 2 + ((directories[d].Length + (d == 0 ? 2 : 0)) * 12) + 4;
        }
        directories[0] = [.. directories[0], (0x8769, 4, Longs((uint)starts[1])), (0x8825, 4, Longs((uint)starts[2]))];

        var head = new List<byte> { 0x49, 0x49, 42, 0, 8, 0, 0, 0 };
        var values = new List<byte>();
        void U16(int number) => head.AddRange([(byte)number, (byte)(number >> 8)]);
        void U32(int number) => head.AddRange([(byte)number, (byte)(number >> 8), (byte)(number >> 16), (byte)(number >> 24)]);
        foreach (var directory in directories)
        {
            U16(directory.Length);
            foreach ((ushort tag, ushort type, byte[] value) in directory)
            {
                int unit = type switch { 2 => 1, 3 => 2, 5 => 8, _ => 4 };    // ASCII, SHORT, RATIONAL, LONG
                U16(tag);
                U16(type);
                U32(value.Length / unit);
                if (value.Length <= 4)
                {
                    head.AddRange(value);
                    head.AddRange(new byte[4 - value.Length]);
                }
                else
                {
                    U32(at + values.Count);
                    values.AddRange(value);
                    if (values.Count % 2 != 0) values.Add(0);                 // a value starts on an even offset
                }
            }
            U32(0);                                                           // no next directory
        }
        Assert.Equal(at, head.Count);
        return [.. head, .. values];
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
