using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Formats.Jpeg;
using Xunit;

namespace MetadataSharp.Tests.Jpeg;

public sealed class JpegEditingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InsertReplaceRemoveAndNoopPreserveEveryUnrelatedByte(bool asynchronous)
    {
        byte[] prefix = [.. JpegFixture.Segment(0xE0, 1), .. JpegFixture.Segment(0xE1, 88, 77, 80), .. JpegFixture.Segment(0xED, 1, 2, 3)];
        byte[] sourceBytes = JpegFixture.Build(prefix);
        using var source = new MemoryStream(sourceBytes);
        using JpegMetadataDocument original = asynchronous ? await JpegMetadataDocument.OpenAsync(source) : JpegMetadataDocument.Open(source);
        Assert.Null(original.Exif);
        var insert = new ExifEdit(); insert.Set(new(ExifDirectoryKind.Ifd0, 0x013B), Text("Alice"));
        using var output = new MemoryStream();
        if (asynchronous) await original.SaveAsync(output, insert); else original.Save(output, insert);
        byte[] inserted = output.ToArray();
        Assert.Equal(sourceBytes, WithoutExif(inserted));
        Assert.Equal(sourceBytes, source.ToArray());
        JpegScanResult scan = JpegScanner.Scan(new MemoryStream(inserted), new());
        Assert.Equal(7L, scan.Exif!.Start);
        using var document = JpegMetadataDocument.Open(new MemoryStream(inserted));
        Assert.True(document.Exif!.TryGetEntry(new(ExifDirectoryKind.Ifd0, 0x013B), out ExifEntry? artist));
        Assert.Equal("Alice\0", Encoding.ASCII.GetString(artist.Value.RawBytes));
        var replace = new ExifEdit(); replace.Set(artist.Key, Text("Bob"));
        using var replaced = new MemoryStream(); document.Save(replaced, replace);
        Assert.Equal(sourceBytes, WithoutExif(replaced.ToArray()));
        using var noop = new MemoryStream(); document.Save(noop, new ExifEdit());
        Assert.Equal(inserted, noop.ToArray());
        var remove = new ExifEdit(); remove.RemoveAllExif();
        using var removed = new MemoryStream(); document.Save(removed, remove);
        Assert.Equal(sourceBytes, removed.ToArray());
        using var originalAgain = new MemoryStream(); original.Save(originalAgain, new ExifEdit());
        Assert.Equal(sourceBytes, originalAgain.ToArray()); // Save never rebinds a document.
    }

    // The read drops a thumbnail whose JPEG stream fails validation and keeps the rest of the file. Inside
    // the JPEG document that means: the metadata opens, and an edit changes its tag and leaves every other
    // byte of the block, the picture that does not read among them, where it is. The edit that discards the
    // whole payload works as well.
    [Fact]
    public void AnUnreadableThumbnailLeavesTheMetadataReadableAndStaysWhereItIsUnderAnEdit()
    {
        byte[] bare = JpegFixture.Build();
        byte[] tiff = BrokenThumbnailTiff();
        byte[] image = [.. bare[..2], .. JpegFixture.Segment(0xE1, JpegFixture.Exif(tiff)), .. bare[2..]];
        using var document = JpegMetadataDocument.Open(new MemoryStream(image));
        Assert.NotNull(document.Exif);
        Assert.True(document.Exif!.TryGetEntry(new(ExifDirectoryKind.Ifd0, 0x0112), out _));
        var edit = new ExifEdit();
        edit.Set(new(ExifDirectoryKind.Ifd0, 0x0112), ExifValue.FromRaw(TiffFieldType.Short, 1,
            Endianness.LittleEndian, [8, 0]));
        using var edited = new MemoryStream();
        document.Save(edited, edit);
        byte[] expected = (byte[])tiff.Clone();
        expected[18] = 8; // the value of Orientation, in its entry
        Assert.Equal(expected, ExifBlock(edited.ToArray()));
        var removal = new ExifEdit();
        removal.RemoveAllExif();
        using var removed = new MemoryStream();
        document.Save(removed, removal);
        Assert.Equal(bare, removed.ToArray());
    }

    /// <summary>An Exif payload whose thumbnail IFD is intact except that its JPEG ends without EOI.</summary>
    private static byte[] BrokenThumbnailTiff()
    {
        var data = new byte[72];
        new byte[] { 0x49, 0x49, 42, 0, 8, 0, 0, 0 }.CopyTo(data, 0);
        new byte[] { 1, 0 }.CopyTo(data, 8);                                    // one 0th-IFD entry
        new byte[] { 0x12, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0 }.CopyTo(data, 10);  // Orientation = 6
        new byte[] { 0x1A, 0, 0, 0 }.CopyTo(data, 22);                          // next: the thumbnail IFD
        new byte[] { 3, 0 }.CopyTo(data, 26);                                   // three entries, no next
        new byte[] { 3, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0 }.CopyTo(data, 28);     // Compression = 6
        new byte[] { 1, 2, 4, 0, 1, 0, 0, 0, 68, 0, 0, 0 }.CopyTo(data, 40);    // JPEG offset 68
        new byte[] { 2, 2, 4, 0, 1, 0, 0, 0, 4, 0, 0, 0 }.CopyTo(data, 52);     // JPEG length 4
        new byte[] { 0xFF, 0xD8, 0xFF, 0x00 }.CopyTo(data, 68);                 // a JPEG that never ends
        return data;
    }

    [Fact]
    public void ProgressiveArithmeticAndDifferentialFramesRoundTripMetadataVerbatim()
    {
        foreach (byte[] image in new[] { JpegFixture.Progressive(), JpegFixture.Arithmetic(),
            JpegFixture.Build(frameMarker: 0xC1), JpegFixture.Build(frameMarker: 0xC3, scan: [1, 1, 0, 1, 0, 0]) })
        {
            byte[] sourceBytes = [.. image[..2], .. JpegFixture.Segment(0xE1, JpegFixture.Exif()), .. image[2..]];
            using var source = new MemoryStream(sourceBytes);
            using JpegMetadataDocument document = JpegMetadataDocument.Open(source);
            Assert.NotNull(document.Exif);
            var edit = new ExifEdit(); edit.Set(new(ExifDirectoryKind.Ifd0, 0x013B), Text("Alice"));
            using var output = new MemoryStream();
            document.Save(output, edit);
            byte[] saved = output.ToArray();
            // Everything the edit does not own — tables, frame and scan headers, entropy-coded data,
            // restart markers and EOI — is copied verbatim, so removing the new APP1 restores the file.
            Assert.Equal(image, WithoutExif(saved));
            using var reopened = JpegMetadataDocument.Open(new MemoryStream(saved));
            Assert.True(reopened.Exif!.TryGetEntry(new(ExifDirectoryKind.Ifd0, 0x013B), out ExifEntry? artist));
            Assert.Equal("Alice\0", Encoding.ASCII.GetString(artist.Value.RawBytes));
        }
    }

    [Fact]
    public void OrdinaryRemovalLastWriteWinsAndMissingRemovalAreIdempotent()
    {
        using var document = JpegMetadataDocument.Open(new MemoryStream(JpegFixture.Build()));
        var missing = new ExifEdit(); missing.Remove(new(ExifDirectoryKind.Ifd0, 0x013B));
        using var copy = new MemoryStream(); document.Save(copy, missing);
        Assert.Equal(JpegFixture.Build(), copy.ToArray());
        var edit = new ExifEdit(); var key = new ExifTagKey(ExifDirectoryKind.Ifd0, 0x013B);
        edit.Set(key, Text("First")); edit.Remove(key); edit.Set(key, Text("Final"));
        using var edited = new MemoryStream(); document.Save(edited, edit);
        using var read = JpegMetadataDocument.Open(new MemoryStream(edited.ToArray()));
        Assert.True(read.Exif!.TryGetEntry(key, out ExifEntry? found));
        Assert.Equal("Final\0", Encoding.ASCII.GetString(found.Value.RawBytes));
        var removal = new ExifEdit(); removal.Remove(key);
        using var result = new MemoryStream(); read.Save(result, removal);
        using var empty = JpegMetadataDocument.Open(new MemoryStream(result.ToArray()));
        Assert.Empty(empty.Exif!.Entries);
        // The block of a JPEG may be left with no tag at all; it is still a block, with a directory in it.
        Assert.Equal(new byte[] { 0x49, 0x49, 42, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, ExifBlock(result.ToArray())[..14]);
    }

    // A tag the vocabulary does not list and a MakerNote do not stand in the way of an edit: no byte of
    // the block moves, so whatever they address is where it was. The directory that gains the new tag is
    // appended as a copy and the header is pointed at it; everything after the header is as it was.
    [Fact]
    public void UnknownAndMakerNoteDataStayAtTheirOffsetsUnderAnEditAndAllowCopyOrWholeRemoval()
    {
        byte[] unknown = JpegFixture.OrientationTiff; unknown[10] = 0xFF; unknown[11] = 0xFF;
        // An Exif IFD pointer in the primary directory, and an opaque inline MakerNote in the Exif directory.
        byte[] maker = [0x49, 0x49, 42, 0, 8, 0, 0, 0, 1, 0, 0x69, 0x87, 4, 0, 1, 0, 0, 0, 26, 0, 0, 0, 0, 0, 0, 0,
            1, 0, 0x7C, 0x92, 7, 0, 4, 0, 0, 0, 1, 2, 3, 4, 0, 0, 0, 0];
        var kept = new ExifTagKey[] { new(ExifDirectoryKind.Ifd0, 0xFFFF), new(ExifDirectoryKind.Exif, 0x927C) };
        foreach ((byte[] tiff, ExifTagKey key) in new[] { unknown, maker }.Zip(kept))
        {
            byte[] image = JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif(tiff)));
            using var doc = JpegMetadataDocument.Open(new MemoryStream(image));
            Assert.True(doc.Exif!.TryGetEntry(key, out ExifEntry? before));
            var edit = new ExifEdit(); edit.Set(new(ExifDirectoryKind.Ifd0, 0x013B), Text("A"));
            using var output = new MemoryStream();
            doc.Save(output, edit);
            byte[] block = ExifBlock(output.ToArray());
            Assert.Equal(tiff[..4], block[..4]);
            Assert.Equal((uint)tiff.Length, BitConverter.ToUInt32(block, 4)); // the copy of the directory
            Assert.Equal(tiff[8..], block[8..tiff.Length]);
            using (var read = JpegMetadataDocument.Open(new MemoryStream(output.ToArray())))
            {
                Assert.True(read.Exif!.TryGetEntry(key, out ExifEntry? after));
                Assert.Equal(before.Value.FieldType, after.Value.FieldType);
                Assert.Equal(before.Value.RawBytes.ToArray(), after.Value.RawBytes.ToArray());
                Assert.True(read.Exif.TryGetEntry(new(ExifDirectoryKind.Ifd0, 0x013B), out ExifEntry? artist));
                Assert.Equal("A\0", Encoding.ASCII.GetString(artist.Value.RawBytes));
            }
            output.SetLength(0);
            doc.Save(output, new ExifEdit()); Assert.Equal(image, output.ToArray());
            var remove = new ExifEdit(); remove.RemoveAllExif();
            using var removed = new MemoryStream(); doc.Save(removed, remove);
            Assert.Equal(JpegFixture.Build(), removed.ToArray());
        }
    }

    [Fact]
    public void SourceIdentityChecksCatchEqualLengthAndLengthChangesBeforeWrites()
    {
        foreach (bool lengthChange in new[] { false, true })
        {
            using var source = new MemoryStream(); source.Write(JpegFixture.Build()); source.Position = 0;
            using var doc = JpegMetadataDocument.Open(source);
            if (lengthChange) source.SetLength(source.Length + 1);
            else { source.Position = source.Length - 3; source.WriteByte(0x7F); }
            using var output = new MemoryStream();
            Assert.Equal(MetadataErrorCode.SourceChanged, Assert.Throws<MetadataException>(() => doc.Save(output, new ExifEdit())).Code);
            Assert.Equal(0, output.Length);
        }
    }

    [Fact]
    public async Task AsyncSourceReallyUsesAsyncReadsAndCancellationLeavesOutputEmpty()
    {
        using var source = new JpegScannerTests.ShortReads(JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif())), true);
        await using var doc = await JpegMetadataDocument.OpenAsync(source);
        using var output = new AsyncOnlyOutput();
        await doc.SaveAsync(output, new ExifEdit());
        Assert.Equal(JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif())), output.ToArray());
        using var cts = new CancellationTokenSource(); cts.Cancel();
        using var canceled = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => doc.SaveAsync(canceled, new ExifEdit(), cts.Token));
        Assert.Empty(canceled.ToArray());
    }

    [Fact]
    public void OutputFailureMayBePartialAndBorrowedSourcesRemainOpenAfterDisposal()
    {
        using var source = new MemoryStream(JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif())));
        var doc = JpegMetadataDocument.Open(source);
        ExifMetadata metadata = doc.Exif!;
        using var output = new FailingOutput();
        Assert.Throws<IOException>(() => doc.Save(output, new ExifEdit()));
        Assert.True(output.Length > 0);
        Assert.Equal(JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif())), source.ToArray());
        doc.Dispose(); doc.Dispose(); Assert.True(source.CanRead);
        Assert.Single(metadata.Entries);
        Assert.Throws<ObjectDisposedException>(() => doc.Save(new MemoryStream(), new ExifEdit()));
    }

    [Fact]
    public void ABlockOutsideTheEditableProfileIsReadAndNotEdited()
    {
        // StripOffsets, as an uncompressed Exif thumbnail has: valid to read, not something the planner writes.
        byte[] tiff = [0x49, 0x49, 42, 0, 8, 0, 0, 0, 1, 0, 0x11, 0x01, 4, 0, 1, 0, 0, 0, 8, 0, 0, 0, 0, 0, 0, 0];
        byte[] jpeg = JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif(tiff)));
        using var doc = JpegMetadataDocument.Open(new MemoryStream(jpeg));
        ExifMetadata metadata = Assert.IsType<ExifMetadata>(doc.Exif);
        Assert.False(metadata.IsEditable);
        Assert.Equal(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x0111), Assert.Single(metadata.Entries).Key);

        using var copy = new MemoryStream();
        doc.Save(copy, new ExifEdit());
        Assert.Equal(jpeg, copy.ToArray());

        var set = new ExifEdit(); set.Set(new(ExifDirectoryKind.Ifd0, 0x0112), ExifValue.FromRaw(TiffFieldType.Short, 1, Endianness.LittleEndian, new byte[] { 1, 0 }));
        var remove = new ExifEdit(); remove.RemoveAllExif();
        foreach (ExifEdit edit in new[] { set, remove })
        {
            using var output = new MemoryStream();
            Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => doc.Save(output, edit)).Code);
            Assert.Empty(output.ToArray());
        }

        using var plain = JpegMetadataDocument.Open(new MemoryStream(JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif()))));
        Assert.True(Assert.IsType<ExifMetadata>(plain.Exif).IsEditable);
    }

    [Fact]
    public void AliasingNonemptyOutputMalformedExifAndOversizeAreRejected()
    {
        using var source = new MemoryStream(JpegFixture.Build());
        using var doc = JpegMetadataDocument.Open(source);
        Assert.Throws<ArgumentException>(() => doc.Save(source, new ExifEdit()));
        using var nonempty = new MemoryStream(new byte[] { 1 });
        Assert.Throws<ArgumentException>(() => doc.Save(nonempty, new ExifEdit())); Assert.Equal(new byte[] { 1 }, nonempty.ToArray());
        Assert.Equal(MetadataErrorCode.InvalidExif, Assert.Throws<MetadataException>(() =>
            JpegMetadataDocument.Open(new MemoryStream(JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif([])))))).Code);
        var big = new ExifEdit(); big.Set(new(ExifDirectoryKind.Ifd0, 0x013B), ExifValue.FromRaw(TiffFieldType.Ascii, 65502, Endianness.LittleEndian, new byte[65502]));
        using var output = new MemoryStream();
        Assert.Equal(MetadataErrorCode.ExifTooLarge, Assert.Throws<MetadataException>(() => doc.Save(output, big)).Code);
        Assert.Empty(output.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveAsPublishesNewPathAndNeverOverwritesExistingFiles(bool asynchronous)
    {
        string dir = Path.Combine(Path.GetTempPath(), "MetadataSharp-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            string source = Path.Combine(dir, "source.jpg"), target = Path.Combine(dir, "target.jpg");
            File.WriteAllBytes(source, JpegFixture.Build());
            using (var doc = asynchronous ? await JpegMetadataDocument.OpenAsync(source) : JpegMetadataDocument.Open(source))
            {
                var edit = new ExifEdit(); edit.Set(new(ExifDirectoryKind.Ifd0, 0x013B), Text("Artist"));
                if (asynchronous) await doc.SaveAsAsync(target, edit); else doc.SaveAs(target, edit);
                Assert.True(File.Exists(target));
                byte[] existing = File.ReadAllBytes(target);
                Assert.Throws<IOException>(() => doc.SaveAs(target, edit));
                Assert.Throws<IOException>(() => doc.SaveAs(source, edit));
                Assert.Equal(existing, File.ReadAllBytes(target));
                Assert.Equal(JpegFixture.Build(), File.ReadAllBytes(source));
            }
            Assert.Equal(2, Directory.GetFiles(dir).Length);
            using var reopened = JpegMetadataDocument.Open(target); Assert.NotNull(reopened.Exif);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SaveAsPreflightFailureCreatesNoTargetOrTemporaryFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "MetadataSharp-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            using var doc = JpegMetadataDocument.Open(new MemoryStream(JpegFixture.Build()));
            var edit = new ExifEdit(); edit.Set(new(ExifDirectoryKind.Ifd0, 0x013B), ExifValue.FromRaw(TiffFieldType.Ascii, 65502, Endianness.LittleEndian, new byte[65502]));
            Assert.Throws<MetadataException>(() => doc.SaveAs(Path.Combine(dir, "result.jpg"), edit));
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void InvalidEditOperationsDoNotMutatePendingChanges()
    {
        var edit = new ExifEdit(); Assert.True(edit.IsEmpty);
        var artist = new ExifTagKey(ExifDirectoryKind.Ifd0, 0x013B);
        Assert.Equal(MetadataErrorCode.TagNotEditable, Assert.Throws<MetadataException>(() => edit.Set(new(ExifDirectoryKind.Ifd0, 0x8769), Text("A"))).Code);
        Assert.Equal(MetadataErrorCode.TagNotEditable, Assert.Throws<MetadataException>(() => edit.Remove(new(ExifDirectoryKind.Thumbnail, 0x0201))).Code);
        Assert.Throws<ArgumentException>(() => edit.Set(artist, ExifValue.FromRaw(TiffFieldType.Ascii, 1, Endianness.LittleEndian, [0xFF])));
        Assert.True(edit.IsEmpty);
        edit.Set(artist, Text("A")); Assert.Throws<ArgumentException>(() => edit.RemoveAllExif());
        Assert.False(edit.Snapshot().RemoveAll);
        var removal = new ExifEdit(); removal.RemoveAllExif();
        Assert.Throws<ArgumentException>(() => removal.Set(artist, Text("B")));
        Assert.Empty(removal.Snapshot().Changes);
    }

    [Fact]
    public void OppositeOrderEditsSwapEachRationalComponentNotTheWholePair()
    {
        ExifValue little = ExifValue.FromRaw(TiffFieldType.Rational, 1, Endianness.LittleEndian, [1, 0, 0, 0, 2, 0, 0, 0]);
        Assert.Equal(new byte[] { 0, 0, 0, 1, 0, 0, 0, 2 }, ExifEdit.ConvertOrder(little, Endianness.BigEndian).RawBytes.ToArray());
        byte[] bigTiff = [0x4D, 0x4D, 0, 42, 0, 0, 0, 8, 0, 1, 1, 0x12, 0, 3, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0];
        using var doc = JpegMetadataDocument.Open(new MemoryStream(JpegFixture.Build(JpegFixture.Segment(0xE1, JpegFixture.Exif(bigTiff)))));
        var edit = new ExifEdit(); edit.Set(new(ExifDirectoryKind.Ifd0, 0x0112), ExifValue.FromRaw(TiffFieldType.Short, 1, Endianness.LittleEndian, [6, 0]));
        using var output = new MemoryStream(); doc.Save(output, edit);
        using var result = JpegMetadataDocument.Open(new MemoryStream(output.ToArray()));
        Assert.Equal(Endianness.BigEndian, result.Exif!.ByteOrder);
        Assert.Equal(new byte[] { 0, 6 }, Assert.Single(result.Exif.Entries).Value.RawBytes.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SaveAsCleansTemporaryFilesOnIoFailureOrPublicationRace(bool competingCreator)
    {
        string dir = Path.Combine(Path.GetTempPath(), "MetadataSharp-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        string target = Path.Combine(dir, "target.jpg");
        try
        {
            using var source = new OnCopySource(JpegFixture.Build());
            using var doc = JpegMetadataDocument.Open(source);
            source.Arm(() =>
            {
                if (competingCreator) File.WriteAllBytes(target, [77]);
                else throw new IOException("Injected copy-phase source failure.");
            });
            Assert.Throws<IOException>(() => doc.SaveAs(target, new ExifEdit()));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            if (competingCreator) Assert.Equal(new byte[] { 77 }, File.ReadAllBytes(target));
            else Assert.False(File.Exists(target));
            Assert.Equal(JpegFixture.Build(), source.ToArray());
        }
        finally { Directory.Delete(dir, true); }
    }

    private sealed class OnCopySource(byte[] bytes) : MemoryStream(bytes)
    {
        private Action? action;
        private int zeroSeeks;
        internal void Arm(Action onCopy) { action = onCopy; zeroSeeks = 0; }
        public override long Position
        {
            get => base.Position;
            set
            {
                base.Position = value;
                if (value == 0 && action is not null && ++zeroSeeks == 2) action();
            }
        }
    }

    private static ExifValue Text(string text)
    { byte[] bytes = Encoding.ASCII.GetBytes(text + "\0"); return ExifValue.FromRaw(TiffFieldType.Ascii, (uint)bytes.Length, Endianness.LittleEndian, bytes); }
    /// <summary>The TIFF structure of the first APP1 Exif segment of a JPEG.</summary>
    internal static byte[] ExifBlock(byte[] image)
    {
        for (int at = 2; at + 4 <= image.Length && image[at] == 0xFF && image[at + 1] != 0xDA;)
        {
            int length = (image[at + 2] << 8) | image[at + 3];
            if (image[at + 1] == 0xE1 && length >= 8 && image.AsSpan(at + 4, 6).SequenceEqual("Exif\0\0"u8))
                return image[(at + 10)..(at + 2 + length)];
            at += 2 + length;
        }
        throw new InvalidOperationException("The JPEG has no Exif segment.");
    }

    internal static byte[] WithoutExif(byte[] image)
    {
        JpegSegment? exif = JpegScanner.Scan(new MemoryStream(image), new()).Exif;
        return exif is null ? image : [.. image[..(int)exif.Start], .. image[(int)(exif.Start + exif.Length)..]];
    }
    private sealed class AsyncOnlyOutput : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Async path used sync writes.");
        public override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("Async path used sync writes.");
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); byte[] bytes = buffer.ToArray(); base.Write(bytes, 0, bytes.Length); return ValueTask.CompletedTask; }
    }
    private sealed class FailingOutput : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer) { base.Write(buffer[..Math.Min(2, buffer.Length)]); throw new IOException("Injected output failure."); }
    }

    [Fact]
    public void BytesAfterEoiSurviveAnEditVerbatim()
    {
        byte[] trailer = [0x00, 0x11, 0xFF, 0xD8, 0x42];
        byte[] sourceBytes = [.. JpegFixture.Build(), .. trailer];
        using JpegMetadataDocument document = JpegMetadataDocument.Open(new MemoryStream(sourceBytes));
        var edit = new ExifEdit(); edit.Set(new(ExifDirectoryKind.Ifd0, 0x013B), Text("Alice"));
        using var output = new MemoryStream();
        document.Save(output, edit);
        byte[] edited = output.ToArray();
        Assert.Equal(trailer, edited[^trailer.Length..]);
        Assert.Equal(sourceBytes, WithoutExif(edited));
    }
}
