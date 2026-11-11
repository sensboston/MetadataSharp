using MetadataSharp.Core;

namespace MetadataSharp.Formats.Tiff;

/// <summary>Finite read-only TIFF framing and accounting budgets, captured before I/O.</summary>
public sealed record TiffReadOptions
{
    /// <summary>
    /// The budgets of a whole TIFF-based file when the caller names none. Two of the defaults of
    /// <see cref="MetadataLimits"/> are sized for an EXIF block inside another container (65,527
    /// bytes is the most a JPEG APP1 segment holds), and a valid TIFF-based file exceeds them with
    /// one ICC profile, Photoshop block or DNG profile table: a value may be 16 MiB here, and
    /// 64 MiB may be retained in all.
    /// </summary>
    internal static MetadataLimits FileLimits { get; } = new() { MaxValueBytes = 16_777_216, MaxRetainedMetadataBytes = 67_108_864 };

    /// <summary>
    /// The budgets. Unless set, those of <see cref="MetadataLimits"/> with
    /// <see cref="MetadataLimits.MaxValueBytes"/> 16,777,216 and
    /// <see cref="MetadataLimits.MaxRetainedMetadataBytes"/> 67,108,864.
    /// </summary>
    public MetadataLimits Limits { get; init; } = FileLimits;
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
