using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace MetadataSharp.Core.MakerNotes;

/// <summary>Corpus-backed Olympus and OM System MakerNote renderings.</summary>
internal static class OlympusMakerNoteInterpretations
{
    internal static string? Interpret(int scope, string name, ExifValue value)
    {
        if (name == "CameraID" && value.FieldType is TiffFieldType.Ascii or TiffFieldType.Undefined)
        {
            ReadOnlySpan<byte> bytes = value.RawBytes;
            int length = bytes.IndexOf((byte)0);
            if (length < 0) length = bytes.Length;
            return length == 0 ? null : Encoding.ASCII.GetString(bytes[..length]);
        }
        if (name == "SpecialMode")
            return SpecialMode(value);
        if (scope == 0x0204 && name == "DigitalZoom")
            return DigitalZoom(value);

        if (!ExifTags.TryInteger(value, 0, out long number))
            return name == "WhiteBalance" ? WhiteBalance(value) : null;

        return name switch
        {
            "Quality" => LookupOrStored(number, (1, "Standard Quality (SQ)"), (2, "High Quality (HQ)"),
                (3, "Super High Quality (SHQ)"), (6, "Raw")),
            "Macro" => LookupOrStored(number, (0, "Off"), (1, "On"), (2, "Super macro")),
            "BWMode" or "FlashMode" or "ExternalFlashBounce" => LookupOrStored(number, (0, "Off"), (1, "On")),
            "OneTouchWB" => LookupOrStored(number, (0, "Off"), (1, "On"), (2, "On (preset)")),
            "FlashDevice" => LookupOrStored(number, (0, "None"), (1, "Internal"), (4, "External"), (5, "Internal + External")),
            "FocusRange" => LookupOrStored(number, (0, "Normal"), (1, "Macro")),
            "FocusMode" => LookupOrStored(number, (0, "Auto"), (1, "Manual")),
            "SharpnessFactor" => LookupOrStored(number, (0, "Normal"), (1, "Hard"), (2, "Soft")),
            "Contrast" => LookupOrStored(number, (0, "High"), (1, "Normal"), (2, "Low")), 
            _ => name == "WhiteBalance" ? WhiteBalance(value) : null,
        };
    }

    private static string? SpecialMode(ExifValue value)
    {
        if (value.Count != 3 || !ExifTags.TryInteger(value, 0, out long mode) ||
            !ExifTags.TryInteger(value, 1, out long sequence) || !ExifTags.TryInteger(value, 2, out long direction))
            return null;
        string result = mode switch { 0 => "Normal", 2 => "Fast", 3 => "Panorama", _ => "(" + mode.ToString(CultureInfo.InvariantCulture) + ")" };
        if (mode == 0)
            return result;
        result += ", Sequence number " + sequence.ToString(CultureInfo.InvariantCulture);
        if (mode != 2)
            result += ", " + (direction switch
            {
                1 => "Left to right",
                2 => "Right to left",
                3 => "Bottom to top",
                4 => "Top to bottom",
                _ => "(" + direction.ToString(CultureInfo.InvariantCulture) + ")",
            });
        return result;
    }

    private static string? WhiteBalance(ExifValue value)
    {
        if (!ExifTags.TryInteger(value, 0, out long mode)) return null;
        if (value.Count == 1)
            return mode == 1 ? "Auto" : null;
        if (value.Count != 2 || !ExifTags.TryInteger(value, 1, out long setting)) return null;
        if (mode == 1)
            return setting == 0 ? "Auto" : "Auto (" + setting.ToString(CultureInfo.InvariantCulture) + ")";
        if (mode == 3 && setting == 0) return "One-touch";
        if (mode == 2)
            return setting switch
            {
                2 => "3000 Kelvin", 3 => "3700 Kelvin", 4 => "4000 Kelvin", 5 => "4500 Kelvin",
                6 => "5500 Kelvin", 7 => "6500 Kelvin", 8 => "7500 Kelvin", _ => null,
            };
        return null;
    }

    private static string? DigitalZoom(ExifValue value)
    {
        if (value.FieldType != TiffFieldType.Rational || value.Count != 1 || value.RawBytes.Length < 8)
            return null;
        ReadOnlySpan<byte> raw = value.RawBytes;
        bool little = value.ByteOrder == Endianness.LittleEndian;
        uint numerator = little ? BinaryPrimitives.ReadUInt32LittleEndian(raw) : BinaryPrimitives.ReadUInt32BigEndian(raw);
        uint denominator = little ? BinaryPrimitives.ReadUInt32LittleEndian(raw[4..]) : BinaryPrimitives.ReadUInt32BigEndian(raw[4..]);
        if (denominator == 0) return null;
        float ratio = (float)numerator / denominator;
        if (ratio is 0 or 1) return "None";
        return ratio.ToString("0.0", CultureInfo.InvariantCulture) + "x";
    }

    private static string LookupOrStored(long number, params (long Number, string Label)[] entries)
    {
        foreach ((long key, string label) in entries)
            if (key == number)
                return label;
        return "(" + number.ToString(CultureInfo.InvariantCulture) + ")";
    }

}
