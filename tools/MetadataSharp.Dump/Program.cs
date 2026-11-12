using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats;
using MetadataSharp.Formats.Bmff;
using MetadataSharp.Formats.Cr3;
using MetadataSharp.Formats.Exv;
using MetadataSharp.Formats.Jp2;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Formats.Jxl;
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
//   TAG     <key>  <type>  <count>  <value>  [<interpreted>]
//   STATUS  ok | refused | not-attempted | error  <detail>
// The sixth TAG field is optional. It is what the library makes of the value - ExifTags.Interpret for
// the standard EXIF directories, MakerNote.Interpret for MakerNote tags and their fields, and
// IptcDatasetNames.Interpret for IPTC datasets - and it is empty where that is null. A record without
// the field says no interpretation is offered for the tag at all, which is the case for XMP records
// and for the elements of an array nested in a MakerNote sub-directory.
// A file whose reader refused or was not attempted simply carries no TAG records. A JPEG, PNG, TIFF or PSD
// that was read, with one kind of its metadata refused, is ok with the detail exif-refused:<code>,
// iptc-refused:<code> or xmp-refused:<code>, several of them separated by commas.
//
// With --previews the TAG records give way to one record per preview the library lists, in its order:
//   PREVIEW <index from 1>  <mime type>  <width>x<height>  <length in bytes>  <source>
// An unknown dimension is written as 0.
//
// With --icc they give way to at most one record, for the ICC profile IccProfiles.Read returns:
//   ICC     <length in bytes>  <MD5 of the bytes, in lower-case hexadecimal>
// The reference prints the profile itself (exiv2 -pC) and its own tests keep the MD5 of that output,
// so the record says of the profile what the reference's expectations say of it.
//
// With --comments they give way to one record per comment Comments.Read lists, in its order:
//   COMMENT <index from 1>  <text, escaped like a value>
// The reference prints one comment and a line end (exiv2 -pc): for a JPEG, the first of these.
internal static class Program
{
    private const long MaxWholeFileBytes = 64L * 1024 * 1024;

    private static int Main(string[] args)
    {
        var paths = new List<string>();
        bool previews = false, icc = false, comments = false;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "-h" or "--help")
            {
                Console.Error.WriteLine("usage: MetadataSharp.Dump [--previews|--icc|--comments] [--files-from FILE|-] [file...]");
                return 0;
            }
            if (arg == "--previews") { previews = true; continue; }
            if (arg == "--icc") { icc = true; continue; }
            if (arg == "--comments") { comments = true; continue; }
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
        if ((previews ? 1 : 0) + (icc ? 1 : 0) + (comments ? 1 : 0) > 1)
        {
            Console.Error.WriteLine("--previews, --icc and --comments each replace the TAG records: give one");
            return 2;
        }
        if (paths.Count == 0) { Console.Error.WriteLine("no input paths given"); return 2; }

        using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
        stdout.NewLine = "\n";
        foreach (string path in paths)
        {
            Record(stdout, "FILE", Escape(path));
            try
            {
                if (previews) ListPreviews(path, stdout);
                else if (icc) IccProfile(path, stdout);
                else if (comments) ListComments(path, stdout);
                else Dump(path, stdout);
            }
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

    private static void ListPreviews(string path, StreamWriter stdout)
    {
        if (!WithinBudget(new FileInfo(path).Length, stdout)) return;
        IReadOnlyList<PreviewImage> found = Previews.List(File.ReadAllBytes(path));
        for (int i = 0; i < found.Count; i++)
        {
            PreviewImage preview = found[i];
            string size = (preview.Width ?? 0).ToString(CultureInfo.InvariantCulture) + "x" + (preview.Height ?? 0).ToString(CultureInfo.InvariantCulture);
            Record(stdout, "PREVIEW", (i + 1).ToString(CultureInfo.InvariantCulture), preview.MimeType, size,
                preview.Length.ToString(CultureInfo.InvariantCulture), preview.Source);
        }
        Done(stdout);
    }

    private static void IccProfile(string path, StreamWriter stdout)
    {
        if (!WithinBudget(new FileInfo(path).Length, stdout)) return;
        if (IccProfiles.Read(File.ReadAllBytes(path)) is byte[] profile)
        {
            string digest = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(profile)).ToLowerInvariant();
            Record(stdout, "ICC", profile.Length.ToString(CultureInfo.InvariantCulture), digest);
        }
        Done(stdout);
    }

