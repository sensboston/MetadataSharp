using System.Buffers.Binary;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Internal;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Formats.Tiff;

namespace MetadataSharp.Formats.Raf;

/// <summary>
/// Read-only metadata of a Fujifilm RAF file. The file starts with a header (<c>FUJIFILM</c>) that
/// says where a whole JPEG file and, in later models, a TIFF structure lie in it. The metadata is
/// that of the embedded JPEG file, as the reference tool (Exiv2) has it; the tags of the primary
/// directory of the TIFF structure that the JPEG file does not have are added. The raw data is never
/// decoded and nothing is written, as the reference writes nothing to a RAF file.
/// </summary>
public sealed class RafMetadata
{
    private RafMetadata(ExifMetadata? exif, IptcPacket? iptc, XmpPacket? xmp, IReadOnlyList<string> comments,
        long jpegOffset, long jpegLength, long? tiffOffset, long tiffLength)
    {
        Exif = exif;
        Iptc = iptc;
        Xmp = xmp;
        Comments = comments;
        JpegOffset = jpegOffset;
        JpegLength = jpegLength;
        TiffOffset = tiffOffset;
        TiffLength = tiffLength;
    }

    /// <summary>
    /// The EXIF block of the embedded JPEG file, with a recognised MakerNote; offsets in it count as
    /// they do in that JPEG file. Tags of the TIFF structure that the block does not have follow
    /// when both are in one byte order, and stand alone when the JPEG file has no EXIF block. Null
    /// when neither has a tag. <see cref="ExifMetadata.IsEditable"/> is false.
    /// </summary>
    public ExifMetadata? Exif { get; }

    /// <summary>The IPTC datasets of the embedded JPEG file, or null.</summary>
    public IptcPacket? Iptc { get; }

    /// <summary>The XMP packet of the embedded JPEG file, or null.</summary>
    public XmpPacket? Xmp { get; }

    /// <summary>The comments of the embedded JPEG file; empty when it has none.</summary>
    public IReadOnlyList<string> Comments { get; }

    /// <summary>Where the embedded JPEG file starts.</summary>
    public long JpegOffset { get; }

    /// <summary>The length of the embedded JPEG file in bytes.</summary>
    public long JpegLength { get; }

    /// <summary>Where the TIFF structure starts, or null when the file has none there (older models hold raw data in its place).</summary>
    public long? TiffOffset { get; }

    /// <summary>The length the header gives for the TIFF structure; zero when the file has none.</summary>
    public long TiffLength { get; }

    /// <summary>Whether the bytes start as a RAF file does.</summary>
    internal static bool HasSignature(ReadOnlySpan<byte> header) => header.Length >= 8 && header[..8].SequenceEqual("FUJIFILM"u8);

    /// <summary>Reads one complete RAF file held in memory.</summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidRaf"/> when the file does not start as a RAF file, is
    /// shorter than its header, or the header places the JPEG file or the TIFF structure outside the
    /// file; <see cref="MetadataErrorCode.LimitExceeded"/> when a budget is exceeded; otherwise the
    /// code the EXIF, IPTC or XMP reader reports for what it refused. Bytes in the place of the
    /// JPEG file that are no JPEG file are not an error: the file then has no metadata from there.
    /// </exception>
    public static RafMetadata Read(ReadOnlySpan<byte> data, MetadataLimits? limits = null) => Read(data, limits, null);

