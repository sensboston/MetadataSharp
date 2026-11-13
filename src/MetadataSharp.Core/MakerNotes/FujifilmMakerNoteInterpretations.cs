using System.Globalization;

namespace MetadataSharp.Core.MakerNotes;

internal static partial class MakerNoteLabels
{
    private static string? FujifilmWhiteBalanceFineTune(ExifValue value)
    {
        if (value.FieldType != TiffFieldType.SLong || value.Count != 2 || value.RawBytes.Length != 8 ||
            !ExifTags.TryInteger(value, 0, out long red) || !ExifTags.TryInteger(value, 1, out long blue) ||
            red % 20 != 0 || blue % 20 != 0)
            return "(" + value + ")";
        return "R: " + Whole(red / 20) + " B: " + Whole(blue / 20);
    }

    private static string? FujifilmDigitalZoom(ExifValue value)
    {
        if (value.FieldType != TiffFieldType.Long || value.Count != 1 || !ExifTags.TryInteger(value, 0, out long raw))
            return null;
        string zoom = ((double)(uint)raw / 8).ToString("0.##########", CultureInfo.InvariantCulture);
        return zoom + "(" + Whole((uint)raw) + ")";
    }

    private static string? FujifilmDriveSetting(ExifValue value)
    {
        if (value.FieldType != TiffFieldType.Long || value.Count != 1 || !ExifTags.TryInteger(value, 0, out long raw))
            return null;
        uint packed = (uint)raw;
        int mode = (int)(packed & 0xff);
        int second = (int)((packed >> 8) & 0xff);
        int third = (int)((packed >> 16) & 0xff);
        int framesPerSecond = (int)(packed >> 24);
        string result = mode switch { 0 => "Single", 1 => "Continuous Low", 2 => "Continuous High", _ => Stored(mode) };
        if (framesPerSecond != 0)
            result += ", " + Whole(framesPerSecond) + " fps";
        if (mode != 0)
            result += ", (" + Whole(second) + ", " + Whole(third) + ")";
        return result;
    }

    private static string? FujifilmFaceElementTypes(ExifValue value)
    {
        if (value.FieldType is not (TiffFieldType.Ascii or TiffFieldType.Short))
            return "(" + value + ")";
        ReadOnlySpan<byte> bytes = value.RawBytes;
        int count = checked((int)value.Count);
        if (value.FieldType == TiffFieldType.Ascii && count > 0)
            count--;
        var labels = new string[count];
        for (int index = 0; index < count; index++)
        {
            int number = value.FieldType == TiffFieldType.Ascii
                ? bytes[index] - '0'
                : value.ByteOrder == Endianness.LittleEndian
                    ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(index * 2, 2))
                    : System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(index * 2, 2));
            labels[index] = number switch { 1 => "Face", 2 => "Left Eye", 3 => "Right Eye", _ => Stored(value.FieldType == TiffFieldType.Ascii ? bytes[index] : number) };
        }
        return string.Join(' ', labels);
    }
}