    private static void ListComments(string path, StreamWriter stdout)
    {
        if (!WithinBudget(new FileInfo(path).Length, stdout)) return;
        IReadOnlyList<string> found = Comments.Read(File.ReadAllBytes(path));
        for (int i = 0; i < found.Count; i++)
            Record(stdout, "COMMENT", (i + 1).ToString(CultureInfo.InvariantCulture), Escape(found[i]));
        Done(stdout);
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
        // CR3 is a BMFF file of the major brand "crx " that keeps its metadata outside a meta box.
        if (At(head, 4, 0x66, 0x74, 0x79, 0x70) && At(head, 8, 0x63, 0x72, 0x78, 0x20)) { Cr3(path, stdout, length); return; }
        if (At(head, 4, 0x66, 0x74, 0x79, 0x70)) { Bmff(path, stdout, length); return; }
        // JPEG XL: the signature box of a container (its file type box comes second), or the two
        // bytes a bare codestream starts with, which holds no metadata.
        if (At(head, 0, 0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A) || At(head, 0, 0xFF, 0x0A)) { Jxl(path, stdout, length); return; }
        if (At(head, 0, 0x1A, 0x45, 0xDF, 0xA3)) { Record(stdout, "STATUS", "not-attempted", "matroska"); return; }
        Record(stdout, "STATUS", "not-attempted", "magic");
    }

    private static void Jpeg(string path, StreamWriter stdout, long length)
    {
        if (!WithinBudget(length, stdout)) return;
        using JpegMetadataDocument document = JpegMetadataDocument.Open(path);
        Exif(document.Exif, stdout);
        // The document reads each kind on its own, so a refused IPTC stream or XMP packet leaves the
        // rows of the other kinds in place and travels in the detail of the status, as for a TIFF.
        var refused = new List<string>();
        IptcPacket? iptc = null;
        try { iptc = document.ReadIptc(); }
        catch (MetadataException failure) { refused.Add("iptc-refused:" + failure.Code); }
        if (iptc is not null) Iptc(iptc, stdout);
        XmpPacket? xmp = null;
        try { xmp = document.ReadXmp(); }
        catch (MetadataException failure) { refused.Add("xmp-refused:" + failure.Code); }
        if (xmp is not null) Xmp(xmp, stdout);
        Done(stdout, refused);
    }

