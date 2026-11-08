using MetadataSharp.Core;
using MetadataSharp.Formats.Tiff;

namespace MetadataSharp.Formats.Dng;

public readonly record struct DngVersion(byte Major, byte Minor, byte Patch, byte Build);

public enum DngImageRole { Unknown, CfaRaw, RgbThumbnail }

public enum DngLayoutIssueCode
{
    UnsupportedVersion,
    MissingRequiredField,
    UnsupportedFieldShape,
    InvalidSemanticValue,
    UnsupportedDirectoryRole,
    UnsupportedCompression,
    UnsupportedSampleLayout,
    UnsupportedExtension
}

public sealed class DngLayoutIssue
{
    internal DngLayoutIssue(DngLayoutIssueCode code, uint directoryOffset, ushort? tagId, string message)
    { Code = code; DirectoryOffset = directoryOffset; TagId = tagId; Message = message; }
    public DngLayoutIssueCode Code { get; }
    public uint DirectoryOffset { get; }
    public ushort? TagId { get; }
    public string Message { get; }
}

public sealed class DngImageLayout
{
    internal DngImageLayout(DngImageRole role, uint width, uint height, ushort bitsPerSample, ushort samplesPerPixel, IReadOnlyList<TiffByteRange> strips)
    { Role = role; Width = width; Height = height; BitsPerSample = bitsPerSample; SamplesPerPixel = samplesPerPixel; Strips = strips; }
    public DngImageRole Role { get; }
    public uint Width { get; }
    public uint Height { get; }
    public ushort BitsPerSample { get; }
    public ushort SamplesPerPixel { get; }
    public IReadOnlyList<TiffByteRange> Strips { get; }
}

public sealed class DngDirectoryInfo
{
    internal DngDirectoryInfo(uint offset, bool isRoot, DngImageLayout? image, IReadOnlyList<TiffFieldInfo> fields, IReadOnlyList<TiffDirectoryLink> links)
    { Offset = offset; IsRoot = isRoot; Image = image; Fields = fields; Links = links; }
    public uint Offset { get; }
    public bool IsRoot { get; }
    public DngImageLayout? Image { get; }
    public IReadOnlyList<TiffFieldInfo> Fields { get; }
    public IReadOnlyList<TiffDirectoryLink> Links { get; }
}
