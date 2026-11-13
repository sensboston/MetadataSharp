namespace MetadataSharp.Core.Binary;

/// <summary>Validates nonnegative sizes without overflowing or narrowing unchecked.</summary>
internal static class CheckedRange
{
    internal static bool TryGetSpanRange(long offset, long length, int totalLength,
        out int start, out int count)
    {
        start = count = 0;
        if (totalLength < 0 || offset < 0 || length < 0 ||
            offset > totalLength || length > totalLength - offset)
            return false;

        start = (int)offset;
        count = (int)length;
        return true;
    }

    /// <summary>Multiplies a count by an element width, returning false rather than overflowing.</summary>
    internal static bool TryMultiply(uint count, int width, out int length)
    {
        length = 0;
        if (width <= 0 || count > (uint)(int.MaxValue / width))
            return false;

        length = (int)count * width;
        return true;
    }

    internal static bool TryAdd(long offset, long length, out long end)
    {
        end = 0;
        if (offset < 0 || length < 0 || offset > long.MaxValue - length)
            return false;

        end = offset + length;
        return true;
    }
}
