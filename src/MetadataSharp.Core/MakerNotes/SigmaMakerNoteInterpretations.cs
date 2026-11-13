using System.Text;

namespace MetadataSharp.Core.MakerNotes;

/// <summary>Corpus-backed Sigma and Foveon MakerNote renderings.</summary>
internal static class SigmaMakerNoteInterpretations
{
    internal static string? Interpret(string name, ExifValue value)
    {
        if (value.FieldType != TiffFieldType.Ascii) return null;
        string? text = Ascii(value);
        if (text is null) return null;

        return name switch
        {
            "ExposureMode" => FirstCharacter(text, ('P', "Program"), ('A', "Aperture priority"),
                ('S', "Shutter priority"), ('M', "Manual")),
            "MeteringMode" => FirstCharacter(text, ('A', "Average"), ('C', "Center"), ('8', "8-Segment")),
            "Exposure" or "Contrast" or "Shadow" or "Highlight" or "Saturation" or "Sharpness" or
                "FillLight" or "ColorAdjustment" or "Quality" => StripLabel(text),
            _ => null,
        };
    }

    private static string? FirstCharacter(string value, params (char Code, string Label)[] labels)
    {
        if (value.Length == 0) return null;
        foreach ((char code, string label) in labels)
            if (value[0] == code) return label;
        return "(" + value + ")";
    }

    private static string StripLabel(string value)
    {
        int separator = value.IndexOf(':');
        if (separator < 0) return value;
        int start = separator + 1;
        if (start < value.Length && value[start] == ' ') start++;
        return value[start..];
    }

    private static string? Ascii(ExifValue value)
    {
        ReadOnlySpan<byte> bytes = value.RawBytes;
        int length = bytes.IndexOf((byte)0);
        if (length < 0) length = bytes.Length;
        return length == 0 ? null : Encoding.ASCII.GetString(bytes[..length]);
    }
}
