using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Reference;

/// <summary>The selected box stream is malformed or exceeds the probe's limits.</summary>
internal sealed class BoxError(string message) : PyException("BoxError", message);

/// <summary>One box header: its type, absolute start, size, header size and, for a uuid box, the user type.</summary>
internal sealed record Box(byte[] Kind, long Start, long Size, long HeaderSize, byte[]? UserType = null)
{
    public long PayloadStart => Start + HeaderSize;

    public long End => Start + Size;

    public bool Equals(Box? other) => other is not null && Kind.AsSpan().SequenceEqual(other.Kind) && Start == other.Start && Size == other.Size
                                      && HeaderSize == other.HeaderSize && (UserType is null ? other.UserType is null : other.UserType is not null && UserType.AsSpan().SequenceEqual(other.UserType));

    public override int GetHashCode() => HashCode.Combine(Start, Size, HeaderSize);
}

// Bounded BMFF box-header inspection for B7 reference work, formerly tools/reference/bmff_structure.py.
//
// This tool only accounts for a selected flat byte range and optionally reads an ftyp box. It does not recursively
// infer container types, parse item locations, validate AVIF/HEIF/HEIC/CR3/JXL, or decode images. Malformed input
// is an error, not an absent box or a successful recognition. All offsets are absolute.
internal static class BmffStructure
{
    public const long MaxInputBytes = 16 * 1024 * 1024;
    public const int MaxBoxes = 4096;

    private static ulong BigEndian(ReadOnlySpan<byte> data)
    {
        ulong value = 0;
        foreach (byte b in data) value = (value << 8) | b;
        return value;
    }

    /// <summary>Parse one flat range; no recursion or semantic interpretation is implied.</summary>
    public static List<Box> Boxes(byte[] data, long start = 0, long? end = null)
    {
        if (data.Length > MaxInputBytes) throw new BoxError("input exceeds size limit");
        long stop = end ?? data.Length;
        if (!(0 <= start && start <= stop && stop <= data.Length)) throw new BoxError("invalid box range");
        var result = new List<Box>();
        long pos = start;
        while (pos < stop)
        {
            if (result.Count >= MaxBoxes) throw new BoxError("box count exceeds limit");
            if (stop - pos < 8) throw new BoxError("truncated box header");
            ulong size32 = BigEndian(data.AsSpan((int)pos, 4));
            byte[] kind = data.AsSpan((int)pos + 4, 4).ToArray();
            long header = 8;
            // A 64-bit size is a Python int there: a size past long.MaxValue is larger than any range.
            System.Numerics.BigInteger size;
            if (size32 == 1)
            {
                if (stop - pos < 16) throw new BoxError("truncated extended box size");
                size = BigEndian(data.AsSpan((int)pos + 8, 8));
                header = 16;
            }
            else if (size32 == 0)
            {
                size = stop - pos;
            }
            else
            {
                size = size32;
            }
            byte[]? userType = null;
            if (kind.AsSpan().SequenceEqual("uuid"u8))
            {
                if (stop - pos < header + 16) throw new BoxError("truncated UUID box header");
                userType = data.AsSpan((int)(pos + header), 16).ToArray();
                header += 16;
            }
            if (size < header || size > stop - pos) throw new BoxError("invalid box size");
            result.Add(new Box(kind, pos, (long)size, header, userType));
            pos += (long)size;
        }
        return result;
    }

    /// <summary>Return raw major/compatible brands and minor version; not format validation.</summary>
    public static PyDict Ftyp(byte[] data, Box box)
    {
        if (!box.Kind.AsSpan().SequenceEqual("ftyp"u8)) throw new BoxError("not an ftyp box");
        // Recheck a caller-supplied box; do not trust manufactured Box instances.
        if (box.Start < 0 || box.HeaderSize < 8 || box.PayloadStart > box.End || box.End > data.Length) throw new BoxError("invalid ftyp bounds");
        byte[] payload = PyBytes.Slice(data, box.PayloadStart, box.End);
        if (payload.Length < 8 || payload.Length % 4 != 0) throw new BoxError("invalid ftyp payload");
        var compatible = new List<object?>();
        for (int i = 8; i < payload.Length; i += 4) compatible.Add(payload[i..(i + 4)]);
        return PyDict.Of(("majorBrand", payload[..4]), ("minorVersion", (long)BigEndian(payload.AsSpan(4, 4))), ("compatibleBrands", compatible));
    }

    /// <summary>Report signature shape only; never assert validity, decodability, or metadata.</summary>
    public static string JxlSignature(byte[] data)
    {
        if (data.AsSpan().StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0x00, 0x0C, (byte)'J', (byte)'X', (byte)'L', (byte)' ', 0x0D, 0x0A, 0x87, 0x0A]))
            return "container-signature";
        if (data.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xFF, 0x0A])) return "codestream-signature";
        return "unknown";
    }
}
