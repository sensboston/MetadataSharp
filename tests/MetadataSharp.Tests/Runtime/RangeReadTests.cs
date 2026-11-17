using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Formats;
using Xunit;

namespace MetadataSharp.Tests.Runtime;

/// <summary>
/// Reading costs the ranges a snapshot is made of and not the file. Every fixture of the repository is
/// read three ways — from an array, from a path and from a seekable stream — and the three snapshots
/// have to agree in everything they hold. A file of 24 MB whose metadata is in its first kilobyte has
/// to cost kilobytes, a pointer that leaves the file has to be refused exactly as it is for an array,
/// and a stream that cannot seek has to follow the documented rule.
/// </summary>
public sealed class RangeReadTests
{
    // ---- the equivalence gate ----

    /// <summary>Every fixture of the repository, which is what every format it can read has one of.</summary>
    /// <returns>The paths of the files, relative to the root of the repository.</returns>
    public static TheoryData<string> Corpus()
    {
        var data = new TheoryData<string>();
        string root = RepositoryRoot();
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "tests", "fixtures"), "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            data.Add(Path.GetRelativePath(root, path));
        return data;
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void A_file_reads_the_same_from_an_array_a_path_and_a_stream(string relative)
    {
        string path = Path.Combine(RepositoryRoot(), relative);
        byte[] bytes = File.ReadAllBytes(path);

        ImageMetadata fromArray = ImageMetadata.Read(bytes);
        ImageMetadata fromMemory = ImageMetadata.Read(new CountingStream(new MemoryStream(bytes, writable: false)));
        ImageMetadata fromPath = ImageMetadata.Read(path);

        AssertSame(fromArray, fromMemory, relative + " read from a memory stream");
        AssertSame(fromArray, fromPath, relative + " read from its path");
    }

    // ---- the byte-count gate ----

    /// <summary>
    /// A TIFF of 24 MB with two tags in its primary directory and its values beside it: the read must
    /// reach the directory and the values and stop, so it may not cost more than a share of the file
    /// and not more than a stated number of kilobytes for a file this size.
    /// </summary>
    [Fact]
    public void A_file_of_24_megabytes_is_read_by_the_kilobyte()
    {
        byte[] file = SparseTiff(24 * 1024 * 1024);
        var stream = new CountingStream(new MemoryStream(file, writable: false));

        ImageMetadata metadata = ImageMetadata.Read(stream);

        Assert.Equal(MetadataFormat.Tiff, metadata.Format);
        Assert.NotNull(metadata.Exif);
        Assert.True(stream.BytesRead <= 512 * 1024,
            $"{stream.BytesRead.ToString(CultureInfo.InvariantCulture)} bytes were read of a file whose metadata is in its first kilobyte.");
        Assert.True(stream.BytesRead <= file.Length / 8,
            $"{stream.BytesRead.ToString(CultureInfo.InvariantCulture)} bytes were read of {file.Length.ToString(CultureInfo.InvariantCulture)}.");
        // The same file as an array answers the same, which is what says the window is not the answer.
        AssertSame(ImageMetadata.Read(file), metadata);
    }

    /// <summary>Metadata reads stop at JPEG SOS and skip PNG/WebP image payloads.</summary>
    [Theory]
    [InlineData("jpeg")]
    [InlineData("png")]
    [InlineData("webp")]
    public void Large_image_payloads_are_not_scanned(string kind)
    {
        const int imageBytes = 4 * 1024 * 1024;
        byte[] file = kind switch
        {
            "jpeg" => Jpeg.JpegFixture.Build(prefix: Jpeg.JpegFixture.Segment(0xFE, "before"u8.ToArray()), entropy: new byte[imageBytes]),
            "png" => [137, 80, 78, 71, 13, 10, 26, 10, .. Png.PngFixture.Chunk("IHDR", Png.PngFixture.Header()),
                .. Png.PngFixture.Chunk("IDAT", new byte[imageBytes]),
                .. Png.PngFixture.Chunk("tEXt", "Comment\0after"u8.ToArray()), .. Png.PngFixture.Chunk("IEND", [])],
            _ => WebP.WebPFixture.Riff(WebP.WebPFixture.Chunk("VP8L", [0x2f, 0, 0, 0, 0, .. new byte[imageBytes]]))
        };
        using var stream = new CountingStream(new MemoryStream(file, writable: false));
        ImageMetadata result = ImageMetadata.Read(stream);
        AssertSame(ImageMetadata.Read(file), result);
        Assert.True(stream.BytesRead <= 64 * 1024, $"{kind}: {stream.BytesRead} bytes read");
        Assert.False(stream.RepeatedRead, $"{kind}: underlying bytes were read more than once");
        if (kind == "jpeg") Assert.Contains("before", result.Comments);
        if (kind == "png") Assert.Contains("after", result.Comments);
    }

