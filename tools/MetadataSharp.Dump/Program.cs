using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Jp2;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Formats.Png;
using MetadataSharp.Formats.Psd;
using MetadataSharp.Formats.Tiff;
using MetadataSharp.Formats.WebP;

// Byte-level metadata dump for the differential parity harness, deliberately restricted to the
// public API: whatever this tool prints is what a library consumer can read, and a tag it cannot
// name is reported as a numeric key rather than silently promoted.
//
// Grammar - one record per line, tab-separated fields, LF endings, UTF-8 without BOM:
//   FILE    <path, escaped like a value>
//   TAG     <key>  <type>  <count>  <value>
//   STATUS  ok | refused | not-attempted | error  <detail>
// A file whose reader refused or was not attempted simply carries no TAG records.
internal static class Program
{
    private const long MaxWholeFileBytes = 64L * 1024 * 1024;

    private static int Main(string[] args)
    {
        var paths = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "-h" or "--help")
            {
                Console.Error.WriteLine("usage: MetadataSharp.Dump [--files-from FILE|-] [file...]");
                return 0;
            }
            if (arg == "--files-from")
            {
                if (++i == args.Length) { Console.Error.WriteLine("--files-from wants a path or -"); return 2; }
                foreach (string line in args[i] == "-" ? Console.In.ReadToEnd().Split('\n') : File.ReadLines(args[i]))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length != 0) paths.Add(trimmed);
                }
                continue;
            }
            if (arg.Length != 0) paths.Add(arg);
        }
        if (paths.Count == 0) { Console.Error.WriteLine("no input paths given"); return 2; }

        using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
        stdout.NewLine = "\n";
        foreach (string path in paths)
        {
            Record(stdout, "FILE", Escape(path));
            try { Dump(path, stdout); }
            catch (MetadataException failure) { Record(stdout, "STATUS", "refused", failure.Code.ToString()); }
            // A bug in a reader is a fact worth reporting precisely, so the type and the first line of
            // the message travel with the record; the harness counts this status as an unsound run.
            catch (Exception failure)
            {
                string reason = failure.GetType().Name + ": " + failure.Message.Split('\n')[0];
                Record(stdout, "STATUS", "error", Escape(reason.Length > 180 ? reason[..180] : reason));
            }
        }
        stdout.Flush();
        return 0;
    }

    private static void Dump(string path, StreamWriter stdout)
    {
        var head = new byte[16];
        long length;
        using (var probe = File.OpenRead(path))
        {
            length = probe.Length;
            int want = (int)Math.Min(length, head.Length);
            if (want > 0) _ = probe.Read(head.AsSpan(0, want));
        }
        if (At(head, 0, 0xFF, 0xD8)) { Jpeg(path, stdout, length); return; }
        if (At(head, 0, 0x89, 0x50, 0x4E, 0x47)) { Png(path, stdout, length); return; }
        if (At(head, 0, 0x52, 0x49, 0x46, 0x46) && At(head, 8, 0x57, 0x45, 0x42, 0x50)) { WebP(path, stdout, length); return; }
        if (At(head, 0, 0x49, 0x49, 0x2A, 0x00) || At(head, 0, 0x4D, 0x4D, 0x00, 0x2A)) { Tiff(path, stdout, length); return; }
        if (At(head, 0, 0x38, 0x42, 0x50, 0x53)) { Psd(path, stdout, length); return; }
        if (At(head, 4, 0x6A, 0x50, 0x20, 0x20)) { Jp2(path, stdout, length); return; }
        if (At(head, 0, 0x3C, 0x3F) || At(head, 0, 0xEF, 0xBB, 0xBF)) { Sidecar(path, stdout, length); return; }
        if (At(head, 4, 0x66, 0x74, 0x79, 0x70)) { Record(stdout, "STATUS", "not-attempted", "iso-bmff"); return; }
        if (At(head, 0, 0x1A, 0x45, 0xDF, 0xA3)) { Record(stdout, "STATUS", "not-attempted", "matroska"); return; }
        Record(stdout, "STATUS", "not-attempted", "magic");
    }

    private static void Jpeg(string path, StreamWriter stdout, long length)
    {
        if (!WithinBudget(length, stdout)) return;
        using JpegMetadataDocument document = JpegMetadataDocument.Open(path);
        Exif(document.Exif, stdout);
        if (document.ReadIptc() is IptcPacket iptc) Iptc(iptc, stdout);
        if (document.ReadXmp() is XmpPacket xmp) Xmp(xmp, stdout);
        Done(stdout);
    }

    private static void Png(string path, StreamWriter stdout, long length)
    {
        if (!WithinBudget(length, stdout)) return;
        using PngMetadataDocument document = PngMetadataDocument.Open(path);
        Exif(document.ReadExif(), stdout);
        if (document.ReadXmp() is XmpPacket xmp) Xmp(xmp, stdout);
        Done(stdout);
    }

    private static void WebP(string path, StreamWriter stdout, long length)
    {
        if (!WithinBudget(length, stdout)) return;
        using WebPMetadataDocument document = WebPMetadataDocument.Open(path);
        Exif(document.ReadExif(), stdout);
        if (document.ReadXmp() is XmpPacket xmp) Xmp(xmp, stdout);
        Done(stdout);
    }

    private static void Tiff(string path, StreamWriter stdout, long length)
    {
        if (!WithinBudget(length, stdout)) return;
        using TiffMetadataDocument document = TiffMetadataDocument.Open(path);
        TiffLayout layout = document.Layout;
        // The layout is the library's read-side accounting of every field; the bytes of a field are
        // taken from the same file the layout describes. The document's own value accessor is the
        // descriptive-edit target, which refuses most tags and is not what a byte dump wants.
        byte[] data = File.ReadAllBytes(path);
        Dictionary<uint, string> groups = Groups(layout);
        foreach (TiffDirectoryInfo directory in layout.Directories)
        {
            string group = groups.ContainsKey(directory.Offset) ? groups[directory.Offset] : "Thumbnail";
            foreach (TiffFieldInfo field in directory.Fields)
            {
                TiffFieldType type = (TiffFieldType)field.FieldType;
                Tag(stdout, "Exif." + group + ".0x" + Hex(field.TagId), TypeName(type), field.Count,
                    Render(Slice(data, field.ValueRange), type, field.Count, layout.ByteOrder));
            }
        }
        Done(stdout);
    }

    private static void Psd(string path, StreamWriter stdout, long length)
    {
        if (!WithinBudget(length, stdout)) return;
        PsdMetadata metadata = PsdMetadata.Read(File.ReadAllBytes(path));
        if (metadata.GetResource(0x0404) is byte[] resource && resource.Length != 0) Iptc(IptcPacket.Parse(resource), stdout);
        Done(stdout);
    }

    private static void Jp2(string path, StreamWriter stdout, long length)
    {
        if (!WithinBudget(length, stdout)) return;
        Jp2Metadata metadata = Jp2Metadata.Read(File.ReadAllBytes(path));
        for (int i = 0; i < metadata.XmlBoxCount; i++)
        {
            byte[] box = metadata.GetXmlBox(i);
            if (box.AsSpan().StartsWith("<?xpacket"u8) || box.AsSpan().StartsWith("<?xml"u8)) Xmp(XmpPacket.Parse(box), stdout);
        }
        Done(stdout);
    }

    private static void Sidecar(string path, StreamWriter stdout, long length)
    {
        if (!WithinBudget(length, stdout)) return;
        Xmp(XmpPacket.Load(path), stdout);
        Done(stdout);
    }

    private static void Exif(ExifMetadata? metadata, StreamWriter stdout)
    {
        if (metadata is null) return;
        foreach (ExifEntry entry in metadata.Entries)
            Tag(stdout, "Exif." + Group(entry.Key.Directory) + ".0x" + Hex(entry.Key.TagId),
                TypeName(entry.Value.FieldType), entry.Value.Count, Render(entry.Value));
    }

    private static void Iptc(IptcPacket packet, StreamWriter stdout)
    {
        foreach (IptcDataset dataset in packet.Datasets)
        {
            byte[] bytes = new byte[dataset.Value.ByteLength];
            dataset.Value.CopyRawTo(bytes);
            bytes = Trim(bytes);
            string text = (packet.CharacterSet == IptcCharacterSet.Utf8 ? Encoding.UTF8 : Encoding.Latin1).GetString(bytes);
            Tag(stdout, RecordName(dataset.Key.Record) + ".0x" + dataset.Key.Dataset.ToString("x2", CultureInfo.InvariantCulture),
                "String", (uint)bytes.Length, Escape(text));
        }
    }

    private static void Xmp(XmpPacket packet, StreamWriter stdout)
    {
        if (!packet.IsEditable) return;
        foreach (KeyValuePair<XmpPropertyName, XmpValue> property in packet.Properties)
        {
            string key = "Xmp." + Prefix(property.Key.NamespaceUri) + "." + property.Key.LocalName;
            XmpValue value = property.Value;
            if (value.Kind is XmpValueKind.Text)
            {
                string text = value.Text ?? string.Empty;
                Tag(stdout, key, "XmpText", (uint)Encoding.UTF8.GetByteCount(text), Escape(text));
            }
            else if (value.Kind is XmpValueKind.Bag or XmpValueKind.Sequence)
            {
                IReadOnlyList<string> items = value.Items;
                Tag(stdout, key, value.Kind == XmpValueKind.Bag ? "XmpBag" : "XmpSeq", (uint)items.Count, Join(items));
            }
            else
            {
                // Rendered the way the reference tool prints a language alternative, so the parity
                // harness compares text rather than two spellings of the same property.
                var parts = new List<string>();
                foreach (KeyValuePair<string, string> language in value.Languages)
                    parts.Add("lang=\"" + language.Key + "\" " + Escape(language.Value));
                Tag(stdout, key, "LangAlt", (uint)parts.Count, string.Join(", ", parts));
            }
        }
    }

    private static string Join(IReadOnlyList<string> items)
    {
        var parts = new List<string>(items.Count);
        foreach (string item in items) parts.Add(Escape(item));
        return string.Join(", ", parts);
    }

    private static Dictionary<uint, string> Groups(TiffLayout layout)
    {
        var groups = new Dictionary<uint, string>();
        uint? exif = null, gps = null, interop = null, next = null;
        foreach (TiffDirectoryInfo directory in layout.Directories)
        {
            foreach (TiffDirectoryLink link in directory.Links)
            {
                switch (link.Kind)
                {
                    case TiffDirectoryLinkKind.Exif: exif ??= link.TargetIfdOffset; break;
                    case TiffDirectoryLinkKind.Gps: gps ??= link.TargetIfdOffset; break;
                    case TiffDirectoryLinkKind.Interoperability: interop ??= link.TargetIfdOffset; break;
                    case TiffDirectoryLinkKind.NextIfd: next ??= link.TargetIfdOffset; break;
                }
            }
        }
        foreach (TiffDirectoryInfo directory in layout.Directories)
        {
            uint offset = directory.Offset;
            groups[offset] = offset == exif ? "Photo" :
                offset == gps ? "GPSInfo" :
                offset == interop ? "Iop" :
                offset == layout.FirstIfdOffset ? "Image" : "Thumbnail";
        }
        return groups;
    }

    private static string Group(ExifDirectoryKind directory) => directory switch
    {
        ExifDirectoryKind.Ifd0 => "Image",
        ExifDirectoryKind.Exif => "Photo",
        ExifDirectoryKind.Gps => "GPSInfo",
        ExifDirectoryKind.Interop => "Iop",
        _ => "Thumbnail",
    };

    private static string RecordName(byte record) => record switch
    {
        1 => "Iptc.Envelope",
        2 => "Iptc.Application2",
        _ => "Iptc.Record" + record.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>The conventional prefix of a standard namespace URI; anything else keeps its local part.</summary>
    private static string Prefix(string namespaceUri) => namespaceUri switch
    {
        "http://purl.org/dc/elements/1.1/" => "dc",
        "http://purl.org/dc/terms/" => "dcterms",
        "http://purl.org/dc/dcmitype/" => "dcmitype",
        "http://ns.adobe.com/xap/1.0/" => "xmp",
        "http://ns.adobe.com/xap/1.0/mm/" => "xmpMM",
        "http://ns.adobe.com/xap/1.0/rights/" => "xmpRights",
        "http://ns.adobe.com/xap/1.0/bj/" => "xmpBJ",
        "http://ns.adobe.com/xap/1.0/Note/" => "xmpNote",
        "http://ns.adobe.com/xap/1.0/sType/ResourceEvent#" => "stEvt",
        "http://ns.adobe.com/xap/1.0/sType/ResourceRef#" => "stRef",
        "http://ns.adobe.com/xap/1.0/sType/Part#" => "stPart",
        "http://ns.adobe.com/photoshop/1.0/" => "photoshop",
        "http://ns.adobe.com/photograph/1.0/" => "photograph",
        "http://ns.adobe.com/tiff/1.0/" => "tiff",
        "http://ns.adobe.com/exif/1.0/" => "exif",
        "http://ns.adobe.com/exif/1.0/aux/" => "aux",
        "http://ns.adobe.com/pdf/1.3/" => "pdf",
        "http://ns.adobe.com/xmp/1.0/DynamicMedia/" => "xmpDM",
        "http://iptc.org/std/Iptc4xmpCore/1.0/xmlns/" => "Iptc4xmpCore",
        "http://iptc.org/std/Iptc4xmpExt/2008-02-29/" => "Iptc4xmpExt",
        "http://prismstandard.org/namespaces/basic/1.0/" => "prism",
        "http://www.day.com/schemas/handler/1.0/" => "handler",
        "http://www.flickr.com/ns/creative-commons/2006-06-15/" => "cc",
        "http://www.w3.org/XML/1998/namespace" => "xml",
        "http://www.w3.org/1999/02/22-rdf-syntax-ns#" => "rdf",
        _ => Tail(namespaceUri),
    };

    /// <summary>
    /// A display prefix for a namespace the table does not know. A packet's own serialization prefix is
    /// deliberately not part of the property name, so the vendor label in the authority is the closest
    /// stable guess; a URI without one is reported verbatim rather than as a plausible-looking short name.
    /// </summary>
    private static string Tail(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed) || parsed.Host.Length == 0) return uri;
        string first = parsed.Host.Split('.')[0];
        return first is "www" or "ns" || first.Length == 0 ? parsed.Host : first;
    }

    private static string TypeName(TiffFieldType type) => type switch
    {
        TiffFieldType.Byte => "Byte",
        TiffFieldType.Ascii => "Ascii",
        TiffFieldType.Short => "Short",
        TiffFieldType.Long => "Long",
        TiffFieldType.Rational => "Rational",
        TiffFieldType.SByte => "SByte",
        TiffFieldType.Undefined => "Undefined",
        TiffFieldType.SShort => "SShort",
        TiffFieldType.SLong => "SLong",
        TiffFieldType.SRational => "SRational",
        TiffFieldType.Float => "Float",
        TiffFieldType.Double => "Double",
        _ => "Type" + ((ushort)type).ToString(CultureInfo.InvariantCulture),
    };

    private static int Size(TiffFieldType type) => type switch
    {
        TiffFieldType.Byte or TiffFieldType.Ascii or TiffFieldType.SByte or TiffFieldType.Undefined => 1,
        TiffFieldType.Short or TiffFieldType.SShort => 2,
        TiffFieldType.Long or TiffFieldType.SLong or TiffFieldType.Float => 4,
        TiffFieldType.Rational or TiffFieldType.SRational or TiffFieldType.Double => 8,
        _ => 0,
    };

    private static string Render(ExifValue value)
    {
        var bytes = new byte[value.ByteLength];
        value.CopyRawTo(bytes);
        return Render(bytes, value.FieldType, value.Count, value.ByteOrder);
    }

    /// <summary>Every element the declared type and count ask for, or a marker saying they are not there.</summary>
    private static string Render(byte[] bytes, TiffFieldType type, uint count, Endianness order)
    {
        if (type is TiffFieldType.Ascii) return Escape(Encoding.Latin1.GetString(Trim(bytes)));
        int size = Size(type);
        if (size == 0) return "undefined-type";
        if ((long)count * size > bytes.Length) return "truncated-value";
        var text = new StringBuilder();
        for (int i = 0; i < (int)count; i++)
        {
            if (i != 0) text.Append(' ');
            int at = i * size;
            text.Append(type switch
            {
                TiffFieldType.Byte => bytes[at].ToString(CultureInfo.InvariantCulture),
                TiffFieldType.SByte => unchecked((sbyte)bytes[at]).ToString(CultureInfo.InvariantCulture),
                TiffFieldType.Undefined => bytes[at].ToString(CultureInfo.InvariantCulture),
                TiffFieldType.Short => U16(bytes, at, order).ToString(CultureInfo.InvariantCulture),
                TiffFieldType.SShort => S16(bytes, at, order).ToString(CultureInfo.InvariantCulture),
                TiffFieldType.Long => U32(bytes, at, order).ToString(CultureInfo.InvariantCulture),
                TiffFieldType.SLong => S32(bytes, at, order).ToString(CultureInfo.InvariantCulture),
                TiffFieldType.Float => ReadSingle(bytes, at, order).ToString("R", CultureInfo.InvariantCulture),
                TiffFieldType.Double => ReadDouble(bytes, at, order).ToString("R", CultureInfo.InvariantCulture),
                TiffFieldType.Rational => U32(bytes, at, order).ToString(CultureInfo.InvariantCulture) + "/" +
                    U32(bytes, at + 4, order).ToString(CultureInfo.InvariantCulture),
                _ => S32(bytes, at, order).ToString(CultureInfo.InvariantCulture) + "/" +
                    S32(bytes, at + 4, order).ToString(CultureInfo.InvariantCulture),
            });
        }
        return text.ToString();
    }

    private static ushort U16(byte[] bytes, int at, Endianness order) => order == Endianness.LittleEndian ?
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at, 2)) : BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(at, 2));

    private static short S16(byte[] bytes, int at, Endianness order) => order == Endianness.LittleEndian ?
        BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(at, 2)) : BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(at, 2));

    private static uint U32(byte[] bytes, int at, Endianness order) => order == Endianness.LittleEndian ?
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at, 4)) : BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at, 4));

    private static int S32(byte[] bytes, int at, Endianness order) => order == Endianness.LittleEndian ?
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at, 4)) : BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(at, 4));

    private static float ReadSingle(byte[] bytes, int at, Endianness order) => order == Endianness.LittleEndian ?
        BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at, 4)) : BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(at, 4));

    private static double ReadDouble(byte[] bytes, int at, Endianness order) => order == Endianness.LittleEndian ?
        BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(at, 8)) : BinaryPrimitives.ReadDoubleBigEndian(bytes.AsSpan(at, 8));

    private static string Hex(ushort value) => value.ToString("x4", CultureInfo.InvariantCulture);

    private static byte[] Trim(byte[] bytes)
    {
        int end = bytes.Length;
        while (end > 0 && bytes[end - 1] == 0) end--;
        return end == bytes.Length ? bytes : bytes[..end];
    }

    /// <summary>Escapes everything that would break the one-record-per-line grammar.</summary>
    private static string Escape(string value)
    {
        if (value.Length == 0) return value;
        bool needs = false;
        foreach (char c in value) if (c < 0x20 || c == 0x7F || c == '\\') { needs = true; break; }
        if (!needs) return value;
        var text = new StringBuilder(value.Length + 8);
        foreach (char c in value)
        {
            if (c == '\\') text.Append("\\\\");
            else if (c == '\t') text.Append("\\t");
            else if (c == '\n') text.Append("\\n");
            else if (c == '\r') text.Append("\\r");
            else if (c < 0x20 || c == 0x7F) text.Append("\\x").Append(((byte)c).ToString("x2", CultureInfo.InvariantCulture));
            else text.Append(c);
        }
        return text.ToString();
    }

    /// <summary>The bytes a layout range points at, clipped to the file: a corrupt range is a value to
    /// report, not a reason to stop printing the rest of the directory.</summary>
    private static byte[] Slice(byte[] data, TiffByteRange range)
    {
        if (range.Offset < 0 || range.Length < 0 || range.Offset >= data.Length) return [];
        int available = (int)Math.Min(range.Length, (long)data.Length - range.Offset);
        var bytes = new byte[available];
        Array.Copy(data, range.Offset, bytes, 0, available);
        return bytes;
    }

    private static bool At(byte[] data, int at, params int[] expected)
    {
        if (at + expected.Length > data.Length) return false;
        for (int i = 0; i < expected.Length; i++) if (data[at + i] != expected[i]) return false;
        return true;
    }

    private static bool WithinBudget(long length, StreamWriter stdout)
    {
        if (length <= MaxWholeFileBytes) return true;
        Record(stdout, "STATUS", "not-attempted", "too-large");
        return false;
    }

    private static void Tag(StreamWriter stdout, string key, string type, uint count, string value) =>
        Record(stdout, "TAG", key, type, count.ToString(CultureInfo.InvariantCulture), value);

    private static void Done(StreamWriter stdout) => Record(stdout, "STATUS", "ok");

    private static void Record(StreamWriter stdout, params string[] fields)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            if (i != 0) stdout.Write('\t');
            stdout.Write(fields[i]);
        }
        stdout.Write('\n');
    }
}
