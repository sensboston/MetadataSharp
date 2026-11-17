namespace MetadataSharp.Core.Internal;

/// <summary>
/// The bytes of one file, read on demand rather than all at once. A reader asks for the range it
/// needs and gets exactly that: an offset, a length, and no assumption that the rest is anywhere in
/// memory. Every range is checked against <see cref="Length"/> before it is read, so a hostile
/// structure cannot make this type read outside the file.
/// </summary>
/// <remarks>
/// <para>
/// Two sources exist: one over bytes the caller already holds (<see cref="FromMemory"/>), which copies
/// nothing, and one over a seekable stream (<see cref="FromStream"/>), which reads in blocks and keeps
/// the blocks it loaded for the lifetime of the source. A block is never reused for other content, so a
/// span handed out by <see cref="ByteView.Slice(int, int)"/> stays valid and unchanged even after the
/// cache stops holding it.
/// </para>
/// <para>
/// No concurrent use is supported, as everywhere else in this library. The byte counters are for
/// tests and benchmarks: they say what was actually taken from the underlying source, which is the
/// number a reader that only wants a header has to keep small.
/// </para>
/// </remarks>
internal abstract class ByteSource
{
    /// <summary>The length of the file, in bytes, from the first byte a read can see.</summary>
    internal abstract long Length { get; }

    /// <summary>How many bytes were taken from the underlying source, block loads included.</summary>
    internal long BytesRead { get; private set; }

    /// <summary>How many reads of the underlying source were made, block loads included.</summary>
    internal long ReadCalls { get; private set; }

    /// <summary>Charges one read of the underlying source to the counters.</summary>
    /// <param name="bytes">The number of bytes the read took.</param>
    protected void Counted(int bytes)
    {
        BytesRead += bytes;
        ReadCalls++;
    }

    /// <summary>
    /// Copies <paramref name="destination"/>.Length bytes from <paramref name="offset"/>.
    /// </summary>
    /// <param name="offset">The first byte to copy, from the start of the file.</param>
    /// <param name="destination">The bytes that receive the range.</param>
    /// <exception cref="InvalidOperationException">
    /// The range lies outside the file. A reader checks every offset against <see cref="Length"/> before
    /// it asks, as it always had to against the length of the array it was given, so this is a defect in
    /// the reader and not something input can cause.
    /// </exception>
    internal abstract void Read(long offset, Span<byte> destination);

    /// <summary>True when the range lies inside the file.</summary>
    /// <param name="offset">The first byte of the range.</param>
    /// <param name="length">The length of the range.</param>
    /// <returns>True when the whole range can be read.</returns>
    internal bool Holds(long offset, long length) => offset >= 0 && length >= 0 && offset <= Length - length;

    /// <summary>Reads a range into a new array, after the range is checked.</summary>
    /// <param name="offset">The first byte of the range.</param>
    /// <param name="length">The length of the range.</param>
    /// <returns>The bytes of the range.</returns>
    internal byte[] Copy(long offset, long length)
    {
        if (!Holds(offset, length) || length > Array.MaxLength) throw Outside(offset, length);
        byte[] bytes = new byte[length];
        Read(offset, bytes);
        return bytes;
    }

    /// <summary>The whole file as one array, for the readers that still need it.</summary>
    /// <returns>The bytes of the file.</returns>
    internal byte[] ToArray() => Copy(0, Length);

    /// <summary>The first bytes of the file, for a signature, without reading past the end.</summary>
    /// <param name="length">How many bytes are wanted.</param>
    /// <returns>Between zero and <paramref name="length"/> bytes.</returns>
    internal ReadOnlySpan<byte> Head(int length)
    {
        int count = (int)Math.Min(Math.Clamp(length, 0, int.MaxValue), Length);
        return Slice(0, count);
    }

    /// <summary>A view of the whole file.</summary>
    /// <returns>The view.</returns>
    internal ByteView Whole() => ByteView.Over(this, 0, Length);

    /// <summary>
    /// A span over a range, from a block that is never changed afterwards, so the span can outlive the
    /// call and be held by the caller for as long as it needs it.
    /// </summary>
    /// <param name="offset">The first byte of the range.</param>
    /// <param name="length">The length of the range.</param>
    /// <returns>The bytes of the range.</returns>
    internal abstract ReadOnlySpan<byte> Slice(long offset, int length);

