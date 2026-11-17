using System.Buffers.Binary;
using System.Globalization;

namespace MetadataSharp.Formats;

/// <summary>The pixel size of a picture, as its container states it: width and height in pixels.</summary>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
public readonly record struct ImageSize(int Width, int Height)
{
    /// <summary>The size as <c>640 x 480</c>, the way the Exiv2 tools write it.</summary>
    /// <returns>The width, the word <c>x</c> between spaces, and the height.</returns>
    public override string ToString() =>
        Width.ToString(CultureInfo.InvariantCulture) + " x " + Height.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The pixel size a file states in its own structure, read without decoding a picture and without
/// the EXIF block: the frame header of a JPEG, the header chunk of a PNG, the first directory of a
/// TIFF structure or the header chunk of a WebP.
/// </summary>
/// <remarks>
/// <para>
/// A metadata snapshot describes tags, not pixels, so a caller that wants the size of a picture, as
/// <c>exiv2 -p s</c> prints it, has to ask for it separately. The readers here are header readers:
/// they stop at the first number they find and never look at image data, so a truncated or damaged
/// file still reports what its header says.
/// </para>
/// <para>
/// The formats covered are the ones whose header states the size at a fixed place: JPEG (the first
/// frame header), PNG (IHDR), TIFF and every format built on a TIFF structure (DNG, CR2, NEF, ARW,
/// PEF, ORF, RW2), and WebP (VP8X, VP8 or VP8L). No other format is read; <see langword="null"/>
/// says the size is not stated where this reader looks, and the caller decides what to print.
/// </para>
/// </remarks>
public static class ImageGeometry
{
    /// <summary>The most bytes <see cref="Read(string)"/> reads before it gives up.</summary>
    public const int MaxHeaderBytes = 1_048_576;

    /// <summary>Reads the pixel size a file states in its own header.</summary>
    /// <param name="data">The first bytes of the file; <see cref="MaxHeaderBytes"/> are enough for every format read here.</param>
    /// <returns>The size, or <see langword="null"/> when the format is not one this reader knows or its header does not state one.</returns>
    public static ImageSize? Read(ReadOnlySpan<byte> data)
    {
        if (IsJpeg(data)) return JpegSize(data);
        if (IsPng(data)) return PngSize(data);
        if (IsWebP(data)) return WebPSize(data);
        if (IsTiff(data)) return TiffSize(data);
        return null;
    }

    /// <summary>Reads the pixel size a file states in its own header.</summary>
    /// <param name="path">The path of the file; its first <see cref="MaxHeaderBytes"/> bytes are read.</param>
    /// <returns>The size, or <see langword="null"/> when the file is of a format this reader does not measure.</returns>
    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be read.</exception>
    public static ImageSize? Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] buffer = new byte[(int)Math.Min(stream.Length, MaxHeaderBytes)];
        int read = 0;
        while (read < buffer.Length)
        {
            int count = stream.Read(buffer, read, buffer.Length - read);
            if (count == 0) break;
            read += count;
        }
        return Read(buffer.AsSpan(0, read));
    }

    private static bool IsJpeg(ReadOnlySpan<byte> data) => data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8;

    private static bool IsPng(ReadOnlySpan<byte> data) =>
        data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;

    private static bool IsWebP(ReadOnlySpan<byte> data) =>
        data.Length >= 12 && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F' &&
        data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P';

    private static bool IsTiff(ReadOnlySpan<byte> data) => data.Length >= 8 &&
        ((data[0] == 0x49 && data[1] == 0x49 && (data[2] == 42 || data[2] == 0x4F || data[2] == 0x55) && data[3] == 0) ||
         (data[0] == 0x4D && data[1] == 0x4D && data[2] == 0 && (data[3] == 42 || data[3] == 0x4F || data[3] == 0x55)));

    // Walk the segments to the first frame header. Every marker with a length is skipped by that
    // length; entropy-coded data never precedes the frame header, so no marker emulation is possible.
    private static ImageSize? JpegSize(ReadOnlySpan<byte> data)
    {
        int at = 2;
        while (at + 1 < data.Length)
        {
            if (data[at] != 0xFF) { at++; continue; }
            int marker = data[at + 1];
            if (marker == 0xFF) { at++; continue; }
            if (marker is 0x01 or 0xD8 || marker is >= 0xD0 and <= 0xD7) { at += 2; continue; }
            if (marker is 0xD9 or 0xDA) return null;
            if (at + 3 >= data.Length) return null;
            int length = BinaryPrimitives.ReadUInt16BigEndian(data[(at + 2)..]);
            if (length < 2) return null;
            bool frame = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
            if (frame)
            {
                if (at + 8 >= data.Length) return null;
                int height = BinaryPrimitives.ReadUInt16BigEndian(data[(at + 5)..]);
                int width = BinaryPrimitives.ReadUInt16BigEndian(data[(at + 7)..]);
                return width == 0 ? null : new ImageSize(width, height);
            }
            at += 2 + length;
        }
        return null;
    }

    // The signature is followed by the IHDR chunk, which a PNG file must start with.
    private static ImageSize? PngSize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 24) return null;
        if (data[12] != (byte)'I' || data[13] != (byte)'H' || data[14] != (byte)'D' || data[15] != (byte)'R') return null;
        uint width = BinaryPrimitives.ReadUInt32BigEndian(data[16..]);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(data[20..]);
        if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue) return null;
        return new ImageSize((int)width, (int)height);
    }

    // The three header chunks store the size in three shapes: VP8X as two 24-bit values minus one,
    // VP8 in the key frame header after the start code, VP8L as 14-bit values minus one.
    private static ImageSize? WebPSize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 30) return null;
        ReadOnlySpan<byte> id = data[12..16];
        if (id.SequenceEqual("VP8X"u8))
        {
            int width = 1 + (data[24] | (data[25] << 8) | (data[26] << 16));
            int height = 1 + (data[27] | (data[28] << 8) | (data[29] << 16));
            return new ImageSize(width, height);
        }
        if (id.SequenceEqual("VP8 "u8))
        {
            if (data[23] != 0x9D || data[24] != 0x01 || data[25] != 0x2A) return null;
            int width = BinaryPrimitives.ReadUInt16LittleEndian(data[26..]) & 0x3FFF;
            int height = BinaryPrimitives.ReadUInt16LittleEndian(data[28..]) & 0x3FFF;
            return width == 0 || height == 0 ? null : new ImageSize(width, height);
        }
        if (id.SequenceEqual("VP8L"u8))
        {
            if (data[20] != 0x2F) return null;
            int bits = data[21] | (data[22] << 8) | (data[23] << 16) | (data[24] << 24);
            int width = 1 + (bits & 0x3FFF);
            int height = 1 + ((bits >> 14) & 0x3FFF);
            return new ImageSize(width, height);
        }
        return null;
    }

    // ImageWidth (0x0100) and ImageLength (0x0101) of the first directory, in whatever type they use:
    // a TIFF file may state them as a short or as a long, and a RAW file may state them only there.
    private static ImageSize? TiffSize(ReadOnlySpan<byte> data)
    {
        bool little = data[0] == 0x49;
        uint directory = ReadUInt32(data, 4, little);
        if (directory < 8 || directory > data.Length - 2) return null;
        int at = (int)directory;
        int count = ReadUInt16(data, at, little);
        at += 2;
        if (at + count * 12 > data.Length) return null;
        int width = 0, height = 0;
        for (int i = 0; i < count; i++)
        {
            int entry = at + i * 12;
            int tag = ReadUInt16(data, entry, little);
            if (tag is not (0x0100 or 0x0101)) continue;
            int type = ReadUInt16(data, entry + 2, little);
            uint value = type switch
            {
                // A short value is stored in the first two bytes of the four-byte value field.
                3 => (uint)ReadUInt16(data, entry + 8, little),
                4 => ReadUInt32(data, entry + 8, little),
                _ => 0u,
            };
            if (value is 0 or > int.MaxValue) return null;
            if (tag == 0x0100) width = (int)value; else height = (int)value;
            if (width != 0 && height != 0) return new ImageSize(width, height);
        }
        return width != 0 && height != 0 ? new ImageSize(width, height) : null;
    }

    private static int ReadUInt16(ReadOnlySpan<byte> data, int at, bool little)
    {
        if (at < 0 || at + 2 > data.Length) return 0;
        return little ? BinaryPrimitives.ReadUInt16LittleEndian(data[at..]) : BinaryPrimitives.ReadUInt16BigEndian(data[at..]);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int at, bool little)
    {
        if (at < 0 || at + 4 > data.Length) return 0;
        return little ? BinaryPrimitives.ReadUInt32LittleEndian(data[at..]) : BinaryPrimitives.ReadUInt32BigEndian(data[at..]);
    }
}
