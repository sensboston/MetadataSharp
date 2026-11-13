using System.IO.Compression;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Formats.Internal;

namespace MetadataSharp.Formats.Png;

public sealed partial class PngMetadataDocument
{
    /// <summary>
    /// The name every written iCCP chunk carries. The name of a chunk that is replaced described
    /// the profile that is replaced, and the new profile is not interpreted for one of its own.
    /// </summary>
    internal const string IccProfileName = "ICC Profile";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// Writes a copy of the file whose comment is <paramref name="comment"/>, or that has no comment
    /// when it is null. The comment of a PNG is the text chunk with the keyword <c>Comment</c>.
    /// Every such chunk, <c>tEXt</c>, <c>zTXt</c> or <c>iTXt</c>, wherever it stands, is taken out;
    /// the new one stands where the first of them stood, or, in a file that had none, directly
    /// before the image data. It is a <c>tEXt</c> chunk when every character of the text is one of
    /// ISO-8859-1 and a reader that tries UTF-8 first gets the same text from those bytes;
    /// otherwise an uncompressed <c>iTXt</c> chunk in UTF-8 with no language. Every other chunk is
    /// copied as it is. <see cref="Comments.Read"/> returns the comment of the result.
    /// </summary>
    /// <param name="destination">A writable, seekable, empty stream that is not the source.</param>
    /// <param name="comment">The comment, or null for none.</param>
    /// <exception cref="ArgumentException">
    /// The comment is empty, holds the character U+0000 or is not well-formed UTF-16: a reader would
    /// not give such a text back. The destination is not as described.
    /// </exception>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when the chunk exceeds
    /// <see cref="MetadataLimits.MaxRetainedMetadataBytes"/>, or the result would have more chunks
    /// than <see cref="MetadataLimits.MaxSegments"/> or be longer than
    /// <see cref="MetadataLimits.MaxInputBytes"/>.
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> when the file has an unknown chunk that is
    /// not safe to copy.
    /// <see cref="MetadataErrorCode.SourceChanged"/> when the source is not what was opened.
    /// <see cref="MetadataErrorCode.UnsafeRelocation"/> when the planned output, read as a file
    /// before it is written, is not the source with exactly this change.
    /// </exception>
    public void SaveComment(Stream destination, string? comment) =>
        SaveEmbedded(destination, EmbeddedKind.Comment, comment, null, false, default).GetAwaiter().GetResult();

    /// <inheritdoc cref="SaveComment(Stream, string)"/>
    public Task SaveCommentAsync(Stream destination, string? comment, CancellationToken cancellationToken = default) =>
        SaveEmbedded(destination, EmbeddedKind.Comment, comment, null, true, cancellationToken);

    /// <summary>Writes Exiv2's PNG comment keyword while retaining the public Comment-chunk behavior.</summary>
    internal void SaveReferenceComment(Stream destination, string? comment) =>
        SaveEmbedded(destination, EmbeddedKind.Description, comment, null, false, default).GetAwaiter().GetResult();

    /// <summary>As <see cref="SaveComment(Stream, string)"/>, to a new file that is published only when it is complete.</summary>
    /// <param name="destinationPath">Path for publishing comment changes in PNG format.</param>
    /// <param name="comment">Comment text to store, or null to remove the comment.</param>
    public void SaveCommentAs(string destinationPath, string? comment) =>
        SaveEmbeddedPath(destinationPath, EmbeddedKind.Comment, comment, null, false, default).GetAwaiter().GetResult();

    /// <inheritdoc cref="SaveCommentAs(string, string)"/>
    public Task SaveCommentAsAsync(string destinationPath, string? comment, CancellationToken cancellationToken = default) =>
        SaveEmbeddedPath(destinationPath, EmbeddedKind.Comment, comment, null, true, cancellationToken);