    /// <summary>The failure a range outside the file gives, with the offset it asked for.</summary>
    /// <param name="offset">The first byte of the range.</param>
    /// <param name="length">The length of the range.</param>
    /// <returns>The exception to throw.</returns>
    internal InvalidOperationException Outside(long offset, long length) =>
        new($"The read asked for [{offset}, {offset + length}), outside a file of {Length} bytes.");

    /// <summary>A source over bytes the caller holds: nothing is copied and nothing is counted as read.</summary>
    /// <param name="bytes">The whole file.</param>
    /// <returns>The source.</returns>
    internal static ByteSource FromMemory(ReadOnlyMemory<byte> bytes) => new MemorySource(bytes);

    /// <summary>A source over a seekable stream, positioned at the first byte of the file.</summary>
    /// <param name="stream">The seekable, readable stream to read from.</param>
    /// <param name="limits">The budgets: the input budget bounds the file; source blocks are 4 KiB.</param>
    /// <param name="cancellationToken">Ends the read between blocks.</param>
    /// <returns>The source.</returns>
    internal static ByteSource FromStream(Stream stream, MetadataLimits limits, CancellationToken cancellationToken) =>
        new StreamSource(stream, limits, cancellationToken);

    // A source over bytes already in memory: nothing to cache and nothing to check against a stream.
    private sealed class MemorySource : ByteSource
    {
        private readonly ReadOnlyMemory<byte> bytes;

        internal MemorySource(ReadOnlyMemory<byte> bytes) { this.bytes = bytes; }

        internal override long Length => bytes.Length;

        internal override void Read(long offset, Span<byte> destination)
        {
            if (!Holds(offset, destination.Length)) throw Outside(offset, destination.Length);
            bytes.Span.Slice((int)offset, destination.Length).CopyTo(destination);
        }

        internal override ReadOnlySpan<byte> Slice(long offset, int length) =>
            Holds(offset, length) ? bytes.Span.Slice((int)offset, length) : throw Outside(offset, length);
    }

    // A source over a seekable stream: blocks are read once, kept while the source lives, and counted.
    private sealed class StreamSource : ByteSource
    {
        private readonly Stream stream;
        private readonly long origin;
        private readonly int block;
        private readonly CancellationToken token;
        private readonly Dictionary<long, byte[]> loaded = [];
        private readonly List<byte[]> held = [];

        internal StreamSource(Stream stream, MetadataLimits limits, CancellationToken token)
        {
            this.stream = stream;
            this.token = token;
            origin = stream.Position;
            Length = stream.Length - origin;
            if (Length < 0) throw new ArgumentException("The stream is positioned past its end.", nameof(stream));
            block = 4_096;
        }

        internal override long Length { get; }

        internal override void Read(long offset, Span<byte> destination)
        {
            if (!Holds(offset, destination.Length)) throw Outside(offset, destination.Length);
            token.ThrowIfCancellationRequested();
            if (destination.IsEmpty) return;
            long first = offset / block, last = (offset + destination.Length - 1) / block;
            if (first == last)
            {
                loaded.TryGetValue(first, out byte[]? bytes);
                bytes ??= Load(first);
                bytes.AsSpan((int)(offset - first * block), destination.Length).CopyTo(destination);
                return;
            }
            long at2 = offset;
            while (!destination.IsEmpty)
            {
                long index = at2 / block;
                loaded.TryGetValue(index, out byte[]? bytes);
                bytes ??= Load(index);
                int within = (int)(at2 - index * block);
                int count = Math.Min(bytes.Length - within, destination.Length);
                bytes.AsSpan(within, count).CopyTo(destination);
                destination = destination[count..];
                at2 += count;
            }
        }

        internal override ReadOnlySpan<byte> Slice(long offset, int length)
        {
            if (!Holds(offset, length)) throw Outside(offset, length);
            token.ThrowIfCancellationRequested();
            if (length == 0) return [];
            long first = offset / block, last = (offset + length - 1) / block;
            if (first == last)
            {
                loaded.TryGetValue(first, out byte[]? bytes);
                bytes ??= Load(first);
                return bytes.AsSpan((int)(offset - first * block), length);
            }
            // A range that spans blocks is copied once into an array of its own. That array is held for
            // the lifetime of the source, so the span is valid for as long as the caller may use it.
            byte[] copy = new byte[length];
            Read(offset, copy);
            held.Add(copy);
            return copy;
        }

