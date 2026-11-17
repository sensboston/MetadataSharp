using System.Buffers;
using System.Security.Cryptography;
using MetadataSharp.Core;

namespace MetadataSharp.Formats.Internal;

// Represent the ContainerIo state used by metadata parsing and output.
internal static class ContainerIo
{
    // Read metadata data only after the input extent and applicable resource budgets are checked.
    internal static async ValueTask Read(Stream source, Memory<byte> bytes, bool asynchronous, CancellationToken token)
    {
        while (!bytes.IsEmpty)
        {
            token.ThrowIfCancellationRequested();
            int count = asynchronous ? await source.ReadAsync(bytes, token).ConfigureAwait(false) : source.Read(bytes.Span);
            if (count == 0) throw new IOException("Unexpected end of container input.");
            bytes = bytes[count..];
        }
    }
    internal static async ValueTask Write(Stream output, ReadOnlyMemory<byte> bytes, bool asynchronous, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (asynchronous) await output.WriteAsync(bytes, token).ConfigureAwait(false); else output.Write(bytes.Span);
    }
    // Take a stable copy without exposing mutable metadata state.
    internal static async Task Copy(Stream input, Stream output, long start, long length, int capacity, bool asynchronous, CancellationToken token)
    {
        input.Position = start; byte[] buffer = ArrayPool<byte>.Shared.Rent(capacity);
        try
        {
            while (length > 0)
            {
                int count = (int)Math.Min(length, capacity);
                await Read(input, buffer.AsMemory(0, count), asynchronous, token).ConfigureAwait(false);
                await Write(output, buffer.AsMemory(0, count), asynchronous, token).ConfigureAwait(false); length -= count;
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
    // Keep Hash focused on metadata and its format-specific validation contract.
    internal static async Task<byte[]> Hash(Stream source, long length, int capacity, bool asynchronous, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (source.Length != length) throw Changed();
        source.Position = 0; byte[] buffer = ArrayPool<byte>.Shared.Rent(capacity);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long remaining = length;
            while (remaining > 0)
            {
                int count = (int)Math.Min(capacity, remaining);
                await Read(source, buffer.AsMemory(0, count), asynchronous, token).ConfigureAwait(false);
                hash.AppendData(buffer, 0, count); remaining -= count;
            }
            token.ThrowIfCancellationRequested(); if (source.Length != length) throw Changed();
            return hash.GetHashAndReset();
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
    internal static MetadataException Changed() => new(MetadataErrorCode.SourceChanged, "The source changed since opening.");
    // Keep Output focused on metadata and its format-specific validation contract.
    internal static void Output(Stream source, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (ReferenceEquals(source, destination) || !destination.CanWrite || !destination.CanSeek || destination.Position != 0 || destination.Length != 0)
            throw new ArgumentException("Destination must be distinct, writable, seekable, empty and at zero.", nameof(destination));
    }
    // Keep Target focused on metadata and its format-specific validation contract.
    internal static string Target(string path, string? sourcePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(path); string target = Path.GetFullPath(path);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if ((sourcePath is not null && string.Equals(target, sourcePath, comparison)) || File.Exists(target) || Directory.Exists(target))
            throw new IOException("Destination must be a new path distinct from the source.");
        return target;
    }
    // Keep Publish focused on metadata and its format-specific validation contract.
    internal static async Task Publish(string target, int buffer, bool asynchronous, CancellationToken token, Func<Stream, Task> emit)
    {
        token.ThrowIfCancellationRequested(); string temporary = Path.Combine(Path.GetDirectoryName(target)!, ".metadatasharp-" + Guid.NewGuid().ToString("N") + ".tmp"); bool created = false;
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer, asynchronous ? FileOptions.Asynchronous : FileOptions.None))
            {
                created = true; await emit(file).ConfigureAwait(false);
                if (asynchronous) await file.FlushAsync(token).ConfigureAwait(false); else file.Flush();
            }
            token.ThrowIfCancellationRequested(); File.Move(temporary, target, overwrite: false); created = false;
        }
        finally { if (created) { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } } }
    }
}