    /// <summary>Large metadata crossing many cache blocks is never fetched twice.</summary>
    [Fact]
    public void Png_large_metadata_reads_each_underlying_range_only_once()
    {
        byte[] text = new byte[200_000];
        "Other\0"u8.CopyTo(text);
        byte[] file = Png.PngFixture.Build(before: Png.PngFixture.Chunk("tEXt", text));
        using var stream = new CountingStream(new MemoryStream(file, writable: false));
        ImageMetadata actual = ImageMetadata.Read(stream);
        AssertSame(ImageMetadata.Read(file), actual);
        Assert.False(stream.RepeatedRead);
        Assert.True(stream.BytesRead <= file.Length);
    }

    /// <summary>IDAT CRC is not checked by read-only snapshots; metadata CRC still is.</summary>
    [Fact]
    public void Png_checks_metadata_crc_but_not_image_crc_when_reading()
    {
        byte[] file = Png.PngFixture.Build(after: Png.PngFixture.Chunk("tEXt", "Comment\0after"u8.ToArray()));
        int idat = Encoding.ASCII.GetString(file).IndexOf("IDAT", StringComparison.Ordinal);
        int text = Encoding.ASCII.GetString(file).IndexOf("tEXt", StringComparison.Ordinal);
        file[idat + 4] ^= 1;
        Assert.Contains("after", ImageMetadata.Read(file).Comments);
        using (var source = new MemoryStream(file))
            Assert.Throws<MetadataException>(() => MetadataSharp.Formats.Png.PngMetadataDocument.Open(source));
        file[text + 4] ^= 1;
        Assert.NotEmpty(ImageMetadata.Read(file).Errors);
    }

    // ---- hostile input ----

    /// <summary>A value that points past the end of its file answers as it does for an array, and no read leaves the file.</summary>
    [Fact]
    public void A_value_outside_the_file_answers_as_it_does_for_an_array()
    {
        byte[] file = SparseTiff(1024);
        // Tag 0x013B (Artist) holds an offset past the end of the file.
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(8 + 2 + 12 + 8, 4), 0x7FFFFFF0);

