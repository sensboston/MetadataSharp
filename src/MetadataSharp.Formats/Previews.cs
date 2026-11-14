using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using MetadataSharp.Core;

namespace MetadataSharp.Formats;

/// <summary>
/// One preview image embedded in a file: where its bytes are and what they are.
/// </summary>
/// <param name="MimeType">
/// <c>image/jpeg</c> for a JPEG stream; <c>image/tiff</c> for an embedded TIFF or a reduced uncompressed
/// image re-encoded as a standalone TIFF; <c>video/H265</c> or <c>video/AV1</c> for a coded BMFF
/// thumbnail item, whose encoded item bytes can be extracted without decoding.
/// </param>
/// <param name="Width">Width in pixels, when the preview or its container states it.</param>
/// <param name="Height">Height in pixels, when the preview or its container states it.</param>
/// <param name="Offset">Position of the first byte, from the start of the file.</param>
/// <param name="Length">Number of bytes.</param>
/// <param name="Source">The structure that points at the preview, for example <c>Exif.Thumbnail.JPEGInterchangeFormat</c>.</param>
public sealed record PreviewImage(string MimeType, int? Width, int? Height, long Offset, int Length, string Source);

/// <summary>
/// Read-only access to the preview images a file carries: the EXIF thumbnail, the JPEG previews
/// a MakerNote points at, the reduced images of a TIFF-based raw file, the thumbnail items of a
/// BMFF file and the thumbnail resource of a PSD. Pixels are not decoded and the input is never mutated;
/// encoded preview forms such as base64 or hex thumbnails can be transformed by <see cref="Extract"/>.
/// </summary>
public static class Previews
{
    internal const string JpegType = "image/jpeg";
    internal const string RawRgbType = "image/x-raw-rgb";
    internal const string HevcType = "video/H265";
    internal const string Av1Type = "video/AV1";

    /// <summary>
    /// List the previews of a complete file, smallest picture first. A pointer that leaves the
    /// file, overlaps the directory that declares it, or does not lead to the start of a JPEG
    /// stream is not listed; a file of an unknown kind has no previews.
    /// </summary>
    /// <exception cref="MetadataException"><see cref="MetadataErrorCode.LimitExceeded"/> when the file exceeds the input budget.</exception>
    /// <param name="file">Complete input image file bytes starting at the container signature.</param>
    /// <param name="limits">Resource budgets applied while parsing image file; null selects the default limits.</param>
    /// <returns>Preview records sorted from smallest image to largest.</returns>
    public static IReadOnlyList<PreviewImage> List(ReadOnlySpan<byte> file, MetadataLimits? limits = null)
    {
        limits = Checked(file, limits);
        var scanner = new PreviewScanner(file, limits);
        List<PreviewImage> found = scanner.Run();
        found.Sort(Compare);
        return found.AsReadOnly();
    }

    /// <summary>
    /// Return exactly the bytes of one preview. A JPEG preview must begin with SOI and end with EOI
    /// (zero padding after EOI is tolerated and returned); a range that runs past its picture is cut
    /// at the picture's last end-of-image marker, because the range is a length the container states
    /// and not the picture itself; uncompressed samples must have the length their dimensions imply.
    /// </summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.InvalidJpeg"/> or <see cref="MetadataErrorCode.InvalidTiff"/> when the
    /// preview lies outside the file or is not what it claims to be;
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> for a type that is only listed.
    /// </exception>
    /// <param name="file">Complete input image file bytes starting at the container signature.</param>
    /// <param name="preview">Preview record returned by <see cref="List"/> whose byte range is to extract.</param>
    /// <param name="limits">Resource budgets applied while parsing image file; null selects the default limits.</param>
    /// <returns>The selected preview bytes, including tolerated zero padding after a JPEG EOI marker, and cut at the last EOI marker when the stated range runs past it.</returns>
    public static byte[] Extract(ReadOnlySpan<byte> file, PreviewImage preview, MetadataLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(preview);
        limits = Checked(file, limits);
        if ((preview.Source is "EPS.AI7.Hex" or "EPS.Photoshop.Hex") ||
            preview.Source.StartsWith("Xmp.Thumbnail", StringComparison.Ordinal) ||
            preview.Source.StartsWith("PNG.RawExif.", StringComparison.Ordinal) ||
            (preview.MimeType == "image/tiff" && preview.Source.EndsWith(".StripOffsets", StringComparison.Ordinal)))
        {
            var scanner = new PreviewScanner(file, limits);
            _ = scanner.Run();
            if (scanner.TryGetGenerated(preview, out byte[]? generated) && generated is not null) return (byte[])generated.Clone();
            throw new MetadataException(MetadataErrorCode.InvalidTiff, "The generated preview no longer matches the file.", preview.Offset);
        }
        bool jpeg = preview.MimeType == JpegType;
        bool rgb = preview.MimeType == RawRgbType;
        bool direct = preview.MimeType is "image/tiff" or "image/x-wmf" or "image/x-portable-anymap" or
            "application/octet-stream" or "video/H265" or "video/AV1";
        if (!jpeg && !rgb && !direct)
            throw new MetadataException(MetadataErrorCode.UnsupportedFeature, "The preview media type has no extractor.", preview.Offset);
        MetadataErrorCode invalid = jpeg ? MetadataErrorCode.InvalidJpeg : MetadataErrorCode.InvalidTiff;
        if (preview.Offset < 0 || preview.Length <= 0 || preview.Offset > (long)file.Length - preview.Length)
            throw new MetadataException(invalid, "The preview lies outside the file.", preview.Offset);
        ReadOnlySpan<byte> bytes = file.Slice((int)preview.Offset, preview.Length);
        if (jpeg)
        {
            if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
                throw new MetadataException(MetadataErrorCode.InvalidJpeg, "The preview does not begin with a JPEG start-of-image marker.", preview.Offset);
            int last = bytes.Length;
            while (last > 4 && bytes[last - 1] == 0) last--;
            if (bytes[last - 2] != 0xFF || bytes[last - 1] != 0xD9)
            {
                // A preview's byte range is a length the container states, not the picture itself: a
                // range can run past its picture into what follows it. The picture ends at its last
                // end-of-image marker, and a range that holds none is not a JPEG.
                int end = LastEndOfImage(bytes);
                if (end < 4)
                    throw new MetadataException(MetadataErrorCode.InvalidJpeg, "The preview does not end with a JPEG end-of-image marker.", preview.Offset + preview.Length);
                return bytes[..end].ToArray();
            }
        }
        else if (rgb && (preview.Width is not int width || preview.Height is not int height || width <= 0 || height <= 0 ||
            (long)width * height * 3 != preview.Length))
        {
            throw new MetadataException(MetadataErrorCode.InvalidTiff, "The sample count does not match the dimensions of the preview.", preview.Offset);
        }
        return bytes.ToArray();
    }

    // Validate preview input limits before traversing any container-specific pointers.
    private static MetadataLimits Checked(ReadOnlySpan<byte> file, MetadataLimits? limits)
    {
        limits ??= new MetadataLimits();
        limits.Validate();
        if (file.Length > limits.MaxInputBytes)
            throw new MetadataException(MetadataErrorCode.LimitExceeded, "The file exceeds the input budget.", 0);
        return limits;
    }

    // The end of the last picture a byte range holds: in entropy-coded data a 0xFF byte is always
    // followed by 0x00 or a marker, so the last 0xFF 0xD9 pair is an end-of-image marker.
    private static int LastEndOfImage(ReadOnlySpan<byte> bytes)
    {
        for (int i = bytes.Length - 2; i >= 2; i--)
            if (bytes[i] == 0xFF && bytes[i + 1] == 0xD9)
                return i + 2;
        return 0;
    }

    private static long Area(PreviewImage preview) =>
        preview.Width is int width && preview.Height is int height ? (long)width * height : 0;

    // Sort previews by pixel area, then by byte offset for stable ordering.
    private static int Compare(PreviewImage left, PreviewImage right)
    {
        int order = Area(left).CompareTo(Area(right));
        if (order == 0) order = left.Length.CompareTo(right.Length);
        return order != 0 ? order : left.Offset.CompareTo(right.Offset);
    }
}

