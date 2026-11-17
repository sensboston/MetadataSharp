using MetadataSharp.Harness.Python;

namespace MetadataSharp.Harness.Reference;

internal sealed class ExtentError(string message) : PyException("ExtentError", message);

// Bounded byte-range accounting for independently supplied BMFF item extents, formerly
// tools/reference/bmff_extents.py.
//
// This is *not* an iloc parser: callers must obtain offset, length and base from an independently reviewed source,
// resolve construction methods, and establish item ownership. Zero-length ranges and overlapping ranges are reported
// rather than silently discarded. No image decoding or metadata semantics are implied.
internal static class BmffExtents
{
    public const long MaxInputBytes = 16 * 1024 * 1024;
    public const int MaxExtents = 4096;
    public const int MaxOverlaps = 4096;
    // A property, not a constant, so that a test can lower the budget as the Python test patched it.
    public static long MaxTotalExtentBytes { get; internal set; } = 64 * 1024 * 1024;

    /// <summary>
    /// Validate (base, offset, length) triplets and return ordered byte facts. Values must be nonnegative integers,
    /// and all ranges must lie within data. Input order and duplicates are preserved. Overlap is a separate fact;
    /// the caller, not this low-level helper, decides whether an overlap is legal. A triplet is any list of values
    /// (a Python tuple or list); an integer is a long or an int, never a bool.
    /// </summary>
    public static PyDict InspectExtents(byte[] data, IReadOnlyList<object?> extents)
    {
        if (data.Length > MaxInputBytes) throw new ExtentError("input exceeds size limit");
        if (extents.Count > MaxExtents) throw new ExtentError("extent count exceeds limit");
        var facts = new List<PyDict>();
        System.Numerics.BigInteger totalLength = 0;
        foreach (object? triplet in extents)
        {
            if (triplet is not System.Collections.IList list || triplet is string || list.Count != 3) throw new ExtentError("expected base, offset, length");
            var values = list.Cast<object?>().ToList();
            if (values.Any(value => !IsInt(value) || ToBig(value) < 0)) throw new ExtentError("extent values must be nonnegative integers");
            var (@base, offset, length) = (ToBig(values[0]), ToBig(values[1]), ToBig(values[2]));
            var start = @base + offset;
            var end = start + length;
            if (start > data.Length || end > data.Length) throw new ExtentError("extent outside input");
            totalLength += length;
            if (totalLength > MaxTotalExtentBytes) throw new ExtentError("total extent bytes exceed limit");
            byte[] payload = PyBytes.Slice(data, (long)start, (long)end);
            facts.Add(PyDict.Of(("base", (long)@base), ("offset", (long)offset), ("start", (long)start), ("length", (long)length),
                                ("sha256", JpegOwnedFixtures.Sha256(payload))));
        }
        var overlaps = new List<object?>();
        for (int i = 0; i < facts.Count; i++)
        {
            var a = facts[i];
            for (int j = i + 1; j < facts.Count; j++)
            {
                var b = facts[j];
                if (a.Long("length") != 0 && b.Long("length") != 0
                    && Math.Max(a.Long("start"), b.Long("start")) < Math.Min(a.Long("start") + a.Long("length"), b.Long("start") + b.Long("length")))
                {
                    if (overlaps.Count >= MaxOverlaps) throw new ExtentError("overlap count exceeds limit");
                    overlaps.Add(new List<object?> { (long)i, (long)j });
                }
            }
        }
        return PyDict.Of(("extents", facts.Cast<object?>().ToList()), ("overlaps", overlaps));
    }

    private static bool IsInt(object? value) => value is int or long or System.Numerics.BigInteger;

    private static System.Numerics.BigInteger ToBig(object? value) => value switch
    {
        int i => i,
        long l => l,
        System.Numerics.BigInteger b => b,
        _ => 0,
    };
}
