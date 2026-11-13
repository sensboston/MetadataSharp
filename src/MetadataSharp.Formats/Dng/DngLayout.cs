using MetadataSharp.Core;
using MetadataSharp.Formats.Tiff;

namespace MetadataSharp.Formats.Dng;

/// <summary>An owned immutable DNG inventory snapshot. Not a decoder, renderer or relocation certificate.</summary>
public sealed class DngLayout
{
    // Keep the validated DNG directory graph and input bounds for later layout queries.
    internal DngLayout(Endianness byteOrder, long sourceLength, uint firstIfdOffset, DngVersion version, DngVersion backwardVersion,
        bool isSupported, DngDirectoryInfo[] directories, TiffAccountedRange[] ranges, DngLayoutIssue[] issues)
    {
        ByteOrder = byteOrder; SourceLength = sourceLength; FirstIfdOffset = firstIfdOffset;
        Version = version; BackwardVersion = backwardVersion; IsSupported = isSupported;
        Directories = Array.AsReadOnly(directories); Ranges = Array.AsReadOnly(ranges); Issues = Array.AsReadOnly(issues);
    }
    /// <summary>The byte order of the file.</summary>
    public Endianness ByteOrder { get; }
    /// <summary>The length of the source in bytes.</summary>
    public long SourceLength { get; }
    /// <summary>The offset of the first directory, from the header.</summary>
    public uint FirstIfdOffset { get; }
    /// <summary>DNGVersion (0xC612) of the first directory.</summary>
    public DngVersion Version { get; }
    /// <summary>DNGBackwardVersion (0xC613) of the first directory; for a file without it, DNGVersion with its last two bytes zero.</summary>
    public DngVersion BackwardVersion { get; }
    /// <summary>True when the file is within the accepted DNG profile: there are no issues.</summary>
    public bool IsSupported { get; }
    /// <summary>Every directory reached from the header.</summary>
    public IReadOnlyList<DngDirectoryInfo> Directories { get; }
    /// <summary>Every range of the file the directories account for, in file order.</summary>
    public IReadOnlyList<TiffAccountedRange> Ranges { get; }
    /// <summary>The reasons, if any, the file is outside the accepted DNG profile.</summary>
    public IReadOnlyList<DngLayoutIssue> Issues { get; }
    /// <summary>Reads the inventory of a DNG from a readable, seekable stream at position zero, and leaves the stream open.</summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidDng"/> or <see cref="MetadataErrorCode.InvalidTiff"/> for a
    /// malformed file; <see cref="MetadataErrorCode.UnsupportedFeature"/> for a TIFF without
    /// DNGVersion in its first directory and for BigTIFF; <see cref="MetadataErrorCode.LimitExceeded"/>
    /// when a budget is exceeded.
    /// </exception>
    /// <param name="source">Readable, seekable TIFF/DNG input stream positioned at the start; it remains open after the read.</param>
    /// <param name="options">Settings for DNG layout parsing; null selects the defaults.</param>
    /// <returns>The parsed DngLayout for the complete input file.</returns>
    public static DngLayout Read(Stream source, DngReadOptions? options = null) => DngReader.Read(source, (options ?? new()).Snapshot(), false, default).GetAwaiter().GetResult();
    /// <summary>Reads the inventory of a file. Fails as <see cref="Read(Stream, DngReadOptions?)"/> does.</summary>
    /// <param name="path">Path of the TIFF/DNG file to identify or open.</param>
    /// <param name="options">Settings for DNG layout parsing; null selects the defaults.</param>
    /// <returns>The parsed DngLayout for the complete input file.</returns>
    public static DngLayout Read(string path, DngReadOptions? options = null) => PathRead(path, (options ?? new()).Snapshot(), false, default).GetAwaiter().GetResult();
    /// <inheritdoc cref="Read(Stream, DngReadOptions?)"/>
    public static Task<DngLayout> ReadAsync(Stream source, DngReadOptions? options = null, CancellationToken cancellationToken = default) => DngReader.Read(source, (options ?? new()).Snapshot(), true, cancellationToken);
    /// <inheritdoc cref="Read(string, DngReadOptions?)"/>
    public static Task<DngLayout> ReadAsync(string path, DngReadOptions? options = null, CancellationToken cancellationToken = default) => PathRead(path, (options ?? new()).Snapshot(), true, cancellationToken);
    // Keep PathRead focused on TIFF/DNG and its format-specific validation contract.
    private static async Task<DngLayout> PathRead(string path, DngReadOptions options, bool asynchronous, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrEmpty(path); token.ThrowIfCancellationRequested();
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, options.TiffOptions.Limits.CopyBufferBytes, asynchronous ? FileOptions.Asynchronous : FileOptions.None);
        return await DngReader.Read(file, options, asynchronous, token).ConfigureAwait(false);
    }
}