    private static void Png(string path, StreamWriter stdout, long length)
    {
        if (!WithinBudget(length, stdout)) return;
        using PngMetadataDocument document = PngMetadataDocument.Open(path);
        // As for a JPEG: each kind is read on its own, and a refusal travels in the detail of the status.
        var refused = new List<string>();
        ExifMetadata? exif = null;
        try { exif = document.ReadExif(); }
        catch (MetadataException failure) { refused.Add("exif-refused:" + failure.Code); }
        Exif(exif, stdout);
        IptcPacket? iptc = null;
        try { iptc = document.ReadIptc(); }
        catch (MetadataException failure) { refused.Add("iptc-refused:" + failure.Code); }
        if (iptc is not null) Iptc(iptc, stdout);
        XmpPacket? xmp = null;
        try { xmp = document.ReadXmp(); }
        catch (MetadataException failure) { refused.Add("xmp-refused:" + failure.Code); }
        if (xmp is not null) Xmp(xmp, stdout);
        Done(stdout, refused);
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

    private static void Cr3(string path, StreamWriter stdout, long length)
    {
        if (!WithinBudget(length, stdout)) return;
        Cr3Metadata metadata = Cr3Metadata.Read(File.ReadAllBytes(path));
        // The note is a box of its own here, not the value of a tag: the reference prints no rows
        // for where it starts and how it is ordered.
        Exif(metadata.Exif, stdout, noteRows: false);
        if (metadata.Xmp is XmpPacket xmp) Xmp(xmp, stdout);
        Done(stdout);
    }

    private static void Jxl(string path, StreamWriter stdout, long length)
    {
        if (!WithinBudget(length, stdout)) return;
        // A jumb box is only a fact about the file, and the record grammar has no line for it.
        JxlMetadata metadata = JxlMetadata.Read(File.ReadAllBytes(path));
        Exif(metadata.Exif, stdout);
        if (metadata.Xmp is XmpPacket xmp) Xmp(xmp, stdout);
        Done(stdout);
    }

    private static void Psd(string path, StreamWriter stdout, long length)
    {
        if (!WithinBudget(length, stdout)) return;
        PsdMetadata metadata = PsdMetadata.Read(File.ReadAllBytes(path));
        // As for a TIFF: a resource its reader refuses leaves the rows of the other kinds in place.
        var refused = new List<string>();
        ExifMetadata? exif = null;
        try { exif = metadata.ReadExif(); }
        catch (MetadataException failure) { refused.Add("exif-refused:" + failure.Code); }
        Exif(exif, stdout);
        IptcPacket? iptc = null;
        try { iptc = metadata.ReadIptc(); }
        catch (MetadataException failure) { refused.Add("iptc-refused:" + failure.Code); }
        if (iptc is not null) Iptc(iptc, stdout);
        XmpPacket? xmp = null;
        try { xmp = metadata.ReadXmp(); }
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

    private static void Exif(ExifMetadata? metadata, StreamWriter stdout, bool noteRows = true)
    {
        if (metadata is null) return;
        foreach (ExifEntry entry in metadata.Entries)
            Tag(stdout, "Exif." + Group(entry.Key.Directory) + "." + Name(entry.Key),
                TypeName(entry.Value.FieldType), entry.Value.Count, Value(entry),
                ExifTags.Interpret(entry.Key, entry.Value));
        if (metadata.MakerNote is MakerNote makerNote)
        {
            string group = makerNote.Format switch { MakerNoteFormat.Canon => "Canon", _ => makerNote.Format.ToString() };
            // The reference reports where a note it recognises starts and which byte order it is read in
            // as two rows of a group of their own.
            if (noteRows)
            {
                Tag(stdout, "Exif.MakerNote.Offset", "Long", 1, makerNote.Offset.ToString(CultureInfo.InvariantCulture));
                Tag(stdout, "Exif.MakerNote.ByteOrder", "Ascii", 3, makerNote.ByteOrder == Endianness.BigEndian ? "MM" : "II");
            }
            foreach (MakerNoteEntry entry in makerNote.Entries)
            {
                // The reference tool prints an array tag it splits as its fields only, under a
                // group named after the array.
                if (entry.Fields.Count != 0)
                {
                    // AFInfo is printed whole and its fields share the MakerNote's own group.
                    bool olympusIfd = makerNote.Format is MakerNoteFormat.Olympus2 or MakerNoteFormat.OMSystem;
                    // So is the Nikon preview pointer, whose fields are the directory it points to.
                    bool nikonPreview = makerNote.Format == MakerNoteFormat.Nikon3 && entry.TagId == 0x0011;
                    // And the Sony pointer to the Minolta directory of the DSLR-A100.
                    bool sony = makerNote.Format is MakerNoteFormat.Sony1 or MakerNoteFormat.Sony2;
                    bool sonyMinolta = sony && entry.TagId == 0xB028;
                    bool keepParent = olympusIfd || nikonPreview || sonyMinolta || makerNote.Format == MakerNoteFormat.Canon && entry.TagId == 0x0026;
                    if (keepParent)
                        Tag(stdout, "Exif." + group + "." + entry.Name, TypeName(entry.Value.FieldType), entry.Value.Count, Render(entry.Value),
                            makerNote.Interpret(entry));
                    string fieldGroup = olympusIfd ? OlympusGroup(entry.TagId)
                        : nikonPreview ? "NikonPreview"
                        : sonyMinolta ? "SonyMinolta"
                        : keepParent ? group
                        : makerNote.Format == MakerNoteFormat.Nikon3 ? "Nikon" + NikonArrayGroup(entry)
                        : sony ? SonyArrayGroup(makerNote.Format, entry.TagId)
                        : makerNote.Format == MakerNoteFormat.Minolta ? MinoltaArrayGroup(entry.TagId)
                        : makerNote.Format == MakerNoteFormat.Samsung2 ? "SamsungPictureWizard"
                        : group + ArrayGroup(entry.TagId);
                    foreach (MakerNoteField field in entry.Fields)
                    {
                        // The reference prints the vibration-reduction byte of VRInfo together with the two
                        // bytes that follow it in the array, as one three-byte row.
                        if (fieldGroup == "NikonVr" && field.Index == 4 && entry.Value.ByteLength >= 7)
                        {
                            byte[] array = new byte[entry.Value.ByteLength];
                            entry.Value.CopyRawTo(array);
                            Tag(stdout, "Exif.NikonVr." + field.Name, "Byte", 3,
                                string.Join(' ', array[4..7].Select(b => b.ToString(CultureInfo.InvariantCulture))),
                                makerNote.Interpret(entry, field));
                            continue;
                        }
                        // An entry of a sub-directory that is itself a split array: its elements only,
                        // under the group the reference gives the A100's settings.
                        if (field.Fields.Count != 0)
                        {
                            string elementGroup = (makerNote.Format == MakerNoteFormat.Sony1 ? "Sony1" : "Sony2") + "MltCsA100";
                            foreach (MakerNoteField element in field.Fields)
                                Tag(stdout, "Exif." + elementGroup + "." + element.Name,
                                    TypeName(element.Value.FieldType), element.Value.Count, Render(element.Value));
                            continue;
                        }
                        Tag(stdout, "Exif." + fieldGroup + "." + field.Name,
                            TypeName(field.Value.FieldType), field.Value.Count, Render(field.Value),
                            makerNote.Interpret(entry, field));
                    }
                    continue;
                }
                Tag(stdout, "Exif." + group + "." + (entry.Name ?? "0x" + entry.TagId.ToString("x4", CultureInfo.InvariantCulture)),
                    TypeName(entry.Value.FieldType), entry.Value.Count, Render(entry.Value), makerNote.Interpret(entry));
            }
        }
    }

    private static string OlympusGroup(ushort tag) => tag switch
    {
        0x2010 => "OlympusEq", 0x2020 => "OlympusCs", 0x2030 => "OlympusRd", 0x2031 => "OlympusRd2",
        0x2040 => "OlympusIp", 0x2050 => "OlympusFi", 0x3000 => "OlympusRi",
        _ => "Olympus0x" + tag.ToString("x4", CultureInfo.InvariantCulture)
    };

    private static string MinoltaArrayGroup(ushort tag) => tag switch
    {
        0x0001 => "MinoltaCsOld", 0x0003 => "MinoltaCsNew", 0x0004 => "MinoltaCs7D", 0x0114 => "MinoltaCs5D",
        _ => "Minolta0x" + tag.ToString("x4", CultureInfo.InvariantCulture)
    };

    private static string SonyArrayGroup(MakerNoteFormat format, ushort tag) => tag switch
    {
        0x0114 => format == MakerNoteFormat.Sony1 ? "Sony1Cs" : "Sony2Cs",
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
            0x0091 => version.StartsWith("01", StringComparison.Ordinal) ? "Si01xx" : "Si02xx",
            0x0097 => version switch { "0103" => "Cb3", "0212" => "Cb4", _ => "Cb2b" },
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
            string field = IptcDatasetNames.TryGetName(dataset.Key, out string? name)
                ? name : "0x" + dataset.Key.Dataset.ToString("x2", CultureInfo.InvariantCulture);
            string key = RecordName(dataset.Key.Record) + "." + field;
            // The typed form the library offers; the raw column below is the dump's own and is unchanged.
            string? typed = IptcDatasetNames.Interpret(dataset.Key, dataset.Value);
            // A value in the form the specification gives its dataset is printed as that form reads;
            // one in any other form, a time without seconds for one, is printed as the text it is.
            switch (IptcDatasetNames.FormatOf(dataset.Key))
            {
                case IptcValueFormat.UInt16 when bytes.Length == 2:
                    Tag(stdout, key, "Short", 1, ((bytes[0] << 8) | bytes[1]).ToString(CultureInfo.InvariantCulture), typed);
                    continue;
                case IptcValueFormat.Date when bytes.Length == 8 && Digits(bytes, 0, 8):
                    Tag(stdout, key, "Date", 8, Ascii(bytes, 0, 4) + "-" + Ascii(bytes, 4, 2) + "-" + Ascii(bytes, 6, 2), typed);
                    continue;
                case IptcValueFormat.Time when IptcTime(bytes) is string time:
                    Tag(stdout, key, "Time", 11, time, typed);
                    continue;
                case IptcValueFormat.Binary:
                    Tag(stdout, key, "Undefined", (uint)bytes.Length, string.Join(' ', bytes.Select(b => b.ToString(CultureInfo.InvariantCulture))), typed);
                    continue;
            }
            bytes = Trim(bytes);
            // Without a declared character set the bytes are UTF-8 if they read as UTF-8, and Latin-1 if not.
            string text = packet.CharacterSet == IptcCharacterSet.Utf8 ? Encoding.UTF8.GetString(bytes) : Utf8OrLatin1(bytes);
            Tag(stdout, key, "String", (uint)bytes.Length, Escape(text), typed);
        }
    }

    private static bool Digits(byte[] bytes, int at, int count)
    {
        for (int i = at; i < at + count; i++) if (bytes[i] < '0' || bytes[i] > '9') return false;
        return true;
    }

    private static string Ascii(byte[] bytes, int at, int count) => Encoding.ASCII.GetString(bytes, at, count);

    /// <summary>
    /// HHMMSS, with or without the offset from UTC as a sign and HHMM, written with colons. A time
    /// without an offset is printed with a zero offset, as the reference prints it.
    /// </summary>
    private static string? IptcTime(byte[] bytes)
    {
        if (bytes.Length is not (6 or 11) || !Digits(bytes, 0, 6)) return null;
        string time = Ascii(bytes, 0, 2) + ":" + Ascii(bytes, 2, 2) + ":" + Ascii(bytes, 4, 2);
        if (bytes.Length == 6) return time + "+00:00";
        if ((bytes[6] != '+' && bytes[6] != '-') || !Digits(bytes, 7, 4)) return null;
        return time + (char)bytes[6] + Ascii(bytes, 7, 2) + ":" + Ascii(bytes, 9, 2);
    }

    private static string Utf8OrLatin1(byte[] bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
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
                // The reference puts the default language first, wherever the packet has it.
                var parts = new List<string>();
                foreach ((string language, string value) in entry.Languages)
                {
                    string part = "lang=\"" + language + "\" " + Escape(value);
                    if (language == "x-default") parts.Insert(0, part);
                    else parts.Add(part);
                }
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
        return definition is null ? "0x" + Hex(key.TagId) : ReferenceSpelling(key) ?? definition.Name;
    }

    /// <summary>The key the reference prints for a tag it spells differently from Exif 3.0. The library
    /// keeps the standard's spelling; a comparison by key needs the two tools to say the same word for the
    /// same tag number. Only tags the library already names are listed: this is spelling, not vocabulary.</summary>
    private static string? ReferenceSpelling(ExifTagKey key) => (key.Directory, key.TagId) switch
    {
        (ExifDirectoryKind.Ifd0 or ExifDirectoryKind.Thumbnail, 0x8769) => "ExifTag", // ExifIFDPointer
        (ExifDirectoryKind.Ifd0 or ExifDirectoryKind.Thumbnail, 0x8825) => "GPSTag", // GPSInfoIFDPointer
        (ExifDirectoryKind.Exif, 0x8827) => "ISOSpeedRatings", // PhotographicSensitivity
        (ExifDirectoryKind.Exif, 0xA005) => "InteroperabilityTag", // InteroperabilityIFDPointer
        _ => null,
    };

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

    /// <summary>The value of a standard entry: <see cref="Render(ExifValue)" />, except for the three tags
    /// Exif gives a character code and the reference prints decoded: UserComment, GPSProcessingMethod and
    /// GPSAreaInformation.</summary>
    private static string Value(ExifEntry entry) =>
        entry.Value.FieldType == TiffFieldType.Undefined
        && entry.Key is { Directory: ExifDirectoryKind.Exif, TagId: 0x9286 } or { Directory: ExifDirectoryKind.Gps, TagId: 0x001B or 0x001C }
            ? UserComment(entry.Value)
            : Render(entry.Value);

    /// <summary>
    /// A commented value in the reference's notation: the text that follows the eight-byte character code, behind <c>charset=Ascii</c>, <c>charset=Jis</c> or <c>charset=Unicode</c>, bare
    /// when the code is eight zero bytes, and behind <c>charset=InvalidCharsetId</c> for any other code.
    /// Text ends at the first zero byte under the ASCII and the zero code; UTF-16 follows its byte order
    /// mark, or the byte order of the block without one. A value shorter than its code is empty.
    /// </summary>
    private static string UserComment(ExifValue value)
    {
        var bytes = new byte[value.ByteLength];
        value.CopyRawTo(bytes);
        if (bytes.Length < 8) return string.Empty;
        ReadOnlySpan<byte> code = bytes.AsSpan(0, 8);
        byte[] body = bytes[8..];
        if (code.SequenceEqual("UNICODE\0"u8))
        {
            bool big = value.ByteOrder == Endianness.BigEndian;
            int start = 0;
            if (body.Length >= 2 && body[0] == 0xFF && body[1] == 0xFE) { big = false; start = 2; }
            else if (body.Length >= 2 && body[0] == 0xFE && body[1] == 0xFF) { big = true; start = 2; }
            Encoding utf16 = big ? Encoding.BigEndianUnicode : Encoding.Unicode;
            return "charset=Unicode " + Escape(utf16.GetString(body, start, (body.Length - start) & ~1));
        }
        if (code.SequenceEqual("JIS\0\0\0\0\0"u8)) return "charset=Jis " + Escape(Utf8OrLatin1(body));
        bool ascii = code.SequenceEqual("ASCII\0\0\0"u8);
        if (!ascii && code.IndexOfAnyExcept((byte)0) >= 0) return "charset=InvalidCharsetId " + Escape(Utf8OrLatin1(body));
        int end = Array.IndexOf(body, (byte)0);
        string text = Escape(Utf8OrLatin1(end < 0 ? body : body[..end]));
        return ascii ? "charset=Ascii " + text : text;
    }

    /// <summary>A FLOAT or DOUBLE the way the reference prints one: fifteen significant digits, shortest form.</summary>
    private static string Real(double value) =>
        double.IsNaN(value) ? "nan"
        : double.IsInfinity(value) ? (value < 0 ? "-inf" : "inf")
        : value.ToString("G15", CultureInfo.InvariantCulture).Replace('E', 'e');

    private static string Render(ExifValue value)
    {
        var bytes = new byte[value.ByteLength];
        value.CopyRawTo(bytes);
        return Render(bytes, value.FieldType, value.Count, value.ByteOrder);
    }

    /// <summary>Every element the declared type and count ask for, or a marker saying they are not there.</summary>
    private static string Render(byte[] bytes, TiffFieldType type, uint count, Endianness order)
    {
        if (type is TiffFieldType.Ascii) return Escape(Utf8OrLatin1(Text(bytes)));
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
                TiffFieldType.Float => Real(ReadSingle(bytes, at, order)),
                TiffFieldType.Double => Real(ReadDouble(bytes, at, order)),
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

    /// <summary>The text of an ASCII value: the bytes before its first NUL, which is where the standard
    /// ends a string and where the reference stops printing one.</summary>
    private static byte[] Text(byte[] bytes)
    {
        int end = Array.IndexOf(bytes, (byte)0);
        return end < 0 ? bytes : bytes[..end];
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

    /// <summary>A TAG record with the interpreted value as its sixth field, empty when there is none.</summary>
    private static void Tag(StreamWriter stdout, string key, string type, uint count, string value, string? interpreted) =>
        Record(stdout, "TAG", key, type, count.ToString(CultureInfo.InvariantCulture), value, Escape(interpreted ?? string.Empty));

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
