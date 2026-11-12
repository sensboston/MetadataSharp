using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Formats.Internal;

namespace MetadataSharp.Formats.Jpeg;

public sealed partial class JpegMetadataDocument
{
    /// <summary>The most a COM segment holds: the largest segment length, less the two bytes of the length itself.</summary>
    internal const int MaxCommentBytes = 65_533;

    /// <summary>The most one APP2 segment holds of a profile: a COM payload less the identifier, the number and the count.</summary>
    internal const int MaxIccChunkBytes = 65_519;

    /// <summary>The count of the chunks is one byte.</summary>
    internal const int MaxIccChunks = 255;

    /// <summary>The least a chunk holds of a profile: exiv2 stops reading the segments of a file at a shorter one.</summary>
    internal const int MinIccChunkBytes = 4;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// Writes a copy of the file whose comment is <paramref name="comment"/>, or that has no comment
    /// when it is null. Every COM segment before the first scan is taken out; the new one stands
    /// where the first of them stood, or, in a file that had none, directly before the first
    /// segment that is not an application segment. The text is written as UTF-8 without a
    /// terminator. Every other byte of the file is copied: the other segments, the image data and
    /// whatever follows the image. <see cref="Comments.Read"/> returns the comment of the result.
    /// </summary>
    /// <param name="destination">A writable, seekable, empty stream that is not the source.</param>
    /// <param name="comment">The comment, or null for none.</param>
    /// <exception cref="ArgumentException">
    /// The comment is empty, holds the character U+0000 or is not well-formed UTF-16: a reader would
    /// not give such a text back. The destination is not as described.
    /// </exception>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.CommentTooLarge"/> when the UTF-8 bytes of the comment are more
    /// than the 65,533 one COM segment holds.
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when they exceed
    /// <see cref="MetadataLimits.MaxRetainedMetadataBytes"/>, or the result would have more
    /// segments than <see cref="MetadataLimits.MaxSegments"/>.
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> when bytes that belong to no segment come
    /// before the place of the comment, so that a reader that stops at them would not find it.
    /// <see cref="MetadataErrorCode.SourceChanged"/> when the source is not the file that was opened.
    /// <see cref="MetadataErrorCode.UnsafeRelocation"/> when the planned result, read again before
    /// anything is written, is not the source with this one change.
    /// </exception>
    public void SaveComment(Stream destination, string? comment) =>
        SaveEmbedded(destination, EmbeddedKind.Comment, comment, null, false, default).GetAwaiter().GetResult();

    /// <inheritdoc cref="SaveComment(Stream, string)"/>
    public Task SaveCommentAsync(Stream destination, string? comment, CancellationToken cancellationToken = default) =>
        SaveEmbedded(destination, EmbeddedKind.Comment, comment, null, true, cancellationToken);

    /// <summary>As <see cref="SaveComment(Stream, string)"/>, to a new file that is published only when it is complete.</summary>
    public void SaveCommentAs(string destinationPath, string? comment) =>
        SaveEmbeddedPath(destinationPath, EmbeddedKind.Comment, comment, null, false, default).GetAwaiter().GetResult();

    /// <inheritdoc cref="SaveCommentAs(string, string)"/>
    public Task SaveCommentAsAsync(string destinationPath, string? comment, CancellationToken cancellationToken = default) =>
        SaveEmbeddedPath(destinationPath, EmbeddedKind.Comment, comment, null, true, cancellationToken);

