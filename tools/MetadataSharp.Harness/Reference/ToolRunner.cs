using System.Globalization;
using System.Numerics;
using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Reference;

/// <summary>What a child left: its exit status, and its output decoded as UTF-8 with replacement.</summary>
internal readonly record struct ToolResult(int Code, string Stdout, string Stderr);

/// <summary>Runs a command with a timeout in seconds; the gates take one so that their tests can stand in for the
/// pinned tool and the fixture generator.</summary>
internal delegate ToolResult Runner(IReadOnlyList<string> command, int timeout = 120);

// The process runner and the annotation writer the reference parity scripts shared (sync_parity.run and
// sync_parity.annotate, which makernote, operations, charset and CRW parity imported).
internal static class ToolRunner
{
    /// <summary>subprocess.run(command, capture_output=True, timeout=timeout): the output decoded with replacement
    /// and no newline translation; 124 and "not finished in time" when the timeout passes.</summary>
    public static ToolResult Run(IReadOnlyList<string> command, int timeout = 120)
    {
        try
        {
            var done = PyProcess.Run(command, timeout: (long)timeout, text: false);
            return new(done.ReturnCode, PyText.Utf8Replace(done.StdoutBytes), PyText.Utf8Replace(done.StderrBytes));
        }
        catch (PyTimeoutExpired)
        {
            return new(124, "", "not finished in time");
        }
    }

    /// <summary>Prints lines as annotations of at most size characters each, limit of them.</summary>
    public static void Annotate(string level, string title, IEnumerable<string> lines, int size = 3000, int limit = 9)
    {
        var chunk = new List<string>();
        int length = 0, sent = 0;
        foreach (string? line in lines.Append(null))
        {
            if (line is null || length + Py.Len(line) + 3 > size)
            {
                if (chunk.Count > 0 && sent < limit)
                {
                    string text = string.Join("\n", chunk).Replace("%", "%25", StringComparison.Ordinal).Replace("\r", "%0D", StringComparison.Ordinal)
                        .Replace("\n", "%0A", StringComparison.Ordinal);
                    Out.Print($"::{level} title={title}::{text}");
                    sent++;
                }
                chunk = [];
                length = 0;
            }
            if (line is not null)
            {
                chunk.Add(PyText.Head(line, 300));
                length += Py.Len(chunk[^1]) + 3;
            }
        }
    }

    /// <summary>int(text) of a run of decimal digits (any Unicode decimal digit, as Python's \d and int() take
    /// them), with an optional minus sign.</summary>
    public static BigInteger ParseDigits(string text)
    {
        bool negative = text.StartsWith('-');
        BigInteger value = 0;
        foreach (char c in negative ? text[1..] : text) value = value * 10 + CharUnicodeInfo.GetDecimalDigitValue(c);
        return negative ? -value : value;
    }
}

/// <summary>fractions.Fraction of two integers: kept in lowest terms with a positive denominator, equal by value.</summary>
internal readonly record struct Fraction
{
    public Fraction(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero) throw new PyException("ZeroDivisionError", $"Fraction({numerator}, 0)");
        BigInteger divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        if (denominator.Sign < 0) divisor = -divisor;
        Numerator = numerator / divisor;
        Denominator = denominator / divisor;
    }

    public BigInteger Numerator { get; }

    public BigInteger Denominator { get; }

    public override string ToString() => Denominator.IsOne ? Numerator.ToString(CultureInfo.InvariantCulture) : $"{Numerator}/{Denominator}";
}
