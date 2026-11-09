using System.Globalization;
using System.Text;
using MetadataSharp.Core;

namespace MetadataSharp.Formats.Eps;

/// <summary>
/// Restricted EPSF 3.0 header inspection; never interprets or executes
/// PostScript. This does not establish valid, safe or renderable EPS content.
/// </summary>
public sealed class EpsHeader
{
    private const int MaxFileBytes = 8 * 1024 * 1024;
    private const int MaxLineBytes = 4096;
    private const int MaxHeaderLines = 1024;

    private EpsHeader(int left, int bottom, int right, int top)
    {
        Left = left; Bottom = bottom; Right = right; Top = top;
    }
    /// <summary>Declared lower-left X coordinate in default PostScript points.</summary>
    public int Left { get; }
    /// <summary>Declared lower-left Y coordinate in default PostScript points.</summary>
    public int Bottom { get; }
    /// <summary>Declared upper-right X coordinate in default PostScript points.</summary>
    public int Right { get; }
    /// <summary>Declared upper-right Y coordinate in default PostScript points.</summary>
    public int Top { get; }

    /// <summary>Inspect the ASCII DSC header of a complete bounded buffer.</summary>
    public static EpsHeader Read(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxFileBytes) throw Limit();
        int pos = 0;
        if (!Line(data, ref pos).SequenceEqual("%!PS-Adobe-3.0 EPSF-3.0"u8))
            throw Unsupported("Only the textual EPSF 3.0 header is supported.");
        int left = 0, bottom = 0, right = 0, top = 0;
        bool found = false;
        for (int i = 0; i < MaxHeaderLines; i++)
        {
            ReadOnlySpan<byte> line = Line(data, ref pos);
            if (line.SequenceEqual("%%EndComments"u8))
            {
                if (!found) throw Bad("Missing BoundingBox header comment.");
                return new EpsHeader(left, bottom, right, top);
            }
            if (!line.StartsWith("%%"u8)) throw Unsupported("Only an explicit DSC comment header is supported.");
            if (!line.StartsWith("%%BoundingBox:"u8)) continue;
            if (found) throw new MetadataException(MetadataErrorCode.AmbiguousMetadata, "Duplicate BoundingBox header comments.");
            string[] values = Encoding.ASCII.GetString(line[14..]).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length != 4 || !Parse(values[0], out left) || !Parse(values[1], out bottom) ||
                !Parse(values[2], out right) || !Parse(values[3], out top))
                throw Unsupported("Only four immediate integer bounding-box coordinates are supported.");
            if (right <= left || top <= bottom) throw Bad("Invalid BoundingBox extent.");
            found = true;
        }
        throw Limit();
    }

    private static bool Parse(string value, out int result) =>
        int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result);

    private static ReadOnlySpan<byte> Line(ReadOnlySpan<byte> data, ref int pos)
    {
        if (pos >= data.Length) throw Bad("Truncated EPS header.");
        int start = pos;
        while (pos < data.Length && data[pos] != 10 && data[pos] != 13)
        {
            byte value = data[pos++];
            if (value != 9 && (value < 32 || value > 126)) throw Bad("Non-ASCII EPS header.");
            if (pos - start > MaxLineBytes) throw Limit();
        }
        if (pos == data.Length) throw Bad("Unterminated EPS header line.");
        ReadOnlySpan<byte> result = data.Slice(start, pos - start);
        byte ending = data[pos++];
        if (ending == 13 && pos < data.Length && data[pos] == 10) pos++;
        return result;
    }
    private static MetadataException Bad(string message) => new(MetadataErrorCode.InvalidEps, message);
    private static MetadataException Unsupported(string message) => new(MetadataErrorCode.UnsupportedFeature, message);
    private static MetadataException Limit() => new(MetadataErrorCode.LimitExceeded, "EPS exceeds a fixed header budget.");
}