    internal static RafMetadata Read(ReadOnlySpan<byte> data, MetadataLimits? limits, KindRefusals? refusals)
    {
        limits?.Validate();
        if (limits is not null && data.Length > limits.MaxInputBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The RAF file exceeds the input budget.", 0);
        if (!HasSignature(data)) throw Fail("The file does not start with the header of a RAF file.", 0);
        if (data.Length < 108) throw Fail("The RAF file is shorter than its header.", data.Length);
        long jpegOffset = BinaryPrimitives.ReadUInt32BigEndian(data[84..]), jpegLength = BinaryPrimitives.ReadUInt32BigEndian(data[88..]);
        if (jpegOffset + jpegLength > data.Length) throw Fail("The header of the RAF file places the JPEG file outside the file.", 84);
        if (jpegLength < 12) throw Fail("The JPEG file of the RAF file is shorter than a JPEG file is.", 88);
        long tiffOffset = BinaryPrimitives.ReadUInt32BigEndian(data[100..]), tiffLength = BinaryPrimitives.ReadUInt32BigEndian(data[104..]);
        if (tiffOffset + tiffLength > data.Length) throw Fail("The header of the RAF file places the TIFF structure outside the file.", 100);
        if (tiffOffset + 4 > data.Length) throw Fail("The header of the RAF file places the TIFF structure at the end of the file.", 100);

        ExifMetadata? exif = null;
        IptcPacket? iptc = null;
        XmpPacket? xmp = null;
        IReadOnlyList<string> comments = [];
        byte[] jpeg = data.Slice((int)jpegOffset, (int)jpegLength).ToArray();
        var inner = new KindRefusals();
        try
        {
            using JpegMetadataDocument document = JpegMetadataDocument.Open(new MemoryStream(jpeg, writable: false), limits, inner);
            exif = document.Exif;
            try { iptc = document.ReadIptc(); }
            catch (MetadataException refusal) { inner.Iptc = refusal; }
            try { xmp = document.ReadXmp(); }
            catch (MetadataException refusal) { inner.Xmp = refusal; }
            try { comments = MetadataSharp.Formats.Comments.Read(jpeg, limits); }
            catch (MetadataException) { comments = []; }
        }
        catch (MetadataException)
        {
            // Bytes that are no JPEG file give nothing, and the file is read all the same: the
            // reference does so, and a file may hold only a stub where the JPEG file would be.
        }

        ReadOnlySpan<byte> head = data.Slice((int)tiffOffset, 4);
        bool hasTiff = head.SequenceEqual("II*\0"u8) || head.SequenceEqual("MM\0*"u8);
        if (hasTiff)
        {
            try { exif = Merge(exif, TiffExif.Read(data.Slice((int)tiffOffset, (int)tiffLength), limits)); }
            catch (MetadataException refusal) { inner.Exif ??= refusal; }
        }
        if (refusals is null)
        {
            if ((inner.Exif ?? inner.Iptc ?? inner.Xmp) is MetadataException first) throw first;
        }
        else (refusals.Exif, refusals.Iptc, refusals.Xmp) = (inner.Exif, inner.Iptc, inner.Xmp);
        return new RafMetadata(exif, iptc, xmp, comments, jpegOffset, jpegLength, hasTiff ? tiffOffset : null, hasTiff ? tiffLength : 0);
    }

    /// <summary>The tags of the JPEG file, then those of the primary directory of the TIFF structure it does not have.</summary>
    private static ExifMetadata Merge(ExifMetadata? jpeg, ExifMetadata tiff)
    {
        var entries = new List<ExifEntry>();
        if (jpeg is not null)
        {
            entries.AddRange(jpeg.Entries);
            // A value keeps the byte order it was read in: tags of another order are left out.
            if (jpeg.ByteOrder != tiff.ByteOrder) return new ExifMetadata(jpeg.ByteOrder, entries, jpeg.MakerNote, editable: false);
        }
        foreach (ExifEntry entry in tiff.Entries)
            if (entry.Key.Directory == ExifDirectoryKind.Ifd0 && (jpeg is null || !jpeg.TryGetEntry(entry.Key, out _)))
                entries.Add(entry);
        return new ExifMetadata(tiff.ByteOrder, entries, jpeg?.MakerNote, editable: false);
    }

    private static MetadataException Fail(string message, long offset) => new(MetadataErrorCode.InvalidRaf, message, offset);
}