    /// <summary>
    /// Writes a copy of the file whose ICC profile is <paramref name="profile"/>, or that has no
    /// profile when it is null. Every APP2 <c>ICC_PROFILE</c> segment before the first scan is taken
    /// out, whether or not they made a whole profile. The new profile is cut into chunks of at most
    /// 65,519 bytes, numbered from one, each with the count of all, in order and next to each other
    /// directly after the APP0 and APP1 segments the file starts with. Every chunk but the last is
    /// full, except that the last two share the bytes when the last would hold fewer than four:
    /// exiv2 stops reading the segments of a file at a shorter chunk. Every other byte of the file
    /// is copied: the other segments, the image data and whatever follows the image.
    /// <see cref="IccProfiles.Read"/> returns the profile of the result. The profile is not
    /// interpreted; the array is copied before the method returns.
    /// </summary>
    /// <param name="destination">A writable, seekable, empty stream that is not the source.</param>
    /// <param name="profile">The whole profile, or null for none.</param>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidIccProfile"/> when the profile is shorter than the 128
    /// bytes of a header, its size field is not the number of bytes given, or the <c>acsp</c>
    /// signature is absent.
    /// <see cref="MetadataErrorCode.IccProfileTooLarge"/> when it needs more than 255 chunks, that
    /// is, when it is longer than 16,707,345 bytes.
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when it exceeds
    /// <see cref="MetadataLimits.MaxRetainedMetadataBytes"/>, or the result would have more
    /// segments than <see cref="MetadataLimits.MaxSegments"/>.
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> when bytes that belong to no segment come
    /// before the place of the profile, so that a reader that stops at them would not find it.
    /// <see cref="MetadataErrorCode.SourceChanged"/> when the source is not the file that was opened.
    /// <see cref="MetadataErrorCode.UnsafeRelocation"/> when the planned result, read again before
    /// anything is written, is not the source with this one change.
    /// </exception>
    public void SaveIccProfile(Stream destination, byte[]? profile) =>
        SaveEmbedded(destination, EmbeddedKind.IccProfile, null, Copy(profile), false, default).GetAwaiter().GetResult();

    /// <inheritdoc cref="SaveIccProfile(Stream, byte[])"/>
    public Task SaveIccProfileAsync(Stream destination, byte[]? profile, CancellationToken cancellationToken = default) =>
        SaveEmbedded(destination, EmbeddedKind.IccProfile, null, Copy(profile), true, cancellationToken);

    /// <summary>As <see cref="SaveIccProfile(Stream, byte[])"/>, to a new file that is published only when it is complete.</summary>
    public void SaveIccProfileAs(string destinationPath, byte[]? profile) =>
        SaveEmbeddedPath(destinationPath, EmbeddedKind.IccProfile, null, Copy(profile), false, default).GetAwaiter().GetResult();

    /// <inheritdoc cref="SaveIccProfileAs(string, byte[])"/>
    public Task SaveIccProfileAsAsync(string destinationPath, byte[]? profile, CancellationToken cancellationToken = default) =>
        SaveEmbeddedPath(destinationPath, EmbeddedKind.IccProfile, null, Copy(profile), true, cancellationToken);

    private enum EmbeddedKind { Comment, IccProfile }

    /// <summary>
    /// One run of the planned output: <c>Length</c> bytes of the source from <c>Start</c>, or, when
    /// <c>Bytes</c> is given, those bytes.
    /// </summary>
    private readonly record struct Piece(long Start, long Length, byte[]? Bytes);

    /// <summary>The planned output and what it is to hold: the segments written, one payload each.</summary>
    private sealed record EmbeddedPlan(List<Piece> Pieces, long Length, EmbeddedKind Kind, byte[][] Payloads);

    private static byte[]? Copy(byte[]? bytes) => bytes is null ? null : (byte[])bytes.Clone();

    private async Task SaveEmbedded(Stream destination, EmbeddedKind kind, string? comment, byte[]? profile, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ContainerIo.Output(source, destination);
        EmbeddedPlan? plan = await PrepareEmbedded(kind, comment, profile, asynchronous, token).ConfigureAwait(false);
        await EmitEmbedded(destination, plan, asynchronous, token).ConfigureAwait(false);
    }

    private async Task SaveEmbeddedPath(string destinationPath, EmbeddedKind kind, string? comment, byte[]? profile, bool asynchronous, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        string target = ContainerIo.Target(destinationPath, sourcePath);
        EmbeddedPlan? plan = await PrepareEmbedded(kind, comment, profile, asynchronous, token).ConfigureAwait(false);
        await ContainerIo.Publish(target, limits.CopyBufferBytes, asynchronous, token,
            output => EmitEmbedded(output, plan, asynchronous, token)).ConfigureAwait(false);
    }