/// <summary>
/// One bounded pass over the structures that can point at a preview. Every position is checked
/// against the file before it is read, every count against <see cref="MetadataLimits"/>; a
/// structure that does not hold is left alone rather than reported.
/// </summary>
internal ref struct PreviewScanner
{
    private const int MaxPreviews = 64;
    private const int MaxStrips = 4096;

    private readonly ReadOnlySpan<byte> data;
    private readonly MetadataLimits limits;
    private readonly int nesting;
    private readonly List<PreviewImage> found;
    private int directories;
    private int subImages;
    private readonly Dictionary<(string Source, long Offset, int Length), byte[]> generated = new();

    // Retain the file span and limits while scanning preview pointers.
    internal PreviewScanner(ReadOnlySpan<byte> data, MetadataLimits limits, int nesting = 0)
    {
        this.data = data;
        this.limits = limits;
        this.nesting = nesting;
        found = new List<PreviewImage>();
        directories = 0;
        subImages = 0;
    }

    /// <summary>A TIFF structure: where offset zero is, where the region ends, and the byte order.</summary>
    private readonly record struct Frame(int Origin, int End, bool Little);

    /// <summary>A directory entry whose value lies inside the region; <c>At</c> is the position of the value in the file.</summary>
    private readonly record struct Entry(ushort Tag, ushort Type, uint Count, int At);

    // Keep Run focused on metadata and its format-specific validation contract.
    internal bool TryGetGenerated(PreviewImage preview, out byte[]? bytes) =>
        generated.TryGetValue((preview.Source, preview.Offset, preview.Length), out bytes);

    internal List<PreviewImage> Run()
    {
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8) Segments(2);
        else if (data.Length >= 7 && data[0] == 0xFF && data[1] == 0x01 && data.Slice(2, 5).SequenceEqual("Exiv2"u8)) Segments(7);
        else if (IsCrwHeader()) Crw();
        else if (data.Length >= 4 && data[..4].SequenceEqual("\0MRM"u8)) Mrw();
        else if (data.Length >= 8 && data[..8].SequenceEqual("FUJIFILM"u8)) Raf();
        else if (IsJp2Header()) Jp2();
        else if (MetadataSharp.Formats.Pgf.PgfMetadata.HasSignature(data)) Pgf();
        else if (IsRw2Header()) Tiff(0, data.Length, panasonicRaw: true);
        else if (IsTiffHeader()) Tiff(0, data.Length);
        else if (data.Length >= 4 && data[..4].SequenceEqual("8BPS"u8)) Psd();
        else if (data.Length >= 8 && data[0] == 0x89 && data.Slice(1, 3).SequenceEqual("PNG"u8)) Png();
        else if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8)) WebP();
        else if (data.Length >= 12 && data.Slice(4, 4).SequenceEqual("ftyp"u8)) Bmff();
        else if (data.Length >= 4 && (data[..4].SequenceEqual("IIRC"u8) || data[..4].SequenceEqual("MMCR"u8))) Tiff(0, data.Length);
        else if (data.Length >= 11 && data[..11].SequenceEqual("%!PS-Adobe-"u8)) Eps();
        else if (data.Length >= 4 && data[..4].SequenceEqual(new byte[] { 0xC5, 0xD0, 0xD3, 0xC6 })) Eps();
        else if (LooksLikeXmp()) XmpThumbnail();
        return found;
    }

    // TIFF itself, and the raw formats Exiv2 exposes as TIFF for preview discovery:
    // ORF ("IIRO", "IIRS", "MMOR"). RW2 has its own bounded path for Panasonic MakerNote previews.
    private bool IsRw2Header() => data.Length >= 8 && data[..4].SequenceEqual("IIU\0"u8);

    private bool IsTiffHeader()
    {
        if (data.Length < 8) return false;
        ReadOnlySpan<byte> head = data[..4];
        return head.SequenceEqual("II*\0"u8) || head.SequenceEqual("MM\0*"u8) || head.SequenceEqual("IIRC"u8) ||
            head.SequenceEqual("MMCR"u8) || head.SequenceEqual("IIRO"u8) ||
            head.SequenceEqual("IIRS"u8) || head.SequenceEqual("MMOR"u8);
    }

    // CRW and RAF expose their preview byte ranges through dedicated bounded readers.
    private bool IsCrwHeader() => data.Length >= 14 && data[0] == data[1] &&
        (data[0] == (byte)'I' || data[0] == (byte)'M') && data.Slice(6, 8).SequenceEqual("HEAPCCDR"u8);

    private static bool IsJp2Header(ReadOnlySpan<byte> bytes) => bytes.Length >= 12 &&
        bytes[..4].SequenceEqual(new byte[] { 0, 0, 0, 12 }) && bytes.Slice(4, 4).SequenceEqual("jP  "u8) &&
        bytes.Slice(8, 4).SequenceEqual(new byte[] { 13, 10, 135, 10 });

    private bool IsJp2Header() => IsJp2Header(data);

    private void Crw()
    {
        MetadataSharp.Formats.Crw.CrwMetadata metadata = MetadataSharp.Formats.Crw.CrwMetadata.Read(data, limits);
        if (metadata.ThumbnailOffset is long thumbnail)
            Jpeg(thumbnail, metadata.ThumbnailLength, "Exif.Thumbnail.JPEGInterchangeFormat", 0, 0);
        if (metadata.PreviewOffset is long preview)
            Jpeg(preview, metadata.PreviewLength, "Exif.Image2.JPEGInterchangeFormat", 0, 0);
    }

    private void Mrw()
    {
        (int start, int length) = MetadataSharp.Formats.Mrw.MrwMetadata.Locate(data);
        Tiff(start, start + length);
    }

    private void Raf()
    {
        MetadataSharp.Formats.Raf.RafMetadata metadata = MetadataSharp.Formats.Raf.RafMetadata.Read(data, limits);
        int jpegStart = checked((int)metadata.JpegOffset);
        int jpegEnd = checked(jpegStart + (int)metadata.JpegLength);
        if (jpegEnd <= data.Length && jpegEnd - jpegStart >= 4 && data[jpegStart] == 0xFF && data[jpegStart + 1] == 0xD8)
        {
            Segments(jpegStart + 2, jpegEnd);
            Jpeg(jpegStart, metadata.JpegLength, "Exif.Image2.JPEGInterchangeFormat", 0, 0);
        }
        if (metadata.TiffOffset is long tiff && metadata.TiffLength >= 8)
        {
            int start = checked((int)tiff);
            Tiff(start, checked(start + (int)metadata.TiffLength));
        }
    }

    private void Pgf()
    {
        if (data.Length < 24 || nesting >= limits.MaxDirectoryDepth) return;
        int regionStart = data[20] == 2 ? 24 + 256 * 3 : 24;
        long headerEnd = (long)BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) + 8;
        if (headerEnd < regionStart || headerEnd > data.Length) return;
        Nested(data.Slice(regionStart, (int)headerEnd - regionStart), regionStart);
    }

    // Scan a bounded embedded image while keeping every returned range in the outer file's coordinates.
    private void Nested(ReadOnlySpan<byte> bytes, int origin)
    {
        var embedded = new PreviewScanner(bytes, limits, nesting + 1);
        foreach (PreviewImage item in embedded.Run())
        {
            var preview = item with { Offset = item.Offset + origin };
            Add(preview);
            if (embedded.generated.TryGetValue((item.Source, item.Offset, item.Length), out byte[]? generatedBytes))
                generated[(preview.Source, preview.Offset, preview.Length)] = generatedBytes;
        }
    }

    private void Jp2()
    {
        // The JP2 Exif UUID payload contains a TIFF header (or an Exif prefix before it).
        ReadOnlySpan<byte> exifUuid = "JpgTiffExif->JP2"u8;
        int at = 0;
        for (int boxes = 0; boxes < 1024 && data.Length - at >= 8; boxes++)
        {
            uint shortLength = Be32(at);
            uint type = Be32(at + 4);
            int headerLength = 8;
            long length = shortLength;
            if (shortLength == 1)
            {
                if (data.Length - at < 16) return;
                ulong extendedLength = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(at + 8, 8));
                if (extendedLength > int.MaxValue) return;
                length = (long)extendedLength;
                headerLength = 16;
            }
            else if (shortLength == 0) length = data.Length - at;
            if (length < headerLength || length > data.Length - at) return;
            int end = at + (int)length;
            int uuidAt = at + headerLength;
            if (type == 0x75756964 && end - uuidAt >= 16 && data.Slice(uuidAt, 16).SequenceEqual(exifUuid))
            {
                int payload = uuidAt + 16;
                int tiff = payload;
                if (!HasTiffAt(tiff, end) && end - payload >= 4)
                {
                    uint relative = Be32(payload);
                    if (relative <= end - payload - 4) tiff = payload + 4 + (int)relative;
                }
                if (!HasTiffAt(tiff, end))
                {
                    int prefix = data.Slice(payload, end - payload).IndexOf("Exif\0\0"u8);
                    if (prefix >= 0) tiff = payload + prefix + 6;
                }
                if (HasTiffAt(tiff, end)) Tiff(tiff, end);
            }
            at = end;
        }
    }

    private bool HasTiffAt(int at, int end) => at >= 0 && end - at >= 8 &&
        (data.Slice(at, 4).SequenceEqual(new byte[] { 0x49, 0x49, 42, 0 }) ||
         data.Slice(at, 4).SequenceEqual(new byte[] { 0x4D, 0x4D, 0, 42 }));

    private bool LooksLikeXmp() => data.StartsWith("<?xpacket"u8) || data.StartsWith("<?xml"u8) ||
        (data.Length >= 10 && data[..10].SequenceEqual("<x:xmpmeta"u8));

    private void XmpThumbnail()
    {
        MetadataSharp.Core.Xmp.XmpPacket packet;
        try { packet = MetadataSharp.Core.Xmp.XmpPacket.Parse(data); }
        catch (MetadataException) { return; }
        var entries = packet.Entries.ToDictionary(entry => entry.Key, StringComparer.Ordinal);
        foreach (string prefix in new[] { "xmpGImg", "xapGImg" })
        {
            string root = "Xmp.xmp.Thumbnails[1]/" + prefix + ":";
            if (!entries.TryGetValue(root + "image", out MetadataSharp.Core.Xmp.XmpEntry? image) || image.Values.Count == 0 ||
                !entries.TryGetValue(root + "format", out MetadataSharp.Core.Xmp.XmpEntry? format) || format.Values.Count == 0 ||
                format.Values[0] != "JPEG" ||
                !entries.TryGetValue(root + "width", out MetadataSharp.Core.Xmp.XmpEntry? width) || width.Values.Count == 0 ||
                !entries.TryGetValue(root + "height", out MetadataSharp.Core.Xmp.XmpEntry? height) || height.Values.Count == 0 ||
                !int.TryParse(width.Values[0], out int w) || !int.TryParse(height.Values[0], out int h) || w <= 0 || h <= 0)
                continue;
            byte[] jpeg;
            try { jpeg = Convert.FromBase64String(image.Values[0]); }
            catch (FormatException) { continue; }
            if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8 || jpeg[^2] != 0xFF || jpeg[^1] != 0xD9) continue;
            int storedAt = data.IndexOf(Encoding.ASCII.GetBytes(image.Values[0]));
            if (storedAt < 0) continue;
            var preview = new PreviewImage(Previews.JpegType, w, h, storedAt, jpeg.Length, "Xmp.Thumbnail[1]");
            generated[(preview.Source, preview.Offset, preview.Length)] = jpeg;
            Add(preview);
            return;
        }
    }

    private void Eps()
    {
        // DOS EPS sections are byte-preserving native previews; the PostScript body is never executed.
        if (data.Length >= 30 && data[..4].SequenceEqual(new byte[] { 0xC5, 0xD0, 0xD3, 0xC6 }))
        {
            uint wmStart = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
            uint wmLength = BinaryPrimitives.ReadUInt32LittleEndian(data[16..]);
            uint tiffStart = BinaryPrimitives.ReadUInt32LittleEndian(data[20..]);
            uint tiffLength = BinaryPrimitives.ReadUInt32LittleEndian(data[24..]);
            if (wmLength > 0 && wmStart <= data.Length && wmLength <= data.Length - wmStart)
                Add(new PreviewImage("image/x-wmf", null, null, wmStart, (int)wmLength, "EPS.WMF"));
            if (tiffLength > 0 && tiffStart <= data.Length && tiffLength <= data.Length - tiffStart)
                Add(new PreviewImage("image/tiff", null, null, tiffStart, (int)tiffLength, "EPS.TIFF"));
        }
        // Photoshop EPS stores a hex-encoded sequence of image-resource blocks; Exiv2 selects
        // resource 0x0409 and returns the JPEG after its 28-byte thumbnail header.
        int photoshop = data.IndexOf("%BeginPhotoshop:"u8);
        if (photoshop >= 0)
        {
            int photoshopLineEnd = data[photoshop..].IndexOf((byte)'\n');
            if (photoshopLineEnd >= 0)
            {
                int photoshopHexStart = photoshop + photoshopLineEnd + 1;
                int end = data[photoshopHexStart..].IndexOf("%EndPhotoshop"u8);
                if (end >= 0 && TryDecodeHex(data.Slice(photoshopHexStart, end), out byte[] irb))
                    AddPhotoshopPreview(irb, photoshopHexStart);
            }
        }
        // The plain EPS AI7 hex-thumbnail syntax is a native decoded PNM preview in Exiv2.
        int marker = data.IndexOf("%AI7_Thumbnail"u8);
        if (marker < 0) return;
        int lineEnd = data[marker..].IndexOf((byte)'\n');
        if (lineEnd < 0) return;
        ReadOnlySpan<byte> header = data.Slice(marker, lineEnd);
        string text = Encoding.ASCII.GetString(header);
        string[] parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4 || !int.TryParse(parts[1], out int width) || !int.TryParse(parts[2], out int height) ||
            parts[3] != "8" || width <= 0 || height <= 0 || (long)width * height * 3 > 256L * 1024 * 1024) return;
        int beginData = marker + lineEnd + 1;
        int dataMarker = data[beginData..].IndexOf("%%BeginData:"u8);
        if (dataMarker < 0) return;
        int dataLineEnd = data[(beginData + dataMarker)..].IndexOf((byte)'\n');
        if (dataLineEnd < 0) return;
        string[] dataFields = Encoding.ASCII.GetString(data.Slice(beginData + dataMarker, dataLineEnd))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (dataFields.Length < 3 || dataFields[0] != "%%BeginData:" || dataFields[2] != "Hex") return;
        int hexStart = beginData + dataMarker + dataLineEnd + 1;
        int endData = data[hexStart..].IndexOf("%%EndData"u8);
        if (endData < 0 || endData > limits.MaxInputBytes) return;
        if (!TryDecodeHex(data.Slice(hexStart, endData), out byte[] rgb)) return;
        int expected = checked(width * height * 3);
        if (rgb.Length != expected) return;
        byte[] pnmHeader = Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n");
        byte[] pnm = new byte[pnmHeader.Length + rgb.Length];
        pnmHeader.CopyTo(pnm, 0); rgb.CopyTo(pnm, pnmHeader.Length);
        var preview = new PreviewImage("image/x-portable-anymap", width, height, hexStart, pnm.Length, "EPS.AI7.Hex");
        generated[(preview.Source, preview.Offset, preview.Length)] = pnm;
        Add(preview);
    }

    private bool TryDecodeHex(ReadOnlySpan<byte> source, out byte[] decoded)
    {
        decoded = [];
        const int maxDecodedBytes = 256 * 1024 * 1024;
        if (source.Length > Math.Min(limits.MaxInputBytes, 2L * maxDecodedBytes)) return false;
        int digits = 0;
        foreach (byte value in source)
        {
            if (Nibble(value) >= 0) digits++;
            else if (!HexWhitespace(value)) return false;
        }
        if ((digits & 1) != 0 || digits / 2 > maxDecodedBytes) return false;
        byte[] result = new byte[digits / 2];
        int high = -1, written = 0;
        foreach (byte value in source)
        {
            int nibble = Nibble(value);
            if (nibble < 0) continue;
            if (high < 0) high = nibble;
            else { result[written++] = (byte)((high << 4) | nibble); high = -1; }
        }
        decoded = result;
        return true;
    }

    private static int Nibble(byte value) => value >= (byte)'0' && value <= (byte)'9' ? value - '0' :
        value >= (byte)'A' && value <= (byte)'F' ? value - 'A' + 10 :
        value >= (byte)'a' && value <= (byte)'f' ? value - 'a' + 10 : -1;

    private static bool HexWhitespace(byte value) =>
        value == (byte)' ' || value == (byte)'\t' || value == (byte)'\r' || value == (byte)'\n' || value == (byte)'\f';

    private static (int? Width, int? Height) JpegDimensions(ReadOnlySpan<byte> jpeg)
    {
        int at = 2;
        for (int count = 0; count < 4096 && jpeg.Length - at >= 4 && jpeg[at] == 0xFF; count++)
        {
            byte marker = jpeg[at + 1];
            if (marker == 0xFF) { at++; continue; }
            if (marker is 0xD8 or 0xD9 or 0xDA) return (null, null);
            int size = BinaryPrimitives.ReadUInt16BigEndian(jpeg.Slice(at + 2, 2));
            if (size < 2 || size > jpeg.Length - at - 2) return (null, null);
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                if (size < 7 || marker is 0xC3 or 0xC7 or 0xCB or 0xCF) return (null, null);
                return (BinaryPrimitives.ReadUInt16BigEndian(jpeg.Slice(at + 7, 2)),
                    BinaryPrimitives.ReadUInt16BigEndian(jpeg.Slice(at + 5, 2)));
            }
            at += size + 2;
        }
        return (null, null);
    }

    private void AddPhotoshopPreview(ReadOnlySpan<byte> irb, int sourceOffset)
    {
        int at = 0;
        for (int count = 0; count < limits.MaxSegments && irb.Length - at >= 12; count++)
        {
            if (!irb.Slice(at, 4).SequenceEqual("8BIM"u8)) return;
            ushort id = BinaryPrimitives.ReadUInt16BigEndian(irb.Slice(at + 4, 2));
            int nameLength = irb[at + 6] + 1;
            nameLength += nameLength & 1;
            int sizeAt = at + 6 + nameLength;
            if (sizeAt > irb.Length - 4) return;
            uint size = BinaryPrimitives.ReadUInt32BigEndian(irb.Slice(sizeAt, 4));
            int payload = sizeAt + 4;
            if (size > irb.Length - payload) return;
            if (id == 0x0409 && size >= 32)
            {
                int jpegStart = payload + 28;
                int jpegLength = checked((int)size - 28);
                if (jpegLength >= 4 && irb[jpegStart] == 0xFF && irb[jpegStart + 1] == 0xD8 &&
                    irb[jpegStart + jpegLength - 2] == 0xFF && irb[jpegStart + jpegLength - 1] == 0xD9)
                {
                    ReadOnlySpan<byte> jpeg = irb.Slice(jpegStart, jpegLength);
                    (int? width, int? height) = JpegDimensions(jpeg);
                    var preview = new PreviewImage(Previews.JpegType, width, height, sourceOffset, jpegLength, "EPS.Photoshop.Hex");
                    generated[(preview.Source, preview.Offset, preview.Length)] = jpeg.ToArray();
                    Add(preview);
                }
                return;
            }
            at = payload + (int)size + ((int)size & 1);
        }
    }

    // ---- containers that carry an Exif block ----

    private void Segments(int at) => Segments(at, data.Length);

    private void Segments(int at, int limit)
    {
        if (at < 0 || limit < at || limit > data.Length) return;
        for (int count = 0; count < limits.MaxSegments && limit - at >= 4; count++)
        {
            if (data[at] != 0xFF) return;
            byte marker = data[at + 1];
            if (marker == 0xFF) { at++; continue; }
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7)) { at += 2; continue; }
            if (marker is 0xD9 or 0xDA) return;
            int length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at + 2, 2));
            if (length < 2 || length > limit - at - 2) return;
            int payload = at + 4, end = at + 2 + length;
            if (marker == 0xE1 && end - payload >= 14 && data.Slice(payload, 6).SequenceEqual("Exif\0\0"u8))
            {
                Tiff(payload + 6, end);
                return;
            }
            at = end;
        }
    }

    // Keep Png focused on metadata and its format-specific validation contract.
    private void Png()
    {
        int at = 8;
        for (int count = 0; count < limits.MaxSegments && data.Length - at >= 12; count++)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at, 4));
            if (length > (uint)(data.Length - at - 12)) return;
            int payload = at + 8, end = payload + (int)length;
            if (data.Slice(at + 4, 4).SequenceEqual("eXIf"u8))
            {
                Tiff(payload, end);
                return;
            }
            if (data.Slice(at + 4, 4).SequenceEqual("iTXt"u8)) PngXmp(payload, end);
            else if (data.Slice(at + 4, 4).SequenceEqual("zTXt"u8)) PngRawExif(payload, end);
            at = end + 4;
        }
    }

    // ImageMagick's compressed Raw profile type exif chunk is hex text around a TIFF block.
    private void PngRawExif(int start, int end)
    {
        int keywordLength = data.Slice(start, end - start).IndexOf((byte)0);
        if (keywordLength < 0 || !data.Slice(start, keywordLength).SequenceEqual("Raw profile type exif"u8)) return;
        int method = start + keywordLength + 1;
        if (method >= end || data[method] != 0) return;
        byte[]? text = Inflate(data.Slice(method + 1, end - method - 1));
        if (text is null) return;
        ReadOnlySpan<byte> profile = text;
        while (!profile.IsEmpty && (profile[0] == (byte)'\r' || profile[0] == (byte)'\n')) profile = profile[1..];
        int firstEnd = profile.IndexOf((byte)'\n');
        if (firstEnd < 0 || !Encoding.ASCII.GetString(profile[..firstEnd]).Trim().Equals("exif", StringComparison.OrdinalIgnoreCase)) return;
        profile = profile[(firstEnd + 1)..];
        int secondEnd = profile.IndexOf((byte)'\n');
        if (secondEnd < 0 || !int.TryParse(Encoding.ASCII.GetString(profile[..secondEnd]).Trim(), out int expectedBytes) ||
            expectedBytes <= 0 || expectedBytes > limits.MaxValueBytes) return;
        profile = profile[(secondEnd + 1)..];
        byte[] hex = new byte[expectedBytes];
        int high = -1, decoded = 0;
        foreach (byte value in profile)
        {
            if (value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') continue;
            int digit = value switch
            {
                >= (byte)'0' and <= (byte)'9' => value - '0',
                >= (byte)'A' and <= (byte)'F' => value - 'A' + 10,
                >= (byte)'a' and <= (byte)'f' => value - 'a' + 10,
                _ => -1
            };
            if (digit < 0) return;
            if (high < 0) high = digit;
            else
            {
                if (decoded >= hex.Length) return;
                hex[decoded++] = (byte)((high << 4) | digit);
                high = -1;
            }
        }
        if (decoded != expectedBytes || high >= 0) return;
        int tiffAt = hex.AsSpan().IndexOf("Exif\0\0"u8);
        if (tiffAt >= 0) tiffAt += 6;
        else if (hex.Length >= 4 && (hex.AsSpan(0, 4).SequenceEqual(new byte[] { 0x49, 0x49, 42, 0 }) ||
                 hex.AsSpan(0, 4).SequenceEqual(new byte[] { 0x4D, 0x4D, 0, 42 }))) tiffAt = 0;
        if (tiffAt < 0 || hex.Length - tiffAt < 8) return;
        var embedded = new PreviewScanner(hex.AsSpan(tiffAt), limits, nesting + 1);
        foreach (PreviewImage item in embedded.Run())
        {
            byte[]? previewBytes = null;
            if (embedded.generated.TryGetValue((item.Source, item.Offset, item.Length), out byte[]? transformed))
                previewBytes = transformed;
            else if (item.Offset >= 0 && item.Offset <= hex.Length - tiffAt - item.Length)
                previewBytes = hex.AsSpan(tiffAt + (int)item.Offset, item.Length).ToArray();
            if (previewBytes is null) continue;
            string source = "PNG.RawExif." + item.Source;
            var preview = new PreviewImage(item.MimeType, item.Width, item.Height, method + 1L, previewBytes.Length, source);
            generated[(preview.Source, preview.Offset, preview.Length)] = previewBytes;
            Add(preview);
        }
    }

    private byte[]? Inflate(ReadOnlySpan<byte> compressed)
    {
        try
        {
            using var input = new MemoryStream(compressed.ToArray(), writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            byte[] buffer = new byte[Math.Min(8192, limits.CopyBufferBytes)];
            while (true)
            {
                int read = zlib.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                if (read > limits.MaxValueBytes - output.Length) return null;
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }
        catch (InvalidDataException) { return null; }
    }

    // PNG's standard XMP iTXt chunk can carry the same base64 preview as an XMP sidecar.
    private void PngXmp(int start, int end)
    {
        int keywordEnd = data.Slice(start, end - start).IndexOf((byte)0);
        if (keywordEnd < 0 || !data.Slice(start, keywordEnd).SequenceEqual("XML:com.adobe.xmp"u8)) return;
        int at = start + keywordEnd + 1;
        if (end - at < 2 || data[at++] != 0 || data[at++] != 0) return; // uncompressed iTXt only
        int languageEnd = data.Slice(at, end - at).IndexOf((byte)0);
        if (languageEnd < 0) return;
        at += languageEnd + 1;
        int translatedEnd = data.Slice(at, end - at).IndexOf((byte)0);
        if (translatedEnd < 0) return;
        at += translatedEnd + 1;
        if (at < end) Nested(data.Slice(at, end - at), at);
    }

    private void WebP()
    {
        int at = 12;
        for (int count = 0; count < limits.MaxSegments && data.Length - at >= 8; count++)
        {
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(at + 4, 4));
            if (length > (uint)(data.Length - at - 8)) return;
            int payload = at + 8, end = payload + (int)length;
            if (data.Slice(at, 4).SequenceEqual("EXIF"u8))
            {
                // Some writers keep the JPEG identifier in front of the TIFF header.
                if (end - payload >= 6 && data.Slice(payload, 6).SequenceEqual("Exif\0\0"u8)) payload += 6;
                Tiff(payload, end);
                return;
            }
            long next = (long)end + (length & 1);
            if (next > data.Length) return;
            at = (int)next;
        }
    }

    // ---- TIFF ----

    // Keep Tiff focused on metadata and its format-specific validation contract.
    private void Tiff(int origin, int end, bool panasonicRaw = false)
    {
        if (origin < 0 || end > data.Length || end - origin < 8) return;
        bool little;
        if (data[origin] == 0x49 && data[origin + 1] == 0x49) little = true;
        else if (data[origin] == 0x4D && data[origin + 1] == 0x4D) little = false;
        else return;
        var frame = new Frame(origin, end, little);
        long next = U32(origin + 4, little);
        for (int index = 0; index < limits.MaxDirectories && next != 0; index++)
        {
            string group = index == 0 ? "Image" : index == 1 ? "Thumbnail" :
                "Image" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            next = ImageDirectory(frame, origin + next, group, index == 1, index == 0, panasonicRaw, 0);
        }
    }

    /// <summary>Read one image directory, list what it holds, and return the offset of the next one in its chain.</summary>
    private long ImageDirectory(Frame frame, long position, string group, bool thumbnail, bool first, bool panasonicRaw, int depth)
    {
        var entries = new List<Entry>();
        if (!Directory(frame, position, entries, out int tableEnd, out long next)) return 0;
        int tableStart = (int)position;

        long offset = Number(frame, entries, 0x0201), length = Number(frame, entries, 0x0202);
        long compression = Number(frame, entries, 0x0103, 1);
        bool reduced = thumbnail || (Number(frame, entries, 0x00FE, 0) & 1) == 1;
        Entry? strips = Find(entries, 0x0111), counts = Find(entries, 0x0117);
        // Exiv2's RW2 loader reads PanasonicRaw.PreviewImage, not a generic linked thumbnail IFD.
        // Keep scanning its EXIF/MakerNote directories, but don't promote the linked IFD1.
        if (!panasonicRaw || !thumbnail)
        {
            if (offset >= 0 && length >= 0)
            {
                Jpeg(frame.Origin + offset, length, "Exif." + group + ".JPEGInterchangeFormat", tableStart, tableEnd);
            }
            else if (strips is Entry strip && counts is Entry count && strip.Count == count.Count && strip.Count != 0)
            {
                // A reduced image, or the old-style JPEG image a CR2 keeps in its first directory.
                if ((compression == 6 || (compression == 7 && reduced)) && strip.Count == 1)
                    Jpeg(frame.Origin + Element(frame, strip, 0), Element(frame, count, 0), "Exif." + group + ".StripOffsets", tableStart, tableEnd);
                else if (compression == 1 && reduced)
                    Samples(frame, entries, strip, count, "Exif." + group + ".StripOffsets", tableStart, tableEnd);
            }
        }

        // A Panasonic RW2 keeps the picture it embeds in its raw directory, as the field the
        // reference calls PanasonicRaw.PreviewImage, and has no EXIF directory of its own to hold a note.
        if (panasonicRaw && first && Find(entries, 0x002E) is Entry embedded &&
            (embedded.Type == 1 || embedded.Type == 7) && embedded.Count != 0)
            Jpeg(embedded.At, embedded.Count, "Exif.PanasonicRaw.PreviewImage", tableStart, tableEnd);

        if (first)
        {
            string make = Text(Find(entries, 0x010F));
            long exif = Number(frame, entries, 0x8769);
            if (exif > 0) ExifDirectory(frame, frame.Origin + exif, make);
            // A DNG converted from a Pentax file keeps the original MakerNote here.
            if (Find(entries, 0xC634) is Entry original && (original.Type == 1 || original.Type == 7))
                MakerNote(frame, original.At, (int)original.Count, make);
        }

        if (depth < limits.MaxDirectoryDepth && Find(entries, 0x014A) is Entry children && (children.Type == 4 || children.Type == 13))
        {
            for (int i = 0; i < children.Count && i < limits.MaxDirectories; i++)
            {
                long child = Element(frame, children, i);
                if (child <= 0) continue;
                subImages++;
                string name = "SubImage" + subImages.ToString(System.Globalization.CultureInfo.InvariantCulture);
                ImageDirectory(frame, frame.Origin + child, name, false, false, panasonicRaw, depth + 1);
            }
        }
        return next;
    }

    // Keep ExifDirectory focused on metadata and its format-specific validation contract.
    private void ExifDirectory(Frame frame, long position, string make)
    {
        var entries = new List<Entry>();
        if (!Directory(frame, position, entries, out _, out _)) return;
        if (Find(entries, 0x927C) is Entry note && (note.Type == 1 || note.Type == 7))
            MakerNote(frame, note.At, (int)note.Count, make);
    }

    /// <summary>
    /// Uncompressed strips, when they are the simplest case there is: 8-bit RGB, interleaved, the
    /// strips one after another without a gap or an overlap, and exactly as long as the picture needs.
    /// </summary>
    private void Samples(Frame frame, List<Entry> entries, Entry strips, Entry counts, string source, int tableStart, int tableEnd)
    {
        long width = Number(frame, entries, 0x0100), height = Number(frame, entries, 0x0101);
        if (width <= 0 || height <= 0 || width > 65535 || height > 65535) return;
        if (Number(frame, entries, 0x0106) != 2 || Number(frame, entries, 0x0115) != 3 || Number(frame, entries, 0x011C, 1) != 1) return;
        if (Find(entries, 0x0102) is not Entry bits || bits.Count != 3) return;
        for (int i = 0; i < 3; i++) if (Element(frame, bits, i) != 8) return;
        if (strips.Count > MaxStrips) return;
        long start = Element(frame, strips, 0), at = start;
        for (int i = 0; i < strips.Count; i++)
        {
            long size = Element(frame, counts, i);
            if (Element(frame, strips, i) != at || size <= 0) return;
            at += size;
        }
        long total = width * height * 3;
        if (at - start != total || total > int.MaxValue) return;
        long position = frame.Origin + start;
        if (!Free(position, total, tableStart, tableEnd)) return;
        byte[]? tiff = TiffSamples(frame, entries, strips, counts, (int)total);
        if (tiff is null) return;
        var preview = new PreviewImage("image/tiff", (int)width, (int)height, position, tiff.Length, source);
        generated[(preview.Source, preview.Offset, preview.Length)] = tiff;
        Add(preview);
    }

    // Exiv2's TIFF-thumbnail loader re-encodes an uncompressed reduced image as a standalone TIFF.
    // Keep its image tags, relocate the strip data into a bounded, little-endian TIFF, and report the
    // original first-strip position as the source offset.
    private byte[]? TiffSamples(Frame frame, List<Entry> entries, Entry strips, Entry counts, int total)
    {
        ushort[] supported = [0x0100, 0x0101, 0x0102, 0x0103, 0x0106, 0x0111, 0x0115, 0x0116, 0x0117, 0x011A, 0x011B, 0x0128];
        var values = new List<(ushort Tag, ushort Type, uint Count, byte[] Bytes)>();
        foreach (Entry entry in entries)
        {
            if (!supported.Contains(entry.Tag)) continue;
            int unit = TiffTypeWidth(entry.Type);
            long length = (long)unit * entry.Count;
            if (unit == 0 || entry.Count == 0 || length > limits.MaxValueBytes || length > int.MaxValue) return null;
            byte[] bytes = new byte[(int)length];
            if (entry.Tag == 0x0111)
            {
                if (entry.Count != strips.Count) return null;
            }
            else if (entry.Tag == 0x0117)
            {
                if (entry.Count != counts.Count) return null;
                for (int i = 0; i < entry.Count; i++)
                {
                    long stripLength = Element(frame, counts, i);
                    if (stripLength <= 0 || !WriteTiffScalar(bytes, i * unit, entry.Type, stripLength)) return null;
                }
            }
            else
            {
                ReadOnlySpan<byte> raw = data.Slice(entry.At, (int)length);
                raw.CopyTo(bytes);
                if (!frame.Little && unit > 1)
                {
                    int word = entry.Type is 5 or 10 ? 4 : unit;
                    for (int at = 0; at < bytes.Length; at += word)
                        for (int left = 0, right = word - 1; left < right; left++, right--)
                            (bytes[at + left], bytes[at + right]) = (bytes[at + right], bytes[at + left]);
                }
            }
            values.Add((entry.Tag, entry.Type, entry.Count, bytes));
        }
        if (values.Count == 0 || !values.Exists(value => value.Tag == 0x0100) ||
            !values.Exists(value => value.Tag == 0x0101) || !values.Exists(value => value.Tag == 0x0111) ||
            !values.Exists(value => value.Tag == 0x0117) || values.Count > ushort.MaxValue) return null;

        values.Sort((left, right) => left.Tag.CompareTo(right.Tag));
        int directoryEnd = 8 + 2 + values.Count * 12 + 4;
        int auxiliaryEnd = directoryEnd;
        int[] auxiliaryOffsets = new int[values.Count];
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i].Bytes.Length <= 4) continue;
            if ((auxiliaryEnd & 1) != 0) auxiliaryEnd++;
            if (values[i].Bytes.Length > limits.MaxValueBytes - auxiliaryEnd) return null;
            auxiliaryOffsets[i] = auxiliaryEnd;
            auxiliaryEnd += values[i].Bytes.Length;
        }
        if ((auxiliaryEnd & 1) != 0) auxiliaryEnd++;
        int pixelsAt = auxiliaryEnd;
        if (pixelsAt > limits.MaxValueBytes || total > limits.MaxValueBytes - pixelsAt) return null;
        using var output = new MemoryStream(pixelsAt + total);
        output.Write([0x49, 0x49, 42, 0, 8, 0, 0, 0]);
        WriteLe16(output, (ushort)values.Count);
        for (int i = 0; i < values.Count; i++)
        {
            var value = values[i];
            WriteLe16(output, value.Tag);
            WriteLe16(output, value.Type);
            WriteLe32(output, value.Count);
            if (value.Bytes.Length <= 4)
            {
                output.Write(value.Bytes);
                for (int padding = value.Bytes.Length; padding < 4; padding++) output.WriteByte(0);
            }
            else WriteLe32(output, (uint)auxiliaryOffsets[i]);
        }
        WriteLe32(output, 0);
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i].Bytes.Length <= 4) continue;
            while (output.Length < auxiliaryOffsets[i]) output.WriteByte(0);
            output.Write(values[i].Bytes);
        }
        while (output.Length < pixelsAt) output.WriteByte(0);
        foreach (var value in values)
        {
            if (value.Tag != 0x0111) continue;
            long cursor = pixelsAt;
            int unit = TiffTypeWidth(value.Type);
            for (int i = 0; i < value.Count; i++)
            {
                if (!WriteTiffScalar(value.Bytes, i * unit, value.Type, cursor)) return null;
                cursor += Element(frame, counts, i);
            }
        }
        // StripOffsets is inline for the ordinary one-strip thumbnail. Write its final value before
        // serializing the IFD, which means the offsets are computed before the table is emitted.
        byte[] result = output.ToArray();
        for (int i = 0; i < values.Count; i++)
        {
            var value = values[i];
            if (value.Tag != 0x0111) continue;
            int table = 10;
            for (int j = 0; j < i; j++) table += 12;
            if (value.Bytes.Length <= 4) value.Bytes.CopyTo(result, table + 8);
            else value.Bytes.CopyTo(result, auxiliaryOffsets[i]);
        }
        int dataAt = checked((int)Element(frame, strips, 0));
        output.SetLength(0);
        output.Write(result);
        output.Write(data.Slice(frame.Origin + dataAt, total));
        return output.ToArray();
    }

    private static int TiffTypeWidth(ushort type) => type switch
    {
        1 or 2 or 6 or 7 => 1,
        3 or 8 => 2,
        4 or 9 or 11 or 13 => 4,
        5 or 10 or 12 => 8,
        _ => 0
    };

    // Write one unsigned component in the TIFF field's original type, with overflow refused.
    private static bool WriteTiffScalar(Span<byte> bytes, int at, ushort type, long value)
    {
        if (value < 0) return false;
        switch (type)
        {
            case 1: case 2: case 6: case 7:
                if (value > byte.MaxValue) return false;
                bytes[at] = (byte)value; return true;
            case 3:
                if (value > ushort.MaxValue) return false;
                BinaryPrimitives.WriteUInt16LittleEndian(bytes[at..], (ushort)value); return true;
            case 4: case 13:
                if (value > uint.MaxValue) return false;
                BinaryPrimitives.WriteUInt32LittleEndian(bytes[at..], (uint)value); return true;
            default: return false;
        }
    }

    private static void WriteLe16(Stream output, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        output.Write(bytes);
    }

    private static void WriteLe32(Stream output, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        output.Write(bytes);
    }

    // ---- MakerNotes ----

    private void MakerNote(Frame frame, int at, int length, string make)
    {
        if (length < 12 || at < 0 || at > data.Length - length) return;
        ReadOnlySpan<byte> note = data.Slice(at, length);
        var entries = new List<Entry>();
        if (note.StartsWith("Nikon\0"u8))
        {
            // A TIFF of its own, ten bytes into the note; its offsets count from that header.
            if (length < 18 || !Order(note.Slice(10, 2), out bool little)) return;
            var inner = new Frame(at + 10, frame.End, little);
            if (!Directory(inner, inner.Origin + U32(inner.Origin + 4, little), entries, out _, out _)) return;
            long preview = Number(inner, entries, 0x0011);
            if (preview <= 0) return;
            var directory = new List<Entry>();
            long position = inner.Origin + preview;
            if (!Directory(inner, position, directory, out int tableEnd, out _)) return;
            Pointer(inner, directory, 0x0201, 0x0202, "Exif.NikonPreview.JPEGInterchangeFormat", (int)position, tableEnd);
            return;
        }
        if (note.StartsWith("AOC\0"u8))
        {
            // The two bytes after the signature are not a reliable byte-order mark: the TIFF's order holds.
            if (Directory(frame, at + 6, entries, out int tableEnd, out _))
                Pointer(frame, entries, 0x0004, 0x0003, "Exif.Pentax.PreviewOffset", at + 6, tableEnd);
            return;
        }
        if (note.StartsWith("PENTAX \0"u8))
        {
            // The TIFF's byte order again; offsets count from the start of the note.
            var pentax = new Frame(at, frame.End, frame.Little);
            if (Directory(pentax, at + 10, entries, out int tableEnd, out _))
                Pointer(pentax, entries, 0x0004, 0x0003, "Exif.PentaxDng.PreviewOffset", at + 10, tableEnd);
            return;
        }
        if (note.StartsWith("QVC\0\0\0"u8))
        {
            var casio = new Frame(frame.Origin, frame.End, false);
            if (Directory(casio, at + 6, entries, out int tableEnd, out _))
            {
                Pointer(casio, entries, 0x0004, 0x0003, "Exif.Casio2.PreviewImageStart", at + 6, tableEnd);
                if (Find(entries, 0x2000) is Entry image && (image.Type == 1 || image.Type == 7))
                    Jpeg(image.At, image.Count, "Exif.Casio2.PreviewImage", 0, 0);
            }
            return;
        }
        if (note.StartsWith("Panasonic\0\0\0"u8))
        {
            if (Directory(frame, at + 12, entries, out _, out _) &&
                Find(entries, 0x002E) is Entry image && (image.Type == 1 || image.Type == 7))
                Jpeg(image.At, image.Count, "Exif.PanasonicRaw.PreviewImage", 0, 0);
            return;
        }
        if (make.StartsWith("SAMSUNG", StringComparison.OrdinalIgnoreCase))
        {
            var samsung = new Frame(at, checked(at + length), frame.Little);
            if (!Directory(samsung, at, entries, out _, out _) || Find(entries, 0x0035) is not Entry subIfd) return;
            long position = samsung.Origin + Element(samsung, subIfd, 0);
            var previewEntries = new List<Entry>();
            if (position >= at && position < samsung.End && Directory(samsung, position, previewEntries, out int tableEnd, out _))
                Pointer(samsung, previewEntries, 0x0201, 0x0202, "Exif.SamsungPreview.JPEGInterchangeFormat", (int)position, tableEnd);
            return;
        }
        if (note.StartsWith("OLYMPUS\0"u8))
        {
            if (!Order(note.Slice(8, 2), out bool little)) return;
            var olympus = new Frame(at, frame.End, little);
            if (Directory(olympus, at + 12, entries, out _, out _)) Olympus(olympus, entries, "Olympus2");
            return;
        }
        if (note.StartsWith("OLYMP\0"u8))
        {
            if (Directory(frame, at + 8, entries, out _, out _)) Olympus(frame, entries, "Olympus");
            return;
        }
        if (note.StartsWith("SONY DSC \0"u8) || note.StartsWith("SONY CAM \0"u8))
        {
            if (Directory(frame, at + 12, entries, out _, out _)) Sony(frame, entries);
            return;
        }
        if (make.StartsWith("SONY", StringComparison.OrdinalIgnoreCase))
        {
            if (Directory(frame, at, entries, out _, out _)) Sony(frame, entries);
            return;
        }
        if (make.StartsWith("Canon", StringComparison.OrdinalIgnoreCase))
        {
            if (!Directory(frame, at, entries, out int tableEnd, out _)) return;
            // PreviewImageInfo: the length is the third number, the start the sixth.
            if (Find(entries, 0x00B6) is Entry info && info.Type == 4 && info.Count >= 6)
                Jpeg(frame.Origin + Element(frame, info, 5), Element(frame, info, 2), "Exif.Canon.PreviewImageInfo", at, tableEnd);
            return;
        }
        if (make.Contains("MINOLTA", StringComparison.OrdinalIgnoreCase) && !note.StartsWith("MINOL"u8) && !note.StartsWith("CAMER"u8))
        {
            if (Directory(frame, at, entries, out int tableEnd, out _)) Minolta(frame, entries, "Minolta", at, tableEnd);
        }
    }

    private void Olympus(Frame frame, List<Entry> entries, string group)
    {
        if (Find(entries, 0x0100) is Entry thumbnail && (thumbnail.Type == 1 || thumbnail.Type == 7))
            Jpeg(thumbnail.At, thumbnail.Count, "Exif." + group + ".ThumbnailImage", 0, 0);
        if (Find(entries, 0x2020) is not Entry settings) return;
        // The camera settings are a directory: behind a pointer, or stored as the value itself.
        long position = -1;
        if (settings.Type == 4 || settings.Type == 13) position = frame.Origin + Element(frame, settings, 0);
        else if (settings.Type == 7) position = settings.At;
        var directory = new List<Entry>();
        if (position < 0 || !Directory(frame, position, directory, out int tableEnd, out _)) return;
        Pointer(frame, directory, 0x0101, 0x0102, "Exif.OlympusCs.PreviewImageStart", (int)position, tableEnd);
    }

    private void Sony(Frame frame, List<Entry> entries)
    {
        if (Find(entries, 0x2001) is Entry preview && (preview.Type == 1 || preview.Type == 7))
        {
            // The JPEG stream follows a header of 32 bytes in the files that have one.
            if (preview.Count > 36 && data[preview.At + 32] == 0xFF && data[preview.At + 33] == 0xD8)
                Jpeg(preview.At + 32, preview.Count - 32, "Exif.Sony1.PreviewImage", 0, 0);
            else
                Jpeg(preview.At, preview.Count, "Exif.Sony1.PreviewImage", 0, 0);
        }
        // The A100 keeps a Minolta directory behind this pointer.
        long minolta = Number(frame, entries, 0xB028);
        var directory = new List<Entry>();
        long position = frame.Origin + minolta;
        if (minolta > 0 && Directory(frame, position, directory, out int tableEnd, out _))
            Minolta(frame, directory, "SonyMinolta", (int)position, tableEnd);
    }

    // Keep Minolta focused on metadata and its format-specific validation contract.
    private void Minolta(Frame frame, List<Entry> entries, string group, int tableStart, int tableEnd)
    {
        if (Find(entries, 0x0081) is Entry thumbnail && (thumbnail.Type == 1 || thumbnail.Type == 7))
            Jpeg(thumbnail.At, thumbnail.Count, "Exif." + group + ".Thumbnail", 0, 0);
        Pointer(frame, entries, 0x0088, 0x0089, "Exif." + group + ".ThumbnailOffset", tableStart, tableEnd);
    }

    // Resolve a paired TIFF offset/length tag only when its range stays inside the owning table.
    private void Pointer(Frame frame, List<Entry> entries, ushort offsetTag, ushort lengthTag, string source, int tableStart, int tableEnd)
    {
        long offset = Number(frame, entries, offsetTag), length = Number(frame, entries, lengthTag);
        if (offset > 0 && length > 0) Jpeg(frame.Origin + offset, length, source, tableStart, tableEnd);
    }

    private static bool Order(ReadOnlySpan<byte> mark, out bool little)
    {
        little = mark[0] == 0x49;
        return (mark[0] == 0x49 && mark[1] == 0x49) || (mark[0] == 0x4D && mark[1] == 0x4D);
    }

    // ---- directory primitives ----

    /// <summary>
    /// Read the directory at a position in the file. Entries whose type is unknown or whose value leaves the
    /// region are dropped; a directory that does not fit, is empty or is too large is refused whole.
    /// </summary>
    private bool Directory(Frame frame, long position, List<Entry> entries, out int tableEnd, out long next)
    {
        tableEnd = 0;
        next = 0;
        if (directories >= limits.MaxDirectories) return false;
        if (position < frame.Origin || position > frame.End - 2L) return false;
        int at = (int)position;
        int count = U16(at, frame.Little);
        // A directory without entries holds nothing and still leads to its successor: the block of a
        // file whose only EXIF is a thumbnail starts with one.
        if (count > limits.MaxEntriesPerDirectory || count * 12L > frame.End - at - 2L) return false;
        directories++;
        tableEnd = at + 2 + count * 12;
        for (int i = 0; i < count; i++)
        {
            int entry = at + 2 + i * 12;
            ushort tag = U16(entry, frame.Little), type = U16(entry + 2, frame.Little);
            uint items = U32(entry + 4, frame.Little);
            int width = type switch { 1 or 2 or 6 or 7 => 1, 3 or 8 => 2, 4 or 9 or 11 or 13 => 4, 5 or 10 or 12 => 8, _ => 0 };
            if (width == 0) continue;
            long bytes = (long)items * width;
            long value = bytes <= 4 ? entry + 8 : (long)frame.Origin + U32(entry + 8, frame.Little);
            if (value < frame.Origin || bytes > frame.End - value) continue;
            entries.Add(new Entry(tag, type, items, (int)value));
        }
        if (frame.End - tableEnd >= 4)
        {
            next = U32(tableEnd, frame.Little);
            tableEnd += 4;
        }
        return true;
    }

    // Resolve metadata using the format-defined identifiers and naming rules.
    private static Entry? Find(List<Entry> entries, ushort tag)
    {
        foreach (Entry entry in entries)
            if (entry.Tag == tag) return entry;
        return null;
    }

    /// <summary>The first number of a field, or <paramref name="missing"/> when the field is absent or not an unsigned integer.</summary>
    private long Number(Frame frame, List<Entry> entries, ushort tag, long missing = -1)
    {
        if (Find(entries, tag) is not Entry entry || entry.Count == 0) return missing;
        long value = Element(frame, entry, 0);
        return value < 0 ? missing : value;
    }

    // Read one TIFF entry value while checking its type, count and byte range.
    private long Element(Frame frame, Entry entry, int index)
    {
        if ((uint)index >= entry.Count) return -1;
        return entry.Type switch
        {
            1 => (long)data[entry.At + index],
            3 => (long)U16(entry.At + index * 2, frame.Little),
            4 or 13 => (long)U32(entry.At + index * 4, frame.Little),
            _ => -1L,
        };
    }

    // Decode a preview text field only when it contains a supported string value.
    private string Text(Entry? field)
    {
        if (field is not Entry entry || entry.Type != 2) return "";
        ReadOnlySpan<byte> bytes = data.Slice(entry.At, (int)Math.Min(entry.Count, 64));
        int end = bytes.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? bytes : bytes[..end]).Trim();
    }

    private ushort U16(int at, bool little) => little
        ? BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(at, 2))
        : BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at, 2));

    private uint U32(int at, bool little) => little
        ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(at, 4))
        : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at, 4));

    // ---- PSD ----

    private void Psd()
    {
        if (data.Length < 34) return;
        long at = 30L + Be32(26);
        if (at > data.Length - 4L) return;
        long end = at + 4 + Be32((int)at);
        at += 4;
        if (end > data.Length) return;
        for (int count = 0; count < limits.MaxSegments && end - at >= 12; count++)
        {
            int start = (int)at;
            ushort id = Be16(start + 4);
            // A Pascal string padded to an even length, then the size of the payload.
            long sizeAt = at + 6 + ((data[start + 6] + 2) & ~1);
            if (sizeAt > end - 4) return;
            uint size = Be32((int)sizeAt);
            long payload = sizeAt + 4;
            if (size > end - payload) return;
            // Thumbnail resource: a header of 28 bytes (format 1 is JPEG; width; height; ...; compressed size at 20).
            if (id == 0x040C && size > 28 && Be32((int)payload) == 1)
            {
                uint compressed = Be32((int)payload + 20);
                Jpeg(payload + 28, compressed != 0 && compressed <= size - 28 ? compressed : size - 28, "Photoshop.Thumbnail", 0, 0);
            }
            at = payload + size + (size & 1);
        }
    }

    // ---- BMFF ----

    private static ReadOnlySpan<byte> CanonMetadataBox => new byte[] { 0x85, 0xC0, 0xB6, 0x87, 0x82, 0x0F, 0x11, 0xE0, 0x81, 0x11, 0xF4, 0xCE, 0x46, 0x2B, 0x6A, 0x48 };
    private static ReadOnlySpan<byte> CanonPreviewBox => new byte[] { 0xEA, 0xF4, 0x2B, 0x5E, 0x1C, 0x98, 0x4B, 0x88, 0xB9, 0xFB, 0xB7, 0xDC, 0x40, 0x6E, 0x4D, 0x16 };

    private const uint MetaBox = 0x6D657461, MovieBox = 0x6D6F6F76, UuidBox = 0x75756964;
    private const uint InformationBox = 0x69696E66, EntryBox = 0x696E6665, ReferenceBox = 0x69726566, LocationBox = 0x696C6F63;
    private const uint PropertiesBox = 0x69707270, ContainerBox = 0x6970636F, AssociationBox = 0x69706D61, ExtentBox = 0x69737065;
    private const uint ThumbnailReference = 0x74686D62, JpegItem = 0x6A706567, HevcItem = 0x68766331, Av1Item = 0x61763031;
    private const uint CanonThumbnail = 0x54484D42, CanonPreview = 0x50525657;

    // Keep Box focused on metadata and its format-specific validation contract.
    private bool Box(int at, int limit, out uint type, out int payload, out int end)
    {
        type = 0;
        payload = 0;
        end = 0;
        if (at < 0 || limit - at < 8) return false;
        long size = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at, 4));
        type = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at + 4, 4));
        int header = 8;
        if (size == 1)
        {
            if (limit - at < 16) return false;
            ulong large = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(at + 8, 8));
            if (large > int.MaxValue) return false;
            size = (long)large;
            header = 16;
        }
        else if (size == 0)
        {
            size = limit - at;
        }
        if (size < header || size > limit - at) return false;
        payload = at + header;
        end = at + (int)size;
        return true;
    }

    private void Bmff()
    {
        int at = 0;
        for (int count = 0; count < limits.MaxSegments && Box(at, data.Length, out uint type, out int payload, out int end); count++)
        {
            if (type == MetaBox) Meta(payload + 4, end);
            else if (type == MovieBox) CanonMovie(payload, end);
            else if (type == UuidBox && end - payload >= 24 && data.Slice(payload, 16).SequenceEqual(CanonPreviewBox))
            {
                // CR3: a count of eight bytes, then the PRVW box with the width, the height and the size of a JPEG.
                if (Box(payload + 24, end, out uint inner, out int body, out int bodyEnd) && inner == CanonPreview && bodyEnd - body >= 16)
                    Boxed(body + 16, bodyEnd, BinaryPrimitives.ReadUInt32BigEndian(data.Slice(body + 12, 4)), "Canon.PRVW");
            }
            at = end;
        }
    }

    private void CanonMovie(int at, int limit)
    {
        for (int count = 0; count < limits.MaxSegments && Box(at, limit, out uint type, out int payload, out int end); count++)
        {
            if (type == UuidBox && end - payload >= 16 && data.Slice(payload, 16).SequenceEqual(CanonMetadataBox))
            {
                int child = payload + 16;
                for (int inner = 0; inner < limits.MaxSegments && Box(child, end, out uint kind, out int body, out int bodyEnd); inner++)
                {
                    // CR3: THMB holds the width, the height and the size of a JPEG, then the JPEG. That is
                    // version 0 of the box; version 1 holds a picture of another coding and is not listed.
                    if (kind == CanonThumbnail && bodyEnd - body >= 16 && data[body] == 0)
                        Boxed(body + 16, bodyEnd, BinaryPrimitives.ReadUInt32BigEndian(data.Slice(body + 8, 4)), "Canon.THMB");
                    child = bodyEnd;
                }
            }
            at = end;
        }
    }

    /// <summary>A JPEG that a box states the length of: the picture has to end inside the box.</summary>
    private void Boxed(int at, int boxEnd, uint length, string source)
    {
        if (length <= (uint)(boxEnd - at)) Jpeg(at, length, source, 0, 0);
    }

    // Keep Meta focused on metadata and its format-specific validation contract.
    private void Meta(int at, int limit)
    {
        var types = new Dictionary<uint, uint>();
        var places = new Dictionary<uint, (long Offset, long Length)>();
        var thumbnails = new List<uint>();
        var sizes = new List<(int Width, int Height)>();
        var extents = new Dictionary<uint, (int Width, int Height)>();
        int associations = -1, associationsEnd = 0;
        for (int count = 0; count < limits.MaxSegments && Box(at, limit, out uint type, out int payload, out int end); count++)
        {
            if (type == InformationBox) Information(payload, end, types);
            else if (type == ReferenceBox) References(payload, end, thumbnails);
            else if (type == LocationBox) Locations(payload, end, places);
            else if (type == PropertiesBox)
            {
                int child = payload;
                for (int inner = 0; inner < limits.MaxSegments && Box(child, end, out uint kind, out int body, out int bodyEnd); inner++)
                {
                    if (kind == ContainerBox) Properties(body, bodyEnd, sizes);
                    else if (kind == AssociationBox) { associations = body; associationsEnd = bodyEnd; }
                    child = bodyEnd;
                }
            }
            at = end;
        }
        if (associations >= 0) Associations(associations, associationsEnd, sizes, extents);
        foreach (uint id in thumbnails)
        {
            if (!types.TryGetValue(id, out uint kind) || !places.TryGetValue(id, out (long Offset, long Length) place)) continue;
            if (kind == JpegItem)
            {
                Jpeg(place.Offset, place.Length, "Bmff.thmb", 0, 0);
                continue;
            }
            string? mime = kind == HevcItem ? Previews.HevcType : kind == Av1Item ? Previews.Av1Type : null;
            if (mime is null || !Free(place.Offset, place.Length, 0, 0)) continue;
            bool known = extents.TryGetValue(id, out (int Width, int Height) extent);
            Add(new PreviewImage(mime, known ? extent.Width : (int?)null, known ? extent.Height : (int?)null, place.Offset, (int)place.Length, "Bmff.thmb"));
        }
    }

    private uint Be32(int at) => BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at, 4));

    private ushort Be16(int at) => BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at, 2));

    // iinf: a count, then one infe box per item; versions 2 and 3 carry the item type.
    private void Information(int at, int end, Dictionary<uint, uint> types)
    {
        if (end - at < 6) return;
        at += data[at] == 0 ? 6 : 8;
        for (int count = 0; count < limits.MaxTotalEntries && Box(at, end, out uint type, out int payload, out int boxEnd); count++)
        {
            if (type == EntryBox && boxEnd - payload >= 12)
            {
                byte version = data[payload];
                if (version == 2) types[Be16(payload + 4)] = Be32(payload + 8);
                else if (version == 3 && boxEnd - payload >= 14) types[Be32(payload + 4)] = Be32(payload + 10);
            }
            at = boxEnd;
        }
    }

    // iref: boxes named after the kind of reference; the item a 'thmb' box starts from is a thumbnail.
    private void References(int at, int end, List<uint> thumbnails)
    {
        if (end - at < 4) return;
        bool wide = data[at] != 0;
        at += 4;
        for (int count = 0; count < limits.MaxTotalEntries && Box(at, end, out uint type, out int payload, out int boxEnd); count++)
        {
            if (type == ThumbnailReference && boxEnd - payload >= (wide ? 4 : 2) && thumbnails.Count < 64)
                thumbnails.Add(wide ? Be32(payload) : Be16(payload));
            at = boxEnd;
        }
    }

    // iloc: only an item stored in the file itself as one extent has a position worth reporting.
    private void Locations(int at, int end, Dictionary<uint, (long Offset, long Length)> places)
    {
        if (end - at < 8) return;
        byte version = data[at];
        if (version > 2) return;
        int offsetSize = data[at + 4] >> 4, lengthSize = data[at + 4] & 15, baseSize = data[at + 5] >> 4;
        int indexSize = version == 0 ? 0 : data[at + 5] & 15;
        if (!Sized(offsetSize) || !Sized(lengthSize) || !Sized(baseSize) || !Sized(indexSize)) return;
        at += 6;
        long items;
        if (version < 2) { items = Be16(at); at += 2; }
        else { if (end - at < 4) return; items = Be32(at); at += 4; }
        for (long item = 0; item < items && item < limits.MaxTotalEntries; item++)
        {
            int head = (version < 2 ? 2 : 4) + (version == 0 ? 0 : 2) + 2 + baseSize + 2;
            if (end - at < head) return;
            uint id = version < 2 ? Be16(at) : Be32(at);
            at += version < 2 ? 2 : 4;
            int method = 0;
            if (version != 0) { method = Be16(at) & 15; at += 2; }
            int reference = Be16(at);
            at += 2;
            ulong origin = Field(at, baseSize);
            at += baseSize;
            int extentCount = Be16(at);
            at += 2;
            int each = indexSize + offsetSize + lengthSize;
            if ((long)extentCount * each > end - at) return;
            if (extentCount == 1 && method == 0 && reference == 0 && lengthSize != 0)
            {
                ulong offset = origin + Field(at + indexSize, offsetSize), length = Field(at + indexSize + offsetSize, lengthSize);
                if (offset <= int.MaxValue && length <= int.MaxValue) places[id] = ((long)offset, (long)length);
            }
            at += extentCount * each;
        }
    }

    private static bool Sized(int size) => size is 0 or 4 or 8;

    // Read a little-endian TIFF scalar of the requested byte width.
    private ulong Field(int at, int size) => size switch
    {
        4 => Be32(at),
        8 => BinaryPrimitives.ReadUInt64BigEndian(data.Slice(at, 8)),
        _ => 0,
    };

    // ipco: the properties in order; only the spatial extent matters here.
    private void Properties(int at, int end, List<(int Width, int Height)> sizes)
    {
        for (int count = 0; count < limits.MaxTotalEntries && Box(at, end, out uint type, out int payload, out int boxEnd); count++)
        {
            uint width = 0, height = 0;
            if (type == ExtentBox && boxEnd - payload >= 12) { width = Be32(payload + 4); height = Be32(payload + 8); }
            sizes.Add(width <= int.MaxValue && height <= int.MaxValue ? ((int)width, (int)height) : (0, 0));
            at = boxEnd;
        }
    }

    // ipma: for every item, the one-based indices of its properties.
    private void Associations(int at, int end, List<(int Width, int Height)> sizes, Dictionary<uint, (int Width, int Height)> extents)
    {
        if (end - at < 8) return;
        byte version = data[at];
        bool wideIndex = (data[at + 3] & 1) != 0;
        long entries = Be32(at + 4);
        at += 8;
        for (long entry = 0; entry < entries && entry < limits.MaxTotalEntries; entry++)
        {
            int idSize = version < 1 ? 2 : 4;
            if (end - at < idSize + 1) return;
            uint id = version < 1 ? Be16(at) : Be32(at);
            at += idSize;
            int count = data[at];
            at++;
            int each = wideIndex ? 2 : 1;
            if (count * each > end - at) return;
            for (int i = 0; i < count; i++)
            {
                int index = wideIndex ? Be16(at + i * 2) & 0x7FFF : data[at + i] & 0x7F;
                if (index >= 1 && index <= sizes.Count && sizes[index - 1].Width > 0 && sizes[index - 1].Height > 0)
                    extents[id] = sizes[index - 1];
            }
            at += count * each;
        }
    }

    // ---- results ----

    /// <summary>True when the range lies in the file and shares no byte with the directory that declares it.</summary>
    private bool Free(long offset, long length, int tableStart, int tableEnd)
    {
        if (offset < 0 || length <= 0 || length > int.MaxValue || offset > data.Length - length) return false;
        return offset >= tableEnd || offset + length <= tableStart;
    }

    private void Jpeg(long offset, long length, string source, int tableStart, int tableEnd)
    {
        if (length < 4 || !Free(offset, length, tableStart, tableEnd)) return;
        int at = (int)offset, end = at + (int)length;
        if (data[at] != 0xFF || data[at + 1] != 0xD8 || data[at + 2] != 0xFF) return;
        int? width = null, height = null;
        // The frame header states the dimensions. A lossless frame is raw sensor data, not a preview.
        int position = at + 2;
        for (int count = 0; count < limits.MaxSegments && end - position >= 4 && data[position] == 0xFF; count++)
        {
            byte marker = data[position + 1];
            if (marker == 0xFF) { position++; continue; }
            if (marker is 0xD8 or 0xD9 or 0xDA) break;
            int size = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position + 2, 2));
            if (size < 2 || size > end - position - 2) break;
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                if (marker is 0xC3 or 0xC7 or 0xCB or 0xCF) return;
                if (size >= 7)
                {
                    height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position + 5, 2));
                    width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position + 7, 2));
                }
                break;
            }
            position += 2 + size;
        }
        Add(new PreviewImage(Previews.JpegType, width, height, offset, (int)length, source));
    }

    // Keep Add focused on metadata and its format-specific validation contract.
    private void Add(PreviewImage preview)
    {
        if (found.Count >= MaxPreviews) return;
        foreach (PreviewImage known in found)
            if (known.Offset == preview.Offset && known.Length == preview.Length) return;
        found.Add(preview);
    }
}
