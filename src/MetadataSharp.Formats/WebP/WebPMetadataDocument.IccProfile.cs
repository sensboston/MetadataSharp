using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Formats.Internal;

namespace MetadataSharp.Formats.WebP;

public sealed partial class WebPMetadataDocument
{
    /// <summary>The bit of the VP8X flags that says the file has an ICCP chunk.</summary>
    private const byte IccFlag = 0x20;

    /// <summary>
    /// Writes a copy of the file whose ICC profile is <paramref name="profile"/>, or that has no
    /// profile when it is null. The profile is the payload of the <c>ICCP</c> chunk, which stands
    /// where the <c>ICCP</c> chunk of the source stood, or, in a file that had none, directly after
    /// the <c>VP8X</c> chunk. The ICC bit of the <c>VP8X</c> flags is set or cleared with it and the
    /// RIFF size rewritten. A file in the simple layout, one <c>VP8 </c> or <c>VP8L</c> chunk
    /// alone, is given a <c>VP8X</c> chunk with its canvas size, as the EXIF and XMP saves give it
    /// one; removing the profile keeps the <c>VP8X</c> chunk. Every other chunk is copied as it is.
    /// A file that already holds exactly this profile is copied as it is.
    /// <see cref="IccProfiles.Read"/> returns the profile of the result. The profile is not
    /// interpreted; the array is copied before the method returns.
    /// </summary>
    /// <param name="destination">A writable, seekable, empty stream that is not the source.</param>
    /// <param name="profile">The whole profile, or null for none.</param>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidIccProfile"/> when the profile is shorter than the 128
    /// bytes of a header, its size field is not the number of bytes given, or the <c>acsp</c>
    /// signature is absent.
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when it exceeds
    /// <see cref="MetadataLimits.MaxRetainedMetadataBytes"/>, the result would have more chunks
    /// than <see cref="MetadataLimits.MaxSegments"/> or be longer than
    /// <see cref="MetadataLimits.MaxInputBytes"/>, or longer than a RIFF file can be.
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> when the file has a chunk of an unknown kind.
    /// <see cref="MetadataErrorCode.SourceChanged"/> when the source is not what was opened.
    /// <see cref="MetadataErrorCode.UnsafeRelocation"/> when the planned output, read as a file
    /// before it is written, is not the source with exactly this change.
    /// </exception>
    public void SaveIccProfile(Stream destination, byte[]? profile) =>
        SaveIcc(destination, Clone(profile), false, default).GetAwaiter().GetResult();

    /// <inheritdoc cref="SaveIccProfile(Stream, byte[])"/>
    public Task SaveIccProfileAsync(Stream destination, byte[]? profile, CancellationToken cancellationToken = default) =>
        SaveIcc(destination, Clone(profile), true, cancellationToken);

    /// <summary>As <see cref="SaveIccProfile(Stream, byte[])"/>, to a new file that is published only when it is complete.</summary>
    public void SaveIccProfileAs(string destinationPath, byte[]? profile) =>
        SaveIccPath(destinationPath, Clone(profile), false, default).GetAwaiter().GetResult();

    /// <inheritdoc cref="SaveIccProfileAs(string, byte[])"/>
    public Task SaveIccProfileAsAsync(string destinationPath, byte[]? profile, CancellationToken cancellationToken = default) =>
        SaveIccPath(destinationPath, Clone(profile), true, cancellationToken);

    /// <summary>The planned output, the profile it is to hold and the VP8X payload it is to begin with.</summary>
    private sealed record IccPlan(PlannedOutput Output, byte[]? Profile, byte[] Extended);

    private static byte[]? Clone(byte[]? bytes) => bytes is null ? null : (byte[])bytes.Clone();

    private async Task SaveIcc(Stream destination, byte[]? profile, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ContainerIo.Output(source, destination);
        IccPlan? plan = await PrepareIcc(profile, asynchronous, token).ConfigureAwait(false);
        await EmitIcc(destination, plan, asynchronous, token).ConfigureAwait(false);
    }

    private async Task SaveIccPath(string destinationPath, byte[]? profile, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        string target = ContainerIo.Target(destinationPath, sourcePath);
        IccPlan? plan = await PrepareIcc(profile, asynchronous, token).ConfigureAwait(false);
        await ContainerIo.Publish(target, options.Limits.CopyBufferBytes, asynchronous, token,
            output => EmitIcc(output, plan, asynchronous, token)).ConfigureAwait(false);
    }

    private Task EmitIcc(Stream output, IccPlan? plan, bool asynchronous, CancellationToken token) =>
        plan is null
            ? ContainerIo.Copy(source, output, 0, scan.Length, options.Limits.CopyBufferBytes, asynchronous, token)
            : plan.Output.Emit(source, output, options.Limits.CopyBufferBytes, asynchronous, token);

    /// <summary>
    /// The VP8X payload of the output: that of the source, or, for a file in the simple layout, the
    /// one the other saves give it (the canvas of the image, the alpha bit of a VP8L image that
    /// says it uses alpha), with the ICC bit as asked.
    /// </summary>
    private byte[] ExtendedWith(bool profile)
    {
        byte[] extended = scan.ExtendedPayload?.ToArray() ?? new byte[10];
        if (scan.ExtendedPayload is null)
        {
            WebPScanner.Put24(extended.AsSpan(4), scan.Width - 1);
            WebPScanner.Put24(extended.AsSpan(7), scan.Height - 1);
            if (scan.ImageAlphaHint) extended[0] |= 0x10;
        }
        extended[0] = (byte)((extended[0] & ~IccFlag) | (profile ? IccFlag : 0));
        return extended;
    }

