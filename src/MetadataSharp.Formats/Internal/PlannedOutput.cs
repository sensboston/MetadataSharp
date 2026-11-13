namespace MetadataSharp.Formats.Internal;

/// <summary>
/// An output before it is written: runs of the source and new bytes, in the order they are to
/// stand. It can be read as a file, so that a scanner can be run over it, and then written.
/// </summary>
internal sealed class PlannedOutput
{
    /// <summary><c>Length</c> bytes of the source from <c>Start</c>, or, when <c>Bytes</c> is given, those bytes.</summary>
    private readonly record struct Run(long Start, long Length, byte[]? Bytes);

    private readonly List<Run> runs = [];

    /// <summary>The length of the output.</summary>
    internal long Length { get; private set; }

    /// <summary>Appends a range of the source; an empty range adds nothing.</summary>
    internal void Copy(long start, long length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (length == 0) return;
        runs.Add(new Run(start, length, null));
        Length += length;
    }

    /// <summary>Appends new bytes; an empty array adds nothing.</summary>
    internal void Add(byte[] bytes)
    {
        if (bytes.Length == 0) return;
        runs.Add(new Run(0, bytes.Length, bytes));
        Length += bytes.Length;
    }

    /// <summary>The output as a stream that can be read and repositioned. It reads the source and does not own it.</summary>
    internal Stream Open(Stream source) => new View(source, runs, Length);

    /// <summary>Writes the output.</summary>
    internal async Task Emit(Stream source, Stream output, int capacity, bool asynchronous, CancellationToken token)
    {
        foreach (Run run in runs)
        {
            if (run.Bytes is byte[] bytes) await ContainerIo.Write(output, bytes, asynchronous, token).ConfigureAwait(false);
            else await ContainerIo.Copy(source, output, run.Start, run.Length, capacity, asynchronous, token).ConfigureAwait(false);
        }
    }

    private sealed class View(Stream source, List<Run> runs, long length) : Stream
    {
        private long position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;

        // Retain Position state required to parse or write metadata data safely.
        public override long Position
        {
            get => position;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                position = value;
            }
        }

        /// <summary>The run that holds the position and how far into it the position is; false at the end.</summary>
        private bool Locate(out Run run, out long within)
        {
            long start = 0;
            foreach (Run candidate in runs)
            {
                if (position < start + candidate.Length)
                {
                    run = candidate;
                    within = position - start;
                    return true;
                }
                start += candidate.Length;
            }
            run = default;
            within = 0;
            return false;
        }

        // The source is read the way ContainerIo reads it: through the span overload when
        // synchronous, through the memory overload when not, and through no other.
        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty || !Locate(out Run run, out long within)) return 0;
            int count = (int)Math.Min(buffer.Length, run.Length - within);
            if (run.Bytes is byte[] bytes)
            {
                bytes.AsSpan((int)within, count).CopyTo(buffer);
            }
            else
            {
                source.Position = run.Start + within;
                count = source.Read(buffer[..count]);
            }
            position += count;
            return count;
        }

        // Read metadata data only after the input extent and applicable resource budgets are checked.
        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            return Read(buffer.AsSpan(offset, count));
        }

        // Read async from bounded metadata data, validating its declared extent before consuming fields.
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (buffer.IsEmpty || !Locate(out Run run, out long within)) return 0;
            int count = (int)Math.Min(buffer.Length, run.Length - within);
            if (run.Bytes is byte[] bytes)
            {
                bytes.AsMemory((int)within, count).CopyTo(buffer);
            }
            else
            {
                source.Position = run.Start + within;
                count = await source.ReadAsync(buffer[..count], cancellationToken).ConfigureAwait(false);
            }
            position += count;
            return count;
        }

        // Read async from bounded metadata data, validating its declared extent before consuming fields.
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateBufferArguments(buffer, offset, count);
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        // Keep Seek focused on metadata and its format-specific validation contract.
        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => position + offset, _ => length + offset };
            return position;
        }

        public override void Flush() { }
        // Keep SetLength focused on metadata and its format-specific validation contract.
        public override void SetLength(long value) => throw new NotSupportedException();
        // Serialize metadata in its defined byte order without disturbing adjacent data.
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
