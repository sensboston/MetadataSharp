namespace MetadataSharp.Harness.Python;

// binascii.Error: the message is the one CPython gives.
internal sealed class BinasciiError(string message) : Exception(message);

// base64 as CPython's base64 and binascii modules have it.
internal static class PyBase64
{
    private const byte Pad = (byte)'=';

    /// <summary>base64.b64encode(data) as text.</summary>
    public static string Encode(ReadOnlySpan<byte> data) => Convert.ToBase64String(data);

    /// <summary>
    /// base64.b64decode(text, validate=...): ValueError (as ArgumentException) for text that is not ASCII,
    /// otherwise binascii.a2b_base64(data, strict_mode=validate).
    /// </summary>
    public static byte[] Decode(string text, bool validate = false)
    {
        if (text.Any(c => c > 0x7F)) throw new ArgumentException("string argument should contain only ASCII characters");
        return A2bBase64(text.Select(c => (byte)c).ToArray(), validate);
    }

    /// <summary>
    /// binascii.a2b_base64(data, strict_mode=strict), a port of CPython's Modules/binascii.c as of 3.12.4 and
    /// 3.13, which in strict mode also refuse padding at the start of a quad ("Excess padding not allowed").
    /// </summary>
    public static byte[] A2bBase64(ReadOnlySpan<byte> data, bool strict)
    {
        var output = new List<byte>(data.Length / 4 * 3 + 3);
        if (strict && data.Length > 0 && data[0] == Pad) throw new BinasciiError("Leading padding not allowed");
        int quadPosition = 0, pads = 0;
        int leftChar = 0;
        bool paddingStarted = false;
        for (int i = 0; i < data.Length; i++)
        {
            byte character = data[i];
            // Pad sequences: the invalid ones are ignored.
            if (character == Pad)
            {
                paddingStarted = true;
                if (strict && quadPosition == 0) throw new BinasciiError("Excess padding not allowed");
                if (quadPosition >= 2 && quadPosition + ++pads >= 4)
                {
                    // A pad sequence ends the input; in strict mode nothing may follow it.
                    if (strict && i + 1 < data.Length) throw new BinasciiError("Excess data after padding");
                    return output.ToArray();
                }
                continue;
            }
            int value = Value(character);
            if (value >= 64)
            {
                if (strict) throw new BinasciiError("Only base64 data is allowed");
                continue;
            }
            // Characters that are not '=', in the middle of the padding, are not allowed.
            if (strict && paddingStarted) throw new BinasciiError("Discontinuous padding not allowed");
            pads = 0;
            switch (quadPosition)
            {
                case 0:
                    quadPosition = 1;
                    leftChar = value;
                    break;
                case 1:
                    quadPosition = 2;
                    output.Add((byte)((leftChar << 2) | (value >> 4)));
                    leftChar = value & 0x0f;
                    break;
                case 2:
                    quadPosition = 3;
                    output.Add((byte)((leftChar << 4) | (value >> 2)));
                    leftChar = value & 0x03;
                    break;
                default:
                    quadPosition = 0;
                    output.Add((byte)((leftChar << 6) | value));
                    leftChar = 0;
                    break;
            }
        }
        if (quadPosition == 1)
        {
            // Exactly one extra valid, non-padding character: no input encodes to such a length.
            throw new BinasciiError("Invalid base64-encoded string: number of data characters "
                                    + $"({output.Count / 3 * 4 + 1}) cannot be 1 more than a multiple of 4");
        }
        if (quadPosition != 0) throw new BinasciiError("Incorrect padding");
        return output.ToArray();
    }

    private static int Value(byte character) => character switch
    {
        >= (byte)'A' and <= (byte)'Z' => character - 'A',
        >= (byte)'a' and <= (byte)'z' => character - 'a' + 26,
        >= (byte)'0' and <= (byte)'9' => character - '0' + 52,
        (byte)'+' => 62,
        (byte)'/' => 63,
        _ => 64,
    };
}