    /// <summary>
    /// Everything that can refuse, before a byte is written: the content, the source, the plan, and
    /// the planned output read again. Null when the output is the source as it is.
    /// </summary>
    private async Task<EmbeddedPlan?> PrepareEmbedded(EmbeddedKind kind, string? comment, byte[]? profile, bool asynchronous, CancellationToken token)
    {
        byte[][] payloads = kind == EmbeddedKind.Comment ? CommentPayloads(comment) : IccPayloads(profile);
        token.ThrowIfCancellationRequested();
        byte[] current = await Hash(source, scan.Length, limits.CopyBufferBytes, asynchronous, token).ConfigureAwait(false);
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(digest, current))
            throw new MetadataException(MetadataErrorCode.SourceChanged, "The source content has changed since opening.");
        JpegSegment[] removed = scan.Segments.Where(segment => IsTarget(scan, segment, kind)).ToArray();
        // Nothing to take out and nothing to put in: the file is copied as it is.
        if (removed.Length == 0 && payloads.Length == 0) return null;
        EmbeddedPlan plan = Splice(kind, removed, payloads);
        await VerifyEmbedded(plan, asynchronous, token).ConfigureAwait(false);
        return plan;
    }

    /// <summary>The segments an edit of this kind replaces: those of the kind before the first scan.</summary>
    private static bool IsTarget(JpegScanResult scanned, JpegSegment segment, EmbeddedKind kind) =>
        segment.Start < scanned.ScanOffset && (kind == EmbeddedKind.Comment ? segment.Marker == 0xFE : segment.IsIccProfile);

    private byte[][] CommentPayloads(string? comment)
    {
        if (comment is null) return [];
        // A reader drops the zero bytes a comment ends with and lists no empty comment.
        if (comment.Length == 0)
            throw new ArgumentException("A comment cannot be empty; null removes the comment.", nameof(comment));
        if (comment.Contains('\0'))
            throw new ArgumentException("A comment cannot hold the character U+0000.", nameof(comment));
        byte[] bytes;
        try { bytes = StrictUtf8.GetBytes(comment); }
        catch (EncoderFallbackException) { throw new ArgumentException("The comment is not well-formed UTF-16.", nameof(comment)); }
        if (bytes.Length > MaxCommentBytes)
            throw new MetadataException(MetadataErrorCode.CommentTooLarge, "The comment exceeds the 65,533 bytes one COM segment holds.");
        if (bytes.Length > limits.MaxRetainedMetadataBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The comment exceeds the retained metadata budget.");
        return [bytes];
    }

    private byte[][] IccPayloads(byte[]? profile)
    {
        if (profile is null) return [];
        IccProfiles.Exact(profile);
        if (profile.Length > MaxIccChunks * MaxIccChunkBytes)
            throw new MetadataException(MetadataErrorCode.IccProfileTooLarge, "The ICC profile needs more than the 255 APP2 segments a JPEG can number.");
        int count = (profile.Length + MaxIccChunkBytes - 1) / MaxIccChunkBytes;
        if (profile.Length > limits.MaxRetainedMetadataBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The ICC profile exceeds the retained metadata budget.");
        // Every chunk but the last is full, unless the last would hold fewer than four bytes: the
        // pinned exiv2 reads four bytes of each chunk and, at a shorter one, stops reading the
        // segments of the file, so that what follows the chunk (a comment, IPTC data, the frame)
        // is lost to it. The chunk before the last then gives up as many bytes as the last lacks.
        int tail = profile.Length - (count - 1) * MaxIccChunkBytes;
        int lent = count > 1 && tail < MinIccChunkBytes ? MinIccChunkBytes - tail : 0;
        byte[][] payloads = new byte[count][];
        for (int index = 0, at = 0; index < count; index++)
        {
            int length = index == count - 1 ? profile.Length - at : MaxIccChunkBytes - (index == count - 2 ? lent : 0);
            byte[] payload = new byte[14 + length];
            "ICC_PROFILE\0"u8.CopyTo(payload);
            payload[12] = (byte)(index + 1);
            payload[13] = (byte)count;
            profile.AsSpan(at, length).CopyTo(payload.AsSpan(14));
            payloads[index] = payload;
            at += length;
        }
        return payloads;
    }

    /// <summary>
    /// The source without the removed segments and with the new ones at their place. A comment goes
    /// where the first comment was, or before the first segment that is not an application segment.
    /// A profile goes after the APP0 and APP1 segments the file starts with; the chunks taken out
    /// are not counted among them, so a chunk between two of them does not decide the place.
    /// </summary>
    private EmbeddedPlan Splice(EmbeddedKind kind, JpegSegment[] removed, byte[][] payloads)
    {
        byte marker = kind == EmbeddedKind.Comment ? (byte)0xFE : (byte)0xE2;
        long at = 2;
        if (payloads.Length != 0)
        {
            if (kind == EmbeddedKind.Comment)
            {
                // There is always such a segment: the frame header, at the latest.
                at = removed.Length != 0 ? removed[0].Start : scan.Segments.First(segment => segment.Marker is < 0xE0 or > 0xEF).Start;
            }
            else
            {
                foreach (JpegSegment segment in scan.Segments)
                {
                    if (IsTarget(scan, segment, kind)) continue;
                    if (segment.Marker is not (0xE0 or 0xE1)) break;
                    at = segment.Start + segment.Length;
                }
            }
            // A reader that takes one segment after another stops at bytes that are none.
            long expected = 2;
            foreach (JpegSegment segment in scan.Segments)
            {
                if (segment.Start >= at) break;
                if (segment.Start != expected) break;
                expected = segment.Start + segment.Length;
            }
            if (expected != at)
                throw new MetadataException(MetadataErrorCode.UnsupportedFeature,
                    "Bytes that belong to no segment precede the place of the new segment; a reader that stops at them would not find it.", expected);
        }
        byte[] inserted = new byte[payloads.Sum(payload => 4L + payload.Length)];
        int to = 0;
        foreach (byte[] payload in payloads)
        {
            inserted[to] = 0xFF;
            inserted[to + 1] = marker;
            BinaryPrimitives.WriteUInt16BigEndian(inserted.AsSpan(to + 2), checked((ushort)(payload.Length + 2)));
            payload.CopyTo(inserted, to + 4);
            to += 4 + payload.Length;
        }
        var pieces = new List<Piece>();
        long cursor = 0, length = 0;
        bool placed = inserted.Length == 0;
        foreach (JpegSegment segment in removed)
        {
            if (!placed && at <= segment.Start)
            {
                Source(at);
                Place();
            }
            Source(segment.Start);
            cursor = segment.Start + segment.Length;
        }
        if (!placed)
        {
            Source(at);
            Place();
        }
        Source(scan.Length);
        return new EmbeddedPlan(pieces, length, kind, payloads);

        void Source(long end)
        {
            // The place is a segment boundary and no removed segment holds it, so the cursor never passes it.
            if (end < cursor) throw Refuse("the place of the new segment lies inside a removed one");
            if (end > cursor) { pieces.Add(new Piece(cursor, end - cursor, null)); length += end - cursor; }
            cursor = end;
        }

        void Place()
        {
            pieces.Add(new Piece(0, inserted.Length, inserted));
            length += inserted.Length;
            placed = true;
        }
    }

    private static MetadataException Refuse(string what) =>
        new(MetadataErrorCode.UnsafeRelocation, "The planned output failed verification and was not written: " + what + ".");

    /// <summary>
    /// Reads the planned output as a file, before it is written, and holds it against the source.
    /// The plan is not trusted. The output has to scan as a JPEG within the same budgets; its
    /// segments of the edited kind before the first scan have to be exactly the ones planned, next
    /// to each other, reached from the start of the file through segments alone, and hold the
    /// planned bytes; every other segment has to be a segment of the source, in the source's order,
    /// with the same marker, length and identity; and from the first scan to the end the output has
    /// to be as long as the source. The bytes of what is copied are the source's by construction.
    /// </summary>
    private async Task VerifyEmbedded(EmbeddedPlan plan, bool asynchronous, CancellationToken token)
    {
        using var planned = new PlannedStream(source, plan.Pieces, plan.Length);
        JpegScanResult after;
        try
        {
            after = asynchronous ? await JpegScanner.ScanAsync(planned, limits, token).ConfigureAwait(false) : JpegScanner.Scan(planned, limits);
        }
        catch (MetadataException failure)
        {
            if (failure.Code == MetadataErrorCode.LimitExceeded)
                throw new MetadataException(failure.Code, "The edited file exceeds a budget of the limits it would be read with: " + failure.Message);
            throw Refuse("the output does not scan as a JPEG");
        }
        if (after.Length != plan.Length) throw Refuse("the output does not have the planned length");
        if (after.ScanCount != scan.ScanCount || after.FrameCount != scan.FrameCount ||
            after.Length - after.ScanOffset != scan.Length - scan.ScanOffset || after.Length - after.EoiOffset != scan.Length - scan.EoiOffset)
            throw Refuse("the image data moved within the file or changed its length");
        JpegSegment[] kept = scan.Segments.Where(segment => !IsTarget(scan, segment, plan.Kind)).ToArray();
        JpegSegment[] same = after.Segments.Where(segment => !IsTarget(after, segment, plan.Kind)).ToArray();
        if (kept.Length != same.Length) throw Refuse("a segment the edit did not name is missing or new");
        for (int index = 0; index < kept.Length; index++)
        {
            JpegSegment was = kept[index], now = same[index];
            if (was.Marker != now.Marker || was.Length != now.Length || was.PayloadLength != now.PayloadLength || was.IsExif != now.IsExif ||
                was.IsStandardXmp != now.IsStandardXmp || was.IsExtendedXmp != now.IsExtendedXmp || was.IsPhotoshop != now.IsPhotoshop ||
                was.IsIccProfile != now.IsIccProfile)
                throw Refuse("a segment the edit did not name changed");
        }
        JpegSegment[] written = after.Segments.Where(segment => IsTarget(after, segment, plan.Kind)).ToArray();
        if (written.Length != plan.Payloads.Length) throw Refuse("the output does not hold the planned number of segments");
        if (written.Length == 0) return;
        long expected = 2;
        foreach (JpegSegment segment in after.Segments)
        {
            if (segment.Start != expected) throw Refuse("the new segments are not reached through segments alone");
            expected = segment.Start + segment.Length;
            if (ReferenceEquals(segment, written[^1])) break;
        }
        for (int index = 0; index < written.Length; index++)
        {
            JpegSegment segment = written[index];
            byte[] payload = plan.Payloads[index];
            if (index != 0 && segment.Start != written[index - 1].Start + written[index - 1].Length)
                throw Refuse("the new segments are not next to each other");
            if (segment.PayloadLength != payload.Length) throw Refuse("a new segment does not have the planned length");
            byte[] read = new byte[payload.Length];
            planned.Position = segment.PayloadOffset;
            await ReadExact(planned, read, asynchronous, token).ConfigureAwait(false);
            if (!read.AsSpan().SequenceEqual(payload)) throw Refuse("a new segment does not hold the planned bytes");
        }
    }

    private async Task EmitEmbedded(Stream output, EmbeddedPlan? plan, bool asynchronous, CancellationToken token)
    {
        if (plan is null)
        {
            await CopyRange(output, 0, scan.Length, asynchronous, token).ConfigureAwait(false);
            return;
        }
        foreach (Piece piece in plan.Pieces)
        {
            if (piece.Bytes is byte[] bytes) await Write(output, bytes, asynchronous, token).ConfigureAwait(false);
            else await CopyRange(output, piece.Start, piece.Length, asynchronous, token).ConfigureAwait(false);
        }
    }

    /// <summary>The planned output as a stream that can be read and repositioned: runs of the source and the new bytes, in order.</summary>
    private sealed class PlannedStream : Stream
    {
        private readonly Stream source;
        private readonly List<Piece> pieces;
        private readonly long length;
        private long position;

        internal PlannedStream(Stream source, List<Piece> pieces, long length)
        {
            this.source = source; this.pieces = pieces; this.length = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;

        public override long Position
        {
            get => position;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                position = value;
            }
        }

        /// <summary>The piece that holds the position and how far into it the position is; false at the end.</summary>
        private bool Locate(out Piece piece, out long within)
        {
            long start = 0;
            foreach (Piece candidate in pieces)
            {
                if (position < start + candidate.Length)
                {
                    piece = candidate;
                    within = position - start;
                    return true;
                }
                start += candidate.Length;
            }
            piece = default;
            within = 0;
            return false;
        }

        // The source is read the way the copy reads it: through the array overload, and no other.
        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, buffer.Length - offset);
            if (count <= 0 || !Locate(out Piece piece, out long within)) return 0;
            count = (int)Math.Min(count, piece.Length - within);
            if (piece.Bytes is byte[] bytes)
            {
                bytes.AsSpan((int)within, count).CopyTo(buffer.AsSpan(offset));
            }
            else
            {
                source.Position = piece.Start + within;
                count = source.Read(buffer, offset, count);
            }
            position += count;
            return count;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (buffer.IsEmpty || !Locate(out Piece piece, out long within)) return 0;
            int count = (int)Math.Min(buffer.Length, piece.Length - within);
            if (piece.Bytes is byte[] bytes)
            {
                bytes.AsMemory((int)within, count).CopyTo(buffer);
            }
            else
            {
                source.Position = piece.Start + within;
                count = await source.ReadAsync(buffer[..count], cancellationToken).ConfigureAwait(false);
            }
            position += count;
            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => position + offset, _ => length + offset };
            return position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
