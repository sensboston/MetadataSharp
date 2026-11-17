using System.Text;
using MetadataSharp.Core;
using MetadataSharp.Core.MakerNotes;
using MetadataSharp.Core.Tiff;

namespace MetadataSharp.Tests.MakerNotes;

/// <summary>Builds EXIF blocks with a MakerNote of each layout the library decodes, for the tests of the MakerNote writer.</summary>
internal static class MakerNoteFixture
{
    /// <summary>One entry of a directory: the tag, the TIFF type code, the count and the bytes of the value.</summary>
    internal sealed record E(ushort Tag, ushort Type, uint Count, byte[] Data);

    /// <summary>A layout: how the note starts and what the offsets of its values count from.</summary>
    /// <param name="Base">0: the TIFF header. 1: the start of the note. 2: the TIFF header inside a Nikon3 note.</param>
    internal sealed record Vendor(string Name, string Make, MakerNoteFormat Format, Func<bool, byte[]> Header, int Base, bool Next = true, bool? NoteBig = null);

    internal static byte[] B16(int value, bool big) => big ? [(byte)(value >> 8), (byte)value] : [(byte)value, (byte)(value >> 8)];

    internal static byte[] B32(long value, bool big) => big
        ? [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]
        : [(byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24)];

    internal static E Ascii(ushort tag, string text) => new(tag, 2, (uint)text.Length + 1, [.. Encoding.ASCII.GetBytes(text), 0]);

    internal static E Short(ushort tag, bool big, params int[] values) => new(tag, 3, (uint)values.Length, values.SelectMany(value => B16(value, big)).ToArray());

    internal static E Long(ushort tag, bool big, params long[] values) => new(tag, 4, (uint)values.Length, values.SelectMany(value => B32(value, big)).ToArray());

    internal static E Undefined(ushort tag, byte[] data) => new(tag, 7, (uint)data.Length, data);

    /// <summary>
    /// A directory that lies at <paramref name="at"/> in the block, with the values that do not fit
    /// their entries after it; their offsets are stored counted from <paramref name="origin"/>.
    /// </summary>
    internal static byte[] Ifd(bool big, int at, int origin, bool next, params E[] entries)
    {
        int table = 2 + (entries.Length * 12) + (next ? 4 : 0);
        var head = new List<byte>();
        var values = new List<byte>();
        head.AddRange(B16(entries.Length, big));
        foreach (E entry in entries)
        {
            head.AddRange(B16(entry.Tag, big));
            head.AddRange(B16(entry.Type, big));
            head.AddRange(B32(entry.Count, big));
            if (entry.Data.Length <= 4)
            {
                head.AddRange(entry.Data);
                head.AddRange(new byte[4 - entry.Data.Length]);
            }
            else
            {
                if ((values.Count & 1) != 0) values.Add(0);
                head.AddRange(B32(at + table + values.Count - origin, big));
                values.AddRange(entry.Data);
            }
        }
        if (next) head.AddRange(new byte[4]);
        if ((values.Count & 1) != 0) values.Add(0);
        return [.. head, .. values];
    }

    /// <summary>
    /// An EXIF block: Make and Model in the primary directory, and an Exif directory whose only tag
    /// is the MakerNote, which <paramref name="note"/> builds for the offset it is given.
    /// </summary>
    internal static byte[] Build(bool big, string make, string model, Func<int, byte[]> note)
    {
        byte[] makeBytes = [.. Encoding.ASCII.GetBytes(make), 0], modelBytes = [.. Encoding.ASCII.GetBytes(model), 0];
        if (makeBytes.Length <= 4 || modelBytes.Length <= 4) throw new ArgumentException("Make and Model are longer than an entry holds.");
        // Every value starts at an even offset.
        if ((makeBytes.Length & 1) != 0) makeBytes = [.. makeBytes, 0];
        if ((modelBytes.Length & 1) != 0) modelBytes = [.. modelBytes, 0];
        int makeAt = 50, modelAt = makeAt + makeBytes.Length, exifAt = modelAt + modelBytes.Length;
        int noteAt = exifAt + 18;
        byte[] noteBytes = note(noteAt);
        return
        [
            .. big ? "MM"u8 : "II"u8, .. B16(42, big), .. B32(8, big),
            .. B16(3, big),
            .. B16(0x010F, big), .. B16(2, big), .. B32(make.Length + 1, big), .. B32(makeAt, big),
            .. B16(0x0110, big), .. B16(2, big), .. B32(model.Length + 1, big), .. B32(modelAt, big),
            .. B16(0x8769, big), .. B16(4, big), .. B32(1, big), .. B32(exifAt, big),
            .. B32(0, big),
            .. makeBytes, .. modelBytes,
            .. B16(1, big),
            .. B16(0x927C, big), .. B16(7, big), .. B32(noteBytes.Length, big), .. B32(noteAt, big),
            .. B32(0, big),
            .. noteBytes,
        ];
    }

