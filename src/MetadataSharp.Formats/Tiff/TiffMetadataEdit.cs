using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using MetadataSharp.Core;

namespace MetadataSharp.Formats.Tiff;

/// <summary>Finite read/write ceilings captured before opening a TIFF editing source.</summary>
public sealed record TiffEditOptions
{
    public TiffReadOptions ReadOptions { get; init; } = new();
    public long MaxOutputBytes { get; init; } = 1_073_741_824;
    internal TiffEditOptions Snapshot()
    {
        ArgumentNullException.ThrowIfNull(ReadOptions);
        if (MaxOutputBytes <= 0) throw new ArgumentOutOfRangeException(nameof(MaxOutputBytes));
        return this with { ReadOptions = ReadOptions.Snapshot() };
    }
}

/// <summary>An editable field addressed by an original-source IFD offset, not an output offset.</summary>
public readonly record struct TiffFieldKey(uint DirectoryOffset, ushort TagId);

/// <summary>Addressed baseline descriptive changes. Removal is not physical erasure.</summary>
public sealed class TiffMetadataEdit
{
    private readonly Dictionary<TiffFieldKey, ExifValue?> changes = [];
    public TiffMetadataEdit() { }
    public bool IsEmpty => changes.Count == 0;
    public void Set(TiffFieldKey key, ExifValue value)
    {
        TiffEditPolicy.Key(key); ArgumentNullException.ThrowIfNull(value);
        if (!TiffEditPolicy.Value(key.TagId, value, true)) throw new ArgumentException("Value is outside the writable TIFF descriptive shape.", nameof(value));
        changes[key] = value;
    }
    public void Remove(TiffFieldKey key) { TiffEditPolicy.Key(key); changes[key] = null; }
    internal Dictionary<TiffFieldKey, ExifValue?> Snapshot() => new(changes);
}

internal static class TiffEditPolicy
{
    internal static bool Text(ushort tag) => tag is 269 or 270 or 271 or 272 or 285 or 305 or 306 or 315 or 316;
    internal static bool Pointer(ushort tag) => tag is 330 or 34665 or 34853 or 40965;
    internal static void Key(TiffFieldKey key)
    {
        if (key.DirectoryOffset < 8 || (key.DirectoryOffset & 1) != 0 || !(Text(key.TagId) || key.TagId == 274))
            throw new ArgumentException("Key must address a baseline descriptive field in a source IFD.", nameof(key));
    }
    internal static byte[] Bytes(ExifValue value, Endianness order)
    {
        byte[] bytes = new byte[value.ByteLength]; value.CopyRawTo(bytes);
        if (value.FieldType == TiffFieldType.Short && value.ByteOrder != order) Array.Reverse(bytes);
        return bytes;
    }
    internal static bool Value(ushort tag, ExifValue value, bool writing)
    {
        byte[] bytes = new byte[value.ByteLength]; value.CopyRawTo(bytes);
        if (tag == 274)
        {
            if (value.FieldType != TiffFieldType.Short || value.Count != 1) return false;
            ushort orientation = value.ByteOrder == Endianness.LittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes);
            return orientation is >= 1 and <= 8;
        }
        if (!Text(tag) || value.FieldType != TiffFieldType.Ascii || bytes.Length == 0 || bytes[^1] != 0) return false;
        for (int i = 0; i < bytes.Length - 1; i++) if (bytes[i] is < 0x20 or > 0x7e) return false;
        if (tag != 306) return true;
        return bytes.Length == 20 && (!writing || DateTime.TryParseExact(Encoding.ASCII.GetString(bytes, 0, 19), "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
    }
    internal static bool Shape(TiffFieldInfo f, uint samples, uint extras, int segmentCount)
    {
        bool Short(uint n) => f.FieldType == 3 && f.Count == n;
        bool Integer(uint n) => f.FieldType is 3 or 4 && f.Count == n;
        if (Text(f.TagId)) return f.FieldType == 2 && f.Count >= 1 && (f.TagId != 306 || f.Count == 20);
        return f.TagId switch
        {
            254 => f.FieldType == 4 && f.Count == 1,
            255 or 274 or 259 or 262 or 266 or 277 or 284 or 296 or 317 => Short(1),
            256 or 257 or 278 or 322 or 323 => Integer(1),
            258 or 280 or 281 or 339 => Short(samples),
            297 => Short(2),
            282 or 283 or 286 or 287 => f.FieldType == 5 && f.Count == 1,
            273 or 279 or 325 => Integer((uint)segmentCount),
            324 => f.FieldType == 4 && f.Count == segmentCount,
            338 => Short(extras),
            330 => f.FieldType == 4 && f.Count > 0,
            34665 or 34853 or 40965 or 513 or 514 => f.FieldType == 4 && f.Count == 1,
            _ => false
        };
    }
}
