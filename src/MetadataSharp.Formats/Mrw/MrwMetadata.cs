using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Internal;
using MetadataSharp.Formats.Tiff;

namespace MetadataSharp.Formats.Mrw;

/// <summary>
/// Read-only metadata of a Minolta MRW file. The file starts with a header block (<c>\0MRM</c>)
/// that holds a chain of blocks; the one named <c>TTW</c> is a TIFF structure with the directories
/// and the Minolta MakerNote, and it is read as a TIFF file is. The raw data is never decoded and
/// nothing is written, as the reference tool (Exiv2) writes nothing to an MRW file.
/// </summary>
public sealed class MrwMetadata
{
    // Capture validated inputs and stable state for this metadata object.
    private MrwMetadata(ExifMetadata? exif, IptcPacket? iptc, XmpPacket? xmp, long tiffOffset, long tiffLength)
    {
        Exif = exif;
        Iptc = iptc;
        Xmp = xmp;
        TiffOffset = tiffOffset;
        TiffLength = tiffLength;
    }

    /// <summary>The directories of the TTW block, with a recognised MakerNote. Offsets in it count from <see cref="TiffOffset"/>.</summary>
    public ExifMetadata? Exif { get; }

    /// <summary>The IPTC datasets the TTW block holds (tag 0x83BB, or the Photoshop resources of tag 0x8649), or null.</summary>
    public IptcPacket? Iptc { get; }

    /// <summary>The XMP packet of tag 700 of the primary directory of the TTW block, or null.</summary>
    public XmpPacket? Xmp { get; }

    /// <summary>Where the TIFF structure of the TTW block starts in the file.</summary>
    public long TiffOffset { get; }

    /// <summary>The length of the TTW block in bytes.</summary>
    public long TiffLength { get; }

    /// <summary>Whether the bytes start as an MRW file does.</summary>
    internal static bool HasSignature(ReadOnlySpan<byte> header) => header.Length >= 4 && header[..4].SequenceEqual("\0MRM"u8);

    /// <summary>Reads one complete MRW file held in memory.</summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidMrw"/> when the file does not start as an MRW file, the
    /// chain of blocks leaves the header block or the file, or there is no TTW block;
    /// <see cref="MetadataErrorCode.LimitExceeded"/> when a budget is exceeded; otherwise the code
    /// the TIFF, IPTC or XMP reader reports for what it refused.
    /// </exception>
    public static MrwMetadata Read(ReadOnlySpan<byte> data, MetadataLimits? limits = null) => Read(data, limits, null);

    // Read metadata data only after the input extent and applicable resource budgets are checked.
    internal static MrwMetadata Read(ReadOnlySpan<byte> data, MetadataLimits? limits, KindRefusals? refusals)
    {
        limits?.Validate();
        if (limits is not null && data.Length > limits.MaxInputBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The MRW file exceeds the input budget.", 0);
        (int start, int length) = Locate(data);
        ReadOnlySpan<byte> tiff = data.Slice(start, length);
        ExifMetadata? exif = null;
        IptcPacket? iptc = null;
        XmpPacket? xmp = null;
        try { exif = TiffExif.Read(tiff, limits); }
        catch (MetadataException refusal) when (refusals is not null) { refusals.Exif = refusal; refusals.Xmp = refusal; }
        try { iptc = TiffIptc.Read(tiff, limits); }
        catch (MetadataException refusal) when (refusals is not null) { refusals.Iptc = refusal; }
        try
        {
            if (exif is not null && exif.TryGetEntry(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x02BC), out ExifEntry? entry) && entry.Value.ByteLength != 0)
                xmp = XmpPacket.Parse(entry.Value.RawBytes);
        }
        catch (MetadataException refusal) when (refusals is not null) { refusals.Xmp = refusal; }
        return new MrwMetadata(exif, iptc, xmp, start, length);
    }

    /// <summary>
    /// The TTW block. The header block says where the raw data starts; the blocks before that are
    /// eight bytes each (a zero byte, three letters, the length) followed by their content.
    /// </summary>
    internal static (int Start, int Length) Locate(ReadOnlySpan<byte> data)
    {
        if (!HasSignature(data) || data.Length < 8) throw Fail("The file does not start with the header block of an MRW file.", 0);
        long end = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        long at = 8;
        for (int blocks = 0; ; blocks++)
        {
            // A block of no length would keep the chain where it is; this many blocks no camera writes.
            if (blocks > 4096) throw Fail("The header block of the MRW file holds more blocks than a file has.", at);
            if (at + 8 > end || at + 8 > data.Length) throw Fail("The blocks of the MRW file end without a TTW block.", at);
            long length = BinaryPrimitives.ReadUInt32BigEndian(data[((int)at + 4)..]);
            if (data.Slice((int)at + 1, 3).SequenceEqual("TTW"u8))
            {
                if (length > data.Length - (at + 8)) throw Fail("The TTW block of the MRW file is longer than the file.", at);
                return ((int)at + 8, (int)length);
            }
            if (length > end - (at + 8)) throw Fail("A block of the MRW file is longer than the header block that holds it.", at);
            at += 8 + length;
        }
    }

    // Preserve the format-specific error code and source offset for malformed metadata data.
    private static MetadataException Fail(string message, long offset) => new(MetadataErrorCode.InvalidMrw, message, offset);
}