        private byte[] Load(long index)
        {
            long at = index * block;
            int count = (int)Math.Min(block, Length - at);
            byte[] bytes = new byte[count];
            Fill(at, bytes);
            loaded[index] = bytes;
            held.Add(bytes);
            return bytes;
        }

        // One read of the underlying stream, counted, with the position checked first.
        private void Fill(long offset, Span<byte> destination)
        {
            token.ThrowIfCancellationRequested();
            stream.Position = origin + offset;
            int filled = 0;
            while (filled < destination.Length)
            {
                int count = stream.Read(destination[filled..]);
                if (count == 0)
                    throw new IOException("The stream ended before the length it reported.");
                filled += count;
            }
            Counted(destination.Length);
        }
    }
}

/// <summary>
/// A window on a file: the whole of it, a range of it, or the bytes the caller already holds. Reads go
/// through the source, so a reader written against this type touches only the ranges it asks for.
/// </summary>
/// <remarks>
/// The type is a <c>ref struct</c> so that the bytes-in-memory case costs nothing: a view over a span
/// slices that span and never copies. A view over a source hands out spans of blocks the source keeps,
/// so a returned span stays valid for as long as the caller holds it.
/// </remarks>
internal readonly ref struct ByteView
{
    private readonly ReadOnlySpan<byte> memory;
    private readonly ByteSource? source;
    private readonly long origin;

    private ByteView(ReadOnlySpan<byte> memory, ByteSource? source, long origin, int length)
    {
        this.memory = memory;
        this.source = source;
        this.origin = origin;
        Length = length;
    }

    /// <summary>The number of bytes in this window.</summary>
    internal int Length { get; }

    /// <summary>True when the window is empty.</summary>
    internal bool IsEmpty => Length == 0;

    /// <summary>A view over bytes the caller holds; nothing is copied.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The view.</returns>
    internal static ByteView Over(ReadOnlySpan<byte> bytes) => new(bytes, null, 0, bytes.Length);

    /// <summary>A view over a range of a source.</summary>
    /// <param name="source">The source.</param>
    /// <param name="origin">The first byte of the range.</param>
    /// <param name="length">The length of the range.</param>
    /// <returns>The view.</returns>
    internal static ByteView Over(ByteSource source, long origin, long length) =>
        new(default, source, origin, checked((int)length));

    // A view over a range of this one, without reading anything yet.
    private ByteView SliceView(int start, int length)
    {
        if (start < 0 || length < 0 || start > Length - length)
            throw Outside(start, length);
        return source is null
            ? new(memory.Slice(start, length), null, 0, length)
            : new(default, source, origin + start, length);
    }

    /// <summary>The bytes of a range, as a span of the window.</summary>
    /// <param name="start">The first byte of the range, within the window.</param>
    /// <param name="length">The length of the range.</param>
    /// <returns>The bytes.</returns>
    internal ReadOnlySpan<byte> Slice(int start, int length)
    {
        Check(start, length);
        // The window may itself begin at an offset of the source: the range is read where it lies.
        return source is null ? memory.Slice(start, length) : source.Slice(origin + start, length);
    }

    /// <summary>The bytes from <paramref name="start"/> to the end of the window.</summary>
    /// <param name="start">The first byte of the range, within the window.</param>
    /// <returns>The bytes.</returns>
    internal ReadOnlySpan<byte> Slice(int start) => Slice(start, Length - start);

    /// <summary>The bytes of a range, so that <c>view[a..b]</c> reads as it does on a span.</summary>
    /// <param name="range">The range, within the window.</param>
    /// <value>The bytes of the range.</value>
    internal ReadOnlySpan<byte> this[Range range]
    {
        get
        {
            (int start, int length) = range.GetOffsetAndLength(Length);
            return Slice(start, length);
        }
    }

    /// <summary>One byte of the window.</summary>
    /// <param name="index">The position of the byte, within the window.</param>
    /// <value>The byte.</value>
    internal byte this[int index] =>
        source is null ? memory[index] : source.Slice(origin + Check(index, 1), 1)[0];

    /// <summary>A window on a range of this window, without reading anything yet.</summary>
    /// <param name="start">The first byte of the range, within the window.</param>
    /// <param name="length">The length of the range.</param>
    /// <returns>The view.</returns>
    internal ByteView View(int start, int length) => SliceView(start, length);

    /// <summary>A window on the rest of this window.</summary>
    /// <param name="start">The first byte of the range, within the window.</param>
    /// <returns>The view.</returns>
    internal ByteView View(int start) => SliceView(start, Length - start);

    /// <summary>
    /// A read-only stream over the same bytes, for a container document that walks its own structures
    /// through a stream: it reads what it seeks to and nothing else.
    /// </summary>
    /// <returns>The stream, positioned at the first byte of the window.</returns>
    internal Stream OpenStream() => source is null
        ? new MemoryStream(memory.ToArray(), writable: false)
        : new ByteSourceStream(source, origin, Length);

    /// <summary>The bytes of the window as one array.</summary>
    /// <returns>A copy of the bytes.</returns>
    internal byte[] ToArray() => source is null ? memory.ToArray() : source.Copy(origin, Length);

    /// <summary>Where a byte first occurs in the window.</summary>
    /// <param name="value">The byte to look for.</param>
    /// <returns>The position, or -1 when the window does not hold it.</returns>
    internal int IndexOf(byte value)
    {
        if (source is null) return memory.IndexOf(value);
        const int Step = 65_536;
        for (int at = 0; at < Length; at += Step)
        {
            int index = Slice(at, Math.Min(Step, Length - at)).IndexOf(value);
            if (index >= 0) return at + index;
        }
        return -1;
    }

    /// <summary>Where a run of bytes first occurs in the window.</summary>
    /// <param name="value">The bytes to look for.</param>
    /// <returns>The position, or -1 when the window does not hold them.</returns>
    internal int IndexOf(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty) return 0;
        if (source is null) return memory.IndexOf(value);
        const int Step = 65_536;
        int overlap = Math.Max(1, value.Length - 1);
        for (int at = 0; at < Length; at += Step - overlap)
        {
            int index = Slice(at, Math.Min(Step, Length - at)).IndexOf(value);
            if (index >= 0) return at + index;
        }
        return -1;
    }

    /// <summary>Whether the window begins with the bytes given.</summary>
    /// <param name="prefix">The bytes to look for.</param>
    /// <returns>True when the first bytes of the window are those bytes.</returns>
    internal bool StartsWith(ReadOnlySpan<byte> prefix) =>
        Length >= prefix.Length && Slice(0, prefix.Length).SequenceEqual(prefix);

    private long Check(int start, int length)
    {
        if (start < 0 || length < 0 || start > Length - length) throw Outside(start, length);
        return start;
    }

    private InvalidOperationException Outside(int start, int length) =>
        source is null
            ? new InvalidOperationException($"The read asked for [{start}, {start + length}), outside a window of {Length} bytes.")
            : source.Outside(origin + start, length);
}

