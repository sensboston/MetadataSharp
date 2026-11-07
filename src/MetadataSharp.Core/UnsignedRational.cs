namespace MetadataSharp.Core;

/// <summary>Preserves an unsigned numerator/denominator pair without normalization.</summary>
public readonly record struct UnsignedRational(uint Numerator, uint Denominator)
{
    /// <summary>Converts to double, or returns false and zero for a zero denominator.</summary>
    public bool TryGetDouble(out double value)
    {
        value = 0;
        if (Denominator == 0)
            return false;
        value = (double)Numerator / Denominator;
        return true;
    }
}