    /// <summary>
    /// Everything that can refuse, before a byte is written: the profile, the source, the plan, and
    /// the planned output read again. Null when the output is the source as it is.
    /// </summary>
    private async Task<IccPlan?> PrepareIcc(byte[]? profile, bool asynchronous, CancellationToken token)
    {
        if (profile is not null)
        {
            IccProfiles.Exact(profile);
            Budget(profile.Length);
        }
        await Verify(asynchronous, token).ConfigureAwait(false);
        // The scan has found at most one, after VP8X and before the image.
        WebPChunk? old = scan.Chunks.FirstOrDefault(chunk => chunk.Type == "ICCP");
        if (profile is null && old is null) return null;
        if (profile is not null && old is not null && old.Size == profile.Length)
        {
            byte[] held = await Payload(old, asynchronous, token).ConfigureAwait(false);
            if (held.AsSpan().SequenceEqual(profile)) return null;
        }
        if (scan.Unknown)
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "Unknown WebP chunks prevent effective editing.");
        byte[]? written = profile is null ? null : WebPScanner.Chunk("ICCP", profile);
        byte[] extended = ExtendedWith(profile is not null);
        long length = scan.Length - (old?.Length ?? 0) + (written?.Length ?? 0) + (scan.ExtendedPayload is null ? 18 : 0);
        if (length > uint.MaxValue - 1L)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "Edited WebP exceeds the RIFF size limit.");
        byte[] riff = new byte[12];
        "RIFF"u8.CopyTo(riff);
        "WEBP"u8.CopyTo(riff.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(riff.AsSpan(4), (uint)(length - 8));

        var output = new PlannedOutput();
        output.Add(riff);
        output.Add(WebPScanner.Chunk("VP8X", extended));
        // Without a chunk to replace, the profile follows VP8X directly.
        if (old is null && written is not null) output.Add(written);
        foreach (WebPChunk chunk in scan.Chunks)
        {
            if (chunk.Type == "VP8X") continue;
            if (ReferenceEquals(chunk, old))
            {
                if (written is not null) output.Add(written);
                continue;
            }
            output.Copy(chunk.Start, chunk.Length);
        }
        var plan = new IccPlan(output, profile, extended);
        if (output.Length != length) throw Refuse("the plan does not have the length the RIFF header was given");
        await VerifyIcc(plan, asynchronous, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return plan;
    }

    private static MetadataException Refuse(string what) =>
        new(MetadataErrorCode.UnsafeRelocation, "The planned output failed verification and was not written: " + what + ".");

    /// <summary>
    /// Reads the planned output as a file, before it is written, and holds it against the source.
    /// The plan is not trusted. The output has to scan as a WebP within the same budgets, which
    /// checks the RIFF size, the order of the chunks and that the VP8X flags agree with them; its
    /// image has to be the size of the source's and end as far from the end of the file; its VP8X
    /// payload has to be the planned one; every chunk but VP8X and ICCP has to be a chunk of the
    /// source, in the source's order, with the same type and length; and the ICCP chunk, when one
    /// was asked for, has to be there once and hold the profile.
    /// </summary>
    private async Task VerifyIcc(IccPlan plan, bool asynchronous, CancellationToken token)
    {
        await using Stream planned = plan.Output.Open(source);
        WebPScan after;
        try
        {
            after = await WebPScanner.Scan(planned, options.Limits, asynchronous, token).ConfigureAwait(false);
        }
        catch (MetadataException failure)
        {
            if (failure.Code == MetadataErrorCode.LimitExceeded)
                throw new MetadataException(failure.Code, "The edited file exceeds a budget of the limits it would be read with: " + failure.Message);
            throw Refuse("the output does not scan as a WebP");
        }
        if (after.Length != plan.Output.Length) throw Refuse("the output does not have the planned length");
        if (after.Width != scan.Width || after.Height != scan.Height || after.ImageAlphaHint != scan.ImageAlphaHint ||
            after.Length - after.ImageEnd != scan.Length - scan.ImageEnd)
            throw Refuse("the image changed its size or moved against the end of the file");
        if (after.ExtendedPayload is not byte[] extended || !extended.AsSpan().SequenceEqual(plan.Extended))
            throw Refuse("the VP8X chunk is not the planned one");
        static bool Other(WebPChunk chunk) => chunk.Type is not ("VP8X" or "ICCP");
        WebPChunk[] kept = scan.Chunks.Where(Other).ToArray(), same = after.Chunks.Where(Other).ToArray();
        if (kept.Length != same.Length) throw Refuse("a chunk the edit did not name is missing or new");
        for (int index = 0; index < kept.Length; index++)
        {
            if (kept[index].Type != same[index].Type || kept[index].Size != same[index].Size)
                throw Refuse("a chunk the edit did not name changed");
        }
        WebPChunk[] written = after.Chunks.Where(chunk => chunk.Type == "ICCP").ToArray();
        if (written.Length != (plan.Profile is null ? 0 : 1)) throw Refuse("the output does not hold the planned number of ICCP chunks");
        if (plan.Profile is not byte[] profile) return;
        if (written[0].Size != profile.Length) throw Refuse("the ICCP chunk does not have the length of the profile");
        byte[] read = new byte[profile.Length];
        planned.Position = written[0].Payload;
        await ContainerIo.Read(planned, read, asynchronous, token).ConfigureAwait(false);
        if (!read.AsSpan().SequenceEqual(profile)) throw Refuse("the ICCP chunk does not hold the profile");
    }
}
