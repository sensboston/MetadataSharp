using System.Globalization;
using System.Text;

namespace MetadataSharp.Core.MakerNotes;

/// <summary>One thing of a MakerNote a key names: a tag, a field of an array, a tag of a sub-directory, or an element of an array in one.</summary>
/// <param name="Key">The key, as Exiv2 names it: <c>Exif.Canon.OwnerName</c>, <c>Exif.CanonCs.Quality</c>.</param>
/// <param name="Path">The tag, then the field, then the element: what stays the same when the note is read again.</param>
/// <param name="Entry">The tag of the note's directory.</param>
/// <param name="Field">The field of the tag, or null for the tag itself.</param>
/// <param name="Element">The element of the field, or null.</param>
internal sealed record MakerNoteTarget(string Key, string Path, MakerNoteEntry Entry, MakerNoteField? Field, MakerNoteField? Element)
{
    /// <summary>The value the key names.</summary>
    internal ExifValue Value => Element?.Value ?? Field?.Value ?? Entry.Value;

    /// <summary>The name the key ends with.</summary>
    internal string Name => Key[(Key.LastIndexOf('.') + 1)..];
}

/// <summary>
/// The keys of the tags of a MakerNote, in the groups Exiv2 prints them in: the group of the note
/// (<c>Canon</c>, <c>Nikon3</c>, <c>Sony1</c>), and for the fields of an array or the tags of a
/// sub-directory the group of that array (<c>CanonCs</c>, <c>NikonLd3</c>, <c>OlympusCs</c>).
/// </summary>
internal static class MakerNoteKeys
{
    private static readonly string[] Vendors =
        ["Canon", "Nikon", "Fujifilm", "Pentax", "Sony", "Panasonic", "Olympus", "OMSystem", "Minolta", "Casio", "Samsung", "Sigma"];

