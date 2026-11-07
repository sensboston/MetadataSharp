namespace MetadataSharp.Core;

/// <summary>Preserves a signed numerator/denominator pair without normalization.</summary>
public readonly record struct SignedRational(int Numerator, int Denominator)
{
    /// <summary>Converts without integer division; a zero denominator returns false and zero.</summary>
    public bool TryGetDouble(out double value)
    {
        value = 0;
        if (Denominator == 0)
            return false;
        value = (double)Numerator / Denominator;
        return true;
    }
}