    /// <summary>
    /// Writes a copy of the file whose ICC profile is <paramref name="profile"/>, or that has no
    /// profile when it is null. The profile is the <c>iCCP</c> chunk: the name
    /// <c>ICC Profile</c>, the compression method 0 and the profile as a zlib stream. It stands
    /// where the <c>iCCP</c> chunk of the source stood, or, in a file that had none, directly after
    /// the header chunk, which is before the palette and the image data. A file that already holds
    /// exactly this profile, and no <c>sRGB</c> chunk, is copied as it is.
    /// <para>
    /// Writing a profile takes the <c>sRGB</c> chunk out. That chunk says the samples are sRGB; the
    /// specification asks that a file hold one of the two and not both, and readers differ in which
    /// they follow when both are there. Removing a profile leaves an <c>sRGB</c> chunk where it is.
    /// <c>gAMA</c>, <c>cHRM</c> and <c>cICP</c> chunks are copied; the profile is not interpreted,
    /// so they are not rewritten to agree with it.
    /// </para>
    /// Every other chunk is copied as it is. <see cref="IccProfiles.Read"/> returns the profile of
    /// the result. The array is copied before the method returns.
    /// </summary>
    /// <param name="destination">A writable, seekable, empty stream that is not the source.</param>
    /// <param name="profile">The whole profile, or null for none.</param>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidIccProfile"/> when the profile is shorter than the 128
    /// bytes of a header, its size field is not the number of bytes given, or the <c>acsp</c>
    /// signature is absent.
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when it exceeds
    /// <see cref="MetadataLimits.MaxRetainedMetadataBytes"/>, or the result would have more chunks
    /// than <see cref="MetadataLimits.MaxSegments"/> or be longer than
    /// <see cref="MetadataLimits.MaxInputBytes"/>.
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> when the file has an unknown chunk that is
    /// not safe to copy.
    /// <see cref="MetadataErrorCode.SourceChanged"/> when the source is not what was opened.
    /// <see cref="MetadataErrorCode.UnsafeRelocation"/> when the planned output, read as a file
    /// before it is written, is not the source with exactly this change, or the written chunk does
    /// not inflate to the profile.
    /// </exception>
    public void SaveIccProfile(Stream destination, byte[]? profile) =>
        SaveEmbedded(destination, EmbeddedKind.IccProfile, null, Clone(profile), false, default).GetAwaiter().GetResult();

    /// <inheritdoc cref="SaveIccProfile(Stream, byte[])"/>
    public Task SaveIccProfileAsync(Stream destination, byte[]? profile, CancellationToken cancellationToken = default) =>
        SaveEmbedded(destination, EmbeddedKind.IccProfile, null, Clone(profile), true, cancellationToken);

    /// <summary>As <see cref="SaveIccProfile(Stream, byte[])"/>, to a new file that is published only when it is complete.</summary>
    /// <param name="destinationPath">Path for publishing ICC profile changes in PNG format.</param>
    /// <param name="profile">Complete ICC profile bytes, or null to remove the profile.</param>
    public void SaveIccProfileAs(string destinationPath, byte[]? profile) =>
        SaveEmbeddedPath(destinationPath, EmbeddedKind.IccProfile, null, Clone(profile), false, default).GetAwaiter().GetResult();

    /// <inheritdoc cref="SaveIccProfileAs(string, byte[])"/>
    public Task SaveIccProfileAsAsync(string destinationPath, byte[]? profile, CancellationToken cancellationToken = default) =>
        SaveEmbeddedPath(destinationPath, EmbeddedKind.IccProfile, null, Clone(profile), true, cancellationToken);

    private enum EmbeddedKind { Comment, Description, IccProfile }

    /// <summary>
    /// The planned output and what it is to hold: the type and the data of the one chunk written,
    /// if any, and the text or the profile that chunk has to give back.
    /// </summary>
    private sealed record EmbeddedPlan(PlannedOutput Output, EmbeddedKind Kind, PngChunk[] Removed, string? Type, byte[]? Data, string? Comment, byte[]? Profile);

