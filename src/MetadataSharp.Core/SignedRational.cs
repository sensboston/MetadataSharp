using System.Globalization;

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

    /// <summary>
    /// The numerator divided by the denominator in double-precision arithmetic. A zero denominator
    /// gives what that division gives: an infinity, or NaN for 0/0. <see cref="TryGetDouble" />
    /// refuses a zero denominator instead.
    /// </summary>
    public double ToDouble() => (double)Numerator / Denominator;

    /// <summary>The pair as written, <c>numerator/denominator</c>, in invariant digits: <c>-2/3</c>.</summary>
    public override string ToString() =>
        Numerator.ToString(CultureInfo.InvariantCulture) + "/" + Denominator.ToString(CultureInfo.InvariantCulture);
}