/// <summary>
/// A read-only, seekable stream over a range of a <see cref="ByteSource"/>. It is what the container
/// documents take: they were written against a stream and walk their structures through it, so handing
/// them this instead of a copy of the file costs the bytes they ask for and nothing more.
/// </summary>
internal sealed class ByteSourceStream : Stream
{
    private readonly ByteSource source;
    private readonly long origin;
    private readonly long length;
    private long position;

    /// <summary>A stream over the whole of a source.</summary>
    /// <param name="source">The source to read.</param>
    internal ByteSourceStream(ByteSource source) : this(source, 0, source.Length) { }

    /// <summary>A stream over a range of a source, ending where the range ends.</summary>
    /// <param name="source">The source to read.</param>
    /// <param name="origin">The first byte of the range.</param>
    /// <param name="length">The length of the range.</param>
    internal ByteSourceStream(ByteSource source, long origin, long length)
    {
        this.source = source;
        this.origin = origin;
        this.length = length;
    }

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => true;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => length;

    /// <inheritdoc />
    public override long Position
    {
        get => position;
        set => position = value;
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        long left = length - position;
        if (left <= 0) return 0;
        int count = (int)Math.Min(buffer.Length, left);
        if (count == 0) return 0;
        source.Read(origin + position, buffer[..count]);
        position += count;
        return count;
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => origin switch
    {
        SeekOrigin.Begin => position = offset,
        SeekOrigin.Current => position += offset,
        SeekOrigin.End => position = length + offset,
        _ => throw new ArgumentOutOfRangeException(nameof(origin)),
    };

    /// <inheritdoc />
    public override void Flush() { }

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException("The stream is read-only.");

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("The stream is read-only.");
}
