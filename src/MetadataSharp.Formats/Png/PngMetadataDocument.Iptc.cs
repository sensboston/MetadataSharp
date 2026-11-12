using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Formats.Internal;
using MetadataSharp.Formats.Jpeg;

namespace MetadataSharp.Formats.Png;

public sealed partial class PngMetadataDocument
{
    /// <summary>
    /// Writes the file with the IPTC edit applied to a distinct, writable, seekable, empty stream. An
    /// edit that changes nothing gives a copy of the source.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PNG defines no chunk for IPTC. The stream is stored the way exiv2 and ImageMagick store it, in
    /// the text chunk with the keyword "Raw profile type iptc": the profile is written in hexadecimal
    /// and holds Photoshop image resources, whose resource 0x0404 is the stream.
    /// </para>
    /// <para>
    /// A chunk the file has is replaced where it stands and keeps its form: a tEXt chunk stays tEXt, a
    /// profile that is the bare IIM stream stays one, resources that begin with the Photoshop
    /// signature keep it, and every resource other than the stream keeps its bytes and its place. The
    /// IPTC digest (resource 0x0425) is left as it is unless
    /// <see cref="IptcWriteOptions.UpdateDigest"/> asks for it to be recomputed; it is removed with
    /// the stream and never added. A file without the chunk gets a zTXt chunk directly before the
    /// image data. Removing the stream removes the chunk when nothing else is left in it.
    /// </para>
    /// </remarks>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.SourceChanged"/> when the source is no longer the data that was
    /// opened; <see cref="MetadataErrorCode.AmbiguousMetadata"/> when the file has two such chunks;
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> when the file has an unknown chunk that is
    /// not safe to copy; otherwise the code of the reason the edit is refused.
    /// </exception>
    public void SaveIptc(Stream destination, IptcEdit edit) => SaveIptcCore(destination, edit, null, false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveIptc(Stream, IptcEdit)"/>
    public Task SaveIptcAsync(Stream destination, IptcEdit edit, CancellationToken cancellationToken = default) => SaveIptcCore(destination, edit, null, true, cancellationToken);
    /// <summary>As <see cref="SaveIptc(Stream, IptcEdit)"/>, with options; null options are the defaults.</summary>
    public void SaveIptc(Stream destination, IptcEdit edit, IptcWriteOptions? options) => SaveIptcCore(destination, edit, options, false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveIptc(Stream, IptcEdit, IptcWriteOptions)"/>
    public Task SaveIptcAsync(Stream destination, IptcEdit edit, IptcWriteOptions? options, CancellationToken cancellationToken = default) => SaveIptcCore(destination, edit, options, true, cancellationToken);
    /// <summary>As <see cref="SaveIptc(Stream, IptcEdit)"/>, to a new path; the file appears there complete or not at all.</summary>
    /// <exception cref="IOException">The destination exists or is the source.</exception>
    public void SaveIptcAs(string destinationPath, IptcEdit edit) => SaveIptcPath(destinationPath, edit, null, false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveIptcAs(string, IptcEdit)"/>
    public Task SaveIptcAsAsync(string destinationPath, IptcEdit edit, CancellationToken cancellationToken = default) => SaveIptcPath(destinationPath, edit, null, true, cancellationToken);
    /// <summary>As <see cref="SaveIptcAs(string, IptcEdit)"/>, with options; null options are the defaults.</summary>
    /// <exception cref="IOException">The destination exists or is the source.</exception>
    public void SaveIptcAs(string destinationPath, IptcEdit edit, IptcWriteOptions? options) => SaveIptcPath(destinationPath, edit, options, false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="SaveIptcAs(string, IptcEdit, IptcWriteOptions)"/>
    public Task SaveIptcAsAsync(string destinationPath, IptcEdit edit, IptcWriteOptions? options, CancellationToken cancellationToken = default) => SaveIptcPath(destinationPath, edit, options, true, cancellationToken);

    private async Task SaveIptcCore(Stream destination, IptcEdit edit, IptcWriteOptions? write, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ArgumentNullException.ThrowIfNull(edit); ContainerIo.Output(source, destination);
        Prepared prepared = await PrepareIptc(edit, write, asynchronous, token).ConfigureAwait(false);
        await Emit(destination, prepared, asynchronous, token).ConfigureAwait(false);
    }
    private async Task SaveIptcPath(string path, IptcEdit edit, IptcWriteOptions? write, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ArgumentNullException.ThrowIfNull(edit);
        string target = ContainerIo.Target(path, sourcePath);
        Prepared prepared = await PrepareIptc(edit, write, asynchronous, token).ConfigureAwait(false);
        await ContainerIo.Publish(target, options.Limits.CopyBufferBytes, asynchronous, token, output => Emit(output, prepared, asynchronous, token)).ConfigureAwait(false);
    }

    private async Task<Prepared> PrepareIptc(IptcEdit edit, IptcWriteOptions? write, bool asynchronous, CancellationToken token)
    {
        IptcEdit captured = edit.Capture();
        if (!captured.IsEmpty || !(write?.UpdateDigest ?? false)) return await PrepareIptcCore(captured, write, asynchronous, token).ConfigureAwait(false);
        // An edit without a change that asks for the digest: the digest alone is recomputed where the
        // profile can be edited, and the file is copied where it cannot, as it is without the option.
        Prepared copy = await PrepareIptcCore(captured, null, asynchronous, token).ConfigureAwait(false);
        try { return await PrepareIptcCore(captured, write, asynchronous, token).ConfigureAwait(false); }
        catch (MetadataException) { return copy; }
    }
    private async Task<Prepared> PrepareIptcCore(IptcEdit captured, IptcWriteOptions? write, bool asynchronous, CancellationToken token)
    {
        bool refresh = write?.UpdateDigest ?? false;
        await Verify(asynchronous, token).ConfigureAwait(false);
        if (captured.IsEmpty && !refresh) return new(true, null, null);
        PngChunk? chunk = Profile("iptc");
        byte[]? profile = chunk is null ? null : await ProfileBytes(chunk, asynchronous, token).ConfigureAwait(false);

        // The form of what the file has: the bare stream, or resources with or without their signature.
        bool bare = false, signed = false;
        PhotoshopResources? resources = null; IptcPacket? held = null;
        if (profile is { Length: > 0 })
        {
            byte[] signature = "Photoshop 3.0\0"u8.ToArray();
            if (profile[0] == 0x1C) { bare = true; held = IptcPacket.Parse(profile); }
            else
            {
                signed = profile.AsSpan().StartsWith(signature);
                resources = PhotoshopResources.Parse(signed ? profile : [.. signature, .. profile], new IptcLimits());
                resources.ValidateTargets();
                if (resources.Iptc is PhotoshopResources.Resource resource) held = IptcPacket.Parse(resource.Data.Span);
            }
        }

        byte[]? stream = null;
        if (captured.RemovesAll)
        {
            if (held is null) return new(true, null, null);
        }
        else
        {
            IptcPacket start = held ?? IptcPacket.CreateEmpty();
            IptcPacket changed = start.Apply(captured)!;
            // Nothing was added to an absent stream, or no byte of the stored one was changed.
            if (ReferenceEquals(changed, start) && !(refresh && resources is not null && resources.DigestIsStale())) return new(true, null, null);
            stream = new byte[changed.ByteLength]; changed.CopyRawTo(stream);
        }

        byte[]? content = stream;
        if (!bare)
        {
            byte[]? rewritten = (resources ?? PhotoshopResources.Empty).Rewrite(stream, write?.UpdateDigest ?? false, int.MaxValue);
            content = rewritten is null || rewritten.Length == 14 ? null : signed ? rewritten : rewritten[14..];
        }
        if (scan.Chunks.Any(c => c.UnsafeUnknown)) throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "An unknown unsafe-to-copy PNG chunk prevents editing.");
        byte[]? rebuilt = null;
        if (content is not null)
        {
            // Two characters a byte and a line break every thirty-six: bounded before it is built.
            Budget(content.Length * 3L + (chunk?.Size ?? 0));
            byte[] text = PngRawProfile.Encode("iptc", content);
            if (!PngRawProfile.Decode(text).AsSpan().SequenceEqual(content))
                throw new MetadataException(MetadataErrorCode.UnsafeRelocation, "The planned raw profile does not read back and was not written.");
            byte[] keyword = "Raw profile type iptc\0"u8.ToArray();
            bool plain = chunk?.Type == "tEXt";
            // zTXt: the keyword, compression method 0, the zlib stream.
            byte[] data = plain ? [.. keyword, .. text] : [.. keyword, 0, .. Deflate(text)];
            Budget((long)data.Length + text.Length + (chunk?.Size ?? 0));
            rebuilt = Crc.Chunk(plain ? "tEXt" : "zTXt", data);
        }
        token.ThrowIfCancellationRequested(); return new(false, chunk, rebuilt);
    }
}
