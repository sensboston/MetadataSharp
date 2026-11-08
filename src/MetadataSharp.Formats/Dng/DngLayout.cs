using MetadataSharp.Core;
using MetadataSharp.Formats.Tiff;

namespace MetadataSharp.Formats.Dng;

/// <summary>An owned immutable DNG inventory snapshot. Not a decoder, renderer or relocation certificate.</summary>
public sealed class DngLayout
{
    internal DngLayout(Endianness byteOrder, long sourceLength, uint firstIfdOffset, DngVersion version, DngVersion backwardVersion,
        bool isSupported, DngDirectoryInfo[] directories, TiffAccountedRange[] ranges, DngLayoutIssue[] issues)
    {
        ByteOrder = byteOrder; SourceLength = sourceLength; FirstIfdOffset = firstIfdOffset;
        Version = version; BackwardVersion = backwardVersion; IsSupported = isSupported;
        Directories = Array.AsReadOnly(directories); Ranges = Array.AsReadOnly(ranges); Issues = Array.AsReadOnly(issues);
    }
    public Endianness ByteOrder { get; }
    public long SourceLength { get; }
    public uint FirstIfdOffset { get; }
    public DngVersion Version { get; }
    public DngVersion BackwardVersion { get; }
    public bool IsSupported { get; }
    public IReadOnlyList<DngDirectoryInfo> Directories { get; }
    public IReadOnlyList<TiffAccountedRange> Ranges { get; }
    public IReadOnlyList<DngLayoutIssue> Issues { get; }
    public static DngLayout Read(Stream source, DngReadOptions? options = null) => DngReader.Read(source, (options ?? new()).Snapshot(), false, default).GetAwaiter().GetResult();
    public static DngLayout Read(string path, DngReadOptions? options = null) => PathRead(path, (options ?? new()).Snapshot(), false, default).GetAwaiter().GetResult();
    public static Task<DngLayout> ReadAsync(Stream source, DngReadOptions? options = null, CancellationToken cancellationToken = default) => DngReader.Read(source, (options ?? new()).Snapshot(), true, cancellationToken);
    public static Task<DngLayout> ReadAsync(string path, DngReadOptions? options = null, CancellationToken cancellationToken = default) => PathRead(path, (options ?? new()).Snapshot(), true, cancellationToken);
    private static async Task<DngLayout> PathRead(string path, DngReadOptions options, bool asynchronous, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrEmpty(path); token.ThrowIfCancellationRequested();
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, options.TiffOptions.Limits.CopyBufferBytes, asynchronous ? FileOptions.Asynchronous : FileOptions.None);
        return await DngReader.Read(file, options, asynchronous, token).ConfigureAwait(false);
    }
}