    private static byte[]? Clone(byte[]? bytes) => bytes is null ? null : (byte[])bytes.Clone();

    // Apply the requested PNG comment or ICC profile change to the staged chunk list.
    private async Task SaveEmbedded(Stream destination, EmbeddedKind kind, string? comment, byte[]? profile, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ContainerIo.Output(source, destination);
        EmbeddedPlan? plan = await PrepareEmbedded(kind, comment, profile, asynchronous, token).ConfigureAwait(false);
        await EmitEmbedded(destination, plan, asynchronous, token).ConfigureAwait(false);
    }

    // Publish the staged PNG embedded-data edit to a separate destination path.
    private async Task SaveEmbeddedPath(string destinationPath, EmbeddedKind kind, string? comment, byte[]? profile, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        string target = ContainerIo.Target(destinationPath, sourcePath);
        EmbeddedPlan? plan = await PrepareEmbedded(kind, comment, profile, asynchronous, token).ConfigureAwait(false);
        await ContainerIo.Publish(target, options.Limits.CopyBufferBytes, asynchronous, token,
            output => EmitEmbedded(output, plan, asynchronous, token)).ConfigureAwait(false);
    }

    private Task EmitEmbedded(Stream output, EmbeddedPlan? plan, bool asynchronous, CancellationToken token) =>
        plan is null
            ? ContainerIo.Copy(source, output, 0, scan.Length, options.Limits.CopyBufferBytes, asynchronous, token)
            : plan.Output.Emit(source, output, options.Limits.CopyBufferBytes, asynchronous, token);

    /// <summary>
    /// Everything that can refuse, before a byte is written: the content, the source, the plan, and
    /// the planned output read again. Null when the output is the source as it is.
    /// </summary>
    private async Task<EmbeddedPlan?> PrepareEmbedded(EmbeddedKind kind, string? comment, byte[]? profile, bool asynchronous, CancellationToken token)
    {
        string? type = null;
        byte[]? data = null;
        if ((kind is EmbeddedKind.Comment or EmbeddedKind.Description) && comment is not null)
            (type, data) = CommentChunk(comment, kind == EmbeddedKind.Description ? "Description" : "Comment");
        if (kind == EmbeddedKind.IccProfile && profile is not null)
        {
            IccProfiles.Exact(profile);
            Budget(profile.Length);
        }
        await Verify(asynchronous, token).ConfigureAwait(false);
        // A written profile and an sRGB chunk are two statements about one thing: the chunk goes.
        PngChunk[] removed = scan.Chunks.Where(chunk => kind switch
        {
            EmbeddedKind.Comment => chunk.Comment,
            EmbeddedKind.Description => chunk.Description || chunk.Comment,
            _ => chunk.Type == "iCCP" || (profile is not null && chunk.Type == "sRGB"),
        }).ToArray();
        // Nothing to take out and nothing to put in: the file is copied as it is.
        if (removed.Length == 0 && comment is null && profile is null) return null;
        if (profile is not null)
        {
            if (removed.Length == 1 && removed[0].Type == "iCCP" && await Holds(removed[0], profile, asynchronous, token).ConfigureAwait(false))
                return null;
            type = "iCCP";
            data = [.. Encoding.Latin1.GetBytes(IccProfileName), 0, 0, .. Deflate(profile)];
        }
        if (scan.Chunks.Any(chunk => chunk.UnsafeUnknown))
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "An unknown unsafe-to-copy PNG chunk prevents editing.");
        EmbeddedPlan plan = Splice(kind, removed, type, data, comment, profile);
        await VerifyEmbedded(plan, asynchronous, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return plan;
    }

