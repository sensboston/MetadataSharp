using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Bmff;
using MetadataSharp.Formats.Exv;
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
// A file whose reader refused or was not attempted simply carries no TAG records. A TIFF or PSD
// that was read, with its IPTC stream or XMP packet refused, is ok with the detail
// iptc-refused:<code>, xmp-refused:<code>, or both separated by a comma.
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
        // ORF ("IIRO", "MMOR") and RW2 ("IIU\0") are TIFF structures under their own identifier.
        if (At(head, 0, 0x49, 0x49, 0x52, 0x4F) || At(head, 0, 0x4D, 0x4D, 0x4F, 0x52) || At(head, 0, 0x49, 0x49, 0x55, 0x00)) { Tiff(path, stdout, length); return; }
        if (At(head, 0, 0x38, 0x42, 0x50, 0x53)) { Psd(path, stdout, length); return; }
        if (At(head, 0, 0xFF, 0x01, 0x45, 0x78, 0x69, 0x76, 0x32)) { Exv(path, stdout, length); return; }
        if (At(head, 4, 0x6A, 0x50, 0x20, 0x20)) { Jp2(path, stdout, length); return; }
        if (At(head, 0, 0x3C, 0x3F) || At(head, 0, 0xEF, 0xBB, 0xBF)) { Sidecar(path, stdout, length); return; }
        if (At(head, 4, 0x66, 0x74, 0x79, 0x70)) { Bmff(path, stdout, length); return; }
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
        byte[] file = File.ReadAllBytes(path);
        ExifMetadata exif = TiffExif.Read(file);
        Exif(exif, stdout);
        // IPTC or XMP that its reader refuses does not take the EXIF of the file with it: the rows
        // already written stand, and each refusal travels in the detail of the status.
        var refused = new List<string>();
        IptcPacket? iptc = null;
        try { iptc = TiffIptc.Read(file); }
        catch (MetadataException failure) { refused.Add("iptc-refused:" + failure.Code); }
        if (iptc is not null) Iptc(iptc, stdout);
        XmpPacket? xmp = null;
        try { xmp = XmlPacket(exif); }
        catch (MetadataException failure) { refused.Add("xmp-refused:" + failure.Code); }
        if (xmp is not null) Xmp(xmp, stdout);
        Done(stdout, refused);
    }

    /// <summary>The XMP packet a TIFF-based file stores as the value of tag 700 in its primary directory.</summary>
    private static XmpPacket? XmlPacket(ExifMetadata exif)
    {
        if (!exif.TryGetEntry(new ExifTagKey(ExifDirectoryKind.Ifd0, 0x02BC), out ExifEntry? entry) || entry.Value.ByteLength == 0)
            return null;
        byte[] packet = new byte[entry.Value.ByteLength];
        entry.Value.CopyRawTo(packet);
        return XmpPacket.Parse(packet);
    }

    private static void Exv(string path, StreamWriter stdout, long length)
    {
        if (!WithinBudget(length, stdout)) return;
        ExvMetadata metadata = ExvMetadata.Read(File.ReadAllBytes(path));
        Exif(metadata.Exif, stdout);
        if (metadata.Iptc is IptcPacket iptc) Iptc(iptc, stdout);
        if (metadata.Xmp is XmpPacket xmp) Xmp(xmp, stdout);
        Done(stdout);
    }

    private static void Bmff(string path, StreamWriter stdout, long length)
    {
        if (!WithinBudget(length, stdout)) return;
        BmffMetadata metadata = BmffMetadata.Read(File.ReadAllBytes(path));
        Exif(metadata.Exif, stdout);
        if (metadata.Xmp is XmpPacket xmp) Xmp(xmp, stdout);
        Done(stdout);
    }

    private static void Psd(string path, StreamWriter stdout, long length)
    {
        if (!WithinBudget(length, stdout)) return;
        PsdMetadata metadata = PsdMetadata.Read(File.ReadAllBytes(path));
        if (metadata.GetResource(0x0404) is byte[] resource && resource.Length != 0) Iptc(IptcPacket.Parse(resource), stdout);
        // As for a TIFF: a packet the XMP reader refuses leaves the rows already written in place.
        var refused = new List<string>();
        XmpPacket? xmp = null;
        try
        {
            if (metadata.GetResource(0x0424) is byte[] packet && packet.Length != 0) xmp = XmpPacket.Parse(packet);
        }
        catch (MetadataException failure) { refused.Add("xmp-refused:" + failure.Code); }
        if (xmp is not null) Xmp(xmp, stdout);
        Done(stdout, refused);
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
            Tag(stdout, "Exif." + Group(entry.Key.Directory) + "." + Name(entry.Key),
                TypeName(entry.Value.FieldType), entry.Value.Count, Render(entry.Value));
        if (metadata.MakerNote is MakerNote makerNote)
        {
            string group = makerNote.Format switch { MakerNoteFormat.Canon => "Canon", _ => makerNote.Format.ToString() };
            foreach (MakerNoteEntry entry in makerNote.Entries)
            {
                // The reference tool prints an array tag it splits as its fields only, under a
                // group named after the array.
                if (entry.Fields.Count != 0)
                {
                    // AFInfo is printed whole and its fields share the MakerNote's own group.
                    bool olympusIfd = makerNote.Format is MakerNoteFormat.Olympus2 or MakerNoteFormat.OMSystem;
                    bool keepParent = olympusIfd || makerNote.Format == MakerNoteFormat.Canon && entry.TagId == 0x0026;
                    if (keepParent)
                        Tag(stdout, "Exif." + group + "." + entry.Name, TypeName(entry.Value.FieldType), entry.Value.Count, Render(entry.Value));
                    string fieldGroup = olympusIfd ? OlympusGroup(entry.TagId)
                        : keepParent ? group
                        : makerNote.Format == MakerNoteFormat.Nikon3 ? "Nikon" + NikonArrayGroup(entry)
                        : makerNote.Format is MakerNoteFormat.Sony1 or MakerNoteFormat.Sony2 ? SonyArrayGroup(entry.TagId)
                        : group + ArrayGroup(entry.TagId);
                    foreach (MakerNoteField field in entry.Fields)
                        Tag(stdout, "Exif." + fieldGroup + "." + field.Name,
                            TypeName(field.Value.FieldType), field.Value.Count, Render(field.Value));
                    continue;
                }
                Tag(stdout, "Exif." + group + "." + (entry.Name ?? "0x" + entry.TagId.ToString("x4", CultureInfo.InvariantCulture)),
                    TypeName(entry.Value.FieldType), entry.Value.Count, Render(entry.Value));
            }
        }
    }

    private static string OlympusGroup(ushort tag) => tag switch
    {
        0x2010 => "OlympusEq", 0x2020 => "OlympusCs", 0x2030 => "OlympusRd", 0x2031 => "OlympusRd2",
        0x2040 => "OlympusIp", 0x2050 => "OlympusFi", 0x3000 => "OlympusRi",
        _ => "Olympus0x" + tag.ToString("x4", CultureInfo.InvariantCulture)
    };

    private static string SonyArrayGroup(ushort tag) => tag switch
    {
        0x2010 => "Sony2010e", 0x3000 => "SonySInfo1", 0x9400 => "SonyMisc3c", 0x9402 => "Sony2Fp",
        0x9403 => "SonyMisc1", 0x9404 => "SonyMisc2b",
        _ => "Sony0x" + tag.ToString("x4", CultureInfo.InvariantCulture)
    };

    /// <summary>The reference tool's group for a split Nikon3 array, chosen like the layout: by tag and version.</summary>
    private static string NikonArrayGroup(MakerNoteEntry entry)
    {
        byte[] raw = new byte[Math.Min(entry.Value.ByteLength, 4)];
        if (raw.Length == 4)
        {
            byte[] all = new byte[entry.Value.ByteLength];
            entry.Value.CopyRawTo(all);
            raw = all[..4];
        }
        string version = raw.Length == 4 ? Encoding.ASCII.GetString(raw) : "";
        return entry.TagId switch
        {
            0x001F => "Vr", 0x0023 => "Pc", 0x0024 => "Wt", 0x0025 => "Ii", 0x0088 => "Af",
            0x0098 => version switch { "0100" => "Ld1", "0204" => "Ld3", "0800" or "0801" or "0802" => "Ld4", _ => "Ld2" },
            0x0091 => "Si02xx", 0x0097 => "Cb2b",
            0x00A8 => version switch { "0100" or "0101" => "Fl1", "0102" => "Fl2", "0106" => "Fl6", "0107" or "0108" => "Fl7", _ => "Fl3" },
            0x00B0 => "Me", 0x00B7 => version == "0100" ? "Af2" : "Af22", 0x00B8 => "Fi", 0x00B9 => "AFT",
            _ => "0x" + entry.TagId.ToString("x4", CultureInfo.InvariantCulture)
        };
    }

    private static string ArrayGroup(ushort tag) => tag switch
    {
        0x0001 => "Cs",
        0x0004 => "Si",
        0x0005 => "Pa",
        0x000F => "Cf",
        0x0012 => "Pi",
        0x0093 => "Fi",
        0x00A0 => "Pr",
        0x0035 => "Ti",
        0x4013 => "AfMiAdj",
        0x4016 => "VigCor2",
        0x4018 => "LiOp",
        0x4019 => "Le",
        0x4020 => "Am",
        0x4021 => "Me",
        0x4025 => "Hdr",
        0x4028 => "AfC",
        _ => "0x" + tag.ToString("x4", CultureInfo.InvariantCulture)
    };

    private static void Iptc(IptcPacket packet, StreamWriter stdout)
    {
        foreach (IptcDataset dataset in packet.Datasets)
        {
            byte[] bytes = new byte[dataset.Value.ByteLength];
            dataset.Value.CopyRawTo(bytes);
            bytes = Trim(bytes);
            string text = (packet.CharacterSet == IptcCharacterSet.Utf8 ? Encoding.UTF8 : Encoding.Latin1).GetString(bytes);
            string field = IptcDatasetNames.TryGetName(dataset.Key, out string? name)
                ? name : "0x" + dataset.Key.Dataset.ToString("x2", CultureInfo.InvariantCulture);
            Tag(stdout, RecordName(dataset.Key.Record) + "." + field, "String", (uint)bytes.Length, Escape(text));
        }
    }

    private static void Xmp(XmpPacket packet, StreamWriter stdout)
    {
        foreach (XmpEntry entry in packet.Entries)
        {
            string key = "Xmp." + entry.Prefix + "." + entry.Path;
            if (entry.HasChildEntries)
            {
                // The reference tool prints a structure, and an array whose items are not plain text,
                // as a text row naming the form; the fields and the items follow under their own keys.
                string form = "type=\"" + entry.Kind + "\"";
                Tag(stdout, key, "XmpText", (uint)form.Length, form);
            }
            else if (entry.Kind is XmpEntryKind.Text)
            {
                string text = entry.Values.Count == 0 ? string.Empty : entry.Values[0];
                Tag(stdout, key, "XmpText", (uint)Encoding.UTF8.GetByteCount(text), Escape(text));
            }
            else if (entry.Kind is XmpEntryKind.LangAlt)
            {
                // Rendered the way the reference tool prints a language alternative, so the parity
                // harness compares text rather than two spellings of the same property.
                var parts = new List<string>();
                foreach ((string language, string value) in entry.Languages)
                    parts.Add("lang=\"" + language + "\" " + Escape(value));
                Tag(stdout, key, "LangAlt", (uint)parts.Count, string.Join(", ", parts));
            }
            else
            {
                Tag(stdout, key, "Xmp" + entry.Kind, (uint)entry.Values.Count, Join(entry.Values));
            }
        }
    }

    private static string Join(IReadOnlyList<string> items)
    {
        var parts = new List<string>(items.Count);
        foreach (string item in items) parts.Add(Escape(item));
        return string.Join(", ", parts);
    }

    /// <summary>Which EXIF directory a layout offset is, because the layout reports offsets and the
    /// tag vocabulary is keyed by directory. The links in the tree decide, exactly as they do for the
    /// group label the reference output uses.</summary>
    private static Dictionary<uint, ExifDirectoryKind> Groups(TiffLayout layout)
    {
        var groups = new Dictionary<uint, ExifDirectoryKind>();
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
            groups[offset] = offset == exif ? ExifDirectoryKind.Exif :
                offset == gps ? ExifDirectoryKind.Gps :
                offset == interop ? ExifDirectoryKind.Interop :
                offset == layout.FirstIfdOffset ? ExifDirectoryKind.Ifd0 : ExifDirectoryKind.Thumbnail;
        }
        return groups;
    }

    /// <summary>The field name the standards give a tag, or its numeric ID when this release does not
    /// name it: an unnamed tag is reported as a gap, never as a plausible-looking invented label.</summary>
    private static string Name(ExifTagKey key)
    {
        ExifTagDefinition? definition = ExifTags.Find(key);
        return definition is null ? "0x" + Hex(key.TagId) : definition.Name;
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

    /// <summary>Ends a file that was read, naming the kinds of metadata whose reader refused them.</summary>
    private static void Done(StreamWriter stdout, List<string> refused)
    {
        if (refused.Count == 0) Done(stdout);
        else Record(stdout, "STATUS", "ok", string.Join(",", refused));
    }

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