        AssertSameOutcome(file);
    }

    /// <summary>A directory that points at itself answers as it does for an array, and the walk stops.</summary>
    [Fact]
    public void A_cyclic_directory_answers_as_it_does_for_an_array()
    {
        byte[] file = SparseTiff(4096);
        // The primary directory sits at 8; its next pointer leads back to it.
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(8 + 2 + 2 * 12, 4), 8);

        AssertSameOutcome(file);
    }

    /// <summary>
    /// Reads a hostile file from an array and from a counted stream: both have to end the same way —
    /// the same metadata, or the same refusal — and the stream must not cost more than the file.
    /// </summary>
    /// <param name="file">The file, already malformed.</param>
    private static void AssertSameOutcome(byte[] file)
    {
        ImageMetadata? expected = null;
        MetadataException? refusal = null;
        try { expected = ImageMetadata.Read(file); }
        catch (MetadataException malformed) { refusal = malformed; }

        var stream = new CountingStream(new MemoryStream(file, writable: false));
        if (refusal is null)
        {
            AssertSame(expected!, ImageMetadata.Read(stream));
        }
        else
        {
            MetadataException? fromStream = Record.Exception(() => ImageMetadata.Read(stream)) as MetadataException;
            Assert.NotNull(fromStream);
            Assert.Equal(refusal.Code, fromStream!.Code);
            Assert.Equal(refusal.Message, fromStream.Message);
            Assert.Equal(refusal.SourceOffset, fromStream.SourceOffset);
        }
        Assert.True(stream.BytesRead <= file.Length + 4096,
            $"{stream.BytesRead.ToString(CultureInfo.InvariantCulture)} bytes were read of a file of {file.Length.ToString(CultureInfo.InvariantCulture)}.");
    }

    // ---- a stream that cannot seek ----

    /// <summary>A stream that cannot seek is buffered, within the budget, and answers as a file does.</summary>
    [Fact]
    public void A_stream_that_cannot_seek_answers_as_a_file_does()
    {
        string path = Path.Combine(RepositoryRoot(), "tests", "fixtures", "b9", "owned-marker-nef.nef");
        byte[] bytes = File.ReadAllBytes(path);

        ImageMetadata fromArray = ImageMetadata.Read(bytes);
        ImageMetadata fromPipe = ImageMetadata.Read(new CountingStream(new MemoryStream(bytes, writable: false), canSeek: false));

        AssertSame(fromArray, fromPipe);
    }

    /// <summary>A stream that cannot seek, and holds more than the budget, is refused, and nothing past the budget is read.</summary>
    [Fact]
    public void A_stream_that_cannot_seek_is_stopped_at_the_input_budget()
    {
        var stream = new CountingStream(new MemoryStream(new byte[64 * 1024], writable: false), canSeek: false);
        var limits = new MetadataLimits { MaxInputBytes = 8 * 1024 };

        MetadataException failure = Assert.Throws<MetadataException>(() => ImageMetadata.Read(stream, limits));

        Assert.Equal(MetadataErrorCode.LimitExceeded, failure.Code);
        Assert.True(stream.BytesRead <= 8 * 1024 + 1,
            $"{stream.BytesRead.ToString(CultureInfo.InvariantCulture)} bytes were read under a budget of 8192.");
    }

    // ---- builders ----

    /// <summary>
    /// A little-endian TIFF whose primary directory holds Orientation and Artist, whose values follow
    /// it, and whose rest is <paramref name="length"/> bytes of nothing, as the image data of a raw
    /// file is.
    /// </summary>
    private static byte[] SparseTiff(int length)
    {
        const int artist = 12;
        int values = 8 + 2 + 2 * 12 + 4;
        byte[] file = new byte[Math.Max(length, values + artist + 8)];
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0, 2), 0x4949);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2, 2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4, 4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(8, 2), 2);
        // Orientation (0x0112), SHORT, one value, in the entry itself.
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(10, 2), 0x0112);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(12, 2), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(14, 4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(18, 2), 1);
        // Artist (0x013B), ASCII, twelve bytes, after the directory.
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(22, 2), 0x013B);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(24, 2), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(26, 4), (uint)artist);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(30, 4), (uint)values);
        Encoding.ASCII.GetBytes("Sparse Read\0").CopyTo(file.AsSpan(values, artist));
        return file;
    }

    // ---- comparison ----

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MetadataSharp.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }

    /// <summary>Two snapshots that agree in every kind of metadata and in what they refused.</summary>
    private static void AssertSame(ImageMetadata expected, ImageMetadata actual, string? where = null)
    {
        string named = where is null ? string.Empty : ": " + where;
        // A TGA file whose bytes do not say so is named by its extension, so a path knows the format
        // of a file whose bytes do not: an array cannot, and that difference is the name, not the read.
        if (expected.Format != MetadataFormat.Unknown)
        {
            Assert.Equal(expected.Format, actual.Format);
            Assert.Equal(expected.MimeType, actual.MimeType);
        }

        Assert.Equal(expected.Exif is null, actual.Exif is null);
        if (expected.Exif is { } left && actual.Exif is { } right)
        {
            Assert.Equal(left.ByteOrder, right.ByteOrder);
            Assert.True(left.IsEditable == right.IsEditable,
                $"an array gives IsEditable {left.IsEditable} and {right.IsEditable} came back{named}");
            Assert.True(left.Entries.Count == right.Entries.Count,
                $"{left.Entries.Count.ToString(CultureInfo.InvariantCulture)} entries from an array and {right.Entries.Count.ToString(CultureInfo.InvariantCulture)} from elsewhere{named}");
            for (int index = 0; index < left.Entries.Count; index++)
            {
                Assert.Equal(left.Entries[index].Key, right.Entries[index].Key);
                Assert.Equal(left.Entries[index].Value.FieldTypeCode, right.Entries[index].Value.FieldTypeCode);
                Assert.Equal(Raw(left.Entries[index].Value.RawBytes), Raw(right.Entries[index].Value.RawBytes));
            }
            Assert.Equal(left.MakerNote?.Format, right.MakerNote?.Format);
            Assert.Equal(left.MakerNote?.Entries.Count ?? 0, right.MakerNote?.Entries.Count ?? 0);
            if (left.MakerNote is { } leftNote && right.MakerNote is { } rightNote)
                for (int index = 0; index < leftNote.Entries.Count; index++)
                {
                    Assert.Equal(leftNote.Entries[index].TagId, rightNote.Entries[index].TagId);
                    Assert.Equal(leftNote.Entries[index].Name, rightNote.Entries[index].Name);
                    Assert.Equal(Raw(leftNote.Entries[index].Value.RawBytes), Raw(rightNote.Entries[index].Value.RawBytes));
                }
        }

        Assert.Equal(expected.Iptc is null, actual.Iptc is null);
        if (expected.Iptc is { } leftIptc && actual.Iptc is { } rightIptc)
        {
            Assert.Equal(leftIptc.Datasets.Count, rightIptc.Datasets.Count);
            for (int index = 0; index < leftIptc.Datasets.Count; index++)
            {
                Assert.Equal(leftIptc.Datasets[index].Key, rightIptc.Datasets[index].Key);
                Assert.Equal(Raw(leftIptc.Datasets[index].Value), Raw(rightIptc.Datasets[index].Value));
            }
        }

        Assert.Equal(expected.Xmp is null, actual.Xmp is null);
        if (expected.Xmp is { } leftXmp && actual.Xmp is { } rightXmp)
        {
            Assert.Equal(leftXmp.ByteLength, rightXmp.ByteLength);
            byte[] leftBytes = new byte[leftXmp.ByteLength], rightBytes = new byte[rightXmp.ByteLength];
            leftXmp.CopyRawTo(leftBytes);
            rightXmp.CopyRawTo(rightBytes);
            Assert.Equal(Raw(leftBytes), Raw(rightBytes));
        }

        Assert.Equal(Raw(expected.IccProfile.Span), Raw(actual.IccProfile.Span));
        Assert.Equal(expected.Comments, actual.Comments);
        Assert.Equal(expected.Previews, actual.Previews);
        // Video properties hold lists of names (the compatible brands of a QuickTime file), which a
        // record compares by instance: compare the names, not the list that holds them.
        Assert.Equal(Properties(expected), Properties(actual));
        Assert.Equal(
            expected.Errors.Select(error => (error.Family, error.Code, error.Exception.SourceOffset, error.Exception.Message)),
            actual.Errors.Select(error => (error.Family, error.Code, error.Exception.SourceOffset, error.Exception.Message)));
    }

    // The video properties, with their name lists spelled out, as a file has two of them.
    private static IEnumerable<(string Key, string Value, string Items)> Properties(ImageMetadata metadata) =>
        (metadata.Video?.Properties ?? []).Select(property =>
            (property.Key, property.Value, string.Join(" | ", property.Items ?? [])));

    // A stable rendering of bytes, so that a difference names the value it is in.
    private static string Raw(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes);

    private static string Raw(IptcValue value)
    {
        byte[] bytes = new byte[value.ByteLength];
        value.CopyRawTo(bytes);
        return Raw(bytes);
    }

    /// <summary>
    /// A stream that counts what is taken from it, and, when it is asked to, refuses to seek: the two
    /// numbers the gates are made of.
    /// </summary>
    private sealed class CountingStream : Stream
    {
        private readonly Stream inner;
        private readonly bool seeks;

        internal CountingStream(Stream inner, bool canSeek = true)
        {
            this.inner = inner;
            seeks = canSeek && inner.CanSeek;
        }

        internal long BytesRead { get; private set; }

        internal long ReadCalls { get; private set; }
        internal bool RepeatedRead { get; private set; }
        private readonly List<(long Start, long End)> ranges = [];
        private void CountRange(long start, int count)
        {
            if (!seeks || count == 0) return;
            long end = start + count;
            if (ranges.Any(range => start < range.End && end > range.Start)) RepeatedRead = true;
            ranges.Add((start, end));
        }

        public override bool CanRead => true;

        public override bool CanSeek => seeks;

        public override bool CanWrite => false;

        public override long Length => seeks ? inner.Length : throw new NotSupportedException();

        public override long Position
        {
            get => seeks ? inner.Position : throw new NotSupportedException();
            set
            {
                if (!seeks) throw new NotSupportedException();
                inner.Position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            long start = seeks ? inner.Position : 0;
            int count = inner.Read(buffer);
            CountRange(start, count);
            BytesRead += count;
            ReadCalls++;
            return count;
        }

        public override int ReadByte()
        {
            long start = seeks ? inner.Position : 0;
            int value = inner.ReadByte();
            if (value >= 0) { CountRange(start, 1); BytesRead++; }
            ReadCalls++;
            return value;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (!seeks) throw new NotSupportedException();
            return inner.Seek(offset, origin);
        }

        public override void Flush() { }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