    /// <summary>
    /// The chunk for a comment: tEXt when the text is ISO-8859-1 and reads back as itself, iTXt in
    /// UTF-8 otherwise. A reader that tries UTF-8 first takes the ISO-8859-1 bytes C3 A9 for one
    /// character, so a text of those two characters is written as iTXt.
    /// </summary>
    private (string Type, byte[] Data) CommentChunk(string comment, string keyword)
    {
        // A reader drops the zero bytes a comment ends with and lists no empty comment.
        if (comment.Length == 0)
            throw new ArgumentException("A comment cannot be empty; null removes the comment.", nameof(comment));
        if (comment.Contains('\0'))
            throw new ArgumentException("A comment cannot hold the character U+0000.", nameof(comment));
        byte[] data;
        string type;
        byte[]? narrow = comment.All(character => character <= 0xFF) ? Encoding.Latin1.GetBytes(comment) : null;
        if (narrow is not null && Comments.Decode(narrow) == comment)
        {
            type = "tEXt";
            data = [.. Encoding.ASCII.GetBytes(keyword), 0, .. narrow];
        }
        else
        {
            byte[] wide;
            try { wide = StrictUtf8.GetBytes(comment); }
            catch (EncoderFallbackException) { throw new ArgumentException("The comment is not well-formed UTF-16.", nameof(comment)); }
            type = "iTXt";
            // The keyword, not compressed, the method, no language, no translated keyword.
            data = [.. Encoding.ASCII.GetBytes(keyword), 0, 0, 0, 0, 0, .. wide];
        }
        Budget(data.Length);
        return (type, data);
    }

