using MetadataSharp.Core;

namespace MetadataSharp.Formats.Tiff;

/// <summary>Finite read-only TIFF framing and accounting budgets, captured before I/O.</summary>
public sealed record TiffReadOptions
{
    public MetadataLimits Limits { get; init; } = new();
    public int MaxDirectoryLinks { get; init; } = 4096;
    public int MaxAccountedRanges { get; init; } = 16384;
    internal TiffReadOptions Snapshot()
    {
        ArgumentNullException.ThrowIfNull(Limits); Limits.Validate();
        if (MaxDirectoryLinks <= 0 || MaxAccountedRanges <= 0) throw new ArgumentOutOfRangeException(nameof(TiffReadOptions));
        return this with { Limits = Limits with { } };
    }
}

/// <summary>An owned immutable accounting snapshot, not a pixel decoder or relocation certificate.</summary>
public sealed class TiffLayout
{
    internal TiffLayout(Endianness order, long length, uint first, TiffDirectoryInfo[] directories,
        TiffAccountedRange[] ranges, TiffLayoutIssue[] issues)
    { ByteOrder = order; SourceLength = length; FirstIfdOffset = first; Directories = Array.AsReadOnly(directories); Ranges = Array.AsReadOnly(ranges); Issues = Array.AsReadOnly(issues); }
    public Endianness ByteOrder { get; }
    public long SourceLength { get; }
    public uint FirstIfdOffset { get; }
    public IReadOnlyList<TiffDirectoryInfo> Directories { get; }
    public IReadOnlyList<TiffAccountedRange> Ranges { get; }
    public IReadOnlyList<TiffLayoutIssue> Issues { get; }
    public bool IsSupported => Issues.Count == 0 && Directories.Any(d => d.Image is not null) && Directories.All(d => d.Image?.IsSupported != false);
    public static TiffLayout Read(Stream source, TiffReadOptions? options = null) => TiffLayoutReader.Read(source, (options ?? new()).Snapshot(), false, default).GetAwaiter().GetResult();
    public static TiffLayout Read(string path, TiffReadOptions? options = null) => PathRead(path, (options ?? new()).Snapshot(), false, default).GetAwaiter().GetResult();
    public static Task<TiffLayout> ReadAsync(Stream source, TiffReadOptions? options = null, CancellationToken cancellationToken = default) => TiffLayoutReader.Read(source, (options ?? new()).Snapshot(), true, cancellationToken);
    public static Task<TiffLayout> ReadAsync(string path, TiffReadOptions? options = null, CancellationToken cancellationToken = default) => PathRead(path, (options ?? new()).Snapshot(), true, cancellationToken);
    private static async Task<TiffLayout> PathRead(string path, TiffReadOptions options, bool asynchronous, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrEmpty(path); token.ThrowIfCancellationRequested();
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, options.Limits.CopyBufferBytes, asynchronous ? FileOptions.Asynchronous : FileOptions.None);
        return await TiffLayoutReader.Read(file, options, asynchronous, token).ConfigureAwait(false);
    }
}
