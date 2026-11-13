using System.Globalization;
using System.Xml;

namespace MetadataSharp.Core.Xmp;

internal enum XmpStepKind
{
    /// <summary>A top-level property.</summary>
    Property,
    /// <summary>A field of a structure: <c>/prefix:name</c>.</summary>
    Field,
    /// <summary>An array item counted from one: <c>[n]</c>.</summary>
    Index,
    /// <summary>The last array item: <c>[last()]</c>.</summary>
    Last,
    /// <summary>A qualifier: <c>/?prefix:name</c>.</summary>
    Qualifier,
    /// <summary>The language qualifier: <c>/?xml:lang</c>.</summary>
    Language
}

/// <summary>One step of a path. A step named by prefix has a null <see cref="Uri"/> until the edit is applied to a packet.</summary>
internal readonly record struct XmpStep(XmpStepKind Kind, string Prefix, string Name, string? Uri, int Index);

/// <summary>
/// The path syntax of <see cref="XmpEntry.Key"/>: <c>Xmp.prefix.name</c>, then any number of
/// <c>[n]</c>, <c>[last()]</c>, <c>/prefix:field</c> and <c>/?prefix:qualifier</c> steps.
/// </summary>
internal static class XmpPath
{
    private const int MaxSteps = 64;

    // Parse the bounded path grammar here; namespace prefixes stay unresolved until a packet is available.
    internal static XmpStep[] Parse(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length > 4096) throw Invalid(path, "it is longer than 4,096 characters");
        if (!path.StartsWith("Xmp.", StringComparison.Ordinal)) throw Invalid(path, "it does not start with \"Xmp.\"");
        int dot = path.IndexOf('.', 4);
        if (dot < 0) throw Invalid(path, "it has no property name");
        string prefix = path[4..dot];
        int at = dot + 1;
        string name = Name(path, ref at);
        var steps = new List<XmpStep> { new(XmpStepKind.Property, Verified(path, prefix), Verified(path, name), null, 0) };
        while (at < path.Length)
        {
            if (steps.Count >= MaxSteps) throw Invalid(path, "it has more than 64 steps");
            if (path[at] == '[')
            {
                int close = path.IndexOf(']', at);
                if (close < 0) throw Invalid(path, "an index is not closed");
                string index = path[(at + 1)..close];
                at = close + 1;
                if (index == "last()") { steps.Add(new(XmpStepKind.Last, string.Empty, string.Empty, null, 0)); continue; }
                if (index.Length == 0 || index.Length > 9 || !index.All(char.IsAsciiDigit) ||
                    !int.TryParse(index, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number < 1)
                    throw Invalid(path, "an index is not a number counted from one or last()");
                steps.Add(new(XmpStepKind.Index, string.Empty, string.Empty, null, number));
                continue;
            }
            if (path[at] != '/') throw Invalid(path, "a step does not start with '/' or '['");
            at++;
            bool qualifier = at < path.Length && path[at] == '?';
            if (qualifier) at++;
            string step = Name(path, ref at);
            int colon = step.IndexOf(':');
            if (colon < 0) throw Invalid(path, "a field or qualifier has no prefix");
            string stepPrefix = step[..colon], stepName = step[(colon + 1)..];
            if (stepPrefix == "xml")
            {
                if (!qualifier || stepName != "lang") throw Invalid(path, "xml:lang is the only name of the xml namespace, and it is a qualifier");
                steps.Add(new(XmpStepKind.Language, "xml", "lang", null, 0));
                continue;
            }
            steps.Add(new(qualifier ? XmpStepKind.Qualifier : XmpStepKind.Field, Verified(path, stepPrefix), Verified(path, stepName), null, 0));
        }
        for (int i = 0; i + 1 < steps.Count; i++)
            if (steps[i].Kind == XmpStepKind.Language) throw Invalid(path, "nothing follows xml:lang");
        return steps.ToArray();
    }

    internal static XmpStep[] Property(XmpPropertyName name)
    {
        name.Validate();
        return [new(XmpStepKind.Property, string.Empty, name.LocalName, name.NamespaceUri, 0)];
    }

    // Read one path component while leaving separators for the grammar-specific caller.
    private static string Name(string path, ref int at)
    {
        int start = at;
        while (at < path.Length && path[at] != '[' && path[at] != '/') at++;
        return path[start..at];
    }

    // Validate XML local-name syntax and preserve the original path for precise refusal errors.
    private static string Verified(string path, string name)
    {
        try { return XmlConvert.VerifyNCName(name); }
        catch (Exception e) when (e is XmlException or ArgumentException) { throw Invalid(path, "\"" + name + "\" is not an XML name"); }
    }

    private static ArgumentException Invalid(string path, string reason) =>
        new("\"" + (path.Length > 120 ? path[..120] : path) + "\" is not an XMP path: " + reason + ".", nameof(path));
}
