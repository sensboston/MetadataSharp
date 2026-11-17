using MetadataSharp.Core;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Png;
using MetadataSharp.Formats.WebP;
using MetadataSharp.Tests.Png;
using MetadataSharp.Tests.WebP;
using Xunit;

namespace MetadataSharp.Tests.Containers;

public sealed class ContainerAuditTests
{
    private static XmpEdit Edit()
    { var edit = new XmpEdit(); edit.Set(new("http://ns.adobe.com/xap/1.0/", "CreatorTool"), XmpValue.CreateText("audit")); return edit; }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicationRaceAndMidEmissionFailureNeverOverwriteOrLeaveTemporary(bool webp)
    {
        byte[] bytes = webp ? WebPFixture.Image(0) : PngFixture.Build();
        using var source = new HookSource(bytes);
        using var png = webp ? null : PngMetadataDocument.Open(source);
        using var web = webp ? WebPMetadataDocument.Open(source) : null;
        void Save(string path) { if (web is not null) web.SaveXmpAs(path, Edit()); else png!.SaveXmpAs(path, Edit()); }
        string dir = Path.Combine(Path.GetTempPath(), "container-audit-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            string raced = Path.Combine(dir, "raced"); source.Next = () => File.WriteAllText(raced, "winner");
            Assert.Throws<IOException>(() => Save(raced)); Assert.Equal("winner", File.ReadAllText(raced));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            string failed = Path.Combine(dir, "failed");
            // First read verifies identity; second read is during encoded-range emission.
            source.Next = () => source.Next = () => throw new IOException("Injected emission read failure.");
            Assert.Throws<IOException>(() => Save(failed)); Assert.False(File.Exists(failed)); Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally { Directory.Delete(dir, true); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PathOwnershipSnapshotsAndOptionValidation(bool webp)
    {
        byte[] bytes = webp ? WebPFixture.Image(1) : PngFixture.Build();
        string path = Path.Combine(Path.GetTempPath(), "container-source-" + Guid.NewGuid().ToString("N")); File.WriteAllBytes(path, bytes);
        try
        {
            using var output = new MemoryStream();
            XmpPacket packet;
            if (webp)
            {
                await using (var doc = await WebPMetadataDocument.OpenAsync(path)) await doc.SaveXmpAsync(output, Edit());
                await using var reader = await WebPMetadataDocument.OpenAsync(new MemoryStream(output.ToArray())); packet = (await reader.ReadXmpAsync())!;
                await reader.DisposeAsync(); Assert.Throws<ObjectDisposedException>(() => reader.ReadXmp());
                Assert.Throws<ArgumentOutOfRangeException>(() => WebPMetadataDocument.Open(new MemoryStream(bytes), new() { Limits = new() { MaxInputBytes = 0 } }));
            }
            else
            {
                await using (var doc = await PngMetadataDocument.OpenAsync(path)) await doc.SaveXmpAsync(output, Edit());
                await using var reader = await PngMetadataDocument.OpenAsync(new MemoryStream(output.ToArray())); packet = (await reader.ReadXmpAsync())!;
                await reader.DisposeAsync(); Assert.Throws<ObjectDisposedException>(() => reader.ReadXmp());
                Assert.Throws<ArgumentOutOfRangeException>(() => PngMetadataDocument.Open(new MemoryStream(bytes), new() { MaxCompressedXmpBytes = 0 }));
            }
            Assert.Equal("audit", packet.Properties[new("http://ns.adobe.com/xap/1.0/", "CreatorTool")].Text);
            File.Delete(path); // Also exercises owned path handle closure on Windows.
        }
        finally { File.Delete(path); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterEmissionMayLeaveCallerOwnedPartialOutput(bool webp)
    {
        byte[] bytes = webp ? WebPFixture.Image(0) : PngFixture.Build(); using var source = new MemoryStream(bytes);
        using var png = webp ? null : PngMetadataDocument.Open(source);
        using var web = webp ? WebPMetadataDocument.Open(source) : null;
        using var cancellation = new CancellationTokenSource(); using var output = new CancelOutput(cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => web is not null
            ? web.SaveXmpAsync(output, Edit(), cancellation.Token) : png!.SaveXmpAsync(output, Edit(), cancellation.Token));
        Assert.Equal(10, output.Length); Assert.True(output.CanWrite);
    }
    private sealed class CancelOutput(CancellationTokenSource cancellation) : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { base.Write(buffer.Span[..Math.Min(10, buffer.Length)]); cancellation.Cancel(); cancellationToken.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
    }
    private sealed class HookSource(byte[] bytes) : MemoryStream(bytes)
    {
        internal Action? Next;
        public override int Read(Span<byte> buffer) { Action? next = Next; Next = null; next?.Invoke(); return base.Read(buffer); }
    }
}
