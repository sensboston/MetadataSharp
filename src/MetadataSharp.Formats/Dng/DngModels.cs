using MetadataSharp.Core;
using MetadataSharp.Formats.Tiff;

namespace MetadataSharp.Formats.Dng;

/// <summary>A DNG version number, as DNGVersion (0xC612) and DNGBackwardVersion (0xC613) store it.</summary>
/// <param name="Major">The first byte.</param>
/// <param name="Minor">The second byte.</param>
/// <param name="Patch">The third byte.</param>
/// <param name="Build">The fourth byte.</param>
public readonly record struct DngVersion(byte Major, byte Minor, byte Patch, byte Build);

/// <summary>The role of a directory in the accepted DNG profile, from its NewSubFileType (254).</summary>
public enum DngImageRole
{
    /// <summary>The directory has no role in the profile.</summary>
    Unknown,
    /// <summary>The raw image: colour filter array data, NewSubFileType 0.</summary>
    CfaRaw,
    /// <summary>A reduced RGB image, NewSubFileType 1.</summary>
    RgbThumbnail
}

/// <summary>Why a file is outside the accepted DNG profile.</summary>
public enum DngLayoutIssueCode
{
    /// <summary>DNGVersion or DNGBackwardVersion is not a version the profile accepts.</summary>
    UnsupportedVersion,
    /// <summary>A field the profile requires is absent.</summary>
    MissingRequiredField,
    /// <summary>A field does not have the type and count the profile requires.</summary>
    UnsupportedFieldShape,
    /// <summary>A field has the required shape and a value the profile does not accept.</summary>
    InvalidSemanticValue,
    /// <summary>A directory, or a link between directories, has no place in the profile.</summary>
    UnsupportedDirectoryRole,
    /// <summary>The compression is not one the profile accepts.</summary>
    UnsupportedCompression,
    /// <summary>The sample layout is outside the profile; tiles are.</summary>
    UnsupportedSampleLayout,
    /// <summary>A field is not on the list the profile accepts for the role of its directory.</summary>
    UnsupportedExtension
}

/// <summary>One reason a file is outside the accepted DNG profile.</summary>
public sealed class DngLayoutIssue
{
    internal DngLayoutIssue(DngLayoutIssueCode code, uint directoryOffset, ushort? tagId, string message)
    { Code = code; DirectoryOffset = directoryOffset; TagId = tagId; Message = message; }
    /// <summary>The kind of issue.</summary>
    public DngLayoutIssueCode Code { get; }
    /// <summary>The offset of the directory the issue was found in.</summary>
    public uint DirectoryOffset { get; }
    /// <summary>The tag the issue is about, or null when it concerns the directory as a whole.</summary>
    public ushort? TagId { get; }
    /// <summary>A description of the issue in English.</summary>
    public string Message { get; }
}

/// <summary>The image a DNG directory describes and where its data is.</summary>
public sealed class DngImageLayout
{
    internal DngImageLayout(DngImageRole role, uint width, uint height, ushort bitsPerSample, ushort samplesPerPixel, IReadOnlyList<TiffByteRange> strips)
    { Role = role; Width = width; Height = height; BitsPerSample = bitsPerSample; SamplesPerPixel = samplesPerPixel; Strips = strips; }
    /// <summary>The role of the directory.</summary>
    public DngImageRole Role { get; }
    /// <summary>ImageWidth (256).</summary>
    public uint Width { get; }
    /// <summary>ImageLength (257).</summary>
    public uint Height { get; }
    /// <summary>BitsPerSample (258) of one sample.</summary>
    public ushort BitsPerSample { get; }
    /// <summary>SamplesPerPixel (277).</summary>
    public ushort SamplesPerPixel { get; }
    /// <summary>The strips of image data, in the order of StripOffsets (273).</summary>
    public IReadOnlyList<TiffByteRange> Strips { get; }
}

/// <summary>One directory of a DNG file.</summary>
public sealed class DngDirectoryInfo
{
    // Capture validated inputs and stable state for this TIFF/DNG object.
    internal DngDirectoryInfo(uint offset, bool isRoot, DngImageLayout? image, IReadOnlyList<TiffFieldInfo> fields, IReadOnlyList<TiffDirectoryLink> links)
    { Offset = offset; IsRoot = isRoot; Image = image; Fields = fields; Links = links; }
    /// <summary>The offset of the directory in the file.</summary>
    public uint Offset { get; }
    /// <summary>True for the first directory of the file.</summary>
    public bool IsRoot { get; }
    /// <summary>The image the directory describes, or null when none could be established.</summary>
    public DngImageLayout? Image { get; }
    /// <summary>The entries, in directory order.</summary>
    public IReadOnlyList<TiffFieldInfo> Fields { get; }
    /// <summary>The pointers that leave the directory.</summary>
    public IReadOnlyList<TiffDirectoryLink> Links { get; }
}
