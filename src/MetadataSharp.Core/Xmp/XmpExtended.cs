using System.Security.Cryptography;
using System.Xml.Linq;

namespace MetadataSharp.Core.Xmp;

/// <summary>
/// Extended XMP, the form a JPEG file keeps a packet in that is too long for one APP1 segment: a
/// standard packet that fits, an extension part with the rest of the properties, and in the standard
/// packet <c>xmpNote:HasExtendedXMP</c>, the MD5 digest of the extension part as 32 upper-case
/// hexadecimal digits. This type joins the two parts into one packet and divides one packet in two.
/// </summary>
internal static class XmpExtended
{
    private static readonly XName Reference = XName.Get("HasExtendedXMP", XmpNamespaces.Note);

    /// <summary>The two parts of a packet that is kept as Extended XMP.</summary>
    internal sealed record Parts(byte[] Standard, byte[] Extension, string Digest);

    /// <summary>The digest that names an extension part.</summary>
    internal static string Digest(ReadOnlySpan<byte> extension) => Convert.ToHexString(MD5.HashData(extension));

    /// <summary>The digest the standard packet names, or null when it names none or the value is not a plain text.</summary>
    internal static string? ReferenceOf(XmpPacket standard) =>
        standard.Tree?.Find(Reference) is { Kind: XmpEntryKind.Text, Qualifiers.Count: 0, Language: null } node ? node.Value : null;

    /// <summary>
    /// One packet with the properties of both parts, the reference left out. Both parts have to be
    /// editable and a property may be in one of them only; anything else cannot be joined without
    /// choosing, and is refused.
    /// </summary>
    internal static XmpPacket Join(XmpPacket standard, ReadOnlySpan<byte> extension, XmpLimits limits)
    {
        XmpPacket second = XmpPacket.Parse(extension, new() { Limits = limits });
        if (standard.Tree is null || second.Tree is null) throw XmpCodec.Unsupported();
        if (second.Tree.About.Length != 0 && second.Tree.About != standard.Tree.About) throw XmpCodec.Unsupported();
        XmpTree whole = standard.Tree.Clone();
        whole.Properties.RemoveAll(property => property.Name == Reference);
        foreach (XmpNode property in second.Tree.Properties)
        {
            if (property.Name == Reference || whole.Find(property.Name) is not null)
                throw new MetadataException(MetadataErrorCode.AmbiguousMetadata, "An XMP property is in both the standard packet and its extension.");
            whole.Properties.Add(property.Clone());
        }
        foreach (KeyValuePair<string, string> pair in second.Tree.Declarations) whole.Declarations.TryAdd(pair.Key, pair.Value);
        return XmpPacket.FromTree(whole, limits);
    }

    /// <summary>
    /// Divides a packet that is longer than <paramref name="standardBytes"/>: the largest properties
    /// go to the extension part until the standard packet, with the reference, fits. The parts are
    /// joined again and refused unless they hold what the packet holds.
    /// </summary>
    /// <exception cref="MetadataException">
    /// <see cref="MetadataErrorCode.XmpTooLarge"/> when no division fits;
    /// <see cref="MetadataErrorCode.UnsupportedFeature"/> when the packet itself has the reference property.
    /// </exception>
    internal static Parts Split(XmpPacket packet, int standardBytes)
    {
        XmpTree whole = packet.Tree ?? throw XmpCodec.Unsupported();
        if (whole.Find(Reference) is not null) throw XmpCodec.Unsupported();
        XmpLimits limits = packet.Limits;
        int count = whole.Properties.Count;

        // What each property adds to a packet, measured on a packet of that property alone. The
        // namespaces it declares are counted with it every time, so the sum is never too small.
        int empty = XmpWriter.Write(new XmpTree { About = whole.About }, limits).Length;
        var sizes = new int[count];
        long estimate = empty + 128;
        for (int i = 0; i < count; i++)
        {
            var one = new XmpTree { About = whole.About };
            one.Properties.Add(whole.Properties[i]);
            sizes[i] = XmpWriter.Write(one, limits).Length - empty;
            estimate += sizes[i];
        }
        int[] order = Enumerable.Range(0, count).OrderByDescending(i => sizes[i]).ThenBy(i => i).ToArray();
        var moved = new bool[count];
        int next = 0;
        while (estimate > standardBytes && next < count)
        {
            moved[order[next]] = true;
            estimate -= sizes[order[next++]];
        }
        if (next == 0 && count != 0) { moved[order[0]] = true; next = 1; }

        while (true)
        {
            var first = new XmpTree { About = whole.About };
            var second = new XmpTree { About = whole.About };
            for (int i = 0; i < count; i++) (moved[i] ? second : first).Properties.Add(whole.Properties[i]);
            byte[] extension = XmpWriter.Write(second, limits, null, wrapper: false);
            string digest = Digest(extension);
            first.Properties.Add(new XmpNode(Reference, "xmpNote") { Value = digest });
            XmpPacket standard = XmpPacket.FromTree(first, limits);
            if (standard.ByteLength <= standardBytes)
            {
                XmpPacket joined = Join(standard, extension, limits);
                if (!SameProperties(whole, joined.Tree!))
                    throw new MetadataException(MetadataErrorCode.UnsafeRelocation, "The Extended XMP parts as written do not read back as the edit; nothing was written.");
                byte[] bytes = new byte[standard.ByteLength];
                standard.CopyRawTo(bytes);
                return new Parts(bytes, extension, digest);
            }
            if (next >= count) throw new MetadataException(MetadataErrorCode.XmpTooLarge, "The XMP does not fit a JPEG file, even as Extended XMP.");
            moved[order[next++]] = true;
        }
    }

    /// <summary>The same properties, in any order: joining puts the properties of the extension last.</summary>
    private static bool SameProperties(XmpTree left, XmpTree right)
    {
        if (left.About != right.About || left.Properties.Count != right.Properties.Count) return false;
        foreach (XmpNode property in left.Properties)
            if (right.Find(property.Name) is not XmpNode other || !XmpNode.Same(property, other)) return false;
        return true;
    }
}
