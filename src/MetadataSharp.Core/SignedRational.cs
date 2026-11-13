using System.Globalization;

namespace MetadataSharp.Core;

/// <summary>Preserves a signed numerator/denominator pair without normalization.</summary>
/// <param name="Numerator">The signed numerator.</param>
/// <param name="Denominator">The signed denominator; zero is preserved and is not rejected at construction.</param>
public readonly record struct SignedRational(int Numerator, int Denominator)
{
    /// <summary>Converts without integer division; a zero denominator returns false and zero.</summary>
    /// <param name="value">Receives the quotient when the denominator is nonzero, or zero on failure.</param>
    /// <returns><see langword="true"/> if the denominator is nonzero.</returns>
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
    /// <returns>The quotient using IEEE 754 double-precision division.</returns>
    public double ToDouble() => (double)Numerator / Denominator;

    /// <summary>The pair as written, <c>numerator/denominator</c>, in invariant digits: <c>-2/3</c>.</summary>
    /// <returns>The numerator and denominator separated by a slash.</returns>
    public override string ToString() =>
        Numerator.ToString(CultureInfo.InvariantCulture) + "/" + Denominator.ToString(CultureInfo.InvariantCulture);
}