    /// <summary>True for <c>Exif.GROUP.NAME</c> whose group is one of a MakerNote this library decodes.</summary>
    internal static bool IsMakerNoteKey(string? key)
    {
        if (key is null) return false;
        string[] parts = key.Split('.');
        if (parts.Length != 3 || parts[0] != "Exif" || parts[1].Length == 0 || parts[2].Length == 0) return false;
        foreach (string vendor in Vendors)
            if (parts[1].StartsWith(vendor, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>The group of the tags of the note's own directory.</summary>
    internal static string Group(MakerNoteFormat format) => format switch
    {
        MakerNoteFormat.Canon => "Canon",
        MakerNoteFormat.OMSystem => "Olympus2",
        _ => format.ToString(),
    };

    /// <summary>
    /// True for the layouts in which a value that grows is appended. Each was confirmed by the
    /// reference tool on files of its corpus (the MakerNote parity job): it reads the appended value
    /// and every other tag of the note as before. All four count their offsets from the TIFF header, so
    /// the value goes to the end of the EXIF block. A layout is not listed when the corpus has no file
    /// in which the library could append for it: the notes that count from themselves (Fujifilm,
    /// Nikon3, Olympus2, OM System, Pentax in a DNG, Samsung2) are the last thing of the block in no
    /// file of the corpus, and the others have no file with a text tag there.
    /// </summary>
    internal static bool Grows(MakerNoteFormat format) =>
        format is MakerNoteFormat.Canon or MakerNoteFormat.Nikon1 or MakerNoteFormat.Nikon2 or MakerNoteFormat.Sigma;

    /// <summary>Everything of the note a key names, tags before their fields. A key that occurs twice names the first.</summary>
    internal static List<MakerNoteTarget> Enumerate(MakerNote note)
    {
        var targets = new List<MakerNoteTarget>();
        string group = Group(note.Format);
        bool olympus = note.Format is MakerNoteFormat.Olympus2 or MakerNoteFormat.OMSystem;
        bool sony = note.Format is MakerNoteFormat.Sony1 or MakerNoteFormat.Sony2;
        foreach (MakerNoteEntry entry in note.Entries)
        {
            string tagPath = Hex(entry.TagId);
            targets.Add(new("Exif." + group + "." + (entry.Name ?? "0x" + Hex(entry.TagId)), tagPath, entry, null, null));
            if (entry.Fields.Count == 0) continue;
            string fieldGroup =
                olympus ? OlympusGroup(entry.TagId)
                : note.Format == MakerNoteFormat.Nikon3 && entry.TagId == 0x0011 ? "NikonPreview"
                : sony && entry.TagId == 0xB028 ? "SonyMinolta"
                : note.Format == MakerNoteFormat.Canon && entry.TagId == 0x0026 ? group
                : note.Format == MakerNoteFormat.Nikon3 ? "Nikon" + NikonArrayGroup(entry)
                : sony ? SonyArrayGroup(note.Format, entry.TagId)
                : note.Format == MakerNoteFormat.Minolta ? MinoltaArrayGroup(entry.TagId)
                : note.Format == MakerNoteFormat.Samsung2 ? "SamsungPictureWizard"
                : group + CanonArrayGroup(entry.TagId);
            foreach (MakerNoteField field in entry.Fields)
            {
                string fieldPath = tagPath + "/" + Hex(field.Index);
                targets.Add(new("Exif." + fieldGroup + "." + field.Name, fieldPath, entry, field, null));
                if (field.Fields.Count == 0) continue;
                // The settings array inside the Minolta directory of a Sony DSLR-A100.
                string elementGroup = (note.Format == MakerNoteFormat.Sony1 ? "Sony1" : "Sony2") + "MltCsA100";
                foreach (MakerNoteField element in field.Fields)
                    targets.Add(new("Exif." + elementGroup + "." + element.Name, fieldPath + "/" + Hex(element.Index), entry, field, element));
            }
        }
        return targets;
    }

    /// <summary>
    /// True for a tag that is not edited as a value: one that addresses data or a directory, the
    /// length that goes with such an address, and the two tags the cipher of the Nikon arrays is keyed with.
    /// </summary>
    internal static bool IsPointer(MakerNoteFormat format, MakerNoteTarget target)
    {
        if (target.Element is not null) return false;
        if (target.Field is null)
        {
            // A tag that leads to a directory, read or not.
            foreach (MakerNoteField field in target.Entry.Fields)
                if (field.Slot is not null) return true;
            ushort tag = target.Entry.TagId;
            if (format == MakerNoteFormat.Nikon3 && tag is 0x0011 or 0x001D or 0x00A7) return true;
            if (format is MakerNoteFormat.Olympus2 or MakerNoteFormat.OMSystem &&
                tag is 0x2010 or 0x2020 or 0x2030 or 0x2031 or 0x2040 or 0x2050 or 0x3000 or 0x4000 or 0x5000) return true;
            if (format is MakerNoteFormat.Sony1 or MakerNoteFormat.Sony2 && tag == 0xB028) return true;
        }
        else if (target.Field.Slot is null)
            return false; // A field of an array is a number of the array.
        string name = target.Name;
        return name.EndsWith("Offset", StringComparison.Ordinal) || name.EndsWith("Offsets", StringComparison.Ordinal) ||
            name.EndsWith("Start", StringComparison.Ordinal) || name.EndsWith("IFD", StringComparison.Ordinal) ||
            name is "Preview" or "PreviewImageLength" or "PreviewLength" or "ThumbnailLength" or
                "JPEGInterchangeFormat" or "JPEGInterchangeFormatLength" or "StripByteCounts";
    }

    private static string Hex(ushort value) => value.ToString("x4", CultureInfo.InvariantCulture);

    // Map Olympus pointer tags to their separate Exiv2 groups instead of the parent note group.
    private static string OlympusGroup(ushort tag) => tag switch
    {
        0x2010 => "OlympusEq", 0x2020 => "OlympusCs", 0x2030 => "OlympusRd", 0x2031 => "OlympusRd2",
        0x2040 => "OlympusIp", 0x2050 => "OlympusFi", 0x3000 => "OlympusRi",
        _ => "Olympus0x" + Hex(tag),
    };

    private static string MinoltaArrayGroup(ushort tag) => tag switch
    {
        0x0001 => "MinoltaCsOld", 0x0003 => "MinoltaCsNew", 0x0004 => "MinoltaCs7D", 0x0114 => "MinoltaCs5D",
        _ => "Minolta0x" + Hex(tag),
    };

    // Keep Sony array fields grouped by their tag-specific decoded layout and camera generation.
    private static string SonyArrayGroup(MakerNoteFormat format, ushort tag) => tag switch
    {
        0x0114 => format == MakerNoteFormat.Sony1 ? "Sony1Cs" : "Sony2Cs",
        0x2010 => "Sony2010e", 0x3000 => "SonySInfo1", 0x9400 => "SonyMisc3c", 0x9402 => "Sony2Fp",
        0x9403 => "SonyMisc1", 0x9404 => "SonyMisc2b",
        _ => "Sony0x" + Hex(tag),
    };

    /// <summary>The group of a split Nikon3 array, chosen as its layout is: by the tag and the version the array starts with.</summary>
    private static string NikonArrayGroup(MakerNoteEntry entry)
    {
        ReadOnlySpan<byte> raw = entry.Value.RawBytes;
        string version = raw.Length >= 4 ? Encoding.ASCII.GetString(raw[..4]) : string.Empty;
        return entry.TagId switch
        {
            0x001F => "Vr", 0x0023 => "Pc", 0x0024 => "Wt", 0x0025 => "Ii", 0x0088 => "Af",
            0x0098 => version switch { "0100" => "Ld1", "0204" => "Ld3", "0800" or "0801" or "0802" => "Ld4", _ => "Ld2" },
            0x0091 => version.StartsWith("01", StringComparison.Ordinal) ? "Si01xx" : "Si02xx",
            0x0097 => version switch { "0103" => "Cb3", "0212" => "Cb4", _ => "Cb2b" },
            0x00A8 => version switch { "0100" or "0101" => "Fl1", "0102" => "Fl2", "0106" => "Fl6", "0107" or "0108" => "Fl7", _ => "Fl3" },
            0x00B0 => "Me", 0x00B7 => version == "0100" ? "Af2" : "Af22", 0x00B8 => "Fi", 0x00B9 => "AFT",
            _ => "0x" + Hex(entry.TagId),
        };
    }

    // Use Canon’s published array group names for editable keys of split fields.
    private static string CanonArrayGroup(ushort tag) => tag switch
    {
        0x0001 => "Cs", 0x0004 => "Si", 0x0005 => "Pa", 0x000F => "Cf", 0x0012 => "Pi", 0x0093 => "Fi",
        0x00A0 => "Pr", 0x0035 => "Ti", 0x4013 => "AfMiAdj", 0x4016 => "VigCor2", 0x4018 => "LiOp",
        0x4019 => "Le", 0x4020 => "Am", 0x4021 => "Me", 0x4025 => "Hdr", 0x4028 => "AfC",
        _ => "0x" + Hex(tag),
    };
}