    /// <summary>The block of a vendor whose note holds <paramref name="entries"/>, built for the byte order of the note.</summary>
    internal static byte[] Build(Vendor vendor, bool big, Func<bool, E[]> entries, string model = "MODEL ONE")
    {
        bool noteBig = vendor.NoteBig ?? big;
        return Build(big, vendor.Make, model, start =>
        {
            byte[] header = vendor.Header(big);
            int origin = vendor.Base switch { 0 => 0, 1 => start, _ => start + 10 };
            return [.. header, .. Ifd(noteBig, start + header.Length, origin, vendor.Next, entries(noteBig))];
        });
    }

    internal static byte[] Text(string text) => Encoding.Latin1.GetBytes(text);

    internal static readonly Vendor[] Vendors =
    [
        new("Canon", "Canon Inc.", MakerNoteFormat.Canon, _ => [], 0),
        new("Nikon1", "NIKON CORPORATION", MakerNoteFormat.Nikon1, _ => [], 0),
        new("Nikon2", "NIKON CORPORATION", MakerNoteFormat.Nikon2, _ => [.. Text("Nikon\0"), 1, 0], 0),
        new("Nikon3", "NIKON CORPORATION", MakerNoteFormat.Nikon3,
            big => [.. Text("Nikon\0"), 2, 0x10, 0, 0, .. big ? "MM"u8 : "II"u8, .. B16(42, big), .. B32(8, big)], 2),
        new("Fujifilm", "FUJIFILM", MakerNoteFormat.Fujifilm, _ => [.. Text("FUJIFILM"), 12, 0, 0, 0], 1, NoteBig: false),
        new("Pentax", "PENTAX Corporation", MakerNoteFormat.Pentax, _ => [.. Text("AOC\0"), 0, 0], 0),
        new("PentaxDng", "PENTAX Corporation", MakerNoteFormat.PentaxDng, _ => [.. Text("PENTAX \0"), 0, 0], 1),
        new("Sony1", "SONY CORP", MakerNoteFormat.Sony1, _ => Text("SONY DSC \0\0\0"), 0),
        new("Sony2", "SONY CORP", MakerNoteFormat.Sony2, _ => [], 0),
        new("Panasonic", "Panasonic", MakerNoteFormat.Panasonic, _ => Text("Panasonic\0\0\0"), 0, Next: false),
        new("Olympus", "OLYMPUS IMAGING CORP.", MakerNoteFormat.Olympus, _ => [.. Text("OLYMP\0"), 1, 0], 0),
        new("Olympus2", "OLYMPUS IMAGING CORP.", MakerNoteFormat.Olympus2, big => [.. Text("OLYMPUS\0"), .. big ? "MM"u8 : "II"u8, 3, 0], 1),
        new("OMSystem", "OM Digital Solutions", MakerNoteFormat.OMSystem, big => [.. Text("OM SYSTEM\0\0\0"), .. big ? "MM"u8 : "II"u8, 4, 0], 1),
        new("Minolta", "MINOLTA CO.", MakerNoteFormat.Minolta, _ => [], 0),
        new("Casio", "CASIO COMPUTER", MakerNoteFormat.Casio, _ => [], 0),
        new("Casio2", "CASIO COMPUTER", MakerNoteFormat.Casio2, _ => Text("QVC\0\0\0"), 0, NoteBig: true),
        new("Samsung2", "SAMSUNG TECHWIN", MakerNoteFormat.Samsung2, _ => [], 1),
        new("Sigma", "SIGMA CORP", MakerNoteFormat.Sigma, _ => [.. Text("SIGMA\0\0\0"), 1, 0], 0),
    ];

    internal static Vendor Find(string name) => Vendors.Single(vendor => vendor.Name == name);

    internal static ExifMetadata Exif(byte[] tiff) => ExifTiffReader.Read(tiff).Metadata;

    internal static MakerNote Note(byte[] tiff) => Exif(tiff).MakerNote ?? throw new InvalidOperationException("The fixture has no MakerNote the library decodes.");

    /// <summary>The key of what lies at a path (tag, field and element as four hex digits each).</summary>
    internal static string Key(byte[] tiff, string path) => MakerNoteKeys.Enumerate(Note(tiff)).First(target => target.Path == path).Key;

    /// <summary>The value at a path, or null when the note has nothing there.</summary>
    internal static byte[]? Raw(byte[] tiff, string path)
    {
        MakerNoteTarget? found = MakerNoteKeys.Enumerate(Note(tiff)).FirstOrDefault(target => target.Path == path);
        return found is null ? null : found.Value.RawBytes.ToArray();
    }

    /// <summary>The block with the MakerNote changes of an edit applied by the writer.</summary>
    internal static byte[] Apply(byte[] tiff, Action<ExifEdit> change)
    {
        var edit = new ExifEdit();
        change(edit);
        return MakerNoteWriter.Apply(tiff, edit.Snapshot().MakerNote, new MetadataLimits());
    }
}