    // Keep Deflate focused on PNG and its format-specific validation contract.
    private static byte[] Deflate(byte[] bytes)
    {
        using var packed = new MemoryStream();
        using (var zlib = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(bytes, 0, bytes.Length);
        return packed.ToArray();
    }

    /// <summary>The profile an iCCP chunk holds, or null when the chunk is not read within the budget.</summary>
    private static byte[]? Inflated(byte[] data, int maximum, CancellationToken token)
    {
        int name = Array.IndexOf(data, (byte)0);
        if (name is < 1 or > 79 || data.Length - name < 2 || data[name + 1] != 0) return null;
        try { return BoundedZlib.Expand(data[(name + 2)..], maximum, token); }
        catch (MetadataException) { return null; }
    }

    /// <summary>Whether a chunk of the source holds exactly this profile.</summary>
    private async Task<bool> Holds(PngChunk chunk, byte[] profile, bool asynchronous, CancellationToken token)
    {
        if (chunk.Size > options.Limits.MaxRetainedMetadataBytes) return false;
        byte[] data = await Payload(chunk, asynchronous, token).ConfigureAwait(false);
        return Inflated(data, profile.Length, token) is byte[] held && held.AsSpan().SequenceEqual(profile);
    }

    /// <summary>
    /// The source without the removed chunks and with the new one at its place: where the first
    /// removed comment or the removed iCCP chunk stood, else before the first IDAT chunk for a
    /// comment and after the IHDR chunk for a profile.
    /// </summary>
    private EmbeddedPlan Splice(EmbeddedKind kind, PngChunk[] removed, string? type, byte[]? data, string? comment, byte[]? profile)
    {
        byte[]? written = data is null ? null : Crc.Chunk(type!, data);
        PngChunk? first = removed.FirstOrDefault(chunk => chunk.Type != "sRGB");
        long at = first?.Start ?? (kind != EmbeddedKind.IccProfile ? scan.Insertion : scan.Chunks[0].Start + scan.Chunks[0].Length);
        var output = new PlannedOutput();
        long cursor = 0;
        bool placed = written is null;
        foreach (PngChunk chunk in scan.Chunks)
        {
            if (!placed && chunk.Start == at)
            {
                output.Copy(cursor, chunk.Start - cursor);
                output.Add(written!);
                cursor = chunk.Start;
                placed = true;
            }
            if (Array.IndexOf(removed, chunk) >= 0)
            {
                output.Copy(cursor, chunk.Start - cursor);
                cursor = chunk.Start + chunk.Length;
            }
        }
        output.Copy(cursor, scan.Length - cursor);
        if (!placed) throw Refuse("the place of the new chunk is not where a chunk begins");
        return new EmbeddedPlan(output, kind, removed, type, data, comment, profile);
    }

    private static MetadataException Refuse(string what) =>
        new(MetadataErrorCode.UnsafeRelocation, "The planned output failed verification and was not written: " + what + ".");

    /// <summary>
    /// Reads the planned output as a file, before it is written, and holds it against the source.
    /// The plan is not trusted. The output has to scan as a PNG within the same budgets, which
    /// checks every checksum and the order of the chunks; its chunks of the edited kind have to be
    /// exactly the one planned, or none; every other chunk has to be a chunk of the source that the
    /// plan did not remove, in the source's order, with the same type, length and checksum; and
    /// the written chunk, read back and decoded the way a reader decodes it, has to give the
    /// comment or the profile.
    /// </summary>
    private async Task VerifyEmbedded(EmbeddedPlan plan, bool asynchronous, CancellationToken token)
    {
        await using Stream planned = plan.Output.Open(source);
        PngScan after;
        try
        {
            after = await PngScanner.Scan(planned, options.Limits, asynchronous, token).ConfigureAwait(false);
        }
        catch (MetadataException failure)
        {
            if (failure.Code == MetadataErrorCode.LimitExceeded)
                throw new MetadataException(failure.Code, "The edited file exceeds a budget of the limits it would be read with: " + failure.Message);
            throw Refuse("the output does not scan as a PNG");
        }
        if (after.Length != plan.Output.Length) throw Refuse("the output does not have the planned length");
        bool Edited(PngChunk chunk) => plan.Kind switch
        {
            EmbeddedKind.Comment => chunk.Comment,
            EmbeddedKind.Description => chunk.Description || chunk.Comment,
            _ => chunk.Type == "iCCP",
        };
        PngChunk[] kept = scan.Chunks.Where(chunk => Array.IndexOf(plan.Removed, chunk) < 0).ToArray();
        PngChunk[] same = after.Chunks.Where(chunk => !Edited(chunk)).ToArray();
        if (kept.Length != same.Length) throw Refuse("a chunk the edit did not name is missing or new");
        for (int index = 0; index < kept.Length; index++)
        {
            PngChunk was = kept[index], now = same[index];
            if (was.Type != now.Type || was.Size != now.Size || was.Checksum != now.Checksum || was.Xmp != now.Xmp ||
                was.UnsafeUnknown != now.UnsafeUnknown || was.Profile != now.Profile || was.Comment != now.Comment ||
                was.Description != now.Description)
                throw Refuse("a chunk the edit did not name changed");
        }
        PngChunk[] written = after.Chunks.Where(Edited).ToArray();
        if (written.Length != (plan.Data is null ? 0 : 1)) throw Refuse("the output does not hold the planned number of chunks");
        if (plan.Data is not byte[] data) return;
        PngChunk added = written[0];
        if (added.Type != plan.Type || added.Size != data.Length) throw Refuse("the new chunk does not have the planned type and length");
        byte[] read = new byte[data.Length];
        planned.Position = added.Payload;
        await ContainerIo.Read(planned, read, asynchronous, token).ConfigureAwait(false);
        if (!read.AsSpan().SequenceEqual(data)) throw Refuse("the new chunk does not hold the planned bytes");
        if (plan.Profile is byte[] profile)
        {
            if (Inflated(read, profile.Length, token) is not byte[] held || !held.AsSpan().SequenceEqual(profile))
                throw Refuse("the new chunk does not inflate to the profile");
        }
        else
        {
            // After the keyword: nothing for tEXt; iTXt adds compression, language, and translation fields.
            int text = Encoding.ASCII.GetByteCount(plan.Kind == EmbeddedKind.Description ? "Description" : "Comment") +
                1 + (added.Type == "tEXt" ? 0 : 4);
            if (read.Length <= text || Comments.Decode(read.AsSpan(text)) != plan.Comment)
                throw Refuse("the new chunk does not read back as the comment");
        }
    }
}
