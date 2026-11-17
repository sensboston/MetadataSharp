#pragma warning disable CS0618 // Exercise the retained obsolete compatibility facade.
using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Formats.Tiff;
using Xunit;

namespace MetadataSharp.Tests.Tiff;

public sealed class TiffWriteAcceptanceTests
{
    private static TiffMetadataEdit Edit(string text = "accepted")
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text + "\0"); var edit = new TiffMetadataEdit();
        edit.Set(new(8, 305), ExifValue.FromRaw(TiffFieldType.Ascii, (uint)bytes.Length, Endianness.LittleEndian, bytes)); return edit;
    }
    private static byte[] Save(byte[] bytes, TiffMetadataEdit? edit = null, TiffEditOptions? options = null)
    { using var doc = TiffMetadataDocument.Open(new MemoryStream(bytes), options); using var output = new MemoryStream(); doc.Save(output, edit ?? Edit()); return output.ToArray(); }
    private static void Prefix(byte[] source, byte[] output)
    { Assert.Equal(source[..4], output[..4]); Assert.Equal(source[8..], output[8..source.Length]); }
    private static string Text(ExifValue value) { byte[] bytes = new byte[value.ByteLength]; value.CopyRawTo(bytes); return Encoding.ASCII.GetString(bytes).TrimEnd('\0'); }

    [Theory]
    [InlineData(1, false)] [InlineData(5, false)] [InlineData(7, false)] [InlineData(8, false)] [InlineData(32946, false)] [InlineData(32773, false)]
    [InlineData(1, true)] [InlineData(5, true)] [InlineData(7, true)] [InlineData(8, true)] [InlineData(32946, true)] [InlineData(32773, true)]
    public void CompressionSelectorsPreserveOpaqueSegmentBytes(ushort compression, bool tiles)
    {
        // Accounting fixtures only: the pixel ramp is not claimed to encode each codec.
        var f = StandaloneTiffFixture.Create(tiles: tiles);
        f.Directory(8, StandaloneTiffFixture.Fields(tiles).Select(field => field.Tag == 259 ? ((ushort)259, (ushort)3, new uint[] { compression }) : field).ToArray());
        byte[] output = Save(f.Bytes); Prefix(f.Bytes, output);
        var layout = TiffLayout.Read(new MemoryStream(output)); Assert.True(layout.IsSupported);
        Assert.Equal(compression, layout.Directories.Single().Image!.Compression); Assert.Equal(new TiffByteRange(2048, 256), Assert.Single(layout.Directories.Single().Image!.Segments));
    }
    [Theory]
    [InlineData(false, false, false)] [InlineData(true, false, false)] [InlineData(false, true, false)] [InlineData(true, true, false)]
    [InlineData(false, false, true)] [InlineData(true, true, true)]
    public void RgbPlanesAndExplicitAlphaRetainAllSegments(bool big, bool tiles, bool alpha)
    {
        var f = StandaloneTiffFixture.Create(big, tiles); uint spp = alpha ? 4u : 3u;
        var fields = StandaloneTiffFixture.Fields(tiles).Select(field => field.Tag switch
        {
            258 => ((ushort)258, (ushort)3, Enumerable.Repeat(8u, (int)spp).ToArray()),
            262 => ((ushort)262, (ushort)3, new uint[] { 2 }),
            277 => ((ushort)277, (ushort)3, new uint[] { spp }),
            273 or 324 => (field.Tag, (ushort)4, Enumerable.Range(0, (int)spp).Select(i => (uint)(2048 + i * 256)).ToArray()),
            279 or 325 => (field.Tag, (ushort)4, Enumerable.Repeat(256u, (int)spp).ToArray()),
            _ => field
        }).Append(((ushort)284, (ushort)3, new uint[] { 2 })).ToList();
        if (alpha) fields.Add((338, 3, [2]));
        f.Directory(8, fields.ToArray()); byte[] output = Save(f.Bytes); Prefix(f.Bytes, output);
        var layout = TiffLayout.Read(new MemoryStream(output)); Assert.True(layout.IsSupported); Assert.Equal((int)spp, layout.Directories.Single().Image!.Segments.Count);
        foreach (var range in layout.Directories.Single().Image!.Segments) Assert.Equal(f.Bytes.AsSpan((int)range.Offset, (int)range.Length).ToArray(), output.AsSpan((int)range.Offset, (int)range.Length).ToArray());
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SharedRationalStorageRemainsAliasedWithoutExtraRanges(bool big)
    {
        var f = StandaloneTiffFixture.Create(big); var fields = StandaloneTiffFixture.Fields().Concat(new (ushort, ushort, uint[])[] { (282, 5, [72, 1]), (283, 5, [72, 1]) }).ToArray();
        f.Directory(8, fields);
        for (int i = 0; i < fields.Length; i++) if (fields[i].Item1 is 282 or 283)
        { int entry = 8 + 2 + 12 * i; f.U32(entry + 4, 1); f.U32(entry + 8, 264); }
        var options = new TiffEditOptions { ReadOptions = new() { MaxAccountedRanges = 4 } };
        byte[] output = Save(f.Bytes, Edit("abc"), options); Prefix(f.Bytes, output);
        var layout = TiffLayout.Read(new MemoryStream(output), options.ReadOptions); Assert.True(layout.IsSupported); Assert.Equal(4, layout.Ranges.Count);
        Assert.Equal(new TiffByteRange(264, 8), Assert.Single(layout.Ranges, r => r.Kind == TiffRangeKind.FieldValue).Range);
        Assert.Equal(layout.Directories.Single().Fields.Single(v => v.TagId == 282).ValueRange, layout.Directories.Single().Fields.Single(v => v.TagId == 283).ValueRange);
    }
    [Fact]
    public void CandidatePreviewAndOddLengthAppendPaddingArePreserved()
    {
        var f = StandaloneTiffFixture.Create(); f.Directory(8, [.. StandaloneTiffFixture.Fields(), (513, 4, [3000]), (514, 4, [32])]);
        byte[] original = f.Bytes[..4095]; byte[] output = Save(original); Prefix(original, output);
        Assert.Equal(0, output[4095]); Assert.Equal(4096u, BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(4)));
        var layout = TiffLayout.Read(new MemoryStream(output)); Assert.True(layout.IsSupported);
        Assert.Equal(new TiffByteRange(3000, 32), layout.Directories.Single().Image!.JpegPreview);
    }
    [Theory]
    [InlineData(37500)] [InlineData(700)] [InlineData(33723)] [InlineData(34675)] [InlineData(347)]
    public void OpaqueExtensionsRefuseEffectiveEditsBeforeAnyOutput(ushort tag)
    {
        var f = StandaloneTiffFixture.Create(); f.Directory(8, [.. StandaloneTiffFixture.Fields(), (tag, 7, [0])]);
        using var doc = TiffMetadataDocument.Open(new MemoryStream(f.Bytes)); using var output = new MemoryStream();
        Assert.Equal(MetadataErrorCode.UnsafeRelocation, Assert.Throws<MetadataException>(() => doc.Save(output, Edit())).Code); Assert.Empty(output.ToArray());
        Assert.Equal(f.Bytes, Save(f.Bytes, new()));
    }
    [Fact]
    public void EntryAndValueCeilingsRefuseBeforeEmission()
    {
        var f = StandaloneTiffFixture.Create();
        foreach (var options in new[] {
            new TiffEditOptions { ReadOptions = new() { Limits = new() { MaxEntriesPerDirectory = 9 } } },
            new TiffEditOptions { ReadOptions = new() { Limits = new() { MaxTotalEntries = 9 } } },
            new TiffEditOptions { ReadOptions = new() { Limits = new() { MaxValueBytes = 4 } } } })
        {
            using var doc = TiffMetadataDocument.Open(new MemoryStream(f.Bytes), options); using var output = new MemoryStream();
            Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => doc.Save(output, Edit())).Code); Assert.Empty(output.ToArray());
        }
    }
    [Fact]
    public async Task ClassicAddressBoundaryIsPlannedWithoutGiantBuffersOrPixelIo()
    {
        // Pure planner boundary: copy a validated small model, vary only its source prefix length.
        // This does not claim end-to-end hashing/copying of a 4-GiB source.
        var f = StandaloneTiffFixture.Create(); var layout = TiffLayout.Read(new MemoryStream(f.Bytes));
        TiffLayout WithLength(long length) => new(layout.ByteOrder, length, layout.FirstIfdOffset, layout.Directories.ToArray(), layout.Ranges.ToArray(), []);
        var options = new TiffEditOptions { MaxOutputBytes = 4294967296L, ReadOptions = new() { Limits = new() { MaxInputBytes = 4294967296L } } };
        Task<byte[]> Read(long at, int count) => Task.FromResult(f.Bytes.AsSpan((int)at, count).ToArray());
        var boundary = new TiffAppendPlanner(WithLength(4294967296L - 127), options, Read, default);
        var fits = await boundary.Create(Edit("abc").Snapshot()); Assert.NotNull(fits); Assert.Equal(127, fits.Tail.Length);
        Assert.Equal(4294967296L - 126, (long)BinaryPrimitives.ReadUInt32LittleEndian(fits.Root));
        foreach (long length in new[] { 4294967296L - 127, 4294967296L })
        {
            var planner = new TiffAppendPlanner(WithLength(length), options, Read, default);
            Assert.Equal(MetadataErrorCode.LimitExceeded, (await Assert.ThrowsAsync<MetadataException>(() => planner.Create(Edit().Snapshot()))).Code);
        }
    }
    [Fact]
    public void PublicationRaceAndMidEmissionReadFailurePreserveWinnerAndCleanup()
    {
        var f = StandaloneTiffFixture.Create(); using var source = new HookSource(f.Bytes); using var doc = TiffMetadataDocument.Open(source);
        string dir = Path.Combine(Path.GetTempPath(), "tiff-publication-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            string raced = Path.Combine(dir, "raced.tif"); source.Next = () => File.WriteAllText(raced, "winner");
            Assert.Throws<IOException>(() => doc.SaveAs(raced, Edit())); Assert.Equal("winner", File.ReadAllText(raced)); Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            string failed = Path.Combine(dir, "failed.tif"); source.Next = () => source.Next = () => throw new IOException("Injected emission read failure.");
            Assert.Throws<IOException>(() => doc.SaveAs(failed, Edit())); Assert.False(File.Exists(failed)); Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            using var output = new MemoryStream(); source.Next = () => source.Next = () => throw new IOException("Injected stream failure.");
            Assert.Throws<IOException>(() => doc.Save(output, Edit())); Assert.Equal(8, output.Length); Assert.True(output.CanWrite);
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task SnapshotBeforeAsyncIdentityIoAndCancellationAfterOutput()
    {
        var f = StandaloneTiffFixture.Create(); using var source = new HookSource(f.Bytes); await using var doc = await TiffMetadataDocument.OpenAsync(source);
        var edit = Edit("snapshot"); source.NextAsync = () => edit.Set(new(8, 305), ExifValue.FromRaw(TiffFieldType.Ascii, 6, Endianness.LittleEndian, "later\0"u8));
        using var output = new MemoryStream(); await doc.SaveAsync(output, edit);
        using (var read = TiffMetadataDocument.Open(new MemoryStream(output.ToArray()))) Assert.Equal("snapshot", Text(read.ReadValue(new(read.Layout.FirstIfdOffset, 305))!));
        using var cancellation = new CancellationTokenSource(); using var partial = new CancelOutput(cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => doc.SaveAsync(partial, Edit(), cancellation.Token)); Assert.Equal(4, partial.Length); Assert.True(partial.CanWrite);
        string dir = Path.Combine(Path.GetTempPath(), "tiff-cancel-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            using var cancelPath = new CancellationTokenSource(); source.NextAsync = () => source.NextAsync = cancelPath.Cancel;
            string path = Path.Combine(dir, "cancelled.tif"); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => doc.SaveAsAsync(path, Edit(), cancelPath.Token));
            Assert.False(File.Exists(path)); Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public void InvalidTargetDateIsOpaqueToUnrelatedEditsButCannotBeReadOrOverwritten()
    {
        var f = StandaloneTiffFixture.Create(); var fields = StandaloneTiffFixture.Fields().Append(((ushort)306, (ushort)2, new uint[] { 0 })).ToArray();
        f.Directory(8, fields); int entry = 8 + 2 + 12 * (fields.Length - 1); f.U32(entry + 4, 20); f.U32(entry + 8, 512);
        "2026:99:99 99:99:99\0"u8.CopyTo(f.Bytes.AsSpan(512));
        using var doc = TiffMetadataDocument.Open(new MemoryStream(f.Bytes)); using var output = new MemoryStream();
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => doc.ReadValue(new(8, 306))).Code);
        var date = new TiffMetadataEdit(); date.Set(new(8, 306), ExifValue.FromRaw(TiffFieldType.Ascii, 20, Endianness.LittleEndian, "2026:11:07 12:34:56\0"u8));
        Assert.Equal(MetadataErrorCode.UnsupportedFeature, Assert.Throws<MetadataException>(() => doc.Save(output, date)).Code); Assert.Empty(output.ToArray());
        Prefix(f.Bytes, Save(f.Bytes));
        var remove = new TiffMetadataEdit(); remove.Remove(new(8, 306)); byte[] removed = Save(f.Bytes, remove);
        using var clean = TiffMetadataDocument.Open(new MemoryStream(removed)); Assert.Null(clean.ReadValue(new(clean.Layout.FirstIfdOffset, 306)));
    }
    [Fact]
    public void WholeFileIsNotLimitedToEmbeddedExifCapacityAndSnapshotsOutliveDocument()
    {
        var f = StandaloneTiffFixture.Create(); byte[] source = new byte[128 * 1024]; f.Bytes.CopyTo(source, 0);
        byte[] output = Save(source); Prefix(source, output); Assert.True(output.Length > source.Length);
        ExifValue snapshot; TiffLayout layout;
        using (var doc = TiffMetadataDocument.Open(new MemoryStream(output)))
        { layout = doc.Layout; snapshot = doc.ReadValue(new(layout.FirstIfdOffset, 305))!; }
        Array.Clear(output); Assert.True(layout.FirstIfdOffset >= source.Length); Assert.Equal("accepted", Text(snapshot));
    }
    [Fact]
    public void OversizedEditSnapshotRefusesBeforeCopyingOrSourceIdentityIo()
    {
        var f = StandaloneTiffFixture.Create(); using var source = new HookSource(f.Bytes);
        using var doc = TiffMetadataDocument.Open(source, new() { ReadOptions = new() { Limits = new() { MaxRetainedMetadataBytes = 4096 } } });
        var edit = Edit(new string('x', 10000)); source.Next = () => throw new InvalidOperationException("Identity I/O must not start for an over-budget snapshot.");
        using var output = new MemoryStream();
        Assert.Equal(MetadataErrorCode.LimitExceeded, Assert.Throws<MetadataException>(() => doc.Save(output, edit)).Code);
        Assert.NotNull(source.Next); Assert.Empty(output.ToArray());
    }
    [Theory]
    [InlineData(0, 1, 16, 1, 0, false)] [InlineData(1, 1, 8, 2, 0, true)]
    [InlineData(0, 2, 16, 1, 1, false)] [InlineData(1, 2, 8, 2, 2, true)]
    [InlineData(2, 3, 16, 1, 0, false)] [InlineData(2, 4, 8, 1, 1, true)]
    [InlineData(2, 4, 16, 2, 2, true)] [InlineData(0, 2, 8, 1, 2, false)]
    public void GrayRgbBitDepthsContiguousSeparateAndBothAlphaKinds(uint photo, uint samples, uint bits, uint planar, uint alpha, bool tiles)
    {
        var f = StandaloneTiffFixture.Create(tiles: tiles); int segments = planar == 2 ? (int)samples : 1;
        uint bytesPerSegment = 256 * (bits / 8) * (planar == 2 ? 1 : samples);
        var fields = StandaloneTiffFixture.Fields(tiles).Select(field => field.Tag switch
        {
            258 => ((ushort)258, (ushort)3, Enumerable.Repeat(bits, (int)samples).ToArray()),
            262 => ((ushort)262, (ushort)3, new uint[] { photo }),
            277 => ((ushort)277, (ushort)3, new uint[] { samples }),
            273 or 324 => (field.Tag, (ushort)4, Enumerable.Range(0, segments).Select(i => 2048u + (uint)i * bytesPerSegment).ToArray()),
            279 or 325 => (field.Tag, (ushort)4, Enumerable.Repeat(bytesPerSegment, segments).ToArray()), _ => field
        }).Append(((ushort)284, (ushort)3, new uint[] { planar })).Append(((ushort)339, (ushort)3, Enumerable.Repeat(1u, (int)samples).ToArray())).ToList();
        if (alpha != 0) fields.Add((338, 3, [alpha]));
        f.Directory(8, fields.ToArray()); byte[] output = Save(f.Bytes); Prefix(f.Bytes, output);
        var layout = TiffLayout.Read(new MemoryStream(output)); Assert.True(layout.IsSupported); Assert.Equal(segments, layout.Directories.Single().Image!.Segments.Count);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EveryAddressedTagAddsReadsNoOpsAndRemovesWithOriginalEndian(bool big)
    {
        var f = StandaloneTiffFixture.Create(big); var edit = new TiffMetadataEdit();
        ushort[] tags = [269, 270, 271, 272, 285, 305, 315, 316, 306, 274];
        foreach (ushort tag in tags)
        {
            byte[] bytes = tag == 274 ? [6, 0] : Encoding.ASCII.GetBytes(tag == 306 ? "2026:11:07 12:34:56\0" : $"tag-{tag}\0");
            edit.Set(new(8, tag), ExifValue.FromRaw(tag == 274 ? TiffFieldType.Short : TiffFieldType.Ascii, tag == 274 ? 1u : (uint)bytes.Length, Endianness.LittleEndian, bytes));
        }
        byte[] output = Save(f.Bytes, edit); Prefix(f.Bytes, output);
        using var doc = TiffMetadataDocument.Open(new MemoryStream(output)); var same = new TiffMetadataEdit(); var remove = new TiffMetadataEdit();
        foreach (ushort tag in tags)
        {
            var key = new TiffFieldKey(doc.Layout.FirstIfdOffset, tag); var value = doc.ReadValue(key); Assert.NotNull(value); Assert.Equal(doc.Layout.ByteOrder, value.ByteOrder);
            if (tag == 274) { byte[] bytes = new byte[2]; value.CopyRawTo(bytes); Assert.Equal(big ? new byte[] { 0, 6 } : new byte[] { 6, 0 }, bytes); }
            else Assert.Equal(tag == 306 ? "2026:11:07 12:34:56" : $"tag-{tag}", Text(value));
            same.Set(key, value); remove.Remove(key);
        }
        using var noop = new MemoryStream(); doc.Save(noop, same); Assert.Equal(output, noop.ToArray());
        using var removed = new MemoryStream(); doc.Save(removed, remove); Prefix(output, removed.ToArray());
        using var clean = TiffMetadataDocument.Open(new MemoryStream(removed.ToArray()));
        foreach (ushort tag in tags) Assert.Null(clean.ReadValue(new(clean.Layout.FirstIfdOffset, tag)));
    }
    private sealed class HookSource(byte[] bytes) : MemoryStream(bytes)
    {
        internal Action? Next;
        internal Action? NextAsync;
        public override int Read(Span<byte> buffer)
        { if (buffer.Length > 1000) { Action? action = Next; Next = null; action?.Invoke(); } return base.Read(buffer); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { if (buffer.Length > 1000) { Action? action = NextAsync; NextAsync = null; action?.Invoke(); } cancellationToken.ThrowIfCancellationRequested(); return base.ReadAsync(buffer, cancellationToken); }
    }
    private sealed class CancelOutput(CancellationTokenSource cancellation) : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { byte[] bytes = buffer.ToArray(); base.Write(bytes, 0, bytes.Length); cancellation.Cancel(); cancellationToken.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
    }
}
